# T21 规格轴复审（r3）：t18 修复轮对 t15 Findings 的关闭判定

- 审查票：t21（round 3），被审对象：t18（repair，收口 t15-F2..F5 + t16 工程轴 F2..F5）
- 审查轴：规格轴（是否按 requirements-v2.md / architecture-v2.md / review-eng-t3.md §7 做对），**不评风格、不评命名、不替对方改码**
- 取样时间：2026-11-06 17:39:02（源码）／17:36:48（构建产物）
- 本票判定范围（派工给定的 6 项）：F2 接线可判性、F3 load 抛错置 Degraded、F4 ResetSession 抹账、F5 订阅失败回滚、测试源码哈希订正、F1(T1a) 仍开。

---

## 0. 结论

| 项 | 判定 | 一句话 |
|---|---|---|
| F2 生产补发接线可判性 | **关闭** | 已加 IL 契约用例并经本轮变异实测可红 |
| F3 load 抛错置 Degraded | **关闭** | `MarkLoadFailed` + 显式 `MarkDegraded`，用例载荷==1 不重试 |
| F4 ResetSession 抹账 vs 矩阵⑤ | **关闭（附残留 O3）** | 退役元数据使"跨局计数=0"可核验；旧账其余字段仍不保留 |
| F5 订阅失败回滚 | **关闭（附残留 O2）** | 两条路径都先 `UnpatchOwned` + `UnsubscribeNativeEvents` 再抛 |
| 测试源码哈希订正 | **关闭** | t18 报告哈希 == 磁盘 `6ee1cdaf…`；`340899f9…` 无对应文件 |
| F1(T1a) | **仍开（本票不要求关闭）** | 40 条用例仍零真实存档同 fixture、零 none/MEMBER→α/OWNER 断言 |

**verdict = pass（仅限上述 6 项）。**

**本票 pass ≠ Bug3 Green。** 用户目标"同存档跨局主机身份恢复"的决定性证据仍缺（F1），T1b/T2a/T3a/T4a 仍是 UNKNOWN。另有两项不归实现者裁决的跨轴问题需 Architect 一句话定调：**O1（invocation list 拆分与 review-eng-t3 §7.3 明文相反）**、**O3（F4 字面差距）**。

---

## 1. 被审字节绑定（复审结论只对这些字节有效）

| 路径 | 字节 | sha256 |
|---|---|---|
| `Core/Patches/P2PGroupStateRecovery/GroupLeaseLedger.cs` | 6097 | `338dc8d927da9e12785d1c20e882acd700fcfa36779664e3ab4a1ac4d59798b5` |
| `Core/Patches/P2PGroupStateRecovery/GroupLoadPlanner.cs` | 1344 | `a45533850f31bd96f3d63e39c56b2e5a084fc3f782b1ef984521d39fe64afb0f` |
| `Core/Patches/P2PGroupStateRecovery/GroupStateRecoveryCore.cs` | 20042 | `a3ca6aa0ab3725397e218579ee57fb68465cb83d4298706988adfb6dd4e6f819` |
| `Core/Patches/P2PGroupStateRecovery/P2PGroupRegistration.cs` | 1790 | `5b0c4e9ba4c87cbeb7363b3846b27f26f464e8acad51c25bc07831d5fbe04ea4` |
| `Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs` | 45224 | `3afe988c4a83a7dcb73d954cf87c584714f81b53db2b20459fc77cd5987897e6` |
| `SteamP2PFriends.csproj` | 27097 | `c4f4f7f52587dfe1a0c27f74a485a2d75f17c54e7e41fab701255aa396564b04` |
| `WhitelistTests/Evidence/Runtime/P2PGroupStateRecoveryRuntimeTests.cs` | 56822 | `6ee1cdaf88fccfdacdb73ae56485e19acd138268f5998b6aab5d51f0065826ff` |
| `WhitelistTests/Program.cs` | 130629 | `000293d72f5239adb1fdc83ad4276881fe559ccae20e4a01fcd3d296c4d29b84` |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationAuditModules.cs` | 11924 | `5d21c3a5d7f1e8f089c813dab5ecc425ac704a5605e4517c5958516fac656968` |
| `bin/Release/SteamP2PFriends.dll`（送审构建，17:36:48） | — | `178a73063e29243fed114b8ce4f71e8a508d1ac405cf15b8947b6db71ff8f5d3` |
| `WhitelistTests/bin/Release/SteamP2PFriends.dll` | — | 同上（手工 cp，与 `WhitelistTests/SteamP2PFriends.WhitelistTests.csproj:42-43` HintPath 指向一致） |

- `AuditModules` 哈希与 t14 轮完全相同（`5d21c3a5…`）→ 挂接入口未被本轮改动，仍是 `ApplyV2AuditFixPatches_Core` 内一次 `RegisterManual`。
- `git diff --cached SteamP2PFriends.csproj` = 纯追加 9 行 `Compile`（本轮 5 个 `P2PGroupStateRecovery/*.cs` + Bug1 的 4 个文件属 t7 票），无删除/重排。
- 5 个新生产文件与测试文件处于 `A`（已 add），csproj/Program.cs 为 staged `M`，不存在"该交付却仍是未跟踪"的缺口。

---

## 2. 独立复跑与变异实验（本轮实跑，非引用他人结论）

### 2.1 基线
`WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe`（送审 DLL `178a7306…`）：
`=== Final Result: 686/688 PASS (Failed: 2) ===`，Bug3-01..40 全 PASS，唯一两项失败为 GBA01/NIL01
（`System.TypeInitializationException: SDG.Unturned.SteamPlayer → NetReflection → ReflectionTypeLoadException`，属无 Unity 运行时环境缺失，非本轮回归）。

### 2.2 F2 的"可判性"实证（本票最关键的一步）
把 `P2PGroupStateRecoveryPatch.cs:705` 的 `NotifyFlagsUpdated(quests, token, mapName);` 注释掉（其余不动）：

1. `dotnet build SteamP2PFriends.csproj -c Release` → 0 错 0 警（私有未用方法不告警，故必须靠用例而非编译器兜底）；
2. 产物 DLL（sha256 前缀 `529fc3c0…`）拷入 `WhitelistTests/bin/Release/` → 运行：`685/688 PASS (Failed: 3)`；
3. **唯一新增红 = `Bug3-38 Production Notify Wiring Is Judgeable (spec-F2)`**，失败信息 `生产路径必须恰好接入一次 NotifyFlagsUpdated，实际=0`；基线两条环境失败不变。

→ 上一轮的 F2（删掉生产补发后 28 条用例仍全绿）**已经真实关闭**：现在删掉接线会立刻变红，且红的正是该缺陷本身，不是别的用例连带。

### 2.3 还原证明
备份回写 → 源码 45224B / `3afe988c…`（**与送审字节完全一致**）→ 重建 0 错 0 警 → DLL `178a7306…`（**与送审 DLL 完全一致**，确定性构建）→ cp → 运行 686/688、Bug3 40/40、打印 `SPF_DLL_SHA256=178a7306…`。

---

## 3. 六项逐条判定

### 3.1 F2 生产补发接线可判性 —— 关闭
- 生产唯一调用点：`P2PGroupStateRecoveryPatch.cs:705`（`ExecuteDeferredLoad` 内，位于 `:698-702` load 后二次租约复核与 `:706` 组发布之间）。全文件 grep 仅此一处。
- 用例：`Test_ProductionWiring_ExecuteDeferredLoadCallsNotifyFlags`（`P2PGroupStateRecoveryRuntimeTests.cs:922-929`），`IlContractProbe.CountMethodCalls(execute, "…P2PGroupStateRecoveryPatch", "NotifyFlagsUpdated") == 1`。
- 可失败性由 §2.2 变异实测坐实（不是"断言存在即认为可判"）。
- 另：切断了 `E4` 的"通知在组发布之前"顺序契约——`:705` 通知、`:706` 发布，未倒置。

### 3.2 F3 原生 load 抛错必须置 Degraded —— 关闭
- `P2PGroupStateRecoveryPatch.cs:678-684`：catch → `NoteDeferredLoadFailed("native-load-threw")` + 明确 `not retrying` + return（不重入 `ExecuteDeferredLoad`，不给第二次 load）。
- `GroupStateRecoveryCore.cs:414-422`：`NoteDeferredLoadFailed` 内 `_ledger.MarkLoadFailed(reason); _ledger.MarkDegraded("load-failed:" + reason);`。
- `GroupLeaseLedger.cs:101-105` 的 `MarkLoadFailed` 本身仍只置 `LoadFailed`；**Degraded 由 Core 显式补齐**，最终两要件同时成立，符合矩阵行⑩"LoadFailed/Degraded"。
- 用例 `Test_LoadThrows_DegradesAndDoesNotRetry`（`:935-963`）：注入 `InvalidOperationException("t18-native-load-threw")`，断言 `loadAttempts==1`、`RealLoadSuccess==0`、`State==LoadFailed`、`Degraded`、`!CanNotify`；本轮基线运行 PASS（Bug3-39）。
- 与 E3 口径统一：prefix 回滚走 `AbandonPending` → `MarkAbandoned`（同样置 `Degraded`）；两条失败路径现在在"是否可观测降级"上一致，不再一条有一无。

### 3.3 F4 ResetSession 抹账 vs 矩阵⑤ —— 关闭（附残留 O3）
- `GroupStateRecoveryCore.cs:211-228`：`ResetSession` 在 `_ledger.Reset()` **之前**记录 `_retiredLeases++`、`_lastRetiredToken`、`_lastRetiredEpoch`（仅当旧账 `Token != 0`），新增只读属性 `RetiredLeases/LastRetiredToken/LastRetiredEpoch`（`:178-180`）。
- 用例 `Test_ResetSession_RetiresOldLeaseBeforeReset`（`:969-982`）断言 `RetiredLeases==1`、`LastRetiredToken==86`、`LastRetiredEpoch==epochBefore`、`Epoch==before+1`、`Ledger.Token==0`、`State==None`（Bug3-40 PASS）。
- 因此矩阵⑤"旧 token 跨局计数=0"现在是**可核验的 0**：不再是无从区分的"账被清空"，而是"上一租约确实存在并已被显式退役、新账从零开始"。原矛盾解除。
- **残留 O3**：旧账除 token/epoch 外的历史值（NotifyAttempts/RealLoadSuccess 等）仍未保留，`Test_FlagsNotify_OldTokenAfterSessionReset_NoNotify:329` 仍以 `Ledger.Token != 84L`（"账本不得保留上一局 token"）为期望。与 review-eng-t3 §7.4 行⑤"旧账保留历史值不新增"的字面仍有差距。**不判缺陷**：t15-F4 的两种合法出口之一是"由 Architect 明确改写行⑤措辞"，而"退役元数据"是否足以满足行⑤属于措辞裁决，不由实现者决定。

### 3.4 F5 订阅失败须回滚已装 patch —— 关闭（附残留 O2）
- `P2PGroupStateRecoveryPatch.cs:165-176`：`SubscribeNativeEvents()` 抛错 → `UnpatchOwned(harmony)` + `UnsubscribeNativeEvents()` + `LifecycleSubscribeFailed(rollbackStatus=rolled-back/rollback-failed)` 再抛。
- `:178-186`：订阅标志缺失（`VerifyCurrent` 路径）→ 同样先回滚再抛。
- `:188-192`：只有全部成立才提交 `_harmony/_registered/_registeredHarmonyId/_registeredModuleMvid/State=Ready`——"Ready 是最后一步"的顺序未被破坏。
- `UnpatchOwned`（`:234-251`）只 Unpatch 本模块两个（现为三个）方法，异常内部捕获并返回 false，不会牵连其他模块补丁——符合 §7.5"只撤销本模块精确 patch"。
- 局限如实记录：该路径**无自动化用例**（测试无"订阅失败"seam），只能代码核验；这与 t15-F5 的 requiredFix 表述一致（当时要求的是代码层回滚，未要求 seam）。
- **残留 O2**：`:196-200` 只处理 `P2PGroupRegistrationException`；**`:201-207` 的通用 catch 仍不调用 `UnpatchOwned`**，因此若 `:154-156` 三处 `harmony.Patch` 中途抛错（非注册异常派生），已装前缀/后缀会留活，而 `State=Failed`、`_harmony=null`（后续无法清理）。触发条件很窄（目标已解析成功、补丁方法签名固定），影响是可能重复前缀 → 同一次 `load` 被两次 `OnLoadRequested`（第二次落入 ForcedPassThrough，恢复被跳过）。**不判 blocker**：属 F5 的边角未覆盖，非本轮引入，且需要 Harmony 在签名已核验的前提下抛错。

### 3.5 测试源码哈希订正 —— 关闭
- 磁盘：`WhitelistTests/Evidence/Runtime/P2PGroupStateRecoveryRuntimeTests.cs` 56822B / `6ee1cdaf…`。
- `team.json` 中 t18 报告引用的正是 `6ee1cdaf…`（t18 用真实字节，未沿用旧值）。
- `340899f9…` 全仓库只出现在**我上一轮的报告** `review-spec-t14-r2.md` 里，没有任何文件内容等于它 → 该哈希是 t14 轮的孤立中间态/笔误，已被 t18 的真实哈希取代。**订正成立。**

### 3.6 F1（T1a 同 fixture 真实存档 none/MEMBER→α/OWNER）—— 仍开
- 40 条用例（Bug3-01..40）中仍无任何一条读取真实存档、无 MENU→`InitializePlayer`/`load`→GAME 的真实组件顺序回放、无"同 fixture 只换候选 DLL 得 α/OWNER"的对照断言；`NewPlayerQuests()` 仍是合成对象，`EligibilityResolverForTests`/`MapNameResolverForTests`/`DeferredLoadInvokerForTests` 仍是注入 seam。
- t18 新增的 `Prefix_Save` 兜底（`Patch:556-599` / Bug3-32）降低了"GAME 后缀永不到来 → 本局进度静默丢失"的风险，但它是 **E1 安全阀**（走 `NoteWatchdogForcedRelease`、显式 `Degraded`），不构成 T1a 的对照证据。
- 依派工要求：**记录为仍开，且不得顺带记为通过**。关闭路径二选一（同 t15 报告）：建 T1a 专用实现票（真实存档同 fixture + 只换候选 DLL），或由 Architect 书面把 T1a 重定义为"宿主边界不可执行"的具体 UNKNOWN 并写解锁条件。

---

## 4. 既有判据回归（确认本轮修复未把 T1 契约做坏）

| 判据 | 复核方式 | 结果 |
|---|---|---|
| E4 补发条件（真实 load 正常返回 + 同 epoch/地图/实例 + payload 非空） | `Patch:698-702`、`:709-734`、`:815-822`；Bug3-18/23 基线 PASS | 保持 |
| 空存档/空 payload → 0 次且记 `SkippedEmpty` | `TryNotifyFlagsCore:740-813` 前置分支；Bug3-19 PASS | 保持 |
| 矩阵三行计数（0/1/1、0/0、1/1） | Bug3-20/21/22/28/39/40 PASS | 保持 |
| 非进程级一次、非玩家实例级一次 | Bug3-21（ResetSession 后新 token 再通知一次）+ Bug3-37（旧实例换新 token 仍按租约计） | 保持 |
| "恰好一次"（真实 load ≤1、补发 ≤1） | Bug3-24 IL（TryNotifyFlagsCore 内 load/setFlag/ServerAssignToGroup==0；ExecuteDeferredLoad 内 save/InitializePlayer==0）+ Bug3-15 + Bug3-32（重复 save 不二次 load） | 保持，且被新增的 save 兜底加强了 |
| 通知先于组发布 | `:705` 先、`:706` 后 | 保持 |
| 五禁止项 | 5 个生产文件 grep：`ServerAssignToGroup`/`ServerAssignToMainGroup`/`StreamReader`/`BinaryReader`/`FileStream`/`GroupManager.*.load()` **0 命中**；`InitializePlayer` 仅 `Patch:18-20` 注释；`GroupManager` 仅 `:144`（AccessTools 目标）与 `:293`（DeclaringType 校验）；生产 `quests.load()` 仅 `:674` 一处 | 全部未违反 |
| §4.3 门控 11 项（P2P host + server + 唯一真实 local host + loopback + 动态组开关） | `BuildEligibility:858-891` 改为构造 `GroupRecoverySessionFacts` → `Evaluate()/ToEligibility()`，判定仍集中在 `GroupRecoveryEligibility.IsEligible`（11 项合取 + `OwnerSteamId!=0 && ==ProviderUserId && 计数==1 && DynamicGroups`）。**是等价重构，不是放宽**：`HasOwningSteamPlayer` 为假时强制 `OwnerSteamId=0`、计数=0，比旧写法更严 | 未破坏 |
| Guest/单机/dedicated 不干预 | `Postfix:605-636` 三道前置 return；`Prefix_Load` 未过 `IsEligible` 即放行；`Prefix_Save` 以 `Core.HasPending` 为门（Guest 从不产生 pending） | 保持 |
| 会话边界清理 | `:418-423` MENU 重置、`:425-428` 关服重置、`:456-463` `ResetSession + _pendingQuests=null` | 保持 |
| 未宣称端到端 Green | t18 报告自述 UNKNOWN，未见越界宣称 | 合规 |

---

## 5. 待裁决项与 UNKNOWN（不由本票关闭）

### 5.1 需 Architect 一句话裁决（跨轴/措辞，不归实现者）
- **O1（medium）invocation list 拆分**：`Patch:771-790` 改为 `handler.GetInvocationList()` 逐订阅者 try/catch 并累计 `NotifyFailedSubscribers`（Bug3-33）。这与 review-eng-t3 §7.3 明文"**不私拆 invocation list 改变下游调用顺序/异常语义**……某监听者抛错后后续监听者可能未执行，这是部分投递风险"相反；而工程轴 `review-eng-t16.md:96-99` 的 F5 requiredFix 恰恰要求逐订阅者隔离。t18 是照工程轴做的，**属两轴冲突，不是擅自偏离**。需 Architect 书面追认（接受新语义：单个监听者抛错不再吞掉后续监听者，失败数入账、仍不整批重发），或回退单次 multicast。
  - 注：新语义方向上更接近"就绪通知必须到达"，未突破"≤1 次尝试、不自动重发"（Bug3-33 已断言第二次调用被拒）。
- **O3（low-medium）F4 字面差距**：见 §3.3。退役元数据是否足以满足 §7.4 行⑤"旧账保留历史值"，需 Architect 明确；若否定，则需改用 append-only 租约历史。

### 5.2 本轮补记的既有设计豁免（非 t18 新增，供 Architect 决定是否接受口径）
- **O4（low）**：`SubscribeNativeEvents:319-376` 在 Provider/Player 订阅遇 `TypeInitializationException`/`ReflectionTypeLoadException`（`IsEnvironmentTypeLoadFailure`）时标记 `*_SubscriptionUnavailable` 并**继续 Ready**；`VerifyCurrent:178-186` 也不把 unavailable 计为缺失。这是 t14 为"无 Unity 运行时的测试宿主"引入的兜底，真机不触发；但与 §7.5"任一必要原生生命周期订阅失败不得 Ready"的字面有出入。Level 订阅失败仍一律抛错回滚，MENU 会话重置因此始终有保障，实际风险有限。

### 5.3 UNKNOWN 与解锁条件
| UNKNOWN | 归属/解锁 |
|---|---|
| T1a 真实存档同 fixture none/MEMBER→α/OWNER | **F1，仍开**；T1a 专用实现票，或 Architect 书面重定义 |
| T1b 真机 1H+1G（菜单重进 / 完整重启 × 各两次） | 真机门禁票（同存档、B 入场前窗口内 Host ID==α 且 OWNER） |
| T2a 客机重连应用权威成员表 | 客机侧独立门禁票 |
| T3a 真实 Guest 退队驱动与两端断言（权威移除 + 本地 group=none） | 客机侧独立门禁票 |
| T4a 恢复后双方呈现的 group ID == α、退出后非同队 | 客机侧/UI 独立门禁票 |
| E1 残余：既无第二次 load、玩家又从未触发 save 的极端路径仍会整局跳过 load | 现有 `Prefix_Save` 已覆盖"有 save"的绝大多数路径；残余记为已知边界，由真机日志（`watchdog:save-prefix` 计数）观察 |
| VendorUI 脏标记 / 任务成就 UI 实际刷新 | 需 SDK 显示层观测，未做 |

---

## 6. 复审边界与未做事项
- 未评命名/复杂度/注释密度（另一轴负责）；未重跑构建基线（用 17:36 送审产物 + 本轮变异实验的两次自建）；未做真机/SDK 实机观测。
- 变异实验只做了 F2 一处（派工要求的"可判性"实证），完成后**已完整还原并给出双向哈希**（源码 `3afe988c…`、DLL `178a7306…`，与送审一致）。
- 本报告绑定磁盘字节；若 t18 之后又改字节，须重新取样复核。

---

**判定**：verdict = **pass**（本票 6 项）。**不代表 Bug3 端到端 Green**：F1 仍开，T1b/T2a/T3a/T4a 仍 UNKNOWN，O1/O3/O4 待 Architect 定调。

---

**报告字节自证**：本行之前的主体 = 17634 字节 / sha256 `f9f3ac027f212cd5b37ae52a7af55f37e8d7b57ca25cf9320ad18396cf0a54f5`；本行加入后为本票最终交付字节（最终大小/哈希由 t21 回执记录）。
