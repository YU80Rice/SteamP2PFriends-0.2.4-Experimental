# 03: Domain Id、Region Key 与 Bound Key 规范化

**What to build:** 让跨模块注册、空间同步和 Zombie 导航都使用稳定且类型明确的身份，不再依赖显示名称或裸整数。

**Blocked by:** 02 / Patch Registration Orchestrator 拆分

**Status:** completed

- [x] Domain Id 与显示名称分离，机器身份不可变且跨模块一致。
- [x] 二维世界区域统一使用 Region Key，编码、解码和边界规则集中管理。
- [x] Zombie 导航区域使用独立 Bound Key，不与 Region Key 混用。
- [x] Session、Connection、Region 和 Entity generation 的语义保持正交。
- [x] PureMemory 与 StaticIL 证据覆盖身份契约，现有功能行为不变。

完成证据：`Core/Identity` 值对象、`LeaseTicket` 生命周期轴、Zombie/Animal Bound 接缝、`IdentityContractTests` 与 `IdentityStaticILContractTests`；主项目/测试项目 Release 构建 0 errors / 0 warnings，全量测试 `183/183 PASS`。Runtime 仍 Pending，Resource Production Control Seam 未迁移。

## 验收关单(2026-09-11,用户批量批准)

- Runtime 确认:RegionKey/generation 轴(Session/Connection/Region)贯穿租约获取、2 秒滞回、重入、重连与快照复制全因果链,三端 0 fault、0 会话重建(`audit/2026-09-11/Runtime-0.2.4.8-Ticket12-0031.md`;前序 `audit/2026-09-07/Runtime-0.2.4.8-Ticket11-2343.md`)。
- 边界:经 Resource 链验收(Ticket 11/12)**间接确认**;Zombie Bound Key 的专项三端矩阵未执行。
- 状态:ready-for-human → completed(用户 2026-09-11 验收)。
