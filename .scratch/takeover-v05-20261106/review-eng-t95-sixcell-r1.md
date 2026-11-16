# 工程轴复审：t95（G 门六格姿态覆盖）— 第 1 轮

- 复审对象：t95 交付的字节（`Bug1GateProbe.cs` / `Bug1GateProbeLogic.cs` / `Bug1GateProbeTests.cs` / `Program.cs`）
- 复审人：reviewer-eng（工程轴；只判正确性、边界、回归风险、可测性，不判需求）
- 复审日期：2026-11-08
- 结论：**needs_revision**（4 条 medium/low；无 blocker，但两条 acceptance 未满足）

## 0. 结论摘要

| 维度 | 判定 | 依据 |
| --- | --- | --- |
| float32 位级比较（local 10 + world 16） | **通过** | `ReadPoseEntry` 逐字段填 10/16；`PosesEqual`→`FloatArrayBitEqual(count)`→`BitEqual` 位模式比较；MV-3 改 1e-4 近似 → 红 Bug1-87/99 |
| P0/P1/P2 同 callback + 还原门 | **部分不通过** | 六格路径 `MeasureCellAxis` 只有 P0/P1 复烘，**没有 P2 复烘**（报告 §3 第 4 步与代码不符）；MV-5 让 `restoreExact` 恒真 → 全绿 |
| pose 子树遍历有界 | **不通过** | `CollectSubtree` 无节点/深度上限；`AddColliderChain` 有 64 守卫；MV-1（子树只留首节点）→ 787/789 **全绿** |
| 覆盖校验器与采集端同源、可复算 | **通过** | `EvaluateCoverage` 直接吃 `_cellSamples` 同一批对象；`BuildCoverageJson` 逐字段读 `report.Cells`；同输入复算一致（Bug1-101） |
| 六格归类/连续帧稳定化 | **部分不通过** | 稳定化真实存在于代码（`HeadingStableFrames=2`），但**零测试**：MV-4 删除稳定化 → 全绿；注释称「帧」实为「采样」 |
| 无生产行为改动、默认关闭、未改 judge/Bug3 | **通过** | `Enable` 默认 false；`Bug1ProbeJudge.cs` 仍 `9eeee011`；Bug3 五文件哈希未变 |
| 自动化可证 vs 1H+2G | **通过** | 报告 §8 明确列出「仍需实机」清单，未把离线绿灯当端到端结论 |

## 1. 被审字节锁定（本人实测）

| 文件 | 行数 | 字节 | sha256（前 16） | mtime |
| --- | --- | --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1GateProbe.cs` | 1497 | 68077 | `33c65d22e98fef96` | 03:10 |
| `Platform/Diagnostics/Patches/Bug1GateProbeLogic.cs` | 1275 | 62550 | `549297721f284fe2` | 03:09 |
| `WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs` | 842 | 50986 | `6896fdf5101b…` | 03:16 |
| `WhitelistTests/Program.cs` | 1108 | — | `78f2b6b165c6…` | 03:15 |
| `SteamP2PFriends.csproj` | — | — | `af991c70de6b…` | 00:23 |
| `bin/Release/SteamP2PFriends.dll` | — | — | `8413c48434c07084` | 03:26 |

与 t95 自报一致（DLL 两处：主树与 `WhitelistTests/bin/Release` 同哈希）。

## 2. 三命令复跑（本人实测）

| 命令 | 结果 |
| --- | --- |
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | exit 0，**0 警告 0 错误**；DLL 复现 `8413c484`（确定性） |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | exit 0，**0 错误 4 警告**（CS0219×1、CS0649×3，均为既有） |
| `./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | exit 1 → **787/789 PASS (Failed: 2)**；仅 `[PureMemory] GBA01`、`NIL01` 失败（`TypeInitializationException → SDG.Unturned.SteamPlayer → NetReflection → ReflectionTypeLoadException`，控制台缺 Unity，既有环境失败）；Bug1 段 103/103 |

与 t95 交付口径一致。

## 3. 自跑变异（仓库外副本 `../_t97mut`，逐字节还原）

副本由主树 tar 复制（排除 `.git/.scratch/bin/obj/Release/publishi/Assets/.agent-teams/启动器`），`HintPath=..\Libs` 因副本与主仓同级而自然解析；副本独立重建得 `8413c484`（与主树一致）。脚本 `D:/tmp/mut97.py`（字节级替换 + `MATCH_COUNT` 断言 + `.t97bak` 备份）+ `D:/tmp/drive97.sh`（apply→Rebuild→cp DLL→跑 exe→抓 `FAIL [`→restore→Rebuild→打哈希）。

| 变异 | 改动点 | 变异后 DLL | 结果 | 新红用例 |
| --- | --- | --- | --- | --- |
| **MV-1** 子树只留首节点 | `Bug1GateProbe.cs:1237` 后插 `if (Environment.TickCount != int.MinValue) return;` | `476169da44ae0c70` | **787/789（与基线全同）** | 无 |
| **MV-2** 反例条件恒假 | `Bug1GateProbeLogic.cs` `if (observed && (sample.Torn \|\| !sample.VertexMoved))` 追加 `&& Environment.TickCount == int.MinValue` | `964a7b893041d4b2` | 786/789 | **Bug1-101** |
| **MV-3** 位比较换 1e-4 近似 | `Bug1GateProbeLogic.cs:441` `BitEqual(float)` → `Math.Abs(a-b) <= 1e-4f` | `fded98c53ce6f49d` | 785/789 | **Bug1-87、Bug1-99** |
| **MV-4** 去掉连续 2 次稳定化 | `Bug1GateProbe.cs:411-413` → `string heading = bucket;` | `45d104fbf905eef7` | **787/789（全绿）** | 无 |
| **MV-5** 还原门永不失败 | 两处 `restoreExact = false;` → `= Environment.TickCount == int.MinValue;` | `214709321d7d0854` | **787/789（全绿）** | 无 |

每条还原后：源 `33c65d22`/`54929772`、DLL `8413c484`、复跑 787/789。副本已删除，主树哈希未变。

## 4. 逐条 acceptance

| # | 判据 | 判定 | 证据 |
| --- | --- | --- | --- |
| 1 | float32 位级比较覆盖 local 10 项 + world 16 项，无 epsilon/哈希替代；换近似必然红 | **通过** | `Bug1GateProbe.cs:1272-1310` 逐字段写 10+16；`Bug1GateProbeLogic.cs:440-443` 位模式；`:531-536` 用 `PoseLocalFieldCount=10`/`PoseWorldFieldCount=16`；MV-3 红 Bug1-87/99 |
| 2 | P0/P1/P2 同 callback、每帧可还原、BakeMesh 之后无残留偏移 | **不通过** | 六格路径 `MeasureCellAxis`（`:456-566`）只复烘 P0（`:489`）与 P1（`:508`），还原后**没有 P2 复烘**，只比 transform 位 + pose + collider + raycast；MV-5 全绿说明还原门无任何用例 |
| 3 | pose 子树遍历有界（节点数上限/去重/顺序稳定） | **不通过** | `CollectSubtree`（`:1236-1248`）无节点/深度上限（只有 `AddColliderChain` 的 64）;去重与顺序在 `BuildPoseNodePlan`（`Logic:508-527`）有且被测；MV-1 全绿说明采集侧遍历零覆盖 |
| 4 | 覆盖校验器与采集端字段同源、可复算 | **通过** | `EvaluateCoverage` 直接遍历 `_cellSamples` 同一对象；`BuildCoverageJson` 读 `report.Cells`；Bug1-101 断言同输入复算逐格计数一致 |
| 5 | 六格归类不被瞬时抖动打穿；连续帧要求真实存在 | **部分不通过** | 稳定化在代码里（`Logic:118` + `Probe:410-413`）**但零测试**：MV-4 全绿；且注释/常量名为「帧」，实际是「两次采样」（默认间隔 1s） |
| 6 | 无生产行为改动、默认关闭、未改 judge 与 Bug3 五文件 | **通过** | `Enable` 默认 false（`Probe:131-133`）；`Bug1ProbeJudge.cs` = `9eeee011`；Bug3 五文件 `7a82de8e/a89f40b9/3d5044f7/a4553385/5b0c4e9b` 未变；`Release/`、`publishi/` 无 03:00 后写入 |
| 7 | 自动化可证与需 1H+2G 的边界如实区分 | **通过** | `bug1-gate-pose-coverage-t95.md` §6/§8 明列「仍未验证」项（BakeMesh 真实蒙皮、残差阈值、六格实机覆盖），并声明「本票未实机」 |

## 5. Findings

### R17-01（medium）采集侧 pose 遍历无上限，且整段零覆盖

- 位置：`Platform/Diagnostics/Patches/Bug1GateProbe.cs:1236`（`CollectSubtree`）
- 问题：递归遍历 Skeleton 全子树，**没有节点数上限、没有深度上限**；`AddColliderChain` 有 `guard++ < 64`，`PathOf` 有 64，唯独这里没有。UMA/换装模型的最坏情况下，`SnapshotPose` 每次采样要走 4 遍（C0/C1/A1/A2），每遍都建路径字符串，热路径成本随子树线性增长且不可预期。
- 测试证据：MV-1（让 `CollectSubtree` 只收根节点、不递归）→ **787/789 全绿**。现有 `Bug1-100` 只测 `BuildPoseNodePlan` 的纯逻辑，从未调用生产 `SnapshotPose`/`CollectSubtree`。
- requiredFix：给 `CollectSubtree` 加节点数 + 深度双上限（超限记 `nodes_truncated` 并使对应样本 `Available=false`/覆盖 UNKNOWN，不得静默截断）；补一条生产侧用例——构造 `root→child→grandchild` 三节点与一个越界深度，断言 `SnapshotPose().Entries.Count` 随子树增长且超限被显式标记。

### R17-02（medium）「连续 2 帧」稳定化是空转的，且命名与语义不符

- 位置：`Bug1GateProbe.cs:410-413`；常量 `Bug1GateProbeLogic.cs:118`
- 问题：`UpdateHeadingStability` + `_headingStableFrames >= HeadingStableFrames` 确实在代码里，但**没有任何用例覆盖**：MV-4 直接把 `heading` 改成 `bucket`（完全绕过稳定化）→ 787/789 全绿。另外该计数在 `SampleOnce` 里按**采样**递增（默认 `SampleIntervalSeconds=1.0`），不是按帧；注释「连续 2 帧」与实机语义不一致，会误导实机结论（1s 抖动与 16ms 抖动不是一回事）。
- requiredFix：把判定抽成纯函数（输入 `bucket/lastBucket/stableCount`，输出 `acceptedBucket/nextCount`），补 3 条用例（首帧同桶→unstable；第二次同桶→采纳；两桶交替→始终 unstable）；注释与常量改名（`HeadingStableSamples`）。

### R17-03（medium）六格路径缺 P2 复烘，还原门无顶点级复核，且与报告不符

- 位置：`Bug1GateProbe.cs:528-560`（`MeasureCellAxis` 还原后段）、报告 `bug1-gate-pose-coverage-t95.md:44`
- 问题：报告 §3 第 4 步写「还原 → 位级校验 → 取 A2 → **复烘 P2**」，但六格路径里没有 P2 复烘（`BakeSample(` 只在 `:489` P0 与 `:508` P1 两处；P2 复烘仅存在于 t92 的 `RunGate:819`）。也就是「BakeMesh 之后无残留偏移」只被 transform 位比 + pose + collider + raycast 覆盖，**顶点级残留没有被测**。MV-5（`restoreExact` 恒真）→ 全绿，进一步说明还原门本身也没有用例。
- requiredFix：二选一——(a) 还原后补一次 `BakeSample` 并与 P0 做逐点位级比较（失败即 `InvarianceOk=false` 并记原因）；或 (b) 把 acceptance 与报告 §3 明确改为「姿态级还原门（不含顶点级）」，并在 JSON 里标注 `restore_gate=pose`。任一选择都要补一条能红的用例（把 `restoreExact` 改假的变异必须使该用例红）。

### R17-04（low）unassigned 样本占用 MaxSamples 预算，可能提前收尾

- 位置：`Bug1GateProbe.cs:418-426`、`:634-657`（`CheckCoverageLimits`）
- 问题：格子不稳（`cell == null`）与姿态不稳的样本都会 `_cellSamples.Add(... unassigned/不可用 ...)`，而 `CheckCoverageLimits` 用 `_cellSamples.Count >= maxSamples` 收尾。实机转向频繁时，可能先被这类无效样本耗尽 240 上限，六格却还没覆盖，最终 `max_samples_reached` + `SAMPLES_INSUFFICIENT` —— 结论不算错，但会浪费一轮实机。
- requiredFix：`maxSamples` 只计 `Available && cell != null` 的有效样本；无效样本单独计数（JSON 增 `unassigned_samples`），并在 `max_samples_reached` 的 detail 里区分「有效样本耗尽」与「抖动耗尽」。

## 6. 通过项（供后续轮次对照）

- 位级比较是**真位比较**：`BitConverter.ToInt32` 取位模式（`Logic:441`），±0 不等、NaN 按 payload；`PosesEqual` 同时比 `Path`/`Role`/`HasLocal`/`HasWorld`，任一不可用即 false（不把「读不到」当「没变」）。
- 位比较的**接线**被 IL 钉住：`Bug1-102` 断言 `SampleOnce` 调 `SnapshotPose`×2、`PosesEqual`；`MeasureCellAxis` 调 `PosesEqual`×2；`CheckCoverageLimits`/`FinishCoverage` 调 `EvaluateCoverage`；`OnPreCull` 调 `SampleOnce`。
- 同一 callback 内取样：`MeasureCellAxis` 的基线、施加、还原全在 `onPreCull` → `SampleOnce` 调用栈内完成，不跨帧比动画。
- 还原用 `finally`（`:528`），异常路径也会复位 `localPosition`，不会把 +0.1U 留在模型上。
- 覆盖判定顺序合理：反例（`!VertexMoved || Torn`，即 t94 §4.1 极性）→ FAIL；样本不足 → UNKNOWN；epsilon 未指定 → UNKNOWN（不替决策者挑阈值）；无正证据 → UNKNOWN；六格全过 → PASS。短路顺序保证「有反例」不会被「样本不足」掩盖。
- 同源导出：`_cellSamples` 是唯一数据源，`EvaluateCoverage` 与 `BuildCoverageJson` 都吃同一批对象，不存在「一处算一处不写」。
- 默认关闭：`Enable=false`；关闭时 `Initialize` 不 `Arm`，不挂相机、不装 detour；`OnEnabledChanged` 重新开启会 `ResetCoverageState()` 并 `_ranOnce=false`，上一轮结论不会污染下一轮。
- 目标解析走既有 `Bug1ProbeTargetSelector`（复用 t30 修过的 `ReferenceEquals` 判空），未重新引入 `playerID == null`。
- `epsilon` 未指定时 `coverage_status=UNKNOWN`，与 t76 §2.2「不自选阈值」一致。

## 7. pass 边界（本轮判定只覆盖这些）

- 本判定只对 §1 的字节成立；任何再改动（含测试）都必须重绑哈希重审。
- **自动化可证**：位级比较、覆盖判定规则、JSON 字段同源、默认关闭、IL 接线。
- **仍不可证（必须 1H+2G 实机）**：`BakeMesh` 在 Mono/真实蒙皮下是否返回真实蒙皮顶点；真实动画下 `A0==A1` 能否成立（否则大量 `POSE_UNSTABLE`，属有效结论不是通过）；六格是否真被覆盖到；`max_moved/min_moved` 的 0.05/0.01 判据在真实 rig 上的表现。
- 本轮**未**在 1H+2G 上运行；不得据本轮结论宣称「G 门成立」或「Bug1 已修复」。

## 8. 复现命令

```bash
# 1) 绑定字节
sha256sum Platform/Diagnostics/Patches/Bug1GateProbe.cs \
          Platform/Diagnostics/Patches/Bug1GateProbeLogic.cs \
          WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs \
          WhitelistTests/Program.cs bin/Release/SteamP2PFriends.dll

# 2) 三命令
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe

# 3) 变异（副本必须与主仓同级，HintPath=..\Libs 才能解析）
python D:/tmp/mut97.py MV1      # 子树只留首节点
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
cp bin/Release/SteamP2PFriends.dll WhitelistTests/bin/Release/
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe
python D:/tmp/mut97.py restore  # 逐字节还原，再 Rebuild 核对 8413c484
```


<!-- self_sha256(body)=a2b057052fb3352355e4acfdabe9cf9a174778bdb8925b011596e3522a7e51e7 -->
