# 需求规格说明书：Collision Migration Slice

版本：`0.2.4.9`（Metadata Source 在实施时授予；不发 GitHub Release）

决策来源：[Collision Migration Slice 权威边界与共享控制面准入](../map-collision-migration-slice/map.md)（ADR 0009–0014）

## 问题陈述

房主开 P2P 听主机后，客机走进远区，门和场景物件的碰撞时有时无：客机离开后，房主脚下的可采集树会被插件关掉；客机再进来，权限门的动画剔除又可能把人卡住。维护者以为 Collision 已经接到 Spatial Observer Index，测试也绿了，但生产上真正改原生状态的仍是每帧扫描客户端的私有覆盖集。Resource 已经走通租约生命周期，Collision 还在旁边另算一套观察者、另写一套 enable/disable，两套需求还会互相读取。结果是：第二领域无法证明共享控制面可复用，每铺一个新域都要再复制轮询、账本、retry 和故障恢复。

## 解决方案

把 Collision 做成 Resource 之后的第二个真实 Migration Slice。Control Plane 维护唯一的 World Presence Observer 空间事实，用 Demand Projection Engine 按各域 Demand Policy 投影 typed Domain Demand，用 Lifecycle Orchestration Engine 把需求变成 Acquire 与 Release。Collision 只通过 Domain Execution Port 执行租约型 Collision Override，并用 Acquisition Receipt 证明自己有权撤销什么。正式切换在会话边界一次性完成：之后只有这一条插件侧 Collision Authority Writer；旧私有覆盖集、对可采集树的越权写入、以及 Resource 对旧覆盖查询的回退全部退出生产调用图。影子期可以只读对照，但不能当正式证据，也不能在失败时切回旧 Writer。

## 用户故事

1. 作为房主，我想客机走进远区后权限门和场景物件的碰撞仍然可用，以便客机不会被卡住或掉出世界。
2. 作为客机，我想在房主不在的区域开关钥匙门后碰撞立刻跟上动画，以便门不会在房主端被剔除后把我拉回去。
3. 作为房主，我想客机离开我脚下的区域后，我仍能看到并采集身边的树和矿，以便联机不会弄坏我自己的单机可见性。
4. 作为客机，我想远离房主砍树时资源碰撞仍由资源域负责，以便砍树不会因为碰撞域切换而失效或被误关。
5. 作为房主，我不想插件把我自己从碰撞需求里排除出去，以便最后一个客机离开不会关掉我仍站着的区域。
6. 作为待审核的客机，我想我在世界里的空间需求仍按既有 World Presence Observer 规则投影，以便批准前后不会突然少一整块世界。
7. 作为单机玩家（未开 P2P），我不想场景物件和树木的可见性被这套租约改掉，以便单机节奏保持原版。
8. 作为房主，我想两个客机同区时一个人离开不会拆掉另一个人仍需要的门碰撞，以便多人探索不会互相踩掉生命周期。
9. 作为客机，我想短暂走出区域再马上回来时，门和碰撞不会在滞回窗口里闪灭，以便走路不会触发反复开关。
10. 作为房主，我想客机断线重连后旧的碰撞租约不能再改门，以便过期事务不会打到新会话。
11. 作为房主，我想换图或关房后上一局的 pending 释放全部作废，以便新 Session Epoch 从干净状态开始。
12. 作为维护者，我想 Collision 不再扫描客户端名册来自建覆盖集，以便每个新域不必再复制一套观察者轮询。
13. 作为维护者，我想 Resource 与 Collision 各自声明 Demand Policy，由同一套投影引擎计算区域集合，以便半径不同也不会再造第二套枚举和 diff。
14. 作为维护者，我不想把物件半径写成所有领域的默认半径，以便物品域和僵尸域以后仍能用自己的空间政策。
15. 作为维护者，我想共享生命周期编排引擎拥有滞回、retry、补偿调度和局部故障隔离，以便 Collision 不要复制一份资源接缝状态机。
16. 作为维护者，我想资源域以行为保持方式迁入同一编排引擎，以便已验收的砍树和资源碰撞语义不被这次抽象改掉。
17. 作为维护者，我想一个碰撞区域失败只隔离该区域该次转换，以便其它门、其它区域和资源租约还活着。
18. 作为维护者，我不想僵尸逻辑抛错就把碰撞和资源会话一起拆掉，以便外层 Update 不再是跨域熔断器。
19. 作为维护者，我想一条读不全的观察者记录不要冻结所有人的本拍更新，也不要被当成已经离开而释放碰撞，以便加入中的客机不会误关世界。
20. 作为维护者，我想会话身份对不上时能有限恢复或显式熔断，以便不会只打一条日志然后整局失明。
21. 作为维护者，我想 retry 不再按区域单槽互吞，以便两个观察者同区时各自的重试资格还在。
22. 作为测试人员，我想在影子期只看到对照日志、看不到原生状态被新路径改写，以便旧 Writer 仍是当时唯一的生产写入者。
23. 作为测试人员，我不想用「和新旧覆盖集有多像」来判定影子通过，以便房主新增覆盖这种预期差异不会被当成失败。
24. 作为测试人员，我想正式切换候选和影子候选用不同的构建指纹，以便影子跑了几小时不能冒充正式 Runtime PASS。
25. 作为测试人员，我想正式切换后诊断能证明旧覆盖写入次数为零，并且新路径确实在 Acquire 和 Release，以便不再出现「新类型存在、旧路径独自干活」。
26. 作为测试人员，我想 Release 日志能指出撤销的是哪一张 Acquisition Receipt，以便不是按区域无条件关闭。
27. 作为房主，我想客机离开且我也不再需要该区域后，插件只拿掉它自己加过的门动画保活，以便门的开关和锁状态不被历史快照回滚。
28. 作为维护者，我想无法证明所有权时宁可不关，也不要误关，以便不确定状态走非破坏性安全策略而不是旧轮询。
29. 作为维护者，我想失败时不能在同一局切回旧覆盖 Writer，以便第二权威写入者不会借故障复活。
30. 作为维护者，我想这阶段插件版本是 0.2.4.9，以便运行日志、程序集和 Case-ID 能把 Collision 切片与 0.2.4.8 结构基线分开。
31. 作为贡献者，我不想本规格去修队伍、枪声或主机看客机卡顿，以便口述 bug 仍等诊断包。
32. 作为贡献者，我不想本规格去改 Route B 审核或直连广告信息，以便连接与安全仍是另一条线。

## 实现决策

- 版本：本阶段 Metadata Source 授予 `0.2.4.9`，发布通道仍为 Experimental。不创建 git tag，不发 GitHub Release。Case-ID 随版本身份生成。影子候选与正式切换候选必须是可区分的构建指纹。
- 测试接缝：唯一对外行为接缝是 Lifecycle Orchestration Engine。Resource 与 Collision 都是该接缝上的 Domain Execution Port。禁止在 Collision 补丁、私有覆盖集合或协调器的领域分支上建立第二套行为接缝。
- 分层调用：Canonical World Presence Observers → Demand Projection Engine → typed Domain Demand → Lifecycle Orchestration Engine → Acquire/Release → Domain Execution Port → Native State Authority。投影引擎不改原生状态；编排引擎不重新投影空间；执行端口不扫描观察者、不重算需求、不持有第二套编排状态机。
- 观察者事实：Control Plane 维护唯一的 World Presence Observer 空间事实。Collision Demand Policy 必须包含 Host。Guest 资格沿用既有 World Presence Observer，不在 Collision 补丁里自行判断授权。Pending Guest 是否投影世界需求不在本规格改写。
- 投影：共享 Demand Projection Engine 执行区域枚举、世界边界裁剪、需求计数和进入/退出差异。领域只声明 Demand Policy（资格、空间形状、半径来源、Domain Id）。不存在跨域共享的默认半径或需求掩码。需求身份至少是 Domain Id + Region Key。Resource 保持已验收的资源区域半径政策；Collision 以原版物件区域半径的切比雪夫投影为行为保持基线，须用证据验证适用范围，不得升为共享默认值。领域可有独立的 Domain Demand Projection State，但不得复制 Observer Spatial Authority，也不得每域各建会独立漂移的观察者索引。
- 跨层身份：控制面与领域层只使用 Region Key。裸整数编码只允许出现在受控原生边界转换器，不得作为跨层契约。
- 编排：共享 Lifecycle Orchestration Engine 拥有需求聚合、Acquire/Release、Hysteresis Release、身份校验、retry、补偿调度、局部故障隔离和统一诊断。它消费 Session Epoch、Connection Generation、Region Generation，不重新生成或合并这些轴。领域通过 Domain Execution Port 声明 Lifecycle Policy 并执行原生操作。不强制所有领域整区快照；恢复可以是快照、可逆操作、补偿句柄、幂等重试或非破坏性失败。
- Resource 迁入：以行为保持方式把资源生产接缝迁入共享编排引擎。可保留薄的资源门面，不得长期保留第二套通用生命周期状态机。先用表征测试固定当前资源行为，经兼容端口接入，用 StaticIL 证明不再持有第二套编排状态机，重跑资源回归，再让 Collision 成为第二域。Collision Runtime 通过之前，不得宣称引擎已被两个领域证明。不得为了抽象改变已验收的资源半径、滞回和 retry 语义。
- 故障隔离：默认单元是 Domain Id + Region Key + Transition（加上 Session Epoch 与 Region Generation）。Collision 区域失败不影响其它碰撞区域，也不清除资源租约；反之亦然。单观察者贡献失败不撤销其它观察者对同一区域的贡献。`EndSession` 只由已确认的会话终止或不可恢复的会话级身份失效触发。即使仍从同一插件更新入口调用，僵尸、资源、碰撞也必须有独立故障边界；外层共享捕获不得清空其它领域。
- 样本捕获：单条不可用观察者记录不得冻结其它有效观察者的本拍更新。无效样本对应的旧需求进入 Deferred Observer Demand，不得视为零需求，不得因此 destructive Release。只有确认离开、代次失效、会话重置或有界恢复策略之后才能清理其贡献。
- 会话身份：缺失或不一致时必须有限重试或重建、使旧 epoch 的 pending/retry/补偿不得提交、进入可区分的恢复/隔离/就绪状态、超时显式熔断、身份不确定时阻止新写入和破坏性释放。禁止只打一条去重日志后永久静默。持续异常用有界心跳汇总，恢复必须有闭环日志。
- Retry：身份必须与失败事务粒度一致。观察者贡献事务区分领域、区域、观察者、连接代次、会话；聚合生命周期转换事务区分领域、区域、期望转换、区域代次、会话。禁止区域单槽互吞。已被容忍无害化的单槽互吞仍是正式切换阻塞项。retry 耗尽只熔断对应事务，不触发旧 Writer。
- Collision 执行：成功 Acquire 必须产生 Acquisition Receipt，绑定领域、区域、会话代次、区域代次、Acquire 代次，并列出实际取得的 Collision Override 及可安全撤销方式。新 Acquire 使旧 receipt 失效。最终提交边界必须拒绝陈旧会话/区域代次、失效的期望转换，以及与当前 receipt 不一致的命令。执行端口不重新扫描观察者。
- Collision Override：只覆盖插件侧 LevelObject、门动画、相关 Collider 或 culling 保活。门的玩法开关和锁不属于 Collision。可采集树和矿石不属于 Collision 操作集合，也不进入 Collision receipt。Release 撤销当前有效 receipt 仍持有的 Override，把最终状态交还 Native State Authority，不是整区 disable，也不是对 enable 做机械反操作。无法证明所有权则非破坏性失败，进入有界可观测的协调或隔离。
- Host 与 Release：canonical observer facts 显示 Host 仍贡献 Collision Demand 时，编排层不得发出最终 Release。执行端口若收到与当前 receipt 或需求身份矛盾的 Release，拒绝提交并报告不变量违反。
- 切换：影子期旧 Writer 仍是唯一生产 Writer；新路径无副作用。影子比较按差异分类，不把旧仅远端覆盖当金标。正式候选构建在插件启动或新会话建立时确定唯一 Collision Authority Writer。不做运行中按区域、玩家或比例切流，不保留旧 Writer 开关。任何可部署候选构建不得包含两条可写生产路径。回滚是结束会话、部署上一份已验收构建、建立新 Session Epoch。
- 退役完成条件：旧覆盖扫描、旧集合、旧刷新写入、资源对旧覆盖查询的回退、生产影子比较全部退出生产调用图。旧算法若保留，只作为测试夹具或离线对照。完成条件是新 Writer 有效且旧 Writer 生产调用为零，不能只证明新路径有日志。
- 诊断：转换日志至少能关联领域、区域、转换、会话代次、区域代次、尝试次数、结果和原因；观察者贡献再加观察者与连接代次。Release 必须能指向具体 Acquisition Receipt。诊断不得成为第二套 Writer。
- 注册：接入第三域只注册 Demand Policy、Lifecycle Policy 和 Domain Execution Port，不在共享引擎增加领域分支。Registration Closure 后本次会话的领域集合不可变。
- 明确不冻结：共享引擎类名、泛型参数、文件布局、协调器是否改名、补偿采用快照还是可逆操作、公开 Capability Lease 数量、心跳秒数、retry 键的具体类型、提交切片。这些由实施票在不违反本规格不变量的前提下选择。

## 测试决策

好测试只断言对外行为与证据类允许的结构形状，不断言共享引擎的类名或文件路径。一类 PASS 不得升级成另一类。唯一测试入口保持现有分类输出顺序。先例：资源生产接缝的纯内存生命周期契约、空间索引差异契约、碰撞账本代数、资源/碰撞结构 StaticIL、单点补丁计数契约。

- PureMemory（最高接缝 = 生命周期编排引擎）：0→1 Acquire 与 N→0 Release；两秒滞回与滞回重入；多观察者同区/跨区；资源与碰撞对同一区域的需求独立；Host 贡献使碰撞需求不得在 Host 仍在时归零；Deferred Observer Demand 不产生破坏性释放；单区域/单领域失败不影响其它；旁路异常不触发跨域会话结束；retry 不互吞；陈旧代次不能提交；Acquire 产生唯一 receipt，新 Acquire 使旧 receipt 失效；无 receipt 的 Release 不 destructive；重复 Release 幂等；影子计算不改 Fake 原生状态。资源表征测试必须在迁入前后保持已验收语义。
- StaticIL：碰撞补丁不再扫描客户端名册建立私有覆盖；旧覆盖写入退出生产调用图；碰撞不写可采集树；资源路径不再查询旧覆盖谓词；原生碰撞写操作只有执行端口一条生产入口；执行端口提交边界校验代次与 receipt；资源不再持有第二套通用生命周期状态机；Registration Closure 后领域集合不可变。结构归属先例可延伸，但必须增加旧 Writer 退役与跨域读取退出的调用图断言。
- BuildArtifact：程序集版本与身份为 `0.2.4.9`；影子候选与正式切换候选指纹可区分；独立核验 SHA-256 / MVID / Case-ID。
- Runtime：必须来自正式切换候选，且三端同一哈希。至少覆盖 Host 独在、Guest 独在远区开关门、Guest 进入后再离开 Host 区、两 Guest 同区一人离开、两 Guest 跨区、滞回重入、断线重连、会话切换、Host 脚下树不因远端离开而消失、远区砍树仍由资源域执行、陈旧 receipt 的 Release 被拒绝、retry 耗尽不触发旧路径、旧 Writer 调用数为零、Release 日志指向 receipt。影子 DLL 日志不得当作本条 PASS。Horde/Beacon/单机非 P2P 不作为本切片专项场景，但本轮不得改变它们的原有语义。

## 范围外

- Item、Zombie、Animal、Vehicle 的 Migration Slice。Animal 仍须先设计决策，禁止直接接 SPI。
- Listen-Host Dedicated Gate 与 Join Routing 已关单工作。
- Route B 审核 fail-open、白名单中途关闭、隔离信号、OwnerPrefix 反射、握手容量。
- 直连/DNS 广告信息、IPv6、DNS 策略。
- 09-17 口述三条（队伍不跨局、主机看客机卡顿、枪声）。
- 主机侧僵尸/动物 tick 切片性能、载具物理 dedicated 门。
- 把「并入本地玩家覆盖或去掉树 disable」当作本切片替代方案。
- 公开拆分门动画与静态 Collider 的 Capability Lease（执行层按变更粒度记 receipt 即可）。
- GitHub Release 与 git tag。

## 补充说明

实施票见 [issue.md](./issue.md) 的九张垂直切片。版本身份与资源表征合在 01；投影（02）与生命周期编排（03）分开；Collision 只读影子（05）与准入修复（04）并行，不被准入门阻塞；Collision 执行端口（06）与原子切换退役（08）分开，二者之间由 Go/No-Go 票（07）汇合；切换与旧 Writer 退役必须同票。正式切换候选必须同时满足准入门与旧 Writer 为零。Wayfinder 子票保持归档，不作为生产接线证据。

