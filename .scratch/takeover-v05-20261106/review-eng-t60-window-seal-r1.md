# T63 工程轴复审 r1：t60 窗口收口 / 订阅泄漏 / 插值只读 / 同源导出

- 审查对象：t60（`bug1-entry-t60-window-seal.md`，报告 sha256 `2cb817af…`）
- 审查轮次：t63 round 1（attempt 2，attempt_id `bc4eb6c5-1a06-471c-87b7-b4e1ec45f0b5`）
- 审查者：reviewer-eng（工程轴（只判代码质量、边界、回归风险与工程规范，不评是否满足需求））
- 判定：**needs_revision**（1 high / 2 medium / 2 low）
- 审查方式：只读主树；四条变异全部在仓库外副本 `../_t63mut` 上跑，逐字节还原；不采信自述

## 0. 结论

t60 的窗口收口在**本轮真实数据上是对的**：trace 尾部 1044 帧确实一条记录都没有（无 RENDER_READY 也无
BEFORE_UPDATE），裁掉它们没有删掉任何观测。四条变异（M-A/M-B/M-C/M-D）全部真红且各自只红对应用例，
订阅有对称卸载、插值公式与 SDK 一致、`input.tick` 全仓零写入、RUN_END 与 result.json 同源。

但收口判据只有「相位是否齐全」一维，`EndStep(reason)` 的 reason **没有参与裁剪判断**。当
`BEFORE_UPDATE` detour 在窗口中途停止而 `RENDER_READY` 继续建帧时（ESC 暂停、剔除、目标离场前的窗口），
这些帧与「目标离场后的越界帧」在数据上同形，会被一并计入 `window_tail_excluded` 裁掉，`phase_missing_genuine`
回到 0，标签落 `NoThresholdEvent`。**仪表自身的失能被读成「没有异常」**——这正是 Bug1 要区分的那类现象。

## 1. 被审字节锁定（本人实测，与 t60 自述一致）

| 文件 | 字节 | sha256 |
| --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1ProbeWindowBoundary.cs` | 8932 | `4217568ad47feca65a1aa582aa09d6d733cba110cf95da46b0872b160f87102f` |
| `Platform/Diagnostics/Patches/Bug1ProbeRuntime.cs` | 62778 | `e6d3bc265c7422a4152f62054fad58264f94957d5de6bd6c0ebacd70e177b2f4` |
| `Platform/Diagnostics/Patches/Bug1ProbeJudge.cs` | 23582 | `7228213064b9272383c403e28dd99d507c8d075154825bc7d68609126081028a` |
| `Platform/Diagnostics/Patches/Bug1ProbePatch.cs` | 11707 | `22308d77197af4e58a34c8625f813e862783eebc06c3f5b129aaf29b28dd83d6`（未变） |
| `Platform/Diagnostics/Patches/Bug1ProbeControl.cs` | 23724 | `53a6c36bb809f7fc22a748823fbd2864d4bdc909cef76d39d75fc69cc55d617f`（未变） |
| `Platform/Diagnostics/Patches/Bug1ProbeModel.cs` | 3684 | `eb40f8060db49aadfd65ec3328cb1c4104f8b3bf3ce6b3fdb6a0d2ffd92c5acd`（未变） |
| `Platform/Diagnostics/Patches/Bug1ProbeRxPhases.cs` | 16693 | `8d0d36fc94cb1d6b8ba98fb99e73c6e1691ccee04e2f7d928ad3830f1964ca4a`（未变） |
| `Platform/Diagnostics/Patches/Bug1ProbeTargetSelector.cs` | 6682 | `b716369fac0f4bace41f9a86297755b22d665fd7b64e7382e25fa9feb9d06da8`（未变） |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeT60Tests.cs` | 19923 | `8df9ff4d29d345cb3ee59b99671eb4407164731df814e74de5516f35c88f7229`（新增） |
| `WhitelistTests/Program.cs` | 142682 | `b9933c0b3ae784f02bda1b6b383aced017b9e0d476aad793fc868f36a1fd7018` |
| `SteamP2PFriends.csproj` | 27575 | `ed629ae7064015d55d2e1bee15b01ba1eb6a7a565dcdb80ac36892499350b948`（`git diff --numstat HEAD` = `15 0`，纯追加） |
| `bin/Release/SteamP2PFriends.dll` | 1491456 | `d6da583532deaf45019af455c51a3b9b144dde962beff3a4726736fc0780d7dc` |

Bug3 五文件前缀未变：`7a82de8e` / `a89f40b9` / `3d5044f7` / `a4553385` / `5b0c4e9b`。

## 2. 三命令复跑（主树，实测）

| 命令 | 退出码 | 结果 |
| --- | --- | --- |
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 0 | 0 警告 0 错误，DLL 复现 `d6da5835`（两次重建一致，确定性） |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | 0 | 0 错误，4 条既有警告（CS0219×1、CS0649×3） |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | 1 | **744/746 PASS (Failed: 2)**，仅 GBA01/NIL01（控制台缺 Unity：`TypeInitializationException SDG.Unturned.SteamPlayer → NetReflection → ReflectionTypeLoadException`）；`FAIL [Runtime] Bug1` = 0（Bug1 61/61） |

## 3. 四条变异（仓库外副本 `../_t63mut`，tar 复制后独立重建，得 `d6da5835`；已删除）

脚本 `D:/tmp/mut63.py`（字节级替换 + `.t63bak` 备份）与 `D:/tmp/drive63.sh`；每条跑完从 `.t63bak` 复制回，
**还原后 DLL 每次都回到 `d6da5835`**，最终副本三源文件回 `e6d3bc26` / `72282130` / `4217568a`，复跑 744/746。

| 变异 | 改动点 | 变异 DLL sha256（前 16） | 结果 | 新红用例 |
| --- | --- | --- | --- | --- |
| M-A | `Bug1ProbeRuntime.cs`：`Finalize(_window)` 调用改为 `new Bug1WindowBounds()` | `9faa7690236dc9d3` | 743/746 | **Bug1-60**（`window_tail_excluded` 期望 3 实际 0；`!result.Contains("MISSING_PHASE")` 失败） |
| M-B | `Bug1ProbeJudge.cs`：删掉 `if (!f.HasBeforeUpdate) { PhaseMissingGenuine++; MarkUnknown(...); }` 的计数体 | `14c1b69b1da3250e` | 741/746 | **Bug1-56**（真缺应 4 实际 1）、**Bug1-57**（应 2 实际 1）、**Bug1-61**（修复前应 UNKNOWN 实际 NoThresholdEvent） |
| M-C | `Bug1ProbeWindowBoundary.cs`：`Classify` 四个分支全删、只 `return null` | `8b740d00c43424df` | 743/746 | **Bug1-59**（无消费记录应报 `no_consume_yet`） |
| M-D | `Bug1ProbeWindowBoundary.cs`：`Compute` 里 `if (!s.HasTick) { HasTick = true; Tick = 0; }` | `e10449235f4c5628` | 743/746 | **Bug1-58**（缺 tick 必须标记不可用） |

注意 M-A 只由 Bug1-60 兜住：Bug1-55/56/57 直调 `Finalize`，删掉生产调用点它们不会红。也就是说
「生产 EndStep 真的调了收口」这条保证目前只有一条用例、且它断言的正是本报告 §4 里被质疑的行为。

## 4. 逐行审 `Bug1ProbeWindowBoundary`（F1）

`Finalize(Bug1StepWindow)`（`Bug1ProbeWindowBoundary.cs:36-90`）的实际判据是：

- `:41-52` 扫全表，`HasBeforeUpdate && HasRenderReady` 才算 complete，记 `firstComplete/lastComplete`；
- `:54-58` **没有任何 complete 帧就直接返回、一帧不裁**——这条挡住了「detour 全灭被洗成干净窗口」（Bug1-56 覆盖，M-B 变异也证明该路径有效）；
- `:60-78` `HeadExcluded = firstComplete`、`TailExcluded = count-1-lastComplete`，对 `[first..last]` 计 `InteriorPartial`，然后 `RemoveRange` 头尾。

裁剪本身是「只删头尾不完整段、内部半帧保留」（Bug1-57 覆盖，M-A/M-B 不误伤），**没有删整段**，这点通过。

问题在判据维度：**它只问「相位齐不齐」，不问「为什么缺、缺的是哪一相」**。`EndStep(reason)`
（`Bug1ProbeRuntime.cs:842`，唯一调用点 `Disable(reason)` 于 `:213`）的 `reason` 只写进 `end_reason` 字段
（`:886`），从未传入 `Finalize`（`:850`）。于是下面两种数据形态完全不可区分：

1. 目标离场 / 关卡切换 → 两相同时停止 → 尾部帧既无 before 也无 render（本轮真实 trace 就是这种，见 §8）；
2. `BEFORE_UPDATE` detour 中途停止、`RENDER_READY` 继续建帧（ESC 暂停、剔除、相机仍在跑）→ 尾部是
   **render-only**，被 `lastComplete` 之后的 `TailExcluded` 一并裁掉。

形态 2 被裁后 `PhaseMissingGenuine=0`、`WindowExcludedReason=WINDOW_TAIL_PARTIAL`、`Judge` 落
`NoThresholdEvent`——读起来是「窗口干净、无阈值事件」，而事实是**仪表自己在窗口中途失能**。这正是
Bug1 想区分的「culling / ESC 暂停导致 sim 停止」那一类，诊断工具把它当越界帧吃掉了。

回避这一点需要「尾部帧有 render 无 before ⇒ 不是越界而是中途停止」的信息，而该信息就在帧里
（`HasRenderReady`/`HasBeforeUpdate`），只是 `Finalize` 没看。最小修法见 §10 F1。

## 5. 订阅、幂等与泄漏

- `HookBoundaryEvents()`（`Bug1ProbeRuntime.cs:370-376`）挂 `Player.onPlayerDestroyed` 与 `Level.onLevelLoaded`，各自 try/catch 记 `BOUNDARY_HOOK_FAILED:*`；`UnhookBoundaryEvents()`（`:378-382`）逐个 `-=`，同样吞异常。
- 卸载路径齐全：`Enable` 成功且非 offline 时才挂（`:153`）；`RollbackEnable`（`:175`）与 `Disable`（`:220`）都调 `UnhookBoundaryEvents()`；`Disable` 里 `Enabled=false` 在 `EndStep` 之后，重复调用被 `if (!Enabled) return;`（`:209`）挡住，不会二次 EndStep。
- 幂等：`Finalize` 二次调用时首尾已无残缺帧 → `HeadExcluded=TailExcluded=0`，`InteriorPartial` 重算同值，不重复裁剪（Bug1-55 断言调用后 `StepFirstFrame/StepLastFrame` 同步即此性质）。
- 测试隔离：`__Offline` 下不挂不摘边界事件，用例不会污染真实事件表。
- 结论：**无泄漏、无重复订阅、幂等成立**。

## 6. 插值字段采集（只读性与 SDK 公式）

- `SampleInterpolation()`（`Bug1ProbeRuntime.cs:677-729`）只做 `GetValue`：`PlayerInput.tick`（property）、`PlayerInput.RATE`（field）、`PlayerMovement.lastUpdatePos`（field）、`_movement.transform.position`；`Time.realtimeSinceStartup` 走 `TryReadRealtimeSinceStartup` 包 try/catch。
- `grep -rn "SetValue(" Platform/Diagnostics` = 0 命中，`PlayerInput` 的 `tick` 无任何写路径 → **远端 tick 只读成立**。
- 公式：`Bug1InterpolationSample.Compute`（`Bug1ProbeWindowBoundary.cs:117-152`）`Lerp=(SampleTime-Tick)/Rate`、`LerpClamped` 夹 [0,1]，与 SDK `PlayerMovement.cs:1746` `Vector3.Lerp(lastUpdatePos, transform.position, (Time.realtimeSinceStartup - player.input.tick) / PlayerInput.RATE)` 一致（`Mathf.Lerp` 亦夹 [0,1]）。
- 缺项（无 tick / 无 sampleTime / rate<=0）只进 `Unavailable` 列表，**不填默认值**（M-D 变异把默认值塞回去 → Bug1-58 红，证明用例有效）。
- `last_update_pos` 取的是字段快照，与 `transform.position` 同一时刻读取，不存在跨帧拼接。

## 7. RUN_END 与 result.json 同源

六字段（`window_head_excluded`/`window_tail_excluded`/`window_interior_partial`/`phase_missing_genuine`/
`phase_missing_out_of_window`/`window_excluded_reason`）两处都直接读同一个 `verdict.*`：

- RUN_END 行：`Bug1ProbeRuntime.cs:883-888`；
- result.json：`Bug1ProbeRuntime.cs:1158-1163`。

`verdict` 由 `Bug1ProbeJudge.Judge(_window)`（`:873`）单次产出，无二次计算 → **同源成立**。相位计数亦在
`Judge` 之前同步（`:869-872`），不存在「判读看到 0、打印看到 N」的旧缺陷。

## 8. 测试有效性：Bug1-61「真实 trace 回放」说了什么、没说什么（F2）

t60 把 Bug1-61 当「真实 trace 证伪了原始误判」的证据：raw = `Unknown/MISSING_PHASE`（tail 缺相位 200）
→ sealed = `NoThresholdEvent/NO_CANDIDATE_RUN`（head_excluded=1、tail_excluded=1043、genuine=0）。逐行核对后
这个结论**在本轮数据上成立**，但用例本身证明不了它自述的那件事：

- `ReplayWindow`（`Bug1ProbeT60Tests.cs:278-292`）对每个帧都传 `HasRenderReady = true`（`:288`
  `Frame(frames[i], hasBefore.Contains(frames[i]), true, 0.02 * i)`）——它**只知道 before 的集合，不知道
  render 的集合**，于是把「没有观测」的帧当成了「有 render 缺 before」的帧。
- 真实 trace 的实测（`bug1-20261107T044218Z/HOST/trace.jsonl`，19474 行）：`RENDER_READY` 6338 帧、
  `BEFORE_UPDATE` 6337 帧，最后一条记录帧 13415；**尾部 1044 帧（13416..14459）一条记录都没有**
  （`tail_render=0`、`tail_before=0`），整条 trace 只有 1 帧 render-only（frame 7078，内部半帧）。
  也就是说：raw 的 200 条 `phase_BEFORE_UPDATE@…` 是**外推出来的空帧**被硬编码成 render-only 造出来的。
- 因此该用例既不能证明「真实尾部确有 RENDER_READY 而 before 停了」，也不能证明「收口后不得再出现
  MISSING_PHASE」——后半句在「所有帧都有 render」的前提下必然成立，属自证。

它与 F1 是同一枚硬币两面：真实数据是形态 1（无观测），而用例把形态 2（render-only 尾部）当成常态并固化了
「裁掉且不报」的期望（Bug1-60 同样：`__TestAddPartialTailFrames(3)` 造的就是 render-only，`EndStep("t60")`
是**非边界 reason**，却断言 `phase_missing_genuine:0` 且 `!result.Contains("MISSING_PHASE")`）。

## 9. 边界

| 检查项 | 结果 |
| --- | --- |
| Bug3 五文件哈希 | 未变（`7a82de8e`/`a89f40b9`/`3d5044f7`/`a4553385`/`5b0c4e9b`） |
| `Release/`、`publishi/` 新写入 | `find … -newermt "2026-11-07 11:00"` 无输出 |
| `csproj` | `git diff --numstat HEAD` = `15 0`（纯追加，5 Bug3 + 5 Bug1 + 5 本轮 `Bug1ProbeT60Tests.cs`/`Bug1ProbeWindowBoundary.cs` 等），无删除、无重排 |
| `playerID == null` 全仓 | **1 处真实代码**：`Platform/UI/SteamPersonaDisplay.cs:271` `if (sp != null && sp.playerID != null && sp.playerID.steamID == steamId)`——**t60 之前就在，非本轮引入**，见 O-01 |
| probe 默认关闭 | `Bug1ProbeControl.Initialize` 读 cfg `Enabled`（默认 false），关闭态不装 hook |

## 10. Findings

### F1（high）收口判据无「为什么缺」这一维，render-only 尾部会把仪表失能洗成正常

- 位置：`Platform/Diagnostics/Patches/Bug1ProbeWindowBoundary.cs:36-90`（判据）；`Platform/Diagnostics/Patches/Bug1ProbeRuntime.cs:850`（`Finalize(_window)` 未传 reason）、`:842`/`:213`（reason 来源）
- 问题：`Finalize` 只按 `HasBeforeUpdate && HasRenderReady` 求 complete 边界，尾部凡不完整一律 `TailExcluded`。当 `BEFORE_UPDATE` detour 在窗口中途停止而 `RENDER_READY` 继续时，该段与「越界帧」同形被裁，`PhaseMissingGenuine=0`、标签落 `NoThresholdEvent`。`window_tail_excluded` 虽在 RUN_END 里可见，但语义读作「越界」，无法提示「中途停止」。
- requiredFix（最小）：`Finalize(Bug1StepWindow window, string endReason)` 增加 `TailRenderOnly` 计数（尾部 `HasRenderReady && !HasBeforeUpdate` 的帧数）；仅当 `endReason ∈ {target_left, level_changed, session_reset, CONFIG_CHANGED}` 时把 render-only 尾部计入 `TailExcluded`，否则保留帧并 `MarkUnknown("PHASE_STOPPED_MID_WINDOW")`；同时把 `window_tail_render_only` 加进 RUN_END 与 result.json（同源）。对应把 Bug1-60 的 `EndStep("t60")` 改成边界 reason、新增一条「非边界 reason + render-only 尾部 ⇒ UNKNOWN」用例。

### F2（medium）Bug1-61「真实回放」对无记录帧伪造 `HasRenderReady=true`

- 位置：`WhitelistTests/Evidence/Runtime/Bug1ProbeT60Tests.cs:288`（`Frame(..., true, ...)`），`:230-260`（只用 `hasRender`/`hasBefore` 两个集合，尾部空帧靠 `RUN_END.frame_count` 外推）
- 问题：把「无观测帧」变成「render-only 帧」，用例的 raw 基线不来自真实数据；其「sealed 后无 MISSING_PHASE」的断言在伪造前提下自证。真实 trace 尾部 1044 帧零记录（§8），用例并未验证这一点。
- requiredFix：`ReplayWindow` 增加 `HashSet<int> hasRender` 参数，传 `hasRender.Contains(frames[i])`；对外推的空帧单独断言 `HasBeforeUpdate=false && HasRenderReady=false`；把「真实 trace 尾部无记录」作为独立断言写进用例，render-only 场景留在 Bug1-60 的合成窗口里。

### F3（medium）`PhaseMissingOutOfWindow` 恒 0，与「拆成双计数」的表述不符

- 位置：`Platform/Diagnostics/Patches/Bug1ProbeJudge.cs:141` `verdict.PhaseMissingOutOfWindow = 0;`（字段声明 `:110`）
- 问题：字段被 RUN_END/result.json 导出（`Runtime.cs:887`、`:1162`），但没有任何生产者；下游若按它区分「真缺」与「越界缺」永远读到 0。t60 自述「缺相位拆成 genuine/out_of_window 双计数」在代码里只有一半。
- requiredFix：要么在 F1 的修法里让它真正累加（被裁掉的 render-only 尾部帧数），要么从 RUN_END/result.json 移除该字段并在文档改口径；保留恒 0 字段是最差选项。

### O-01（low，pre-existing，carry-over）`SteamPersonaDisplay.cs:271` 仍用 `playerID != null`

- 位置：`Platform/UI/SteamPersonaDisplay.cs:271`
- 说明：SDK `SteamPlayerID` 重载 `==`/`!=` 在任一侧为 null 时抛 NRE（本项目已有实证），该行未改过、非 t60 引入。验收项「`playerID == null` 全仓 0 命中」因此字面上无法达成（2 条为注释、1 条为既有代码）。建议另开小票改 `ReferenceEquals`，不阻塞本票。

## 11. 复现、结论分层与 pass 边界

复现（主树只读，变异在 `../_t63mut`）：

```bash
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe | tail -40
# 变异：python D:/tmp/mut63.py MA|MB|MC|MD|restore ; bash D:/tmp/drive63.sh
# 真实 trace 回放：设置 SPF_BUG1_TRACE_JSONL=.../bug1-20261107T044218Z/HOST/trace.jsonl 后跑 Bug1-61
```

**已确认**：四条变异全部真红且只红对应用例；逐字节还原（DLL 两次回 `d6da5835`）；三命令与确定性重建；
订阅挂/摘对称且 offline 不挂；`Finalize` 幂等；`input.tick` 零写入；插值公式与 SDK 一致、缺项不填默认值；
RUN_END 与 result.json 六字段同源；Bug3 五文件与 `Release/`、`publishi/` 未被触碰。

**待验证**：真实主机上「ESC 暂停 / 剔除」期间是否会产出 render-only 尾部（决定 F1 的现实频率；本轮 trace 未出现）。

**已被否定**：t60 自述隐含的「Bug1-61 用真实 trace 证明了真实尾部是 render-only」——真实尾部零记录（§8）；
「缺相位已拆成 genuine/out_of_window 双计数」——后者恒 0（F3）。

**pass 边界**：本轮判 needs_revision，仅因 F1（仪表会把自身中途失能读成正常）。即便 F1 修完，
本票也**不代表 Bug1 已诊断、不代表仪表可采、不代表可出包**；实机四项 UNKNOWN 仍然有效。


<!-- self_sha256(body)=933a205dfa72a35fa7d020ae9e8f4a50a3a3266df9fb235a1e430ed7afc492d3 -->
