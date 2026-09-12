# Ticket 02 实施与交付报告:物品周期生命周期 Listen-Host Dedicated Gate(ItemManager.Update 单点门控对齐)

- **日期**:2026-09-12 10:43
- **基线**:HEAD `5f642c9`(chore(ticket01): track listen-host batch plans, context terms, and audit)
- **票据**:`.scratch/listen-host-dedicated-gate/issues/02-item-periodic-lifecycle-dedicated-gate.md`(规格 `spec.md` 同目录)
- **范围**:listen-host-dedicated-gate 批 3 票实施票 02(物品周期门)。共享 Runtime 验收归票 05(`listen-host-join-routing-runtime-acceptance`),本票不宣称 Runtime PASS。
- **冻结图对表**:`.scratch/map-top-level-architecture-blueprint/map.md` 铁规 1(单机/听主本地零破坏)、铁规 2(严禁全局伪造 `Dedicator.IsDedicatedServer`)、TDD 闭环;票 02 Blocked by:None,与票 01 无代码阻塞边,先例直接沿用。

## 1. 结论

票面六项全部静态闭环:红→绿链(271/274 契约红→274/274)、双 Release 0/0、三项静态门禁 PASS、`git diff --check` 干净;双轴审查 **round 1 Standards CLEAN / Spec NOT CLEAN(F1、F2)→ 修复 → round 2 双轴 CLEAN**(每轮全新实例,无 blocking,延期项全部具名于 §7)。新交付物指纹见 §5。**物品周期 despawn/respawn 语义的 Runtime 证据未到位前,本票状态为 `implemented-pending-runtime`。**

## 2. 变更清单(新增 3 文件 1193 行;修改 4 文件 +47/−0)

| 文件 | 变更 |
|---|---|
| `Adapters/Item/Patches/ItemManagerUpdateDedicatedGatePatch.cs`(新,900 行) | ① `ItemManager.Update` 尾部早退守卫 `(!Dedicator.IsDedicatedServer \|\| !Level.isLoaded)` 单点 Transpiler:`Dedicator.get_IsDedicatedServer`→`ListenRegionSyncEligibility.IsDedicatedOrP2PHost()`,replacement 必须恰=1 否则抛(登记即失败进 `DiagnosticBuildValid` 阻断门);签名自检(private instance void,0 参)+ Transpiler owner 唯一性自检,形状逐处对照 ticket01 先例。② 三路区域观测探针(观测式,不生成/不销毁任何实体,不构成第二套生成器):`generateItems(byte,byte)` Prefix 按区域计数(onLevelLoaded 预生成与 onRegionUpdated 进区两个 vanilla 来源均被观测,生成算法仍由 AuthoritativeItemGenerationGatePatch 账本约束);`despawnItems()` Prefix/Postfix 快照 vanilla 静态轮转游标(反射读 `despawnItems_X/Y`)与区域 items.Count,由纯函数 `ObserveDespawn` 五分判定(ArenaOrNoLevel/RemovedExpired/OccupiedNoExpiry/EmptyScanned/AnomalyGrew);`respawnItems()` Prefix/Postfix 同款快照(游标 `respawnItems_X/Y`、spawns 存在性、窗口观测值 `now-lastRespawn > Respawn_Time` 只读观测),纯函数 `ObserveRespawn` 七分判定(NoSpawnpoints/Cooldown/WindowPassedIdle/Respawned/RespawnedNoAsset/AnomalyShrunk)。③ 三路日志独立 5s 限频时钟 + `WorldSyncDiagnosticCore` 配额点(Item.generateItems / Item.despawnItems / Item.respawnItems),计数不受限频影响;会话重置回调清计数;Verbose 关闭时探针立即返回(热路径成本 ≈1 次 bool 读) |
| `SteamP2PFriends.csproj` | +1 行 Compile(领域物理路径 `Adapters\Item\Patches\`) |
| `Core/Registration/...PatchRegistrationLegacyDiagnostics.cs` | +13:ticket01 块后新增 RegisterManual 块(同 try/catch → `_registrationStageFailed`) |
| `Core/Registration/...PatchRegistrationCriticalVerification.cs` | +26:ticket01 验证块后新增 `AllRegistrationsSucceeded` 阻断门(失败强制 DIAGNOSTIC BUILD INVALID,输出 summary/replacement/signature/transpilerOwner) |
| `WhitelistTests/Evidence/StaticIL/Adapters/Item/ItemUpdateDedicatedGateStaticILTests.cs`(新,129 行) | IG1:现行 U3 IL(`PatchProcessor.GetCurrentInstructions maxTranspilers:0`)中 `ItemManager.Update` 方法体 `get_IsDedicatedServer` 调用点恰 1 处(onRegionUpdated 内调用点属另一方法,不在契约内);IG2:transpiler 契约——ReplacementCount==1、**签名等价栈平衡断言(两函数 0 参 + bool 返回 ⇒ 调用点栈净变化一致)**、替换后 dedicated 0 处/eligibility 恰 1 处、指令数不变、非目标指令 opcode/operand 逐位不变、调用点后短路消费者 opcode 不变、替换目标是现有资格函数本身(先例 ticket01 ZG1/ZG2 / M1I06) |
| `WhitelistTests/Evidence/PureMemory/Adapters/Item/ItemUpdateDedicatedGateEligibilityTests.cs`(新,164 行) | IGP3 资格回归锁(听主机真含 LAN 形态/专用服真/普通单机假/客机假/菜单假——工单「普通单机/客机早退不变」子句,接线由 IG1/IG2 联合闭合)、IGP1 `ObserveDespawn` 纯函数表(5 用例含 AnomalyGrew 不可能形态)、IGP2 `ObserveRespawn` 纯函数表(6 用例,含 Runtime 判别关键形态:**Cooldown=窗口未到(calls>0)≠早退仍在(calls==0)**)。反射设置 vanilla/HostManager 静态 backing field,finally 逐项精确还原 |
| `WhitelistTests/Program.cs` | 注册 5 个新测试(269→274 注册数,单一入口纪律保持) |

未触及:`onLevelLoaded` 一次性预生成(spec 明令保留)、`AuthoritativeItemGenerationGatePatch` 每区域每会话账本、`onRegionUpdated` askItems/本地分支门控(P0-B 系列已对齐)、despawnItems/respawnItems/generateItems 生成算法本体、`Dedicator.IsDedicatedServer` 本体(不全局伪造)、版本号、`docs/architecture/` 基线工件、`.scratch` 其他批票单。

## 3. 红→绿链(TDD 闭环,逐轮实测)

1. **RED-0**(类型期):测试文件落地后主/测试工程编译失败(CS0246/CS0103,`ItemManagerUpdateDedicatedGatePatch` 不存在)——红测先行的第一形态。
2. **RED-1**(骨架期):patch 结构落全但 Transpiler 只计数不替换 + `ObserveDespawn/ObserveRespawn` 返回 NotObserved 占位 → **271/274**,红点恰为本票三个契约缺口:`IG2 ItemUpdateGateTranspilerContract`、`IGP1 ItemDespawnObservation`、`IGP2 ItemRespawnObservation`。`IG1`(调用点唯一性)与 `IGP3`(资格回归锁)自始绿——它们锁的是「缝已存在且现行 U3 IL 满足单点前提」,属回归锁而非本轮修复对象;此定性如实具名(与 ticket01 §3-1 同一定性)。
3. **GREEN-1**:实现原位替换与两个三分判定函数 → **274/274**,双 Release Rebuild 0 errors / 0 warnings(`TreatWarningsAsErrors`)。
4. **GREEN-2(round 2 修复)**:Spec 轴 round 1 F1(诊断三路未闭合——generateItems 只计数不输出)+ F2(栈平衡无测试断言)→ 新增 `TryEmitGenerateLog`(配额点实发,同时消除 round 1 Standards P3 死代码)、三路日志拆分独立限频时钟(同时收口 Standards P2 共用时钟吞窗)、IG2 增加签名等价栈平衡断言 → **274/274** 复绿,门禁复跑全 PASS。

## 4. 验证矩阵(静态门禁,全部实测)

| 门禁 | 结果 |
|---|---|
| 主 DLL Release Rebuild | 0 errors / 0 warnings(`TreatWarningsAsErrors`) |
| 测试 exe Release Rebuild | 0 errors / 0 warnings |
| 全套测试(唯一入口) | **274/274 PASS**(原 269 + 新增 5) |
| `Tools/Verify-BuildFingerprintArtifact.ps1` | PASS(INDEPENDENT_ARTIFACT_VERIFICATION_PASS) |
| `Tools/Verify-EvidenceClassLayout.ps1` | PASS(EVIDENCE_CLASS_LAYOUT_PASS) |
| `Tools/Verify-Ticket09Documentation.ps1`(PS5.1 原样) | PASS(TICKET09_DOCUMENTATION_METADATA_PASS) |
| `git diff --check` | CLEAN |

## 5. 产物身份(最终指纹,可复现构建)

构建机制未触碰(portable PDB + PathMap,跨路径可复现性延续)。§3 各轮重建使旧中间产物失效,下表为本轮**唯一有效身份**,Runtime 证据绑定以本表为准;Ticket01 报告指纹(C1BE82E4…/121ED075…)自本票源码变更起不再可由 HEAD 复现,由本表接替:

| 产物 | SHA-256 | MVID |
|---|---|---|
| `bin/Release/SteamP2PFriends.dll` | `9887C2280CACEADA725A09396820D0D5747BB22F56723C11A80E814BB72E0C4B` | `d786adb7-120d-45fe-b68f-8dd79a7f034b` |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `C0957E52F6888F63634C28FF95BC544A88A7A9BFC7B7BDEBE1F302581F2A648C` | `0d2754be-db91-48c3-aa10-3ea494f1e1b6` |

## 6. Seam gaps 与取舍(全部具名,无静默跳过)

1. **「普通单机/客机早退不变」的证据分解**:资格函数无 Update 参数,该子句不可能由单一 PureMemory 断言完整覆盖。采用两证据域联合闭合:IGP3(听主机真/单机假/客机假——被替换资格位)+ IG1/IG2(替换恰好落在该守卫调用点且仅一处——接线)。Spec 轴 round 2 复核判定其余条款满足、无缺口。
2. **探针读 `Provider.modeConfigData.Items.Respawn_Time` 与 `LevelItems.spawns`**:窗口观测值是 vanilla 同一比较式(`Time.realtimeSinceStartup - lastRespawn > Respawn_Time`)的只读重放,不重写生成逻辑、不生成任何实体——与 ticket01 `ObservePass` 观测 `respawnZombieIndex` 轮转同一哲学,不构成第二套生成器(Standards round 1/2 复核确认)。测试宿主不执行任何 hook(纯函数直调),不受 BattlEye/资产缺失影响。
3. **`ReadCursors` 游标快照与 vanilla 轮转的竞态**:despawn/respawn 的轮转游标是 vanilla 静态字段,Update 游戏线程单线程执行,Prefix→vanilla→Postfix 同线程内游标不变;反射读取 fail-closed(不可解析时该次调用不计数,`__state=null`),不干预 vanilla。诊断计数的完备性取舍:fail-closed 丢弃的样本在 `calls` 上表现为低估,不产生误报(Runtime 判读以 calls>0 与形态计数为准)。
4. **诊断计数绑定 Verbose 开关**:despawn/respawn 轮转每帧多次调用、generateItems 在 dedicated onLevelLoaded 单帧 4096 次调用,均为热路径;Verbose 关闭时探针零成本返回(与 ticket01 同款更严格取舍)。Runtime 验收(票 05)按 SOP 使用 UMM 诊断包(verbose 默认开启),不受影响。
5. **三路日志独立时钟**(round 2 修复引入):round 1 Standards P2 指出共用 `_lastDiagLogTime` 会让先到日志吞掉另一路 5s 窗——respawn 行印有 Runtime 判读关键串(calls==0 ⇒ 早退仍在),被吞窗会损害验收证据链。修复为三字段分时钟,会话重置同步清零。
6. **README 33 行「地面掉落物不按专用服节奏消失/再生」将在本票提交中同步标注 ticket02 已实现待 Runtime**(与 ticket01 的 README 行同款最小修改,见提交)。

## 7. 判断性坏味道(全部延期,理由具名;双轴 round 2 确认不阻断)

**Standards 轴 round 1 → round 2 闭合情况**:
1. ~~P2 双路日志共用限频时钟~~ → round 2 修复(§6-5),已闭合。
2. ~~P3 `DiagQuotaGenerateId` 死代码~~ → round 2 随 F1 修复实发,已闭合。
3. **P3 `RegionCounters` 数据团(延期)**:generate/despawn/respawn 三表共用同一胖计数结构,generate 路径只用 `Calls` 字段。拆分为三个窄结构是纯内部重构,不影响观测语义与 Runtime 判读;随下一次该文件结构轮收口。
4. **P3 IGP 序号与 Zombie 区夹杂(延期)**:`Program.cs` 中 IGP* 注册行插在 DG4 之后 Animal 区之前,与 01 的 ZG-P 序号语义(ZG-P1=资格)不完全一致。注册区按域切分属测试入口结构性整理,不在本票最小增量内,随批独立收束。

**Spec 轴 round 1 → round 2 闭合情况**:F1(诊断三路未闭合)、F2(栈平衡无断言)均 round 2 修复并复测,裁决 CLEAN。

## 8. Runtime 判读口径(移交票 05 共享验收,1 Host + 2 Guest,普通 PEI)

1. **早退已打开的判据**:`[ItemGateDiag/Item] respawnItems … elig=true region=(x,y) calls=…` 行存在且 `calls` 持续增长(轮转每帧推进)。`elig=true` 且 `respawnItems` `calls==0` 持续整个测试窗 ⇒ 早退仍在,FAIL。
2. **「窗口未到」与「早退仍在」的区分**:听主机进世界后立即存在 `calls>0` 与 `cooldown`/`idle`/`nospawn` 计数 ⇒ 早退已打开、仅等待 `Respawn_Time` 窗口(不判 FAIL);整个窗口 `calls==0` ⇒ FAIL(spec 测试决策条款)。
3. **周期 despawn 生效判据**:`removed` 计数随时间增长(过期物品被移除并广播 SendDestroyItem);`occupied` 增长为非空无过期区域停留,非失败形态。
4. **再生生效判据**:`spawned` 计数在 despawn 后随 `Respawn_Time` 窗口增长;目标量由 vanilla `spawns.Count × Spawn_Chance` 决定,与预生成/进区生成共享同一上限。
5. **刷满/无界累加风险(工单子句 5)**:按区域观测 `spawned` 与 `calls` 曲线——vanilla 目标量机制下 `spawned` 应收敛;若同一区域 `spawned` 持续无界增长,本票 Runtime FAIL,另开收缩票(spec/工单明令禁止在本票内临时重构预生成)。
6. **generateItems 观测语义**:`calls` 覆盖两个 vanilla 来源(听主机本地玩家进区触发、dedicated 由 onLevelLoaded 触发);听主机上每次会话每区域受 `AuthoritativeItemGenerationGatePatch` 账本约束只执行一次,重复调用以 `skip` 日志(Authoritative 前缀)佐证,不构成本票判据。
7. **单机回归判据**:未开 P2P 的单机世界 `[ItemGateDiag/Item]` 行不应出现 `removed/spawned` 增长(vanilla 早退使 despawn/respawn 不被调用 ⇒ calls==0;若单机出现增长 ⇒ 本票破坏单机零侵入,FAIL)。
