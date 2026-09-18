# 03: Resource 经领域端口迁入共享生命周期编排引擎

**What to build:** 资源经窄执行端口迁入共享生命周期编排引擎；滞回、retry、身份校验和局部故障隔离由共享引擎拥有。资源已验收语义保持。Collision 仍不写生产。

**Blocked by:** 02 Resource 经声明式 Demand Policy 接入共享投影引擎

**Status:** ready-for-agent

- [ ] Lifecycle Orchestration Engine 消费 typed Resource Demand。
- [ ] 共享引擎拥有需求聚合、0→1 Acquire、N→0 Release、Hysteresis、身份校验、retry、补偿调度和诊断。
- [ ] Resource 通过 Domain Execution Port 操作原生状态，不扫描观察者、不重算需求、不持有第二套编排状态机。
- [ ] 可保留薄的资源门面，但原有通用生命周期状态机退出生产权威。
- [ ] 引擎消费 Session Epoch、Connection Generation、Region Generation，不重新生成或合并这些轴。
- [ ] 01 的表征行为保持；不得改变已验收的半径、滞回和 retry 语义。
- [ ] Collision 仍不具备生产写入资格。
- [ ] PureMemory、StaticIL、BuildArtifact 通过；完成后 implemented-pending-runtime。
