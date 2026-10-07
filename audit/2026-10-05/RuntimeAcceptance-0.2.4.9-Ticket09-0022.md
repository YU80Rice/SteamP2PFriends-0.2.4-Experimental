# RuntimeAcceptance-0.2.4.9-Ticket09-0022 — Collision 代次自举诊断复测

- **日期**：2026-10-05
- **票**：`.scratch/collision-migration-slice/issues/09-shared-1h2g-runtime-acceptance.md`
- **候选**：`0.2.4.9 / CandidateRole=Cutover`
- **测试形态**：Host 1 + Guest 2；P2P listen-host
- **前置异常**：本轮首次开房被工坊依赖 `InventorySort 背包排序插件 (3802727197)` 的空 origin 阻断；取消订阅后重新开房成功。本异常不是票 10 插件逻辑失败。

## 1. 证据包

- Host：`D:\Agent-工作目录\DevelopMyUNMultiplayerModAndModloader\启动器\UnturnedModManager\publish\UMM-v2.2.1-win-x64\UMM-诊断包_20261005_002205\LogOutput.log`（18,965 行）
- Guest 1：`D:\Agent-工作目录\DevelopMyUNMultiplayerModAndModloader\启动器\UnturnedModManager\publish\UMM-v2.2.1-win-x64\UMM-诊断包_20261005_010154\LogOutput.log`（8,029 行；包名为 2026-10-05，但包内摘要/日志来源仍指向 2026-10-01 19:21:13，且无 Collision 行，不能作为本轮 Guest1 运行证据）
- Guest 2：`D:\Agent-工作目录\DevelopMyUNMultiplayerModAndModloader\启动器\UnturnedModManager\publish\UMM-v2.2.1-win-x64\UMM-诊断包_20261005_002233.zip` 内 `LogOutput.log`（7,967 行）
- 候选构建审计：`audit/2026-10-01/RuntimeFix-0.2.4.9-Ticket10-2159.md`
- 票 10 候选部署身份：
  - SHA-256：`F6C46B82AF24A91098D2C96037CC44C1ABA00258DA69C39A87057366C1F9F083`
  - MVID：`bcd76cfa-b191-43ec-89bc-989698897100`
  - Version：`0.2.4.9`
  - Case-ID：`SPF-0.2.4.9-Experimental-CollisionSlice-Cutover`
  - CandidateRole：`Cutover`

## 2. 三端身份

Host、Guest 1、Guest 2 的 `[BuildFingerprint]` 行均报告同一 SHA-256、MVID、Version、Case-ID 和 CandidateRole；但 Guest 1 目录虽然命名为 `20261005_010154`，其 `UMM-诊断摘要.txt` 的“最近一次受管会话”和来源日志仍是 **2026-10-01 19:21:13**，且该 `LogOutput.log` 没有 `[CollisionExecution]` 行。因此 Guest 1 只能作为旧会话的身份/连接辅助证据，不能被表述为本轮 Host/Guest 2 会话的有效客机运行证据。Host 日志本身保留了本轮两条远端连接及 `observers=3` 的直接证据。

## 3. Host 会话与双客机加入

Host `LogOutput.log`：

- `StartP2PServer` 成功进入 `Elver` 房间（行 904）。
- `Provider.onServerHosted` 回调触发，listen server 就绪（行 951 起）。
- 两条远端 Steam transport 均进入 `FindingRoute -> Connected`：行 7232、8958。
- `[MultiObserver/M0]` 先出现 `observers=2`（行 8150），随后出现 `observers=3`（行 9921 起），且 `pendingObservers=0`。
- 在 `observers=3` 期间，`collisionDemandRegions` 与 `collisionActiveLeases` 均有有效值，`collisionWriter=CollisionExecutionPort`。
- 两条远端连接关闭后，观察者数回落为 1（行 18059 起），会话以 `host-session-ended:DisconnectCompleted` 收束（行 18922）。

这证明本轮 Host 实际观察到房主 + 两个远端连接，而不是仅有单客机测试。

## 4. Collision 诊断复测

Host `LogOutput.log` 的 `[CollisionExecution]` 结果：

- `outcome=success`：160 条。
- `outcome=rejected`：0 条。
- `reason=acquire-identity-rejected`：0 条。
- 首次成功行从 `regionGeneration=1 acquireGeneration=1` 开始（行 1254），随后出现 `acquireGeneration=2、3...`，证明票 10 的读侧初始代次已使首次 Acquire 成立，并且提交后代次继续推进。
- `receiptAcquireGeneration=` 记录存在（111 条诊断行）；该计数是 receipt 诊断输出行的粒度，不与 160 条 CollisionExecution success 逐条一一对应，二者不能直接相减或视为同一事件计数。它仍说明成功 Acquire 进入 receipt 路径。
- `LifecycleReassert` 成功行存在（行 4671–4673），说明租约成立后的维护拍仍在运行。
- `[MultiObserver/M0]` 在双客机阶段持续显示 `collisionWriter=CollisionExecutionPort`，未出现回退旧 Writer 的运行时信号。
- 会话结束时 Resource `SessionEnd` 为 `outcome=success cleanup=complete`（行 18921），无跨域中止信号。

## 5. 人工现象

用户报告：碰撞和开关门同步恢复，人工观察无异常。该现象与 Host 侧 Collision Acquire success、receipt 记录、维护拍成功和双客机观察者汇总一致。

## 6. 票 09 三问结论

1. **门区域 Collision Acquire 是否成功？——通过。** Host 本轮 160 条 CollisionExecution success，0 条 identity rejection；首次 generation 为 1。
2. **门是否进入实际 ownership/receipt？——通过。** Host 存在 receiptAcquireGeneration 记录，CollisionExecutionPort 为当前 writer；用户观察到门碰撞与开关门行为恢复。
3. **客机需求能否在房主相机范围外维持状态？——人工现象通过，日志支撑为部分。** 用户观察到远区碰撞/开关门同步恢复；Host 双客机阶段有非零 Collision demand/active lease。当前日志未把具体门坐标、具体 Guest SteamID、房主相机剔除状态与单条 receipt 做一一关联，因此不宣称超出相机剔除距离这一子断言已由日志独立证明。

## 7. 运行验收判定

- **票 10 代次自举诊断目标：PASS。** 首次 Collision Acquire 已从上一轮 4293 条全拒、generation 0，恢复为 generation 1 起步并成功提交；人工碰撞与开关门行为恢复。
- **票 09 本轮诊断复测：PASS（核心 Collision 复测）。** Host 本轮确实有 1 Host + 2 Guest 连接、`observers=3`、Collision success 且无拒绝；Guest 2 为本轮包，Guest 1 提供同身份辅助包。
- **完整 Migration Slice 关单：暂不在本报告单独宣称 completed。** 原因是 Guest 1 目录命名与包内实际会话来源不一致，且该包没有 Collision 行，不能作为本轮 Guest1 运行证据；本报告只覆盖本轮 Host/Guest2 可证的 Collision 代次自举诊断目标。完整票 09 其余 Resource 回归、同/跨区矩阵、离开隔离、滞回、会话恢复、旧 Writer 零调用等需按票面逐项归档后再由用户关单。

## 8. 后续边界

- 不修改票 10 代码，不回退 06/08，不恢复旧 Collision Writer。
- 本次开房前置异常归环境工坊依赖：取消订阅 `InventorySort 背包排序插件 (3802727197)` 后成功开房；不纳入 Collision 缺陷。
- 票 09 保持 `in-progress`，本报告只记录核心诊断复测通过与证据边界。
