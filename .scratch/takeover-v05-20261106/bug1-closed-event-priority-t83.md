# t83 交付报告：闭合事件优先级的三处收口（R14-01 / R14-02 / T81-F1）

- 任务：t83（repair-round-2），attempt 2，attempt_id `d685d962-4f5f-4aa7-9a36-eb7ef475e4a7`
- 依据：captain 收窄指令（m03016）+ t82 复审 findings
- 口径：本轮只修判定器与导出器的一致性，**不宣称 Bug1 已诊断**，也不把「有闭合 M1」表述为「顿挫已修复」

## 1. 范围与改动（生产 2 处，其余全在测试侧）

| 项 | 文件:行 | 改动 |
| --- | --- | --- |
| R14-01 | `Platform/Diagnostics/Patches/Bug1ProbeRuntime.cs:1280` | manifest 补 `right_censored_ranges`，与 RUN_END（`:936`）、result.json（`:1324`）使用同一构造器 `RightCensoredRangesJson(verdict.RightCensoredRanges)`，三处同源 |
| R14-02 | `Platform/Diagnostics/Patches/Bug1ProbeJudge.cs:301-302` | 孤儿分支（`window.RightCensored && RightCensoredSegments == 0`）return 前显式置 `EventScanComplete = true; StrictM1EventsIsLowerBound = false;`——扫描其实已穷尽，不能停在默认 false 谎称未穷尽 |

测试侧（只加断言与子例，不改生产语义）：

- `WhitelistTests/Evidence/Runtime/Bug1ProbeJudgmentTests.cs`
  - `Test_RightCensored_Unknown`：orphan 子例补 `EventScanComplete == true` 与 `!StrictM1EventsIsLowerBound`
  - `Test_NonFiniteSample_Unknown`：新增两条时钟门子例——`MonoFrequency = 0` → `CLOCK_INVALID`；`MonoTicks` 与前一帧相等 → `CLOCK_NOT_MONOTONIC`
- `WhitelistTests/Evidence/Runtime/Bug1ProbeT60Tests.cs`
  - Bug1-60 追加「7 个 T79 字段三处同源」断言：对 `trace.jsonl` 的 RUN_END、`manifest.json`、`result.json` 逐一断言 `event_aggregation_revision` / `selected_event_complete` / `event_scan_complete` / `strict_m1_events_is_lower_bound` / `right_censored_segments` / `right_censored_ranges` / `closed_events` 存在，并交叉核对 revision、`event_scan_complete`、`right_censored_segments`
  - Bug1-60 追加**带真实截断区间**的落盘子例（旧子例的区间恒空，缺字段也断言不出来）

## 2. 关键实现细节（两个坑，写清楚供审查复核）

### 2.1 落盘用例必须走 `OnEnqueue/OnDequeue`，不能直接改窗口计数

第一版子例直接写 `censoredWindow.RxEnqueueRecords = 4; RxDequeueRecords = 4;`，结果落盘为
`"label":"Unknown","reason":"PHASE_COVERAGE_MISSING","n_expected":0`。

原因在 `Bug1ProbeRuntime.EndStep`（`:884-885`）：

```csharp
if (_window.RxEnqueueRecords.HasValue) _window.RxEnqueueRecords = _rxEnqueueRecords;
if (_window.RxDequeueRecords.HasValue) _window.RxDequeueRecords = _rxDequeueRecords;
```

相位计数只有静态累加器一套真值（t48/F1 的既定语义），`EndStep` 会用静态计数覆盖窗口字段；`__TestEnable` 把静态计数清 0，于是窗口上写的 4 被覆盖成 0。修法是让用例走真实路径：`__TestBindQueue(queue, seqField)` → 4 次 `OnEnqueue` → 4 次 `OnDequeue`，静态计数自然到 4/4。

### 2.2 截断区间是 `[4,8]` 而不是 `[5,8]`

释放帧 4 之后 V 不再变化，`[4,8]` 本身就是一个新的「同点静止」水平小步段，因此被记为右截断段；闭合 M1 的 `[0,3]/4` 与它严格分开。这正是 T79 要求的「右截断只局部化，不推翻更早闭合事件」。

## 3. 三命令（主树，`-t:Rebuild`）

| 命令 | 结果 |
| --- | --- |
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 已成功生成，**0 警告 0 错误** |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | 已成功生成，4 条既有警告，**0 错误** |
| `./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `=== Final Result: 766/768 PASS (Failed: 2) ===`，FAIL 仅 `GBA01 Native First Observe Binding`、`NIL01 Real Native Lookup Contract`（既有环境失败）；Bug1 段 82/82 PASS；exit=1 属既有约定 |

## 4. 哈希对照（改前 → 改后）

| 文件 | 改前（t80 交付态） | 改后 | 行数 |
| --- | --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1ProbeJudge.cs` | `9db37e18f7c92d0801ec7733457990e32fc78425d60a7ee68f6b054a47e0a41e` | `9eeee011c6ee98eed53e8658249a9e27f2329f508b94cd53df10794d713220ce` | 856 → 860 |
| `Platform/Diagnostics/Patches/Bug1ProbeRuntime.cs` | `0214487e77f590d6a93555b1bf52a08e40a181f04a186dec12236c89abd06bfa` | `bb53d368f3edf9a825af89a4973fd8f3810fc55e8882d456db5f2f71362a3397` | 1420 → 1422 |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeT60Tests.cs` | `154ac184…`（409 行） | `dcf2e60e4a16e9b816c4160d7ab2e4dc29d6d2323f8a0e9c5255c64b0868ff87` | 543 |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeJudgmentTests.cs` | `db9da7417bf6ce111c2eca597b096ace625bce3a81a5379515868152657b89ea` | `20cfe25ceeceddfc6ab220e50af4f38a4a02f6670253cf2216db24ada70a2980` | 382 |
| `WhitelistTests/Program.cs` | `71d5268ad401ae842ee97c71ba368f590a1554ba86b0f7dae8087c9949095c8e` | 未改（无新增编号，子例塞进已注册方法内） | 1085 |
| `bin/Release/SteamP2PFriends.dll` | `e648b92dddcfdbba9fc08b3ffbb2ea3658dfa417c329b1c96a88c8eb0dca3761` | `7d62590857d4c0e3dab5888947ac9e44737ef4688bb7694ca782bd32b6d7d283` | — |

## 5. 变异证据（仓库外副本 `../_t83mut`，逐字节还原后已 `rm -rf`）

每行给出「文件:行 + 原文 → 替换文 + 期望红名单 + 实测红名单 + 变异后 DLL」。副本还原后与主树 `diff -r`（排除 bin/obj/.git/.scratch/Release/publishi/mut.py）**零差异**，`Bug1ProbeJudge.cs` 还原哈希 `9eeee011…`。

| 变异 | 文件:行 | 原文 → 替换文 | 期望 | 实测 | 变异后 DLL |
| --- | --- | --- | --- | --- | --- |
| MV1（R14-01） | `Bug1ProbeRuntime.cs:1280` | `manifest.Append("\"right_censored_ranges\":").Append(RightCensoredRangesJson(verdict.RightCensoredRanges)).Append(',');` → 删行（注释占位） | Bug1-60 红 | Bug1-60 红（765/768） | `e31ac3463138baa6b5b10d5193378a9a8983bb22cdeaa85d5dad6116a8b366da` |
| MV2（R14-02） | `Bug1ProbeJudge.cs:301-302` | `verdict.EventScanComplete = true;` + `verdict.StrictM1EventsIsLowerBound = false;` → 删两行 | Bug1-06 红 | Bug1-06 红（765/768） | `f9d7bb34adc8dc21d1daa1d98b4e4004bf50574b56f556854669035bba7f75a8` |
| MV3（T81-F1 时钟） | `Bug1ProbeJudge.cs:488` | `if (f.MonoFrequency <= 0) …CLOCK_INVALID…` → `if (f.MonoFrequency < 0) …` | Bug1-08 红 | Bug1-08 红（765/768） | `0dd17bbea474131489c731a53a60d640dceedee5e1d0dbbff6146b1c549aefcc` |
| MV4（T81-F1 时钟） | `Bug1ProbeJudge.cs:489-490` | `else if (previousTicks != long.MinValue && f.MonoTicks <= previousTicks)` → `… && f.MonoTicks < previousTicks` | Bug1-08 红 | Bug1-08 红（765/768） | `41e77e0820b49dc398e11fc8220603bc989923008f054f0299f0c91ed738948e` |

> 变异写法说明：时钟门最初尝试 `if (false)` / `else if (false && …)`，因为项目开了 `TreatWarningsAsErrors`，常量条件触发 CS0162（不可达代码）直接编译失败，所以改成 `< 0` / `< previousTicks` 这类运行期不可达但不恒假的写法。

### 5.1 必须记住的注入位置坑（R14-03 教训）

审查官实测过：把「尾段覆盖候选三元组」注入到 censored 块内（约 `:526-560`）**会全绿**，因为随后 `:662-664` 的选段赋值会把被覆盖的三元组再写回真实闭合事件。**这类变异必须注入到选段之后**，否则得到的「绿」不代表断言无效。

## 6. 边界核验

- Bug3 五文件哈希未变：`a89f40b949f841d0`（`P2PGroupStateRecoveryPatch.cs`）、`7a82de8e38fa0f6a`（`GroupStateRecoveryCore.cs`）、`3d5044f7870fbb17`（`GroupLeaseLedger.cs`）、`a45533850f31bd96`（`GroupLoadPlanner.cs`）、`5b0c4e9ba4c87cbe`（`P2PGroupRegistration.cs`）
- `Release/`、`publishi/` 未被写：`find Release publishi -newermt "2026-11-07 23:00" -type f` 无输出
- 调试语句已清除：`grep -rn "T83DEBUG" WhitelistTests/Evidence/Runtime/` 无命中
- 未改 `WhitelistTests/Program.cs`、`SteamP2PFriends.csproj`、`Bug1ProbeModel.cs`、`Bug1ProbePatch.cs`、`Bug1ProbeControl.cs`

## 7. 已自动化可证 vs 仍需实机

**已自动化可证（本轮）**

- manifest / result.json / RUN_END 三处 7 个聚合字段同源，且在有真实截断区间时字段值一致（MV1 删字段即红）
- 孤儿分支的 `event_scan_complete=true` / `is_lower_bound=false` 语义（MV2 即红）
- 两处时钟门（`MonoFrequency<=0`、`MonoTicks` 非单调）确实被用例覆盖（MV3/MV4 即红）

**仍需 1H+1G 实机（不得由本轮绿灯推出）**

- Mono 下 BCL `Queue<PlayerInputPacket>` 的 Enqueue/Dequeue detour 是否真触发（离线进程只能装 BCL 两条 patch，5 条 Assembly-CSharp 原生 patch 报 `HarmonyException: IL Compile Error`）
- 启动期重试在真实关卡时序下是否命中（`Level.onLevelLoaded` / `Player.onPlayerCreated` 的实际顺序）
- `isDismissed` 成员是否存在
- 真实对局里是否会出现「闭合 M1 + 右截断尾段」的组合，以及该组合下的产物是否与离线夹具一致

本轮的 766/768 只说明判定器与导出器的字段一致性有测试兜底，**不构成 Bug1 已诊断或顿挫已修复的结论**。
