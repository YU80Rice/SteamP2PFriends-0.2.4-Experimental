---
title: "Collision Migration Slice：共享控制面第二域与旧 RemoteCoverage 退役"
status: "ready-for-agent"
labels:
  - "ready-for-agent"
created_at: "2026-09-18T22:00:00+08:00"
updated_at: "2026-09-18T22:00:00+08:00"
version: "0.2.4.9"
---

# Collision Migration Slice：共享控制面第二域与旧 RemoteCoverage 退役

## 概述

把 Collision 作为 Resource 之后的第二个真实 Migration Slice：共享观察者事实、共享投影引擎、共享生命周期编排引擎；Collision 只经 Domain Execution Port 执行租约型 Collision Override。正式切换后旧 `RemoteCoverage` Writer 退出生产调用图。本阶段插件版本授予 `0.2.4.9`。

## 关联规范

- 规格：[spec.md](./spec.md)
- 决策地图：[Collision Migration Slice 权威边界与共享控制面准入](../map-collision-migration-slice/map.md)（已关闭，ADR 0009–0014）
- 模板域：结构基线 Resource Migration Sample（Ticket 10/11/12）
- 分线：Listen-Host Dedicated Gate、Join Routing、09-17 口述 bug、Route B 均不在本规格
- 实施票：
  - [01 建立 0.2.4.9 迁移基线并固定 Resource 表征行为](./issues/01-version-and-resource-characterization.md)
  - [02 Resource 经声明式 Demand Policy 接入共享投影引擎](./issues/02-shared-demand-projection-for-resource.md)
  - [03 Resource 经领域端口迁入共享生命周期编排引擎](./issues/03-shared-lifecycle-orchestration-for-resource.md)
  - [04 修复共享控制面的正式切换准入阻塞项](./issues/04-control-plane-readiness-invariants.md)
  - [05 Collision 声明式 Demand Policy 与只读影子验证](./issues/05-collision-demand-policy-and-readonly-shadow.md)
  - [06 Collision Execution Port 与 Acquisition Receipt](./issues/06-collision-execution-port-and-acquisition-receipt.md)
  - [07 闭合 Collision 正式切换准入证据](./issues/07-cutover-readiness-go-nogo.md)
  - [08 会话边界原子切换并退役旧 Collision Writer](./issues/08-session-boundary-cutover-and-legacy-writer-retirement.md)
  - [09 共享 1 Host + 2 Guest Runtime 验收与 Collision Slice 关单](./issues/09-shared-1h2g-runtime-acceptance.md)

当前前沿：04 静态闭环（`implemented-pending-runtime`），票 05 可并行认领。阻塞图：`01 → 02 → 03 → 04` 与 `02 → 05` 并行，`03+05 → 06`，`04+05+06 → 07 → 08 → 09`。票 01 已关单（`audit/2026-09-18/Implementation-0.2.4.9-CollisionTicket01-1235.md`）；票 02 静态闭环、状态 `implemented-pending-runtime`（`audit/2026-09-18/Implementation-0.2.4.9-CollisionTicket02-1418.md`）；票 03 静态闭环、状态 `implemented-pending-runtime`（`audit/2026-09-18/Implementation-0.2.4.9-CollisionTicket03-1908.md`）；票 04 静态闭环、状态 `implemented-pending-runtime`（`audit/2026-09-18/Implementation-0.2.4.9-CollisionTicket04-2330.md`）。三张票的 Runtime 验收均归票 09。

## 票 03/02 → 票 04 的具名交接（已在本票收口）

- **retry 登记已改为事务粒度**：键从「区域单槽 + 拥有者观察者」改为「区域 + 观察者 + 连接代次」，区域单槽互吞退出（M6P40 注释具名的边界已消除）；陈旧连接代次的登记不再具备写入资格。
- **样本捕获层 `capture-incomplete` 整体冻结已解除**：改为逐条准入（`ObserverSampleAdmission`），单条不可用记录只暂缓它自己的观察者，整批不可判定才整批暂缓；无效样本进入 Deferred Observer Demand，不触发破坏性释放。
- **会话身份已具备有界恢复与显式熔断**：`SessionIdentityGate` 有限窗口 + 有界心跳 + 熔断；身份不确定时阻断写入与破坏性释放但保留全部领域状态，恢复后要求重建会话。

## 票 04 → 票 05/06/07/08 的具名移交

- Collision Demand Policy 与只读影子（票 05）、Execution Port 与 Acquisition Receipt（票 06）接在已加固的共享引擎上；本票未新增任何 Collision 执行类型、未切换 Authority Writer。
- 票 07 的准入 Go/No-Go 需消费本票结论：准入门静态不变量已落地，但 Runtime 证据仍归票 09。

## 测试接缝

唯一对外行为接缝是 Lifecycle Orchestration Engine。Resource 与 Collision 都是该接缝上的 Domain Execution Port。先例是 Resource 生产接缝的 PureMemory 契约。不在 Collision patch、私有覆盖集合或 coordinator 领域分支上建立第二套行为接缝。StaticIL、BuildArtifact、Runtime 是同一功能的证据类，不是第二接缝。
