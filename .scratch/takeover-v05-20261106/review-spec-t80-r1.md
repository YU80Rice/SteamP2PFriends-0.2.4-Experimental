# T81 规格轴复审 t80：CLOSED_EVENT_PRIORITY-v1 是否忠实落地 T79

- 审查官：reviewer-spec（规格轴独立复审），只读判定；未改生产/测试字节。
- 被审：t80（ds-probe2），依 `.scratch/takeover-v05-20261106/architecture-decision-t79-closed-event-priority.md`（`a1a1bd60f9fbdd93d49cb96693995b111eb81894c1d8e4f4aac3f2e4004e41da`，110 行）实现「右截断只局部生效」。
- 结论：**pass**（8 条验收全部成立；附 1 条 low 覆盖缺口，不阻塞，见 §6）。本判定只对 §1 所列字节有效。
- 边界：本票证明的是「判定器聚合语义按 T79 落地」，**不等于 Bug1 已修复、不等于真实数据证实 M1 判据、不等于 0.2.4.11 可出包**；真实 1H+1G 仍为 UNKNOWN（§7）。

## 1. 被审字节绑定

本机实测（主树，审查结束时复取一致）：

| 文件 | 字节 | 行 | sha256 |
|---|---:|---:|---|
| `Platform/Diagnostics/Patches/Bug1ProbeJudge.cs` | 43830 | 856 | `9db37e18f7c92d0801ec7733457990e32fc78425d60a7ee68f6b054a47e0a41e` |
| `Platform/Diagnostics/Patches/Bug1ProbeModel.cs` | 6401 | 143 | `79ef2f47250adf6016a8e5ca538336c31c1cfb512cdb4512f9a8a04f9e568f4c` |
| `Platform/Diagnostics/Patches/Bug1ProbeRuntime.cs` | 79700 | 1420 | `0214487e77f590d6a93555b1bf52a08e40a181f04a186dec12236c89abd06bfa` |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeM1RuleTests.cs` | 46355 | 793 | `b341b066dc9641e0a534513f7c1282b6ce33e105115b8c15b22c56269813b12c` |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeJudgmentTests.cs` | 20294 | 364 | `db9da7417bf6ce111c2eca597b096ace625bce3a81a5379515868152657b89ea` |
| `WhitelistTests/Program.cs` | 147251 | — | `71d5268ad401ae842ee97c71ba368f590a1554ba86b0f7dae8087c9949095c8e`（未改） |
| `SteamP2PFriends.csproj` | 27575 | — | `ed629ae7064015d55d2e1bee15b01ba1eb6a7a565dcdb80ac36892499350b948`（未改） |

交付 DLL：`bin/Release/SteamP2PFriends.dll` = `WhitelistTests/bin/Release/SteamP2PFriends.dll` = 1506816B / `e648b92dddcfdbba9fc08b3ffbb2ea3658dfa417c329b1c96a88c8eb0dca3761`。

与派工描述核对：t80 自报 Judge 700→856 行、Model 135→143、Runtime 1355→1420、M1RuleTests 651→793、JudgmentTests 319→364，与上表行数一致；Program.cs/csproj 未动，与「扩子例、不新开编号」一致。**无字节不符项。**

## 2. 逐条验收

### ① 闭合 M1 不被尾部右截断推翻 —— 成立

- `Bug1ProbeJudge.cs:549-562`：`if (e + 1 >= frames.Count)` 分支只做 `v.RightCensoredSegments++` 与 `v.RightCensoredRanges.Add(...)`，随后 `break`；**不再 `MarkUnknown+return`**，因此先前累计的 `v.ClosedEvents` 与代表三元组全部保留。
- `:283-291`：闭合事件先逐件验证关联，失败才整步 UNKNOWN；`:314-318`：`closedM1 > 0` → `M1StallCatchup / M1_STALL_CATCHUP`，`Valid` 保持 `true`。
- 变异 M1（把尾段恢复成 `MarkUnknown + v.Valid=false + return`，即「推翻」语义）→ 详见 §4，用例 Bug1-01/06/70/71/78/80 变红，证明该行为由用例承重。

### ② 右截断只局部生效 —— 成立

- 截断范围起点取 `frames[s - 1].Frame`（停段首点）、终点 `frames[e].Frame`（窗口末帧），`:554-561`；与正式候选三元组字段分开存放（`Bug1RightCensoredRange` vs `CandidateA/B/ReleaseFrame`），`:117-124`、`:661-679`。
- 代表三元组只从 `bestM1*/bestAuth*` 选出（`:661-679`），代码路径上不存在「用尾段末帧充 release」的赋值。
- 用例 Bug1-70 断言规格例范围恰为 `[104,110]` 且「不得改成 [100,110]」（`Bug1ProbeM1RuleTests.cs:171-174`）；Bug1-71 断言 AUTH 后尾段记为 `[4,8]` 且仍报 AUTH（`:247-254`）。

### ③ 纯尾无闭合事件仍 Unknown —— 成立

- `:324-333`：`closedM1 == 0 && ClosedEvents.Count == 0 && RightCensoredSegments > 0` → `Unknown / RIGHT_CENSORED`、`Valid=false`、三元组与 `LagWitnessFrame` 全 `-1`、`SelectedEventComplete=false`，并保留截断范围。
- 用例 Bug1-06（`Bug1ProbeJudgmentTests.cs:224-231`）断言 `RightCensoredSegments==1`、`ReleaseFrame==-1`、`!Valid && !EventScanComplete && StrictM1EventsIsLowerBound`；R10 纯截断子例（`Bug1ProbeM1RuleTests.cs:538-544`）同样断言。

### ④ 汇总优先级与既有 UNKNOWN 优先的关系 —— 成立

实际顺序（`:250-345`）：`capture_tail` 门 → 相位覆盖门（`:257`）→ `ValidateContext`（`:264`）→ `ValidateFrames`（`:270`）→ 水平分类（`:280`）→ 逐件关联（`:283`）→ 外部 censor 定位失败（`:292`）→ 汇总 `闭合 M1 > 闭合 AUTH > 仅右截断 Unknown > 无事件 NoThresholdEvent`（`:314-337`）。

即：**一切完整性先验全部在汇总之前**，T79 的豁免只在「其它门已通过、只剩未判定的尾段」时生效，与既有 UNKNOWN 优先级无冲突。`MarkUnknown` 在早门里仍直接置 `Label=Unknown` 并 `return`，不会被后续汇总改写。

### ⑤ 五字段语义 + strict 同源 —— 成立

| 字段 | 实现位置 | 语义核对 |
|---|---|---|
| `selected_event_complete` | `:340-346`、`IsRepresentativeEventComplete:348-361` | 由代表三元组反查 `ClosedEvents` 对应件的 `EvidenceComplete`；无代表事件为 `false`（纯截断分支显式置 false，`:332`） |
| `event_scan_complete` | `:302` | `RightCensoredSegments == 0`，有截断一律 false |
| `strict_m1_events_is_lower_bound` | `:303` | `RightCensoredSegments > 0` |
| `right_censored_ranges` | `:117-124`、Runtime `RightCensoredRangesJson` | 逐段 a/b、`release_frame=-1`、`reason="RIGHT_CENSORED"` |
| `closed_events` | `:102-112`、Runtime `ClosedEventsJson` | 每件 kind/a/b/release/`evidence_complete` |

**strict 同源**：`:305-311` 先遍历 `ClosedEvents` 数 `Kind=="M1"` 得 `closedM1`，再 `verdict.StrictM1Events = closedM1`；代码中不存在第二套 M1 计数来源（分类阶段只做 `ClosedEvents.Add`，不再自增 `StrictM1Events`）。导出侧 RUN_END `Bug1ProbeRuntime.cs:923`、result.json `:1315` 均写 `verdict.StrictM1Events`，manifest `:1276` 写 `is_lower_bound`，三处同源。

逐件关联在计数之前：`:283` 的 `ValidateClosedEventAssociations(window, verdict)`（`:369-384`）对每个 `ClosedEvents` 条目以自身 `[StallStartFrame, ReleaseFrame]` 调 `ValidateCandidateAssociations`，任一件失败即整步 `Unknown/Valid=false/SelectedEventComplete=false` 并 `return`（此时 `StrictM1Events` 尚未赋值，保持 0）。符合「不能只验代表段」与「计数前验证」。

### ⑥ 帧级规格例 100..110 逐字段复现 —— 成立

用例构造与裁决 §3 完全一致（`Bug1ProbeM1RuleTests.cs:154-178`）：`WindowFrom(100, 11)`；i=0..3 `V=0, A=0.02i`；i=4..10 `V=A=0.08`；每帧 dt=1/75（`MonoTicks = i*1e7/75`）、cap=5、Y/Z=0。

断言集（同文件 `:163-178`）：`Label==M1StallCatchup`、`Reason=="M1_STALL_CATCHUP"`、`Valid`、`StrictM1Events==1`、`CandidateA/B/Release==100/103/104`、`SelectedEventComplete`、`!EventScanComplete`、`StrictM1EventsIsLowerBound`、`RightCensoredSegments==1 && Ranges[0]==[104,110] && ReleaseFrame==-1`、`ClosedEvents` 恰一件 `M1(100,103,104)`。主树运行 766/768（含 Bug1-70 通过）说明这些断言在送审字节上全绿，逐字段与裁决书一致，无缺项。

补充核对「105..110 不参与其成立」：代表三元组与 `RenderRootSpanXZ`/`LagBefore/AfterReleaseU` 均由 `frames[a..r]` 计算（`:661-679`），尾段帧不在代表事件证明域内。

### ⑦ 豁免边界不越界（六类仍 UNKNOWN）—— 五类有用例、一类仅代码可证

| 类别 | 用例 | 结果 |
|---|---|---|
| 缺相位（尾部真实缺 BEFORE_UPDATE） | Bug1-80 负控 `Bug1ProbeM1RuleTests.cs:566-585` | `Unknown / MISSING_PHASE`，且 `!Valid && StrictM1Events==0`（早段闭合 M1 不豁免）✓ |
| 相机重复 | R10 子例 `:508-514` | `Unknown / MULTI_CAMERA_SAMPLE` ✓ |
| 暂停姿态 | R10 子例 `:517-523` | `Unknown / PAUSE_CROSSED` ✓ |
| 关联失败（缺 inputseq） | `:546-555` | `Unknown / INPUT_SEQ_MISSING` ✓ |
| 端点未解析 | `:557-568` | `Unknown / MODEL_ENDPOINT_UNRESOLVED` ✓ |
| 时钟异常 | **无专测** | 见 §6 T81-F1 |

代码层面这六类都在分类之前返回（`ValidateFrames:484-487` 的 `CLOCK_INVALID`/`CLOCK_NOT_MONOTONIC`；`:569` 的释放 dt 非法），结构上不会被 T79 豁免；但「时钟异常」缺一条可失败用例。

### ⑧ 两条以上变异证明承重 + 红名单点名 —— 成立

见 §4：M1（尾段推翻闭合事件）红 6 条、M2（`EventScanComplete` 硬置 true）红 5 条，均点名到用例编号，并逐字节还原。

### ⑨ R03 fail-closed 与分母 —— 成立

- 有 trace：R03 实际回放并通过（主树与副本均 766/768，`Registered=768`）。
- 无 trace：把测试产物复制到仓库父目录之外（`D:/tmp/t81-full`，无 `启动器` 祖先）后运行 → `=== Final Result: 765/768 PASS (Failed: 3) ===`，唯一新增失败是 `Bug1-72 R03 … R03 需要真实 trace 输入但未找到`，抛 `InvalidOperationException` 而非跳过；`Registered` 恒为 768，`SKIP=0`。与裁决 §5 表格逐格一致（768/765/3/0）。
- 补充：同一份产物若同时缺 `Release/` 目录，会额外出现 3 条 `BuildArtifact*` 失败（与 trace 无关的构建产物依赖），本次已把 `Release/` 一并复制后复测，排除该干扰。

## 3. 观察项（非阻塞，不需开票）

1. `manifest.json` 未写 `right_censored_ranges`（只写 `right_censored_segments`/`closed_events`），RUN_END 与 result.json 均完整写 7 字段。裁决 §2 要求字段存在且可核对，manifest 作为第三源略有不对称；不影响判定语义，建议下次顺手补齐。
2. 运行期没有独立的人读「文本摘要」行，`不穷尽` 只能通过 `event_scan_complete=false` / `is_lower_bound=true` / `label=M1StallCatchup` 读取；机器可读字段已足以表达，未见「摘要仍写 Unknown」的情况。
3. `strict_m1_events` 与 `auth_segments` 并存（`AuthSegments` 在 `:600` 自增，`ClosedEvents` 另计），两者不是同一计数口径，属既有设计；本票未改变其含义。

## 4. 变异与独立复跑（审查官自跑）

副本：`D:/Agent-工作目录/DevelopMyUNMultiplayerModAndModloader/_t81iso`（tar 从主树复制，排除 `.git/bin/obj/.scratch/Release/publishi/启动器`）。脚本 `D:/tmp/t81_mut.sh`，日志 `D:/tmp/t81-mut.log`；每条 `-t:Rebuild` 全量重建生产 + 重建测试项目 + 覆盖两处 DLL + 运行 exe。

| 轮次 | 改动 | 生产 DLL sha256 | 结果 | 新增红（点名） |
|---|---|---|---:|---|
| M0 | 无（基线） | `e648b92d…dca3761` | 766/768 | — （仅 GBA01/NIL01 环境失败） |
| M1 | `Bug1ProbeJudge.cs:562` 尾段恢复 `MarkUnknown + Valid=false + return` | `9e4b55ce08760ae30d6e17289d2e6e1880773189c19d988410fa219f1a5cbc50` | 760/768 | Bug1-01、Bug1-06、Bug1-70、Bug1-71、Bug1-78、Bug1-80 |
| M2 | `:302` `EventScanComplete = RightCensoredSegments == 0` → `= true` | `9d9d1045f8be838100e2647c07183e6795677fe120f09ca2a0d8bdaaa7766720` | 761/768 | Bug1-01、Bug1-06、Bug1-70、Bug1-71、Bug1-80 |
| R0 | 还原 Judge 后重建 | `e648b92d…dca3761`（与送审逐字节一致） | 766/768 | — |

还原核验：`sha256sum Platform/Diagnostics/Patches/Bug1ProbeJudge.cs` = `9db37e18f7c92d0801ec7733457990e32fc78425d60a7ee68f6b054a47e0a41e`，与 §1 绑定值一致；主树未被修改。

解读：M1 证明「尾段推翻闭合事件」这一错误语义会同时打红**五条**既有/新增用例（而非只靠规格例），M2 证明 `event_scan_complete` 不是装饰字段；两者都符合裁决 §4「不能只断言 `StrictM1Events==1`」的防假守卫要求。

其它核实：
- 历史运行产物未覆盖：`启动器/UnturnedModManager/publish/UMM-v2.2.1-win-x64/bug1-20261107T111121Z/HOST/{result.json,manifest.json,trace.jsonl}` mtime 均为 2026-11-07 19:13:42，早于 t80 工作窗口；`result.json` 仍为旧格式（无 `event_aggregation_revision`），`label=M1StallCatchup`、`strict_m1_events=1` 未变。
- `m1_rule_revision` 仍为 `M1-XZ-RELATIVE-v2`，新增 `event_aggregation_revision=CLOSED_EVENT_PRIORITY-v1`（Model `:143` 常量），两代口径可区分。
- `git status`：t80 只改 5 个已跟踪文件（Judge/Model/Runtime/两个测试文件），Program.cs 与 csproj 无改动。

## 5. 与 T79 用例期望表的对照（逐项）

| 裁决要求 | 实现/用例 | 判定 |
|---|---|---|
| Bug1-01 / Bug1-70 原 [0,3]/4 不变 + 追加静止尾段 | `Bug1ProbeJudgmentTests.cs:117-134`；`Bug1ProbeM1RuleTests.cs:139-152` | ✓ |
| Bug1-06 纯右截断仍 Unknown + strict=0/三元组 -1/范围断言 | `Bug1ProbeJudgmentTests.cs:214-231` | ✓ |
| Bug1-71 原 AUTH 不变 + 尾段保留 AUTH、扫描不完整 | `:239-254` | ✓ |
| Bug1-78 代表 8/11/12 不被最长尾段遮蔽 | `:457-478` | ✓ |
| Bug1-80 纯 censored 仍 Unknown、五类先验仍 Unknown、新增「早 M1+尾部缺相位」负控 | `:508-585` | ✓（时钟项见 §6） |
| Bug1-72 有 trace 仍 AUTH/M1=0、缺 trace 显式未执行 | `:160-181` + 本轮 765/768 实测 | ✓ |
| Bug1-73/74/75/76/77/79/81 数学门不变 | 未改动相关代码，全部 PASS | ✓ |
| 「两件闭合 M1+尾段」计数 2、closed_events 两件、代表稳定 | `:180-199` | ✓ |
| 「外部 censor 无法定位不放行」 | `Bug1ProbeJudgmentTests.cs:233-250` | ✓ |

## 6. Findings

| id | severity | 问题 | 位置 | requiredFix |
|---|---|---|---|---|
| T81-F1 | low | 「时钟异常」这一类豁免边界没有可失败用例：`ValidateFrames` 的 `CLOCK_INVALID`（`MonoFrequency<=0`）与 `CLOCK_NOT_MONOTONIC`（`MonoTicks` 非单调）在 `Bug1Probe*.cs` 中零引用，尾段释放 `dt<=0` 的 `CLOCK_INVALID`（Judge `:569`）也无用例。代码顺序保证这类输入在分类前就 UNKNOWN，因此不影响本票语义判定；但按派工验收第⑥条「六类都被用例证明」，该类目前只有代码证据。 | `Bug1ProbeJudge.cs:484-487,569` | 下一轮补一条用例（如把 `frames[k].MonoFrequency` 置 0，或把某帧 `MonoTicks` 倒序），断言 `Unknown / CLOCK_INVALID` 或 `CLOCK_NOT_MONOTONIC` 且 `StrictM1Events==0`；不阻塞本票。 |

判定：**pass**。该 finding 为覆盖补强，不改变本轮语义结论。

## 7. 口径边界（务必随报告一并引用）

1. 零 M1 ≠ Bug1 已修复：本票只证明「已闭合事件不被尾段吞掉、尾段单独未知」这一聚合语义；M1 判据（XZ、2cm、相对落后、收敛）仍是 T71 v2 未变，真实性仍待实机。
2. 未宣称真实数据证实 M1 判据：唯一真实 trace 回放（R03）给的仍是 `AUTH_STEP_LOCKED / M1=0`，不是 M1 正例。
3. 未宣称 0.2.4.11 可出包：`Release/`、`publishi/` 未写；本轮无出包动作。
4. 实机 1H+1G（真机菜单重进、完整重启、双端队员可见性）仍为 **UNKNOWN**，解锁条件不变，不由本票计数顶替。

## 8. 声明

- 本次审查只读主树：除读取与运行既有产物外，未修改任何生产/测试文件；所有变异在仓库外副本完成并逐字节还原（§4 R0 与 Judge 哈希核对）。
- 所有结论只对 §1 的字节组合有效；若后续任一被审文件变更，需重新绑定与重审。
- 复跑命令：`dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild`；`dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild`；`./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe`（exit 1 属既有约定，766/768，仅 GBA01/NIL01 环境失败）。

> 自绑定：本报告正文（截至上一行）15673B / sha256 `7d79e2f4f45272b273c3cdcd98b8d253a6ad64ecf138061becbd36603bfdc39e`；本行追加后整体哈希自然变化，判定效力以上述正文哈希为准。
