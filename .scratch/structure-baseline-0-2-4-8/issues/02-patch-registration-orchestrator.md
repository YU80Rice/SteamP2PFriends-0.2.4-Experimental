# 02: Patch Registration Orchestrator 拆分

**What to build:** 让插件拥有一个只负责编排、验证和 Registration Closure 的顶层注册入口，使领域行为不再由大型 Registry 持有。

**Blocked by:** 01 / U3-SDK Registration Trace 与注册基线

**Status:** completed

- [x] 顶层注册入口只编排 Domain、Transport、Security、Diagnostics 和 Verification 注册。
- [x] 生命周期适配器与状态复制适配器分别登记，并产生可审计注册结果。
- [x] Registration Closure 后注册表不可变；重复注册、未知 Domain Id、角色缺失和顺序冲突在关闭前有明确失败结果。
- [x] Harmony target、owner、priority、执行顺序及运行时行为与迁移前保持一致（源码结构核对；Runtime 仍待验证）。
- [x] Release 构建、现有测试入口和独立审核通过。

## Ticket 02 交付证据

- 顶层入口：`Core/Lifecycle/SteamP2PFriendsPlugin.PatchRegistry.cs` 仅委托 `PatchRegistrationOrchestrator`。
- 编排模块：`Core/Registration/PatchRegistrationOrchestrator.cs` 与 `SteamP2PFriendsPlugin.PatchRegistrationOrchestration.cs`。
- 注册模块：`SteamP2PFriendsPlugin.PatchRegistrationTransportModules.cs`、`SteamP2PFriendsPlugin.PatchRegistrationSecurityModules.cs`、`SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs`、`SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs`、`SteamP2PFriendsPlugin.PatchRegistrationDomainModules.cs`、`SteamP2PFriendsPlugin.PatchRegistrationAuditModules.cs`；验证模块：`SteamP2PFriendsPlugin.PatchRegistrationHarmonyVerification.cs`、`SteamP2PFriendsPlugin.PatchRegistrationCriticalVerification.cs`、`SteamP2PFriendsPlugin.PatchRegistrationVerification.cs`。
- Closure seam：`Core/Registration/RegistrationClosure.cs`；测试：`WhitelistTests/Core/RegistrationClosureTests.cs`。
- 注册顺序以 `docs/architecture/registration-trace.md` 的 U3-SDK 追踪为准；未改变 Harmony target、owner、priority 或既有调用块内部顺序。
- Release 构建：主项目与测试项目均 `0 errors / 0 warnings`。
- 测试：现有单入口保持 `181/181 PASS`；Closure 场景并入现有 RC2 入口，未改变总数。
- `git diff --check`：通过。
- Runtime：未执行；本 Ticket 交付不等同于 SP/U3DS/P2P 功能验收。

## 验收关单(2026-09-11,用户批量批准)

- Runtime 确认:三端日志 `HarvestHookRegistration verification=ownerCount=1 matchingPatchCount=1` 与注册编排/闭合行为一致(`audit/2026-09-07/Runtime-0.2.4.8-Ticket11-2343.md`、`audit/2026-09-11/Runtime-0.2.4.8-Ticket12-0031.md`);多轮 1H+2G 无注册顺序或补丁行为回归报告。
- 边界:经 Resource 链验收(Ticket 11/12)与三端回归**间接确认**,未做逐域专项验证矩阵。
- 状态:ready-for-human → completed(用户 2026-09-11 验收)。
