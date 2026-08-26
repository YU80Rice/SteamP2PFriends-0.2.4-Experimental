# 03: Domain Id、Region Key 与 Bound Key 规范化

**What to build:** 让跨模块注册、空间同步和 Zombie 导航都使用稳定且类型明确的身份，不再依赖显示名称或裸整数。

**Blocked by:** 02 / Patch Registration Orchestrator 拆分

**Status:** ready-for-human

- [x] Domain Id 与显示名称分离，机器身份不可变且跨模块一致。
- [x] 二维世界区域统一使用 Region Key，编码、解码和边界规则集中管理。
- [x] Zombie 导航区域使用独立 Bound Key，不与 Region Key 混用。
- [x] Session、Connection、Region 和 Entity generation 的语义保持正交。
- [x] PureMemory 与 StaticIL 证据覆盖身份契约，现有功能行为不变。

完成证据：`Core/Identity` 值对象、`LeaseTicket` 生命周期轴、Zombie/Animal Bound 接缝、`IdentityContractTests` 与 `IdentityStaticILContractTests`；主项目/测试项目 Release 构建 0 errors / 0 warnings，全量测试 `183/183 PASS`。Runtime 仍 Pending，Resource Production Control Seam 未迁移。
