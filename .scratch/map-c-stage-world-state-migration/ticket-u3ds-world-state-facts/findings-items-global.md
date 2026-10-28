# Item、存档、全局与玩家状态入口盘点

- 状态：首轮源码事实文档；复核状态见 [审查链](review-chain.md)；不能作为冻结规格或实施授权。
- 证据：StaticSource 仅描述源码观察，不是仓库新增 Evidence Class；不是 Runtime；未构建、未部署、未授予 DLL 身份。
- 核查日期：2026-10-07。
- 插件固定 HEAD：`57e3c4e4411b4f8831f0bf6d489b9fea9922c47c`；本轮 `git rev-parse HEAD` 相符。
- SDK 固定 HEAD：`ea7b4973af5ba10f62baad2bfde36ab2e5b060eb`；本轮 `git -C D:/Agent-工作目录/U3-SDK rev-parse HEAD` 相符。
- 下文 `SDK/` 展开为 `D:/Agent-工作目录/U3-SDK/Assets/Runtime/Assembly-CSharp/Unturned/`。
- 下文 `Plugin/` 展开为本仓库根 `D:/Agent-工作目录/DevelopMyUNMultiplayerModAndModloader/SteamP2PFriends-0.2.4-Experimental/`。
- 路径后的 `:行号` 均针对以上固定 checkout；本文件只盘点分配的域，不穷尽全部世界行为。

## Item：权威数据与空间表现分开

1. `onLevelLoaded` 为全图分配 `ItemRegion[,]`，清零 instance 与计时游标；Dedicated 才逐区调用 `generateItems`。这是全图权威数据初始化，不能单独证明全图物理实例活跃。来源：`SDK/Managers/ItemManager.cs:920-950`。
2. `generateItems` 按地图 spawn 与 Spawn_Chance 选自然物品，产生 ItemData；替换区域自然列表时保留 `isDropped` 物品。它不是对同一自然列表的无损重发。来源：`SDK/Managers/ItemManager.cs:847-907`。
3. 原生单机在本地玩家进入区域时调用生成；服务器为每玩家设置 `loadedRegions[x,y].isItemsLoaded`，Dedicated 发 `askItems`，非 Dedicated 在本地构造表现实例。来源：`SDK/Managers/ItemManager.cs:1017-1065`。
4. `Update` 尾部 Dedicated gate 之后按 X/Y 游标轮转全图区域进行 despawn 与 respawn；不是一帧无界处理全部物品。来源：`SDK/Managers/ItemManager.cs:1201-1269`。
5. despawn 比较 realtime 与 `lastDropped`，按 `isDropped` 选择 Dropped/Natural 时间；删除 ItemData 后可靠发送 Destroy，单次只移除一件。来源：`SDK/Managers/ItemManager.cs:731-752`。
6. respawn 检查 lastRespawn、目标数量、随机 spawn、safezone/间距等，添加 ItemData 并向相关区域连接可靠发送 Item。来源：`SDK/Managers/ItemManager.cs:767-838`。
7. 计时扫描和 ItemData 存在不等于刚体保活：物理实例 `drops` 在生成模型处加入，LevelObjects 尚在更新时禁用重力/设 kinematic；区域激活后恢复刚体。来源：`SDK/Managers/ItemManager.cs:457-508,954-975`。
8. 客户端出区把表现 drops 排入延迟销毁并清 `isNetworked`；服务器另清该玩家 loaded 标志。这不能解释为删除服务器 ItemData。来源：`SDK/Managers/ItemManager.cs:980-1009`。

## Item：原生已有复制与交互权威

9. `askItems(ITransportConnection, byte, byte, float)` 对单区域 ItemData 分包可靠发送；空区域也发空包。公开旧 `askItems(CSteamID,...)` 重载为空，研究与实现须锁定正确签名。来源：`SDK/Managers/ItemManager.cs:639-684`。
10. `ReceiveItems` 处理首包清表现、设网络状态并排队实例化；`ReceiveItem` 对未 networked 区域不应用新增。这要求单独研究基线/增量到达衔接，可靠入队不能等同客机应用确认。来源：`SDK/Managers/ItemManager.cs:528-633`。
11. `dropItem` 做散布、落点 SphereCast、asset/扩展事件检查，然后添加 ItemData 并向 `ITEM_REGIONS` 相关连接可靠发送 Item；不需要从零设计一个掉落协议。来源：`SDK/Managers/ItemManager.cs:157-212`。
12. 客户端通过区域与 instanceID 发拾取请求；服务端检查区域、玩家、死亡/逮捕状态，查 ItemData；Dedicated 另校验距离。来源：`SDK/Managers/ItemManager.cs:137-151,294-331`。
13. 权威背包 `tryAddItem` 成功才移除地面 ItemData 并可靠发 Destroy；背包不足则发送 SPACE。来源：`SDK/Managers/ItemManager.cs:361-388`。
14. 玩家手动丢弃由 `PlayerInventory.ReceiveDropItem` 校验 page/jar/asset/allowManualDrop，调用 `ItemManager.dropItem` 后 `removeItem`；这里是背包域与地面物品域的事务交界。来源：`SDK/Player/PlayerInventory.cs:984-1040`。

## Item：当前插件控制面缺口

15. 当前生产注册调用 AuthoritativeItemGenerationGate、ItemManagerUpdateDedicatedGate、ItemManagerRegionSyncPatch。来源：`Plugin/Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs:326-333,459-469`；`Plugin/Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationTransportModules.cs:108-118`。
16. 生成 gate 以区域状态 NotStarted→Preparing→Committed 防重复，失败可 Abort；M1 则在原生 onRegionUpdated 中拓宽远端观察者生成资格。来源：`Plugin/Adapters/Item/Patches/AuthoritativeItemGenerationGatePatch.cs:62-105`；`Plugin/Adapters/Item/ItemGenerationAuthorityAdapter.cs:41-81`。
17. M2 明确只监督原生可靠 `askItems` 基线，不拥有拾取删除或玩家/僵尸掉落增量；连接代次改变时会清该玩家 loaded 标志。来源：`Plugin/Adapters/Item/ItemObserverReplicationAdapter.cs:266-272,328-353`。
18. ItemDomainAdapter 的 Acquire/Release/Tick、观察者进入/退出/断线、ReplicationTick 都是空实现；仅 session/reset 设置复制 ready。不能因 SPI 名义注册认定已经完成共享生命周期接线。来源：`Plugin/Adapters/Item/ItemDomainAdapter.cs:19-66`。

19. 旧 P0B3 文件仍包含全图预生成 transpiler，且其 postfix 可调用 P0B6 再生成；但在当前 `Core/Registration` 的 `.cs` 文本检索未找到该 RegisterManual 调用。只确认文件存在与检索结果，不能将旧文件注释当作当前生产注册。来源：`Plugin/Adapters/Item/Patches/ItemManagerP0B3PreGeneratePatch.cs:340-406,460-468`；对比事实 15 的当前调用点；未核验运行时 Harmony 清单。
20. 当前周期 gate 实现把 Update 唯一 Dedicated getter 替换为 `IsDedicatedOrP2PHost`，保留原生 despawn/respawn 算法；不是共享需求驱动的计时策略。来源：`Plugin/Adapters/Item/Patches/ItemManagerUpdateDedicatedGatePatch.cs:338-396`，注册见事实 15。

## 保存与恢复：不能承诺地上物品跨会话存在

21. Survival 的 `SaveManager.save` 依次保存玩家、Vehicle、Barricade、Structure、Object、Lighting、Group；这个调用列表没有 ItemManager、ResourceManager、ZombieManager、AnimalManager。来源：`SDK/Managers/SaveManager.cs:42-82`。该结论只针对这里的核心保存链。
22. 已核查 ItemManager 完整源码没有 save/load 文件写入入口；新地图加载重新创建全图区域数组。结合事实 21，原生核心链不提供地面 ItemData 的跨会话恢复，不能把它写入“对齐 U3DS”默认验收。来源：`SDK/Managers/ItemManager.cs:1-1311`，初始化关键点 `:920-950`；`SDK/Managers/SaveManager.cs:57-73`。
23. 玩家背包是另一回事：Player.save 调用 inventory/life/skills/clothing/animator/quests 等；Inventory load 读取玩家 Inventory.dat，save 对死亡与丢失配置另有分支。不能由“背包保存”推出“地上掉落保存”。来源：`SDK/Player/Player.cs:1795-1804`；`SDK/Player/PlayerInventory.cs:1861-1896,1932-1958`。
24. Shutdown 与 Disconnect 在服务端且 Level loaded 时，中断延迟任务奖励后保存；Shutdown 调全局 save，Disconnect 调相应玩家 save。来源：`SDK/Managers/SaveManager.cs:85-131`。
25. 原生 ServerSavedata Dedicated 使用 `/Servers`，非 Dedicated 使用 `/Worlds`，拼 Provider.serverID；LevelSavedata 加 `/Level/<地图名>`。这是存档路由差异，不是生命周期需求政策。来源：`SDK/Files/ServerSavedata.cs:9-41`；`SDK/Files/LevelSavedata.cs:15-38`。
26. PlayerSavedata 在 hasSync 时使用 `/Sync/<SteamID>_<characterID>/<地图名>`，否则走 ServerSavedata `/Players/...`；本轮未穷尽 hasSync 所有赋值及插件 override，不能仅凭原生路径承诺 P2P Host/Guest 最终落盘位置。来源：`SDK/Files/PlayerSavedata.cs:10-66`。

## 时间、天气：全局权威和持续时钟

27. Lighting 的初始全局状态包含服务器时钟、周期/偏移、月相、风、活动天气 GUID/混合值/NetId 与 dateCounter，按连接可靠发给新玩家。来源：`SDK/Managers/LightingManager.cs:363-426`。
28. Provider 初始全局链除 Lighting 还调用 Vehicle、Animal、Level、Zombie；玩家初始状态独立调用 Player.SendInitialPlayerState。这里“Global”指初始分发入口，不表示每域都需要全图物理活跃。来源：`SDK/Provider/Provider.cs:4749-4757,4947-4957`。
29. Lighting 使用 Provider.time/offset/cycle 更新日内进度；Server 发风和 dateCounter 更新，活动天气变化也可靠发远端连接。来源：`SDK/Managers/LightingManager.cs:148-153,513-567`。
30. 天气预报与活动计时按 deltaTime 倒计时；Update 在 `Provider.isServer && shouldTickScheduledWeather` 时驱动，未使用 Region Lease。Dedicated 的 weather blend 更新另有 gate，须与客户端渲染混合区分。来源：`SDK/Managers/LightingManager.cs:579-655,914-948`。
31. Lighting.dat 保存 cycle/time、weather stage/计时器/GUID/blend/dateCounter，并在 load 恢复；是少量全局状态持久化。来源：`SDK/Managers/LightingManager.cs:960-1051`。

## 玩家与事件：入口盘点，未穷尽

32. Player.SendInitialPlayerState 汇集 clothing、inventory、life、skills、stance、quests、equipment、animator；多人列表重载不发送 inventory。Inventory 初始发送仅给主人，LocalPlayer 则直接投影本地库存事件。来源：`SDK/Player/Player.cs:1650-1670`；`SDK/Player/PlayerInventory.cs:1211-1232`。
33. 玩家 life/quests 各自持有 load/save 和初始发送入口；这不是世界区域仓库。位置/背包/生命/装备/任务不应因附近 Item lease 释放而随之销毁。前句是源码事实，后句为边界建议。来源：`SDK/Player/PlayerLife.cs:388-400,2476-2529`；`SDK/Player/PlayerQuests.cs:2296-2328,2647-2870`；Player 保存入口见事实 23。
34. BeaconManager 按导航 nav 建列表，参与者数来自同 nav 且存活的 Provider.clients；register/deregister 发 onBeaconUpdated。导航身份与二维 Item Region 不能直接互换。来源：`SDK/Managers/BeaconManager.cs:18-37,52-87`。
35. InteractableBeacon 在服务端 Update 延迟三秒后，若没有同 nav 存活玩家会造成自毁伤害；ManualOnDestroy 路径注销，满足完成条件后跟踪参与者 horde kill，并经 ItemManager 发奖励掉落。来源：`SDK/Interactable/InteractableBeacon.cs:97-127,184-215,248-255`。
36. 因此 Beacon 牵涉 Barricade 实体、Zombie/nav 事件、PlayerQuests 与 Item 奖励；简单套用二维 Item Acquire/Release 不能覆盖这些行为。此为事实 34-35 的架构推论，非已批准模型。

37. Life 初始统计值只发 owner，其他观察者只在死亡时收 Dead；Quests 则区分 AllState 与 OwnerState，LocalPlayer 给自己时跳过 RPC。世界状态接收人不能一律扩大为所有玩家。来源：`SDK/Player/PlayerLife.cs:388-405`；`SDK/Player/PlayerQuests.cs:2296-2339`。

## 面向迁移问询的具体差异

- 玩家去无人城镇：原生 U3DS 的 ItemData 曾在地图初始化时生成且可能经过全局刷新/消失；按首次到访生成会改变随机时点与计时历史。依据事实 1-6；尚未决定这种差异是否允许。
- 玩家离开掉落区：客户端表现可以释放，服务端物品列表仍是拾取/刷新权威。依据事实 8、11-13；释放租约与删除业务对象必须分开问询。
- 两人拾取同一件：原生已有查 instanceID→背包成功→删除路径。依据事实 12-13；重复请求、连接代次和消息竞争仍须运行证据。
- 玩家重连与房间重启：重连基线可读取当前会话 ItemData，重启重新初始化地图物品数组。依据事实 1、9、17、21-22；两者不能合成一个“恢复”验收场景。
- 玩家全部离开某区：Lighting 仍是服务端全局时钟；Beacon 却能因 nav 内无人而自毁。依据事实 30、35；不能制定所有领域同一无人行为。
- 玩家加入房间：global、每玩家公开状态、owner 私有状态与区域物品基线是不同入口。依据事实 9、27-28、32、37；相关性与权限检查需要分别保留。

## 候选职责划分（建议，需用户问询后冻结）

以下建议依赖上述编号事实；不是迁移顺序、功能范围或接口签名的批准。

| 职责 | 候选处理 | 事实依据 |
|---|---|---|
| ItemData 权威仓库与原生拾取校验 | 继续复用原生，避免复制第二实体账本 | 1、11-14 |
| 全图 Item 首次生成 | 与玩家可见语义一起决策；可候选改为首次领域需求触发 | 1-3、16、19 |
| Item 周期自然刷新/消失 | 决策继续全图轻量计时、需求内计时或进区补算；不能直接随 Release 删除 | 4-6、20 |
| Item 客户端表现和远区需求 | 纳入 Item 专属需求/租约，保留每观察者基线与连接身份 | 7-8、16-18 |
| 原生基线与增量 | 当前 SPF 迁移复用原生协议，核查实际一致性与恢复；不自动新增 LMN 依赖 | 9-17；后续 [0021: SPF 原生世界同步迁移不自动成为 LMN 消费方](../../../docs/adr/0021-spf-native-world-sync-and-gameplay-mod-channel-boundary.md) |
| Lighting 时间/天气 | 候选保留全局权威与少量持续时钟，不强行绑到二维租约 | 27-31 |
| 玩家保存、背包及任务 | 保持玩家身份生命周期，单独核查 P2P 初始化与存档路由 | 23-26、32-33 |
| Beacon 等事件 | 明确事件/nav 需求与跨域调用，再设计租约策略 | 34-36 |

## 本轮未查边界与后续问询入口

- 不穷尽所有玩家移动、装备、伤害、技能、UI 及插件组队/死亡桥；本轮只有初始/保存/关键事务入口。
- 未穷尽所有天气 asset 的服务器效果、LevelLighting 客户端 blend 实现、地图自定义天气脚本。
- 未穷尽全部任务条件/奖励、NPC 商店、空投、竞技场、节庆与地图脚本事件；Beacon 仅为跨域样例。
- 未核验 ItemManager 之外的扩展插件 onPreSave/onPostSave 是否保存掉落；不能把核心链结论泛化到第三方持久化插件。
- 未通过三端日志验证增量与基线排序、两人同时拾取、离区后再进、重连、自然消失和刷新；源码仅说明路径存在。
- 未证明运行时 Harmony 注册成功；旧 P0B3/P0B6 代码存在不等于激活。
- 下一轮用户需决定：无人区域的自然物品“真实时间继续变化”还是“等待观察/补算”，以及首次到访是否应与 U3DS 当前世界分布等价。
- 下一轮需决定：地面掉落跨会话持久化是否属于另加功能；不要默认把它纳入 U3DS 对齐。
- Item 首切片候选应先冻结需求、一次生成、释放不删权威数据、基线/增量职责；完整实现票待全域依赖盘点与问询后再生成。
- 本轮没有生产修改或构建，不虚构 Red/Green；实施 TDD 缺口由主报告具名 `GAP-C-P2-IMPLEMENTATION-TDD` 承接。
