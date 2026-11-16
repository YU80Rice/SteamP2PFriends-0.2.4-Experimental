# 0.2.4.11 三遗留 Bug 根因裁决与最小技术方案 v2

> T2 架构工作产物；作者 gpt-architect，2026-11-06。**不是实现完成、不是实机 Green、不是自审通过**。提交团队后仍须独立 Standards/Spec 双轴审查。范围为裁决与方案，不修改生产源码、历史票、存档或发布目录。

## 1. 约束、证据等级与总裁决

需求唯一新基线为 [requirements-v2](requirements-v2.md)，SHA-256 `17fc427c0e6ed115b680eedfa3119191c558609947051fc241facc4ebb94017e`。基线 Git `4f92627c5fa8291e8300f1c1d0994c40d91bdf5c`，版本 0.2.4.10；目标 0.2.4.11。已有脏文件不归本任务，不清理、不回滚。

证据排序：用户现象 > 匹配身份的原始实机日志/存档与原生边界行为 > 匹配 MVID 的 IL > SDK 源码 > 历史分析推断。不能把调用成功、patchability、静态候选、测试替身通过冒充最终行为通过。

| 项目 | 本轮裁决 | 下一步资格 |
|---|---|---|
| Bug1 主机看客机顿挫 | 真实现象成立；以该聚合断言“Culling 100% 是主因”的归因不成立，实际是否涉及剔除仍需逐实体测量；最有针对性的候选是服务端远端角色仅按输入模拟步更新，缺失主机本地逐渲染帧表现路径，尚未取得真实根/模型轨迹验证 | **先派定向仪表/测量，不直接派插值修复** |
| Bug2 客机枪声主机听不到 | 原生远端普通枪的音频路径存在可解释的覆盖缺口；**未发现 SPF 拆除原版补偿的证据，该特定候选不成立** | 允许窄音频叶补偿候选；必须先记录原版/基线的实际叶调用与副作用 Red，再同场景候选 Green |
| Bug3 队伍 | 初始化先于组表加载是 T1 已证实充分原因；不是四项现象唯一已证根因。旧 alpha 仍合法，“废弃旧组写回”不是独立缺陷的已证事实；退队客户端/UI仍缺证 | 可先实施 T1 精确生命周期修复，T2/T3/T4 独立收证，不连带关闭 |

SPF 原生协议修复不自动引入 LMN；遵守 [ADR 0021](../../docs/adr/0021-spf-native-world-sync-and-gameplay-mod-channel-boundary.md)。不改原生游戏 DLL，不伪造 dedicated，不把全局传输、世界同步和生命周期大改混入三 Bug。

## 2. Bug2：枪声路径与“拆补偿”候选裁决

### 2.1 可复核调用链

1. SDK [UseableGun](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs#L880-L933) 的 `fire` 在服务端确认发射后调用 `SendPlayShoot.Invoke`；普通远端 shooter 分支不直接调用 `shoot`，仅保留服务端 hook/shot-count 等职责。
2. [SteamChannel:160–178](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Provider/SteamChannel.cs#L160-L178) 的 remote helper 在非 dedicated 构建排除 `IsLocalServerHost`，并排除 owner。此语义不是 SPF 加上的过滤。
3. [ClientInstanceMethod:110–114](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/NetInvokable/ClientInstanceMethod.cs#L110-L114) 的 list `Invoke` 进入 `SendAndLoopbackIfAnyAreLocal`；只在列表真实包含本地 loopback 时回环。
4. SPF [ClientMethodLoopbackPatch:378–468](../../Core/Patches/ClientMethodLoopbackPatch.cs#L378-L468) 保留上述列表语义：逐远端发送，`shouldLoopback` 只由列表内 loopback 决定；未将“总是回环”降成条件回环。另一 `InvokeAndLoopback` 的强制回环语义是另一 API，不能偷换。
5. SPF [SteamPlayerIsLocalServerHostPatch:91–129](../../Core/Patches/SteamPlayerIsLocalServerHostPatch.cs#L91-L129) 把 host 身份校正为 `loopback && steamID == Provider.user`。它纠正远端误标，不移除 host 音频处理；不能倒退该身份修复来制造枪声。
6. 生产目录检索 `UseableGun/SendPlayShoot/ReceivePlayShoot/playGunshot` 只找到 [隔离准入门的类型清单](../../Security/Patches/P2PQuarantineAdmissionPatches.cs#L47-L60)。该门拦待审批玩家的服务端 mutation；`ReceivePlayShoot` 是 ONLY_FROM_SERVER，不匹配其服务端/owner 筛选条件，亦无替换音频叶行为。

匹配原生游戏 MVID `54793ea7-706c-40bf-9e5a-e3a126378d7c` 的 [IL 实录](../matt-orchestrate/spf-runtime-evidence-20261031/net/clr-probe/blueprint-il.txt) 再确认：`fire` token `06004EBF` 调 remote helper；`playGunshot` token `06004EBA` 是设置 clip/volume/maxDistance/pitch 后 `PlayOneShot` 的音频叶；`shoot` token `06004EBC` 与 `project` token `06004EB5` 均包含 rechamber shot-count 副作用。SDK 行号仅辅助，候选锚点必须重新对实际 ABI/IL 验证。

**明确回答：未能指出“SPF 拆掉原版服务端本地播放补偿”的具体文件/行为，因此拒绝这条归因。** “原版 listen-host 能听到”不能从当前 SDK/IL 单独推出另一条补偿，更不能据此宣布 SPF 删除了它。同游戏版本、同枪型/发射分支的无 SPF 实际对照尚未附在现有证据；此比较事实保持 UNKNOWN，不否定用户体验，也不虚构删除点。真正可审计的候选是 SPF 支持的 listen-host 远端普通枪分支没有到达本机音频叶，而非修复已证实的代码删除。

### 2.2 允许的最小候选（不是验收结论）

采用**局部音频叶补偿**，不改 generic helper/网络收件人、不二次运行 `shoot/project/ReceivePlayShoot`。新增独立手动登记 patch，作用于真实 `fire` 的已确认发射 `SendPlayShoot` 锚点，只补调原生 `playGunshot` 一次。

门控必须同时满足：P2P host、server、非 dedicated、本地 host 身份明确、shooter 非本地 owner、原生这次发射走非 projectile 且不会本地 `shoot/project` 的分支、真实 audio source/attachments/equipment 可用。范围与位置保持原生音源衰减语义；锚点必须位于使用本发配件耐久的时刻，**不能在整个 fire Postfix 才补音**，否则最后一发消音器耐久归零后音色可能错。锚点匹配不唯一/签名变更应拒绝安装并给可见失败，不猜偏移。

用真实成功发射事件而非 `startPrimary`、按键次数或 `fire` 入口计数关联音频；后者可能包含未成功发射路径。编号只做有界观测，不变更原生 gameplay。候选可回退为卸载该 patch；不需要迁移存档/包格式/重构音频系统。

**证伪/停止条件**：若基线同一发已进入 `playGunshot`，则不能继续加补偿；转查音源 parent、clip、volume、listener/遮挡/距离，直到真实声终端 Red。若候选产生重复音、shot-count/弹药/耐久差异，立即撤回，不靠去重 timer 掩盖错误路径。

### 2.3 验证边界

现有 [CLR 探针结论](../matt-orchestrate/spf-runtime-evidence-20261031/net/clr-probe/result-and-blueprint.md) 的 recipient=0 是 helper 合约，不是枪声 Red；完整 `fire/ReceivePlayShoot` 被 Unity ECall/附件图阻断，不能称通过或症状复现。先用真实 Unity SDK 图执行基线/候选同发射场景，实际命中 `playGunshot/PlayOneShot` 并比较 gameplay 副作用；最终仍按 S1–S4 在真机确认听感、数量、距离及 Host→Guest/自身、projectile、既有七类声音回归。Guest→Guest 只由 1H+2G 关闭。

## 3. Bug1：剔除数字、暂停混淆与可判定的下一步

### 3.1 为什么“100% 剔除”不能作为主因

[DP-1 实现:199–307](../../Platform/Diagnostics/Patches/PlayerManagerCullingDiagnosticPatch.cs#L199-L307) 每次写包读的是 **forClient 的 culledPlayers 集合大小**，随后跨收件人、跨调用求和。`calls=126,totalCulled=126` 表示集合大小观测均值为 1，不是 126 个入站 Guest 状态被丢掉；其中既可能有发给 Host 的 writer，也有发给 Guest 的 writer。聚合不保留逐收件人身份，不构成逐状态剔除率。

原生 [sendPlayerStates:159–238](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Managers/PlayerManager.cs#L159-L238) 对每个 recipient 排除 self，以同组/权限/距离判断 culling；只在剔除状态变化或未剔除且有更新时进 playersToSend。稳定远距 culling 时 `playersToSend=0` 是合法结果。SDK阈值 `MAX_VISIBLE_DISTANCE=576`，实际生产阈值仍以匹配游戏 IL/运行时为准。

原始 Host [诊断日志](../../../启动器/UnturnedModManager/publish/UMM-v2.2.1-win-x64/UMM-诊断包_20261031_002429/LogOutput.log#L3718-L4036) 中 Host 坐标约 `(70.33,41.66,-217.67)`，Guest `30s` 样本约 `(-72.97,35.16,485.87)`，水平距离约 718m，非逐 writer 同帧值，**足以构成合法远距剔除的替代解释，不能作为近身顿挫 Red**。初次邀请同组发生于 L4047 后，此后 culling 消失也有原生“同组不剔除”解释。

`remoteUpdatesTotal=0` 是发送前队列/聚合观测，不等于 Guest 无输入、根不移动或模型无渲染更新；Host 日志里的 `[Client]` 也可能是本机 loopback，不是另一物理 Guest 的抓包。低 ping/累计零丢包只降低输送故障优先级，不能关闭所有输入抖动/队列时序可能性。

### 3.2 与七段 ESC 暂停精确分离

本轮从同份原日志重读暂停开始/结束，使用 `Time.realtimeSinceStartup` 的 t，不以菜单打开与否猜暂停：

| 段 | t 起点→终点（秒） | 结论 |
|---|---|---|
| 1 | 177.93→180.74 | 无远端阶段的暂停，剔除窗口之前 |
| 2 | 194.58→205.47 | 同上 |
| 3 | 278.83→298.67 | 结束后才出现目标 308.71→333.91 聚合；至少相隔约 10 秒 |
| 4 | 867.48→989.18 | 包含退出/重建阶段，不能混入有效移动段 |
| 5 | 1034.05→1039.90 | 第二局 Guest 加入前 |
| 6 | 1051.90→1074.60 | Guest 加入附近，必须丢弃恢复过渡 |
| 7 | 1189.54→日志末尾（至少1274.72） | 远端离开后的暂停，无结束事件，不杜撰时长 |

原日志起止锚点为 L2174/2195、2352/2444、3070/3223、9670/10982、12018/12072、12176/12555、13813/14470；目标剔除 L3741–4036 周边 `timeScale=1.00` 且 FixedUpdate/simulate 计数前进。因此**这段聚合异常不能直接归为正在 ESC 暂停**，但也不能由“不重叠”升格为顿挫根因。七段暂停与视觉顿挫各自取证。

测量协议：按 epoch/connection/实例分段；逐帧记录 timeScale、unscaledTime、deltaTime、frameCount；只纳入 timeScale=1、无 ESC、已Ready、正常可视距离段，按需求丢弃起止过渡。暂停/加载/传送单列，不用暂停把有效顿挫样本稀释，也不用剔除稳定暂停样本证明修复。

### 3.3 当前最小机制候选与仪表票

[原生 PlayerMovement.Update:1804–1824](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerMovement.cs#L1804-L1824) 的远端 snapshot 表现分支只在 `!Provider.isServer`；主机同时是 server，远端角色使用 [PlayerInput:1790–1814](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerInput.cs#L1790-L1814) 的服务端输入模拟，RATE=0.08。SPF [Movement 初始化桥](../../Core/Patches/PlayerMovementInitializePlayerPrefixPatch.cs#L9-L32) 给远端 Client prefab 补服务端 controller，但不提供逐帧表现插值。这是**有源码支撑的缺口候选，不是已测定的近因**。Ready 后无 guard 阻断证据；[UpdateGuard](../../Core/Patches/PlayerUpdateGuardPatch.cs#L103-L115) 不是可凭猜测删除的节流器。

下一张应是 Builder 的**只读仪表 + 定向测量票**，不是直接生产插值修复：

- **Host 侧必装**：对非本地主体的 `PlayerInput.ReceiveInputs` 解析完成/队列消费、`PlayerMovement.simulate` 原生前后记录输入 simulation/sequence、unscaled时间、root世界位姿/控制器状态；每个渲染帧末尾记录 root 与真正可见 third 模型/renderer 的世界位姿及 yaw，附 parent/实例ID，证明测的是屏幕上同一对象。每次新增/断连/退菜单重置有界环形缓存，限目标/限窗口，不全场逐帧日志刷屏。
- **Guest 侧辅助**：记录本地输入序号/时间与发送，不以两机绝对时钟做精确单向延迟；使用序号关联。Reverse 方向在 Guest 对 Host 做同样 root/可见模型帧采样用于 M4；Host 单侧已足够区分“权威步进 vs 本机表现”，Guest 仪表不能替代 Host 模型端点。
- **执行环境**：必须 Unity 运行时真实对象图。可先在匹配 SDK 场景跑确定性输入；最终1H+1G近身实机按 M1/M2 走/跑各有效10秒并录屏，站立/跳跃/转向/急停另段。不能用 CLR 假 Transform、NSB函数读写或每秒聚合替代停帧补步轨迹。

读数裁决：输入消费节拍异常且 root跟着停→先修输入/调度；root按约80ms规律跳而模型不独立平滑→才批准仅表现层缓冲；root连续而模型停→模型层/动画覆盖顺序；实际近身 writer错误发送 sentinel且真实表现使用该路径→才重开 culling 候选。任何分支都须先同场景基线 Red，再改生产。

若表现候选获证，最小方案是主机远端可见模型的延迟插值端口，保留 authoritative root/controller、命中与碰撞、原生模拟/回包；不把 PlayerMovement 根、Provider.isServer、NSB客户端门控全局改成客机。模型localPosition会被原生 Update写回（[1827–1838](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerMovement.cs#L1827-L1838)），故必须先确认写入时序、独立表现节点与拾取/瞄准射线引用，不能先把 third 任意移走。teleport/重生/纠正/车辆/断连清缓冲并吸附权威，yaw走最短路径；何时实施取决于上述真实数据，不在本T2预先宣告。

## 4. Bug3：已证顺序根因与不可合并的剩余项

### 4.1 T1 根因已证，不是“唯一原因已证”

真实 [run-10 原始日志](../matt-orchestrate/spf-runtime-evidence-20261031/team/clr-probe/run-10.log) 与 [边界裁决](../matt-orchestrate/spf-runtime-evidence-20261031/analysis/team-seam-decision.md) 固定真实存档和原生方法体：MENU→Host `PlayerQuests.InitializePlayer/load`→GAME `GroupManager.onLevelLoaded/load`，期望 alpha `112589994979229698/OWNER`，实际 `0/MEMBER`，原始退出码2。先加载组表的 run-09仅是机制对照，不是修复Green。

[PlayerQuests:2578–2599](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerQuests.cs#L2578-L2599) 在server初始化时调用load；[2824–2850](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerQuests.cs#L2824-L2850) 读到动态组后查未就绪组表，查不到即loadMainGroup，清为none并降MEMBER。随后 [GroupManager GAME回调](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Managers/GroupManager.cs#L165-L179) 新建组表再加载，并不修复先被清掉的玩家字段。实际Host日志L10645–10676完全复现这一次序。原生 [Provider:1553–1556](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Provider/Provider.cs#L1553-L1556) 本就可先建本地玩家/初始化/发初始态；应修复P2P组合下的生命周期契约，不能把整个原生玩家初始化入口替换成自写图。

### 4.2 ...9698 / ...9699 是什么

[首次/二局证据表](../matt-orchestrate/spf-runtime-evidence-20261031/team/working-evidence.md) 与原始Host日志：首次存档alpha OWNER/members=2（L10475–10481）；次局Host先none，用户另建beta `...9699/OWNER`（L12091附近）；Guest随后原生读取自己的alpha `...9698/MEMBER`，组表仍 `groupKnown=True,members=2`（L12473–12478）。

因此：**“写回废弃旧组”措辞应撤回**。alpha在持久化组表中仍合法，原生load恢复Guest到alpha是数据一致的行为；成员数是GroupInfo计数，尚不等价一份逐人权威成员名单。Host因先前T1丢失alpha身份后新建beta，已经能解释双方分裂。此处不是已经证实的跨局旧对象覆盖/异步乱序写回独立缺陷，不得擅自删除alpha、将Guest迁到beta或强制踢出。若将来证明在新epoch正确恢复/明确换组后仍被旧epoch回写，才另立带写入者、对象generation、源存档身份的独立缺陷。

T3也不能并入T1：Host L13277–13279已收到并应用Guest alpha→none；其before的members已为1，与 [原生leaveGroup先减员再InvokeAndLoopback](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerQuests.cs#L596-L609) 相容。它支持权威至少已执行一次退队变化，不证明Guest收到/apply/UI刷新。客户端探针缺席不等于未执行。T4同组ID消失有组身份分裂的合理解释，但仍要实际同组标识渲染端点确认。

### 4.3 最小生产方案（交Builder以TDD验证）

优先采用**会话/地图就绪门控 + 仅本地主机Quests持久化load延后一次**，保留所有原生反序列化与组有效性判断：

1. 新增独立手动登记的队伍恢复模块；只对P2P host + server + 唯一真实local host（非零SteamID、loopback、local一致）生效。world/session epoch、地图、目标实例组成恢复租约；MENU/断连/销毁/新局清空 pending、ready、重入状态。Guest和dedicated/普通单机全放行。
2. 在原生Quests.InitializePlayer过程触发的首次load、组表尚未完成当前GAME生命周期时，仅将该load排队并阻止这一次过早执行；初始化容器/事件订阅仍执行一次。**不要二次执行完整InitializePlayer、不要先执行load后再重读**，避免事件重复/flags重复/旧UI状态扩散。具体patch位置/重入必须由真实边界测试证明。
3. 当前原生GroupManager GAME回调成功完成（含无组存档的合法空表）后，验证同epoch/地图/实例仍有效，只执行一次原生Quests.load；之后通过现有原生组状态发送/事件路径发布字段，修复已早发的none本机投影。只同步已恢复字段，不能用 `ServerAssignToGroup` 冒充恢复——[该API会members++](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerQuests.cs#L507-L519)。不扩展组包、不引入自定义频道。
4. **禁止“提前GroupManager.load就好了”**：GAME回调会重新new knownGroups，提前load可能被清掉；禁止存档手写解析器/反射硬填alpha/OWNER；不能因组表空就猜默认队伍。生命周期异常/取消不宣告恢复成功，不把待恢复值以none保存覆盖；原生 `wasLoadCalled` 与save门的行为须测试。若延后会改变非组Quests就绪行为，必须在same-fixture和SDK显示/flags回归中解决，不扩成全Player阻塞。

最小测试集合：同run-10输入顺序只换候选DLL得alpha/OWNER；正确顺序不重复load；空存档/无合法组走原生默认；同进程二次局不同地图/epoch不复用；pending期间退服、旧回调、销毁实例不恢复到新局；远端/单机/dedicated不干预；members不增加；flags/quest订阅只一次；早发none被原生发布纠正。生产注册必须实际执行 `RegisterManual` 或既有父入口，验证owner+original+PatchMethod；不以测试手动绑定替代。

T2后续验证真实Guest读取/本机apply并审计groupID/rank与Host期望一致。T3另用损坏alpha/beta快照发真实Guest leave，分别记录request→Host member-count/none→transport发送→GuestReceiveGroupState→字段/UI；不要等T1修好后只测健康快照就宣告遗留退队已修。若仍失败，在第一次分歧处另作窄修，不能预先宣布需要重写generic loopback。T4真实同组显示、退队标识消失独立关闭。

## 5. 三 Bug 依赖与共享文件冲突矩阵

因果图不等于修改顺序：

- `原生组表未就绪 → 本地Host读组失效(T1) → Host另建beta、Guest合法恢复alpha(T2的解释候选) → 同组判定/标识(T4的解释候选)`；T2/T4仍各自验收。
- Bug1表现与Bug2音频没有已证的相互前置；共享的是Host/server/owner身份和原生更新域，不是“都因传输”。不要把“改完generic loopback可能一起好”作为依赖。
- Bug3同组与否影响PlayerManager远距剔除规则，会改变Bug1探针读数，但不是近身顿挫根因证明；Bug1测量必须固定距离/同组状态。改完队伍再测，只能比较同等条件。
- Bug1若触碰third/武器挂点parent，可能影响Bug2音源世界位置；补音必须基于原生挂点，不把表现偏移写入权威射击位置。这是集成回归依赖。

以下为候选所有权建议，不授权修改历史已脏文件；新增文件名由Builder定，公共接线仅一个集成者写入。

| 文件/面 | Bug1 | Bug2 | Bug3 | 冲突/处理 |
|---|---|---|---|---|
| 独立新模块 | 移动只读仪表；获证后远端表现模块 | 普通枪本机音频叶patch | 组加载生命周期patch | 独立文件可并行；禁止拷贝一份Host/epoch全局状态各自猜 |
| [HostManager](../../Platform/Host/HostManager.cs)、[初始化tracker](../../Platform/Host/PlayerInitializationTracker.cs)、[IsLocalServerHost修正](../../Core/Patches/SteamPlayerIsLocalServerHostPatch.cs) | 只读门控/身份 | 只读门控/身份 | 只读门控/身份 | 高语义耦合，默认零修改；不为过测试放宽身份门 |
| [ClientMethodLoopbackPatch](../../Core/Patches/ClientMethodLoopbackPatch.cs) | 无需修改 | 无需修改 | 现有强制回环路径应复用 | 如首次分歧指向此层须独立裁决、扩大回归；不是三票公共重写点 |
| [注册AuditModules](../../Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationAuditModules.cs)、[注册DiagnosticsModules](../../Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs)、[关键注册核验](../../Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationCriticalVerification.cs)、[要求目录](../../Core/Registration/PatchRegistrationRequirements.cs) | 仪表/表现登记 | patch登记 | 生命周期登记 | **文本冲突高**；分模块生产registrar、集中串行接线与owner核验；别并发改同一长文件 |
| [主项目](../../SteamP2PFriends.csproj)、[测试项目](../../WhitelistTests/SteamP2PFriends.WhitelistTests.csproj)、[测试入口](../../WhitelistTests/Program.cs) | 新源码/测量判定 | 新源码/音频负控 | 新源码/边界回放 | 显式Compile/正式用例登记易漏；集中合并，唯一测试ID去重 |
| [会话诊断核心](../../Platform/Diagnostics/Patches/WorldSyncDiagnosticCore.cs) 与session reset回调 | 订阅清环缓存 | 清观测关联 | 清pending/ready | 生命周期语义冲突中高；优先消费既有reset，不能推进共享epoch或清其他域 |
| [DomainModules](../../Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDomainModules.cs) | 无 | 无 | 无 | 进入T2已脏，三Bug不需要改，不回滚他人工作 |
| [版本属性](../../Build/Version.props)、发布清单/包 | 最后统一 | 最后统一 | 最后统一 | 单一集成者在审查和真实验证后统一升0.2.4.11；禁止边修边覆盖0.2.4.10稳定包 |

## 6. 最小真实验证路径与修复顺序

### 6.1 分层路径（不能跨层冒领）

| 路径 | 可自动化 | 需SDK/原生集成 | 只能真实多人/用户体验关闭 |
|---|---|---|---|
| Bug1 M1–M4 | 轨迹判定器用已知轨迹Red/Green；epoch/identity/teleport/yaw/缓存边界；patch注册核验 | 真实Unity模型帧、sim前后/输入序号/帧时，基线满足停帧补步失败再候选同场景Green；CLR NSB读写不算M1 | 主机实际近身走跑跳转急停/静止画面；反向流畅；1H+1G即可最低闭环 |
| Bug2 S1–S4 | 完整IL锚点唯一、准入负控、原生字段/副作用对照判定、注册身份 | 真正fire到音频叶端点、普通/消音器最后耐久/爆发或自动连发/无弹失败/projectile/host-self重复音负控；无真实AudioSource则不算行为Green | 每发可听数、音源位置/距离；1H+1G关闭已确认方向；Guest互听必须1H+2G |
| Bug3 T1 | 已有run-10原生边界Red；只换候选生产DLL+真实registrar同fixtureGreen；多epoch/取消/无组负控 | 真正菜单/GAME生命周期、原生load与UI字段/事件；无HarmonyX可用注册宿主是需要解决的工具边界，不是症状Red | 同存档菜单重进、完整进程重启各两次，Guest尚未进先保alpha/OWNER |
| Bug3 T2/T3/T4 | 合法组字段/重复load/member数不增单测；损坏快照fixture可辅助 | 真正Guest退队request/Host权威/Guestreceive-apply独立观测链 | 真Guest重连alpha；损坏快照退队后两端none/UI；真同组ID出现、退队消失，不以T1 Green连带通过 |

自动构建入口是 `dotnet build SteamP2PFriends.csproj -c Release` 与 `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release`；正式运行使用仓库当前受支持runner/依赖宿主，留完整命令、环境、DLL SHA/MVID、exit与唯一test ID，不能临时用仅定向CLI取代正式全量。队伍边界的已执行run-10展开命令见[原记录](../matt-orchestrate/spf-runtime-evidence-20261031/analysis/team-seam-decision.md#L7-L18)；本T2只重读原证据，未声称已新跑该命令。

### 6.2 调度与停止门

1. **立即并行开启测量准备与T1修复**：移动票先做有界Host仪表/SDK端点，枪声票先补齐真实fire/AudioSource基线端点；二者都不得用既有CLR外部边界失败冒充Red。队伍已有可靠Red，应先走真实registrar/same-fixture Green，不等待另两项先证完。
2. **生产集成顺序建议 T1队伍 → Bug2窄音频叶 → Bug1获证后的表现/调度修复**。依据是可归因性与可测性，不是臆定技术前置。移动测量可先行，公共注册/项目文件串行合并。若Bug2基线已命中叶，停止音频补偿，回到真实音源问题；若Bug1图不支持表现缺口，停止插值方案。
3. 每个生产改动先Red→Green→正式唯一用例回归→两名独立审查CLEAN；仪表若需部署亦先审，不把未审诊断DLL直接丢给用户试。然后按[实机循环](../../docs/agents/real-machine-test-loop.md)与[自动部署SOP](../../docs/agents/auto-rm-test-sop.md)以同一候选身份做1H+2G，记录加载中的版本/SHA、场景、前后存档指纹和日志/录像关联。
4. 缺SDK/真机时标具体UNKNOWN与下一动作，不把需求降成静态/聚合通过。不覆盖原始存档；损坏快照作为可恢复隔离测试输入，记录选择的是哪个world/slot/character，测试后由明确备份恢复。未经用户授权不新建远端设施或私自部署稳定包。

## 7. 回归面、证据账与交付边界

每项保留需求的独立 PASS/FAIL/UNKNOWN：M1/M2/M3/M4，S1/S2/S3/S4及Guest互听，T1组件/T1实机/T2/T3权威/T3客户端/T4，R1/R2/R3。候选不得因为整体总数高而折叠掉某项UNKNOWN。

本轮建议回归最小集包含：Host→Guest原有移动广播/位置yaw、校正/传送/死亡重生/车辆切换和跨局清理；枪的ammo/shot-count/耐久/弹壳/投射物及七类已工作音效；队伍创建/邀请/接纳/拒绝/离开/所有权/member数/静态主组与动态组/保存重启/标识；P2P-only负控（普通单机与dedicated不变）；准入隔离不能因仪表/音效放宽。按R2再测碰撞、门、物品落地拾取/幽灵物品与僵尸行动/掉落；这些既有域不是本轮重构范围。

本T2继承但不冒领的结果：队伍run-10原生组件Red有效；枪声完整fire与移动完整Update CLR尝试是ECall/对象图边界阻断，不是Red也不是Green；未新跑游戏、未启动后台任务、未产出DLL。架构任务的“完成”只表示关键问题已裁决、未知明确且后续路径可执行；最终产品修复尚须后续实现、独立审查及实机证据。

本文件唯一新增。要求文件与历史原票不修改；本轮无任何生产源码修改。

### 本轮实际校验

已完整读回本报告；以Python解析本地Markdown链接，44个文件链接全部存在，初验发现的诊断核心路径笔误已修正后重验通过；未遗留APPEND占位符。需求SHA-256与T1给出的冻结值逐字匹配。检查命令退出码0，标记 `ARCHITECTURE_CONTENT_CHECK_PASS`。

本轮是架构裁决而非生产实现，明确不新跑Red→Green构建/运行循环：队伍复用身份一致的原生run-10 Red；另两项已有完整运行被Unity ECall阻断的证据，新增CLR替身无从取得真实模型/AudioSource终点，必须交上述SDK/真机测量票。此跳过不豁免后续Builder的Red-first、不把任何Bug登记为PASS，也不授予本报告独立双轴CLEAN。

