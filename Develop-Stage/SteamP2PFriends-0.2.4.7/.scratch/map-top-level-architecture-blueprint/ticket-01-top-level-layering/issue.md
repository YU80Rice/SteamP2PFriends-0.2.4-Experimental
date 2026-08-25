# Ticket 1: 顶层分层解耦与 4000 行 Plugin 瘦身方案

- **Label**: `wayfinder:grilling`
- **Type**: HITL
- **Parent**: `map-top-level-architecture-blueprint`
- **Status**: Open (Frontier)

---

## Question

当前 `SteamP2PFriendsPlugin.cs` 拥有超过 4,000 行代码，同时堆叠了 BepInEx 入口、配置绑定、各种 Patch 手动注册、Update/Tick 主循环调度、Route B 隔离计时器、会话清理与诊断。
如何将其彻底拆分为以下清晰的 5 层体系结构，将 `Plugin.cs` 瘦身至 150 行以内的纯粹生命周期转发中枢？

### 拟定 5 层体系
1. **Core Layer**：`Plugin.cs`、`ConfigManager`、`LifecycleDispatcher`；
2. **MultiObserver Engine Layer**：`Coordinator`、`WorldPresenceObserverSet`、`SessionEpochTracker`；
3. **Domain Adapters Layer**：`Adapters/Item/`、`Adapters/Zombie/`、`Adapters/Environment/`；
4. **Security & Route B Layer**：`Admission/`、`Quarantine/`、`Approval/`；
5. **UI & Diagnostics Layer**：`UI/`、`Logging/`、`Diagnostics/`。
