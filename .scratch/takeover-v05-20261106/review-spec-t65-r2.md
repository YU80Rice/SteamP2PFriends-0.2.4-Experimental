# 窗口边界归因复审（t66 / 规格轴 r2，被审 t65）

- 审查官：reviewer-spec（规格轴独立实例）
- 任务：t66，attempt `fde53c55-ed37-4a05-94b5-3736ffb26675`（首次派发 attempt 1 已由平台作废）
- 被审：t65（ds-probe2）"窗口裁剪来源判据 + 边界订阅可判 + out_of_window 实测"
- 审查时点：2026-11-07 13:50–14:55（Asia/Shanghai）
- 只读：主树生产/测试**零修改**；全部变异在仓库外副本 `D:/Agent-工作目录/DevelopMyUNMultiplayerModAndModloader/_t66iso` 完成并逐字节还原。

## 1. 结论摘要

**verdict = pass**（status=completed）。Captain 七条逐条成立，T62 的 F1/F2/F3 三条全部有可失败的绑定证据；四条必做变异在本机独立复跑全部变红，还原后源码与 DLL 与送审字节逐字节一致。

| # | 判据 | 结论 | 关键证据（文件:行） |
|---|---|---|---|
| 1 | 裁剪由"来源"决定而非"相位齐不齐" | 成立 | `Bug1ProbeWindowBoundary.cs:131`（`legalTail ? tailCount : 0`）、`:161-174`（`ClassifyTail`）、`:176-180`（合法集合仅 2 项） |
| 2 | render-only 尾部判"detour 中途停止" | 成立 | `Bug1ProbeJudge.cs:169-176`（门 1 → `PHASE_STOPPED_MID_WINDOW`）；合成 Bug1-68 + 真实 trace Bug1-61 |
| 3 | 三条边界各有行为用例，删订阅必红 | 成立 | Bug1-65（三条行为）+ Bug1-67（IL 订阅读取）；M3 删订阅 → Bug1-67 红 |
| 4 | out_of_window 实测 == head+tail | 成立 | `Bug1ProbeJudge.cs:164`；Bug1-66（5 == 2+3）；M4 改回 0 → 4 条红 |
| 5 | 订阅对称/Disable 幂等/tick 零写/RUN_END 同源 | 未被破坏 | `Bug1ProbeRuntime.cs:377-389`、`:220-227`、`:1255-1278`；`.tick =` 全仓 0 命中 |
| 6 | 四条变异自跑并逐字节还原 | 成立 | 本文 §5（9e04ee55 / 9748ad2a / 76a2b99f / 0b6a3619 → 还原 7168ace4） |
| 7 | pass 边界写明 | 成立 | t65 报告 §11；本文 §6 复述 |

## 2. 被审字节绑定

本判定**只对下列字节有效**（主树 `sha256sum` 实测，与 t65 报告 §8 逐项一致）：

| 文件 | 字节 | SHA-256 |
|---|---|---|
| `Platform/Diagnostics/Patches/Bug1ProbeWindowBoundary.cs` | 14154（285 行） | `c3a98c0010f1f557dc1065bb29d39d5d73657372725ddd19da1bb5d762cdf944` |
| `Platform/Diagnostics/Patches/Bug1ProbeJudge.cs` | 26438（557 行） | `f41613c0194dc36716d2b17458956e0bc025d44e0323db40ccc834083de8f314` |
| `Platform/Diagnostics/Patches/Bug1ProbeRuntime.cs` | 70006（1317 行） | `114742df5e12f647964842f3df8d8078ac363ee51e08d3b94b393eeba0048a5c` |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeT60Tests.cs` | 24658（409 行） | `154ac184a22b7dd989a3c8cc10efcb7e9f553203cce6b0cc222402f5e9d25409` |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeT65Tests.cs` | 17970（321 行） | `be418ae2bbd24e54e05182608bb0313211145306facbfb5b121c1ab0943f3514` |
| `WhitelistTests/Program.cs` | 144661（1071 行） | `f61df0d20f3e8bc2720a6f013bd9513df441b8187fc20392aeb2bd09d1bc9a24` |
| `SteamP2PFriends.csproj` | 27575 | `ed629ae7064015d55d2e1bee15b01ba1eb6a7a565dcdb80ac36892499350b948`（未改） |
| `bin/Release/SteamP2PFriends.dll` = `WhitelistTests/bin/Release/SteamP2PFriends.dll` | 1496576 | `7168ace45a67cacd4578aac7789c11c38ed1d00af87317262d4b563628a3dd6f` |

未动项复核：`Bug1ProbeControl.cs` `53a6c36b…`、`Bug1ProbeEntryLogic.cs` `04a4beeb…`、`Bug1ProbeRxPhases.cs` `8d0d36fc…` 与 t54/t63 一致；Bug3 五文件 `a89f40b9/7a82de8e/3d5044f7/a4553385/5b0c4e9b` 未变；`Release/`、`publishi/` 最后写入均早于本轮窗口。

## 3. 逐条判定

### 3.1 裁剪只由来源决定，且 session_reset 永不解释缺失

- 裁剪开关只有一处：`Bug1ProbeWindowBoundary.cs:131` `bounds.TailExcluded = legalTail ? tailCount : 0;`，`legalTail` 来自 `IsLegalBoundary(ClassifyTail(...))`。
- `ClassifyTail`（`:161-174`）返回的五类里只有 `boundary_player_destroyed`、`boundary_level_changed` 进入 `IsLegalBoundary`（`:176-180`）；`session_reset`、`config_changed`、未知 reason 与 `ctx==null`/`HookFailClosed` 全部归 `suspected_capture_failure`。**"相位齐不齐"不参与该判定**——裁剪只在"源已确认合法收口"时发生，与尾部帧是否缺 BU 无关。
- `target_left` 还额外要求 `!ctx.TargetAlive`（`:168`），即"原因字符串说目标走了"必须有"目标确实已销毁"佐证，二者缺一即 capture_failure。
- 不裁时尾部保留并显式标记：`:143` `TailUnexplained = tailCount > 0 && !legalTail`，`:111-122` 统计 render-only 数量，`:133-139` 写 `WINDOW_TAIL_UNEXPLAINED`/`WINDOW_TAIL_PARTIAL`。
- 空窗口保护：`:97-101` 零齐全帧直接返回，不做任何裁剪（`HeadExcluded=0, TailExcluded=0`）。
- result.json 可见性：`Bug1ProbeRuntime.cs:869` 写 `_window.CaptureState = CaptureStateJson(ctx, bounds)`，`:1255-1278` 输出 `capture_state`（含 `end_reason/target_alive/third_alive/renderer_alive/hook_fail_closed/last_hook_error/first_frame/tail_boundary`）与 `window_tail_boundary`、`window_tail_unexplained`、`window_tail_first_frame`。RUN_END 同一个 verdict 落盘（`:883-912` 与 `:1255-1278` 同源，Bug1-60 直接断言字符串）。

### 3.2 render-only 尾部判 detour 中途停止

- 合成样本：Bug1-68（`Bug1ProbeT65Tests.cs`）构造 3 帧"有 RENDER_READY、无 BEFORE_UPDATE"的尾帧，`ctx` 给 `session_reset`（非合法边界）→ 断言 `tail_excluded == 0`、Judge 判 `Unknown/PHASE_STOPPED_MID_WINDOW`；同形状在合法边界下允许裁且不再报该 reason（同一用例后半段）。
- 真实 trace：Bug1-61 解析 `recorded=6338`、`run_end_frame=14459`，用 RUN_END 帧号补齐"记录停止到收口之间"的空白尾帧，四种上下文各判一次（见 §4）。裁决顺序保证 render-only 先于"未解释"处理（Judge `:169-176` 在 `:178-184` 之前）。

### 3.3 三条边界行为用例 + 订阅可判

- Bug1-65（行为级，三条全覆盖）：离线 `Enable` → 关联 frames → 依次触发 `OnPlayerDestroyedBoundary`/`OnLevelLoadedBoundary`/`OnSessionReset`，断言 `Enabled=false` 且 `trace.jsonl` 的 RUN_END `end_reason` 分别为 `target_left`/`level_changed`/`session_reset`，并含 `"capture_state":{`。
- Bug1-67（IL 级）：`CountFieldLoads(HookBoundaryEvents, "SDG.Unturned.Player", "onPlayerDestroyed") >= 1` 与 `("SDG.Unturned.Level","onLevelLoaded") >= 1`；M3 实测删一则红（§5）。
- 订阅点：`Bug1ProbeRuntime.cs:377-383`，仅 `!__Offline` 时由 `:160` 调用；退订 `:385-389` 对称。

### 3.4 out_of_window 实测

- `Bug1ProbeJudge.cs:164` `verdict.PhaseMissingOutOfWindow = window.WindowHeadExcluded + window.WindowTailExcluded;`（已不再是 `= 0`）。
- Bug1-66 用 9 帧构造 head=2/tail=3/interior=1/remaining=4，断言 `CandidateCount==3`、`genuine==1`、`out_of_window==5`、恒等式 `head+tail+remaining==9`、`genuine+out_of_window<=9`（不交叠）。
- Bug1-60 断言 RUN_END 中 `"phase_missing_out_of_window":3`；Bug1-61 断言真实 trace 上 `out_of_window == head+tail`。
- **说明（非 finding）**：Captain 判据第④条字面式 `head+tail+interior+remaining == 原帧数` 与"interior 半帧仍留在判定窗内"互斥，照写会把 9 算成 10。t65 采用 `head+tail+remaining == 原帧数`，并单独断言 `interior`/`genuine`/`out_of_window` 与"不交叠"，是唯一自洽的记账方式，建议 Captain 采用该措辞。

### 3.5 未被破坏的既有契约

- 订阅对称：`:377-383` 与 `:385-389` 成对，删/加同一组方法。
- `Disable` 幂等：`:220` 首行 `if (!Enabled) return;`。
- `input.tick` 零写入：全仓（Core/Platform/Adapters/WhitelistTests，排除 `.scratch`）`.tick =` **0 命中**；探针内仅局部变量 `double tick = 0d` 与读值 `tick = (float)raw`。
- RUN_END 与 result.json 同源：同一 `Bug1Verdict` 分别序列化（`:883-912`、`:1255-1278`），Bug1-60 逐字段断言。

## 4. 真实 trace 三方对照（本机复跑输出）

`bug1-20261107T044218Z/HOST/trace.jsonl`，`recorded=6338`、`run_end_frame=14459`：

| 上下文 | 结果 |
|---|---|
| 修复前（raw，不裁剪） | `Unknown/MISSING_PHASE`，tail 段缺相位条目 > 0（200 帧） |
| `session_reset`（目标已销毁） | `tail_excluded=0`、`Unknown/CAPTURE_FAILURE_TAIL` |
| `target_left`（目标已销毁、detour 健康） | `head_excluded=1`、`tail_excluded=1043`、`boundary_player_destroyed`、`NoThresholdEvent/NO_CANDIDATE_RUN`、`genuine=0`、`out_of_window=head+tail` |

同一份字节，仅上下文不同 → 三种结局，证明"是来源决定裁剪"而非"相位齐不齐"。M1（把 session_reset 加回合法集合）时该用例直接报 `session_reset 不得裁剪尾部，实际 tail_excluded=1043`，说明此对照确实承重。

## 5. 独立变异矩阵（本机实跑）

副本 `_t66iso`；每条：`-t:Rebuild` 生产 → 重建测试项目 → 覆盖两处 DLL → 跑 exe。日志 `D:/tmp/t66-mut.log`、`D:/tmp/t66-<TAG>.log`。

| 变异 | 位置 | 变异 DLL SHA-256 | 结果 | 新增红 |
|---|---|---|---|---|
| M0 基线 | — | `7168ace4…`（=送审） | 754/756 | GBA01/NIL01（环境） |
| M1 session_reset 加入合法集合 | `Bug1ProbeWindowBoundary.cs` ClassifyTail 末尾 | `9e04ee55beeaa7a05815f2b48886df5a3985f96769460f44fa9a47271d17799d` | 750/756 | Bug1-61、62、62b、68 |
| M2 尾部一律裁剪 | 同上 `:131` | `9748ad2a6f635f42a2db3a9c9d25f785523e9990eb602e73da566c7d9a8efcdf` | 748/756 | Bug1-61、62、62b、64、68、69 |
| M3 删 `Player.onPlayerDestroyed` 订阅 | `Bug1ProbeRuntime.cs:377` | `76a2b99f22777de804eeab62f4568952afbcac57ce700dc9dbb2585bcac32783` | 753/756 | Bug1-67 |
| M4 out_of_window 改回 0 | `Bug1ProbeJudge.cs:164` | `0b6a36198dc01b8b4aeb6db9b4bbd37402f79a4999f699205c705f24249b5e05` | 750/756 | Bug1-60、61、63、66 |
| R0 还原 | 三源文件 | `7168ace4…` | 754/756 | 仅环境两条 |

还原后源码哈希：`c3a98c00…` / `f41613c0…` / `114742df…`（与 §2 送审值逐字节相同）。M4 的 Bug1-60 失败转储中出现了完整的 RUN_END，可见 `window_tail_excluded:3`、`window_tail_render_only:3`、`window_tail_boundary:"boundary_player_destroyed"` 与 `capture_state:{...,"first_frame":0,"tail_boundary":"boundary_player_destroyed"}` —— `capture_state` 五要素与首帧号确实到达落盘层。

## 6. 残留与边界（pass ≠ 可出包）

1. **pass 只覆盖"窗口账目与归因正确、且这些判断可被测试证伪"**，不覆盖：真实 Mono 宿主上 `Queue<PlayerInputPacket>` detour 是否真的触发、1H+1G 实机能否采集到完整窗口、Bug1 根因是否已定位、0.2.4.11 是否可出包。t65 报告 §11 已作同一声明，本轮不追加任何"已诊断"措辞。
2. **仍需 1H+1G**：①BCL 队列 detour 在 Mono 下的真实触发率（失败形态=窗口缺样→UNKNOWN，不得伪造）；②目标销毁事件在真实对局中的触发时序（本轮只证"若触发则归因正确"）；③`isDismissed` 是否存在；④热切换 cfg 与采集生命周期的交互。
3. **t63 遗留**：`RUN_END` 仍缺 spec §2 要求的 phase 覆盖/丢记录/溢出/hash 全字段（本轮只补齐 window_* 与 capture_state），归属后续票。
4. **口径待 Captain 确认**：§3.4 的恒等式措辞（建议改用 `head+tail+remaining==原帧数`）。
5. 本报告未评代码风格、命名与注释密度（属工程轴）。

## 7. 审查边界声明

- 主树未修改任何生产/测试/项目文件；`git status` 与被审前一致，§2 哈希为审查结束时复核值。
- 全部变异在 `_t66iso` 副本内完成；该副本由主树 tar 复制（排除 `.git/bin/obj/.scratch/Release/publishi`），基线 DLL 与主树逐字节一致后开始。
- 未运行真实 Unity 主机；所有结论限于"离线可判"层面。
- 本报告正文（不含本行）12013 字节，sha256 = `2cb97069eff8c01b37e217739581600492638bcee1d551900a343c2f5db65595`；t65 报告 `bug1-entry-t65-window-boundary-provenance.md`（`ce13b1dc…`）仅作为被审方自述参考，不参与判定。
