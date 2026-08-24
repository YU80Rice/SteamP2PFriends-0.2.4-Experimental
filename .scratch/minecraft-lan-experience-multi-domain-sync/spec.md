# 需求规格说明书：未转变者 Minecraft LAN 式本地多人联机体验与多领域全状态同步规范

## Problem Statement

在《未转变者》（Unturned）的原生游戏架构中，游戏被严格划分为两种运行形态：
1. **单人游戏（Singleplayer）**：运行完整的本地客户端与内置简化服务端逻辑，但所有世界实体（丧尸、动物、掉落物、资源节点、物理碰撞）仅围绕**房主单一视口坐标**进行加载与模拟；
2. **独立服务端（U3DS / Dedicated Server）**：运行无图形界面的独立控制台服务端，维护全图全局实体池与网络通道，但需要玩家具备服务器配置、端口映射、额外内存与进程管理等专业知识。

在 **房主自建 P2P 联机（Listen-Host）** 场景下，房主既是逻辑运算主机，又是本地操作客机。原生引擎由于缺乏多玩家视口的空间感知与按需调度机制，当客机玩家（Guest）通过网络加入房主世界并在不同区域移动时，原生逻辑无法感知远端客机的空间存在，导致出现大量致命的联机体验缺陷：
- **资源与碰撞缺失**：客机远离房主时，树木、矿石、建筑物碰撞无法正确初始化，甚至出现穿模与无法砍树/挖矿；
- **生物与丧尸不同步**：野生动物与丧尸无法在客机周围按需生成与漫游，或在客机视口中出现幽灵模型、瞬移及击中判定失效；
- **实体状态脱节**：掉落物品、防御工事、载具等在多玩家同时交互时缺乏严格的代次保护与增量同步；
- **验证覆盖不足**：此前工程仅对物品领域（M1/M2）与丧尸领域（M3/M4）进行了初步验证，尚未将动物、资源、碰撞、载具等全领域纳入统一的标准化观察者架构，缺乏对 Minecraft LAN 级别全要素联机体验的完整需求定义与规范化验证体系。

## Solution

在不启动外部独立服务端（U3DS）、不修改原版单人存档结构的前提下，构建一套标准化的 **Minecraft LAN 式本地多人联机架构**：
1. **统一控制面（Control Plane）**：构建纯内存、零引擎依赖的多观察者空间索引引擎（`MultiObserver` / `SpatialObserverIndex`），将房主与所有加入的客机抽象为对等的世界存在观察者（`World Presence Observer`），基于 2D 网格与 1D 区域统一计算多玩家视口的空间并集需求；
2. **开放式领域适配器管道（Domain Adapter SPI Pipeline）**：定义标准化的生命周期契约（`ILifecycleDomainAdapter`）与状态复制契约（`IStateReplicationAdapter`），使物品、丧尸、野生动物、树木/矿石资源、物理碰撞、载具与防御工事等所有游戏领域均以可插拔、可独立隔离的适配器形式接入；
3. **空间租约与滞后防抖调度（Region Lease & Hysteresis Release）**：为每个被观察者覆盖的世界区域颁发功能性能力凭证（`LeaseTicket`），当区域内最后一名玩家离开时提供 2 秒滞后防抖缓冲，避免频繁销毁与重建实体引发卡顿；
4. **多网络通道与准入隔离**：支持 Steam 好友邀请/大厅、IP+端口直连（局域网、虚拟局域网）、内网穿透（SakuraFrp 等反向代理）等多种连接方式，并配合 Route B 软隔离安全机制，为所有玩家提供与 U3DS 100% 等价的无缝本地联机体验。

## User Stories

1. 作为一名房主玩家，我希望直接在单人游戏菜单中一键开启 P2P 好友联机，以便无需搭建和配置复杂的 U3DS 服务端即可与朋友一起游玩。
2. 作为一名客机玩家，我希望通过 Steam 好友列表直接点击“加入游戏”或接受房主邀请，以便快速进入房主的世界。
3. 作为一名处于局域网或虚拟局域网（如蒲公英、Zerotier、Radmin）中的玩家，我希望通过输入“IP + 端口”直连房主，以便在无 Steam 社区连接时依然能正常联机。
4. 作为一名使用内网穿透工具（如 SakuraFrp、Frp）的玩家，我希望通过映射后的域名或公网端口加入房主房间，以便跨越 NAT 限制进行远程联机。
5. 作为一名远离房主的客机玩家，我希望探索地图远端的森林与矿区时，周围的树木和矿石具备完整的物理碰撞实体，以便我不会穿模且能正常砍伐树木和开采矿石。
6. 作为一名砍伐树木的玩家，我希望树木倒下、产生木头掉落物的动画与物理过程在所有附近玩家（无论房主还是客机）视口中实时同步，以便大家看到一致的世界物理状态。
7. 作为一名开采矿石的玩家，我希望矿石受到攻击时的火花特效、耐久度扣减以及最终爆出矿石资源的状态完全同步，以便多人协作采集资源。
8. 作为一名在旷野中探索的客机玩家，我希望我所在的区域能够根据环境生态按需生成野生动物（如鹿、猪、狼、熊），以便我在远离房主时也能进行狩猎与生存。
9. 作为两名同时靠近同一群动物的玩家，我希望动物的漫游 AI、受惊逃跑和反击行为在房主逻辑中统一运算并精准同步至双方客户端，以便我们能配合围捕猎物而不产生幽灵模型或重复生成。
10. 作为一名客机玩家，我希望在野外放置篝火、睡袋、木墙等防御工事（Barricade / Structure）时，放置判定、模型渲染及血量扣减即时同步给房主和其他玩家，以便我参与基地建设。
11. 作为一名驾驶载具的玩家，我希望在驾驶汽车跨越不同地图区域时，物理载具不会因为离开房主视口而被强制冻结或销毁，且副驾驶与其他乘客能平滑同步坐姿与移动轨迹。
12. 作为一名客机玩家，我希望房主和其他玩家能够准确看到我身上穿戴的衣服、防弹背心、头盔以及手持的枪械配件，以便获得完整的视觉沉浸感。
13. 作为一名与箱子和门交互的玩家，我希望储物箱物品存取、密码门开关具有严格的权威验证与状态同步，以便防止物品复制或未授权访问。
14. 作为一名因网络波动断线的客机玩家，我希望在重连后能够无缝恢复角色背包、健康状态与空间视口，并且我先前的断线不会导致房主端的空间租约发生泄漏。
15. 作为一名房主玩家，我希望在结束游戏退出世界时，所有玩家建造的建筑、采集的资源、角色数据能够安全回写到原生单人存档中，以便下次加载时完好无损。
16. 作为一名未在白名单中的访客玩家，我希望在房主批准之前进入 Route B 软隔离状态（可在世界中加载环境但无法移动、破坏或攻击），以便房主可以在游戏内图形界面中安全审核我的加入请求。
17. 作为一名第三方领域扩展开发者，我希望通过实现标准的 `ILifecycleDomainAdapter` 接口接入新的自定义实体生命周期管理，以便无需修改核心插件代码即可拓展模组生态。
18. 作为一名网络协议开发者，我希望通过 `IStateReplicationAdapter` 规范实现自定义网络状态增量同步，以便在多观察者并发视口下保证单调递增的代次一致性。

## Implementation Decisions

### 1. 分层架构与核心模块边界
- **控制面模块（Control Plane - `MultiObserver`）**：
  - 维护全局世界存在观察者集合（`MultiObserverPresenceTracker`），每个观察者分配唯一的 `ConnectionGeneration` 单调代次；
  - 纯内存空间索引中枢（`SpatialObserverIndex`），支持 2D 网格（`byte x, byte y`）与 1D 边界（`byte bound`）拓扑计算；
  - 零 Unturned / Unity 引擎耦合，不产生任何 Mono 垃圾回收抖动，提供极高的单测运行效率。
- **数据面模块（Data Plane - `Adapters`）**：
  - 按业务领域完全物理隔离：`Adapters/Item`、`Adapters/Zombie`、`Adapters/Animal`、`Adapters/Resource`、`Adapters/Collision`、`Adapters/Structure`、`Adapters/Vehicle`、`Adapters/Security`；
  - 各领域私有 Harmony 补丁强制在领域目录内的 `Patches/` 物理就近收拢（Colocation）；
  - 严格遵循 SPI 契约驱动，通过 `SpatialObserverIndex.RegisterLifecycleAdapter` 与 `RegisterReplicationAdapter` 动态挂载。
- **平台外设层（Platform Layer - `Platform`）**：
  - 统一收拢传输抽象（`Platform/Transport`）、界面交互（`Platform/UI`）与诊断自检（`Platform/Diagnostics`）。

### 2. 标准化 SPI 契约定义
所有领域适配器必须实现以下纯抽象 SPI 契约之一或两者：

```csharp
// 生命周期与空间租约契约
public interface ILifecycleDomainAdapter
{
    string DomainKey { get; }
    void OnObserverEntered(byte regionX, byte regionY, LeaseTicket ticket);
    void OnObserverExited(byte regionX, byte regionY, LeaseTicket ticket);
    void OnAllObserversReleased(byte regionX, byte regionY, LeaseTicket ticket);
    void OnSessionReset();
}

// 状态快照与增量复制契约
public interface IStateReplicationAdapter
{
    string DomainKey { get; }
    void OnObserverPresenceAcquired(ulong steamId, byte regionX, byte regionY, uint connectionGeneration);
    void OnObserverPresenceRevoked(ulong steamId, byte regionX, byte regionY, uint connectionGeneration);
    void OnRegionStateMutated(byte regionX, byte regionY, uint newRegionGeneration);
    void OnSessionReset();
}
```

### 3. 租约防抖与代次一致性模型
- **防抖卸载（Hysteresis Release）**：当某一区域观察者计数降为 0 时，控制面启动 2.0 秒物理倒计时，若期间无新观察者进入则触发 `OnAllObserversReleased`；若有新观察者进入则无缝取消销毁流程。
- **四维代次正交分离**：
  - `SessionEpoch`（会话代次）：地图重载或游戏重启递增；
  - `ConnectionGeneration`（连接代次）：特定客户端重连递增；
  - `RegionGeneration`（区域代次）：区域物理实体发生结构性突变（如树倒、矿碎）时递增；
  - `EntityGeneration`（实体代次）：实体对象复用或消亡时递增。

### 4. 故障隔离与阻断门禁（Adapter Fault Supervisor）
- 任一领域适配器发生内部未捕获异常或空间需求失步时，控制面故障熔断器将其标记为隔离态（Quarantined），记录有界回退日志（Bounded Backoff），绝不波及其他领域或拖垮游戏主循环。

## Testing Decisions

### 1. 测试接缝选择（Test Seams）
- **主要接缝**：测试套件直接建立在控制面纯内存 SPI 边界上（`SpatialObserverIndex`、`ILifecycleDomainAdapter`、`IStateReplicationAdapter`）。
- **外部行为驱动**：单元测试仅校验状态转移、租约派发、代次单调递增、断线资源回收与并发边界计算等对外公开行为，绝不绑定适配器私有内部字段。

### 2. 测试镜像分层拓扑
测试工程 `WhitelistTests` 严格与主工程保持 1:1 领域镜像拓扑：
- `WhitelistTests/Core/`：核心配置与日志策略测试；
- `WhitelistTests/MultiObserver/`：空间索引算法、多观察者世界存在与影子账本测试；
- `WhitelistTests/Adapters/Item/`：物品生成权限与状态复制测试（M1 + M2）；
- `WhitelistTests/Adapters/Zombie/`：丧尸区域租约与快照复制测试（M3 + M4）；
- `WhitelistTests/Adapters/Animal/`：野生动物生成与漫游租约测试（规划中 M5）；
- `WhitelistTests/Adapters/Resource/`：树木与矿石资源碰撞同步测试（规划中 M6）；
- `WhitelistTests/Adapters/Security/`：白名单持久化与 Route B 软隔离测试；
- `WhitelistTests/Platform/`：界面投影、网络门控与兼容性探针测试；
- `WhitelistTests/Fakes/`：纯内存隔离 Fake 上下文。

### 3. 测试通过基线
- 新增领域必须编写对应领域的覆盖单测，现有 **118 项** 回归测试套件在任何代码演进中必须维持 **100% PASS**（零警告、零失败）。

## Out of Scope

- **禁止启动外部独立服务端进程**：严禁在后台拉起 `Unturned.exe -batchmode -nographics +secureserver/LAN` 或任何 U3DS 独立服务端；
- **禁止破坏原生单人存档格式**：禁止改写房主 `Saves/Singleplayer` 目录为自定义非标二进制结构，所有同步数据必须通过 Unturned 原生序列化/反序列化机制持久化；
- **禁止全局伪造 Dedicated Server 标记**：严禁在房主图形渲染主线程中全局修改 `Dedicator.IsDedicatedServer = true`，以免破坏房主本地的音频监听器、UI 交互与渲染管线；
- **禁止跳过 Unity 主线程契约**：所有涉及 GameObject 创建、物理射线与 UI 渲染的操作必须在 Unity 主线程派发，禁止在网络后台线程直接调用引擎 API。

## Further Notes

- 本需求规格文档作为后续里程碑（M5 野生动物租约、M6 树木与矿石物理碰撞同步、M7 防御工事与载具状态复制）的顶层设计基准与验收蓝图。
- 当有新的状态同步需求提出时，应首先基于本规范中的 SPI 契约评估领域划分，在 `.scratch/` 下创建子 Ticket 并编写测试用例，实施红绿重构。
