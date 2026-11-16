# T26 工程轴复审 r4：写安全标记生命周期、Ready 窗口、变异可失败性

- 审查对象：t24 交付字节（Bug3 恢复实现第 4 轮）
- 审查人：reviewer-eng（工程轴；不评需求满足度，不改源码）
- 审查日期：2026-11-06
- 任务：t26，attempt 2 / `053d86a7-9244-41c7-8fc4-19b314ff4a29`

## 0. 结论

**verdict = pass（可进入生产交付核验）**。无 blocker、无 high。

四条 acceptance 要求的变异全部由本人自跑、先绿后红、逐字节还原；fail-closed 屏障、Ready 双门、
flags 单次 multicast 三条关键语义都具备真实的可失败用例（不再是自报）。r3 的 F1/F2/F3 判定闭合，
F4/F6 仍在但为 low，不影响上线正确性。

未达标项只有一条且为 low：⑥ 后半「新增代码无死代码」不成立（t22 回退的残骸：`ForcedPassThrough`
枚举成员与其计数器、`NoteNotifySubscriberFailed` 一族）。它不改变任何运行时行为，但让两条用例变成
**结构上不可能失败**的断言（见 R4-02），属测试有效性问题而非上线风险，故不据此阻塞。

仍然存在、且已在 t24 自报 UNKNOWN 中的残余风险：deferred load 永久无法兑现时（GAME 组表永不到来 /
执行前被判 not-ready / prefix 异常走 blocked 分支），该实例本局既不 load 也不 save，**本局任务进度丢失
但不写坏档**。这是 t24 明确选择的失败方向，且相对基线（原版会提前 load 并把 Nil/MEMBER 落盘）不再
产生错误数据；判为 medium 残余风险，不阻塞，但必须写进 0.2.4.11 known-issues（R4-01）。

## 1. 被审字节锁定

工作树为 **LF** 行尾（r1 的 CRLF 已改）。所有 sha256 为本人实测：

| 文件 | 字节 | sha256 |
| --- | --- | --- |
| `Core/Patches/P2PGroupStateRecovery/GroupStateRecoveryCore.cs` | 22511 | `13358e6c89619ae2bb45c61ebdb21264a6d3cfd294536a8cae3524b637271468` |
| `Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs` | 56257 | `2e0df7dd1d6e98039fd5153fd0deb320509abce552a0bbf08c897cbd2aeb8523` |
| `Core/Patches/P2PGroupStateRecovery/GroupLeaseLedger.cs` | 6308 | `8a495c7b3b90fa2c6877a3fb28948653180e4abd88e02da7ef843d82e1a2b5f4` |
| `Core/Patches/P2PGroupStateRecovery/GroupLoadPlanner.cs` | 1344 | `a45533850f31bd96f3d63e39c56b2e5a084fc3f782b1ef984521d39fe64afb0f` |
| `Core/Patches/P2PGroupStateRecovery/P2PGroupRegistration.cs` | 1790 | `5b0c4e9ba4c87cbeb7363b3846b27f26f464e8acad51c25bc07831d5fbe04ea4` |
| `WhitelistTests/Evidence/Runtime/P2PGroupStateRecoveryRuntimeTests.cs` | 72690 | `e18b3f4578a27e404fcb0aa87f4dab9c75d3ad9125ae972a537ca8111176f155` |
| `bin/Release/SteamP2PFriends.dll` | 1445376 | `64cd3fed828d48d0515d3b440466c56626c91cde8a6da5315645d55734e873bf` |

字节归属说明：`git status` 对 Patch.cs 显示 `AM`，是 `core.autocrlf=true` 的归一化意图提示
（`warning: LF will be replaced by CRLF the next time Git touches it`）；实测工作树文件与
`git show ":<path>"`（index blob）**同为 56257B / 2e0df7dd**，内容一致，`git diff --numstat` 为空。

## 2. 独立复跑（本人实测，非引用 t24 自报）

| 命令 | 退出码 | 结果 |
| --- | --- | --- |
| `dotnet build SteamP2PFriends.csproj -c Release` | 0 | 0 错 0 警 |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release` | 0 | 0 错 4 警（CS0649×3、CS0219×1，均既有） |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | 1 | **692/694**，Failed 2 = GBA01 / NIL01 |

- Bug3 用例 **46/46 PASS**（≥41 的合同要求满足）。
- 两项失败的既有环境原因：`TypeInitializationException → SDG.Unturned.NetReflection →
  ReflectionTypeLoadException`（控制台无 Unity 运行时），与本票无关，t14/r3 同因。
- 与 t24 自报（DLL 64cd3fed、46 条全绿、692/694）**一致**。

## 3. 变异可失败性（本人自跑，先基线后变异，逐字节还原）

方法：备份 → 脚本做**字节替换**（`D:/tmp/mut_r4.py`）→ `dotnet build SteamP2PFriends.csproj -c Release`
→ `cp bin/Release/SteamP2PFriends.dll WhitelistTests/bin/Release/`（保证测试跑的就是被变异字节）→
跑 exe → 解析 `FAIL [...]` 与 `=== Final Result ===` → 还原（`git show ":<path>" > <path>`，见 §6 EOL 陷阱）。

基线：DLL `64cd3fed828d48d0515d3b440466c56626c91cde8a6da5315645d55734e873bf`，692/694，Bug3 46/46 绿。

| 变异 | 改动点 | 变异后 DLL sha256 | 结果 | 新增红用例 |
| --- | --- | --- | --- | --- |
| **M1** fail-closed 改回放行 | `P2PGroupStateRecoveryPatch.cs:621` `if (slot != null && slot.Managed && !slot.NativeLoadCompleted)` 末尾追加 `&& Environment.TickCount == int.MinValue` | `fff4cdc19a3e7a553a17ff4b07930024b014fc063b0fd5a9dfbbbb2808dad309` | 690/694 | **Bug3-39 Load Throws Degrades Without Retry**、**Bug3-44 Reset Session Keeps Dirty Object Barrier** |
| **M2** 跳过 Ready 门 | `GroupStateRecoveryCore.cs:331` `if (!IsGroupsReadyFor(mapName))` 末尾追加同一恒假条件 | `5177f2a3e3ceb20484bfce083c61c54fc48873488c67302473b00597cdbada9c` | 690/694 | **Bug3-17 Second Deferral Leaves No Pending**、**Bug3-42 Not Ready Never Authorizes** |
| **M3** flags 回退成逐订阅者隔离 | `P2PGroupStateRecoveryPatch.cs:866` `handler();` → `foreach (Delegate rawSubscriber in handler.GetInvocationList()) { try { ((FlagsUpdated)rawSubscriber)(); } catch (Exception) { Core.NoteNotifySubscriberFailed(); } }` | `72a90a0771221f3ba72d352814854402f0d0eadce8592861d1e1b274dc1f5f44` | 690/694 | **Bug3-33 Flags Notify Multicast Isolates Subscribers**、**Bug3-45 Notify Throws Does Not Block Save** |
| **M4** 清空写入安全标记 | `P2PGroupStateRecoveryPatch.cs:1032` `slot.Managed = true;` → `false;` | `23edffe2cfe317bb64c9e47bc6e8466e0a763f46226ec72fe36a9e13a5e4080e` | 690/694 | **Bug3-39**、**Bug3-44** |

四条变异的共同基线失败恒为 GBA01 + NIL01（环境），其余 2 项即上表新增红用例，无一例外。

还原核验：源码回到 `2e0df7dd` / `13358e6c` / `8a495c7b`，重建 DLL 回到
`64cd3fed828d48d0515d3b440466c56626c91cde8a6da5315645d55734e873bf`，复跑 **692/694**（仅 GBA01/NIL01），
Bug3 46/46 绿 —— 逐字节还原成立。

判读：

- **M1/M4 证明 F1 的修复不是空话**：把"未完成 load 的实例禁止保存"这道屏障拿掉（直接或清空标记），
  两条针对写坏档的用例立刻红。
- **M2 证明 Ready 门是承重的**：它同时红在授权侧（17）与拒绝侧（42），说明 Core 门不是被 Execute 侧
  二次门掩盖的死逻辑。
- **M3 证明 t22 回退的异常语义被用例钉住**：逐订阅者隔离会让 C 在 B 抛错后继续执行（33 红），且会把
  失败误记成成功（45 红）。这条同时反证 F5「首抛中断后续」不是缺陷而是本轮刻意选择。
- **不会失败的自述**：t24 未给出 4/4 的完整红名单与哈希，本轮补齐；M1 的红名单比 t24 自述更宽（多 2 条）。

## 4. 逐条 acceptance

### ① 自跑全部变异并贴证据（4 类）——**通过**

四类变异全部覆盖：M1（fail-closed 改回放行）→ 红 39/44；M2（跳过 Ready 门）→ 红 17/42；
M3（flags 回退逐订阅者隔离）→ 红 33/45；M4（清空写入安全标记）→ 红 39/44。每条给 DLL sha256、
前后计数、新红用例编号、逐字节还原（§3）。要求的"F1 用例红 / F2 用例红 / Bug3-33 红"全部命中。

### ② 独立复跑——**通过**

生产 build 0 错 0 警；测试工程 build 0 错 4 警（既有 CS0649×3、CS0219）；exe 仅 GBA01/NIL01 失败，
692/694，Bug3 46/46 绿（≥41）。与 t24 自报数字一致，未发现自报虚高。

### ③ 写入安全标记的键与生命周期——**通过（残余 medium，R4-01）**

键 = `{Epoch, Token, MapName, Managed, NativeLoadCompleted}`，槽挂在 `ConditionalWeakTable<PlayerQuests,_>`。

- 置脏：`Patch.cs:1024 MarkInstanceManaged`（决定拦截原生 load 后）、`:1037 MarkInstanceBlocked`（prefix 异常且 host 上下文）。
- 置净：**仅** `:1047 MarkInstanceComplete`，且要求 `slot.Token / slot.Epoch / slot.MapName` 与本次执行租约
  三者一致，否则**静默不置净**（偏向拒绝保存，方向正确）。
- 释放：没有显式释放。`Patch.cs:486 ResetBoundary` 只重置 Core 与 `_pendingQuests`，不动槽；
  `:1076 EnsureCurrentToken` 在 epoch 变化时轮换 token 并把 `NativeLoadCompleted` 清 false，但保留 `Managed`。
- **「第二个存档永不保存」不可达**：脏槽绑定 PlayerQuests 实例；原生 `load()` 的唯一起点是
  `ClientMessageHandler_PlayerConnected.cs:111 → Player.InitializePlayer()`，每连接构造新的 Player
  （`Player.cs:1614` / `:1642`），"同一实例进入第二局"在原生路径不存在，脏槽随对象死亡被 CWT 回收。
  且 `Completed` 只在当前 epoch 内成立，`ResetSession` 后新会话的首次 load 必然重走 defer→complete。
- **但存在「本局永久不保存」**：pending 被丢弃后（execute 前 `not-ready-at-execute`、`token-mismatch`、
  `lease-changed`，或 prefix 异常走 `hostContext` 分支 `MarkInstanceBlocked`），该实例此局再无第二次
  `load()` 请求，屏障永久生效 → 本局既不 load 也不 save，玩家当局任务进度丢失（不写坏档）。
  这是 t24 明确选择的失败方向，判 medium 残余，须写进 known-issues（R4-01）。
- **混淆检查**：无把"租约已退役"当成"本局 load 成功"的路径——退役时 epoch 递增、槽仍是旧 epoch，
  只会更严格；反向的"旧租约的 Completed 被新会话复用"需要同实例跨局，已证不可达。

### ④ Ready 谓词四输入——**通过（残余 low，R4-03）**

- **null**：`NativeGroupTablePresent()` 的 `is IDictionary` 为假 → 拒绝（`Patch.cs:997`），满足"null 必须拒绝"。
- **合法空表**：`Dictionary` 非 null → 放行，满足"空表必须放行"（新图无组是正常态）。
- **跨 epoch**：`IsGroupsReadyFor` 要求 `_groupsReadyEpoch == _epoch`，`ResetSession`（`Core.cs:225`）
  置 -1 → 拒绝；Bug3-17/42 双面覆盖，M2 证明会红。
- **map 切换**：`string.Equals(..., Ordinal)` 不等 → Stale，pending 丢弃（Bug3-04 覆盖）。
- **残留窗口（low）**：ready 键只有 map 名 + epoch，不看 `knownGroups` 实例；若同一局内 GAME→GAME
  重载且未触发 SETUP/MENU 边界（`ResetBoundary` 唯一入口是 `OnLevelLoaded(level<=MENU)` 与
  `OnServerShutdown`），旧 ready 会继续命中。当前时序 GAME 重载必经 MENU，实际不可达，属"以事件顺序为
  隐式前提"的假设，见 R4-03。

### ⑤ t22 回退的异常语义——**通过**

- 单次 multicast（`Patch.cs:866 handler();`）：首个订阅者抛错即中断后续，`Bug3-33` 断言 C 不执行——绿。
- 不吞：外层 catch 记 `NoteNotifyFailed("notification-threw")` + `RoleLogger.Error`，账本可观测。
- 不重试：`NotifyAttempts>0` 使再次进入走"重复抑制"，`Bug3-45` 断言 `NotifyFailed==1`——绿。
- **正常 load 成功但通知抛异常不禁止保存**：`MarkInstanceComplete` 在通知之前（`:762`），`NotifyFlagsUpdated`
  的返回值被忽略，`PublishNativeGroupState` 照常执行（`:791-792`）——通知失败不阻断组状态发布，也不影响
  写入安全证明。M3 反向证明这层语义被用例钉死（隔离化后 33/45 双红）。

### ⑥a 五条禁止项 / members / 原生 load 恰好一次——**通过**

生产 5 文件 grep：`GroupManager.load` 0、`ServerAssignToGroup` 0、`ServerAssignToMainGroup` 0、
`new GroupInfo` 0、`knownGroups.Add` 0、`File.WriteAllText|WriteAllBytes` 0；`members` 仅 2 处注释
（`Patch.cs:25`、`:913`），无自增；`quests.load()` 全仓库仅 `Patch.cs:757` 一处调用，IL 用例
Bug3-10 亦断言 `ExecuteDeferredLoad → load ==1`。

### ⑥b 新增代码无死条件 / 无死代码——**未通过（low，R4-02，不阻塞）**

`GroupLoadDecision.ForcedPassThrough=2`（`Core.cs:18`）无生产者、无消费者（全仓库仅枚举定义）；
`_forcedPassThrough`（`Core.cs:154`）从无自增，属性 `:173` 恒 0；`NoteNotifySubscriberFailed`
（`Core.cs:403`）、`_notifyFailedSubscribers`（`:164`）、`Ledger.MarkNotifySubscriberFailed` /
`NotifyFailedSubscribers` 全部无调用点（t22 回退残骸）；`Core.cs:16-18`、`:288` 注释仍描述
"ForcedPassThrough/SkippedWatchdog"，与现状不符。**连带两条用例变成结构上不可能失败的断言**
（测试 `:376`、`:720`），详见 R4-02。

### ⑦ finding 必带 severity 与 requiredFix——**通过**

§5 全部 finding 均给 severity + 文件:行 + requiredFix；无 high，故不存在"以建议后续优化放行 high"。

## 5. Findings

### R4-01（medium，残余风险，不阻塞）本局永久不保存

- 位置：`Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs:621`（屏障）、
  `:1037`（`MarkInstanceBlocked`）、`P2PGroupStateRecoveryPatch.cs:712-751`（pending 被 Abandon 的三个出口）
- 现象：pending 一旦被丢弃（`not-ready-at-execute` / `token-mismatch` / `lease-changed`），或 prefix 异常
  走 `hostContext` 的 blocked 分支，该实例本局再无 `load()` 触发点，屏障永久生效：不 load、不 save、
  只有一行 Warn，玩家当局任务进度丢失。不写坏档（相对 r3 的 F1 是改善）。
- 可达性：需要"GAME 组表永不到来"或"执行前被判未就绪"。正常关卡流程不可达；但这是本票唯一会
  **静默丢进度**的路径，且 t24 自己也列为 UNKNOWN。
- requiredFix：①（必须）把该失败方向写进 0.2.4.11 known-issues，并确保 `SaveBlockedDeadline` 计数与
  `save-blocked:*` 日志可被支持渠道检索；②（可选，若下轮有余量）为 pass-through 成功路径补一个
  `Postfix_Load` 清槽，或对 `not-ready-at-execute` 保留 pending 而不是 Abandon（前者更小、更安全）。

### R4-02（medium，不阻塞）死代码与两条结构性不可能失败的断言

- 位置：`GroupStateRecoveryCore.cs:18`（`ForcedPassThrough=2`）、`:154`/`:173`（`_forcedPassThrough` 恒 0）、
  `:164`/`:403`（`_notifyFailedSubscribers`、`NoteNotifySubscriberFailed` 无调用点）、
  `GroupLeaseLedger.cs:46`/`:167`（`NotifyFailedSubscribers`、`MarkNotifySubscriberFailed` 同）、
  测试 `P2PGroupStateRecoveryRuntimeTests.cs:376`、`:720`
- 现象：t22 回退把逐订阅者计数删了实现，却留下计数器、方法、枚举成员与注释；两条用例仍在断言
  `ForcedPassThrough == 0`、`NotifyFailedSubscribers == 0`——这两个字段在全仓库无自增点，
  **断言不可能失败**，属假绿（虽不掩盖任何真实回归，因为 39/44/33/45 已覆盖行为）。
- requiredFix：删除枚举成员 `ForcedPassThrough`、两个计数器、`NoteNotifySubscriberFailed`、
  `Ledger.MarkNotifySubscriberFailed` 与对应两条断言；同步订正 `Core.cs:16-18`、`:288` 的过时注释。
  若保留计数器，则必须在真实路径自增并让用例断言"变异时非零"。

### R4-03（low）Ready 键只含 map 名

- 位置：`Core/Patches/P2PGroupStateRecovery/GroupStateRecoveryCore.cs:245-263`、`P2PGroupStateRecoveryPatch.cs:672-710`
- 现象：ready 以 `(epoch, mapName)` 为键，不绑定 `knownGroups` 实例；若同局内同一张图 GAME→GAME 重载且
  未触发 SETUP/MENU 边界事件，旧 ready 继续命中 → 授权 load 时组表可能是新表未 load，退化为原 Bug3 形态
  且本用例集不红。当前时序 GAME 重载必经 MENU，实际不可达。
- requiredFix：把 `knownGroups` 引用（或 level 装载序号）记为 ready 代次，与 map/epoch 一同校验；
  或在 `OnLevelLoaded` 进入 GAME 前先失效 ready。属加固项，非必须。

### R4-04（low）`VerifyCurrent` 与登记期订阅口径不一致（r3 F4 未改）

- 位置：`P2PGroupStateRecoveryPatch.cs:240-246`
- 现象：登记期允许 `_providerSubscribed/_playerSubscribed` 为 environment-unavailable，但 `VerifyCurrent`
  要求三者全真，同 Id 热重载校验会因此判失败并走 rollback。
- requiredFix：统一口径——记录每个订阅的 `unavailable` 原因，`VerifyCurrent` 只在"声明可用却缺失"时失败。

### R4-05（low）26/46 条用例无 try/finally，断言失败会泄漏单例状态

- 位置：`WhitelistTests/Evidence/Runtime/P2PGroupStateRecoveryRuntimeTests.cs`（26 条，例如 `:16-33`、
  `:112-132`、`Test_CandidateDllHashRecorded`）
- 现象：这些用例共享生产静态单例 `Core`、seam 与 `_registered`；一旦断言抛错，`ResetForTests()` 不执行，
  后续用例可能级联红/假绿，导致变异报告的红名单失真。
- requiredFix：统一改为 `try { ... } finally { P2PGroupStateRecoveryPatch.ResetForTests(); }`，或抽
  `Setup/Teardown` 包装。属测试可靠性欠债，不影响生产。

### R4-06（low）`Core.Notifying` 期间的 save 被一律拒绝且不记账

- 位置：`P2PGroupStateRecoveryPatch.cs:615`
- 现象：通知回调内（同一线程）若有人触发 save，直接被拒，无账本记录；同步回调下窗口极窄。
- requiredFix：加 `NoteSaveBlockedNotify` 计数或仅对"同一实例且尚未 complete"拒绝，便于现场定位。

### R4-07（low，非本票）BuildArtifact Independent Verifier 间歇红

- 位置：`WhitelistTests` 的既有 `[BuildArtifact] BuildArtifact Independent Verifier` 用例
- 现象：r3 期间 3 跑中出现 1 次额外失败（powershell 子进程），本轮 3 跑未复现。
- requiredFix：保持"仅 GBA01/NIL01 计入失败"的口径时，需在交付说明中声明该用例为环境 flake。

## 6. 复现命令

```bash
# 基线
dotnet build SteamP2PFriends.csproj -c Release
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release
WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe | tail -5   # 692/694

# 变异（示例 M1）；脚本 D:/tmp/mut_r4.py 内含 M1..M4 的字节替换字典
python /d/tmp/mut_r4.py M1
dotnet build SteamP2PFriends.csproj -c Release
cp bin/Release/SteamP2PFriends.dll WhitelistTests/bin/Release/
WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe | grep -E "^FAIL |Final Result"
sha256sum bin/Release/SteamP2PFriends.dll      # fff4cdc1…

# 还原（不要用 git checkout --，会把 LF 改成 CRLF）
git show ":Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs" > Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs
```

**EOL 陷阱**：仓库 `core.autocrlf=true`，`git checkout -- <file>` 会把 LF 工作树改写成 CRLF
（Patch 变 57483B、sha256 变化），破坏"逐字节还原"；还原只能用 `git show ":<path>"`（index 存 LF）。

**审查只读声明**：除变异期间对 `P2PGroupStateRecoveryPatch.cs` / `GroupStateRecoveryCore.cs` 的临时字节
替换外，未修改任何文件；结束时工作树字节与 §1 锁定表一致，重建 DLL 与交付 DLL 同哈希。
