# U3DS Resource、Collision/Object、Building 职责事实草稿

- 日期：2026-10-07；研究票：[原生 U3DS 世界状态、激活与复制职责事实调查](ticket.md)。
- 状态：首轮源码事实文档；复核状态见 [审查链](review-chain.md)；StaticSource 仅描述源码观察，不是新增 Evidence Class，不授予 Runtime PASS、DLL 身份或实施批准。
- 插件 HEAD：`57e3c4e4411b4f8831f0bf6d489b9fea9922c47c`；SDK HEAD：`ea7b4973af5ba10f62baad2bfde36ab2e5b060eb`，本轮均经 `git rev-parse HEAD` 核对。
- 未改生产、测试；未构建、部署或提交。以下路径行号属于上述 checkout；源码不证明当前实机运行结果。
- 路径约定：`SDK/` = `D:/Agent-工作目录/U3-SDK/Assets/Runtime/Assembly-CSharp/Unturned/`；`Plugin/` = `D:/Agent-工作目录/DevelopMyUNMultiplayerModAndModloader/SteamP2PFriends-0.2.4-Experimental/`。
- 写入批次：Resource/Collision → Object/Building → 生产计时/保存/建议/未查范围。

## 事实：Resource

1. 原生树/可采集点以 `LevelGround` 的区域列表及 `ResourceSpawnpoint` 保存生命、死亡时间；重置条件为死亡、asset.reset > 1、realtime 已过阈值。
   来源：`SDK/Level/ResourceSpawnpoint.cs:113-121,190-208`；`SDK/Managers/ResourceManager.cs:663-673`。
2. U3DS 的资源模型激活显式包含 Dedicated 分支；模型与树桩还分别受条件及生死可见性控制，因此不能把模型激活等同于生命状态。
   来源：`SDK/Level/ResourceSpawnpoint.cs:390-410`。
3. `ResourceManager.Update` 的守卫是 isServer + Level.isLoaded；轮巡世界二维区域、每区 spawn 索引，执行资源重生检查，无观察者需求守卫。
   来源：`SDK/Managers/ResourceManager.cs:787-821`。
4. 重生还查询 PLAYER/ENEMY，以及配置启用时 BARRICADE 的碰撞遮挡；全域逻辑扫描并不能证明所有遮挡物当前已激活。
   来源：`SDK/Managers/ResourceManager.cs:661-685`。
5. 采伐链原生调用 `ItemManager.dropItem` 生成木头/树枝等，再调用 `ServerSetResourceDead`；Resource 与 Item 真实存在跨域调用关系。
   来源：`SDK/Managers/ResourceManager.cs:161-275`（dropItem 点 `214,232,249`，dead 点 `275`）。
6. 原生存活/死亡增量是 `SendResourceAlive/Dead.InvokeAndLoopback(Reliable, GatherRemoteClients...)`；接收方使用区域 isNetworked 门控。
   来源：`SDK/Managers/ResourceManager.cs:409-469,651-658`。
7. 初始基线为 SendResources/ReceiveResources：客端标记 isNetworked 并按资源索引应用死活；原生 onRegionUpdated 在 step 3 为玩家附近未加载区可靠发送。
   来源：`SDK/Managers/ResourceManager.cs:477-559,713-781`。
8. 当前共享控制面注册 Resource、Collision 两种 demand policy，ResourceProductionControlSeam 接同一个投影/生命周期引擎。
   来源：`Plugin/Core/ControlPlane/MultiObserverShadowCoordinator.cs:135-165`。
9. Resource 增量补丁记录 NativeDelta 和客端 decoder 观测；区域补丁自述并输出 nativeWriteCompletion=not-observable。这些入口记录不能单独证明端到端已应用。
   来源：`Plugin/Adapters/Resource/Patches/ResourceManagerHarvestReplicationPatch.cs:494-497,551-554,588-609`；`Plugin/Adapters/Resource/Patches/ResourceManagerRegionSyncPatch.cs:455-480`。

## 事实：Collision 是激活覆盖，不是 Object 玩法状态

10. LevelObject 普通根激活受 active/cinematic、条件及重要碰撞逻辑决定；Dedicated 强制加入 collisionAlways 条件，但渲染器仍有独立判定。NPC/Decal 走另一分支。
    来源：`SDK/Level/LevelObject.cs:1049-1078`。
11. 插件 Collision Acquire 只覆盖满足筛选的 LevelObject（排除 NPC、Decal、无 Collider 等）：原来 inactive 时置 active 并登记所有权，门动画设 AlwaysAnimate。
    来源：`Plugin/Adapters/Collision/LevelObjectCollisionAdapter.cs:270-332`。
12. Release 撤销已拥有的动画 culling，LevelObject 调原生刷新方法重新决定激活；不是无条件 SetActive(false)，也不是删除原生实体。
    来源：`Plugin/Adapters/Collision/LevelObjectCollisionAdapter.cs:405-423`。
13. 当前生产 CollisionExecutionPort 在共享生命周期引擎注册，观察者投影注册也明确使用 Collision domain。
    来源：`Plugin/Core/ControlPlane/MultiObserverShadowCoordinator.cs:137-138,159-165`。

上述 10-13 仅描述碰撞/动画运行条件；地图门是否开、复位多久、是否可交互由下一节 Object 原生状态路径负责。

## 事实：地图 Object

14. Object 的状态字节存于 `LevelObjects.objects[x,y][index].state`；基线编码逐索引发送非空 state，ReceiveObjects 再应用到 interactable/rubble。
    来源：`SDK/Managers/ObjectManager.cs:741-783,792-816`。
15. 二进制状态请求检查安全坐标、玩家生死、索引/组件、可用性、当前状态、距离（mod hook 可例外）及资产条件；成功后原生可靠增量加 loopback，再更新 state[0]。
    来源：`SDK/Managers/ObjectManager.cs:593-662`。
16. Update 以 isServer 为执行守卫，轮巡所有区域；updateObjects 支持二进制状态复位、水/油回补、rubble 单节或全节恢复，各用对应原生可靠 RPC。
    来源：`SDK/Managers/ObjectManager.cs:858-924,1035-1069`。
17. Object 基线按玩家区域状态发送；服务端维护玩家 loadedRegions，客端离区重置 isNetworked。该网络范围与 LevelObject 根/renderer 激活不是同一变量。
    来源：`SDK/Managers/ObjectManager.cs:956-1027`；`SDK/Level/LevelObject.cs:1049-1078`。
18. 本轮没有核验 Object 专门的 multi-observer 生产 seam；共享控制面的两项注册只覆盖 Resource/Collision，不能把 Collision 验收自动扩大为所有 Object 交互同步验收。
    来源：`Plugin/Core/ControlPlane/MultiObserverShadowCoordinator.cs:135-165`；`SDK/Managers/ObjectManager.cs:593-662`。

## 事实：Barricade / Structure

19. 服务端 onLevelLoaded 建立二维 region，调用原生 load；Barricade 除地面区域还有 vehicleRegions。加载对象会实例化原生模型并关联 serversideData。
    来源：`SDK/Managers/BarricadeManager.cs:2832-2847,3075-3103,3338-3349`；`SDK/Managers/StructureManager.cs:1021-1035`。
20. 地面 Barricade 的服务端区域退出只清玩家 isBarricadesLoaded；客端退出排入 drops 延迟销毁队列。Structure 对应 isStructuresLoaded；服务端原生权威对象没有在这个退出分支销毁。
    来源：`SDK/Managers/BarricadeManager.cs:2852-2879`；`SDK/Managers/StructureManager.cs:1043-1067`。
21. 基线原生 Dedicated gate 分别为 Barricade step 2、Structure step 1；范围内未加载区标记后调用发送入口。单纯复用发送资格不等于全图物理保活。
    来源：`SDK/Managers/BarricadeManager.cs:2886-2902`；`SDK/Managers/StructureManager.cs:1074-1090`。
22. 真实 Barricade 基线入口是 internal SendRegion，可靠发送 SendMultipleBarricades；public askBarricades 是空壳。Structure internal askStructures 是真实分包发送入口。
    来源：`SDK/Managers/BarricadeManager.cs:2270-2318`；`SDK/Managers/StructureManager.cs:888-928`。
23. 原生 Barricade 状态增量有 BarricadeDrop.SendUpdateState；删除有 SendDestroyBarricade，均用区域/车辆身份决定接收者，而非插件账本自行替代原生状态。
    来源：`SDK/Managers/BarricadeManager.cs:1379-1392,2769-2784,3396-3423`。
24. plant < ushort.MaxValue 指 vehicleRegions 索引；ushort.MaxValue 才指地面二维区域。车辆区域注册默认 isNetworked，初始发送在车辆发送之后并携 parentNetId。
    来源：`SDK/Managers/BarricadeManager.cs:2449-2456,2510-2519,2707-2728`。
25. 地面增量以 BARRICADE_REGIONS 范围收集连接；车辆工事增量使用全体连接。全体收件人不等于全部车辆物理都处于模拟状态。
    来源：`SDK/Managers/BarricadeManager.cs:3396-3423`。
26. BuildingDomainAdapter Acquire/Release 目前调用旧领域账本；Entered 只调用 ShouldReplicateSnapshot 且丢弃返回值，没有 SendRegion/askStructures 发送动作。
    来源：`Plugin/Adapters/Structure/BuildingDomainAdapter.cs:41-57,69-76`。
27. 两个旧 SnapshotLedger 在 ShouldReplicateSnapshot 首次见观察者时就写 AcknowledgedGeneration/DeltaSequence 并返回 true；该字段不是网络 ACK。
    来源：`Plugin/Adapters/Structure/BarricadeSnapshotAdapter.cs:43-69`；`Plugin/Adapters/Structure/StructureSnapshotAdapter.cs:43-69`。
28. Barricade 放置/伤害 postfix 按 transform/point 算二维坐标，固定 plant=0、index=0 增账本代次与序号；未从原生 tryGetRegion 解析真实 plant，也没有在此发送。
    来源：`Plugin/Adapters/Structure/Patches/BarricadeStateReplicationPatch.cs:68-87,102-121`。
29. 因此需要先核清旧 Building 账本中的 plant=0 与原生地面 sentinel 的语义差异，再决定迁移身份；不能照抄为新领域契约。
    来源：`Plugin/Core/Identity/BarricadeKey.cs:14-24`；原生事实见 24；本项为从两处源码差异得出的调查结论，非已批准修改。

30. 原生 Structure 放置可靠发送 SendSingleStructure、删除可靠 SendDestroyStructure；所有者/组及移动变换也有原生实例 RPC。不是只存在区域基线。
    来源：`SDK/Managers/StructureManager.cs:113,154,282,439-480`。
31. 现有两种 Building region patch 在原生 onRegionUpdated 定位 Dedicated getter 并插资格 helper；这是本地分支资格修补，不能解释为已迁入共享生命周期。
    来源：`Plugin/Adapters/Structure/Patches/BarricadeManagerRegionSyncPatch.cs:397-427`；`Plugin/Adapters/Structure/Patches/StructureManagerRegionSyncPatch.cs:380-410`；共享注册见 8。

## 事实：无人时生产/计时的代表性差异

32. Farm 的成熟判定来自 planted 时间戳与 Provider.time 差；视觉成长协程在非 Dedicated 侧，不能把视觉 Update/协程是否运行等同于作物成熟是否推进。
    来源：`SDK/Interactable/InteractableFarm.cs:50,54-87`。
33. Farm 还在 POST_DRIZZLE 天气事件执行向上 BLOCK_WIND 射线，通过后 updatePlanted(1) 并在服务端调用 updateFarm；OnEnable 订阅天气。
    来源：`SDK/Interactable/InteractableFarm.cs:136-164`。
34. Generator 组件 Update 用 realtime-lastBurn > burn 每次扣油 1；askBurn 在服务端调用 updateState。燃油/开关初值由 state 解码，效果与 updateWire 相连。
    来源：`SDK/Interactable/InteractableGenerator.cs:47-59,98-116,259-283`。
35. RainBarrel 在 POST_DRIZZLE 事件进行 BLOCK_WIND 射线；无遮挡则置满并在服务端更新原生 state。OnEnable 订阅天气、OnDisable 退订。
    来源：`SDK/Interactable/InteractableRainBarrel.cs:37-64`。
36. 从 32-35 推断：若将整个工事对象停用，Generator 燃烧 tick、雨桶/作物事件及遮挡结果可能改变；仅保留原生 state 或恢复基线不能自动补偿遗漏的执行。具体保留/补偿政策仍待用户决定。
    来源：32-35；本项为源码推断，不是 Runtime 重现。

## 事实：保存和恢复

37. Survival 的 SaveManager.save 调玩家、Vehicle、Barricade、Structure、Object、Lighting、Group 保存；这一清单没有 ResourceManager 保存项，且全局保存清单不证明全图模拟保活。
    来源：`SDK/Managers/SaveManager.cs:42-73`。没有据此断言所有扩展保存路径都不存在。
38. Object 原生 `/Objects.dat` 保存按全域 state 列表序列化；load 在 Survival 按版本读取区域并恢复 state。
    来源：`SDK/Managers/ObjectManager.cs:1082-1127,1130-1187`。
39. Barricade `/Barricades.dat` 保存 Provider.time、instanceCount、地面全域数据及不被自动清除车辆的工事；车辆写 instanceID/subvehicleIndex 用于加载修复身份。
    来源：`SDK/Managers/BarricadeManager.cs:3120-3162`。
40. Barricade saveRegion 仅保存 isSaveable 资产，并有 Dedicated + Decay_Time + objActiveDate 筛选；不是无条件保存每件物体。序列化含健康、state、位置、旋转、owner/group、活动时间。
    来源：`SDK/Managers/BarricadeManager.cs:3356-3390`。
41. Structure `/Structures.dat` 保存 Provider.time、instanceCount 及全域数据；服务端 onLevelLoaded 调 load，单人走 isServer 权威入口，没有限定为 Dedicated。
    来源：`SDK/Managers/StructureManager.cs:1033-1035,1143-1253,1253-1270`。

## 候选职责划分（建议，待用户决定）

- 保留 Native State Authority：原生实体仓库、资产实例化/交互校验、原生可保存数据和恢复格式；multi-observer 不另造第二世界仓库（依据 1、14、19、37-41）。
- 迁移空间执行资格：房主视角外、客机需求区的插件激活覆盖、释放所有权、失败恢复与需求投影；已迁入的 Resource/Collision 作为样例，Building 不凭旧 SPI 注册提前算完成（依据 8、11-13、26-31）。
- 单独决策逻辑计时：全域轻量时间戳/轮巡可以与空间渲染/碰撞分离；但 Resource 重生遮挡、雨水射线、Generator 更新必须先选保留执行或等价补偿，不能统一“无人即停”（依据 3-4、16、32-36）。
- 当前 SPF 原生复制迁移复用已有基线/增量，不自动新增 LMN 依赖；不能新增平行权威 writer，也不能将账本 bool/Acknowledged 当发送或客端确认（依据 6-7、14-17、22-28）。功能模组与基础设施范围澄清见 [0021: SPF 原生世界同步迁移不自动成为 LMN 消费方](../../../docs/adr/0021-spf-native-world-sync-and-gameplay-mod-channel-boundary.md)。
- Building 应至少区分地面工事、Structure 与车辆挂载工事身份；地图门/地图 rubble 的玩法状态单列 Object，Collision override 只负责执行条件（依据 10-18、24-29）。

## 未查边界与后续证据

- 已查计时仅 Farm/Generator/RainBarrel 三例；未穷尽 Sentry、Oven、Oil、Trap、火源、储物权限、NPC/mod hook、远程开关及自定义资产。
- 未逐个 prefab 核对建筑 collider/renderer/Update 默认层级与真实 inactive 行为；无运行日志，因此不声称所有地面建筑全图物理保活，也不声称关闭区域后生产现状。
- 未追 Provider.time 的时钟定义/离线增长政策、LevelSavedata 单人/服务器目录选择、所有保存调用者、完整 decay/load 兼容链；不能把 native 保存入口扩大为 P2P 跨会话实测通过。
- 未完成 Object 全部插件 patch 的生产 Registration Trace；未核验现有建筑补丁的当前三端端到端表现；需要后续针对性源码追踪和 1 Host + 2 Guest 日志验收。
