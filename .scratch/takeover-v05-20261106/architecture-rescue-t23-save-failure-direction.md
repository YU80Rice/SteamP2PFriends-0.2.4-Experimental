# T23 救援裁决：保旧存档的失败方向与有界第4轮

2026-11-06／gpt-architect。承接 [t19 实测 F1/F2](review-eng-t19.md#L71-L85) 与 [T22 裁决](architecture-decision-t22-flags-and-retired-lease.md)，不重做事实调查，不在本票修改生产。

## 1. 唯一选型与不可让步的优先级

**选择①：请求一次有界第4轮，修复 F1/F2 并并入 T22 回退，再对最终字节走双轴。不是选择②，也不是给 Captain 留 A/B/C 选择题。** 第4轮实施仍须用户明确授权；本裁决提供技术选择，不替代平台/用户的轮次授权。未获授权时停在 escalated，当前候选不得出包。

**失败方向必须是少执行：保留旧 Quests.dat 优先于兑现本局 Quests 进度；写坏默认值/半填列表、把未就绪导致的 Nil/MEMBER 持久化不可接受。** 本局某个失败恢复实例可以进入显式 `SaveBlocked/Degraded`，不保存该实例本局 Quests 进度，但不能静默伪成功。其他角色、其他存档、其他已成功加载实例不连坐。

优先级固定：①不破坏既有文件/成员身份；②不读未就绪组表、不重复原生 load；③成功加载后保持正常保存；④只在安全前提齐全时保证进度落盘。**“必然释放”只能是到期限后得到“安全兑现”或“明确阻断”的结果，不能解释为无条件执行一次 load/save。** 成功租约 load 恰好一次；失败路径允许0次，已进入后失败至多1次，不能为了满足“恰好一次”强行运行。

第4轮值得继续：F1 有已实测的确定性破坏路径，F2 与原Bug同源；授权门、写入门及相关反例可在现有接缝验证，不需重写存档格式/组算法。单撤 save 兜底不能堵住同租约二次请求的 ForcedPassThrough，故不选②。安全前提永不到达时“不保存该实例本局进度”的边界可以登记，但不能以此带着 F1/F2 出包。

## 2. 函数级最小修改清单

行号按 t19 绑定的原字节；修后按函数定位，不要求保留旧行号。

| 接缝 | 必须改动与失败返回 |
|---|---|
| [Core.IsGroupsReadyFor:237–241](../../Core/Patches/P2PGroupStateRecovery/GroupStateRecoveryCore.cs#L237-L241) | 改为internal供glue调用，按§3判定当前epoch/非空map；不以成员数大于0判就绪 |
| [Core.TryTakeDeferredLoad:294–316](../../Core/Patches/P2PGroupStateRecovery/GroupStateRecoveryCore.cs#L294-L316) | epoch/map/token校验通过后、ClearPending/MarkClaimed之前检查Ready；不Ready返回新增 `DeferredLoadOutcome.NotReady`，out token=0，不Claim、不增加load授权，不执行原生load |
| [Core.OnLoadRequested:255–275](../../Core/Patches/P2PGroupStateRecovery/GroupStateRecoveryCore.cs#L255-L275) | 同租约重复请求且未Ready时仍返回Deferred，保留同一pending与原账，不ClearPending、不ForcedPassThrough、不重新Begin计数；不得用重复次数代替Ready |
| [Patch.Prefix_Load:498–548](../../Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs#L498-L548) | 对实际延后的唯一localHost实例先提交§4写入门再返回false；已失败/放弃的同key或加载/通知重入不再原生load。已确认目标且未Ready的提交/日志异常，回滚pending后仍return false并SaveBlocked，不再catch→return true。无法确认且从未接管的非目标仍原生放行 |
| [Patch.Prefix_Save:556–599](../../Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs#L556-L599) | **先看实例写入门，再看HasPending/资格**。已接管而没有完整成功证明时默认false；仅当前同key pending且Ready时允许一次受控load，再由完整成功证明决定是否true。任何已接管目标的授权/执行/日志异常、重入保存、partial failure均false；不保留catch→true |
| [Patch.ExecuteDeferredLoad:638–706](../../Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs#L638-L706) | 执行前再验身份/Ready与一次性授权；进入时门仍Blocked；只有原生load正常返回且同lease仍有效才设置Complete证明。抛错/取消保持Blocked，不重试；Complete须先于通知和日志提交。通知抛错不撤销真实数据完整证明 |
| [GroupTable Postfix:605–630](../../Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs#L605-L630) | 真实原生GROUP生命周期正常返回、native表对象存在后才NoteGroupTableLoaded。未Ready不授权；合法空表可Ready。正常兑现路径保留，不提前调用GroupManager.load |
| [PlayerToken/PlayerTokens:35–42](../../Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs#L35-L42)、[ResetBoundary:456–463](../../Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs#L456-L463) | 利用现有弱实例关联承载§4最小完整性证明，不用单个全局Ledger的State代替。Reset不擦除活对象的不安全标记，也不能把“账已退役但load曾成功”误判成“从未成功” |

save期限的具体分流：

- 无受控恢复门（其他实例/Guest/单机/dedicated从未被本模块延后）→return true，保留原生语义。
- 已Complete的同实例数据→return true，原生正常save；通知失败不能单独禁止它保存。
- 该目标正在受控load/通知重入、没有Complete→return false，不能因ExecutingDeferredLoad而放行save。
- 当前pending、身份匹配且§3 Ready→Claim一次、执行原生load；成功且租约未变则Complete→true；失败/不明→false。`HasPending`清空不改变这个结果，后续save仍检查同实例证明。
- 当前pending但未Ready→**不调用load**，本次save false；将该租约以 `SaveBlocked:GroupsNotReadyAtDeadline/Degraded` 明确终止并撤销pending，保留实例Blocked证明。后到GROUP回调不复活该终态；该失败实例本局不自动恢复，正常退出/新局以新key重建。不是无限静默pending，也不是到期限就冒险读盘。

日志/诊断只做可见性，不影响返回值；阻断先提交状态再写Error（注明该实例本局Quests不保存、旧文件保留、正常退出并重启恢复）。诊断写入再抛错也不能翻成true。不加弹窗框架/踢人/自动存档修复器。

## 3. Ready 的编译级谓词与空表语义

Core已有字段直接使用，方法从private收紧为以下internal实现（源码规格，不是本票改码）：

```csharp
internal bool IsGroupsReadyFor(string mapName)
{
    return !string.IsNullOrEmpty(mapName)
        && _groupsReadyEpoch == _epoch
        && string.Equals(_groupsReadyMap, mapName, StringComparison.Ordinal);
}
```

Patch只读核实真实表对象，不查成员数；当前ABI [knownGroups是private Dictionary](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Managers/GroupManager.cs#L30-L32)。以现有Harmony反射设施缓存 `FieldInfo`，登记时解析/类型校验，失败按既有E5注册失败，不猜字段：

```csharp
private static System.Reflection.FieldInfo _knownGroupsField;

private static bool NativeGroupTablePresent()
{
    return _knownGroupsField != null
        && _knownGroupsField.GetValue(null) is System.Collections.IDictionary;
}
// 同lease、同实例/审批门先成立；下式只负责Ready：
bool readyForNativeLoad = Core.IsGroupsReadyFor(mapName)
    && NativeGroupTablePresent();
```

`_knownGroupsField = AccessTools.Field(typeof(GroupManager), "knownGroups")` 仅只读，不能创建/填表。反射异常在已接管目标上按NotReady/Blocked处理，不放行；Core内TryTake至少必须自带IsGroupsReadyFor校验，Patch在所有受控执行前再核实native表存在。不要把反射放进纯Core状态机。

- mapName=null或空→false；没有当前epoch正常GROUP完成记录→false，即使残留旧表有成员。
- knownGroups=null/字段不可取/类型不符→false；**非null空Dictionary合法**，无需Count>0，也不要求保存的alpha一定存在。
- 当前正常GROUP完成、同epoch同map、真实表非null→Ready；合法存档中某个组确已不存在时，原生默认组判断仍保留。

本裁决选择“**表未Ready时禁止授权**”，不是“先授权、先load，最后拦save”。因此未就绪时根本不进入 [原生getGroupInfo/loadMainGroup](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerQuests.cs#L2811-L2850)，不会以空表缺席为由把alpha降到Nil/MEMBER。组表真正Ready后合法组失效的原生降级不被禁止；不硬保一个无效组。

## 4. 写入安全证明：键、退役与清除时机

**键为 `(PlayerQuests对象引用身份, recoveryEpoch, instanceToken)`，并绑定mapName。** 使用现有ConditionalWeakTable按对象引用定位，槽内只保存当前/最近受控lease的key、`Managed`、`NativeLoadCompleted`和阻断原因。可用小enum等价表达，但不得以wasLoadCalled、全局Ledger.State或是否存在pending代替NativeLoadCompleted。

- 第一次真正延后该对象的load之前写 `Managed=true, NativeLoadCompleted=false`。这是单实例写屏障，不是恢复算法或全存档历史。
- 仅本key一次原生load正常返回、授权前后Ready/lease均有效才置true；并在真实load成功账提交后、通知/日志之前完成。load首行wasLoadCalled=true不算成功。日志失败不得把已经成功的原生load改记LoadFailed；通知失败亦不得撤销数据完整证明。
- Prefix_Save先用对象引用查槽：Managed且NativeLoadCompleted=false→false，即使Core.HasPending=false、账已Reset、P2P资格后来变化，均不能让同一个partial对象绕过。无槽/从未接管的对象仍原生放行。Managed且NativeLoadCompleted=true的数据不因账本退役/NotifyFailed被当成从未load成功。
- 新局新实例或明确开始的新lease使用新epoch/token key，先置Blocked，自己的正常load成功后变Complete；绝不能继承旧key的true，也不能把旧失败槽全局套给第二个正常存档。新lease仅在当前真实localHost的原生初始化load请求经完整Eligibility判定后建立，不由save/watchdog/后到旧callback创建。
- 若确有同一PlayerQuests引用跨epoch复用，在上述有效新初始化入口为该引用分配新的token generation（更新现有PlayerToken.Value，不能沿用旧回调持有的token），提交新key+Blocked后才进入新load；旧Complete证据只能说明旧key成功，不可给新key开门。save、通知或超时路径都不得自行轮换token来绕过Blocked。旧捕获token先经ExecuteDeferredLoad身份校验拒绝，不能借新epoch伪装初始化。

| 边界 | 证明处理与保存结果 |
|---|---|
| 正常save成功后 | **不清除Complete证明**；保留供后续同对象周期save，后续允许保存但不再load/补发。save本身抛错不自动重读或重试写盘，由原生保存错误处理 |
| ResetSession/MENU/Shutdown | Core照T22清活动账并记退役元数据。实例证明不随之清零：Complete+旧epoch=“成功后退役”，不是从未成功；Blocked+旧epoch继续阻止该旧活对象迟到save。只撤销旧授权/待执行租约，不自动再load |
| 玩家销毁回调 | 作废pending/内部授权，不立即删除仍可能被持有引用并调用save的Blocked槽；Complete槽亦保留已完整加载事实。引用真正不可达后由ConditionalWeakTable回收，不建永久强引用字典 |
| 同实例明确新lease | 使用新key重新置Blocked→成功后Complete；旧callback必须在授权前被epoch/token校验拒绝，不能把旧Complete认证给新key |

所以“本局从未完整load成功”和“上局成功但账已退役”可以区分：前者false、后者true只证明该实例已完整加载，**不借它授权任何新租约load或恢复到新对象**。原生save自身原有的有效性判断仍运行；本模块不额外制造保存调用。此最小活对象安全证明不是保存旧RealLoadSuccess/NotifyAttempts全账，**不推翻T22的退役元数据裁决**。

只在ExecuteDeferredLoad后写 `return Ledger.State != LoadFailed` 不是完整修复：pending已清，第二次save从旧首行放行；Reset后State还会变None。必须让实例屏障先于这些短路，才守住“少执行”方向。

## 5. 用例改写与可变红的保存证据

不得只把原断言加一个 `!` 就交付。当前 [Bug3-39:935–962](../../WhitelistTests/Evidence/Runtime/P2PGroupStateRecoveryRuntimeTests.cs#L935-L962) 必须继续**真实调用生产Prefix_Save/受控执行接缝**，并先构造Ready、真实native表对象存在的受控fixture，确保会进入load失败分支，不被NotReady提前拦下而假测。

| 编号 | 固定改动/新增断言 |
|---|---|
| **Bug3-39 改写** | `Assert(!Prefix_Save(quests), "load抛错必须拒绝原生save，保留旧文件")`；loadAttempts=1、RealLoadSuccess=0、LoadFailed+Degraded、NotifyAttempts=0。第二次save仍false且loadAttempts仍1，不能因pending已空放行 |
| **Bug3-32 改写** | 保留“save期限兑现一次”的正控，但前置必须为本epoch同map表Ready+非null。成功后Prefix_Save=true，重复save可true但不再load。删除“表未Ready也必须强制load/save”的旧期望 |
| **Bug3-15/29–31相关断言校准** | 保留提交回滚/生命周期重置的既有变异检测。已确定目标且未Ready时catch回滚不再期待native放行；同租约重复请求不再期待ForcedPassThrough。仍须证明无悬空pending、旧回调不兑现，不能通过删除用例消红 |
| **Bug3-41 新增：原生写段不可达** | 在真实生产Prefix_Save接缝外，用有副作用的save-writer哨兵：只有生产返回true才可进入它；失败load fixture先模拟原生已置wasLoadCalled=true且内存被部分改动。失败后原生写段进入数必须0；在隔离临时文件上保存前后字节/mtime亦应一致。不得用独立MaySave假函数替代生产前缀 |
| **Bug3-42 新增：Ready授权否定** | epoch/map/token正确但未Ready、map=null/空、native表=null分别不能Authorized，loadEntry=0、save=false。正常GROUP完成的非null空表允许Ready；旧epoch残留非空表不允许。未Ready的重复load请求也不运行原生尾部 |
| **Bug3-43 新增：正控与第二存档隔离** | A正常完整load→Complete→多次save允许且总load=1；Reset活动账后A的成功证明仍可辨。第二局新实例B/新key以Blocked起步、自己的正常load后save允许，不能继承A许可，也不能因Ledger新账0永久拦B |
| **Bug3-44 新增：旧脏对象持久屏障** | A partial failure后pending清空，再Reset/资格切换/日志抛错，A保存仍false且写段进入0。新实例B不被A阻断；销毁回调后仍持有A引用并迟到save也不能删除屏障后放行 |
| **Bug3-45 新增：通知失败不等于数据失败** | 正常GROUP路径实际load接缝成功，单次multicast首抛→NotificationFailed；同实例数据Complete保持，save允许、无第二次load/通知。同时符合T22 Bug3-33首抛后后续订阅者不执行 |

Bug3-41 的最低可执行证明是：载入失败后断言生产前缀返回false，实际写段哨兵0，且已有临时文件内容未变；有原生fixture时再经真实Harmony save入口验证同一前缀确已安装/拦截。受控writer仅检验“会不会进入写段”，**不冒充原生River保存实现或T1a原生存档恢复Green**。不得对真实用户存档试破坏路径。

必须做反例变异：①把受控失败返回改true，39/41必须红于写段进入或文件变化；②删Ready判定，42必须红于授权/loadEntry；③恢复HasPending=false首行旁路，39第二次save/44必须红；④让Reset抹掉实例成功/失败证明，43或44必须红；⑤删安全兑现原生调用，32正控必须红。只跑“全部返回false”的实现会被32/43/45正控击穿。

## 6. 与 T22、既有禁止项及日志语义的一致性

**T22完整保留**：flags采用委托快照单次multicast，首抛中断、外层记失败、不重发；Bug3-33按T22改写。退役账只需可核验标识，不新增完整历史字段。本票增加的是仍可被调用save的活对象完整性位，不是恢复旧账计数。

`NativeLoadCompleted`只认证数据完整，不以flags投递成功为条件。普通GROUP兑现成功后的新增flags按T12/T22最多一次；save期限watchdog分支仍保持既有 `SkippedWatchdog/Degraded`、新增flags=0，不因本票偷偷改通知语义。若共用ExecuteDeferredLoad，明确传递执行原因区分GroupReady与SaveDeadline；save安全证明不依赖最终Ledger.State恰好叫LoadSucceeded。

成员计数不增加；成功路径原生load恰好1、失败至多1/未授权可0；`NotifyAttempts≤RealLoadSuccess≤1`、旧租约不能在新局兑现。这五组既有禁止项继续有效：

1. 不提前/重复调用GroupManager.load，不重排MENU/GAME原生生命周期。
2. 不二次完整InitializePlayer，不为partial failure重试原生load，不以wasLoadCalled伪造成功。
3. 不手写Quests/Groups解析/覆盖算法，不反射改wasLoadCalled或硬填alpha/OWNER/空列表来凑保存；不写回未验证默认状态。
4. 不用ServerAssignToGroup恢复身份导致members++，不删合法alpha、不全表清理、强踢或擅迁到beta。
5. 不放宽唯一真实P2P Host/审批/epoch门、不干预未接管的Guest/单机/dedicated；不伪造奖励/逐flag事件、不拆multicast或自动重发。

明确修订的只有旧“已确认目标未Ready时异常/重复也必须原生放行”策略：它与本次数据安全优先级冲突，**以T23为准**；旧E3回滚/不二次load的目标保留，不要求维持错误的fail-open返回值。T22的两项结论没有冲突，无须改需求冻结字节。

watchdog计数只在真正Authorized后记“兑现”；未Ready截止应记 `SaveBlocked/Deadline`，不能把授权尝试计成已load。元数据缺失/诊断异常对已接管目标是拒绝，不是全局禁存；Error含实例别名、epoch/map/token、原生load是否进入/正常返回、阻断原因，且通过现有诊断输出明确“本局该实例Quests不落盘、旧文件保留”。

## 7. 第4轮边界、限制解锁与出包结论

建议用户授权**仅一次有界第4轮**，修本文件清单加T22通知回退，复用既有接缝及F1/F2真实失败依据；不把原生存档事务重写、全游戏save包装、插件生态异常隔离、其他Bug实现塞进来。范围必要代价包括实例写屏障和相关旧用例改写，不能只贴“失败时return false”两行留下第二次save旁路。

第4轮须重新独立双轴绑定同一最终源码/测试/DLL指纹；完成39/41数据保护负控、42Ready否定、32/43/45正常保存正控及原有回归，再处理其余t19低级项的明确归属。自动循环上限不被本票重置；若用户不授权就停止且不出当前包，不改称选②直接交付。若本轮仍无法证明上述安全门，继续escalated，不自动第5轮。

可登记的失败限制仅有：

- GROUP就绪前触发save截止，租约以Blocked终止，该实例本局Quests不保存；解锁为正常退出后新实例/新epoch，组表Ready后一次原生load正常返回。旧key不自动复活。
- 原生load partial failure，该实例本局Quests不保存；解锁为查明IO/对象图原因，保旧文件、正常退出并在新实例重新初始化，不对半填对象重试。
- flags订阅者抛错导致本机部分投递，按T22记录；它不应阻止已经完整加载的数据正常保存，也不授本机表现PASS。

这些是明确、可见、数据不破坏的失败方向，**不是正常流程可以一直不落盘的验收豁免**。正常同存档重进/完整重启仍按原需求验收；T1a与实机T1b/T2/T3/T4未关闭的项照旧UNKNOWN，不被第4轮测试计数顶替。

**当前候选阻塞0.2.4.11-preview：F1/F2写盘/授权门和T22回退未按最终字节验证前，不出包。** 本T23仅交付救援裁决，不修改生产、不运行构建/游戏或测试，不单方面授予第4轮授权。
