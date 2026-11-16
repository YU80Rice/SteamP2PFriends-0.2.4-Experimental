# t110 / R21-01：G 门探针重试预算按触发源拆分

**结论**：G 门探针在 listen host 上「永不启动」的缺陷已修复。重试预算不再是一个全局计数器，而是
`level-loaded` 与 `player-created` 各自一份（各 `MaxArmRetries = 3` 次）；`target-gone` 情形单独计数。
早于托管就绪的 `level-loaded` 拒绝只会耗尽它自己的额度，`player-created` 到来时仍然 ARMED。
反向语义保留：**同一触发源**用满 3 次仍然如实 `RETRY_GIVEUP`，没有改成永不死心。

口径：本票只修「探针能否启动」这一条生命周期缺陷；它**不构成** G 门成立，也不构成 Bug1 已修复。
G 门结论仍需要真实 1H+2G 采集（见 §9）。

---

## 1. 实机证据（修复前）

主机日志 `UMM-诊断包_20261108_110015`（DLL `DD75D956`）：

```
112:Warning [SPF-Bug1Gate] REFUSED ROLE_NOT_HOST origin=startup
137:Info    [SPF-Bug1Gate] RETRY attempt=1/3 trigger=level-loaded
138:Warning [SPF-Bug1Gate] REFUSED ROLE_NOT_HOST origin=retry:level-loaded
144:Info    [SPF-Bug1Gate] RETRY attempt=2/3 trigger=level-loaded
145:Warning [SPF-Bug1Gate] REFUSED ROLE_NOT_HOST origin=retry:level-loaded
146:Info    [SPF-Bug1Gate] RETRY attempt=3/3 trigger=level-loaded
147:Warning [SPF-Bug1Gate] REFUSED ROLE_NOT_HOST origin=retry:level-loaded
148:Warning [SPF-Bug1Gate] RETRY_GIVEUP attempts=3 trigger=level-loaded
```

之后客机进入、主机正常游玩，探针再未尝试，因此没有 `GATE.json`。

## 2. 根因

listen host 的生命周期是：启动 → 载入关卡（此时 `Provider.isServer` 仍为 false）→ 才开始托管 → 玩家创建。
旧实现只有一个全局计数：

- `Bug1GateProbe.cs:272-277`：`private static int _armRetries;`（单一计数器，`MaxArmRetries = 3`）；
- `Bug1GateProbe.cs:237-244`：`EnsureRetryHook()` 同时订阅 `Level.onLevelLoaded` 与 `Player.onPlayerCreated`，
  两个源调用同一个 `TryRetry`，共用同一个计数器。

于是三次 `level-loaded` 拒绝（都发生在托管就绪之前，必然 `ROLE_NOT_HOST`）就把预算吃光；
等 `player-created` 触发时 `_armRetries >= MaxArmRetries`，直接 GIVEUP 并摘钩。

这是「预算记账粒度」错误，不是拒绝判据错误：`Provider.isServer` 的判定本身是对的。

## 3. 修复设计

### 3.1 判定下沉为纯函数（可离线）

`Bug1GateProbeLogic.cs`：

- `:1211` 触发源常量：`RetryTriggerLevelLoaded = "level-loaded"`、`RetryTriggerPlayerCreated = "player-created"`；
  原因码 `RetryReasonRetry = "RETRY"`、`RetryReasonGiveUp = "RETRY_GIVEUP"`、
  `RetryReasonUnknownTrigger = "RETRY_TRIGGER_UNKNOWN"`。
- `:1224` `internal sealed class Bug1RetryBudgetState { public int LevelLoaded; public int PlayerCreated; }`
- `:1230` `internal struct Bug1RetryDecision { Retry; GiveUp; Used; Trigger; Reason; }`
- `:1244` `public static Bug1RetryDecision DecideRetry(Bug1RetryBudgetState state, string trigger, int maxPerTrigger)`

判定顺序：

1. `state == null` → GIVEUP（`Retry=false, GiveUp=true`）；
2. 触发源按 `StringComparison.Ordinal` 精确匹配；不是两个已知源 → `RETRY_TRIGGER_UNKNOWN` 且**不动任何计数**；
3. `used = 该源自己的计数`；`maxPerTrigger <= 0 || used >= maxPerTrigger` → GIVEUP，`Used = used`；
4. 否则 `Used = used + 1`、`Retry = true`、`GiveUp = false`、`Reason = RETRY`，并写回**该源**字段。

### 3.2 生产接线

`Bug1GateProbe.cs`：

- `:47` `private static readonly Bug1GateProbeLogic.Bug1RetryBudgetState _retryBudget = new ...;`
- `:49` `private static int _targetGoneRetries;`（target-gone 是第三种情形，独立计数，不复用两个触发源的额度）
- `:244` `EnsureRetryHook()`：按源订阅，且只挂「该源还有预算」的钩子
  （`:248` `!_levelRetryHooked && _retryBudget.LevelLoaded < MaxArmRetries`，`:258` player 侧对称）；
- `:287` `UnhookRetrySource(string trigger)`：只摘掉耗尽的那一个源，另一源的订阅与额度不动；
- `:323-331` 两个回调改为传常量（`TryRetry(Bug1GateProbeLogic.RetryTriggerLevelLoaded)` /
  `TryRetry(Bug1GateProbeLogic.RetryTriggerPlayerCreated)`）——记账按字符串精确匹配，字面量漂移会让预算静默回到共享；
- `:337` 唯一记账点 `AccountRetry(string trigger) => DecideRetry(_retryBudget, trigger, MaxArmRetries)`；
- `:342` `TryRetry`：`!decision.Retry` → 打 `RETRY_GIVEUP attempts=<used>/<max> trigger=<源>（该触发源已耗尽；另一触发源保留自己的预算）`
  并 `UnhookRetrySource(trigger)`；可重试 → 打 `RETRY attempt=<used>/<max> trigger=<源>` 并 `Arm("retry:" + trigger)`；
- `:157-159` 重新启用时两源计数与 `_targetGoneRetries` 一起清零；
- `:427-440` `HandleTargetGone` 改用 `_targetGoneRetries`（原 `_armRetries` 5 处）。

## 4. 用例

`WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs:1161` `Test_R21_RetryBudgetIsPerTrigger()`，
注册为 `WhitelistTests/Program.cs:1020` `Bug1-113 Retry Budget Is Per Trigger (T110/R21-01)`。

| 断言组 | 内容 |
| --- | --- |
| 纯判定正例 | `level-loaded` 连续 3 次 `Retry && Used==1/2/3`；第 4 次 `!Retry && GiveUp && Reason==RETRY_GIVEUP` |
| **核心正例（实机缺陷复现）** | 紧接上一步 `DecideRetry(state, player-created, 3)` → **`Retry`**、`Used==1`、`state.PlayerCreated==1` |
| 反向 | `player-created` 自己用满 3 次后第 4 次同样 GIVEUP（不能永不死心） |
| 未知触发源 | `"camera-pre-cull"` → `GIVEUP && Reason==RETRY_TRIGGER_UNKNOWN`，两计数不变 |
| 生产路径 | `__TestResetRetryBudget()` → 3 次 `__TestAccountRetry(level-loaded)` → 第 4 次拒绝 → `__TestAccountRetry(player-created)` **必须 Retry**；`__TestRetryBudget` 断言 `3 / 1` |
| 接线 IL | `IlContractProbe.CountMethodCalls(TryRetry, typeof(Bug1GateProbe).FullName, "AccountRetry") >= 1` |

生产路径组走的是 `TryRetry` 用的同一个 `AccountRetry`，不是另写一份判定——所以「把记账改回全局共享」会在这里变红。

## 5. 变异证据（承重）

副本 `../_t110mut`（`tar` 复制，排除 `bin/obj/.git/.scratch/Release/publishi/Develop-Stage/publish`，
补 `Build/Version.props`），脚本 `../_t110mut/mut.py`：唯一替换 → 先 build 生产 → build 测试 → 跑 exe →
打印变异后 DLL 与 FAIL 行 → 从主树 `cp` 还原 → 打印还原后源码 sha256。副本已 `rm -rf`。

**MV-R21（把计数改回全局共享）**

- 文件/锚点：`Platform/Diagnostics/Patches/Bug1GateProbe.cs:337` `AccountRetry`
- 原文 → 替换文：

```csharp
// 原文
return Bug1GateProbeLogic.DecideRetry(_retryBudget, trigger, MaxArmRetries);

// 替换（所有触发源都消费 level-loaded 的额度 = 复现全局共享缺陷）
Bug1GateProbeLogic.Bug1RetryDecision shared =
    Bug1GateProbeLogic.DecideRetry(_retryBudget, Bug1GateProbeLogic.RetryTriggerLevelLoaded, MaxArmRetries);
shared.Trigger = trigger;
return shared;
```

- 变异后 DLL sha256：`bb4ccfacecebd29c274f9491fd5ab11a7c6e828761eac2bfa2c8e5c931c3a61b`
- 结果：`=== Final Result: 797/800 PASS (Failed: 3) ===`
- 红名单（只红一条自建用例 + 两条既有环境失败）：
  - `FAIL [Runtime] Bug1-113 Retry Budget Is Per Trigger (T110/R21-01): Bug1Gate: level-loaded 3 次拒绝后 player-created 仍必须可重试（ARMED 而非 GIVEUP）`
  - `FAIL [PureMemory] GBA01 Native First Observe Binding`（既有环境失败）
  - `FAIL [PureMemory] NIL01 Real Native Lookup Contract`（既有环境失败）
- 还原后源码 sha256：`837c0a5d42f6a4b11f5115645f860023e31a97dffde5e564d41b10743f94644d`（与主树逐字节一致）

## 6. 三命令

| 命令 | 结果 |
| --- | --- |
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 已成功生成，0 警告 0 错误 |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | 已成功生成，0 错误（4 条既有警告） |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `=== Final Result: 798/800 PASS (Failed: 2) ===`；Bug1 段 `114/114 PASS`；失败仅 `GBA01`、`NIL01` 两条既有环境项（`TypeInitializationException: SDG.Unturned.SteamPlayer` 链）；exit=1 属既有约定（有失败即返回 1） |

基线 797/799 → 798/800：分母 +1（新增 Bug1-113），失败项仍只有那两条环境失败。

## 7. 哈希与边界

### 7.1 涉及文件（改前 → 改后）

| 文件 | 改前 | 改后 | 行数 |
| --- | --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1GateProbe.cs` | `44b8115c2f162f720798b9f42c083c5d17c3b0aeb53304fe941848045e70f12e`（1670） | `837c0a5d42f6a4b11f5115645f860023e31a97dffde5e564d41b10743f94644d` | 1752 |
| `Platform/Diagnostics/Patches/Bug1GateProbeLogic.cs` | `7a0c6d24de12934a5af92bc9e1bdffc89cf7595a8c7bba0eb16d9f2f67937c8d`（1416） | `8330acf132dce0ea98b37cb152c7baa4060416dae145196d637ef4c4c941dc4b` | 1484 |
| `WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs` | `7bd27b859cbda46ee606d054ba0e0237f154d45e046c8d28595709bcf5e143a6`（1156） | `892bc56a197f2cba96c0bfebbcbff8c930f81e073832bf5a3cd8ca242bdd6c32` | 1240 |
| `WhitelistTests/Program.cs` | `5dcd7b8d93499ae6ebdd160617fe41adb30a48f168ae8bb0b77b04324e785201`（1118） | `a2a43fabd47ae3a8ed1417fa98dba0f32d4b2675c3a54c38dc4490075cace637` | 1119 |
| `bin/Release/SteamP2PFriends.dll` | `dd75d956df26087e1594b98ac611074c06f7b53b33b02e9257588a9b11e5ec67` | `0736a6023b64a4d6e916eb63471dcdc50c15cef32963fcde250d77e6423028db` | — |

`Program.cs` 只加 1 行（Bug1-113 注册），t69 的 `T69-01` 行未动。

### 7.2 边界核验

- Bug3 五文件哈希未变：`a89f40b949f841d0…`（P2PGroupStateRecoveryPatch.cs）、`7a82de8e38fa0f6a…`（GroupStateRecoveryCore.cs）、
  `3d5044f7870fbb17…`（GroupLeaseLedger.cs）、`a45533850f31bd96…`（GroupLoadPlanner.cs）、`5b0c4e9ba4c87cbe…`（P2PGroupRegistration.cs）。
- `find Release publishi Develop-Stage publish -newermt "2026-11-08 11:00" -type f` 无输出（未写这四个目录）。
- 变异副本 `../_t110mut` 已删除；主树 `Bug1GateProbe.cs` 与还原值逐字节一致（`837c0a5d…`）。

## 8. 默认关闭 / 零副作用

- `Initialize()` 仍然只在 `_enabledEntry.Value == true` 时调用 `Arm("startup")`；`Enable` 默认 `false` 未改。
- 未启用时：不订阅 `Camera.onPreCull`、不订阅 `Level.onLevelLoaded` / `Player.onPlayerCreated`（钩子只在 `Arm` 的拒绝分支或成功分支里按需挂），
  不写 `GATE.json`、不建 run 目录、不产生本探针日志。
- 本票新增的只有「预算结构」与「按源摘钩」两处内部状态，不含任何文件写入。

## 9. 仍需真实 1H+2G（不得把自动化绿灯当端到端可用）

1. **重试时序是否真的命中**：离线只能证明「按源记账」这条判定正确；真实关卡里 `level-loaded` 触发几次、
   托管就绪发生在第几次之后、`player-created` 是否落在 `MaxArmRetries` 预算内，必须在 1H+2G 上复采一次，
   期望日志出现 `RETRY attempt=… trigger=player-created` 且最终 `GATE_RESULT=` 有输出。
2. **`GATE.json` 是否落地**：需要确认 `BepInEx/diagnostics/spf-bug1-gate/<runId>/GATE.json` 生成，
   且 `pose_coverage` 六格是否被「正常玩一遍」覆盖（idle/walk/sprint × 两朝向）。
3. **Mono 下 BCL/Unity detour 与 `BakeMesh` 行为**：离线进程只能装 BCL 两条 Queue patch，
   `Assembly-CSharp` 原生方法在离线是 `HarmonyException: IL Compile Error (unknown location)`；
   真实主机上 `BakeMesh` 采到的蒙皮点是否随 `Model_0/1` 位移、`isDismissed` 是否存在，仍待实机。
4. **target-gone 独立计数**：本票只做了结构拆分与用例，真实「目标中途离场」时序未在实机复现过。

## 10. 口径声明

- 本票证据只说明「离线证据链自洽 + 变异可红」，**不**说明 G 门成立、也**不**说明 Bug1 已被诊断或修复。
- `GATE_RESULT=PASS` 只在真实 1H+2G 上有意义；离线一律 UNKNOWN/NOT_MEASURED 的既有约束未改。
- 报告未改动 `Release/`、`publishi/`、`Develop-Stage/`、`publish/`。
