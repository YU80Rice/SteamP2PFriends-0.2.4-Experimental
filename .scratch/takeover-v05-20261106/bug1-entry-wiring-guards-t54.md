# Bug1 仪表接线守卫补钉（t54 / T52-F1、T52-F2、R10-01、R10-02、R10-04）

- 票号：t54（repair，round 1），被修：t52 findings + t53（R10-01/R10-02）
- 基线 DLL：`683c25e24825e073fdd2673bd5d70f77842920392dd1dfaa2c3e0dcab8232da5`（t53 送审字节）
- 本轮 DLL：`9fe6a0c8570d2e047a0ae96aa94a831363e3e04f4f5ea12f1093c9dbbcc327e7`
- 三命令：生产 0 警告 0 错误 / 测试 4 条既有警告 0 错误 / `=== Final Result: 735/737 PASS (Failed: 2) ===`
- 结论：**四条守卫缺口全部闭合，五条变异全部变红并逐字节还原**。仍未做的是 1H+1G 实机采集（见 §9），本报告不把自动化绿灯写成端到端可用。

## 1. 范围与取舍

| 项 | 处置 | 理由 |
| --- | --- | --- |
| T52-F1（生产开启路径无 IL 绑定） | 修，纯测试侧 | 生产已是 `TryStartCore → EnablePreparedRun`，缺的是「换回直连 `Enable` 会红」的证据 |
| T52-F2（忽略 `outcome.Unhook` 全绿） | 修，抽 `ApplyRetryOutcome` | 采纳动作必须可被行为用例直接断言，而不是只断言「调用存在」 |
| R10-01（保留调用但永不摘钩全绿） | 修，同上 | 与 T52-F2 同洞：钉调用不钉结果 |
| R10-02（T49-F1 只覆盖 2/5 输入） | 修，补三条纯测试 | 「已有目录 / HOST 非空 / run 目录非空」三种输入此前无用例，递归删除引入时不会红 |
| R10-04（cfg 回写无用例） | 修，测试侧反射注入 | 已实测 `ConfigEntry<T>` 的 4 参构造是 `internal`（`IsAssembly=true`），可用 `Activator` + `NonPublic` 构造，无需生产 seam |
| R10-05（`AbortStart` 在 SettingChanged 内回写 cfg 造成同步再入，深度 2） | **登记不改** | 当前无害；改它会动热切换语义，超出本票 |
| T49-F2（auto 档遇 CROUCH/PRONE 套 walk 上限） | 不动 | 由 captain 另问架构裁决 |
| T49-F3（探针外六处同类 `playerID == null`） | 不动 | 另开低优先级票 |

生产文件本轮只有一处**纯搬移**（`ApplyRetryOutcome` 抽取，语句/顺序/日志词照搬）与四个 `internal` 测试缝，运行时语义不变。

## 2. T52-F1：生产开启路径必须经回滚入口（纯 IL 断言）

用例 `Test_T52_F1_StartCoreRoutesThroughEnablePreparedRun`（Bug1-50）：

```
TryStartCore 调 Bug1ProbeControl.EnablePreparedRun   >= 1
TryStartCore 调 Bug1ProbeRuntime.Enable              == 0
TryStartCore 调 Bug1ProbeControl.PrepareRun          >= 1
```

判别力：把 `Bug1ProbeControl.cs:185` 换回 `Bug1ProbeRuntime.Enable(config)`（t52 的 MUT-4）→ `EnablePreparedRun` 计数归零、`Runtime.Enable` 计数变 1，两条断言同时失败。实测见 §6。

## 3. T52-F2 / R10-01：记账结果必须真的被采纳

### 3.1 生产改动（纯搬移）

`TryRetryStartup` 里原来的三句内联采纳：

```csharp
_startupRetries = outcome.AttemptsUsed;
if (outcome.GiveUp) { RoleLogger.Warn("[Shared]", Label + " RETRY_GIVEUP attempts=" + _startupRetries); }
if (outcome.Unhook) UnhookStartupRetry();
```

搬进 `internal static void ApplyRetryOutcome(Bug1RetryOutcome outcome)`，调用点改为 `ApplyRetryOutcome(outcome);`。日志词、判断顺序、写回时机与搬移前逐字一致。

### 3.2 测试缝（4 个，`internal`，不进生产路径）

`__TestArmRetryHook()`（只置 `_retryHooked=true`，不订阅 Unity 事件）、`__TestRetryHookArmed`、`__TestStartupRetries`、`__TestResetRetryState()`。

### 3.3 用例 `Test_T52_F2_RetryOutcomeIsAdopted`（Bug1-51）

两层：

1. IL：`TryRetryStartup` 调 `ApplyRetryOutcome` ≥ 1（绕过采纳 → 红）。
2. 行为（直调生产 `ApplyRetryOutcome`）：
   - `{AttemptsUsed=2, GiveUp=true, Unhook=true}` → `__TestStartupRetries==2` 且 `!__TestRetryHookArmed`（放弃分支真的摘钩）；
   - `{AttemptsUsed=1, GiveUp=false, Unhook=false}` → `__TestStartupRetries==1` 且 `__TestRetryHookArmed`（未到上限必须继续等）。

判别力：`if (false && outcome.Unhook)`（MUT-5）、`if (outcome.Unhook) { }`（M-B3 式「保留调用但永不摘钩」）、以及整句换成 `_startupRetries = outcome.AttemptsUsed;`（MUT-6）——三种都会在第 ① 组断言处红。实测见 §6。

## 4. R10-02：回滚只碰「本次新建且为空」的目录（补三条输入）

`Test_T49_F1_EnableFailureRemovesNewRunDirectory`（Bug1-45）在原两条输入之外追加三条，直接调生产 `DiscardNewRunDirectory`：

| 输入 | 期望 |
| --- | --- |
| 两级目录都已存在，`createdHost=false, createdRun=false` | 一个都不删 |
| `HOST` 内有 `trace.jsonl`，`createdHost=true, createdRun=true` | `HOST`、其内容、run 目录全部保留 |
| run 目录内有 `user-notes.txt`、`HOST` 为空，`true/true` | 只删空 `HOST`，run 目录与用户文件保留 |

判别力：把「判空 + `Directory.Delete(path, false)`」改成「`Directory.Delete(path, true)` 且去掉判空」（MUT-7）→ 第 ② 组（非空 HOST 被递归删掉）与第 ③ 组（run 目录里的用户文件被连带删掉）同时红。实测见 §6。

## 5. R10-04：cfg 回写已有用例覆盖

先验结论（反射实测 `Libs/BepInEx.dll`）：

- `ConfigEntry<T>` 只有一个构造：`Void .ctor(ConfigFile, ConfigDefinition, T, ConfigDescription)`，且 `IsPublic=False / IsAssembly=True`（`internal`）；
- `Value` 属性的 get/set 都是 public；
- `ConfigDescription.Empty` 静态属性可用。

因此用例 `Test_R10_04_AbortStartWritesBackConfig`（Bug1-52）在临时目录建真实 `ConfigFile`，用 `Activator.CreateInstance(..., BindingFlags.Instance|NonPublic, ...)` 构造 `ConfigEntry<bool>`（初值 `true`），反射注入静态字段 `_enabledEntry`，调用生产 `AbortStart(new InvalidOperationException(...))`，断言 `Value` 被回写为 `false`；`finally` 恢复原字段并删临时目录。**没有新增生产 seam**。

## 6. 变异证据（仓库外副本 `../_t54mut`，已逐字节还原并删除）

副本建立方式：`tar` 复制主树（排除 `.git/bin/obj/Release/publishi/.scratch`），基线重建后 DLL 与主树逐字节一致（`9fe6a0c8…`），源码 `Bug1ProbeControl.cs` 同为 `53a6c36b…`。

| 变异 | 改法 | 变异后 Control.cs sha256 | 变异后 DLL sha256 | 红名单 | 总数 |
| --- | --- | --- | --- | --- | --- |
| MUT-4（T52-F1） | `TryStartCore` 直连 `Bug1ProbeRuntime.Enable`，绕过 `EnablePreparedRun` | `f141f868…` | `6e1870ec21001a79e7c83979c3d7477d99a786c3a8235e5eac23cd3b2bff9616` | **Bug1-50** | 734/737 |
| MUT-5（T52-F2） | `if (false && outcome.Unhook) UnhookStartupRetry();` | `a97cbc97…` | `c25ee3afce7c4ef8d740bc8c810116127f0400358d7acc05a3d81f42a2123499` | **Bug1-51** | 734/737 |
| MUT-5b（M-B3 / R10-01） | `if (outcome.Unhook) { }`：保留调用、永不摘钩 | `a82f0dbb…` | `767a13b79f132b45512f488dd3092f7fa09e7406d05ae5fa4a9bc23223411e03` | **Bug1-51** | 734/737 |
| MUT-6（R10-01 接线） | `ApplyRetryOutcome(outcome);` → `_startupRetries = outcome.AttemptsUsed;` | `0ba7b3bc…` | `42dede8e3febddf33d3e1f76796c4efb329047ba5b3bf5f3738e1f831d06f86f` | **Bug1-51** | 734/737 |
| MUT-7（R10-02） | 去掉判空并把 `Directory.Delete(path, false)` 改成 `(path, true)`（HOST 与 run 两级都改） | `0181f982…` | `7f1bc5ad85daea05259e384746aabda5ae272734060ed3a06f60701bae7469cc` | **Bug1-45** | 734/737 |

每条变异后都 `cp` 回主树 `Bug1ProbeControl.cs`，五轮结束后副本重建：DLL `9fe6a0c8…`、`Bug1ProbeControl.cs` `53a6c36b…`，`diff -q` 与主树源码一致（SOURCE-IDENTICAL），随后 `rm -rf ../_t54mut` 删除副本。所有变异都只在副本里跑，主树未参与。

对照 t53 结论：M-B3 在 t53 是**绿**（732/734 全绿），本轮同一改法变**红**（Bug1-51），即 R10-01 的缺口被真正做活。

## 7. 三命令输出摘要

```
$ dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
  已成功生成。  0 个警告  0 个错误

$ dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
  4 个警告（均为既有 CS0649/CS0219，未新增）  0 个错误  已成功生成。

$ cd WhitelistTests/bin/Release && ./SteamP2PFriends.WhitelistTests.exe
  FAIL [PureMemory] GBA01 Native First Observe Binding
  FAIL [PureMemory] NIL01 Real Native Lookup Contract (TypeInitializationException: SDG.Unturned.SteamPlayer)
  === Final Result: 735/737 PASS (Failed: 2) ===
```

- Bug1 段 52/52 PASS（Bug1-01…Bug1-52）；失败只有两条既有环境失败（GBA01、NIL01），与 t53 基线一致；`exe` 退出码 1 属既有约定（有失败即 1）。
- 新增用例：Bug1-50（T52-F1）、Bug1-51（T52-F2/R10-01）、Bug1-52（R10-04）；Bug1-45 追加 R10-02 三条输入。

## 8. 逐文件 sha256 与未动项

| 文件 | 行数 | sha256 |
| --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1ProbeControl.cs` | 456 | `53a6c36bb809f7fc22a748823fbd2864d4bdc909cef76d39d75fc69cc55d617f` |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeEntryRepairTests.cs` | 736 | `43f2d28dc4a203c71b8246d0c4198b75d3c8254d4ebb7893a756a0df752de013` |
| `WhitelistTests/Program.cs` | 1052 | `a66c3fb59e6869a7642593a883ccc3581615b861b92fc368c08bd9c9a3911e5d` |
| `bin/Release/SteamP2PFriends.dll` | — | `9fe6a0c8570d2e047a0ae96aa94a831363e3e04f4f5ea12f1093c9dbbcc327e7` |

未动（与 t53 送审一致）：`Bug1ProbeEntryLogic.cs` `04a4beeb…`、`Bug1ProbeRuntime.cs` `17ca472b…`、`Bug1ProbePatch.cs` `22308d77…`、`Bug1ProbeTargetSelector.cs` `b716369f…`、`Bug1ProbeJudge.cs` `5d863441…`、`Bug1ProbeNativeSpeed.cs` `109e283e…`、`SteamP2PFriends.csproj`。

Bug3 五文件哈希复核（未变）：

```
a89f40b949f841d0  Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs
7a82de8e38fa0f6a  Core/Patches/P2PGroupStateRecovery/GroupStateRecoveryCore.cs
3d5044f7870fbb17  Core/Patches/P2PGroupStateRecovery/GroupLeaseLedger.cs
a45533850f31bd96  Core/Patches/P2PGroupStateRecovery/GroupLoadPlanner.cs
5b0c4e9ba4c87cbe  Core/Patches/P2PGroupStateRecovery/P2PGroupRegistration.cs
```

`Release/`、`publishi/` 未写：`find Release publishi -newermt "2026-11-07 06:00" -type f` 无输出。`Platform/Diagnostics/Patches/` 下 `playerID == null|playerID != null` 仍为 0 命中。

## 9. 已自动化可证 vs 仍需 1H+1G 实机

**已自动化可证（本轮新增/加强）**

- 生产开启路径必经 `EnablePreparedRun`（IL），绕过即红。
- 重试记账结果的采纳（写回次数 + 按 `Unhook` 摘钩）可被行为断言，`ApplyRetryOutcome` 被绕过或被掏空即红。
- 目录回滚只删「本次新建且为空」的两级目录，非空/已有目录不被碰，递归删除即红。
- `AbortStart` 会把 cfg 的 `Enable` 回写 `false`（真实 `ConfigEntry` 实例）。
- 上述均在离线控制台进程内跑，不依赖 Unity 运行时。

**仍需 1H+1G 实机（本票未证明，也不声称证明）**

1. Mono 下 BCL `Queue<PlayerInputPacket>` 的 `Enqueue`/`Dequeue` detour 是否真的触发（离线进程只能装这两条 BCL patch，5 条 Assembly-CSharp 原生 patch 报 `HarmonyException: IL Compile Error (unknown location)`）。
2. 启动期 `Enable=true` 的重试在真实关卡时序下是否命中（`Level.onLevelLoaded` / `Player.onPlayerCreated` 的触发顺序、`Provider.isServer` 与 `Level.isLoaded` 的真实取值）。
3. `PlayerInput.isDismissed` 成员是否存在、`OnReceiveInputsAfter` 的提前拒绝分支在真机是否出现。
4. R10-05 的 cfg 回写同步再入（深度 2）在 BepInEx ConfigurationManager 热切换下的真实行为，只登记未验证。

## 10. 未改项与理由

- **R10-05**（`AbortStart` 在 `SettingChanged` 回调内回写 cfg 触发同步再入 `OnEnabledChangedCore`，深度 2）：当前无害（第二次进入时 `Enable=false`，走关闭分支且 `Runtime.Enabled` 已为 false，无副作用）；修它需要引入「正在收尾」重入标志，会改变热切换语义，超出本票范围。建议在实机核对日志中观察是否出现成对 `STOP`/`REFUSED`。
- **T49-F2**（`auto` 档遇 CROUCH/PRONE 仍套 walk 上限）：待 captain 架构裁决。
- **T49-F3**（探针外六处同类 `playerID == null`）：另开低优先级票。
- **Bug1-46**：已降级为纯函数用例，注释写明「只覆盖 `EvaluateStartupRetry`/`AccountRetryAttempt` 穷举，不证明生产接线」，生产侧由 Bug1-47（调用存在）与 Bug1-51（结果被采纳）负责。

## 11. 交付边界

本轮只产出**守卫证据**：证明「接线/采纳/回滚」这三类回归会红，不构成 Bug1 根因结论，也不构成「仪表在真实主机可用」的结论——后者仍以 1H+1G 采集为准。
