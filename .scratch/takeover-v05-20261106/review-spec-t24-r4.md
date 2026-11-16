# T25 规格轴复审 r4：F1/F2 关闭与 flags 回退（被审 t24）

日期：2026-11-06
审查官：reviewer-spec（规格轴独立审查）
被审任务：t24「Bug3 修复轮 r4：F1 存档写入安全 + F2 组表 Ready 门 + t22 flags 回退（同一批收口）」
参照物：`requirements-v2.md`（21763B / `17fc427c…`）、`architecture-v2.md`（28732B / `d1d7ab31…`）、`architecture-decision-t22-flags-and-retired-lease.md`（10293B / `c6fbec1a…`）、`architecture-rescue-t23-save-failure-direction.md`（20203B / `f6f96b1b…`）、`review-eng-t19.md`（16613B / `66aa58b1…`）

---

## 0. 结论

**verdict = needs_revision**

按行为判：**F1（存档写入安全、fail-closed）与 F2（组表 Ready 门）已按 t23 真实关闭**，而且是我自己跑变异验证过的——把 `Prefix_Save` 的受控失败返回改成 `true`，`Bug3-39/41/44/46` 立刻变红；删掉 `TryTakeDeferredLoad` 的 NotReady 门，`Bug3-42`（并连带 `Bug3-17`）变红；把实例写入屏障短路，`Bug3-39`（第二次 save）与 `Bug3-44`（跨局旧脏对象）变红。t22 的 flags 回退也在生产代码与用例两侧落地（单次 multicast、首抛中断、不重试、`NotificationFailed`）。

判 needs_revision 的原因**不在行为**，而在三条「裁决里写明必须做到、交付里没做到」的规格项：

1. **R1（low）** t22 明文要求「Program.cs 保留 Bug3-33 ID 但名称须改为 'Multicast Stops On Subscriber Exception, No Retry' 等准确表述」。交付里 `WhitelistTests/Program.cs:882` 仍是 `Bug3-33 Flags Notify Multicast **Isolates Subscribers** (F5)`，测试方法名也仍是 `Test_FlagsNotify_Multicast**Continues**AfterSubscriberThrows`——**名字断言的行为正好是裁决否决掉的那一个**，而方法体内的断言已是相反语义（C 不执行、A=1/B=1/C=0）。这不是风格问题：读 `Program.cs` 的人会以为「逐订阅者隔离」已经实现，而它恰恰是 t22 明确不追认的方案。
2. **R2（medium）** t23 §5 对 Bug3-39 的要求含「NotifyAttempts=0」。交付的 `Test_LoadThrows_DegradesAndDoesNotRetry`（`P2PGroupStateRecoveryRuntimeTests.cs:966-1001`）只断言 `!core.Ledger.CanNotify()`，注释却写「不补发通知」。`CanNotify()==false` 在「根本没有尝试通知」和「尝试过但状态不允许」两种情况下都为真，**证不出 attempt 未发生**。
3. **R3（low）** t23 §5 对 Bug3-41 的要求含「模拟原生已置 `wasLoadCalled=true` 且内存部分改动」。交付的 `Test_SaveBlocked_LeavesArchiveFileUntouched`（`:1006-1048`）只做「哨兵 + 文件字节 + mtime」，没有这一步，因此无法证明拒绝保存**不依赖**原生 `wasLoadCalled` 恰好为 false。

三条都是几行到几十行的机械收口，不改任何生产逻辑。除此之外本轮 F1/F2/t22 的实质工作我判为合格。

> 本轮 verdict 不是 pass，当然更不等于 Bug3 Green。T1a 与实机 T1b/T2a/T3a/T4a 仍按前几轮口径为 UNKNOWN（见 §6）。

---

## 1. 被审字节绑定

**声明：本判定只对下列字节有效。**若后续任何被审文件字节变化，本报告结论即失效，须重新取证。

| 文件 | 字节 | sha256 |
|---|---:|---|
| `Core/Patches/P2PGroupStateRecovery/GroupLeaseLedger.cs` | 6308 | `8a495c7b3b90fa2c6877a3fb28948653180e4abd88e02da7ef843d82e1a2b5f4` |
| `Core/Patches/P2PGroupStateRecovery/GroupLoadPlanner.cs` | 1344 | `a45533850f31bd96f3d63e39c56b2e5a084fc3f782b1ef984521d39fe64afb0f` |
| `Core/Patches/P2PGroupStateRecovery/GroupStateRecoveryCore.cs` | 22511 | `13358e6c89619ae2bb45c61ebdb21264a6d3cfd294536a8cae3524b637271468` |
| `Core/Patches/P2PGroupStateRecovery/P2PGroupRegistration.cs` | 1790 | `5b0c4e9ba4c87cbeb7363b3846b27f26f464e8acad51c25bc07831d5fbe04ea4` |
| `Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs` | 56257 | `2e0df7dd1d6e98039fd5153fd0deb320509abce552a0bbf08c897cbd2aeb8523` |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationAuditModules.cs` | 11924 | `5d21c3a5d7f1e8f089c813dab5ecc425ac704a5605e4517c5958516fac656968` |
| `SteamP2PFriends.csproj` | 27097 | `c4f4f7f52587dfe1a0c27f74a485a2d75f17c54e7e41fab701255aa396564b04` |
| `WhitelistTests/Evidence/Runtime/P2PGroupStateRecoveryRuntimeTests.cs` | 72690 | `e18b3f4578a27e404fcb0aa87f4dab9c75d3ad9125ae972a537ca8111176f155` |
| `WhitelistTests/Program.cs` | 131931 | `7519ea32963667fbbb4ffdb2dcdd3efa39a9e2679738ec6853c5ed74f4ac4bb7` |

交付产物：

- `bin/Release/SteamP2PFriends.dll` = `WhitelistTests/bin/Release/SteamP2PFriends.dll` = 1445376B / `64cd3fed828d48d0515d3b440466c56626c91cde8a6da5315645d55734e873bf`
- `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` = 729088B / `09fc6a56d289933adbdee0d5149c0147cc8584c0561494422841a35ce474bcba`

与 t24 自报字节一致性：t24 自报 DLL `64cd3fed…`，与磁盘一致；无「派工描述与交付字节不符」项。

我用上述源码以 `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` 重建，**产出 DLL 与交付 DLL 逐字节相同（`64cd3fed…`）**，这构成「源码 ↔ DLL ↔ 用例计数」的可复现链条。

---

## 2. 逐条判定（t25 acceptance）

判定基线：`WhitelistTests/bin/Release` 运行交付 exe → exit 1，`=== Final Result: 692/694 PASS (Failed: 2) ===`；唯一失败为 `GBA01`/`NIL01`（`System.TypeInitializationException: SDG.Unturned.SteamPlayer → NetReflection → ReflectionTypeLoadException`，控制台缺 Unity 运行时，与历轮同因）；`Bug3-01..46` 全 PASS，共 46 条。

| # | 判据 | 判定 | 定位 |
|---|---|---|---|
| 1 | F1「load 失败 → save 被拒 → 重复仍被拒」三态**可失败**断言 | **pass**（可失败性我实测） | `P2PGroupStateRecoveryRuntimeTests.cs:966-1001`：`!Prefix_Load` → `!Prefix_Save` → 再 `!Prefix_Save`，`loadAttempts==1`、`RealLoadSuccess==0`、`State==LoadFailed`、`Degraded`、`IsSaveBlockedForTests`。变异 A 下该用例整条变红（见 §3） |
| 2 | F2：`IsGroupsReadyFor` 谓词与 `TryTakeDeferredLoad` Ready 门同一判据，未 Ready 时授权不可达 | **pass** | `GroupStateRecoveryCore.cs:255-260`（谓词：非空 map && `_groupsReadyEpoch==_epoch` && Ordinal 相等）与 `:312-343`（epoch/map 校验后、取 token 与 `MarkClaimed` 前 `return NotReady`）；`P2PGroupStateRecoveryPatch.cs:739-746` 在 `ExecuteDeferredLoad` 再验同一谓词 + `NativeGroupTablePresent()`；用例 `:1054-1075`（`NotReady` 且 pending 保留、`loadEntry=0`） |
| 3 | Bug3-39 改写是否真可失败（把 fail-closed 改回放行必须变红），审查官自跑并贴 DLL sha256 与前后计数 | **pass** | 见 §3 变异 A：`688/694`，新增红 4 条含 Bug3-39；DLL `78346c8f…` |
| 4 | Bug3-41 证明文件没被动过（哨兵 + 隔离临时文件字节/mtime） | **部分 pass → R3** | `:1006-1048` 有哨兵 `writeSectionCalls==0`、逐字节比较、mtime 相等；**缺** t23 §5 要求的「模拟原生已置 `wasLoadCalled=true` 且内存部分改动」 |
| 5 | t22 回退按裁决落地：委托快照单次 multicast、首抛中断后续、不重试、外层记 `NotificationFailed`；A/B/C 与 attempt/failed 计数符合；重入/finally/exactly-once 未删未放宽 | **行为 pass，命名不合裁决 → R1** | 生产 `P2PGroupStateRecoveryPatch.cs:833-894`：`Try { handler(); } catch { NoteNotifyFailed("notification-threw"); return false; }`（无 invocation list 拆分、无订阅者计数）；用例 `:697-727` 断言 `!dispatched`、A=1、B=1、C=0、`NotifyFailed==1`、`NotifyAttempts==1`、`NotifyFailedSubscribers==0`，二次调用被拒且计数不增。重入 `Bug3-23`、finally/IL `Bug3-24`、exactly-once `Bug3-18/21/28`、不变式 `Bug3-22` 均在册且 PASS |
| 6 | 写入安全标记跨局不得误拦：「本局未 load 成功」与「load 成功但租约已退役」走不同分支 | **pass** | `Bug3-43 New Lease Completes, Other Archive Untouched`（`:1100-1135`：Reset 后同一已完成对象仍可保存，总 load=1；新 epoch 新 key 自己成功后可保存）+ `Bug3-44 Reset Session Keeps Dirty Object Barrier`（`:1141-1165`：走生产边界 MENU 后仍 `!Prefix_Save`、`RealLoadSuccess==0`）；实现侧 `MarkInstanceComplete` 只认 `slot.Token==token && slot.Epoch==Core.Epoch && map` 相等，`ResetSession` 不触 CWT，`EnsureCurrentToken` 跨 epoch 轮换 token 并重置 `NativeLoadCompleted=false`（`P2PGroupStateRecoveryPatch.cs:1047-1090`） |
| 7 | 判定绑定字节并声明适用边界 | **pass** | §1 + §0 |
| 8 | 残留项写明归属，不得以 pass 掩盖 | **pass**（本轮本就不是 pass） | §6 |

补充核对（t23 函数级清单，逐条读码）：

- ① `IsGroupsReadyFor` 为 `internal`，谓词与 t23 §3 逐字一致（`GroupStateRecoveryCore.cs:255-260`）。
- ② `TryTakeDeferredLoad` 在 Ready 检查前**未** `ClearPending`、**未** `MarkClaimed`（顺序正确，`:312-343`）。
- ③ `OnLoadRequested` 同租约重复且未 Ready → 仍返回 `Deferred`，保留 pending 与原账（`:266-306`，`_deferredRepeatRequests++`，不 `Begin`）。变异 B 证明这条与 Ready 门互为兜底（Bug3-17 连带变红）。
- ④ `Prefix_Load`：已接管而提交/日志异常 → `AbandonPending("prefix-failed")` + `MarkInstanceBlocked` + `return false`；非目标 `return true`（`:557-600`）。没有「catch → 原生放行」的旧行为。
- ⑤ `Prefix_Save` 实例写入门**先于** `HasPending` 判断（`:621-640`），异常一律 `false`（`:654-665`）。
- ⑥ `ExecuteDeferredLoad` 执行前再验身份/Ready/一次性授权；`MarkInstanceComplete` 在 `NoteDeferredLoadSucceeded` 之后、通知/发布之前（`:761` 一带）；抛错 `NoteDeferredLoadFailed` + `LoadFailed/Degraded`、不重试。
- ⑦ 组表 Postfix 只在 `level > BUILD_INDEX_MENU`、P2P host + server + 非 dedicated、非空 map 时推进；未 Ready 不授权且**不清 pending**（`:672-710`）。
- ⑧ 完整性证明用 `ConditionalWeakTable<PlayerQuests, WriteSafetySlot>`，未借用 `wasLoadCalled`/全局账本状态（`:46-57`、`:1024-1090`）。

**一处顺序偏差（记为观察，不单列 finding）**：`Postfix_GroupTableLoaded:685` 先 `Core.NoteGroupTableLoaded(mapName)` 再在下游两个执行点验 `NativeGroupTablePresent()`，与 t23 ⑦「native 表对象存在才登记就绪」的书写顺序不同。因该方法体是 `GroupManager.onLevelLoaded` 的 postfix（原生组表 load 已在该方法体内完成），且两个消费点都复验表对象，**未观测到可授权到原生 load 的路径**，故只作观察交 Architect 备查。

---

## 3. 我自跑的变异实验（不采信 Builder 自述）

三处变异都是我改、我建、我跑、我还原。每行给出：改动 → 变异后源码 sha256（变化文件）→ DLL sha256 → 结果计数 → 新增红。

| 变异 | 改动 | 源码 sha256（改动文件） | DLL sha256 | 结果 | 新增红 |
|---|---|---|---|---|---|
| 基线（送审） | — | — | `64cd3fed…` | `692/694` | —（仅 GBA01/NIL01 环境失败） |
| **A**（t23 §5 变异①，fail-closed 反转） | `Prefix_Save` 两处受控失败返回改 `true`（`:632` 一带的 `return false;` 与 `if (outcome != Authorized) return false;`） | `P2PGroupStateRecoveryPatch.cs` → `19afb6b9ff9d3deb417095bfea2326fad5982cee5d957e4fc1c3330fb9ad6f52` | `78346c8f6cebd06d19f1da7ea8f9decc9e1d2059434347e04ddb90901982fdeb` | `688/694`（Failed 6） | **Bug3-39**（同一场景重复保存仍必须被拒绝）、**Bug3-41**（load 失败的实例必须拒绝保存）、**Bug3-44**（未完成成功 load 的旧脏对象必须继续拒绝保存）、**Bug3-46**（native 组表为 null 时必须拒绝保存） |
| **B**（t23 §5 变异②，删 Ready 门） | 删 `GroupStateRecoveryCore.TryTakeDeferredLoad` 的 `if (!IsGroupsReadyFor(mapName)) { …; return NotReady; }` 整块 | `GroupStateRecoveryCore.cs` → `b09676adf576e3eaf5ee89bc75582ffaa82f61e37f5e9b63adec487c9c2dc6cd` | `b310599ed696f97b8f0bbe5236ed1c235fb87713e545c497c99dbbfad31fc228` | `690/694`（Failed 4） | **Bug3-42**（Not Ready Never Authorizes, t24/F2）、**Bug3-17**（Second Deferral Leaves No Pending, E1） |
| **C**（t23 §5 变异③，实例写入门短路） | `Prefix_Save` 屏障条件改为 `if (false && slot != null && slot.Managed && !slot.NativeLoadCompleted)` | `P2PGroupStateRecoveryPatch.cs` → `ce2ee919b3d8b09a25d5e8c0fb7f0eedcd471f8bf7ecdeb8492b6dbffd18f0ca` | `6e3a61e6440043fdbf9ceee4b8b2e725af232f85cb0dbe71396384993401cc54` | `690/694`（Failed 4） | **Bug3-39**（第二次 save 未被拒）、**Bug3-44**（跨局旧脏对象被放行） |

结论：

- 验收第 3 条（Bug3-39 必须真可失败）**成立**：变异 A 下它红，变异 C 下它同样红（第二次 save 分支）。
- F2 的 Ready 门是**承重**的：删掉即 `Bug3-42` 红，且连带 `Bug3-17` 红（说明 Ready 门同时兜住 E1 的重复延后路径），并非装饰性判断。
- 「重复仍被拒」与「跨局持久屏障」两条语义分别被变异 C 的两次红单独钉住。
- t23 §5 变异④（Reset 抹实例证明 → 43/44 红）与变异⑤（删安全兑现原生调用 → 32 红）我**没有**跑；本轮 acceptance 只强制 ①（我另加 ②③），故列出为未做项，供 Captain 判断是否需要补。据读码推测：④ 若把 `MarkInstanceComplete` 证明在 Reset 时清掉，`Bug3-43`（Reset 后同一对象仍可保存）会红；⑤ 若删 `ExecuteDeferredLoad` 中的受控 `quests.load()` 调用，`Bug3-32` 的 `loadInvocations==1` 会红。二者均为推测，未实测，不作为结论。

**还原证明（必须记录）**：变异后我把 `P2PGroupStateRecoveryPatch.cs` 还原为备份字节，得 56257B / `2e0df7dd1d6e98039fd5153fd0deb320509abce552a0bbf08c897cbd2aeb8523`（与 §1 送审字节一致）；`git hash-object` = `git rev-parse :<path>` = `a8b1f19b…`（工作树与暂存 blob 同对象）。强制重建后 DLL 回到 `64cd3fed…`，复跑 `692/694`、`Bug3-01..46` 全 PASS。当前工作区不保留任何变异字节。

**取证过程中的两处坑（供后续审查复跑参考，非被审对象缺陷）**：

1. **增量构建会骗人**：源码被外部改写（我用 python 文本改写后又用 `git show :<path>` 取回原始 blob）之后，一次普通 `dotnet build -c Release` 仍链接了旧的变异目标文件，产出 `5177f2a3…` 并跑出 `Bug3-17/42` 两条「假红」。改用 `-t:Rebuild` 后立刻回到 `64cd3fed…` / `692/694`。**结论：本仓复跑必须 `-t:Rebuild`，否则会把构建缓存问题误判成代码缺陷。**
2. **本机 DLL 字节非严格可复现**：同一份源码连续两次 `-t:Rebuild` 得到 `64cd3fed…` 与 `72a90a07…` 两个不同哈希（MVID/时间戳类噪声）。因此**DLL 哈希只能作为「交付快照标识」，绑定物是源码 sha256**；任何以 DLL 哈希为唯一绑定物的验收口径都应改成「源码 sha256 + 由该源码重建得到的 DLL」。

另：本仓 `core.autocrlf` 开启，`git checkout -- <path>` 会把 LF 改成 CRLF 从而改变字节；还原必须用 `git show :<path> > <path>` 或直接 cp 备份。

---

## 4. Findings

| id | severity | problem | requiredFix | file:line |
|---|---|---|---|---|
| `T25-R1` | low | t22 裁决明文要求「Bug3-33 保留 ID、名称须改为 'Multicast Stops On Subscriber Exception, No Retry' 等准确表述」。交付仍为 `Bug3-33 Flags Notify Multicast Isolates Subscribers (F5)`，测试方法名仍为 `Test_FlagsNotify_MulticastContinuesAfterSubscriberThrows`；而方法体内断言的是相反语义（C 不执行、A=1/B=1/C=0、`NotifyFailedSubscribers==0`）。**用例名称断言了一个被裁决否决的行为**，会误导后续审计与出包说明 | 保留 `Bug3-33` ID，把 Program.cs 注册名与方法名改成与实现一致的表述（例：`Bug3-33 Flags Notify Multicast Stops On Subscriber Exception, No Retry (t22)`），方法体断言一行不动 | `WhitelistTests/Program.cs:882`、`WhitelistTests/Evidence/Runtime/P2PGroupStateRecoveryRuntimeTests.cs:697` |
| `T25-R2` | medium | t23 §5 明确要求 Bug3-39 断言 `NotifyAttempts=0`。交付只断言 `!core.Ledger.CanNotify()`，而 `CanNotify()` 同时被「从未尝试通知」和「状态不允许通知」满足，**无法证明失败路径没有发出就绪通知**；测试注释「不补发通知」因此大于实际断言 | 在 Bug3-39 中加 `Assert(core.Ledger.NotifyAttempts == 0, …)`（可同时加 `NotifyFailed == 0`），保留现有断言不变 | `WhitelistTests/Evidence/Runtime/P2PGroupStateRecoveryRuntimeTests.cs:966-1001` |
| `T25-R3` | low | t23 §5 要求 Bug3-41 额外「模拟原生已置 `wasLoadCalled=true` 且内存部分改动」。交付只有哨兵 + 文件字节 + mtime，未构造该前提，因此不能证明拒绝保存**不依赖**原生 `wasLoadCalled` 恰好为 `false` 这一巧合 | 在 Bug3-41 调用 `Prefix_Save` 前用反射置 `wasLoadCalled=true` 并改动一条内存态（如 flagsList/questsList 增加一项），再断言 `!Prefix_Save`、哨兵 0、文件字节与 mtime 不变；其余断言不动 | `WhitelistTests/Evidence/Runtime/P2PGroupStateRecoveryRuntimeTests.cs:1006-1048` |

三条都属于「使既有证据名副其实」的收口，**不需要改动任何生产代码**，也不推翻本轮 F1/F2 的行为结论。

---

## 5. 已确认合规（明确肯定，避免下一轮重复劳动）

- **F1 失败方向 = 少执行**：`Prefix_Save` 对「已接管但无完整成功证明」默认 `false`，且 `ExecutingDeferredLoad || Notifying` 期间直接 `false`（`P2PGroupStateRecoveryPatch.cs:608-666`）；`MarkInstanceComplete` 只在同 token/同 epoch/同 map 且 load 正常返回后置位（`:1047-1059`）。写坏默认值、半填列表、未就绪 Nil/MEMBER 落盘这几类风险被结构性挡住。
- **F2 Ready 门**：谓词单一来源、`NotReady` 不消费 pending 不授权、执行前复验；变异 B 证明其承重。
- **Bug3-41 的「文件没被动过」**：除计数器外，确有逐字节比较与 mtime 相等（`:1006-1048`），符合 acceptance 第 4 条主项；只缺 §5 那一步前提构造。
- **t22 flags 回退**：生产侧单次 `handler()`、首抛中断、`NotificationFailed`、不重试、不拆 invocation list、不留订阅者计数（`:833-894`）；用例侧 A/B/C 与 `NotifyAttempts/NotifyFailed` 与裁决逐项对齐（`:697-727`）；`Bug3-33` 是被改写而非删除。
- **重入/finally/exactly-once 未被删除或放宽**：`Bug3-18/21/22/23/24/28` 均在册且 PASS；`Bug3-18` 的「第二次通知被拒且计数不增」仍在。
- **跨局不误拦**：`Bug3-43`（已完成对象在 Reset 后仍可保存）与 `Bug3-44`（未完成对象跨 Reset 持续拦截）两条走的是不同分支，acceptance 第 6 条成立。
- **写安全 §4 键与轮换**：CWT 槽键为 `(PlayerQuests 引用, epoch, instanceToken)`+map；`EnsureCurrentToken` 在跨 epoch 同引用时轮换 token 并重置 `NativeLoadCompleted=false`，save/通知/超时路径均不自行轮换；`ResetSession` 不触碰 CWT。
- **五组禁止项**：未提前/重复 `GroupManager.load`（`GroupManager` 仅作 `AccessTools` 目标与 `DeclaringType` 校验，另加 `knownGroups` 只读反射）；未二次 `InitializePlayer`（仅注释出现）；无手写 Quests/Groups 解析、无反射改 `wasLoadCalled`；未用 `ServerAssignToGroup`；未放宽唯一真实 P2P Host/资格门；未干预未接管 Guest/单机/dedicated（变异与读码双向确认放行路径）。
- **`Bug3-32` 改写**：前置已加 `NativeGroupTableProbeForTests = () => true` 与 `NoteGroupTableLoaded("PEI")`，并保留「重复 save 不二次 load」正控（`:657-693`）；「未 Ready 也强制兑现」的旧断言已不存在（该否定分支由 `Bug3-46` 接手）。

---

## 6. 残留与归属

| 项 | 现状 | 归属 |
|---|---|---|
| **T1a**（同 fixture 真实存档、只换候选 DLL 得原 α/OWNER） | 仍无任何用例覆盖，46 条中零真实存档 fixture | **后续轮**（需求票 T1a 专用），本轮计数不得顶替 |
| **T1b / T2a / T3a / T4a**（真机 1H+2G、客机重连/退队/UI 呈现） | 未验证 | **需真机 1H+1G/1H+2G**，由实机门禁票关闭 |
| **E1 在「整局不再 save 且 GAME 后缀永不到来」** | 仍不落盘（`wasLoadCalled=false` → 原生 save no-op），但**不写坏档**；该情形已被 t23 §7 登记为可接受限制 | t23 §7 已登记；若需消除，另开「无 save 兜底」票 |
| **KNOWN-MULTICAST-PARTIAL-DELIVERY** | 首个订阅者抛错后后续订阅者可能未收到本次 bulk 就绪；不影响服务端事实正确性 | t22 已登记，出包说明须携带 |
| **`RegisterManual` 通用 catch 不回滚**（`P2PGroupStateRecoveryPatch.cs:231-237`） | 若 `harmony.Patch` 中途抛非注册异常，已装补丁可能留活 | **后续轮**（t21 已记，超出 t19 F1/F2 范围） |
| **观察：`:685` 先登记 Ready 后验表对象** | 无观测到的授权泄漏路径 | Architect 备查，不单独计缺陷 |
| **本机 DLL 字节非严格可复现** | 连续两次 `-t:Rebuild` 哈希不同 | 证据口径建议（源码 sha256 为绑定物），非被审对象缺陷 |

---

## 7. 复审边界

- 本报告只判「是否满足需求契约与架构裁决」，未评判命名/复杂度/注释密度等风格项（另一轴负责）。
- 全程只读生产代码；三次变异均为临时改动并已还原，还原后工作树与暂存 blob 同对象（`a8b1f19b…`），DLL 回到 `64cd3fed…`，`692/694`。
- 未重跑 `dotnet build` 之外的任何部署/真机流程；未宣称 Bug3 Green。
- 本判定仅对 §1 所列字节有效。

---

报告自绑定：`review-spec-t24-r4.md` = 21867 B / sha256 `4269866f89ee7c0d21a977c1c26c413fa0dfc8c6a09c19bf493d10f43842858b`
