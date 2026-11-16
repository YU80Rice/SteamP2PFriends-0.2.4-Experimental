# T74 规格轴复审 r1：M1-XZ-RELATIVE-v2 是否被忠实实现（被审 t73）

2026-11-07／reviewer-spec（DeepSeek）。只读审查：本报告判定只对 §2 的字节有效；审查期间未改动主树任何生产/测试文件，全部变异在仓库外副本 `_t74iso` 完成并逐字节还原。

## 1. 结论

**verdict = needs_revision（status=failed）**。判定器主体**已按 M1-XZ-RELATIVE-v2 正确落地**：水平投影、C_H 段（≥3 个渲染间隔=4 点）、释放门（`cap×dt+eps` 严格 > 且 ≥20 格=2cm）、落后见证、回补收敛五组谓词都在**生产 `Bug1ProbeJudge`** 里真实生效，且我用两条独立变异证明它们承重（§4）。R03 真实 trace 回放与「原 result.json 不被覆盖」也成立。

不通过的原因是 **R01–R11 未全部按 §5 表格交付**：R07 的输入形状没有按表格构造（累计微动 0.0015U < ε），使该案例要证的「累计跨度大 ≠ 落后证据」没有用例可证伪；另有 AUTH 判定谓词与 §3.4 措辞不一致、R10 只覆盖 7 类 UNKNOWN 中的 4 类。三条都不影响「M1 是否被误判」的正确性，但不是逐字一致，须补一轮（见 §6）。

另：Captain 派工文本中的「806/768 通过」与实测不符——本机实测 **766/768**（失败仅 GBA01/NIL01 环境项）。该数字不作为 Bug1 已诊断的依据。

## 2. 被审字节绑定（2026-11-07 20:30 取样，主树）

| 文件 | 字节 | sha256 |
|---|---|---|
| `Platform/Diagnostics/Patches/Bug1ProbeJudge.cs` | 34531 | `615080f4f132ea964acd30592203d4db5d98ca3717e6b400ba062e7b744723f0` |
| `Platform/Diagnostics/Patches/Bug1ProbeModel.cs` | — | `daf08c53309795d28139d6bd4d1933bc5d6f95c0b5042a0cae6f9f4628f1437f` |
| `Platform/Diagnostics/Patches/Bug1ProbeRuntime.cs` | — | `a66aee9a934190131278bae84557c78a03a2436445114785634b9d1e79876c69` |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeM1RuleTests.cs` | 29713 | `0b4b019179bbfc0ec8177eb75288ed8602e98dc031117d3a7107a78439870749` |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeJudgmentTests.cs` | — | `6ff42723251858d0f9c7164e6828e35667909a2893277cacdd369fd49c805bed` |
| `WhitelistTests/Program.cs` | — | `71d5268ad401ae842ee97c71ba368f590a1554ba86b0f7dae8087c9949095c8e` |
| `SteamP2PFriends.csproj` | — | `ed629ae7064015d55d2e1bee15b01ba1eb6a7a565dcdb80ac36892499350b948`（未改） |
| 交付 DLL（两处 bin 同哈希） | 1502208 | `81da407a7bec87e88e0a642a5b1885e37541c28d7418259fdb897f2212341f27` |
| t73 报告 `bug1-m1-xz-relative-v2.md` | 15823 | `fdda1d86e8082641c881a41d4ba6d1ab8f9c48b318ab22791c4c7e81c1444fd4` |
| 裁决 `architecture-decision-t71-m1-relative-motion.md` | 14081 | `6a91855b1badfc468a9406faa435fa7f5d3151b4817153f2630389f29d46dcee` |

以上八项与 t73 报告 §4 自报逐项一致；`git diff HEAD --numstat` 显示 `Program.cs` **+139/−0**、`csproj` **+15/−0**（纯追加，未删除既有行），无重排。

## 3. 逐项判定

### 3.1 五组谓词是否在生产 Judge 真实生效 —— PASS

全部位于 `Bug1ProbeJudge.cs` 的生产路径 `Judge() → ClassifyHorizontalMotion()`，不是夹具复制品：

1. **水平投影**：`ClassifyHorizontalMotion`（:407-532）全程只用 `VPos.Quantized().Horizontal()` / `APos.Quantized().Horizontal()`；Y 不参与任何候选或见证（`ComputeSimRootSpan3D` 是唯一 3D 计算，仅赋值导出，:520/:601-620）。
2. **C_H 段**：`IsHorizontalSmallStep`（:534-539）逐帧 `HorizontalDistance(V_i,V_{i−1}) ≤ eps`；段枚举 `:416-423` 以 `step = e+1` 前进覆盖全部最大段；`e - s + 1 < MinStallDisplacements` 且常量 `MinStallDisplacements = 3`（`Bug1ProbeModel.cs:117`）——`e−s+1` 是**步数**，故恰为「≥3 个连续渲染帧间隔 = 4 点」，与 §3.2 一致。
3. **释放门**：`boundary = w.NativeSpeedCapUS * dt + eps`（:449），`!(releaseHorizontal > boundary)` 才拒绝（:451）→ 严格 >；2cm 门用整数网格 `HorizontalAtLeastGrids`（`Bug1ProbeModel.cs:92-98`：`gx*gx+gz*gz >= grids*grids`，grids=20）→ 含边界 ≥ 且避免 19.999 格浮点误差；`dt` 取 `(release.MonoTicks − frames[e].MonoTicks)/release.MonoFrequency`（:437-439），未换 RATE/FPS。
4. **落后见证**：`FindLagWitness`（:542-560）遍历 `i∈[a+1,b]`，要求 `HorizontalAtLeastGrids(A_i, A_a, 20)`（=||T_i||≥2cm）、`gapLength > eps`、`HorizontalDot(gap, progress)/progressLength > eps`——三条与 §3.3 逐字对应。
5. **回补收敛**：`converged = (lagBefore − lagAfter) > eps && HorizontalDot(carrierDelta, gapBefore) > 0d`（:471-472）。

**结构性保证（A/V 恒重合不能判 M1）**：见证要求 `gapLength > eps`，A_i=V_i 时 gap=0，见证必假，M1 布尔必假——不依赖 D 取值，与 §3.3「提高/降低 D 都不能改变」一致。R09 另以「SIM 三维跨度 > ε 但 RR 始终 A=V」直接断言非 M1（`Bug1ProbeM1RuleTests.cs:326-345`，其中 `:344` 断言 `SimRootSpan3D > EpsilonU`），正面证伪「SIM 三维触发」路径。

**M1/AUTH 互斥**：`:474-486` 命中 M1 即 `continue`，`:488-500` 才试 AUTH，同段不可能两者（同一 `if/else-if` 链）；R02/R08 断言 `StrictM1Events==0` / `AuthSegments==1` 双向固定。

### 3.2 R01–R11 与 §5 表格对照 —— **部分不通过**

| 规则 | §5 要求 | 实测 | 判定 |
|---|---|---|---|
| R01 | V=[0,0,0,0,.08]、A 逐帧 +.02、dt=1/75、cap5 → M1 一次，见证在停段、释放收敛 | `Bug1-70` PASS：Label=M1、M1=1、LagWitnessFrame=1、[0,3]/4、LagBefore>LagAfter | 一致 |
| R02 | A=V=[0,0,0,0,.08] → AUTH，非 M1 | `Bug1-71` PASS：Auth、M1=0、AuthSegments=1 | 一致 |
| R03 | 本次 raw 回放 → AUTH、strict_m1_events=0、不删原误报 | `Bug1-72` PASS（见 3.3） | 一致 |
| R04 | 仅 Y 运动 0.01/0.08U → 均无 C_H；0.08 例超过旧三维 B | `Bug1-73` PASS：两例 `HorizontalCandidates==0`、UNKNOWN(RIGHT_CENSORED)；混合释放记 `RELEASE_NOT_EXCEEDED@4` 并落 NoThresholdEvent | 一致 |
| R05-a | V 在前 1.0、释放回到 A → 不得 M1，删见证应变红 | `Bug1-74` PASS：M1=0、LagWitnessFrame=−1、`LAG_WITNESS_MISSING@4` | 一致 |
| R05-b | 真实落后但向外突跳 → 不得 M1，删收敛/方向应变红 | `Bug1-75` PASS：M1=0、`ERROR_NOT_CONVERGED@4` | 一致 |
| R06 | 19/20/21 格；D 按 ≥20 格；B 低于 19mm 以免被速度门遮住；B 仍严格 > | `Bug1-76` PASS：cap=0.1 构造 B<19mm，0.019 拒（`RELEASE_BELOW_2CM@4`、候选 0），0.020/0.021 过（候选 1） | 一致 |
| R07 | A/V 同步微动 ≤ε、**累计超 ε** 后同步大步 → 不得 M1 | `Bug1-77` PASS 但输入形状不符：`0.0005×3 = 0.0015U < ε=0.001732U`（`M1RuleTests:279-286`），累计**未**超 ε | **不一致（T74-F1）** |
| R08 | 长 AUTH 段 + 较短真 M1 → 真 M1 不被遮蔽、计数真实 | `Bug1-78` PASS：M1=1、Auth=1、stallSegments=2、代表段 [8,11]/12 | 一致 |
| R09 | 只有 SIM 运动、RR 恒 A=V → 不得 M1，禁跨相位拼见证 | `Bug1-79` PASS：M1=0、LagWitnessFrame=−1、`SimRootSpan3D > ε` 仍不判 M1 | 一致 |
| R10 | 缺 RR/关联/真实端点、相机重复、暂停/姿态、截断 → UNKNOWN 先行 | `Bug1-80` PASS 但只覆盖 缺RR/相机重复/暂停/截断 四类 | **部分（T74-F3）** |
| R11 | 静止 M3 3D 漂移、跳跃/落地 Y 停顿、yaw 359→0 仍走各自判据 | `Bug1-81` PASS：yaw 跨零仍 AUTH、落地垂直不改变 XZ 分类、非有限 yaw→UNKNOWN(NON_FINITE) | 基本一致（未单列「静止 M3 漂移」，由既有 M3 用例承担） |

### 3.3 R03 真实 trace 回放与原 result.json 未被覆盖 —— PASS

- 回放助手 `ReplayCandidateSegment`（`M1RuleTests.cs:430` 起）逐行读 `trace.jsonl` 中 `frame_count ∈ [11562,12216]` 的 `FRAME` 记录，按 `phase` 取 RENDER_READY/BEFORE_UPDATE，`mono_ticks` 取自记录本身，**无相位硬编码、无位置填充**；`ResolveTraceDirectory`（:413-427）先读 `SPF_BUG1_TRACE_JSONL`，再向上找 `启动器/.../bug1-20261107T111121Z/HOST`。
- 本机实测该断言 PASS：`Label=AuthStepLocked`、`StrictM1Events=0`、`AuthSegments=1`、候选 11562/12215/12216、`RenderRootSpanXZ ≤ ε`。
- 原始结果未被覆盖（本审查官直接读文件）：`.../bug1-20261107T111121Z/HOST/result.json` 仍为旧 schema——`"label":"M1StallCatchup"`、`"root_span":0.00999999…`、`"strict_m1_events":1`；`manifest.json` 的 `spf_assembly_sha256=7168ace4…`（旧 DLL）、且**没有** `m1_rule_revision` 字段；`trace.jsonl` sha256=`f0627abd…` 与裁决 §2 记录一致；三个文件 mtime 均为 11-07 19:13（该 run 于 19:11 产生），t73 工作在其后，未写入。
- `Bug1-72` 内还有显式断言：`original.Contains("M1StallCatchup")`（`:166`）。

### 3.4 §3.1 字段导出与三处同源、旧 root_span 已删 —— PASS

- `Bug1Verdict` 新增 `M1RuleRevision`（默认 `Bug1ProbeConstants.M1RuleRevision="M1-XZ-RELATIVE-v2"`）、`MotionAxes`、`HorizontalMotionMinU=0.02`、`RenderRootSpanXZ`、`SimRootSpan3D`、`LagWitnessFrame/Authority/Carrier`、`LagBeforeReleaseU/LagAfterReleaseU`、`StrictM1Events`、`HorizontalStallSegments`、`HorizontalCandidates`、`AuthSegments`、`NonM1Segments`、`CandidateRejections`（`Bug1ProbeJudge.cs:111-130`）。
- **RUN_END**（`Bug1ProbeRuntime.cs:913-928`）写出全部新字段；**result.json**（:1280-1294）同名写出并带 `lag_witness_authority_pos`/`lag_witness_carrier_pos`；**manifest**（:1261-1263）写 `m1_rule_revision`/`motion_axes`/`horizontal_motion_min_u`。三者同源同一个 `verdict` 对象。
- **旧 `root_span` 已彻底移除**：全仓 `grep "root_span"` 在 `Platform/ Core/ WhitelistTests/` 的 `.cs` 中 **0 命中**（仅注释说明「不再写」）；`Bug1Verdict` 无 `RootSpan` 字段。历史 `trace/result.json` 里仍有旧字段，属被保留的原文件（`Runtime.cs:1278-1279` 注释明示不覆盖历史 run）。
- 仍引用 `root_span` 的文件只在 `.scratch/`（t71 裁决、T4 规格、t72 分析脚本/报告）——它们是历史口径的分析物，报告未把它们与 v2 混读；`bug1-m1-xz-relative-v2.md` 明确以 `m1_rule_revision` 标注新口径。

### 3.5 SIM 三维 span 是否还有触发 M1 的路径 —— 无

`SimRootSpan3D` 全仓仅三处：字段声明（Judge:114）、赋值（:520）、计算（:601-620）；无任何比较/分支读它。`RootSpan`/`root_span` 名字在代码里已不存在。R09 以「SIM span > ε 但 RR 恒 A=V」的实跑断言封闭该回归。

### 3.6 UNKNOWN 优先级 —— 成立

`Judge` 顺序：窗口收口门（:200-205）→ `ValidatePhaseCoverage`（:209，缺相位）→ `ValidateContext`（:215；含 `RightCensored`→`RIGHT_CENSORED`、暂停、相机重复、模型端点、速度上限等）→ `ValidateFrames`（:221）→ 分类。分类内部，末段无释放时报 `RIGHT_CENSORED` 并 `Valid=false`（:429-434），不会落到 NoThresholdEvent。即 UNKNOWN 一律先于 NO_THRESHOLD_EVENT；R10 实测四条均 UNKNOWN。关联失败在 `ValidateCandidateAssociations`（:626+，缺 `input_seq`/`rx_id`/`queue_depth` 逐字段写名）——但它排在「无候选→NoThresholdEvent」之后；无候选时不做关联校验，属合理短路。

### 3.7 交付口径诚实性 —— 成立

报告 §8：①明示改变 Judge 生产字节、不适用 TEST_STRENGTHEN_ONLY、须双轴；②「只修判定器，不修角色顿挫」，`strict_m1_events=0` 不说明 Bug1 已修复；③三种标签含义固定；④历史 result.json 保留；⑤自动化只证规则本身，真机 M1 仍需 1H+1G。全文未见「Bug1 已解决/已修复」表述。

## 4. 本审查官实跑变异（副本 `_t74iso`，逐条 `-t:Rebuild` + 重建测试 + 跑 exe）

| 变异 | 判据文件 sha256 / DLL sha256 | 结果 |
|---|---|---|
| M0 基线（送审字节） | Judge `615080f4…` / DLL `81da407a…` | **766/768**，仅 GBA01/NIL01；Bug1 段全 PASS |
| M1 删落后见证（`if (witness >= 0 && converged)` → `if (converged)`） | Judge `7532c438…` / DLL `2bbc2e39…` | **765/768**，唯一新增红 **Bug1-74（R05-a）**：`R05-a 不得判 M1，label=M1StallCatchup` |
| M2 删回补收敛（`converged = true`） | Judge `8688642f…` / DLL `cffcbae3…` | **764/768**，新增红 **Bug1-75（R05-b）** 与 **Bug1-11（Authority Moves And Returns Is Not M1，T71/v2）** |
| R0 还原 | Judge `615080f4…` / DLL `81da407a…` | **766/768**，与送审逐字节一致 |

结论：五组谓词中「落后见证」「回补收敛」经独立证伪；R05-a/R05-b 的失败信息点名具体拒绝原因，可定位。

## 5. 本人独立复跑（主树，非引用 t73）

- `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` → 0 警告 0 错误；DLL `81da407a…` 与送审一致。
- `dotnet build WhitelistTests/… -c Release -t:Rebuild` → 0 错误、4 条既有警告。
- `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` → **766/768**，失败仅 GBA01/NIL01（`TypeInitializationException → SDN.Unturned.SteamPlayer/NetReflection` 环境缺运行时）；`RunTest("Bug1-…")` 共 **82** 条全部 PASS（含 Bug1-70…81 十二条新用例）。

## 6. Findings

### T74-F1（medium）R07 未按 §5 构造「累计超 ε」输入，抗「累计跨度」误判的用例实际缺失
- 位置：`WhitelistTests/Evidence/Runtime/Bug1ProbeM1RuleTests.cs:279-286`
- 事实：微动为 `x = 0.0005 * i`（i=0..3），累计跨度 **0.0015U < ε=0.001732U**；`ε = sqrt(3)×0.001`（`Bug1ProbeModel.cs:113`）。§5 R07 要求「逐帧≤ε、**累计超 ε** 后同步大步」。因累计未越界，`RenderRootSpanXZ <= ε` 的断言只证明了小跨度，而「累计跨度大也不构成落后证据」这一命题没有被任何用例证伪。
- 影响：非 M1 正确性风险（本审查官 M1/M2 变异证明见证与收敛承重；且「按累计 span 判 M1」的回归会被 R05-a 挡住），但 §5 明列的规则用例未逐字交付。
- requiredFix：把微动改为累计 > 3ε 且逐帧 ≤ ε（如 8 帧 × 0.0008U = 5.6mm），断言 `StrictM1Events==0`、`LagWitnessFrame==-1`、存在代表段且 `CandidateRejections` 含 `LAG_WITNESS_MISSING@<r>`、`RenderRootSpanXZ > ε`；并补一条变异（把 M1 判据换成 `RenderRootSpanXZ > ε`）确认 R07 变红。

### T74-F2（low）AUTH 判定用「距段首 ≤ε」而非 §3.4 的「段内最大成对跨度 ≤ε」
- 位置：`Platform/Diagnostics/Patches/Bug1ProbeJudge.cs:566-571`
- 事实：`IsAuthStepLocked` 只检查 `HorizontalDistance(A_i, A_a) <= eps`；§3.4 原文是 `max||A_i − A_j|| ≤ eps`。对称抖动可让段内真实跨度达 ~2ε 仍判 AUTH。
- 影响：只影响 AUTH 与 NON_M1 的标签区分，不会产生 M1 误报；但属对 §3.4 的偏离。
- requiredFix：改为段内 A 的最大成对水平距离（或等价的 `max−min` 逐轴判据）≤ ε。

### T74-F3（low）R10 只覆盖 §5 所列 7 类 UNKNOWN 中的 4 类
- 位置：`WhitelistTests/Evidence/Runtime/Bug1ProbeM1RuleTests.cs:349-375`
- 事实：已覆盖缺 RR、相机重复、暂停、截断；未覆盖「关联失败（缺 input_seq/rx_id/queue_depth）」「真实端点未解析」「姿态/固定条件不符」。代码路径存在（`ValidateCandidateAssociations`、`ValidateContext` 的 `model_endpoint`/`fixed_condition:*`），但没有失败用例将其绑定到 R10。
- requiredFix：在 R10 内追加两条断言：缺 `HasLastInputSeq` → `INPUT_SEQ_MISSING`；`ModelEndpointResolved=false` → `MODEL_ENDPOINT_UNRESOLVED`。

## 7. 判 PASS 的部分（不得因上述 findings 回退）

水平投影与「Y 不参与」、C_H 的 4 点/逐帧 ε 定义、释放门严格 > 与 2cm 网格含边界、落后见证三条件、回补收敛两条件、全部最大段遍历与代表段选择、M1/AUTH 互斥、R03 真实回放、原结果保留、§3.1 字段与三处同源、root_span 删除、SIM 三维去布尔化、UNKNOWN 优先级、交付口径诚实性。

## 8. 边界声明

- 本判定只对 §2 字节有效；字节变更即失效。
- 通过/不通过都**不**构成 Bug1 根因结论：`strict_m1_events=0` 只说明该段几何不构成 M1 证据，不代表角色顿挫已消失，也不代表 0.2.4.11 可出包。
- 真机 M1 是否存在仍须 1H+1G 采集；本票不产出该证据。
- 「806/768」为派工笔误，实测 766/768；Bug1 段共 82 条用例全部 PASS（t73 自述 81 条为计数口径差异，不影响结论）。

## 9. 本报告自绑定

本报告正文（不含本节）16562 字节，sha256 = `7403886408c3bab2461457e04af24601ad8e70ebd8ec1e56203cbe941b75d136`。审查期间主树生产/测试源文件未被我修改：复审结束时 `Bug1ProbeJudge.cs` = `615080f4…`、`bin/Release/SteamP2PFriends.dll` = `81da407a…`（与送审一致）；全部变异在 `_t74iso` 副本完成并已还原。

