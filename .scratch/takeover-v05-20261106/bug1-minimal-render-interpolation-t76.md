# T76 最小视觉插值裁决：先验证纯渲染叶节点，失败才转view proxy

2026-11-07／gpt-architect。**本票只交规格，不改生产、不开游戏。** 唯一裁决：先做Model_0/1纯叶节点的有界几何门G，通过才用LateUpdate水平插值；G失败即停止该路线转隔离view proxy，不直接跳proxy，也不批准未经验证的最小方案作为已修复功能。最小路线不等于已证明可行，不允许不断往父节点试写直到“看起来动了”。

## 1. U3DS/专用服务器为什么避开这个结构性缺口

这里的U3DS按“独立dedicated进程+玩家游戏客户端”拓扑解释，未审其产品内部实现，不凭名称保证无任何顿挫。

| 谁在看 | 原生角色与位置路径 | 结论 |
|---|---|---|
| 专服进程 | Provider.isServer=true；远端角色输入模拟root、非local Third归零；没有玩家主相机在这条路径观察 | 仍可有离散权威步进，但玩家不是从专服视图看它 |
| 连专服的玩家看别人，包括同机开的玩家客户端 | 游戏客户端Provider.isServer=false、目标channel.IsLocalPlayer=false；[Movement.Initialize:2021–2035](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerMovement.cs#L2021-L2035)建NSB，[Update:1496–1501](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerMovement.cs#L1496-L1501)读snapshot，[1804–1823](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerMovement.cs#L1804-L1823)把远端root写成snapshot.pos | 玩家看已缓冲的位置；[NSB:139–142](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Network/NetworkSnapshotBuffer.cs#L139-L142)确实调用lerp，不需要更高的包率来解释 |
| 本项目listen-host自己看Guest | 同一个进程既server又有本地玩家相机；目标不是local，跳过[1663起本地Lerp](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerMovement.cs#L1663-L1665)和!server快照分支，[1827–1840](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerMovement.cs#L1827-L1840)使third.local=0，PRONE为-0.1Y | 主机观察到原生server远端的直接跟随路径，缺本地呈现平滑 |

所以这是**原生角色分支与本项目listen-host组合的结构差异**，不是已证明SPF删除了原来工作的插值。专服客户端也可能因丢包、NSB欠流、帧率差而卡；不能宣称U3DS在所有环境天然流畅。若所谓U3DS实际也是同进程server+相机，须重新核对角色日志，不能沿用该拓扑推论。实际程序集/patch身份仍按[T61](bug1-root-cause-t61.md#L131-L142)锁定。

## 2. 选哪一个transform：只试Model_0/Model_1，不碰Third/Skeleton

唯一最小候选是**现有远端`player.third/Model_0`、`Model_1`的transform**，只写其位置；不重排父链，不改rotation/scale，不新建包住骨骼的空节点。新空节点若只包renderer，不会自动解决蒙皮问题；若包Skeleton，则又搬动判定体，禁止。

证据：[PlayerAnimator:1680–1694](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerAnimator.cs#L1680-L1694)从Third取CharacterAnimator、Model_0/1的SkinnedMeshRenderer及同级Skeleton；[1597–1635](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerAnimator.cs#L1597-L1635)表明本地分支才明确禁用骨骼Collider。不能据此声称远端Third是纯渲染。原生root、Third、Skeleton、骨骼、Collider、aim/枪口与AudioSource均不在本候选写集。

**关键不确定性不是“setter能否调用”，而是移动renderer transform能否真的移动蒙皮表面。** SDK只给组件取法，prefab的`bones/rootBone/bindposes`、实际shader/引擎蒙皮基准未在这些C#行中给出。不能仅凭它名叫Model就断言可行。

具体可证伪原理：一类世界骨骼蒙皮可写成 `v_world = R × Σ w_j(R^-1 × B_j × P_j)v`，其中R是renderer变换，B是原骨骼世界矩阵，P是bindpose。只改R可能被R^-1抵消；表面仍跟着原骨骼，挪renderer或其纯空父节点都没效果。若必须改B才动表面，骨骼上的Collider也会移动，违反硬门。**这是反例机制，不冒称已经从当前实机shader验证了该公式**；某些实际绑定/渲染实现的参考基准不同，必须用下述一次有界试验判定，不在纸面上捏造成功或绝对不可能。

### 准入门G：先证明“几何真的移动，物理从未移动”

在匹配实际游戏/引擎/资产的SDK场景中，只选一个目标、固定本帧骨骼pose/相机，正常装备/外观不要由模组强制修改。记录两renderer的完整路径、父链、mesh/material、bones/rootBone、Collider及gameplay读取者绑定，形成profile指纹。

1. **纯节点门**：实际可见表面集合至少包含一个有非空mesh/顶点的renderer，空集合不能算通过；当前空/隐藏的可选Model单列，不充作成功样本。两个节点及其子树不能有任何Collider（包括当前disabled）、Rigidbody/Joint/CharacterController、骨骼判定节点、AudioSource、武器/相机/gameplay组件；原生判定/射线起点不引用它们。含未知组件/引用、或实际带物理骨骼者直接不准入，不能先禁Collider来凑条件。
2. **刚性位移门**：分别对X和Z施加0.10U位移，并还原；至少两个姿态/朝向。比较实际蒙皮后的世界表面点，而非仅transform.position或bounds中心。可以用同帧BakeMesh只读快照转世界点并用真实画面核对，测试前后原骨骼pose哈希必须相同。每个被画renderer的整个快照或已证明覆盖的表面点组，均须满足 `max||(p_after-p_before)-delta||≤epsilon`；物理root/原骨骼/Collider矩阵与enabled不变，受控射线结果不变。BakeMesh只用于验证，不把烘焙mesh变成另一套呈现方案。
3. **覆盖/可逆门**：还原后表面回到基线epsilon内；验证实际可见的身体/衣物/附着外观是否随同一刚性偏移，无部分部件撕裂。不能只挑一个动了的中心点或隐藏未移动的武器/衣帽来宣布通用方案成立。

G通过才允许后面的插值。若世界表面不动、只部分顶点动、必须挪原骨骼才能动、或要改shader/bindposes才能生效，**本最小transform路线即失败**，停止并以证据转到T61的隔离view proxy设计票；不轮流尝试Third/Skeleton/再包一个空父节点。当前静态材料尚不能给G签PASS，最小方案的可行性仍待这一实证。

## 3. 回调与原生归零如何并存

选择 **LateUpdate写纯渲染叶位置，主相机onPreCull只核验，不在那里再抢写**。不是修改/替换PlayerMovement.Update，也不让其跳过归零。

每帧顺序固定：

1. 下一次目标`PlayerMovement.Update`执行前，先恢复本模块上一帧**自有**的leaf位置偏移；原生Update/动画照常运行。恢复仅针对当前generation且实际localPosition仍与上次本模块写后保存值逐分量一致的节点；若别的写者已改值，不用旧快照覆盖它，转冲突失败状态。
2. LateUpdate在原生动画求值后读本帧原生leaf基线与原生Third姿态offset，算本帧视觉水平偏移deltaXZ；设置两个leaf的world position=`本帧基线world position+deltaXZ`，Y不改。记录写后实际local/world位置，不能每帧在旧偏移上再累加。
3. 主相机提交前核验实际leaf值/renderer集合/校准关系与本模块写后相符。原生改的是Third父节点；本模块后写Model叶，属性不同，正常归零并不与本模块抢同一值。若后续动画/衣物/其他patch又覆盖leaf、或实际几何不再符合G，立即停，不靠反复把执行顺序调到“更后”打赢抢写。

只读Prefix/Postfix可采样/恢复自有视觉状态，但不得return false截原生入口、吞原生异常或延缓权威模拟。Native归零、本模块Late写、主相机核验各记frame/seq/node generation/base/applied/observed位置；`LATE_WRITER_CONFLICT`一次即禁用本目标本次arm。仅LastApplied transform匹配也不能证明G，几何准入证据不可省。

[HumanClothes:1584–1601](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Characters/HumanClothes.cs#L1584-L1601)会换sharedMesh；LOD/装备/材质/层级变化必须核对profile，未知变化不准带着旧顶点/蒙皮结论继续写。只支持经核验的相机/外观集合；新增能画该目标的瞄具/反射/特殊相机则暂停，不能让主相机看平滑、另一个镜头看间歇原位置而不报告。

## 4. 是否需要“渲染前搬、渲染后还原”

**不选择它作为首轮路线，渲染后还原也不是物理安全的证明。** 对通过G的真正纯渲染叶而言，从始至终没有改任何物理相关节点；持有偏移到下一次原生更新前也不改变原生Collider/射线。因此它不具有T61“临时搬整个Third连同判定体”的物理事务问题。该论证依赖写集/引用闭包，不依赖“赶在FixedUpdate前恢复”。

如果把同样操作放进onPreCull/onPostRender，**对合格纯叶在原理上可行**，但多相机、提前Camera.Render、取消/异常不配对仍有视觉状态管理成本，且不解决蒙皮矩阵抵消。本裁决固定采用§3的帧级幂等基线恢复，不为一个已无物理副作用的写集增加相机成对事务。若G不通过、写集含骨骼/Collider，渲染后还原绝不能使它变安全；最小替代是隔离view proxy，不是挪动后调用Physics.SyncTransforms。

即便通过G，仍需检查影响：原生相机/aim位置不改；renderer的可见bounds、剔除、阴影/LOD可能变化，是需测的视觉副作用；第三方读取该leaf作为gameplay/camera目标则超出准入profile。不能无依据说任何mod/任何相机下都绝无影响。

## 5. 唯一插值协议：独立视觉时钟，只平滑XZ，不外推

采样只在Host目标的**有效原生walking simulate正常返回后**，保存权威root位置p、simulation序号s、姿态/epoch/模型generation、实际mono时间；零位移样本也保存。不是ReceiveInputs入队时的坐标，不改原生RATE/simulation/input.tick。

沿用[T61候选时钟](bug1-root-cause-t61.md#L100-L111)：样本域 `u_i=s_i×RATE`，正常RATE=0.08s；自身视觉时钟u_v在已知相邻样本之间前进，初始实验延迟D固定0.13s。目标水平点 `P_v=(1-alpha)H(p_i)+alpha H(p_(i+1))`，`alpha=Clamp01((u_v-u_i)/(u_(i+1)-u_i))`，H为XZ投影。每帧时钟推进不超过实际单调帧dt，不按平均FPS或每包实际到达间隔直接推速度；实际到达抖动另外记录。

本帧 `deltaXZ=P_v-H(currentAuthorityRoot)`，Y偏移=0；将这个共同world偏移加到两个Model各自的本帧原生基线上。没有足够两点时保持当前原生显示、预热，不从旧样本回放使启用瞬间倒退；不得叠加前帧offset。姿态Y、原生PRONE的-0.1Y、骨骼动画与朝向不由此最小模块插值。

上界明确：**超出最后已确认样本的速度外推距离=0U**。欠流可沿已知轨迹走到最后确认位置，然后保持；无新有效样本≥0.25s置STALE、清历史并原生对齐。逻辑积压`u_last-u_v>0.25s`也原生对齐重建，不突然提速追赶。额外安全门 `||deltaXZ||>nativeCap×0.25s+epsilon` 就重置，不通过钳偏移把异常藏掉。

这比现状“立刻冻在最后A”多一个显示延迟，不应先倒退再冻结；断网不保证更好。冻结后恢复数据重新预热，不补播失联轨迹。正常可避免逐80ms阶梯，但停/转显示迟滞、视图与原hitbox约vD量级视差仍存在（倍率1走约0.585U、跑约0.91U，另含离散相位）。因此不承诺零橡皮筋/零穿角：碰撞判定不动不等于瞄准体验不变，必须实机看角落/移动射击和近战。

## 6. 重置、退化与可观测标记

每次重置记 `run/targetAlias/epoch/generation/frame/mono/reset_seq/reason/old_sample_range/new_authority_position/restored_nodes`，重置后第一个主相机可见提交点应与新原生目标在epsilon内；旧generation样本绝不能再次生效。恢复只限仍存在且仍为本模块最后写值的leaf；若值已被别人接管则不强覆盖，报告冲突。销毁后的Unity对象不再读取/写入。

| 事件 | 强制动作（无可选解释） |
|---|---|
| 原生传送/纠正、重生/生成、死亡、epoch/连接/实例变化 | 清历史、撤自有偏移、当帧原生对齐；新实例重新做profile/身份门，不复用旧引用。死亡/载具/ragdoll不在本最小支持域 |
| 站姿走跑→蹲/趴/游泳、进出载具/坐下、显著垂直运动/起跳 | 立即原生对齐并SuspendedStance；保持原生Y/骨骼/朝向。回到支持姿态且稳定0.5s后仅可重新预热，不能接旧轨迹。跳跃/游泳的平滑不在本试验承诺内，但不得回归其原表现 |
| 目标离开主相机可见范围 | 恢复原生leaf、清历史，SuspendedVisibility；不在屏外追演。重新可见且身份/profile稳定0.5s后重新预热；不能只靠Renderer.isVisible（可能来自别的相机）证明主相机可见 |
| 包空洞≥0.25s、积压越界、simulation倒退/重复不明、暂停/时间倒退恢复 | 分别标STALE/BACKLOG_RESET/CLOCK_RESET，恢复原生并重建，不外推、不重放 |
| 共享mesh/material/bones/父链/装备外观或相机集合改变 | 若不在已验证profile内，立即停本次arm，标PROFILE_CHANGED；不盲写新模型 |
| 异常大跳却无合法事件绑定 | UNCLASSIFIED_DISCONTINUITY并原生对齐；相关观测UNKNOWN，不把距离阈值当传送事实 |
| OnDisable/退出、该帧缺原生Update/动画阶段、写后发生未知覆盖 | 只撤本模块仍拥有的偏移；不再应用新偏移，缺相位标UNKNOWN。稳定运行中冲突为失败锁，禁止每帧自动抢回写权 |

必须有对应的可断言用例：reset发生后第一个真实camera端点对齐、下帧不回旧位置；重复reset幂等；死亡/断连/同ID新实例不继承旧偏移；PRONE/Y原生offset不被清成站姿；未知事件不被当有效连续走跑。**不能因为数值判据改成XZ，就取消M2跳转/M3静止与纠正回归。**

## 7. 开关、灰度与自动回退

建议配置契约（未来实现，当前没有可运行命令）：`ExperimentalHostVisual.Enabled=false`、`TargetSteamId`显式指定一个获准Guest、`Mode=RendererLeafTrial`、`ArmOnce=true`；必须是Host自身主相机看非local目标，非dedicated。没有目标/已验证profile/完整回调覆盖就拒绝arm。配置开启不授权更改装备、原生角色标志或自动执行有侵入性的校准；G先在SDK完成，实机仅在明确的受控试验窗口核实。

状态固定为Off→WaitingForProfile→Warmup→Active→Suspended或FailedSticky。默认OFF、仅一目标、仅站姿平地走跑、受支持主相机/外观，不在.11主干默认开启。先交经独立审查的试验件，后进行1H+1G A/B；不是用户等待件的静默热更新，也不把默认关闭当成Bug1已修。

自动失败锁（任何一条触发即停止本次arm；要再试需人工重新arm，而非无限自动循环）：

- 权威root/原骨骼/Collider/原生输入时钟/射线或伤害差异、G几何失效、未识别gameplay引用。
- 一次LATE_WRITER_CONFLICT、不可逆还原、profile/可绘制相机集合未知变化，或安全清理失败。日志失败不能继续冒充Active；不能为恢复画面而重写权威或开关Collider。
- 单目标稳态模块开销在两段连续120个有效帧窗口中p95>0.5ms，或稳态每帧GC分配/对象材料持续增长。G的临时几何审计成本单列，不拿校准开销当稳态也不隐藏实际总帧抖动。

回退效果必须可见验证：撤自有叶偏移、清样本、停回调/写者，保留原生Third/动画/绘制；不重载玩家、不修存档、不发纠正包。若另一写者已接管值、无法确认自有偏移已清，记ROLLBACK_UNCERTAIN并锁为失败：不得强写旧快照覆盖它，也不得宣称已完整恢复；停止试验，由操作者正常退出重进重建原生对象，禁止自动重载玩家。这里能保证停止本模块，不凭空保证未知写者下的画面复原。没有完整回退证据就禁止把候选留在用户常用局里。停止原因用现有日志/诊断报告，不新造弹窗框架。

## 8. A/B证据与放弃门：一轮最小验证，不无限试探

### 两条独立验收轴

**视觉连续性**：固定原协议/原生speed cap/epsilon与有效窗口，以真实可见表面或G已标定的render节点读数测每渲染帧XZ位移、小位移停段、回补超界数、供样mono间隔/欠流率、显示滞后分布与主相机录像。不能把插值器算出的P_v直接写进测量V；原Third载体仍可A=V，应保留为`v_carrier`诊断，不把它继续当新实际可见端点。节点到权威原点的几何标定必须固定在asset/profile里，不用每窗口A0-V0消掉真实lag。

原A组可因AUTH_STEP_LOCKED而strict M1本来就为0，所以“B也M1=0”不证明改善。需显示原80ms阶梯被中间可见位置替代，同时无新增超界补步/闪烁/部件撕裂；[T71](architecture-decision-t71-m1-relative-motion.md)规则与M2画面证据分别登记。角色骨骼姿势有动画不等于位移平滑。

**判定一致性**：相同受控输入/随机初态，开关前后原生root/controller、原骨骼/Collider几何和enabled、输入/tick/simulation、状态发送、受控碰撞/射线、真实射击/近战命中伤害完全一致；相机/aim位置不变。原生骨骼不是静止不动，而是与不启用模块时的相同模拟一致。视觉偏移导致玩家瞄准体验差也算候选失败，不能以“服务器数据没变”辩称用户错觉。

先G的静态两轴0.10U试验，再受控SDK走跑/重置/关闭与负控；通过才实机A→B→A（关闭→开启→回退），站姿走/跑各至少原需求10s并去首尾0.5s，保持地图/输入/距离/帧率与网络条件记录。实际原生动作/命中负控不得只靠合成坐标。双人试验不关闭原M4反向或最终1H+2G多Guest验收；必须另确认未选中的角色完全不变。测试计划包括：原生归零后写及后写者冲突、无包/短抖动/250ms欠流、姿态/死亡/teleport/视野/LOD、开关回退、shadow/相机和外观完整性。

### 固定终止规则

本路线预算是**一组匹配SDK/资产的G试验 + 一轮经审查的实机A/B验证**。纯参数/路径拼写错误可以修正后重做该门，但不能换一个更危险祖先节点或偷偷改shader/骨架来续命。

以下任一证据必须放弃此transform最小路线、提交失败raw/profile并转隔离view proxy（另派实现/审查，不在此票自动写代码）：

1. G证实只挪Model transform不挪实际表面、不能统一平移可见部件、或要动带判定的bones才能成功。
2. 安全准入闭包不能成立；需动Collider/权威root/aim、改原生动画调度或依赖渲染窗口内没有任何射线才能保持安全。
3. 一次受控验证证明native后写不可协调、还原不可靠、外观/阴影/武器显著脱节；不能以反复改变callback优先级抢写作为修复。
4. 有效B段仍无真正中间呈现位置、顿挫/瞄准体验更差或性能越预算，且要改变本票固定的纯叶/时钟边界才可能改善。

若仅因网络持续断供或总体渲染帧严重卡住，则停止视觉修复宣称，回到输入/调度调查；proxy也不能产生未知真实运动，不把它当万能兜底。证据不全为UNKNOWN，不强说本最小路线已被数学否定。

## 9. 与既有裁决的关系和本票完成边界

本裁决**取代T61“先直接做新view proxy”的首轮实施顺序**：先有限验证更小的纯Model叶方案，失败证据齐全才转proxy；不改变T61原生角色分支归因、input.tick禁改、物理安全门和版本身份要求。它没有批准把Third临时搬动的旧路线。新的安全结论仅对通过G的纯渲染写集成立，不泛化到共享骨骼。

T22的单次multicast、T23宁阻保存不写坏档、T33不为虚假Provider.user null风险改生产、[T57](architect-ruling-t57-work-policy.md)冻结共享接线与生产改动双轴均保持。后续最小模块也是行为变化，默认OFF也仍要真实Red/Green/变异守卫、独立双轴与实际身份绑定；不享受测试零生产字节单轴例外。未取得证据不宣称.11 Bug1已修，也不擅自把承诺延期.12。

**本票已确认**：专服/客户端拓扑角色解释、原生Third归零写者、Model_0/1为SkinnedMeshRenderer和骨骼Collider风险。**待验证**：当前prefab/engine/shader的真实骨骼绑定、G是否通过、渲染叶位移能否改善及硬安全/性能。**已否定**：仅transform/bounds变化即可证明画面移动、赶在FixedUpdate前还原就必然安全、修input.tick或强进客户端分支可算纯视觉修复。本票未运行SDK/游戏/构建或修改生产代码。