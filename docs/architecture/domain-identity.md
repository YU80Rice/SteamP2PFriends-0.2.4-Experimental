# Domain Identity Contract

## 目的

Ticket 03 将跨模块身份集中到 `SteamP2PFriends.Core.Identity`。身份类型是接口的一部分；显示文本、原生索引和领域内部复合键不得互相冒充。

## 类型

| 类型 | 拓扑/职责 | 允许的边界转换 |
|---|---|---|
| `DomainId` | 领域机器身份；注册、协议和持久化判断使用 | 仅由固定 `DomainIds` 创建 |
| `RegionKey` | 二维世界区域 `(X,Y)` | 在 U3-SDK 原生边界显式由坐标或 `Packed` 编码转换 |
| `BoundKey` | 一维 Zombie/Animal 导航 Bound | 在 U3-SDK 原生边界显式由 `byte` 转换；`255` 为无有效 Bound |
| `BarricadeKey` | `RegionKey + plant` 的防御工事复合身份 | 仅在原生边界由 `(x,y,plant)` 构造 |

`RegionKey` 与 `BoundKey` 没有互相转换。两者在 `SpatialRelevanceDiff`、`SpatialObserverIndex` 和 M0 shadow demand 中使用独立集合。

## Domain Id 与显示名称

适配器通过 `DomainId` 暴露不可变机器身份，通过 `DisplayName` 暴露日志/诊断文本。`RegistrationClosure` 以 `DomainId` 作为字典键；显示名称变化不会改变注册身份。

## Packed 兼容边界

`RegionKey.Packed` 仍是 U3-SDK 原生 `int` 的显式边界编码；`RegionKey` 不再提供到/自 `int` 的隐式转换。`BoundKey` 同样不提供到/自 `byte` 的隐式转换。跨模块接口必须使用值对象，只有原生 patch 边界调用 `FromPacked`、`ToNative` 或坐标构造方法。

## Generation 轴

`SessionEpoch`、`ConnectionToken/Generation`、`RegionGeneration` 和 `EntityGeneration` 继续保持独立。`Core.Identity.LifecycleAxes` 为这些轴提供不同的值对象；`LeaseTicket` 已使用 `SessionEpoch` 与 `RegionGeneration`，其余领域继续按既有计数算法逐批迁移，不在本票据合并生命周期状态。

## 领域归属

- `Adapters.Resource`：Resource 生命周期、快照和 Resource patches；本票据不接入 Production Control Seam。
- `Adapters.Zombie`：Zombie 生命周期、快照和 Zombie patches。
- `Adapters.Animal`：Animal 生命周期、快照和 Animal patches。
- `Adapters.Structure`、`Adapters.Collision`、`Adapters.Item`：保持现有领域归属。
- `MultiObserver`：只保留观察者控制面、Spatial Index 和 SPI；领域实现不得以 `SteamP2PFriends.MultiObserver` 作为命名空间归属。
- `Core.Identity`：只提供身份值对象与固定 Domain Id 来源，不拥有领域行为。
