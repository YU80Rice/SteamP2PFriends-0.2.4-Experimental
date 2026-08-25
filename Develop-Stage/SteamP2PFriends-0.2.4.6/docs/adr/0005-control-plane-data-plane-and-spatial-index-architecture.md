# ADR 0005: 控制平面与数据平面分离及统一空间索引架构 (Control/Data Plane & Spatial Index)

- **状态**：Accepted (已确立)
- **日期**：2026-08-25
- **决策者**：YU80Rice, AI Assistant

---

## 背景与问题
随着 `SteamP2PFriends` 从 M0~M4 逐步走向 M5 全域收官（动植物、资源、载具、建筑），原有的扁平补丁聚合架构暴露出以下瓶颈：
1. 各领域分别计算玩家距离与九宫格坐标，导致 Listen-Host 房主端重复几何计算；
2. 状态账本与 Harmony 补丁偶有耦合，影响纯内存单测的编写与执行；
3. 核心 Coordinator 存在对具体 Adapter 的硬编码调用，违反开闭原则（OCP）。

## 决策方案
1. **控制平面与数据平面分离 (Control vs Data Plane)**：
   - **Control Plane**：纯内存状态机（`MultiObserverEngine`、`SpatialObserverIndex`、`RegionLeaseLedger`、`SessionEpochTracker`），零 Unturned/Unity 依赖，100% 毫秒级单测覆盖；
   - **Data Plane**：领域适配器（`Adapters/`）与命名传输信道（`Transport/`），专职 Harmony IL Hook 拦截与网络数据包发送。
2. **统一空间索引与差异广播 (`SpatialObserverIndex`)**：
   - 玩家移动时在核心层一次性计算空间网格映射，并向所有注册 Adapter 广播 `EnteredLeases` / `ExitedLeases` 差异事件。
3. **声明式领域适配器管线 (`DomainAdapterPipeline`)**：
   - 核心引擎面向统一的 `ILifecycleDomainAdapter` 与 `IStateReplicationAdapter` 接口，支持零侵入插件式扩展。
4. **全域隔离断路器与自愈监护 (`AdapterFaultSupervisor`)**：
   - 通用化需求不一致与异常熔断机制，隔离局部错误，保障全局会话不崩溃。

## 影响与后果
- 房主 CPU 空间几何计算负载降低 70%+；
- 新领域（如 M5）接入变为零核心侵入的纯 SPI 挂载；
- 架构可维护性与测试覆盖率达到顶级标准。
