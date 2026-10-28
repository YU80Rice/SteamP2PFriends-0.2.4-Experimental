# Item Migration Slice：全图数据准备、按需求运行与原生同步闭环

- **Label**: `ready-for-agent`
- **Type**: Specification
- **Status**: Approved specification — 按独立实施票执行，尚未实现
- **Version**: `0.2.4.10-Experimental`

## Problem

Item 已有原生生成/复制补丁和账本，但尚未接入 Resource/Collision 已验证的共享需求与生命周期编排；全图数据准备、自然周期、实际掉落到期和逐观察者基线的职责还未按已接受政策整合。

## Scope

只做 C-P2 Item 首切片。继续复用原生仓库/RPC/交互；不强制接入 LMN，不实现发电机/作物/动物/载具等后续领域补算，不替整张 C 阶段地图关单。

Q14另批准本次 [恢复 U3DS 原生20米拾取距离校验](../item-authoritative-pickup-distance/issue.md)，单列权威交互任务；本生命周期/同步范围不扩大，两者分别验收。

## Reviewable outputs

- [Item Migration Slice 实施规格](spec.md)
- [测试入口与工单拆分建议](ticket-plan.md)
- [规格编制输入](planning-inputs.md)
- [原生创建/删除/同步衔接](native-integration-flow.md)
- [文档审查链](review-chain.md)

当前仅规划，无生产、测试、构建或部署；`GAP-C-P2-IMPLEMENTATION-TDD` 保留。2026-10-08用户批准两条测试入口、七票粒度/依赖和工程默认值；执行必须选择一张独立票，不能一次承接整个规格。见 [已批准票图](ticket-plan.md) 与 [执行输入](execution-inputs.md)。

2026-10-08用户明确本阶段除构建外无需双轴审查，额度用于核心步骤和决策；后续规划按此执行，历史审查保留其当时范围。Q11已选择到期分类对齐U3DS，见 [0025: Item 到期分类对齐 U3DS](../../docs/adr/0025-item-expiry-classification-aligns-with-u3ds.md)。
