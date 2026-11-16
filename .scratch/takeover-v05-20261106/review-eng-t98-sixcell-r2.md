# t103 工程轴复审（r2）：t98 G 门六格姿态覆盖修复轮

被审：t98（`t97` R17-01..R17-04 的修复轮）。判定只对本节字节有效。

结论：**needs_revision**。四条 R17 中 R17-02/R17-03/R17-04 的主体已真闭合（自跑变异均只红对应用例）；**R17-01 只闭合了「有上限」这半句，没闭合「超限必须标不可用」那半句**，且深度上限零覆盖。另发现两处与本轮改动相关/相邻的空白（预算无硬上限、参数级假绿）。

## 1 被审字节（本机实测，与 Captain 表一致）

| 文件 | 行 | 字节 | sha256 |
| --- | --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1GateProbe.cs` | 1554 | 71650 | `1d58f950600c3d54d2b3c69021a6f747a97e946dba27b7797d0fa487ae1e6651` |
| `Platform/Diagnostics/Patches/Bug1GateProbeLogic.cs` | 1416 | 69821 | `7a0c6d24de12934a5af92bc9e1bdffc89cf7595a8c7bba0eb16d9f2f67937c8d` |
| `WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs` | 998 | 61152 | `3e5bae4e3f6efb8c3448e8c44f5bb87ff9b2d8874f18bbe2efec5eaf88b69561` |
| `WhitelistTests/Program.cs` | 1114 | 152611 | `1d6d979dfd15678bec3247a0502bb1f44f188168321c960d70453616d3f0e460` |
| `SteamP2PFriends.csproj` | — | 27726 | `af991c70de6bd822de0fd50d4a7f74496c52f2156e7377dacdd2a294f16d912d` |
| `bin/Release/SteamP2PFriends.dll` | — | 1560064 | `ad1283ac0ea513c414c175caf91d8039da7bcfac43d5e9992424ea81c5766a2a` |

`WhitelistTests/bin/Release/SteamP2PFriends.dll` 与主树同哈希。交付方 inScope 解析只含两个 `.cs`，实际改动含 `Bug1GateProbeTests.cs` 与 `Program.cs`（+6 行注册，T69-01 未动）——按实际文件审。

## 2 复跑（本人 Release 实测）

| 命令 | 退出码 | 结果 |
| --- | --- | --- |
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 0 | 0 警告 0 错误；DLL 复现 `ad1283ac` |
| `dotnet build WhitelistTests/... -c Release -t:Rebuild` | 0 | 0 错误 4 警告（既有 CS0649×3 + CS0219） |
| `./WhitelistTests/bin/Release/...exe` | 1 | **793/795**，FAIL 仅 `GBA01`、`NIL01`（控制台缺 Unity）；Bug1 段 109/109 |

## 3 自跑变异（仓库外副本 `../_t103mut`，独立重建得 `ad1283ac`，逐字节还原）

| 变异 | 改动点 | 变异后 DLL | 结果 | 新红用例 |
| --- | --- | --- | --- | --- |
| MV-A STAND 恒 walk | `Logic:707` `... ? SpeedCellWalk : SpeedCellIdle` → `SpeedCellWalk` | `cc89de9aee63561d` | 792/795 | Bug1-100 |
| MV-B1 上限失效（语义） | `Logic:768` `>= 0 && < cap` → `>= 0 \|\| < cap` | `d2ddcd74093e6ccb` | 792/795 | Bug1-104 |
| MV-B2 **只删深度门** | `Probe:1294` `depth > MaxPoseDepth` → `depth > int.MaxValue` | `c06735967a2f37fb` | **793/795 全绿** | 无 |
| MV-C 去稳定化 | `Logic:745` → `return currentBucket;` | `ee5b37445e0db509` | 792/795 | Bug1-103 |
| MV-D 还原门恒真 | `Logic:781` `return false;` → `continue;` | `8dcaf848a02c4b08` | 792/795 | Bug1-105 |
| MV-E 预算数全量 | `Logic:756` 去掉 `&& sample.Available` | `3c960c0657358687` | 792/795 | Bug1-106 |
| MV-F 删覆盖 note | `Probe:710` 删 `Notes.Add(BuildModeNote(...))` | `bc2a4292e1600cf0` | 792/795 | Bug1-107 |
| MV-Dparam **mode 参数 true→false** | `Probe:710` `BuildModeNote(true, …)` → `(false, …)` | `49d8c2ec87086445` | **793/795 全绿** | 无 |

每条还原后源码回 `1d58f950`/`7a0c6d24`、重建 DLL 回 `ad1283ac`；副本已删，主树哈希与开工一致（只读）。

## 4 逐条判定

### R17-01 有界遍历 —— 主体成立，**未闭合**
成立部分：`CollectSubtree` 加了深度重载（`Probe:1290-1305`），入口 `depth > MaxPoseDepth`、`PoseWalkAllowed(paths.Count, MaxPoseNodes)`（`Logic:765-769`，512 为默认上限）；`H1`（MV-B1 语义失效）确实红 Bug1-104——说明「上限被改成恒真」能被抓。

未闭合两点：
- **截断是静默的、且不标不可用**：命中 512/64 时 `CollectSubtree` 只 `return`，`SnapshotPose` 不置 `Available=false`、不写 `UnavailableReason`；随后 `BuildPoseNodePlan` 只拿到已收集路径，`PosesEqual` 在**子集**上比较，`EvaluateCoverage` 的 `observed = PoseEqual && InvarianceOk` 仍为真 → 截断集可以贡献正证据。这正是 `Logic:136` 注释「超限即 fail-closed」与 `Logic:761-764` 注释「超限 → false，调用方必须把快照标成不可用」**没有兑现**的形态；全仓 grep `Truncat|truncat` 0 命中，无任何标志字段。
- **深度门零覆盖**：MV-B2 把深度门改成恒不触发，793/795 全绿；Bug1-104 只断言 `PoseWalkAllowed` 的节点语义 + `CollectSubtree` 调用它 ≥1 次，不覆盖 `depth > MaxPoseDepth`。

生产者侧：`OnPreCull`（`Probe:328`）先置 `_ranOnce`，`SampleOnce` 每帧按 `SampleIntervalSeconds`（默认 1.0s）节流（`Probe:376-381`）。

### R17-02 采样级稳定化 —— **闭合**
`HeadingStableSamples = 2`（`Logic:125`），`AdvanceHeadingStability`（`Logic:727-746`）把「换桶/unstable 清零」与「连续 2 次才接受」写在纯函数里，生产 `Probe:415-418` 只用它；`HeadingStableFrames` 常量已全仓消失。Bug1-103 覆盖四态，MV-C 只红它。
遗留（low）：字段仍叫 `_headingStableFrames`（`Probe:416`），语义已变但名字没跟上；另因节流单位是采样，默认配置下「连续 2 次」= **≥2 秒同桶**（不是 2 帧），口径变化需在报告/实机说明写明。

### R17-03 位级还原门 + P2 复烘 —— **闭合**
`RestoreBitsEqual`（`Logic:775-784`）逐 float32 位比较、null/长度不等即 false；`Probe:567` 调用它，`Probe:568-588` 在 `Restore(...)`（finally `:553-556`）之后重新 `BakeSample` 并与基线比较（`RestoreEpsilonU = 1e-4`），`surfaceRestored` 并入 `InvarianceOk`（`:596`）。MV-D 只红 Bug1-105；报告 §3 与代码一致（不再是「只改口径」）。

### R17-04 预算只数有效样本 —— **主体闭合，但引入无界面**
`CountValidSamples`（`Logic:749-759`）只数 `Available`，`CheckCoverageLimits`（`Probe:690`）用它判 `maxSamples`；MV-E 只红 Bug1-106。
副作用（本轮新发现，见 R18-03）：预算不再约束 `_cellSamples.Count` 本身，未定样本可无限追加。

## 5 长时采集的有界性（本人估算）

- 有效样本：六格 × `MaxSamples`(240) = 1440 条；每条 `Bug1GateCellSample` 含 4 个短串 + `Detail`（`PoseDiff` 只返回**首个**差异，`Logic:588-618`，实测形态 ≤150 字符）→ 约 0.3–0.4 KB/条 → **有效样本上界约 0.5 MB**，可接受。
- 未定样本：`CheckCoverageLimits` 只数 `CountValidSamples`（`Probe:690`），`_cellSamples.Add`（`:423`/`:441`）无条件追加 → **无任何硬上限**。默认 1 样本/秒 → 约 1.2 MB/h；`SampleIntervalSeconds` 配 0（`Probe:379` 允许 0）时每帧一次，60 fps → **约 70–85 MB/h、约 1.7 GB/天**，长局会 OOM。
- JSON/GATE.json：`BuildCoverageJson` 只输出六格聚合（counts/stances/yaw 区间/detail），**不随样本数线性增长**；风险在内存，不在产物。

## 6 假绿形态总表（含本轮新增）

| # | 形态 | 本轮实例 | 是否被现有用例抓到 |
| --- | --- | --- | --- |
| F-a | 调用存在但结果被丢弃 | — | 本轮未发现（`restoreExact`/`surfaceRestored`/`heading`/`validSamples` 均被消费） |
| F-b | 断言用 `>=1` 聚合 | Bug1-104 的 IL 计数、Bug1-105/106/107 的调用计数 | 部分：能抓「调用消失」，抓不到「实参/语义被改」 |
| F-c | 快照只采不比 | — | 未发现（`SnapshotPose` 有 `PosesEqual` 配对） |
| **F-d** | **上限存在但越界不标记**（新） | `CollectSubtree` 命中 512/64 只 `return`，`Available` 仍真（`Probe:1294-1295`） | **否**，无用例 |
| **F-e** | **参数级假绿**（新，与 T93-F1 同族） | `BuildModeNote(true, …)` 改成 `false` → 793/795 全绿（MV-Dparam） | **否**，IL 不读实参 |

## 7 Debug 下 Bug1-53 失败的归因（与 t98 无关）

本人 Debug 复跑：`dotnet build … -c Debug -t:Rebuild` 两工程 0 错 → Debug exe **792/795**，除 GBA01/NIL01 外仅 `Bug1-53 EnablePreparedRun Result Is Consumed (R12-01)` 红，消息为「R12-01：探针自检失败——返回值参与判定的样本必须数出 1」。

根因（IL 字节实证）：`ProbeConsumeResult` 的 Debug 代码生成是 **`call; ldc.i4.0; ceq; stloc.0; ldloc.0; brfalse.s`**（实测字节 `17 28 … 16 FE 01 0A 06 2C 04 …`），Release 是 **`call; brtrue.s`**（`17 28 … 2D 02 …`）。`IlContractProbe.ResultFeedsConditionalBranch`（`IlContractProbe.cs:360-388`）在 call 后首个非 nop 只接受「条件分支」或「stloc」，遇到 `ldc.i4.0` 直接判未采纳 → 自检 `== 1` 失败。

归因证据：`Bug1GateProbe.cs` mtime 11-08 03:58（t98 窗口），`IlContractProbe.cs` 11-07 08:25、`Bug1ProbeEntryRepairTests.cs` 11-07 08:34（均早于 t98），且 t98 changedPaths 不含这两个文件 → **Debug-only 假红，既有探针的形态假设不足，不属 t98 回归，也不属 t98 blocker**。Release（交付路径）793/795 稳定。

## 8 「参数级断言能否低成本让 MV-D 必红」

- **纯 IL 计数做不到**：`IlContractProbe` 只按 `call` 计数（`CountMethodCalls`/`CountCallsWithBranchOnResult`），没有读「call 前 `ldc.i4.0/1`」的 API；要钉实参得扩探针解析前一条 `ldc.i4` 操作数——可行但等于给 IL 层再加一类脆弱判据。
- **低成本且真红的替代（推荐）**：把模式从布尔参数里拿掉——拆成两个方法 `BuildCoverageNote(samples, valid)` 与 `BuildSingleNote()`，`FinishCoverage` 调前者、单帧路径调后者。此时「传错模式」在语法上不可表达，IL 断言 `FinishCoverage` 调 `BuildCoverageNote ≥1` 即是参数级证据；纯函数用例再断言 `BuildCoverageNote` 文本不含 `single_frame_single_pose`。改动量约 15 行 + 1 条用例，能在 MV-Dparam 的同位置直接变红。
- 次选：`FinishCoverage` 把 note 写入 `result.Notes`（已如此），再让生产路径用例读 GATE.json 的 `notes` 断言覆盖口径——但当前 `FinishCoverage` 依赖 Unity 目标，离线起不来，成本高于上一条。

## 9 边界与只读

- Bug3 五文件仍 `7a82de8e/a89f40b9/3d5044f7/a4553385/5b0c4e9b`；`SteamP2PFriends.csproj` 只增 6 行 Compile（`af991c70`）；`Release/`、`publishi/`、`Development/` 无本轮写入。
- 审查期间只在仓库外副本 `../_t103mut` 改字节（`.bak` 备份、逐字节还原、副本已删）；主树四文件 + DLL 哈希与开工一致（`1d58f950`/`7a0c6d24`/`3e5bae4e`/`1d6d979d`/`ad1283ac`）。

## 10 pass 边界

本判定为 needs_revision：R17-01 的「超限标不可用」与深度门覆盖未达成，R17-04 的预算改动带出无界列表。即使修完，也只能说「离线六格证据链自洽」——BakeMesh 真实位移、六格能否被正常游玩覆盖、root/Third/骨骼在真实物理帧是否静止，仍需 1H+2G 实机；本票不构成 Bug1 已修复的证据。

## 11 复现

```bash
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe    # 793/795
# 变异：仓库外副本，cp .bak 还原，重建后核 DLL ad1283ac
python D:/tmp/mut103.py MB2 && dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild \
  && cp bin/Release/SteamP2PFriends.dll WhitelistTests/bin/Release/ \
  && ./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe   # 793/795 全绿
```

## 12 findings

- **R18-01（medium）** `Platform/Diagnostics/Patches/Bug1GateProbe.cs:1294-1295`：命中 `MaxPoseNodes`/`MaxPoseDepth` 时静默 `return`，`SnapshotPose` 不标 `Available=false`、不写 `UnavailableReason`；与 `Bug1GateProbeLogic.cs:136`「超限即 fail-closed」及 `:761-764`「调用方必须把快照标成不可用」矛盾。截断集仍参与 `PosesEqual`，`observed = PoseEqual && InvarianceOk` 可据子集给出正证据。**requiredFix**：`CollectSubtree` 返回/输出 truncated 标志，`SnapshotPose` 置 `Available=false` 且 `UnavailableReason="pose_truncated:<collected>/<limit>"`；补一条纯函数用例（如 `PoseWalkTruncated`）并保留现有 IL 断言。
- **R18-02（medium）** `Bug1GateProbe.cs:1294`：深度上限零覆盖——MV-B2（`depth > int.MaxValue`）后 793/795 全绿；Bug1-104 只断言节点语义与「调用了 `PoseWalkAllowed`」。**requiredFix**：把深度判定抽成 `PoseDepthAllowed(int depth)` 并在 `CollectSubtree` 调用，用例断言边界（64 允许、65 拒绝）+ IL 计数；或由 R18-01 的 truncation 标志统一覆盖。
- **R18-03（medium）** `Bug1GateProbe.cs:690` + `:423`/`:441`：`MaxSamples` 改为只数有效样本后，`_cellSamples` 本身无硬上限；未定样本（频繁转向/越界姿态）可无限追加，`SampleIntervalSeconds=0` 时约 70–85 MB/h。**requiredFix**：另设 `MaxTotalSamples`（建议 4000）按 `_cellSamples.Count` 判定并 `FinishCoverage("max_total_samples")`，附一条离线用例。
- **R18-04（low）** `WhitelistTests/Evidence/StaticIL/IlContractProbe.cs:360-388`：`ResultFeedsConditionalBranch` 不识别 Debug 的 `ceq` 否定形态，导致 Debug 下 Bug1-53 自检假红（Release 绿）。**pre-existing，不属 t98**；**requiredFix**：接受 `ceq; stloc L; ldloc L; br` 形态，或把自检限定在 Release（`#if !DEBUG` 不推荐，宁可扩探针）。
- **R18-05（low）** `Bug1GateProbe.cs:710`：`Bug1-107` 的 IL 不钉实参，`BuildModeNote(true→false)` 全绿（MV-Dparam `49d8c2ec`）。**requiredFix**：拆 `BuildCoverageNote(...)`/`BuildSingleNote()` 两个方法（详见 §8），使错误模式不可表达。
- **R18-06（low）** `Bug1GateProbe.cs:416`：字段仍名 `_headingStableFrames`；且稳定化单位是采样，默认 1.0s/次 → 「连续 2 次」实为 ≥2 秒。**requiredFix**：字段改名 `_headingStableSamples`，并在实机说明写明 2×`SampleIntervalSeconds` 的口径。
<!-- self_sha256(body)=484fda381a5793a7eaf8369beb1ac5042c5f0321f03487ae74dfed5ccb8928c1 -->
