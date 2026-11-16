# t113 工程轴复审：t110 重试预算按触发源分离（R21-01）

审查对象：t110 交付字节（`Platform/Diagnostics/Patches/Bug1GateProbe.cs`、`Bug1GateProbeLogic.cs`、
`WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs`、`WhitelistTests/Program.cs`）。
审查轮次：第 5 轮，工程轴（正确性/边界/回归/可维护性），不判需求满足度。
结论：**pass（工程可交付）**，附 2 条 medium + 2 条 low；**不等于 G 门成立、不等于 Bug1 已修复**。

## 0. 一句话结论

分源预算在本批字节上「承重」成立：level-loaded 用满 3 次后 player-created 仍可记账并重试
（MV-A 只红 Bug1-113），未知触发源不获得任何额度（MV-C 只红 Bug1-113），GIVEUP 有界、退订成对，
日志不含 SteamID，默认关闭零副作用。两条 medium 都是**当前字节不可达的潜在失效面**（字面量漂移无
测试守卫、未知源会误摘 player 钩子），不阻塞本批字节进实机。

## 1. 字节绑定（本人实测，与票面一致）

| 文件 | 行数 | 字节 | sha256 |
|---|---|---|---|
| `Platform/Diagnostics/Patches/Bug1GateProbe.cs` | 1752 | 81090 | `837c0a5d42f6a4b11f5115645f860023e31a97dffde5e564d41b10743f94644d` |
| `Platform/Diagnostics/Patches/Bug1GateProbeLogic.cs` | 1484 | 72717 | `8330acf132dce0ea98b37cb152c7baa4060416dae145196d637ef4c4c941dc4b` |
| `WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs` | 1240 | 78041 | `892bc56a197f2cba96c0bfebbcbff8c930f81e073832bf5a3cd8ca242bdd6c32` |
| `WhitelistTests/Program.cs` | 1119 | 153569 | `a2a43fabd47ae3a8ed1417fa98dba0f32d4b2675c3a54c38dc4490075cace637` |
| `SteamP2PFriends.csproj`（未改） | 411 | 27726 | `af991c70de6bd822de0fd50d4a7f74496c52f2156e7377dacdd2a294f16d912d` |
| `bin/Release/SteamP2PFriends.dll` | — | 1562624 | `0736a6023b64a4d6e916eb63471dcdc50c15cef32963fcde250d77e6423028db` |

`WhitelistTests/bin/Release/SteamP2PFriends.dll` 与主树 DLL 同哈希。

## 2. 独立复跑（主树）

- `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` → exit 0，0 警告 0 错误，DLL 复现 `0736a602`（确定性构建）。
- `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` → 0 错误 4 警告（CS0649×3 + CS0219，均既有）。
- `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` → exit 1，`=== Final Result: 798/800 PASS (Failed: 2) ===`，
  两条 FAIL 均为环境缺失：`GBA01 Native First Observe Binding`、`NIL01 Real Native Lookup Contract`
  （`TypeInitializationException SDG.Unturned.SteamPlayer → NetReflection → ReflectionTypeLoadException`）。
- Bug1 段 114/114 PASS；`[T14] SPF_DLL_SHA256=0736a602…` 与磁盘一致。

## 3. 自跑变异（仓库外副本 `../_t113mut`，独立重建先复现 `0736a602`；逐字节还原后删除副本）

| 变异 | 改动点 | 变异 DLL（前 16） | 结果 | 新增红 |
|---|---|---|---|---|
| MV-A（对照 Captain） | `Logic.cs:1263` `used = state.LevelLoaded;` | `146994d962b52aaa` | 797/800 | **仅 Bug1-113**（"level-loaded 耗尽不得消耗 player-created 的预算"） |
| MV-B（字面量漂移） | `Probe.cs:326` `TryRetry("level_loaded")` | `c6aa7ec3eda7f8df` | **798/800 全绿** | **无** |
| MV-C（未知源吃额度） | `Logic.cs:1257` 未知分支 `state.LevelLoaded++` | `7433424da4a2d9c1` | 797/800 | 仅 Bug1-113（"未知触发源不得改动任何计数"） |

- MV-A 与 Captain 的 DLL `146994d9`、计数、红名单逐位一致；还原后 `Logic=8330acf1`、`DLL=0736a602`、798/800。
- MV-C 说明「未知源零额度」这条被用例真钉住。
- **MV-B 全绿是本体最重要的负面结果**：常量本身写对了，但没有任何用例钉住「回调必须传常量」，
  漂移后行为退化为 `RETRY_TRIGGER_UNKNOWN` → 立即 GIVEUP 并误摘 player 钩子，而套件仍 798/800。

## 4. 五个必答

1. **预算是否够到 player-created？** 够。启动路径 `Initialize → Arm("startup")` 在菜单期必然 `!Provider.isServer`，
   `EnsureRetryHook()` 同时订阅两个源、各 3 次；level-loaded 每次只消费自己的额度，用满 3 次后
   `EnsureRetryHook` 因 `LevelLoaded < MaxArmRetries` 为假不再重挂（`Probe.cs:248`），player 源保持订阅。
   托管就绪后本地玩家创建 → `OnPlayerCreatedRetry` → 记账 1/3 → `Arm` 此时 `Provider.isServer=true`，
   目标解析成功即 `ARMED`。**「已托管未建玩家」不是漏掉的第三窗口**：该窗口内的唯一入口就是 player-created
   （或运行中开开关时的直接 `Arm`），两条都已覆盖。真正的第三种情形是 target-gone，已独立计数
   （`_targetGoneRetries`，`Probe.cs:49/433`），不与 arm 预算混用。
   边角：若宿主在 player-created 之前就已 3 次因「目标不在」被拒，则 player 额度耗尽后只能靠操作员重开开关；
   这是有界放弃，不是漏洞（见 R21-04）。
2. **GIVEUP 语义与退订是否成对？** 成立。`DecideRetry` 对已知源在 `used >= maxPerTrigger` 时返回
   `Retry=false/GiveUp=true`（`Logic.cs:1265`），不存在自增后重挂的回路；`TryRetry` 只在 `Retry=true` 时 `Arm`，
   GIVEUP 时 `UnhookRetrySource(trigger)` 只摘该源（`Probe.cs:355`）。禁用路径 `TryRetry` 首行
   `_enabledEntry==null || !Value → UnhookRetryHook()`（`:347`）、`OnEnabledChanged(false) → UnhookRetryHook()`（`:168`）
   都成对；`EnsureRetryHook` 先置标志再 `+=`，异常回滚标志（`:250-266`），未发现悬挂回调。
3. **常量是否真防住字面量漂移？** **没有测试守卫**（MV-B 全绿）。三个调用点确实都用常量
   （`Probe.cs:326`、`:331`、`:340` 经参数），静态看无漂移；但漂移后的行为不是「预算静默回到共享」，
   而是**更糟**：该源直接判未知、立即 GIVEUP，并因 R21-02 的 fall-through 误摘 player-created 钩子。
   结论：字面量漂移「不会回到共享」这半句成立，「防漂移」这半句只靠约定、不靠测试。
4. **未知触发源会不会被未来新增源误用？** 记账层不会（`DecideRetry` 不给额度、不动计数，MV-C 钉住）。
   但 `UnhookRetrySource`（`Probe.cs:287-298`）只把 `level-loaded` 当特例，其余（含未知/未来新源）
   一律落到 player 分支，把 player-created 钩子摘掉，同时日志还打印「另一触发源保留自己的预算」——
   这是一条**会误杀合法路径的静默分支**（当前不可达，故记 medium 而非 high）。
5. **日志是否泄露 SteamID？** 不泄露。`REFUSED` 只打原因码 + `origin`；`RETRY/GIVEUP` 打计数与
   trigger 名；`ARMED ... target=` 走 `Describe`（`Probe.cs:1717-1728`）只取 `playerName`；
   `localSteamId` 仅用于目标选择（`:213`），未进任何日志。`GATE_RESULT` 行不含 SteamID。

## 5. 字节级审查要点（只读）

- 预算状态 `Bug1RetryBudgetState{LevelLoaded,PlayerCreated}`（`Logic.cs:1224-1228`）为静态单例，重置点只有
  `OnEnabledChanged(true)`（`Probe.cs:157-158`）与测试缝 `__TestResetRetryBudget`（`:302-308`）；
  重新启用会清零，符合「一次启用周期」口径。
- 唯一记账点 `AccountRetry → DecideRetry`（`Probe.cs:338-341`），`TryRetry` 是唯一调用者（IL 用例钉住 `:1235`）。
- 用例 `Bug1-113`（`Tests.cs:1161-1238`）三段：纯函数正/反/未知、生产记账入口 `__TestAccountRetry` 的 3/1 断言、
  `TryRetry → AccountRetry` IL。**未覆盖**：两个回调是否真的传常量、`UnhookRetrySource` 的分支、
  GIVEUP 后另一源仍订阅（静态字段无测试缝）。
- 默认关闭未变：`Initialize` 只在 `_enabledEntry.Value` 为真时 `Arm`（`Probe.cs:91`）；未启用不订阅钩子、不写盘、不打探针日志。
- 扩张面：本轮仅动上述四个文件；`SteamP2PFriends.csproj` 未改；Bug3 五文件哈希未变。

## 6. Findings

- **R21-01（medium）** 常量漂移无测试守卫。`Platform/Diagnostics/Patches/Bug1GateProbe.cs:326/331` 的
  触发源实参若被改成任意字面量，MV-B 实测 798/800 全绿，运行时静默降级为
  `RETRY_TRIGGER_UNKNOWN` + 立即 GIVEUP。requiredFix：把 `TryRetry(string)` 改为 `TryRetry(RetrySource)`
  枚举（未知值不可表达），或在 `TryRetry` 入口对未知 trigger 打 `RETRY_TRIGGER_UNKNOWN` 且**不摘任何钩子**，
  并补一条调用回调的用例（经 internal 测试缝触发 `OnLevelLoadedRetry`，断言 budget.LevelLoaded 递增）。
- **R21-02（medium）** `UnhookRetrySource` 未知分支误摘 player 钩子。`Bug1GateProbe.cs:287-298`：
  仅 `level-loaded` 走特例，其余一律 `Player.onPlayerCreated -=`。未来新增第三个触发源若忘记改这里，
  会静默摘掉 player-created 的合法重试路径，且日志仍宣称「另一触发源保留自己的预算」。
  requiredFix：显式两分支 + `else { RoleLogger.Error(... RETRY_TRIGGER_UNKNOWN ...); return; }`，不改任何订阅。
- **R21-03（low）** target-gone 的「重试」可能是空操作。`Probe.cs:433-441` 在 `_targetGoneRetries < 3` 时
  先写 `retry n/3` 再 `EnsureRetryHook()`，但后者可能因两源预算均已用满而不订阅任何事件 → 实际不会再有尝试。
  requiredFix：`EnsureRetryHook` 返回是否挂上，未挂上时改打 GIVEUP 原因码（不要只写 retry 备注）。
- **R21-04（low）** 会话内无预算重置入口。预算只在开关重开时清零（`Probe.cs:157-158`）；长会话里
  「3 次 player-created 都不满足目标条件」之后探针静默停摆，操作者只能靠重开开关。建议在实机说明里写明
  「重试预算耗尽 = 需重开 Enable」，或增加一个 cfg 显式 reset 键。

## 7. pass 边界与 UNKNOWN

- 本判定只覆盖「分源预算把 arm 重试链延续到 player-created」这一工程性质；**不构成 G 门成立**，
  也**不构成 Bug1 已修复**。真实 Unity 下 `Player.onPlayerCreated` 是否在 listen host 上按
  level-loaded → 托管 → player-created 的顺序触发、`Provider.isServer` 翻转点具体在哪一帧，
  仍属 UNKNOWN，需要 1H+1G 实机日志（`RETRY`/`ARMED` 行）确认；离线用例只证明判定与记账意志。
- 未验证：真实环境里 `Level.onLevelLoaded` 是否会超过 3 次（换图/重载）——超过则 level 源按设计放弃，
  不影响 player 源；如需更强的「跨图续命」语义，应由后续票明确。

## 8. 复现命令

```bash
cd <repo>
sha256sum Platform/Diagnostics/Patches/Bug1GateProbe.cs Platform/Diagnostics/Patches/Bug1GateProbeLogic.cs
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe   # 798/800，仅 GBA01/NIL01
# 变异（副本）：Logic.cs:1263 used=state.LevelLoaded → 146994d9 → 797/800，仅 Bug1-113
#              Probe.cs:326 TryRetry("level_loaded") → c6aa7ec3 → 798/800（全绿，见 R21-01）
#              Logic.cs:1257 未知分支 state.LevelLoaded++ → 7433424d → 797/800，仅 Bug1-113
```

<!-- self_sha256(body)=d20f5fda8849bc24691bf2c27798576693ae7681cfbe952e8efa15c00c235e55 -->
