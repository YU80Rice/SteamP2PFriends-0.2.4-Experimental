# Ticket 01 实施与交付报告:僵尸重生 Listen-Host Dedicated Gate(respawnZombies 单点门控对齐)

- **日期**:2026-09-12 00:27
- **基线**:HEAD `639e65b`(docs: rewrite experimental README and add contributing guide)
- **票据**:`.scratch/listen-host-dedicated-gate/issues/01-zombie-respawn-dedicated-gate.md`(规格 `spec.md` 同目录)
- **范围**:listen-host-dedicated-gate 批 3 票实施票 01(僵尸重生门)。共享 Runtime 验收归票 05(`listen-host-join-routing-runtime-acceptance`),本票不宣称 Runtime PASS。
- **冻结图对表**:`.scratch/map-top-level-architecture-blueprint/map.md` 铁规 1(单机/听主本地零破坏)、铁规 2(严禁全局伪造 `Dedicator.IsDedicatedServer`)、TDD 闭环;票 01 Blocked by:None,按规格实施顺序先于 Join Routing。

## 1. 结论

票面六项全部静态闭环:红→绿链(267/269 契约红→269/269)、双 Release 0/0、三项静态门禁 PASS、`git diff --check` 干净;双轴审查 **round 1/2/3 全部 CLEAN**(每轮全新实例,无 blocking,延期项全部具名于 §7)。新交付物指纹见 §5。**Resource 域 fault-free 结论不延伸至本票;僵尸重生语义的 Runtime 证据未到位前,本票状态为 `implemented-pending-runtime`。**

## 2. 变更清单(新增 3 文件 897 行;修改 6 文件 +118/−2)

| 文件 | 变更 |
|---|---|
| `Adapters/Zombie/Patches/ZombieManagerRespawnZombiesDedicatedGatePatch.cs`(新,609 行) | ① `ZombieManager.respawnZombies()` 单点 Transpiler:`Dedicator.get_IsDedicatedServer`→`ListenRegionSyncEligibility.IsDedicatedOrP2PHost()`,replacement 必须恰=1 否则抛(登记即失败进 `DiagnosticBuildValid` 阻断门);签名自检(instance/void/0 参)+ Transpiler owner 唯一性自检,形状逐处对照 P0-C1 先例。② 重生门观测 Prefix/Postfix:观察守卫通过后唯一确定性副作用 `ZombieRegion.respawnZombieIndex` 轮转(过守卫先 clamp 再 ++ 后回绕,早于 isDead/窗口/安全区),纯函数 `ObservePass` 四分判定(Passed/EarlyReturned/AmbiguousSingleZombie/NotObserved),按 bound 累计 calls/passed/returned/ambiguous,5s 时间窗 + `WorldSyncDiagnosticCore` 配额限频,会话重置回调清计数;Verbose 关闭时 Prefix 立即返回(每帧热路径成本 ≈1 次 bool 读)。③ `RegisterManual` 聚合 transpiler+probe 全部自检状态供 CriticalVerification 读取 |
| `SteamP2PFriends.csproj` | +1 行 Compile(领域物理路径 `Adapters\Zombie\Patches\`) |
| `Core/Registration/...PatchRegistrationLegacyDiagnostics.cs` | +13:P0-C1 块后新增 RegisterManual 块(同 try/catch → `_registrationStageFailed`) |
| `Core/Registration/...PatchRegistrationCriticalVerification.cs` | +26:P0-C1 验证块后新增 `AllRegistrationsSucceeded` 阻断门(失败强制 DIAGNOSTIC BUILD INVALID,输出 summary/replacement/signature/transpilerOwner) |
| `WhitelistTests/Evidence/StaticIL/Adapters/Zombie/ZombieRespawnDedicatedGateStaticILTests.cs`(新,121 行) | ZG1:现行 U3 IL(`PatchProcessor.GetCurrentInstructions maxTranspilers:0`)中 `respawnZombies` 方法体 `get_IsDedicatedServer` 调用点恰 1 处;ZG2:transpiler 契约——ReplacementCount==1、替换后 dedicated 0 处/eligibility 恰 1 处、指令数不变、非目标指令 opcode/operand 逐位不变、调用点后一指令(短路分支消费者)opcode 不变、替换目标是现有资格函数本身(先例 M1I06 直读 IL + null ILGenerator) |
| `WhitelistTests/Evidence/PureMemory/Adapters/Zombie/ZombieRespawnDedicatedGateEligibilityTests.cs`(新,167 行) | DG1 真值表(专用服真/听主机真/普通单机假/客机假/菜单假)、DG2 听主机普通 PEI 场景资格恒真(含 LAN 形态回归锁)、DG3 部分状态 fail-closed(4 种缺失形态)、DG4 `ObservePass` 纯函数表(7 用例)。反射设置 vanilla/HostManager 静态 backing field,finally 逐项精确还原,字段缺失抛具名异常 |
| `WhitelistTests/Program.cs` | 注册 6 个新测试(263→269 注册数,单一入口纪律保持);Main 入口安装 `InstallBattlEyeTypeResolutionStub()`(见 §6-2) |
| `README.md` | 恢复 Ticket09 文档门禁两契约串 + 僵尸重生行标注「ticket01 已实现,待 Runtime 验收」(见 §6-5) |

未触及:`SendZombieStates` 门控(P0-C1 已对齐)、`generateZombies` 进图预生成、生成表抽取/特化/Boss 上限/Beacon 剩余/`respawnZombiesBound` 轮转/Update 内 dedicated tick 切片、Horde 波次逻辑、`Dedicator.IsDedicatedServer` 本体(不全局伪造)、版本号、`docs/architecture/` 基线工件、`.scratch` 其他批票单。

## 3. 红→绿链(TDD 闭环,逐轮实测)

1. **RED-1**(骨架期):`ReplacementCount` 计数但不替换 + `ObservePass` 返回 NotObserved 占位 → **267/269**,红点恰为本票两个契约缺口:`DG4 GuardPassObservation`、`ZG2 RespawnGateTranspilerContract`。`DG1-DG3`(既有资格缝真值表)与 `ZG1`(调用点唯一性)自始绿——它们锁的是「缝已存在且现行 U3 IL 满足单点前提」这一事实,属回归锁而非本轮修复对象;此定性如实具名。
2. **GREEN-1**:实现原位替换与 `ObservePass` 三分判定 → **269/269**。
3. **RED-2(round 2 修复)**:Spec 轴 F2(count==1 且陈旧索引 3→3 实为早退,被误标 Ambiguous)→ 先加用例 `(1,3,3)=>EarlyReturned` → **268/269**,`DG4 FAIL` 实测复现。
4. **GREEN-2**:`ObservePass` 细化为「`!=`→Passed;`==` 且 count==1 且 before==0→Ambiguous;其余 `==`→EarlyReturned」→ **269/269**。同一轮处理 F1:删除 ZG2 中 `ReferenceEquals` 对象身份锁(规格禁止断言 IL 指令序列之外的实现风格;opcode/operand 快照逐位对比保留全部语义强度)。
5. **ROUND-3**:Standards 轴 round 2 发现 `ObservePass` XML 摘要与新三分漂移 → 纯注释对齐,无语义变更 → 269/269 复绿。

## 4. 验证矩阵(静态门禁,全部实测)

| 门禁 | 结果 |
|---|---|
| 主 DLL Release Rebuild | 0 errors / 0 warnings(`TreatWarningsAsErrors`) |
| 测试 exe Release Rebuild | 0 errors / 0 warnings |
| 全套测试(唯一入口) | **269/269 PASS**(原 263 + 新增 6) |
| `Tools/Verify-BuildFingerprintArtifact.ps1` | PASS(INDEPENDENT_ARTIFACT_VERIFICATION_PASS) |
| `Tools/Verify-EvidenceClassLayout.ps1` | PASS(EVIDENCE_CLASS_LAYOUT_PASS) |
| `Tools/Verify-Ticket09Documentation.ps1`(PS5.1 原样) | PASS(TICKET09_DOCUMENTATION_METADATA_PASS) |
| `git diff --check` | CLEAN |

## 5. 产物身份(最终指纹,可复现构建)

构建机制未触碰(portable PDB + PathMap,跨路径可复现性延续 2337 §二结论)。§3 各轮重建使旧中间产物失效,下表为本轮**唯一有效身份**,Runtime 证据绑定以本表为准:

| 产物 | SHA-256 | MVID |
|---|---|---|
| `bin/Release/SteamP2PFriends.dll` | `C1BE82E41E699C9223B7BC2F072BFD6D9B184474D2311AC539E2EB18C99E3C4A` | `24a97570-cbaf-4d4e-9448-a5526a4dfc65` |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `121ED075D512782106B694D659AC7D5F10CC3F59B109419D5560DC40FF477C39` | `63b102ba-c659-4914-8e71-dcd69e988909` |

**Provenance 更新**:上表取代 Ticket 12 期间的 `BBBDCC25…`/`86C4D5E6…` 对(源码已变,旧值不再可由本 HEAD 复现)。

## 6. Seam gaps 与取舍(全部具名,无静默跳过)

1. **「无信标、非 Horde 不得仍走单机直接 return」的证据分解**:资格函数无 beacon/levelType 参数,该子句不可能由单一 PureMemory 断言完整覆盖。采用两证据域联合闭合:DG2(听主机资格恒真——被替换项)+ ZG1/ZG2(替换恰好落在该守卫调用点且仅一处——接线)。Spec 轴 round 1 复核判定「分解忠实、无缺口」。
2. **测试宿主 BattlEye 类型解析 stub**(非 seam gap,如实具名):DG1-DG3 要真实执行 `IsDedicatedOrP2PHost()`,其调用链触达 `Provider..cctor`;IL 实测 cctor 仅向 4 个 `[BattlEye]` 类类型静态字段写入 null,不调用任何成员,但 JIT 需要解析该程序集类型。本机 Libs 快照无 `BattlEye.dll`(游戏运行时组件,非插件编译依赖)。处置:`Program.Main` 安装仅响应 `BattlEye` 程序集名的 AssemblyResolve,合成 Run 模式动态壳程序集(4 个空类类型)。不落盘、不进 Libs、不随插件打包、不改变游戏内解析;资格函数本体为真实生产代码路径。
3. **count==1 且 0→0 的唯一不可判形态**:vanilla 语义下该形态过守卫与早退不可区分(round 2 起其余 count==1 原样形态已可判早退)。诊断以封闭枚举成员 `AmbiguousSingleZombie` 显式具名并计数,禁止参与误判;普通 PEI 区域多尸形态(count>=2)不受影响。Runtime 判读口径见 §8。
4. **诊断计数绑定 Verbose 开关**:`respawnZombies` 为每帧热路径,Verbose 关闭时探针零成本返回(比 WorldSync 周期探针更严格)。这是每帧成本铁规下的显式取舍;Runtime 验收(票 05)按 SOP 使用 UMM 诊断包(verbose 默认开启),不受影响。
5. **README/Ticket09 门禁既有破损**:HEAD 提交 `639e65b`(README 重写)删除了 `Verify-Ticket09Documentation.ps1` 要求的内容契约串,门禁在本票开工前已红(实测确认:工作区 diff 不含 README 前,脚本即 FAIL)。本轮做最小真实修复:README 增补一句作用域正确的验收状态陈述(本批 Runtime 为 `PENDING`、Develop-Stage/Archive 为历史运行证据),不改写门禁脚本、不弱化契约。Standards/Spec 双轴 round 1 均确认「必要、最小、诚实」。
6. **CONTEXT.md 词条为本批规格会话产物**:会话开始时工作区已含该未提交改动(含 Listen-Host Dedicated Gate/Join Routing/Session Password/Animal Pack 四词条),本票未编辑之,仅随批入库(Standards round 1 具名为夹带+无新 ADR,见 §7-2)。

## 7. 判断性坏味道(全部延期,理由具名;双轴均确认不阻断)

**Standards 轴 round 1**:
1. 诊断 Prefix/Postfix `catch` 静默:热路径每秒多次触发,若逐次 `RoleLogger.Error` 有错误洪水风险;生产正确性(transpiler 替换失败)仍走异常+阻断门路径,观测探针 fail-closed 不影响门控语义。随下一次该文件结构轮收口。
2. CONTEXT.md 四词条夹带且无新 ADR:改动先于本票存在(§6-6);Listen-Host Dedicated Gate 是实现级门控对齐而非新架构决策,ADR 义务归属存疑,建议随批独立收束。
3. `docs/architecture/item-zombie-ownership.md` 为 Ticket06 基线快照,未登记本票新入口:领域归属未变(仍 `Adapters/Zombie/Patches`),基线工件刷新应随结构基线统一动作,不在本票私改冻结快照。
4. `Program.cs` 横幅 `Target: 255 PASS` 陈旧:先于本票存在(263 时已陈旧),纯装饰串,无门禁引用,随批刷新。

**Spec 轴 round 1→已修**:F1(ZG2 实现风格锁)、F2(count==1 早退误标)——非延期,round 2 修复并复测。
**Standards 轴 round 2→已修**:ObservePass XML 摘要漂移——round 3 注释对齐。

## 8. Runtime 判读口径(移交票 05 共享验收,1 Host + 2 Guest)

本票机制断言在共享轮次中的判读(与 Join Routing 断言分列):
- 部署:§5 主 DLL SHA-256 `C1BE82E4…C3C4A` 三端一致(覆盖前备份,指纹不符即停)。
- 主机 UMM 日志(Verbose 开):`[RespawnGateDiag/Zombie] respawnZombies #k/20 elig=True bound=… calls=… passed=… returned=… ambiguous=… allPassed=…`;**判据**:普通 PEI(无信标、非 Horde 图)打死后,对应 bound 的 `passed` 随时间增长 ⇒ 走进对齐分支;`elig=True` 而 `passed` 恒 0 ⇒ 早退仍在(本票 FAIL);`passed>0` 但区域僵尸数未回升 ⇒ 以窗口计时器/`ambiguous` 计数复核「窗口未到」,不得直接判失败(规格测试决策条款)。
- Horde/Beacon 不专项开场景,但本轮日志不得出现二者语义变化(波次/信标剩余计数行为与原版一致)。
- 若预生成与周期重生重复刷怪:本批 Runtime FAIL,另开收缩票,禁止在本票内重构预生成(规格条款)。

## 9. 双轴审查链(output-review-loop 步骤 4 收口记录)

| 轮次 | 增量 | Standards | Spec | 处置 |
|---|---|---|---|---|
| 1 | 全部实现+测试 | CLEAN(4 延期具名) | CLEAN(F1/F2 deferrable) | F1/F2 进 round 2 修复 |
| 2 | F1/F2 修复(先红 268/269 复测) | CLEAN(XML 注释漂移 1 项) | CLEAN(F1/F2 判定关闭) | 注释进 round 3 |
| 3 | 纯注释对齐 | CLEAN(零发现) | CLEAN(零发现) | 闭环 |

每轮均为全新双实例并行调度(`standards-reviewer`/`Spec-Reviewer`),无续用;审查对象均为当轮增量。产物身份(§5)授予本轮闭环后工件;中间 DLL 不携 Case-ID。
