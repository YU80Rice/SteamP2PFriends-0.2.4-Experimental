# 06: Item 与 Zombie 结构迁移

**What to build:** 让 Item 与 Zombie 的生命周期、快照、复制、补丁和测试按各自领域聚合，同时不改变已建立的同步行为。

**Blocked by:** 02 / Patch Registration Orchestrator 拆分；03 / Domain Id、Region Key 与 Bound Key 规范化

**Status:** completed

- [x] Item 与 Zombie 的适配器、补丁、身份和测试归属清晰。
- [x] 现有生命周期、状态复制、Region/Bound 语义和 generation 行为保持不变。
- [x] 不新增并行 Authority Writer，不将结构迁移误报为功能修复。
- [x] 现有 Item/Zombie 测试和静态注册证据保持通过。
- [x] Release 构建、回归测试和独立审核通过。

完成证据：

- 领域归属与入口映射：`docs/architecture/item-zombie-ownership.md`；
- 模块目录与 Registration Trace：`docs/architecture/module-ownership.md`、`docs/architecture/registration-trace.md`；
- 结构 StaticIL 接缝：`WhitelistTests/StaticIL/ItemZombieOwnershipStaticILContractTests.cs`；
- 回归测试：主入口 `186/186 PASS`；
- Release 构建：主项目与测试项目 0 errors / 0 warnings；
- 独立 Standards/Spec 审核与交付报告：`audit/2026-08-26/Implementation-0.2.4.8-1850.md`。

范围说明：本票只完成行为保持型结构迁移；Singleplayer、listen-host、U3DS、P2P Runtime 仍待独立验证，Resource Production Control Seam 和旧生产 Authority Writer 退休不在本票内。

## 验收关单(2026-09-11,用户批量批准)

- Runtime 确认:Item 交互/拾取/掉落物复制与 Zombie 区域生命周期在 2026-09-07/09-11 多轮三端回归中无行为回归报告(`audit/2026-09-07/Runtime-0.2.4.8-Ticket11-2343.md`、`audit/2026-09-11/Runtime-0.2.4.8-Ticket12-0031.md`)。
- 边界:经三端回归**间接确认**,未做 Item/Zombie 专项验证矩阵;Zombie 重生缺口(第三方评审 §4.1)为**存量未修项**而非迁移回归,已登记推广路线(ReviewRecheck-0933 第二梯队)。
- 状态:ready-for-human → completed(用户 2026-09-11 验收)。
