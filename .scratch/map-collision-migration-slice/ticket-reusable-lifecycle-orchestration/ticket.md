# Collision 应复用什么级别的 Production Control Seam

- **Label**: `wayfinder:done`
- **Type**: HITL
- **Parent**: `map-collision-migration-slice`
- **Status**: Closed
- **Owner**: YU80Rice
- **Closed**: 2026-09-18
- **Blocked by**:

---

## 问题

Collision 作为第二个生产领域，应接入什么级别的生命周期编排？共享层是只有接口，还是拥有可执行的状态转换？

## 决议

本票选择「共享生命周期编排引擎，领域通过窄端口声明政策并执行原生操作」。共享层不是只有接口，而是拥有可复用的需求聚合、Acquire/Release 转换、Hysteresis、身份校验、retry、补偿调度、局部故障隔离和统一诊断实现。Resource 与 Collision 不得各自复制这套状态机。

共享引擎消费投影引擎产生的 typed Domain Demand，并消费 Session Epoch、Connection Generation 和 Region Generation 等既有身份，不重新生成或合并身份轴。故障默认隔离到 `DomainId + RegionKey + Transition`；观察者贡献重试与聚合区域转换重试必须使用符合各自事务粒度的身份，禁止区域单槽互吞。

领域端口只负责 Lifecycle Policy 与本领域原生操作。领域可以通过快照、可逆操作、补偿句柄、幂等执行或非破坏性失败实现恢复，不强制复制 Resource 的整区 snapshot 模式。Domain Executor 不得扫描观察者、计算空间需求或持有第二套生命周期编排状态。

Resource 以行为保持方式迁入共享引擎，保留其已经 Runtime 验收的领域语义；目标状态下可以保留薄的 Resource seam 门面，但不得长期保留独立的通用生命周期状态机。Collision 成为第二个真实消费者，用于证明该共享引擎具有跨领域复用能力。新增领域不得要求在共享引擎中增加领域 switch-case。

### 调用链

Canonical World Presence Observers → Shared Demand Projection Engine → typed Domain Demand → Reusable Lifecycle Orchestration Engine → Acquire/Release command → Domain Execution Port → Unturned Native State / Protocol Executor。

Lifecycle Engine 不重新投影空间。Projection Engine 不调用 enable/disable。Coordinator 不按 DomainId 编写领域业务分支。

### 强制推论

- Collision 消费 typed Collision Demand，不建立会独立漂移的观察者索引、玩家扫描器或 `RemoteCoverage`。
- retry identity 与失败事务粒度一致：观察者贡献事务区分 observer 与 connection generation；聚合区域转换区分 domain、region、desired transition 和 generation。
- 故障默认隔离单元至少是 `DomainId + RegionKey + Transition`；仅会话身份整体失效等全局不变量破坏才升级为 session 级熔断。Capability 拆分若发生，隔离键可再加 CapabilityId；本票不要求拆。
- Resource 迁入纪律：先用 characterization / PureMemory 固定当前行为，经兼容端口接入，StaticIL 证明不再持有第二套编排状态机，重跑 Resource 回归门，再让 Collision 成为第二域。Collision Runtime PASS 前，不宣称共享引擎已被两个领域证明。不得同时重写 Resource 原生执行与提炼引擎，不得为泛型改变已验收的半径、滞回和 retry 语义，不得让 Collision 先复制一份再承诺以后合并。
- 领域执行端口必须声明事务与恢复能力；共享引擎调用协议，不假设所有领域都能整区 snapshot。
- 接入第三域只注册 Demand Policy、Lifecycle Policy 和 Domain Execution Port；Registration Closure 后本次会话的领域集合不可变。

### 本票不决定

旧 Writer 删除顺序；Collision Release 是否可主动 disable 原生状态；门动画与静态 Collider 是否拆 Capability；哪些 coordinator 债构成接线阻塞；Runtime 场景矩阵；共享引擎类名/泛型/文件布局；是否继续使用 `MultiObserverShadowCoordinator` 这个名字；Collision 补偿采用 snapshot 还是可逆操作；retry 的具体时间参数。

## 资产

- glossary：Lifecycle Orchestration Engine、Domain Execution Port、Lifecycle Policy
- ADR：[0011 共享生命周期编排引擎与领域端口](../../../docs/adr/0011-shared-lifecycle-orchestration-and-domain-execution-port.md)
