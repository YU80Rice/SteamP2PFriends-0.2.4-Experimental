# Zombie / Animal / Vehicle 原生职责与迁移事实草稿

- 日期：2026-10-07；StaticSource 仅表示源码观察描述，不是新增仓库 Evidence Class；本轮无 StaticIL／Runtime PASS、实机验收或正式输出身份。
- 插件固定 HEAD：`57e3c4e4411b4f8831f0bf6d489b9fea9922c47c`。
- U3-SDK 固定 HEAD：`ea7b4973af5ba10f62baad2bfde36ab2e5b060eb`。
- 两个 HEAD 已用 `git rev-parse HEAD` 实际核对；只写本草稿，不修改生产／测试，不构建。
- 本文件受研究票约束，供后续决策；独立复核状态见 [审查链](review-chain.md)；建议不是用户已批准规格。
- 来源简写 `SDK/` = `D:/Agent-工作目录/U3-SDK/Assets/Runtime/Assembly-CSharp/Unturned/`。
- 来源简写 `PLUGIN/` = 当前仓库根目录；所有行号以以上固定源码为准。

## 首要结论：原生有多个不同层次的“全局”

- Zombie 的 U3DS 全 Bound 初始化、轮询重生，与实体 AI tick／逐帧更新不是同一集合。
  来源：`SDK/Managers/ZombieManager.cs:1558-1568,1742-1788,1811-1819`。
- Animal 原生全局 Pack 初始化与复制，同时仍有专用 tickingAnimals 子集；不能把列表存在当成全部 AI 每帧运行。
  来源：`SDK/Managers/AnimalManager.cs:721-740,789-819,410-434,1019-1052`；`SDK/Animals/Animal.cs:124-147`。
- Vehicle U3DS 全表 OnUpdate，但 FixedUpdate 受物理身份、驾驶状态、server 门约束。
  来源：`SDK/Managers/VehicleManager.cs:2853-2863`；`SDK/Interactable/InteractableVehicle.cs:4231-4233`。

## Zombie：状态池、生成与 Bound

- 权威池是 `ZombieManager.regions[bound].zombies`，Region 以导航 Bound 索引初始化；另有 AllZombies 与 tickingZombies。
  来源：`SDK/Managers/ZombieManager.cs:34-36,1534-1548`；`SDK/Managers/ZombieRegion.cs:18-25`。
- U3DS 加载时遍历全部导航 Bound 调用 generateZombies；listen-host 原生本地进入 newBound 时才生成。
  来源：`SDK/Managers/ZombieManager.cs:1558-1568,1473-1485`。
- generateZombies 使用该 Bound 的出生点、safezone 过滤、模式／FlagData 数量限制；这不是二维世界 Region 遍历。
  来源：`SDK/Managers/ZombieManager.cs:1149-1186`。
- 原生本地离开旧 Bound 会 destroy 旧 Region 并清 isNetworked；server 独立维护 PlayerCountInRegion。
  来源：`SDK/Managers/ZombieManager.cs:1448-1470`。
- 玩家创建、销毁及 Bound 转移更新该计数；计数从零到正／正到零维护 regionsWithPlayers 集合。
  来源：`SDK/Managers/ZombieManager.cs:1470-1516`；`SDK/Managers/ZombieRegion.cs:358-384`。

## Zombie：AI、计时与复制不是一个活动门

- Manager tick 遍历 tickingZombies；U3DS 每帧最多 50 个，非 Dedicated 从零遍历该子集。
  来源：`SDK/Managers/ZombieManager.cs:1742-1775`。
- 实体 isHunting 转真加入 tickingZombies，停止猎捕会移出（首生落地任务另有 needsTickForPlacement）。
  来源：`SDK/Zombies/Zombie.cs:285-335,1888,1986-1993`。
- server 的实体 OnUpdate 只由 regionsWithPlayers 中的僵尸调用；client 则遍历 AllZombies。
  来源：`SDK/Managers/ZombieManager.cs:1711-1726,1779-1788`。
- 实体 OnUpdate 死亡直接返回；server 检测位置／角度变化标记 region.updates，客机插值。
  来源：`SDK/Zombies/Zombie.cs:2719-2738,2779-2791`。
- 轮询重生每帧检查一个 Bound，内层检查一个实体；普通重生有 Dedicated 门，另有 Beacon／Horde 特例与死亡时间条件。
  来源：`SDK/Managers/ZombieManager.cs:1270-1365,1811-1819`。
- 基线按 Bound 可靠发送，包含实体数、服装／外观、位置、角度、死亡等；变化按该 Bound 的远端玩家筛选。
  来源：`SDK/Managers/ZombieManager.cs:669-708,1936-1947`。
- 周期位置变化不可靠发送；生命变化 reliable，speciality 与攻击动画类变化使用不可靠 RPC。
  来源：`SDK/Managers/ZombieManager.cs:699-763,1653-1701`。
- 死亡链可调用 dropLoot，原生 dropLoot 最终调用 ItemManager.dropItem，需与 Item 的协议和权威互相衔接。
  来源：`SDK/Zombies/Zombie.cs:951-1004`；`SDK/Managers/ZombieManager.cs:771-819`。

## Animal：原生 Pack 模型与活动边界

- server 从全局 LevelAnimals.spawns 按邻近出生点分 Pack（16 米判据），再全局随机分配到 maxInstances。
  来源：`SDK/Managers/AnimalManager.cs:721-740,789-819`。
- 实体身份使用全局 animals 列表与 ushort index；初始基线可靠发送全表，原生并非按 Bound 基线。
  来源：`SDK/Managers/AnimalManager.cs:410-434,987-991`。
- 周期位置／角度变化遍历每个合法 client，收集全局 isUpdated 动物，不按该玩家距离筛选；发送不可靠 RPC。
  来源：`SDK/Managers/AnimalManager.cs:913-943,983-991`。
- Alive／Dead reliable 广播所有远端，攻击／惊吓等动画不可靠广播所有远端。
  来源：`SDK/Managers/AnimalManager.cs:437-459`。
- Manager 只 tick tickingAnimals；U3DS 每帧最多 25 个，非 Dedicated 遍历该子集。
  来源：`SDK/Managers/AnimalManager.cs:1019-1052`。
- 实体 updateTicking 仅在逃跑、游荡或猎捕时加入 tickingAnimals，否则移出。
  来源：`SDK/Animals/Animal.cs:124-147`。
- 实体 Update 死亡返回；server 更新位移、每 8–16 秒选游荡目标、处理回血，该方法未包含观察者距离门。
  来源：`SDK/Animals/Animal.cs:945-962,1050-1074,1124-1130`。
- 以上说明 Animal 本体 Update 有自主活动职责；实际 GameObject 是否 active、插件是否抑制或 Unity 是否调用，仍需 runtime 及实例初始化链确认，不能推出“所有动物始终在模拟”。
- 重生扫描一个 Pack：必须整群死亡且死亡计时都达到阈值；复活沿用 pack 出生点，不是单 Region 租约重建。
  来源：`SDK/Managers/AnimalManager.cs:647-704,1069-1070`。
- 死亡 askDamage 调用 AnimalManager.dropLoot，后者调用 ItemManager.dropItem。
  来源：`SDK/Animals/Animal.cs:372-405`；`SDK/Managers/AnimalManager.cs:462-498`。

## Vehicle：全局身份、物理分工与跨区

- server 加载全局 vehicles、Vehicles.dat，再依地图出生点补充自然车辆；车辆查找身份是 instanceID。
  来源：`SDK/Managers/VehicleManager.cs:158-174,2540-2586,2958-3013`。
- 初始基线全表每包最多 50 辆可靠发送，随后发 BarricadeManager 的 vehicle regions。
  来源：`SDK/Managers/VehicleManager.cs:1190-1232`。
- 周期复制遍历 vehiclesNeedingReplicationUpdate，跳过该车驾驶员；对每位合法 client 发送全体其余变动车辆。
  来源：`SDK/Managers/VehicleManager.cs:2696-2749`。
- 300 米距离用于额外高质量字段选择，不是将远车排除；移动身份按 instanceID，不随二维 Region 改写。
  来源：`SDK/Managers/VehicleManager.cs:2771-2807`。
- U3DS 每帧全表 OnUpdate；非 Dedicated 依据本地相机可见性对不可见车作四组分片并积累 deltaTime。
  来源：`SDK/Managers/VehicleManager.cs:2853-2901`（2875 注释明确 listen-server 需调整）。
- updatePhysics：本地驾驶者或 server 无人驾驶车辆为 physical，其余刚体转 kinematic。
  来源：`SDK/Interactable/InteractableVehicle.cs:1490-1519`。
- FixedUpdate 仅 server、physical、未驾驶车辆继续；U3DS 与 listen-host 的车轮处理不同。
  来源：`SDK/Interactable/InteractableVehicle.cs:4229-4267`。
- entity OnUpdate Dedicated 分支检测 physical 车辆位移并加入复制；server 未驾驶 physical 车还有地下纠正。
  来源：`SDK/Interactable/InteractableVehicle.cs:3937-3947,4064-4071`。
- 有驾驶者的 simulate 重载含客户端上报位置、LAN trust 与 recovery 验证；不能把驾驶物理等同 server 无人车 FixedUpdate。
  来源：`SDK/Interactable/InteractableVehicle.cs:3050-3102`。
- 上／下车与换座有原生请求／校验／可靠广播，退出还按玩家可见性处理 culled position。
  来源：`SDK/Managers/VehicleManager.cs:1625-1794,1830-1878,2006,2021-2069`。
- 重生／销毁按全局索引轮询：乘客存在不销毁，爆炸／淹水等待配置时间；自然生成按总量与出生点处理。
  来源：`SDK/Managers/VehicleManager.cs:2343-2424`。

## 保存边界

- 原生 Survival SaveManager.save 调用 VehicleManager.save，但此链未调用 ZombieManager／AnimalManager 保存。
  来源：`SDK/Managers/SaveManager.cs:42-73`；全 Unturned 源码检索 `ZombieManager.save|AnimalManager.save` 无命中。
- 因而不能把“关房重开保留每只动物／僵尸死亡与位置”当作已证实原生承诺；其他扩展插件自有保存不在此结论范围。
- Vehicle 原生写 Vehicles.dat，跳过 autoClearable，保存 asset GUID、instanceID、位置、旋转、燃料、生命、电池、锁等。
  来源：`SDK/Managers/VehicleManager.cs:3269-3331`；加载入口：`SDK/Managers/VehicleManager.cs:2561,2958-3013,3179`。

## 当前插件接线：旧功能存在不等于新共享引擎迁移完成

- 旧 SpatialObserverIndex 已有分开的 Region／Bound 集合及 UpdateBound1D；不能称“multi-observer 完全不支持 Bound”。
  来源：`PLUGIN/Core/ControlPlane/Spatial/SpatialObserverIndex.cs:8-30,44-45,84`。
- 新共享 DemandPolicy 只允许 ChebyshevSquare2D，新的执行端口复制、代次、状态接口使用 RegionKey。
  来源：`PLUGIN/Core/ControlPlane/Demand/DemandPolicy.cs:6-13,37-38`；`PLUGIN/Core/ControlPlane/Lifecycle/IDomainExecutionPort.cs:59-73`。
- 当前共享生产注册 Resource／Collision；未见 Zombie、Animal 或 Vehicle 的该引擎生产端口注册。
  来源：`PLUGIN/Core/ControlPlane/MultiObserverShadowCoordinator.cs:129-165`。
- Zombie 旧补丁调用自己的生命周期适配器，原生 PlayerCount 为主、旧观察者需求仅交叉核验；该适配器自行维护滞回和 region.destroy。
  来源：`PLUGIN/Adapters/Zombie/Patches/ZombieManagerP0DGenerateZombiesPatch.cs:144-151`；`PLUGIN/Adapters/Zombie/ZombieRegionLifecycleAdapter.cs:128-130,172-205,212-236`。
- Zombie SPI 的 OnAcquire／OnRelease／Bound 回调仍空；已有可靠入队基线能力不能升级成客机应用确认。
  来源：`PLUGIN/Adapters/Zombie/ZombieDomainAdapter.cs:31-38,61-74`；`PLUGIN/Adapters/Zombie/ZombieSnapshotAdapter.cs:310`。
- Animal SPI 操作 Bound 账本，而 AnimalRegionLifecycleAdapter.Tick 为空；Snapshot Enqueue 只是 Ledger 调用，未执行原生动物网络发送。
  来源：`PLUGIN/Adapters/Animal/AnimalDomainAdapter.cs:32-44,68-72`；`PLUGIN/Adapters/Animal/AnimalRegionLifecycleAdapter.cs:214-217`；`PLUGIN/Adapters/Animal/AnimalSnapshotAdapter.cs:117-122`。
- Animal 周期发送旧补丁仅替换 Update 第二处 Dedicated getter，保留首处分片行为；这是复制门修复。
  来源：`PLUGIN/Adapters/Animal/Patches/AnimalManagerP0C2SendAnimalStatesPatch.cs:317-364`。
- Vehicle 旧补丁放开发送门并补 physical 位移标记；保留相机分片，未将车辆生命周期迁入共享引擎。
  来源：`PLUGIN/Core/Patches/VehicleManagerP0C1ReplicationPatch.cs:421-468,500-535`。

## 待用户裁定的建议与未查边界

- 建议复用原生实体池、合法交互、数据编码与存档；迁移重点放在插件按需执行授权、观察者相关性、生命周期与恢复，不另建实体仓库。
- Zombie 候选：替代 U3DS 全 Bound 初始化；先决定新引擎 Bound 投影与执行契约、原生计数关系、Horde／Beacon 是否例外、无人时重生计时是否继续。
- Animal 候选：先决定 Pack 身份／跨区／无人后游荡与回血／整群重生语义；原生无分 Region 生成模型，不能直接借用 Zombie Bound。
- Vehicle 候选：先决定无人车物理与计时分工、驾驶／乘客／拖挂持续需求、移动实体跨区代次、载具工事需求，不能把 Region Release 当销毁。
- SPF 原生生物/车辆复制迁移继续以原生协议为基础，不自动成为 LMN 功能模组频道接入；后续若需新增自定义消息再单独论证。范围澄清见 [0021: SPF 原生世界同步迁移不自动成为 LMN 消费方](../../../docs/adr/0021-spf-native-world-sync-and-gameplay-mod-channel-boundary.md)。
- 未查完整 PlayerMovement→车辆 simulate 输入链、所有物理／Unity active 入口、Animal ragdoll culling 与所有异常恢复；应在领域规格阶段补查，当前无性能量化或三端日志。
