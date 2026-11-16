# T72 preview4 HOST trace 独立复核报告

- 复核人：`ds-analyst`；任务：`t72`（attempt 2，attempt_id `4089ad88-bb19-4867-844a-26e250cf8488`）
- 版本语境：基线 0.2.4.10 → 目标 0.2.4.11；本报告只分析 preview4 实机采集包，不涉及源码改动。
- 原则：全部数字由本报告附带脚本从输入文件现算，不采信任何口头转述；不推断根因；不把他人结论当作本报告发现。

## 0. 输入、完整性校验与复现命令

采集目录：`D:\Agent-工作目录\DevelopMyUNMultiplayerModAndModloader\启动器\UnturnedModManager\publish\UMM-v2.2.1-win-x64\bug1-20261107T111121Z`

| 文件 | 相对路径 | 规模 | sha256 |
| --- | --- | --- | --- |
| trace | `HOST\trace.jsonl` | 18417 行 | `f0627abd6e1da009bf30fe49ff91fa0128fa577614e89491afb5db37eb0580f8` |
| result | `HOST\result.json` | — | `1b555eeb401dfe6cba33854e2ba2fc6804907186dfc875eb3d89d90ed20c110c` |
| manifest | `HOST\manifest.json` | — | `2ba6e52d6a037a9840a9fef94c9f799e04c927f54cf7b3e160469e280c8b00cd` |

manifest 关键字段：`role=HOST`、`step_id=step-1`、`spf_assembly_version=0.2.4.11`、`speed_tier=walk`、`epsilon_u=0.0017320508075688772`。

复现命令（在仓库根目录执行；脚本仅用 Python 标准库，Python 3.8+ 即可）：

```bat
cd /d "D:\Agent-工作目录\DevelopMyUNMultiplayerModAndModloader\SteamP2PFriends-0.2.4-Experimental"
python .scratch\takeover-v05-20261106\t72_trace_analysis.py
```

脚本默认读取上表三个绝对路径，也可显式覆盖：

```bat
python .scratch\takeover-v05-20261106\t72_trace_analysis.py ^
  --trace "D:\...\bug1-20261107T111121Z\HOST\trace.jsonl" ^
  --result "D:\...\bug1-20261107T111121Z\HOST\result.json" ^
  --manifest "D:\...\bug1-20261107T111121Z\HOST\manifest.json"
```

输出固定（无时间戳、无随机序），脚本首行即打印三个输入的路径与 sha256。本报告对应的完整输出已保存为 `t72_trace_analysis.out.txt`，sha256 为 `decfcbda5e6979ac6ed9134f8610e275f82bcb6f1fda091326af1e87c5bda2f4`；连续两次运行该哈希一致（确定性已验证）。

## 1. 记录构成与 FRAME 基础计数（脚本 §0–§1）

| kind | 条数 |
| --- | --- |
| `FRAME` | 11473 |
| `RX` | 1736 |
| `CONSUME` | 1735 |
| `SIM` | 3470 |
| `RUN_BEGIN` | 1 |
| `CONTEXT` | 1 |
| `RUN_END` | 1 |
| 合计 | 18417 |

`kind=FRAME` 内部：

| 指标 | 数值 |
| --- | --- |
| 总数 | 11473 |
| `phase=RENDER_READY` | 5737 |
| `phase=BEFORE_UPDATE` | 5736 |
| `valid=true` | 11379 |
| `valid=false` | 94 |
| `valid=false` 的 `invalid_reasons` | `["input_association:no_consume_yet"]` × 94（全部为 `RENDER_READY`） |
| 唯一 `frame_count` 数量 | 5737 |
| `frame_count` 起止范围 | `[11468, 17204]`（范围内无缺号） |
| `(frame_count, phase)` 重复 | 0 |
| `RENDER_READY` 的 `frame_count` 范围 | `[11468, 17204]` |
| `BEFORE_UPDATE` 的 `frame_count` 范围 | `[11469, 17204]`（首帧 11468 只有 RENDER_READY，对应 result 的 `window_head_excluded=1`） |
| `mono_frequency` | 恒为 `10000000`（唯一值） |
| `mono_ticks` 跨度 | 140.264694 s（first `1753467298`，last `3156114243`） |
| 平均帧间隔（仅量级参考） | 24.453 ms ≈ 40.89 fps |

注意：11473 条 FRAME 对应 5737 个 `frame_count`，因为大多数帧同时有 `BEFORE_UPDATE` 与 `RENDER_READY` 两条；`BEFORE_UPDATE` 少 1 条（11468 缺 `BEFORE_UPDATE`）。

## 2. 字段可用性：`model_pos` / `interpolation_unavailable`（脚本 §2）

| 字段 | present | null | absent（缺键） | 可用（非 null 且存在） |
| --- | --- | --- | --- | --- |
| `a_pos` | 5737 | 0 | 5736 | 5737 |
| `v_pos` | 5737 | 0 | 5736 | 5737 |
| `a_pos_before_update` | 5736 | 0 | 5737 | 5736 |
| `authority_pos` | 5737 | 0 | 5736 | 5737 |
| `last_update_pos` | 5737 | 0 | 5736 | 5737 |
| `model_pos` | 5737 | **5737** | 5736 | **0** |
| `lerp_factor` | 5737 | 0 | 5736 | 5737 |
| `lerp_factor_clamped` | 5737 | 0 | 5736 | 5737 |
| `remote_input_tick` | 5737 | 0 | 5736 | 5737 |
| `sample_time_s` | 5737 | 0 | 5736 | 5737 |

（浮点字段均为 3 分量 `[x, y, z]` 数组。）

`interpolation_unavailable` 取值分布：`{"model_pos": 5737}`、`null` 5736 —— 即所有 `RENDER_READY` 帧都声明插值不可用且 `model_pos=null`，`BEFORE_UPDATE` 帧没有该字段。

字段归属：`a_pos`、`v_pos`、`authority_pos`、`last_update_pos`、`lerp_factor`、`lerp_factor_clamped`、`remote_input_tick`、`sample_time_s` **只出现在 `RENDER_READY` 帧**；`BEFORE_UPDATE` 帧只有 `a_pos_before_update`，没有任何其他位置字段。

可用于位置对照的字段：`a_pos`（两 phase 各有其位：`RENDER_READY` 的 `a_pos`、`BEFORE_UPDATE` 的 `a_pos_before_update`）、`v_pos`（仅 `RENDER_READY`）、`authority_pos`、`last_update_pos`（后两者仅 `RENDER_READY`）。**不可用**：`model_pos`（5737 条全 null，另 5736 条缺键，11473 条 FRAME 中无一条含有效值）。

## 3. `v_pos` 与 `a_pos` 的相等性（两个分母，脚本 §3）

相等定义：三维逐分量浮点**完全相等**（`==`，不做容差）；另报 `|Δ|max ≤ 1e-9` 的计数。

| 分母 | 分母条数 | 可比条数 | `v_pos == a_pos` | `|Δ|max` | 不可比 |
| --- | --- | --- | --- | --- | --- |
| ① 仅 `RENDER_READY` FRAME | 5737 | 5737 | **5737 / 5737** | 0 | 0 |
| ② 全部 `valid=true` 的 FRAME | 11379 | 5643 | **5643 / 11379** | 0 | 5736（全部 `BEFORE_UPDATE`，无 `v_pos` 字段） |

两个分母都成立的是「只要 `v_pos` 存在，它就与同帧 `a_pos` 逐位相等」；分母②的 5643 是**可比条数**，不是帧总数。94 条 `valid=false` 的 `RENDER_READY` 帧其 `v_pos` 同样与 `a_pos` 完全相等，但按验收口径不计入分母②（它们 `valid=false`）。

同一 `RENDER_READY` 子集内其他字段与 `a_pos` 的对照（n=5737）：

| 字段 | exact 相等 | 容差 1e-9 内 | `|Δ|max` |
| --- | --- | --- | --- |
| `authority_pos` | 5737 / 5737 | 5737 | 0 |
| `last_update_pos` | 5562 / 5737 | 5562 | 1.907349e-06 |
| `authority_pos` vs `last_update_pos` | 5562 / 5737 | 5562 | 1.907349e-06 |

即 `authority_pos` 与 `a_pos` 逐位相等；`last_update_pos` 有 175 条存在约 1.9e-06（≈1 个 float32 ulp 量级）的差异，属于序列化/浮点回写路径的末位差，不改变「同一位置」的量级判断。

## 4. 权威位置位移与节奏（时间一律用 `mono_ticks / mono_frequency` 换算，脚本 §4）

`RENDER_READY` 的 `a_pos` 序列：5737 个样本、`frame_count` `[11468, 17204]`、时间跨度 140.264694 s。

- 相邻样本位置发生变化的次数（位移步）：**1158** / 5736 个相邻对；完全不变 4578 对。
- 单步位移（仅这 1158 步）：

| 口径 | min | p50 | p90 | max | mean |
| --- | --- | --- | --- | --- | --- |
| 3D 合成 | 2e-06 | 0.360005 | 0.560006 | 0.788937 | 0.391017 |
| 水平 XZ（√(dx²+dz²)） | 0 | 0.359237 | 0.558368 | 0.719245 | 0.387006 |
| 垂直 |Δy| | 2e-06 | 0.037029 | 0.091747 | 0.560001 | 0.044383 |

- 位移步构成：**纯 Y = 87**、**纯 XZ（y 不变）= 0**、混合 = 1071。87 个纯 Y 步中 86 步的 3D 步长 ≤ 1e-04（p50 = 2e-06，浮点噪声级），只有 1 步约 1.0002e-02（0.01U 量级，落在候选窗内，见 §5）。
- 相邻**位移帧**之间的时间间隔（`mono` 换算，n=1157）：min 0.006973 s、**p50 0.080202 s**、p90 0.123106 s、max 10.141954 s、mean 0.110038 s。
- 同一间隔的 `frame_count` 差（仅对照，不用于换算时间）：p50 = 3、p90 = 5、mean = 4.514、max = 654；分布 Top：3 帧 ×423、2 帧 ×302、4 帧 ×227、1 帧 ×83、5 帧 ×79。
- 由「单步位移 / 该步时间间隔」折算的速度：水平 p50 5.001552、p90 10.102711、max 79.843912 u/s；垂直 p50 0.475661、p90 1.426095 u/s。（manifest `speed_tier=walk`；此处仅列数值，是否越界不在本任务结论内。）
- `BEFORE_UPDATE` 的 `a_pos_before_update` 序列（5736 样本、140.217721 s）交叉验证：位移步同为 1158、3D p50/p90 同为 0.360005/0.560006、间隔 p50 0.080129 s、max 10.140197 s（654 帧），与 `RENDER_READY` 序列一致。

## 5. `result.json` 候选窗（`candidate_a=11562` – `candidate_b=12215`）跨度（脚本 §5）

`result.json` 相关字段：`candidate_a=11562`、`candidate_b=12215`、`release_frame=12216`、`root_span=0.00999999999999801`、`epsilon_u=0.0017320508075688772`、`label=M1StallCatchup`、`reason=M1_STALL_CATCHUP`、`n_expected=5736`、`observed=5736`、`strict_m1_events=1`、`window_excluded_reason=WINDOW_HEAD_PARTIAL`。

窗内样本量：`RENDER_READY` 654 条、`BEFORE_UPDATE` 654 条、`SIM` 248 条（`BEFORE` 124 / `AFTER` 124，`frame_count` 范围 11562–12213）。窗口时间跨度 10.115937 s（654 帧）。

窗内各字段跨度（max − min，逐轴）：

| 样本类 | 字段 | n | dx | dy | dz | 对角 |
| --- | --- | --- | --- | --- | --- | --- |
| `RENDER_READY` | `a_pos` | 654 | 0 | 0 | 0 | 0 |
| `RENDER_READY` | `authority_pos` | 654 | 0 | 0 | 0 | 0 |
| `RENDER_READY` | `v_pos` | 654 | 0 | 0 | 0 | 0 |
| `RENDER_READY` | `last_update_pos` | 654 | 0 | 0 | 0 | 0 |
| `SIM` | `a_pos`（BEFORE+AFTER） | 248 | 0 | **0.010002136** | 0 | 0.010002136 |
| `SIM` | `a_pos`（BEFORE） | 124 | 0 | **0.010002136** | 0 | 0.010002136 |
| `SIM` | `a_pos`（AFTER） | 124 | 0 | 0 | 0 | 0 |
| `BEFORE_UPDATE` | `a_pos_before_update` | 654 | 0 | 0 | 0 | 0 |

窗内 Y 取值分布：`SIM AFTER` 恒为 `31.746278763`（124 条）；`SIM BEFORE` 123 条同为 `31.746278763`，仅 1 条为 `31.756280899`（帧 11562 的首条 SIM BEFORE）。成对 `BEFORE→AFTER`：124 对中 123 对位置不变，1 对变化（帧 11562：`y` 31.756280899 → 31.746278762817383，Δy = −0.010002136）。

**`root_span=0.00999` 的归属**：渲染侧（`RENDER_READY` 的 `a_pos`/`authority_pos`/`v_pos`/`last_update_pos`，654 帧）跨度全部为 0，贡献 0；全部非零跨度来自 `SIM` 侧，且仅来自 `candidate_a` 帧 11562 的**首条 SIM BEFORE 样本**，为**纯 Y**、量级 0.01U。组合根位置（`RENDER_READY a_pos ∪ SIM a_pos`，n=902）为 dx=0、dy=0.010002136、dz=0。

需要单独声明：由 trace 已序列化浮点值复算的跨度为 **0.010002136230**，与 `result.json` 的 `root_span=0.00999999999999801` 相差 **2.136e-06**（约 1 个 float32 ulp）。也就是说 `root_span` 的「0.01U 纯 Y」这一结论可复现，但其**精确数值不能由 trace 中已序列化的浮点值 bit 级复现**（差值来源不在本任务范围内，未做归因）。

## 6. `lerp_factor` / `lerp_factor_clamped` / `remote_input_tick`（脚本 §6）

| 指标 | n | min | p50 | p90 | max | mean |
| --- | --- | --- | --- | --- | --- | --- |
| `lerp_factor`（全 trace） | 5737 | 1.995087 | **774.643343** | 1564.095151 | 1755.330125 | 815.447145 |
| `lerp_factor`（候选窗内） | 654 | 19.094658 | 77.416326 | 126.410430 | 145.543674 | 78.145029 |
| `lerp_factor_clamped`（全 trace） | 5737 | 1 | 1 | 1 | 1 | 1 |

- `lerp_factor` 全部 > 1（5737/5737），没有 ==1 或 <1 的样本。
- `lerp_factor_clamped` 唯一取值为 **1**，饱和率 **100%**（5737/5737；窗内 654/654 亦全为 1）。
- 恒等式 `lerp_factor == (sample_time_s - remote_input_tick) / input_rate` 对全部 5737 条精确成立，`|残差|max = 0`；`input_rate` 唯一值 `0.07999999821186066`。
- `remote_input_tick`：首值 = 末值 = `164.09300231933594`，**唯一值 1 个，变化次数 0**；该冻结覆盖 `frame_count [11468, 17204]` 共 5737 帧、140.264694 s，即整段 trace 内它从未刷新。

## 7. 与 Captain 临时数字逐条对照（脚本 §7）

| # | Captain 临时说法 | 本脚本复算 | 判定 |
| --- | --- | --- | --- |
| 1 | FRAME = 11473 | 11473 | 一致 |
| 2 | `RENDER_READY` = 5737 | 5737 | 一致 |
| 3 | `BEFORE_UPDATE` = 5736 | 5736 | 一致 |
| 4 | `valid=true` = 11379 | 11379 | 一致 |
| 5 | `valid=false` = 94 | 94（均为 `input_association:no_consume_yet`） | 一致 |
| 6 | `v_pos == a_pos` 分母① = 5737/5737 | 5737/5737（exact，`|Δ|max=0`） | 一致 |
| 7 | `v_pos == a_pos` 分母② = 5737/5737（称"全部 valid"） | **5643 / 11379**（可比 5643，全部相等；5736 条 `BEFORE_UPDATE` 无 `v_pos` 字段） | **不一致** |
| 8 | 单步位移 p50/p90 = 0.36 / 0.56 | 3D：0.360005 / 0.560006；水平 XZ：0.359237 / 0.558368；垂直 |Δy|：0.037029 / 0.091747 | 一致，但**口径需限定为 3D 合成**；未声明维度会掩盖 Y 分量小一个数量级 |
| 9 | 相邻位移帧间隔"3–4 帧" | `frame_count` 差 p50=3、p90=5、mean=4.514、max=654；时间间隔 p50=0.080202 s | 基本一致（仅 p50 落在 3–4；p90 与 max 明显更大） |
| 10 | p50 = 774.6 | `lerp_factor` p50 = 774.643343；相邻位移帧的**时间**间隔 p50 = 0.080202 s | 数值一致，但该值是 `lerp_factor` **不是时间**；把它当时间/位移节奏读会错 |
| 11 | 候选窗 root_span ≈ 0.01U 纯 Y | SIM dx=0、dy=0.010002136、dz=0；渲染侧全 0；贡献者为帧 11562 首条 SIM BEFORE 样本；trace 复算 0.010002136230 与 result 0.00999999999999801 差 2.136e-06 | **部分一致**：轴向（纯 Y）、量级（0.01U）正确；"0.01U"不能 bit 级复现，且贡献者不是渲染样本 |

**明确的不一致项与正确版本**：

1. **分母②**：Captain 的"5737/5737"只在分母①（仅 `RENDER_READY`）成立；分母②（全部 `valid=true`）的正确写法是 **5643/11379 可比、5643 全部相等**，其余 5736 条 `BEFORE_UPDATE` 没有 `v_pos` 字段，不能计入分子或"相等"。本报告 §3 的两栏数字为准。
2. **p50 774.6 的语义**：数值本身正确（`lerp_factor` p50=774.643343），但不能表述为时间或"位移间隔"；时间口径的 p50 是 0.080202 s（`mono` 换算）。本报告 §4/§6 为准。
3. **"0.01U 纯 Y" 的精确值与来源**：方向与量级正确；精确值在 trace 侧为 0.010002136230（与 result 的 0.00999999999999801 差 2.136e-06，不可 bit 级复现），且该跨度由 SIM 首条 BEFORE 样本贡献、渲染侧贡献为 0。本报告 §5 为准。
4. **0.36/0.56 的维度**：作为 3D 合成值一致；若被引用为"水平位移"则与 XZ 口径（0.359237/0.558368）有末位差异、且会漏掉 Y 分量（0.037029/0.091747）。本报告 §4 为准。

其余（11473 / 5737 / 5736 / 11379 / 94 / 分母① / "3–4 帧"的量级）与 trace 复算一致。

## 8. 结论（只写数据支持的事实）

### 8.1 已确认（可由脚本从 trace/result/manifest 直接复算）

1. trace 共 18417 行：`FRAME` 11473、`RX` 1736、`CONSUME` 1735、`SIM` 3470，另有 `RUN_BEGIN`/`CONTEXT`/`RUN_END` 各 1。`http` 之外的 11473 条 FRAME 由 5737 条 `RENDER_READY` 与 5736 条 `BEFORE_UPDATE` 组成；`valid=true` 11379、`valid=false` 94（全部 `RENDER_READY`，原因 `input_association:no_consume_yet`）；唯一 `frame_count` 5737 个，范围 `[11468, 17204]` 且无缺号；`mono_frequency` 恒为 1e7，覆盖 140.264694 s。
2. `model_pos` 在全部 11473 条 FRAME 中**不可用**（5737 条显式为 `null`，5736 条缺键）；`interpolation_unavailable` 只取 `"model_pos"` 或 `null` 两种值，其中 `"model_pos"` 恰为全部 `RENDER_READY` 帧。位置对照只能用 `a_pos` / `v_pos` / `authority_pos` / `last_update_pos`（后三者只在 `RENDER_READY` 存在），`BEFORE_UPDATE` 侧只能用 `a_pos_before_update`。
3. 凡 `v_pos` 存在处，`v_pos` 与同帧 `a_pos` **逐分量完全相等**：分母① 5737/5737；分母② 5643/11379（可比 5643 全相等）。`authority_pos` 与 `a_pos` 5737/5737 完全相等；`last_update_pos` 5562/5737 完全相等，其余 175 条差 ≤ 1.907349e-06。
4. 权威位置在 140.26 s 内出现 **1158 次**位置变化（5736 个相邻对中）；位移步 3D p50/p90 = 0.360005/0.560006，水平 XZ p50/p90 = 0.359237/0.558368，垂直 |Δy| p50/p90 = 0.037029/0.091747。位移步构成中纯 Y 87 次（86 次为 ≤1e-04 的浮点噪声级）、纯 XZ 0 次、混合 1071 次。
5. 相邻位移帧的时间间隔（`mono` 换算）：p50 0.080202 s、p90 0.123106 s、max 10.141954 s；同一间隔的 `frame_count` 差 p50=3、p90=5、max=654。按"步长/间隔"折算的速度中位数为水平 5.001552 u/s、垂直 0.475661 u/s。
6. 候选窗 11562–12215（654 帧 / 10.115937 s）内，`RENDER_READY` 的 `a_pos`/`authority_pos`/`v_pos`/`last_update_pos` 跨度**全部为 0**（654 帧位置完全冻结）；窗内唯一的非零根位置跨度来自 `SIM` 侧（dx=0、dy=0.010002136、dz=0），具体是帧 11562 的首条 SIM `BEFORE` 样本（Y 31.756280899）与其余 123 条（Y 31.746278763）之差；渲染侧贡献为 0。`result.json` 的 `root_span`（0.00999999999999801）与 trace 复算值（0.010002136230）相差 2.136e-06。
7. `lerp_factor` p50 = 774.643343（全 trace）、`lerp_factor_clamped` 恒为 1（饱和率 100%）；`lerp_factor` 与 `(sample_time_s - remote_input_tick)/input_rate` 在全部 5737 条上精确相等；`remote_input_tick` 首值=末值=`164.09300231933594`，整段 140.264694 s（5737 帧）内变化 0 次。

### 8.2 数据不足以下结论（本任务范围内不做推断）

1. **根因与机制**：trace 只记录位置/因子/计数，不含代码路径、网络包内容或锁/线程信息；「为什么 `remote_input_tick` 全程不刷新」「为什么 `lerp_factor_clamped` 全程饱和」「为什么渲染侧位置冻结 10 s」均无法仅凭 trace 判定，本报告不给出根因。
2. **`remote_input_tick` 冻结的因果**：只能确认"整段未变"，不能判断是发送端未更新、接收端未应用、还是采样点本身如此。
3. **`root_span` 精确值与 trace 复算值 2.136e-06 差异的来源**：可能是 result 侧用原始 double、trace 序列化为 float32，或计算顺序不同；未做代码核对，不做归因。
4. **位移是否"异常"**：本报告只给数值（含按间隔折算的速度，水平 max 79.84 u/s）；是否超出预期、walk 上限 4.5 是否被违反，需要游戏侧语义与设计口径，不能仅凭 trace 断言。
5. **候选窗内渲染冻结与 SIM 单次 0.01U 变化之间的因果/时序关系**：窗内可确认"渲染 654 帧不动、SIM 侧仅 1 个 BEFORE 样本不同"，但不能据此推断哪一侧先发生、谁是因。
6. **帧率结论**：平均帧间隔 24.453 ms（≈40.89 fps）只是全 trace 的平均量级，不能说明任意窗口的帧率或卡顿分布。
7. **`last_update_pos` 175 条 1.9e-06 差异的含义**：只知末位差，不能判断是更新时序还是浮点路径导致。

## 附录：交付物与复现

| 文件 | 说明 |
| --- | --- |
| `.scratch/takeover-v05-20261106/t72_trace_analysis.py` | 分析脚本（纯标准库，固定输出，含输入路径与 sha256，分位数线性插值） |
| `.scratch/takeover-v05-20261106/t72_trace_analysis.out.txt` | 本报告全部数字对应的完整输出；sha256 `decfcbda5e6979ac6ed9134f8610e275f82bcb6f1fda091326af1e87c5bda2f4` |
| `.scratch/takeover-v05-20261106/T72-preview4-trace-复核报告.md` | 本报告 |

复现：`python .scratch\takeover-v05-20261106\t72_trace_analysis.py`（详见 §0）。脚本输出中的 §7 与本报告 §7 一一对应；若输入文件 sha256 变化，脚本首部会打印新的哈希，此时本报告数字需重算。
