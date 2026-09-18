---
title: "用户口述 BUG 批次（2026-09-17）：队伍不跨局 / 主机视角看客机卡顿 / 枪声缺失"
status: "needs-triage"
labels:
  - "needs-triage"
created_at: "2026-09-17T21:26:00+08:00"
updated_at: "2026-09-17T21:26:00+08:00"
---

# 用户口述 BUG 批次（2026-09-17）

三条**已复现、未修复、未定位**的缺陷。纯记录：不写生产代码、不判定根因、不排优先级、不认领归属票。

三条均由用户口述，**未经日志核对**，故整批只挂 `needs-triage`。`spec.md` 尚未产生——等各条完成 triage / `to-spec` 后再落，本批不预写规格。

## 记录范围与边界

- 来源：用户 2026-09-17 会话口述，形态为《未转变者》P2P listen-host（1 Host + 2 Guest）。
- 与 [`guest-join-disconnect-2026-09-17`](../guest-join-disconnect-2026-09-17/issue.md)（已 `wontfix` 结案）**无关**：那条是客户机环回 Direct-IP 误填，不是下列症状。
- 与票 05 [共享三端 Runtime 验收](../listen-host-join-routing-runtime-acceptance/issue.md) **无关**：这三条不在票 05 场景表内，票 05 不因此作废。
- 前置批次：[Listen-Host Dedicated Gate](../listen-host-dedicated-gate/issue.md)、[Join Routing](../join-routing/issue.md)。

## 三条报告（原文照录）

### 01 队伍不跨局：第二局主机掉出第一局创建的队伍

> 第一次游戏，主机创建队伍并拉队友进入，主客机处于同一队伍，箱子可正常交互；第二次游戏，主机作为队长会掉出第一次创建的队伍，导致客机失去队友，需要重新创建队伍并重新邀请

明细：[issues/01-team-membership-lost-on-second-session.md](./issues/01-team-membership-lost-on-second-session.md)

### 02 主机视角看客机移动卡顿

> 主机看客机移动时的帧率很低，一顿一顿的，但双方延迟并不高，且双方帧率都不低，在U3DS和单人都不会发生这种问题

明细：[issues/02-host-view-guest-movement-stutter.md](./issues/02-host-view-guest-movement-stutter.md)

### 03 枪声缺失

> 房主和客机听不见别人枪声，但是别人能听到房主枪声

原文存在歧义（「别人」指谁、是否三端形态、是否仅限枪声），已列入该票待澄清项；**不得**按任一读法当既成事实写进结论。

明细：[issues/03-gunshot-sfx-not-heard-by-host-and-guest.md](./issues/03-gunshot-sfx-not-heard-by-host-and-guest.md)

## 三条共用取证前置

三条都缺可用日志。进 triage 前至少需要：

- [ ] 1 份覆盖三条现象的 1 Host + 2 Guest UMM 诊断包（`LogOutput.log` / `Client.log` / `UMM-summary.txt`），并按 `docs/agents/real-machine-test-loop.md` 核对 `[BuildFingerprint]`
- [ ] 各条复现步骤：第几局、谁开房、是否重启进程、是否换主机、是否同一存档
- [ ] 01 需**跨两局**的日志（第一局建队 + 第二局掉队），单局无法判定
- [ ] 02 需主机侧原始包（现象发生在主机视角）
- [ ] 03 需先澄清方向关系再取证，否则日志无对照基线

## 已核对的事实（记录，非诊断）

- **01**：当前源码 `Core/ Adapters/ Platform/ Client/ Host/ Shared` 全树**无任何队伍写入点**——无 `groupID =` / `groupRank =` 赋值，无 `ServerAssignToGroup` / `leaveGroup` / `sendGroupInvitation` 调用。唯一触及队伍域的是 [`Core/Patches/P2PGroupStateProbe.cs`](../../Core/Patches/P2PGroupStateProbe.cs)，其注释与实现一致：**只读打点**。故 01 的取证**不需要新增探针**。
- **02 现存补丁清单**（若机制结论被证伪再回头查）：`PlayerManagerBroadcastPatch`、`PlayerManagerBroadcastDiagnosticPatch`、`PlayerUpdateGuardPatch`、`PlayerAnimatorSmrEnabledDiagnosticPatch`、`PlayerLookAnimatorDiagnosticPatch`、`Platform/Diagnostics/Patches/PlayerManagerCullingDiagnosticPatch`。
- **02 已定位到原版机制（IL 级，2026-09-17）**：远端玩家的平滑插值 `NetworkSnapshotBuffer<PitchYawSnapshotInfo>` **只在 `!Provider.isServer` 时创建**（`PlayerMovement.InitializePlayer` L1560-1565），且应用插值结果的分支同样被 `!Provider.isServer` 门控（`PlayerMovement.Update` L1403-1420）。主机侧远端玩家只能靠「每收到一个输入包 = 离散推进 80 ms 一步」的服务端重放推进（`PlayerInput.FixedUpdate` 非本地分支 + `PlayerMovement.simulate`，`RATE=0.08`）。详见 [02 票](./issues/02-host-view-guest-movement-stutter.md)。**据此本条从「疑似插件回归」改判为 listen-host 固有形态缺口**——vanilla 从未覆盖「服务端要在本地渲染远端玩家」；是否由插件补主机侧平滑需另开设计票。
- **不存在「摘插件」对照臂（2026-09-17 更正，用户指出）**：插件是本联机形态的唯一使能者——listen-host 下原版 `PlayerManager.Update` 节拍条件永不成立、`sendPlayerStates` 从不被调用（这正是 `PlayerManagerBroadcastPatch` P0-S1 transpiler 的存在理由）。摘掉插件不是「回到原版态」而是联机不复存在。后续任何条目**不得**再提「禁用插件做对照」。可用切分见 [02](./issues/02-host-view-guest-movement-stutter.md)。
- **日志归一化陷阱（跨条目适用）**：`RoleLogger.NormalizeForOutput`（`Platform/Diagnostics/RoleLogger.cs:41`）会把 `[P0-E]` / `[P0-S1]` / `[P0-S2]` 这类 `[P<数字>-…]` 前缀整段剥除——**别按源码里的前缀去 grep 落盘日志**。另：含 `[P0-` 等标记的 `Info` 行会被 `IsDiagnosticMessage` 判为诊断消息、受 `Debug.VerboseDiagnostics` 门控；而 `Warn` / `Error` 行不受该开关影响、常开。
- **03**：当前插件**无任何音频域补丁**（`PlaySound` / `PlayAudioClip` / `EffectManager` / `AudioSource` 在当前源码零命中）。另见主规格硬约束（[spec.md:119](../minecraft-lan-experience-multi-domain-sync/spec.md)）：禁止全局伪造 `Dedicator.IsDedicatedServer`，其理由之一正是会破坏主机本地音频监听器。此条**仅作取证对照点**，不构成根因结论。
