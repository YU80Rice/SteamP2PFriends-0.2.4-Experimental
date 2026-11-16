# T6 工程轴审查：Bug3 组状态恢复实现（t3 / attempt 2）

- 审查者：reviewer-eng（工程轴独立审查，只判代码质量/边界/回归风险/工程规范，不判是否满足用户需求）
- 被审任务：t3（attempt 2），`Core/Patches/P2PGroupStateRecovery/` 两文件 + 三处接线 + 12 条 Bug3 用例
- 审查方式：只读静态审查（源码、反编译参考、测试、注册链、csproj、IL 契约基建），未修改任何源码，未重跑构建，未实机
- 基线事实引用：Captain 复跑——生产 build 0 错 0 警、测试 build 0 错 4 警（既有 CS0649）、645/647 PASS（Bug3-01..12 全 PASS），NIL01/GBA01 失败系 native runtime routes 未配置

## 0. 结论

**verdict = needs_revision（工程上不可直接进入生产）**。

实现主体（纯状态机 + 手动登记 + 一次延迟执行 + 原生回环发布）方向正确、与仓库既有补丁惯例基本一致，注册链与 csproj 接线可核验。但有 3 个 high 问题必须先修：**E1 fail-closed 无兜底（静默导致整局任务进度不落盘）**、**E3 异常路径残留 pending 导致"恰好一次"可被打破（可达双执行）**、**E2 被测行为在测试中零覆盖（把功能整体关掉仍全绿）**。E2 使 Bug3-01..12 的全 PASS 不能作为"该实现已被测试锁住"的证据。

## 1. 审查对象与字节锁定

| 文件 | 行 | 字节 | SHA-256 | git |
| --- | --- | --- | --- | --- |
| `Core/Patches/P2PGroupStateRecovery/GroupStateRecoveryCore.cs` | 246 | 9231 | `5a09a22070d12609abb8c652d6b824f6e84d2f4101e731e172c62888d5dbf202` | 未跟踪 `??` |
| `Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs` | 410 | 18301 | `b0a62e369a643645a983d380a83e2d9706159790c62bc820368eca8fbd463f09` | 未跟踪 `??` |
| `WhitelistTests/Evidence/Runtime/P2PGroupStateRecoveryRuntimeTests.cs` | 273 | 14662 | `97731bad76023f771a4a341c5a83230bd6efa741ca143ff00f29692a5d821400` | 未跟踪 `??` |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationAuditModules.cs` | — | — | 已修改 `M`，diff 仅新增块 L248-257 | `M` |
| `SteamP2PFriends.csproj` | — | — | 已修改 `M`，diff 仅 L279-280 两行 Compile | `M` |
| `WhitelistTests/Program.cs` | — | — | 已修改 `M`，L848 说明行 + L850-861 十二条 RunTest | `M` |

字节差异说明（可闭合，非内容差异）：任务书给 `P2PGroupStateRecoveryPatch.cs` = 17891B，磁盘 18301B，差值 **410 = 文件行数**，即 LF→CRLF 每行 +1 字节的纯 EOL 转换；`GroupStateRecoveryCore.cs` 9231B 与任务书一致（同为 CRLF，246 行）。两文件当前均全 CRLF，内容层面无增删证据。

产物新鲜度：`bin/Release/SteamP2PFriends.dll` 与 `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` 均为 12:39，晚于 patch 的 12:33 mtime → 现有 DLL 对应现盘源码（Captain 的 645/647 结果对得上本次审查字节）。

顺带记录（非本票改动）：`Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDomainModules.cs` 以 `git diff --ignore-all-space` 对比为空 → 仅行尾噪音，无功能改动，不应计入本票交付面。

## 2. 验收判据逐条

| # | 判据 | 结论 | 依据 |
| --- | --- | --- | --- |
| 1 | 生产注册链：经 `ApplyV2AuditFixPatches_Core` 登记，且 owner/original/PatchMethod 可由既有 StaticIL 用例验证，非自报 | **PASS** | `PatchRegistrationAuditModules.cs:251` 在 `_Core`（L202-260）的既有 try/catch 内调用 `RegisterManual(_harmony)`，失败置 `_registrationStageFailed=true`（L256）；`P2PGroupStateRecoveryPatch.cs:94-120` 用 `Harmony.GetPatchInfo(original)` 逐条比对 `owner`+`PatchMethod`（original 由查询键隐式保证）；Bug3-11 用 `IlContractProbe` 断言 `父入口→RegisterManual==1`、`RegisterManual→VerifyRegistered==1`、`HasPatch→GetPatchInfo==1`，Bug3-12 用**真实 Harmony 实例** patch 后再查 `GetPatchInfo` 并 `UnpatchSelf`。自校验式登记在本仓库是既有惯例（AuthHandshakeJournalPatch.cs:187、ClientMethodLoopbackPatch.cs:244/276、ObjectManagerRegionSyncPatch.cs:206 等 15+ 处），未纳入中央 `PatchRegistrationCriticalVerification.VerifyPatch` 不构成偏离。 |
| 2 | "恰好执行一次"：延迟 load 的触发点是否可能重复调用（多帧/重连/销毁重入），并指出可达双执行路径 | **FAIL** | 存在一条可达双执行：`Prefix_Load` 在 L222 已把 pending 登记进 Core，L226 之后再抛异常（例如 L227 的日志或 L226 赋值后的任何异常）→ catch L231-235 **返回 true 放行原生 load**，而 Core 的 pending 未回滚 → 同一关的 `Postfix_GroupTableLoaded` 仍会 `Authorized`（L256/L267）再执行一次 `quests.load()`（L296）。原生 `load()`（`PlayerQuests.cs:2647`）对 `flagsMap/flagsList/questsList` **只 Add 不清**，双执行会产生重复条目。详见 E3。 |
| 3 | pending 生命周期：退服 / 旧回调 / 实例销毁 / 同进程第二局 / 地图 mismatch 五类时序均有可失败测试，且去掉实现会红 | **FAIL** | 五类只在 **Core 纯状态机层**有用例；**胶水层零覆盖**（全测试文件对 `Prefix_Load`/`Postfix_GroupTableLoaded`/`BuildEligibility`/三段事件订阅/`ResetBoundary`/`OnLevelLoaded`/`OnServerShutdown` 的引用数为 0，唯一胶水引用是 `:221 RegisteredForTests`）。更关键：Bug3-05 断言的 `Core.AbandonPending`（L79）**生产无任何调用点**；Bug3-06 断言的 `Core.OnPlayerDestroyed` 因 `P2PGroupStateRecoveryPatch.cs:182` 的 `||` 短路在生产主路径上根本不被调用。详见 E2。 |
| 4 | 显式 Compile 列表与 csproj 接线：新文件真进编译图，未引入未登记第三方依赖 | **PASS** | `SteamP2PFriends.csproj:279-280` 两个 `<Compile Include>` 显式指向新文件；diff 无新增 `PackageReference`/`Reference`；Core 仅引用 `System`/`System.Runtime.CompilerServices`（CWT），Patch 仅引用 `HarmonyLib`/`SDG.Unturned`/`Steamworks`（既有引用）。测试侧由 `WhitelistTests.csproj:142 <Compile Include="Evidence\**\*.cs" />` 通配覆盖，未改测试 csproj。**但**三个新文件均未跟踪，见 E6。 |
| 5 | 测试隔离：Bug3-01..12 是否共享可变静态状态、是否与既有 633 项交叉污染 | **PASS（有脆弱性）** | 01-10 全部用方法内局部 `new GroupStateRecoveryCore()`，无共享实例；Core 无 static 可变字段。进程级副作用仅来自 Bug3-12（真实 patch + 三段原生事件订阅 + `_registered=true`），而 Bug3 段是 `WhitelistTests/Program.cs` 的**最后一个 section**（L845 之后到 Final Result），故 633 项基线不受污染（与 Captain 645/647 实测一致）；但顺序变更/追加用例即可能踩到，见 E8。 |
| 6 | Unity/Harmony 运行时约束：签名、目标解析、异常传播是否循既有惯例，有无 ECall/IL 编译风险 | **PASS（有 2 处缺口）** | `Postfix_GroupTableLoaded(int level)` 与 `AccessTools.Method(typeof(GroupManager),"onLevelLoaded", new[]{typeof(int)})` 签名一致；`Prefix_Load(PlayerQuests __instance)` 与 `PlayerQuests.load()` 无参签名兼容（Harmony 允许额外 `__instance`）；目标解析失败即 Error+return，不做半登记（无裸 `Patch` 无目标）。`Enabled` 常量未被消费，与 `GameplayReadyBitmaskPatch`/`PlayerMovementInitializePlayerPrefixPatch`/`PlayerUpdateGuardPatch`/`SteamPlayerIsLocalServerHostPatch` 完全同惯例。无 `[HarmonyPatch]` 属性 → 不会被 `PatchAll` 顺带注册，与既有手动登记一致。`PublishNativeGroupState` 依赖的 `changeRank` 是**原生 public 方法**（`PlayerQuests.cs:485`，无条件 `SendGroupState.InvokeAndLoopback`），非 ECall、非 no-op，故 Bug3-10 的 IL 契约成立。事件订阅用 `[MethodImpl(NoInlining)]` + 分段 try/catch 处理"cctor 在控制台/单机进程可能失败"，思路上正面回应了历史 run-14 的 `SecurityException` 类型风险（该异常本质是 JIT/类型初始化在方法边界外抛出，NoInlining 使其可被捕获）。缺口：E4（恢复后不补发 flags 通知）、E5（注册失败静默降级）。 |

## 3. Findings

### E1 [high] fail-closed 无计时兜底：pending 永不兑现时，该局任务进度静默不落盘
- 位置：`Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs:208-236`（defer 出口）、`:242-273`（唯一兑现点）、`GroupStateRecoveryCore.cs:143-176`（sameLease 判定）
- 机制：pending 只会被 `Postfix_GroupTableLoaded`（L256/L267）或 `ResetBoundary`（L195-202）消耗。若该关的 `GroupManager.onLevelLoaded` 后缀**不会再来**——插件热重载/中途注入、`Postfix` 在 L251-253 因 `CurrentSurvivalMapName()` 为空提前返回、或 L269-272 吞掉异常——Core 的 pending 永久存在。此后同实例每次 `PlayerQuests.load`：ready 未登记 → 同 lease → `Deferred` → prefix `return false`（L229）→ 原生 load 被永久跳过；而 `wasLoadCalled` 恒 false，原生 `save()` 首行 `if (!wasLoadCalled) return;`（`PlayerQuests.cs:2870-2873`）→ 整局 flags/quests 不写盘。
- 后果：无崩溃、无异常、无日志（除初次 Info），玩家任务进度静默丢失，只能重启进程恢复。仓库自身工作流包含热重载（`RegisterManual` L61-62 的注释即为热重载幂等而写），该路径可达。
- requiredFix：给 pending 加时间/帧数上限（如 `Core.Tick()` 由 `Player.update` 或 `Level` 侧驱动，或记录 `Time.realtimeSinceStartup` 在同一个 prefix 里比对），超时即 `AbandonPending("watchdog")` + 一次 `Warn` + 计数；或在 `Prefix_Load` 增加"同实例第二次 Deferred 请求即放弃延迟并放行（`_forcedPassThrough++` + Warn）"的安全阀。二者取其一即可消除静默 fail-closed，且必须可被新用例观测。

### E2 [high] 被测行为与真实实现脱耦：把延迟功能整体关掉，12 条用例仍全绿
- 位置：`WhitelistTests/Evidence/Runtime/P2PGroupStateRecoveryRuntimeTests.cs`（全文）；`P2PGroupStateRecoveryPatch.cs:208-236`、`:242-273`
- 证据（静态可判，无需构建）：
  1. 测试对胶水符号引用数 = 0（唯一 `:221 RegisteredForTests`）。因此把 `Prefix_Load` 改成恒 `return true`（功能整体失效）、删掉 `Postfix_GroupTableLoaded` 主体、删掉 `SubscribeLevelEvents/SubscribePlayerEvents/SubscribeProviderEvents` 任一订阅、把 `BuildEligibility` 改成恒 `IsEligible=true`，**Bug3-01..12 全部保持 PASS**（Bug3-10 只检查 `ExecuteDeferredLoad` 内部对 `quests.load()` 的调用次数，不检查谁调用了 `ExecuteDeferredLoad`）。
  2. Bug3-05（L75-86）断言 `core.AbandonPending(...)`（L79）后 `AbandonedPending==1`，但 `AbandonPending` 全仓库**无生产调用点**（仅 Core 定义 L214 + 该测试）→ 用"退服"之名测了生产永不执行的分支；生产退服实际走 `Provider.onServerShutdown → ResetBoundary → ResetSession`，只增 `_boundaryResets`（`GroupStateRecoveryCore.cs:116-124`），无任何用例。
  3. Bug3-06（L88-99）断言 `core.OnPlayerDestroyed(51L)==true`，但生产 `OnPlayerDestroyed`（Patch L174-193）因 `L182` 的 `||` 短路（`ReferenceEquals(quests,_pendingQuests)` 为真即不调 Core）在主路径上不触发 Core 通知 → `_destroyedPending` 实际永不增长，"销毁即作废"不变量未实现。
  4. Bug3-08/09 只翻转 `GroupRecoveryEligibility` 的 getter，未覆盖 `BuildEligibility` 的 Unity 对象→字段映射（`Provider.user`、`channel.owner.playerID`、`transportConnection is TransportConnection_Loopback`、`CountClientsWithSteamId`）。
- requiredFix：把胶水决策抽成可注入的纯函数（例如 `internal static GroupLoadDecision Decide(GroupRecoveryEligibility, string mapName, long token, GroupStateRecoveryCore core)`，Prefix 只负责取字段+调用），用假 eligibility 覆盖"defer / pass-through / 放行"三分支；`L182` 改为 `bool wasPending = Core.OnPlayerDestroyed(token) | ReferenceEquals(quests, _pendingQuests)`（非短路，两处都执行）或先判后调；`AbandonPending` 若无生产调用点就删掉它和 Bug3-05，或把它接进 `ResetBoundary` 的退服分支；补一条"删掉订阅/把 Prefix 改恒 true 会红"的用例（seam 化后可写）。

### E3 [high] 异常路径残留 pending → 破坏"恰好一次"，可双执行 `quests.load()`
- 位置：`P2PGroupStateRecoveryPatch.cs:222-235`
- 机制：`Core.OnLoadRequested`（L222）先提交 pending；L226-229 才是"确认延迟"的落地；catch（L231-235）在任何异常下 `return true`（放行原生 load）。若异常发生在 L222 之后、`return false` 之前（L227 的 `RoleLogger.Info` 抛错即可，Log 落盘/控制台 IO 失败并非理论情况），则：原生 load 执行一次（flags 已填）→ pending 仍在 Core → 该关 postfix 到来时 `Authorized` → `ExecuteDeferredLoad` 再执行一次 `load()`（L296）→ 重复 Add。
- 为什么不是低危：这正是本实现对外承诺的核心约束（"deferred native Quests.load executed **once**"，L297），且失败方向是**多执行**而非少执行，`load()` 无幂等保护。
- requiredFix：catch 内先 `Core.AbandonPending("prefix-failed")`（或 `ClearPending`）再 `return true`；更稳的写法是把可抛工作（日志、`CurrentSurvivalMapName`）全部移到 `OnLoadRequested` 之前，登记为最后一步。同时补一条用例：登记后抛异常 → `HasPending==false` 且 `TryTakeDeferredLoad` 返回 `None`。

### E4 [medium] 延迟恢复后不再通知本机 quest flags（本补丁引入的新可观测性缺口）
- 位置：`P2PGroupStateRecoveryPatch.cs:314-330`（只发组事件）；原生 `PlayerQuests.cs:2592`（load 被跳过）、`:2598`（`onFlagsUpdated` 在 flags 仍为空时触发）
- 机制：`InitializePlayer` 里 `load()` 被 prefix 拦下后，其余语句照常执行，`if (channel.IsLocalPlayer) onFlagsUpdated?.Invoke();` 在 flags 为空时就已发出；而 `load()` 全文**不触发** `onFlagsUpdated`（仅 L2255、L2598 两处），`PublishNativeGroupState` 只 `changeRank` 发组事件。结果：组 ID/rank 被回环修正，但本机任务/成就列表与任何依赖 `onFlagsUpdated` 的 UI 收不到"数据已就绪"。
- 影响面：不改服务端事实状态（save 落盘正确），属于表现层/下游监听者的一致性问题；若不修，需在交付说明里明确"恢复后本机列表需一次重开界面/重进才刷新"。
- requiredFix：二选一——(a) 恢复后补发一次原生通知（若存在 public 的重建/刷新入口，如 `onFlagsUpdated` 的显式触发路径，需确认可调用性）；(b) 只延迟"依赖组表"的那部分语义（例如延后到组表就绪再 `load`，但保证 flags 在同一帧内可见），或在文档中登记为已知限制并交由需求轴裁决。

### E5 [medium] 注册失败与"热重载换实例"均静默降级为无补丁
- 位置：`P2PGroupStateRecoveryPatch.cs:57-102`
- 机制：`RegisterManual` 返回 void，`AccessTools` 目标缺失（L68-74）与 `VerifyRegistered` 失败（L79-83）都只是 `Error` 后 return，**不置 `_registrationStageFailed`**（该标志只在 `AuditModules` 的 catch 里置位，而这里没抛异常）→ 上层认为"Core 段全部成功"，启动日志里只有一行 Error。幂等键用 `_registeredHarmonyId` 字符串比对（L61-62）：热重载若复用同一 `Harmony.Id` 而实例已变，会**静默跳过**重新 Patch，整局无恢复能力。
- requiredFix：返回值改为 `bool`/`void throws` 并在 `AuditModules` 里据此置 `_registrationStageFailed`；幂等改为同时校验 `_registeredHarmonyId` 与 `GetPatchInfo` 里仍存在本实例的补丁（即"已登记且仍生效"才跳过），否则重登。

### E6 [medium] 交付完整性：新文件未跟踪，csproj 已引用 → 干净检出/打包会编译失败或漏交付
- 位置：`Core/Patches/P2PGroupStateRecovery/`（`??`）、`WhitelistTests/Evidence/Runtime/P2PGroupStateRecoveryRuntimeTests.cs`（`??`）、`SteamP2PFriends.csproj:279-280`
- 机制：csproj 显式 Compile 两个仅存在于工作区的文件；任何 `git archive`/干净 clone/CI 打包都拿不到它们 → 编译失败；若打包脚本忽略 untracked，则 0.2.4.11 会带着"没有该补丁的 csproj"或干脆构建不出来。
- requiredFix：交付前 `git add` 三个新文件并 pin 本报告第 1 节的 SHA-256；版本归档时以哈希核对而不是以"文件存在"核对。

### E7 [low] 语义/可维护性瑕疵（不阻塞，但会误导后续断言与排障）
- `GroupStateRecoveryCore.cs:143-176`：`_nativeLoadExecutions` 只在 **pass-through** 分支自增，deferred 实际执行的 load 计在 `_flushAuthorizations` → 命名与"原生 load 执行次数"直觉不符（Bug3-01 断言 `NativeLoadExecutions==0` 正是靠这个语义）。建议改名 `PassThroughLoads` 或补注释。
- `GroupStateRecoveryCore.cs:160`：`_pending.Epoch == _epoch` 恒真（pending 只在当前 epoch 创建，`ResetSession` 必清）→ 死条件，读代码的人会以为存在跨 epoch 复用可能。删或注释"防御性"。
- `P2PGroupStateRecoveryPatch.cs:94-102` 注释称校验 owner/original/PatchMethod **三项**，实际显式只比两项（original 由 `GetPatchInfo` 的键隐含）→ 注释与实现对齐一下。
- `P2PGroupStateRecoveryPatch.cs:305`：`PublishNativeGroupState` 在 `finally`（守卫复位）之后调用，`ExecutingDeferredLoad` 不覆盖发布阶段。当前 `changeRank` 不会重入 `load`，无实际风险；但若日后在发布路径里加入会触发 `PlayerQuests.load` 的调用，守卫就失效了。建议把发布纳入 `try/finally` 内。
- `OnLevelLoaded`（L165）与 `Postfix`（L246）硬依赖 `Level.BUILD_INDEX_MENU`，尚无用例钉住该常量与 GAME/SURVIVAL 的相对次序；SDK 升级若调整枚举语义，重置边界会静默改变。建议加一条对 `Level.BUILD_INDEX_MENU` 关系的断言或注释版本前提。

### E8 [low] 测试工程细节
- `WhitelistTests.csproj:43` 以 `HintPath` 引用 `bin/Release/SteamP2PFriends.dll`，无哈希/时间戳绑定 → 只要 DLL 是旧的（未重编），IL 契约用例（Bug3-10/11）会对着旧字节假绿。建议在 Runtime 段打印并断言该 DLL 的 SHA-256（与第 1 节锁定值一致）。
- Bug3-12（L215-235）在 `finally` 里 `UnpatchSelf`，但留下 `_registered=true`、`_registeredHarmonyId` 指向已卸载实例、三段原生事件订阅常驻、`LoadTargetForTests/GroupTableTargetForTests` 被赋值 → 目前靠"Bug3 是最后一个 section"侥幸安全；一旦有人在其后追加 Runtime 用例或调整段序，会出现"补丁已卸但自报已登记"的假象。建议加 `ResetForTests()`（清 `_registered/_registeredHarmonyId/_pendingQuests` 并退订）并在 finally 调用。
- 12 条用例无一条验证"多次 `Postfix_GroupTableLoaded` 到达"（幂等）与"`ExecutingDeferredLoad` 重入守卫"——这两处恰是最容易在后续维护中被改坏的。
- 无任何时序/帧级测试（延迟发生在 spawn 阶段、postfix 在稍后），目前只能靠真机；这属于架构层选择，若要保留，应在报告里明确"胶水层行为仅由真机与代码审查保障"。

## 4. 明确认可的做法（避免只列问题）

- 把可判定逻辑全部下沉到 `GroupStateRecoveryCore`（无 Unity 依赖、无 static 状态、计数完备），是本次实现最正确的工程决策——它让 90% 的语义可测，也把 Harmony 胶水压到"取字段 + 转发"。
- 延迟语义只拦 `load()` 本身、不拦 `InitializePlayer` 其余语句；`ResetSession` 用 epoch 而非布尔，天然免疫"同进程第二局复用旧值"。
- 手动 `RegisterManual`（无 `[HarmonyPatch]`）+ 用真 Harmony 自校验（owner/PatchMethod）而不是信任自己的注册代码，符合仓库 15+ 处既有惯例。
- Postfix 的四道早退（MENU / 非 P2P host / 非 server / dedicated / 非 SURVIVAL 地图）与 Prefix 的门控对称，Guest 与单机路径零行为变化。
- `PublishNativeGroupState` 用 `changeRank` 的原生 `InvokeAndLoopback` 纠正本机 `none` 投影，而不是自己拼网络包——复用原生 `ReceiveGroupState` 语义，且 `changeRank` 同 rank 不短路（已核 `PlayerQuests.cs:485`），设计成立。
- `save()` 的 `!wasLoadCalled` 早退恰好使 pending 窗口内不会被默认值覆写存档（数据安全方向正确）。
- 测试文件无共享实例、断言粒度贴近不变量（token/epoch/map 三元组），12 条用例可读性好。

## 5. 建议的最小修复集（按顺序，不代改代码）

1. E3（小改动、高风险）：`Prefix_Load` catch 内回滚 pending；把可抛工作前移。
2. E1（小改动、消灭静默数据丢失）：pending 上限/安全阀 + 计数 + Warn。
3. E2（中等改动）：抽 `Decide(...)` seam，补 glue 层可红用例；修 `L182` 短路；处理 `AbandonPending` 死代码。
4. E6（流程）：`git add` 三文件 + 哈希入档。
5. E4/E5（设计确认）：E4 交架构轴裁决"是否接受该可观测性缺口"；E5 改返回值并让注册失败可见。
6. E7/E8（清理）：命名/死条件/注释、`ResetForTests`、DLL 哈希断言。

## 6. 方法与限制

- 仅只读：`git status` / `git diff --ignore-all-space` / 逐文件 sha256 / 逐行引用源码与反编译参考；未执行 build、未执行测试、未运行游戏（避免与 Captain 的复跑结论冲突，也避免改写任何工作区状态）。
- 反编译参考来自 `.scratch/bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerQuests.cs`（`load` L2647、`save` L2870、`InitializePlayer` L2578-2607、`changeRank` L485），属 SDK 快照，非运行时真值；行号以该快照为准。
- "把功能关掉仍全绿"是**静态可判**结论（测试文件对胶水符号零引用），未做实验性删除，故未违反"不改源码"约束；若审查方要求实验证据，可由实现方在分支上做一次删除验证（预期 E2 的四条改法均不红）。
- 本报告不评价是否满足用户需求（由另一名审查官负责），也不评价真机效果；Bug3 的 SDK/GAME 端口径一致性不在工程轴职责内。

---

## 7. T12 Architect 追加裁决：E4 / E5（2026-11-06，gpt-architect）

> 本节为 Captain 派发 t12 的产品/架构裁决，不是 reviewer-eng 的补充自审，不改写上文原审查结论。上文原始118行/22394字节 SHA-256=`0f4338740c242018f55cf5fc8cc7bc6cf4e141815bfd2f934a39fbbcc5337df4`；裁决只追加文档，不修改生产代码、不授予修复Green/CLEAN。本节绑定上文所审 patch `b0a62e369a643645a983d380a83e2d9706159790c62bc820368eca8fbd463f09`，不冒认进行中修复轮的新源码。

### 7.1 明确处置

| finding | 裁决 | 产品边界 |
|---|---|---|
| E4 | **修，纳入当前修复轮；不批准默认为已知限制** | 在原生load已经成功的前提下，服务端flags/quests/group事实与save正确性不因缺通知而损坏；缺陷是本地主机下游未感知真实数据已就绪。不能让用户以重开界面/重进作为新增回归的默认补救 |
| E5 | **修，注册失败必须进入可观测终止失败态** | Error/Warn单行或计数均不够；上层不得继续宣告恢复模块/注册阶段健康。热重载无法安全核验或退订时明确拒绝、要求重启进程，而非静默降级 |

本决定不改变 T1a 原生同fixture、T1b真机以及T2/T3/T4独立门禁；修E4不是宣布整个队伍/UI已验收。特别说明：本E4讨论的“save正确”以前提load成功为界，不能拿它掩盖E1尚未load导致不落盘或E3重复load的数据问题。

### 7.2 E4：最小可行入口与语义，为什么不是伪造任务完成

新增核对的SDK事实（运行DLL仍须按实际MVID核验）：[PlayerQuests:66–81](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerQuests.cs#L66-L81) 声明 `public FlagsUpdated onFlagsUpdated;`。它是**公开委托字段，不是C# event**，技术上可由插件正常调用该字段的 `Invoke`；不需要反射event backing field，也不需要修改游戏程序集。

没有找到覆盖所有本地订阅者、无额外状态副作用的public统一Refresh/Rebuild方法。public [VendorUI.MaybeRefresh](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/UI/Player/PlayerNPCVendorUI.cs#L74-L100) 仅在active且needsRefresh时更新单一界面，不会替整个flags订阅域发布就绪；[askQuests:2261–2263](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerQuests.cs#L2261-L2263) 是空方法。不能为刷UI重跑Initialize/load、构造初始状态收包（会再次Add flags/quests）、调用setFlag/addQuest/奖励API，或把`onAnyFlagChanged`、单flag变化、questCompleted当bulk刷新入口。

**批准的最小修法**：仅在本恢复模块实际延期的唯一local host上，原生 `quests.load()` **正常返回且仍同epoch/地图/实例**后，对 `quests.onFlagsUpdated` 的当时委托快照补做**至多一次、非空已恢复payload的授权成功路径一次**bulk-state就绪通知。按Captain追加约束，空存档/无可恢复flags与quests的空payload补发0次：已有空态与初始化通知一致，不制造多余就绪信号；但无队伍而有真实flags/quests仍须通知，不能放在 `isMemberOfAGroup` 条件里面。正常未延后/Guest/dedicated/其他实例及watchdog降级放行不额外发。watchdog若不能保全表现就绪契约，必须显式记Degraded，而非宣告完整恢复。

这确实是插件代原生调用点补发一次通知，但**不宣称这是原生自己再次执行，也不伪造“新增flag/任务完成/解锁成就”**。无参 `onFlagsUpdated` 的已有原生语义是整批状态可重读：原生[初始化尾部:2592–2598](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerQuests.cs#L2590-L2601) 与[整批状态接收后:2255](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerQuests.cs#L2218-L2258) 都在数据装好后调用；本补发的前提与这一语义一致，且确有从“空/未加载”到“已加载”的真实状态变化。

**副作用不是零，也不只是UI**：[LevelObject:468–470](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Level/LevelObject.cs#L468-L470) 订阅后重新 `updateConditions`，可能重判世界对象显示/启用条件；[VendorUI:302–305](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/UI/Player/PlayerNPCVendorUI.cs#L302-L305) 设置needsRefresh。第三方监听者可能有副作用、抛异常、重入load或自行错误假定本事件只来一次。故不能说“补事件完全无风险”；仅允许真实bulk就绪后的一次补偿，不按每个flag逐条发，不伪造旧/新flag值、不额外触发奖励。没有证据证明所有任务/成就UI只依赖此事件，不能以补发次数代替实际显示回归。

若实际运行ABI把该字段改为不可外部调用event/不存在，并且无受支持的无副作用统一入口，**停止这条最小修法，作为ABI硬阻断返回Architect**；不得擅自反射私有事件或以副作用API冒充刷新。不因此自动降格为用户已接受限制。

### 7.3 E4 与 E1 watchdog / E3 exactly-once 的顺序契约

- 一个恢复租约只有一个执行者：`Pending → Claimed/Loading → LoadSucceeded → NotificationsAttempted → Completed/NotificationFailed`，或进入Abandoned/LoadFailed等终态。先取走/撤销pending，再进入原生load；**wasLoadCalled=true不是成功证明**，因为原生在load首行就置位，只有正常返回才有LoadSucceeded。部分读取抛错时不发“就绪”、不自动重跑load。
- 补发通知作为同一恢复事务的独立阶段，先在内部标记 `flagsNotifyAttempted` 再调用用户委托；回调期间再次到来的group postfix/watchdog/重复注册不得执行load或再补发。正常native初始化/收包自己的通知不属于本模块“补发一次”的计数，不试图拦截所有原生通知。若恢复在Initialize尾部之前完成，且能证明其原生尾部会对已就绪数据发通知，则无需再补发；无法证实时只能选择明确时序并测试，不能把“空态先发过”算已就绪通知。
- **不能简单把现有ExecutingDeferredLoad=true延长到通知末尾**：[现有Prefix:212–213](../../Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs#L208-L224) 见该标志就放行，若监听者重入load会反而允许第二次原生load。需要一次性内部入场授权仅供首次受控调用消费；Loading/Publishing期间同租约后续load请求禁止递归执行/重排pending，并记可观测reentry。所有guard在finally按租约清理，但不能清掉永久已执行/已尝试标记使其复活。
- load成功后先保证同实例所有事实已就绪，再按固定顺序补bulk flags通知及原生组发布；**建议flags通知在组发布之前**，使组监听者读到已就绪的flags。通知与组发布各自捕获失败、各有attempt/result，不让其中一项异常触发原生load重试或使另一项完全失去执行机会；主线程执行，不能扔异步timer到下一局。
- 委托采用正常一次multicast调用，外层捕获并登记 `NotificationFailed`，不私拆invocation list改变下游调用顺序/异常语义。某监听者抛错后后续监听者可能未执行，这是部分投递风险；**禁止自动整批重发**（已执行者可能二次副作用）。记录错误、可见状态与未验收项；人工重新初始化/重启按明确流程进行，不把异常吞成恢复全部成功。
- E1超时只可在尚未Claimed的pending上竞争一次终止/安全回退；不能因通知慢/失败重新排队、重读或补发。watchdog若另行执行一次原生安全回退load，也必须共享同一原生load入场/成功/通知令牌；回退不满足组恢复条件就记录降级事实，不假报alpha恢复。注册失败/取消/换图/销毁的租约不投递旧通知。

### 7.4 E4 修复轮的可自动判定验收（Captain追加口径）

必须复用**同一本租约账**，键为 `epoch + map + deferred token`；不是按进程一次，也不是仅按Player实例一次。账里区分真实原生load入场数、**正常返回后**的 `RealLoadSuccess`、`NotifyAttempts`（在委托调用前置位）、实际callback调用/成功/失败及SkippedEmpty/SkippedWatchdog等原因。现有 `NativeLoadExecutions` 实际只统计PassThrough，不能直接冒名当RealLoadSuccess；实现者须校正命名或增加真实正常返回的计数。

不变式：对每个租约 `0≤NotifyAttempts≤RealLoadSuccess≤1`；补发次数不得大于真实成功load次数。只有同token的真实受控deferred load成功、实例/epoch仍匹配、非watchdog降级且bulk payload非空才有通知资格。payload从原生已加载的flagsList/questsList/GetTrackedQuest读取，不另写存档解析；空存档或全部为空记SkippedEmpty。令牌必须在真实成功返回之后授予通知，不是“ExecuteDeferredLoad顺手加一行Invoke”。

下表“补发次数”专指**本恢复模块新增的一次通知**，不包含原生初始化/收包自己的onFlagsUpdated。正控测试在真实PlayerQuests.onFlagsUpdated上挂只读哨兵，校验callback看到的是原生已恢复数据，而不只断言自己的计数。无订阅者可记录NoSubscribers/实际callback=0，但不能把它冒称UI已刷新；用例须明确挂上哨兵才断言实际投递1。

| 按租约测试路径 | RealLoadSuccess | 补发尝试/哨兵调用 | 必须同时核验 |
|---|---|---|---|
| 本局真实授权deferred load成功、flags非空 | 1 | 1 / 1 | 回调读到存档flags/quests与正确组事实；pending已消费；再来组表回调均不增长 |
| 无队伍但真实恢复非空flags/quests | 1 | 1 / 1 | 不被isMemberOfAGroup门误挡；不造组、不members++ |
| Guest / 普通单机 / dedicated / 非目标实例 | 模块账0 | 0 / 0 | 原生各自正常load/原生通知允许存在；本模块不介入 |
| 空存档或无可恢复flags/quests/tracked quest的空payload | 1（若已执行原生load） | 0 / 0 | SkippedEmpty；原生合法默认，不能伪造flags已新增 |
| **旧epoch/token跨入二局** | 旧账保留历史值，不新增 | **本次增量0 / 0** | 旧租约不得复活，不得为二局新玩家补发 |
| **二局新epoch/token、真实授权成功且flags非空** | **新账1** | **1 / 1** | 正控；不得做成进程级一次；即使SteamID或对象地址复用也按新租约判定 |
| **二局新epoch/token、但load未成功 / watchdog降级 / 异常回滚** | 未成功0；若watchdog另有真实成功则单独记降级成功 | **0 / 0** | 显式Degraded及reason，日志不得冒充完整恢复 |
| 本局watchdog安全放行、非正常恢复 | 仅真实正常返回才计；不以授权或wasLoadCalled计 | 0 / 0 | SkippedWatchdog + Degraded；后到组表callback不得二次执行load/补发 |
| 登记pending后异常回滚、原调用随后原生放行 | 不记deferred成功；普通原生成功单列 | 0 / 0 | pending撤销；没有load却发ready必须使测试变红 |
| 原生load部分Add后抛异常 | 0 | 0 / 0 | LoadFailed/Degraded；不得自动再load导致重复Add |
| 哨兵回调抛错或重入load | 1 | 1次尝试；成功计数不得虚报 | NotificationFailed；不重试投递/原生load；随后watchdog/postfix均不复活 |

**必须做反例验证**：把通知移动到load成功之前、让原生load抛错后仍通知，或人为令同token第二次NotifyAttempts增长任一变异，测试必须失败于 `NotifyAttempts > RealLoadSuccess` / `NotifyAttempts>1`；若移除生产补发调用、漏掉二局新token投递，授权正控也必须红。不能像E2那样只测独立状态机getter：应经生产registrar挂到真实Quests.load/GroupManager入口，同原生存档fixture观察正常返回和委托哨兵；若真实宿主边界不可执行，写具体UNKNOWN并保留可测状态机证据，不能用纯计数假绿代替。

计数不替代SDK/实机表现：补充有恢复flag的VendorUI脏标记、LevelObject条件刷新负控，确认回调未制造额外奖励/逐flag变化，监听者异常不导致重复原生读取。其余任务/成就UI的真实刷新未观测即UNKNOWN，不由本追加裁决授PASS。

### 7.5 E5：注册失败的终止性要求与热重载边界

**最小可行接线**：保留void接口时，目标解析失败、patch核验失败或任何**必要**原生生命周期订阅失败必须抛明确注册异常；既有[父注册catch:248–259](../../Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationAuditModules.cs#L248-L259) 随即置 `_registrationStageFailed=true` 并返回失败。也可改bool并由父入口聚合false，但二者只选一条接清楚。不能内部Error后return当成功；native cctor/离线宿主失败可以报告不可用，不可以“patch已装所以订阅失败无所谓”。

注册状态至少有Unregistered/Registering/Ready/Failed（名称可变，语义不能少）。Ready仅在两个精确patch存在且必要的Level/PlayerDestroyed/ServerShutdown订阅、生命周期清理与E1安全阀均就位后提交；失败时registered=false、失败原因/attempt身份稳定保留，父stage失败。不需要杀掉游戏进程，但**不得通过SPF候选注册/自检门继续宣称恢复可用或提供健康P2P开局**；已运行局不强踢/不自动改存档，退到可见降级/拒绝本次恢复并要求正常退出重启的明确路径。

每次失败必须留下单条可定位Error和结构化失败态，至少含 `feature=P2PGroupRecovery,registrationAttempt,ownerId,moduleAssembly/MVID,original,patchMethod,stage,reason,rollbackStatus,restartRequired`；存在pending再附epoch/token和作废原因。reason最少可分TargetResolveFailed/PatchVerifyFailed/LifecycleSubscribeFailed/StaleOwnerOrHotReload/RollbackFailed。状态/错误不能只依靠日志：即使日志I/O再抛错，也不能把失败态抹成Ready。计数仅辅助，不是终态；Warn不是这个注册缺失的充分处置。

部分安装失败须做有界清理：只撤销本模块本次安装的精确patch/委托，不用共享Harmony.Id的UnpatchSelf把全插件其他域一起拆掉；清pending/停watchdog，残留callback检查退休generation不得兑现旧租约。清理失败记RollbackFailed/restartRequired=true，本次尝试终止，不while重试、不每帧偷偷重登、不悄悄放宽ABI。下次显式干净注册/完整重启才可重新尝试，不在原失败账上刷成成功。

**同Harmony.Id不等于同登记生命周期；但new Harmony(id)本身也不必然是错误**。同一Assembly静态patch和订阅仍确实有效时，新wrapper可复用：跳过前仍须核验实际original+owner+**当前PatchMethod身份**与必要订阅状态；不是要求仅以C# Harmony对象ReferenceEquals判健康。若旧wrapper已Unpatch/Dispose、patch缺失，则不能凭 `_registeredHarmonyId` 字符串直接return；须完成有界清理后显式重登并重验，且不得叠加原生事件订阅。

真正换Assembly热重载时，旧delegate.Method/Target与新Assembly不同，`-= 新方法`不能证明移除了旧订阅。**最小安全支持边界：不支持在活动局无所有权交接的Assembly热替换**。必须由旧generation显式Dispose/退订并核实干净边界后才装新generation；不能可靠退订/核验时拒绝新注册，置Failed(StaleOwnerOrHotReload)、restartRequired=true，清楚记录“旧代残留未验证，本次恢复未启用，请正常退出并完整重启游戏加载候选”。不许伪报“registered successfully”。这是一条明确受限部署能力，不是允许产品恢复功能静默缺失的已知限制。

E5自动化门：缺目标/核验false/任一必要订阅抛错→父stage失败且未Ready；同id完整有效重复注册→不增patch或订阅；同id卸载后再注册→重新核验有效；旧Assembly无法安全退订→明确Failed/restartRequired；中途失败→清理只影响本模块、不剩可兑现pending；失败日志抛错→失败态仍成立。重复调用与watchdog竞争不能造成第二次load或flags补发，按§7.4同token账断言。

### 7.6 裁决边界与记录

本T12只追加以上裁决，实际修复由Builder后续实现票承接，必须红绿+独立双轴再验。未运行游戏/构建/生产测试，也未把源码审阅当修复验证；E4的ABI与真实监听者行为仍须修复轮给证。两项均不进入默认“用户已接受限制”；若后续无法安全完成，提交阻断与具体用户影响重新请示，不用交付说明单方面豁免。

本节明确区分“成功load后的事实/落盘正确”与“本机刷新缺失”；watchdog降级、partial load及未验收的UI不混称成功。原工程轴needs_revision保持原样，本裁决本身不是该轮CLEAN。

校验记录：已读回本追加节；新增9个本地文件链接均存在。按原始22394字节切片复算SHA-256，仍为 `0f4338740c242018f55cf5fc8cc7bc6cf4e141815bfd2f934a39fbbcc5337df4`，证明未改工程审查原文；检查命令退出0，标记 `T12_PREFIX_AND_LINKS_PASS`。本轮仅此文档追加；不新建源码/脚本，不做生产构建/游戏测试，原因是本票仅出裁决、实际行为验收留修复轮。
