# T16 工程轴复审 r2：Bug3 修复轮（t14）的边界、假绿与回归风险

审查人：reviewer-eng（工程轴，独立于 t14 实现者）
复审对象：t14 交付（Bug3 恢复修复轮 r2）
结论：**verdict = needs_revision（fail）**
范围声明：只做工程轴判断（正确性/边界/回归风险/可维护性/与既有补丁惯例一致性）；不评是否满足用户需求；除"变异实验"所需的三次临时改码外未修改源码，且每次都还原并校验哈希（见 §4）。

---

## 1 被审字节（结论只对该批字节有效）

| 文件 | 字节数 | sha256 |
| --- | --- | --- |
| `Core/Patches/P2PGroupStateRecovery/GroupStateRecoveryCore.cs` | 14840 | `d7fad63c2a8960488bc4d11b7d8cf14ab5379607b87e35e72186b74441d30961` |
| `Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs` | 37826 | `956f6a12c4fa8c9e82159ba09f7e6345e9d50dab673218c6c980655a3b1edaa8` |
| `Core/Patches/P2PGroupStateRecovery/GroupLeaseLedger.cs` | 4542 | `38f2cc84de6590dacb5310cd6487f8e88117898643cdb0ce6b9784337a805456` |
| `Core/Patches/P2PGroupStateRecovery/GroupLoadPlanner.cs` | 1344 | `a45533850f31bd96f3d63e39c56b2e5a084fc3f782b1ef984521d39fe64afb0f` |
| `Core/Patches/P2PGroupStateRecovery/P2PGroupRegistration.cs` | 1790 | `5b0c4e9ba4c87cbeb7363b3846b27f26f464e8acad51c25bc07831d5fbe04ea4` |
| `WhitelistTests/Evidence/Runtime/P2PGroupStateRecoveryRuntimeTests.cs` | 39047 | `2740f66f8a66cbcf4f3f16ba5522052343228f3a4c9c1bf3bec471d8ebcd468a` |
| `bin/Release/SteamP2PFriends.dll`（受测产物） | 1436160 | `e73363e5a2567fc8bbd4c21b059fc9617d217a45f4bcad87fe3dce893d31a2a7` |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | 681472 | `761319b23228b16316e04f539f220ec6f171d2443f8e267d8c83988a84a61fdf` |

上述源码哈希与 t14 自报一致 → 工作区处于 t14 最终态。**重跑 `dotnet build` 得到完全相同的 DLL 哈希 `e73363e5…`**（确定性构建），因此本报告的哈希绑定不是"只读一次"的弱证据：任何源字节变化都会改变该哈希。

## 2 独立复跑三条 verify（本审查者实跑）

| # | 命令 | 退出码 | 计数 | 与 t14 自报比对 |
| --- | --- | --- | --- | --- |
| 1 | `dotnet build SteamP2PFriends.csproj -c Release` | 0 | 0 错 / 0 警 | 一致 |
| 2 | `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release` | 0 | 0 错 / 4 警（CS0649×3、CS0219×1，均既有文件） | 一致 |
| 3 | `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | 1 | **674/676**，Failed: 2 = GBA01 / NIL01 | 一致 |

失败两项的环境归因复核：两者栈均为 `TypeInitializationException → SDG.Unturned.NetReflection → ReflectionTypeLoadException`，与 t14 描述一致，属控制台进程缺 Unity 运行时，不是本票回归。Bug3-01..28 **28/28 PASS**（输出中 `[T14] SPF_DLL_SHA256=e73363e5…` 与磁盘哈希一致）。

补充事实：`SteamP2PFriends.csproj:35` 有 `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` —— 这是"生产 0 警"被强制的前提，也解释了 §3 中 M-A 字面变异为何无法编译。

## 3 变异实验（本审查者自跑，不采信自述）

每条变异都走完整链路：改源码 → build 生产 → build 测试项目（把新 DLL 复制进测试 bin）→ 跑 676 项 → 记录红名单与 DLL 哈希 → 还原。

| 编号 | 变异内容 | DLL sha256 | 结果 | Bug3 红名单 |
| --- | --- | --- | --- | --- |
| 基线 | 无 | `e73363e5…` | 674/676 | 无 |
| M-A(字面) | `Prefix_Load` 首行 `return true;` | — | **编译失败**：`CS0162 检测到无法访问的代码`（Patch.cs:437） | — |
| M-A(语义) | `P2PGroupStateRecoveryPatch.cs:460` 改为 `decision = GroupLoadDecision.PassThrough`（永不再延后，语义等同"Prefix 恒放行"） | `d710f871…` | 671/676 | **13 / 14 / 15**（3 条） |
| M-A2 | `:447` 通知期重入守卫的 `return false` 改 `return true` | `b3af93f8…` | 673/676 | **23** |
| M-B | `:284-341` `SubscribeNativeEvents` 整体空实现（其余不动） | `0160ffe7…` | 671/676 | **12 / 25 / 26**（LifecycleSubscribeFailed） |
| M-C | `:383` `OnLevelLoaded` 与 `:390` `OnServerShutdown` 方法体清空（订阅仍安装） | `d8af5da6…` | **674/676（全绿）** | **无** |
| 还原 | 恢复备份文件 | `e73363e5…` | 674/676 | 无 |

判定：

- **M-A / M-B 真红 → 核心假绿已消灭。** 13/14/15 证明"延后决策"有胶水级杀手，12/25/26 证明"订阅安装"有杀手，23 证明"通知期重入守卫"有杀手。E2 的主要诉求（不再只测 seam/自报）成立。
- **M-C 全绿 → 假绿未消灭干净。** 把两个会话边界处理器整段删掉，676 项里一条都不红。这是本轮最有分量的发现（见 F2）。
- **M-A 自述不可复现。** t14 报"Prefix 首行放行 → 红 4 条（13/14/15/23）"。字面写法在 `TreatWarningsAsErrors=true` 下是编译错误，无法产出 DLL；语义等价写法只红 3 条，23 是另一个分支（M-A2 单独可红）。说明 t14 的 M-A 实际改法与描述不同（见 F3）。

## 4 逐条验收 + E1/E2/E3/E5/E6 关闭判定

| 验收项 | 判定 | 证据 |
| --- | --- | --- |
| 核对 E1：兜底路径存在且可观测 | **未关闭** | `GroupStateRecoveryCore.cs:181-195` 只有"同租约第二次请求"才 ForcedPassThrough；没有墙钟/帧数兜底。作者自认"GAME 后缀前玩家不会第二次 load"→ 该兜底在真实风险窗口内不触发（F1） |
| 核对 E2：seam 已抽出且 L182 短路已修 | **部分关闭** | seam `GroupLoadPlanner.cs:10` 已抽且 `Patch.cs:460` 只取字段；短路已在 `Patch.cs:406` 改 `wasPending \|= Core.OnPlayerDestroyed(token)`。但边界处理器仍无覆盖（M-C）→ F2 |
| 核对 E2：假绿被真正消灭（M-A/M-B 真红） | **成立** | §3 实跑：M-A 红 13/14/15，M-B 红 12/25/26 |
| 核对 E3：catch 内已回滚账 | **关闭** | `Patch.cs:470-499` 先提交、可抛工作后移，`:485` `AbandonPending("prefix-failed")` + `:488` `NotePrefixRollback`；Bug3-15 断言回滚后 `TryTakeDeferredLoad == None`、`FlushAuthorizations == 0`，能真红（M-A 变异下 15 变红） |
| 核对 E5：失败态传播为可观测失败 | **关闭** | `P2PGroupRegistration.cs` 三类失败原因 + `RestartRequired`；`RegisterManual` 跨 Id 抛 `StaleOwnerOrHotReload`；父层 `AuditModules` catch 置 `_registrationStageFailed`；Bug3-25/26 覆盖 Ready/复用与热重载拒绝，M-B 下二者真红 |
| 核对 E6：新文件已入库、无 `??` 残留 | **关闭（就本票文件而言）** | 5 个生产文件 + 测试文件均 `A `（staged）；`SteamP2PFriends.csproj` 仅 +9 行插入，无删除/重排；Bug3-27 打印 DLL sha256。但工作区仍有他票/未归属的改动（F4） |
| 检查 outOfScope 触碰 | **有越界痕迹，非 t14 声明** | `PatchRegistrationDiagnosticsModules.cs` +11 行（`Bug1ProbePatch.RegisterManual` 接入）、`publishi/0.2.4.10/` 三文件被删、`.scratch/reported-bugs-2026-09-17/` 三文件 M、`.scratch/_review_tmp/` 未跟踪（F4） |
| 检查 csproj 只增不删不重排 | **通过** | `+9/-0`；新增 5 个 Bug3 Compile 项，既有 4 行 Bug1 未动 |
| 检查测试可靠性（时序/线程/静态状态、只到 getter） | **部分通过** | 28 条用例均 `ResetForTests()` 起步、try/finally 复位 seam，未见顺序依赖（连续两次全量跑结果一致）；`EligibilityResolverForTests` 等全局可变态全部 finally 清理；但边界处理器、`PublishNativeGroupState`、`BuildEligibility` 的真实映射仍无胶水覆盖（F2、F7） |

## 5 Findings

### F1（high）E1 的兜底在真实风险窗口内不触发，仍是"静默吞 load + save 永久 no-op"
- 位置：`Core/Patches/P2PGroupStateRecovery/GroupStateRecoveryCore.cs:181-195`；`P2PGroupStateRecoveryPatch.cs:461-468`
- 现象：只有"同一租约的第二次 `PlayerQuests.load`"才返回 `ForcedPassThrough`。真实时序里玩家在 GAME 组表就绪前不会再调第二次 `load`（作者在 UNKNOWN 中自认），因此一旦 Postfix 永不到来（程���中途热加载插件 / 该关卡 `GroupManager.onLevelLoaded` 已在登记前发生 / Postfix 在 `NoteGroupTableLoaded` 之前 early-return 或抛异常），pending 会永久存在：该实例后续每次 `load` 都判 sameLease → Deferred → Prefix 返回 false，原生 load 永不执行，`wasLoadCalled` 恒 false → 原生 `save()` 首行 `if (!wasLoadCalled) return;`（`.scratch/bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerQuests.cs:2870`）→ 整局任务进度不落盘，且无任何超时/重试能把状态恢复。
- 影响：静默、不可恢复、数据丢失；与 E1 的原始风险表述完全一致，本轮只是换了一个在真实时序中可能不发生的触发条件。
- requiredFix：给 pending 一个有界寿命（记录登记时的帧号/时间戳，在 `Update`/`save` 前的前缀或下一个 `onLevelLoaded` 之外的固定帧数后强制放行并 `Degraded`+Warn），或只在"确知 GAME 后缀尚未发生"时登记 pending；无论哪种，都要让 `Degraded` 出现在玩家可见日志而非只在账本里。

### F2（high）会话边界处理器零覆盖：删掉 `OnLevelLoaded`/`OnServerShutdown` 整个会话重置，676 项全绿
- 位置：`P2PGroupStateRecoveryPatch.cs:383-393`（处理器）、`:421-433`（`ResetBoundary`）；对应测试文件通篇无这些符号
- 现象（M-C 实测）：保留订阅、清空两个处理器的方法体 → 674/676 全绿，Bug3 28/28 全 PASS。
- 为什么是高风险而不只是"缺测试"：`ResetBoundary` 是 epoch 递增与 pending 清空的唯一生产入口。若它失效，第二局开始时 `IsGroupsReadyFor(sameMap)` 会因上一局的 `_groupsReadyEpoch` 仍命中而返回 true → Prefix 直接放行 → 原生 `load()` 在组表未就绪时提前执行 → 队伍成员信息按原 Bug3 形态丢失。**也就是说：这处回归会让用户报的 Bug3 在"同一地图第二局"复现，而测试全绿。** 这正是 E2 要防的假绿，只是还剩这一块。
- requiredFix：补两条胶水用例（可用 `ResetForTests` 起步，直接调 `OnLevelLoaded(Level.BUILD_INDEX_MENU)` / `OnServerShutdown`）：①先制造 pending，再调边界处理器，断言 `HasPending == false`、`BoundaryResets == 1`、epoch 已递增；②边界后同图再次 `Prefix_Load` 必须重新 Deferred（而不是因旧 epoch 直接放行）。并把 M-C 作为修复轮的判据（该变异下这两条必须红）。

### F3（medium）M-A 变异的自述与可复现形态不一致，证据需订正
- 位置：`SteamP2PFriends.csproj:35`（`TreatWarningsAsErrors=true`）；`P2PGroupStateRecoveryPatch.cs:434-437`
- 现象：`Prefix_Load` 首行插入 `return true;` 会产生 `CS0162` 并被提升为错误，构建失败、无法产出 DLL；因此"首行放行 → 671/676、红 13/14/15/23"不可能来自该字面改法。我的语义等价变异只红 13/14/15，23 由独立守卫（`:444-450`）决定（M-A2 单独可红）。
- 影响：结论（核心假绿已消灭）不受影响，但"变异证据"作为交付物必须可复现；否则后续轮次无法用同一判据复核。
- requiredFix：在 t14 证据中记录**实际**补丁片段（改了哪一行、改成什么）与对应 DLL 哈希，而不是描述性说法；若采用"首行 return true"的叙述，需注明它不可编译并给出替代形态。

### F4（medium，交付归属）工作区混入非本票改动与发布产物删除
- 现象：`Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs` +11 行（`Platform.Diagnostics.Patches.Bug1ProbePatch.RegisterManual(_harmony)` 接入，属 Bug1 探针）、`publishi/0.2.4.10/{RELEASE_NOTES.zh-CN.md,SteamP2PFriends-0.2.4.10.zip,manifest.json}` 三个文件被 **删除**、`.scratch/reported-bugs-2026-09-17/*` 三文件 M、`.scratch/_review_tmp/` 未跟踪。
- 影响：这些都不在 t14 的 changedPaths 里。发布产物被删尤其危险——0.2.4.11 若按当前工作区打包，0.2.4.10 的发布证据会凭空消失，且 `PatchRegistrationDiagnosticsModules.cs` 的改动未经本票审查。
- requiredFix：由 Captain 归属：属他票的显式记录，不属任何票的还原；在任何打包/冻结动作前确认 `publishi/` 删除是有意为之。

### F5（low-medium）受控补发对 multicast 委托只调用一次，首个抛异常的订阅者会中断其余订阅者且不重试
- 位置：`P2PGroupStateRecoveryPatch.cs:666`（`handler();`）
- 现象：`onFlagsUpdated` 是字段式 multicast 委托；`handler()` 在第一个抛异常的订阅者处中断，后续订阅者永不收到；上层记 `NotifyFailed("callback-threw")` 并明确"不重试、不整批重发"→ 部分 UI 永久不刷新，且账本只记一次失败，看不出"部分成功"。
- requiredFix：改为遍历 `GetInvocationList()`，逐订阅者 try/catch 并累计 `NotifyFailedSubscribers`，至少保证其余订阅者被通知；或在失败后允许一次针对性重发。

### F6（low）通知期重入被拒时，被拒的那次原生 load 没有任何 pending 兜底
- 位置：`P2PGroupStateRecoveryPatch.cs:444-450`
- 现象：命中重入守卫时直接 `return false` 且不登记 pending、不打日志（只有 `ReentryRejections` 计数）。若该次调用真是某实例的首次 `load`，它就被永久跳过，`wasLoadCalled` 保持 false → 同样落到 save no-op。窗口很窄（只在补发回调内），但失败形态与 F1 同级。
- requiredFix：拒绝时至少 `RoleLogger.Warn` 带 token/地图；如判定被拒者可能不是 `_notifyTarget` 的重复调用，应回退为放行而不是拒绝。

### F7（low）`PublishNativeGroupState` 与 `BuildEligibility` 仍无胶水级用例
- `PublishNativeGroupState`（`:700+`）只有 IL 契约（`changeRank` 恰 1 次、不走 `ServerAssignToGroup`），没有"无组时不发布 / 有组时发布"的行为用例；`BuildEligibility`（`:333+`）仍只由 Bug3-08/09 对 `IsEligible` getter 做字段翻转，Unity 对象→字段映射（loopback 判定、`Provider.user` 空值）无覆盖。属残留假绿面，优先级低于 F2。

### F8（low）计数/状态语义容易被误读
- `GroupStateRecoveryCore.cs:277-283`：`NoteInvariantViolation` 内部调用 `_ledger.MarkAbandoned("invariant:"+detail)`，把"账本放弃"与"不变式违反"混成同一状态位，后续读账本无法区分。
- `AbandonPending`（`:247`）在 pending 已空时静默返回 false；`TryNotifyFlagsCore:641` 的 `AbandonPending("notify-lease-changed")` 在多数路径下正好命中"已空"，既不计数也不留日志。
- `RequiredFix`：把"不变式违反"独立成账本字段；`AbandonPending` 返回 false 时至少记一次 `NoteAbandonNoop`。

## 6 通过项（明确记录，避免下一轮重复审）

1. 注册链未被破坏：`PatchRegistrationAuditModules.cs:251` 仍由 `ApplyV2AuditFixPatches_Core` 在既有 try/catch 内登记；`RegisterManual` 无 `[HarmonyPatch]`，不会被 `PatchAll` 顺带注册，与既有手动登记惯例一致。
2. 真注册核验仍是"真 Harmony"而非自报：Bug3-12 用新建 `Harmony` 实例 → `GetPatchInfo(original)` → 比对 `owner + PatchMethod`；Bug3-11 用 IL 证明 `RegisterManual → VerifyCurrent → GetPatchInfo` 调用链存在。
3. E3 修复正确：先提交 pending、再把可抛工作（seam 观察者、日志）后移，catch 内回滚账并清 `_pendingQuests`，回滚后 `TryTakeDeferredLoad` 必为 None（Bug3-15 断言）。
4. E5 修复正确且是"显式失败"而非静默：三类 `FailureReason` + `RestartRequired`；跨 Harmony Id/跨 MVID 走 `UnpatchOwned` 后抛异常，父层 catch 置 `_registrationStageFailed`。
5. E6 就本票文件而言合规：6 个文件已 staged，`git status` 无 `??`；csproj 纯追加 9 行；构建确定性（同源 → 同 DLL 哈希）。
6. 无新增第三方依赖；测试项目仍通过 `Evidence\**\*.cs` 通配与 `HintPath` 引用交付 DLL；`InternalsVisibleTo` 使 IL 契约作用于真实产物。
7. 测试隔离：28 条用例均以 `ResetForTests()` 起步，seam 在 finally 中清理；连续两次全量运行计数完全一致（674/676，红名单相同）。

## 7 结论与建议顺序

`verdict = needs_revision`。E3/E5/E6 关闭；E2 在"M-A/M-B 真红"这一硬指标上成立，但会话边界这一块仍是完全假绿（F2），且 F2 隐藏的失败形态正好是用户报的 Bug3 在第二局复现；E1 换了个触发条件但风险未实质下降（F1）。

建议修复顺序：**F2 → F1 → F6 → F5 → F3（证据订正）**；F4 交 Captain 归属、F7/F8 可并入同轮或记为已知缺口。

修复轮的判据建议直接复用本报告的变异集：同一 DLL 哈希下先跑基线全绿，再跑 M-A(语义) 必须红 13/14/15、M-B 必须红 12/25/26、**M-C 必须红新增的两条边界用例**，最后还原并核对 `e73363e5…`。

## 8 本次审查对工作区的改动与还原证明

- 变异期间仅改 `Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs` 一个文件，备份于 `/tmp/patch_orig.cs`。
- 每次变异后均 `dotnet build` 生产 + 测试项目并实跑 676 项；结束后恢复备份。
- 还原校验：`sha256(P2PGroupStateRecoveryPatch.cs) = 956f6a12c4fa8c9e82159ba09f7e6345e9d50dab673218c6c980655a3b1edaa8`（与 §1 一致），重跑 `dotnet build` 后 `sha256(bin/Release/SteamP2PFriends.dll) = e73363e5a2567fc8bbd4c21b059fc9617d217a45f4bcad87fe3dce893d31a2a7`，套件回到 674/676、Bug3 28/28、0 条 Bug3 FAIL。
