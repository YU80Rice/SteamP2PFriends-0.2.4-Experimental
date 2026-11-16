# T49 规格轴复审 r2 — t48 Bug1 仪表入口修复轮

- 票号：t49（被审 t48，attempt 2；审查官 attempt `5fd6596d-5489-4b57-a9ec-d10f0a75f476`）
- 判定范围：只判「是否满足需求契约与架构裁决」，不评实现风格；只读判定，未修改主树任何字节。
- **verdict：needs_revision**（7 项主判据中 6 项真关闭、1 项部分关闭；F3 的「拒绝路径不留空 run 目录」在 `INSTALL_FAILED` 拒绝路径上仍不成立，见 F1）
- 复审结论**不等于** Bug1 已诊断，也不等于可出包：三项实机 UNKNOWN 仍开（§7）。

## 1. 被审字节绑定（本机实测，2026-11-07 05:16 取样）

| 文件 | 字节 | sha256 | 与 t48 报告一致 |
| --- | --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1ProbeTargetSelector.cs`（新增） | 6682 | `b716369fac0f4bace41f9a86297755b22d665fd7b64e7382e25fa9feb9d06da8` | 是 |
| `Platform/Diagnostics/Patches/Bug1ProbeControl.cs` | 16449 | `aa6c8bea8ef3a73309f6dee1232e9cbb2ef7bb3f57a7e989ab9473b8bf805ab5` | 是 |
| `Platform/Diagnostics/Patches/Bug1ProbeEntryLogic.cs` | 4746 | `51460c685196f26d69303c28388f6931c6e6f7540f0d4913827637fa36fa1299` | 是 |
| `Platform/Diagnostics/Patches/Bug1ProbeNativeSpeed.cs` | 7614 | `109e283e5a37323c3c3838b62e595a290d84a95547f62a340e5581cd2d24dfae` | 是 |
| `Platform/Diagnostics/Patches/Bug1ProbePatch.cs` | 11707 | `22308d77197af4e58a34c8625f813e862783eebc06c3f5b129aaf29b28dd83d6` | 是 |
| `Platform/Diagnostics/Patches/Bug1ProbeRuntime.cs` | 51502 | `17ca472b6e86615b9653207c00c59cb20ef2fc934d559d5f8e2a146e5eb4e48d` | 是 |
| `Platform/Diagnostics/Patches/Bug1ProbeJudge.cs` | 22193 | `5d863441a4376cf2fb5b6ce06a29bc4baf92f8cab4e3a29d6929df79988fbb9f` | 是（未改） |
| `Platform/Diagnostics/Patches/Bug1ProbeModel.cs` | 3684 | `eb40f8060db49aadfd65ec3328cb1c4104f8b3bf3ce6b3fdb6a0d2ffd92c5acd` | 是（未改） |
| `Platform/Diagnostics/Patches/Bug1ProbeRxPhases.cs` | 16693 | `8d0d36fc94cb1d6b8ba98fb99e73c6e1691ccee04e2f7d928ad3830f1964ca4a` | 是（未改） |
| `Platform/Diagnostics/Patches/UseableBarricadeDiagnosticPatch.cs` | — | `50eb1dd3f22f076feff8a906633cacfba1af80e949f4a7764357c5c99d6532f8` | 是（跨票同类判空） |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs` | 11924 | `ba5b7a73b520e807037e38d8ddbfac82953322f0a0899e85a990ef3e6a9eb0eb` | 是（未改） |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeEntryRepairTests.cs`（新增） | — | `51db40833c3f9db4f27f335c29a5ee42cc91a69ca5d13a87709381ec1792629e` | 是 |
| `WhitelistTests/Program.cs` | 131931 | `8b278b6a1ee3fdd1d86a35b670a726fe0e71fa3516e7b80dfef43a465c0a0695` | 是 |
| `SteamP2PFriends.csproj` | 27097 | `8b27ab5587b2656e0153cff2d2577c79d8d20bc2d5a5d544aadc572c732dd89e` | 是 |
| `bin/Release/SteamP2PFriends.dll` | 1465856 | `f28c211a9178818d81d2d58721a4e5b9db101371ce0a6be836ff37561301e2e8` | 是 |

- 主树与 `WhitelistTests/bin/Release/SteamP2PFriends.dll` 同名同哈希；`WhitelistTests` 通过 HintPath 引用 SPF 的 Release 输出。
- `SteamP2PFriends.csproj` 相对 HEAD 为 **14 行纯追加、0 删除**（5 个 Bug3 文件 + 9 个 Bug1Probe 文件，含 `Bug1ProbeTargetSelector.cs`、`Bug1ProbeControl.cs`、`Bug1ProbeEntryLogic.cs`、`Bug1ProbeNativeSpeed.cs`）。
- `WhitelistTests/Program.cs` 登记 Bug1 段 **44 条**（`Bug1-36`…`Bug1-44` 为本轮新增，`Program.cs:937-945`）。
- 本判定只对上述字节有效；后续任何字节变化需重判。

## 2. 七项主判据逐条

### 2.1 R8-01（blocker，判空自爆）— **关闭**

- 唯一判空点：`Bug1ProbeTargetSelector.cs:60-71`。`Snapshot()` 先 `ReferenceEquals(client, null)`（`:63`），再取 `SteamPlayerID id = client.playerID`（`:64`）并 `ReferenceEquals(id, null)`（`:65`）——全程不触发 `SteamPlayerID` 重载的 `==`。
- 三个入口全部改走该层：`Bug1ProbeControl.cs:153` `ResolveTarget`（`:161` 调 `SnapshotAll`）、`Bug1ProbeControl.cs:177` `FindPlayer`（同样经 `SnapshotAll`）、`Bug1ProbeRuntime.cs:241` `TryBindTarget`（内部同样经 `SnapshotAll` + `Select`）。
- 自我排除也不再碰 `SteamPlayerID` 的 `==`：`Bug1ProbeTargetSelector.cs:96` 用 `localSteamId != 0UL && client.SteamId == localSteamId`（`ulong` 比较）跳过并记 `SkipSelf`（`:53`），歧义在 `:119-120` 落 `TARGET_PLAYER_AMBIGUOUS`。
- grep 证据：在 `Platform/Diagnostics/Patches/Bug1Probe*.cs` 内对 `playerID` 的 `==`/`!=` 判空 **0 命中**（命中的 `== null` 全在非 `playerID` 的普通引用上：`clients`、`queue`、`_third`、`Transform` 等）。
- 可失败性：`Bug1ProbeEntryRepairTests.cs:56-84`（Bug1-36）用 `FormatterServices.GetUninitializedObject(typeof(SteamPlayer))` 造 `playerID == null` 的真实类型实例，并**显式断言 `if (id == null)` 确实抛 NRE**（`:70-74`），把隐患钉死在用例里；随后断言 `Snapshot` 不抛、`HasPlayerId=false`、`Select` 记为 `playerid-null` 并跳过。
- 自跑变异见 §3 MUT-B。

### 2.2 F1（规格轴 blocker，相位计数断链）— **关闭**

- 唯一真值：静态累加器 `_rxEnqueueRecords` / `_rxDequeueRecords`（声明 `Bug1ProbeRuntime.cs:76-77`，唯一递增点 `:429`（`OnEnqueue`）与 `:447`（`OnDequeue`））。
- `Enable` 只负责把 window 计数与静态计数一起归零（`:108-115`、`:124-125`），不再各写一套。
- `EndStep` 在判读前同步：`Bug1ProbeRuntime.cs:721-722`
  `if (_window.RxEnqueueRecords.HasValue) _window.RxEnqueueRecords = _rxEnqueueRecords;`（出队同构），随后 `:723-724` 取的 `rxAfterEnqueue/rxAfterDequeue` 与 `:725` `Judge(_window)` **同源**；`RUN_END` 行 `:732-733` 写的就是这两个值，`result.json` 由 `:738` `WriteArtifacts(verdict, reason)` 用同一个 `verdict` 落盘。
- 判失败点：`Bug1ProbeJudge.cs:190-197`——`w.RxEnqueueRecords.HasValue && <= 0` → `MarkUnknown(v, "AFTER_ENQUEUE", "PHASE_COVERAGE_MISSING")`（出队同构）。
- 因此「RUN_END 打 500 而 result.json 判 PHASE_COVERAGE_MISSING」不可能再出现：两者读同一对 int，判据与打印之间无第二个来源。
- 可失败性：MUT-A（§3）删除 `:721-722` 后，`Bug1-38` 实测红，`RUN_END` 变 `"rx_after_enqueue":0,"label":"Unknown","missing_fields":["AFTER_ENQUEUE","AFTER_DEQUEUE"]`。`Bug1-39` 是反向正控（零记录必须判 UNKNOWN），删同步不会让它变红，删 `Judge` 的 `<=0` 判据才会——两侧都有用例。

### 2.3 R8-02（high，Install 失败仍 START_OK）— **关闭**

- `Bug1ProbeRuntime.cs:129-160`：`installed = __InstallSeam != null ? __InstallSeam() : Bug1ProbePatch.Install()`（`:135`）→ 异常归一为 `INSTALL_FAILED:<类型>` → `:142-150` `!installed` 时补 `INSTALL_FAILED:patch_install_returned_false`、`RollbackEnable()`（`:148`）并 `return false`；**`Enabled = true` 在 `:152`，即安装成功之后**；`RUN_BEGIN` 在 `:154` 之后。
- `RollbackEnable`（`:167-194`）真的卸 detour（`Bug1ProbePatch.Uninstall()`）、退订相机、清 window/队列/计数与 `Config`，不留半装状态。
- `Bug1ProbePatch.Install()`（`:42-107`）本身也不是空跑：解析 5 个 target，取 7 个 hook 方法，逐个 `_harmony.Patch` 后用 `Verify(owner+original+PatchMethod)` 逐条核对（`:81-87`，7 条与 7 个 hook 一一对应），任一失败 → `FailClosed = true` 且 `return false`（`:88-93`）。
- `Bug1ProbeControl.cs:126-127`：`Enable` 返回 false 即 `Refuse(Bug1ProbeRuntime.LastError, …)`，不再打 `START_OK`（`START_OK` 在 `:129`，位于成功分支之后）。
- 可失败性：`Bug1ProbeEntryRepairTests.cs`（Bug1-40，`Program.cs:941`）覆盖「seam=false → Enable false / `!Enabled` / `LastError` 以 `INSTALL_FAILED` 开头 / 不写 RUN_BEGIN」「seam 抛异常 → `INSTALL_FAILED:InvalidOperationException`」「随后 seam=true 能再次开启」。
- 说明性缺口（非缺陷）：该用例经 `__TestInstallSeam` 走 Runtime 的决策分支，**没有**直接诱导 `Bug1ProbePatch.Install()` 内部的 verify 失败（离线环境 5 条原生 patch 报 IL Compile Error，构造不出「装了一半」的干净场景）；安装器自身的失败返回由本节的读码证据支撑。

### 2.4 R8-03（high，启动期开启永不生效）— **关闭（离线可证部分）**

- `Bug1ProbeControl.cs:85-94`：非 host 时不再一拒到底——先 `EnsureStartupRetryHook()`（`:90`）再 `Refuse(ROLE_NOT_HOST, …)`（`:91`），日志里写明「最多 N 次」。
- 钩子：`EnsureStartupRetryHook`（`:224-233`）订阅 `Level.onLevelLoaded` / `Player.onPlayerCreated`（各自 try/catch，失败打 `RETRY_HOOK_FAILED`，成功打 `RETRY_ARMED`），`UnhookStartupRetry`（`:235-241`）对应退订；`OnEnabledChanged(true)`（`:63-71`）重置预算并摘旧钩子。
- 判定：`TryRetryStartup`（`:247-283`）→ `DecideRetry`（`:288-294`）→ `Bug1ProbeEntryLogic.cs:76-86` `EvaluateStartupRetry`（未请求/已在跑/次数用尽 → GiveUp；非 host / 关卡未加载 / 无客机 → Wait；齐备 → Retry）。Retry 成功后立即 `UnhookStartupRetry()`（`:265`）；超次数打 `RETRY_GIVEUP`（`:275-278`）。
- 有界可关：`MaxStartupRetries = 3`（`Control.cs:27`），且 `EvaluateStartupRetry` 对 `maxAttempts <= 0` 返回 GiveUp。
- 可失败性：Bug1-41（`Program.cs:942`）穷举 9 个分支（含 maxAttempts=0）。
- **UNKNOWN（保留）**：真实关卡时序（`Provider.isServer` 变 true 与 `Level.onLevelLoaded`、首个 client 进入的先后）离线不可证，见 §7.2。

### 2.5 R8-04（medium，无 Uninstall）— **关闭**

- `Bug1ProbeRuntime.Disable`（`:207-238`）在 `EndStep` 之后 `:222` `Bug1ProbePatch.Uninstall()`，并清绑定与计数。
- `Bug1ProbePatch.Uninstall`（`:114` 起，7 条 `UnpatchOne` 在 `:119-125`）对本 owner 的 7 条 detour 逐条 `UnpatchOne`，`UnpatchOne` 先 `IsPatched` 判幂等、再 `_harmony.Unpatch(original, patchMethod)`（按 PatchMethod 匹配，不会误伤同 target 上其它插件）。
- 可失败性：Bug1-42（`Program.cs:943`）用真实 `Harmony` 装 `Queue<PlayerInputPacket>.Enqueue/Dequeue` 两条 detour，`Enable`→`Disable` 后两条 `Verify` 必须变 false（证明 Uninstall 不是空实现），再 `Enable` 必须成功，`Uninstall()` 连续两次 true（幂等）。
- **UNKNOWN（保留）**：5 条 Assembly-CSharp 原生 detour（simulate prefix/postfix、Update、ReceiveInputs prefix/postfix）在离线进程装不上（`HarmonyException: IL Compile Error`），其真实卸载只能在实机覆盖；t48 报告 §12 已如实声明该环境限制，未拿 BCL 两条冒充原生五条。

### 2.6 F3（medium，拒绝路径留空 run 目录）— **部分关闭**（→ F1 finding）

- 修法与证据：`Bug1ProbeControl.cs:193-221` `PrepareRun` 先 `Bug1ProbeEntryLogic.Validate`（`:199-201`，含 `ALREADY_RUNNING`），**通过后**才 `Directory.CreateDirectory(outputRoot)`（`:202`）；`TryStart` 只走 `PrepareRun`（`:123-125`）。
- 用例：Bug1-43（`Program.cs:944`）断言 `alreadyEnabled=true` 与缺 RunId 两条拒绝路径下 `baseDir`/`root` 均不存在，成功路径才建目录并带上 `SpeedTier`/两档上限。
- **未覆盖的拒绝路径**：`TryStart` 在 `:123` 已经建好目录，`:126` `Enable` 失败时 `:127` 直接 `Refuse`，**不会删掉刚建的 `<runId>/HOST`**。`Install()` 在真实主机上可失败（ABI 解析失败、verify 失败、Harmony 抛错），因此这是可达路径，与 t48 报告 §7「拒绝路径不留空 run 目录」的结论句不符。见 F1。

### 2.7 F4（low，速度上限恒取 sprint）— **关闭**（附残留 F2）

- `Bug1ProbeNativeSpeed.cs:97-125` `ComputeTiers`：`walk = SPEED_STAND × exercise × totalSpeedMultiplier`、`sprint = SPEED_SPRINT × exercise × totalSpeedMultiplier`，两档都冻结；`sprint < walk` 判非法。
- 与 SDK 逐行对照：`.scratch/bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerMovement.cs:93-96`（`SPEED_SPRINT = 7f`、`SPEED_STAND = 4.5f`、`SPEED_CROUCH = 2.5f`、`SPEED_PRONE = 1.5f`）、`:303`（`exercise = 1f + mastery(OFFENSE,EXERCISE) * 0.25f`）、`:309-316`（sprint 分支 `SPEED_SPRINT * exercise * totalSpeedMultiplier`、stand 分支 `SPEED_STAND * exercise * totalSpeedMultiplier`）——同式，无额外系数。
- 取值：`TryResolve` 反射读两个常量与 `player.skills.mastery(...)`、`player.movement.totalSpeedMultiplier`（`:43-89`），任一环取不到返回 false，入口按 `NATIVE_SPEED_CAP_UNRESOLVED` 拒绝（`Control.cs:108-109`）。
- 档位：`ResolveTier`（`:131-138`）`auto`/空 → 按开启瞬间姿态；`walk`/`sprint` 强制；非法值 → null → `Control.cs:115-116` 拒绝，不静默回落。
- 可失败性：Bug1-44（`Program.cs:945`）断言 `4.5/7/1.25/1.1 → 6.1875 / 9.625`、两档不相等、`sprint<walk` 拒绝、auto/空/walk/sprint/非法五分支。
- 残留见 F2（crouch/prone 仍归 walk 档）。

## 3. 自跑变异记录（仓库外副本，主树未动）

副本 `../spf-t49-iso`（与主树同级，`tar` 排除 `.git/bin/obj` 建；`..\Libs` 相对引用成立）。所有构建均为 `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild`，构建后把 DLL 复制到 `WhitelistTests/bin/Release/` 再跑 exe。

| 步骤 | 改动 | 变异源码 sha256 | 产物 DLL sha256 | 结果 | 新增红名单 |
| --- | --- | --- | --- | --- | --- |
| 基线 | 无 | 见 §1 | `f28c211a9178818d81d2d58721a4e5b9db101371ce0a6be836ff37561301e2e8` | 727/729 | —（两条既有环境失败 GBA01/NIL01） |
| MUT-A | 删 `Bug1ProbeRuntime.cs:721-722` 两行 window 计数同步 | Runtime `cf4521a5a9146332c7b63287cc7a2c0b6d0f55b58657bb7d3eb59c8dfaf505dd` | `c8949aa8fbfd35de45e46adb9b06eebe790eebcde77a9d01e17a85a0aa97d9bc` | 726/729 | **Bug1-38**（唯一） |
| MUT-B | `Bug1ProbeTargetSelector.cs:65` `ReferenceEquals(id, null)` → `id == null` | Selector `aac777ddc674b34e35bc4391cf254d4be1c6e52f89420c59244c47d50a6e4d26` | `3578811acc4a303a3aa9a9296d33656cf775eeb370a90b53ee85a5ba38a556b2` | 726/729 | **Bug1-36**（唯一） |
| 还原 | `cp` 回送审文件 + `-t:Rebuild` | Runtime `17ca472b…`、Selector `b716369f…`（= §1） | `f28c211a9178818d81d2d58721a4e5b9db101371ce0a6be836ff37561301e2e8`（逐字节回送审值） | 727/729 | — |

- MUT-A 的失败正文（用例 Bug1-38 输出，摘自运行日志）：
  `RUN_END 入队计数必须是 1，实际={"label":"Unknown","rx_after_enqueue":0,"rx_after_dequeue":0,"missing_fields":["AFTER_ENQUEUE","AFTER_DEQUEUE"]}`
  —— 正是 t46-F1 的症状：相位记录已经产生，但 window 计数没有回填，`Judge` 判 `PHASE_COVERAGE_MISSING`。
- MUT-B 的失败正文：`System.NullReferenceException`（用例 Bug1-36 用未初始化 `SteamPlayer` 触发 `SteamPlayerID.op_Equality` 的 `.steamID` 解引用）。
- 两次变异各自**只**打红一条用例，说明断言是逐点隔离的，不是靠旁路连带变红。
- 主树在整个审查期间未被修改：§1 的 15 个文件哈希在审查开始与结束两次取样完全一致，`bin/Release/SteamP2PFriends.dll` 仍为 `f28c211a…`。

## 4. RUN_END 与 result.json 的同源论证

唯一计数来源是 `Bug1ProbeRuntime` 的两个静态字段，链路没有任何分支可以分叉：

1. 归零：`Enable` → `:108-115`（`_rxEnqueueRecords = 0` 等）；
2. 递增：`OnEnqueue` `:429`、`OnDequeue` `:447`（仅此处 `++`）；
3. 判读前同步：`EndStep` `:721-722` 把静态值写进 `window.RxEnqueueRecords / RxDequeueRecords`；
4. 判据：`Judge(window)` `:725` 只读 `:723-724` 取出的这对值（`Bug1ProbeJudge.cs:190-197`）；
5. 同值双写：`RUN_END` 行 `:732-733` 与 `result.json` 的 `verdict/reason`（`:738` `WriteArtifacts`）都是上面同一个 `verdict`。

因此 t46 观察到的「RUN_END 打 500、result.json 判 PHASE_COVERAGE_MISSING」在结构上不可能重现；且 MUT-A 证明这条同步是承重的（删掉即 0 vs UNKNOWN，并被 Bug1-38 抓住）。`Bug1-39` 是反向正控（零记录必须判 UNKNOWN），与 `Bug1-38` 一起把「计数正确」与「缺样硬判」两侧钉住。

## 5. cfg 语义与文档核对

| 项 | 文档/契约 | 实现 | 判定 |
| --- | --- | --- | --- |
| 配置文件 | `BepInEx/config/SteamP2PFriends.Bug1Probe.cfg` | `Bug1ProbeControl.cs:24` `ConfigFileName` 同值 | 一致 |
| 节 / 键 | `[Bug1Probe]`；`Enable`（默认 **false**）、`TargetPlayer`、`SpeedTier` | `:44-49` 三个 `_config.Bind("Bug1Probe", …)`：`Enable=false`、`TargetPlayer=""`、`SpeedTier="auto"` | 一致 |
| `TargetPlayer` 解析 | 名字 OrdinalIgnoreCase、SteamID 按 Ordinal、歧义报错 | `Bug1ProbeTargetSelector.cs:85` `Select`：`:95` 无 playerID 跳过，`:96` 自我排除，匹配不唯一 → `:119-120` `Status=Ambiguous` + `TARGET_PLAYER_AMBIGUOUS`；唯一匹配 → Selected | 一致 |
| 本体玩家排除 | 必须排除 Host 自己 | `Bug1ProbeTargetSelector.cs:96` `localSteamId != 0UL && client.SteamId == localSteamId`（`ulong` 比较，不经 `SteamPlayerID` 的 `==`），跳过原因 `self`（`:53`）；`localSteamId=0` 表示调用方已排除（`:132` 注释） | 一致 |
| `SpeedTier` 取值 | `auto` / `walk` / `sprint`，非法即拒 | `Bug1ProbeNativeSpeed.cs:131-138`：空或 `auto` → 按当前姿态（`sprinting ? sprint : walk`）、`walk`/`sprint` 强制、其它 → null → `Control.cs:116` `NATIVE_SPEED_CAP_UNRESOLVED` | 一致（crouch/prone 残留见 F2） |
| 拒绝码词表 | 9 个码，日志/result.json/交付说明共用 | `Bug1ProbeEntryLogic.cs:24-32`：`ALREADY_RUNNING`、`RUN_OR_STEP_MISSING`、`TARGET_STEAM_ID_MISSING`、`TARGET_PLAYER_NOT_FOUND`、`TARGET_PLAYER_AMBIGUOUS`、`NATIVE_SPEED_CAP_UNRESOLVED`、`OUTPUT_ROOT_UNRESOLVED`、`ROLE_NOT_HOST`、`ENTRY_INIT_FAILED`；`Control.cs:143` `Refuse` 统一打 `REFUSED <code>` | 一致 |

## 6. Findings

| id | 级别 | 问题 | 证据 | requiredFix |
| --- | --- | --- | --- | --- |
| T49-F1 | medium | 「拒绝路径不留空 run 目录」对 `INSTALL_FAILED` 不成立：`TryStart` 先 `PrepareRun`（含 `CreateDirectory`），再 `Enable`；`Enable` 失败后直接 `Refuse`，已建的 `<baseDir>/<runId>/HOST` 留在磁盘上。t48 报告 §7 把它写成已关闭，属结论过度。 | `Bug1ProbeControl.cs:123`（PrepareRun）→ `:193-221`（`:202` CreateDirectory）→ `:126-127`（Enable false → Refuse，无删除）；Bug1-43 只覆盖 `ALREADY_RUNNING` 与 `RUN_OR_STEP_MISSING` 两条在 CreateDirectory **之前**就拒绝的路径。 | 在 `Enable` 返回 false 的分支删掉本次创建的目录（仅限本次新建、且不得递归删除用户已有目录），或把 `CreateDirectory` 移到 `Enable` 成功之后；并补一条用例：`Enable` 返回 false 时 `<runId>/HOST` **不存在**。 |
| T49-F2 | low | `ResolveTier` 只分 SPRINT/WALK，auto 下若开启瞬间处于 CROUCH/PRONE，会套用 walk 上限（`SPEED_STAND`），而 SDK 原生上限更低（`PlayerMovement.cs:317-323`：`SPEED_CROUCH = 2.5f`、`SPEED_PRONE = 1.5f`），cap 偏松会让慢速 stall 漏判。 | `Bug1ProbeNativeSpeed.cs:131-138`；SDK `PlayerMovement.cs:94-96`（常量）、`:317-323`（分支）。 | 三选一：把 crouch/prone 也建模为独立档并取对应常量；或在 auto 解析到 crouch/prone 时以 `NATIVE_SPEED_CAP_UNRESOLVED` 拒绝；或在 RUN_BEGIN 的 evidence 里显式登记「蹲/趴按 walk 上限」的限制并由 spec 追认。 |
| T49-F3 | low | 探针之外仍有同类 `playerID == null` 写法（R8-01 的同型隐患），属既有代码、非本轮引入，也未在本票范围，但不应被「R8-01 已关闭」的表述掩盖。 | `Adapters/Zombie/ZombieSnapshotAdapter.cs:409`、`Adapters/Resource/Patches/ResourceManagerWorldSyncDiagnosticPatch.cs:391`、`Adapters/Item/ItemLocalPresentationDispatch.cs:316,327`、`Adapters/Item/Patches/ItemRecoveryCommandPatch.cs:234`、`Platform/UI/SteamPersonaDisplay.cs:271`、`Core/Patches/PlayerClothingLoadAppearanceFixPatch.cs:111`（注释）。 | 不阻塞本票；建议另开一条低优先级票统一改为 `ReferenceEquals`（或在共同工具方法里判空），并在其中用同一 NRE 用例做回归。 |
| T49-F4 | info | 本票未逐条覆盖「安装器内部 verify 失败」（`Bug1ProbePatch.cs:81-93`）的用例，只有读码证据；`Bug1-40` 经 `__TestInstallSeam` 验证的是 Runtime 的决策分支。离线环境装不上 5 条原生 patch，构造不出干净场景。 | `Bug1ProbeEntryRepairTests.cs`（Bug1-40）；`Bug1ProbePatch.cs:81-93`。 | 无需修复；在实机 1H 上跑一次 `Enable` 后核对 `spf_hook_errors`/`FailClosed` 即可，归入 §8 的实机项。 |

## 7. 诚实边界（这次判的是「入口与仪表」，不是「Bug1 已诊断」）

- t48 报告标题与开头即写明「只产出可采集的测量能力，不构成 Bug1 根因结论」，本审查认可该定性，且不因本轮 6 项关闭而升级为「Bug1 已诊断」。
- 三项 UNKNOWN 如实保留、未被静默降级：
  1. **Mono 下 BCL `Queue<PlayerInputPacket>` 的 `Enqueue/Dequeue` detour 是否真的触发**——离线只能证明 patch 装上并能被 `Verify` 看到，不能证明运行期被调用；失败形态应为「窗口缺样 → `PHASE_COVERAGE_MISSING`」，而不是伪造样本。
  2. **启动重试在真实关卡时序下是否命中**——`Level.onLevelLoaded` / `Player.onPlayerCreated` 与 `Provider.isServer`、首个 client 进入的真实先后，离线无法复现。
  3. **`isDismissed` 是否存在于发布版**——`ReceiveInputs` 的提前拒绝分支离线不可达，取不到时按 `unavailable` 记录而非猜测。
- 环境限制如实声明：离线进程里 5 条 Assembly-CSharp 原生 target 报 `HarmonyException: IL Compile Error`，只有 BCL 两条（Enqueue/Dequeue）能装；t48 报告 §12 明确写出这一点，没有拿 2 条冒充 7 条。
- 本轮 727/729 的两条失败是 GBA01/NIL01（`System.TypeInitializationException → SDG.Unturned.SteamPlayer → NetReflection → ReflectionTypeLoadException`，控制台缺 Unity 运行时），与本次改动无关，不应被当成回归，也不应被当成通过。

## 8. 未验证项与归属

| 项 | 归属 | 关闭条件 |
| --- | --- | --- |
| T49-F1（安装失败留空目录） | **本轮必须修**（下游 t51 修复轮） | 代码删/挪 CreateDirectory + 一条「Enable=false → 目录不存在」用例变绿 |
| T49-F2（crouch/prone 上限） | 后续轮或 spec 追认 | 建模三档 / 拒绝 / 书面登记限制，三者之一 |
| R8-03 真实启动时序 | **实机 1H+1G** | 实机 `RETRY_ARMED → RETRY_OK` 日志链路出现一次 |
| R8-04 5 条原生 detour 的真实卸载 | **实机 1H+1G** | 实机 `Disable` 后 `spf_hook_errors` 为空且原生行为恢复 |
| F1 计数的真实采集（BCL detour 是否触发） | **实机 1H+1G** | 实机一次完整 step 里 `rx_after_enqueue/dequeue ≥ 1` 且 `PHASE_COVERAGE` 不再是 `MISSING` |
| `isDismissed` 是否存在 | **实机** | 实机日志出现该字段的取值或 `unavailable` 记录 |
| T49-F3（探针外同类判空） | 另开低优先级票 | 统一改 `ReferenceEquals` 并有用例 |

## 9. 复审边界

- 本判定只对 §1 列出的字节有效；任何文件再次变化（含 `Program.cs`、`csproj`）都必须重判。
- 只判规格与需求：命名、注释密度、复杂度、日志措辞等工程风格问题由另一轴负责，本报告不涉及。
- 未修改主树任何生产/测试/构建产物；变异全部在 `../spf-t49-iso` 完成并已在副本内还原（副本保留，供 Captain 复核）。
- `needs_revision` 的范围仅限 T49-F1（medium）这一条；其余 6 项判据已闭合并可作为下一轮回归基线，但**不构成**「Bug1 仪表可发布」或「Bug1 已修复」的结论。

## 10. 自证

- 正文（§1–§9）在追加本节前的字节数与哈希：**24011 B / `a7492fa0ab2a915c322b49d8b7c1eef7c840e9e2b8c1bc4eabbac942acafdcad`**（可截断本节复核）。
- 主树复核：五份被改生产文件（`b716369f` / `aa6c8bea` / `17ca472b` / `22308d77` / `109e283e`）与 `bin/Release/SteamP2PFriends.dll`（`f28c211a`）在本轮结束再次取样，与 §1 完全一致；变异只存在于副本 `../spf-t49-iso`。
