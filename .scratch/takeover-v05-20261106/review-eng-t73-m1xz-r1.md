# t75 工程轴复审：T73 交付（Bug1 M1 判据 M1-XZ-RELATIVE-v2）

- 审查官：reviewer-eng（DeepSeek）
- Reviewed task：t73；被审报告：`.scratch/takeover-v05-20261106/bug1-m1-xz-relative-v2.md`（15823B）
- 裁决依据：`.scratch/takeover-v05-20261106/architecture-decision-t71-m1-relative-motion.md`
- 审查基线：主树，HEAD `4f92627`，时区 Asia/Shanghai
- **verdict = pass**（附 2 条 medium、4 条 low；均有 requiredFix，无 blocker）

## 0. 结论先说

T73 把「三维全时域 RootSpan>ε 判 M1」整体换成「XZ 段枚举 + 同相位落后见证 + 回补收敛 + 释放帧超速边界 + 2cm 事件尺度」。工程轴结论：**代码可读、可测、改动面收敛，六条变异在等价树上全部真红且各自只红对应用例，逐字节可还原；判定链只读、dt 只取单调解时钟、导出与 manifest 只增新字段并按 revision 标注**。

不足以打回的两点（medium，均不阻断本票）：

1. 右截断段的处理是「整步作废」（`return`），会把同一步内**此前已经闭合的 M1** 一起吞掉：事件计数留下 `m1=1`，但 `CandidateA/B/Release` 全为 -1、`candidate_rejections` 为空、标签落 `UNKNOWN/RIGHT_CENSORED`。我用探针实证了这条，且 81 条用例无一覆盖「早段 M1 + 尾段截断」。这是语义选择（UNKNOWN 优先）而非实现错误，但需要 Architect 明确，且报告未披露这一副作用。
2. `R03`（真实 trace 回放）在找不到 trace 时静默 `return`，**仍计 PASS**。当前机器的 766/768 依赖仓库外的 `启动器/.../bug1-20261107T111121Z/HOST/trace.jsonl`；换机或清理该目录后，同一份字节仍会显示 766/768 全绿，而「用真实数据验证 R03 原候选不是 M1」这条证据实际没跑。

## 1. 被审字节（开工前取样，本人实测）

| 文件 | 字节 | sha256 |
| --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1ProbeJudge.cs` | 34531 | `615080f4f132ea964acd30592203d4db5d98ca3717e6b400ba062e7b744723f0` |
| `Platform/Diagnostics/Patches/Bug1ProbeModel.cs` | 5879 | `daf08c53309795d28139d6bd4d1933bc5d6f95c0b5042a0cae6f9f4628f1437f` |
| `Platform/Diagnostics/Patches/Bug1ProbeRuntime.cs` | 74570 | `a66aee9a934190131278bae84557c78a03a2436445114785634b9d1e79876c69` |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeM1RuleTests.cs` | 29713 | `0b4b019179bbfc0ec8177eb75288ed8602e98dc031117d3a7107a78439870749` |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeJudgmentTests.cs` | — | `6ff42723…` |
| `WhitelistTests/Program.cs` | 147251 | `71d5268ad401ae842ee97c71ba368f590a1554ba86b0f7dae8087c9949095c8e` |
| `SteamP2PFriends.csproj` | — | `ed629ae7…`（与 t63 相同，未改） |
| `bin/Release/SteamP2PFriends.dll` | — | `81da407a7bec87e88e0a642a5b1885e37541c28d7418259fdb897f2212341f27` |
| `WhitelistTests/bin/Release/SteamP2PFriends.dll` | — | 同上（同哈希，测试跑的是交付字节） |

mtime：Judge 19:54:36、Model 19:53:19、Runtime 19:55:36、M1RuleTests 20:11:36、Program.cs 20:02:00 —— 与 t73 报告一致。

## 2. 独立复跑（本人实测，不采信自述）

| 命令 | 结果 |
| --- | --- |
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | exit 0，**0 警告 0 错误**，2.76s，DLL 复现 `81da407a…`（确定性构建） |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | exit 0，0 错误、4 警告（CS0649×3 + 既有 CS0219） |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | exit 1 → **766/768 PASS (Failed: 2)** |

失败两项均为控制台缺 Unity 运行时的既有环境项，与 T73 无关：

```
FAIL [PureMemory] GBA01 Native First Observe Binding
FAIL [PureMemory] NIL01 Real Native Lookup Contract:
  System.Reflection.TargetInvocationException ---> System.TypeInitializationException:
  「SDG.Unturned.SteamPlayer」的类型初始值设定项引发异常 --->
  「SDG.Unturned.NetReflection」 ---> System.Reflection.ReflectionTypeLoadException
```

`PASS [Runtime] Bug1` 共 82 行（81 条用例 + 汇总行），Bug1 段 **81/81**；输出 `[T14] SPF_DLL_SHA256=81da407a…` 与磁盘 DLL 一致。

## 3. 六条变异（全部自跑于仓库外副本，逐字节还原）

副本：`/d/Agent-工作目录/DevelopMyUNMultiplayerModAndModloader/_t75mut`（**必须与主仓同级**，否则 `HintPath=..\Libs` 断链；排除 `.git/.scratch/bin/obj/Release/publishi/Assets/.agent-teams`）。副本独立重建得 `81da407a…`，与主树一致，证明副本是等价树。脚本 `D:/tmp/mut75.py`（字节级替换 + `MATCH_COUNT` 断言）与 `D:/tmp/drive75.sh`；备份 `/d/tmp/t75-pristine-judge.cs`。

| 变异 | 改动点 | 变异 DLL sha256（前 16） | 结果 | 新红用例 |
| --- | --- | --- | --- | --- |
| M1 | Judge:474 `if (witness >= 0 && converged)` → `if (converged)` | `78e87d93fe6ae2bf` | 765/768 (Failed: 3) | **Bug1-74** |
| M2 | Judge:471 `converged = …dot…>0` → `converged = true` | `f4ba10ea611d4cad` | 764/768 (Failed: 4) | **Bug1-11、Bug1-75** |
| M3 | Judge:474 追加 `\|\| ComputeSimRootSpan3D(...) > eps` | `301948804ef0e004` | 764/768 (Failed: 4) | **Bug1-72、Bug1-79** |
| M4a | Judge:442-443 释放用三维 `Distance(...)` | `3da09991dea47cd4` | 765/768 (Failed: 3) | **Bug1-73** |
| M4b | M4a + 2cm 门改 `releaseHorizontal < HorizontalMotionMinU` | `499ef8bae58a22d3` | 765/768 (Failed: 3) | **Bug1-73** |
| M5 | Judge:210 `!ValidateFrames(...)` → `false && !ValidateFrames(...)` | `9c4bc1c7b8438d28` | 756/768 (Failed: 14) | **Bug1-04/05/07/08/56/57/61/66/80/81** |
| M6 | Judge:423 `step = e + 1;` → `step = frames.Count;` | `3ab0d148be15946d` | 765/768 (Failed: 3) | **Bug1-78** |

红名单与 T73 自报**逐条一致**。每条变异跑完均 `restore` + 重建，DLL 回 `81da407a…`、Judge 回 `615080f4…`（并复核 `sha256sum`，非文本回替）。

**注意（F3）**：T73 报告里给出的变异 DLL（M1 `a564f2d2…`、M2 `49c15589…`、M3 `ba9aa5d3…`、M4a `0d0db903…`、M4b `dcc0c8a7…`、M5 `7ae503e3…`、M6 `5183ef11…`）与我在等价树上的重放结果**逐条不同**。我在同一副本上对 M1 连续重建两次都得到 `78e87d93…`，说明构建是确定性的；差异只能来自「表格描述的替换文本」与实际替换文本不完全相同（例如多删/少删一行）。红名单相同说明行为等价，但证据本身不能按表格逐字节复现。

## 4. 遮蔽核查：还有没有「被效果遮住的假绿」

我在副本内加了 `WhitelistTests/Evidence/Runtime/T75CensoredTailProbe.cs` 并在副本 `Program.cs` 注册一行（**主树未动**，探针也不进交付）。窗口 9 帧：0..3 为 M1 停段（A 0→.06，V 恒 0）、4 为释放（V=A=.08）、5..8 静止（V=A=.08，末段无释放）。`Bug1ProbeJudge.Judge` 输出：

```
T75PROBE label=Unknown reason=RIGHT_CENSORED valid=False m1=1 auth=0 stall=2 cand=1 A=-1 B=-1 R=-1 reject=[]
```

**同一步内已经闭合的 M1 被尾部右截断整体吞掉**：`StrictM1Events=1`，但 `CandidateA/B/Release` 全 -1、`candidate_rejections` 空、判定 UNKNOWN。现有 81 条用例中最近的 R10 只覆盖「纯 censored → UNKNOWN」，没有覆盖「早段 M1 + 尾段 censored」。这条不是「假绿」（UNKNOWN 不冒充 M1），但它是**漏判面**：步结束时玩家恰好静止（很常见），整步结论会退回 UNKNOWN，事件计数与标签、候选字段三者互相矛盾。

其余遮蔽路径我逐条查过，均**不成立**：

- 门序（Judge:190-235）先做窗口尾部归因与相位完整性，再进 `ClassifyHorizontalMotion`；`ValidateFrames` 的缺帧/时钟/相机重复门在分类前拦截，不会用残缺窗口造候选。
- M5 证实「绕过完整性门」会一次红 10 条用例，说明门本身有覆盖、且覆盖是拦在分类**之前**的。
- 「只检查最长段」：`step = e + 1` 让扫描继续（M6 红 Bug1-78 证明继续扫描不是空话）；R08 把长 AUTH 放在首段、短 M1 放后段，因此 M6 这个「首段即停」的变异能被抓住；真正的「只取最长」实现在此数据下会得到同一结论，但两种写错都会红，属可接受。
- 「2cm 门被 B 门遮住」：R06 用 cap=0.1 把 B 压低，使 0.019/0.020/0.021 三档只由 2cm 门决定，M4b 把门改成 `<` 后 Bug1-73 变红，证明该门单独可失败。
- 「见证被判 AUTH 掩盖」：R05a/b 分别构造见证缺失、收敛不成立两种负控，M1/M2 各红一条，说明两条必要条件都有独立判别力。

## 5. 逐行审读（摘意见，行号为送审字节）

**段枚举（Judge:407-532）**

- `frames` 是 `new List<>(w.Frames)` 排序后的副本，不改窗口本身；排序后 `MonoTicks` 单调性由 `ValidateFrames` 先验保证。
- 小步判据走 `IsHorizontalSmallStep`（量化后 XZ 距离 ≤ `EpsilonU=√3·q`），连续扩展得 `[a..e]`、`step=e+1`，段过滤 `e-s+1 < MinStallDisplacements(3)` 跳过；`[a..e]` 恰 3 个位移（4 点）时**接受**，与裁决 §3.2「≥4 点」一致，R01 覆盖的正是这个最小值。
- 无释放帧（`e+1 >= frames.Count`）→ `RIGHT_CENSORED` + `return`（**F1 的问题点**）。
- `dt = (release.MonoTicks - frames[e].MonoTicks)/release.MonoFrequency`，非正或频率缺失 → `CLOCK_INVALID`；`release.MonoTicks` 若缺失取释放帧自身，与「释放后时间」语义一致。
- 段内位移用**释放帧减停段末帧**（VRelease − `frames[e].VPos`），不是减 `a`；与 §3.2「释放位移」一致。
- 让位/遮蔽：`IsHorizontalSmallStep` 只看 XZ，Y 抖动不参与，与裁决 §3.1 一致。

**2cm 与网格算术（Model:96-140）**

- `HorizontalAtLeastGrids`：`gx=(long)Math.Round((a.X-b.X)/q)`、`gz` 同，比较 `gx²+gz² >= grids²`；全部调用点（Judge:457、Judge:548）入参都是 `Quantized()` 过的坐标，故 `round` 是对**整毫米格**取整而非对浮点四舍五入，0.019/0.020/0.021 边界由 R06 钉住。
- 风险（F4）：该函数自身不校验入参已量化。若将来有调用点直接传原始坐标，19.5 格会被 `round` 成 20 格而通过；属 API 契约未写明，不是当前缺陷。
- 严格号：B 门用 `>`（Judge:451 `!(releaseHorizontal > boundary)` 即 `<=` 拒），D 门用 `>=`（20 格整收）。两者不对称是裁决本意（B 是「超过上限才算释放」，D 是「至少 2cm 才算事件」），非笔误。

**dt/时钟（Runtime:355-356、Judge:437-439）**

- `MonoTicks = Stopwatch.GetTimestamp()`、`MonoFrequency = Stopwatch.Frequency` 在采样时成对写入，trace 里同时落 `mono_ticks`/`mono_frequency`。
- Judge 里 `grep "RATE|1/75|0.08"` = **0 命中**，dt 没有帧率/FPS 后备路径；`MonoFrequency<=0` 与 `MonoTicks<=previous` 在 `ValidateFrames` 先行拦截（M5 变红证明该门有效）。
- `NativeSpeedCapUS` 来自 `config`，在 `Enable` 时写进 `new Bug1StepWindow`（Runtime:115-118），一步一窗、之后不再刷新，符合「cap 不改」。若同一步内玩家切换疾跑/技能导致真实上限变化，B 会用旧 cap；裁决 §3.2 已把 cap 定义为 step 级常量，故不计缺陷，仅作 UNKNOWN 记录。

**只读性**

- 三个生产文件对 `SetValue(`、`.tick =`、`transform.position =`、`Enqueue/Dequeue` 写入的检索均 0 命中（仅 `OnDequeue` 方法名）。
- `SampleInterpolation` 只读 `PlayerInput.tick`（property）、`PlayerInput.RATE`（field）、`PlayerMovement.lastUpdatePos`（field）、`_movement.transform.position`；`Time.realtimeSinceStartup` 读取包在 `TryReadRealtimeSinceStartup` 里。
- Judge 不改窗口、不改 verdict 入参；`ComputeRenderRootSpanXZ`（Judge:579-598）把 `ABeforeUpdate` 也计入跨度，这是**导出字段**的展示口径（判决不受它影响），裁决 §3.1 要求「A 水平跨度」时若含前更新根会略大于纯 `A_pos` 跨度——R03 因此断言的是 `<= EpsilonU` 而非 `== 0`。

**导出（Runtime:913-923、1261-1262、1280-1283）**

- `m1_rule_revision`、`motion_axes`、`render_root_span_xz_u`、`sim_root_span_3d_u`、`strict_m1_events` 在 RUN_END、`manifest.json`、`result.json` 三处同源写入；旧 `root_span` 全仓生产代码 0 命中（仅注释与历史 result.json）。
- **只增不改**成立；`root_span` 属**删除**，但裁决 §6 明确授权「按 revision 重判」，且历史 run 的文件不被覆盖（Runtime:1278-1279 注释）。下游若有脚本读 `root_span` 会拿到空值——这是裁决内的取舍，不是本票缺陷。

## 6. 逐条 acceptance

| # | 合同条目 | 结论 | 证据 |
| --- | --- | --- | --- |
| 1 | 六条变异各自变红、贴 DLL 哈希、逐字节还原 | **pass** | §3 表；每条还原后 DLL 回 `81da407a…`、Judge 回 `615080f4…` |
| 2 | 段枚举：跳帧不被误并为停段 | **pass** | 小步判据逐帧比较（Judge:416-421），`step=e+1`；跳帧使该对不满足小步，段自然断开；M6 证明扫描继续 |
| 3 | 连续段恰 3 帧间隔的行为 | **pass** | `e-s+1 < 3` 才跳过（Judge:426），恰 3 位移（4 点）接受；R01 用 4 点断言 M1 |
| 4 | 2cm 网格算术（≥20 格、浮点不落 19.999） | **pass** | Judge:457/548 均传 `Quantized()`；R06 三档 0.019/0.020/0.021 钉住；F4 仅提示 API 契约 |
| 5 | B 与 D 的严格/非严格 | **pass** | B `>`（Judge:451）、D `>=`（Model:HorizontalAtLeastGrids）；M4b 把 D 改 `<` 后 Bug1-73 红 |
| 6 | `dt` 取单调解时钟、非帧率/固定 0.08 | **pass** | Runtime:355-356 + Judge:437-439；Judge 内 `RATE|1/75|0.08` grep 0 命中 |
| 7 | 遮蔽检查（见证/收敛被 AUTH 掩、2cm 被 B 掩、最长段掩短段） | **pass（附 F1）** | R05a/b、R06、R08 分别红 M1/M2/M4b/M6；新增探针只发现「右截断吞早段 M1」这一条，记 F1 |
| 8 | 完整性/门序未被绕 | **pass** | Judge:190-235 门序；M5 红 10 条 |
| 9 | 只读性（无原生写入、远端 tick 只读） | **pass** | §5 只读性三条 |
| 10 | 三命令 766/768、DLL 哈希、确定性 | **pass** | §2；两次 Rebuild 同哈希 |
| 11 | 无越界改动 | **pass** | Bug3 五文件 `7a82de8e/a89f40b9/3d5044f7/a4553385/5b0c4e9b`；`find Release publishi -newermt "2026-11-07 20:00"` 空；csproj 未改；主树 git status 与开工一致 |

## 7. Findings

| ID | 级别 | 位置 | 问题 | requiredFix |
| --- | --- | --- | --- | --- |
| F1 | medium | `Bug1ProbeJudge.cs:429-434` | 末段无释放帧时 `MarkUnknown(RIGHT_CENSORED); v.Valid=false; return;` 直接结束整步扫描。此前已闭合的 M1 只留在 `StrictM1Events`，`CandidateA/B/Release` 保持 -1、`candidate_rejections` 为空，标签为 UNKNOWN。探针实证 `m1=1 … A=-1 B=-1 R=-1`；81 条用例无覆盖。玩家在步末静止属常见情形，可能把真实 M1 压成 UNKNOWN | 二选一：①把右截断记为该段的 rejection（`RIGHT_CENSORED@e`）后 `continue`，让更早候选保留，仅在「无任何候选」时才把 UNKNOWN 作为结论；②若坚持 UNKNOWN 优先，则在 `result.json` 同时导出 `strict_m1_events`/候选区间被抑制的原因，并补一条「早段 M1 + 尾段 censored」用例钉住该语义。无论哪种，都需 Architect 在裁决里写明优先级 |
| F2 | medium | `Bug1ProbeM1RuleTests.cs:145-150` | `R03` 找不到 trace 目录时 `Console.WriteLine("R03 skipped")` 后 `return`，用例仍计 PASS；trace 路径指向仓库外 `启动器/.../bug1-20261107T111121Z/HOST/trace.jsonl`，非交付产物 | 让 skip 显式失败或在结果里单独记「skipped」，避免 766/768 在无 trace 机器上复制出同样的绿色；同时保留一个最小 fixture（数十帧即可）作为 R03 的兜底，真实 trace 只作为附加校验 |
| F3 | low | `bug1-m1-xz-relative-v2.md` §5 变异表 | 七条变异 DLL 哈希与等价树重放不一致（红名单一致），说明表里写的替换文本与实际替换不同，证据不可逐字节复现 | 变异表补「原文 → 新文」的精确片段（含行号与上下文），并注明构建命令与副本路径 |
| F4 | low | `Bug1ProbeModel.cs:HorizontalAtLeastGrids` | 函数内部按 `round((a-b)/q)` 取整后比较，只有入参已 `Quantized()` 才等价于「≥20 整格」；当前调用点都满足，但函数本身无契约说明 | 在 XML 注释写明「入参必须已 Quantized」，或改为接收整数格坐标，杜绝未来误用 |
| F5 | low | `Bug1ProbeJudge.cs:689-690` | `SpeedBoundary` 生产零调用，仅 `Bug1ProbeJudgmentTests.cs:240` 使用；生产在 Judge:449 内联同一公式，两处可漂移 | 让生产改调 `SpeedBoundary`（保持纯函数），或删除该 helper 并在测试里内联同式 |
| F6 | low | `Bug1ProbeM1RuleTests.cs:476` | 回放硬编码 `MonoFrequency = 10000000L`，未读 trace 每行的 `mono_frequency` | 从记录里解析 `mono_frequency`，缺失时才退回 10MHz |

## 8. pass 边界（上表把合同九项拆成 11 条判定）

**本票判 pass 的含义**：T73 这批字节在工程轴上可进入下一环节（构建 preview3 / 进实机采集），依据是「六条变异真红 + 逐字节还原 + 三命令一致 + 只读 + 边界干净」，且两条 medium 都不影响数据正确性（F1 是漏判面，F2 是证据可复现性），可按 F1/F2 的 requiredFix 单独补票。

**pass 不等于**：

- 不等于 M1 判据已被真实数据证实——R03 只回放了一段**历史误报**（11562–12215），证明「旧候选在新规则下归 AUTH」，不证明新规则能抓到新的真 M1；
- 不等于 2cm/释放边界在真实网络抖动下的表现已验证——±1mm 量化落在 20 格边界、丢帧导致 dt 变大的情形只有合成用例；
- 不等于 `RIGHT_CENSORED` 语义已由需求方确认（见 F1）。

**UNKNOWN（自动化不可证，需实机或真机数据）**：

1. 真机 1H+1G 下同一段是否仍稳定判 AUTH（`input.tick` 来源、插值、渲染链在真实 Unity 下的行为）；
2. 玩家步末静止导致 `RIGHT_CENSORED` 的**实际发生率**（决定 F1 是 rare 还是 systematic）；
3. `PlayerMovement.SPEED_SPRINT`/`PlayerSkills.mastery` 反射在真实游戏版本上的取值时机（本票未改该路径，沿用 t63/t65 结论）。

## 9. 复现命令

```bash
# 基线
cd "/d/Agent-工作目录/DevelopMyUNMultiplayerModAndModloader/SteamP2PFriends-0.2.4-Experimental"
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe

# 副本（必须与主仓同级，否则 ..\Libs 断链）
cd ..
tar -cf - --exclude=.git --exclude=.scratch --exclude=bin --exclude=obj \
    --exclude=Release --exclude=publishi --exclude=Assets \
    --exclude=.agent-teams SteamP2PFriends-0.2.4-Experimental | (mkdir -p _t75mut && tar -xf - -C _t75mut)
cd _t75mut && dotnet build SteamP2PFriends.csproj -c Release     # 应复现 81da407a…

# 变异（脚本内 MATCH_COUNT 断言，逐条 apply → build → cp DLL → 跑 exe → restore）
python D:/tmp/mut75.py M1 && bash D:/tmp/drive75.sh M1
#   M1/M2/M3/M4a/M4b/M5/M6 依次跑，最后 restore 并 sha256sum 校验
```

每条变异后把 `bin/Release/SteamP2PFriends.dll` 复制到 `WhitelistTests/bin/Release/` 再跑 exe（测试工程按 HintPath 加载这份 DLL），判读 `FAIL [` 行与 `=== Final Result: n/m PASS (Failed: k) ===`。

审查期间：主树只读（未改任何生产/测试文件）；副本内新增的探针文件与 Program.cs 改动仅存在于 `_t75mut`，审查结束已删除该副本，主树 `Bug1ProbeJudge.cs` 复核仍为 `615080f4…`。

<!-- self_sha256(body)=2cf11b6fa0481d3d0a0e2e50f363619db52fa2900b23d2090ddf8fbbe35a2520 -->
