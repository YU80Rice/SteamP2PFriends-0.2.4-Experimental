# T46 规格轴审查 —— Bug1 仪表开启入口（t45）：契约、参数来源、缺相位语义、文档可用性

- 审查者：reviewer-spec（规格轴）
- 被审任务：t45「Bug1 仪表开启入口：默认关闭的生产触发 + 参数原生解析 + 缺相位硬判 UNKNOWN」
- 本票 attempt：`9cfb4388-de1b-4251-a698-18dca57d5a29`
- 规格基准：`bug1-instrumentation-spec.md` §2/§3.1/§3.4/§4/§5；t44 报告 R7-01..R7-07；t45 交付说明（inbox 消息正文，本节按原文逐条核对）
- 判定边界：只判入口契约、参数来源、缺相位语义、文档可用性；配置热切换线程安全、反射健壮性等归工程轴 t47

## 1. 结论

**verdict = needs_revision**（status=failed）。本判定只对第 2 节字节有效。

一句话：**入口能开、参数真、R7-03 真闭合，但「缺相位硬判」的喂数在 window 上根本没接上——生产里每一局都会被判成 `PHASE_COVERAGE_MISSING`，哪怕相位已经正常采到。** 结果就是这个仪表会稳定地把「采到了」说成「没采到」，用户按文档第 7 条排查会一路排到错误方向。

| # | 结论 | 等级 |
|---|---|---|
| F1 | 生产窗口相位计数从未同步：每次 run 必判 UNKNOWN(PHASE_COVERAGE_MISSING)，产物自相矛盾 | **blocker** |
| F2 | `Enable` 先置 `Enabled=true` 再装 hook，且忽略 `Install()` 返回值 → 可留下「START_OK 但无 hook」 | high |
| F3 | 输出目录在 `Validate` 之前创建 → `ALREADY_RUNNING` 等拒绝路径留下空 run 目录 | medium |
| F4 | 速度上限固定取 sprint 档，未按姿态分档，spec §3.4「走/跑各有上限」只在 run 档成立且文档未写明 | low |

## 2. 被审字节绑定（本机实测，全部与派工一致）

| 文件 | 字节 | sha256 |
|---|---|---|
| `Platform/Diagnostics/Patches/Bug1ProbeControl.cs` | 8159 | `e0a1885f143bde72288bdf4c99ea72f4b27ef4c4e57c8f9f322a7c52dc4e59c4` |
| `Bug1ProbeEntryLogic.cs` | 3125 | `5bf4f391d5b3d7660f6ede3fe7bbcbc0d573b45483cbc57faa28fbcaa8eaf7b4` |
| `Bug1ProbeNativeSpeed.cs` | 4004 | `235943b8c78104edf62bd4eb86208fabeb18bfa692869335a1edf00c263e688b` |
| `Bug1ProbeRuntime.cs` | 42953 | `9b4397312f8e0af28ebf3a3c703f21aa8f43a298c3c3c02c95f6c76108d505f7` |
| `Bug1ProbeJudge.cs` | 22193 | `5d863441a4376cf2fb5b6ce06a29bc4baf92f8cab4e3a29d6929df79988fbb9f` |
| `Bug1ProbeModel.cs` | 3684 | `eb40f8060db49aadfd65ec3328cb1c4104f8b3bf3ce6b3fdb6a0d2ffd92c5acd` |
| `Bug1ProbePatch.cs` | 8872 | `12f8cbf0e1b9960bc6fc9512aeb4538d367f63db67da2d32930501d3ded031fd`（未改） |
| `Bug1ProbeRxPhases.cs` | 16693 | `8d0d36fc94cb1d6b8ba98fb99e73c6e1691ccee04e2f7d928ad3830f1964ca4a`（未改） |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs` | 2734 | `ba5b7a73b520e807037e38d8ddbfac82953322f0a0899e85a990ef3e6a9eb0eb` |
| `SteamP2PFriends.csproj` | 27409 | `4b56310877bbf18ec5fc83583516747ddd316a90f649de0aac64cbed851706eb` |
| `WhitelistTests/Program.cs` | 137261 | `eecd2ac0c73c3f65ce71278722803d77ad1482e827c8e12c2ea010493fa56387` |
| `bin/Release/SteamP2PFriends.dll`（= 测试目录副本） | 1462784 | `9625a13945ea08c56ae6bc072e8bea3fbbf9db9943b7c715d29dfb3e268e06de` |

## 3. 逐条验收判定

### 3.1 入口契约（默认关闭 / cfg 路径与键名 / 唯一生产入口）—— **pass**

- 默认关闭：`Bug1ProbeControl.cs:38` `Bind("Bug1Probe", "Enable", false, …)`；`Bug1ProbePatch.RegisterManual` 在 `!Enabled` 时直接返回、不装 hook、不写日志（`Bug1ProbePatch.cs:37-38` 注释与实现一致）。
- 路径与键名与交付说明**逐字一致**：`Path.Combine(BepInEx.Paths.ConfigPath, "SteamP2PFriends.Bug1Probe.cfg")` → `BepInEx/config/SteamP2PFriends.Bug1Probe.cfg`；节 `[Bug1Probe]`，键 `Enable`、`TargetPlayer`（`Control.cs:36-41`）。
- 唯一生产入口且非测试可达：`Control.Initialize()` 仅由 `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs:73`（Stage 6「Diagnostics/Probes」）调用，该 stage 在 `PatchRegistrationOrchestration.cs:44-46` 无条件列入注册计划；`TryStart` 全仓只有 `Control.cs:45`（startup）与 `:57`（SettingChanged）两个调用点，无测试专用旁路。
- 开启路径静态走查（照文档）：重启生成 cfg → 改 `Enable=true` → `SettingChanged` → `TryStart("config-change")` → `ENTRY_READY`（启动时已打）→ 参数解析 → `Enable` → `START_OK ... dir=…` → 关闭时 `STOP reason=CONFIG_CHANGED`。链路闭合、无断点。

### 3.2 参数来源可核验（不是编造）—— **pass（含 F4 的档位限制）**

- **TargetSteamId**（`Control.cs:127-154`）：遍历 `Provider.clients`，`client.playerID.steamID.m_SteamID` 取值；`if (id == Provider.client.m_SteamID) continue;` 排除 Host 自己；选择器按名 `OrdinalIgnoreCase` 或按 SteamID64 `Ordinal` 精确匹配（`EntryLogic.cs:36-43`）；0 命中且选择器非空 → `TARGET_PLAYER_NOT_FOUND`，0 命中且选择器为空 → `TARGET_STEAM_ID_MISSING`，**多命中 → `TARGET_PLAYER_AMBIGUOUS`**（`Control.cs:146-150`），不存在"随便挑一个"。
- **RunId/StepId**：UTC `bug1-yyyyMMddTHHmmssZ` / `step-n`（`EntryLogic.cs:24-33`），非手填。
- **NativeSpeedCapUS 与 SDK 逐行对照**（SDK 路径 `.scratch/bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/`）：

| 环节 | SDK 事实 | 实现 |
|---|---|---|
| 常量 | `PlayerMovement.cs:93` `private static readonly float SPEED_SPRINT = 7f;` | `NativeSpeed.cs:49-55` 反射同名字段并校验 `FieldType == typeof(float)` |
| 技能倍率 | `PlayerMovement.cs:303` `float exercise = 1f + (player.skills.mastery((int)EPlayerSpeciality.OFFENSE, (int)EPlayerOffense.EXERCISE) * 0.25f);` | `NativeSpeed.cs:56-57` `mastery(OFFENSE, EXERCISE)` → `1 + 0.25*mastery`，参数顺序与 SDK 一致 |
| 跑档分支 | `PlayerMovement.cs:311` `return SPEED_SPRINT * exercise * totalSpeedMultiplier;` | `NativeSpeed.cs:28` `sprintConstant * exerciseMultiplier * totalSpeedMultiplier`，三因子次序与乘法形式一致 |
| 总倍率 | `PlayerMovement.cs:127` `totalSpeedMultiplier => pluginSpeedMultiplier * clothing.movementSpeedMultiplier * …` | `NativeSpeed.cs:58` 读 `player.movement.totalSpeedMultiplier` 属性（不自行重算） |
| mastery 签名 | `PlayerSkills.cs:402` `public float mastery(int speciality, int index)` | `(int)EPlayerSpeciality.OFFENSE, (int)EPlayerOffense.EXERCISE` 与之匹配 |

→ **算法没有编造，与 SDK sprint 分支同式；文档里 `PlayerMovement.cs:93` 的引用也准确。**

- 异常输入：`Compute`（`NativeSpeed.cs:21-35`）对三个因子分别 `IsPositiveFinite`（拒 NaN/∞/≤0），乘积再校验 `>100d` 拒绝；缺字段/类型不符/反射异常/玩家为空均返回 false 并给 evidence，入口据此 `REFUSED NATIVE_SPEED_CAP_UNRESOLVED`。**缺失/非正/NaN/>100 四类全部拒绝，可达。**
- F4（low）：`TryResolve` 无论当前姿态都取 sprint 档，未按 `stance` 分档；spec §3.4 明写「走/跑各有上限」，实现只在 run 档成立，交付说明也未写"该 cap 仅适用于 run 段"。

### 3.3 「不会部分开启」—— **fail（F2、F3）**

文档 §1.4 承诺「任一不满足都不 Enable（不会部分开启）」。实际两条路径不成立：

- **F2（high）**：`Bug1ProbeRuntime.Enable`（`:79-124`）顺序是 `Enabled = true;`（`:114`）→ 清零计数（`:116-117`）→ `Record("RUN_BEGIN")`（`:118`）→ `try { Bug1ProbePatch.Install(); } catch { LastError = "INSTALL_FAILED:…" }`（`:122-123`）→ `return true;`（`:124`）。`Install()` 的**返回值被完全忽略**，而 `Install()` 在 ABI 未解析/`FailClosed`/任一 Hook 解析失败时就返回 false（`Bug1ProbePatch.cs:43-72`）。后果：hook 一个都没装，`Enabled` 仍为 true、`Enable` 返回 true，`Control.cs:110` 照常打 `START_OK`，`TryStart` 返回 true。这正是验收点名禁止的「`Enabled=true` 但 hook 未安装」，也是文档「不会部分开启」的直接反例；用户看到 START_OK + RUN_BEGIN，却永远采不到相位。
- **F3（medium）**：`Control.cs:91` 先 `Directory.CreateDirectory(outputRoot)`，`:94` 才 `Validate(...)`、`:107` 才 `Enable(...)`。因此 `ALREADY_RUNNING`（以及 `Enable` 返回 false 的任意路径）都在磁盘上留下一个空的 `spf-bug1/<新RunId>/HOST/` 目录——验收要求「任一前置失败时…输出目录未创建」。危害是误导取证：空目录与「跑过但没落盘」形态相同。

### 3.4 缺相位语义 —— **判定器 pass，喂数 fail（F1 blocker）**

判定器本身合规：

- `Bug1ProbeJudge.Judge`（`:114-132`）在 `window == null` 检查之后**第一件事**就是 `ValidatePhaseCoverage`（`:126`），失败即 `Label = Unknown; return`，绝不会继续走到 `:154` 的 `NO_THRESHOLD_EVENT`。
- `ValidatePhaseCoverage`（`:187-203`）：`RxEnqueueRecords.HasValue && <= 0` → `MarkUnknown(v, "AFTER_ENQUEUE", "PHASE_COVERAGE_MISSING")`，出队侧对称；两个都要报时两条都进 `MissingFields`。
- 合成窗语义未破坏：字段为 `int?`，null 时不参与（`Bug1-29` 覆盖）；生产窗在 `Enable` 置 0（`:116-117`）。`Bug1-26/27/28/29` 四条用例分别覆盖缺入队、缺出队、都齐、未设。
- `RUN_END` 落盘字段齐备：`rx_after_enqueue`/`rx_after_dequeue`/`rx_guard_rejected`/`rx_evicted`/`rx_superseded`/`trace_dropped`（`Bug1ProbeRuntime.cs:642-651`）。

**但喂给判定器的两个生产计数字段从来没被更新过（F1 blocker）**：

- 全仓对 `window.RxEnqueueRecords / RxDequeueRecords` 只有两处写入：`Bug1ProbeRuntime.cs:116-117`（`Enable` 里置 0）。
- 相位真实产出时递增的是两个**静态字段**：`:356` `_rxEnqueueRecords++`（AFTER_ENQUEUE 分支）与 `:374` `_rxDequeueRecords++`（AFTER_DEQUEUE 分支）。
- `EndStep`（`:625-660`）直接 `Bug1ProbeJudge.Judge(_window)`，其间没有任何 `_window.Rx… = _rx…` 的同步；`:789-830` 写 `result.json` 用的也是 `verdict`（来自 window）。
- 因此生产环境下 `window.RxEnqueueRecords == window.RxDequeueRecords == 0` 恒成立 → `ValidatePhaseCoverage` 恒失败 → **每一局都返回 `Unknown` / `PHASE_COVERAGE_MISSING`**，与是否真的采到相位无关。

产物会自相矛盾，这一点最要命：同一局的 `trace.jsonl` 里 `RUN_END` 行写着 `"rx_after_enqueue":500,"rx_after_dequeue":500`，而 `result.json` 写着 `"label":"UNKNOWN","reason":"PHASE_COVERAGE_MISSING","missing_fields":["AFTER_ENQUEUE","AFTER_DEQUEUE"]`。文档 §1.7 要求用户据此区分「detour 没触发 vs 配置错」——在当前实现下这条判据永远指向「没触发」，会把人带到完全错误的方向。

同时这也构成**文档与实现不符**：交付说明 §3 写「Runtime 在 Enable 置 0、两相位产出时 ++，RUN_END 落盘」，读起来是 window 计数在产出时 ++，而实现里 ++ 落在静态字段上，window 侧只置过 0。

（另注：现有 35 条 Bug1 用例全部走 `Bug1StepWindow` 手工赋值或纯 `EntryLogic`/`Judge`，没有任何一条经生产 `Enable → 记录 → EndStep` 路径，所以这个断链在 718/720 全绿下完全不可见——与 t35/t42 两次「用例绕开生产」是同一类假绿，只是这次断在被审字段上。）

### 3.5 R7-03（出队侧非目标队列负控）—— **pass（自跑变异证实）**

- `Bug1-25`（`Test_RuntimeWiring_ForeignDequeueProducesNothing`，`Bug1ProbeRxPhaseTests.cs:243-…`）：真 `Queue<PlayerInputPacket>` + 真包，`foreign` 队列入队再真实出队，断言 trace 为空。
- `Bug1-22` 补了三行出队侧断言（`:221-239` 区段）：`AFTER_DEQUEUE` 行数 == 1、trace 总长 == 2、`Enabled` 仍 true，不再只在入队侧做负控。
- **我自跑变异**（仓库外副本 `../spf-t46-iso`，基线字节与主树一致）：只删 `OnDequeue` 内的 `if (_serversidePackets == null || !ReferenceEquals(queue, _serversidePackets)) return;`

| 状态 | Runtime sha256 | DLL sha256 | 结果 |
|---|---|---|---|
| 基线 | `9b439731…` | `9625a139…` | 715/720（失败=GBA01、NIL01、BuildArtifact×3；后三条因副本无 `Release/` 产物，主树为 718/720） |
| 只删出队侧过滤 | `ec6e67320d097be86e44a830914adbb6bfcd7ca4d23b8ce3298c63ab082f99b3` | `cad847515410cf4e00e25447da3b36facdb4cd129129560f31e58c844d6b5560` | **713/720**，新增红 **Bug1-22**（"非目标队列必须被过滤，实际=3"）与 **Bug1-25**（"非目标队列出队不得产生任何记录，实际=1"） |
| 还原 | `9b439731…` | `9625a139…` | 715/720，Bug1-22/25 均 PASS |

→ 出队侧负控真可失败，R7-03 关闭。

### 3.6 文档可用性（照文档做会不会失败）—— **fail（受 F1/F2/F3 影响）**

逐条核对（交付说明原文 → 实现）：

| 文档条 | 实现 | 判定 |
|---|---|---|
| §1.1 cfg 路径、`[Bug1Probe] Enable/TargetPlayer`、默认 false | 一致（`Control.cs:36-41`） | ✓ |
| §1.2 保存即生效、留空自动选唯一客机 | 一致（`SettingChanged` → `TryStart`；`Control.cs:131-150`） | ✓ |
| §1.3 `ENTRY_READY` / `START_OK` 字段顺序与 `cap_evidence` 形状 | 一致（`Control.cs:43-44`、`:110-113`） | ✓ |
| §1.4 8 个 REFUSED 码 + 「任一不满足都不 Enable（不会部分开启）」 | 码全在（`EntryLogic.cs:13-21`）；**「不会部分开启」对 Install 失败与输出目录两条路径不成立** | ✗（F2/F3） |
| §1.5 关闭 → `STOP reason=CONFIG_CHANGED` | 一致（`Control.cs:60-65`） | ✓ |
| §1.6 产物路径 `…/<RunId>/HOST/trace.jsonl+manifest+result` | 一致（`Runtime.cs:786-828`，含 trace.jsonl 改名） | ✓ |
| §1.7 缺相位 → UNKNOWN(PHASE_COVERAGE_MISSING)，据 BIND_PENDING/REFUSED 区分 detour 与配置错 | 判定器一致；**但 F1 使该条件恒真**，判据失效；`BIND_PENDING` 只在绑定失败时打，与相位缺失没有互斥关系，实际无法据此区分 | ✗（F1） |
| §2 参数来源 | 与代码一致；F4 的档位限制未写明 | ✗（F4，low） |
| §3「Runtime…两相位产出时 ++」 | **实现里 window 计数不 ++** | ✗（F1） |
| §4 Bug1-22 三行出队侧断言、Bug1-25 | 一致（实测可红） | ✓ |
| §5 三命令与 718/720、DLL 9625a139 | Captain 复跑一致，我副本复现 DLL 一致 | ✓ |

## 4. Findings

### F1 · blocker · 生产窗口相位计数从未同步，缺相位判定恒真
- 文件/行：`Platform/Diagnostics/Patches/Bug1ProbeRuntime.cs:116-117`（仅置 0）、`:356`/`:374`（++ 落在静态字段）、`:641`（`Judge(_window)` 前无同步）、`Bug1ProbeJudge.cs:187-203`（读 window 字段）
- 问题：`window.RxEnqueueRecords/RxDequeueRecords` 永远是 0，`ValidatePhaseCoverage` 每次必失败 → 所有生产 run 都被判 `UNKNOWN(PHASE_COVERAGE_MISSING)`；同一局 `RUN_END.rx_after_enqueue=N` 与 `result.json` 的 `PHASE_COVERAGE_MISSING` 自相矛盾；文档 §1.7/§3 因此失效。
- requiredFix：在 `EndStep` 调 `Judge` 前把 `_rxEnqueueRecords/_rxDequeueRecords` 同步进 `window.RxEnqueueRecords/RxDequeueRecords`（或在相位产出处同时 `++` window 字段），并补一条**经生产路径**的用例：`Enable → 两条相位记录 → EndStep` 断言 label 不为 `PHASE_COVERAGE_MISSING`、`rx_after_enqueue>=1`；再加一条把同步删掉必须变红的反例（否则该字段仍是假绿）。

### F2 · high · `Enable` 先置 `Enabled=true` 再装 hook，且忽略 `Install()` 返回值
- 文件/行：`Bug1ProbeRuntime.cs:114`、`:118`、`:122-124`；`Bug1ProbePatch.cs:43-72`；`Bug1ProbeControl.cs:107-113`
- 问题：`Install()` 失败（ABI 未解析、`FailClosed`、Hook 解析失败）时 `Enable` 仍 `return true`，`Enabled` 保持 true、`RUN_BEGIN` 已写、`START_OK` 照打，hook 一个没装——「部分开启」的教科书形态，直接违反验收「不得出现 `Enabled=true` 但 hook 未安装」与文档 §1.4。
- requiredFix：`Install()` 返回 false 或抛异常时，在 `Enable` 内回滚（`Enabled=false`、清 `_window`、不落 `RUN_BEGIN`）并 `return false`；由 `Control` 落 `REFUSED INSTALL_FAILED <detail>`，不得打 `START_OK`。同时补一条用例（可用 seam 注入 Install 失败）断言 `Enabled==false && !TryStart`。

### F3 · medium · 输出目录在 `Validate` 之前创建
- 文件/行：`Bug1ProbeControl.cs:91`（`Directory.CreateDirectory`）vs `:94`（`Validate`）、`:107`（`Enable`）
- 问题：`ALREADY_RUNNING` 等拒绝路径留下空 `spf-bug1/<新RunId>/HOST/` 目录，与「跑过但未落盘」不可区分；违反验收「任一前置失败时…输出目录未创建」。
- requiredFix：把目录创建移到 `Validate` 通过之后、`Enable` 成功之后再执行；或在 `Refuse` 前删除刚创建的空目录，并补一条断言「拒绝后 `Directory.Exists(outputRoot)==false`」的用例。

### F4 · low · 速度上限只取 sprint 档，档位未声明
- 文件/行：`Bug1ProbeNativeSpeed.cs:49-59`（恒取 `SPEED_SPRINT`，`stance` 只写进 evidence）
- 问题：spec §3.4 明写「走/跑各有上限」，实现只在 run 档成立；文档也未写「该 cap 仅适用于 run 段」。walk 段若复用同一 cap 会放松阈值（漏检），且用户不知道。
- requiredFix：按 `stance` 选档（SPRINT/WALK 各自常量），或明确拒绝非 sprint 段的采集并在文档与 `cap_evidence` 中写死适用档位。

## 5. 增量边界

- 本次新增/改动全部落在 `Platform/Diagnostics/Patches/Bug1Probe*`（Control/EntryLogic/NativeSpeed 三个新文件 + Runtime/Judge/Model 改动）、`Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs`（+22 行，纯追加两个 try/catch 块）、`SteamP2PFriends.csproj`（+14 行 Compile，0 删除）、`WhitelistTests/Program.cs`（0 删除、+11 条 RunTest）、`WhitelistTests/Evidence/Runtime/Bug1ProbeEntryTests.cs`（新文件）。
- Bug3 五文件哈希未变：`a89f40b9 / 7a82de8e / 3d5044f7 / a4553385 / 5b0c4e9b`。
- `Release/`（最新 `0.2.4.11-preview2` 11-07 01:32）、`publishi/`（11-06 23:56）、`Build/Version.props`（版本号既有改动）时间戳均早于 t45 窗口（03:2x–03:4x），本轮未产出包。
- 顺带说明：`Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationAuditModules.cs` 的 Bug3 `RegisterManual` 追加属**先前 Bug3 票遗留**，非本票引入（哈希与 t42 轮一致 `5d21c3a5…`）。

## 6. 仍需 1H+1G 才能判（UNKNOWN，不计入本轮）

1. Mono 对 BCL 泛型 `Queue<PlayerInputPacket>.Enqueue/Dequeue` 的 detour 是否真触发（R6-05）——注意：**F1 修复前，即使实机采到相位也会被判缺相位，因此实机验收必须先等 F1 关掉**，否则会把「仪表自身的假报警」误读成「detour 没触发」。
2. `PlayerInput.isDismissed` 是否存在于发布版；缺失时 `reject_reason` 只能 null + `native_reject_branch_unresolved`。
3. `PlayerInput.ReceiveInputs` 的提前拒绝分支是否留可辨 bracket。
4. 出队前深度 `queue_depth_before` 无独立接缝（已按 null+unavailable 处理）。
5. 全部相位字段在真实 1H+1G trace 中的联动（`valid` 关联链、RUN_END 计数与丢样一致性）。

## 7. 判定边界

- 本判定只对第 2 节字节有效；后续任何重建/改动都不自动延续。
- 本票不判代码风格、不判热切换线程安全与反射健壮性（t47 负责），不判 Bug1 根因。
- 本票**通过与否**的语义是「仪表可用且不会骗人」；当前因 F1 未通过，不等于 Bug1 已诊断或已修复。
- 全部变异在仓库外副本 `../spf-t46-iso` 执行并还原（Runtime 回 `9b439731…`、DLL 回 `9625a139…`、715/720）；主树未被修改，主树 DLL 仍为 `9625a139…`。

---

本报告主体（不含本行）字节绑定：19952 B / sha256 `db8e54492eae7372bec1d91883140bdfe80b331c57a3950d2de6a76081871869`。本判定只对 t46 派工所列字节（Bug1ProbeControl e0a1885f / EntryLogic 5bf4f391 / NativeSpeed 235943b8 / Runtime 9b439731 / Judge 5d863441 / Model eb40f806 / Patch 12f8cbf0 / RxPhases 8d0d36fc / 注册 ba5b7a73 / csproj 4b563108 / Program.cs eecd2ac0）与 DLL 9625a139 有效。变异在仓库外副本 ../spf-t46-iso 执行并已还原（DLL 回 9625a139）。
