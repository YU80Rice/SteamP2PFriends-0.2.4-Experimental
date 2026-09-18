# 02: Resource 经声明式 Demand Policy 接入共享投影引擎

**What to build:** 资源需求由共享投影引擎按声明式政策从唯一观察者事实算出 typed Resource Demand；资源接缝不再持有会独立漂移的观察者索引。资源仍是唯一生产写入者。

**Blocked by:** 01 建立 0.2.4.9 迁移基线并固定 Resource 表征行为

**Status:** implemented-pending-runtime（静态闭环 `audit/2026-09-18/Implementation-0.2.4.9-CollisionTicket02-1418.md`：Control Plane 唯一空间事实 + 共享 Demand Projection Engine 落地，Resource 接缝改注入引擎并按声明式政策消费 typed demand、不再持有私有观察者索引；资格不合格按 Deferred Observer Demand 暂缓而非释放；生产 Writer 仍唯一；313/313 PASS、三门禁 PASS、双轴最终双 CLEAN；Runtime 归票 09，本票不宣称投影迁移 Runtime 完成）

- [x] Control Plane 维护唯一的 World Presence Observer 空间事实（`ObserverSpatialAuthority`，由协调器接线点建立并注入）。
- [x] Demand Projection Engine 负责区域枚举、世界边界裁剪、去重、计数及进入/退出差异（`DemandProjectionEngine` + 每域 Domain Demand Projection State）。
- [x] Resource 只声明资格、资源区域半径、切比雪夫政策与 Domain Id（`ResourceDemandPolicy.Create`）。
- [x] 输出 typed Resource Demand；需求身份至少是 Domain Id + Region Key（`DomainDemand` / `DomainDemandProjection`）。
- [x] Resource 不再持有独立漂移的观察者索引（接缝零 `SpatialObserverIndex` 字段与构造，StaticIL 门禁锁定；扰动实验实测 FAIL）。
- [x] Resource 仍是唯一生产 Writer（租约/复制/生命周期调用图未变，既有 StaticIL 契约保持绿）。
- [x] 01 的表征行为保持（M6P36–M6P41、SPI05、半径来源契约全部保持绿；半径扰动实测 29 项 FAIL 证明表征门仍有效）。
- [x] PureMemory 红先再绿；StaticIL 与 BuildArtifact 通过（红链见审计 §3）。
- [x] 本票完成后标记 implemented-pending-runtime，不单独宣称投影迁移 Runtime 完成。
