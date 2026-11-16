# Bug2：生产枪声链路、新仪表与最小音频叶补偿规格

> T13／gpt-architect／2026-11-06。**只读规格，待独立双轴审查；不是根因已实测或修复已交付。** 本任务仅新增本文件，不改生产、不运行游戏、不部署DLL。后续Builder必须先实现可审计仪表并取得真实Red，才可启用补偿。

## 1. 冻结事实、禁止路线与执行分批

承接 [requirements-v2 §4](requirements-v2.md#L50-L65)（SHA `17fc427c0e6ed115b680eedfa3119191c558609947051fc241facc4ebb94017e`）和 [architecture-v2 §2](architecture-v2.md#L19-L46)（SHA `d1d7ab31e9c4de41c0678b69ac68976a2e4910511c340a29766978a374b42339`）。唯一确认缺陷是 **Guest普通开枪→Host听不到**；脚步/跳跃/近战/命中/医疗/食物/饮水已能听到。Guest↔Guest仍UNKNOWN，不能由双人材料宣布正常。

“SPF删除原版枪声音频补偿”不是已证根因，本票不重开它；不重造枪声协议，不改generic remote helper、不伪装dedicated，不二次调用 `shoot/project/ReceivePlayShoot`，不以新建假播放器或换成通用音效池冒充修复。两个既有UMM包没有音频点，**不再靠重读它们判音频Red**。

执行批序：A只读仪表/ABI与正控→B真实生产入口基线10发→C按§5分流→D仅H1成立才实现音频叶候选→E同场景原生Red/候选Green、负控和S1–S4真机。仪表和候选分别双轴审查后才可部署；不把本规格视为部署授权。遵守 [ADR0021](../../docs/adr/0021-spf-native-world-sync-and-gameplay-mod-channel-boundary.md)，继续原生协议，无新增LMN依赖。

本票因“只读、不跑游戏”明确不执行诊断技能的运行复现/修复阶段；目标是交付可变红的真实接缝和判定协议，不声称已完成该反馈循环。已知CLR尝试未到达真实音频终点，见 [宿主边界](../matt-orchestrate/spf-runtime-evidence-20261031/net/clr-probe/result-and-blueprint.md#L5-L12)，不能把ECall/fixture NRE当玩家症状Red。

## 2. 完整链路：读码事实与运行待证分开

以下 **S=本轮读码**，不是运行事件；**V=后续必须运行核实**。SDK代码可读但可能与当前游戏ABI有偏差。已有原生IL校准游戏MVID `54793ea7-706c-40bf-9e5a-e3a126378d7c`，音频叶token `06004EBA`、fire `06004EBF`，仅作追溯，不凭token跨版本安装；见 [原生IL材料](../matt-orchestrate/spf-runtime-evidence-20261031/net/clr-probe/blueprint-il.txt)。Builder记录实际运行game/SPF SHA+MVID、方法签名+完整IL摘要、patch owner/优先级/外部hooks后重新对齐。

| 阶段 | 文件/行号与读码行为（S） | 运行验证（V） |
|---|---|---|
| Guest按键→攻击意图 | [Equipment:3071–3137](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerEquipment.cs#L3071-L3137) 读取按键、累积pressed/released；[3309–3337](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerEquipment.cs#L3309-L3337) CaptureAttackInputs生成Start/Stop | 确认UI/忙碌状态未吞输入；一次按键不等于一次成功fire |
| 输入请求→Host | [Input:1571、1629–1713](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerInput.cs#L1571-L1713) 本地捕获、预测并用Reliable SendInputs；[1397–1484](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerInput.cs#L1397-L1484) Host ReceiveInputs解析/拒绝过序；[1747–1810](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerInput.cs#L1747-L1810) 消费packet.primaryAttack并equipment.simulate | 记录clientSimulationFrameNumber、Host实际消费及server simulation；不将最近收到的packet误认本次消费 |
| useable与武器状态门 | [Equipment:2609–2655](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerEquipment.cs#L2609-L2655) 调startPrimary/simulate；[Gun:3166–3210](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs#L3166-L3210) 检查忙碌/重装/保险/切镜等；[Input:1864](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerInput.cs#L1864-L1867)→[Equipment:2810–2825](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerEquipment.cs#L2810-L2825)→[Gun:4547–4552](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs#L4547-L4552) tockShoot | 记录clock与输入意图关系，不能假定startPrimary即枪口声音 |
| 成功发射候选→fire | [Gun:4431–4521](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs#L4431-L4521) 取消/间隔/弹药门后调用fire；[880–914](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs#L880-L914) 原生扣弹并走server广播锚点 | 记录锚点到达和fire正常返回/异常；锚点后仍可能异常，不把未完成fire塞进成功分母 |
| 广播与resolved recipients【第一优先】 | [Gun:912–914](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs#L912-L914) Unreliable SendPlayShoot.Invoke；[SteamChannel:160–178](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Provider/SteamChannel.cs#L160-L178) 枚举Provider.clients，排host/owner、player=null及球外，返回真实PooledTransportConnectionList | 直接取本次返回的集合与Invoke实际入参，记录每个真实连接、Host/owner资格、距离与排除原因；不能另算一份“预期集合”冒充实际收件人 |
| 原生RPC与SPF发送 | [ClientInstanceMethod:110–114](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/NetInvokable/ClientInstanceMethod.cs#L110-L114) 写NetId头→SendAndLoopbackIfAnyAreLocal；[SPF:378–468](../../Core/Patches/ClientMethodLoopbackPatch.cs#L378-L468) P2P gate下逐远端Send，列表有真实loopback才回环 | 确认gate和生效patch，实际每次send/loopback次数/异常；空列表不应凭空制造回环 |
| 收端派发→实例 | [远端Reader:15–44](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/NetMessaging/ClientMessageHandler_InvokeMethod.cs#L15-L44) 按methodIndex派发；[本地回环:170–204](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/NetInvokable/ClientMethodHandle.cs#L170-L204) 带Loopback origin；[生成Reader:132–152](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/NetGen/NetInvokable/UseableGun_NetMethods.cs#L132-L152) ReadNetId→NetIdRegistry.Get→ReceivePlayShoot | 捕获Remote/Loopback origin、方法、NetId实际映射、实例generation和失败门；无实例不能伪造对象播放 |
| 收端播放门 | [Gun:867–875](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs#L867-L875) ONLY_FROM_SERVER，EquipAnimationFinished才shoot；[780–785](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs#L780-L785) source存在才playGunshot | RPC入门、动画门通过、音频叶三者分开计数；shoot有其他副作用，禁止诊断手动补调 |
| 本机原生其他播放入口 | [Gun:918–933](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs#L918-L933) 普通远端server不直接shoot；[958及1195–1198](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs#L958-L1198) 本地owner普通枪自行shoot；[project:509–514](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs#L509-L514) 自带音频，远端project本机路径在[1316–1332](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs#L1316-L1332) | 全部原生入口都要纳入Host音频覆盖观测；普通枪结果不能外推projectile、turret或自射 |
| 音源建立→音频终点 | [equip:3269–3291](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs#L3269-L3291) 非dedicated，远端在thirdModel建AudioSource，custom rolloff/default mixer；[playGunshot:739–765](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs#L739-L765) 选clip/枪管耐久/音量/距离/随机pitch，clip非空才实际PlayOneShot | 分开记录leaf入门、真实PlayOneShot调用前/正常返回/抛异常；API被调用不等于Host耳朵已听到 |

注意SDK [ClientMethodHandle:97–123](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/NetInvokable/ClientMethodHandle.cs#L97-L123) 尚含“listen server未支持，non-dedicated连接全作local”的兼容假设；SPF修正传输分派。不能把这一旧假设当成已验证的通用枪声音频补偿，也不能反向恢复它来吞远端发送。本票只记录真实生效路径，不推翻T2定性。

## 3. 为什么能“只有枪声异常”：三条差异候选

用户已确认其他音效正常，排除了“Host整体没有声音”的粗假设；**不排除枪专属路径/音源损坏**。以下是可解释非对称性的读码候选，不宣称现场已选中根因。

| 优先级/候选 | 结构差异与可检验预测 | 最小证伪 |
|---|---|---|
| H1 本机覆盖缺口（先查resolved recipients） | 普通枪remote helper排host且远端server不shoot；反例：消耗品[134–150](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableConsumeable.cs#L134-L150) **先consume再广播**，[99–107](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableConsumeable.cs#L99-L107) 非dedicated直接playSound，所以同样remote排host仍能听到。近战挥动[1035–1048](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableMelee.cs#L1035-L1048) 也有本机声音时序；近战命中[491–495](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableMelee.cs#L491-L495) 用包含owner/client的集合，[接收:114–143](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableMelee.cs#L114-L143) 单独播命中声。预测：Host有完整成功fire但本机原生leaf/API=0，而同环境消耗品/近战正控正常 | 同shot真实原生leaf>0立即证伪“本机未调用”并禁补；仅列表缺Host不足成立，详§4/§5。Host在真实列表内则查实际loopback/reader，不能仍声称“列表遗漏” |
| H2 枪专属音源/clip/挂点/custom curve失效 | 枪不是通用池：远端thirdModel下自建source、自定义rolloff、枪管可覆盖clip和volume。脚步[815–839](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerMovement.cs#L815-L839) 与落地[778–803](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerMovement.cs#L778-L803) 本机OneShotAudioParameters；消耗品的[Player.playSound:367–381](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/Player.cs#L367-L381) 也走该通用路径。预测：原生leaf有，枪source缺失/禁用/parent不可用、clip空/volume零/curve衰减零等，其他声音可正常 | 同一成功shot原生leaf=0且source/预期clip/可听环境完整，先选H1；原生API正常1次但仍无声，则H1停止、深入H2输出链，不再补声 |
| H3 枪专属收端/武器状态门 | ReceivePlayShoot需实例匹配+EquipAnimationFinished；tock/fire另有保险/延迟/换弹/忙碌门。步/跳落地来自[本机movement:1633–1653](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerMovement.cs#L1633-L1653)，不依赖枪RPC/武器动画门。预测：输入存在但未成功fire，或已分派的枪RPC被动画门/NetId实例拒绝 | Host完成成功fire且本地不需要RPC就缺leaf时，不把它写成丢包/动画门；RPC到实例、门通过且leaf/API仍有则转H2。不能通过强开动画门/假造成功fire补声 |

“跳跃声正常”可能包含落地/动作音；本轮只读已定位落地分支，不强认用户听到的一定是该clip。“击中物体”也不能全等同近战命中：表中是已查明的结构对照，具体实机命中类别按原生clip/事件另记。医疗/食/水以消耗品结构作候选解释，仍逐项做S4，而非把同基类当实测。

## 4. 新仪表：先收件人解析，再验证本机终点覆盖

### 4.1 resolved recipients 的第一优先观测

沿Host同一真实fire调用，在 [GatherRemoteClientConnectionsWithinSphereExcludingOwner返回](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Provider/SteamChannel.cs#L160-L178) 与SendPlayShoot.Invoke入参两处，**分别对真实返回值与真实入参做只读快照，不预设它们是同一集合**：entry序号/连接类型/连接别名/是否loopback/对应Provider.clients角色、列表数/Host包含/owner包含、枪口position/radius以及比较结果。Provider.clients映射只是解释每个实际连接；不让它替代实际列表。并列记录实际target mapping，不能从客户端ID猜Host连接。

直接取调用点返回值或在helper Postfix读 `__result`，以当前fire scope限制；随后在ClientInstanceMethod该具体list Invoke入口核实列表快照与方法目标确为ReceivePlayShoot。**命名与优先级固定**：`helperReturnedRecipients` 是某次实际helper返回时快照，必须带helper声明类型/完整名称/调用者/ShotKey；`resolvedRecipients` **仅指本次SendPlayShoot.Invoke实际入参快照**，以它裁决目标集合；二者不同，以后者为准，前者只作集合变换差异证据，不合并、不取并集。`appliedRecipients` 另记实际每个tc.Send与loopback执行，不能从目标集合推断成功送达。

原生枪当前调用的是 `GatherRemoteClientConnectionsWithinSphereExcludingOwner`；`GatherOwnerAndClientConnectionsWithinSphere` 是本票近战等对照路径，不得把它的返回值贴到枪的resolvedRecipients。若运行时活跃patch使枪实际改走GatherOwnerAndClient或其他helper，则记录真实helperKind与返回值，但仍以SendPlayShoot.Invoke实际入参为准；不得为满足断言主动改helper。不同调用者/ShotKey的集合即使内容相同也不是同一证据。

若走间接分派，按真实 `ClientMethodInfo` 声明类型/方法+NetId核验再取传给SendAndLoopbackIfAnyAreLocal的实参；如该后续实参/实际send又变化，独立记录dispatch差异和最早变更点，不覆盖先前resolvedRecipients来掩盖中途改写。不得为了观测再次调用helper（会分配池列表并得到另一个时点）、改变/排序集合或持有已Dispose的池引用；立即拷贝有界元数据，保持原生调用次数与顺序。不能消费一次性枚举器来截流。

断言先做：①shooter=Guest、Host角色非零且loopback/local/Provider.user一致；②actualRecipients是否含Host；③实际send/loopback与列表匹配。**但 `Host ∈ remote-only-list` 不是该原生API的契约**：缺Host首先登记 `EXPECTED_REMOTE_EXCLUSION`，不是独立Red。消耗品同样排Host却先本机播放，已构成反例。因此正确完整断言是：`HostEffectiveCoverage = 可到Host的实际原生RPC路径 OR Host本机原生音频叶路径`。覆盖集合必须包含Host，不能强迫remote helper包含Host。

机器可判的一行（所有输入已知且同ShotKey）：`HOST_COVERAGE = hostInResolvedRecipients || (nativeHostLocalLeafCount > 0 && nativeHostActualSubmitCount > 0)`。其中resolvedRecipients仅为上述实际Invoke入参；localLeaf是Host原生playGunshot入门，actualSubmit是同scope真实PlayOneShot正常返回，不含补偿。任一输入缺失则整个判断UNKNOWN，不把null当0。该断言只判断结构覆盖，不代表收件人已收到/声卡已输出，更不单独关闭S1/S2。

若实际列表有Host却未进入本机reader，优先查send/loopback映射；若列表按契约排Host且相同成功shot的Host原生leaf/API都为0，**再结合§5排除项**，才把H1置为命中并将H2/H3降为非首要。严格准入一行：`H1_ELIGIBLE = captureComplete && successfulRemoteOrdinaryFire && !hostInResolvedRecipients && nativeHostLocalLeafCount == 0 && nativeHostPlayOneShotBeforeCount == 0 && nativeHostActualSubmitCount == 0 && sourceAndEnvironmentValid && noOtherHostRoute`。覆盖布尔为假本身仍不足授权补偿；静态list缺Host只授权加证据，不授权改helper。这是最短可证伪路线：一张真实集合快照加本机音频终点计数及排除项，不必先逐层搜每台机器。反之，Host本机已有leaf即证伪“缺本机调用”，即使actualSubmit为0也要停止补偿、查音源，而不是据覆盖断言为假重复播。

### 4.2 仪表采样点与关联身份

默认OFF，观测与补偿两个独立开关，首轮 `ObserveOnly=true, Supplement=false`。仅显式选定的一名Guest/武器和Host；基线建议90秒上限、10个有效普通单发，最多10000事件/2MiB，越界即停止该段并 `INCOMPLETE`，不得采样丢音频叶后推论zero。写盘在观察窗口外或有界队列，I/O失败不改原生return/参数/异常。开关启用失败/签名不合/计数点未装全不允许开始Red采样；不能仅日志打印“registered”。

| 点 | 最少字段/作用 | 不可冒认 |
|---|---|---|
| P0初始化/装备 | game/SPF身份、source与thirdModel实例generation、GunAsset/clip/配件身份、安装owner与实际原生方法/IL锚点；当前线程与epoch | 未初始化source不自动分配一个“能响”的对象 |
| P1输入消费/状态门 | clientSimulationFrameNumber（若有）、server simulation/clock、primaryAttack、EquipAnimationFinished/busy/reload/safety、真正使用的packet关联 | 单发不强制对应一个按键；最近RX不是当前consume |
| P2 fire成功锚点 | Host本地fire序号、gun NetId/实例/装备generation、普通/projectile、本地owner、扣弹前后及整个fire返回/抛错、门控快照 | 弹药变化和startPrimary不能代替完整成功发射；锚点后异常单列 |
| P3集合/派发 | §4.1的实际returned list与Invoke list、method info、send/loopback、异常、Reader origin/NetId映射/动画门 | 发送成功不等于已播放；无码包不重写原生协议加shotId |
| P4音频叶 | 所有原生 `playGunshot` 入/出/异常，调用来源shoot/project/RPC/其他、source/clip/预选枪管状态；补偿来源另标 | leaf进入但clip空或source失效不算成功播放；任一原生leaf已到就停止补偿路线 |
| P5真API终点 | 实际 `AudioSource.PlayOneShot(真实clip)` 的before/正常return/throw及相同source/clip/pitch/volume/rolloff；DSP时间只作辅助 | 不用 `isPlaying` 推断每发次数（OneShot可重叠），正常return不等于DSP/耳朵确认 |
| P6环境/连续性 | listener/source/距离/滤波/静音/剔除、epoch/断连/切枪/重生/场景变化、事件丢弃/观测异常计数 | 旧source/旧epoch不能拼成新shot的完整链 |

机器计数字段固定且只对同ShotKey聚合：`nativeHostLocalLeafCount`=origin为Native的P4入门次数；`nativeHostPlayOneShotBeforeCount`=相同scope的Native P5调用前次数；`nativeHostActualSubmitCount`=相同Native P5真实正常返回次数。补偿调用仍执行原生方法，但scope origin明确标Supplement，分别记 `supplementLeafAttemptCount` 与 `supplementActualSubmitCount`，不混入三个native字段；候选总播放API数另为nativeActualSubmit+supplementActualSubmit。before存在而return缺失/throw不计成功submit。无法归属的API事件保留UNASSOCIATED，不摊入某个shot。

`captureComplete`必须由安装覆盖、所选对象/epoch一致、零drop/观测异常、调用前后闭合及全部必需字段可用共同推出；`sourceAndEnvironmentValid`由§4.3逐项排除结果推出，不能用常量true。缺字段是null+原因，不是0或false；禁止把统计模板的默认零当完成计数。上述计数只是派生视图，raw事件必须保留可重算。

稳定去重身份 `ShotKey = (hostSessionEpoch, shooterConnectionGeneration, shooterAlias, weaponEquipGeneration, gunNetId, gunInstanceGeneration, hostFireOrdinal)`。hostFireOrdinal在真正fire锚点分配且单调（溢出fail-closed），每次发射都不同；同一帧多发/自动/三连发不能只按frame/time/ammo去重。InstanceID/NetId可能复用，必须带generation。输入sequence/clock另作因果字段，不冒充唯一ShotKey。

Host trace内以真正调用栈scope关联fire→send→本机leaf/PlayOneShot；以线程局部有界scope栈避免嵌套/同帧交叉。跨端RPC原生 [write无额外shot负载](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/NetGen/NetInvokable/UseableGun_NetMethods.cs#L154-L157)，不可假设有包内shotId。远端只在单发隔离窗口、同weapon/epoch与唯一匹配ordinal下关联；丢包、重排、多个候选或异步音频无法唯一归属时标 `UNASSOCIATED/UNKNOWN`，不按“最近一发”强绑。

P5若不能给extern Unity方法装可靠hook，可在**实际playGunshot的PlayOneShot调用点**插入只读before/after标记，保留原CALL与原参数；记录异常且保持原传播语义，不能换一套播放器。ABI锚点不唯一、外部patch改路、正控未证明观测覆盖时，零计数没有证据效力。调用点标记只能证明原生API调用，不伪称声卡端已输出。

每段导出manifest+JSONL：schema版本、artifact身份、冻结场景/阈值、对象映射/生命周期、raw记录数+漏记录数、安装覆盖、capture start/end/结束原因。每事件包含事件ordinal/phase/ShotKey或关联状态、frame/unscaledTime/dspTime（可用时）、来源native/supplement、采样值/缺失原因；不用字符串拼接日志全文作事件事实。所有连接/对象别名可会话内化名；不记票据/认证payload/Steam凭据。

### 4.3 必须连带排除的假阳性

每个有效单发之前/播放点/之后记录；运行时不支持取得的项明确缺失，不默认“都正常”：

- **距离与遮挡**：监听器→真实source世界坐标距离，枪口/玩家坐标作对照；source min/maxDistance与完整custom rolloff关键帧/配置摘要、spatialBlend、mixer/group、实际pitch/volume；同姿态无遮挡、无介质/墙体切换，记录低通/高通/自定义遮挡组件/路由。不要假定Unity自动做物理遮挡；项目/其他mod若做则查该实际链。初始固定10±1U直接视线，先验证所选枪原生曲线在该距离非零。
- **音量/静音/输出**：source.enabled/mute/activeInHierarchy、listener.enabled/唯一active listener、AudioListener.volume/pause、游戏总音量/效果音量、输出设备/系统mixer静音与焦点/后台音频策略；记录可读值，系统输出状态由真机截图/操作者确认补证，不读无关隐私设置。正控必须在同一listener/同一设备/相同窗口有效。
- **实体/音源剔除**：角色/thirdModel/source的alive、active、parent、renderer可见/剔除状态、source位置与culling/virtualization信息（若API可用）；本机玩家/实际source状态，不用旧Culling聚合代替。source已失效、引用旧装备、实体卸载/不可用时，该发不得作为“需要音频补调”的Red。
- **武器资源与成功性**：gun/配件/预期clip非空且已加载、state[16]消音耐久、装弹/保险/动画门、非projectile分支、真实成功fire，听感窗口内无其他枪/爆炸/菜单音遮蔽。原生leaf有而clip空/volume零/距离衰减为零→H2，不补。

## 5. 真音频终点 Red：失败标签、正控与分流

### 5.1 基线Red的充要取证门（不是现场结论）

在已审查的ObserveOnly仪表下执行**真实Guest按键→原生输入请求→Host实际fire**，普通非projectile枪连续10个有效成功单发，每发间隔至少1秒、逐发编号并隔离其他声音。单发后观察1秒为采样协议，不把它写进游戏逻辑。若原生事件/画面/输入仍在尾部推进，则延长采样并记protocol override，不将观察窗外事件当不存在。

对每发，H1型Red必须同时满足：

1. `ShotKey`唯一，P1/P2证明真实server远端成功fire已完成；原生玩法副作用按该fire真值记账，不以一项ammo变化替代。
2. P3确证真实resolved recipients与分派，Host被remote列表排除且没有其他真实Host RPC覆盖。若实际列表包含Host但路径在具体派发点断开，则另记DISPATCH_GAP型Red，不属于本H1，不批准叶补偿。
3. 完整P4/P5覆盖中 `nativeHostLocalLeafCount == 0 && nativeHostPlayOneShotBeforeCount == 0 && nativeHostActualSubmitCount == 0`；不是probe未装、对象未选中、采样丢失或关联不明。leaf已入即本机调用存在，退出补偿路线。
4. §4.3排除项完整、真实source/预期clip/可听环境有效；不处于重生/切枪/卸载/加载/ESC状态切换。音源不可用不能通过补调绕过。
5. 同一录制窗口的**真实Host自射同枪**正控命中P2/P4/P5且可听，另一个已正常的真实消耗品/近战正控可听，观测日志零drop/错误；正控不能由测试手动调用playGunshot/PlayOneShot。必要的每阶段hook安装/对象覆盖由独立检查证明，不能因为正控过一个分支就假定其他所有分支完整。
6. 录音/操作记录确认Host对应Guest枪声确缺失，或SDK真叶计数构成**组件级API覆盖Red**；两种证据等级分别登记。仅SDK没有听感时不冒领S1实机Red。

满足前五项的单发可变红，10发组成需求对应分母，逐项公布（不得只挑缺声的一发或把失败输入删除不记）；最后一项区分组件Red与用户症状Red。若只有若干发满足，缺失者逐发UNKNOWN，不以“0/10”抹掉观测缺口。

### 5.2 第一分歧与停止树

| 真实数据 | 标签 | 动作 |
|---|---|---|
| remote列表排Host，其他数据未采齐 | EXPECTED_REMOTE_EXCLUSION / UNKNOWN_COVERAGE | 先补本机leaf与排除项；禁止“列表无Host即Bug” |
| remote排Host、本机leaf/API0、成功fire和环境/覆盖完整 | H1_HOST_AUDIO_COVERAGE_GAP | 才允许§6局部叶候选；其他候选降低优先级而非宣告永远不存在 |
| Host在真实列表却send/loopback/reader断开 | DISPATCH_GAP | 给具体方法、owner/连接证据另作最小派发调查；不能修generic helper或直接补叶掩盖 |
| 原生leaf≥1，无论PlayOneShot是否调用/成功 | BASELINE_LEAF_PRESENT_STOP_SUPPLEMENT | **停止补偿**，按source/clip/状态门/调用异常转H2；不能见“没听到”就再播 |
| 原生PlayOneShot正常返回1而无声 | API_PRESENT_AUDIBILITY_UNKNOWN | 查listener/mixer/曲线/遮挡/voice virtualization/输出，原生播放API不等于最终声卡/耳朵 |
| 无成功fire，或RPC被NetId/动画门拒绝 | WEAPON_OR_RECEIVE_GATE | 先定位输入/状态门，不能伪造一次fire/强开动画门 |
| 缺身份/丢样/跨端关联多解/Unity接口不可用 | UNKNOWN | 修仪表或补运行环境再收证，不写Red/Green |

不把计数0与异常路径混合；`PlayOneShotBefore=1, Returned=0` 是实际调用异常，不是缺调用。每个stop标签附最早断点、source/clip身份、原始事件ID和后续最小仪表；不以总PASS覆盖UNKNOWN。

## 6. 最小叶补偿：启用契约、幂等账、判停与随机流硬约束

### 6.1 触发位置与范围

仅在**该ABI/普通枪场景的H1真实Red已被独立审查接受**后启用；新增独立registrar，精确匹配fire的服务端成功SendPlayShoot锚点，原生send不删不重排。使用锚点处真实武器/source/配件快照，必须先于原生远端枪管耐久递减（[1203–1216](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs#L1203-L1216)），不能在整个fire Postfix补播最后一发的错误音色。

实时门控全部真：P2P listen-host+server+非dedicated、唯一有效本地Host身份；shooter是当前获准连接的非本地Guest、目标对象/装备generation仍活跃；本次真实原生分支是非projectile且原生不会本机shoot/project；source/clip与§4环境有效；所有必需hooks/归属链已就绪且无冲突patch；该ABI/武器模式没有已知后续原生重复叶。不得只按 `!IsLocalPlayer` 猜分支，不覆盖Host自射/Guest自射/projectile/无效输入/菜单/断连/旧对象。

因补声锚点在fire返回之前，运行时只能确认原生发射门/广播锚点已到，不能预知整个fire未来是否抛错。P2在原生返回后再提交 `successfulRemoteOrdinaryFire` 事实用于Red/Green分母；若锚点后原生抛错，该shot保留为 `FIRE_ABORTED_AFTER_ANCHOR`，本段候选失败/停止并交最早异常，不重试、不删除这条已有补声记录，不把它伪算成功。不得把“等待完整fire返回后再补”的不可能条件和前述耐久时机同时交给Builder。

在本机原生SendPlayShoot锚点附近只额外调用一次**原生playGunshot**；绝不二次shoot/project/ReceivePlayShoot，否则shot-count、弹壳/火花/轨迹/飞掠、耐久与mod hook都可能重复。原生voice/mixer/clip/曲线逻辑原样，音源若不存在不能自建一个备用对象。未来修改third/武器表现挂点时单独回归枪源世界坐标，不把听感修复写到权威枪口/弹道位置。

### 6.2 幂等与去重键（at-most-once，不承诺跨未知原生路径exactly-once）

使用§4 ShotKey，一个capture/session有界ledger，状态只前进：`Observed → NativePresentStop / Eligible → Claimed → Submitted / SupplementFailed / Invalidated`。`Claimed`必须先于额外叶调用；同key重复hook、递归/重入只返回no-op，失败不释放claim、不重试，不用延时去重timer。不以clip/source/frame相同吞掉合法第二发；并发/同帧burst另有hostFireOrdinal。

为所有原生leaf记录origin，与补偿scope明确区分。**当前shot在补偿前已出现任一原生leaf或API调用，则claim永远不可取得**，feature转停并查音源。异步/无法唯一关联的原生leaf不能证明“没有”，因此该场景禁启用补偿。

不能预知未来：若补偿后又观察到同key的原生leaf，保留原生执行、记 `DUPLICATE_NATIVE_AFTER_SUPPLEMENT`、立即禁用后续补偿并判该候选失败；不能拦原生/删音包/强制静音来把数字做成1。已发生的一次双播不能谎称被幂等消掉。启用前必须由实际IL调用图+活跃patch清单+相同SDK/真机场景轨迹证明当前普通分支无后续native叶；第三方hook/武器模式闭包未知则不启用。跨端无shotId时不承诺全网络去重；本修复只对Host当前原生fire scope负责。

session/connection/装备更换或销毁立即失效旧key，不能异步定时补声到新source；已关闭窗口内保留只读汇总后释放，不做无限last-seen字典。再装备同枪、同NetId/InstanceID复用都走新generation。登记失败、ABI漂移、计数点缺失、观测drop触发安全停；先关补偿不影响原生玩法与原生音频。

### 6.3 Unity全局随机流：必须二选一，默认选隔离

[playGunshot:757](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs#L757-L763) 的pitch会额外调用 `UnityEngine.Random.Range`。**ammo计数相同不足以排除副作用**；额外随机推进可能改变散布、AI、掉落等后续全局随机行为。

- **A（默认）保存/恢复**：同一Unity主线程，在额外原生leaf紧邻范围保存真实 `UnityEngine.Random.state`，try内调用，finally无条件恢复，之后验证恢复状态等同进入前；只包额外调用，不包原生fire/send/后续游戏模拟，不在原生播放本身外层保存恢复。异常也恢复、补偿claim保持失败终态，不能再调用一次；状态读取/恢复不支持或失败即feature停，不声称隔离成功。不跨await/帧/线程。仪表自己不调用Random采样、不改seed。
- **B（例外）非干扰证明**：提供当前实际ABI+所有活跃hook的封闭依赖证明，说明增加该draw为何不影响任何可观测后续行为，并给可变红的真实SDK检验；不能以“Range一次很小”或有限ammo对比代替。证明闭包缺失则不接受B、退回A或禁启用。

A的真实SDK验收：同样初始真实Random.state、输入/资源/原生fire，基线与候选在原生锚点后/本次完整fire后状态一致，后续确定性消费序列一致；调试时移除finally恢复必须使该测试变红。另比较真实散布/命中、原生shot-count/rechamber、弹药、各耐久、壳/火花/投射物和mod hook次数，不只ammo。不得在游戏生产路径为“校验随机”额外抽数；序列比较在受控SDK测试中做。

保存恢复也不是可任意包第三方逻辑的通行证：若补调会触发嵌套原生gameplay/他线程随机消费，不能回滚那段合法消费。已知实际叶应仅设置音源+Range+PlayOneShot；活跃hook扩成带gameplay/异步调用时闭包破坏，停止启用并回架构裁决，不压原生回调掩盖。

## 7. 三段验证边界与真机可执行观测

### 7.1 可直接自动化（不冒充听感或生产行为Red）

- ABI/完整IL调用点唯一、签名/原生字段与实际registrar身份测试；异常锚点/错误owner要使安装失败，不能碰巧 patch 到一个同名方法就PASS。
- 纯判定/账本：remote缺Host但原生本机leaf1→必须STOP；source缺失/clip空/缺listener/观测drop→不得Eligible；同ShotKey重复/递归/失败后重复最多一次claim；同frame两个合法ShotKey各一次；旧epoch/装备key无效；NativeAfterSupplement→整段FAIL且不阻断原生。用构造事件测试判定器只证明它的规则，不证明游戏真的出现这些事件。
- 反例变异必须可红：删除leaf stop门、把去重改frame级、释放失败claim、拿重算recipients冒充actual、把UNKNOWN算zero、移除Random恢复。除最后一项需真实SDK，其余可先纯数据测；完整实际调用仍见下一层。

### 7.2 需SDK/原生集成

匹配Unity版本/真实武器、equipment、thirdModel、AudioSource/listener、NetId/原生输入与RPC reader关系的对象图；优先加载真实运行资产，用原生装备/初始化流程，不手写另一套音频或武器算法。**以实际Guest输入/Host原生fire链驱动**，不直接调用补偿函数或假playGunshot当Red。若SDK只能做到本机原生fire而无真实输入传输，明确ComponentBoundary子范围，不能声称全P2P成功。

必须取得：baseline ordinary remote fire下实际P2/P3/P4/P5计数和完整排除项；candidate相同输入/身份仅换候选生产DLL，真实registrar生效，成功发射→HostAPI恰一次、基线缺覆盖；原生副作用与Random隔离匹配。PlayOneShot在真实Unity API执行，ECall/缺资产/NullRef不是缺声Red，升级宿主或标UNKNOWN。保留原生返回/异常，不用transpile改成无声fake函数来让fixture跑绿。

负控最少：Host自射、Guest自己预测、projectile、无弹/保险/重装未成功fire、切枪/断连/重生/新epoch、同帧多发/连发、音源失效/clip空、已存在原生leaf、外部hook冲突、最后一发消音耐久、补调异常与Random恢复、补调后unexpected原生leaf。真实source/clip通过不等于可听/空间位置通过；这些仍留真机。

### 7.3 真机1H+1G执行协议与分层关闭（S1–S4）

T132 / T131-F3 对齐需求 §2/§4：S1/S4 的听感与方向结果只能由真机 1H+1G 关闭；S2 的真实音频终端、S3 的同一真实 fire 原生副作用对照按 [Unity-SDK 集成] 关闭，不强制完整 Unity Editor。S3 若只能随真机会话采集，就同时标 [真机 1H+1G] 并保留该原生对照端点；听到声音不替代 S3，离线 IL/替身测试亦不替代它。下述真机协议可同时承载集成观测，不把执行场所与判据标签混同。

依照 [实机循环](../../docs/agents/real-machine-test-loop.md) 与 [部署身份SOP](../../docs/agents/auto-rm-test-sop.md) 操作，首次只部署经审查的ObserveOnly基线仪表；后续候选走同一地图/枪/资源/位置/音量。输出版本、game/SPF SHA+MVID、命令/开关、双方实际加载身份、场景与截图/录屏/trace关联；本规格不给一个尚未存在的CLI假命令。

1. **准备并冻结**：Host+一Guest进入同场景，获准且Ready；固定角色/装备generation与同组状态，近身无遮挡10±1U（以实际source/listener位置测）；枪选择明确普通单发非projectile、起始无枪管覆盖，弹药充足。源clip/curves/音量/静音/监听器/游戏焦点/系统输出设备逐项记录；先备份存档，不覆盖稳定包。
2. **正控**：Host在相同窗口自射同款真枪一发，P4/P5实命中且耳朵可听；Guest执行一种已正常消耗品或近战，Host也可听。若正控失败，停止本轮音频Red判定，先修仪表/环境，不继续数Guest缺声。不要手动调用播放API制造正控。
3. **基线10发**：Guest按约1秒隔离单发，双方录制；Host同时保留P2成功fire、P3真实list与P4/P5/API+source数据。可由旁观记录每个Guest实际枪口/弹药行为和Host主观听到的响数，作为关联辅助，不把录屏一帧枪口闪光当发射真值。raw trace逐发key对照，异常输入/观测缺口保留并标失败原因；判定按§5，不凭旧UMM日志零命中。
4. **裁决并停/走**：若任何原生leaf已到，补偿仍关闭，先查音源/输出；若H1 Red门满足且无额外原生路径，再实施与审查候选。若Host在真实收件人内但RPC断开，先给派发最早分歧报告；不顺手改全局收件人。UNKNOWN就收最少缺项，不要求用户无限重复全轮。
5. **候选同协议10发**：仅替换经审查候选，其余冻结；成功shot的Host全部有且只有一次原生音频叶/API（native/supplement来源分开），现场每发恰一声，无多响/漏响，位置与衰减合理。P4多于1、unexpected native、Random状态不符或副作用差异，即失败回退，不以“主观终于听到”吞掉回归。
6. **S3/S4回归**：逐发比较原生shot-count/rechamber、弹药/耐久、弹壳/火花/轨迹与projectile数量、mod hook次数/散布与原生随机流；Host→Guest和双方自射仍不重播，最后消音耐久发音色不提前变化。逐项重复脚步、跳跃/落地、近战、命中物体、医疗、食物、饮水并确认Host可听。位置/转身/不同距离另段检查，不混入固定近身10发分母。

录音可提供真实Host输出环回/麦克风音轨与屏幕/trace时间对齐（明确系统音轨还是环境麦克风）；没有录音时操作员听感按S1登记，不能拿不存在的波形阈值自动PASS。音轨能证明可听波形，不天然证明shot身份；单发隔离+两端记录关联，噪声遮蔽或多源同响则UNKNOWN。不能仅以 `AudioSource.isPlaying` 代替响数/定位。

1H+1G只关闭已确认Host/Guest方向。Guest↔Guest仍UNKNOWN：最终按需求加第二Guest做1H+2G验证；双人Green不宣称全三端声音修复。是否需要后续音源修复票由§5首个真实分歧决定，而不是预先把每种声音重写。

## 8. UNKNOWN台账、Builder交付与本票完整性

| 目前未知（不是失败或通过） | 解锁它的最少新证据 |
|---|---|
| 实际普通Guest fire是否缺Host本机leaf | P2成功fire+P3真实returned/Invoke集合+P4/P5全覆盖及正控，§4排除项；不需重读旧日志 |
| 缺声是否只是枪source/clip/custom curve/mixer失效 | 若leaf已达，保留该发source/listener/clip/rolloff/route与真实API结果，真机同窗正控；停止加补偿 |
| 此现场是否存在后续native/第三方补音路径 | 当前实际ABI完整IL与生效patch清单+完整fire到音频轨迹；闭包未知则不启用 |
| Mod 自定义音效 / 第三方重复播放路径未验证，UNKNOWN | 真实 Mod/活跃 hooks 身份、同 shot 原生/补偿叶关联与出现本地叶时停补并记录的运行证据；观察器的设计理由或替身守护用例不能关闭该项 |
| SDK图是否足以让真实fire与AudioSource终点运行 | 显式图/资产身份、原生调用链与API before/return/throw；ECall失败只说明宿主不够 |
| 真实可听响数、空间位置及输出静音排除 | 上述1H+1G固定场景10发录屏/操作/音轨+环境记录，不从API return推断 |
| Guest互听 | 1H+2G实际两Guest互射独立证据 |

Builder最终交付包含：真实生产registrar与终点锚点安装证据、ObserveOnly基线raw/manifest/分析与最早分歧、独立审查接受的H1或停止报告；若启用补偿，附相同场景候选raw、唯一测试ID/命令/exit、Random隔离与副作用对照、S1–S4逐项状态、回退方法/未关闭UNKNOWN。生产修复文件所有权另派票集中接线，不在本只读规格中修改Core/Platform/Tests/项目/版本。

本票只要求“最小可验证路径已明确”。若最终只得到H2或UNKNOWN，按证据收敛，不为交一个DLL把候选A硬变真。本规格仍待独立Standards/Spec审查；未运行游戏/SDK/构建/测试，不新跑真正Red/Green。下方只记录文档本身的校验结果。

### 本轮文档校验（不是游戏验收）

已读回本规格；本地Markdown文件链接共48个，路径与所引用的行范围均存在，检查退出码0，标记 `T13_LINK_AND_ANCHOR_PASS`。需求、架构和本轮枪源码SHA分别仍为 `17fc427c0e6ed115b680eedfa3119191c558609947051fc241facc4ebb94017e`、`d1d7ab31e9c4de41c0678b69ac68976a2e4910511c340a29766978a374b42339`、`19828172033b064972341126c10c5fe12c6f0a36cf318c7a578608659acc3da1`。本任务无生产写入、游戏/SDK/构建/测试进程或后台job；本轮实际执行仅只读源码/文档、规格写入与文档校验命令。

## 9. T132 测试缝登记与关闭手段同步（2026-11-08）

本节为 T131-F2 文档修正，不是新增生产功能；T13 上文的冻结输入/校验指纹保留其历史身份，新需求字节见 [T132 同步记录](t132-requirements-r4-sync.md)。以下按完整成员名登记，行号为本次只读源码定位。共有 13 个可写静态字段（11 个引用/委托、2 个 bool），另列 1 个只读属性及 5 个清理/测试方法；不能把所有成员统称为 13 个 nullable 字段。

**统一边界**：仅测试用、`internal static`、非公开配置项、不构成受支持的配置面。生产默认 null **仅指可注入引用/委托字段**；`SkipAnchorVerifyForTest` / `SkipFirePatchForTest` 是 bool，生产默认 false；属性和方法不具有“默认 null”语义。生产不得赋测试非默认值或调用 `ApplyForTest` 等测试入口；默认 null/false 时沿真实原生目标、校验、采集与初始化路径执行。源码确实含这些字段的判空/默认值及测试分支，不能写成“生产方法完全不读取测试缝”。

| 所属类型与成员 | 默认 / 只读定位 | 仅用于测试的替换、观测或清理能力 |
|---|---|---|
| `GunshotHostAudioPatch.FireTargetForTest` | null；[Patch:39](../../Core/Patches/GunshotHostAudio/GunshotHostAudioPatch.cs#L39) | 替换 fire 目标，实质改变安装路径 |
| `GunshotHostAudioPatch.PlayGunshotTargetForTest` | null；[Patch:40](../../Core/Patches/GunshotHostAudio/GunshotHostAudioPatch.cs#L40) | 替换 playGunshot 观察目标 |
| `GunshotHostAudioPatch.SkipAnchorVerifyForTest` | false；[Patch:41](../../Core/Patches/GunshotHostAudio/GunshotHostAudioPatch.cs#L41) | 跳过真实 IL 锚点校验，实质改变安装路径 |
| `GunshotHostAudioPatch.SkipFirePatchForTest` | false；[Patch:42](../../Core/Patches/GunshotHostAudio/GunshotHostAudioPatch.cs#L42) | 跳过真实 fire patch，仅安装观察器，实质改变安装路径 |
| `GunshotHostAudioPatch.PostfixVerifierForTest` | null；[Patch:46](../../Core/Patches/GunshotHostAudio/GunshotHostAudioPatch.cs#L46) | 注入观察器 Postfix 回查结果，不能当真实安装证明 |
| `GunshotHostAudioRuntime.FactsProviderForTest` | null；[Runtime:59](../../Core/Patches/GunshotHostAudio/GunshotHostAudioRuntime.cs#L59) | 注入纯数据门控事实，不是真实 SDK 采集 |
| `GunshotHostAudioRuntime.LeafInvokerForTest` | null；[Runtime:60](../../Core/Patches/GunshotHostAudio/GunshotHostAudioRuntime.cs#L60) | 替换音频叶调用，不证明真实 API/可听结果 |
| `GunshotHostAudioRuntime.LeafResolverForTest` | null；[Runtime:63](../../Core/Patches/GunshotHostAudio/GunshotHostAudioRuntime.cs#L63) | 注入叶解析成功/失败 |
| `GunshotHostAudioRuntime.AudioSourceProbeForTest` | null；[Runtime:66](../../Core/Patches/GunshotHostAudio/GunshotHostAudioRuntime.cs#L66) | 替换 AudioSource 探测，不证明真实音源存在 |
| `GunshotHostAudioRuntime.RandomStateSaveForTest` | null；[Runtime:69](../../Core/Patches/GunshotHostAudio/GunshotHostAudioRuntime.cs#L69) | 替代 Unity Random.state 保存；不关闭真实 RNG 隔离 |
| `GunshotHostAudioRuntime.RandomStateRestoreForTest` | null；[Runtime:70](../../Core/Patches/GunshotHostAudio/GunshotHostAudioRuntime.cs#L70) | 替代 Unity Random.state 恢复；不关闭真实 RNG 隔离 |
| `GunshotHostAudioRuntime.CollectObserverForTest` | null；[Runtime:350](../../Core/Patches/GunshotHostAudio/GunshotHostAudioRuntime.cs#L350) | 观察采集源值的接线，不替换原生对象图 |
| `GunshotHostAudioRuntime.ResetForTest()` | 方法；[Runtime:483](../../Core/Patches/GunshotHostAudio/GunshotHostAudioRuntime.cs#L483) | 清运行时测试状态、账本与本类注入字段 |
| `SteamP2PFriendsPlugin.GunshotModuleInitializerForTest`（Diagnostics） | null；[Diagnostics:101](../../Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs#L101) | 替换枪声模块初始化结果/异常，以测试阶段失败传播 |
| `GunshotHostAudioControl.ConfigPathForTest` | 只读属性；[Control:31](../../Core/Patches/GunshotHostAudio/GunshotHostAudioControl.cs#L31) | 读取测试配置路径，无写入配置能力，不是 nullable 注入项 |
| `GunshotHostAudioControl.ResetForTest()` | 方法；[Control:71](../../Core/Patches/GunshotHostAudio/GunshotHostAudioControl.cs#L71) | 清配置订阅、初始化与测试开关状态 |
| `GunshotHostAudioControl.ApplyForTest(Harmony harmony, bool enabled)` | 方法；[Control:88](../../Core/Patches/GunshotHostAudio/GunshotHostAudioControl.cs#L88) | 绕过 BepInEx 读取、以给定开关驱动 Apply；不是生产启用入口 |
| `GunshotHostAudioLedger.Reset()` | 方法；[Policy:234](../../Core/Patches/GunshotHostAudio/GunshotHostAudioPolicy.cs#L234) | 无 ForTest 后缀的账本清理缝，不是绕开 at-most-once 的配置 |
| `GunshotHostAudioPatch.ResetForTest()`（补列清理依赖） | 方法；[Patch:207](../../Core/Patches/GunshotHostAudio/GunshotHostAudioPatch.cs#L207) | 清 patch 状态及五个安装测试字段；不替代实际卸载 |

**安装路径红线**：`SkipFirePatchForTest` / `SkipAnchorVerifyForTest` / `FireTargetForTest` 会实质改变安装路径，**不得被当成受支持配置**。假目标、跳过校验或注入回查成功，只能证明对应测试边界，不能据此宣称真实生产 Harmony 命中、真实 fire 行为或 S1–S4 Green；运行配置不得映射这些成员。

**隔离与复位**：每个设置测试缝的用例必须以 `try/finally` 清理，不论断言失败或抛异常：先卸载该用例拥有的 Harmony patch，再按使用范围调用 Patch/Runtime/Control 的 `ResetForTest()`，复位账本 `Ledger.Reset()`；`GunshotModuleInitializerForTest` 须单独恢复 null（上述 reset 不覆盖它）。各默认引用/委托最终为 null、两个 skip 为 false、Control 测试开关清空，不将状态传给下一用例或生产会话；此处规定清理责任，不声称现有全部测试已被本票审查通过。

**注册失败传播与引用**：[需求 §7 R3-REG](requirements-v2.md) 要求 `U3-REG-06-Probes` 中枪声初始化 false/异常使 stage `ok=false` 并被注册计划记录，不能静默吞错；Bug2-33 为自动化覆盖点。观察器安装/回查/深度守卫引用为 Bug2-20/29/31，回查专门项是 Bug2-31，不是 Bug2-20/29。本节不改变 S1/S2/S3/S4、Mod 兼容或 Guest 互听的 UNKNOWN，不授予实现自审或部署资格。
