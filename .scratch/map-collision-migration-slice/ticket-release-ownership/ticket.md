# Release 的含义是关闭需求，还是主动破坏原生可见性

- **Label**: `wayfinder:done`
- **Type**: HITL
- **Parent**: `map-collision-migration-slice`
- **Status**: Closed
- **Owner**: YU80Rice
- **Closed**: 2026-09-18
- **Blocked by**:

---

## 问题

确认的 Collision Release 可以撤销什么？它是机械反向调用 Acquire，还是撤销当前有效租约型 Override？

## 决议

本票选择「Release 只撤销本插件曾经 Acquire、当前仍持有且能够证明所有权的 Collision Override」。Acquire 成功必须产生与 Domain、Region、Session Epoch、Region Generation 和 Acquire Generation 绑定的 Acquisition Receipt；该 receipt 精确记录本次 Acquire 实际取得的插件侧 LevelObject、门动画、Collider 或 culling 覆盖，以及可安全撤销的方式。

确认的 `N → 0`、Hysteresis 到期和身份有效只是发起 Release 的前置条件，不自动授予整区 destructive disable 权限。Execution Port 仅可撤销当前有效 receipt 中仍由本插件持有的状态维度，不得无条件执行 Acquire 的反操作、恢复整区历史快照，或覆盖 Acquire 后由 vanilla 和合法游戏事件产生的状态变化。

Collision Execution Port 的操作集合不得包含 ResourceSpawnpoint；Resource 状态不属于 Collision receipt。Host 或其它 observer 仍贡献需求时，编排层不得形成最终 Release；Execution Port 不重新扫描观察者，而是在收到与当前 receipt 或身份不一致的命令时拒绝提交并报告不变量违反。

无法证明所有权、样本未知、身份恢复中或 receipt 已陈旧时，Release 采用非破坏性安全状态：不 disable、不调用旧轮询、不触发跨域 EndSession，并进入有界、可观测的 reconciliation 或 quarantine。正常生命周期仍必须支持安全撤销；「永远只 enable」不是终态策略。

### 所有权证明

有效 Acquisition Receipt 绑定 `DomainId + RegionKey + SessionEpoch + RegionGeneration + AcquireGeneration`，并列出 `AcquiredOperations / Overrides`。新 Acquire 使旧 receipt 失效。Capability 若日后拆分，可再加 CapabilityId；本票不要求公开拆分 Capability Lease，但执行层必须按实际变更粒度记录所有权。

撤销优先顺序：原生可撤销 override/token；否则撤销 override 后触发 vanilla 安全重评估；否则仅对仍与本插件写入一致的维度 compare-and-validate；无法判断则非破坏性 no-op 并进入待协调状态。禁止无条件整区 disable、无条件恢复 Acquire 前整区 snapshot、因不确定而重跑旧轮询。

门的 gameplay / 开关 / 锁状态不属于 Collision Release。Renderer、Collider、animation culling 若生命周期不同，必须分别记入 receipt。聚合 native API 若无法证明不会覆盖非本域状态，不得作为 destructive Release API。

### 失败时的 fail-safe

「只 enable 不 disable」仅用于样本未知、身份恢复中、receipt 缺失或不匹配、原生状态无法确认仍由插件持有、补偿失败、vanilla tracker 正在同区转换。此时不 destructive disable、记录带身份的原因、有界 reconciliation、超时 quarantine；不无限重试、不永久静默、不回退旧 Writer。

### 证据

PureMemory：Acquire 产生唯一 receipt；新 Acquire 使旧 receipt 失效；陈旧 epoch/generation 不能 Release；hysteresis 重入取消 pending；重复 Release 幂等；无 receipt 不 destructive；失败不影响其它 region/domain；receipt 不含 Resource。StaticIL：Collision Release 不写 ResourceSpawnpoint、不扫 `Provider.clients`、不回 `RemoteCoverage`；destructive native 只从 Execution Port 进入；提交边界校验 epoch/generation/receipt。Runtime：Host-only 不错 Release；Guest 离开且无其它需求时撤销 override；滞回重入无抖动；双 Guest 一人离开不 Release；门在 Acquire 后的合法变化不被回滚；陈旧 receipt 被拒；失败非破坏且不触发旧轮询；Session Reset 后旧 pending 无资格；Collision Release 不改变 ResourceSpawnpoint；日志证明撤销的是具体 receipt。

### 本票不决定

每个 native 对象的具体 API；公开 Capability Lease 数量；reconciliation 的秒数与类名。

## 资产

- glossary：Collision Override、Acquisition Receipt
- ADR：[0014 租约型 Collision Override 与 Acquisition Receipt](../../../docs/adr/0014-lease-scoped-collision-override-and-acquisition-receipt.md)
