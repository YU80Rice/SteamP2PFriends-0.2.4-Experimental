# 10: 修复 Collision Acquire 代次源自举死锁（06/08 执行路径）

**What to build:** Collision 域首次 Acquire 必须成功：从未 Acquire 的区域，其代次源必须读出**已定义**的初始代次，使引擎票据能通过 `CollisionExecutionPort.CanCommit` 身份门；身份校验语义（提交时 `reader==ticket`、`IsDefined`）一行不改，不得为过门而放宽校验。红测先落：CEP 接缝补"从未 Acquire 的区域首次 Acquire 必须成功"（当前实现恒拒，红）。本票修复后票 09 的诊断复测方可继续；本票不宣称 Runtime PASS。

**实机证据（定案）：** S3 轮主机日志 4293 条 `[CollisionExecution] outcome=rejected reason=acquire-identity-rejected`（统一签名 `regionGeneration=0 acquireGeneration=0`），引擎层 `domain=Collision` 的 `LeaseAcquire` 事件为零——Collision 租约从未成立，门从未被插件接管。取证报告：`audit/2026-09-27/Forensics-0.2.4.9-Ticket09-2051.md`。

**缺陷机理（假设，待红测证实）：** 端口代次源接的是 `LevelObjectCollisionAdapter.GetGeneration`（读 Ledger），Ledger 对从未 Acquire 的区域返回 0；引擎以 `beforeAcquire=Port.ReadRegionGeneration(region)`（=0）构造票据，`CanCommit` 要求 `RegionGeneration.IsDefined`（0 未定义）→ 首次 Acquire 恒拒 → 代次永远涨不到 1 → 自举死锁。Resource 侧先例（`ResourceRegionLifecycleAdapter.GetGeneration`）不返回未定义值，故 Resource 租约成立。既有 CEP 测试均以显式 `generation:7` 开票，绕过自举路径，故 411/411 未拦截。

**Blocked by:** 无上游（缺陷由 06/08 实施引入、票 09 实机暴露）；**本票 Blocks 09**（09 的诊断复测以本票修复为前提）。

**Status:** ready-for-agent

- [ ] 红测（先于修复运行并失败）：CEP 接缝——从未 Acquire 的区域经引擎 Acquire 路径（`beforeAcquire`→票据→`TryAcquire`）首次尝试必须成功；当前实现恒拒。
- [ ] 代次源修复：Ledger/适配器 `GetGeneration` 对未初始化区域返回已定义初始代次（对齐 Resource 先例语义），或等价的会话期初始化方案；不放宽 `CanCommit` 校验。
- [ ] 全量回归 0 失败；既有门不回退（含 CEP17 维护拍转发、StaticIL 再断言结构门、REG04 注册清单契约）。
- [ ] 双次 Rebuild 身份一致 + `Verify-BuildFingerprintArtifact.ps1 -ExpectedCandidateRole Cutover` 独立核验 PASS。
- [ ] Standards/Spec 双轴全新实例并行审查至 CLEAN（审计须记录红线：身份门语义未放宽）。
- [ ] 审计报告 `RuntimeFix-0.2.4.9-Ticket10-<HHMM>.md` + `audit/README.md` 登记（落点标注 06/08 执行路径）。
- [ ] 部署候选并通知票 09 诊断复测（三问：门区域 Acquire 是否成功、门是否被记所有权、客机在需求范围内是否超出相机剔除距离）；本票不关 Collision Slice。
