# Ticket 03 实施与交付报告:SteamID:port 分类(Join Routing 单冒号端口剥离)

- **日期**:2026-09-12 10:48
- **基线**:HEAD `8cff571`(feat(item): align ItemManager.Update despawn/respawn dedicated gate for listen hosts (ticket02))
- **票据**:`.scratch/join-routing/issues/03-steamid-port-suffix-routing.md`(规格 `.scratch/join-routing/spec.md` 同目录)
- **范围**:join-routing 批实施票 03(SteamID:port 分类)。会话密码归票 04;共享 Runtime 验收归票 05(`listen-host-join-routing-runtime-acceptance`),本票不宣称 Runtime PASS。
- **冻结图对表**:`.scratch/map-top-level-architecture-blueprint/map.md` 铁规 1(单机/听主本地零破坏)、TDD 闭环;票 03 Blocked by:None,按规格操作顺序先于会话密码票 04 落地(降低同文件冲突)。

## 1. 结论

票面六项全部静态闭环:红→绿链(274/275 契约红→277/277,含 round 2 F2 修复红 276/277→绿)、双 Release 0/0、三项静态门禁 PASS、`git diff --check` 干净;双轴审查 **round 1 Standards CLEAN / Spec NOT CLEAN(F1)→ round 2 Standards CLEAN / Spec NOT CLEAN(F2)→ round 3 Standards CLEAN / Spec NOT CLEAN(F3)→ round 4 双轴 CLEAN**(每轮全新官方实例,无 blocking,延期项全部具名于 §7)。新交付物指纹见 §5。**`SteamID:port` 分类与连接行为的 Runtime 证据未到位前,本票状态为 `implemented-pending-runtime`。**

流程偏差具名:round 2 首次派发两次遭遇审查官自定义模型端点 `Insufficient account balance`(基础设施故障),用户中断降级方案并恢复 provider 后,以官方审查官全新实例完成 round 2;被取消的降级派发未返回任何裁决,不计入 CLEAN 链。

## 2. 变更清单(新增 1 文件 63 行;修改 3 文件 +30/−3)

| 文件 | 变更 |
|---|---|
| `Platform/Transport/UnifiedJoinAddressClassifier.cs` | +24:`Classify` 在 Trim 后、`ulong.TryParse` 前新增端口剥离契约——①空白守卫:内部残留任何空白字符(含 SteamID 与冒号之间)→ Vanilla,端部空白由既有 Trim 处理(对齐输入形状 `[可选空白] SteamID [可选单个 :port] [可选空白]`);②单冒号检测:第二个冒号立即 Vanilla(禁止「最后一个冒号」切割,多冒号含 IPv6 全拒);③端口文本逐字符十进制累加,任何非数字 → Vanilla,每步累加后 `>65535` 即返回(越界早退不变量使中间值上限 655359 ≪ uint.MaxValue,结构上无溢出),终值 `<1`(`:0`)→ Vanilla;④剥离后剩余部分交给原有 `ulong.TryParse` + `CSteamID.IsValid()/BIndividualAccount()` 判定,out `steamId` 只放 SteamID |
| `WhitelistTests/Evidence/PureMemory/Platform/UnifiedJoinAddressClassifierTests.cs`(新,63 行) | JR1 正例(裸 SteamID、端部空白包裹 `SteamID:27016`、`:1`、`:65535` → SteamP2P 且 `steamId` 为假号 76561199000000001);JR2 负例矩阵(IPv4:port、DNS:port、`SteamID:0`、`SteamID:65536`、`:abc` 非数字后缀、`2001:db8::1` IPv6、`SteamID:27016:1` 多冒号、字面量 `:0`、`:65536`、`SteamID :27016` 冒号前空白、`SteamID:4294967297` 超长端口串 → Vanilla 且 `steamId==0`);JR3 职责边界锁(`TryBuildDirectIpEndpoint`/`TryBuildExplicitDnsEndpoint` 仍剥自己的端口,`SteamID:27016` 不被两者认领)。私有辅助 `IsSteamP2P`/`IsVanilla` 断言 kind 与 out 值,不触 UI 控件 |
| `WhitelistTests/Program.cs` | Platform 区注册 JR1/JR2/JR3(274→277 注册数,单一入口纪律保持),区注释 21→24 Tests |
| `README.md` | 「已知未修」行:`SteamID:端口 粘贴会静默失败` → `SteamID:端口 粘贴(ticket03 已实现,待 Runtime 验收)`;客机 SteamID 加入步骤:`不要带 :端口` → `可带单个 :端口;端口只用于分类,不进入 P2P 连接参数` |

未触及:`MenuPlayConnectP2PRoutePatch`(连接按钮路由与 `P2PJoinManager.TryConnectToHost(targetId)` 参数组装不变,端口天然不进入 P2P 参数)、`TryBuildDirectIpEndpoint`/`TryBuildExplicitDnsEndpoint`/`IsSinglePortDirectIpParameters` 剥端口职责、会话密码(票 04)、Route B、`Dedicator.IsDedicatedServer` 本体、版本号、`docs/architecture/` 基线工件、`.scratch` 其他批票单。

## 3. 红→绿链(TDD 闭环,逐轮实测)

1. **RED-1**:`JR1 SteamIdPortClassifiesAsSteamP2P` 落地后实测 **274/275**(`ID:27016`/`ID:1`/`ID:65535` 被旧 `Classify` 整串 `ulong.TryParse` 拒绝判 Vanilla)——红点即票面缺陷本体。
2. **GREEN-1**:单冒号剥离 + 端口十进制校验落地 → **275/275**。JR2/JR3 为同一接缝的负例矩阵与边界回归锁,按规格矩阵一次性补齐(先例:ticket01 DG1-DG3/ZG1「回归锁自始绿」定性,具名非本轮修复对象)。
3. **RED-2(round 2 修复)**:Spec F2(`76561199000000001 :27016` 冒号前空白被 `ulong.TryParse` 默认 NumberStyles 尾随空白容忍误判 Steam P2P)→ 先加用例 → **276/277** 实测红。
4. **GREEN-2**:空白守卫(Trim 后内部任何空白 → Vanilla)→ **277/277** 复绿。
5. **round 3 F3 处置**:Spec 称端口累加 `uint` 溢出(`:4294967297` 溢出为 1)。静态推演否证溢出机制:每步累加后有 `>65535` 早退,下一轮乘 10 前 `portValue ≤ 65535`,中间值上限 `65535×10+9=655359 ≪ 2^32`,该输入在第 7 位即早退 Vanilla(round 4 Spec 轴独立复核确认)。其覆盖缺口成立 → 补 `:4294967297` 回归锁(实现已正确,断言直接绿,按回归锁定性具名)。

## 4. 验证矩阵(静态门禁,全部实测)

| 门禁 | 结果 |
|---|---|
| 主 DLL Release Rebuild | 0 errors / 0 warnings(`TreatWarningsAsErrors`) |
| 测试 exe Release Rebuild | 0 errors / 0 warnings |
| 全套测试(唯一入口) | **277/277 PASS**(原 274 + 新增 3) |
| `Tools/Verify-BuildFingerprintArtifact.ps1` | PASS(INDEPENDENT_ARTIFACT_VERIFICATION_PASS) |
| `Tools/Verify-EvidenceClassLayout.ps1` | PASS(EVIDENCE_CLASS_LAYOUT_PASS) |
| `Tools/Verify-Ticket09Documentation.ps1`(PS5.1 原样) | PASS(TICKET09_DOCUMENTATION_METADATA_PASS) |
| `git diff --check` | CLEAN |

## 5. 产物身份(最终指纹,可复现构建)

构建机制未触碰(portable PDB + PathMap,跨路径可复现性延续)。round 2 空白守卫后重建使旧中间产物失效,下表为本轮**唯一有效身份**,Runtime 证据绑定以本表为准;Ticket02 报告指纹(`9887C228…`/`d786adb7…`)自本票源码变更起不再可由 HEAD 复现,由本表接替:

| 产物 | SHA-256 | MVID |
|---|---|---|
| `bin/Release/SteamP2PFriends.dll` | `6A2178D0E50E0D985E3A5AFE8D4E86518604B151A4FEC3DDA5CD09FE50955249` | `1f383394-5051-456b-a153-f9873ca44c01` |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `016F74C2A1FAED37850FCA91D2FE941B5C068D1FC8583E52C3DA59C67B12C374` | `d73db060-9402-41fc-898c-d179d251bb37` |

## 6. Seam gaps 与取舍(全部具名,无静默跳过)

1. **连接侧无新增代码即满足「端口不进入 P2P 连接参数」**:P2P 路线生产代码只消费 `Classify` 的 out `steamId`(`MenuPlayConnectP2PRoutePatch.cs:31→49` → `P2PJoinManager.TryConnectToHost(targetId)` → `ServerConnectParameters(hostSteamId, …)`),端口在分类阶段即被丢弃。该子句由 JR1 断言 `steamId==假号`(无端口成分)+ 现有调用链静态闭合,未引入第二处解析。
2. **端口十进制校验用手写累加而非 `ushort.TryParse`**:默认 `NumberStyles.Integer` 允许空白/正负号,会错误接受 `ID: 27016`、`ID:+27016`;逐字符数字校验是规格「十进制端口」的最小忠实实现(Standards round 1 判为 Primitive Obsession 判断性气味,具名延期,见 §7-1)。
3. **JR2/JR3 非红测定性**:规格矩阵中的负例与 Direct-IP/DNS 边界在旧实现下即成立,属「缝已存在」的回归锁;真正的缺陷红测是 JR1(RED-1)与 F2 补测(RED-2)。此定性如实具名,与 ticket01 §3-1 同一口径。
4. **README 客机步骤仍保留「SteamID 路线丢密码」警示**:密码传递属票 04,本票不动;两行 README 变更仅限本票分类语义。
5. **round 2 审查官端点故障**:两次派发 `Insufficient account balance`,用户恢复 provider 后以官方全新实例补跑;被取消的降级派发无裁决产出,不进 CLEAN 链(见 §1)。

## 7. 判断性坏味道(全部延期,理由具名;round 4 双轴确认不阻断)

1. **Primitive Obsession(延期)**:端口以 `string portText` + `uint portValue` 手写累加表达 1–65535 领域概念,未建端口值对象。拆出 Port 类型是纯内部重构,不影响分类语义与测试;随下一次该文件结构轮收口(Standards round 1/2/4 一致具名)。
2. **JR2 用例内聚于单方法(观察项,未升级)**:10 个负例断言共用一个测试方法,失败时以首败断言定位。测试入口按 Evidence Class 组织为 `RunTest` 粒度,拆分为 10 个注册行会稀释注册区密度;维持现状,随测试入口结构性整理(如有)再议。

## 8. Runtime 判读口径(移交票 05 共享验收,1 Host + 2 Guest,普通 PEI)

1. **分类生效判据**:客机直连页粘贴 `房主SteamID:27016` 点连接 → 日志出现 `[UnifiedConnect] route=SteamP2P target=<房主ID> started=True`(RoleLogger 输出),且 P2P 握手照常发起;连接参数中不存在端口成分(代码路径保证,日志无端口字段属预期)。
2. **负例仍走原版**:粘贴 `192.168.1.1:27016` 仍走插件 Direct-IP;`host.example.com:27016`(DNS 开关开)仍走显式 DNS;多冒号/`:0`/`:65536`/非数字后缀落回原版路径,不得出现 route=SteamP2P。
3. **与票 04 的边界**:本票不读取直连页密码框;`SteamID:port` 无密码房间的进入行为与裸 SteamID 完全一致,有密码房间按现行语义失败(票 04 修复)。
4. **回归对照**:裸 SteamID(无端口)行为与 0.2.4.8 前完全一致——分类输出与连接参数均无变化(JR1 第一断言 + 未改连接代码)。
