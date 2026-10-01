# 10: Collision 代次源自举——让租约首次 Acquire 成立

**What to build:** 修正 06/08 执行路径交付中的代次源自举死锁：`LevelObjectCollisionLedger.GetGeneration` 对从未 Acquire 的区域返回 0（`RegionGeneration` 未定义），引擎以该值开票后被 `CollisionExecutionPort.CanCommit` 的 `IsDefined` 检查恒拒——实机签名 4293 条 `outcome=rejected reason=acquire-identity-rejected`、`regionGeneration=0 acquireGeneration=0`，Collision 域零租约、门从未被接管（取证：`audit/2026-09-27/Forensics-0.2.4.9-Ticket09-2051.md`）。本票让从未 Acquire 的区域首次读出**已定义**的初始代次（对齐 `ResourceRegionLifecycleAdapter.GetGeneration` 先例），使首次 Acquire 成立并照常推进代次。完成后由票 09 的诊断复测验证门区域租约成立与门碰撞跟随。

**Blocked by:** 08 会话边界原子切换并退役旧 Collision Writer（其交付中的代次接线缺陷由本票修正；不回退仓库、不改 01–08 已冻结范围；从当前 HEAD 继续改）

**Status:** ready-for-agent

- [ ] 红：CEP 接缝新增「从未 Acquire 的区域首次 Acquire 必须成功」契约并观察失败（现有 CEP 系测试均以显式 `generation:7` 开票，未覆盖自举路径——与启动装配路径同型的测试盲区）。
- [ ] 修复代次源自举：首次读出已定义初始代次，CommitAcquire 后代次照常推进；不引入第二写入路径、不复活旧 Harmony 补丁、不做运行中切流。
- [ ] `CanCommit` 身份门保持不变（sessionEpoch、`IsDefined`、`reader==ticket` 一行不动）。
- [ ] `OnLifecycleTick` 维护拍保留（租约成立后的必需执行机制，既有 411/411 门保持）。
- [ ] 全量回归绿（目标数随新契约递增）；插件与测试宿主 Release 0 error / 0 warning。
- [ ] 双次 Rebuild 身份一致 + `Verify-BuildFingerprintArtifact.ps1 -ExpectedCandidateRole Cutover` 独立核验 PASS；`git diff --check` PASS。
- [ ] Standards/Spec 双轴全新实例并行审查至 CLEAN（每轮 finding 具名修复）。
- [ ] 审计报告按带票号命名落 `audit/`，更新 audit/README.md、issue.md 前沿；票 09 维持 in-progress 不关单。
- [ ] 交付新候选并部署主机、双端哈希比对；三端指纹核验后交票 09 诊断复测（三问：门区域 Collision Acquire success、门被记进所有权、客机在需求范围内而超出房主相机剔除距离）。
