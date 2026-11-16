# Bug1 仪表 · 窗口收口边界归因（t65 / repair-round-2）

- 任务：t65，attempt_id `d0cc9eef-1ae8-41b9-8ce3-1f67d067c03b`
- 基线：t60 交付态（`Bug1ProbeWindowBoundary.cs` `4217568a…`、`Bug1ProbeJudge.cs` `72282130…`、`Bug1ProbeRuntime.cs` `e6d3bc26…`、DLL `d6da5835…`）
- 收口依据：`m01360`（captain 合并 t62/t63 两轴 findings 后的七条验收）

## 1. 本轮修的三件事

| 编号 | 问题 | 修法 |
| --- | --- | --- |
| F1 (high) | `Finalize(window)` 只看「相位齐不齐」，`EndStep(reason)` 的 reason 从未传进来；`BEFORE_UPDATE` detour 中途停掉而 `RENDER_READY` 仍在建帧时，这段 render-only 尾部与越界帧同形，被 `TailExcluded` 一起裁掉 → `phase_missing_genuine=0` → 落 `NoThresholdEvent` | `Finalize(window, ctx)` 引入边界归因；合法裁剪集合收紧为 `{target_left, level_changed}` **且采集面健康**；render-only 尾部单独计数并在 Judge 里硬判 `UNKNOWN(PHASE_STOPPED_MID_WINDOW)` |
| F2 (medium) | Bug1-61 的「真实 trace 回放」对全部帧硬编码 `HasRenderReady=true`，把「无记录帧」变成「render-only 帧」，sealed 后无 MISSING_PHASE 属自证 | 改为从 `trace.jsonl` 逐行解析真实 `kind/phase/frame_count`，`RENDER_READY` 与 `BEFORE_UPDATE` 分别成集合，再回放 |
| F3 (medium) | `phase_missing_out_of_window` 恒 0、无生产者，却在 RUN_END 与 result.json 导出 | 改为真实测量值 `head_excluded + tail_excluded` |

`session_reset` 只收口不解释：它发生在任何一局末尾（包括仪表已坏的那局），所以它不构成「尾部为什么停」的证据。

## 2. 关键语义（避免把「候选」和「实际裁掉」混为一谈）

`Bug1WindowBounds` 现在区分两件事：

- `TailCandidateCount`：末个齐全帧之后的帧数（不管裁不裁）；
- `TailExcluded`：**真的被 `RemoveRange` 移除的帧数**（只有合法边界才等于 `TailCandidateCount`）。

其余新增：`TailFirstFrame/TailLastFrame`、`TailBoundaryKind`、`HeadBoundaryKind`、`TailUnexplained`、`TailRenderOnly`。

`ClassifyTail(tailCount, ctx)`：

| 条件 | kind | 是否裁剪 |
| --- | --- | --- |
| `tailCount <= 0` | `none` | 否 |
| `ctx == null` 或 `ctx.HookFailClosed` | `suspected_capture_failure` | 否 |
| `reason == target_left` 且 `!ctx.TargetAlive` | `boundary_player_destroyed` | 是 |
| `reason == level_changed` | `boundary_level_changed` | 是 |
| 其余（含 `session_reset` / `config_changed`） | `suspected_capture_failure` | 否 |

头部裁剪保持不变（开窗时机造成，与采集中途失效无关），但同样计入 `HeadExcluded`。

## 3. Judge 的两道新门（都在 `ValidatePhaseCoverage` 之前）

1. `WindowTailRenderOnly > 0 && !IsLegalBoundary(WindowTailBoundaryKind)` → `Unknown / PHASE_STOPPED_MID_WINDOW`（missing_fields：`window_tail_render_only`、`window_tail`）。
2. `WindowTailUnexplained` → `Unknown / CAPTURE_FAILURE_TAIL`（missing_fields：`capture_tail@<TailFirstFrame>`、`window_tail`）。

`PhaseMissingOutOfWindow` 改为 `WindowHeadExcluded + WindowTailExcluded`，导出值与判定同源。

## 4. 一个必须记下的环境坑（UnityAlive）

第一版 `BuildBoundaryContext` 直接写 `_target.gameObject`。离线控制台（.NET Framework + 非 Unity 进程）在 **JIT 期**就抛
`System.Security.SecurityException: ECall 不允许调用系统模块`——这**不是**运行时异常，`try/catch` 接不住，直接让 `EndStep` 整体失败、trace.jsonl 不落盘。

修法：把 Unity 对象存活判定抽成 `[MethodImpl(MethodImplOptions.NoInlining)] private static bool UnityAlive(UnityEngine.Component obj)`，只有真实 Unity 进程才会 JIT 到它；`BuildBoundaryContext` 只做值传递。参数类型必须是 `Component`（`UnityEngine.Object` 没有 `gameObject`）。

## 5. 用例（新增 9 条 / 加强 3 条）

| 编号 | 断言要点 | 新加/加强 |
| --- | --- | --- |
| Bug1-55 | `Finalize(w, ctx)` 后 `TailCandidateCount == 3` | 加强（改签名 + 加候选断言） |
| Bug1-60 | 生产 `EndStep("target_left")` + 目标已销毁 → `window_tail_boundary=boundary_player_destroyed`、`phase_missing_out_of_window:3`、trace 落盘 | 加强 |
| Bug1-61 | 真实 trace 三段回放：raw=`Unknown/MISSING_PHASE`；`session_reset` 收口=`Unknown/CAPTURE_FAILURE_TAIL` 且 `tail_excluded=0`；`target_left` 收口=`NoThresholdEvent` 且 `tail_excluded=1043`、`out_of_window=head+tail` | 重写（真实解析，不再硬编码） |
| Bug1-62 | `session_reset` + 目标已销毁 → `Unknown/CAPTURE_FAILURE_TAIL`，`window_tail_excluded==0` | 新加 |
| Bug1-62b | `session_reset` + 采集面健康 → 仍 `Unknown`（证明 session_reset 永不合法） | 新加 |
| Bug1-63 | `target_left` + 健康 → `NoThresholdEvent`、`boundary_player_destroyed`、`tail_excluded==3` | 新加 |
| Bug1-64 | `config_changed` + 健康 → `Unknown/CAPTURE_FAILURE_TAIL`，不裁 | 新加 |
| Bug1-65 | 行为级：Enable → 三条边界回调（`OnPlayerDestroyedBoundary` / `OnLevelLoadedBoundary` / `OnSessionReset`）→ 读 trace.jsonl 的 `RUN_END.end_reason` = `target_left` / `level_changed` / `session_reset` 且 `!Enabled` | 新加 |
| Bug1-66 | `head + tail + remaining == 原帧数`（9 = 2+3+4），`remaining==4`、`TailCandidateCount==3`、`genuine==1`、`out_of_window==5` | 新加 |
| Bug1-67 | IL：`HookBoundaryEvents` 必须引用 `Player.onPlayerDestroyed` 与 `Level.onLevelLoaded` | 新加 |
| Bug1-68 | render-only 尾部 + 非边界原因 → `PHASE_STOPPED_MID_WINDOW` 且 `tail_excluded==0`；合法边界下同形状允许裁 | 新加 |
| Bug1-69 | detour 已 fail-closed 时，即使 `end_reason=target_left` 也归 `capture_failure`、不裁、判 `CAPTURE_FAILURE_TAIL` | 新加（变异 M-I 暴露后补） |

关于「head+tail+interior+remaining」：`interior` 半帧仍在判定窗内，若把它与 `remaining` 相加会重复计数。因此身份式写成
`head + tail + remaining == 原帧数`，并单独断言 `InteriorPartial`、`genuine` 与 `out_of_window`，不重复计。

## 6. 真实 trace 回放（Bug1-61 实测输出）

```
[T60] replay trace=…\spf-bug1\bug1-20261107T044218Z\HOST\trace.jsonl
      recorded=6338 run_end_frame=14459
      raw=Unknown/MISSING_PHASE raw_tail_missing=200
      reset=Unknown/CAPTURE_FAILURE_TAIL reset_tail_excluded=0
      sealed=NoThresholdEvent/NO_CANDIDATE_RUN head_excluded=1 tail_excluded=1043 genuine=0
```

读法：同一份真实 trace，未归因时尾部 1043 帧被判缺相位（`Unknown/MISSING_PHASE`）；用 `session_reset` 收口时**不解释**（仍是 `Unknown/CAPTURE_FAILURE_TAIL`，`tail_excluded=0`）；只有 `target_left`（目标已销毁）才允许把这 1043 帧作为边界裁掉，此时窗内 `genuine=0`，判定落到 `NoThresholdEvent`。这三个分支都由真实字段驱动，没有任何硬编码相位。

## 7. 三命令

| 命令 | 结果 |
| --- | --- |
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 0 警告 / 0 错误 |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | 4 条既有警告 / 0 错误 |
| `./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `=== Final Result: 754/756 PASS (Failed: 2) ===` |

失败仅两条既有环境失败：`[PureMemory] GBA01 Native First Observe Binding`、`[PureMemory] NIL01 Real Native Lookup Contract`（`TypeInitializationException: SDG.Unturned.SteamPlayer` 链）。Bug1 段 **70/70 PASS**。exe 退出码 1 属既有约定（有失败即返回 1）。

## 8. 逐文件 sha256（交付态，磁盘真实值）

| 文件 | 字节 | 行数 | sha256 |
| --- | --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1ProbeWindowBoundary.cs` | 14154 | 285 | `c3a98c0010f1f557dc1065bb29d39d5d73657372725ddd19da1bb5d762cdf944` |
| `Platform/Diagnostics/Patches/Bug1ProbeJudge.cs` | 26438 | 557 | `f41613c0194dc36716d2b17458956e0bc025d44e0323db40ccc834083de8f314` |
| `Platform/Diagnostics/Patches/Bug1ProbeRuntime.cs` | 70006 | 1317 | `114742df5e12f647964842f3df8d8078ac363ee51e08d3b94b393eeba0048a5c` |
| `Platform/Diagnostics/Patches/Bug1ProbeControl.cs`（本票未改） | 23724 | 456 | `53a6c36bb809f7fc22a748823fbd2864d4bdc909cef76d39d75fc69cc55d617f` |
| `SteamP2PFriends.csproj`（未改，无新增生产文件） | 27575 | — | `ed629ae7064015d55d2e1bee15b01ba1eb6a7a565dcdb80ac36892499350b948` |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeT60Tests.cs` | 24658 | 409 | `154ac184a22b7dd989a3c8cc10efcb7e9f553203cce6b0cc222402f5e9d25409` |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeT65Tests.cs` | 17970 | 321 | `be418ae2bbd24e54e05182608bb0313211145306facbfb5b121c1ab0943f3514` |
| `WhitelistTests/Program.cs` | 144661 | 1071 | `f61df0d20f3e8bc2720a6f013bd9513df441b8187fc20392aeb2bd09d1bc9a24` |
| `bin/Release/SteamP2PFriends.dll` | 1496576 | — | `7168ace45a67cacd4578aac7789c11c38ed1d00af87317262d4b563628a3dd6f` |

`WhitelistTests/Program.cs` 的 sha256 是磁盘真实值：其中同时包含 ds-fixer/t69 在并发窗口写入的 `T69-01` 注册行（`Program.cs:900` 附近），该行与本票改动互不影响，本票未删改它，T69-01 当前 PASS。

## 9. 变异（仓库外副本 `../_t65copy`，驱动脚本 `../_t65mut/mut.py`）

副本与主树同深度（`..\..\Libs` 解析一致），`tar` 复制时排除 `bin/obj/.git/.scratch/Release/publishi`、保留 `Build/Version.props`。基线 DLL 与主树逐字节一致（`7168ace4…`，754/756）。每条变异后先从主树 `cp` 回被改文件再重建，还原后源码哈希回到主树值。

| 变异 | 改法 | 变异后 DLL | 红名单 | 总数 |
| --- | --- | --- | --- | --- |
| M-E | `ClassifyTail` 把 `session_reset` 也判成 `boundary_player_destroyed`（即 session_reset 进合法集合） | `2455dcd086f5d8b9e7788535425dc57308166fed21aaeca97367f431f6ce7a07` | Bug1-61、Bug1-62、Bug1-62b、Bug1-68 | 749/755 |
| M-F | `bounds.TailExcluded = legalTail ? tailCount : 0;` → `= tailCount;`（非法边界也裁，含 render-only） | `50ca010b74ed438b0ed75eb8565c0db048cc90dcdbb4a26f651ded57e3939878` | Bug1-61、Bug1-62、Bug1-62b、Bug1-64、Bug1-68 | 748/755 |
| M-G | 删订阅 `Player.onPlayerDestroyed += OnPlayerDestroyedBoundary;` | `5980f00c2630c64e86e7e49747c9c56d991798b3fc80964e728b04ec6acc745d` | Bug1-67 | 752/755 |
| M-H | `verdict.PhaseMissingOutOfWindow = …HeadExcluded + …TailExcluded;` → `= 0;` | `255c2a68b52551b7dfd8649a431f500260197ccf79f2960aa9bc03d9e3fccd66` | Bug1-60、Bug1-61、Bug1-63、Bug1-66 | 749/755 |
| M-I | `ctx.HookFailClosed` 分支返回 `boundary_player_destroyed`（fail-closed 也当合法边界） | `82030e489ef598953372e9621bfd118caa142b30099918c5ed208ca3611cc9d1` | Bug1-69 | 753/756 |
| M-J | `OnPlayerDestroyedBoundary` 去掉引用比较与 `Disable("target_left")` | `ffc0148cd383bf0b69ec051f7524a0209742d18622e2b00ccc9c8446c4522dbc` | Bug1-65 | 752/755 |

说明：
- M-E/M-F 会连带打红多条（同一语义被多个用例从不同角度钉住），这正是收紧后的预期。
- M-I 第一次跑（未加 Bug1-69 前）**没有变红**（753/755），暴露出「fail-closed 边界仍被当合法」这个洞没有用例；补 Bug1-69 后 M-I 变红，主树同时保持 754/756。
- 每条变异后副本重建均回到基线 `7168ace4…`；副本已在核验后删除。

## 10. 已自动化可证 / 仍需实机

**已自动化可证（本轮 green 的范围）**

1. 合法裁剪集合只有 `{target_left, level_changed}` 且要求采集面健康；`session_reset`、`config_changed`、任意其它 reason 都不解释尾部（M-E 红）。
2. render-only 尾部在非边界原因下判 `UNKNOWN(PHASE_STOPPED_MID_WINDOW)` 且不计入 `tail_excluded`（Bug1-68；M-F 红）。
3. `phase_missing_out_of_window` 有真实生产者，且 `head + tail + remaining == 原帧数`（Bug1-66；M-H 红）。
4. 三条边界回调都能真的收口窗口并写出正确 `end_reason`（Bug1-65；M-G、M-J 红）。
5. 真实 trace 的三段回放：raw 判 `MISSING_PHASE`、`session_reset` 不解释、`target_left` 合法裁剪。

**仍需 1H+1G 实机（本轮**没有**证明、也不许写成「Bug1 已诊断」）**

1. Mono 下 BCL `Queue<PlayerInputPacket>` 的 `Enqueue/Dequeue` detour 是否真的触发（离线进程只能装这两条 patch，5 条 Assembly-CSharp 原生 patch 报 `HarmonyException: IL Compile Error (unknown location)`）。
2. `Player.onPlayerDestroyed` / `Level.onLevelLoaded` 在真实关卡切换与玩家离场时序下是否按预期回调、`target_left` 是否总能在窗口变成 render-only 之前到达。
3. `PlayerInput.isDismissed` 是否存在、`ReceiveInputs` 提前拒绝分支是否成立。
4. `capture_state` 五要素在真实主机上的取值（尤其 `hook_fail_closed` 与 `last_hook_error`）。

本轮只产出**测量与归因能力**：它让「尾部为什么被裁/没被裁」可被事后审计，不构成 Bug1 根因结论。

## 11. 边界核验

- Bug3 五文件 sha256 未变：`a89f40b949f841d03eb1a7b2…`（P2PGroupStateRecoveryPatch.cs）、`7a82de8e38fa0f6a00e3dbf4…`（GroupStateRecoveryCore.cs）、`3d5044f7870fbb175bdcfba8…`（GroupLeaseLedger.cs）、`a45533850f31bd96f3d63e39…`（GroupLoadPlanner.cs）、`5b0c4e9ba4c87cbeb7363b38…`（P2PGroupRegistration.cs）。
- `Release/`、`publishi/`：`find … -newermt "2026-11-07 13:00" -type f` 无输出，本票未写。
- 未动：`Platform/UI/SteamPersonaDisplay.cs`（O-01 由 captain 另开票）、`WhitelistTests/Evidence/Runtime/SteamPersonaDisplayPlayerIdGuardTests.cs`、t69 的注册行。
- `SteamP2PFriends.csproj` 未改（本票没有新增生产文件）。
