# 02: 主机视角看客机移动卡顿

**Label:** needs-triage

**Type:** Bug（用户口述，未经日志核对）

**Status:** Open —— 待取证

## 现象（原文照录）

> 主机看客机移动时的帧率很低，一顿一顿的，但双方延迟并不高，且双方帧率都不低，在U3DS和单人都不会发生这种问题

## 用户已排除的变量（据其自述）

- 网络延迟：不高。
- 双方本地渲染帧率：都不低（即非客户端性能问题）。
- U3DS 专用服形态：无此现象。
- 单人形态：无此现象。

现象为**主机侧观察**单向发生；客机看主机是否也卡顿，用户未提及，须补问。

## 原版机制定位（2026-09-17，IL 级核对）

用户指出 U3DS 里对远端玩家有「插帧」平滑算法。已定位到具体实现，并核对出一个关键事实：**该插值只存在于客户端，服务端没有。**

### 算法本体

`SDG.Unturned.NetworkSnapshotBuffer<T>` + `SDG.Unturned.PitchYawSnapshotInfo`（`Libs/Assembly-CSharp.dll`）：

- 8 格环形快照队列，构造参数 `(Provider.UPDATE_TIME, Provider.UPDATE_DELAY)`。
- 常数（`SDG.Unturned.Provider`）：`UPDATE_TIME=0.08f`（12.5 Hz）、`UPDATE_DELAY=0.1f`（100 ms 延迟）、`UPDATE_DISTANCE=0.01f`。
- `getCurrentSnapshot()`：落后 ≤4 格时按 `readDuration` 逐格推进读指针 → 过 `readDelay` 延迟门 → `delta = Clamp01((now - readLast)/readDuration)` → `lastInfo.lerp(下一格, delta)`；落后 >4 格直接跳到最新（追赶）。
- `PitchYawSnapshotInfo.lerp` = 位置 `Vector3.Lerp` + 朝向 `Mathf.LerpAngle`。
- 喂数据：`PlayerMovement.tellState`（L605）——由 `PlayerManager.ReceivePlayerStates`（`[SteamCall(ONLY_FROM_SERVER)]`，L70-99）逐条调用；位移 `sqrMagnitude > 256f`（>16 单位，视作瞬移）走 `nsb.updateLastSnapshot` 硬重置，否则 `nsb.addNewSnapshot` 入队。

### 关键门控：服务端没有这层插值

```csharp
// PlayerMovement.InitializePlayer（L1560-1565）
if (Provider.isServer) { ... } else { nsb = new NetworkSnapshotBuffer<PitchYawSnapshotInfo>(...); }
```
```csharp
// PlayerMovement.Update()
if (nsb != null) snapshot = nsb.getCurrentSnapshot();          // L1174-1176
...
else if (!Provider.isServer) { ...; base.transform.localPosition = snapshot.pos; }   // L1403-1420
```

`nsb` 只在 `!Provider.isServer` 时创建，且**应用插值结果的分支被 `!Provider.isServer` 门控**。

### 那主机看到的客机是怎么动的

另一条完全不同的路径：客机发的是**输入**（`WalkingPlayerInputPacket`：analog/jump/sprint/yaw/pitch/clientPosition），不是位置。`PlayerInput.ReceiveInputs`（`ONLY_FROM_OWNER`，L397）收下入队 `serversidePackets`，再由 `PlayerInput.FixedUpdate` 的**非本地玩家分支**（`if (!Provider.isServer) return;` 之后，L607+）逐包出队重放：

- `look.simulate(…)` / `stance.simulate(…)` / `movement.simulate(simulation, …, RATE)`（L653-659）
- `PlayerMovement.simulate`（L871）内按 `deltaTime = RATE = PlayerInput.RATE = 0.08f` 做 `controller.CheckedMove(velocity * deltaTime)`
- 与客机上报位置的偏差 > 2 cm（`sqrMagnitude > 0.0004f`）时回 `SendSimulateMispredictedInputs` 纠正，否则 `SendAckGoodInputs`

**机制结论：主机侧远端玩家的位移是「每收到一个输入包 = 离散推进 80 ms 一步」，包与包之间没有插值。** 渲染帧率再高，该实体也只以 12.5 Hz 跳步更新——所以主观感受是「这个对象的帧率很低、一顿一顿」，而「双方帧率都不低」「延迟不高」全部吻合：缺的是包间平滑，不是带宽或延迟。

### 为什么 U3DS / 单人没有

- U3DS 是**无头**的：服务端根本没人看画面，vanilla **从不需要**给服务端做插值——不是漏做，是场景不存在。
- 单人没有远端玩家。
- 客机看主机/其他客机是平滑的：客机侧 `nsb` 存在且有插值——这正好解释本条现象为什么是**单向**的。

### 对定性的影响

本条应从「疑似插件回归」改判为**listen-host 固有形态缺口**：原版从未覆盖「服务端要在本地渲染远端玩家」这一场景，插件只是把该场景变出来了。是否由插件补一层主机侧平滑，需另开设计票裁决（注意 `spec.md:119` 禁全局伪造 `Dedicator.IsDedicatedServer` 的铁规——**补平滑**与**伪造 dedicated 标记**不是一回事，但边界要在设计票里显式写清）。

## 取证指引

现象在主机侧，故**主机端原始包**是必需项。判读时优先对齐「主机作为 listen-host 接收并广播客机移动状态」这条链路，即位置/朝向的更新频率，而非渲染帧率。

现有相关补丁（**未判定其中是否有相关者**，仅作排查清单）：

- `Core/Patches/PlayerManagerBroadcastPatch.cs`、`PlayerManagerBroadcastDiagnosticPatch.cs`
- `Core/Patches/PlayerUpdateGuardPatch.cs`
- `Core/Patches/PlayerAnimatorSmrEnabledDiagnosticPatch.cs`、`PlayerLookAnimatorDiagnosticPatch.cs`
- `Platform/Diagnostics/Patches/PlayerManagerCullingDiagnosticPatch.cs`

> **更正（2026-09-17，用户指出）**：本节初版写了「主机侧禁用插件做无插件对照」——**该对照不存在**，是错的。保留此处记录以免重犯。

## 前提更正：本形态下不存在「摘插件」对照组

**插件即联机本身。** listen-host 下原版 `Dedicator.IsDedicatedServer=false`，`PlayerManager.Update` 的节拍条件永不成立、`sendPlayerStates` 从不被调用——这正是 [`PlayerManagerBroadcastPatch`](../../../Core/Patches/PlayerManagerBroadcastPatch.cs) 的 P0-S1 transpiler 存在的理由（见该文件头注释）。摘掉插件不是「回到原版态」，而是**联机不复存在**，无法构成对照臂。故本条只能用在联机存活前提下可做的切分。

## 可用切分（按性价比排序）

1. **日志锚点（最省，现有包即可——先做这个）**：主机包搜 `护栏触发` 与 `防御性护栏`（告警原文在 `Platform/Host/PlayerInitializationTracker.cs:210` / `:193`）。
   `PlayerUpdateGuardPatch` 会对**非本地玩家**（即客机在主机侧的 Player）短路 `PlayerMovement.Update` / `PlayerLook.Update` / `PlayerInput.FixedUpdate` / `PlayerStance.Update`，条件是 `PlayerInitializationTracker` 中该实例未达 `Ready`（含 `Unknown` 的防御性短路与 `Failed`）。若客机实例被短路，主机侧该玩家的移动/视角更新逐帧被拦，与「一顿一顿」直接对应。**这是可证伪项，不是结论**：日志若无此类告警，此方向即被排除。
   - **搜中文，别搜 `[P0-E]`**：`RoleLogger.NormalizeForOutput` 的 `LegacyBracketTag` 正则（`RoleLogger.cs:41`）会匹配 `[P0-E]` 并把捕获组当空 → 整段替换为空串；落盘日志里没有这个前缀。
   - 该告警走 `Warn`（`RoleLogger.cs:85`），**不受 `Debug.VerboseDiagnostics` 影响，常开**。
   - 反向确认有坑：成功行 `Player.InitializePlayer Ready` 走 `Info`，含 `[P0-` 标记会被 `IsDiagnosticMessage` 判为诊断消息、**只在该开关为 true 时输出**。因此「日志里没有 Ready 行」**不能**反推「客机没 Ready」；要正向确认须三端先开 `VerboseDiagnostics` 再复测。
2. **方向反转**：补问「客机看主机移动」是否同样卡顿。注意两方向不同源——主机→客机额外经 P0-S2 注入（房主本地玩家状态的注入路径），不能由一侧结果直接推另一侧。
3. **静止/移动对照**：客机静止时主机看是否正常；只有移动时异常则更靠近状态更新链路。
4. **路由对照**：同一对机器分别走 Steam P2P 与 Direct-IP 局域网单端口两条**插件内**路由（客机日志锚 `[UnifiedConnect] route=`，常开），比对是否只有其中一条卡顿。无需摘插件。
5. **U3DS 基线**：用户已给（不复现），保留为本条唯一的「非本形态」对照。
6. `EnableMultiObserverShadow`（插件 cfg `Architecture` 段，默认 true）是自带的行为开关，但它是否触及玩家状态路径**本轮未核实**，列入待查、不作为可用手段。

## 待澄清

- [ ] 「一顿一顿」是位置跳变（瞬移式补步），还是动画/骨骼卡顿？
- [ ] 客机**静止**时主机看是否正常；只有移动时异常？
- [ ] 走路、跑步、跳跃、载具，哪种最明显？
- [ ] 客机看主机移动是否也有同样卡顿？（用于判定是否单向）
- [ ] 多个客机同时在场时，主机看每个客机都卡，还是只有某一个？
- [ ] 主机自己本地移动、以及主机看僵尸/动物移动是否正常？
- [ ] U3DS 对照时的客户端是否同一台机器、同一份配置？

## 备注

- 本条与「延迟高」是两件事，请勿用 ping 值作为判据；用户已明确延迟不高。
