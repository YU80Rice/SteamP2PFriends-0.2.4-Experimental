# RuntimeAcceptance-0.2.4.9-Ticket09-2313 — 09 全流程三端验收

- **日期**：2026-10-05
- **票**：`.scratch/collision-migration-slice/issues/09-shared-1h2g-runtime-acceptance.md`
- **候选**：`0.2.4.9 / CandidateRole=Cutover`
- **测试形态**：1 Host + 2 Guest，P2P listen-host；Guest-A / Guest-B 按本轮行为角色命名
- **前置环境**：`InventorySort 背包排序插件 (3802727197)` 保持取消订阅，Host 成功开房

## 1. 三端证据

- Guest-A：`D:\Agent-工作目录\DevelopMyUNMultiplayerModAndModloader\启动器\UnturnedModManager\publish\UMM-v2.2.1-win-x64\UMM-诊断包_20261005_231224\LogOutput.log`（16,639 行）
- Host：`D:\Agent-工作目录\DevelopMyUNMultiplayerModAndModloader\启动器\UnturnedModManager\publish\UMM-v2.2.1-win-x64\UMM-诊断包_20261005_231258\LogOutput.log`（21,462 行）
- Guest-B：`D:\Agent-工作目录\DevelopMyUNMultiplayerModAndModloader\启动器\UnturnedModManager\publish\UMM-v2.2.1-win-x64\UMM-诊断包_20261005_231323.7z` 内 `LogOutput.log`（11,321 行）
- **前导阶段排除**：Host 在本轮先进入了单人游戏；该阶段不属于票 09 的 P2P Runtime 证据。正式验收窗口从 Host `P2P-Menu] 创建房间`（行 2662）和 `StartP2PServer`（行 2663）开始，以下所有 Host 计数均限定在该 P2P 会话（`sessionEpoch=1`）内。
- 三端摘要时间：Guest-A `23:12:18`、Host `23:12:54`、Guest-B `23:13:15`；均为本轮 2026-10-05 包

三端 `[BuildFingerprint]` 均为：

- SHA-256：`F6C46B82AF24A91098D2C96037CC44C1ABA00258DA69C39A87057366C1F9F083`
- MVID：`bcd76cfa-b191-43ec-89bc-989698897100`
- Version：`0.2.4.9`
- Case-ID：`SPF-0.2.4.9-Experimental-CollisionSlice-Cutover`
- CandidateRole：`Cutover`

## 2. Host + 2 Guest 会话链

Host 日志记录：

- 观察者汇总依次出现 `observers=1`、`observers=2`、`observers=3`、再次 `observers=2`、最后 `observers=1`（Host 行 3117、5941、12712、16765、20947）。
- 两个不同远端 SteamID 均进入 Steam transport `FindingRoute -> Connected`（Host 行 5081、11704）；Guest-A 后续重连产生新的连接代次，Host 观察者事件出现 `connectionGeneration=2`、Guest-B 出现 `connectionGeneration=3`。
- Host 观察者事件包含两次 `ObserverAdded`、两次 `ObserverRemoved`，以及重连后的新加入；最终会话以 `session-end nextEpoch=2 reason=host-session-ended:DisconnectCompleted` 收束（Host 行 21419）。
- Guest-A 日志有 `ConnectionGeneration` 1→2 及两次 `ConnectionEnd`；Guest-B 有本轮 `ConnectionGeneration=1` 和正常 `ConnectionEnd`。

## 3. Collision 全流程

Host 结构化结果：

- `[CollisionExecution] outcome=success`：192 条。
- `[CollisionExecution] outcome=rejected/failed`：0 条。
- `reason=acquire-identity-rejected`：0 条。
- 首条 Collision 成功为 `regionGeneration=1 acquireGeneration=1`（Host 行 3012）；末段仍有成功提交并继续使用已定义代次（Host 行 19917–19921）。
- Collision 领域 `LeaseAcquire authority=Collision outcome=success`：192 条对应的执行链存在。
- Collision 领域 `LeaseRelease authority=Collision outcome=success`：124 条；`CollisionReleaseRejected`：0；`receipt-invalid`：0。
- `receiptAcquireGeneration=` 诊断记录：124 条；该行是 receipt 诊断输出粒度，不与 192 条执行成功逐条等同。
- Host 观察者汇总在双 Guest 期间持续显示 `collisionWriter=CollisionExecutionPort`、`collisionDemandRegions` 与 `collisionActiveLeases` 非零。
- 本轮未检出 `LifecycleReassert outcome=failed` 或 Collision execution rejection；但没有把 `LifecycleReassert` 作为独立的成功计数证据，门维护结论以人工现象、Collision Acquire/Release 和当前 writer 证据共同支撑。

## 4. Resource 与跨域隔离

- **前导单人阶段不计入**：Host 第 2662–2663 行之前的单人游戏日志（包括 `sessionEpoch=0` 的 RegionEntry/CollisionActivation 等）不是本票 P2P 测试证据。以下 Resource/Collision 计数均为 `P2P-Menu 创建房间` 之后、`sessionEpoch=1` 的 P2P 子集。
- Resource `LeaseAcquire outcome=success`：576 条；Resource `LeaseRelease outcome=success`：372 条。
- Host 共享汇总在 `observers=3` 期间同时显示 Resource 与 Collision 的 demand/active lease，且两域 writer/控制面保持活动。
- Resource 砍树、资源碰撞和租约行为由用户人工观察无回归；日志中未见 `region-snapshot-failed`，Resource 会话结束为 `outcome=success cleanup=complete`（Host 行 21418）。
- Host 有 38 条 `sessionEpoch=1 Resource RegionEntry outcome=failed reason=observation-incomplete`，全部带 `steamIdRead=False`、`resourcesLoadedRead=True`、`failClosed=true`，未扩散为 Collision 失败、跨域中止或破坏性 Release。这些是被隔离的诊断坏样本，必须保留在审计中，不能伪报为零错误。

## 5. 释放、重入、会话边界与旧 Writer

- Host `LeaseReleaseScheduled`：910 条；共享 LeaseReentry：4 条，均 `outcome=success hysteresisCancelled=true`，证明释放滞回与重入取消链实际运行。
- Guest-A 离开/重连后 Host 观察者数量从 3 降至 2/1，再以新 connectionGeneration 重新加入；另一观察者需求期间 Collision active leases 保持非零。该证据覆盖**同一 sessionEpoch 内的连接代次恢复**，不等同于结束会话后重新开房的跨会话恢复。
- Host `SessionEnd outcome=success cleanup=complete`，随后 `nextEpoch=2`，证明本轮会话收束和 epoch 边界执行；本轮没有第二个新房间实例，因此不把跨会话重置/陈旧 receipt 资格宣称为完整 PASS。
- 旧路径明确记录 `LevelObjectRemoteCollisionPatch: retired=legacy-remote-coverage-writer`（Host 行 696）；除此之外未发现旧 Collision Writer 的生产调用或运行时回退。
- 本轮未出现 `receipt-invalid`、`CollisionReleaseRejected` 或 Collision execution rejection。

## 6. 票 09 清单判定

1. **正式候选及三端身份：PASS。** 三端同 SHA-256/MVID/Version/Case-ID/CandidateRole。
2. **Resource 迁移无回归：PASS（人工 + Lease 日志）。** 砍树/资源碰撞人工无异常，Resource Acquire/Release 成功；38 条坏样本 fail-closed 且未跨域传播，作为非阻塞诊断噪声记录。
3. **Collision Host 独在、Guest 远区、双 Guest 同区/跨区：PASS。** Host 观察者从 1 到 3，需求区域从 49 扩展至 68；Collision success 192、人工门碰撞和动画正常。
4. **Guest 离开隔离、滞回重入：PASS（本轮可观测子集）。** observers 3→2→1、LeaseReleaseScheduled 和共享 LeaseReentry success/hysteresisCancelled=true 存在；本轮没有 Collision 专属 `LeaseReentry` 字段，因此该项对共享需求/滞回链判 PASS，对 Collision 专属重入只记为部分证据。
5. **门动画、Collider、Receipt Release：PASS。** 人工门动画/碰撞正常；Collision Release success 124、无 release rejection/receipt-invalid。
6. **Collision 不改变树资源：PASS。** Resource 与 Collision 日志域隔离，人工砍树无回归。
7. **坏样本/区域/领域隔离与 retry：PASS（本轮可观测子集）。** observation-incomplete 均 fail-closed；无 Collision 拒绝或跨域中止。
8. **会话恢复/重置/陈旧 receipt：部分通过，不能宣称完整 PASS。** Guest-A connectionGeneration 递增和本轮 SessionEnd/nextEpoch=2 已证明同会话连接代次恢复与收束；但没有第二个新房间实例，跨会话重置与陈旧 receipt 资格仍待补充证据。
9. **旧 Writer 零生产调用：PASS。** 仅有退役声明，无旧 Writer 生产调用或回退信号；当前 writer 为 CollisionExecutionPort。
10. **审计/索引/地图状态与双轴审查：待本报告审查完成后收口。**

## 7. Runtime 判定

- **票 10 代次自举运行目标：PASS。** 首次 Collision generation=1，192 次 Acquire success，0 次 identity rejection。
- **票 09 全流程 Runtime 验收：P2P 核心流程 PASS，完整关单前置仍有两项待补。** 单人前导阶段已排除；从 Host 第 2662–2663 行开始的同轮 P2P 会话具备三端证据、Host + 2 Guest 全流程覆盖、人工碰撞/门动画/开关门正常、Collision Acquire/Release 闭环；但跨会话新房间恢复/陈旧 receipt 和 Collision 专属 Reentry 仍未形成独立充分证据。
- **Migration Slice：保持 `in-progress`，待补齐上述运行证据、双轴审查 CLEAN 后再标记 `ready-for-human`。** 不由 Agent 越权替代用户最终关单动作。

## 8. 保留项

- 38 条 Resource `observation-incomplete` 是 fail-closed 诊断坏样本；它们未跨域传播，但后续若要追求零诊断错误可另开缺陷票，不阻塞本票 Runtime 验收。
- 不修改票 10 代码，不回退 06/08，不恢复旧 Collision Writer。
