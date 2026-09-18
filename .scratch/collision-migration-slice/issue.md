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

当前前沿：04（与 05 并行）。阻塞图：`01 → 02 → 03 → 04` 与 `02 → 05` 并行，`03+05 → 06`，`04+05+06 → 07 → 08 → 09`。票 01 已关单（`audit/2026-09-18/Implementation-0.2.4.9-CollisionTicket01-1235.md`）；票 02 静态闭环、状态 `implemented-pending-runtime`（`audit/2026-09-18/Implementation-0.2.4.9-CollisionTicket02-1418.md`）；票 03 静态闭环、状态 `implemented-pending-runtime`（`audit/2026-09-18/Implementation-0.2.4.9-CollisionTicket03-1908.md`）。两张票的 Runtime 验收均归票 09。

## 票 03 → 票 04 的具名交接（截获点，不在票 03 内修）

票 03 以**行为保持**方式把资源生产接缝迁入共享编排引擎，因此以下三项仍是票 04 的准入阻塞项，且都已在引擎里预留了收口位置：

- **retry 仍按「区域单槽 + 拥有者观察者」登记**：`spec.md` 要求观察者贡献事务区分领域、区域、观察者、连接代次、会话，禁止区域单槽互吞。票 01 表征门 M6P38/M6P39/M6P40 锁定的正是当前语义，票 03 未改（`M6P40` 注释已列明该边界不属已验收语义）。登记已收进引擎的每域状态（`DomainLifecycleState.AcquireRetries`），票 04 改键为事务粒度即可，不需要再动引擎结构。
- **样本捕获层 `capture-incomplete` 整体冻结**与「无效样本未进入 Deferred Observer Demand」仍归票 04 第 1–2 项；票 03 未触碰 `MultiObserverShadowCoordinator.CaptureSamples`（`git diff` 可证）。
- **会话身份的有界恢复/显式熔断**仍归票 04 第 4 项：票 03 保留既有失败闭合语义（会话收尾失败 → `repair-required` + 拒绝新写入），未新增有界恢复策略。

## 票 02 → 票 04 的具名交接（截获点，不在票 02 内修）

票 02 复审期间暴露的**样本捕获**不变量仍归票 04（其票面 checklist 第 1–2 项，且 `Blocked by: 03`）：

- `MultiObserverShadowCoordinator.CaptureSamples` 仍对单条不完整记录整体冻结本拍（`capture-incomplete` → 跳过全部 reconcile）。规格 `spec.md:61` 要求「单条不可用观察者记录不得冻结其它有效观察者的本拍更新」，该修复属票 04 第 1 项。
- 无效样本对应的旧需求目前不是 Deferred Observer Demand；票 02 已在共享引擎实现其**原语**（资格不合格＝暂缓而非释放，见 `DemandProjectionEngine.DomainDemandProjectionState.Apply`），票 04 第 2 项只需在其上接入样本不可用路径。
- 票 02 未修改上述捕获路径（`git diff` 可证），故不构成票 02 的行为变更。

## 测试接缝

唯一对外行为接缝是 Lifecycle Orchestration Engine。Resource 与 Collision 都是该接缝上的 Domain Execution Port。先例是 Resource 生产接缝的 PureMemory 契约。不在 Collision patch、私有覆盖集合或 coordinator 领域分支上建立第二套行为接缝。StaticIL、BuildArtifact、Runtime 是同一功能的证据类，不是第二接缝。
