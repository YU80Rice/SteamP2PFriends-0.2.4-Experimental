# T5 规格轴审查：Bug3 队伍身份跨局恢复实现（t3 attempt 2）

- 审查类型：规格轴（是否按契约做对），不评工程风格。
- 审查依据：`.scratch/takeover-v05-20261106/requirements-v2.md`（SHA256 `17fc427c…94017e`）、`.scratch/takeover-v05-20261106/architecture-v2.md`（SHA256 `d1d7ab31…b42339`）。
- 边界：本报告未修改任何源码；未重跑构建（Captain 已复跑，结果在下节引用）；不评判命名/复杂度/注释密度（t6 工程轴）。

## 0. 审查对象（绑定字节）

| 文件 | 字节 | SHA256（前 16） | mtime |
| --- | --- | --- | --- |
| `Core/Patches/P2PGroupStateRecovery/GroupStateRecoveryCore.cs` | 9231 | `5a09a22070d12609` | 2026-11-06 11:15:15 |
| `Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs` | 18301 | `b0a62e369a643645` | 2026-11-06 12:33:55 |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationAuditModules.cs` | 11924 | `5d21c3a5d7f1e8f0` | — |
| `SteamP2PFriends.csproj` | 26549 | `12b823df4a5fc629` | — |
| `WhitelistTests/Program.cs` | 122125 | `242054e7651a4dbe` | — |
| `WhitelistTests/Evidence/Runtime/P2PGroupStateRecoveryRuntimeTests.cs` | 14662 | `97731bad76023f77` | 2026-11-06 11:34:49 |

**字节基线差异（需 Captain 确认交付件）**：派工书写 `P2PGroupStateRecoveryPatch.cs` = 17891 B，磁盘实际 18301 B（+410 B，mtime 12:33:55，早于本票派发 12:40:20）。本报告全部结论绑定上表 `b0a62e36…` 字节；若 17891 B 才是 t3 自报的交付件，则两者差异位于文档注释段（410 行版本），需 Captain 以 `git status`/时间线判定哪份为交付物。结论不因该差异改变（关键逻辑行在两份中都存在）。

**已由 Captain 独立复跑、本报告直接引用（不重跑）**：`dotnet build SteamP2PFriends.csproj -c Release` → 0 错 0 警；`dotnet build WhitelistTests -c Release` → 0 错 4 警（既有 CS0649）；`WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` → 645/647 PASS，Bug3-01..12 全 PASS，失败项 NIL01/GBA01 为 native runtime routes 未配置（环境缺失，与本实现无关）。

## 1. 结论

**verdict = needs_revision（fail）**。

一句话：T1 主干实现与 `architecture-v2.md` §4.3 的最小方案结构一致（门控、租约、单次原生 load、原生 changeRank 发布、全部禁止项均被规避），但 **T1a 这条决定性自动化验收判据未交付**（无真实存档同 fixture 的 none/MEMBER→α/OWNER 对照，且 t3 自报端到端未复现、差异未定因），另有一项 §4.3-4 明文要求处理的「非组 Quests 就绪/flags 行为」未处理也未登记 UNKNOWN；T2/T3/T4 未触及且未按 requirements §2 登记 UNKNOWN。故本票不能通过，但代码可作为续修基础，不建议整份废弃。

判定明细（4 条独立关闭项 + 8 条架构约束 + 2 类负控）见 §2–§5，缺口编号见 §6。

## 2. requirements-v2.md：Bug3 四项独立关闭项逐条核对

> 规则：四项分别关闭；T1 组件 Green 不得连带关闭 T2/T3/T4。t3 未宣称修复、status=failed，**这一点做得正确**（没有拿状态机 Green 冒充 Bug3 修复）。

### T1 主机身份跨局恢复（本票唯一实质覆盖项）

| 判据 | 手段 | 实现覆盖位置 | 判定 |
| --- | --- | --- | --- |
| T1a 真实存档 + 真实组件顺序（MENU→`InitializePlayer`/`load`→GAME）+ 同 fixture 基线 none/MEMBER → 只换候选 DLL 得 α/OWNER；原生入口由生产 registrar 挂接 | 自动化 | 拦截点：`P2PGroupStateRecoveryPatch.cs:208-236`（`Prefix_Load` 在 `load()` 前 `return false`）；兑现点：`:242-273`（`GroupManager.onLevelLoaded` postfix，GAME 且 `level > BUILD_INDEX_MENU`）；单次执行：`:275-306`（`quests.load()` 仅一次，`ExecutingDeferredLoad` 防重入）；发布：`:314-330`（`changeRank` 原生回环） | **未覆盖（blocker）**。交付的 12 条自动化（`P2PGroupStateRecoveryRuntimeTests.cs`）全部是：状态机单测（Bug3-01..07）、纯结构体 12 变异负控（08/09）、对**测试自建方法**的 IL 计数（10）、注册器 IL/现场 Harmony 断言（11/12）。**没有任何一条**驱动真实存档、按 MENU→InitializePlayer→GAME 顺序回放，也没有「同一 fixture 先基数 none/MEMBER、再只换候选 DLL」的对照观察。t3 自报端到端 alpha/OWNER 在本机探针下未复现（探针反射为 True，SPF 内部 `BuildEligibility` 读到全 false），差异未定因。 |
| T1b 真机 1H+1G，两种退出路径 × 连续重进两次，B 入场前窗口 Host=α 且 OWNER，无新 β/γ | 真机 1H+1G | 实现提供了可观测落点：`deferred PlayerQuests.load`、`deferred native Quests.load executed once`、`native group state published after restore`（`:227-228`、`:297-298`、`:323-324`） | **UNKNOWN（不在本票可判范围）**，见 §7：应由后续真机门禁票关闭。 |

**T1 子项实现映射（供续修票直接引用）**：
- 「不得提前 `GroupManager.load`」→ 实现只在组表回调**之后**兑现（`:242-273`），未抢先 load 组表。
- 「不得手写存档解析/反射硬填 α/OWNER」→ 恢复值完全来自原生 `quests.load()` 读取的 `PlayerSave`（`PlayerQuests.cs:2675-2688` 读 `groupID`/`groupRank`），补丁不接触存档字节。
- 「不得二次执行 `InitializePlayer`」→ 只调 `quests.load()`（`:296`），未触碰 `InitializePlayer`。
- 「不得先 load 再重读」→ 前缀在首次 `load()` 之前拦截（`:229 return false`），`ExecuteDeferredLoad` 只执行一次，无「先 load 一遍再重读覆盖」。
- 「空存档/无合法组走原生默认、不猜队伍」→ `:318 if (!quests.isMemberOfAGroup) return;`（`isMemberOfAGroup => groupID != CSteamID.Nil`，`PlayerQuests.cs:307`），不构造组、不发默认值。**但无对应测试**（§6 F5）。

### T2 客机旧状态残留 / T3 客机无法退出 / T4 队伍 ID 不可见

| 判据 | 实现状态 | 判定 |
| --- | --- | --- |
| T2a [Unity-SDK 集成] 重连状态下发结束后权威成员表与 Guest 本地 group/rank 均为 α/MEMBER，成员数含 A/B | 未实现、未观测。实现里唯一沾边的是 `PublishNativeGroupState` 的 `changeRank` → `SendGroupState.InvokeAndLoopback`（原生 `PlayerQuests.cs:485-488`，回环+远端可靠下发），理论上会向 Guest 广播组状态；但 T2a 要的是「重连下**发结束之后**」的成员表与 Guest 本地值，无任何证据 | **未覆盖，必须登记 UNKNOWN** |
| T3a 独立夹具保留历史损坏 α/β，真实 Guest 退队入口驱动一次；Host 权威移除 B、B 权威 group=none；两端分开断言 | 完全未触及。生产路径无 `ServerAssignToGroup`/`ServerAssignToMainGroup`（`grep` 为 0，且 Bug3-10 IL 断言为 0），也没有退队/移除成员逻辑 | **未覆盖，独立门禁** |
| T4a 恢复后双方用于呈现的真实 group ID = α、成员/队友判定互认；退出后非同队 | 未实现、未测。`changeRank` → `ReceiveGroupState` 会无条件 `triggerGroupUpdated`（`PlayerQuests.cs:443-466`），提供了呈现层刷新通道，但无 UI/呈现端断言 | **未覆盖，独立门禁** |

**范围外理由是否成立**：成立。`architecture-v2.md` §4.1/§4.2/§6.1 把 T2/T3/T4 定为独立门禁，且 requirements §7 明确 T1 Green 不得连带关闭；本实现的修改面（前缀/后缀 + 状态机 + 注册接线）在最小方案范围内，不应在同票内一并改客机侧与 UI。**但**：范围外不等于可以沉默。requirements §2 规定「UNKNOWN 不是通过，必须写明缺哪一端/哪个观测点」，实现与 t3 报告未逐条登记 T2a/T3a/T4a 的缺失端点，本报告在此代为登记（§7）。

## 3. architecture-v2.md 禁止项核对

| 禁止项 | 证据 | 判定 |
| --- | --- | --- |
| 提前 `GroupManager.load` | 两文件 `grep "GroupManager.load"` 命中 0；`GroupManager` 仅用于 `AccessTools.Method(..., "onLevelLoaded", ...)`（`:65`）与类型断言（`:98`） | 遵守 |
| 手写存档解析 / 反射硬填 α/OWNER | `grep "PlayerSavedata|River|readSteamID"` 命中 0；唯一入口是原生 `quests.load()`（`:296`） | 遵守 |
| 用 `ServerAssignToGroup` 冒充恢复（members++） | `grep` 命中 0；Bug3-10 对 `ExecuteDeferredLoad`/`PublishNativeGroupState` 的 IL 计数 `ServerAssignToGroup==0`、`ServerAssignToMainGroup==0` | 遵守 |
| 二次执行 `InitializePlayer` | `grep` 仅出现在文档注释（`:18-20`）；Bug3-10 IL `InitializePlayer==0` | 遵守 |
| 先 load 再重读 | 前缀在 load 前拦截，`quests.load()` 单次，无第二遍读取 | 遵守 |
| epoch + 地图 + 实例租约 | `GroupStateRecoveryCore.cs:116-124`（`ResetSession` 递增 epoch、清 pending/ready）、`:133-137`（`IsGroupsReadyFor` epoch+map 双等）、`:157-168`（同租约幂等、异租约替换）、`:182-203`（`TryTakeDeferredLoad` 过期判 Stale）、`P2PGroupStateRecoveryPatch.cs:392-398`（`ConditionalWeakTable` 实例 token）、`:285-291`（token 不符丢弃） | 遵守 |
| 只在原生 GAME 组表回调成功后执行一次原生 `Quests.load` | `Postfix_GroupTableLoaded` 挂在 `GroupManager.onLevelLoaded`（组表 `new knownGroups` + `load()` 均在该方法体内，`GroupManager.cs:164-181`），`level <= BUILD_INDEX_MENU` 排除 MENU（`Level.cs:31-33`） | 遵守 |
| 走原生组状态发送/事件路径发布、修复早发 none 本机投影 | `:320-322` 取 `groupID/groupRank` 后调 `changeRank(rank)`；原生 `changeRank` 仅 `SendGroupState.InvokeAndLoopback(...)`（`PlayerQuests.cs:485-488`），`ReceiveGroupState` 无条件 `triggerGroupUpdated` + `broadcastGroupChanged`（`:443-466`），故同 rank 也能纠正本机投影 | 遵守（发布语义正确） |
| 不新增组包/自定义频道 | 无自建 `SendGroupState`/自定义频道，只用原生 `changeRank` | 遵守 |
| 不扩成全 Player 阻塞 | 仅对通过 `BuildEligibility` 的单个本地 Host 实例延后；其他 Player 的 `load()` 直接放行（`:214-215`） | 遵守 |
| **不得把待恢复值以 none 保存覆盖**；原生 `wasLoadCalled`/save 门须测试 | 原生 `save()` 首行 `if (!wasLoadCalled) return;`（`PlayerQuests.cs:2870-2875`），`wasLoadCalled` 只在 `load()` 首行置位（`:2648-2650`）→ pending 窗口内原生 save 是 no-op，结构上安全。但实现无任何断言覆盖该门（Bug3-10 的 `save==0` 只统计 `ExecuteDeferredLoad` 内部调用） | **部分：设计安全，证据缺失**（F3） |
| **若延后会改变非组 Quests 就绪行为，必须在 same-fixture 和 SDK 显示/flags 回归中解决** | 延后 `load()` 后，`InitializePlayer` 内 `if (channel.IsLocalPlayer) onFlagsUpdated?.Invoke()`（`PlayerQuests.cs:2597-2601`）会在 `flagsList` 仍为空时触发；deferred load 之后**没有任何**重新发布 flags/quests 的动作（两文件 `grep "flags|onFlagsUpdated"` 命中 0） | **未遵守**（F2） |

## 4. 门控与「仅本地 Host 的 Quests 持久化 load 延后一次」语义

### 4.1 判定条件（与 §4.3「P2P host + server + 唯一真实 local host」逐项对照）

`BuildEligibility`（`:333-362`）逐项取证：

| 方案要求 | 实现 | 位置 |
| --- | --- | --- |
| P2P host 模式 | `HostManager.IsP2PHostMode` | `:339` |
| server | `Provider.isServer` | `:340` |
| 非 dedicated | `Dedicator.IsDedicatedServer`（`IsEligible` 要求 false） | `:341` |
| 真实本地实例 | `ReferenceEquals(player, Player.LocalPlayer)` | `:344` |
| 本地玩家通道 | `channel.IsLocalPlayer` | `:347` |
| 非零 SteamID | `OwnerSteamId != 0UL`（`IsEligible`） | `:351-353` + `GroupStateRecoveryCore.cs:47+` |
| Host 自持 SteamID（owner == 本进程用户） | `OwnerSteamId == ProviderUserId` | `:354` |
| loopback 连接 | `owner.transportConnection is TransportConnection_Loopback` | `:350` |
| 唯一（不是多客户端同 SteamID） | `MatchingLocalClientCount == 1`，遍历 `Provider.clients` 统计 | `:355`、`:364-381` |
| 动态组功能开启 | `Provider.modeConfigData.Gameplay.Allow_Dynamic_Groups` | `:357-360` |

结论：门控**逐项对应**且额外覆盖「唯一性」与「owner==本机用户」两维，严于方案下限，不与方案冲突。真实 seam 可满足：host 本地玩家在 `Provider.onLevelLoaded(2)` 内以 `TransportConnection_Loopback.Create()` 注册（`Provider.cs:1500-1560`），`clients.Add` 先于 `InitializePlayer`，`Player._localPlayer` 已在 `InitializePlayerStart` 就位（`Player.cs:1546`、`:1642`）。**但这正是 T1a 未证的地方**——t3 探针实测 `BuildEligibility` 全 false，说明在该环境里至少一项不成立；必须逐条件打点定因（§7）。

### 4.2 会话/地图就绪门控

- MENU/SETUP：`OnLevelLoaded` 在 `level <= BUILD_INDEX_MENU` 时 `ResetBoundary`（`:162-167`）→ 清 pending/ready、递增 epoch，旧局值不进新局。
- GAME：postfix 先 `NoteGroupTableLoaded(mapName)`（`:255`）再尝试兑现（`:256-267`），即「原生命中在前、兑现判定在后」，与「先等组表就绪再 load」一致。
- 地图：仅 `ELevelType.SURVIVAL` 参与（`:384-390`），非 survival 关卡不延后、不发布。
- 会话边界三源：MENU（`:165`）、`Provider.onServerShutdown`（`:169-172`）、`Player.onPlayerDestroyed`（`:174-193`）。

### 4.3「延后一次」语义

- 组表未就绪时对同一实例/同 epoch/同地图重复请求返回 `Deferred` 但不二次排队（`GroupStateRecoveryCore.cs:157-168`）；组表就绪后 `PassThrough` 并计入 `_nativeLoadExecutions`（`:151-155`）。
- `ExecutingDeferredLoad` 使被授权的 `quests.load()` 递归回到前缀时直接放行（`:212-213`），避免自我拦截。
- `ExecuteDeferredLoad` 取走 pending 即置空（`:277-278`），token 不符则丢弃（`:285-291`），Stale 时清空（`:257-262`）。
→ 语义成立：「仅本地 Host、在组表就绪后、只执行一次」。

### 4.4 缺口（本节的实质问题）

**(a) 非组 Quests 就绪被改变且未处理。** 延后 `load()` 后，`InitializePlayer` 尾部 `onFlagsUpdated?.Invoke()` 以空 `flagsList` 提前触发（`PlayerQuests.cs:2597-2601`）；deferred `load()` 读完 flags/quests 后，补丁只发布组状态（`:305`、`:314-330`），**不重发 flags/quests 事件**。架构 §4.3-4 把这条列为必须解决项（same-fixture + SDK 显示/flags 回归），实现既未处理、也未在报告中登记为 UNKNOWN。这是本票除 T1a 之外唯一一条正面违反架构约束的问题。

**(b) `AbandonPending` 无生产调用点。** `grep` 显示只存在于 `GroupStateRecoveryCore.cs:214`，补丁侧从未调用；实际清理靠 MENU/shutdown/destroy 三事件。方案文字「MENU/断连/销毁/新局清 pending」中的「断连」因此只是近似覆盖（Host 的「断连」等价于销毁/关服，可接受，但 Bug3-05 测的是生产不会走到的 API，证据强度被高估）。

**(c) save 门只靠 SDK 阅读。** 见 §3 最后两行。

## 5. 负控核对（Guest / 单机 / dedicated 是否确实不干预）

- 生产分支：`Prefix_Load` 在 `!IsEligible` 时 `return true`（放行原生，`:214-215`）；`Postfix_GroupTableLoaded` 另有独立三条件早退（`:246-253`）。异常路径也回落原生（`:231-235`）。→ 行为上「不干预」成立。
- 可失败测试：Bug3-08/09 对 `GroupRecoveryEligibility` 的 12 种变异逐项断言 `IsEligible==false`，**可失败**；Bug3-03/04/07 覆盖新局/换图/旧回调不复用，**可失败**；Bug3-01/02 覆盖一次延后与正确顺序直通，**可失败**。
- **不足**：没有任何测试断言「生产 `Prefix_Load` 真的调用 `BuildEligibility` 并据此放行」（无 `CountMethodCalls(Prefix_Load, BuildEligibility)==1` 之类的 IL/行为断言），也没有 dedicated/Guest 进程内的实测（本机无法起 dedicated）。即负控停在纯数据结构层，未穿透生产 seam（F4）。R3 的「门控与实际注册目标有证据」只满足了后半（Bug3-11/12 证明注册到 `PlayerQuests.load` 与 `GroupManager.onLevelLoaded` 且 owner/original 正确）。

## 6. 缺口清单（findings）

| ID | 严重度 | 问题 | 必需修复 |
| --- | --- | --- | --- |
| F1 | blocker | **T1a 决定性自动化判据未交付**。现有 12 条测试不驱动真实存档与真实组件顺序，无「基线 none/MEMBER → 仅换候选 DLL → α/OWNER」对照；t3 自报端到端未复现且差异未定因。 | 在续修轮补齐 same-fixture 证据：继承 run-10 真实存档，按 `MENU → Player.InitializePlayer → PlayerQuests.load → GroupManager.onLevelLoaded(GAME)` 顺序回放；先断言基线 none/MEMBER，再仅替换候选 DLL 断言 α/OWNER；并先出「`BuildEligibility` 各条件在真实 seam 下的逐项观测表」定位探针 True 与门控全 false 的差异（若属门控过严，须回 gpt-architect 复议而非放宽判据）。 |
| F2 | high | 延后 `load()` 使 `onFlagsUpdated` 以空 flags 提前触发，恢复后不重发 flags/quests 事件；`architecture-v2.md` §4.3-4 明文要求该项在 same-fixture 与 SDK 显示/flags 回归中解决，实现未处理亦未登记 UNKNOWN。 | 二选一并留证：①在 `ExecuteDeferredLoad` 之后以原生事件/字段路径补发 non-group Quests 就绪（不得引入自定义频道、不得二次 `InitializePlayer`）；②若判定该提前触发无外部可观测影响，须给出 SDK 侧调用点/订阅者取证 + same-fixture 回归，并在报告中显式登记为已知偏差。 |
| F3 | medium | 「pending 期间不得以 none 覆盖存档」是空断言：Bug3-10 的 `save==0` 只统计 `ExecuteDeferredLoad` 内部，未验证原生 `wasLoadCalled` 门，也未验证 pending 窗口内原生 save 为 no-op。 | 增加可失败断言：pending 存在时 `wasLoadCalled==false`（或直接触发原生 `save()` 后校验存档字节/mtime 未变），并在 T1a 夹具中覆盖「延后后立即退出」路径。 |
| F4 | medium | 负控只测纯结构体，未穿透生产 seam：无 `Prefix_Load → BuildEligibility → 放行` 的 IL/行为断言；dedicated/Guest 无实测。 | 增加短路断言（Bug3-13）：`Prefix_Load` 对 `BuildEligibility` 的调用计数==1，且在非合格输入下 `GroupLoadDecision`/返回值为放行；对 dedicated 分支至少给出「`Dedicator.IsDedicatedServer==true` 时 Postfix 立即 return」的 IL 或路径断言。 |
| F5 | low | 「空存档/无合法组走原生默认」无测试，只有 `:318` 守卫。 | 增加一条夹具：空 `Quests.dat`（或无 `knownGroups` 命中）下 deferred load 执行一次、`isMemberOfAGroup==false`、`changeRank` 调用 0，且不产生任何组写入。 |
| F6 | low | `AbandonPending` 无生产调用点，Bug3-05 测的是生产不会走到的 API。 | 要么把「断连」接到真实事件并保留测试，要么在报告中把该条证据降级为「核心状态机性质」，不得作为退服清理已闭合的证明。 |

## 7. t3 自报端到端 UNKNOWN 的可判性与归属

分三层，结论不同：

1. **T1a（自动化）属于本票可判范围 → 是验收缺口，不是范围外。** 它不需要真机、不需要 Steam 联机，只要求在同一进程内按真实组件顺序回放真实存档并做「只换 DLL」对照。t3 以「探针无法复现」结案不成立，因为探针失败本身就是待定位的实现问题（可能是门控条件在真实 seam 下不成立，也可能是探针环境缺 `Provider`/`HostManager` 真实状态）。**关闭票**：本 Bug3 实现票的续修轮（t3 round 2 或 Captain 新建「Bug3 T1a 同 fixture 证据」票），并在关闭前提交逐条件观测表。
2. **T1b（真机 1H+1G）不属本票可判范围 → 由后续真机门禁票关闭。** 当前任务图（t1..t7）内**没有**这张票，需 Captain 新建；建议该票一次性覆盖 T1b/T2b/T3b/T4b（同存档菜单重进 ×2、完整进程重启 ×2、B 入场前窗口 α/OWNER、Guest 成员表与退队、双端 ID 呈现）。
3. **T2a/T3a/T4a 属独立实现门禁，当前无任何票覆盖 → 需 Captain 建票**（建议「Bug3 客机侧恢复与退队/呈现」：Guest 重连后本地 group/rank、真实退队入口、UI 呈现端点），且必须等 T1 关闭后按 §6.1 独立推进，**不得用 T1 Green 连带关闭**。

补充：t3 报告的「探针反射 True vs SPF 内部 `BuildEligibility` 全 false」必须先定因再谈修复。若因是门控条件过严（例如 `MatchingLocalClientCount==1` 在真实 host 早期为 2，或 `Allow_Dynamic_Groups` 默认关闭），则属于**方案层缺陷**，应回 gpt-architect 复议，而不是在实现里放宽条件绕过。

## 8. 未做事项（边界声明）

- 未重跑 `dotnet build` / `WhitelistTests`（引用 Captain 复跑结果，见 §0）。
- 未评审命名、复杂度、注释密度、日志风格（t6 工程轴）。
- 未修改任何源码、未改 `.agent-teams/` 状态、未提交。

## 9. 证据引用清单（file:line）

- 补丁：`P2PGroupStateRecoveryPatch.cs:57-77`（手动登记）、`:98`（类型断言）、`:122-155`（三族原生事件订阅）、`:162-167`（MENU 重置）、`:169-172`（关服重置）、`:174-193`（销毁丢弃 pending）、`:208-236`（load 前缀）、`:242-273`（组表后缀）、`:275-306`（单次兑现）、`:314-330`（原生发布）、`:333-362`（门控）、`:364-381`（唯一性计数）、`:384-390`（SURVIVAL 地图）、`:392-398`（实例 token）。
- 状态机：`GroupStateRecoveryCore.cs:47+`（`IsEligible`）、`:116-124`（`ResetSession`）、`:133-137`（`IsGroupsReadyFor`）、`:143-175`（`OnLoadRequested`）、`:182-203`（`TryTakeDeferredLoad`）、`:204-212`（`OnPlayerDestroyed`）、`:214`（`AbandonPending`，无生产调用点）、`:225-232`（执行期标记）。
- 原生 SDK（`.scratch/bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/`）：`Unturned/Player/PlayerQuests.cs:307`（`isMemberOfAGroup`）、`:443-466`（`ReceiveGroupState` 无条件刷新）、`:485-488`（`changeRank` 仅 `InvokeAndLoopback`）、`:499-505`（`ServerAssignToMainGroup`）、`:507-524`（`ServerAssignToGroup`，`members++`）、`:2579-2606`（`InitializePlayer`，含 `onFlagsUpdated`）、`:2648-2650`（`load` 置 `wasLoadCalled`）、`:2675-2688`（读 `groupID`/`groupRank`）、`:2870-2875`（`save` 门）。
- 原生宿主：`Player/Player.cs:1546`、`:1614-1646`（本地实例就位早于 quests 初始化）；`Provider/Provider.cs:1500-1560`（host 本地玩家 loopback 注册先于 `InitializePlayer`）；`Managers/GroupManager.cs:164-181`（组表重建与 `load()` 同方法体）；`Level/Level.cs:31-33`（`BUILD_INDEX_GAME=2`）。
- 测试：`WhitelistTests/Evidence/Runtime/P2PGroupStateRecoveryRuntimeTests.cs`（273 行，Bug3-01..12）、`WhitelistTests/Program.cs`（+14 行登记）、`SteamP2PFriends.csproj`（+2 行 Compile）、`Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationAuditModules.cs`（+11 行 `RegisterManual` 接线，异常置 `_registrationStageFailed=true`）。
