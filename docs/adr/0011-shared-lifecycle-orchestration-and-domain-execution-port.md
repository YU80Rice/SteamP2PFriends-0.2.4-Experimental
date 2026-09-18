# 0011: 共享生命周期编排引擎与领域端口

Collision 作为第二个生产领域，不复制 `ResourceProductionControlSeam`，也不把领域分支写入 coordinator。共享层是可执行的生命周期编排引擎：需求聚合、Acquire/Release、Hysteresis、身份校验、retry、补偿调度和局部故障隔离。领域通过窄端口声明 Lifecycle Policy 并执行原生操作。引擎消费既有身份轴，不重新生成或合并它们。故障默认隔离到 DomainId、RegionKey 与 Transition。Resource 以行为保持方式迁入；目标态可保留薄门面，不得长期保留第二套通用生命周期状态机。

## Considered Options

- 复制 Resource seam 为 Collision seam：否决。会复制需求聚合、retry、hysteresis 和诊断，Item/Zombie 还会再复制。
- 把领域分支写入 `MultiObserverShadowCoordinator`：否决。形成 `switch (DomainId)` 巨型协调器。
- Adapter 直接接收 Enter/Exit、不设接缝：否决。缺少聚合、事务、retry 和 Writer 切换边界。
- 只提炼接口、由各域实现状态机：否决。重复造轮子问题仍在。

## Consequences

- 领域不得扫描观察者、重算 demand 或持有第二套编排状态。
- 引擎不得知道 ResourceSpawnpoint、LevelObject 或门类型。
- 不强制所有领域整区 snapshot。
- 共享引擎类名、泛型和 Collision 补偿策略留给后续 spec。
