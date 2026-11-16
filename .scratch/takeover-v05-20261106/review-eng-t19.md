# T19 工程轴复审（round 3）：t18 交付的 Bug3 恢复实现

- 审查人：reviewer-eng（DeepSeek）
- 审查对象：t18（attempt 1）交付的工作区字节
- 被审基线：t14/t18 声称的“同一 DLL 哈希链”
- 审查方式：只读审查 + 独立复跑（构建、用例、变异）；未修改任何生产语义；变异实验后已逐字节还原
- 结论：**verdict = needs_revision**（E2/E3/E5/E6 实测闭合；E1 兜底引入了新的可破坏存档路径，见 F1/F2）

## 1. 被审字节锁定（结论只对该批字节有效）

| 文件 | 字节 | SHA-256 |
| --- | --- | --- |
| `Core/Patches/P2PGroupStateRecovery/GroupStateRecoveryCore.cs` | 20042 | `a3ca6aa0ab3725397e218579ee57fb68465cb83d4298706988adfb6dd4e6f819` |
| `Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs` | 45224 | `3afe988c4a83a7dcb73d954cf87c584714f81b53db2b20459fc77cd5987897e6` |
| `Core/Patches/P2PGroupStateRecovery/GroupLeaseLedger.cs` | 6097 | `338dc8d927da9e12785d1c20e882acd700fcfa36779664e3ab4a1ac4d59798b5` |
| `Core/Patches/P2PGroupStateRecovery/GroupLoadPlanner.cs` | 1344 | `a45533850f31bd96f3d63e39c56b2e5a084fc3f782b1ef984521d39fe64afb0f` |
| `Core/Patches/P2PGroupStateRecovery/P2PGroupRegistration.cs` | 1790 | `5b0c4e9ba4c87cbeb7363b3846b27f26f464e8acad51c25bc07831d5fbe04ea4` |
| `WhitelistTests/Evidence/Runtime/P2PGroupStateRecoveryRuntimeTests.cs` | 56822 | `6ee1cdaf88fccfdacdb73ae56485e19acd138268f5998b6aab5d51f0065826ff` |
| `bin/Release/SteamP2PFriends.dll`（交付产物） | — | `178a73063e29243fed114b8ce4f71e8a508d1ac405cf15b8947b6db71ff8f5d3` |

`WhitelistTests/bin/Release/SteamP2PFriends.dll` 与 `bin/Release/SteamP2PFriends.dll` 同哈希（测试确实加载交付字节，非源码链接）。

## 2. 独立复跑（全部本回合实跑）

| 命令 | 结果 |
| --- | --- |
| `dotnet build SteamP2PFriends.csproj -c Release` | exit 0，0 错 0 警 |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release` | exit 0，0 错（4 警：CS0649×3、CS0219，均为既有） |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | exit 1，**686/688 PASS，Failed 2**：GBA01 / NIL01（`TypeInitializationException → SDG.Unturned.NetReflection → ReflectionTypeLoadException`，控制台无 Unity 运行时，环境缺失）；Bug3 40/40 PASS；输出 `[T14] SPF_DLL_SHA256=178a7306…` 与磁盘一致 |

补充：同一 exe 连跑 3 次，其中 1 次为 **685/688，Failed 3**，多出的一条是既有用例 `[BuildArtifact] BuildArtifact Independent Verifier`（见 F7）。即 t18 的“Failed: 2”不是每次都稳定复现。

## 3. 变异实验（本回合自跑，逐一还原）

每个变异：改 → `dotnet build SteamP2PFriends.csproj -c Release` → 拷贝 DLL 到测试输出目录 → 跑 exe → 恢复源文件并重建，校验 SHA-256。

| 变异 | 改动点 | DLL SHA-256（前 16） | 结果 | 新红用例 |
| --- | --- | --- | --- | --- |
| 基线 | 无 | `178a73063e29243f` | 686/688 | —（Bug3 40/40） |
| M-A | `P2PGroupStateRecoveryPatch.cs:509` `GroupLoadPlanner.Decide(...)` → `GroupLoadDecision.PassThrough` | `3748ad41a248a9a8` | 678/688，Failed 10 | Bug3-13/14/15/29/30/31/32/39 |
| M-B | `SubscribeNativeEvents()` 运行期直接返回（三段订阅全不执行） | `410bd2d9abc683f1` | 683/688，Failed 5 | Bug3-12/25/26 |
| M-C | `OnLevelLoaded`/`OnServerShutdown` 两处 `ResetBoundary` 调用删除 | `edf555553414d56d` | 683/688，Failed 5 | Bug3-29/30/31 |
| M-D | `Prefix_Save` 内 `ExecuteDeferredLoad(authorizedToken, mapName);` 删除 | `842c813c689a6a41` | 684/688，Failed 4 | Bug3-32/39 |
| 还原 | — | 源 `3afe988c…` / DLL `178a7306…` | 686/688，Bug3 40/40 | 与基线逐字节一致 |

结论：**E2 的“假绿”问题在工程指标上已被消灭**。四个此前全绿的实现部位（判定可放行、事件订阅、会话边界、save 期限）现在各自都有真红用例；这不是“自报”，是本回合独立复现。

## 4. 逐条核对（E1/E2/E3/E5/E6）

### E3 异常路径回滚 —— 关闭
`P2PGroupStateRecoveryPatch.cs:519-532` 先 `_pendingQuests = __instance; committed = true;`，把可抛的 seam/日志工作放到提交点之后；`catch`（`:530-548`）内 `if (committed) { Core.AbandonPending("prefix-failed"); _pendingQuests = null; }` 再 `NotePrefixRollback()` 并 `return true`。原生 load 照常执行，**但 Core 的 pending 已回滚**，Postfix 不会二次 Authorized。Bug3-15 覆盖（M-A 下也红，说明该用例真的走胶水）。

### E2 seam 抽出 + 非短路 —— 关闭
- `GroupLoadPlanner.cs:10` `Decide(eligibility, mapName, playerToken, core)` 承接全部判定；`P2PGroupStateRecoveryPatch.cs:509` 只取字段后调用，`ForcedPassThrough` 分支（`:512-518`）`Warn` 后 `return true`。
- `OnPlayerDestroyed`（`:430-448`）已改 `bool wasPending = ReferenceEquals(quests, _pendingQuests); wasPending |= Core.OnPlayerDestroyed(token);` —— 非短路，`_destroyedPending` 会真实增长。
- 变异矩阵（见 §3）证明四个部位都可判；`Test_ProductionWiring_ExecuteDeferredLoadCallsNotifyFlags`（测试 `:922`）用 `IlContractProbe.CountMethodCalls` 保证生产路径恰好一次补发。

### E5 失败可观测 —— 关闭
`RegisterManual` 三条失败路径均抛 `P2PGroupRegistrationException`（`Patch:143-200`），父调用点 `PatchRegistrationAuditModules.cs:248-258` 捕获后置 `_registrationStageFailed = true` 并 `Error`，最终 `return !_registrationStageFailed`；订阅失败先 `UnpatchOwned + UnsubscribeNativeEvents` 回滚再抛（`:167-190`）。M-B 变异下 Bug3-12/25/26 全红，说明“注册没成功”不会静默。Bug3-12 用真实 `Harmony.GetPatchInfo` 校验 load/groupTable/save 三目标的 owner + PatchMethod，非自报。

### E6 入库 —— 关闭（有 1 处低危）
`git status` 显示 5 个生产文件与 1 个测试文件、`Program.cs`、`SteamP2PFriends.csproj` 均为 `A `/`M `（已 staged），**无 `??` 残留**；`git diff --cached --numstat -- SteamP2PFriends.csproj` = `9 0`（只增不删不重排）。`publishi/0.2.4.10/` 三产物已恢复（不在此票 diff 中）。
低危：`Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDomainModules.cs` 仍是 ` M`，`git diff --numstat` 无输出 = 纯 EOL 噪音；`.scratch/reported-bugs-2026-09-17/` 四个 md 的内容改动属需求勘察票（t1），不是 t18 的范围，但同处工作区，交付前应清账或注明归属。

### E1 兜底 —— **未完全关闭（见 F1/F2）**
已实现部分：`Patch:556-599 Prefix_Save` 在 save 前强制兑现一次；`Core:350 NoteWatchdogForcedRelease` 记账 + `MarkDegraded`；`Core:255-283` 同租约第二次 Deferred → `ForcedPassThrough`；Bug3-32 覆盖“一次 load、Degraded、pending 清空、重复 save 不二次 load”。
未关闭部分：兜底把“不写盘”换成了“可能写坏盘”，且授时点不校验组表就绪。详见 §5。

## 5. Findings

### F1（high，新引入的存档破坏路径）强制兑现失败后仍放行原生 save
- `Patch:583-585`：`TryTakeDeferredLoad` 授权后 `ExecuteDeferredLoad(...)`，随后 `return true` —— 无论 `quests.load()` 成功与否，save 都继续。
- `Patch:677-692`：`load()` 抛错只记 `NoteDeferredLoadFailed("native-load-threw")` 并 `return`，不重试、不阻止后续。
- 原生 `PlayerQuests.load()` 首行即 `wasLoadCalled = true;`（`.scratch/bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerQuests.cs:2649`），随后先把 `isMarkerPlaced/markerPosition/radioFrequency` 重置为默认（:2651-2654），再从 Quests.dat 读 flags/quests。
- 原生 `save()` 只判 `if (!wasLoadCalled) return;`（:2872），然后**无条件**把内存里的 marker/radio/`groupID`/`groupRank`/`inMainGroup`/`flagsList`/`questsList` 全量写盘（:2875-2900）。
- 因此：兜底 load 抛错（IO 异常、`channel.owner.playerID` 为空等）→ 内存是默认值 + 空/半填列表 → 紧接着的原生 save 把这些**覆盖写回 Quests.dat**。修复前该场景是 save 直接 no-op、文件原样保留；t18 之后变成“整份进度被清零写回”。这不是“少存一次”，是数据丢失。
- 更值得注意的是：`Test_LoadThrows_DegradesAndDoesNotRetry`（测试 `:935-967`）在断言 `Assert(P2PGroupStateRecoveryPatch.Prefix_Save(quests), "原生 load 抛错也不得阻塞 save");` —— **把这条破坏性策略固化成了期望行为**，后续任何修复都会先撞这条用例。
- requiredFix：`Prefix_Save` 在 `ExecuteDeferredLoad` 之后读取账本（`Ledger.State == LoadFailed` / `RealLoadSuccess == 0`）时返回 **false**（拦下本次原生 save），并把 `Bug3-39` 的断言改为“load 抛错必须阻止本次 save、不得用默认值覆盖存档”；同时补一条“load 抛错 → 存档未被改写”的用例（可用 seam 记录 save 是否被放行）。

### F2（medium，护栏缺失）watchdog 授权点不证明组表就绪，强制 load 可能把 groupID 降级并落盘
- `Core:294-316 TryTakeDeferredLoad` 只校验 `epoch + map`，**从不校验 `IsGroupsReadyFor(mapName)`**；`Prefix_Save` 与 `Postfix_GroupTableLoaded` 共用它，于是 save 期限可以在一张尚未加载的组表上授权一次 `load()`。
- 原生 `load()` 尾部：`groupID != Nil && !inMainGroup` 时 `GroupManager.getGroupInfo(groupID)` 返回 null（表未加载即 null）→ `loadMainGroup()`（:2811-2824），后者把 `groupID` 改成 `playerID.@group` 或 `CSteamID.Nil`、`groupRank = MEMBER`（:2838-2850）。
- 随后放行的 save 把降级后的 groupID/rank 写盘（:2876-2878）—— 即 Bug3 的原始症状（丢队伍）被“兜底”持久化，而日志里只有一条 `Degraded`/`Warn`，玩家侧无感知。
- 可达性：需要“pending 存在但组表尚未在内存中加载”这一组合（例如玩家在 GAME 组表加载前的窗口内就触发保存/二次 load）。本回合无法在 SDK 环境证明该时序，故定 medium 而非 high；但作为 fail-open 授权点，成本极低：授权前 `if (!Core.IsGroupsReadyFor(mapName))` 则**不强制 load**、改为拦下本次 save 并 Warn（宁可不落盘，也不写错队伍）。
- 另一处同源：`OnLoadRequested` 的同租约 `ForcedPassThrough`（`Core:255-283`）在组表未就绪时直接放行原生 load，等价于回到修复前行为；这是 t18 自认的 F1 残余风险，但**没有任何护栏区分“表未就绪”与“表已就绪”**，建议同上。

### F3（low，计数语义可误读）`WatchdogForcedReleases` 在授权前自增
`Patch:573` 先 `Core.NoteWatchdogForcedRelease("watchdog:save-prefix")`，`:583` 才 `TryTakeDeferredLoad`；后者可返回 `Stale`/`None`（`:583-584` 直接 `return true`）。于是“计数”记录的是“尝试兑现”，不是“已强制放行”，排查时会把未授权的期限点读成已兑现。Bug3-32 只覆盖 Authorized 分支。requiredFix：把 `NoteWatchdogForcedRelease` 移到授权成功之后，或拆成 `WatchdogDeadlineReached` / `WatchdogForcedReleases` 两个计数。

### F4（low，失败态误判）`VerifyCurrent` 与登记时的“环境不可用”容忍口径不一致
登记路径允许 `_providerSubscriptionUnavailable` / `_playerSubscriptionUnavailable` 仍判 Ready（`Patch:172-186`），但 `VerifyCurrent`（`:210-216`）要求 `_providerSubscribed && _playerSubscribed` 同时为真。若某环境首次登记走的是 “unavailable” 容忍分支，之后同 Id 同 MVID 的再次登记（热重载探测）会被判为 `StaleOwnerOrHotReload(restartRequired:true)` 并回滚，属于把“可容忍”误报成“必须重启”。当前控制台环境下两个订阅都成功，故未暴露。requiredFix：`VerifyCurrent` 与登记判定同口径（unavailable 视为满足）。

### F5（low，测试隔离）19/40 条用例没有 `try/finally` 复位
`ResetForTests()` 出现 34 次、`finally` 只有 15 处；`Assert` 抛 `InvalidOperationException`（测试 `:1016-1020`）。凡是“末尾才 `ResetForTests()`”的用例，一旦断言失败就会把 `Core` 单例 / `_pendingQuests` / seam 委托留给后续用例（`RunTest` 只捕获异常计入 FAIL，不复位）。当前固定顺序 + 多数用例开头自复位，掩盖了这个问题；一旦新增/重排用例就会出现连锁红。requiredFix：统一 `try { … } finally { ResetForTests(); }`。

### F6（low，补发期间重入 load 无兜底）
`Patch:469-496` 在 `Core.Notifying` 且 `ReferenceEquals(__instance, _notifyTarget)` 时 `return false`，即丢弃这次 `load()`；注释说明“无法确认同一目标时放行”。由于该路径只发生在补发回调内（此时原生 load 已成功、`wasLoadCalled=true`），丢弃不会造成 pending 悬挂，属可接受；但缺一条“重入被拒后不产生新 pending、不改变账本”的断言（Bug3-23 只断言拒绝本身）。可并入 F5 一起加。

### F7（low，既有基线不稳定）`BuildArtifact Independent Verifier` 间歇红
同一 exe 连跑 3 次，1 次为 685/688，多出 `[BuildArtifact] BuildArtifact Independent Verifier`；手动执行 `Tools/Verify-BuildFingerprintArtifact.ps1 -Path bin/Release/SteamP2PFriends.dll` 输出 `INDEPENDENT_ARTIFACT_VERIFICATION_PASS`、exit 0。该用例通过 `powershell.exe` 子进程做独立校验（`WhitelistTests/Evidence/BuildArtifact/BuildArtifactEvidenceTests.cs:179-201`），存在环境/超时敏感性。**与 Bug3 无关、不是 t18 引入**，但它使“Failed: 2”这一交付口径不稳定，建议在证据里写明“2 条环境失败 + 该用例偶发”，或先修稳定性再作为门禁。

## 6. 回归与范围检查

- **outOfScope 触碰**：`Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs` 仍 +11/-0（`Platform.Diagnostics.Patches.Bug1ProbePatch` 接入，属 Bug1 票），t18 已声明保留；`PatchRegistrationDomainModules.cs` 为纯 EOL 噪音；`publishi/0.2.4.10/` 三产物已恢复。**未发现 t18 越界改生产补丁**。
- **既有 633 项基线**：Bug3 段位于 Runtime 段末尾（`WhitelistTests/Program.cs:850-889`），之后只剩 Final Result；`ResetForTests` 覆盖面足够，实测 686/688 与失败项均与 Bug3 无关，未见交叉污染。
- **运行时约束**：补丁方法签名遵循既有惯例（`public static bool Prefix_*(PlayerQuests __instance)`、`public static void Postfix_*(int level)`）；目标解析 `AccessTools.Method` + 显式 `null` 检查；异常一律 catch 后放行/记账，不向 Harmony 抛（除登记期，登记期抛出由父层捕获）。历史 run-14 的 `SecurityException` 场景未复现；三段订阅包在 `[MethodImpl(NoInlining)]` 方法里，类型 cctor 的 JIT 异常会落在方法边界内，处理得当。
- **`save` 前缀的全局影响**：`PlayerQuests.save` 对所有实例都挂了前缀，但 `Prefix_Save` 首判 `!Core.HasPending` 直接放行，Guest/单机开销可忽略，行为不变。

## 7. 结论

| 判据 | 结论 |
| --- | --- |
| E1 兜底存在且可观测 | **部分**：存在且记 Degraded，但引入 F1/F2 数据风险；且 `Failed` 时不拦 save |
| E2 seam + 非短路 + 假绿消灭 | **通过**（4 个变异自跑全红） |
| E3 catch 回滚 | **通过** |
| E5 失败可观测 | **通过** |
| E6 入库无 `??` | **通过**（附 EOL 噪音与异票 diff 的归属说明） |
| 新增/修改代码的边界与回归风险 | **不通过**：F1（high）、F2（medium） |
| outOfScope / csproj | 通过 |
| 测试可靠性 | 通过但需收口：F3/F5/F6 与 F7 的偶发 |

**verdict = needs_revision**。修复面很小且集中：F1（失败时拦 save + 改用例断言）与 F2（授权前校验组表就绪，不就绪则不强制 load、改拦 save）两处，其余为 low 级收口。E2/E3/E5/E6 已实测闭合，不建议再回退重做。

## 8. 复现命令

```
dotnet build SteamP2PFriends.csproj -c Release
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release
WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe
# 变异（示例，改后重建 + 拷贝 DLL 再跑，跑完还原并校验 sha256）
#  M-A P2PGroupStateRecoveryPatch.cs:509  Decide(...) -> GroupLoadDecision.PassThrough
#  M-B SubscribeNativeEvents() 运行期直接 return
#  M-C OnLevelLoaded / OnServerShutdown 删除 ResetBoundary(...)
#  M-D Prefix_Save 删除 ExecuteDeferredLoad(authorizedToken, mapName);
```
