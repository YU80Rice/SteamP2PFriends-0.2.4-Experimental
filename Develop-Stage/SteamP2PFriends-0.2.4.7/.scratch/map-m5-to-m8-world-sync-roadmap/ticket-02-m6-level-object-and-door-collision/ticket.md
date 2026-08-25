# Ticket 02: M6 场景物件、树木矿石资源与权限门碰撞统一接入 SPI（含 70 版本技术债归并）

## Question

如何彻底解决历史遗留的 `LevelObjectRemoteCollisionPatch` 私有轮询技术债，将 70 版本修复的“远区刷卡门/钥匙门（`InteractableObjectBinaryState`）碰撞动画剔除失效”以及“树木/矿石可采集资源（`ResourceManager` / `ObjectManager`）物理碰撞与采伐同步”统一归并重构为标准的 `Adapters/Collision/LevelObjectCollisionAdapter` 并挂载至 `SpatialObserverIndex`？

## 详细背景与 70 版本复测解惑

1. **70 版本历史背景**：
   - 0.2.3.70 版本修复了 Listen-Host 房主离开客机区域后，Elver 地图中的权限门/钥匙门开门后房主端 Unity `Animation` 被 Culling 剔除导致 Collider 未同步旋转、进而将客机持续物理拉回的缺陷。
   - 早期补丁 `LevelObjectRemoteCollisionPatch` 采用了自建的 `RemotePlayerRegions` 和 `RemoteCoverage` 私有状态机，未纳入 M 系列统一的观察者租约与影子账本中。
2. **为什么 M0~M4 未显式复测该特性**：
   - M0~M4 阶段重心集中在物品权限（M1/M2）与丧尸快照（M3/M4）上，单元测试中虽然保留了 `RC1` / `RC2` 两项底层策略单测，但实机日志中该补丁仅作为遗留后台补丁被动运行，缺乏与新架构 SPI 的闭环验证。
3. **M6 实施方案**：
   - **重构归口**：将 `LevelObjectRemoteCollisionPatch` 改造为标准的 `Adapters/Collision/LevelObjectCollisionAdapter.cs`（实现 `ILifecycleDomainAdapter`），直接接收 `SpatialObserverIndex` 的 `OnObserverEntered` / `OnObserverExited` / `OnAllObserversReleased` 调度；
   - **可采集资源支持**：拦截 `ResourceManager` 树木生成与采伐 RPC，确保远区客机砍树、挖矿时物理碰撞即时生效且掉落木头/矿石与 M1/M2 物品系统联动；
   - **双端实机全量复测**：在后续多机测试清单中设立“跨区域 Elver 刷卡门开合通行”与“远区树木砍伐物理倒下”专属测试用例。
