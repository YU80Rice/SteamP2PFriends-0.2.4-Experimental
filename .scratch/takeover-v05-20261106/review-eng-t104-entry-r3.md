# t106 工程轴复审：t104（R18-01..R18-06 收口）第 3 轮

## 0 结论

**verdict = needs_revision。**

R18-02 / R18-03 / R18-04 / R18-05 / R18-06 五条真闭合，且都有能独立复核的变红证据（见 §3/§5）。
**R18-01 只闭合了「有上限且会输出 truncated」的前半句，没有闭合「截断必须导致快照不可用」的后半句**——两个行为级变异（关掉截断分支、把 `Available=false` 改成 `true`）在 797/799 全绿。现有 Bug1-109 只钉「SnapshotPose 调用过 BuildPoseTruncatedReason ≥1 次」，调用存在但结果被丢弃/反转时它照样绿——这正是 t103 的 F-d「上限存在但越界不标记」的同一形态，换了个位置存活。

不构成对 t104 其它修复的否定：Debug 下 Bug1-53 的转绿经反向证据确认不是「放松成永真」（§5.4）。

## 1 字节绑定（本人实测；判定只对这些字节有效）

| 文件 | 行数 | 字节 | sha256（前 16） | mtime |
| --- | --- | --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1GateProbe.cs` | 1636 | 75300 | `6bf3eed7f9fefed1` | 11-08 04:52 |
| `Platform/Diagnostics/Patches/Bug1GateProbeLogic.cs` | 1416 | 69821 | `7a0c6d24de12934a` | 11-08 03:59 |
| `WhitelistTests/Evidence/StaticIL/IlContractProbe.cs` | 494 | 22708 | `73e11618f0db37ab` | 11-08 05:03 |
| `WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs` | 1108 | 68731 | `649510febc1d8ab6` | 11-08 04:51 |
| `WhitelistTests/Program.cs` | 1118 | 153379 | `5dcd7b8d93499ae6` | 11-08 04:50 |
| `SteamP2PFriends.csproj` | — | 27726 | `af991c70de6bd822` | 未改 |
| `bin/Release/SteamP2PFriends.dll` | — | 1561088 | `7aa5d4449188a13e` | 与 `WhitelistTests/bin/Release` 同哈希 |

与 t104 报告自报值逐项一致。`Bug1GateProbeLogic.cs` 停在 03:59（t98 窗口），本轮未动。

## 2 复跑（本人实测）

| # | 命令 | 退出码 | 结果 |
| --- | --- | --- | --- |
| 1 | `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 0 | 0 警告 0 错误；DLL 复现 `7aa5d444`（确定性） |
| 2 | `dotnet build WhitelistTests/... -c Release -t:Rebuild` | 0 | 0 错误；4 条既有警告（CS0649×3 + CS0219） |
| 3 | `./WhitelistTests/bin/Release/...exe` | 1 | **797/799**，FAIL 仅 `GBA01`/`NIL01`（控制台缺 Unity 运行时）；Bug1 段 **113/113**；`SPF_DLL_SHA256=7aa5d444` 与磁盘一致 |
| 4 | `dotnet build SteamP2PFriends.csproj -t:Rebuild`（Debug） | 0 | 0 错误 |
| 5 | `dotnet build WhitelistTests/... -t:Rebuild`（Debug） | 0 | 0 错误 4 警告 |
| 6 | `./WhitelistTests/bin/Debug/...exe` | 1 | **797/799**，FAIL 仅 GBA01/NIL01；Bug1-53 **PASS**；其余 Bug1 无新增失败 |

票面基线写「793/795」是 t98 时期的值；t104 加了 Bug1-109..112 后为 **797/799**，按实测记录，未掉项。

## 3 自跑变异（仓库外副本 `../_t105mut`，独立重建得 `7aa5d444`；每轮 `.bak` 还原，副本已删）

| 变异 | 改动点 | 变异 DLL | 结果 | 新红用例 |
| --- | --- | --- | --- | --- |
| **MV-1 截断静默**（R18-01） | `Bug1GateProbe.cs:1267` `if (skeletonWalk.Truncated)` → `if (Environment.TickCount == int.MinValue && skeletonWalk.Truncated)` | `054dcd83ffb07e4c` | **797/799** | **无** |
| **MV-2 截断仍可用**（R18-01） | `:1269` `snapshot.Available = false;` → `= true;` | `0a49ad59e1e85d05` | **797/799** | **无** |
| **MV-3 深度门失效**（R18-02） | `PoseDepthAllowed` → `return depth >= 0;` | `9dce2d5093027229` | 796/799 | Bug1-104（`深度 = MaxPoseDepth + 1 必须拒绝`） |
| **MV-4 总预算失效**（R18-03） | `TotalSamplesExceeded` → `return false;` | `b407a671d346f8d5` | 796/799 | Bug1-110（`达到上限必须收尾`） |
| **MV-5 覆盖走单帧 note**（R18-05） | `FinishCoverage` 的 `BuildCoverageNote(...)` → `BuildSingleNote()` | `3fdb17626a8418ea` | 796/799 | Bug1-107（`FinishCoverage 必须写覆盖模式 note，实际=0`） |
| **MV-6 探针放松为永真**（R18-04） | `IlContractProbe.cs` `pop` 分支 `return false` → `return true`（仅测试程序集） | 生产 DLL 不变 `7aa5d444` | 795/799 | Bug1-112（`删掉真实分支后必须判红`）+ Bug1-53（自检 expect 0 got 1） |
| **MV-7 删真实分支**（R18-04 反证） | `Bug1ProbeControl.cs:184-185` 的 `if (!EnablePreparedRun(...)) return Refuse(...)` → `bool enableOk = …; RoleLogger.Info(…+enableOk);`；**Release 重建**后跑 Debug 测试程序集 | `92ae78135aaafe29` | 796/799（Debug） | **Bug1-53**（`call 后 8 条内未见 brtrue/brfalse，实际=0`） |

补充：另跑了一条**行为等价**变异（`pop` 判定短路但随后落入同一 `return false`）→ 797/799 全绿，属正常，不计入判据。

## 4 R18-01 判定：**未闭合**

代码侧确实按 t103 的 requiredFix 改了：

- `Bug1PoseWalkState{Truncated,Collected}`（`Probe:1339-1343`）；`CollectSubtree` 6 参重载在深度/节点超限时 `MarkPoseTruncated` 并回传（`:1361-1370`、`:1382-1387`）。
- `SnapshotPose` 在 `skeletonWalk.Truncated` 时 `Available=false` + `UnavailableReason=BuildPoseTruncatedReason(collected, MaxPoseNodes)`（`:1267-1273`），格式 `pose_truncated:<n>/<limit>`（`:685-690`），且**先于** bone/collider 收集返回，截断集不会进入 `PosesEqual`（observed 路径被否决）。
- `PoseDepthAllowed` 纯函数（`:673-676`）被 `CollectSubtree` 调用（`:1361`）。

问题在**测试只钉了「调用存在」，没钉「判定成立」**：

1. `Bug1-109`（`Bug1GateProbeTests.cs:1013-1030`）的断言是三类：纯函数 `PoseWalkTruncated` 边界、`BuildPoseTruncatedReason` 文本、以及 `CountMethodCalls(SnapshotPose, …, "BuildPoseTruncatedReason") >= 1`。
2. 因此 **MV-1**（分支条件恒假）与 **MV-2**（`Available=false` 反转）都保持 IL 里的调用点不变 → 797/799 全绿。
3. 也就是说：任何人把 `SnapshotPose` 的 fail-closed 行为改坏（包括本次修好的那一行），CI 不会红。t103 的 F-d 判据要求的是「超限必须标不可用」，而不是「调用过一个格式化函数」。

**requiredFix（能真红的最小改法，二选一或都做）**：

- 行为缝：把 `SnapshotPose` 的截断决策抽成可注入的纯函数，例如 `internal static bool ShouldMarkPoseUnavailable(Bug1PoseWalkState state, out string reason)`，`SnapshotPose` 只调它；用例直接断言 `state.Truncated=true → false` 且 `reason=="pose_truncated:<n>/512"`。这样 MV-1/MV-2 等价变异会红（行为被断言，不再是 IL 存在性）。
- 或者给 `CollectSubtree`/`SnapshotPose` 加一个 internal 的构造入口（像 `__TestAddSmoothFrames` 那样）造一棵 >512 节点的假 `Transform` 树，走一次真实 `SnapshotPose`，断言 `Available=false`。此路需要 Unity `Transform`，成本高，建议用前一种。

**定级**：high。它正是 captain 点名的「high 味道」那条的后半句；修法上一轮已到位，但证据链不承重，回归可无声发生。

## 5 其余五条判定

### 5.1 R18-02（深度门）——闭合

`PoseDepthAllowed(int)`（`Probe:673-676`）是独立纯判定，边界用例断言 64 允许 / 65 拒绝 / 0 允许 / -1 拒绝（`Tests:892-897`），`CollectSubtree` 6 参重载调用它（`:1361`，IL 断言 `Tests:914-916`）。MV-3 只红 Bug1-104，与 requiredFix 一致。

### 5.2 R18-03（总样本硬上限）——闭合

`MaxTotalSamplesDefault = 4000`（`Probe:670`），`CheckCoverageLimits` 首段按 `_cellSamples.Count`（**含不可用样本**）判定并走 `FinishCoverage("max_total_samples")`（`:716-720`），字面量在 IL 中被断言（`Tests:1047-1048`）。MV-4 只红 Bug1-110。

内存复算（`SampleIntervalSeconds=0`、60 fps）：样本对象约 0.3–0.4 KB/条，硬上限 4000 → **约 1.2–1.6 MB 封顶**，不再随时长增长；`SampleIntervalSeconds=1.0` 时约 4.4 小时才触顶。JSON 只输出六格聚合，不随样本线性增长。**结论：长时采集不再无界**。

### 5.3 R18-05（参数级假绿）——闭合

`BuildCoverageNote(samples, valid)` / `BuildSingleNote()` 已是两个独立方法（`Probe:699-710`）；覆盖路径只调前者（`:759`），单次测量路径只调后者（`:992`）。`Bug1-107` 断言 `FinishCoverage` 的 `BuildSingleNote` 调用数 `== 0`（`Tests:975-977`），`Bug1-111` 断言 `RunGate` 至少调一次 `BuildSingleNote`。MV-5 让覆盖路径改走单帧 note → 红 Bug1-107，说明方法标识本身就是判据，参数级改写不可表达。与 t103 建议一致，未新增脆弱的实参 IL 解析。

### 5.4 R18-04（Debug ceq 形态）——闭合，且反向证据成立

- Debug 基线：797/799，Bug1-53 **PASS**（t103 时为假红）。
- `ResultFeedsConditionalBranch` 已重写为数据流状态机（`IlContractProbe.cs:357-395`）：接受 `ldloc; ldc.i4.0; ceq; stloc; ldloc; br` 与 `call; brtrue`，`pop` 仍判红。
- 反向证据：MV-7 删除 `TryStartCore` 里真实的条件分支（结果只进日志字符串）→ Debug 796/799，**Bug1-53 红**，消息 `call 后 8 条内未见 brtrue/brfalse，实际=0`。探针不是永真。
- MV-6 把 `pop` 判红改成判绿 → Bug1-112 + Bug1-53 同时红，说明合成样本与真实样本两侧都钉住了。

### 5.5 R18-06（改名与口径）——闭合（代码/报告），文档待随包更新

字段已改名 `_headingStableSamples`（`Probe:57`、`:303`、`:416`、`:418`），全仓 `_headingStableFrames` 0 命中。t104 报告 §实机说明 第 70 行写明「连续 2 次采样 = 2 × SampleIntervalSeconds（默认 ≥2 秒），不是 2 帧」。
遗留（low）：面向操作者的 `Release/0.2.4.11-preview5/测试说明.md` 没有这句话，且 preview5（11-07 23:55，DLL `7d625908`）早于本轮字节；下次打包必须带上该口径，否则实机判读仍会按「帧」理解。

## 6 假绿总表更新

| 形态 | 状态 | 证据 |
| --- | --- | --- |
| **F-d 上限存在但越界不标记** | **仍开（换位）** | 生产侧已修，但测试只钉调用存在：MV-1/MV-2 均 797/799 全绿（§4） |
| **F-e 参数级假绿** | **已闭合** | 方法拆分 + 方法标识断言；MV-5 红（§5.3） |
| F-a 调用存在但结果丢弃 | 已有守卫（R12-01/R12-02 + 反向证据），本轮无新增 | MV-7 反证 |
| 新形态（本轮） | 无 | — |

## 7 交付完整性观察（非本轮阻塞，low）

`git status` 显示 `Platform/Diagnostics/Patches/Bug1GateProbe.cs`、`Bug1GateProbeLogic.cs`、`WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs` 仍是 `??`（未入库），而 csproj 已把它们列入 Compile。与 Bug3 E6、R7-02 同类：干净检出编译不过、他机无法复现 t104 的 797/799。建议随修复轮 `git add`，或在交付说明里注明「只从工作区打包并记录哈希」。

## 8 pass 边界与 UNKNOWN

- 本票只判**离线证据链是否自洽**。797/799 不等于 G 门成立，也不等于 Bug1 已修复。
- 仍需 1H+2G 才能回答：BakeMesh 真实位移量级、六格能被「正常玩一遍」覆盖到什么比例、`root`/`Third`/骨骼在真实物理帧是否保持静止、`2 × SampleIntervalSeconds` 的稳定性窗口在真实转向下是否合适。
- 未验证：Mono 运行时下 `Transform` 子树的真实节点数量级（512 上限是否会在大体型自定义模型上触发截断）。修好 R18-01 的行为断言后，可用真实 trace 复核截断率。

## 9 复现命令

```bash
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe     # 797/799，仅 GBA01/NIL01
# MV-1（截断静默）：改 Bug1GateProbe.cs:1267 后重建 Release，复跑 → 仍 797/799
# MV-7（删真实分支）：改 Bug1ProbeControl.cs:184-185 → 重建 Release → 重建 Debug 测试 → 跑 Debug exe
#                    → 796/799，Bug1-53 红
```

## 10 findings

- **R19-01（high）** `WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs:1013-1030` 与 `Platform/Diagnostics/Patches/Bug1GateProbe.cs:1267-1273`：R18-01 的 fail-closed 行为无任何可失败证据——MV-1（分支恒假）与 MV-2（`Available=false`→`true`）均 797/799 全绿；Bug1-109 只断言纯函数边界、原因文本格式与「SnapshotPose 调过 `BuildPoseTruncatedReason` ≥1 次」。**requiredFix**：抽 `ShouldMarkPoseUnavailable(Bug1PoseWalkState, out string reason)` 之类的纯决策函数，`SnapshotPose` 只调它，用例断言 `Truncated=true → false + pose_truncated:<n>/512`；保留现有 IL 断言作二层防护。
- **R19-02（low）** `Release/0.2.4.11-preview5/测试说明.md` 未写明「连续 2 次 = 2 × SampleIntervalSeconds（默认 ≥2 秒）」，且该包 `7d625908` 早于本轮字节。**requiredFix**：下一次打包时把口径写进实机说明，并在 manifest 记录 `7aa5d444`（或更新后的哈希）。
- **R19-03（low）** `Bug1GateProbeTests.cs:1051-1066`（Bug1-111）名为「Notes Cannot Mix」，实际只断言 `RunGate` 调用 `BuildSingleNote`；覆盖侧的互斥由 Bug1-107 承担。建议改名或补一条 `FinishCoverage` 侧的对称断言，避免读者误以为 111 覆盖了覆盖模式。
- **R19-04（low）** 三个新增生产/测试文件仍 `??` 未入库而 csproj 已引用（见 §7）。**requiredFix**：`git add` 或在交付说明声明打包来源与哈希。

<!-- self_sha256(body)=e1c21f30eb0f4b136e2ec6d159a5fe6fb0b81b49fe7d9f60eb963abc9210db97 -->


