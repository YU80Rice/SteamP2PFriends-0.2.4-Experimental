# 空间需求应如何复用才能避免每个领域再造一套覆盖半径

- **Label**: `wayfinder:done`
- **Type**: HITL
- **Parent**: `map-collision-migration-slice`
- **Status**: Closed
- **Owner**: YU80Rice
- **Closed**: 2026-09-18
- **Blocked by**:

---

## 问题

Collision、Resource 及后续领域需要不同的观察者资格、覆盖形状和半径，但不能各自复制观察者扫描、区域枚举、边界裁剪、需求计数和进入/退出差异状态。共享控制面应复用到哪一层？

## 决议

本票选择「共享观察者事实与投影执行引擎，领域声明 Demand Policy」。Control Plane 维护唯一的 World Presence Observer 空间事实及其连接、移动、断线和会话生命周期，并统一执行区域枚举、世界边界裁剪、需求计数与 Enter/Exit diff。Resource、Collision 及后续领域不得扫描 `Provider.clients`、保存第二份观察者位置真相或实现私有覆盖状态；各领域仅声明 observer eligibility、空间形状、领域半径来源及 `DomainId`，由共享投影引擎产生 typed Domain Demand。

不存在跨领域共享的默认半径或需求掩码。`RegionKey` 只表示区域身份，领域需求至少以 `DomainId + RegionKey` 区分。Resource 保持已验收的 `LevelGround.RESOURCE_REGIONS` Chebyshev policy；Collision 以 `LevelObjects.OBJECT_REGIONS` Chebyshev policy 作为行为保持的迁移基线，并通过后续证据验证其适用范围，不将该常量提升为共享默认值。

Collision 必须消费包含 Host 的 canonical World Presence Observer 样本，不得复刻旧 `RemoteCoverage` 的 remote-only 语义。Guest eligibility 沿用控制面的统一定义，不在 Collision patch 内自行判断授权状态。领域可以维护独立的 demand projection 结果，但不得复制 Observer Spatial Authority。

Collision policy 只生成 Collision demand，不得应用于 `ResourceSpawnpoint`；Resource 和 Collision 对同一 `RegionKey` 的需求彼此独立。Collision 内部是否进一步拆分门动画与静态 Collider Capability，留待后续决策。

### 强制推论

- 需求身份至少是 `DomainId + RegionKey`，不是「该 RegionKey 对所有领域共同活跃」。Capability 拆分若发生，才可能扩展为 `DomainId + CapabilityId + RegionKey`；本票不要求拆。
- Collision Migration Slice 以 `OBJECT_REGIONS` Chebyshev 为行为保持基线；若证据证明门动画或静态 Collider 具有不同空间语义，升级为后续 Capability Policy 决策，不得发明共享默认半径。
- Pending Guest 是否投影世界需求，沿用既有 World Presence Observer 定义，不在 Collision policy 中改写。
- 每个领域可以有独立的 Domain Demand Projection State，但不得拥有独立的 Observer Spatial Authority；不得每域各建一套会独立漂移的观察者索引。

## 资产

- glossary：Demand Policy 改为声明；新增 Demand Projection Engine、Domain Demand Projection State
- ADR：[0010 共享投影引擎与声明式 Demand Policy](../../../docs/adr/0010-shared-projection-engine-and-declarative-demand-policy.md)
