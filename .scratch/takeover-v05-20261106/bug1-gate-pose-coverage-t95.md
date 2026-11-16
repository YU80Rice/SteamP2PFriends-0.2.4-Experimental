# t95 — G 门六格姿态覆盖：子骨骼位级比对 + 自动格子归类 + 离线覆盖校验

- 任务：t95（attempt 2，attempt_id `5462edbe-2edc-4b10-8186-4c8445007ffd`）
- 依据：`.scratch/takeover-v05-20261106/architecture-decision-t94-g-pose-coverage.md`（t94，`05f6a46d…`）
- 基线：t92 三条根变换边已审 CLEAN（`Bug1GateProbe.cs` 955 行 `f376f2a1…`、`Bug1GateProbeLogic.cs` 805 行 `07261a09…`、测试 619 行 `13eb30a3…`、`Program.cs` 1103 行 `0fbb4eac…`、DLL `6c307994…`）
- 结论口径：**本报告只陈述自动化可证的部分。没有跑过 1H+2G 实机，任何「G 门成立 / 全部顶点跟随」都不成立。**

---

## 1. 快照函数覆盖范围（与实现一致）

| 函数 | 位置 | 覆盖范围 | 比较方式 |
| --- | --- | --- | --- |
| `SnapshotTransform(Transform)` | `Bug1GateProbe.cs` | **仅**该 Transform 的世界 position（3 float）+ 世界 rotation（4 float），共 7 标量；不含 scale、不含子节点 | `TransformsEqual` 逐字段 `BitEqual` |
| `SnapshotPose(Transform skeleton, Player target, IList<SkinnedMeshRenderer> renderers)` | `Bug1GateProbe.cs` | Skeleton **全子树**（含 inactive）∪ 每个参与 renderer 的 `bones[]` / `rootBone`（Skeleton 树外也纳入）∪ 玩家 Collider 所挂 Transform 到 root 的**父链**；每节点 localPosition(3)+localRotation(4)+localScale(3)=10 项 + `localToWorldMatrix` m00..m33=16 项，共 26 个 float32 | `PosesEqual` 逐节点逐字段 `BitEqual`（`BitConverter` 位模式相等），**无哈希、无 epsilon、无距离阈值** |
| `SnapshotColliders` / `RaycastProbe` | `Bug1GateProbe.cs` | 物理面：collider 名称/类型/enabled/center/size；一次受控射线命中体与距离 | 字段级相等 + `Available` fail-closed |

`SnapshotPose` 的失败路径一律把快照标成 `Available=false` 并给出原因（`missing:<path>`、`unreadable:<path>`、`empty-node-set`、非有限分量），**不会用空集合相等冒充「未变化」**。

---

## 2. 六格归类规则（桶边界）

- 格子集合：`idle-x, idle-z, walk-x, walk-z, sprint-x, sprint-z`（`Bug1GateProbeLogic.AllCells`）。
- 速度维：用**原生 stance**（稳定，不用瞬时速度）——`IDLE→idle`、`STAND→walk`、`SPRINT→sprint`；`CROUCH/PRONE/SWIM/CLIMB/DRIVING` 不在 T76 支持域，判 `unstable` 并写 `stance_out_of_scope:<NAME>`，不补样本。
- 朝向维：取 `target.transform.forward` 的水平分量 `(fx, fz)`：
  - 非有限 → `heading_non_finite`（unstable）；
  - `|fx| < 0.20 && |fz| < 0.20` → `heading_too_small`（unstable）；
  - `|fx| >= 1.5 × |fz|` → `x`；`|fz| >= 1.5 × |fx|` → `z`；
  - 其余（含 45° 附近）→ `heading_near_45deg`（unstable）。
  - 另需**连续 2 帧**（`HeadingStableFrames=2`）落在同一桶才采纳，避免转向瞬间抖动归档。
- 任一维 unstable → 该帧不归档（`unassigned`，不计有效样本）。
- 两桶相差约 90°，来自「主轴比 1.5」的边界，等价于 `|yaw|` 落在主轴 ±33.7° 内。

---

## 3. P0/P1/P2 与 A0/A1/A2（同一 callback）

`SampleOnce`（相机 `onPreCull` 内）→ 每轴 `MeasureCellAxis`：

1. 零偏移对照 C0/C1：同一帧连取两次 `SnapshotPose`，不等或不可用 → `POSE_UNSTABLE:<diff>`，本次不施加、不记有效样本；
2. 取 P0（`BakeSample` 基线）与 A0（=C1）；
3. 施加 `+0.10U`（X 轴 `(0.1,0,0)` 或 Z 轴 `(0,0,0.1)`，**Y 恒为 0**）→ 取 A1 → 复烘 P1；
4. `finally` 还原 `localPosition` → 位级校验 `restoreExact` → 取 A2（`PosesEqual(A0,A2)`）→ 复烘 P2；
5. 同一次采样内复测 root/Third/collider/raycast 三组不变性；
6. 判 `VertexMoved = anyBake && maxMoved >= 0.05U`、`Torn = maxMoved >= 0.05U && minMoved <= 0.01U`、`EpsilonSpecified`、`MaxResidualU`。

P1 的顶点来自 `SkinnedMeshRenderer.BakeMesh` 的真实蒙皮顶点（世界坐标 = `localToWorldMatrix.MultiplyPoint3x4`），不是 transform 位置。

---

## 4. 离线覆盖校验器（`EvaluateCoverage`）

输入：`IList<Bug1GateCellSample>` + `minSamples` + `epsilonU`；输出 `Bug1GateCoverageReport`（含 `g_evidence_revision=T94_POSE_GRID-v1`、六格各自 `samples/valid/positive/counterexamples/state/detail`）。

判定顺序（短路）：

1. 任一格出现**反例** → `FAIL / POSE_COUNTEREXAMPLE`（detail 指出格子与样本）；
2. 任一格 `valid < minSamples` → `UNKNOWN / SAMPLES_INSUFFICIENT`；
3. 某格 `positive == 0` 且 `epsilonU <= 0` → `UNKNOWN / EPSILON_UNSPECIFIED`（不自选阈值）；
4. 某格 `positive == 0` → `UNKNOWN / NO_POSITIVE_EVIDENCE`；
5. 六格都有正证据 → `PASS`。

其中：

- 反例 = `PoseEqual && InvarianceOk && (Torn || !VertexMoved)`；
- 正证据 = `PoseEqual && InvarianceOk && VertexMoved && EpsilonSpecified && MaxResidualU <= epsilonU`。

**极性说明（必须记录）**：t95 acceptance 括号把「P1 顶点确实移动 且 A0==A1 且 物理未变」写成「反例」，与 t94 §4.1 相反（t94 原文：pose 稳定/观测有效而表面不随动或撕裂 → 否定 profile，立即 FAIL）。实现按 t94；若需按括号字面极性，只改 `EvaluateCoverage` 的一个布尔组合即可。

---

## 5. 逐文件 sha256（改前 → 改后）

| 文件 | 改前 | 改后 | 行数 |
| --- | --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1GateProbeLogic.cs` | `07261a091b60908f75ab42b11a3db36b344d9a09ac0e363cc021f8ad0dcb09b7`（805） | `549297721f284fe2f46c6f670d2e319e2536c0876cace9e7ad9cd26e0e4f09d5` | 1275 |
| `Platform/Diagnostics/Patches/Bug1GateProbe.cs` | `f376f2a1…`（955） | `33c65d22e98fef96665c4a0205156d9bc42fc7f28e92d51c3209e21b81501dc5` | 1497 |
| `WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs` | `13eb30a3a3f714665909607bd10fc8568c9832804aaba3f11b9722e7b3a0e848`（619） | `6896fdf5101bdb3213332f961d84363de19a501755c1b6c9756b84d9eb27414e` | 842 |
| `WhitelistTests/Program.cs` | `0fbb4eacdfc971882756a5a992a9e556e7f2356775389d7b862d112d3796fc18`（1103） | `78f2b6b165c6254b3e44055e74e9162c0652ed928f8852198880c929b8a09446` | 1108 |
| `bin/Release/SteamP2PFriends.dll` | `6c307994912dc3397367db799e337976ca3203e683a440720c4cfb45f987f834` | `8413c48434c07084e0764b918fd3d47a3c6cdba8e1de51b49befd4a564dd7fc5` | — |

新增用例：Bug1-98（姿态集合覆盖/去重/角色）、Bug1-99（位级比较 + 1 ULP）、Bug1-100（六格归类）、Bug1-101（覆盖校验四态 + 可复算）、Bug1-102（生产接线 IL）。`Program.cs` 只追加这 5 行，未动 `T69-01`。

---

## 6. 变异证据（仓库外副本 `../_t95mut`，已逐字节还原并删除）

副本由主树 `tar` 复制（排除 `bin/obj/.git/.scratch/Release/publishi/Develop-Stage`，带 `Build/Version.props`）。每条：唯一替换 → 先 `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` → 再 `dotnet build WhitelistTests/... -t:Rebuild` → 跑 exe → 打印变异后 DLL sha256 → 从主树 `cp` 还原并打印源码 sha256。

| 变异 | 文件:行 | 原文 → 替换文 | 变异后 DLL | 红名单 |
| --- | --- | --- | --- | --- |
| MV-1 删子树比较 | `Bug1GateProbeLogic.cs:509` | `AppendPlan(plan, seen, skeletonSubtreePaths, PoseRoleSkeleton);` → 只取 `skeletonSubtreePaths[0]` | `1593ee117370d3ba0c41e9e133494cb9cc5d30ddabd56c4a30389ca7b0bfc9fa` | Bug1-98（786/789） |
| MV-2 删反例判定 | `Bug1GateProbeLogic.cs:728` | `if (observed && (sample.Torn \|\| !sample.VertexMoved))` → `if (observed && sample.Torn && !sample.Torn)` | `df0098c30aa2c2bf9982ff686e1f0feff2f33eb918cb79a6885cfba004d356d8` | Bug1-101（786/789） |
| MV-3 bit 比较换 epsilon | `Bug1GateProbeLogic.cs:563` | `if (!BitEqual(left[i], right[i])) return false;` → `if (Math.Abs(left[i] - right[i]) >= 1e-4f) return false;` | `4926dd206eb8569462ae0e326c75591b3f16f52167f66df021132b39d42e1c13` | Bug1-99（786/789） |

三条变异都只红一条自建用例（另 2 条红是既有 GBA01/NIL01 环境失败）。还原后 `Bug1GateProbeLogic.cs` 回到 `54929772…`，副本 `diff -r`（排除 `bin/obj/.git/.scratch/Release/publishi/Develop-Stage`）只剩驱动脚本 `mut.py` 与 `__pycache__`，副本已 `rm -rf`。

---

## 7. 三命令

| 命令 | 结果 |
| --- | --- |
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 已成功生成，**0 警告 0 错误** |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | 已成功生成（4 条既有警告，0 错误） |
| `./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `=== Final Result: 787/789 PASS (Failed: 2) ===`，仅 `GBA01 Native First Observe Binding`、`NIL01 Real Native Lookup Contract` 失败（既有环境失败，`TypeInitializationException: SDG.Unturned.SteamPlayer` 链）；**Bug1 段 103/103 PASS** |

exe 在存在失败时返回 1，属仓库既有约定。

其它核验：Bug3 五文件哈希未变（`a89f40b949f841d0` / `7a82de8e38fa0f6a` / `3d5044f7870fbb17` / `a45533850f31bd96` / `5b0c4e9ba4c87cbe`）；`Release/`、`publishi/`、`Develop-Stage/` 无本次时间窗内新文件（未写）。

---

## 8. 自动化可证 vs 仍需 1H+2G

**自动化可证（本票已证）**

- 姿态快照的**集合覆盖**规则：Skeleton 全子树 + renderer `bones[]`/`rootBone` + Collider 父链，按路径去重保序、角色优先 skeleton→bound→collider-parent（Bug1-98）。
- **位级比较**：local 与 world 矩阵任一分量差 1 ULP 即判不等，差异定位到 `path|local[i]` / `path|world[i]`（Bug1-99）。
- **六格归类**：stance 映射、主轴比 1.5、最小分量 0.20、45° 与 NaN 拒绝、`ComposeCellId`（Bug1-100）。
- **覆盖校验语义**：反例→FAIL、样本不足→UNKNOWN、epsilon 未指定→UNKNOWN、无正证据→UNKNOWN、六格全过→PASS，且同输入可复算（Bug1-101）。
- **生产接线**：`SampleOnce` 至少两次 `SnapshotPose` + 一次 `PosesEqual`；`MeasureCellAxis` 两次 `PosesEqual`（A1/A2）；`CheckCoverageLimits`/`FinishCoverage` 各调 `EvaluateCoverage`；`OnPreCull` 调 `SampleOnce`（Bug1-102）。
- 三条变异各自只红对应用例。

**仍需 1H+2G（未验证，不得当成已成立）**

- `BakeMesh` 在 Mono/真实蒙皮上是否返回真实顶点、P1 位移中位数是否接近 0.10U、残差 `max‖Δp−delta‖` 是否 ≤ epsilon（epsilon 仍无裁决值，本探针输出 `epsilon_source=unspecified` 时整体 UNKNOWN）。
- 六格在真实操作下能否被「正常玩一遍」自然覆盖（走/跑/两个朝向），以及 `HeadingStableFrames=2` 在真实转向抖动下是否够稳。
- root/Third/Skeleton 三条边与 collider/raycast 在真实物理帧里是否真的不动（离线只有纯逻辑与合成夹具）。
- 骨骼子树在真实动画下每帧的位级稳定性（本探针要求 A0==A1 才采信，真实环境可能大量 `POSE_UNSTABLE`，那属有效结论而非通过）。

**F6/F7 满足程度**

- **F7（骨骼 pose 覆盖）**：从「只读 Skeleton 根 7 标量」提升为「Skeleton 全子树 + 绑定骨骼 + Collider 父链，每节点 local 10 项 + world 16 项 float32 逐位」；代码与文档已写清每个函数的实际覆盖范围。仍未做的是「跨帧动画稳定性」的实机统计。
- **F6（单帧单姿态）**：从「一次 callback 单姿态」提升为「六格 × 两轴 × 每格多样本，最少 3 个有效样本/格」，并有 `MaxSamples=240` 上限与提前收尾（PASS/反例/上限）。是否真的覆盖到六格，只能由实机产物判定（离线一律 UNKNOWN）。

**未实机声明**：本票未在 1H+2G 上运行过任何一轮；所有 `GATE_RESULT` / `pose_coverage` 结论都需真实采集后才能给出。
