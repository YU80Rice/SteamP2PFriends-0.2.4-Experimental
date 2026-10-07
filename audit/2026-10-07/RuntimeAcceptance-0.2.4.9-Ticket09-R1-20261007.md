# RuntimeAcceptance-0.2.4.9-Ticket09-R1-20261007 — 09-R1 诊断补证与功能证据合并收口

- **日期**：2026-10-07
- **票**：`.scratch/collision-migration-slice/issues/09-shared-1h2g-runtime-acceptance.md`
- **返修票**：`.scratch/collision-lifecycle-diagnostic-repair/`
- **候选**：`0.2.4.9 / CandidateRole=Cutover`
- **测试形态**：本轮 1 Host + 1 Guest；作为 09-R1 诊断播报探针。上一轮 1 Host + 2 Guest 功能与多观察者证据继续作为正式覆盖依据，不被本轮 1H1G 替代。
- **用户关单裁定**：用户确认上一轮 1H2G 证据仍有效，本轮只补诊断播报探针；本轮碰撞、门动画与碰撞正常，符合正常游玩体验。Guest 在流程完成后因内存问题闪退，Host 权威收尾完整。

## 1. 证据包

- Guest：`D:\Agent-工作目录\DevelopMyUNMultiplayerModAndModloader\启动器\UnturnedModManager\publish\UMM-v2.2.1-win-x64\UMM-诊断包_20261007_113000\LogOutput.log`（11,128 行）
- Host：`D:\Agent-工作目录\DevelopMyUNMultiplayerModAndModloader\启动器\UnturnedModManager\publish\UMM-v2.2.1-win-x64\UMM-诊断包_20261007_112912\LogOutput.log`（11,342 行）
- 两端第 15 行 `[BuildFingerprint]` 一致：
  - SHA-256：`2C3AFF22651386117316F474797A15FAF15E16C27382C758B85B4966DD2850A6`
  - MVID：`c9aab7c6-b7ee-411a-88b4-2f92915acf91`
  - Version：`0.2.4.9`
  - Case-ID：`SPF-0.2.4.9-Experimental-CollisionSlice-Cutover`
  - CandidateRole：`Cutover`
- Host 第 710 行 `DiagnosticBuildValid=true`；两端没有 `DiagnosticBuildValid=false`。

## 2. 本轮 Collision 诊断补证

Host 本轮的 `[LifecycleObs]` 已正确保留 Collision 身份，不再被 Resource 出口错标：

- `event=LeaseAcquire domain=Collision authority=Collision outcome=Success`：343 条（首条第 1176 行）。
- `event=LeaseReleaseScheduled domain=Collision authority=Collision demand=0`：245 条（首条第 3372 行，末段 9985–9987）。
- `event=LeaseRelease domain=Collision authority=Collision outcome=Success`：196 条（首条第 3704 行，末段 10290–10296）。
- `[CollisionExecution] outcome=success`：343 条；Collision 执行拒绝/失败：0 条。
- Receipt 关联 `[CollisionExecution]`：196 条，首条第 3703 行包含：
  `sessionEpoch=1 regionGeneration=1 receiptSessionEpoch=1 receiptRegionGeneration=1 receiptAcquireGeneration=50`。
- 本轮未出现 `CollisionAcquireRejected`、`receipt-invalid`、`stale-receipt` 或 `reason=release-identity-rejected`。
- 旧 Writer 仅有退役声明第 696 行：
  `retired=legacy-remote-coverage-writer`。
  `path=Legacy` 记录均为 `outcome=skipped reason=already-active sessionEpoch=0`，未检出实际旧 Writer 写入。
- 日志字段计数按结构化字段组合统计，报告中的示例为整理后的字段集合，不保证字段在原始行中连续相邻；关键首末行号已给出，复验时应按字段集合检索。

本轮 Host 日志没有自然产生 `event=LeaseReentry`；这不否定用户完成的流程，也不将本轮伪报为 Reentry 新样本。此前 2026-10-06 Washington 专测中已有 Reentry/滞回人工与关联证据；本轮的新增责任是确认修复后的 Collision 领域播报与 receipt 双侧身份，已完成。

## 3. 会话与功能结果

- Host 两次 Washington 房间生命周期：
  - 第 843 行创建房间 / 第 1034 行 `SessionBegin sessionEpoch=1`；第 5789 行 `SessionEnd outcome=Success cleanup=complete`，第 5790 行 `nextEpoch=2`。
  - 第 5821 行创建房间 / 第 5990 行 `SessionBegin sessionEpoch=3`；第 11299 行 `SessionEnd outcome=Success cleanup=complete`，第 11300 行 `nextEpoch=4`。
- Host 观察者与连接代次事件完成加入、离开和重新加入；Guest 日志至少有 `ConnectionGeneration` 1→2（Guest 第 155、5852、5871 行附近）。
- 用户人工确认：碰撞正常，门动画正常，门碰撞正常；测试流程在 Guest 闪退前已完成。
- Resource 有 `observation-incomplete` 的 fail-closed 记录，属于既有诊断噪声边界；没有跨域为 Collision rejection 或会话故障。

## 4. Guest 闪退归因

- Guest `LogOutput.log` 在第 11,128 行结束，未产生自身 `SessionEnd`/卸载尾段；`Client.log` 停留在 `Received initial date counter` 附近。
- UMM Guest 摘要报告退出码 `-805306369`，与用户说明的内存问题一致；本包无插件致命异常证据。
- Host 摘要退出码 0，Host 日志出现两个 `SessionEnd outcome=Success cleanup=complete`，最后一轮 Host 权威释放调度完整。
- 因此 Guest 闪退只造成客机本地日志收尾缺失，不推翻闪退前已完成的功能流程，也不破坏 Host 权威侧 Collision/receipt 证据。

## 5. 合并收口判定

1. **09-R1 诊断领域播报：PASS。** Collision 已由 `[LifecycleObs]` 真实播报，`domain=Collision` 与 `authority=Collision` 同行可见。
2. **Collision Acquire/Release/Receipt：PASS。** 343 次执行成功、0 次 Collision rejection；196 条 Release 带完整 receipt 双侧代次。
3. **旧 Writer 退役：PASS。** 仅退役声明，未发现实际旧路径生产写入。
4. **本轮功能体验：PASS（用户人工确认）。** 碰撞、门动画与门碰撞正常；Guest 闪退发生在流程完成后，归因内存问题。
5. **Reentry 证据：合并通过。** 既有 1H2G/第二轮专测证据继续有效；本轮不伪造新的自然 Reentry 行。
6. **陈旧 receipt：保留说明，不作为用户流程失败。** 本轮未自然触发拒绝；09-R1 自动化已覆盖真实身份门拒绝和零原生写入，正常会话清理不必产生自然 stale-receipt 行。

**最终 Runtime 判定：PASS。** 本轮完成 09-R1 诊断播报补证；与既有 1H2G 功能证据合并后，支持票 09、09-R1 与 P1 Collision Migration Slice 保持 `completed`。
