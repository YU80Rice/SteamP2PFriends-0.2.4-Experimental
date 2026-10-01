# 10: Collision 代次源自举——让租约首次 Acquire 成立

**What to build:** 修正 06/08 执行路径交付中的代次源自举死锁：`LevelObjectCollisionLedger.GetGeneration` 对从未 Acquire 的区域返回 0（`RegionGeneration` 未定义），引擎以该值开票后被 `CollisionExecutionPort.CanCommit` 的 `IsDefined` 检查恒拒——实机签名 4293 条 `outcome=rejected reason=acquire-identity-rejected`、`regionGeneration=0 acquireGeneration=0`，Collision 域零租约、门从未被接管（取证：`audit/2026-09-27/Forensics-0.2.4.9-Ticket09-2051.md`）。本票让从未 Acquire 的区域首次读出**已定义**的初始代次（对齐 `ResourceRegionLifecycleAdapter.GetGeneration` 先例），使首次 Acquire 成立并照常推进代次。完成后由票 09 的诊断复测验证门区域租约成立与门碰撞跟随。

**Blocked by:** 08 会话边界原子切换并退役旧 Collision Writer（其交付中的代次接线缺陷由本票修正；不回退仓库、不改 01–08 已冻结范围；从当前 HEAD 继续改）

**Status:** implemented-pending-runtime

- [x] 红：CEP 接缝新增「从未 Acquire 的区域首次 Acquire 必须成功」契约并观察失败（CEP18/CEP19 对 HEAD 红 411/413；宿主 JIT 无法执行含 Unity extern 调用点的真实 Store.OnSessionBegin——接缝约束已具名，CEP18 用真实生产代次源 + 可证身份 Fake store，CEP19 走真实 Store.Acquire 生产提交入口）。
- [x] 修复代次源自举：`LevelObjectCollisionAdapter.GetGeneration` 读侧合成 `InitialRegionGeneration=1U`（与 Ledger 首次 CommitAcquire 落点 0+1 一致，提交后照常 +1 推进）；不引入第二写入路径、不复活旧 Harmony 补丁、不做运行中切流。
- [x] `CanCommit` 身份门保持不变（sessionEpoch、`IsDefined`、`reader==ticket` 一行未动）。
- [x] `OnLifecycleTick` 维护拍保留（CEP17 仍绿，静态 IL 契约仍绿）。
- [x] 全量回归绿：413/413 PASS（411+CEP18/19）；插件与测试宿主 Release 0 error / 0 warning。
- [x] 双次 Rebuild 身份一致（SHA-256 `F6C46B82…` / MVID `bcd76cfa…`）+ `Verify-BuildFingerprintArtifact.ps1 -ExpectedCandidateRole Cutover` 独立核验 PASS；`git diff --check` PASS。
- [x] Standards/Spec 双轴全新实例并行审查至 CLEAN（Round 1 双 CLEAN + 3 条具名气味当场收敛；Round 2 双 CLEAN；红线「身份门语义未放宽」已记录于审计）。
- [x] 审计报告 `audit/2026-10-01/RuntimeFix-0.2.4.9-Ticket10-2159.md` 落盘并登记 `audit/README.md`；issue.md 前沿已更新；票 09 维持 in-progress 不关单。
- [ ] 交付新候选并部署主机、双端哈希比对；三端指纹核验后交票 09 诊断复测（三问：门区域 Collision Acquire success、门被记进所有权、客机在需求范围内而超出房主相机剔除距离）。候选已交付（`bin/Release/SteamP2PFriends.dll`，Cutover 角色）；部署与三端核验按 `docs/agents/auto-rm-test-sop.md` 人工执行。
