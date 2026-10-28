# 本阶段先做原生对照，再从 Item 开始逐域迁移

- **Label**: `wayfinder:done`
- **Type**: HITL
- **Parent**: `map-c-stage-world-state-migration`
- **Status**: Closed (User decision recorded)
- **Owner**: YU80Rice
- **Closed**: 2026-10-07

## Question

C 阶段后续工作应只深入 Item，还是先了解原生 U3DS 与剩余世界状态的关系？

## Comments

### 用户裁定 — 2026-10-07

用户原文：

> 先调研U3DS的联机体验如何实现（世界状态等），然后明确哪些需要避免复用U3DS的全地图保活逻辑，哪些需要转入multi-observer
> 而Item只是迁移的一个开始，就像资源和碰撞那样

本阶段先调查原生实现，再讨论运行职责的复用与迁移；Item 是后续逐域迁移起点。该裁定没有指定每个领域的休眠规则、全部后续顺序、新协议或持久化扩展。

原生是否采用全图持续激活属于待调查事实，不能将用户这段目标描述当作所有领域现状已证实。
