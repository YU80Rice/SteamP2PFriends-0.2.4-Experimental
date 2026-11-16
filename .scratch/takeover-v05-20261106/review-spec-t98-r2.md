# 规格轴复审 t98（G 门六格姿态覆盖修复轮）— T102 / round 2

- 审查官：reviewer-spec（规格轴，独立于实现）
- 被审任务：t98（ds-probe2），承接 t96 的三条 findings
- 审判依据：`architecture-decision-t94-g-pose-coverage.md`（§4.1 极性、§2 六格、§3 F7 位级、§5 记账）与 t96 原 finding 文本；**不采用 t95 派工书中写反的括号**
- 结论：**pass**（三条 finding 全部关闭；另记 1 条 low 残留，不影响结案）
- 本判定只对第 1 节列出的字节有效

## 1. 被审字节（本机实测，逐项与 Captain 票面一致）

| 文件 | 字节 | 行数 | sha256 |
|---|---|---|---|
| `Platform/Diagnostics/Patches/Bug1GateProbe.cs` | 71650 | 1554 | `1d58f950600c3d54d2b3c69021a6f747a97e946dba27b7797d0fa487ae1e6651` |
| `Platform/Diagnostics/Patches/Bug1GateProbeLogic.cs` | 69821 | 1416 | `7a0c6d24de12934a5af92bc9e1bdffc89cf7595a8c7bba0eb16d9f2f67937c8d` |
| `WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs` | 61152 | 998 | `3e5bae4e3f6efb8c3448e8c44f5bb87ff9b2d8874f18bbe2efec5eaf88b69561` |
| `WhitelistTests/Program.cs` | 152611 | 1114 | `1d6d979dfd15678bec3247a0502bb1f44f188168321c960d70453616d3f0e460` |
| `SteamP2PFriends.csproj` | 27726 | — | `af991c70de6bd822de0fd50d4a7f74496c52f2156e7377dacdd2a294f16d912d`（未改） |
| `bin/Release/SteamP2PFriends.dll`（两处 bin 同哈希） | 1560064 | — | `ad1283ac0ea513c414c175caf91d8039da7bcfac43d5e9992424ea81c5766a2a` |

- t98 的 inScope 契约仍被解析成两个 `.cs` 片段；实际另改 `Bug1GateProbeTests.cs` 与 `Program.cs`。本报告按**实际改动文件**审，不因其未声明就判未改。
- `Program.cs` 在 `:1010-1015` 追加 Bug1-103..108 六行（Bug1-100 为 `:1007` 既有登记行），`T69-01` 行未动；`RunTest("Bug1-` 共 109 条。

## 2. 逐条判定（t96 三条 findings）

### F1（high）EPlayerStance 无 IDLE → idle 格不可达、STAND 冒充 walk — **已关闭，且双向可失败**

- 生产派生：`Bug1GateProbe.cs:405-407`
  ```
  string stanceName = StanceName(target);
  bool moving = IsMoving(target);
  string speedCell = Bug1GateProbeLogic.ClassifyStanceCell(stanceName, moving, out speedReason);
  ```
  `IsMoving`（`:657-668`）读原生 `target.movement.isMoving`（`PlayerMovement.cs:1108` 的公开属性，SDK 已核）；`StanceName`（`:641-651`）仍返回 `stance.stance.ToString()`。
- 归类（`Bug1GateProbeLogic.cs:697-714`）：`SPRINT→sprint`；`STAND+moving→walk`；`STAND+!moving→idle`；`CROUCH/PRONE/SWIM/CLIMB/DRIVING/SITTING→unstable(stance_out_of_scope:* )`；空→`unstable(stance_unreadable)`；其他→`unstable(stance_unknown:*)`。**"IDLE" 分支已删除**，仓库内 `"IDLE"` 仅剩测试断言（`Bug1GateProbeTests.cs:680`，要求判 unstable）。
- 全仓 grep：`ClassifyStanceCell` 调用点仅生产 `Bug1GateProbe.cs:407` 与测试；无任何“STAND 无条件 walk”残留路径；`HeadingAxisRatio`/`HeadingMinComponent` 0 命中（旧 ±33.7° 做法确已消失）。
- 反向要求（“无法判定 → unknown，不得冒充 idle/walk”）：用例 `Test_T95_SixCellClassification`（`Bug1GateProbeTests.cs:670-685`）显式断言 `"IDLE"→unstable` 且 reason 含 `stance_unknown`、`CROUCH→unstable`、空串→`unstable`。**且该断言可失败**（见 §3 变异 B）。
- 自跑变异：MV-A（`STAND` 恒 walk）→ 792/795，唯一新增红 **Bug1-100**；MV-B（`stance_unknown` 分支改返 `idle`）→ 792/795，唯一新增红 **Bug1-100**。两侧均只红对应自建用例。

### F2（medium）样本无 stance/yaw、朝向桶 ±33.7° 宽于 t94 ±5° — **已关闭**

- 判桶（`Bug1GateProbeLogic.cs:645-647`）：`atan2` 实测 yaw 与 `HeadingNominalZDeg=0`/`HeadingNominalXDeg=90` 比较，容差 `HeadingToleranceDeg=5f`（`:120`）；窗外 → `unstable(yaw_outside_nominal)`。旧主轴比 1.5 已删除（grep 0 命中）。
- 样本与产物：`Bug1GateCellSample` 带 `Stance`/`MeasuredYawDeg`；`EvaluateCoverage`（`:828-837`）逐样本累 `Stances` 与 `MinYawDeg/MaxYawDeg`；`BuildCoverageJson` 输出 `stances[]`/`measured_yaw_deg_min|max`。用例 `Test_T98_CoverageJsonRecordsStanceAndYaw`（`Bug1GateProbeTests.cs:967-996`）断言 idle 格 `stances:["STAND"]`、sprint 格 `["SPRINT"]`、yaw 0/90 区间。
- 自跑变异：MV-E（容差 5°→45°，即回退到旧宽窗语义）→ 792/795，唯一新增红 **Bug1-100**（`84° 超出 ±5° 窗必须 unstable` 与 94.5° 边界断言同时失效）。判桶窗口的窄化是可失败的。
- t98 报告称“cells[] 输出残差”——实测 `Bug1GateCellState` 导出 stances/yaw 区间与正证据计数，残差在样本级；不构成 finding（t94 未要求 cells 级残差）。

### F3（low）过期 `single_frame_single_pose` note 与双口径 — **已关闭（附 1 条 low 残留）**

- note 拆分（`Bug1GateProbeLogic.cs:789-796`）：`BuildModeNote(coverageMode, samples, validSamples)`；`coverageMode=false` 才返回 `ModeSingleNote`。
- 生产接线：覆盖收尾 `Bug1GateProbe.cs:710` 写 `BuildModeNote(true, …)`；单次测量路径 `:943` 写 `BuildModeNote(false, 0, 0)`。全仓 `ModeSingleNote` 只在 `BuildModeNote` 内被引用，不再有第二处硬编码单帧 note。
- 用例：`Test_T98_ModeNoteIsSplit`（`Bug1GateProbeTests.cs:949-964`）断言覆盖文案含采样数、不含 `single_frame_single_pose`，并用 IL 断言 `FinishCoverage` 调 `BuildModeNote` 至少 1 次。
- **残留（T102-F1 low）**：该 IL 断言只钉“调用存在”，不钉参数值。MV-D 把 `:710` 的 `true` 改成 `false`（即原缺陷原样回归）→ **793/795 全绿**。当前字节是正确的，但该缺陷的回归不会被任何用例拦截。

## 3. 变异矩阵（本审查官自跑，副本 `D:/Agent-工作目录/DevelopMyUNMultiplayerModAndModloader/_t102iso`，脚本 `D:/tmp/t102_mut.sh`，日志 `D:/tmp/t102-mut.log`）

每条：`-t:Rebuild` 生产 + 重建测试项目 + 覆盖两处 DLL + 跑 exe。

| 变异 | 改动 | 生产 DLL sha256 | 结果 | 红名单 |
|---|---|---|---|---|
| M0 | 无（基线） | `ad1283ac…` | 793/795 | 仅 GBA01/NIL01 |
| A | `STAND` 恒 walk | `cc89de9aee63561d71b469c4570aa19cc3506d3dfaa95dd6bff4d94ed52fd6a8` | 792/795 | Bug1-100 |
| B | `stance_unknown` → idle | `296a9b17a40b14a0a830e7f299f84ce8aeea32a24480c5274a0ff86161e8e033` | 792/795 | Bug1-100 |
| C | `RestoreBitsEqual` 位比较删除（恒 true） | `f5a7b0bc1f5f274774251097416009a8056a412d081bc82250275b5034e704b2` | 792/795 | Bug1-105 |
| D | 覆盖收尾 note 改传 `false` | `9de5a4a91ef1557f7f7ec23d2ae90ac9c396c6eea7a3d600db8ecd65f763819a` | **793/795（全绿）** | — → T102-F1 |
| E | `HeadingToleranceDeg` 5°→45° | `cb4dfdfa32b07bf5637a056b9899648fec263bf28e003d8cdd515d04491be135` | 792/795 | Bug1-100 |
| R0 | 还原 | `ad1283ac…`（逐字节回送审） | 793/795 | — |

- 还原核验：`Bug1GateProbeLogic.cs` 回 `7a0c6d24…`、`Bug1GateProbe.cs` 回 `1d58f950…`，重建 DLL 逐字节回 `ad1283ac…`、793/795。
- 变异 C 同时覆盖 t97 的 R17-03（位级还原门），证明其修复可失败。

## 4. 极性判定（t94 §4.1 vs t95 派工书）

- 实现极性：`EvaluateCoverage:845-855` —— `observed = PoseEqual && InvarianceOk`；`observed && (Torn || !VertexMoved)` → `Counterexamples++` → 整体 `FAIL/POSE_COUNTEREXAMPLE`；`observed && VertexMoved && EpsilonSpecified && MaxResidualU<=epsilonU` → `Positive++`。即“**pose 逐位稳定、物理门未变，但真实蒙皮表面不随动或撕裂 ⇒ FAIL**”。
- 与 t94 §4.1 **一致**；t95 派工书括号（“表面动了才叫反例”）未被采纳，符合 Captain 的裁决要求。
- 阈值接线：`Bug1GateProbe.cs:544-545` `VertexMoved = anyBake && maxMoved >= offset*0.5`（0.10U→0.05U）、`Torn = VertexMoved && minMoved <= offset*0.1`（0.01U），与 t95 报告口径一致，无内插/骨骼旁路。
- 用例侧：`Test_T95_CoverageEvaluatorRules`（`:730-746`）分别构造“稳定但不随动”与“撕裂”两例，均要求 FAIL 且 detail 指出格名——与 t94 极性同向。

## 5. 补偿性判定（范围/口径）

- 探针仍**默认 OFF**：`file.Bind(Section,"Enable", false, …)`（`Bug1GateProbe.cs:125`）；`_enabledEntry.SettingChanged` 仅在开启后 arm（`:85-86`）。
- **无新增生产插值**：`Bug1GateProbe.cs` 内 `LateUpdate`/`Slerp`/`Lerp(` 0 命中；姿态只做位级快照与比较。
- **epsilon 未指定仍只能 UNKNOWN**：`EvaluateCoverage:880-904` —— 有效样本充足但 `Positive==0` 且 `epsilonU<=0` → `UNKNOWN/EPSILON_UNSPECIFIED`；用例 `:753-761` 钉死。六格全正且 epsilon=0 时也**不会** PASS（先落入 `Positive==0` 分支）。
- 判定顺序符合 t94：反例（短路 FAIL）→ 样本不足 → epsilon 未指定 → 无正证据 → PASS；cells 逐格 State 与整体 Status 分离，整体 UNKNOWN 不会被写成通过。

## 6. 残留与移交（不计入本票 findings）

1. **`PoseWalkAllowed` 静默截断（移交 t103 / R17-01）**：`Bug1GateProbeLogic.cs:766-770` 注释写明“超限 → 调用方必须把快照标成不可用”，但生产 `CollectSubtree`（`Bug1GateProbe.cs:1293-1298`）命中上限只 `return`，`SnapshotPose` 不置 `Available=false`，也不写 `missing:*`。若真实角色骨骼节点 > 512（或深度 > 64），姿态集合会被**无声截断**，随后仍按“pose 相等”计入正证据，与 t94 §3“集合缺失 → `UNKNOWN/POSE_UNAVAILABLE`，不是空表相等 PASS”不一致。该点与 t97 的 R17-01 同处，按票面分工交 t103 判，本票不重复计，但**请在 t103 明确结论**。
2. **`ClassifySpeedCell` 已成死代码**（`Bug1GateProbeLogic.cs:662`，全仓无调用）：属工程/风格轴，t103 域，不计。
3. **六格 PASS 生产可达性**：本票只能判“离线语义与接线正确”。真实 `BakeMesh` 顶点、六格能否被正常玩一遍自然采到、骨骼子树在真实物理帧是否逐位不动、`isMoving` 在真实移动中的抖动，均需 1H+2G 实机；**pass ≠ G 门成立 ≠ Bug1 已修复**。
4. **尽力而为的可用性缺口**：`IsMoving`/`StanceName` 异常时分别回落 `false`/空串（`Bug1GateProbe.cs:655/663`）。空串 → `stance_unreadable → unstable`（正确，不冒充）；但 `isMoving` 读取失败会得到 `STAND+false → idle`，即“无法判定移动状态”会被记成 idle。`isMoving` 是公开属性、实际不可读概率极低，故不计 finding；若后续要严格对齐 t94“不可读 → UNKNOWN”，应让 `IsMoving` 返回 `bool?` 并把 null 映射到 `unstable`。

## 7. findings

| id | severity | problem | requiredFix |
|---|---|---|---|
| T102-F1 | low | `Bug1GateProbe.cs:710` 覆盖收尾传 `BuildModeNote(true, …)`；Bug1-107 只用 IL 断言“调用存在”（≥1），不钉参数。MV-D 把参数改成 `false`（t96-F3 原缺陷原样回归）后 793/795 全绿，回归不可被拦截。 | 在 Bug1-107 增加参数级断言（把 note 组装抽成纯函数 `/ 或用可捕获 Notes 的 seam 断言覆盖模式 note 出现且不含 `single_frame_single_pose`），使 MV-D 必然变红。 |

## 8. 验收对照（8 条）

| # | 条目 | 结果 |
|---|---|---|
| 1 | F1：idle/walk 由真实 STAND + 原生 isMoving 派生（代码+用例） | passed（`:405-407`、`:657-668`、`:697-714`；Bug1-100） |
| 2 | F1 反向：无法判定姿态 → unknown，不得冒充 idle/walk，且可失败 | passed（`"IDLE"→unstable` 断言 + MV-B 变红） |
| 3 | F2：样本与 cells 记 stance/yaw，判桶 ±5°，旧 ±33.7° 已消失 | passed（`:645-647`、`:828-837`、Bug1-108、MV-E 变红） |
| 4 | F3：过期 note 消失、按模式拆分、无双口径 | passed（`:789-796`、`:710`、`:943`；残留见 T102-F1） |
| 5 | 极性：按 t94 §4.1 判反例 | passed（`:845-855` 与 t94 一致） |
| 6 | 变异独立性：自跑 MV-A + 至少一条 t97 变异，核 DLL 哈希 | passed（A/B/C/D/E 五条，R0 逐字节还原） |
| 7 | 范围：无新增插值、默认 OFF、epsilon 未指定只能 UNKNOWN、不得写成闸门通过 | passed |
| 8 | 字节绑定：报告写明 sha256 与行数并声明适用范围 | passed（§1） |

## 9. 声明

本判定仅对第 1 节字节有效。核对方式：主树只读 + 仓库外副本 `_t102iso` 变异（已逐字节还原并保留日志 `D:/tmp/t102-mut.log`）；主树源码未改，判定后 `Bug1GateProbe.cs`=`1d58f950…`、`Bug1GateProbeLogic.cs`=`7a0c6d24…`、构建产物 `ad1283ac…`。
