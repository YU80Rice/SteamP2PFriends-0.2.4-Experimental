# t117 工程轴复审（t114，R21-01/R21-02）——枚举化触发源与未知源不退订

复审对象：t114（ds-probe2）交付的 G 门探针修复轮字节。审阅者：reviewer-eng（工程轴）。
结论：**pass**。四条 acceptance 的核心行为都能被自跑变异证明；两条运行期「静默失效」路径（字符串触发源、未知源误摘 player 钩子）在当前字节上已不存在，新增用例能抓到「调用点传错枚举」。遗留为 4 条 low（未来枚举成员扩展、用例缺 try/finally、默认关闭口径、对照变异哈希不可复现）。

## 0. 结论摘要

| 问题 | 判定 | 依据 |
| --- | --- | --- |
| 枚举化是否消除「运行期静默失效」 | 是（当前两源） | 类型层无字符串；调用点由 IL 字面量断言钉死（MV-4 红） |
| 未知触发源是否可能误摘 player 钩子 | 否（已修） | MV-2 只红 Bug1-114，消息点名 R21-02 |
| 新增测试缝是否被生产误用 | 否 | 6 个 `__Test*` 全仓库仅测试调用 |
| 变异对照 | 行为一致、哈希不一致 | 计数 797/801、798/801 与红名单一致；DLL 哈希不同（见 §4） |

口径：本报告只判「探针的触发源记账与退订在工程上是否成立」，**不构成 G 门成立、探针可实机使用或 Bug1 已修复**。

## 1. 被审字节与独立复跑

字节锁（本人实测，与 t114 自报逐条一致）：

| 文件 | 行数 | 字节 | sha256（前 16） |
| --- | --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1GateProbe.cs` | 1784 | 82735 | `8628ec559a271721` |
| `Platform/Diagnostics/Patches/Bug1GateProbeLogic.cs` | 1510 | 73738 | `e7de8d6e3c49aafe` |
| `WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs` | 1328 | 83834 | `bc4546785dbc6381` |
| `WhitelistTests/Program.cs` | 1120 | 153752 | `04f1b024b2f2d4b2` |
| `Platform/Diagnostics/Patches/Bug1ProbeJudge.cs`（对照） | 860 | 44162 | `9eeee011c6ee98ee`（未变） |
| `SteamP2PFriends.csproj` | — | — | `af991c70de6bd822`（未变） |
| `bin/Release/SteamP2PFriends.dll` | — | 1563136 | `3ee2e52f62d58c3a`（两 bin 同哈希） |

三命令（主树）：

| 命令 | 退出码 | 结果 |
| --- | --- | --- |
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 0 | 0 警告 0 错误，DLL 复现 `3ee2e52f`（确定性） |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | 0 | 0 错误 4 警告（CS0649×3 + CS0219，均既有） |
| `./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | 1 | **799/801 PASS (Failed: 2)**，仅 `GBA01`/`NIL01`（`TypeInitializationException → SDG.Unturned.SteamPlayer → NetReflection → ReflectionTypeLoadException`，控制台缺 Unity 运行时）；Bug1 段 115/115，`Bug1-114` PASS |

`[T14] SPF_DLL_SHA256=3ee2e52f…` 与磁盘一致。

## 2. 四问逐条回答

### 2.1 枚举化是否真的消除了「跑起来才失效」这一类缺陷？

**类型层已消除，运行期残留两类边缘，均有守卫或已被判据覆盖。**

- 旧路径是 `DecideRetry(state, string trigger, …)`：拼错 `"level_loaded"` 能编译，运行期落 `default` → `RETRY_TRIGGER_UNKNOWN` → 立即 GIVEUP，探针在真机停摆而离线全绿（t113 的 MV-B 实证：那个变异在旧字节上全绿）。
- 现字节：`Bug1GateProbeLogic.Bug1RetryTrigger` 枚举（`Logic:1215-1222`），`DecideRetry(Bug1RetryBudgetState, Bug1RetryTrigger, int)`（`Logic:1263`）。生产调用点只有 `Probe:357`（level）与 `Probe:362`（player）；传字符串 → CS1503，拼错成员 → CS0117。全仓已无两个旧字符串常量（只剩 `Logic:1212` 注释里的举例），`TriggerName` 仅用于日志/`Bug1RetryDecision.Trigger` 展示（`Logic:1225`、`:1268`、`Probe:305`），不参与任何判定。
- **调用点传错枚举能被抓**：`Bug1-114` 用 `TriggerLiteralBeforeTryRetry` 逐条读 `call TryRetry` 前压栈的 `ldc.i4` 操作数（测试 `:1269-1279`、`:1306-1326`）。我把两个回调的实参对调（MV-4）→ `798/801`，红 `Bug1-114`，消息 `OnLevelLoadedRetry 必须传 LevelLoaded（传错枚举=静默停摆）`。若是变量而非字面量（例如 `TryRetry(someField)`），辅助函数返回 -1，同样红。
- **残余一**：`(Bug1RetryTrigger)2` 这类强转仍能编译，运行期判未知并 GIVEUP（`Logic:1284-1287`）。这是有意设计（不猜、不共享额度），且 `Bug1-113` 已覆盖未知源路径。
- **残余二**：给枚举**新增成员**而不改两处 `switch`（`DecideRetry` 与 `UnhookRetrySource`）仍能编译，运行期表现为该源立即 GIVEUP（并在 `UnhookRetrySource` 只打日志）。这是「从字符串漂移」转成「漏改 switch」的同类风险，属未来扩展面，见 R22-01。

### 2.2 `UnhookRetrySource` 的 default 只记日志后 return，会不会留下永久订阅？

**当前不会；退订成对性成立。** 依据：

- 唯一会订阅的地方是 `EnsureRetryHook()`（`Probe:244-274`），它只挂 `Level.onLevelLoaded` 与 `Player.onPlayerCreated` 两个已知源，且都以 `_levelRetryHooked`/`_playerRetryHooked` 幂等，并在 `catch` 里回滚标志。没有任何路径能订阅「未登记触发源」的事件。
- 三个退订入口：`UnhookRetryHook()`（`Probe:276-282`，成对摘两源，用于 Disable/重新启用）、`UnhookRetrySource(trigger)`（`Probe:289-309`，`switch` 只摘对应源）、`TryRetry` 早退分支（`Probe:379`，cfg 关掉时成对摘）。`Disable`/`Reset` 路径见 `Probe:157-159`（重新启用清零预算）。
- `UnhookRetrySource` 的 `default` 只打 `RETRY_UNHOOK_SKIPPED …（未知触发源不摘除任何订阅）` 后 `return`，不碰 `_levelRetryHooked`/`_playerRetryHooked`，也不触碰事件——这正是 R21-02 要的语义。MV-2 实证：未知源改走 player 分支 → `798/801`，只红 `Bug1-114`，消息 `未知触发源不得摘除 player-created 订阅（R21-02 核心）`。
- 反问的「真实但未登记的源」：既然订阅点只有两处，这类源不可能被挂上，所以不存在「挂着但摘不掉」的现存路径；只有未来新增源才会触发（R22-01）。

### 2.3 新增测试缝是否可能被生产路径误用，或让用例绕过真实接线？

**生产侧零调用，但用例确实走的是「标志位 + 直调生产方法」，不证明事件真的被摘掉。**

- 6 个缝（`Probe:312-352`）全部 `internal`、`__Test` 前缀；全仓库调用点只在 `Bug1GateProbeTests.cs:1208-1299`（grep 已核），生产零调用。
- `Bug1-114` 第 3 组：`__TestSetRetryHookFlags(true,true)` 直接把两个标志摆真，再调生产 `UnhookRetrySource((Bug1RetryTrigger)99)`，断言两标志不变；随后用已知源验证「只摘自己」，避免「谁都不摘」假绿（`:1291-1297`）。这确实只证明 `switch` 与标志位，不证明 `Level.onLevelLoaded -= …` 真的执行成功（`-=` 的异常被 `catch` 吞掉，离线也无法可靠订阅 Unity 静态事件——控制台里 `SteamPlayer` 的 cctor 已经会抛，见 NIL01）。
- 结论：这是可接受的离线近似（判定逻辑与账号记账都在生产方法里），但**不能用它宣称「事件订阅已核验」**。另外该用例没有 `try/finally`：若断言在中途失败，`_levelRetryHooked/_playerRetryHooked` 会以 true 泄漏到后续用例，`EnsureRetryHook` 便不再重挂（R22-02）。

### 2.4 变异对照

见 §3、§4：计数与红名单与 Captain 完全一致，DLL 哈希不一致（编辑文本不同，非构建不确定性）。

## 3. 自跑变异（仓库外副本 `../_t117mut`）

副本：tar 复制（排除 `.git/.scratch/.agent-teams/bin/obj/Release/publishi/Assets/Develop-Stage/publish`）+ `cp -a Libs`；独立 `Rebuild` 复现 `3ee2e52f`，基线 799/801；每条变异后 `dotnet build` → `cp bin/Release/SteamP2PFriends.dll WhitelistTests/bin/Release/` → 跑 exe → 用 `.t117bak` 字节还原 → 重建校验回 `3ee2e52f`。

| 变异 | 改动点 | 变异 DLL（前 16） | 结果 | 新增红 |
| --- | --- | --- | --- | --- |
| MV-1 字符串分发+拼错 | `Logic:1275-1288` switch → `name = TriggerName(trigger)`，`"level_loaded"` 与实际 `"level-loaded"` 不等 | `f85d2562e34cd5b2` | **797/801** | `Bug1-113`（`level-loaded 第 1 次应可重试`）+ `Bug1-114`（`LevelLoaded 第一次应 Retry/used=1`） |
| MV-2 未知源走 player 摘钩 | `Probe:303-307` default → player 分支 | `4f07c57ae411f645` | **798/801** | 只 `Bug1-114`（`未知触发源不得摘除 player-created 订阅（R21-02 核心）`） |
| MV-3 共享预算（等价变异） | `Logic:1281-1283` `used = state.LevelLoaded;` | `f9a868e57118feb8` | **797/801** | `Bug1-113`（`level-loaded 耗尽不得消耗 player-created 的预算（R21-01 核心）`）+ `Bug1-114` |
| MV-4 调用点传错枚举 | 两个回调实参对调（`Probe:354-363`） | `d4b682e81f5fe73f` | **798/801** | 只 `Bug1-114`（`OnLevelLoadedRetry 必须传 LevelLoaded（传错枚举=静默停摆）`） |

还原证明：每次 restore 后 `Bug1GateProbe.cs`/`Bug1GateProbeLogic.cs` 回到 `8628ec55…`/`e7de8d6e…`，重建 DLL 回 `3ee2e52f62d58c3a…`；副本已删除，主树开工/收工五个文件哈希一致（§5）。

## 4. 与 Captain 对照：行为一致，哈希不一致

| 变异 | Captain DLL | 我复现 DLL | 计数 | 红名单 |
| --- | --- | --- | --- | --- |
| MV-1 | `8e193972cdbcd831`（797/801） | `f85d2562e34cd5b2`（797/801） | 一致 | 一致（113+114） |
| MV-2 | `71953765af86debf`（798/801） | `4f07c57ae411f645`（798/801） | 一致 | 一致（114） |

构建是确定性的（同一副本连续两次 `Rebuild` 均为 `3ee2e52f`；MV-1/MV-2 各自只跑一次但还原后重建稳定），所以哈希差异只能来自**替换文本不同**——t114 报告只给了「改回字符串匹配并拼错」「未知源走 player 分支」的描述，没有给出逐字编辑。行为等价、判据成立，但「对照哈希」作为可复现证据链并不成立（R22-03，low；与 t75/t80/t82/t104 同类）。

## 5. 默认关闭与边界

- **默认关闭**：`Initialize()`（`Probe:66-95`）只在 `_enabledEntry.Value` 为真时 `Arm("startup")`；`Arm` 的三条入口是 startup（:91）、config-change（:163）、retry（:392，只有已 Arm 才可能走到）。`EnsureRetryHook` 只由 `Arm`（:189/:198）与 `HandleTargetGone`（:472）调用，后者同样在探测已启用路径上。关闭时 `OnLevelLoadedRetry`/`OnPlayerCreatedRetry` 若要触发，前提是曾被订阅；而订阅只在 `EnsureRetryHook` 且通过 `_retryBudget < MaxArmRetries` 闸门。
- **口径修正（R22-04）**：`Initialize` 会 `new ConfigFile(path, true)`（`Probe:73`），BepInEx 首次加载会落地 `SteamP2PFriends.Bug1GateProbe.cfg`。因此「Enable=false 时零写盘」严格说不成立——**探针产物（trace/GATE.json/samples）不写、钩子不挂、探针日志不出**，但配置文件会由 BepInEx 创建/更新。这是既有行为，不是本轮引入，建议交付说明写成「零探针产物写盘」。
- **Bug3 未受影响**：五文件 `7a82de8e38fa0f6a` / `a89f40b949f841d0` / `3d5044f7870fbb17` / `a45533850f31bd96` / `5b0c4e9ba4c87cbe` 与 t98/t104 轮一致；`Bug1ProbeJudge.cs` 仍 `9eeee011c6ee98ee`。
- **csproj** `af991c70de6bd822` 未改；`Program.cs` 仅 +1 行（`Bug1-114` 注册，`:1021`），`T69-01` 未动。
- `Release/`、`publishi/`、`Develop-Stage/`、`publish/` 自 2026-11-08 11:00 起无写入（`find -newermt` 空）。

## 6. Findings

**R22-01（low，未来扩展面）** 给 `Bug1RetryTrigger` 新增成员而漏改 `DecideRetry`/`UnhookRetrySource` 的 switch 可编译通过，运行期该源直接 GIVEUP（`Logic:1284-1287`）、`UnhookRetrySource` 只记日志（`Probe:303-307`）。当前只有两个源，无法触发；但「枚举化」把字符串漂移换成了「漏改 switch」，仍是编译期静默。
requiredFix：加一条枚举完备性用例——`Enum.GetValues(typeof(Bug1RetryTrigger))` 每个成员都过 `DecideRetry`，断言 `Reason != RETRY_TRIGGER_UNKNOWN`；或在 `DecideRetry` 的 default 里加 `#warning`/Debug 断言。

**R22-02（low，测试隔离）** `Test_R21_EnumTriggerIsGuarded`（`Bug1GateProbeTests.cs:1246-1300`）用 `__TestSetRetryHookFlags(true,true)` 摆布静态标志，末尾才 `__TestResetRetryBudget()`，无 `try/finally`。断言中途失败时两个标志会以 true 泄漏，后续用例的 `EnsureRetryHook` 会因标志已真而不再订阅（探针静默不重试），造成跨用例假绿/假红。
requiredFix：把 `__TestResetRetryBudget()` 放进 `finally`，或让 `__TestSetRetryHookFlags` 记录进入值并在 `__TestResetRetryBudget` 中强制回落 false。

**R22-03（low，证据可复现）** 与 Captain 的 MV-1/MV-2 对照哈希不一致（§4），报告未给逐字编辑文本，第三方无法复算同一 DLL。
requiredFix：变异表补「文件:行 + 改前/改后 + 变异后哈希」，或附带 patch 文件。

**R22-04（low，口径）** 「Enable=false 零写盘」不严谨：`Initialize` 会创建 BepInEx 配置文件（`Probe:73`）。
requiredFix：交付说明改为「零探针产物写盘（trace/GATE.json/samples）」，避免实机审计时被当成不实声明。

## 7. Pass 边界与 UNKNOWN

- 本判定只覆盖：触发源枚举化、分源预算、未知源不退订、测试缝可见性与四条变异。**不覆盖**真实 Unity 事件系统的行为（离线控制台无法可靠订阅 `Level.onLevelLoaded`/`Player.onPlayerCreated`；`NIL01` 的 cctor 异常即为此类）。
- 未验证：listen host 真机时序下，枚举路径与 `EnsureRetryHook` 的交互是否与离线推演一致（仍需 1H+2G 实机）；`UnhookRetrySource` 的 `-=` 在真实事件上是否真的能摘（离线只验标志位）。
- 未验证：G 门是否成立、探针是否能采集出可用样本、Bug1 是否被修复——全部在本票之外。

## 8. 复现命令

```bash
# 主树基线
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe    # 799/801

# 变异（仓库外副本，逐字节还原）
bash /d/tmp/drive117.sh MV1   # 797/801, Bug1-113 + Bug1-114
bash /d/tmp/drive117.sh MV2   # 798/801, Bug1-114
bash /d/tmp/drive117.sh MV3   # 797/801, Bug1-113 (+114)
bash /d/tmp/drive117.sh MV4   # 798/801, Bug1-114
```

<!-- self_sha256(body)=4d4db03629f09708ae0cea014fa3a1dfd661a3f6c049333087d65f923c38f9e1 -->
