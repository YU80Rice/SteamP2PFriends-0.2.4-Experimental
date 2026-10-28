# 原生 U3DS 世界状态、激活与复制职责事实调查

- **Label**: `wayfinder:research`
- **Type**: AFK
- **Parent**: `map-c-stage-world-state-migration`
- **Owner**: YU80Rice
- **Assignee**: YU80Rice (Codex 执行)
- **Status**: Closed (首轮事实地图复核完成；2026-10-07)
- **Blocked by**: 无

## Question

原生 U3DS 如何维护与同步各类世界状态？哪些部分依赖全局扫描、观察者空间、实体活动或持续计时？当前 listen-host 补丁与 multi-observer 接线分别承担什么？

## Investigation contract

- 对照插件与 U3-SDK 固定 commit；记录来源路径、行号、调用链和证据限制。
- 覆盖 Item、Resource、Collision/Object、Zombie、Animal、Barricade/Structure、Vehicle；并盘点玩家、时间/天气、存档与事件边界。
- 对每域区分权威状态存储、初始化/刷新、模拟与计时、空间激活、初始基线、增量/交互、保存恢复。
- 不以 Dedicated getter、全局实体列表或全图计时扫描单独推断全图物理/AI 保活。
- 列出可能复用或迁移的运行职责，明确这是建议而非用户已批准决策。
- Item 既有原生拾取/掉落协议与可靠入队基线不能描述为从零缺失；可靠入队不等于客机应用确认。
- 不将物品跨会话持久化预设为原生行为；先查实际保存调用链。
- SPF 原生世界同步复用既有协议；功能模组的 LMN 规则不自动成为本迁移的依赖要求。用户后续范围澄清见 [0021: SPF 原生世界同步迁移不自动成为 LMN 消费方](../../../docs/adr/0021-spf-native-world-sync-and-gameplay-mod-channel-boundary.md)。
- 只写研究文档，不修改生产实现，不构建、不部署、不授予 DLL 身份或 Runtime PASS。

## Assets

- [原生世界状态调查索引与迁移讨论入口](findings.md) 及其具名分报告。
- [C 阶段原生世界状态首轮调查审查链](review-chain.md)：R2 Standards CLEAN / Spec CLEAN。

## Comments

### Claim — 2026-10-07

已在调查前领取。本轮先收集事实，为之后的用户问询提供材料；不会提前生成 ready-for-agent 实施票。

### 首轮 Resolution — 2026-10-07

manager、关键实体及代表性生产组件的首轮事实地图已完成；R1 文档规范问题已修复，R2 使用两个全新独立上下文并双轴 CLEAN。完成范围只限本轮事实材料，不代表全部世界行为已盘点完毕。

后续进入 [原生职责复用与按需运行的边界是什么](../ticket-runtime-duty-boundary/ticket.md) 的 live exchange。各分报告未查项与 `GAP-C-P2-IMPLEMENTATION-TDD` 保持；不冻结运行政策、接口、后续顺序或实施票，不授予 Runtime PASS。
