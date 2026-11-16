# t65 修复方案（先落设计，等 t63 复审回来再动生产字节）

- 任务：t65 repair-round-2，attempt 1，attempt_id `d0cc9eef-1ae8-41b9-8ce3-1f67d067c03b`
- 状态：**只写设计与改动清单，未改任何生产文件**（t63 绑的是 t60 字节，并发写入会让它的 verdict 作废）
- 上游：t62（review）findings + captain 对 T62-F1 的修法要求

## 0. 一句话

现在 `Finalize` 用「有没有记录」裁头尾，但「没记录」有两种原因：合法边界（玩家离场/关卡切换/session reset）与采集中途失效（`_third` 没了、hook 抛异常、detour 静默死掉）。后者被裁掉后整轮落到 `NO_THRESHOLD_EVENT`，等于把「仪表坏了」读成「这段本来就没超阈值」。修法：**裁剪必须带可判定的来源**；来源不合法就不许裁，直接 `UNKNOWN` 并写明从第几帧开始、当时采集面是什么状态。

## 1. 现状锚点（当前树，落码时按内容锚点定位，不依赖行号）

| 位置 | 现状 |
| --- | --- |
| `Bug1ProbeWindowBoundary.Finalize(Bug1StepWindow window)` | 纯函数，只看 `HasBeforeUpdate && HasRenderReady` 找首末齐全帧，直接 `RemoveRange` 裁掉两端；没有任何「为什么这里断了」的输入 |
| `Bug1WindowBounds` | `HeadExcluded / TailExcluded / InteriorPartial / FirstFrame / LastFrame / RemainingFrames / ExcludedReason`，`ExcludedReason` 只有 `WINDOW_HEAD_PARTIAL / WINDOW_TAIL_PARTIAL / WINDOW_BOUNDARY_NONE` |
| `Bug1ProbeRuntime.EndStep(string reason)`（`Bug1WindowBounds bounds = Bug1ProbeWindowBoundary.Finalize(_window);` 处） | 调 `Finalize` 后只把 4 个统计写回窗口，**不看 `reason`**，也不看目标/渲染链/hook 状态 |
| `Bug1ProbeRuntime.HookBoundaryEvents / OnPlayerDestroyedBoundary / OnLevelLoadedBoundary` | 已存在，分别 `Disable("target_left")` / `Disable("level_changed")`；`OnSessionReset()` 走 `Disable("session_reset")` |
| `Bug1ProbeJudge.Judge(window)` | 拷贝窗口统计后 `verdict.PhaseMissingOutOfWindow = 0;`（注释说是结构性保留） |
| `Bug1ProbeRuntime.WriteArtifacts` 的 result.json | 已有 `window_head_excluded / window_tail_excluded / window_interior_partial / phase_missing_genuine / phase_missing_out_of_window / window_excluded_reason` |

## 2. 设计：边界来源（provenance）

### 2.1 新增上下文对象（纯数据，无 Unity 依赖）

```csharp
internal sealed class Bug1WindowBoundaryContext
{
    public string EndReason;        // EndStep 的 reason 原样
    public bool TargetAlive;        // ReferenceEquals(_target, null) == false 且未被销毁
    public bool ThirdAlive;         // _third 可用
    public bool RendererAlive;      // _thirdRenderer 可用
    public bool HookFailClosed;     // Bug1ProbePatch.FailClosed
    public string LastHookError;    // Bug1ProbePatch.LastFailure 的类型名或空
}
```

`Finalize(window, ctx)` 重载：旧签名保留（`ctx = null` 时按 `suspected_capture_failure` 处理——fail-closed，不因为调用方忘了传上下文就默默裁剪）。

### 2.2 边界分类（合法集合只有两个）

**定死的口径（captain 2026-11-07 更正）**：`session_reset` 只能「关闭窗口」，不能「解释缺失」——它发生在任何一局末尾，包括仪表已经坏掉的那局；若算作合法裁剪原因，「采集中途失效 + 随后会话结束」就会被合法地裁成 `tail_excluded`，正好把 F1 要堵的洞重新打开。所以合法集合只有 `{target_left, level_changed}` 两个真会**导致不再产生记录**的事件。

| kind | 触发条件 | 是否裁剪 | 判定影响 |
| --- | --- | --- | --- |
| `boundary_player_destroyed` | `EndReason == "target_left"` **且**采集面健康（`TargetAlive==false` 或目标已销毁，`ThirdAlive && RendererAlive && !HookFailClosed`） | 裁 | 合法，`window_tail_unexplained=false` |
| `boundary_level_changed` | `EndReason == "level_changed"` **且**采集面健康 | 裁 | 合法，`window_tail_unexplained=false` |
| `suspected_capture_failure` | 其余一切 `tail>0` 的情况：`session_reset`、`config_changed`、`user_disable`、其它 reason，或 `target_left`/`level_changed` 但采集面不健康 | **不裁** | `window_tail_unexplained=true`；Judge → `UNKNOWN(CAPTURE_FAILURE_TAIL)`，`missing_fields` 含 `window_tail` 与 `capture_tail@<firstFrame>` |
| `none` | `TailExcluded == 0` | — | 一切不变 |

补充规则：

- `TailExcluded == 0` → `kind = "none"`，一切不变。
- `HeadExcluded` 同样记录 `window_head_boundary`，但**本票只对 tail 做「来源不合法即 UNKNOWN」**；head 半帧是开窗时机造成的，不属于「采集中途死掉」的形态。这一点写进报告，避免被读成漏判。
- 一个齐全帧都没有时（`firstComplete < 0`）：保持 t60 的「不裁剪」，交给 `ValidateFrames` 报 `MISSING_PHASE`——这是最保守的路径，不新增语义。
- `suspected_capture_failure` 下**不裁**是关键：裁了就变成「窗内没有缺相位」，正是要堵的洞。

### 2.3 判定顺序（`Bug1ProbeJudge.Judge`）

```
1) window == null → NULL_WINDOW
2) 拷贝窗口统计；PhaseMissingOutOfWindow = WindowHeadExcluded + WindowTailExcluded（结构性，见 §2.5）
3) if (window.WindowTailUnexplained || window.TailBoundaryKind == suspected_capture_failure)
       MarkUnknown(v, "window_tail", "TAIL_PARTIAL_UNEXPLAINED");          // 契约字面要求的原因码，保留
       MarkUnknown(v, "capture_tail@" + window.TailFirstFrame, "CAPTURE_FAILURE_TAIL");  // 更具体的原因码
       → return Unknown（Label = Unknown，绝不可能落到 NoThresholdEvent / NO_CANDIDATE_RUN）
4) ValidatePhaseCoverage → ValidateContext → ValidateFrames → …（原样不动）
```

`MarkUnknown` 只在 `Reason` 为空时写入，所以把 3)、4) 放在最前面就能保证 `reason` 是这两者之一，而不是被后面的 `MISSING_PHASE` 覆盖；`missing_fields` 仍然会累积，保留全部证据。两者都不会产出 `NoThresholdEvent`/`NO_CANDIDATE_RUN`。

### 2.4 窗口上新增的字段

`Bug1StepWindow`：

- `string TailBoundaryKind`（`none` / `boundary_player_destroyed` / `boundary_level_changed` / `suspected_capture_failure`；`session_reset` 只作为 `EndReason` 记录，不产生独立 kind）
- `string HeadBoundaryKind`
- `bool WindowTailUnexplained`
- `int TailFirstFrame = -1`、`int TailLastFrame = -1`（被裁掉的尾段帧号范围，供 `capture_tail@<frame>` 与报告定位）
- `string CaptureState`（`target=0/1;third=0/1;renderer=0/1;hook_failclosed=0/1;last_hook_error=<name>`）

`Bug1Verdict` 同步带出：`TailBoundaryKind`、`WindowTailUnexplained`、`TailFirstFrame`、`TailLastFrame`、`CaptureState`。

产物（RUN_END 与 result.json 同源）追加 6 字段：
`window_tail_boundary`、`window_tail_unexplained`、`window_tail_first_frame`、`window_tail_last_frame`、`capture_state`、`window_head_boundary`。

### 2.5 `PhaseMissingOutOfWindow` 的口径（t65 契约第三条）

选**结构性常量**这一支，并在代码注释与报告里写明：

```
PhaseMissingOutOfWindow = WindowHeadExcluded + WindowTailExcluded
```

理由与自证：

- 被剔除的帧已经从 `Frames` 移除，判定器无法再逐帧重算；把它写成 `head + tail` 是**同一事实的另一种表述**，不是独立统计。
- 与 `PhaseMissingGenuine` 天然互斥：`Genuine` 只统计仍在 `Frames` 里的帧，`OutOfWindow` 只统计被剔除的帧，两者交集为空、并集 = 原窗口帧数。
- 可自证：`head + tail + interior_partial + RemainingFrames == 原窗口帧数`，用例里直接断言这条恒等式。

**它什么时候非 0（回应 T62-F3）**：只要发生**合法边界裁剪**（`target_left` / `level_changed` 且采集面健康），`head+tail > 0` 就非 0；`suspected_capture_failure` 时因为不裁剪，该项恒 0 而由 `phase_missing_genuine` 接管。因此它不是「恒 0 的结构性常量」，而是一个有明确非 0 条件的测量值：**被合法边界剔除的帧数**。t60 里 `verdict.PhaseMissingOutOfWindow = 0` 的写法是错的（把一个本可非 0 的量写死成 0），本票一并改掉；代码注释与报告都要写明这条定义，避免再被读成「结构性常量」。

如果评审更希望「重算」，备选实现（不在本票做）：`Finalize` 不 `RemoveRange`，改为给帧打 `OutOfWindow=true`，`ValidateFrames` 跳过并计入 `OutOfWindow`。代价是改动 t60 已交付的 7 条用例的语义，本票不做。

## 2.6 T62-F2：订阅行为零用例绑定（medium）

现状：`HookBoundaryEvents()` 里两行订阅（`Player.onPlayerDestroyed += ...`、`Level.onLevelLoaded += ...`）没有任何用例绑定——审查官删掉整行订阅仍 744/746 全绿。三条边界（destroyed / levelLoaded / session_reset）各要一条用例。

分层设计（两种判据各管一件事，不互相冒充）：

| 判据 | 覆盖什么 | 为什么不用另一种 |
| --- | --- | --- |
| **IL 级**：断言 `HookBoundaryEvents` 里存在对 `Player.onPlayerDestroyed` / `Level.onLevelLoaded` 的成员引用（`ldsfld` + `add`） | 「订阅语句被删」必红 | 行为级要真触发 SDK 静态事件，而离线进程里 `SDG.Unturned.Player` 的静态构造可能抛 `TypeInitializationException`（NIL01 已实证同类失败），不可靠 |
| **行为级**：离线 seam 下 `Enable` → `__TestBindTarget(fake)` → 反射调用 `OnPlayerDestroyedBoundary(fake)` / `OnLevelLoadedBoundary(1)` / 直接 `OnSessionReset()` | 「回调真的收口」——断言窗口已 finalize、trace 里 `end_reason` 正确、`!Enabled` | IL 只能证「订阅语句在」，证不了「回调真的走到 Finalize」 |

两者合起来才完整：删订阅 → IL 红；把 handler 改成空实现 / 不调 `Disable` → 行为红。

## 3. 用例设计（新增 Bug1-62..67，另改 Bug1-60/61）

| 编号 | 构造 | 断言 |
| --- | --- | --- |
| Bug1-62 | 4 帧齐全 + 3 帧「仅 RR」（tail），`EndStep("session_reset")`，上下文 `TargetAlive=false`（模拟目标已销毁但没收到 destroy 事件） | `label != NoThresholdEvent`；`label == Unknown`；`reason == CAPTURE_FAILURE_TAIL`；`missing_fields` 含 `window_tail` 与 `capture_tail@<first>`；`window_tail_excluded == 0`（不裁） |
| Bug1-62b | 同上 tail，`EndStep("session_reset")`，采集面**健康** | 仍 `Unknown/CAPTURE_FAILURE_TAIL`、`window_tail_excluded == 0`——证明 `session_reset` 永不进入合法裁剪集合 |
| Bug1-63 | 同上 tail，`EndStep("target_left")`，采集面健康 | `label == NoThresholdEvent`；`window_tail_boundary == boundary_player_destroyed`；`window_tail_unexplained == false`；`window_tail_excluded == 3` |
| Bug1-64 | 同上 tail，`EndStep("config_changed")`，采集面健康 | `label == Unknown`；`reason == CAPTURE_FAILURE_TAIL`；`missing_fields` 含 `window_tail`；`window_tail_excluded == 0`（不裁） |
| Bug1-65 | 离线 seam：`Enable` → `__TestBindTarget(fakePlayer)` → 反射调用 `OnPlayerDestroyedBoundary(fakePlayer)`；再 `Enable` → `OnLevelLoadedBoundary(1)`；再 `Enable` → `OnSessionReset()` | 三次都在 trace.jsonl 的 `RUN_END` 行读到 `"end_reason":"target_left"` / `"level_changed"` / `"session_reset"`，且 `!Runtime.Enabled`（F2 行为级） |
| Bug1-67 | IL：`CountMemberReferences(HookBoundaryEvents, "SDG.Unturned.Player", "onPlayerDestroyed") >= 1` 且 `Level.onLevelLoaded` 同 | 删掉任一订阅行必红（F2 IL 级） |
| Bug1-66 | 内部半帧窗口（head=0/tail=0/interior=2）+ 有 tail 的窗口各一次 | `PhaseMissingOutOfWindow == head + tail`；`head + tail + interior_partial + RemainingFrames == 原帧数`；`Genuine` 与 `OutOfWindow` 不重叠 |
| Bug1-60（改） | 原用例把 `EndStep("t60")` 改成 `EndStep("target_left")` | 保持「合法边界裁剪」路径仍可到 `NoThresholdEvent` |
| Bug1-61（改） | 真实 trace 回放扩成三段 | ①raw（不裁）`Unknown/MISSING_PHASE`；②按 `target_left` 收口 `NoThresholdEvent`；③按 `session_reset` + 目标已销毁收口 `Unknown/CAPTURE_FAILURE_TAIL` |

必红要求：删掉 §2.3 的第 3 步（capture failure 分支）→ Bug1-62 必须红；删掉第 4 步（unexplained 分支）→ Bug1-64 必须红。

## 4. 改动清单（落码时按此执行，等 captain 放行）

### 4.1 `Platform/Diagnostics/Patches/Bug1ProbeWindowBoundary.cs`

1. `Bug1WindowBounds` 增：`TailFirstFrame`、`TailLastFrame`、`TailBoundaryKind`、`HeadBoundaryKind`、`TailUnexplained`；新增 kind 常量（`BoundaryPlayerDestroyed / BoundaryLevelChanged / BoundarySessionReset / BoundaryManualStop / SuspectedCaptureFailure / BoundaryNone`）。
2. 新增 `Bug1WindowBoundaryContext` 类（§2.1）。
3. `Finalize(window)` 保留为 `Finalize(window, null)` 的转发；新增 `Finalize(Bug1StepWindow window, Bug1WindowBoundaryContext ctx)`：
   - 计算 head/tail 与帧号范围；
   - 按 §2.2 分类；
   - `suspected_capture_failure` → **不 RemoveRange**，`RemainingFrames` 保持原样，`ExcludedReason = "WINDOW_TAIL_CAPTURE_FAILURE"`；
   - 其余情况沿用现有 `RemoveRange` 逻辑。

### 4.2 `Platform/Diagnostics/Patches/Bug1ProbeRuntime.cs`

1. `EndStep` 的 `Finalize(_window)` 调用改为先构造上下文：

   ```csharp
   var ctx = new Bug1WindowBoundaryContext {
       EndReason = reason,
       TargetAlive = !ReferenceEquals(_target, null) && _target.gameObject != null,
       ThirdAlive = !ReferenceEquals(_third, null),
       RendererAlive = !ReferenceEquals(_thirdRenderer, null),
       HookFailClosed = Bug1ProbePatch.FailClosed,
       LastHookError = Bug1ProbePatch.LastFailure == null ? string.Empty : Bug1ProbePatch.LastFailure.GetType().Name,
   };
   Bug1WindowBounds bounds = Bug1ProbeWindowBoundary.Finalize(_window, ctx);
   ```
   `TargetAlive` 的 `gameObject` 读取要 try/catch（销毁后的 Unity 对象访问会抛）。
2. 把新统计写回窗口：`TailBoundaryKind / HeadBoundaryKind / WindowTailUnexplained / TailFirstFrame / TailLastFrame / CaptureState`。
3. `RUN_END` extra 追加 6 字段（§2.4），`WriteArtifacts` 的 result.json 追加同 6 字段（同源，用 `verdict` 值）。
4. 测试缝：`__TestBindTarget(Player target)`（设置 `_target`）、`__TestSetCaptureHealth(bool thirdAlive, bool rendererAlive, bool hookFailClosed, string lastHookError)`（覆盖上下文里读不到真实对象的部分）。

### 4.3 `Platform/Diagnostics/Patches/Bug1ProbeJudge.cs`

1. `Bug1StepWindow` / `Bug1Verdict` 各加 §2.4 的字段。
2. `Judge` 开头按 §2.3 插入两步；`PhaseMissingOutOfWindow` 改成 `window.WindowHeadExcluded + window.WindowTailExcluded` 并加注释（§2.5）。

### 4.4 测试与注册

- `WhitelistTests/Evidence/Runtime/Bug1ProbeT60Tests.cs`：改 Bug1-60/61，新增 Bug1-62..66。
- `WhitelistTests/Program.cs`：Bug1-61 之后追加 5 条 `RunTest`。

## 5. 变异清单（落码后跑，仓库外副本）

| 变异 | 改法 | 期望红 |
| --- | --- | --- |
| M-E | 删掉 Judge 的 `WindowTailUnexplained / suspected_capture_failure` 分支 | Bug1-62、Bug1-62b、Bug1-64 |
| M-F | 把 `session_reset` 加进合法裁剪集合（按旧口径） | Bug1-62b |
| M-G | `Finalize` 在 capture failure 时仍然 `RemoveRange` | Bug1-62（`window_tail_excluded` 变 3） |
| M-H | `PhaseMissingOutOfWindow` 改回常量 0 | Bug1-66 |
| M-I | 删掉 `HookBoundaryEvents` 里 `Player.onPlayerDestroyed += ...` 一行 | Bug1-67（IL） |
| M-J | `OnPlayerDestroyedBoundary` 改成空实现 | Bug1-65（行为） |

每条记录变异后 DLL sha256 与红名单，逐字节还原并核对源码哈希。

## 6. 与 t65 契约三条 acceptance 的映射

| 契约条目 | 本方案落点 |
| --- | --- |
| ①TailExcluded>0 且收口不由边界事件解释 → `window_tail_unexplained=true` + `MarkUnknown("window_tail","TAIL_PARTIAL_UNEXPLAINED")`；补「尾部仅 RR + session_reset → label != NoThresholdEvent」用例 | §2.2（合法集合只有 `{target_left, level_changed}`）+ §2.3 第 3 步 + Bug1-62/62b/64。`window_tail_unexplained` 照写，同时给出更具体的 `CAPTURE_FAILURE_TAIL`（契约允许「独立 reason 码」）；`session_reset` **不进入**合法集合，不再有一行开关的歧义 |
| ②边界事件用例（`OnPlayerDestroyedBoundary` / `OnLevelLoadedBoundary`）或 IL 断言；更正报告 §9 措辞 | Bug1-65（行为用例，强于 IL）；报告 §9 措辞更正见 §7 |
| ③`PhaseMissingOutOfWindow` 择一 | §2.5 选结构性常量 + 恒等式自证 + Bug1-66 |

## 7. 报告 §9 措辞更正（t60 报告 `bug1-entry-t60-window-seal.md`）

原文把 `NO_THRESHOLD_EVENT` 说成「真实标签」，容易被读成「这一轮没问题」。改为：

> 收口后同一份数据不再报 `MISSING_PHASE`，而是给出 `NO_THRESHOLD_EVENT` **或** 带原因码的 `UNKNOWN`——取决于尾部缺失是否被合法边界事件解释。合法集合只有 `{target_left, level_changed}`；真实 trace 的 1043 帧尾部是「目标已销毁但当时版本没有 destroy 钩子」，按 t65 规则属于 `suspected_capture_failure`，会判 `UNKNOWN(CAPTURE_FAILURE_TAIL)` 而不是 `NO_THRESHOLD_EVENT`；`session_reset` 只能关闭窗口、不能解释缺失，不进入合法集合。**这仍然只是测量能力，不构成 Bug1 根因结论。**

## 8. 待并入

captain 提到 t63 可能还有第二条 findings（订阅泄漏、同源导出之类）。本方案预留：`HookBoundaryEvents` 已用 `_retryHooked` 之外无幂等标志，若复审指出重复订阅，在 `Enable` 与 `RollbackEnable` 之间加 `_boundaryHooked` 幂等标志即可（同批字节一次收口，不另开轮次）。

## 9. 修订记录

| 时间 | 改动 | 原因 |
| --- | --- | --- |
| 初稿 | 五类边界，`session_reset` 在采集面健康时算合法 | 当时按 captain 口述「三类合法」 |
| 修订 1 | 合法集合收紧为 `{target_left, level_changed}`；`session_reset` 只记录为 `EndReason`，永不解释缺失；`suspected_capture_failure` 一律不裁 → `UNKNOWN(CAPTURE_FAILURE_TAIL)` | captain 更正：「session_reset 只能关闭窗口，不能解释缺失」，否则「采集中途失效 + 随后会话结束」会被合法裁掉，F1 的洞重新打开 |
| 修订 1 | 新增 Bug1-62b（session_reset + 采集健康仍须 Unknown）与变异 M-F | 钉死「session_reset 永不合法」这条口径 |
| 修订 2 | 并入 T62-F2：Bug1-65（三条边界行为级）+ Bug1-67（IL 订阅存在性）+ 变异 M-I/M-J | 审查官 M-E 实证删掉订阅行仍全绿 |
| 修订 2 | 并入 T62-F3：`PhaseMissingOutOfWindow` 从「结构性常量 0」改为 `head+tail` 的真实测量值，写明非 0 条件 | 原写法把一个本可非 0 的量写死，容易被误读成在测什么 |
