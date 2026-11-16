# T98 — G 门六格姿态覆盖：修复轮（Fixes t96 findings + t97 R17-01..R17-04）

- 任务：t98「repair-round-2」，attempt `25cceea9-1117-47c6-a57e-3b37b5015e78`
- 基线：t95 终态（`Bug1GateProbeLogic.cs` 1275 行 `54929772…`、`Bug1GateProbe.cs` 1497 行 `33c65d22…`、`Bug1GateProbeTests.cs` 842 行 `6896fdf5…`、`Program.cs` 1108 行 `78f2b6b1…`、DLL `8413c484…`；787/789 PASS、Bug1 段 103/103）
- 本报告只描述**测量能力**的修复，不构成「Bug1 顿挫已定位」的结论；G 门是否成立仍取决于真实 1H+2G 采集。

## 1. t96 三条 acceptance 的处置

### 1.1 idle 格被 walk 冒充（t96-F1）

根因：SDK 的 `EPlayerStance` 枚举是 `CLIMB/SWIM/SPRINT/STAND/CROUCH/PRONE/DRIVING/SITTING`，**没有 IDLE**。旧 `ClassifyStanceCell(string)` 只把字符串 `"IDLE"` 映射到 idle，而生产侧 `StanceName()` 返回的是枚举名，于是所有站立样本（无论是否在走）都落进 walk 格，idle 格永远拿不到样本。

修复：
- `ClassifyStanceCell(string stanceName, bool moving, out string reason)`：`SPRINT → sprint`；`STAND + moving → walk`；`STAND + !moving → idle`；`CROUCH/PRONE/SWIM/CLIMB/DRIVING/SITTING → unstable(stance_out_of_scope:…)`；空 → `stance_unreadable`；其它未知值 → `stance_unknown:…`。**不再存在 `"IDLE"` 分支**，因此不可能再被不存在的枚举值冒充。
- 移动态取自原生 `PlayerMovement.isMoving`（SDK `PlayerMovement.cs:290-291` 公开属性，`:1108` 由输入 `move.x/move.z` 阈值 0.1 计算），是稳定量，不用速度差分。
- 生产侧 `SampleOnce` 每次采样都记录真实 `stance` 枚举名（`Bug1GateCellSample.Stance`），并写入 `cells[].stances`。

### 1.2 stance / measured_yaw 入产物，朝向窗收敛到 ±5°（t96-F2）

- `ClassifyHeadingBucket(float forwardX, float forwardZ, out float yawDeg, out string reason)`：`yaw = atan2(fx, fz)` 归一化到 [0,360)，与标称 0°（+Z）和 90°（+X）比较，容差 `HeadingToleranceDeg = 5f`；窗外 → `unstable(yaw_outside_nominal)`。旧的「主轴分量 ≥ 次轴 1.5 倍」等价于 ±33.7° 窗，已删除。
- 每个样本记录 `MeasuredYawDeg`；`cells[]` 输出 `stances`、`measured_yaw_deg_min`、`measured_yaw_deg_max`，可直接复核「这一格为什么收进了这条样本」。
- 没有放宽 t94 §2：`±5°` 按裁决书实现，未做任何默许放宽。

### 1.3 note 按模式拆分（t96-F3）

- `BuildModeNote(bool coverageMode, int samples, int validSamples)`：
  - 覆盖模式：`六格采样：姿态由 stance（STAND+isMoving 派生 idle/walk、SPRINT→sprint）与实测 yaw（0°/90°±5°）区分；采样 N 次、有效样本 M 个`
  - 单次测量路径：`single_frame_single_pose：…`
- `RunGate`（单次测量）写单帧 note；`FinishCoverage`（覆盖采样）写覆盖 note，不再复用 `_lastResult` 里的过期单帧说法。

## 2. t97 工程轴四条 + 三条全绿变异的处置

| finding | 问题 | 修复 | 防回归用例 |
| --- | --- | --- | --- |
| R17-01 / MV-1 | `CollectSubtree` 无节点/深度上限，且生产遍历零覆盖 | 新增 `MaxPoseNodes=512`、`MaxPoseDepth=64`、纯函数 `PoseWalkAllowed(collected,maxNodes)`；`CollectSubtree` 改为带 `depth` 的重载并在入口判定，超限即停止 | Bug1-104：边界值 + `CollectSubtree → PoseWalkAllowed` 的 IL 断言 |
| R17-02 / MV-4 | 「连续 2 帧」实际按**采样**递增，注释与常量却叫帧（默认 1s 间隔，语义错误） | 常量改名 `HeadingStableSamples` 并注明单位；稳定性判定抽成纯函数 `AdvanceHeadingStability(prev,cur,count,out newCount)`（换桶/unstable 清零），生产侧只调它 | Bug1-103：首样本不稳、连续两次接受、换桶清零 |
| R17-03 / MV-5 | 还原门无用例；且 `MeasureCellAxis` 还原后**没有 P2 复烘**（t95 报告写成「复烘 P2」与代码不符） | 位级还原门抽成 `RestoreBitsEqual(float[],float[])` 并由 `MeasureCellAxis` 调用；还原后新增 P2 复烘，与 P0 基线比较，`restore_residual_u > RestoreEpsilonU` 即 `surfaceRestored=false`，并入 `InvarianceOk` | Bug1-105：1 ULP 判不等、长度/null 判不等、`MeasureCellAxis → RestoreBitsEqual` 的 IL 断言 |
| R17-04 | unassigned / 姿态不稳样本也占 `MaxSamples=240` 预算 | 新增 `CountValidSamples`（只数 `Available`），`CheckCoverageLimits` 用它判上限 | Bug1-106：计数规则 + `CheckCoverageLimits → CountValidSamples` 的 IL 断言 |
| t96-F3 | 过期 `single_frame_single_pose` note 与六格口径矛盾 | 见 §1.3 | Bug1-107：两种模式文案 + `FinishCoverage → BuildModeNote` 的 IL 断言 |

## 3. 文件与哈希（改前 → 改后）

| 文件 | 改前 | 改后 |
| --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1GateProbeLogic.cs` | 1275 行 `549297721f284fe2f46c6f670d2e319e2536c0876cace9e7ad9cd26e0e4f09d5` | 1416 行 `7a0c6d24de12934a5af92bc9e1bdffc89cf7595a8c7bba0eb16d9f2f67937c8d` |
| `Platform/Diagnostics/Patches/Bug1GateProbe.cs` | 1497 行 `33c65d22e98fef96665c4a0205156d9bc42fc7f28e92d51c3209e21b81501dc5` | 1554 行 `1d58f950600c3d54d2b3c69021a6f747a97e946dba27b7797d0fa487ae1e6651` |
| `WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs` | 842 行 `6896fdf5101bdb3213332f961d84363de19a501755c1b6c9756b84d9eb27414e` | 998 行 `3e5bae4e3f6efb8c3448e8c44f5bb87ff9b2d8874f18bbe2efec5eaf88b69561` |
| `WhitelistTests/Program.cs` | 1108 行 `78f2b6b165c6254b3e44055e74e9162c0652ed928f8852198880c929b8a09446` | 1114 行 `1d6d979dfd15678bec3247a0502bb1f44f188168321c960d70453616d3f0e460` |
| `bin/Release/SteamP2PFriends.dll` | `8413c48434c07084e0764b918fd3d47a3c6cdba8e1de51b49befd4a564dd7fc5` | `ad1283ac0ea513c414c175caf91d8039da7bcfac43d5e9992424ea81c5766a2a` |

`SteamP2PFriends.csproj`、`Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs` 未改。`WhitelistTests/Program.cs` 只新增 Bug1-103…Bug1-108 六行，`T69-01` 注册行未动（并发窗口产物，与本票改动互不影响）。

## 4. 新增用例（6 条）

| 编号 | 用例 | 钉住什么 |
| --- | --- | --- |
| Bug1-103 | `Test_T98_HeadingStabilityCountsSamples` | 稳定性单位是采样；换桶/unstable 清零 |
| Bug1-104 | `Test_T98_PoseWalkIsBounded` | 节点上限边界 + 生产 `CollectSubtree` 真的调用 `PoseWalkAllowed` |
| Bug1-105 | `Test_T98_RestoreGateIsBitLevel` | 1 ULP 判不等 + 生产 `MeasureCellAxis` 真的调用 `RestoreBitsEqual` |
| Bug1-106 | `Test_T98_BudgetCountsOnlyValidSamples` | 只有可用样本占预算 + 生产调用点 |
| Bug1-107 | `Test_T98_ModeNoteIsSplit` | 两种模式文案互斥 + `FinishCoverage` 真的写覆盖 note |
| Bug1-108 | `Test_T98_CoverageJsonRecordsStanceAndYaw` | `cells[]` 带真实 stance 与实测 yaw 区间 |

同时把 Bug1-100（六格归类）改写成新语义：`STAND+!moving → idle`、`STAND+moving → walk`、`"IDLE"` 必须判 unknown、yaw 0°/90° 回传、84° 与 94.5° 分列窗内外、`ComposeCellId(STAND 静止, z) == "idle-z"`。

## 5. 变异证据（仓库外副本 `../_t98mut`，已删除；`diff -r` 除构建产物与 `mut.py` 外零差异）

每条：唯一替换 → 先 `dotnet build` 生产、再 build 测试 → 跑 exe → 打印变异后 DLL sha256 → 从主树 `cp` 还原并打印源码 sha256。基线 793/795，变异后均 792/795（多出 1 条自建用例红，另有 2 条既有环境失败 GBA01/NIL01）。

| 变异 | 文件:行 / 原文 → 替换文 | 变异后 DLL | 红名单 |
| --- | --- | --- | --- |
| MV-A（t96-F1 主问题） | `Bug1GateProbeLogic.cs` `if (stance == "STAND") return moving ? SpeedCellWalk : SpeedCellIdle;` → `return SpeedCellWalk;` | `cc89de9aee63561d71b469c4570aa19cc3506d3dfaa95dd6bff4d94ed52fd6a8` | Bug1-100 |
| MV-B（R17-01 / t97 MV-1） | `Bug1GateProbe.cs` `if (!…PoseWalkAllowed(paths.Count, …MaxPoseNodes)) return;` → `if (paths.Count < 0) return;` | `b3193c55fa53b8843e64405366ac6830b2fd84077e1cf310b16133d2f34fb2cb` | Bug1-104 |
| MV-C（R17-02 / t97 MV-4） | `Bug1GateProbeLogic.cs` `return newCount >= HeadingStableSamples ? currentBucket : HeadingUnstable;` → `return currentBucket;` | `ee5b37445e0db5093973b609d636b4729ac32cbf7221efe55c0e107ba1c6d178` | Bug1-103 |
| MV-D（R17-03 / t97 MV-5） | `Bug1GateProbe.cs` `bool restoreExact = …RestoreBitsEqual(localBitsBefore, localBitsAfter);` → `bool restoreExact = true;` | `c36ef7090efa40de57ba64b46b66d6d77a17991ba651a42b503a1bad6c1d7091` | Bug1-105 |
| MV-E（R17-04） | `Bug1GateProbe.cs` `if (…CountValidSamples(_cellSamples) >= maxSamples)` → `if (_cellSamples.Count >= maxSamples)` | `784986d74a6357f3df8bea48cfb789aa8c258931c09b7377038be2a3af4b5052` | Bug1-106 |
| MV-F（t96-F3） | `Bug1GateProbe.cs` `result.Notes.Add(…BuildModeNote(true, _cellSamples.Count, validSamples));` → 固定字符串 | `95cf93a69a9744c36992a1d0da40329d18fd3f87c681092c322e382d4a355243` | Bug1-107 |

还原后：`Bug1GateProbeLogic.cs` = `7a0c6d24…`、`Bug1GateProbe.cs` = `1d58f950…`（与主树逐字节一致）。t97 的三条全绿变异（子树只留首节点 `476169da`、去稳定化 `45d104fb`、还原门恒真 `214709321d`）在新字节上均已变红（分别对应 MV-B/MV-C/MV-D 的等价改法）。

## 6. 三命令

| 命令 | 结果 |
| --- | --- |
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 已成功生成，0 警告 0 错误 |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | 已成功生成，4 条既有警告 0 错误 |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `=== Final Result: 793/795 PASS (Failed: 2) ===`，Bug1 段 109/109 |

两条失败仍是既有环境项：`GBA01 Native First Observe Binding`、`NIL01 Real Native Lookup Contract`（`TypeInitializationException: SDG.Unturned.SteamPlayer` 链）。exe 有失败即返回 1，属既有约定。

## 7. 未触碰项

- Bug3 五文件哈希未变：`a89f40b949f841d0`（`P2PGroupStateRecoveryPatch.cs`）、`7a82de8e38fa0f6a`（`GroupStateRecoveryCore.cs`）、`3d5044f7870fbb17`（`GroupLeaseLedger.cs`）、`a45533850f31bd96`（`GroupLoadPlanner.cs`）、`5b0c4e9ba4c87cbe`（`P2PGroupRegistration.cs`）。
- `find Release publishi Develop-Stage -newermt "2026-11-08 03:50" -type f` 无输出（未写）。
- 未改 judge 侧 `Bug1Probe*.cs`、未改 `IlContractProbe.cs`、未动 `WhitelistTests/Program.cs` 的 T69-01 行。
- 未扩大 t94 语义：仍是六格、±5°、float32 位比较、P0/P1/P2 同 callback；F6/F7 之外的架构问题不在本票。

## 8. 自动化可证 vs 仍需 1H+2G

已自动化可证（离线）：
1. stance+isMoving → idle/walk/sprint 的映射，且 `"IDLE"` 不再被接受；站立样本必落 idle 格。
2. yaw 0°/90° ±5° 入格、窗外 unstable；`cells[]` 带真实 stance 与 yaw 区间。
3. 稳定性按采样计数；节点/深度上限；还原门位级比较；预算只数有效样本；note 按模式拆分。
4. 上述每条都有「删掉实现必红」的变异证据。

仍需实机（1H+2G）才能回答：
1. 操作者「正常玩一遍」能否真的覆盖六格（尤其 sprint 格与两个 90° 朝向桶）；`stance` 在真实移动中是否稳定为 STAND/SPRINT。
2. `PlayerMovement.isMoving` 在真实输入下的抖动程度（0.1 阈值附近的边界行为）。
3. 真实蒙皮表面在 0.10U 位移下是否每个 renderer 都满足 `max‖Δp−delta‖ ≤ epsilon`；epsilon 目前仍未指定（`MaxDeviationU=0` → `UNKNOWN/EPSILON_UNSPECIFIED`），需要决策者给阈值。
4. P2 复烘在真实渲染管线中是否与 P0 位级/`RestoreEpsilonU` 一致（离线无 Unity 渲染）。
5. 本报告**不声称**已完成实机采集，也不声称 G 门已通过。
