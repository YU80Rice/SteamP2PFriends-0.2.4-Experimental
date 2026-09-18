# 票 04 RED-A 取证（新增结构契约先红，既有门全绿）

新增 `WhitelistTests/Evidence/StaticIL/ControlPlaneReadinessStaticILContractTests.cs`（7 项准入结构契约）并注册后、生产实现落地**之前**的实测输出。用途：证明新增门禁在实现之前确实是红的，而不是事后补的绿灯。

## 输出原文（节选：表头 / 全部 FAIL / 汇总）

```
===============================================================
=== SteamP2PFriends Modular TestRunner (Target: 335 PASS) ===
===============================================================

...
  FAIL [StaticIL] Control Plane Readiness StaticIL
  FAIL [StaticIL] Readiness Retry Transaction Scope
  FAIL [StaticIL] Readiness Deferred Demand Never Releases
  FAIL [StaticIL] Readiness Session Identity Gate
  FAIL [StaticIL] Readiness Per-Record Sample Admission
  FAIL [StaticIL] Readiness Outer Catch Does Not End Session
  FAIL [StaticIL] Readiness Bounded Fault Heartbeat
...
=== Final Result: 335/342 PASS (Failed: 7) ===
```

完整日志（本地工作件，`.log` 被仓库忽略规则排除，故在此留档）：`.scratch/collision-migration-slice/evidence/ticket04-red-a.log`。

## 判读

- 335 项既有门全绿、7 项新增契约全红 → 新增门禁确实由本票引入且先红；
- 实现落地后这 7 项转绿（见 `audit/2026-09-18/Implementation-0.2.4.9-CollisionTicket04-2330.md` §3）；
- 行为面新门（ARI/SAM/SIG）锁的是本票新写的行为，落地即绿，故其红点以扰动负控制取得（见 `ticket04-perturbation.md`），未冒充「先写空实现后补」。
