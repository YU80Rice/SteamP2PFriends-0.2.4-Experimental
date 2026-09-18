# 04: 修复共享控制面的正式切换准入阻塞项

**What to build:** 正式切换所需的控制面不变量已在共享引擎上实现：坏样本不冻结其它需求、也不被当成离开而释放；会话身份可恢复或熔断；跨域异常不结束其它领域会话；retry 按事务粒度，不再区域单槽。本票只证明静态准入实现完成，不宣称 Readiness Gate 已 Runtime 通过。

**Blocked by:** 03 Resource 经领域端口迁入共享生命周期编排引擎

**Status:** implemented-pending-runtime（静态闭环：共享控制面准入不变量落地；360/360 PASS、Release 双构建 0 error/0 warning 且两次指纹一致、三门禁 PASS、扰动负控制 16 次逐项取证（4 次无咬合已具名）、双轴审查逐轮全新实例推进，最终 Spec 第 9 轮 / Standards 第 8 轮双 CLEAN（逐轮处置见审计报告 §10）；完整证据见 `audit/2026-09-18/Implementation-0.2.4.9-CollisionTicket04-2330.md`。本票不宣称 Readiness Gate 已 Runtime 通过，Runtime 归票 09）

- [x] 不完整观察者样本不冻结其它观察者、区域或领域（ARI01 + `ObserverSampleAdmission` SAM01–SAM03：逐条准入，单条坏记录只暂缓它自己；StaticIL 锁定协调器经该策略准入）。
- [x] 样本未知进入 Deferred Observer Demand，不触发 destructive Release（ARI02/ARI04 + 引擎 `DeferObserver`：贡献原样保留、零释放；StaticIL 锁定暂缓方法体内零 `OnRelease`/零需求递减）。
- [x] 僵尸或旁路异常不会通过共享外层捕获跨域结束会话（插件更新入口拆为两个独立故障边界 + `HandleTickFailure` 不再结束会话；StaticIL「Outer Catch Does Not End Session」锁定；僵尸域新增独立 `ReportTickFailure`）。
- [x] 会话身份能有界恢复或显式熔断；旧 epoch / generation 无写入资格（`SessionIdentityGate` + ARI07/ARI08：身份不确定阻断写入与破坏性释放但保留租约；窗口耗尽显式熔断、重建会话后旧代次无资格）。
- [x] retry 身份与观察者贡献或聚合转换的事务粒度一致；区域单槽退出（引擎 retry 键 = 区域 + 观察者 + 连接代次；ARI05 同区两观察者互不吞并、ARI06 陈旧代次无资格；StaticIL 锁定事务键）。
- [x] 故障至少隔离到 Domain Id + Region Key + Transition（ARI09：同域一区 acquire/Release 失败不牵动同域其它区域与另一领域、不升级成领域熔断；ARI10 熔断领域不参与新会话也不挡其它领域）。
- [x] 异常持续、恢复和熔断具备有界心跳，不得只留一条去重日志后永久静默（`BoundedHeartbeat` + ARI03/ARI10：首条 + 有界重复 + 显式终止 + 恢复闭环；StaticIL 锁定熔断心跳非一次去重）。
- [x] PureMemory 与 StaticIL 证明上述不变量；BuildArtifact 绑定 0.2.4.9（360/360 PASS、9 项新结构契约、`Verify-BuildFingerprintArtifact.ps1` PASS、两次 Rebuild 指纹一致）。
- [x] 不切换 Collision Authority Writer；不宣称完整准入门 Runtime 通过（本票零 Collision 执行类型；唯一领域注册点契约仍绿；Runtime 归票 09）。
