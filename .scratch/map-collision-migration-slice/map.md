# Wayfinder Map: Collision Migration Slice 权威边界与共享控制面准入

- **Label**: `wayfinder:map`
- **Owner**: YU80Rice
- **Status**: Closed (Admitted to `/to-spec`)
- **Created**: 2026-09-18
- **Closed**: 2026-09-18

---

## 目的地

Collision Migration Slice 的决策与规格已锁定，可以交给 `/to-spec` → `/to-tickets`。到达终点时必须同时成立：

1. Observer Spatial Authority、Domain Demand Authority、Execution Authority、Native State Authority 已按域划清；跨域只共享 typed Domain Demand / Region Lease，不共享私有覆盖集。
2. 观察者事实由 Control Plane 统一提供；Demand Projection Engine 按声明式 Demand Policy 投影；领域不再扫描 `Provider.clients`。
3. 共享 Lifecycle Orchestration Engine 拥有 Acquire/Release、hysteresis、retry 与局部故障隔离；Collision 经 Domain Execution Port 成为第二个真实消费者，而不是 `ResourceProductionControlSeam` 的复制品。
4. 单一 Authority Writer：允许只读 Shadow Computation，禁止双写；会话边界原子切换；旧 `RemoteCoverage` Writer 与生产影子比较均退出生产调用图。
5. Collision Control-Plane Readiness Gate 已裁定正式切换阻塞前置；影子可先跑但非证据。
6. Release 只撤销当前有效 Acquisition Receipt 上的 Collision Override，不是整区 disable，也不是 Acquire 的机械反操作。

本图的架构任务不是修掉「树消失」或「门卡人」两个症状，而是：**用第二个真实迁移域证明 Resource 之后的共享控制面可复用，新增领域不必再复制观察者轮询、生命周期账本、retry、generation、hysteresis 和故障恢复。**

---

## 笔记

- **工作语言**：简体中文。
- **查阅技能**：grilling、domain-modeling；事实调查用 research。实施不在本图；下一窗口 `/to-spec` → `/to-tickets`，实施票再 `/implement`。
- **领域词汇**：`CONTEXT.md`。停止把 `RemoteCoverage` 叫成「Collision 覆盖权威」。
- **模板域**：Resource Migration Sample 已完成。Collision 是下一张 Migration Slice。
- **香草门控与 SPI 铺域分线**：Dedicated Gate / Join Routing（B 批）与本图分线，不并票。
- **历史纠偏**：旧票「场景物件、树木矿石资源与权限门碰撞统一接入 SPI」保持归档；其生产接线宣告已被否定。
- **ADR**：0009 权威边界；0010 投影引擎；0011 生命周期编排；0012 会话边界切换；0013 准入门；0014 Acquisition Receipt。

---

## 已有决策

- [Resource 与 Collision 结构迁移](../structure-baseline-0-2-4-8/issues/05-resource-collision-ownership.md) — 目录与命名空间已分域；Collision SPI 驱动明确未接线。
- Resource Migration Sample（结构基线 Ticket 10/11/12）— Production Control Seam 已接线，旧 Resource Writer 已退出，Runtime Gate 通过。
- Listen-Host Dedicated Gate 与 Join Routing（01–05）— 与本图分线。
- 用户 2026-09-18 锁定本图目的地粒度 — 只锁定 Collision Migration Slice。
- [Collision Migration Slice 的权威边界是什么](ticket-authority-boundary/ticket.md) — 共享空间事实、分域投影、分域执行、原生状态不复制；`RegionKey` 不授予执行资格；Collision 退出 `ResourceSpawnpoint` 写入。
- [空间需求应如何复用才能避免每个领域再造一套覆盖半径](ticket-spatial-demand-policy/ticket.md) — 共享观察者事实与投影执行引擎，领域只声明 Demand Policy；无共享默认半径；Collision 含 Host。
- [Collision 应复用什么级别的 Production Control Seam](ticket-reusable-lifecycle-orchestration/ticket.md) — 共享生命周期编排引擎与领域端口；Resource 行为保持迁入；故障隔离到 DomainId+RegionKey+Transition。
- [单一 Authority Writer 的切换和影子验证契约是什么](ticket-single-writer-cutover/ticket.md) — 双计算禁双写；会话边界原子切换；旧 Writer 与生产影子比较均须退役；回滚仅部署级。
- [哪些控制面债当前仍违反 Collision 接线所需的不变量](ticket-control-plane-debt-facts/ticket.md) — 事实调查 [findings.md](ticket-control-plane-debt-facts/findings.md)；不裁定准入门。
- [哪些控制面不变量构成 Collision 准入门](ticket-control-plane-readiness-gate/ticket.md) — 正式切换用完整准入门；影子可先跑但非证据；坏样本进暂缓不得误 Release；单槽互吞仍阻塞。
- [Release 的含义是关闭需求，还是主动破坏原生可见性](ticket-release-ownership/ticket.md) — Release 撤销当前有效 Acquisition Receipt 上的 Collision Override；不能证明所有权则非破坏性 fail-safe。

---

## 前沿索引

无打开的 HITL 或 AFK 子票。规格与九张实施票已写入 [Collision Migration Slice](../collision-migration-slice/issue.md)。票 01（0.2.4.9 版本身份 + Resource 表征门）已关单：`audit/2026-09-18/Implementation-0.2.4.9-CollisionTicket01-1235.md`。当前前沿是 [02 Resource 经声明式 Demand Policy 接入共享投影引擎](../collision-migration-slice/issues/02-shared-demand-projection-for-resource.md)。实施窗口 `/implement`。

---

## 尚未明确

以下不再阻塞本图，作为 `/to-spec` 的实现开放项，而不是未完成的 wayfinder 决策：

- Collision 补偿采用 snapshot、可逆操作还是补偿句柄（必须能表达为 Acquisition Receipt，不强制整区 snapshot）。
- Collision 域内部是否拆分静态 Collider 与门动画等公开 Capability Lease（执行层已要求按变更粒度记 receipt）。
- reconciliation / quarantine 的时间参数、类名、文件布局、共享引擎是否改名。
- Collision 完成后 Item 是否能直接复用同一编排引擎（本图结束后才可能毕业）。

---

## 范围外

- Item / Zombie 铺域、Vehicle（M8）。
- Animal 域设计决策（禁止直接接 SPI）。
- Route B fail-open / Denied 终态、白名单中途关闭卡死、隔离信号、OwnerPrefix 反射、握手容量位置。
- 直连 / DNS A2S advertisement、IPv6、DNS 策略。
- 09-17 口述三条（队伍不跨局 / 主机看客机卡顿 / 枪声）；缺 UMM 日志前不立目标。
- 主机侧僵尸/动物 tick 切片性能、载具物理 dedicated 门。
- 重开或复活旧 M5–M8 地图的 Animal-first / M7 未完成项作为本图依赖。
- 把评审 §七第 6 项「并入本地玩家覆盖或去掉 `tree.disable()`」当作本图目的地的替代品。
