# 规格轴复审 t62（被审 t60）：窗口收口 / 裁头尾语义 / 输入关联 / 插值字段

- 审查者：reviewer-spec（规格轴，独立实例）
- 被审：t60（attempt 2，`e48bef2a`；ds-probe2）
- 审查时间：2026-11-07 13:17–13:45
- **判定：needs_revision**（status=failed）。3 条 findings：F1 high（裁尾无来源判据，会把握手侧中途全灭静默判成 `NO_THRESHOLD_EVENT`）、F2 medium（边界收口接线零用例，删除订阅后 744/746 全绿，且报告 §9 称"离线用例验证了接线"与事实不符）、F3 low（`phase_missing_out_of_window` 为结构性常量 0，非测量值）。
- 边界：本判定只对 §2 的字节有效；pass 亦不等于 Bug1 根因已定位或 0.2.4.11 可出包（§7）。

---

## 1. 结论摘要

| 验收项 | 判定 | 依据 |
|---|---|---|
| ① 三处收口都在，且后于最后一帧记录、在 Judge 之前 | **pass（代码）/ 见 F2（无用例）** | `Bug1ProbeRuntime.cs:842-877`（EndStep：`Finalize` → 写窗口统计 → `Judge`）；`:1197-1204` session_reset；`:384-393` target_left；`:395-402` level_changed |
| ② `head/tail/genuine` 三数同源、不掩盖系统性 detour 全灭 | **fail** | 三数同源 ✓（`Finalize` 计算 → `_window` → `verdict` → RUN_END/result.json，`:850-856`、`:883-888`、`:1158-1163`）；但"全灭"保护只覆盖**零齐全帧**，**中途 BU 相位全灭**会被裁成尾部并得到 `NO_THRESHOLD_EVENT`（见 F1） |
| ③ `PhaseMissingGenuine` / `PhaseMissingOutOfWindow` 互斥且不可顶替 | **pass（带 F3）** | `Bug1ProbeJudge.cs:292-293` 仅窗内帧计数；`:141` `PhaseMissingOutOfWindow = 0`；裁剪发生在判定前 |
| ④ 四类原因码足以解释 113/6338 失配，`last_consume_before_render` 被 trace 支持 | **pass** | 分类函数 `Bug1ProbeWindowBoundary.cs:236-243`；真实 trace 抽样 6225/6225 关联成立、113 条无前序消费（§5） |
| ⑤ 插值字段足以区分「无插值」与「插值异常」，缺失进 `interpolation_unavailable` | **pass** | `Bug1ProbeWindowBoundary.cs:96-165`、`Bug1ProbeRuntime.cs` `InterpolationJson`（缺字段写 `null` + 数组，不填默认值） |
| ⑥ 自跑 ≥3 条变异并逐字节还原 | **pass** | 5 条变异（含 1 条审查官加测），§4；还原后 DLL `d6da5835…`、744/746 |
| ⑦ 判定阈值/标签/候选逻辑零改动 | **pass** | `Bug1ProbeJudge.cs` 与 t54 字节 `5d863441…` 的 diff 仅有新增字段与 genuine 计数（§3.7） |
| ⑧ 真实 trace 复核报告结论 | **pass** | 本机复现 `recorded=6338 run_end_frame=14459 raw=Unknown/MISSING_PHASE raw_tail_missing=200 sealed=NoThresholdEvent/NO_CANDIDATE_RUN head_excluded=1 tail_excluded=1043 genuine=0`（§5） |
| ⑨ pass 边界与产物字节绑定 | **pass** | §7、§2 |

---

## 2. 被审字节绑定（主树实测，2026-11-07 13:20 取样）

| 文件 | 字节 | 行 | sha256 |
|---|---:|---:|---|
| `Platform/Diagnostics/Patches/Bug1ProbeWindowBoundary.cs`（新） | 8932 | 195 | `4217568ad47feca65a1aa582aa09d6d733cba110cf95da46b0872b160f87102f` |
| `Platform/Diagnostics/Patches/Bug1ProbeJudge.cs` | 23582 | 514 | `7228213064b9272383c403e28dd99d507c8d075154825bc7d68609126081028a` |
| `Platform/Diagnostics/Patches/Bug1ProbeRuntime.cs` | 62778 | 1204 | `e6d3bc265c7422a4152f62054fad58264f94957d5de6bd6c0ebacd70e177b2f4` |
| `SteamP2PFriends.csproj` | 27575 | 409 | `ed629ae7064015d55d2e1bee15b01ba1eb6a7a565dcdb80ac36892499350b948` |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeT60Tests.cs`（新） | 19923 | 346 | `8df9ff4d29d345cb3ee59b99671eb4407164731df814e74de5516f35c88f7229` |
| `WhitelistTests/Program.cs` | 142682 | 1061 | `b9933c0b3ae784f02bda1b6b383aced017b9e0d476aad793fc868f36a1fd7018` |
| `bin/Release/SteamP2PFriends.dll` | 1491456 | — | `d6da583532deaf45019af455c51a3b9b144dde962beff3a4726736fc0780d7dc` |

与 t60 报告 §8 逐项一致。**未动**（与 t54 送审字节对比）：
`Bug1ProbeControl.cs 53a6c36b`、`Bug1ProbeEntryLogic.cs 04a4beeb`、`Bug1ProbePatch.cs 22308d77`、`Bug1ProbeRxPhases.cs 8d0d36fc`、
`Bug1ProbeNativeSpeed.cs 109e283e`；Bug3 五文件 `a89f40b9 / 7a82de8e / 3d5044f7 / a4553385 / 5b0c4e9b`；
`csproj` 相对 t54 只多一行 `Compile Include="...Bug1ProbeWindowBoundary.cs"`；`Program.cs` 纯追加（`diff` 无 `-` 行）；
`find Release publishi -newermt "2026-11-07 12:00"` 无输出。

---

## 3. 逐条核对

### 3.1 三处收口

| 收口点 | 代码 | 落到 | 时序 |
|---|---|---|---|
| session_reset | `Bug1ProbeRuntime.cs:1197-1204` `OnSessionReset()` → `Disable("session_reset")` | `Disable → EndStep` | `_sessionEpoch++` 后立即收 |
| 目标离场 | `:384-393` `OnPlayerDestroyedBoundary(Player)`：`Enabled/_window` 判空 → `ReferenceEquals(player,_target)` → `Disable("target_left")` | 同上 | 销毁回调即收 |
| 关卡切换 | `:395-402` `OnLevelLoadedBoundary(int)` → `Disable("level_changed")` | 同上 | 下一次 `onLevelLoaded` 收上一窗（SDK 无 onLevelUnloaded，合理） |

订阅/退订：`:370-382`（`HookBoundaryEvents`/`UnhookBoundaryEvents`，各自 try/catch，失败记 `BOUNDARY_HOOK_FAILED:*`），
仅在 `!__Offline` 时挂载（`:153`、`Disable` 内 `:225`）。目标判等用 `ReferenceEquals(player,_target)`，不会被同 SteamID 的其它对象误触。

**时序核验**：`EndStep` 内 `Finalize(_window)` 在 `Record("CONTEXT"…)` 之前、`Judge(_window)` 之前（`:850-877`），
即「最后一帧记录（FRAME 行）→ CONTEXT → RUN_END」三者顺序中，裁剪发生在 CONTEXT/RUN_END 写入之前、判定之前；
`_window.Frames` 是裁剪后对象，故 `verdict.NObserved` 也是裁剪后的值。**顺序正确**。

但**没有任何用例证明这两条订阅真的挂上了**（F2）：全仓 grep `HookBoundaryEvents|onPlayerDestroyed|target_left|level_changed`
在 `WhitelistTests/` 下 0 命中（只有 DLL 二进制命中）；`Bug1-55..61` 无一条触发边界回调。

### 3.2 裁头/裁尾与"是否掩盖全灭"

实现（`Bug1ProbeWindowBoundary.cs:39-97`）：
- 找首个/末个 `HasBeforeUpdate && HasRenderReady` 帧；裁掉两端、统计 `HeadExcluded/TailExcluded`；
- 中间半帧只进 `InteriorPartial`，**不裁**（保留给 Judge 报缺）；
- **零齐全帧 → 不裁剪**（`:60-65` `firstComplete < 0` 提前返回），交给 Judge 报 `MISSING_PHASE`；
- 同步 `StepFirstFrame/StepLastFrame`；`ExcludedReason` 只有 `WINDOW_HEAD_PARTIAL`/`WINDOW_TAIL_PARTIAL`（可拼接）。

三数同源已核实：`Finalize` → `_window.WindowHeadExcluded/TailExcluded/InteriorPartial`（`:850-856`）
→ `verdict.WindowHeadExcluded/…`（`Bug1ProbeJudge.cs:137-140`）→ RUN_END（`:883-888`）与 `result.json`（`:1158-1163`）同一对象。
`PhaseMissingGenuine` 由 Judge 在窗内帧上累加（`Judge.cs:292-293`），与 head/tail 是不同量纲但同一窗口，不会出现
「裁掉的帧又被算 genuine」：raw 路径（不裁剪）才会两者都出现（Bug1-61 的 `raw` 分支正好演示）。

**问题在"是否掩盖系统性 detour 全灭"**：`Finalize` 只保护"零齐全帧"。若 **BEFORE_UPDATE 侧中途全灭**
（BU detour 被别的插件卸掉、`OnBeforeUpdate` 因 `movement` 不匹配持续早退、`TryBindTarget` 长期失败等），
而 RENDER_READY（相机订阅）继续每帧建帧，则尾部全部是 `RR 有 / BU 无` 的帧 —— 与真实 trace 里目标销毁后的尾部**同一形态**，
会被整段裁进 `TailExcluded`，Judge 只在残留前缀上判定，通常得到 `NoThresholdEvent / NO_CANDIDATE_RUN`。
修前同一份数据给的是 `UNKNOWN / MISSING_PHASE`（响亮的采集失败），修后变成自信的"没有停顿"——
这是本票引入的**语义回归**，不是纯增量。详见 F1。

### 3.3 genuine / out_of_window 互斥

- `OutOfWindow` 在 Judge 中恒 0（`Judge.cs:141`），因为越界帧在判定前已被移除；`Genuine` 只对窗内帧累加。
- 互斥成立（同一帧不可能既被裁又计数）。但 `out_of_window` 是**结构性常量**而非测量值：若将来有调用方绕过 `Finalize` 直接 Judge
  （当前只有测试这么做），越界缺相位会全部落到 `Genuine`，两字段无法区分。见 F3（low）。

### 3.4 输入关联（113/6338）

- 规则 `last_consume_before_render`：`OnCameraPreCull` 取 `_rx.Last`（最近一次 AFTER_DEQUEUE）填 `rx_id/inputseq/queue_depth`（`:650-663`）；
  `Bug1InputAssociation.Classify`（`Bug1ProbeWindowBoundary.cs:236-243`）给出
  `no_consume_yet / last_consume_missing_inputseq / last_consume_missing_rx_id / last_consume_missing_queue_depth` 四码；
  `JsonFragment`（`:245-262`）写规则名、原因码、候选数组；`invalid_reasons` 由 `input_association:<码>` 构成（`:679`）。
- **真实 trace 抽样复核（本审查官实跑，§5）**：6338 条 RENDER_READY 中，6225 条的 `rx_id/inputseq` 与"该事件之前最近一条 AFTER_DEQUEUE"完全一致
  （0 条错配），113 条之前没有任何 AFTER_DEQUEUE（即 `no_consume_yet`），恰好等于首个消费帧 7191 之前的 113 帧（7078..7190）。
  → 规则与计数都被数据支持，报告"暖机段"的解释成立。
- 四类码覆盖度：`hasLastConsume` 假、以及 last 记录三字段缺失的全部组合，均能落到具体码，`Classify` 返回 null 仅表示关联成立。**充分**。

### 3.5 插值字段

`Bug1InterpolationSample`（`Bug1ProbeWindowBoundary.cs:96-165`）+ `SampleInterpolation`（`Bug1ProbeRuntime.cs:684-740`）+
`InterpolationJson`：8 个字段全部写进 RENDER_READY；任一不可得 → 对应 `Has* = false`、进 `interpolation_unavailable`、JSON 写 `null`。

用于区分两类情形的能力（下一次采集）：
- **无插值（模型=权威）**：`lerp_factor_clamped == 1`（tick 陈旧）+ `model_pos ≈ authority_pos`；
- **插值正常**：`0 < lerp < 1` 且 `model_pos ≈ Lerp(last_update_pos, authority_pos, lerp)`；
- **插值异常**：`0 < lerp < 1` 而 `model_pos` 明显偏离上式；
- 缺字段时三个 `null` + `interpolation_unavailable`，不会被误当"无插值"。
`lerp = (sample_time_s - remote_input_tick)/input_rate` 与 SDK `PlayerMovement.cs:1746` 的
`(Time.realtimeSinceStartup - input.tick)/PlayerInput.RATE` 同式，`rate>0` 才置有效（`:118-121`）。**判据充分**。

### 3.6 判定语义零改动

`diff -u _t55iso/.../Bug1ProbeJudge.cs`（t54 字节 `5d863441…`）→ 差异仅：
`Bug1StepWindow` 新增 4 字段、`Bug1Verdict` 新增 6 字段、`Judge` 开头抄写 4 个窗口字段并置 `OutOfWindow=0`、
`ValidateFrames` 两行改为 `PhaseMissingGenuine++` 后再 `MarkUnknown`。**阈值常量、`FindLargestStallCandidate`、标签集合、
`NO_THRESHOLD_EVENT` 判据一处未改**（grep `EpsilonU/MinStall/NO_THRESHOLD` 与旧文件逐行一致）。

### 3.7 用例覆盖（Bug1-55..61）

七条用例逐条核对：`Bug1-55` 裁头尾+StepFrame 同步；`Bug1-56` 零齐全帧不裁、`Genuine==4` 精确；`Bug1-57` 内部半帧
`InteriorPartial==2`、`Genuine==2`、`OutOfWindow==0`；`Bug1-58` lerp 计算 + 缺字段不可用 + rate<=0；
`Bug1-59` 四码 + JSON 形态；`Bug1-60` 生产 `Enable→帧→EndStep`，RUN_END 与 result.json 同源 6 字段 + `Finalize` IL 断言；
`Bug1-61` 真实 trace 前后对比。**共同缺口：没有一条覆盖 `HookBoundaryEvents` 及其两个回调**（F2）。

---

## 4. 变异矩阵（本审查官实跑）

环境：仓库外副本 `_t62mut`（tar 复制主树，排除 .git/bin/obj/.scratch/Release/publishi；三个被改动文件哈希与主树一致）。
脚本 `D:/tmp/t62mut.py`：备份 → 每个变异先恢复原始字节 → 替换 → `dotnet build -c Release -t:Rebuild` →
重建测试项目 → 覆盖两处 DLL → 跑 exe → 记录哈希与红名单。日志 `/tmp/t62-<tag>.log`。

| # | 变异 | 变异后 DLL sha256 | 总数 | 红名单 |
|---|---|---|---|---|
| M0 | 基线（无变异） | `d6da583532deaf45019af455c51a3b9b144dde962beff3a4726736fc0780d7dc` | 744/746 | 仅 GBA01、NIL01（环境） |
| M-A | `EndStep` 的 `Finalize(_window)` → `new Bug1WindowBounds()` | `9faa7690236dc9d3c3bfe0c8507ac593e37e273841c334e5d1c8313c2e704951` | 743/746 | **Bug1-60** |
| M-B | Judge 删掉 BU 分支的 `PhaseMissingGenuine++` | `864c09d677c0add24f9b6b1f0cdbb4c1f0cd5d2ecab1491d8d45e0b6fc2c84b1` | 742/746 | **Bug1-56、Bug1-57** |
| M-C | `Classify` 的 `no_consume_yet` → `return null` | `97a04b136d3819dae6e0eea973f612b2357eb8f918e7d283da25f8b705e2091f` | 743/746 | **Bug1-59** |
| M-D | 插值 `if (HasTick && HasSampleTime && HasRate)` → `if (true)` | `9540ac41c6f9053ae94ccba564d71422c597582e35b38db04c108b678d945657` | 743/746 | **Bug1-58** |
| **M-E（审查官加测）** | 删掉 `Player.onPlayerDestroyed += OnPlayerDestroyedBoundary;` | `bea6a54c09e01f00…` | **744/746** | **无（全绿）** |

- M-A/M-C/M-D 与 t60 自报的 DLL 哈希**逐字节相同**；M-B 因我的替换文本与 t60 略不同（保留花括号）而哈希不同，红名单一致（Bug1-56/57）。
- **M-E 是本票的关键反证**（F2）：删掉"目标离场即收口"这条订阅后，全量 744/746、Bug1 段 61/61 仍全绿。
- 还原：三个源文件哈希回 `e6d3bc26…`/`72282130…`/`4217568a…`，重建后 DLL 回 `d6da5835…`，744/746。主树自始至终未改。

---

## 5. 真实 trace 复核（本机实跑）

命令：`_t62mut` 内 `WhitelistTests\bin\Release\SteamP2PFriends.WhitelistTests.exe`，用例 Bug1-61 自动定位
`.../spf-bug1/bug1-20261107T044218Z/HOST/trace.jsonl`，输出：

```
[T60] replay trace=...\bug1-20261107T044218Z\HOST\trace.jsonl recorded=6338 run_end_frame=14459
      raw=Unknown/MISSING_PHASE raw_tail_missing=200
      sealed=NoThresholdEvent/NO_CANDIDATE_RUN head_excluded=1 tail_excluded=1043 genuine=0
```

→ `head_excluded=1 / tail_excluded=1043 / genuine=0`、raw→sealed 的标签翻转**完全可复现**，与 t60 报告 §4 一致。

独立的关联抽样（`D:/tmp/t62trace.py`，直接解析 trace）：

```
FRAME/RENDER_READY=6338  CONSUME/AFTER_DEQUEUE=1699
assoc ok=6225  bad=0  no_consume=113
null_reasons: {None: 113}   first dq event_seq=231
```

即：113 条失配帧在首个 AFTER_DEQUEUE 之前，规则 `last_consume_before_render` 对余下 6225 条 100% 命中，无一条错配。

---

## 6. Findings

### F1（high）：裁尾没有"来源判据"，BU 侧中途全灭会被静默判成 `NO_THRESHOLD_EVENT`

- 位置：`Platform/Diagnostics/Patches/Bug1ProbeWindowBoundary.cs:60-96`（裁剪决策）；`Bug1ProbeRuntime.cs:850-877`（EndStep 直接采用未经解释的裁剪结果）。
- 现象：`Finalize` 只看"帧是否齐全"，不看"为什么不齐全"。凡尾部出现 `HasRenderReady=true && HasBeforeUpdate=false`，
  一律算 `tail_excluded` 并移除。若 BU 侧 detour 在中途失效（被其他插件卸载 patch、`OnBeforeUpdate` 因 movement 不匹配持续早退、
  绑定长期失败），尾部的这批帧正是"采集坏了"的证据，却被裁掉；Judge 在残留前缀上给出 `NoThresholdEvent / NO_CANDIDATE_RUN`。
  修前同样的数据是 `Unknown / MISSING_PHASE`（响亮的失败），修后变成自信的"无停顿"——**本票引入的语义回归**。
- 现有防护只覆盖零齐全帧（`:60-65`），不覆盖"中途停止"；边界钩子降低但不消除概率（钩子失败只写 `LastError`，不去判定）。
- 现存可辨证据：`window_tail_excluded` 数值 + `end_reason`（`target_left`/`level_changed` vs `session_reset`）。
  分析者事后能看出异常，但**标签本身是错的**，下游按 label 取结论就会漏判。
- requiredFix（最小）：`EndStep` 在 `Finalize` 后判断——若 `TailExcluded > 0` 且本次收口**不是**由边界事件解释
  （`end_reason ∉ {target_left, level_changed}`），写入 `window_tail_unexplained=true` 并让 `Judge` 记
  `MarkUnknown("window_tail", "TAIL_PARTIAL_UNEXPLAINED")`（或至少引入独立 reason 码，使其不可能与 `NO_THRESHOLD_EVENT` 混淆）；
  配套用例：构造"尾部 RR-only、收口原因 session_reset"的窗口，断言 label 不为 `NoThresholdEvent`。

### F2（medium）：边界收口接线零用例覆盖，且报告 §9 的覆盖声明与事实不符

- 位置：`Bug1ProbeRuntime.cs:370-402`（`HookBoundaryEvents` / 两个回调）；`WhitelistTests/Evidence/Runtime/Bug1ProbeT60Tests.cs`（无相关用例）。
- 证据：M-E 删掉 `Player.onPlayerDestroyed += OnPlayerDestroyedBoundary;` → **744/746 全绿、Bug1 段 61/61**；
  `grep -rn "onPlayerDestroyed|target_left|level_changed|HookBoundaryEvents" WhitelistTests/` 0 命中（仅 DLL 二进制）。
- 而 t60 报告 §9 第 2 条写「Player.onPlayerDestroyed / Level.onLevelLoaded 在真实关卡时序下是否在窗口内命中
  （**离线用例只验证了接线与判定函数**）」——后半句不成立：离线用例只验证了 `Finalize` 与 Judge，从未触达订阅。
  这是本轮"自述 > 可判性"的典型形态，也正是 t60 要修的那个缺陷（目标离场未收口）的回归保护缺口。
- requiredFix：①加一条用例——离线 seam 下调用 `Enable` 后通过反射/内部 seam 触发 `OnPlayerDestroyedBoundary(_target)`
  与 `OnLevelLoadedBoundary(n)`，断言窗口收口、`end_reason` 分别为 `target_left`/`level_changed`、`TailExcluded` 被计入；
  或至少 IL 断言 `Enable` 路径调用 `HookBoundaryEvents`、`Disable` 调用 `UnhookBoundaryEvents`。②更正报告 §9 措辞。

### F3（low）：`phase_missing_out_of_window` 是结构性常量 0，不是测量值

- 位置：`Bug1ProbeJudge.cs:141` `verdict.PhaseMissingOutOfWindow = 0;`。
- 现象：该字段在生产路径上永远为 0（因为裁剪在判定前），语义上等价于"由构造保证"。一旦有调用方绕过 `Finalize` 直接 `Judge`
  （当前仅测试的 raw 分支），越界缺相位全部计入 `Genuine`，两字段无法区分；字段名容易被读成"越界缺相位=0（已测）"。
- requiredFix（择一）：在 `Judge` 内按 `window.StepFirstFrame/StepLastFrame` 重算越界缺相位（与 genuine 互斥且可自证），
  或在文档/字段注释中把它明确标注为"结构性常量，越界量见 window_head_excluded/window_tail_excluded"。

---

## 7. 边界与归属

- 本票只证「仪表能正确收口、正确归因、正确记录插值观测字段」。**不证** Bug1 根因（`NO_THRESHOLD_EVENT` 只表示收口后无超阈候选），
  **不证**仪表可在实机采集（5 条原生 patch 的 `IL Compile Error`、`Queue` detour 是否触发、边界钩子真实时序、反射字段可得性 均为 UNKNOWN），
  **不证** 0.2.4.11 可出包。
- 残留归属：F1/F2 属本票必须收口（同一轮修复即可）；F3 可随 F1 一起改或登记后续；实机四项 UNKNOWN 归 1H+1G 采集票；
  t49-F2（crouch/prone 上限）、t49-F3（探针外同类判空）仍为既有后续项，与本票无关。

## 8. 审查边界声明

1. 全程只读主树：未改任何生产/测试文件；全部变异在仓库外副本 `_t62mut` 完成并已逐字节还原（DLL 回 `d6da5835…`、744/746）。
2. 本判定只对 §2 的字节有效；任一文件字节变化后需重取哈希并重跑 M0 与至少一条接线变异。
3. 报告内每个 `文件:行` 均在本次审查中 grep 复核，未沿用前几轮的读数。
