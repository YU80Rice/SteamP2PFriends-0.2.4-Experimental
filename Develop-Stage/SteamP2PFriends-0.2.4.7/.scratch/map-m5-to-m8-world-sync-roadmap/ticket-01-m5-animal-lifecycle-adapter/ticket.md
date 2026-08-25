# Ticket 01: M5 野生动物（Animal/Fauna）多观察者生成与漫游租约适配器

## Question

如何将《未转变者》中的野生动物生态系统（`AnimalManager`）接入纯内存 `SpatialObserverIndex`，使旷野远区客机周围的动物能够按需生成、漫游 AI 正确运算，且多玩家接近同一区域时不发生实体重复与幽灵击中？

## 详细背景与实施方案

1. **当前现状**：
   - 原生 Unturned 单人逻辑下，`AnimalManager` 仅在房主靠近动物生成点（Navmesh 导航网格区域）时触发生成并驱动 AI。
   - 客机远离房主时，客机视口周围无法自然生成鹿、狼、熊等野生动物，破坏了单人/U3DS 等价生存体验。
2. **实施计划**：
   - 建立 `Adapters/Animal/AnimalRegionLifecycleAdapter.cs`（实现 `ILifecycleDomainAdapter`）；
   - 建立 `Adapters/Animal/AnimalSnapshotReplicationAdapter.cs`（实现 `IStateReplicationAdapter`）；
   - 拦截 `AnimalManager.generateAnimals` 与 `AnimalManager.sendAnimal`，由控制面租约统一驱动生成与消亡；
   - 编写 `WhitelistTests/Adapters/Animal/` 自动化单元测试（覆盖首次获取、代次推进、滞后防抖与断线清理）。
