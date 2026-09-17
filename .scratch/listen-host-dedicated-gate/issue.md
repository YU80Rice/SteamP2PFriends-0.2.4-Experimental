---
title: "Listen-Host Dedicated Gate：僵尸重生与物品周期 despawn/respawn"
status: "completed"
labels:
  - "ready-for-agent"
created_at: "2026-09-11T16:00:00+08:00"
updated_at: "2026-09-18T00:56:00+08:00"
---

# Listen-Host Dedicated Gate：僵尸重生与物品周期 despawn/respawn

## 概述

让 P2P listen-host 在原版 dedicated 早退条件上与专用服对齐，使僵尸死亡后能再次生成、地面物品能按服务器节奏消失并再生。只改 Listen-Host Dedicated Gate，不迁移僵尸域或物品域 SPI，不打开全量 tick 切片。

## 关联规范

- 规格：[spec.md](./spec.md)
- 实施票：[01 僵尸重生](./issues/01-zombie-respawn-dedicated-gate.md)、[02 物品周期](./issues/02-item-periodic-lifecycle-dedicated-gate.md)
- 共享 Runtime 验收：[05](../listen-host-join-routing-runtime-acceptance/issues/05-shared-1h2g-runtime-acceptance.md)（2026-09-18 用户关单 completed）
- 机制独立于 [Join Routing](../join-routing/spec.md)
