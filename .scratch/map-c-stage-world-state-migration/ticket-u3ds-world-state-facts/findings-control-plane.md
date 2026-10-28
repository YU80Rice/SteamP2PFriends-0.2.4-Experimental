# 共享控制面与调查基线

日期：2026-10-07。首轮源码事实文档；复核状态见 [审查链](review-chain.md)，不作为冻结实施规格。

## 来源与限制

- 插件根：`D:/Agent-工作目录/DevelopMyUNMultiplayerModAndModloader/SteamP2PFriends-0.2.4-Experimental`；HEAD=`57e3c4e4411b4f8831f0bf6d489b9fea9922c47c`，调查开始时无 tracked diff。
- SDK 根：`D:/Agent-工作目录/U3-SDK`；HEAD=`ea7b4973af5ba10f62baad2bfde36ab2e5b060eb`，调查开始时无 tracked diff。下文 SDK 来源相对该根。
- 插件引用的 `../Libs/Assembly-CSharp.dll` SHA-256=`E1146353E5C9BFF901EE94829640D88919C5E89F6A6B90B22C73ABF5C1608F94`；FileVersion=`0.0.0.0`。引用入口：`SteamP2PFriends.csproj:46-49`。
- 固定源码不能单独证明该 DLL 与实际游戏、实际 U3DS 版本逐方法等价；本轮不启动游戏，不反编译或构建新的 DLL。
- 本轮是源码事实调查，不新增 Evidence Class，不宣称 PureMemory、StaticIL、BuildArtifact 或 Runtime 门禁通过。
- 对照“同 U3DS 体验”时还须固定地图、难度、配置和内容包。官方说明服务器配置可被地图覆写，显式配置与默认配置的优先级不同：[Server Configuration](https://docs.smartlydressedgames.com/en/latest/servers/server-configuration.html)；该当前文档仅补充配置边界，不替代固定 SDK 调用链。

## 已核验插件事实

1. 当前共享生产配置仅登记 Resource、Collision 的 Demand Policy 与执行入口；`Core/ControlPlane/MultiObserverShadowCoordinator.cs:126-165`，实际 reconcile 入口 `:310-311`。
2. 投影政策当前只实现 `ChebyshevSquare2D`，未实现形状会拒绝；`Core/ControlPlane/Demand/DemandPolicy.cs:11-15,38-39`。因此不能把现有二维路径直接当作生物导航 Bound 或移动载具的完整空间契约。
3. 领域端口禁止自建观察者扫描、重算需求和第二套编排状态；`Core/ControlPlane/Lifecycle/IDomainExecutionPort.cs:25-28`。这约束迁移方式，不代表所有领域已经完成生产接线。
4. Item 登记 SPI：`Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDomainModules.cs:50-52`；其 Acquire/Release、观察者进出和复制 Tick 仍为空：`Adapters/Item/ItemDomainAdapter.cs:29-62`。
5. Item 基线能力为 `ReliableEnqueueBaseline`，明确不拥有拾取删除、玩家/僵尸掉落增量；`Adapters/Item/ItemObserverReplicationAdapter.cs:265-272`。提交发生在原生调用已执行后：`:418-442`，不能宣称已收到客机应用 ACK。
6. Building 的进入回调只调用 `ShouldReplicateSnapshot` 且忽略返回值；`Adapters/Structure/BuildingDomainAdapter.cs:69-76`。该调用不能证明通过这个入口发送了真实快照；原生其他发送链路须另查。
7. 仓库对功能性联机模组有 LMN 规则（`AGENTS.md`、`CONTRIBUTING.md`），但不能自动套用于 SPF 原生世界同步迁移。[Ticket 4: 命名通信通道 (LaunchMultiplayerNet) 与双端路由规范](../../map-top-level-architecture-blueprint/ticket-04-channel-transport-routing/issue.md) 仍 Open；当前 csproj Reference 区 `:38-100` 无 LMN 引用不是本 P2 缺失判决。用户后续纠正见 [0021: SPF 原生世界同步迁移不自动成为 LMN 消费方](../../../docs/adr/0021-spf-native-world-sync-and-gameplay-mod-channel-boundary.md)。
8. 原生玩家空间更新存在二维 Region 分步和独立导航 Bound 事件：SDK `Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerMovement.cs:1843-1887`；逐玩家 loaded 状态分开记录各域：`Managers/LoadedRegion.cs:9-20`、`Managers/LoadedBound.cs:9`（后两路径同属该 SDK 的 Unturned 目录）。这不是一个所有领域共用的 loaded bit。
9. 原生区域增量收件人按每名玩家坐标与区域半径筛选：SDK `Assets/Runtime/Assembly-CSharp/Unturned/Regions/Regions.cs:263-304`；remote 版本另有 `!DEDICATED_SERVER` 编译条件下排除本地 Host。全局状态保存不意味着每次变化都向全体发送。
10. 旧 `SpatialObserverIndex` 已有独立 Bound 集合与 `UpdateBound1D`：`Core/ControlPlane/Spatial/SpatialObserverIndex.cs:39-46,84-89`。第 2 项只针对新的共享需求投影政策，不能概括为整个 multi-observer 从未支持 Bound。

## 已确定与待裁定

- 用户已确定：原生调查先行，Item 是逐域迁移起点；详见 [本阶段先做原生对照，再从 Item 开始逐域迁移](../ticket-destination/ticket.md)。
- 仍待事实支持及用户裁定：各域何时激活/休眠、无人计时是否连续或补算、Release 实际释放什么及 Item 之后的顺序；当前 SPF 原生同步不以 LMN 接入为前提。
- 调查应保留原生状态与存档的权威归属；是否改变某个原生执行门、计时扫描或空间相关性，必须逐项论证，不能按 Dedicated getter 一刀切。
- “无人区域”不能直接推导“删除世界状态”；“释放观察者订阅”也不能直接推导“停止刷新/生产计时”。具体行为留待逐域问询。

## 文档验证与实施边界

- 本轮不产生生产源码、测试或 DLL；不伪造 Red/Green。`GAP-C-P2-IMPLEMENTATION-TDD`：实施接缝、Red/Green 与完整回归归后续冻结实施票，本调查不能解除该门。
- 文档核验采用来源定位、内部链接与范围一致性检查，以及全新 Standards/Spec 独立上下文。审查记录见本目录后续 review-chain 文档。
- 不授予新 SHA/MVID/Case-ID；不改变已验收 Resource/Collision 的 Runtime 结论。
