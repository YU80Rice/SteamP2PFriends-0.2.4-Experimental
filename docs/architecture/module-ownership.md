# Ticket 04：Core / Platform / Security 模块归属

状态：源码结构完成，Runtime Pending

## 唯一权威入口

| 模块 | 当前物理权威目录 | 唯一权威入口 | Registration Trace |
|---|---|---|---|
| Core Control Plane | `Core/ControlPlane` | `MultiObserverShadowCoordinator` / `SpatialObserverIndex` | `U3-REG-05` |
| Core Identity | `Core/Identity` | `DomainIds` | `U3-REG-05` |
| Core Lifecycle | `Core/Lifecycle` | `SteamP2PFriendsPlugin` lifecycle partial | `U3-REG-07` |
| Core Registration | `Core/Registration` | `PatchRegistrationOrchestrator` + `RegistrationClosure` | `U3-REG-07` |
| Core Ownership | `Core/Ownership` | `ModuleOwnershipCatalog`（模块所有权权威根） | `U3-REG-07` |
| Platform Client | `Platform/Client` | `P2PJoinManager` | `U3-REG-01` / `U3-REG-03` |
| Platform Host | `Platform/Host` | `HostManager` | `U3-REG-03` / `U3-REG-05` |
| Platform Transport | `Platform/Transport` | `ExplicitDnsDirectIpService` | `U3-REG-01` |
| Platform UI | `Platform/UI` | `P2PNativeMenuUI` | `U3-REG-03` / `U3-REG-05` |
| Platform Diagnostics | `Platform/Diagnostics` | `RoleLogger` | `U3-REG-02` / `U3-REG-04` / `U3-REG-06` |
| Security | `Security` | `P2PApprovalManager` + `P2PWhitelistService` | `U3-REG-03` |
| Animal adapter | `Adapters/Animal` | `AnimalDomainAdapter`（生命周期/复制）；`Adapters/Animal/Patches`（Animal 补丁） | `U3-REG-05`；详见 `animal-structure-ownership.md` |
| Resource adapter | `Adapters/Resource` | `ResourceDomainAdapter`（生命周期/复制）；`Adapters/Resource/Patches`（Resource 补丁） | `U3-REG-05` |
| Collision adapter | `Adapters/Collision` | `LevelObjectCollisionAdapter`（生命周期）；`Adapters/Collision/Patches`（静态物体碰撞补丁） | `U3-REG-05` |
| Item adapter | `Adapters/Item` | `ItemDomainAdapter`（生命周期/复制）；`Adapters/Item/Patches`（Item 生成、区域同步、诊断补丁） | `U3-REG-05` |
| Zombie adapter | `Adapters/Zombie` | `ZombieDomainAdapter`（生命周期/复制）；`Adapters/Zombie/Patches`（Bound 生命周期、生成、状态复制、实体诊断） | `U3-REG-05` |

## 受控跨领域位置

`Core/Patches` 是当前唯一受控跨领域补丁位置。这里的补丁同时观察或修改多个 U3-SDK
管理器/生命周期阶段，无法由源码调用链证明属于单一领域。它们只能由
`PatchRegistrationOrchestrator` 的既有阶段入口注册；不得在 `Platform`、`Security` 或
任一 `Adapters/<Domain>` 下复制第二份实现。

Security 的准入补丁已经从 `Adapters/Security` 迁入 `Security/Patches`；领域适配器目录不再
承担准入状态机的权威实现。Transport/UI/Diagnostics 的补丁仍在各自物理子目录中，后续可
独立迁移 namespace，但本票不改变 Harmony target、owner、priority 或注册顺序。

## Ticket 05：Resource / Collision 归属

Resource 与 Collision 的物理目录和编译命名空间现在一致：

- `Adapters/Resource` 只拥有 Resource 生命周期、快照、区域同步、世界同步诊断、采伐复制和资源碰撞补丁；
- `Adapters/Collision` 只拥有静态 LevelObject 碰撞适配器及远端静态物体碰撞补丁；
- `LevelGroundRemoteTreeCollisionPatch` 保留在 Resource，因为它以 ResourceSpawnpoint 和树木/矿石资源状态为原生锚点；
- `Core/Patches` 不再编译出上述 Resource/Collision 五个旧命名空间权威类型，避免物理目录与注册入口分裂。

资源补丁仍由 `PatchRegistrationOrchestrator` 的既有 Wrapper/Region 阶段按原调用顺序登记；
Host 会话复位、断线清理和注册后验证只改为指向同一领域入口。旧生产 Authority Writer 仍是唯一
生产权威，Resource Production Control Seam 本票不接线。

## Ticket 06：Item / Zombie 归属

Item 与 Zombie 的补丁、生命周期辅助类型和领域适配器已统一到各自 `Adapters/<Domain>/Patches`
与 `Adapters/<Domain>` 物理根；`Core/Identity` 继续是 `RegionKey`、`BoundKey` 和生命周期轴的唯一身份来源。
旧 `Core.Patches`、`Core.Patches.P0EZombieLifecycle` 与 `Core.Patches.P0EDiagnostic.ZombieEntityMappingDiagnosticPatch`
不再编译出 Item/Zombie 权威类型。注册与验证仍由原有 Registration Trace 入口执行，未添加兼容副本或并行 writer。

## 保持不变

- P2P 频道、SteamID、配置键、插件 GUID、Route B 状态机和现有日志文本语义不变；
- Registration Trace 的七个阶段及其 U3-SDK 锚点不变；
- Resource Production Control Seam 与旧 Authority Writer 未迁移；
- `Core/ControlPlane` 仍是纯内存控制面，物理目录整理不代表 Runtime 已接线。

## Ticket 07：Animal / Structure / Barricade / Vehicle

`Adapters.Animal` 的两个 AnimalManager 补丁已与 `Adapters/Animal/Patches` 对齐；
`Adapters.Structure` 与 `Adapters.Barricade` 共享 `BuildingDomainAdapter`，但分别保留
Structure/Barricade 的补丁入口。区域同步补丁和 `P0EBarricadeLifecycle` 已从
`Core/Patches` 移入 `Adapters/Structure/Patches`，旧命名空间不再编译出权威类型。

`Adapters.Vehicle` 为 Pending：VehicleManager 世界同步、原生生命周期和交互诊断仍由
`Core/Patches` 承载，当前没有足够源码证据证明单一领域适配器边界，因此不移动、不新增
兼容入口。其他未确认的跨领域诊断/审计补丁同样保留在 `Core/Patches`，原因登记在
`docs/architecture/animal-structure-ownership.md`。

`Adapters.Object` 同样为 Pending：`ObjectManagerRegionSyncPatch`、
`ObjectManagerWorldSyncDiagnosticPatch` 与 `Issue7ObjectBinaryStateDiagnosticPatch` 的
区域同步、诊断和二进制观察职责尚未形成可证明的单一适配器边界，因此保持在
`Core/Patches`，不创建并行 `Adapters/Object` 权威入口。
