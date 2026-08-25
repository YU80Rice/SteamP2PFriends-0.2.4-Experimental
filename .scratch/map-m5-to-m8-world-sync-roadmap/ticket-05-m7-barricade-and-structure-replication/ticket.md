# Ticket 05: M7 玩家建筑与防御工事（Barricade / Structure）跨区域生命周期与状态复制适配器

## 目标与背景

在 Unturned 中，玩家放置的防御工事（`Barricade`，如储物箱、床、发电机、聚光灯、铁丝网等）与建筑结构（`Structure`，如地板、墙壁、门框、屋顶等）是多人联机生存的核心载体。

1. **核心需求**：
   - 将原版散落的 `BarricadeManagerRegionSyncPatch` 与 `StructureManagerRegionSyncPatch` 重构归口至标准的 `Adapters/Structure/` 领域命名空间；
   - 实现 `BarricadeRegionLifecycleAdapter` 与 `StructureRegionLifecycleAdapter`（实现 `ILifecycleDomainAdapter` 与 `IStateReplicationAdapter`），统一接入 `SpatialObserverIndex`；
   - 建立 `BarricadeSnapshotAdapter` 与 `StructureSnapshotAdapter`，为观察者视口重连、跨区域跨服放置提供四维代数隔离（`SessionEpoch` $\perp$ `ConnectionGeneration` $\perp$ `RegionGeneration` $\perp$ `BuildingGeneration`）；
   - 拦截建筑的放置（`askPlace`）、损坏（`askDamage`）、修复（`askRepair`）、回收（`askSalvage`）与交互状态（`updateState`），驱动状态序列号（DeltaSequence）单调递增并广播全端；
   - 确保 `Route B` 白名单安全隔离与权限阻断（如非白名单玩家禁止破坏/回收工事）。

## 架构接缝与 TDD 单测规划（M7B01~M7B08 + M7S01~M7S08 共 16 项新测试）

### 1. Barricade 领域单测（M7B01~M7B08）
* `M7B01_FirstAcquireCreatesGeneration`: 首次激活 2D 工事网格生成初始代次 1；
* `M7B02_PlaceBarricadeAdvancesGeneration`: 放置工事驱动该区域代次递增；
* `M7B03_DamageBarricadeAdvancesGeneration`: 破坏/扣血驱动代次递增并记录状态快照；
* `M7B04_UpdateStateAdvancesDeltaSequence`: 交互状态变更（储物箱/发电机/门锁）递增 Delta 序列号；
* `M7B05_ReleaseHysteresisDeadline`: 离开工事区域触发 2 秒平滑滞后释放；
* `M7B06_StaleSessionCannotRelease`: 跨会话过期代次无法释放租约；
* `M7B07_ReconnectInvalidationResync`: 客机断线重连精准分发增量版本；
* `M7B08_DisconnectCleansObserverState`: 离线精准清理观察者订阅。

### 2. Structure 领域单测（M7S01~M7S08）
* `M7S01_FirstAcquireCreatesGeneration`: 首次激活 2D 建筑网格生成初始代次 1；
* `M7S02_PlaceStructureAdvancesGeneration`: 放置建筑地板/墙壁驱动该区域代次递增；
* `M7S03_DamageStructureAdvancesGeneration`: 破坏/扣血驱动代次递增；
* `M7S04_SalvageStructureAdvancesGeneration`: 回收拆除建筑驱动代次递增；
* `M7S05_ReleaseHysteresisDeadline`: 离开建筑区域触发 2 秒平滑滞后释放；
* `M7S06_StaleSessionCannotRelease`: 跨会话过期代次无法释放租约；
* `M7S07_ReconnectInvalidationResync`: 客机断线重连精准分发增量版本；
* `M7S08_DisconnectCleansObserverState`: 离线精准清理观察者订阅。

## 验收目标
* 纯内存单测套件从 165 项扩充至 **181/181 PASS**；
* 版本号升级为 **`0.2.4.8` (MultiObserver-M7)**；
* 双端构建无警告、无错误。
