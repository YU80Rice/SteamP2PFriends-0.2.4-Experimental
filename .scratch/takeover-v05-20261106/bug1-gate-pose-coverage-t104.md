# T104 G 门姿态覆盖 repair-round-3（R18-01..R18-06）

**结论口径（先写清楚）**：本轮只让「离线证据链自洽」——姿态截断 fail-closed、深度上限、总样本硬上限、模式 note 不可混写、IL 探针不再在 Debug 下假红。
**这不构成 G 门成立，也不构成 Bug1 已修复**；真实蒙皮位移/六格覆盖仍需 1H+2G 实机采集。

## 1 改前基线（t98 终态）

| 文件 | 行数 | sha256 |
| --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1GateProbe.cs` | 1554 | `1d58f950600c3d54d2b3c69021a6f747a97e946dba27b7797d0fa487ae1e6651` |
| `Platform/Diagnostics/Patches/Bug1GateProbeLogic.cs` | 1416 | `7a0c6d24de12934a5af92bc9e1bdffc89cf7595a8c7bba0eb16d9f2f67937c8d` |
| `WhitelistTests/Evidence/StaticIL/IlContractProbe.cs` | 505 | `46e5c94bdb0f275fe04e0408f6d11ba196908776e1676672ce87b91e6100f9fe` |
| `WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs` | 998 | `3e5bae4e3f6efb8c3448e8c44f5bb87ff9b2d8874f18bbe2efec5eaf88b69561` |
| `WhitelistTests/Program.cs` | 1114 | `1d6d979dfd15678bec3247a0502bb1f44f188168321c960d70453616d3f0e460` |
| `bin/Release/SteamP2PFriends.dll` | — | `ad1283ac0ea513c414c175caf91d8039da7bcfac43d5e9992424ea81c5766a2a` |

改前三命令：生产 0 警 0 错、测试 4 条既有警告 0 错、`Final Result: 793/795 PASS`（仅 GBA01/NIL01），Bug1 段 109/109。

## 2 逐条修复

### R18-01 超限必须标不可用（core）

`CollectSubtree` 命中 `MaxPoseNodes`/`MaxPoseDepth` 时原来静默 `return`，截断集仍参与 `PosesEqual`，可据子集给出正证据。

- 新增 `Bug1PoseWalkState { bool Truncated; int Collected; }`；`CollectSubtree` 改 6 参（`node, root, paths, map, depth, state`），两处超限都走 `MarkPoseTruncated(paths, state)`（置 `Truncated=true`、`Collected=paths.Count`）。
- `SnapshotPose`：遍历后 `if (skeletonWalk.Truncated) { snapshot.Available=false; snapshot.UnavailableReason = BuildPoseTruncatedReason(...); return snapshot; }` → 截断快照一律不可用，`PosesEqual` 判不等，`observed` 不可能成立。
- `Bug1GateProbe.cs:685` `BuildPoseTruncatedReason(collected, maxNodes)` → `"pose_truncated:<collected>/<limit>"`。
- 用例 `Bug1-109 Test_T104_PoseTruncationIsFailClosed`：`PoseWalkTruncated` 边界 + `BuildPoseTruncatedReason(512, MaxPoseNodes) == "pose_truncated:512/512"` + `SnapshotPose→BuildPoseTruncatedReason` IL ≥1。

### R18-02 深度上限零覆盖

- `Bug1GateProbe.cs` 新增纯函数 `PoseDepthAllowed(int depth) => depth >= 0 && depth <= Bug1GateProbeLogic.MaxPoseDepth;`（:1361 在 `CollectSubtree` 调用）。
- `Bug1-104 Test_T98_PoseWalkIsBounded` 扩充：`PoseDepthAllowed(64)` 真、`(65)` 假、`(0)` 真、`(-1)` 假；并断言 `CollectSubtree`（6 参重载）→`PoseDepthAllowed` IL ≥1，保留原有 `PoseWalkAllowed` IL ≥1。
- 变异 MV-B2 已复跑并变红（见 §4）。

### R18-03 总样本硬上限

- `Bug1GateProbe.cs:670` `internal const int MaxTotalSamplesDefault = 4000;`；`:695` `TotalSamplesExceeded(int total) => total >= MaxTotalSamplesDefault;`。
- `CheckCoverageLimits` 开头（:716）：`if (TotalSamplesExceeded(_cellSamples.Count)) { FinishCoverage("max_total_samples"); return; }`，先于 `EvaluateCoverage`，覆盖「未定样本无限追加」。
- 用例 `Bug1-110 Test_T104_TotalSampleBudget`：`TotalSamplesExceeded(3999)==false`、`(4000)==true`，`CheckCoverageLimits→TotalSamplesExceeded` IL ≥1，`ContainsStringLiteral(limits,"max_total_samples")`。

### R18-04 Debug 否定形态假红（pre-existing，顺手清）

原 `ResultFeedsConditionalBranch` 只认 `pop` / 直接条件分支 / `stloc+ldloc+br`。Debug 实测形态为：

```
call EnablePreparedRun
ldc.i4.0
ceq
stloc.s 20
ldloc.s 20
brfalse.s
```

- 重写为数据流状态机（`IlContractProbe.cs:356`）：`pending` 表示「栈顶是调用结果派生值」，`tainted` 记录结果落过的局部；`ldc.i4.*`/比较指令/`dup` 不改变 pending；`stloc L` 置 false 并登记 L；`ldloc L` 置 `tainted.Contains(L)`；`pop` 直接判红；条件分支返回 `pending`。
- 三种形态（`call; brtrue`、`call; ldc; ceq; stloc; ldloc; brfalse`、`call; stloc; ldloc; brtrue`）都判真；`call; pop` 仍判红。
- **反向证据**：Debug 基线 `Bug1-53 PASS`；在副本里删掉真实分支（保留调用、结果丢弃、改为 `if (enableError != null) Warn(...)`）→ `Bug1-53 FAIL`（795/799）。证明探针没有被放松成永真。
- 未使用 `#if !DEBUG`。

### R18-05 模式 note 不可混写

- 删除 `BuildModeNote(bool coverageMode, …)`，拆成两个方法：`BuildCoverageNote(int samples,int validSamples)`（固定覆盖口径文案 + 采样/有效数）与 `BuildSingleNote()`（固定单帧口径文案）。
- `FinishCoverage` 只调 `BuildCoverageNote`；`RunGate`/`Fail` 路径只调 `BuildSingleNote` → 「把覆盖写成单帧」在类型层面不可表达。
- 用例 `Bug1-107 Test_T98_ModeNoteIsSplit`：覆盖文本含「六格采样」不含 `single_frame_single_pose`；单帧文本含 `single_frame_single_pose`；`FinishCoverage→BuildCoverageNote` ≥1 且 `FinishCoverage→BuildSingleNote` ==0。
- 另有 `Bug1-111 Test_T104_ModeNotesCannotMix` 复核两段文本互斥与 `RunGate→BuildSingleNote` IL ≥1。

### R18-06 字段改名与实机单位说明

- `_headingStableFrames` → `_headingStableSamples`（字段 :57、清零 :303、读取 :416、写回 :418）。
- **实机说明**：稳定性按「连续 2 次采样」计，即 `2 × SampleIntervalSeconds`（默认 1.0s → ≥2 秒），不是 2 帧；转向抖动在 1 秒粒度上就会被清零。

## 3 测试与用例

新增/改写用例（`WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs`，998 → 1108 行）：

| 编号 | 用例 | 承重点 |
| --- | --- | --- |
| Bug1-104 | `Test_T98_PoseWalkIsBounded` | 节点上限 + **深度上限 64/65** + 两条 IL |
| Bug1-107 | `Test_T98_ModeNoteIsSplit` | note 拆分 + `FinishCoverage→BuildSingleNote == 0` |
| Bug1-109 | `Test_T104_PoseTruncationIsFailClosed` | 截断 → `pose_truncated:<n>/<limit>` 不可用 |
| Bug1-110 | `Test_T104_TotalSampleBudget` | 4000 硬上限 + 收尾原因 |
| Bug1-111 | `Test_T104_ModeNotesCannotMix` | 覆盖/单帧文本互斥 |
| Bug1-112 | `Test_T104_IlProbeAcceptsDebugNegation` | 手工 Debug IL 正/反例（`Pop` 必须假） |

`WhitelistTests/Program.cs`（1114 → 1118 行）在 Bug1-108 后追加 Bug1-109..112；`T69-01` 注册行未动。

## 4 变异证据（仓库外副本 `../_t104mut`，已删除）

流程：唯一替换 → 先 build 生产 → build 测试 → 跑 exe → 记录 DLL sha256 与红名单 → 从主树逐字节还原并核哈希。每条只红一条用例（796/799，另两条为既有 GBA01/NIL01）。

| 变异 | 文件:行 | 原文 → 替换文 | 变异后 DLL sha256 | 红名单 |
| --- | --- | --- | --- | --- |
| M1（R18-01） | `Bug1GateProbe.cs:1270` | `snapshot.UnavailableReason = BuildPoseTruncatedReason(skeletonWalk.Collected, …);` → `= "pose_truncated";` | `171dae257640085c09bfa5ee7c755d4a5f6fad78e992decc4f271d59d309057d` | Bug1-109 |
| M2（R18-02，即 MV-B2） | `Bug1GateProbe.cs:1361` | `if (!PoseDepthAllowed(depth))` → `if (!(depth < Bug1GateProbeLogic.MaxPoseDepth + 1))` | `252cb4574c4a41a0acac092a4776fcecee4ca2d02569321295223e1d678dfac0` | Bug1-104 |
| M3（R18-03） | `Bug1GateProbe.cs:716` | `if (TotalSamplesExceeded(_cellSamples.Count))` → `if (_cellSamples.Count > MaxTotalSamplesDefault)` | `c8000717d05cfee167457ecf3558784b74b70598ccdac3d7a487926e2f2c4d4c` | Bug1-110 |
| M4（R18-05，即 MV-Dparam） | `Bug1GateProbe.cs:759` | `result.Notes.Add(BuildCoverageNote(_cellSamples.Count, validSamples));` → `result.Notes.Add(BuildSingleNote());` | `3fdb17626a8418ea58380a987dace4f5ebab14e399a76c02f7221a4e503913be` | Bug1-107 |
| M5（R18-04） | `IlContractProbe.cs:383` | `if (IsInt32Constant(current)) continue;` → `if (IsInt32Constant(current)) return false;` | `7aa5d4449188a13ec088cb5d56a819d0744b639b87b8395e0c824511c7b2d79e`（只改测试程序集，生产 DLL 不变） | Bug1-112 |

还原核对：`Bug1GateProbe.cs` 回到 `6bf3eed7…`，`IlContractProbe.cs` 回到 `73e11618…`；副本删除前 `diff -r`（排除构建产物）与主树零差异。

## 5 Debug 探针扩展与反向证据

副本内用 `-c Debug` 构建生产、把 `bin/Debug/SteamP2PFriends.dll` 覆盖到 `bin/Release`（仅副本），再重建测试运行：

| 场景 | Debug 生产 DLL | Bug1-53 | 总计 |
| --- | --- | --- | --- |
| Debug 基线（修复后探针） | `e29b1e463b2dac163823ddb864c69f662878a39b20ff7b58395de3ea687d18cd` | **PASS** | 796/799 |
| Debug + 删掉真实分支（保留调用、结果丢弃） | `6bde001362b73f747fb7cd4877ba632dbcd26ee3535b0ef825cc46c20231b166` | **FAIL** | 795/799 |

Debug 基线的第三条失败是既有的 `[StaticIL] T07 AnimalStructureOwnershipStaticIL`（与本轮无关，Release 下通过）；其余两条为 GBA01/NIL01。

## 6 三命令（主树终态）

```
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
  → 0 个警告 / 0 个错误
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
  → 4 个警告 / 0 个错误（既有）
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe
  → === Final Result: 797/799 PASS (Failed: 2) ===
  → FAIL: [PureMemory] GBA01 Native First Observe Binding
          [PureMemory] NIL01 Real Native Lookup Contract
  → Bug1 段 113/113 PASS
```

## 7 逐文件哈希（改后）

| 文件 | 行数 | sha256 |
| --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1GateProbe.cs` | 1636 | `6bf3eed7f9fefed116befcd0bbe13398b13a5ca685c3d39f4d8a1abc89f79ce5` |
| `Platform/Diagnostics/Patches/Bug1GateProbeLogic.cs` | 1416（未改） | `7a0c6d24de12934a5af92bc9e1bdffc89cf7595a8c7bba0eb16d9f2f67937c8d` |
| `WhitelistTests/Evidence/StaticIL/IlContractProbe.cs` | 494 | `73e11618f0db37ab7d21846457e548bfc12c0480067f6268de5d2032969a8e29` |
| `WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs` | 1108 | `649510febc1d8ab697362ac11fa6d62a46e1ba5b7cdf9555000447a0eb371b7b` |
| `WhitelistTests/Program.cs` | 1118 | `5dcd7b8d93499ae6ebdd160617fe41adb30a48f168ae8bb0b77b04324e785201` |
| `bin/Release/SteamP2PFriends.dll` | — | `7aa5d4449188a13ec088cb5d56a819d0744b639b87b8395e0c824511c7b2d79e` |

## 8 边界与未触碰

- Bug3 五文件哈希未变：`a89f40b949f841d0` / `7a82de8e38fa0f6a` / `3d5044f7870fbb17` / `a45533850f31bd96` / `5b0c4e9ba4c87cbe`。
- `Release/`、`publishi/`、`Develop-Stage/` 无本轮写入（`find … -newermt "2026-11-08 04:40"` 无输出）。
- 未改判定器 `Bug1Probe*.cs`、未改 `Bug1GateProbeLogic.cs` 的六格/位比较口径、未动 `T69-01`。
- 变异全部在仓库外副本进行，主树未参与变异；副本已 `rm -rf`。

## 9 仍需实机（不得由本报告推出）

1. `SnapshotPose` 在真实骨骼树上的节点数与深度是否会触发 512/64 上限（触发即 UNKNOWN，属有效结论）。
2. 六格是否能在「正常玩一遍」中被覆盖，`2 × SampleIntervalSeconds` 的稳定性窗口在真实转向下是否合适。
3. 每被画 renderer 的 `max‖Δp−delta‖` 与 epsilon 是否成立——epsilon 未指定时一律 `UNKNOWN/EPSILON_UNSPECIFIED`。
4. `BakeMesh` 在 Mono 运行时对目标骨骼的实际行为（离线只验证了接口与采样索引）。

