# Ticket 5: 自动化测试套件分层与回归架构升级

- **Label**: `wayfinder:task`
- **Type**: AFK
- **Parent**: `map-top-level-architecture-blueprint`
- **Status**: Open (Frontier)

---

## Question

当前 `WhitelistTests` 项目已经容纳了 114 项测试，但测试执行器全部集中在一个庞大的 `Program.cs` 中。
如何对测试套件进行结构化重构：
1. 按领域拆分为 `CoreTests/`、`SecurityTests/`、`ItemAdapterTests/`、`ZombieAdapterTests/`、`EnvironmentAdapterTests/`；
2. 引入标准化的 Test Runner 与断言辅助工具；
3. 保障在不启动游戏与 Steam 客户端的情况下，纯内存执行毫秒级回归测试，并在持续集成中保持 100% 绿色？
