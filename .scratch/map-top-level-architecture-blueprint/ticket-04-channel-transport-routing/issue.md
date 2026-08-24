# Ticket 4: 命名通信通道 (LaunchMultiplayerNet) 与双端路由规范

- **Label**: `wayfinder:grilling`
- **Type**: HITL
- **Parent**: `map-top-level-architecture-blueprint`
- **Status**: Open (Frontier)

---

## Question

功能性联机模组铁规要求：必须基于 `LaunchMultiplayerNet` 命名通道实现双端闭环同步。
在当前 Listen-Host 混合模式下：
1. 原版 Unturned RPC 与 `LaunchMultiplayerNet` 频道消息各自承担什么职责？
2. 如何设计清晰的通道接入层（`TransportChannelRouter`），使领域适配器只需声明 `SendSnapshot`、`BroadcastDelta`，底层自动选择原生 RPC 或命名私有频道？
3. 如何保障在没有 U3DS 的情况下，客机与房主之间的消息可靠、有界、防篡改？
