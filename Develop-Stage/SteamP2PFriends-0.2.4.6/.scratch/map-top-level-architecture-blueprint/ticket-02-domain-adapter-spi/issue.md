# Ticket 2: 领域生命周期与复制适配器统一 SPI 契约设计

- **Label**: `wayfinder:prototype`
- **Type**: HITL
- **Parent**: `map-top-level-architecture-blueprint`
- **Status**: Open (Frontier)

---

## Question

在 M1~M4 中，Item 与 Zombie 分别实现了 `ItemGenerationAuthorityAdapter`、`ItemObserverReplicationAdapter`、`ZombieRegionLifecycleAdapter`、`ZombieSnapshotAdapter`，它们逻辑严谨但接口签名略有差异。
如何抽象出声明式的统一领域适配器契约：
- `ILifecycleDomainAdapter` (0->1 Acquire, 1->0 Hysteresis Release, Native Reconcile)
- `IStateReplicationAdapter` (Observer Snapshot, Delta Sync, Token Invalidation)

使得后续 M5（Animal、Resource、Vehicle、Structure、Barricade）可以直接以插件式 SPI 的形态挂载入 MultiObserver 引擎？
