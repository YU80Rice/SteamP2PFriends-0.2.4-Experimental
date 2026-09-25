# 06: Collision Execution Port 与 Acquisition Receipt

**What to build:** Collision 经领域端口接入共享生命周期引擎，Acquire 成功产生 Acquisition Receipt，Release 只撤销当前有效 receipt 上仍由插件持有的 Collision Override。本票即使编译进 DLL，也不得成为生产 Authority Writer。

**Blocked by:** 03 Resource 经领域端口迁入共享生命周期编排引擎；05 Collision 声明式 Demand Policy 与只读影子验证

**Status:** implemented-pending-runtime（静态/纯内存闭环：`audit/2026-09-26/Implementation-0.2.4.9-CollisionTicket06-2350.md`；404/404 PASS、双次 Release Rebuild 身份一致、四门禁 PASS、五轮双轴最终双 CLEAN；正式切换 Runtime 归票 09）

- [x] Collision 作为 Domain Execution Port 接入共享编排引擎，消费 typed Collision Demand。
- [x] Acquire / Release 命令完成身份校验；执行端口不扫描观察者、不重算需求。
- [x] 成功 Acquire 产生绑定 Domain、Region、Session Epoch、Region Generation、Acquire Generation 的 Receipt，并列出实际取得的 Override。
- [x] Release 只撤销 receipt 所代表且仍由插件拥有的 Collision Override，不是整区 disable，也不是 enable 的机械反操作。
- [x] 无 receipt、陈旧代次或身份不确定时不执行 destructive disable。
- [x] Collision 操作集合不含可采集树的 enable/disable；Resource 状态不进入 Collision receipt。
- [x] StaticIL 与注册契约证明本票路径仍不具备生产写入资格。
- [x] PureMemory、StaticIL、BuildArtifact 通过。
