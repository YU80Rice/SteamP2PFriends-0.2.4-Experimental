# Collision Migration Slice 的权威边界是什么

- **Label**: `wayfinder:done`
- **Type**: HITL
- **Parent**: `map-collision-migration-slice`
- **Status**: Closed
- **Owner**: YU80Rice
- **Closed**: 2026-09-18
- **Blocked by**:

---

## 问题

Collision 切片完成后，观察者空间事实、领域需求投影、插件侧状态执行和 Unturned 原生状态分别由谁负责？`RegionKey`、Domain Demand 与 Capability 是否必须彼此区分？

## 决议

本票选择两个推荐项。Collision Migration Slice 采用「共享空间事实、分域需求投影、分域执行、原生状态不复制」的权威边界。`SpatialObserverIndex` 提供统一的观察者空间事实；Resource 与 Collision 分别形成本领域的 typed Demand / Region Lease，并仅执行各自拥有的插件侧能力。Unturned 原生 Manager 与可见性 tracker 继续保持原生状态权威。

`RegionKey` 仅表示区域身份，不表示领域需求或执行资格；Resource Demand 与 Collision Demand 相互独立，不得从另一领域的活跃状态推导本领域资格。

因此，Collision 对 `ResourceSpawnpoint.enable()/disable()` 的写入必须退出生产调用图，Resource 也不得继续读取 Collision 私有的 `RemoteCoverage`/`IsRegionCovered`。迁移期间仅允许无副作用的影子比较，禁止双写和静默回退旧 Writer。Collision 域内部是否进一步拆分门动画、静态 Collider 等 Capability，留待后续决策。

### 强制推论（随 Q1 生效，非另选）

- **A**：插件生产调用图中，只有 Resource Execution Authority 可以根据 Resource Demand 对 ResourceSpawnpoint 的原生激活状态进行补充或覆盖；Collision 不得直接写入。Unturned vanilla 原生 tracker 仍保留其原生执行职责。
- **B**：Host 位于区域 R、Guest 进入后再离开、远端需求归零但 Host/vanilla 或 Resource demand 仍在时，Collision 不得关闭 R 中的 ResourceSpawnpoint。一个领域或观察者来源的需求归零，不等于全局需求归零。
- **C**：Resource 不能 fallback 到 `LevelObjectRemoteCollisionPatch.IsRegionCovered`；Collision 不能在 Resource seam 未就绪时接管树木/矿石；不允许通过跨域私有状态「临时救一下」。

### 验收约束（写入后续 spec，本票不实施）

生产调用图：Collision 不再调用 `ResourceSpawnpoint.enable/disable`；Resource 路径不再调用 `LevelObjectRemoteCollisionPatch.IsRegionCovered`；Collision adapter/patch 不扫描 `Provider.clients` 建立私有 observer coverage；两域不互读对方可变静态集合；跨域使用 typed control-plane contract，而不是裸 `int` region encoding。

行为边界：Host 位于区域时 Guest 离开不能关闭 Host 脚下资源；Resource Demand 存在而 Collision Demand 不存在时只激活 Resource 所需能力；反之不得借机开启资源实体；一个领域故障不得撤销另一个领域的有效 Lease；一个领域 Release 只能撤销该领域确实 Acquire 且仍拥有的插件能力。

证据边界：PureMemory 证明跨域 Demand 独立；StaticIL/调用图证明旧跨域 Writer 和私有读取退出；Runtime 证明 Host-only、Guest-only、Guest 离开、双 Guest 同区/跨区时没有跨域误释放；「新 Writer 有日志」不等于旧 Writer 已退役。

## 资产

- glossary 修订：`CONTEXT.md`（Observer Spatial Authority / Domain Demand Authority）
- ADR：[0009 共享空间事实与分域执行](../../../docs/adr/0009-shared-spatial-facts-and-per-domain-execution.md)
