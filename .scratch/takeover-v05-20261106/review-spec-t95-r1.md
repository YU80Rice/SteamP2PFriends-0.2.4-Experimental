# T96 规格轴复审：t95 六格姿态覆盖（G 门）

- 审查官：reviewer-spec（规格轴，只判契约/裁决是否被忠实执行，不评风格）
- 被审：t95（G 门六格姿态覆盖，含 F6/F7 提升）
- 判定依据：`architecture-decision-t94-g-pose-coverage.md` §2/§3/§4（唯一极性依据）、t76 §2.1–§2.3、派工书 6 条 acceptance
- **verdict：needs_revision（status=failed）**——极性正确、位级比较真实可失败，但**六格中 idle 两格在生产不可达**（`EPlayerStance` 无 `IDLE`），且站立样本被归入 walk 格；另有"测得 yaw/真实 stance 未记录、桶窗过宽"与"过期 note"两条。

---

## 1. 被审字节绑定（本机实测，逐项与派工一致）

| 文件 | 字节 | 行 | sha256 |
|---|---:|---:|---|
| `Platform/Diagnostics/Patches/Bug1GateProbe.cs` | 68077 | 1497 | `33c65d22e98fef96665c4a0205156d9bc42fc7f28e92d51c3209e21b81501dc5` |
| `Platform/Diagnostics/Patches/Bug1GateProbeLogic.cs` | 62550 | 1275 | `549297721f284fe2f46c6f670d2e319e2536c0876cace9e7ad9cd26e0e4f09d5` |
| `WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs` | 50986 | 842 | `6896fdf5101bdb3213332f961d84363de19a501755c1b6c9756b84d9eb27414e` |
| `WhitelistTests/Program.cs` | 151454 | 1108 | `78f2b6b165c6254b3e44055e74e9162c0652ed928f8852198880c929b8a09446` |
| `SteamP2PFriends.csproj` | 27726 | — | `af991c70de6bd822de0fd50d4a7f74496c52f2156e7377dacdd2a294f16d912d`（本轮未改） |
| 交付 DLL（两处 bin 同哈希） | 1556480 | — | `8413c48434c07084e0764b918fd3d47a3c6cdba8e1de51b49befd4a564dd7fc5` |

复跑：主树 `SteamP2PFriends.WhitelistTests.exe` → **787/789 PASS**，失败仅 `GBA01`/`NIL01`（`TypeInitializationException → SDG.Unturned.SteamPlayer → NetReflection → ReflectionTypeLoadException`，控制台无 Unity 运行时，属既有环境失败）；Bug1 段 103 条全 PASS（含 Bug1-100/101/102）。

> 本判定只对上述字节有效。任何一次源码/测试/DLL 改动后需重新绑定。

---

## 2. 极性核对（本次最关键的误判风险）

派工书把「成功判据」误写成「反例」，并明确要求以 t94 §4.1 为准。核对结果：

- 实现（`Bug1GateProbeLogic.cs:731-740`）：
  - 反例 = `sample.PoseEqual && sample.InvarianceOk && (sample.Torn || !sample.VertexMoved)`
  - 正证据 = `sample.PoseEqual && sample.InvarianceOk && sample.VertexMoved && sample.EpsilonSpecified && sample.MaxResidualU <= epsilonU`
- t94 §4.1 原文语义：**pose 稳定、物理门未变，而真实蒙皮表面不随动或撕裂 → 足以否定该 profile（FAIL）**；完整阳性覆盖仍是必要条件。
- 两者一致；报告 `bug1-gate-pose-coverage-t95.md` §4 亦明确写出"t95 acceptance 括号与 t94 相反，实现按 t94"。

**结论：极性正确，未按错误括号实现，不因此判 needs_revision。**

---

## 3. 逐条 acceptance 判定

| # | 判据 | 结果 | 证据 |
|---|---|---|---|
| 1 | 忠实实现 t94 §4.1 极性 | **passed** | 见 §2；`Logic:731-740` 与 t94 §4.1 同义 |
| 2 | pose 快照覆盖 Skeleton 全子树 + 绑定骨骼 + Collider 父链，float32 位级比较，变异必须红 | **passed** | `Bug1GateProbe.cs:1162-1215`（子树 + `bones[]`/`rootBone` + `Collider` 父链 + 计划去重）；`Logic:532-566` 全位级；落盘为 10 local + 16 world float32（`Logic:118,532-566`）；**自跑变异见 §4，Bug1-99 变红**（1 ULP 相邻被判相等即失败） |
| 3 | 六格归类规则稳定、文档写出桶边界 | **failed** | 朝向桶有 2 帧连续要求（`Probe:411,606-616`，`Logic:118`）与 45°/过小/NaN 拒绝（`Logic:605-628`），文档化了阈值；**但 idle 两格在生产不可达、站立被归 walk（T96-F1）；测得 yaw 未记录、桶窗 1.5 主轴比≈±33.7° 而非 t94 的 yaw±5°（T96-F2）** |
| 4 | 每格最小有效样本数可判 | **failed** | 机制存在（`Logic:700-760`，`min_samples_per_cell`），但 idle-x/idle-z 永远拿不到样本 → 恒 `UNKNOWN/SAMPLES_INSUFFICIENT`（T96-F1） |
| 5 | 四态语义正确；样本不足必须 UNKNOWN 不是 PASS | **passed**（附口径说明） | `Logic:722-742` 反例短路 FAIL；`:753-778` 不足 → `Status=UNKNOWN / SAMPLES_INSUFFICIENT`；`:781-785` epsilon 未指定 → UNKNOWN；`:788-793` 无正证据 → UNKNOWN；全过才 PASS。实现没有名为 `INSUFFICIENT` 的独立状态，用"UNKNOWN + 专用 reason"表达——语义满足「不足不得 PASS」，仅名称与派工四态用词不同，记为口径说明，不计 finding |
| 6 | 自动化可证 / 仍需 1H+2G 如实区分，不得声称已实机运行 | **passed** | 报告 §8 分列两项，并明写"本票未在 1H+2G 运行过任何一轮" |
| 7 | 报告与注释中的覆盖声明与实现一致 | **failed** | 报告 §2 称"速度维用原生 stance——IDLE→idle"，而真实 `EPlayerStance` 无 IDLE（T96-F1）；`Bug1GateProbe.cs:900` 仍写 `single_frame_single_pose：只测一个相机帧、一个姿态（t76 §2.2 的多姿态需实机/Architect 决定）`，与本轮六格多姿态采样、t94 已裁决多姿态矛盾，且 `FinishCoverage` 复用 `_lastResult`（`:659-671`）使最终 GATE.json 同时带该过期 note（T96-F3） |

---

## 4. 自跑变异（本审查官独立执行，副本 `_t96iso`）

脚本 `D:/tmp/t96_mut.sh`；每条 `dotnet build -t:Rebuild` 生产 + 重建测试工程 + 覆盖两处 DLL + 跑 exe。

| # | 变异 | 变异源码 sha256 | 变异 DLL sha256 | 结果 | 红名单 |
|---|---|---|---|---|---|
| M0 | 基线 | — | `8413c484…`（与主树逐字节一致） | 787/789 | 仅 GBA01/NIL01 |
| M1 | `BitEqual(float)` → `Math.Abs(a-b) < 1e-4f` | `461405484656d2b3751f6165e2271acb812efeee3f70c975e3dfd90d82d80fb9` | `406be71fc36d6c5f5a809ad88f7a15394a23b46ff68a93e055180b3138be8f80` | **785/789** | **Bug1-87（`float 位级比较`）**、**Bug1-99（`local 1 ULP 相邻值被判相等（不允许 epsilon）`）** |
| R0 | 还原 | `54929772…`（=送审） | `8413c484…`（=送审，逐字节） | 787/789 | 仅 GBA01/NIL01 |

**结论：位级比较被真实钉住，「BitEqual 换成 Math.Abs<1e-4 必须红」这条验收实质成立。**

---

## 5. Findings

### T96-F1（high）idle 两格在生产不可达，站立样本被归入 walk 格

- 位置：`Platform/Diagnostics/Patches/Bug1GateProbeLogic.cs:668-687`（`ClassifyStanceCell`）、`Platform/Diagnostics/Patches/Bug1GateProbe.cs:621-631`（`StanceName`）
- 事实：
  - t94 §2 第 19–22 行明确：`I0/I90 = STAND、静止idle`；`W0/W90 = STAND、实际行走步态`。即 idle 与 walk **共用 STAND stance**，靠"是否真实移动"区分。
  - 生产 `StanceName` 返回 `target.stance.stance.ToString()`，即原生枚举名；SDK `EPlayerStance` 只有 `CLIMB, SWIM, SPRINT, STAND, CROUCH, PRONE, DRIVING, SITTING`，**没有 IDLE**。
  - `ClassifyStanceCell` 只把字符串 `"IDLE"` 映射到 idle、`"STAND"` 无条件映射到 walk。因此 `idle-x/idle-z` 在真实环境**没有输入能命中**；而站着不动（stance=STAND）会被计入 **walk** 格。
- 后果：
  1. `EvaluateCoverage` 永远在 idle 两格报 `SAMPLES_INSUFFICIENT`（`Logic:753-778`（`if (missing.Count > 0)` 在 :774）），**六格 PASS 在生产内不可能达成**（fail-closed，不会误判通过，但本轮承诺的"六格可判"落空，实机 1H+2G 必然空跑）。
  2. walk 格会装入"站立不动"的样本，而 t94 要求 walk 代表"实际行走步态"；这些样本还可被计为正证据（`Logic:735-740`），使 walk 的"已证"含义失真——正是 t94 §2 要分离的 `idle` 与 `walk` 再次合流（F6 的回退形态）。
  - 离线用例 `Bug1GateProbeTests.cs:670-671` 断言 `ClassifyStanceCell("IDLE")==idle`，属"离线绿、生产不可达"。
- requiredFix：生产侧改为由 **STAND + 稳定移动态**派生 idle/walk（原生 `PlayerStance` 的移动标志或速度阈值均可，但须是稳定量而非单帧瞬时值），SPRINT → sprint；`IDLE` 分支要么删除、要么由派生值提供；并补一条以**生产可达输入**驱动的映射用例（至少覆盖 STAND 静止 / STAND 行走 / SPRINT / CROUCH / 未知值），以及在产物里记录每次采样的真实 stance 字符串以便事后复核。

### T96-F2（medium）每格样本未记录"真实 stance / 测得 yaw"，朝向桶窗口宽于 t94 的 ±5°

- 位置：`Platform/Diagnostics/Patches/Bug1GateProbe.cs:527`（样本 Detail）、`Bug1GateProbeLogic.cs:605-628`（`ClassifyHeadingBucket`）、`:1241-1253`（`Bug1GateCellSample` 字段）、`:820-838`（cells 导出）
- 事实：
  - t94 §2 要求"每格记录真实 stance、测得 yaw、动作证据"；产物 `cells[]` 只有 `samples/valid/positive/counterexamples/state/detail`，`detail` 仅 `axis=..;max_moved_u=..;min_moved_u=..`；`Bug1GateCellSample` 无 stance/yaw 字段；stance 只出现在 `cell-unstable:` 拒绝行（`Probe:423`）。
  - 桶边界用主轴比 1.5（≈±33.7° 内都算该轴桶）+ `min component 0.20`，与 t94 §2 的"yaw0 / yaw0+90°，允许 ±5°"不符：操作者瞄准 20°/70° 也能分别进入 x/z 桶，两基底只差 50°，弱于 t94 要求的约 90° 正交检查。
- requiredFix：`cells[]` 与样本详情增加 `stance`、`measured_yaw_deg`（及每个正证据样本的残差），并按测得 yaw 与格标称值（0°/90°）±5° 判入格；若认为±5°不可执行，需 Architect 书面改写 t94 §2，不得由实现默默放宽。

### T96-F3（low）GATE.json 仍输出过期的"单帧单姿态"口径

- 位置：`Platform/Diagnostics/Patches/Bug1GateProbe.cs:900`（note 文本）、`:659-671`（`FinishCoverage` 复用 `_lastResult` 后追加 `coverage_finished`）
- 事实：最终产物同时含 `coverage_finished:samples=N` 与 `single_frame_single_pose：只测一个相机帧、一个姿态（t76 §2.2 的多姿态需实机/Architect 决定）`；t94 已裁决多姿态六格，note 的括注与现状相反，读者会误判本轮只测了一个姿态。
- requiredFix：note 按模式拆分——覆盖采样模式下写"六格采样 N 次、有效样本 M 个、姿态由 stance+yaw 区分"，或仅在单次测量路径添加该 note。

> 非规格轴观察（转工程轴，不计 finding）：`ClassifySpeedCell`（`Logic:631-655`）已无生产调用点（`grep` 只命中测试），与 `ClassifyStanceCell` 语义重叠，建议删除或标注 test-only，避免下一轮误接。

---

## 6. 判 PASS 的明细（须保留，不得在后续轮回退）

1. 极性：反例 = pose 位级稳 + 物理门未变 + （表面不动或撕裂）→ FAIL 短路；正证据需 `VertexMoved && epsilon 指定 && MaxResidualU<=epsilon`。
2. 位级：`PosesEqual` 逐节点逐字段 `BitEqual`，`HasLocal/HasWorld` 缺失不等；`Available=false` 一律不等；无哈希、无 epsilon、无两位小数（我以 1 ULP 相邻值 + `Math.Abs<1e-4` 变异双向验证）。
3. 节点集合：Skeleton 全子树（含 inactive）+ renderer `bones[]`/`rootBone` + Collider 挂点父链，按路径去重；不可读 → `unavailable:<detail>` 而非空表相等。
4. 稳定性：零偏移对照 C0/C1 同 callback 比较（`Probe:429-431`）；每轴 P0/P1/P2 三点逐位（`:498-499`、`:547-548`）；朝向桶要求连续 2 次同桶才采纳（`Logic:118`、`Probe:606-616`），45°/过小/NaN 一律 unstable。
5. 覆盖判定：反例优先 FAIL；样本不足 / epsilon 未指定 / 无正证据一律 UNKNOWN，绝不 PASS；`min_samples_per_cell` 可配。
6. 口径：报告 §8 明列"自动化可证 6 项 / 仍需 1H+2G 4 项"，并声明本轮未跑实机；产物带 `g_evidence_revision`。
7. 未越界：`SteamP2PFriends.csproj` 本轮未改；未出现新增姿态驱动器、未引入插值/近似比较、未用 bounds 或骨骼根替代真实蒙皮顶点。

---

## 7. 仍需 1H+2G（本票无法关闭，归属实机轮）

1. `BakeMesh` 在真实 Mono 下是否可读、顶点世界坐标与画面一致（决定 `VertexMoved/Torn` 是否有意义）。
2. 六格能否被"正常玩一遍"自然覆盖（含本轮修正后的 idle/walk 派生是否真的可采到样本）。
3. 三条变换边 + collider/raycast 在真实物理帧内的稳定性（离线无法证明物理回调时序）。
4. 骨骼子树跨帧位级稳定：真实动画是否每帧都在改 pose（若改，则 `POSE_UNSTABLE` 将常态化，六格样本永远不可用）。

以上四项任一失败的表现都应是 UNKNOWN，不得以离线 787/789 代替。

---

## 8. 边界声明

- 本报告只对上表 6 个字节（sha256 逐项列出）负责；任何改动需重新绑定并重审。
- **pass 不等于 G 门成立**：即使后续修好 F1–F3 且双轴通过，也只能证明"离线可判 + 极性正确"，G 门是否成立仍须 1H+2G 实机；本轮 verdict 本身为 needs_revision。
- 未修改任何生产/测试源码；变异在仓库外副本 `_t96iso` 完成并已还原（源码与 DLL 均逐字节回到送审值）。

---

自绑定：被审文件的逐项 sha256 见 §1（本报告不内嵌自身哈希，避免自指）。本判定只对 §1 列出的字节有效。
