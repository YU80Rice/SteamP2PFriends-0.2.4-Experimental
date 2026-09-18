# 03: Resource 经领域端口迁入共享生命周期编排引擎

**What to build:** 资源经窄执行端口迁入共享生命周期编排引擎；滞回、retry、身份校验和局部故障隔离由共享引擎拥有。资源已验收语义保持。Collision 仍不写生产。

**Blocked by:** 02 Resource 经声明式 Demand Policy 接入共享投影引擎

**Status:** implemented-pending-runtime（静态闭环 `audit/2026-09-18/Implementation-0.2.4.9-CollisionTicket03-1908.md`：共享 Lifecycle Orchestration Engine + Domain Execution Port 落地，Resource 生产接缝退化为薄门面、原有通用生命周期状态机退出生产权威；335/335 PASS、Release 双 0/0（两次 Rebuild 指纹一致）、三门禁 PASS、扰动负控制 12 组逐项证伪、双轴 round 1–4 最终双 CLEAN；Runtime 归票 09，本票不宣称编排迁移 Runtime 完成）

- [x] Lifecycle Orchestration Engine 消费 typed Resource Demand（`LifecycleOrchestrationEngine.Observe` 调用共享投影引擎并消费 `DomainDemandProjection`；LOE01 锁定两个领域共用唯一空间事实、各自拿到自己领域的需求）。
- [x] 共享引擎拥有需求聚合、0→1 Acquire、N→0 Release、Hysteresis、身份校验、retry、补偿调度和诊断（LOE02/03/05/06/07/10/11；`ResourceProductionControlSeam` 内这些状态与逻辑全部移除，StaticIL 与扰动 P2/P8/P9 锁定）。
- [x] Resource 通过 Domain Execution Port 操作原生状态，不扫描观察者、不重算需求、不持有第二套编排状态机（`ResourceExecutionPort` 只转发原生操作与失败分类；接缝零泛型集合字段、零嵌套编排类型）。
- [x] 可保留薄的资源门面，但原有通用生命周期状态机退出生产权威（接缝唯一持有的编排对象是共享引擎；门面只做构造、注册与转发）。
- [x] 引擎消费 Session Epoch、Connection Generation、Region Generation，不重新生成或合并这些轴（`IDomainExecutionPort.ReadRegionGeneration` 是唯一读取面；LOE05 区域代次门、LOE06 会话代次门锁定）。
- [x] 01 的表征行为保持；不得改变已验收的半径、滞回和 retry 语义（M6P36–M6P41、SPI05、半径来源契约全部保持绿；扰动 P1 打掉滞回即 4 项 FAIL、P7 改节奏即 M6P39 FAIL、P8 去重试即 5 项表征门 FAIL）。
- [x] Collision 仍不具备生产写入资格（全程序集唯一领域注册点位于 Resource 接缝构造函数内；Lifecycle 命名空间零 Collision 引用；扰动 P6 证明该门有效）。
- [x] PureMemory、StaticIL、BuildArtifact 通过；完成后 implemented-pending-runtime（332/332 PASS、三门禁 PASS、产物独立核验 PASS）。
