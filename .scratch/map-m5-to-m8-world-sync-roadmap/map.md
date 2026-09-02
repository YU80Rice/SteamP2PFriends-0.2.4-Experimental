# Wayfinder Map: M5 至 M8 全要素世界状态同步演进路线图

## Destination

通过 M5（动物）、M6（场景与门碰撞/资源）、M7（建筑与工事）、M8（载具）的渐进式适配器实现与验证，将 Unturned 所有世界交互要素完整接入纯内存 `SpatialObserverIndex` 控制面，最终达成与 U3DS 100% 等价的 Minecraft LAN 本地联机体验。

## Notes

- **领域内聚铁规**：每个领域的 Patch 必须与其 Adapter 位于同级目录（`Adapters/<Domain>/Patches/`）。
- **SPI 标准契约**：必须基于 `ILifecycleDomainAdapter` 与 `IStateReplicationAdapter` 接入 `SpatialObserverIndex`，严禁新增独立的私有区域轮询逻辑。
- **历史回归门禁**：现有 118 项单元测试与历史 Stage 7（70版本权限门、Route B 白名单）特性必须持续保持 100% 全绿。

## Decisions so far

- [Step 1 ~ Step 5 顶层架构重构完成](../map-top-level-architecture-blueprint/map.md): 完成了 5 大核心重构步骤，建立 SPI 契约、控制面/数据面分离、Plugin.cs 瘦身至 267 行及领域镜像测试套件（118/118 PASS）。

## Frontier (Takeable Tickets)

- [Ticket 01: M5 野生动物（Animal/Fauna）多观察者生成与漫游租约适配器](ticket-01-m5-animal-lifecycle-adapter/ticket.md) (`wayfinder:done`, archived in 0.2.4.5)
- [Ticket 02: M6 场景物件、树木矿石资源与权限门碰撞统一接入 SPI（含 70 版本技术债归并）](ticket-02-m6-level-object-and-door-collision/ticket.md) (`wayfinder:done`, archived in 0.2.4.6)
- [Ticket 05: M7 玩家建筑与防御工事（Barricade / Structure）跨区域生命周期与状态复制适配器](ticket-05-m7-barricade-and-structure-replication/ticket.md) (`wayfinder:task`, IN PROGRESS)

## Not yet specified

- **M8 载具（Vehicles）跨区域物理运算与多乘客坐姿同步**：待实体层空间租约全面成熟后进行细化拆解。

## Out of scope

- 任何需要启动 U3DS 独立服务端外部进程的方案。
- 破坏 Unturned 原生单人存档格式的改动。
