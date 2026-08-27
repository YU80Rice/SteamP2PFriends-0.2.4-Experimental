# 10: Resource Production Control Seam 行为迁移

**What to build:** 让 Resource 首个真正接入 `World Presence Observer → SpatialObserverIndex → Domain Adapter Pipeline` 的生产控制接缝，统一驱动区域租约、碰撞、采伐和状态复制。

**Blocked by:** 02 / Patch Registration Orchestrator 拆分；03 / Domain Id、Region Key 与 Bound Key 规范化；05 / Resource 与 Collision 结构迁移；08 / Evidence Class 测试结构与门禁；09 / Build Fingerprint 与独立产物关联

**Status:** implemented-pending-runtime

- [x] Resource 只有一个生产 Authority Writer，旧路径不得与新接缝并行写入同一状态。
- [x] Host、授权 Guest 和多个观察者的区域需求并集能够驱动 Resource Region Lease。
- [x] 覆盖 Acquire、2 秒 Hysteresis Release、重新进入、Connection Generation、Region Generation 和 Session Reset。
- [x] 远端 Resource 碰撞、采伐、区域重建、快照与增量复制行为可在高层接缝验证。
- [x] PureMemory、StaticIL、BuildArtifact 和必要 Runtime 证据状态完整记录，Release 构建与独立审核通过。

Evidence 状态：PureMemory PASS、StaticIL PASS、BuildArtifact PASS；Runtime（SP/listen-host/U3DS/P2P）PENDING。详细实现、构建、独立核验与审核证据归档于 `audit/2026-08-27/Implementation-0.2.4.8-Ticket10.md`。
