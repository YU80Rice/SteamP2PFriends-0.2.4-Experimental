# 0010: 共享投影引擎与声明式 Demand Policy

空间需求复用不共享一份跨域需求掩码，也不让每个领域自己实现投影器。Control Plane 维护唯一的 World Presence Observer 空间事实，并统一执行区域枚举、边界裁剪、需求计数和 Enter/Exit diff。各领域只声明 eligibility、空间形状、半径来源和 DomainId。不存在共享默认半径。Collision 必须包含 Host；Guest eligibility 沿用既有 World Presence Observer，不在 Collision patch 内改写。

## Considered Options

- 所有领域共享一套需求掩码和半径：否决。今天 `RESOURCE_REGIONS` 与 `OBJECT_REGIONS` 碰巧同为 3，不能证明语义相同。
- 共享观察者事实后由各领域自己算 `RegionKey` 集合：否决。会把私有轮询升级成各域私有投影器，Item/Zombie 仍会复制枚举、diff 和计数。

## Consequences

- 领域可维护独立的 demand projection 结果，但不得复制 Observer Spatial Authority。
- Collision 以 `OBJECT_REGIONS` 为迁移基线，须用证据验证适用范围，不得把该常量升为共享默认值。
- Production Control Seam 的类形状仍由编排契约票决定；本 ADR 只锁「不能每域复制观察者索引状态」。
