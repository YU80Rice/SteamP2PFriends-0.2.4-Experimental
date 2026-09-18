# 哪些控制面不变量构成 Collision 准入门

- **Label**: `wayfinder:done`
- **Type**: HITL
- **Parent**: `map-collision-migration-slice`
- **Status**: Closed
- **Owner**: YU80Rice
- **Closed**: 2026-09-18
- **Blocked by**:

---

## 问题

哪些控制面不变量是 Collision 正式切换的强制准入门？准入前能否运行影子链路？

## 决议

本票决定：Collision 正式切换采用完整的 Control-Plane Readiness Gate。凡违反已锁定共享观察者事实、共享投影引擎、共享生命周期编排、单一 Authority Writer 和身份轴隔离的不变量，均为正式接线的阻塞前置，并随 Resource 迁入共享引擎一并闭合；不另建「重写整个 coordinator」的 wayfinder 地图。

正式切换前必须证明：单条不可用观察者记录不会冻结其它观察者、区域或领域，也不会被误判为离开并产生 destructive Release；单区域或单领域失败不会清除其它区域或领域状态；Zombie 或旁路异常不会通过共享外层异常处理触发跨域 `EndSession`；Session 身份缺失后能够有限恢复或显式熔断，旧 epoch 不具备写入资格；retry identity 与观察者贡献或聚合生命周期转换的实际事务粒度一致，不再发生区域单槽互吞；Collision Execution Port 在最终提交边界拒绝陈旧 epoch/generation；持续异常、恢复和熔断均有有界但不永久静默的诊断。

准入门尚未闭合时允许运行无副作用 Shadow Computation，以发现投影、身份、retry 和故障传播问题；但影子链路不得写原生状态、触发 destructive Release、影响生产领域或作为正式切换证据。正式候选构建必须满足全部准入条件，且不得在失败时回退旧 Writer。

Resource M6P31 和 Ticket 11/12 的成功是已有基础证据，但不等于本准入门已通过；已经被容忍的区域单槽 retry 互吞仍是正式切换阻塞项。

### 准入不变量

- **样本捕获隔离**：有效样本继续推进；无效样本对应的旧需求进入未知/暂缓，不得视为零需求。只有确认离开、generation 失效、Session Reset，或经过有界恢复策略后，才能清理其贡献。
- **领域和区域故障隔离**：默认影响 `DomainId + RegionKey + Transition + SessionEpoch + RegionGeneration`。即使仍从同一插件 Update 入口调用，也必须有独立故障边界；外层共享 catch 不得清空其它领域。`EndSession` 只由已确认的会话终止或不可恢复的 session 级身份失效触发。
- **Session 身份**：有限重试或重建；旧 epoch 的 pending/retry/补偿不得提交；显式 recovering / quarantined / ready；超过恢复条件后熔断；身份不确定时阻止新写入和 destructive Release；恢复、重试、熔断均有因果日志。
- **Retry 身份**：观察者贡献事务与聚合生命周期转换事务分开；同区多观察者、跨域、Acquire 与 Release、旧 generation 均不得互吞。retry exhaustion 只熔断对应事务。
- **Collision Execution Port**：最终提交边界复核 Session Epoch、Region Generation、必要时 Connection Generation、Authority Writer / Registration Closure、Desired Transition 是否仍有效。
- **可观测性**：首次详细记录，持续异常用有界 heartbeat 汇总，恢复必须闭环。不得只留一条去重日志后整局失明。
- **旧路径**：沿用 ADR-0012；准入失败不得切换；正式切换后不得编译可切换的旧 Writer 应急路径。

### 影子期

可计算 typed Collision Demand、模拟 Acquire/Hysteresis/retry、记录差异、暴露缺陷。不可写原生状态、不可 destructive Release、不可因影子异常对生产领域 EndSession、不可把影子 DLL 当正式 Runtime PASS。遇到不可用样本只记「无法判定」，不转成生产 Release。

### 证据

准入 Runtime 必须来自正式切换候选 DLL。影子候选只用于发现和修复。证据矩阵：单坏观察者不冻结其它观察者；样本未知不产生错误 Release；单区域/单领域失败不影响其它；Zombie/旁路异常不跨域 EndSession；session identity 可恢复或熔断；旧 epoch 无写入资格；retry 不互吞；Collision 提交边界复核身份；持续异常有 heartbeat；失败不回退旧 Writer。

### 本票不决定

先独立落共享引擎还是同一批先迁 Resource 再接 Collision；CaptureSamples 的过滤/缓存/quarantine 算法；session identity 从哪个事件重建；heartbeat 秒数；retry key 的 C# 类型；coordinator 是否改名；提交切片。

## 资产

- glossary：Control-Plane Readiness Gate 收紧；新增 Deferred Observer Demand
- ADR：[0013 Collision 正式切换准入门与影子证据边界](../../../docs/adr/0013-collision-readiness-gate-and-shadow-evidence-boundary.md)
