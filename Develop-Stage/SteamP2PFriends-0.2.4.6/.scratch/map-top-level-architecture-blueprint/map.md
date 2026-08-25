# Wayfinder Map: SteamP2PFriends 顶层架构重构与全景蓝图

- **Label**: `wayfinder:map`
- **Owner**: YU80Rice, AI Assistant
- **Status**: Active (Wayfinding in progress)

---

## Destination

将 `SteamP2PFriends` 从历史单体补丁聚合架构（4,003 行 `SteamP2PFriendsPlugin.cs` 及杂乱散落补丁）彻底解耦重构为**分层严密、深模块化（Deep Modules）、可插拔、具备统一 SPI 契约的 Minecraft 局域网票据式多观察者生命周期体系**，为后续 M5 全域收官与稳定发布奠定坚不可摧的架构底座。

---

## Notes

- **工作语言**：简体中文
- **架构范式**：Minecraft LAN-Style Chunk-Ticket Leasing Architecture (ADR 0001~0004)
- **铁规**：
  1. 保证单机本地玩家（Listen-Host 房主）与 U3DS 运行路径的零侵入与零破坏；
  2. 严禁全局伪造 `Dedicator.IsDedicatedServer`；
  3. 严守四维代数隔离公理（`SessionEpoch` $\perp$ `ConnectionGeneration` $\perp$ `RegionGeneration` $\perp$ `EntityGeneration`）；
  4. 遵循 TDD 闭环（Red $\rightarrow$ Green $\rightarrow$ 单元测试验证 $\rightarrow$ 3 端多机运行验收 $\rightarrow$ 阶段冻结）。

---

## Decisions so far

<!-- 已闭环决议索引（随工单决策推进不断追加） -->
- 暂无（正在进行前沿决议探讨）

---

## Live Decision Tickets (Frontier)

1. [Ticket 1: 顶层分层解耦与 4000 行 Plugin 瘦身方案](ticket-01-top-level-layering/issue.md) (`wayfinder:grilling`, HITL)
2. [Ticket 2: 领域生命周期与复制适配器统一 SPI 契约设计](ticket-02-domain-adapter-spi/issue.md) (`wayfinder:prototype`, HITL)
3. [Ticket 3: 历史 P0/P1 系列补丁清洗、归并与退役矩阵](ticket-03-legacy-patch-retirement/issue.md) (`wayfinder:research`, AFK)
4. [Ticket 4: 命名通信通道 (LaunchMultiplayerNet) 与双端路由规范](ticket-04-channel-transport-routing/issue.md) (`wayfinder:grilling`, HITL)
5. [Ticket 5: 自动化测试套件分层与回归架构升级](ticket-05-test-suite-architecture/issue.md) (`wayfinder:task`, AFK)

---

## Not yet specified (Fog of War)

- M5 阶段（动植物/资源/环境交互）在全新领域适配器 SPI 下的具体实施方案；
- 0.3.0 稳定版本的配置持久化与向下兼容性保障；
- 动态热重载与房间生命周期多状态转换的平滑迁移。

---

## Out of scope

- 脱离 SteamP2P 生态重写游戏底层物理引擎；
- 改变 Unturned 原版核心资产打包或存档文件二进制格式。
