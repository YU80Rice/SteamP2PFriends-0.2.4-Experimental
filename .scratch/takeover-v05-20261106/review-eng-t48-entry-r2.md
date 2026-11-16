# T50 工程轴复审 r2 — Bug1 仪表入口修复轮（t48）

审查人：reviewer-eng（工程轴独立审查）
被审对象：t48「Bug1 仪表入口修复轮：判空自爆 + 相位计数断链 + fail-closed 开启」
审查日期：2026-11-07
任务：t50（attempt 2 / 595f1542-5d38-4592-b5f2-6b98ff2ce0c3），Reviewed task = t48

## 0. 结论

**verdict = pass（可进入实机待测交付）**，附 2 条 medium、4 条 low 与 3 项 UNKNOWN。

四条合同强制变异全部自跑且**全部变红**，红名单与自述一致（A→Bug1-38、B→Bug1-36、C→Bug1-40、D→Bug1-42），
还原后主树字节与 DLL 哈希均回到送审值。R8-01（判空自爆）、R8-02（Install 失败仍 START_OK）、
R8-04（关闭不卸 detour）、F1（相位计数断链）四项在代码层与变异层均成立。

**但本轮仍存在一处"标出来但不判红"的假绿（R9-01）**：R8-03 的启动重试只测了纯谓词
`EvaluateStartupRetry`，真正的接线（`EnsureStartupRetryHook` / `TryRetryStartup` / 有界计数 / 摘钩）
零覆盖——我把 `EnsureStartupRetryHook` 变异成永不挂钩后，727/729 全绿。这条不阻断交付
（探针默认关闭，最坏结果是"探针没起来"，不是"采到错数据"），但**不得**在交付说明里
声称"启动重试已被自动化验证"。

## 1. 被审字节锁定

| 文件 | 字节 | sha256（前 16 / 全量见文末） |
| --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1ProbeControl.cs` | 16449 | `aa6c8bea8ef3a733…` |
| `Platform/Diagnostics/Patches/Bug1ProbeEntryLogic.cs` | 4746 | `51460c685196f26d…` |
| `Platform/Diagnostics/Patches/Bug1ProbeNativeSpeed.cs` | 7614 | `109e283e5a37323c…` |
| `Platform/Diagnostics/Patches/Bug1ProbeRuntime.cs` | 51502 | `17ca472b6e86615b…` |
| `Platform/Diagnostics/Patches/Bug1ProbeJudge.cs` | 22193 | `5d863441a4376cf2…` |
| `Platform/Diagnostics/Patches/Bug1ProbeModel.cs` | 3684 | `eb40f8060db49aad…` |
| `Platform/Diagnostics/Patches/Bug1ProbePatch.cs` | 11707 | `22308d77197af4e5…` |
| `Platform/Diagnostics/Patches/Bug1ProbeRxPhases.cs` | 16693 | `8d0d36fc94cb1d6b…` |
| `Platform/Diagnostics/Patches/Bug1ProbeTargetSelector.cs` | 6682 | `b716369fac0f4bac…` |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeEntryTests.cs` | 8876 | `ae8eb0df4452fe54…` |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeEntryRepairTests.cs` | 24670 | `51db40833c3f9db4…` |
| `WhitelistTests/Program.cs` | 139203 | `8b278b6a1ee3fdd1…` |
| `SteamP2PFriends.csproj` | 27492 | `8b27ab5587b2656e…` |
| `bin/Release/SteamP2PFriends.dll` | 1480192 | `f28c211a9178818d…` |

与 t48 自报一致：DLL `f28c211a…`；`Bug1ProbePatch.cs` 由 t45 的 8872B 增至 11707B（新增 Uninstall 族），
`Bug1ProbeTargetSelector.cs` 为 t48 新文件（6682B）。

Bug3 五个生产文件哈希未变：
`a89f40b9…`（Patch）、`7a82de8e…`（Core）、`3d5044f7…`（Ledger）、`a4553385…`（Planner）、
`5b0c4e9b…`（Registration）。

## 2. 独立复跑（本人执行，不采信自述）

| 命令 | 退出码 | 结果 |
| --- | --- | --- |
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 0 | 0 警告 0 错误，DLL 仍 `f28c211a…`（确定性构建） |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | 0 | 0 错误 4 警告（CS0649×3 + CS0219，均既有） |
| `./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | 1 | **727/729 PASS (Failed: 2)**，Bug1 段 44 条全绿 |

两条失败与送审字节无关：`[PureMemory] GBA01`、`NIL01`（`TypeInitializationException
SDG.Unturned.SteamPlayer → NetReflection → ReflectionTypeLoadException`，控制台进程缺 Unity 运行时）。
与 t48 自述数字完全一致。

测试输出 `[T14] SPF_DLL_SHA256=f28c211a…` 与磁盘 DLL 一致，说明跑的是送审字节而不是陈旧产物。

## 3. 变异（仓库外副本 `../_t50mut`，已删除）

副本以 tar 复制（排除 `.git/.scratch/bin/obj/Release/publishi/Assets`），先独立重建得
`f28c211a…`，确认与主树同字节后再变异；每条变异单独重建、覆盖
`WhitelistTests/bin/Release/SteamP2PFriends.dll` 后跑全量；结束后逐文件还原并与主树对哈希。

| 变异 | 改动点 | 变异后 DLL | 结果 | 新红用例 |
| --- | --- | --- | --- | --- |
| A 删 EndStep 计数同步 | `Bug1ProbeRuntime.cs:721-722` 两行 `_window.Rx*Records = _rx*Records` 删除 | `bb1d7737c00c5abd…` | 726/729（Failed 3） | **Bug1-38**（`rx_after_enqueue` 期望 1、实际 0，判定变 Unknown） |
| B 一处判空改回 `== null` | `Bug1ProbeTargetSelector.cs:66` `ReferenceEquals(id, null)` → `id == null` | `ca34c4322295b046…` | 726/729 | **Bug1-36**（NRE 从 `Snapshot` 抛出） |
| C Install 失败仍置 Enabled | `Bug1ProbeRuntime.cs:126` `installed = __InstallSeam != null ? … : Install()` → `installed = true` | `e044227791a1b044…` | 726/729 | **Bug1-40**（Install=false 时 Enable 仍返回 true） |
| D 去掉 Uninstall 的 detour 卸载 | `Bug1ProbePatch.cs:114-125` 循环体 → `ok = true` | `415a8bc4862e80fc…` | 726/729 | **Bug1-42**（Disable 后 Enqueue detour 仍在） |
| E 启动重试钩子永不挂载（本轮自加，验证假绿） | `Bug1ProbeControl.cs:226` 首行插 `if (Environment.TickCount != int.MinValue) return;` | `086b9c08421d1df9…` | **727/729 全绿** | **无**（→ R9-01） |

还原核验：副本内 `Bug1ProbeRuntime.cs`=`17ca472b…`、`Bug1ProbePatch.cs`=`22308d77…`、
`Bug1ProbeControl.cs`=`aa6c8bea…`、`Bug1ProbeTargetSelector.cs`=`b716369f…`，与主树逐字节一致；
主树 DLL 仍 `f28c211a…`。副本目录已删除。

> 说明：合同要求的四条变异（A/B/C/D）**全部真红**，与 t48 自述红名单一致，本轮未发现自述失真。
> E 是我额外加的"应当全绿"对照，用于量化 R8-03 的测试覆盖缺口。

## 4. 逐条 acceptance 判定

### ① fail-closed 开启 —— PASS

`Bug1ProbeRuntime.cs:89-155`：先 `try { installed = seam ?? Bug1ProbePatch.Install(); }`，
失败（返回 false 或抛异常）→ 补 `LastError="INSTALL_FAILED:<Type>"` → `RollbackEnable()` → `return false`；
**只有** `installed == true` 之后才 `Enabled = true`（:152）、才 `Record("RUN_BEGIN")`、才 `BoundCamera(true)`。
`Bug1ProbeControl.TryStart` 在 `Enable` 返回 false 时走 `Refuse`，不会打 `START_OK`。
变异 C 证明这条分支有用例守（Bug1-40 同时断言 `!Enabled`、`INSTALL_FAILED` 前缀、无 RUN_BEGIN、失败后可再次开启）。

`RollbackEnable()`（:167-196）责任完整：`Enabled=false` → `Bug1ProbePatch.Uninstall()`（异常只记
`|UNINSTALL_FAILED:<Type>`）→ `BoundCamera(false)` → 清 `Config`/全部绑定/`_window`/trace/`_rx`/计数。
不存在"START_OK 但零 hook"，也不存在"Enabled 卡死"。

### ② Uninstall 幂等 —— PASS（覆盖 2/3 条路径）

`Bug1ProbePatch.Uninstall()`（:114-130）对 7 条 detour 逐条 `UnpatchOne`：
`original == null`、`patchMethod == null`、`!IsPatched(...)` 三种情况都直接算成功，
所以重复调用、未装过、target 已空都不抛；只摘本 owner（`_harmonyId` + `PatchMethod` 双匹配）的补丁，
同 target 上其它插件的补丁保留。`Registered=false` 无论如何都置。

`Disable()`（:207-240）与 `RollbackEnable()` 都调用它；`OnSessionReset()`（:1006-1011）走 `Disable("session_reset")`。
Bug1-42 覆盖：Disable 后两条 BCL detour 确实消失、再次 Enable 成功、`Uninstall()` 连续两次返回 true。

未覆盖的第三段（R9-04 low）：Bug1-42 是**测试自己**用 `harmony.Patch` 装上 Queue 两条 detour
（离线环境 `Assembly-CSharp` 的 5 条原生方法在测试进程里 IL Compile Error），
而 `Enable` 走的是 `__TestInstallSeam(() => true)` 捷径——即"`Install()` 真装 → `Uninstall()` 真摘"
这一条端到端链仍未在离线用例里闭合（`Install` 自身只被真实主进程验证）。

### ③ 启动重试 —— 代码 PASS，测试 FAIL（R9-01）

`Bug1ProbeEntryLogic.EvaluateStartupRetry`（:99-113）是纯函数、无 Unity 依赖，判定序为：
要求关闭/已在跑/预算耗尽 → GiveUp；非 host / 关卡未载 / 无可观察客机 → Wait；齐备 → Retry。

接线（`Bug1ProbeControl.cs:224-291`）：
`EnsureStartupRetryHook` 以 `_retryHooked` 去重，挂 `Level.onLevelLoaded` 与 `Player.onPlayerCreated`；
`TryRetryStartup` 每次 `_startupRetries++` 在 `Retry` 判定之后、`TryStart` 之前，
成功或 `_startupRetries >= MaxStartupRetries(3)` 或 GiveUp 时 `UnhookStartupRetry()`。
**上限是硬的（3 次）、终止是彻底的（摘钩子）、不会每次玩家进出都触发 Install**（成功后已摘钩）。
`OnEnabledChanged` 重新开启时把 `_startupRetries` 归零并先摘旧钩子，预算不会跨会话泄漏。

但：**上述接线零用例**。Bug1-41 只调用纯谓词；变异 E（钩子永不挂载）727/729 全绿。
也就是说"`EvaluateStartupRetry` 改了会红"，而"`EnsureStartupRetryHook` 被删/`TryRetryStartup` 被掏空/
预算写成无限"都不会红。这是本轮唯一一处与 R8-03 修复声明不匹配的覆盖缺口（见 R9-01）。

### ④ F1 计数同步 —— PASS

单一真值：`_rxEnqueueRecords`/`_rxDequeueRecords`（`Bug1ProbeRuntime.cs:76-77`），
`OnEnqueue` 在**非 guardRejected** 分支 `++`（:429），`OnDequeue` 在落记录后 `++`（:447）；
`Enable` 建窗时窗口计数置 0（:113-114），`EndStep` 在**调用 `Judge` 之前**把静态值同步进窗口
（:721-722），随后 `Judge(_window)` 与 `RUN_END` 行用的是同一份已同步数据，
`WriteArtifacts(verdict, …)` 写 `result.json` 也来自同一个 `verdict` 对象——
**不存在"RUN_END 打印 500 / 判定看到 0"的自相矛盾**。变异 A 正是通过打断这两行同步让 Bug1-38 变红，
反向证明同步点是判定的唯一入口。

线程可见性：计数与 `_traceLines` 均为普通静态字段，无锁无 `volatile`；写入点在 Harmony 主线程 hook
（`PlayerInput.ReceiveInputs` / `Queue.Enqueue|Dequeue` / `PlayerMovement.simulate|Update`），
`EndStep` 由 `Disable`/`OnSessionReset`/配置关闭触发，也在主线程（见 ⑦），因此当前**单线程模型下无撕裂**。
但这依赖调用方自律，代码里没有断言（R9-06 low）。

小瑕疵（R9-05 low）：同步写成 `if (_window.RxEnqueueRecords.HasValue) …`，而 `RUN_END` 的回退表达式
`_window.RxEnqueueRecords ?? _rxEnqueueRecords` 在 HasValue=false 时会打印静态值、同时让
`ValidatePhaseCoverage` 跳过覆盖检查——生产窗口恒 HasValue（Enable 置 0），当前不可达；
一旦有人复用合成窗口就会重现"两套数"的老问题。

### ⑤ 假绿检查 —— 有 1 处（R9-01），其余为真穿透

| 用例 | 是否走生产入口 | 判定 |
| --- | --- | --- |
| Bug1-36 / 37 | `Bug1ProbeTargetSelector.Snapshot/Select/SelectBySteamId`（生产唯一实现点） | 真穿透（36 还自带 SDK 运算符危害证明） |
| Bug1-38 / 39 | 生产 `Runtime.Enable` → `OnEnqueue/OnDequeue` → `EndStep` → `Judge` → 落盘 | 真穿透（仅 `__TestInstallSeam` 与 `__TestOffline` 两处 seam） |
| Bug1-40 | 生产 `Enable` 的失败分支（seam 注入 false / 抛异常 / 恢复 true） | 真穿透 |
| Bug1-41 | 仅 `Bug1ProbeEntryLogic.EvaluateStartupRetry`（纯谓词） | **接线未覆盖（R9-01）** |
| Bug1-42 | 生产 `Enable`/`Disable`/`Uninstall` + 真 Harmony detour | 真穿透（Install 段被 seam 替代，见 R9-04） |
| Bug1-43 | 生产 `Control.PrepareRun`（Validate 通过后才建目录） | 真穿透 |
| Bug1-44 | 生产 `NativeSpeed.ComputeTiers/ResolveTier` | 真穿透（纯算术） |

另：`Bug1ProbeControl.TryStart`（真正的入口，含 `ResolveTarget`/`FindPlayer`/`TryResolve` 调用序）
仍无任何用例——R8-01 的 NRE 若从 `TryStart` 里别处复发，现有 44 条抓不到（`ResolveTarget`
与 `FindPlayer` 现在都改走 selector，风险已大幅下降）。

### ⑥ 扩张面 —— PASS（含一次授权内修改）

- `SteamP2PFriends.csproj`：`git diff HEAD --numstat` = **14/0**，纯追加 14 条 `Compile`
  （Bug3 五条 + Bug1 九条，含新文件 `Bug1ProbeTargetSelector.cs`），无删除、无重排。
- `WhitelistTests/Program.cs`：**98/0**，纯追加（Bug1-36..44 九条 `RunTest` + Bug3 段）。
- `Release/`：目录 mtime 停在 11-07 01:32（preview2），本轮窗口（04:45+）未写。
- `publishi/`：`0.2.4.10` 17:28、`0.2.4.11-preview` 23:56，均未动。
- `Platform/Diagnostics/Patches/UseableBarricadeDiagnosticPatch.cs`：**3/1**，`git diff` 全文只有
  `owner.playerID == null` → `ReferenceEquals(owner.playerID, null)` 加三行注释。
  行为差异仅限"原来必抛 NRE 被 catch 成 `playerIdNull=unknown(NullReferenceException)`，
  现在能读到 true/false"——这是诊断输出变准，不是行为改写，属 Captain 授权的同款修复，判**不越界**。
- 未新增任何 PackageReference/Reference；`Bug1ProbeEntryRepairTests.cs` 通过既有
  `Evidence\**\*.cs` 通配进入编译（已实测编译成功）。

### ⑦ 线程亲和 —— PASS（with caveat）

`SettingChanged` → `OnEnabledChanged` → `TryStart` / `Disable` 全链在调用线程同步执行。
本仓 `BepInEx.dll` 的 `ConfigFile` 无 `FileSystemWatcher`/`ConfigReloader`（t47 已用 Cecil 核对），
`Reload` 只在构造函数里被调用，因此**没有后台线程自动重载**；实际热切换来自 ConfigurationManager
的 IMGUI，主线程。`Disable` 里 `_traceLines.Clear()`/`_rx.Reset()` 与 hook 写入同线程，无竞争窗口。

caveat 同 ④/⑦：代码里没有线程断言，API 层也不禁止别的插件从工作线程写 cfg（R9-06 low）。
"关闭后是否真卸干净"：`Disable` → `Uninstall` 摘 7 条 detour、`BoundCamera(false)` 退订
`Camera.onPreCull`、`_window=null`、绑定的 Unity 引用全部置空 → 是。

## 5. Findings

### R9-01（medium）启动重试只测谓词，接线零覆盖 → "R8-03 已修复"缺少可失败的证据

- 位置：`Platform/Diagnostics/Patches/Bug1ProbeControl.cs:224-291`（`EnsureStartupRetryHook` /
  `TryRetryStartup` / `UnhookStartupRetry`）；用例 `WhitelistTests/Evidence/Runtime/Bug1ProbeEntryRepairTests.cs`
  的 `Test_R8_03_StartupRetryDecision`（只调 `Bug1ProbeEntryLogic.EvaluateStartupRetry`）。
- 证据：变异 E 把 `EnsureStartupRetryHook` 改成永不在运行期挂钩（编译期不触发 CS0162），
  重建 DLL `086b9c08…` 后 **727/729、Bug1 44/44 全绿**，与基线完全一致。
- 影响：R8-03 的"钩子挂没挂、预算是不是 3、成功后是否摘钩、GiveUp 后是否彻底停"四条都没有自动化守门。
  代码经我逐行阅读是正确的（见 ③），所以不阻断交付；但这与本轮"补 9 条必然变红的用例"的表述不符。
- requiredFix（最小）：把 `TryRetryStartup` 的"决策 + 计数 + 是否继续挂"抽成 internal 纯函数
  （输入 `trigger/verdict/attemptsUsed/max`，输出 `NextAction{StayHooked,Unhook,Start}`），
  由 `TryRetryStartup` 只负责执行副作用；加一条用例断言"第 4 次触发不再调用 TryStart 且已摘钩"。
  退路：交付说明明确写"启动重试仅有代码审查，无自动化用例"，不得写成已验证。

### R9-02（medium）入口无兜底 try/catch，且拒绝/放弃后配置仍显示"已开启"

- 位置：`Bug1ProbeControl.cs:63-81`（`OnEnabledChanged`）、`:83-141`（`TryStart`）。
- 事实：`TryStart` 内部只有零散的 `try`（`Provider.client.m_SteamID`、`Directory.CreateDirectory`，
  以及 `TryResolve`/`Enable` 各自内部），整体无 catch；`OnEnabledChanged` 也没有。
  任何意外异常会沿 `ConfigEntry.SettingChanged` 的调用栈抛回 BepInEx/IMGUI 回调。
  同时 `Enable` 失败、`TryStart` 拒绝、`RETRY_GIVEUP` 三条路径都不会把 `_enabledEntry.Value` 回写为 false，
  玩家看到 cfg 是 `Enable=true`，其实仪表没跑（只有日志 `REFUSED`/`RETRY_GIVEUP`）。
- requiredFix：`OnEnabledChanged` 包 try/catch 记 `ENTRY_CRASH:<Type>`（与本文件既有
  `ENTRY_INIT_FAILED` 同一词表）；拒绝/放弃路径把 `_enabledEntry.Value=false`
  或至少在 START/STOP 之外再打一行 `PROBE_IDLE reason=<code>`，让"配置为真但未运行"可被一眼看出。

### R9-03（low）重试钩子只在 `ROLE_NOT_HOST` 时挂载，其余拒绝路径永久放弃

- 位置：`Bug1ProbeControl.cs:85-96`。
- 事实：只有 `!Provider.isServer` 才 `EnsureStartupRetryHook()`；若 `isServer==true` 但目标/关卡尚未就绪
  （`TARGET_STEAM_ID_MISSING`、`TARGET_PLAYER_NOT_FOUND`、`NATIVE_SPEED_CAP_UNRESOLVED`），
  `TryStart` 直接 `Refuse` 返回 false，**不挂任何重试**，配置仍为 true → 必须人工再切一次开关。
- requiredFix：把"是否挂重试"与拒绝码解耦（除 `ALREADY_RUNNING` 外都可挂），或明确文档化该限制。

### R9-04（low）离线用例未覆盖 "真 Install → 真 Uninstall" 全链

- 位置：`Bug1ProbeEntryRepairTests.cs:284-320`（Bug1-42 自己 `harmony.Patch` 两条 BCL detour，
  `Enable` 走 `__TestInstallSeam(() => true)`）。
- 影响：`Install()` 的 5 目标解析 + 7 hook 身份校验 + 半装回滚仍只在真实进程里被验证；
  "Install 装了一半就失败 → Uninstall 能否清干净"没有离线用例（代码看是对的：`UnpatchOne` 按
  `IsPatched` 判定，半装也能逐条摘）。
- requiredFix：在离线环境用 seam 之外的方式（或让 `Install()` 支持注入 5 个 target）
  造一次"部分 Verify 失败"，断言 `Uninstall` 后 7 条全部 `IsPatched==false`。

### R9-05（low）同步与打印用了两套回退表达式

- 位置：`Bug1ProbeRuntime.cs:721-724`。
- 事实：同步带 `HasValue` 守卫，打印用 `?? _rx*Records` 回退；生产窗口恒 HasValue，当前不可达，
  但合成窗口（HasValue=false）会同时出现"打印非零、覆盖检查被跳过"。
- requiredFix：判读与打印统一用同一表达式，或把窗口计数字段去掉 nullable（生产恒有值）。

### R9-06（low）线程亲和只靠约定，无断言

- 位置：`Bug1ProbeRuntime.cs:76-77`（普通静态 int）、`:207`（`Disable`）、`:702`（`EndStep`）。
- 事实：hook 写入与 `EndStep` 同步都假定主线程；`__TestEnable` 与真实 `Enable` 之间也没有线程断言。
- requiredFix：在 `Enable/Disable/EndStep` 首行记录 `Thread.CurrentThread.ManagedThreadId` 并与
  `Enable` 时的值比对，不一致时写 `THREAD_MISMATCH` 并 Degraded（只观测，不改行为）。

## 6. 泄漏与残留

- 静态单例：`Bug1ProbeRuntime` 的 `_window`/`_traceLines`/`_rx`/`Config`/绑定字段在
  `Disable`/`EndStep`/`RollbackEnable` 三处都会被清；异常路径靠 `finally` 兜底（`EndStep` :741-746）。
- 测试隔离：Bug1-38/39/40/42 都有 `finally → Cleanup(root)`（`__TestDisable` + seam 归 null +
  `__Offline(false)` + 删临时目录），Bug1-42 额外 `Uninstall` + `Harmony.UnpatchID`。
  Bug1-36/37/41/43/44 只碰纯函数与临时目录，无单例残留。
- 已知残留（不阻断）：`Bug1ProbePatch.Attempted/_harmony/_harmonyId` 在 Bug1-42 后指向测试 Harmony 实例；
  探针默认关闭，后续用例不会走到该实例；`__TestDisable` 不清 `Config`/`LastError`（沿用 t35 R7-04）。
- 测试会在仓库根留下 `SteamP2PFriendsNativeLeafProbe.dll`（3072B，05:23 重生成，被 .gitignore 覆盖），
  属既有测试副产物，非本轮新增。

## 7. UNKNOWN（离线不可证，必须在实机说明里保留）

1. **Mono 下 BCL detour 是否真触发**：`Queue<PlayerInputPacket>.Enqueue/Dequeue` 的泛型实例方法
   patch 在离线进程只能装这两条（原生 5 条报 IL Compile Error），真机 Mono 是否内联/是否命中未证。
2. **启动重试的真实时序**：`Player.onPlayerCreated` 与 `Level.onLevelLoaded` 的先后、以及
   `Provider.isServer` 在"点 Host 到关卡载入之间"的取值，决定 R9-03 是否真的会咬人。
3. **`SettingChanged` 的实际触发线程**：本仓 BepInEx 无文件监视（Cecil 结论），但 ConfigurationManager
   版本若变化可能改变这一前提。

## 8. 复现命令

```bash
# 基线
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe    # 727/729

# 变异（仓库外副本，tar 复制后先重建确认 f28c211a）
python /d/tmp/mut50.py backup && python /d/tmp/mut50.py A|B|C|D|E   # 在副本目录内执行
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
cp bin/Release/SteamP2PFriends.dll WhitelistTests/bin/Release/
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe
python /d/tmp/mut50.py restore
```

## 9. 全量哈希（供复核）

```
aa6c8bea8ef3a73309f6dee1232e9cbb2ef7bb3f57a7e989ab9473b8bf805ab5  Bug1ProbeControl.cs
51460c685196f26d69303c28388f6931c6e6f7540f0d4913827637fa36fa1299  Bug1ProbeEntryLogic.cs
109e283e5a37323c3c3838b62e595a290d84a95547f62a340e5581cd2d24dfae  Bug1ProbeNativeSpeed.cs
17ca472b6e86615b9653207c00c59cb20ef2fc934d559d5f8e2a146e5eb4e48d  Bug1ProbeRuntime.cs
5d863441a4376cf2fb5b6ce06a29bc4baf92f8cab4e3a29d6929df79988fbb9f  Bug1ProbeJudge.cs
eb40f8060db49aadfd65ec3328cb1c4104f8b3bf3ce6b3fdb6a0d2ffd92c5acd  Bug1ProbeModel.cs
22308d77197af4e58a34c8625f813e862783eebc06c3f5b129aaf29b28dd83d6  Bug1ProbePatch.cs
8d0d36fc94cb1d6b8ba98fb99e73c6e1691ccee04e2f7d928ad3830f1964ca4a  Bug1ProbeRxPhases.cs
b716369fac0f4bace41f9a86297755b22d665fd7b64e7382e25fa9feb9d06da8  Bug1ProbeTargetSelector.cs
ae8eb0df4452fe54b26553b6761632d2532761c7af708899ee7379a9b2e386f6  Bug1ProbeEntryTests.cs
51db40833c3f9db4f27f335c29a5ee42cc91a69ca5d13a87709381ec1792629e  Bug1ProbeEntryRepairTests.cs
8b278b6a1ee3fdd1d86a35b670a726fe0e71fa3516e7b80dfef43a465c0a0695  Program.cs
8b27ab5587b2656e0153cff2d2577c79d8d20bc2d5a5d544aadc572c732dd89e  SteamP2PFriends.csproj
f28c211a9178818d81d2d58721a4e5b9db101371ce0a6be836ff37561301e2e8  bin/Release/SteamP2PFriends.dll
```


<!-- self_sha256(body)=cab27944c8b1538591d09fbcabbbc2486c3a8a500d9da15d0918cfdbd504e9fb -->
