# T82 工程轴复审（r1）：CLOSED_EVENT_PRIORITY-v1 右截断局部化

- 被审票：t80（Reviewer: 本报告；Builder 交付见 t80 报告）
- 复审人：reviewer-eng（DeepSeek，工程轴）
- 日期：2026-11-07
- 结论：**needs_revision**（1 条 medium 阻断 acceptance 第 8 条，2 条 low）

## 0. 结论摘要

t80 的核心语义是对的：撤掉裸 `RightCensored` 全局否决之后，闭合事件（M1/AUTH）不再被正常尾段推翻，
而「尾部无法定位」仍然强制 Unknown、「扫描未穷尽」通过 `EventScanComplete=false` +
`StrictM1EventsIsLowerBound=true` 对外声明。我逐条走查了四种可能放行的路径（§4），没有发现
「把不可定位/未穷尽当成完整结果」的放行面 —— 这一点是干净的。

四条变异我都在仓库外副本重跑，红名单与 t80 自报逐条一致（§3）。

打回的原因不在判定语义，而在交付面：

- **manifest.json 少写了一个字段**（`right_censored_ranges`）。RUN_END 有、result.json 有、manifest 没有；
  我用生产路径探针（`Enable → EndStep → 读盘`）实测得到 `M=0,R=1,T=1`。
- 更关键的是，**没有任何用例读 manifest.json**（`grep -rn "manifest.json" WhitelistTests/` 为空）；
  唯一的生产路径落盘用例只断言 t60 的四个旧字段。因此「三处同源由生产路径用例读盘验证」这条
  acceptance 在当前字节上不成立 —— 它是靠人工读代码确认的，不是靠测试锁住的。

这两点合起来是同一个交付风险：离线判读工具如果以 manifest 为入口，会拿不到截断区间；
而测试不会红。修法很小（补一行 Append + 在一个落盘用例里读三处），但必须修完再判 pass。

## 1. 被审字节（本人实测，全部与合同/交付一致）

| 文件 | 字节 | sha256 |
| --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1ProbeJudge.cs`（856 行） | 43830 | `9db37e18f7c92d0801ec7733457990e32fc78425d60a7ee68f6b054a47e0a41e` |
| `Platform/Diagnostics/Patches/Bug1ProbeModel.cs`（143 行） | 6401 | `79ef2f47250adf6016a8e5ca538336c31c1cfb512cdb4512f9a8a04f9e568f4c` |
| `Platform/Diagnostics/Patches/Bug1ProbeRuntime.cs`（1420 行） | 79700 | `0214487e77f590d6a93555b1bf52a08e40a181f04a186dec12236c89abd06bfa` |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeM1RuleTests.cs`（793 行） | 46355 | `b341b066dc9641e0a534513f7c1282b6ce33e105115b8c15b22c56269813b12c` |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeJudgmentTests.cs`（364 行） | 20294 | `db9da7417bf6ce111c2eca597b096ace625bce3a81a5379515868152657b89ea` |
| `WhitelistTests/Program.cs`（未改） | 147251 | `71d5268ad401ae842ee97c71ba368f590a1554ba86b0f7dae8087c9949095c8e` |
| `SteamP2PFriends.csproj`（未改） | 27575 | `ed629ae7064015d55d2e1bee15b01ba1eb6a7a565dcdb80ac36892499350b948` |
| `bin/Release/SteamP2PFriends.dll` | 1506816 | `e648b92dddcfdbba9fc08b3ffbb2ea3658dfa417c329b1c96a88c8eb0dca3761` |

`WhitelistTests/bin/Release/SteamP2PFriends.dll` 与上表 DLL 同哈希；测试加载的是交付字节。
mtime：Judge 22:08、Runtime 22:02、M1RuleTests 22:10（均在 t80 窗口内）。

## 2. 三命令复跑（本人实测）

| 命令 | 退出码 | 结果 |
| --- | --- | --- |
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 0 | 0 警告 0 错误；重建 DLL 恒 `e648b92d…`（确定性） |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | 0 | 0 错误；4 警告（CS0649×3 + CS0219，均既有） |
| `./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | 1 | **766/768 PASS (Failed: 2)**；仅 GBA01、NIL01（控制台缺 Unity：`TypeInitializationException SDG.Unturned.SteamPlayer → NetReflection → ReflectionTypeLoadException`）；Bug1 段 82/82；`SPF_DLL_SHA256=e648b92d…` 与磁盘一致 |

`exit=1` 属既有约定（两条环境失败），与 t80 自报一致。

## 3. 四条变异（自跑，仓库外副本）

副本：`../_t82mut`（与主仓同级，`..\Libs` 才解析；tar 复制时排除 `.git/.scratch/bin/obj/Release/publishi/Assets/.agent-teams`），
独立重建得到与主树一致的 `e648b92d…`。脚本 `D:/tmp/mut82.py`（字节级替换 + MATCH_COUNT 断言 + `.t82bak` 备份/还原）、
`D:/tmp/drive82.sh`。副本内额外注册了一条导出探针（§8），故副本分母是 769（主树 768）。

| 变异 | 改动点（送审字节行号） | 变异 DLL | 结果 | 新红用例 |
| --- | --- | --- | --- | --- |
| MV-1 尾段推翻闭合事件 | `Bug1ProbeJudge.cs:526-560` censored 块 → `MarkUnknown(...,"RIGHT_CENSORED"); v.Valid=false; v.ClosedEvents.Clear(); return;` | `0406e700ecf79a9b…` | 760/769（Failed: 9） | Bug1-01、06、70、71、78、80 |
| MV-2 尾段覆盖三元组 | 选段赋值（`:662-664`）之后追加：若 `RightCensoredRanges.Count>0` 则 CandidateA/B 取该区间、ReleaseFrame=-1 | `8f9dd8a87e21e26a…` | 762/769（Failed: 7） | Bug1-01、70、71、78 |
| MV-3 扫描完整性硬置真 | `:302` `verdict.EventScanComplete = verdict.RightCensoredSegments == 0;` → `= true;` | `d79c6ef5cec533e2…` | 761/769（Failed: 8） | Bug1-01、06、70、71、80 |
| MV-4 跳过逐件关联 | `:283` `if (!ValidateClosedEventAssociations(...))` → `if (false && !...)` | `72603be6e9740c2f…` | 762/769（Failed: 7） | Bug1-01、70、71、80 |

- 红名单与计数与 t80 自报**逐条一致**（把探针那一条从分母扣掉即 760/768、762/768、761/768、762/768）。
- 每条 restore 后重建 DLL 均回 `e648b92d…`、源回 `9db37e18…`（逐字节还原；两轮独立还原哈希一致）。
- 变异 DLL 哈希取决于「等价替换」的具体文本（空白/注释差异即可改变哈希），故不作为同一性判据；
  本次四个哈希与 t80 自报未必相同，属正常（t75 那一轮的哈希差异同理）。

**配方教训（写给下一轮）**：MV-2 不能注入在 censored 块内 —— 那里写的 `CandidateA/B/Release` 会被
`:662-664` 的选段赋值覆盖，我第一次那样写得到 766/769 全绿（无效变异），必须注入在选段**之后**。

## 4. 撤掉全局否决后，还有没有「放行」的路？

我把所有会走到汇总的路径列了一遍（Judge:190-370），逐条看「不可定位」与「未穷尽」是否会被读成完整结果：

1. **窗口里没有齐全帧**（全 render-only）→ 上游尾部归因（`:190-235`）先判 `WindowTailUnexplained`，
   落 `CAPTURE_FAILURE_TAIL` / `TAIL_PARTIAL_UNEXPLAINED` 的 Unknown，进不到分类。✔ 不放行。
2. **`window.RightCensored` 为真但一个局部区间都没定位到** → `:292-299` 显式
   `MarkUnknown("step_end_boundary","RIGHT_CENSORED"); Valid=false; return;`，三元组保持 -1。✔ 不放行。
3. **定位到了局部区间、同时已闭合若干事件** → 按 M1 > AUTH 汇总，但 `:302-303` 会写
   `EventScanComplete=false`、`StrictM1EventsIsLowerBound=true`，三处导出都带这两个旗标。✔ 不冒充完整。
4. **只有局部区间、没有闭合事件** → `:325-336` 落 Unknown/`RIGHT_CENSORED`，三元组与 LagWitnessFrame 全 -1、
   `SelectedEventComplete=false`。✔ 不放行。

唯一残余是**语义方向的错**而不是放行：`RIGHT_CENSORED` 那条提前 `return` 发生在计数派生（`:302-311`）之前，
于是 `event_scan_complete` 留在默认 `false` —— 明明扫描已跑完（`ranges=[]`），导出却写「未穷尽」。
我的探针实测就是 `scan=false ranges=[]`（§8）。它偏保守、不会误报 M1，但会误导判读，见 R14-02。

结论：**没有 blocker 级放行面**；t80 关于「右截断只局部生效」的实现方向与 T79 裁决一致。

## 5. `right_censored_ranges` 的区间与 `release=-1`

- 区间定义在 censored 块（`:526-560`）：`StallStartFrame = frames[s - 1].Frame`、`StallEndFrame = frames[e].Frame`。
  `s` 从 `step=1` 起，故 `s-1 >= 0`；`e` 只在 `e+1 < frames.Count` 时继续扩张，故 `e <= Count-1`。
  → 区间**恒在 [0, N-1] 内**、`start < end`、无越界。
- `break` 之后不再扫，因此**至多一条区间**；不存在重叠，也不存在「先记 A 段再记 B 段」的叠加。
- 「漏尾」：若最后一段停步短于 `MinStallDisplacements`（3 个位移）会在 `:426` continue，不记区间但循环正常结束 →
  `EventScanComplete=true`。这是「最小停段」规则，不是漏尾（短段本来就够不上候选）。
- `release=-1` 由 `Bug1RightCensoredRange.ReleaseFrame = -1` 固定（`:117-123`），JSON 字段名 `release_frame`
  与闭合事件的 `release_frame` 同名字段一致；消费端目前只有测试与判读文档，无生产分支读取该字段。
- 一处命名小事：字段用复数（`Ranges`）但实现至多一条；消费端不应按多段设计（记入 §11 说明，不单列 finding）。

## 6. `strict_m1_events` 是否真由 `closed_events` 派生

`grep -rn "StrictM1Events" Platform/Diagnostics/Patches/*.cs` 的全部命中：

- `Bug1ProbeJudge.cs:164` 字段声明、`:190` 旗标声明、`:194` 注释；
- `Bug1ProbeJudge.cs:311` `verdict.StrictM1Events = closedM1;` —— **唯一写入点**；
- `Bug1ProbeRuntime.cs:923/934`（RUN_END）、`:1277/1315/1320`（manifest/result）—— 纯读。

旧版分类循环里的 `StrictM1Events++` 已删，没有第二套计数。`closedM1` 就是对 `ClosedEvents` 里 `Kind=="M1"`
的件数（`:305-310`），与 `ClosedEvents` 同源，不会出现「计数 1 但区间 2」的双写漂移。

## 7. 逐件关联是否在各自 `[a, release]` 上、失败是否仍 Unknown

- `ValidateClosedEventAssociations`（`:369-380`）对**每一件**闭合事件用**它自己的**
  `(closed.StallStartFrame, closed.ReleaseFrame)` 调 `ValidateCandidateAssociations`，不是用全局窗口范围。
- 域判定（`:790-822`）为 `f.Frame < frameA || f.Frame > frameB → 跳过`，即**含 release 帧端点**；
  逐帧要求 `HasLastInputSeq/HasLastRxId/HasQueueDepth`，SIM 按 id 去重且要求三项齐全，`TrackRxInputSeq` 拒 rx_id=0。
- 任一 `EvidenceComplete=false` → `:283-288` `Label=Unknown; Valid=false; SelectedEventComplete=false; return;`。
- MV-4 实测真红（01/70/71/80，其中 80 是缺 inputseq 的负控），说明这条链是活的。

## 8. 三处导出是否同源（探针实测）

副本内新增 `WhitelistTests/Evidence/Runtime/T82ExportProbe.cs`，走**生产路径**：
`__TestOffline(true)` → `__TestInstallSeam(()=>true)` → `Enable(config, OutputRoot=临时目录)` →
`__TestAddSmoothFrames(4)` → `EndStep("t82-probe")`，然后分别读 `manifest.json`、`result.json`、
`trace.jsonl` 的 `RUN_END`，逐字段打印 `key:M=?,R=?,T=?`：

```text
T82PROBE event_aggregation_revision:M=1,R=1,T=1
         selected_event_complete:M=1,R=1,T=1
         event_scan_complete:M=1,R=1,T=1
         strict_m1_events_is_lower_bound:M=1,R=1,T=1
         right_censored_segments:M=1,R=1,T=1
         right_censored_ranges:M=0,R=1,T=1        ← manifest 缺失
         closed_events:M=1,R=1,T=1
         | result_label="Unknown" scan=false ranges=[]
```

- 代码侧核对一致：manifest 块 `Bug1ProbeRuntime.cs:1274-1279` 写 6 个字段，**没有** `right_censored_ranges`；
  RUN_END `:931-937` 与 result.json `:1317-1323` 各写 7 个。
- `ClosedEventsJson`（`:1350`）与 `RightCensoredRangesJson`（`:1371`）是共用构造器 —— **不是**三处各写一遍字符串，这点符合要求。
- 但「同源」的**验证**是缺的：`grep -rn "manifest.json" WhitelistTests/` 为空，仓库里没有任何用例读 manifest；
  唯一生产路径落盘用例 `Bug1ProbeT60Tests.cs:176-240` 只断言 t60 的四个旧字段（`window_tail_excluded`、
  `phase_missing_genuine`、`window_tail_boundary`、`phase_missing_out_of_window`），7 个新字段一个都没断言。
- 另外探针那行 `scan=false ranges=[]` 是 R14-02 的证据：`RIGHT_CENSORED` 提前 return 跳过了 `:302-303`。

## 9. 边界与「主树零改动」

| 检查 | 结果 |
| --- | --- |
| Bug3 五文件哈希 | `7a82de8e…`/`a89f40b9…`/`3d5044f7…`/`a4553385…`/`5b0c4e9b…`，与开工一致，未变 |
| `Release/`、`publishi/` | `find Release publishi -newermt "2026-11-07 22:00"` 无输出 → 本票未写发布产物 |
| `SteamP2PFriends.csproj` | `ed629ae7…` 未改（本票只扩子例、不新开文件/编译项） |
| `WhitelistTests/Program.cs` | `71d5268a…` 未改（子例在既有 Bug1-xx 内追加） |
| 主树还原 | Judge `9db37e18…`、Model `79ef2f47…`、Runtime `0214487e…`、M1RuleTests `b341b066…`、DLL `e648b92d…` |
| 副本 | `../_t82mut` 已 `rm -rf`（含探针文件与改过的 Program.cs），删后主树哈希复核一致 |

## 10. Findings

**R14-01（medium，必须修）** — `Platform/Diagnostics/Patches/Bug1ProbeRuntime.cs:1274-1279`（manifest 块）
manifest.json 只写 6 个 T79 字段，**缺 `right_censored_ranges`**；RUN_END（`:931-937`）与 result.json（`:1317-1323`）各有 7 个。
且 `WhitelistTests/` 内无任何用例读 manifest.json，生产路径落盘用例 `Bug1ProbeT60Tests.cs:176-240` 也未覆盖 7 个新字段。
后果：以 manifest 为入口的离线判读拿不到截断区间；「三处同源由生产路径读盘验证」这条 acceptance 不成立，
而测试不会红（正是本轮最该堵的那类假绿）。
**requiredFix**：
1. manifest 补 `manifest.Append("\"right_censored_ranges\":").Append(RightCensoredRangesJson(verdict.RightCensoredRanges)).Append(',');`
   （与 RUN_END/result.json 同一构造器）；
2. 在 `Bug1ProbeT60Tests` 的生产路径用例里读 `manifest.json` 与 `result.json`，对 7 个字段逐一断言，
   并与 trace 的 RUN_END 行对齐（可以只断言存在性 + 关键值：`event_aggregation_revision`、`event_scan_complete`、
   `right_censored_segments`、`closed_events` 长度）；
3. 再加一条「有截断区间」的落盘用例（现有 T60 用例无 censored 区间，`right_censored_ranges` 恒空，断言空数组抓不到缺字段）。

**R14-02（low）** — `Platform/Diagnostics/Patches/Bug1ProbeJudge.cs:292-299`
`window.RightCensored && RightCensoredSegments == 0` 的分支在计数派生（`:302-311`）之前 `return`，
导致 `event_scan_complete` 保持默认 `false`。语义上扫描其实已穷尽（`ranges=[]`），导出却写「未穷尽」。
探针实证：`result_label="Unknown" scan=false ranges=[]`。
**requiredFix**：在该分支 `return` 前显式 `verdict.EventScanComplete = true; verdict.StrictM1EventsIsLowerBound = false;`
（或把计数派生提到分支之前），并在 Bug1-06 的子例里断言 `event_scan_complete == true`。

**R14-03（low）** — 变异证据的可复现性（无生产文件）
「尾段覆盖三元组」若注入在 censored 块内（`:526-560`），写进去的 `CandidateA/B/Release` 会被 `:662-664` 的选段赋值覆盖，
变异体表现为**全绿**（我首版即 766/769 全绿）；必须在选段之后注入才红（`8f9dd8a8…`，762/769，红 01/70/71/78）。
建议交付方在证据里写「文件:行 + 原文/替换文 + 期望红名单」，而不是只给 DLL 哈希 —— 等价替换的哈希不可作为同一性判据。
（本轮四条红名单与计数我已逐条复现，结论不受影响。）

## 11. pass 边界与 UNKNOWN

- 本报告判的是**判定器聚合语义**：右截断局部化后，「已闭合事件不被尾段推翻」与「未穷尽不冒充完整」两条都成立。
- **不等于** M1 已被真实数据证实：R03 的真实 trace 回放只在有该 trace 的机器上有意义（t75 F2 已记），
  本轮新子例仍是合成窗口直调 `Judge`。
- **不等于** Bug1 已修复：本票只改判读口径，不修顿挫、不给根因。
- UNKNOWN（我无法在本轮证实）：①真实对局里 `step_end_boundary` 触发的频率与是否总伴随尾部不可定位；
  ②`Stopwatch.GetTimestamp()` 在 Unity/Mono 下的单调性与精度（dt 的直接输入）；③是否存在依赖 manifest
  的离线判读工具（若有，R14-01 立刻从 medium 升为 high）。

## 12. 复现命令

```bash
# 基线（主树）
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe

# 变异（仓库外副本，需与主仓同级）
cd ..
tar --exclude=.git --exclude=.scratch --exclude=bin --exclude=obj --exclude=Release \
    --exclude=publishi --exclude=Assets --exclude=.agent-teams \
    -cf - SteamP2PFriends-0.2.4-Experimental | (mkdir -p _t82mut && tar -xf - -C _t82mut)
cd _t82mut && python /d/tmp/mut82.py MV1 && dotnet build SteamP2PFriends.csproj -c Release
cp bin/Release/SteamP2PFriends.dll WhitelistTests/bin/Release/
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe | grep -E "FAIL \[Runtime\] Bug1|Final Result"
python /d/tmp/mut82.py restore && dotnet build SteamP2PFriends.csproj -c Release
```





<!-- self_sha256(body)=b918f00709d7bd37c52d1cd06bb51cd4f4cd7ec10ff14edb0014ad535bde689f -->
