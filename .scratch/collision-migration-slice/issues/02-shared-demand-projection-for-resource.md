# 02: Resource 经声明式 Demand Policy 接入共享投影引擎

**What to build:** 资源需求由共享投影引擎按声明式政策从唯一观察者事实算出 typed Resource Demand；资源接缝不再持有会独立漂移的观察者索引。资源仍是唯一生产写入者。

**Blocked by:** 01 建立 0.2.4.9 迁移基线并固定 Resource 表征行为

**Status:** ready-for-agent

- [ ] Control Plane 维护唯一的 World Presence Observer 空间事实。
- [ ] Demand Projection Engine 负责区域枚举、世界边界裁剪、去重、计数及进入/退出差异。
- [ ] Resource 只声明资格、资源区域半径、切比雪夫政策与 Domain Id。
- [ ] 输出 typed Resource Demand；需求身份至少是 Domain Id + Region Key。
- [ ] Resource 不再持有独立漂移的观察者索引。
- [ ] Resource 仍是唯一生产 Writer。
- [ ] 01 的表征行为保持。
- [ ] PureMemory 红先再绿；StaticIL 与 BuildArtifact 通过。
- [ ] 本票完成后标记 implemented-pending-runtime，不单独宣称投影迁移 Runtime 完成。
