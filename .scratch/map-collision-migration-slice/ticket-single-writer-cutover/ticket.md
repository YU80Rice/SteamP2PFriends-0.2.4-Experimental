# 单一 Authority Writer 的切换和影子验证契约是什么

- **Label**: `wayfinder:done`
- **Type**: HITL
- **Parent**: `map-collision-migration-slice`
- **Status**: Closed
- **Owner**: YU80Rice
- **Closed**: 2026-09-18
- **Blocked by**:

---

## 问题

Collision 的新 Authority Writer 应如何完成影子验证、权威切换与旧路径退役？

## 决议

本票选择「允许双计算、禁止双写入；在会话边界原子切换，并以旧 Writer 完全退出生产调用图为完成条件」。迁移前，旧 `RemoteCoverage` Writer 可以继续作为唯一生产 Writer，新控制面仅执行无副作用的 Shadow Computation。影子比较用于解释新旧语义差异，而不把旧 remote-only 集合当作正确性金标；Host 新增覆盖、Resource 写入退出 Collision 等属于预期差异，授权 Guest 的 Collision 区域缺失、需求抖动及错误释放属于禁止差异。

正式候选构建在插件启动或新 Session 建立时确定唯一 Collision Authority Writer，不进行运行中按区域、玩家或比例切流。切换后，只有共享生命周期编排引擎经 Collision Domain Execution Port 可以写入 Collision 原生状态；旧扫描、旧集合、旧刷新 Writer、Resource 对 `IsRegionCovered` 的 fallback 以及生产影子比较全部退出生产调用图。

新 Writer 失败、retry exhaustion 或局部熔断时不得调用旧轮询，也不得在同一会话切回旧 Writer。回滚仅允许通过结束会话、部署上一份已验收构建并建立新 Session Epoch 完成。切片验收必须同时证明新 Writer 有效和旧 Writer 调用为零，不能再次以「新路径有日志」替代旧权威退役证据。

### 阶段

1. **历史行为刻画**：旧 Writer 仍是唯一 Writer；固定其实际行为，不证明其正确。
2. **只读影子计算**：新链路无副作用；可维护仅用于验证的编排状态与差异日志；不得提交原生状态，不得在旧 Writer 失败时接管。
3. **准入门判定**：按差异类别判定，不用相似度百分比。预期差异包括 Host 只出现在新 Demand、ResourceSpawnpoint 退出 Collision Writer、canonical observer 生命周期更稳定的清理。禁止差异包括授权 Guest 的 LevelObject 区域缺失、静止时无原因抖动、一 Guest 离开释放另一仍在场需求、旧 generation 残留、越界 RegionKey、Resource Demand 被当成 Collision Demand、新路径依赖私有扫描、单区域异常清空其它需求。
4. **会话边界原子切换**：启动或新会话一次性确定唯一 Writer。实现可分批提交，但任何可部署候选构建不得包含两个可写生产路径，不保留旧 Writer 开关。
5. **旧路径退役**：旧扫描、旧集合、旧 reconcile、旧刷新写入、Resource fallback、生产影子比较全部退出。若保留旧算法，只作为测试夹具或离线 oracle。
6. **失败与回滚**：运行时不得切回旧 Writer；安全状态的 destructive/non-destructive 细节由 Release 票决定；整体不可接受则部署级回滚。

### 影子比较结构

结果分类：`OnlyInNew` / `OnlyInLegacy` / `InBoth` / `ExpectedDifference` / `UnexpectedDifference`。聚合比较用 `DomainId + RegionKey`；归因还带 `ObserverId + ConnectionGeneration`。目标是所有差异可分类、可解释，不是两边相等。

### 证据

StaticIL：旧 Writer 不再注册、不再从 Tick 进入、无 Collision 对 ResourceSpawnpoint 写入、Resource 不引用 `IsRegionCovered`、Collision 不扫 `Provider.clients`、原生 Collision 写操作只有 Execution Port 一条生产入口。BuildArtifact：影子候选与正式切换候选分别有独立指纹。Runtime：新 Writer 执行、旧 Writer 调用为零、Host-only/Guest-only/双 Guest、离开不误释放、retry exhaustion 不触发旧路径、会话重建后旧 generation 无提交资格。

### 本票不决定

Collision Release 失败时的安全状态细节；哪些 coordinator 债阻塞接线；源码删除的提交切片与文件顺序；影子比较器的具体类名。

## 资产

- glossary：Read-Only Shadow Computation 收紧；新增 Session-Boundary Cutover
- ADR：[0012 会话边界原子切换与旧 Writer 退役](../../../docs/adr/0012-session-boundary-cutover-and-legacy-writer-retirement.md)
