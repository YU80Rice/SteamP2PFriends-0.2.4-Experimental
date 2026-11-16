# T64 Bug2 只读观察探针：精确定位、路由对照与共享接线依赖

2026-11-07／gpt-architect。**实施前定位，不是实现票。** 本任务只新增本报告，不改Core/Platform/Tests/项目/版本，不构建、运行游戏或补播放。后续ObserveOnly探针继续服从[T13真实音频终点与判停契约](bug2-audio-leaf-spec.md)，不能把本次静态定位当音频Red。

## 1. 先纠正归因：P2P Prefix不重新计算收件人

[现有P2P Prefix:378–469](../../Core/Patches/ClientMethodLoopbackPatch.cs#L378-L469) 的方法是 `SteamP2PFriends.Core.Patches.ClientMethodLoopbackPatch.SendAndLoopbackIfAnyAreLocal_Prefix(ClientMethodHandle, ENetReliability, List<ITransportConnection>, NetPakWriter) : bool`。

| 行/动作 | 实际意义 |
|---|---|
| [382–388](../../Core/Patches/ClientMethodLoopbackPatch.cs#L382-L388) | ShouldProcessClientHostListen不成立时return true，交原生；不是新增helper或换列表 |
| [394–409](../../Core/Patches/ClientMethodLoopbackPatch.cs#L394-L409) | Flush当前writer并扫描**传入参数transportConnections**；真实TransportConnection_Loopback设置shouldLoopback，其余算remote。这里没有再次调用Gather、也没有按Host SteamID重新排除 |
| [422–432](../../Core/Patches/ClientMethodLoopbackPatch.cs#L422-L432) | 逐实际列表，loopback不走tc.Send，其余逐个`tc.Send(writer.buffer, writer.writeByteIndex, reliability)`。跳过loopback只是避免把本机连接当远端发，不是删除本机分派 |
| [443–445](../../Core/Patches/ClientMethodLoopbackPatch.cs#L443-L445) | shouldLoopback为真才InvokeLoopback一次。本机是否执行取决于实际列表中有没有loopback |
| [468](../../Core/Patches/ClientMethodLoopbackPatch.cs#L468-L469) | return false，原生方法体不执行。因此在被跳过的原生体内打transpiler，不能覆盖该P2P实际Send路径 |

`SendPlayShoot`的列表是在[Gun:914](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs#L912-L914)调用原生[SteamChannel helper:160–178](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Provider/SteamChannel.cs#L160-L178)得到，167–168在非dedicated编译下跳过IsLocalServerHost，再排owner/null player/球外。**“正确remote列表”恰可以不含Host，不能把“正确”定义成“必须含Host”。**

SDK [ClientMethodHandle:97–123](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/NetInvokable/ClientMethodHandle.cs#L97-L123)还保留不支持listen server时的假设：非dedicated遇到任何一个连接都当本地、break，然后loopback，不向这个真实远端发送；空列表没有loopback。SPF按实际连接类别分派替代的是这一旧假设，不是一个已证正确的枪声补偿。

两人Host+Guest且Guest为owner时，remote helper排Host+owner→列表空，**SDK旧分派与SPF分派都不会回环**。三人且另一个Guest B在范围内时，原生旧分派会误把B视为local而不向B发，SPF才向真实B发。此差异不能证明“SPF删掉原来正确的Host覆盖”，更不授权让所有remote消息强制回环。

## 2. 开火链路与Harmony target清单

SDK文件均位于Assembly-CSharp源树；下面是**精确声明类型/签名的建议目标**，行号供读码定位，实际安装必须以运行模块元数据再校验。`SDG.Unturned`以下简记U；Vector3/AudioClip属UnityEngine，NetPakWriter属SDG.NetPak，ITransportConnection/ENetReliability属SDG.NetTransport，NetId/PooledTransportConnectionList属U。

| 阶段/文件:行 | 声明类型和方法签名 | 重载/泛型与观察选择 |
|---|---|---|
| 捕获扳机 [Equipment:3309](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerEquipment.cs#L3309-L3337) | U.PlayerEquipment, `internal void CaptureAttackInputs(out EAttackInputFlags primaryAttack, out EAttackInputFlags secondaryAttack)` | 非泛型；用两个ByRef EAttackInputFlags解析，Postfix看结果，不改out。它表示意图，不是成功发射 |
| useable入口 [Equipment:2524–2530](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerEquipment.cs#L2524-L2530)；[Gun:3166](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs#L3166-L3170) | U.PlayerEquipment `private bool StartUsablePrimary()`→U.UseableGun `public override bool startPrimary()` | 零参数非泛型；初期只需patch Gun声明的override，Prefix/Postfix记录bool。不要patch所有Useable基类去污染其他物品 |
| 服务器仿真/开火节拍 [Gun:4347、4431、4547](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs#L4431-L4521) | U.UseableGun `public override void simulate(UInt32, Boolean)`；`private void tockShoot(UInt32 clock)`；`public override void tock(UInt32 clock)` | 非泛型，按参数解析；首次可只跟tockShoot/fire，simulate/tock为需要区分武器状态门时的辅助点。原生tockShoot:4520调用fire |
| 实际fire [Gun:880](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs#L880-L914) | U.UseableGun `private void fire()` | 零参数非泛型；Prefix建fire调用ID，Postfix记正常返回，Finalizer记异常并无条件收scope，保留原异常。fire entry不直接计成功shot |
| 发送 [Gun:867/914](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs#L867-L914) | 私有静态readonly字段`SendPlayShoot : U.ClientInstanceMethod`；调用该实例的`Invoke(NetId, ENetReliability, List<ITransportConnection>) : void` | **Invoke有多种重载/泛型版本，必须取非泛型类上的三个参数、无callback这一项**。目标声明U.ClientInstanceMethod，见[110–114](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/NetInvokable/ClientInstanceMethod.cs#L110-L114)。Prefix截实际入参，Postfix/Finalizer收scope |
| 收端 [Gun:869–875](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs#L869-L875) | U.UseableGun `public void ReceivePlayShoot()` | 零参数非泛型；Prefix记录实例/动画门，Postfix/Finalizer记录结果。ONLY_FROM_SERVER且EquipAnimationFinished才shoot，不主动调用它 |
| 本机声音叶 [Gun:739–765](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs#L739-L765) | U.UseableGun `private void playGunshot()` | 零参数非泛型；Prefix/Postfix/Finalizer，只观察真实调用。私有字段gunshotAudioSource/配件/clip只读反射缓存，不新建AudioSource |
| 真API终点 [Gun:761–763](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs#L761-L765) | UnityEngine.AudioSource `public void PlayOneShot(AudioClip)`→`public void PlayOneShot(AudioClip, Single)` | 位于UnityEngine.AudioModule，二者均有托管body；建议只观察两参数托管重载，防一/二参数包装双计。只在所选Gun/source/叶scope内计数。底层`PlayOneShotHelper(AudioSource,AudioClip,Single)`为InternalCall、无body，不作普通Harmony目标 |

精确解析使用实际Assembly中的声明Type与参数Type数组，校验DeclaringType、return、static/instance、ContainsGenericParameters与managed body，不能靠方法名任选第一个。Inherited private目标尤其要从声明类型解析。目标缺失/装钩不可用→探针Unavailable，不允许“没有点命中所以音频0”。

### 当前元数据已确认，但不是同次实机身份

本票用Mono.Cecil只读元数据检查（没有执行目标静态构造或游戏）：构建依赖 [Libs Assembly-CSharp](../../../Libs/Assembly-CSharp.dll) MVID=`dc7bb31c-d2f3-4609-950a-d545f6f7347c`；当前磁盘 [游戏Assembly-CSharp](<E:/Steam/steamapps/common/Unturned/Unturned_Data/Managed/Assembly-CSharp.dll>) MVID=`6f5a40c0-cf17-4365-93e5-6915f8bd2634`、SHA=`0cc7d579a2dae3283fa02d0a567fe01410da885127d72eb2ae23c440e4d6c660`。两者上述gun/helper/loopback/两种Steam Send目标有managed body，Invoke重载已枚举；早期T13的`54793…`是另一身份。

当前磁盘 [AudioModule](<E:/Steam/steamapps/common/Unturned/Unturned_Data/Managed/UnityEngine.AudioModule.dll>) MVID=`deaf4488-108c-4ffb-89b6-cc3614b8a5a5`，两种PlayOneShot有body、Helper无body且InternalCall。**有托管body只说明是可考虑的Harmony目标，不保证当前Unity Mono内联/平台/其他patch下实际观测成功**，必须正控验证。

入场必须记录同次运行game/SPF完整SHA/MVID，校验实际fire的helper、ClientMethodHandle分支与字段的完整IL，再查生效patch顺序；不能拿SDK行号、Libs或今天磁盘字节冒充前一场运行身份。签名可解析不等于行为版本已锁。

## 3. 接收者三处取样：分别存，不合并

| 名称 | 精确位置与取得方式 | 不得冒认 |
|---|---|---|
| helperReturnedRecipients | U.SteamChannel `public PooledTransportConnectionList GatherRemoteClientConnectionsWithinSphereExcludingOwner(Vector3, Single)`，[160–179](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Provider/SteamChannel.cs#L160-L179)。Postfix读真实`__result`，仅当前选定Gun.fire scope；参数含球心/radius。类型PooledTransportConnectionList继承List<ITransportConnection> | 不重调helper计算预期，不排序/改写；复制有界元数据后放掉引用。[池ReleaseAll](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/NetInvokable/TransportConnectionListPool.cs#L22-L59)会清复用，禁止跨帧持有 |
| resolvedRecipients | **实际SendPlayShoot handle** 的U.ClientInstanceMethod.Invoke精确list重载Prefix，直接读NetId/reliability/transportConnections实参 | 它才是该消息声明目标。与helper快照不同就记录变更，不取并集/用helper覆盖它。字段SendPlayShoot及基类protected clientMethodInfo只读反射；不靠ToString判消息 |
| appliedRecipients | 真实具体transport.Send入口+正常返回/异常，以及真实private InvokeLoopback入口与下游ReceivePlayShoot。源应用点是SPF[426–432与443–445](../../Core/Patches/ClientMethodLoopbackPatch.cs#L426-L445) | 目标集合不代表实际发送，更不代表Steam接受/对端收包。原生method return也不等于播放成功 |

消息身份：从U.UseableGun私有静态SendPlayShoot字段取得实际handle（游戏网络与装备已就绪后才读值），再核实U.ClientMethodHandle的[protected clientMethodInfo:168](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/NetInvokable/ClientMethodHandle.cs#L168-L170)，以及[ClientMethodInfo:21–29](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/NetInvokable/NetReflection.cs#L21-L29)内declaringType/name/methodIndex=UseableGun/ReceivePlayShoot/当前index。字段是internal/protected，需精确缓存FieldInfo，不能以派生类GetMethod去找基类private loopback。metadata解析不提前Get静态字段值触发NetReflection初始化。

### 实际应用点的建议targets

- U.ClientMethodHandle `protected void SendAndLoopbackIfAnyAreLocal(ENetReliability, List<ITransportConnection>, NetPakWriter)`，[86–125](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/NetInvokable/ClientMethodHandle.cs#L86-L125)：用观察Prefix在现有P2P Prefix之前建立应用scope，Postfix/Finalizer关闭；**不在会被跳过的原body内打点当唯一证据**。必须验证实际prefix优先级/顺序，不假定“名字叫Prefix就先跑”。
- 为嵌套其他RPC设置scope栈栅栏，同样观察基类`SendAndLoopbackIfLocal(ENetReliability,ITransportConnection,NetPakWriter)`和`SendAndLoopback(ENetReliability,List<ITransportConnection>,NetPakWriter)`。只计栈顶真正SendPlayShoot，其他消息不能因外层fire仍活跃被误算。泛型RPC最后也走这些非泛型基类分派，不必对所有闭合泛型Invoke扩散装钩。
- U.ClientMethodHandle **private** `void InvokeLoopback(NetPakWriter)`，[170–220](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/NetInvokable/ClientMethodHandle.cs#L170-L220)：DeclaredMethod精确解析、Prefix/Postfix/Finalizer。它内部catch reader异常，所以正常return只能表示调用完成，需再看ReceivePlayShoot/叶计数。现有[反射绑定:42–45](../../Core/Patches/ClientMethodLoopbackPatch.cs#L42-L45)可作解析范例，不调用现有wrapper来“测试播放”。
- `SDG.NetTransport.SteamNetworkingSockets.TransportConnection_SteamNetworkingSockets`（internal class）`public void Send(Byte[], Int64, ENetReliability)`，[126–149](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/NetTransport_SteamNetworkingSockets/TransportConnection_SteamNetworkingSockets.cs#L126-L149)：在Assembly-CSharp里，不是Steamworks原生层。通过实际游戏Assembly.GetType精确解析internal类型；Prefix/Postfix/Finalizer记尝试/正常返回/异常。
- `SDG.NetTransport.SteamNetworking.TransportConnection_SteamNetworking`（internal **struct**）同签名，[92–121](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/NetTransport_SteamNetworking/TransportConnection_SteamNetworking.cs#L92-L121)：同样managed；装箱可能每次不同，不用boxed对象ReferenceEquals当连接身份。用同次Provider.clients映射的稳定连接字段/代际标识，不改struct实例。它还可能shouldNetIgnoreSteamId后直接return，返回不等于发出。

只patch `ITransportConnection.Send`接口不够，因为接口本身没有应用body。Arm时枚举**当前实际具体连接类型**并锁支持目标；出现其他实现/装钩失败就标该连接覆盖不全，不能记零发送。SteamNetworkingSockets方法内部[137](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/NetTransport_SteamNetworkingSockets/TransportConnection_SteamNetworkingSockets.cs#L126-L149)才调用SteamGameServerNetworkingSockets.SendMessageToConnection并得到EResult，void Send即使报错也会返回；一期不patch Steamworks/PInvoke，只把normalReturn与acceptance/delivery UNKNOWN分开。

所有观察Prefix保持void/不改参数，不给原方法return false；Finalizer只清自己的scope/记录，不吞改原异常。若其他prefix导致观测prefix没跑，或发生不受栈栅栏覆盖的嵌套发送，该段INCOMPLETE而非“Host发送0”。不记录payload/凭据、不消费原NetPakReader、不改变writer游标。

## 4. “原生应给谁/原实现做什么/SPF实际做什么”三列对照

下面比较**相同角色事实、同一已解析列表L**输入下的方法语义，不是假装做过“移除全部SPF”的整机A/B。原生构造身份与其他patch也可能改变helper前置事实，必须另记。OwnerAndClient包含Host还要求Host确有player且在该事件范围内。

| 事件与来源 | API/业务路径表达的覆盖 | SDK旧非dedicated基类实现 | SPF按实际连接的覆盖 |
|---|---|---|---|
| 普通枪SendPlayShoot [914](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs#L912-L933) | remote球内非owner/非Host；普通nonlocal server不shoot，只有服务端Hook+补shot-count | L空→无远端/无本机；L非空→误当local而只回环 | 每个真实remote各Send一次；只有L中真loopback才本机一次。正常helper的L无Host，本机缺口未由此方法补齐 |
| projectile SendPlayProject [1326–1332](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs#L1326-L1332) | remote排owner/Host，但server已先project，[509–514](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs#L509-L514)含音频叶 | 同样对非空L作local假设，不代表业务应强制本机再project | 正常remote发送；Host音频由先前本机project，不需为听声扩列表 |
| 消耗品SendPlayConsume [134–150](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableConsumeable.cs#L134-L150) | 先consume本机声音，再remote排owner/Host | 旧方法若非空L回环可能额外消费表现，不应据此定“正确Host覆盖” | remote各发，本机已有consume；强加Host会重复本机表现/声音 |
| 近战命中 [491–495](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableMelee.cs#L491-L495)→[152](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableMelee.cs#L147-L152) | GatherOwnerAndClient球内集合，含owner及合格Host | 对任何非空L都只回环，真实远端可能没收到 | remote各发+真loopback一次，Host经ReceiveSpawnMeleeImpact播放 |
| 子弹材料命中 [Gun:1971–1984](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs#L1971-L1984)→[DamageTool:1432–1438](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Tools/DamageTool.cs#L1432-L1438) | GatherOwnerAndClient球内集合；与SendPlayShoot不同 | 同上非空L假设 | 真Host loopback可达ReceiveSpawnBulletImpact，再播命中声 |
| 脚步/落地/近战挥动 | 有本机非dedicated代码，不依赖SendPlayShoot | 不在此消息的收件人争议内 | 不需要因补枪声改它们的路由 |

所以静态可确认的差异是：**SPF修正了旧SDK“把任意连接当local”的分派假设**，不是自己把本应包含Host的枪声列表重新算坏。无须等实机才能确认源码这两套语义不同；但是否就是此次无声的唯一运行原因，仍需实际身份/终点观测，不能由差异直接推出。

若要找“哪一行让Host不回环”：业务remote helper是原生167–168排Host；SPF443–445只在列表有真实loopback时回环。这两者构成普通远端枪的**本机音频覆盖空位**，不是“改443为无条件回环”的许可。后一改法影响所有经该wrapper的消息，尤其先本机播放的消耗品/projectile；改helper还会改变其他调用者集合。

## 5. 为什么其他声音仍能到Host：分别追到播放路径

“Host能听到”在此指**代码存在到本机播放API的机制**，仍以非dedicated、资源/状态/范围/音量条件成立为前提，不把它当已测可听次数。

| 类别 | Host触发/播放路径（SDK） | 机制裁决 |
|---|---|---|
| 脚步声 | Movement.Update [1624–1653](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerMovement.cs#L1624-L1653)：非dedicated、脚步节拍/ground/isMoving门→[815–839](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerMovement.cs#L815-L839) PlayFootstepAudioClip→OneShotAudioParameters.Play | Host对远端也本机执行，不需要枪声RPC回环；可自听到该Guest脚步 |
| 落地声 | server movement simulate [1346–1361](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerMovement.cs#L1346-L1361) grounded转换且非resim/速度条件→PlayLandAudioClip；Update远端ground检测 [1633–1639](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerMovement.cs#L1633-L1639)也可触发；[778–803](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerMovement.cs#L778-L803) BipedLand音频 | 服务端仿真/本机Update有播放入口，不依赖remote list；实际是否每次重复须轨迹核对，不能由两个调用点相加算响数 |
| “跳跃声”（起跳瞬间） | 上述已查明的是落地与移动声音；本轮未定位独立起跳clip的明确调用链。用户听到的“跳跃声”不可直接等同BipedLand | **UNKNOWN：独立起跳声身份/触发尚未确认**。不捏造SendPlayJump；最少补实际clip与起跳/落地时间关联或资源/动画事件证据。不会因此否定用户已听到动作声的事实 |
| 近战挥动 | UseableMelee仿真 [1035–1048](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableMelee.cs#L1035-L1048) 到playUseSoundTime且非dedicated→player.playSound；[Player:367–381](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/Player.cs#L367-L381)→本机OneShotAudioParameters | Host服务端远端武器有本机挥动声，不靠SendPlayShoot |
| 近战命中 | ServerSpawnMeleeImpact用OwnerAndClient集合；[Receive:114–143](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableMelee.cs#L114-L143) 播材料MeleeImpact及可选武器impactAudio | 与挥动不同：依合格Host loopback收客户端表现消息，再本机播；并非所有“能听到”都一定有server直接叶 |
| 子弹击中物体 | 普通材料路径OwnerAndClient→[DamageTool.ReceiveSpawnBulletImpact:1420–1425](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Tools/DamageTool.cs#L1420-L1425)→[PlayBulletImpactAudio:1357–1372](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Tools/DamageTool.cs#L1357-L1372)；自定义弹匣effect另经[1322–1340](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Tools/DamageTool.cs#L1322-L1340)设置OwnerAndClient后triggerEffect | 标准材料命中有Host回环入口；自定义effect是否带何种clip取决于asset，本票不把材料路径覆盖所有资产。击中物体声与枪口声确为两条路线 |
| 消耗品使用（医疗/食/水结构） | [startPrimary:146](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableConsumeable.cs#L134-L150)先consume；[consume:99–107](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableConsumeable.cs#L88-L107)非dedicated直接player.playSound，再SendPlayConsume remote广播 | 本机声音先发生，广播排Host不妨碍Host听到；医疗/食/水仍须按实际物品与USE/AID分支分别回归 |
| 普通Guest枪口声 | fire server [918–933](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs#L918-L933)明注“不本机shoot”，仅Hook/统计；remote列表又排Host。客户端走ReceivePlayShoot→shoot→playGunshot | 对**非local普通非projectile**，所核SDK没有对应本机兜底叶；Host自己为owner则[1195–1198](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs#L1195-L1198)另有shoot，projectile亦有本机project，不能笼统说所有枪声只有客户端RPC |

统一假设的裁决：**作为缺口类别成立**——listen-host的server角色不自动拥有所有client呈现路径；Bug1是缺位置缓冲，Bug2在上述普通远端枪分支缺本机声音入口。它们不是一个已证同根缺陷，更不能统一改Provider.isServer或泛型路由来修。也不是“server不能接收任何客户端表现消息”：近战/子弹命中可经Host loopback接收；普通枪是该消息列表排Host又没有对应本机叶。跳跃clip未确认、其他未枚举声音不允许写成“枪声是全游戏唯一无server兜底音效”。实际Bug2仍可能有额外source/clip问题，T13停止条件保留。

### 若后续H1实证，修哪个层

**首选仍为T13的局部原生playGunshot叶补调，不选扩Host回环。** 只作用于成功的Host端远端普通枪、原生本机叶确为0且音源有效、排除self/projectile；按每次真实fire claim一次，不按收件人数播放，多人时Host也只有一次，其他Guest/owner原有声音不重发。额外Random.Range必须按T13隔离，叶已存在立即停补；本票不实施。

仅对SendPlayShoot加一个Host loopback看似小，但其收端是ReceivePlayShoot→**shoot而不是纯音频叶**。shoot含[833–858](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs#L833-L858) shot-count/rechamber、耐久、弹壳/火花/轨迹/hook；server fire已经补统计，远端枪管耐久还在[1203–1216](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs#L1203-L1216)处理。强行回环可能把它们重复执行，不是等价音频修复。若改为OwnerAndClient，还把owner预测播放再收一次RPC；若全局强回环，消耗品/projectile也会叠加。不能靠事后定时去重掩盖。

## 6. 观察阶段最小改动面与冻结依赖

以下为**拟新增文件/改动，尚未实施**，职责可落在三份文件中，不要求引入新框架：

| 位置 | 职责/是否必需共享修改 |
|---|---|
| 新Platform/Diagnostics/Patches/Bug2AudioProbePatch.cs | 精确目标解析、独立观察hooks与scope栅栏/受控卸载；不改ClientMethodLoopbackPatch源码或原生helper逻辑 |
| 新Platform/Diagnostics/Bug2AudioProbeSession.cs | 有界事实快照/调用ID/目标代际与只读事件输出；继承T13字段、完整性与零计数准入，不假播放器 |
| 新Platform/Diagnostics/Bug2AudioProbeControl.cs | 默认OFF、选择目标/窗口、Arm/Stop、禁补偿控制。ObserveOnly与补偿明确分离，本阶段没有补偿开关实现 |
| [SteamP2PFriends.csproj](../../SteamP2PFriends.csproj#L13-L16) | **必须修改**：生产Compile列表显式，新增源码不自动入DLL。处于冻结共享入口，必须等集成窗口，不能只把.cs放目录就说已编进来 |
| [DiagnosticsModules:36–80](../../Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs#L36-L80) | **必须接线**RegisterManual与Control.Initialize并明确probe ready/unavailable；不能只新增无人调用的registrar。Core/Registration属T57共享冻结集，与项目一起串行集成；不改玩法准入/传输路由 |
| 新WhitelistTests/Evidence/Runtime/Bug2AudioProbeTests.cs | 生产目标/真实caller与scope/只读负控、嵌套其他消息、异常cleanup、pooled列表不持有、未覆盖不能判zero、默认off与卸载身份；用例不是S1真机Green |
| [WhitelistTests/Program.cs](../../WhitelistTests/Program.cs) | **必须修改**：正式登记新唯一用例，单独定向运行不能代替正式runner。也是当前冻结共享文件，等集成票接线 |
| [测试csproj:139–143](../../WhitelistTests/SteamP2PFriends.WhitelistTests.csproj#L139-L143) | **通常无需改**：已有Evidence/**/*.cs glob；但[42–44](../../WhitelistTests/SteamP2PFriends.WhitelistTests.csproj#L42-L44)引用的是bin/Release生产DLL，必须核验加载的新DLL，不是只编过测试文件 |
| Build/Release/publishi/SDK/原生DLL | 本阶段不需要修改；未来封装候选另票按[T57](architect-ruling-t57-work-policy.md)审查并绑定新字节，不覆盖当前等待件 |

实施者执行顺序固定：①只读锁ABI与待审输入→②在新文件中准备模块/测试→③冻结解除后的单一集成窗口修改csproj+DiagnosticsModules+Program→④Red/Green、真实caller变异及生产双轴→⑤默认OFF新候选身份封装→⑥人工Enable做真实10发ObserveOnly，按判据分流。**不是本票让实现者现在改冻文件。** 共享文件冻结期间可准备独立源码/测试，但没有正式接线前不声称产物可运行。

观察只记录真实调用，不补发、改收件人、调用shoot/project/Receive/playGunshot、改Random/Time/角色门，也不新建音源。probe异常自清理并Unavailable/INCOMPLETE，保留原生流程/参数/异常语义，不把安装失败变成玩法逻辑改写。卸载只触及本模块持有的具体PatchMethod/target，不能UnpatchSelf拆掉其他域。

## 7. 可证伪观察判据与交付清单

| 判据 | 所需真实证据 | 否定它的数据/动作 |
|---|---|---|
| H1本机音频覆盖缺口 | 真实成功remote ordinary fire、实际resolved列表无Host、同shot原生localLeaf=0、真实PlayOneShot调用前/正常返回均0、source/environment有效、没有其他Host路线、覆盖完整且正控真命中 | 任一原生localLeaf>0就停“补叶”路线查source；列表含Host但reader断则DISPATCH_GAP，不是H1。仅helper少Host不成立 |
| 收件人传递或应用差异 | 同call helper快照与实际Invoke实参不同，或实际非loopback resolved目标无对应Send尝试，或loopback存在却无InvokeLoopback入口 | 三处在身份/次序/次数上完全一致会否定这一路由差异；仍无声则向receiver/clip/输出走，不强改列表 |
| H2枪专属音源/输出问题 | 已有原生leaf/真实音频API，仍不可听，source/clip/音量/监听器/衰减/遮挡/剔除需关联排除 | leaf根本未调用且完整source/环境有效会降低H2优先；不允许“没听见”直接多播一遍 |

每项输出需区分call attempted、normal return、Steam acceptance、remote received、actual audio API、主观可听；后四者不能从前两者推断。read-only hook“能解析”也不能替代真机Positive control；PlayOneShot内部clip=null早退与actual submit要分开，API正常返回亦不代表DSP/耳朵输出。

### 最终状态分栏

- **已确认（静态/元数据）**：目标声明/参数/托管body、三个不同集合/应用点、SPF跳过原body的风险、项目/runner/注册共享接线需求、所核SDK各音效路径结构差异。
- **待验证**：运行中具体transport类型/接口dispatch可观测性、Harmony实际生效/内联/顺序、该次SendPlayShoot的真实列表与同shot音频计数、loopback reader成功、实际clip/source/听感、跳跃独立clip，以及SDK与同次运行IL一致性。
- **已否定为静态结论**：SPF378–468重算了收件人；remote列表缺Host本身就是Bug；非dedicatedSDK旧分派等价于向真实远端发送；只打原body点可覆盖P2P发送；Send正常返回=Steam成功；所有正常声音都靠server直接播；全局强回环是纯音频修复。

交付给Builder的检查单就是§2目标签名、§3三处scope/反射/应用点、§4/5机制对照、§6冻结文件依赖与§7证伪门。本票未改实现、未安装Harmony/执行声音，不授予Bug2关闭；仍待独立审查与后续真实ObserveOnly证据。
