# 哪些控制面债当前仍违反 Collision 接线所需的不变量

- **Label**: `wayfinder:done`
- **Type**: AFK
- **Parent**: `map-collision-migration-slice`
- **Status**: Closed
- **Closed**: 2026-09-18
- **Blocked by**:
- **Created**: 2026-09-18

---

## 问题

现有控制面被评审指出至少两类债：整批 fail-closed（单条观察者记录不完整丢弃整批样本；计数下溢 throw → 指数退避），以及 `host-session-identity` 错过一次 `NotifyHostSessionStarted` 后整局失明。Resource 切片是带着这些债走通的。Ticket 12 另证实 `_acquireRetries` 单槽登记互吞。

这些是**事实调查**，不是用户选择题。本票必须对照当前生产调用图与 Runtime 审计，逐条判定下列不变量在 Collision 接线前是否已被违反、被部分修复、或仅静态可判定：

1. 单个观察者或单个区域记录异常不会撤销其它区域的有效需求；
2. Session 身份缺失或错过通知后存在可重建、重试或显式熔断路径，不能永久静默；
3. 一个领域失败不会释放其它领域的 Lease；
4. retry/compensation 以 `(domain, region, observer/session identity)` 为足够区分度，不发生单槽互吞；
5. Coordinator 重启或会话切换后不会让旧 generation 获得写入资格；
6. 故障必须可观测，且不会悄悄恢复旧私有轮询。

产出必须落到可引用的 Markdown，每条声明带回一手来源（生产文件:行、audit 报告、测试契约）。本票**不裁定**哪些债阻塞 Collision——那是「哪些控制面不变量构成 Collision 准入门」在事实到位后的 grilling。

## 笔记

- 查阅技能：research。只读，不改生产代码。
- 对照源：`Core/ControlPlane/MultiObserverShadowCoordinator.cs`、`Adapters/Resource/ResourceProductionControlSeam.cs`、`audit/2026-09-09/`、`audit/2026-09-11/`、`ARCHITECTURE-REVIEW-0.2.4-Experimental.md` §3.5/§3.6。`audit/2026-09-04/ReviewRecheck-0.2.4.8-0933.md` 停在 Ticket 11 瘫痪期，不得当作当前状态。
- 发现写入 `.scratch/map-collision-migration-slice/ticket-control-plane-debt-facts/findings.md`。

## 决议

事实调查完成，不裁定准入门。对照表与派生事实见 [findings.md](findings.md)（HEAD `4593770`）。会话身份失明与 retry 单槽仍被违反；Resource 区域隔离已部分修复；跨域 Lease 隔离与旧轮询回退仍与接线冲突。准入裁定留给「哪些控制面不变量构成 Collision 准入门」。
