# T61 Bug1 根因归类：服务端远端角色缺位置呈现插值，不是 tick 失效

2026-11-07／gpt-architect。只读归因与候选规格；不修改生产、不运行游戏。本票重算既有trace并核对SDK，不授予Bug1实机PASS。

## 1. 结论与确认边界

**选中的代码解释是：Host作为server看非local Guest时，PlayerMovement.Update每帧把Third相对权威root归零，Third继承离散root；该角色根本不执行本地Lerp块。不是“Lerp执行了但input.tick不刷新、因子被钳成1”。**

| 档位 | 裁决 |
|---|---|
| 已由所核SDK确认、实机记录相符 | server+非local角色不建NSB、不开本地Lerp；非PRONE的Third.localPosition每帧归零。记录的6338个渲染点V=A，与该原生分支自洽；多数位置变化与输入出队同帧 |
| 已由所核SDK确认 | 远端_tick只在InitializePlayer赋值，不在server远端消费包时刷新；但它是累计simulation速率护栏的起点。不能伪造刷新来修视觉 |
| 已被SDK反证 | Host本地root每20ms移动、远端每80ms移动的“四倍root频差”。本地Move也在count%4分支，名义12.5Hz；50Hz的equipment.tock不是人物位移 |
| 已被否定为本段因果解释 | 把1746的local Lerp套在IsLocalPlayer=false目标上，然后仅用假想alpha≥1解释吸附。该分支不执行，不能把未执行表达式当根因 |
| 待验证的运行时结论 | 当前实机实际IL/活跃patch与SDK分支一致、没有额外后写者；插件render-only平滑能否在原生判定不变前提下改善体验。现有trace不记录写入调用栈/实际alpha，也不是全骨骼/全顶点运动记录 |

限定语很重要：“原版行为”指**SDK原生代码在server+非local组合下执行的分支**；不等于本票已运行无SPF的官方listen-host对照，也不外推所有版本/模式。该分支在普通dedicated没有本地观察者；本项目把非dedicated本地视图与server组合起来，暴露了需要补齐的呈现缺口。没有证据说SPF破坏了一条原来在该角色上已工作的Lerp。

**在所核SDK范围内，对server非local角色root/Third承载位置的第二路连续位置平滑来源：无。** 骨骼姿势动画不是角色root位移插值；此“无”不替代实机IL、prefab、第三方patch/组件清单核验。

## 2. 谁写位置：三种视角的真实分支

统一SDK根为 `.scratch/bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/`，以下链接是读码事实，不是本轮运行调用栈。

| 角色/视角 | root写者/频率 | 可见位置写者 |
|---|---|---|
| Host自己的local角色 | [Input.FixedUpdate:1540–1544](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerInput.cs#L1540-L1544) 仅count%SAMPLES=0进入，[1590](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerInput.cs#L1585-L1594)调用movement.simulate(...RATE)。SAMPLES=4、正常50次FixedUpdate/s时Move名义12.5Hz | [Movement:1663](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerMovement.cs#L1663-L1665) IsLocalPlayer分支包住[1746–1752](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerMovement.cs#L1739-L1752)：first.position=Lerp(lastUpdatePos,root,alpha)，third.position=first.position，并写aim父节点 |
| Host看Guest（server、非local） | [Input:1721–1734](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerInput.cs#L1721-L1734) 在FixedUpdate消费队列，不是在ReceiveInputs里直接移动；[1801–1806](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerInput.cs#L1801-L1806)调用原生walking simulate(...RATE)。其中[CheckedMove:1341–1344](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerMovement.cs#L1341-L1344)及[地面/边界修正:1397–1468](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerMovement.cs#L1397-L1468)写权威root | [Movement:1827–1840](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerMovement.cs#L1827-L1840) !IsLocalPlayer路径逐Update写third.localPosition；PRONE为(0,-0.1,0)，其余zero。没有自己的位置Lerp |
| Guest看Host（非server、非local） | [Initialize:2021–2035](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerMovement.cs#L2021-L2035) 非server建NetworkSnapshotBuffer；[Update:1496–1501](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerMovement.cs#L1496-L1501)读current snapshot；[1804–1823](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerMovement.cs#L1804-L1823)非server远端把root.localPosition写为snapshot.pos | 同样third.local=zero，但跟随的是**已由客户端快照缓冲处理的root**，不是Host端直接输入模拟root |

[Input:1648–1654](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerInput.cs#L1648-L1654) 的每FixedUpdate equipment.tock与_clock++不能当成Move频率。50Hz/TPS与12.5Hz输入模拟是不同量；实际FixedUpdate受暂停/卡顿影响，名义数不是本次Host自身root的实测频率。

反向平滑有代码机制，不需要“Host发给Guest更多包”的假设：[tellState:685–696](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerMovement.cs#L685-L696)送入客户端NSB，[getCurrentSnapshot:139–142](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Network/NetworkSnapshotBuffer.cs#L139-L142)确实计算Clamp01因子并调用lastInfo.lerp，而[PitchYawSnapshotInfo.lerp:58–63](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Network/NetworkSnapshotBuffer.cs#L58-L63)对position做Vector3.Lerp。缓冲欠流/追赶仍可能停顿，不保证所有客户端场景始终流畅；本票未采Guest端包密度，不能宣称反向更新更密。

Third的父链并非凭名字猜：[Player:1576–1582](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/Player.cs#L1576-L1582) 远端取root.Find("Third")，first=null；因此零local位置自然继承root。精确站姿偏移仍按原生pose/parent；6338个承载点相等不意味着全骨骼、顶点完全没有动画。

已核的PlayerAnimator没有另一套server远端root位移缓冲；它[1680–1694](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerAnimator.cs#L1680-L1694)取Third上的CharacterAnimator、renderer和Skeleton，负责姿势/动画。该SDK未定位到名为PlayerClothes的源码文件；已定位的[HumanClothes:1554–1568](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Characters/HumanClothes.cs#L1554-L1568)所见位置写是衣物/头部hook局部位置，不是把角色Third吸到root的逐帧写者。最终归因写者就是Movement的远端localPosition归零分支，不需要发明额外follower脚本。

## 3. _tick全部赋值与alpha的因果陷阱

在该SDK PlayerInput类内，`_tick`是[894–895](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerInput.cs#L894-L895)的private float与只读tick getter。完整赋值搜索只有两处（不计CLR默认0）：

| 位置 | 守卫/频率 | server上的Guest是否执行 |
|---|---|---|
| [InitializePlayer:1905–1907](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerInput.cs#L1905-L1907) | 每次原生初始化调用无条件赋Time.realtimeSinceStartup；不是每包事件 | 是；若没有重复初始化，本次实例只在这里赋一次 |
| [FixedUpdate:1524–1544](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerInput.cs#L1524-L1544) | 未isDismissed、channel.IsLocalPlayer且count%4=0 | 否，目标IsLocalPlayer=false |

若强行把远端tick代入alpha=(now-tick)/0.08，初始化80ms后raw alpha通常≥1，Vector3.Lerp会Clamp01到1；**这只是未执行表达式的反事实计算，不是该角色真正的写入过程**。刷新tick也不会让remote突然进入IsLocalPlayer块，何况后面仍把third.local归零。

远端tick还有不可破坏的原生用途：[Input:1729](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerInput.cs#L1723-L1734)以 `simulation > (now+5-tick)/RATE`约束累计模拟提前量。每包重写tick≈now会把允许的累计simulation反复压回约62.5；当累计帧数超过它时可能长期拒绝后续消费。**禁止改远端input.tick、simulation或RATE来“恢复插值”**。插件可新增独立视觉时钟，不伪装成原生输入时钟。

## 4. 这次数据到底确认了什么

来源是 [HOST trace](../../../启动器/UnturnedModManager/publish/UMM-v2.2.1-win-x64/spf-bug1/bug1-20261107T044218Z/HOST/trace.jsonl) 与 [result](../../../启动器/UnturnedModManager/publish/UMM-v2.2.1-win-x64/spf-bug1/bug1-20261107T044218Z/HOST/result.json)。只读复算脚本为 [t61_trace_summary.py](t61_trace_summary.py)，trace SHA-256=`06d9f0062dba150a737949520a405ea217845e4ed1c86e2ac16a771d54afea78`。

| 项 | 复算结果/限制 |
|---|---|
| 行/角色 | 19474行；RENDER_READY=6338，frame7078–13415、epoch=1；四个camera/third/renderer/endpoint验证布尔各6338真 |
| 位置关系 | 6338/6338满足|a_pos-v_pos|≤1e-9，零帧存在记录的承载位置偏移。不是写入调用栈，单靠此式无法区分Lerp(alpha=1)与直接归零 |
| root变化 | 相邻6337个delta，以>1e-9为变化共有234，其中233≤1U、1个273.670673U；233/234与AFTER_DEQUEUE同帧，7/234与AFTER_ENQUEUE同帧。故应说“出队消费帧”，不说“收包时刻直接移动” |
| 精度敏感性 | 两个变化仅0.0000858307U；阈值1e-4或0.001时变化数降至232。不能混用234/233/232的分母；本报告统一1e-9，仅按≤1U另分普通变化 |
| 位移 | 所有渲染delta p50/p90=0、p99≈0.360014U；普通变化3位小数众数0.360（191次）。也有少量0.56等，不能不查姿态就算所有步都恰好0.36 |
| 时间 | 全部1699次出队的相邻mono间隔p50=0.0801401s、p90=0.1098623s、p99=0.1446072s、max=0.6808208s；普通移动短间隔≤0.25s子集p50=0.0802302s≈12.46Hz，不是从帧数换出来 |
| 帧间隔 | 渲染dt p50=17.8417ms、p90=41.62118ms、p99=69.889064ms；该6338点覆盖137.960516s，不能假定实测恒60–75FPS。后文60–75仅是题设估算场景 |
| 有效性 | 113个RENDER_READY valid=false、原因input_association。最终result整体valid=false/Unknown/MISSING_PHASE，expected=observed=7381、missing_fields=4173、strict_m1_events=0。描述统计不是严格M1成立/通过 |

frame9208那次273.67U没有同帧enqueue/dequeue。它与常规输入步明显不同，但缺真实传送/重生/纠正事件绑定，**不能把它确认为传送**，也不够凭一例拟合大跳阈值。当前数据最支持“可见承载位置锁权威输入步”；代码进一步区分为直接跟随，而不是已运行Lerp饱和。结束CONTEXT在frame14459已是epoch=2，而上述可见点cohort全是epoch=1，不能拿末尾上下文给整段补完整性。整个Bug1根因仍需版本锁定及修正窗口后的正式观测，不把result Unknown改成Green。

### 量化：80ms一步有多大

以SDK[4.5/7U/s](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerMovement.cs#L93-L96)、倍率1、恒速平地举例；实际速度必须用本实例技能/倍率。

| 速度 | 每次RATE=.08原生模拟位移 | 真正均匀60FPS位移/帧 | 真正均匀75FPS位移/帧 |
|---|---|---|---|
| walk 4.5U/s | 0.36U | 0.075U | 0.060U |
| sprint 7U/s | 0.56U | 0.116667U | 0.093333U |

80ms周期约占4.8–6个渲染帧间隔：通常几帧位置不变，再一帧前进0.36/0.56U，跳步约为均匀每帧量的4.8–6倍。若真实更新是15–20Hz，则周期50–66.7ms、60–75FPS下约3–5个帧间隔；**不能同时把每次.08s模拟和20Hz持续前进直接当正常4.5U/s**：在全为有效恒速前进且无节流/纠正时会推出1.2–1.6倍速度。RX消息率、enqueue、dequeue与有位移sim是不同量，本次真正mono周期更支持12.5Hz。

## 5. 最小候选的写点：新建纯渲染代理，不搬原生Third

**建议作为0.2.4.11后续独立实现/实验preview推进，不塞回当前preview3，不因发现需要新呈现层就自动把Bug1推到0.2.4.12。** 这是原目标的必要修复候选，不是本票已许可上线的实现。当前件可先交仪表+本报告做定向诊断，Bug1仍未关闭，不能称三Bug实际修复完成。

原树大致为 `PlayerRoot(权威controller) → Third → Skeleton → Spine/四肢(Collider)`，Third还有Model_0/Model_1。源证据：[Animator:1620–1635](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerAnimator.cs#L1620-L1635) 显示骨骼上确有Collider，本地分支才显式禁用它们；[远端:1680–1694](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerAnimator.cs#L1680-L1694)未作相同禁用。本票未验证实机prefab每个collider的运行enabled状态，所以不能把Third称为已证纯视觉节点。

**现成树里没有已证明满足“带着骨骼移动、却不移动同骨骼Collider”的安全子transform。** 移Third或Skeleton会移动其下判定节点；只移Model_0/1的renderer transform不移动它绑定的bones，也不能保证得到所需的完整蒙皮平移。

推荐可写对象明确为新增 `PlayerRoot/SPF_ViewProxy_<generation>`，其下只有视觉Skeleton与重绑到该骨架的Model_0/1及必要纯渲染外观。原Third/Skeleton/Collider/aim/装备逻辑/AudioSource保持原引用与原位置。**不直接Instantiate整个Third再删除脚本**，避免Awake/注册/碰撞先执行；使用只含Transform/Renderer的受控构造或专用纯视觉prefab，资源复用sharedMesh/material，重绑骨骼一次完成。

时机为**原生Movement.Update归零与本帧动画求值完成之后、主相机提交之前的受控LateUpdate**：复制原骨骼局部pose到视觉骨架，设置proxy世界位置为视觉采样值（再包含原生姿态offset/scale）；若proxy父为权威root，则用父InverseTransformPoint求其local位置，不能直接把world误填为local。每frame只应用一次，并在真正camera端核对，不能只凭脚本执行顺序想当然。PlayerAnimator不是归零写者，不替换它的动画算法。

原本可视renderer必须只停止重复绘制，**不得禁用原Third GameObject、原骨架、原CharacterAnimator或Collider**。停止原renderer绘制是否影响原动画剔除/culling/LOD，必须SDK与实机核实；不假定Renderer.enabled/forceRenderingOff在此游戏没有副作用。隐藏原绘制和替代可视目标的接线是本方案的验收内容，不是随意一个开关。

硬门：对同一输入，服务端root/controller、原hitbox骨骼/Collider世界pose与enabled、碰撞/命中/伤害结果、输入序号/原生simulation/tick、对外状态包均不因模块开关改变。只允许proxy的渲染姿态变化。骨骼姿态复制本身不写回原骨架。若无法证明这些量不变，候选失败，不能以“只改了Third”自认安全。

### 代价与更省的替代边界

每目标B根骨骼、R个renderer，初始化O(B+R)对象/绑定，稳态每帧O(B)复制局部pos/rotation/scale，**不是每帧重绑骨骼或复制mesh**。假设B=32–64（只作量级估算，实际须数资产）、2名Guest、75FPS，就是4800–9600次骨骼pose复制/秒；每pose约10个float，纯数据约0.19–0.38MB/s，若复制4×4矩阵约0.31–0.61MB/s。开销主要在Unity属性跨界调用、变换脏标记/层级传播与蒙皮，不是这些字节本身，**不能据此承诺某个ms数**。若替代而非叠画，draw call不必翻倍；原动画仍要运行，资源/GC/CPU/GPU都要profiling。

实施前预算固定为：2Guest稳态额外主线程p95≤1ms/帧、稳态GC.Alloc=0、不持续增长对象/材料、同一可视模型不双重绘制；这是候选验收预算，不是已测性能。超预算不默认打开。

“渲染前临时搬原Third、渲染后还原”几何上能动同一骨架，确实省复制骨架，但**不满足当前已证明的安全边界，不推荐作为最小正式修法**：Transform setter已改变全局共享骨骼/Collider pose，命中/射线/脚本读取不只发生在FixedUpdate；autoSyncTransforms、显式Physics.SyncTransforms或其他渲染回调中的查询都可能看到临时位置。恢复还涉及物理缓存同步，多相机/瞄具/反射、回调异常/取消未配对也不能只靠两个事件保证finally式还原。

阻断点不是“Unity必然每次setter立刻更新broadphase”，也不是“蒙皮不允许移动”，而是**渲染事件不构成全游戏的隔离事务**。这条路线只有在控制全部查询/相机生命周期并证明无观察者看见临时pose时才可另裁决，不能从“物理步进前还原”推出判定不变。两次挪整棵树也会传播B个变换、可能同步Collider，不能未经实测声称必比proxy省B倍。GPU顶点偏移/专用绘制矩阵可能更省CPU，但需要验证原shader、阴影、culling/LOD等另一套渲染接线，不是本票已证明可直接替换的捷径。

## 6. 插值规则、无包上界与重置

首选**有界延迟插值，不做未来外推**。样本来自Host每次有效原生walking simulate正常返回后的权威位置、server simulation序号、实际mono到达时刻；即使该次零位移也记录，以识别停止。不是每个渲染帧把相同root重复当新目标，也不是在ReceiveInputs刚入队时采尚未模拟的位置。

相邻有效模拟样本为(p_i,u_i)，`u_i=serverSimulation_i×RATE`是新增视觉系统的采样域；不写原生simulation。用自有单调视觉时钟u_v，在两已确认样本间线性插值，系数Clamp01((u_v-u_i)/(u_(i+1)-u_i))。实际mono消费间隔用于缓冲与欠流诊断；不把到包抖动直接当速度时钟，避免0.36U/很短到达间隔造成虚假高速。

候选初始延迟推荐冻结为D=0.13s（本次出队p90≈0.110s加典型一帧≈0.018s的量级），不是改变RATE/原生时钟。初始化从**当前已显示位置/当前权威位置**起步，缓存预热不把画面倒回旧p_i；视觉时钟之后每帧前进不超过实际帧dt、不得倒退、不得跳过已知中间轨迹追赶。D是实验参数，需固定后A/B；不能调到隐藏测量失败才报Green。80ms逻辑步仅为正常walking，车辆等另一种步域不套用。

- **没有新包时预测距离上界=0U**：不允许超出最后确认样本的路径外推。可把已缓存的已知轨迹走完，到最后确认位置后保持，记录underflow；不是按最后速度猜客机还在前进。
- **无新有效模拟样本≥0.25s**：标STALE并收口历史，保持当前最后已确认位置。若实际权威被其他合法事件改变立即按该事件重置，不等待0.25s。恢复数据时新建连续段，不补播失联轨迹、不高速追赶；已显示位置不能因重新选旧样本而倒退。若输入突发令逻辑积压 `u_last-u_v > 0.25s`，也立即对齐当前确认目标并重建，记BACKLOG_RESET；不得无限拖尾或提速追赶。该重置本身可能可见，必须单记，不能藏进稳态smooth指标。
- 原地停/断包与现状比较：现状立刻停在最后A；候选从稍落后的显示位置沿已知路径继续到同一个最后A，再冻结，约多一个显示时延。**不是先倒退去上一样本**；若观察到启用/补包时回撤，初始化/时钟实现即失败。稳态走跑更连续，代价是停止显示晚；在大量断包场景并不能“修网络”，可能因延迟/冻结感觉更差，必须A/B，不宣称无包体验必改善。
- 传送、原生位置纠正、生成/重生、死亡、姿态切换（至少蹲/趴）、进出载具/坐下、epoch/连接/模型generation变化、simulation重置、时间倒退/暂停恢复：当帧对齐新权威+原生姿态offset、清历史并重置视觉时钟，禁止跨边界插值拖影。未知大跳只能记UNCLASSIFIED_DISCONTINUITY并安全重建，不把唯一273.67U事件硬认证为teleport。

重置可断言：事件后第一个真正渲染提交点proxy与新原生目标在epsilon内；后续不回到旧样本；原生root/hitbox没有被proxy反写；重复reset幂等、旧generation样本无效。缺真实事件终点时该项UNKNOWN，不靠单纯距离阈值冒领。

不能承诺只做Lerp就永不超调/穿模：源样本间实际转弯可能被直线弦切角，复制的当帧骨骼pose与延迟root可能相位不同产生足底滑动，跳跃/地形与武器火花、影子、队友标识也可能与延迟模型脱节；视图与原生hitbox存在时延视差。D量级下走/跑滞后距离约vD=0.585/0.91U（还含离散采样相位，并非固定误差）。不能为了消掉视差去搬hitbox、改伤害或伪造输入；需移动射击/近战/拾取/角落/台阶回归。**不动权威可避免新增服务端回写式rubber-band，但不能保证任何重同步都没有可见跳变**，超出这条可证范围不得写“无橡皮筋”。

## 7. 两个主指标与可证伪条件

1. **呈现脱钩/平滑指标组**：在有效恒速段统计e=|V-A|、proxy每渲染帧位移及除以(cap×实际dt+epsilon)的比值、停帧段数。e应出现有界的非零延迟分布，不能把“越小越好”误设目标；e始终0说明未生效/被后写者覆盖/采错端点。位移仍成0.36U阶梯说明呈现写点或时钟失败。不得通过放宽原速度阈值来消红。
2. **样本供应/调度指标组**：真正CONSUME/after-sim mono间隔p50/p90/p99/max，加buffer underflow比例/最长保持、渲染dt。若V已有中间位置而仍顿挫，先看欠流与渲染/camera/动画帧节奏；如果供应稳定却e不断增长，是视觉时钟/重置错误；若供应大缺口，单纯表现层不能生成未知真实运动。

反证顺序：实际IL显示Lerp在远端也执行、或目标实际IsLocalPlayer=true→本SDK分支归因必须重开；同点写入观测显示别的组件最后覆盖third→追真正末写者；evidence把a/v取成同一实例/失去renderer归属→本统计对“无偏移”的证明无效；存在server远端root连续插值样本→不能称该运行路径无平滑。骨骼姿势有动画不构成对root平滑缺失的反证。

新增proxy后，**不能继续测原Third当V**然后声称新画面无偏移或已经平滑。仪表须重新绑定到真实绘制代理、验证renderer/bones与目标归属、发布新端点证据；A仍为原权威root。这是新呈现层与只读仪表接线的受影响差量，不准直接把理论p_smooth写进v_pos伪造实测。

## 8. 版本锁定、实施归属与未闭合项

本SDK Movement/Input字节分别为 `4e7685011b6ccef8fed6fd46e79b1ccc3d8e16f68c2e7e0113a5d0af24b9c417`、`ec602f6245b1cce30a3d92cfe418343193b5b8fc4efec375edbb9c3ddf0b9263`。它们不是当前游戏DLL身份。协调消息给定待测SPF为preview3、SHA前缀9fe6a0c8；本脚本未在trace顶层取得spf_assembly_sha256/game_assembly_sha256/game_assembly_mvid字段，所以不把该前缀伪称为已从本trace独立取到完整模块指纹。

实施第一门：从同次UMM模块清单/日志取得实际游戏DLL SHA/MVID与SPF完整SHA；对实际Assembly-CSharp做只读IL核验：Update的IsLocalPlayer包围Lerp、!server快照路径、remote third setter；Input._tick的全部stfld及守卫；列出所有生效Harmony/第三方后写者，并采一次目标角色标志与真实父链/Collider状态。任何不符，先纠正归因，不能照本SDK行号盲贴patch。静态IL部分不需启动新实例；同次patch/层级证据缺失时仍须补采，不能用当前磁盘已更新的游戏版本替代当时运行字节。

**推进建议：归属0.2.4.11的后续独立、默认关闭、可即时关闭还原原生绘制的实验模块**；默认开关关闭不是绕过Bug1验收。进正式修复候选前必须：实际ABI锁定、纯渲染/权威判定不变证明、低层buffer/重置反例可红、独立双轴、同输入实机A/B录像和两主指标、性能预算、站姿走跑及原需求跳跃/转向/急停/纠正等回归。开关关闭时销毁/回收proxy、恢复原绘制和端点，原生状态包/保存不得改变。

本票**不建议未经用户接受就把承诺的Bug1悄悄改为.12**。若阶段条件尚不满足，0.2.4.11当前只可作为“仪表+根因报告的诊断preview”交付并保持Bug1未完成，不能把报告替代实际修复、不能宣布团队目标完成。真正的延期/缩减承诺须另获用户同意，不由本归因票冒签。

本轮实际验证仅包括只读脚本对现有文件复算与SDK检索；没有实现或跑候选。整个result仍Unknown，T60窗口收口/关联修复未由本票验证。后续新模块既改生产又改呈现契约，按[T57](architect-ruling-t57-work-policy.md)仍需双轴，不享受生产零字节的测试单轴例外。
