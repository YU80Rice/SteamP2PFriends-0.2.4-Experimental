# t114 修复报告：触发源枚举化（R21-01）+ 未知源不误摘钩子（R21-02）

- 任务：t114 / repair（Fixes findings of t113）
- 成员：ds-probe2，attempt 2 `c92f01b4-b6e0-4759-b326-e4fac3292f0b`
- 基线（改前）：exe 798/800 PASS（仅 GBA01/NIL01），Bug1 段 114/114；DLL `0736a6023b64a4d6e916eb63471dcdc50c15cef32963fcde250d77e6423028db`
- 结论：两条 medium 已收口；触发源在编译期不可拼错，未知源不再触碰任何订阅；两条承重变异各自变红并逐字节还原。

## 1. 改前事实（t113 复审，已复现）

| 项 | 位置 | 症状 |
| --- | --- | --- |
| R21-01 | `Bug1GateProbe.cs:326` `TryRetry(Bug1GateProbeLogic.RetryTriggerLevelLoaded)` | 改成字面量 `"level_loaded"` 后编译通过；`DecideRetry` 按字符串比对判为未知源 → `RETRY_TRIGGER_UNKNOWN` → 立即 GIVEUP。审查官 MV-B：DLL `c6aa7ec3`、**798/800 全绿**，探针在生产上静默停摆 |
| R21-02 | `Bug1GateProbe.cs:287-298` `UnhookRetrySource(string)` | 只判 level-loaded，其余值一律落 player 分支摘 `Player.onPlayerCreated`；未知/未来新增源会误摘 player 钩子，日志还宣称「另一触发源保留预算」 |

## 2. R21-01：触发源改枚举

`Platform/Diagnostics/Patches/Bug1GateProbeLogic.cs`

- `:1215` 新增 `public enum Bug1RetryTrigger { LevelLoaded = 0, PlayerCreated = 1 }`（删除了 `RetryTriggerLevelLoaded` / `RetryTriggerPlayerCreated` 两个字符串常量）。
- `:1225` 新增 `public static string TriggerName(Bug1RetryTrigger trigger)`：仅用于日志/产物展示（`"level-loaded"` / `"player-created"` / `"unknown-trigger:<n>"`），**不参与任何记账判定**。
- `:1263` `DecideRetry` 签名改为 `DecideRetry(Bug1RetryBudgetState state, Bug1RetryTrigger trigger, int maxPerTrigger)`；`:1277-1290` 用 `switch (trigger)` 分派 `used = state.LevelLoaded / state.PlayerCreated`，`default:` 只有强转出来的非法枚举值会到达，直接 `RETRY_TRIGGER_UNKNOWN` 返回且不改计数；`:1297` 写回同样按枚举。
- 「编译期不可拼错」的论证：调用点现在写 `TryRetry(Bug1RetryTrigger.LevelLoaded)`，拼错成员名 → `CS0117`；把字符串塞进去 → `CS1503`（不能把 string 转成枚举）。运行期不再存在「字符串精确匹配失败 → 落未知分支」的路径。

调用点（`Platform/Diagnostics/Patches/Bug1GateProbe.cs`）：`:357` `OnLevelLoadedRetry` → `TryRetry(Bug1RetryTrigger.LevelLoaded)`；`:362` `OnPlayerCreatedRetry` → `TryRetry(Bug1RetryTrigger.PlayerCreated)`；`:370` `AccountRetry(Bug1RetryTrigger trigger)` → `DecideRetry(_retryBudget, trigger, MaxArmRetries)`；`:375` `TryRetry(Bug1RetryTrigger trigger)`。

## 3. R21-02：未知源只记日志、不动订阅

`Platform/Diagnostics/Patches/Bug1GateProbe.cs:289` `UnhookRetrySource(Bug1RetryTrigger trigger)` 改为 `switch`：

- `LevelLoaded` → 只摘 `Level.onLevelLoaded`，`_levelRetryHooked = false`；
- `PlayerCreated` → 只摘 `Player.onPlayerCreated`，`_playerRetryHooked = false`；
- `default:` → `RoleLogger.Warn("[Shared]", Label + " RETRY_UNHOOK_SKIPPED trigger=" + TriggerName(trigger) + "（未知触发源不摘除任何订阅）")` 后 `return`，**不碰任何标志、不解绑任何事件**。

为让订阅状态可离线断言，新增四个测试缝（`:322-345`）：`__TestAccountRetry(Bug1RetryTrigger)`、`__TestUnhookRetrySource(Bug1RetryTrigger)`、`__TestRetryHookFlags(out bool, out bool)`、`__TestSetRetryHookFlags(bool, bool)`。

## 4. 守卫用例（Bug1-114）

`WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs:1246` `Test_R21_EnumTriggerIsGuarded`，注册于 `WhitelistTests/Program.cs:1021`。四组断言：

1. **枚举→计数器映射**：`LevelLoaded` 只增 `state.LevelLoaded`、`PlayerCreated` 只增 `state.PlayerCreated`（互不污染）。
2. **展示名可区分**：`TriggerName` 两个已知源分别返回 `level-loaded` / `player-created`。
3. **回调传的枚举常量被 IL 钉死**：`:1306` `TriggerLiteralBeforeTryRetry(MethodInfo)` 用 `IlContractProbe.ReadInstructions` 找到 `call TryRetry`，向前取压栈的 `ldc.i4` 操作数，要求 `OnLevelLoadedRetry` 恰为 `0`、`OnPlayerCreatedRetry` 恰为 `1`——传错/传未知值即红（把「改错不报错」变成「改错就红」）。
4. **未知源不动订阅**：`__TestSetRetryHookFlags(true,true)` 后 `__TestUnhookRetrySource((Bug1RetryTrigger)99)`，两个标志都必须仍为 `true`；随后用两个已知源验证「只摘自己」（防止「谁都不摘」的假绿）。

Bug1-113（`Test_R21_RetryBudgetIsPerTrigger`）同步改为枚举调用，未知源用 `(Bug1RetryTrigger)99` 强转构造，其余断言不变。

## 5. 变异（唯一承重证据，副本 `../_t114mut`，跑完已删）

脚本 `../_t114mut/mut.py`：唯一替换 → 先 `dotnet build` 生产 → 再 build 测试 → `subprocess.run([exe], cwd=dirname(exe), capture_output=True)` 跑全量 → 打印变异后 DLL sha256 与含 `Bug1-` 的 FAIL 行 → 从主树 `cp` 还原 → 打印还原后源码 sha256。

| 变异 | 文件:行 | 原文 → 替换文 | 变异后 DLL sha256 | 结果 | 红名单 |
| --- | --- | --- | --- | --- | --- |
| MV-1 改回字符串匹配并拼错一个字面量 | `Bug1GateProbeLogic.cs:1277-1290` | `switch (trigger) { case LevelLoaded: used = state.LevelLoaded; ... }` → `string drifted = trigger.ToString(); if (drifted == "level_loaded") ... else if (drifted == "PlayerCreated") ... else { RETRY_TRIGGER_UNKNOWN; return; }` | `8e193972cdbcd831f50cfd84f0a200bb7add18a4771113c670d3c012c163cdbc` | **797/801**（Failed 4 = 2 条既有环境 + 2 条自建） | **Bug1-113**、**Bug1-114** |
| MV-2 未知源走 player 分支摘钩 | `Bug1GateProbe.cs:303-307`（`default:` 分支） | `default: RoleLogger.Warn(... RETRY_UNHOOK_SKIPPED ...); return;` → `default: _playerRetryHooked = false; return;` | `71953765af86debfc492887376e7fe2ec185b8f7918d2857b67a9314787257a7` | **798/801**（Failed 3 = 2 条既有环境 + 1 条自建） | **Bug1-114** |

- 每条变异只让对应的自建用例变红，没有连带打红其它 Bug1 用例。
- 还原后源码 sha256：`Bug1GateProbe.cs` `8628ec559a271721250902624cf969dc236b8c8d52834150f3bbacdb72c9fd97`、`Bug1GateProbeLogic.cs` `e7de8d6e3c49aafeba32522ad93e74ac91163c53cba3df37f593bfe848fdc5ea`，与主树逐字节一致；副本 `../_t114mut` 已删除。

## 6. 三命令（最终树）

| 命令 | 结果 |
| --- | --- |
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 已成功生成，**0 警告 0 错误** |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | 已成功生成，**4 条既有警告（CS0649，均为既有测试字段）、0 错误** |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `=== Final Result: 799/801 PASS (Failed: 2) ===`，FAIL 仅 `[PureMemory] GBA01 Native First Observe Binding` 与 `[PureMemory] NIL01 Real Native Lookup Contract`（TypeInitializationException 链，既有环境失败），**Bug1 段 115/115 PASS**；exit=1 属既有约定（有失败即 1） |

## 7. 终态哈希

| 文件 | 行数 | sha256 |
| --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1GateProbe.cs` | 1784 | `8628ec559a271721250902624cf969dc236b8c8d52834150f3bbacdb72c9fd97` |
| `Platform/Diagnostics/Patches/Bug1GateProbeLogic.cs` | 1510 | `e7de8d6e3c49aafeba32522ad93e74ac91163c53cba3df37f593bfe848fdc5ea` |
| `WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs` | 1328 | `bc4546785dbc63816dfcd396eebc3b1163ae14724061ed7b32223043161c1141` |
| `WhitelistTests/Program.cs` | 1120 | `04f1b024b2f2d4b255e7176a3bab2cd289d97699a62643b92d953a33f609c306` |
| `bin/Release/SteamP2PFriends.dll` | — | `3ee2e52f62d58c3ab5f8cd9d4d2ae1e53b2a49e94b7f202a85ee082df69e7e2b` |

改前对照：Probe `837c0a5d…`(1752)、Logic `8330acf1…`(1484)、Tests `892bc56a…`(1240)、Program `a2a43fab…`(1119)、DLL `0736a602…`。

## 8. 边界与未改项

- **默认关闭不变**：`Initialize()` 仍只在 `_enabledEntry.Value == true` 时 `Arm("startup")`；未启用时不订阅 `Level.onLevelLoaded` / `Player.onPlayerCreated`、不挂相机回调、不写盘、不打本探针日志。本轮未改这段。
- **Bug3 五文件哈希未变**：`a89f40b949f841d0` / `7a82de8e38fa0f6a` / `3d5044f7870fbb17` / `a45533850f31bd96` / `5b0c4e9ba4c87cbe`。
- **未写** `Release/`、`publishi/`、`Develop-Stage/`、`publish/`（`find ... -newermt "2026-11-08 12:00"` 无输出）。
- `T69-01` 注册行未动；Bug1 段由 114 增至 115（只加 Bug1-114）。
- 变异全部在仓库外副本进行，主树未做任何变异。

## 9. 口径

本轮只把两处「改错就静默失效」的隐患变成「改错就编译失败 / 改错就红」：触发源枚举化 + 未知源不摘订阅。**不构成 G 门成立，也不构成 Bug1 已修复**；真实 1H+1G 上的重试时序（level-loaded 与 player-created 实际先后）、`GATE.json` 是否落盘仍属实机待验项，离线证据只说明这两条判据自身自洽且可承重。
