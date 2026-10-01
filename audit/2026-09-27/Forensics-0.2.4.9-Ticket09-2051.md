# Forensics 0.2.4.9 — Ticket 09 S3 轮 Acquire 拒绝取证

日期：2026-09-27 20:51
证据来源：用户 PowerShell 亲自采样（权威）+ 源码接线（git 可复核）
结论效力：**推翻 RuntimeFix-0.2.4.9-Ticket09-0938.md §2 的根因叙事**；本轮返修落点按主工作树第一分叉定为 **06/08 执行路径**。

## 1. 实测定案（用户回贴，权威）

上一轮 S3 失败测试（候选 `F055B34C…`）主机日志：

- `[CollisionExecution]` 诊断 **4293 条全部 `outcome=rejected reason=acquire-identity-rejected`，无一成功**；
- 引擎层面 `domain=Collision` 的 `LeaseAcquire` 事件 **为零**；
- 样例行统一签名：`regionGeneration=0 acquireGeneration=0`（如 region=(29,27)…，sessionEpoch=1，日志第 1187 行起）。

即：**整场测试 Collision 租约一次都未成立**。没有租约就没有所有权，`OnLifecycleTick` 维护拍无物可保——0938 报告 §2"原版停用后无人再激活"的叙事建立在"租约曾成功"的错误前提上，作废。门隐形墙的直接原因是**门从未被插件接管**。

## 2. 缺陷定位（源码接线，待红测证实）

- 端口代次源：`MultiObserverShadowCoordinator.cs:150-154` 将 `LevelObjectCollisionAdapter.GetGeneration`（读适配器 Ledger）接为 `_generationReader`。
- Ledger 语义：`LevelObjectCollisionLedger.GetGeneration` 对从未 Acquire 的区域返回 **0**（`_generations.TryGetValue` 缺省 0）。
- 引擎 Acquire 路径：`LifecycleOrchestrationEngine.cs:817` 以 `beforeAcquire = Port.ReadRegionGeneration(region)`（=0）构造票据（`CreateTicket:1255-1259`）。
- 提交门：`CollisionExecutionPort.CanCommit` 要求 `ticket.RegionGeneration.IsDefined`——`RegionGeneration(0)` 未定义 → **首次 Acquire 恒被拒**；代次只在 CommitAcquire 后才变 1 → **自举死锁**：读 0 → 票 0 → 拒 → 永远读 0。与实机 4293 条 `regionGeneration=0 acquireGeneration=0` 签名逐字段吻合。
- 对照 Resource 侧成功先例：`ResourceRegionLifecycleAdapter.GetGeneration` 的代次源不返回未定义值，故 Resource 租约成立。
- 既有测试为何未拦截：CEP 系测试均以显式 `generation:7` 构造票据，绕过了自举路径——盲区与"启动装配路径"同型。

## 3. 下一刀（按主工作树裁定，从当前 HEAD 继续改，不回退票号）

落点：06/08 执行路径——**Collision 代次源的自举语义**。方向：让从未 Acquire 的区域首次读出**已定义**的初始代次（对齐 Resource 先例），提交门身份校验保持不变（reader==ticket 仍强制）；红测在 CEP 接缝补"从未 Acquire 的区域首次 Acquire 必须成功"（当前红）。既有 `OnLifecycleTick` 维护拍保留——租约成立后它才是必需的执行机制（与旧 Writer postfix 同型的持续强制），本轮不回退。

## 4. 今晚诊断复测的效力

分叉已由本取证定案，复测不再承担"决定返修落点"的职责；在新候选上重测**预期仍是全拒绝**（接线缺陷仍在 `7C0ADF80…` 中）。是否仍跑由用户定：跑=回归+确认签名；不跑=等下一刀落地后一并验。09 维持 in-progress 不关单。
