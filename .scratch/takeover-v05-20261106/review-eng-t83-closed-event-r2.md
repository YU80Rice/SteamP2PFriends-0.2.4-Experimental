# T84 工程轴复审（r2）：R14-01 / R14-02 闭环核验

- 被审票：t83（Reviewer: 本报告；实现见 t83 报告 `bug1-closed-event-priority-t83.md`）
- 复审人：reviewer-eng（DeepSeek，工程轴）
- 日期：2026-11-07
- 结论：**pass**（R14-01、R14-02 均以自跑变异证明闭环；无 blocker/high；1 条 low 观察）

## 0. 结论摘要

t83 修的两处我都在**新字节**上复验过，且都用变异证明「删掉修复就会红」：

- **R14-01**：`manifest.json` 补上 `right_censored_ranges`（`Bug1ProbeRuntime.cs:1280`），
  与 RUN_END（`:936`）、result.json（`:1324`）共用 `RightCensoredRangesJson`。
  测试侧新增了真实截断区间 `[4,8]` 的落盘子例，三处逐字符比对。
  我删掉 manifest 那一行 → **765/768，红 Bug1-60**（只红这一条），说明这条链现在是活的。
- **R14-02**：孤儿分支（外部 `RightCensored` 标志但定位不到区间）在 `return` 前显式
  `EventScanComplete=true` / `StrictM1EventsIsLowerBound=false`（`Bug1ProbeJudge.cs:292-307`）。
  我删掉这两句 → **765/768，红 Bug1-06**（只红这一条）。

另外我把上一轮的四种语义回退（尾段推翻 / 尾段覆盖三元组 / 扫描完整性硬置真 / 跳过逐件关联）
全部重跑了一遍，仍然全红（§5）；t83 新增的两条时钟门用例也确认真实（MV7/MV8 的 DLL 哈希与 t83 自报
逐位相同，说明其变异配方可复现）。

字节锁、三命令、Bug3 边界全部一致，主树在审查前后同哈希。

## 1. 被审字节（本人实测，与 t83 自述一致）

| 文件 | 字节 | sha256 前 12 | mtime |
| --- | --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1ProbeJudge.cs`（860 行） | 44162 | `9eeee011c6ee` | 23:09 |
| `Platform/Diagnostics/Patches/Bug1ProbeModel.cs`（143 行，未改） | 6401 | `79ef2f47250a` | 21:59 |
| `Platform/Diagnostics/Patches/Bug1ProbeRuntime.cs`（1422 行） | 79947 | `bb53d368f3ed` | 23:10 |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeT60Tests.cs`（543 行） | 33485 | `dcf2e60e4a16` | 23:22 |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeJudgmentTests.cs`（382 行） | 21747 | `20cfe25ceece` | 23:12 |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeM1RuleTests.cs`（793 行，未改） | 46355 | `b341b066dc96` | 22:10 |
| `WhitelistTests/Program.cs`（未改） | 147251 | `71d5268ad401` | 20:02 |
| `SteamP2PFriends.csproj`（未改） | 27575 | `ed629ae70640` | 13:03 |
| `bin/Release/SteamP2PFriends.dll` | 1506816 | `7d62590857d4…` | 23:31 |

## 2. 三命令复跑（本人实测）

| 命令 | 退出码 | 结果 |
| --- | --- | --- |
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 0 | 0 警告 0 错误；DLL 恒 `7d625908…` |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | 0 | 0 错误；4 警告（CS0649×3 + CS0219，既有） |
| `./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | 1 | **766/768 PASS (Failed: 2)**；仅 GBA01、NIL01（控制台缺 Unity）；Bug1 82/82；`SPF_DLL_SHA256=7d625908…` 与磁盘一致 |

`exit=1` 属既有约定（两条环境失败），与 t83 自报一致。

## 3. R14-01 闭环：manifest 补字段 + 落盘三处比对

- 生产：`Bug1ProbeRuntime.cs:936`（RUN_END）、`:1280`（manifest）、`:1324`（result.json）
  三处都调用同一个 `RightCensoredRangesJson(verdict.RightCensoredRanges)`（`:1373`），不是各写一遍字符串。
- 测试（`Bug1ProbeT60Tests.cs`）：
  - `:63-72` 定义 `T79AggregationFields` 七字段数组（含 `right_censored_ranges`）；
  - `:255-271` 读 `manifest.json` 与 `result.json`，对七字段逐一断言**三处都有**，再断言
    `event_scan_complete` 布尔一致、`right_censored_segments` 数值一致；
  - `:292-345` 新增带真实区间的落盘子例：构造 `Divergent` 帧（A/V 分离，帧 0..3 递增、4..8 同点静止），
    走真实 `OnEnqueue/OnDequeue` 造 Rx 记录（`EndStep` 会用静态计数覆盖窗口，t83 已把这一坑写进报告），
    然后断言三处都含
    `{"stall_start_frame":4,"stall_end_frame":8,"release_frame":-1,"reason":"RIGHT_CENSORED"}` 与
    `{"kind":"M1","stall_start_frame":0,"stall_end_frame":3,"release_frame":4,"evidence_complete":true}`，
    以及 `right_censored_segments:1`、`event_scan_complete:false`、`is_lower_bound:true`、`label:M1StallCatchup`。
- 变异：删掉 manifest 那一行（`:1280`）→ DLL `f43ae10232026ba1…`，**765/768（Failed:3）**，
  红名单只有 **Bug1-60**。空数组抓不到缺字段的问题，正是被 `[4,8]` 这个非空子例堵住的。

## 4. R14-02 闭环：孤儿分支显式声明「扫描完整」

- 生产：`Bug1ProbeJudge.cs:292-307` 的孤儿分支（`window.RightCensored && RightCensoredSegments == 0`）
  在 `return` 前写 `EventScanComplete = true; StrictM1EventsIsLowerBound = false;`。
  这与「有局部区间」路径（`:306-307` 按 `RightCensoredSegments == 0` 派生）语义一致：
  前者是「扫描完了但尾段定位不到」，后者是「扫描没跑完（有截断）」。
- 测试：`Bug1ProbeJudgmentTests.cs` 的 Bug1-06 子例断言 `event_scan_complete==true` 且 `!is_lower_bound`（MonoFrequency<=0 那段同文件 :286-294 是 Bug1-08）。
- 变异：删掉这两句 → DLL `fdaa64f53de78da8…`，**765/768（Failed:3）**，红名单只有 **Bug1-06**。

## 5. 语义回退防护（上一轮四条变异重跑）

| 变异 | 改动点（t83 字节） | 变异 DLL | 结果 | 新红用例 |
| --- | --- | --- | --- | --- |
| MV-3 扫描完整性硬置真 | `Bug1ProbeJudge.cs:306` `EventScanComplete = RightCensoredSegments == 0` → `= true` | `33aace1cf1ebdb59…` | 760/768 | 01、06、60、70、71、80 |
| MV-4 跳过逐件关联 | `:283` `if (!ValidateClosedEventAssociations(...))` → `if (false && !...)` | `96bb654cfc91fd19…` | 761/768 | 01、60、70、71、80 |
| MV-5 尾段覆盖三元组 | 选段赋值（`:667-669`）之后：有区间则 CandidateA/B 取该区间、ReleaseFrame=-1 | `07846e2e13491c73…` | 762/768 | 01、70、71、78 |
| MV-6 尾段推翻闭合事件 | censored 块（`:558-564`）→ `MarkUnknown(...,"RIGHT_CENSORED"); Valid=false; ClosedEvents.Clear(); return;` | `0bf80d6d0da26964…` | 759/768 | 01、06、60、70、71、78、80 |

四条全部真红，且 MV-5/MV-6 的红名单与 t80 那一轮一致（MV-5 新增 78，因 t83 追加了 R08+尾段子例）。
MV-6 多红 60 是新增的截断落盘子例在防御「尾段推翻」。

`strict_m1_events` 仍只有一个写入点：`Bug1ProbeJudge.cs:315` `verdict.StrictM1Events = closedM1;`
（`grep -n "StrictM1Events\s*="` 仅此一处；其余是字段声明与导出读取）。

## 6. t83 新增时钟门用例是真实的

| 变异 | 改动点 | 变异 DLL | 结果 | 红用例 |
| --- | --- | --- | --- | --- |
| MV-7 `MonoFrequency <= 0` → `< 0` | `Bug1ProbeJudge.cs:488` | `0dd17bbea4741314…` | 765/768 | Bug1-08 |
| MV-8 `MonoTicks <= previous` → `<` | `:489` | `41e77e0820b49dc3…` | 765/768 | Bug1-08 |

这两个 DLL 哈希与 t83 自报的 MV3/MV4（`0dd17bbe…`、`41e77e08…`）**逐位相同**，说明其变异配方可复现；
本轮由此确认 Bug1-08 的两条子例不是空跑。
（我 MV1/MV2 的哈希与 t83 不同，是因为删除文本不同 —— 我删整行，t83 可能删不同空白；红名单一致，不影响结论。）

## 7. 边界

| 检查 | 结果 |
| --- | --- |
| Bug3 五文件 | `7a82de8e…`/`a89f40b9…`/`3d5044f7…`/`a4553385…`/`5b0c4e9b…`，未变 |
| `Release/`、`publishi/` | `find … -newermt "2026-11-07 23:00"` 无输出 |
| `Program.cs`、`csproj` | 未改（`71d5268a…`、`ed629ae7…`） |
| 主树还原 | Judge `9eeee011…`、Runtime `bb53d368…`、T60Tests `dcf2e60e…`、JudgmentTests `20cfe25c…`、DLL `7d625908…` |
| 副本 | `../_t84mut` 已 `rm -rf`（含为构建而 `cp -a` 的 `Libs`），删后主树同哈希 |

## 8. Findings

**R15-01（low，不阻断）** — `WhitelistTests/Evidence/Runtime/Bug1ProbeT60Tests.cs:255-271`
三处同源的断言分两层：空区间用例只断言 `right_censored_ranges:[]` 在 manifest/result 中存在，
`closed_events:[]` 只断言 manifest；逐字符相等只在非空子例（`:327-337`）里做。
若将来某个导出把 JSON 字段名改掉（如 `evidence_complete` → `evidence`），存在性循环仍会绿，
只有非空子例可能红。建议补一行「manifest 的这两段子串 == result 的这两段子串 == RUN_END 的这两段子串」，
成本一行、收益是字段名漂移也能抓。
（当前无实际缺陷：两个子例覆盖了「空/非空」两条路径，`[4,8]` 子例已能抓字段缺失。）

其余检查未发现问题：

- 放行路径（r1 §4 的四条）在 t83 的改动后没有新增缺口：孤儿分支现在显式声明 `scan=true`，
  有区间路径仍是 `scan=false` + `lower_bound=true`，两处汇总顺序未动。
- 区间计算、`release=-1`、`strict_m1_events` 单写入点（现 `:315`）、逐件关联（各自 `[a,release]`，失败 → Unknown）
  与 r1 判定一致，未被 t83 改动触碰。
- t83 未改 `Program.cs`/`csproj`，未新增测试编号（沿用 Bug1-06/08/60 的子例），与「扩子例」口径一致。

## 9. pass 边界

- 本报告判定的是**判定器聚合语义与导出一致性**：右截断局部化后不推翻闭合事件、不把不可定位当完整、
  三处导出同字段集且同值，且这些都由会失败的用例锁住。
- **不等于** M1 已被真实数据证实：R03 的真实 trace 回放仍依赖本机 `启动器/.../bug1-*/HOST/trace.jsonl`（t75 F2）。
- **不等于** Bug1 已修复：本票只改判读口径与导出，不修顿挫、不给根因。
- 仍为 UNKNOWN（本轮无法证实）：①真实对局里 `step_end_boundary` 的触发频率与是否总伴随尾段不可定位；
  ②`Stopwatch.GetTimestamp()` 在 Unity/Mono 下的单调性与精度；③是否存在以 manifest 为入口的离线判读工具
  （若有，R15-01 的字段名漂移风险应升为 medium）。

## 10. 复现命令

```bash
# 基线（主树）
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe

# 变异（仓库外副本，必须与主仓同级并把 Libs 一并带过去，否则 HintPath ..\Libs 解析失败）
cd ..
rm -rf _t84mut && mkdir -p _t84mut
tar --exclude=.git --exclude=.scratch --exclude=bin --exclude=obj --exclude=Release \
    --exclude=publishi --exclude=Assets --exclude=.agent-teams \
    -cf - SteamP2PFriends-0.2.4-Experimental | tar -xf - -C _t84mut
cp -a Libs _t84mut/Libs
cd _t84mut/SteamP2PFriends-0.2.4-Experimental
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release   # 先建测试工程
python /d/tmp/mut84.py MV1 && dotnet build SteamP2PFriends.csproj -c Release
cp bin/Release/SteamP2PFriends.dll WhitelistTests/bin/Release/
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe | grep -E "FAIL \[Runtime\] Bug1|Final Result"
python /d/tmp/mut84.py restore && dotnet build SteamP2PFriends.csproj -c Release
```




<!-- self_sha256(body)=25dc19d5acd9c8408e007c95e4c54c2249468880c33c65fe860a5f712aaa7f6f -->
