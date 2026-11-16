# T80 / T82 闭合事件优先级（CLOSED_EVENT_PRIORITY-v1）实现报告

- 任务：t80（repair，attempt 2，attempt_id `7aa8d3c7-ba25-4496-8e65-4cb07fe42063`）
- 裁决依据：`.scratch/takeover-v05-20261106/architecture-decision-t79-closed-event-priority.md`
  （110 行，SHA-256 `a1a1bd60f9fbdd93d49cb96693995b111eb81894c1d8e4f4aac3f2e4004e41da`）
- 交付口径（先写在最前）：本次只改**判定器的聚合语义**，不修角色顿挫、不改采样与阈值、不产根因结论。
  「零 M1」不等于 Bug1 已修复；`M1StallCatchup` 仍只是「该步内出现一次证据闭合的落后-回补事件」，
  不是「Bug1 已被真实数据证实」。历史 `result.json` 与旧结论一律保留，新口径只以 revision 标注。

---

## §1 裁决书映射（逐条 → 落点）

| 裁决要求 | 实现落点 |
| --- | --- |
| 正常尾段无未来释放 → 只记该段 `RIGHT_CENSORED`，不推翻更早闭合事件 | `Bug1ProbeJudge.ClassifyHorizontalMotion`：`e + 1 >= frames.Count` 分支不再 `MarkUnknown + Valid=false + return`，改为 `RightCensoredSegments++`、追加 `Bug1RightCensoredRange`、`break` |
| 裸 `w.RightCensored` 的全局否决必须撤掉，改为「能定位到尾段才放行」 | `ValidateContext` 删除原 `if (w.RightCensored) MarkUnknown(...)`；`Judge` 在分类后加 `window.RightCensored && RightCensoredSegments == 0` → `UNKNOWN / RIGHT_CENSORED`（定位不到即不放行） |
| 唯一汇总顺序：闭合 M1 > 闭合 AUTH > 仅右截断 Unknown > 完整无事件 NoThresholdEvent | `Judge` 汇总段按此顺序生成 label/reason；纯截断分支显式清空三元组与 `LagWitnessFrame`、`SelectedEventComplete=false`、`Valid=false` |
| `strict_m1_events` = 已闭合且完成全部验证的 M1 件数 | `verdict.StrictM1Events = closedM1`（由 `ClosedEvents` 里 `Kind=="M1"` 计数得出，不另写一套） |
| 候选三元组必须是代表闭合事件的真实帧号，不得被尾段覆盖/捏造释放 | 代表段在 `ClassifyHorizontalMotion` 收尾统一写 `CandidateA/B/ReleaseFrame`；尾段分支只写 `RightCensoredRanges`，不碰三元组 |
| `selected_event_complete` | `Judge` 末尾 `CandidateA >= 0` 时 `SelectedEventComplete = IsRepresentativeEventComplete(verdict)`（按 a/b/release 在 `ClosedEvents` 中找代表件，取其 `EvidenceComplete`） |
| `event_scan_complete` / `strict_m1_events_is_lower_bound` | `EventScanComplete = RightCensoredSegments == 0`；`StrictM1EventsIsLowerBound = RightCensoredSegments > 0` |
| `right_censored_segments` / `right_censored_ranges` | `Bug1RightCensoredRange{StallStartFrame, StallEndFrame, ReleaseFrame=-1, Reason="RIGHT_CENSORED"}`，与正式候选分开导出 |
| `closed_events`（kind/a/b/release/evidence_complete），且 M1 件数必须等于 `strict_m1_events` | `Bug1ClosedEvent`；`ValidateClosedEventAssociations` 逐件写 `EvidenceComplete`；计数由同一列表派生 |
| 关联必须**每件**在各自 `[a, release]` 内验证，不能只验最长代表 | `ValidateClosedEventAssociations` 对每件调 `ValidateCandidateAssociations(w, v, closed.StallStartFrame, closed.ReleaseFrame)`，任一失败 → `UNKNOWN`、`Valid=false`、`SelectedEventComplete=false` |
| 只局部化「尚无未来 release」；其余完整性失败仍 UNKNOWN | `ValidatePhaseCoverage` / `ValidateContext` / `ValidateFrames` 全部保持原样与优先；`WindowTailUnexplained`、`PHASE_STOPPED_MID_WINDOW`、`CAPTURE_FAILURE_TAIL` 两道门未动 |
| 代表选择：同类停段最长 → 最早 a → 最早 release；M1 优先于 AUTH | `IsBetterRepresentative(length, aFrame, releaseFrame, bestLength, bestAFrame, bestRFrame)`；收尾 `bestM1Start >= 0 ? M1 : AUTH` |
| 导出器与汇总器同轮更新，`RUN_END` / `result.json` / `manifest` 同源 | `Bug1ProbeRuntime`：三处都从同一 `Bug1Verdict` 读字段（见 §2.3） |

## §2 生产改动

### 2.1 `Platform/Diagnostics/Patches/Bug1ProbeModel.cs`

- `Bug1ProbeConstants` 新增：
  - `public const string EventAggregationRevision = "CLOSED_EVENT_PRIORITY-v1";`
  - 注释写明：右截断只局部生效、事件计数是下界、与 `M1RuleRevision` 并存、不覆盖历史产物。

### 2.2 `Platform/Diagnostics/Patches/Bug1ProbeJudge.cs`

新增 DTO：

- `internal sealed class Bug1ClosedEvent { string Kind; int StallStartFrame; int StallEndFrame; int ReleaseFrame; bool EvidenceComplete; }`（`Kind` 取 `"M1"` / `"AUTH"`）
- `internal sealed class Bug1RightCensoredRange { int StallStartFrame; int StallEndFrame; int ReleaseFrame = -1; string Reason = "RIGHT_CENSORED"; }`

`Bug1Verdict` 新增：`EventAggregationRevision`（默认取常量）、`SelectedEventComplete`、`EventScanComplete`、
`StrictM1EventsIsLowerBound`、`RightCensoredSegments`、`RightCensoredRanges`、`ClosedEvents`。

`ValidateContext`：删除裸 `w.RightCensored` 的全局否决（原 `:318`），改注释说明交由分类后确认能否定位真实尾段。

`Judge` 汇总段（现 `:271`–`:352`）：

1. `ClassifyHorizontalMotion(window, verdict)`；
2. `!verdict.Valid` → `Unknown`（几何/完整性失败仍然先失败关闭）；
3. `ValidateClosedEventAssociations` 失败 → `Unknown` + `Valid=false` + `SelectedEventComplete=false`；
4. `window.RightCensored && RightCensoredSegments == 0` → `MarkUnknown("step_end_boundary","RIGHT_CENSORED")`，
   并**显式**把 `Reason` 固定为 `RIGHT_CENSORED`（否则会被几何原因 `NO_CANDIDATE_RUN` 占位），`Valid=false`；
5. `EventScanComplete` / `StrictM1EventsIsLowerBound`；
6. `StrictM1Events = closedM1`（同源不变量）；
7. 汇总：`closedM1 > 0` → `M1StallCatchup/M1_STALL_CATCHUP`；否则 `ClosedEvents.Count > 0` → `AuthStepLocked/AUTH_STEP_LOCKED`；
   否则 `RightCensoredSegments > 0` → `Unknown/RIGHT_CENSORED`（清三元组、清 `LagWitnessFrame`、`SelectedEventComplete=false`）；
   否则 `NoThresholdEvent/NO_THRESHOLD_EVENT`；
8. `CandidateA >= 0` 时写 `SelectedEventComplete` 与 `EventFrames`。

`ClassifyHorizontalMotion`：

- 尾段分支（`e + 1 >= frames.Count`）：只记局部 censored 段并 `break`；
- M1 分支：不再 `StrictM1Events++`，改 `ClosedEvents.Add(new Bug1ClosedEvent { Kind = "M1", ... })`，
  并用 `IsBetterRepresentative` 维护代表段；
- AUTH 分支：同样入 `ClosedEvents`（`Kind = "AUTH"`）并维护代表；
- 收尾：代表段优先 M1，统一写 `CandidateA/B/ReleaseFrame`、`RenderRootSpanXZ`、`SimRootSpan3D`、落后见证与 lag 前后值；
  无代表时 `Reason = HorizontalStallSegments == 0 ? "NO_CANDIDATE_RUN" : "NON_M1_HORIZONTAL_PATTERN"`。

新增私有助手：`IsRepresentativeEventComplete`、`ValidateClosedEventAssociations`、`IsBetterRepresentative`。

### 2.3 `Platform/Diagnostics/Patches/Bug1ProbeRuntime.cs`

三处导出同源（全部读同一个 `Bug1Verdict`）：

- `RUN_END` extra：`event_aggregation_revision`、`selected_event_complete`、`event_scan_complete`、
  `strict_m1_events_is_lower_bound`、`right_censored_segments`、`right_censored_ranges`、`closed_events`；
- `result.json`：同名字段（紧跟 `strict_m1_events`）；
- `manifest.json`：`event_aggregation_revision`（常量）+ 上述布尔/段数/`closed_events`。

新增 JSON 助手：`ClosedEventsJson(List<Bug1ClosedEvent>)`、
`RightCensoredRangesJson(List<Bug1RightCensoredRange>)`。

## §3 测试改动（按裁决「扩子例、不新开编号」）

`WhitelistTests/Evidence/Runtime/Bug1ProbeM1RuleTests.cs`：

- 新增 `WindowFrom(int firstFrame, int frameCount, double speedCap = SpeedCap)`，`Window(n)` 转调之（支持 100 起始的规格例）；
- **Bug1-70 / R01** 追加三个子例：
  1. 闭合 M1 + 4 帧静止尾段（`Window(9)`）→ `M1`、`Valid`、`StrictM1Events==1`、三元组 `0/3/4`、
     `SelectedEventComplete`、`!EventScanComplete`、`StrictM1EventsIsLowerBound`、
     `RightCensoredSegments==1` 且范围 `[4,8]`、`release=-1`、`reason=RIGHT_CENSORED`、
     `ClosedEvents` 恰一件 `M1(0,3,4)`、`EventAggregationRevision=="CLOSED_EVENT_PRIORITY-v1"`；
  2. **裁决书帧级规格例**（`WindowFrom(100, 11)`）→ 断言见 §4；
  3. 两件闭合 M1 + 尾段（`Window(14)`，两段等长）→ `StrictM1Events==2`、`ClosedEvents` 两件 M1、
     代表 `0/3/4`（等长取最早 a）、尾部 `[8,13]`。
- **Bug1-71 / R02** 追加：闭合 AUTH + 静止尾段 → `AuthStepLocked`、`Valid && SelectedEventComplete`、
  `!EventScanComplete && IsLowerBound`、`StrictM1Events==0`、三元组 `0/3/4`、`ClosedEvents` 一件 `AUTH`。
- **Bug1-78 / R08** 追加：R08 夹具扩到 17 帧并接静止尾段 → 仍 `M1`、`StrictM1Events==1`、
  代表仍 `8/11/12`、`ClosedEvents==2`、`RightCensoredSegments==1`。
- **Bug1-80 / R10** 追加：
  - 纯右截断（`Window(4)`）→ `StrictM1Events==0`、三元组全 `-1`、`RightCensoredRanges[0] == [0,3]`/`release=-1`、
    `!EventScanComplete && IsLowerBound`、`!SelectedEventComplete`；
  - **负控**：早段闭合 M1 + 尾部真实缺相位（`Frames[7].HasBeforeUpdate=false`）→
    `Unknown`、`Reason=="MISSING_PHASE"`、`!Valid`、`StrictM1Events==0`（证明只豁免「尚无未来 release」一条）。

`WhitelistTests/Evidence/Runtime/Bug1ProbeJudgmentTests.cs`：

- **Bug1-01**（canonical）追加「闭合 M1 + 静止尾段」子例：label/valid/计数/三元组不变，两布尔与范围 `[4,8]` 正确；
- **Bug1-06**（右截断）追加：
  - 纯截断断言：`StrictM1Events==0`、三元组 `-1`、`RightCensoredRanges[0].ReleaseFrame==-1`、`!Valid && !EventScanComplete && IsLowerBound`；
  - **外部标志无法定位**（平滑窗口 + `RightCensored=true`）→ `Unknown/RIGHT_CENSORED`（定位不到不放行）；
  - **外部标志可定位**（M1 + 尾段 + `RightCensored=true`）→ 仍 `M1StallCatchup`。

`WhitelistTests/Program.cs`：**未改**（只扩子例，用例编号仍 Bug1-01…Bug1-81，注册总数不变）。

## §4 裁决书帧级规格例（实测）

夹具（`WindowFrom(100, 11)`，dt=1/75、cap=5、Y/Z=0）：

| frame | 100 | 101 | 102 | 103 | 104 | 105…110 |
| --- | --- | --- | --- | --- | --- | --- |
| A.x | 0.00 | 0.02 | 0.04 | 0.06 | 0.08 | 0.08 |
| V.x | 0.00 | 0.00 | 0.00 | 0.00 | 0.08 | 0.08 |

实测输出（全部断言通过）：

- `label = M1StallCatchup`、`reason = M1_STALL_CATCHUP`、`valid = true`
- `strict_m1_events = 1`、`candidate_a/b/release_frame = 100/103/104`
- `selected_event_complete = true`、`event_scan_complete = false`、`strict_m1_events_is_lower_bound = true`
- `right_censored_segments = 1`，范围 `a=104, b=110, release_frame=-1, reason=RIGHT_CENSORED`（**不是** `[100,110]`）
- `closed_events = [M1(100,103,104,evidence_complete=true)]`

## §5 改动前后 sha256

| 文件 | 改前 | 改后 |
| --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1ProbeJudge.cs` | 700 行 `ee0ef9f0b4fda028a177311624bf722ae21dc3c5ef68648fb76a8ef1c15708b1` | 856 行 `9db37e18f7c92d0801ec7733457990e32fc78425d60a7ee68f6b054a47e0a41e` |
| `Platform/Diagnostics/Patches/Bug1ProbeModel.cs` | 135 行 `daf08c53309795d28139d6bd4d1933bc5d6f95c0b5042a0cae6f9f4628f1437f` | 143 行 `79ef2f47250adf6016a8e5ca538336c31c1cfb512cdb4512f9a8a04f9e568f4c` |
| `Platform/Diagnostics/Patches/Bug1ProbeRuntime.cs` | 1355 行 `a66aee9a934190131278bae84557c78a03a2436445114785634b9d1e79876c69` | 1420 行 `0214487e77f590d6a93555b1bf52a08e40a181f04a186dec12236c89abd06bfa` |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeM1RuleTests.cs` | 651 行 `b81d72a4eb72a88a78535f2913407a1b538dd75545a05abfcf7f998af4d0cb8e` | 793 行 `b341b066dc9641e0a534513f7c1282b6ce33e105115b8c15b22c56269813b12c` |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeJudgmentTests.cs` | 319 行 `6ff42723251858d0f9c7164e6828e35667909a2893277cacdd369fd49c805bed` | 364 行 `db9da7417bf6ce111c2eca597b096ace625bce3a81a5379515868152657b89ea` |
| `WhitelistTests/Program.cs` | `71d5268ad401ae842ee97c71ba368f590a1554ba86b0f7dae8087c9949095c8e` | 未改（同值） |
| `SteamP2PFriends.csproj` | `ed629ae7064015d55d2e1bee15b01ba1eb6a7a565dcdb80ac36892499350b948` | 未改（同值） |
| `bin/Release/SteamP2PFriends.dll` | `0e6ebcae13ba7fb0fe88875fd3a3d95136a033c4be92858f7501ae1cd33108c7` | `e648b92dddcfdbba9fc08b3ffbb2ea3658dfa417c329b1c96a88c8eb0dca3761` |

## §6 变异证据（仓库外副本 `../_t80mut`，跑完 `diff` 与主树一致后删除）

副本用 `tar` 从主树复制（排除 `bin/obj/.git/.scratch/Release/publishi`，带 `Build/`）；驱动脚本
`_t80mut/mut.py`：唯一替换 → 先 `dotnet build` 生产、再 build 测试 → 跑 exe 提取 FAIL 与 Final Result →
打印变异后 DLL sha256 → 从主树 `cp` 还原并打印源码 sha256。

| 变异 | 改法 | 变异后 DLL | 总数 | 红名单 |
| --- | --- | --- | --- | --- |
| MV-1 | 尾段恢复旧行为 `MarkUnknown(v,"right_censored@…","RIGHT_CENSORED") + Valid=false + return`（即推翻同一步已闭合事件） | `38121016e882fe4d49cf70578e6e0140659867335e16c5f5ee2fa1226682ec54` | 760/768 | Bug1-01、Bug1-06、Bug1-70、Bug1-71、Bug1-78、Bug1-80 |
| MV-2 | 代表三元组被尾段覆盖（`CandidateA/B/ReleaseFrame` 取 `RightCensoredRanges[0]` 的 a/b/b，等于捏造释放点） | `66e12f6682232b242e3d53ee272ae2bbf45193bb608e255535571b965339a607` | 762/768 | Bug1-01、Bug1-70、Bug1-71、Bug1-78 |
| MV-3 | `verdict.EventScanComplete = true;` 硬置（扫描完整性撒谎） | `9d9d1045f8be838100e2647c07183e6795677fe120f09ca2a0d8bdaaa7766720` | 761/768 | Bug1-01、Bug1-06、Bug1-70、Bug1-71、Bug1-80 |
| MV-4 | `if (false && !ValidateClosedEventAssociations(window, verdict))`（跳过逐件关联校验） | `2f109c73e46363365302065e2cedee6e41a25b3356971c0b1963b7644bab3df5` | 762/768 | Bug1-80（主目标）、Bug1-01、Bug1-70、Bug1-71 |

说明：

- 四条变异各自至少命中一条**本票新增**的断言：MV-1 命中「闭合 M1/AUTH 被尾段推翻」；MV-2 命中三元组/范围断言；
  MV-3 命中 `event_scan_complete` 完整性字段；MV-4 命中逐件关联（Bug1-80 的 `INPUT_SEQ_MISSING` 子例）。
- MV-4 额外连带 Bug1-01/70/71 是**预期耦合**：跳过该调用同时不再写 `EvidenceComplete`，
  于是 `SelectedEventComplete` 变 false；这不是「被别的条件遮住」，而是同一条路径的两个可观测后果。
- 每轮还原后 `Bug1ProbeJudge.cs` 均回到 `9db37e18f7c92d0801ec7733457990e32fc78425d60a7ee68f6b054a47e0a41e`；
  四轮结束后 `diff` 主树与副本该文件为 `SOURCE_IDENTICAL`，副本 `rm -rf` 已删。

## §7 三命令（主树最终态）

| 命令 | 结果 |
| --- | --- |
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | `已成功生成`，0 个警告、0 个错误 |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | `已成功生成`，0 个错误（4 条既有警告） |
| `./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `=== Final Result: 766/768 PASS (Failed: 2) ===` |

- 失败仅 `[PureMemory] GBA01 Native First Observe Binding` 与 `[PureMemory] NIL01 Real Native Lookup Contract`
  （既有环境失败，`TypeInitializationException: SDG.Unturned.SteamPlayer` 链），与本票改动无关。
- Bug1 段 82/82 全 PASS（本票只扩子例、不新增编号，故条数不变）。
- exe 退出码 1 属既有约定（有失败即 1）。
- 分母恒为 768：R03 有 trace 时执行并通过，不把任何未跑项从分母里减掉；无 trace 时 R03 以
  `InvalidOperationException` 明确失败（765/768），不引入 SKIP 框架。

## §8 边界核验

- Bug3 五文件哈希未变：`a89f40b949f841d0`（`P2PGroupStateRecoveryPatch.cs`）、`7a82de8e38fa0f6a`
  （`GroupStateRecoveryCore.cs`）、`3d5044f7870fbb17`（`GroupLeaseLedger.cs`）、`a45533850f31bd96`
  （`GroupLoadPlanner.cs`）、`5b0c4e9ba4c87cbe`（`P2PGroupRegistration.cs`）。
- `find Release publishi -newermt "2026-11-07 21:50" -type f` 无输出（未写发布目录）。
- `grep -rn "playerID == null\|playerID != null" Platform/Diagnostics/Patches/` → 0 命中。
- 未触碰 `Core/`、`Build/`、`Develop-Stage/`、`docs/`。

## §9 已自动化可证 vs 仍需实机

**已自动化可证（离线、纯内存 Judge 入口）**

- 正常尾段无释放只产生局部 `RIGHT_CENSORED`，不再推翻同一步已闭合的 M1/AUTH；
- 汇总优先级与代表选择（M1 > AUTH > 仅截断 > 无事件；最长→最早 a→最早 release）；
- `strict_m1_events` 与 `closed_events` 中 M1 件数同源；三元组来自代表闭合事件、不被尾段覆盖；
- `selected_event_complete` / `event_scan_complete` / `strict_m1_events_is_lower_bound` 语义与导出；
- 只豁免「尚无未来 release」：真实缺相位（`MISSING_PHASE`）、相机重复、暂停、关联失败、端点未解析、
  时钟异常、外部截断标志无法定位，仍全部 UNKNOWN；
- `RUN_END` / `result.json` / `manifest` 三处字段同源（生产路径用例读盘核对）。

**仍需 1H+1G 实机（不得据本报告宣称已诊断）**

1. Mono 下 BCL `Queue<PlayerInputPacket>` 的 `Enqueue/Dequeue` detour 是否真的触发（离线进程只能装这两条，
   5 条 `Assembly-CSharp` 原生 patch 报 `HarmonyException: IL Compile Error`）；
2. 真实对局里「同一步内先有闭合事件、随后尾部无释放」的窗口是否真的出现，以及其尾段范围是否被正确识别；
3. 逐件关联数据（`inputseq/rx_id/queue_depth`）在真实事件区间内是否完整——不完整时会按设计落到 UNKNOWN。

## §10 未改项

- 采样、`eps`、`D=2cm`、`B_r = cap×dt + eps`、`MinStallDisplacements`、M1-XZ-RELATIVE-v2 的 C_H / 落后见证 /
  回补收敛判据：全部未动（T79 只取代「右截断一律作废整步」的解释）。
- `WhitelistTests/Program.cs` 未改；`SteamP2PFriends.csproj` 未改；未新增源文件。
- 未覆盖或删除任何历史 `result.json`；旧结论按 revision 重判，`event_aggregation_revision` 只作新标注。
- 本票改动了 Judge 的生产字节（`ee0ef9f0…` → `9db37e18…`），**不适用** TEST_STRENGTHEN_ONLY，
  须按流程走双轴复审；本报告不预先批准任何后续字节。


