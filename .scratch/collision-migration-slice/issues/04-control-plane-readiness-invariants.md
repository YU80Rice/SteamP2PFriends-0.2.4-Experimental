# 04: 修复共享控制面的正式切换准入阻塞项

**What to build:** 正式切换所需的控制面不变量已在共享引擎上实现：坏样本不冻结其它需求、也不被当成离开而释放；会话身份可恢复或熔断；跨域异常不结束其它领域会话；retry 按事务粒度，不再区域单槽。本票只证明静态准入实现完成，不宣称 Readiness Gate 已 Runtime 通过。

**Blocked by:** 03 Resource 经领域端口迁入共享生命周期编排引擎

**Status:** ready-for-agent

- [ ] 不完整观察者样本不冻结其它观察者、区域或领域。
- [ ] 样本未知进入 Deferred Observer Demand，不触发 destructive Release。
- [ ] 僵尸或旁路异常不会通过共享外层捕获跨域结束会话。
- [ ] 会话身份能有界恢复或显式熔断；旧 epoch / generation 无写入资格。
- [ ] retry 身份与观察者贡献或聚合转换的事务粒度一致；区域单槽退出。
- [ ] 故障至少隔离到 Domain Id + Region Key + Transition。
- [ ] 异常持续、恢复和熔断具备有界心跳，不得只留一条去重日志后永久静默。
- [ ] PureMemory 与 StaticIL 证明上述不变量；BuildArtifact 绑定 0.2.4.9。
- [ ] 不切换 Collision Authority Writer；不宣称完整准入门 Runtime 通过。
