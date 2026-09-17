---
title: "Join Routing：会话密码、SteamID 密码传递与 SteamID:port 分类"
status: "ready-for-agent"
labels:
  - "ready-for-agent"
created_at: "2026-09-11T16:00:00+08:00"
updated_at: "2026-09-18T00:56:00+08:00"
---

# Join Routing：会话密码、SteamID 密码传递与 SteamID:port 分类

## 概述

在进入世界之前，让房主能为当前 listen-host session 设置可选密码，让客机 SteamID 路线把直连页密码传入连接参数，并让地址栏 `SteamID:port` 被分类为 Steam P2P。不持久化密码，不改写原版 `PASSWORD` 失败语义，不触及 Route B Quarantine。

## 关联规范

- 规格：[spec.md](./spec.md)
- 实施票：[03 SteamID:port](./issues/03-steamid-port-suffix-routing.md)、[04 会话密码](./issues/04-listen-host-session-password.md)
- 共享 Runtime 验收：[05](../listen-host-join-routing-runtime-acceptance/issues/05-shared-1h2g-runtime-acceptance.md)（2026-09-18 用户关单 completed；票 03 地址栏整串 `SteamID:port` 仍 pending）
- 机制独立于 [Listen-Host Dedicated Gate](../listen-host-dedicated-gate/spec.md)
