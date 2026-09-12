# Ticket 04 实施与交付报告:Listen-Host Session Password(会话密码与 SteamID 路线密码传递)

- **日期**:2026-09-12 11:23
- **基线**:HEAD `0394745`(feat(join): classify SteamID with optional :port suffix as Steam P2P (ticket03))
- **票据**:`.scratch/join-routing/issues/04-listen-host-session-password.md`(规格 `.scratch/join-routing/spec.md` 同目录)
- **范围**:join-routing 批实施票 04(会话密码)。共享 Runtime 验收归票 05(`listen-host-join-routing-runtime-acceptance`),本票不宣称 Runtime PASS。
- **冻结图对表**:`.scratch/map-top-level-architecture-blueprint/map.md` 铁规 1(单机/听主本地零破坏)、铁规 2(严禁全局伪造 `Dedicator.IsDedicatedServer`)、TDD 闭环;票 04 Blocked by:None,按规格操作顺序在分类票 03 之后落地(降低同文件冲突)。

## 1. 结论

票面七项全部静态闭环:红→绿链(RED-1 实测 278/280:JR4/JR5 红 + JR6 回归锁自始绿 → GREEN-1 280/280)、双 Release 0/0、三项静态门禁 PASS、`git diff --check` CLEAN;双轴审查 **round 1 Standards CLEAN / Spec NOT CLEAN(F1)→ round 2 双轴 CLEAN**(每轮全新官方实例,无 blocking 残留,延期项全部具名于 §7)。新交付物指纹见 §5。**会话密码与 SteamID 路线密码传递的 Runtime 证据未到位前,本票状态为 `implemented-pending-runtime`。**

round 1 Spec F1 修复:开房菜单返回动作(`OnClickedBackFromRole`)补 `SessionPassword.ClearRuntime()`;顺带提前修复 round 1 Standards JC-2(`P2PJoinManager.Reset()` 清空 `_targetPassword`)。修复增量复跑全矩阵后以全新实例完成 round 2 复审。

## 2. 变更清单(新增 2 文件 134 行;修改 7 文件 +85/−20)

| 文件 | 变更 |
|---|---|
| `Core/Shared/SessionPassword.cs`(新,60 行) | 会话密码策略缝,四个成员:①`ResolveSessionServerPassword` 开房菜单输入→当前会话 `Provider.serverPassword`(null→空串,不 trim);②`BuildSteamP2PConnectParameters` 直连页密码框→客机 `ServerConnectParameters(CSteamID, password)`(null→空串);③`HasPassword`(string 重载+连接参数重载)唯一布尔可观测性出口;④`ClearRuntime()` 写 `Provider.serverPassword = string.Empty`(原版 setter 在 `isServer=false` 时只算托管 SHA1、不触 SteamGameServer,菜单期/测试宿主安全) |
| `WhitelistTests/Evidence/PureMemory/Platform/SessionPasswordTests.cs`(新,74 行) | JR4 无密码/有密码参数组装(断言 `HasPassword(parameters)` 布尔+`steamId` 为假号,禁止读明文);JR5 房主输入语义(null→无密码、非空→有密码、纯空白→有密码即不 trim);JR6 运行时清空+幂等(fixture 写入→`ClearRuntime`→空,finally 还原);断言全部经 `SessionPassword.HasPassword` 出口 |
| `WhitelistTests/Program.cs` | Platform 区注册 JR4/JR5/JR6(277→280 注册数,单一入口纪律保持),区注释 24→27 Tests |
| `Platform/Client/P2PJoinManager.cs` | +`_targetPassword` 字段;`TryConnectToHost` 新增 `(ulong, string)` 重载(旧单参签名委托 null,lobby/旧菜单路径行为不变);`DoConnect` 改用 `SessionPassword.BuildSteamP2PConnectParameters` 组装并只记 `hasPassword=`;`Reset()` 清空 `_targetPassword`(round2) |
| `Platform/UI/Patches/MenuPlayConnectP2PRoutePatch.cs` | Steam P2P 分支读取原版直连页 `passwordField.Text`→传入 `TryConnectToHost(targetId, directPagePassword)`,日志追加 `hasPassword=`;IPv4/DNS 路径零改动 |
| `Platform/Host/HostManager.cs` | `StartP2PServer` 增 `string sessionPassword = null` 参数+日志只记 hasPassword;`ConfigureCommonServerSettings` 增同名默认参,`Provider.serverPassword` 由硬编码空串改为经缝写入+`[SessionPassword] hasPassword=` 日志;`StopP2PServer` 与 `AbortHostStart` 各加 `SessionPassword.ClearRuntime()`;`StartLanServer` 走默认 null,行为不变 |
| `Platform/UI/P2PNativeMenuUI.cs` | 开房菜单新增密码框(y=505,`IsPasswordField=true` 遮罩、`MaxLength=0` 与原版直连页一致、`OnPasswordChanged` 不 trim;复制/创建/返回三按钮整体下移 45px 让位);`OpenRoleMenu` 每次打开 `_sessionPassword` 默认空(不读配置);`OnClickedBackFromRole` 清字段+`ClearRuntime()`(round2 F1 修复);`Destroy` 清 UI 引用;`TryStartHost` 传 `_sessionPassword`,注释具名其有意不进 `PersistLastRoomSettings` |
| `SteamP2PFriends.csproj` | 注册 `Core\Shared\SessionPassword.cs` |
| `README.md` | 「已知未修」行:`SteamID 路线丢密码` → `SteamID 路线密码传递(ticket04 已实现,待 Runtime 验收)`;客机 SteamID 加入步骤新增第 3 步(有密码房间在直连页密码框填密码),尾段改写为票 04 语义(会话级密码、原版 PASSWORD 失败、不保存) |

未触及:`connectionFailureInfo` 与失败枚举、原版失败面板、`Dedicator.IsDedicatedServer` 本体、Route B、`UnifiedJoinAddressClassifier`、`Develop-Stage/` 归档、版本号、`.scratch` 其他批票单。无新 Harmony patch(仅既有 `MenuPlayConnectP2PRoutePatch` Prefix 内部扩展),按规格测试决策不引入 StaticIL 契约。

## 3. 红→绿链(TDD 闭环,逐轮实测)

1. **RED-1**:缝 `SessionPassword` 以「当前行为提取」形态落地(`ResolveSessionServerPassword`/`BuildSteamP2PConnectParameters` 均硬编码空串,与既有生产代码 `Provider.serverPassword = string.Empty`、`ServerConnectParameters(hostSteamId, string.Empty)` 完全等价)后,`JR4 SteamIdRouteCarriesDirectPagePassword` 与 `JR5 HostMenuInputResolvesSessionPassword` 实测 **278/280 FAIL**——红点即票面缺陷本体(客机密码硬编码丢弃、房主密码硬编码清空)。
2. **GREEN-1**:两缝修复为直通语义(null→空串、不 trim)→ **280/280 PASS**。
3. **JR6 非红测定性**:`ClearRuntime` 是本票新增行为(旧代码不存在任何会话结束清空路径),其 PureMemory 锁「自始绿」;真正证明「会话结束后运行时密码为空」旧缺陷的证据是静态缺失(全仓 grep 仅 `HostManager.cs:765` 一处写空串,无清空调用),接线后由 `StopP2PServer`/`AbortHostStart`/`OnClickedBackFromRole` 三处闭合。此定性如实具名,与 ticket01 DG1-DG3、ticket03 JR2/JR3「缝已存在/新行为的回归锁」同一口径。

## 4. 验证矩阵(静态门禁,全部实测;round 2 修复增量后复跑)

| 门禁 | 结果 |
|---|---|
| 主 DLL Release Rebuild(清中间产物) | 0 errors / 0 warnings(`TreatWarningsAsErrors`) |
| 测试 exe Release Rebuild(清中间产物) | 0 errors / 0 warnings |
| 全套测试(唯一入口) | **280/280 PASS**(原 277 + 新增 3) |
| `Tools/Verify-BuildFingerprintArtifact.ps1` | PASS(INDEPENDENT_ARTIFACT_VERIFICATION_PASS) |
| `Tools/Verify-EvidenceClassLayout.ps1` | PASS(EVIDENCE_CLASS_LAYOUT_PASS) |
| `Tools/Verify-Ticket09Documentation.ps1`(PS5.1 原样) | PASS(TICKET09_DOCUMENTATION_METADATA_PASS) |
| `git diff --check` | CLEAN |

## 5. 产物身份(最终指纹,可复现构建)

构建机制未触碰(portable PDB + PathMap,跨路径可复现性延续)。round 2 修复增量(BackFromRole 清空+Reset 清 `_targetPassword`)后重建使 round 1 中间产物失效,下表为**最终唯一有效身份**,Runtime 证据绑定以本表为准;Ticket03 报告指纹(`6A2178D0…`/`016F74C2…`)自本票源码变更起不再可由 HEAD 复现,由本表接替:

| 产物 | SHA-256 | MVID |
|---|---|---|
| `bin/Release/SteamP2PFriends.dll` | `A69B78AFDA3185865FF79AC54708DFCEF625DBF9EE622B6492033D9E76877217` | `2ea4dbc6-be2e-4f72-95d0-dfd438ad0f62` |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `CD08947A0A7A03824D378A01B29B4C3905F6C6A47B00DF6873C99B1384BF456A` | `b5063d14-9937-4b4b-9eb0-b63d297419fc` |

## 6. Seam gaps 与取舍(全部具名,无静默跳过)

1. **`P2PNativeMenuUI.Destroy` 有意不调 `SessionPassword.ClearRuntime()`(round 2 Spec F1 处置的核心取舍)**:`Destroy` 在 U3DS(`CanTouchClientUi()==false`)时被 `EnsureCreated` **每 tick 调用**;若在其中写 `Provider.serverPassword`,dedicated 服务器上 `isServer=true` 阶段会每 tick 触发行内 `SteamGameServer.SetPasswordProtected(false)` 并清掉专用服自身密码——违反 AGENTS.md「U3DS 零侵入」铁规。会话级清空由 `StopP2PServer`(结束会话;ESC 回菜单经 `ProviderDisconnectPatch` 亦汇入)+ `AbortHostStart`(销毁房间)覆盖,菜单返回由 `OnClickedBackFromRole` 覆盖(该处理器只在客户端菜单期可达,`isServer=false`,幂等安全);`Destroy` 仅清理 UI 引用与 `_sessionPassword` 内存态,属其正确职责范围。round 2 双轴确认成立。
2. **「返回菜单」语义的三重覆盖**:字面覆盖=`OnClickedBackFromRole`(开房菜单返回按钮);会话覆盖=ESC→断开→`ProviderDisconnectPatch`→`StopP2PServer`→`ClearRuntime`;失败覆盖=`AbortHostStart`。运行时密码在所有到达菜单的路径上均为空,`OpenRoleMenu` 再将输入框重置为默认空。
3. **客机 lobby/旧 SteamID 菜单路径保持空密码**:`TryConnectFromLobby` 与 `P2PNativeMenuUI.TryJoin` 仍走 `TryConnectToHost(steamId)`(委托 null)。规格密码来源唯一=原版直连页密码框;旧插件内建加入菜单无密码框且禁止新增专用密码弹窗,故不接密码,行为与票前一致。
4. **密码框不设 Tooltip**:`ISleekField` 的 `TooltipText` 归属在本机 Glazier 程序集反编译中未能确认,为不引入运行时风险以注释与 README 承载说明;标签文本「房间密码:」与同菜单其他字段一致。
5. **`ClearRuntime` 的 `SetPasswordProtected(false)` 顺带效应**:`StopP2PServer` 中清空先于 `TryCloseGameServerApi` 执行,GameServer 仍存活时该调用即原版「取消密码保护」语义,方向正确;`AbortHostStart` 与菜单期 `isServer=false` 时 setter 跳过该调用,无副作用。

## 7. 判断性坏味道(全部延期,理由具名;round 2 双轴确认不阻断)

1. **Primitive Obsession(延期)**:会话密码以 `string` 贯穿(菜单字段→`Provider.serverPassword`/`ServerConnectParameters.password` 均为 SDK string)。包一个密码值对象属夸夸其谈通用性,`SessionPassword` 缝已承载策略与隐私出口;随 vanilla SDK 类型演进再议(Standards round 1/2 一致具名)。
2. **`AbortHostStart` 缩进漂移(延期)**:该函数既有代码块缩进本就漂移,本票插入 `ClearRuntime` 两行按现场深度对齐,未整块重排以免混入语义 diff;下次触碰该函数时整块重缩进(Standards round 1/2 一致具名)。
3. **LAN 路径多一条会话密码布尔日志(延期)**:`StartLanServer` 走默认 `sessionPassword=null`,`[SessionPassword] hasPassword=False` 一条 Info 属噪音但语义无害(LAN 行为与票前完全一致);若需静音,把该日志下沉到 P2P 专用路径,随下一次 HostManager 日志结构调整(Standards round 1 具名,round 2 维持)。
4. **round 1 JC-2 已闭合**:`P2PJoinManager.Reset()` 现清空 `_targetPassword`(round 2 修复增量),不再延期。

## 8. Runtime 判读口径(移交票 05 共享验收,1 Host + 2 Guest,普通 PEI)

1. **无密码房间不变**:房主开房菜单密码留空创建 → 客机(裸 SteamID 或 `SteamID:27016`)不填密码可正常进入;房主日志出现 `[SessionPassword] hasPassword=False`。
2. **有密码房间,客机填对**:房主密码框输入任意非空值创建 → 客机在直连页密码框填同一密码,SteamID 路线可进入;日志只有 `hasPassword=True`(房主端+客机端 `[Diag] ServerConnectParameters constructed ... hasPassword=True`),**任何日志不得出现明文或长度**。
3. **有密码房间,客机不填/填错**:原版产生 `PASSWORD` 失败呈现(不改写 `connectionFailureInfo`,无插件第二块提示,无专用弹窗)。
4. **会话生命周期**:该局结束后(ESC 断开回菜单)房主再开新局,上一局密码不得沿用——新局默认无密码(除非重新输入);房主日志在结束路径后可核对 `[SessionPassword] hasPassword=False`。
5. **回归对照**:未设密码房间的进入行为、IPv4 直连/DNS 路线的密码传递、U3DS 专用服路径,均与 0.2.4.8 前完全一致(JR4 空参断言 + `StartLanServer` 默认参 + diff 未触及失败/UI 语义)。




