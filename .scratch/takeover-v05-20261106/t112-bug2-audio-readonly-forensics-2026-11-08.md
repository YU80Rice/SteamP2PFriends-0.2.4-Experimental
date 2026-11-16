# T112 Bug2 客机开枪声主机侧缺失：只读链路取证报告

| 项 | 值 |
|---|---|
| 任务 | t112（attempt 2，`86242117-0277-4c6d-9823-bd9a86f53a8d`） |
| 执行 | ds-analyst |
| 日期 | 2026-11-08 |
| 基线 | SteamP2PFriends 0.2.4.11 工作树（`Core/` + `Platform/`），SDK 源码副本 |
| 性质 | **只读**：未修改任何生产/测试代码，未写入 `Release/`、`publishi/`、`Develop-Stage/`、`publish/`，未运行游戏进程 |
| 证据等级 | 读码（静态）。凡标「真机」的结论均未由本报告验证 |
| 规格书 | `.scratch/takeover-v05-20261106/bug2-audio-leaf-spec.md`（211 行，sha256 `4de87f04…`） |

> 本报告只回答「代码允许什么／不允许什么」。读码能证明「不存在调用路径」，不能证明「实机一定无声」；后者见 §7。

---

## 0. 复现基线与输入

| 输入 | 路径 | sha256（前 8 位） |
|---|---|---|
| SPF 补丁 | `Core/Patches/ClientMethodLoopbackPatch.cs` | `8e25113b` |
| SPF 补丁 | `Core/Patches/SteamPlayerIsLocalServerHostPatch.cs` | `773cabb1` |
| SPF 主机门 | `Platform/Host/HostManager.cs` | `154809c3` |
| SDK | `…/Assembly-CSharp/Unturned/Useable/UseableGun.cs` | `19828172` |
| SDK | `…/Unturned/Provider/SteamChannel.cs` | `8926bdea` |
| SDK | `…/Unturned/Provider/SteamPlayer.cs` | `317ffd66` |
| SDK | `…/Unturned/Player/PlayerInput.cs` | `ec602f62` |
| SDK | `…/Unturned/Player/PlayerEquipment.cs` | `0d19497e` |
| SDK | `…/NetInvokable/ClientMethodHandle.cs` | `ae189415` |
| SDK | `…/NetInvokable/ClientInstanceMethod.cs` | `698b61c1` |
| SDK | `…/NetGen/…/UseableGun_NetMethods.cs` | `22ed5fa0` |
| SDK | `…/Unturned/Managers/EffectManager.cs` | `e516dde1` |

SDK 源码副本位于仓库内 `.scratch/bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/`（与任务给出的 `D:\Agent-工作目录\03-参考资料与SDK\网络与游戏SDK\U3-SDK` 同源；本文行号以副本为准）。程序集归属：`Assembly-CSharp` = 游戏 SDK；`SteamP2PFriends.dll` = SPF。

---

## 1. 结论速览

1. **链路**：见 §2，共 12 跳，每跳有 file:line 与程序集。
2. **主机侧从哪一跳开始不同**：`UseableGun.fire()` 的角色分支 —— `UseableGun.cs:958 if (channel.IsLocalPlayer)`。客机为 `true`，走 `1195-1198 → shoot() → playGunshot()`；主机上该枪 `channel.IsLocalPlayer == false`，整段本机播放被跳过。随后唯一的远端补偿路径 `912-914 SendPlayShoot.Invoke(..., GatherRemoteClientConnectionsWithinSphereExcludingOwner(...))` 的收件集合又被 `SteamChannel.cs:166-168` 明确排除 Host 本人。因此**分叉发生在第 5 跳（`fire()` 的 958 分支），决定性排除发生在第 6 跳（收件人计算 166-168）**。
3. **主机侧是否存在播放路径**：**普通枪（`projectile == null`）——不存在。** 唯一例外是 `projectile != null` 的枪：`fire()` 的 `1316-1332` 分支在服务端对 `!IsLocalPlayer` 调用 `project(origin, direction, …)`，而 `project()` 开头 `509-514` 会调用 `playGunshot()`，所以榴弹类武器在主机上有声。哨戒炮（`InteractableSentry`）是独立实体，不属于玩家枪链路；Mod 事件钩子（`UseableGunEventHook`）无音频且 SPF 未订阅。逐条排除见 §4。
4. **最小 Red 观察点**：首选在 `ClientInstanceMethod.Invoke(NetId, ENetReliability, List<ITransportConnection>)`（`ClientInstanceMethod.cs:110-114`）入口记录 `resolvedRecipients`（实际入参、发送前），并在 `UseableGun.playGunshot`（`UseableGun.cs:739`）入口记录 `nativeHostLocalLeafCount`；判据见 §5。该两点均无副作用、不改行为。
5. **只能真机确认的结论**：见 §7（1H+1G 与 1H+2G 分开列）。
6. **与规格书对照**：§6 逐条给出「一致 / 需修正 / UNKNOWN」。

---

## 2. 完整链路（客机扣扳机 → 主机侧潜在播放）

> 「Host 执行」列：该跳在 Host 进程上是否发生。

| # | 程序集 | 位置 | 行为 | Host 执行 |
|---|---|---|---|---|
| 1 | SDK | `PlayerInput.cs:1571` | 本地采集攻击输入 `equipment.CaptureAttackInputs(out pendingPrimaryAttackInput, …)` | 否（客机） |
| 2 | SDK | `PlayerInput.cs:1637` / `1650-1652` | 本地分支 `equipment.simulate(...)`；`consumed < buffer` 时 `equipment.tock(clock)` | 否（客机） |
| 3 | SDK | `PlayerEquipment.cs:2650-2655`、`2810-2820`；`UseableGun.cs:4547-4552`、`4431-4521` | `simulate → useable.tock → Gun.tock → tockShoot`；`4506` 射速门、`4513` 弹药门、`4520 fire()` | 否（客机先本地预测） |
| 4 | SDK | `PlayerInput.cs:1388`、`1713`、`1891`；`1397` | 客机 `SendInputs.Invoke(..., Reliable, …)`；主机 `ReceiveInputs` 收包 | 是（收包） |
| 5 | SDK | `PlayerInput.cs:1858-1868` | 服务端分支按 buffered input 逐 tick 驱动：`if (player.life.IsAlive) player.equipment.tock(clock)` | 是 |
| 6 | SDK | `PlayerEquipment.cs:2810-2820`；`UseableGun.cs:4547-4552`；`4431-4521` | 同样的 `Gun.tock → tockShoot`；`4506/4513/4518/4520 fire()` | 是 |
| 7 | SDK | `UseableGun.cs:880`、`910`、`912-914` | `fire()`；`Provider.isServer` 时广播 `SendPlayShoot.Invoke(GetNetId(), Unreliable, channel.GatherRemoteClientConnectionsWithinSphereExcludingOwner(transform.position, EffectManager.INSANE))` | 是（但集合为空，见 #8） |
| 8 | SDK | `SteamChannel.cs:160-179`（关键 `166-168`、`171-173`） | 遍历 `Provider.clients`：非 dedicated 下 `if (potentialRecipient.IsLocalServerHost) continue;` 直接跳过 Host；再排 owner 与半径外 | 是（Host 被跳过） |
| 9 | SDK | `SteamPlayer.cs:786`、`788`、`802` | `IsLocalServerHost = transportConnection != null && !Dedicator.IsDedicatedServer`；`isLocalPlayer = newPlayerID.steamID == Provider.client`；写回 `SteamChannel.IsLocalPlayer` | 是（Host 的 SteamPlayer 为 true；客机的为 false） |
| 10 | SDK | `ClientInstanceMethod.cs:110-114` | `Invoke(NetId, reliability, List<ITransportConnection>)` → 写头 → `ClientMethodHandle.SendAndLoopbackIfAnyAreLocal` | 是（空列表时什么都不发） |
| 11 | SPF + SDK | `ClientMethodLoopbackPatch.cs:376-469`（含 `395-408`、`424-433`、`443-459`、`460-466`）；`ClientMethodHandle.cs:86-126` | SPF 替换原版派发：只有 `TransportConnection_Loopback` 才 `shouldLoopback`，其余逐个 `tc.Send`；空列表记 `no-op` 并返回 | 是（空列表 → 静默） |
| 12 | SDK | `ClientMessageHandler_InvokeMethod.cs:14-40`；`UseableGun_NetMethods.cs:132-157`；`UseableGun.cs:867-875`、`780-785`、`739-765`、`763` | 收端 `ReadMessage → readMethod → ReceivePlayShoot`（需 `IsEquipAnimationFinished`）→ `shoot()` → `playGunshot()` → `gunshotAudioSource.PlayOneShot(clip)` | 仅在收到 RPC 时；本链路集合为空，故不发生 |

补充（音源存在性）：`UseableGun.cs:3269-3292` 在 `!Dedicator.IsDedicatedServer` 时为每把枪建 `AudioSource`；`IsLocalPlayer` 建在玩家物体上（`3271-3275`），远端建在 `thirdModel` 上（`3276-3278`），并设置 `spatialBlend=1`、Custom rolloff（`3282-3290`）。**主机上客机那把枪的 AudioSource 是存在的**——缺的不是 source，而是调用者。

---

## 3. 分叉点：为什么主机侧两条路都走不到

客机路径（Host 进程上的同一函数）：

```
fire()                                   UseableGun.cs:880
  ├─ 958  if (channel.IsLocalPlayer)      // 客机 true → 960-1199
  │        └─ 1195-1198  shoot()          //   → 780-785 → playGunshot() → 763 PlayOneShot
  └─ 912-914 SendPlayShoot.Invoke(..., gather(...))   // 客机不是 server，不执行
```

主机路径：

```
fire()                                   UseableGun.cs:880
  ├─ 910  if (Provider.isServer)          // true
  │        └─ 912-914 收件集合 = gather(...)  // SteamChannel.cs:166-168 跳过 Host，171-173 跳过 owner
  │            空列表 → ClientInstanceMethod.Invoke → SPF 376-469 → 无 Send、无 Loopback
  ├─ 916-920 if (!channel.IsLocalPlayer) InvokeModHookShotFiredEvents()  // 只有事件，无音频
  ├─ 958  if (channel.IsLocalPlayer)      // 主机上 false → 整段本机播放被跳过
  ├─ 1201-1218 服务端对 !IsLocalPlayer 扣枪管耐久
  └─ 1222+ 弹道（普通枪走 1238-1249；projectile 才走 1316-1332 → project() → 509-514）
```

**最早分叉点 = `UseableGun.cs:958`**（角色分支：客机进本机播放，主机不进）。**排除性证据 = `SteamChannel.cs:166-168`**（即使广播路径存在，Host 也被显式 `continue` 跳过）。两者共同导致：主机既没有本机叶调用，也不在远端收件集合内。

SPF 是否改变了这一点？`ClientMethodLoopbackPatch.cs:376-469` 只在「有连接」时决定是否回环；`460-466` 对空列表直接 `no-op`，不做本地回放。原版 `ClientMethodHandle.cs:86-126` 的 `shouldLoopback` 也只会在遍历到至少一个连接时置 true（`108-115` 的 `break` 位于循环体内），空列表同样不回环。**因此在 2 端形态下，SPF 与 vanilla 对该 RPC 的行为相同：都不产生主机侧播放。** SPF 的差异只在「收件集合非空且包含远端连接」时体现（vanilla 会把非 dedicated 连接一律当本地并 `break`，SPF 改为按连接类型逐个 `Send`）——这属于 3 端路由差异，不是 2 端缺声的原因。

---

## 4. 主机侧播放路径逐条排除

| 候选路径 | 位置 | 能否播放客机普通枪声 | 依据 |
|---|---|---|---|
| 本机 owner 音频叶 | `UseableGun.cs:958 → 1195-1198 → 780-785 → 763` | **否** | `channel.IsLocalPlayer` 在主机上为 false（`SteamPlayer.cs:788/802`） |
| 远端 `SendPlayShoot` RPC | `UseableGun.cs:912-914 → SteamChannel.cs:160-179 → ClientInstanceMethod.cs:110-114 → … → 867-875` | **否** | 收件集合被 `166-168` 排除 Host；2 端时集合为空 |
| `project()` 内置播放 | `UseableGun.cs:1316-1332 → 1326 → 509-514` | **是（仅 projectile!=null）** | `1316 if (!channel.IsLocalPlayer)` 后 `1326 project(...)`；`509-514` 无条件 `playGunshot()` |
| 近战 / 命中 / 消耗品 | 另行实现（spec §3 引用） | 不适用 | 属其它 Bug 范围；本报告未展开 |
| 哨戒炮 | `InteractableSentry.cs:22/114-133/269/290-300` | 否（独立实体） | 与玩家 `UseableGun` 无关 |
| Mod 事件钩子 | `UseableGunEventHook.cs`（`OnShotFired` 等 UnityEvent） | 否 | 仅事件、无音频；SPF 全库无 `OnShotFired` 订阅 |
| 音频池 / EffectManager | 全 SDK `PlayOneShot` 仅 `UseableGun.cs:763` 一处针对枪 | 否 | 无第二条枪声提交路径 |

**结论：普通枪（`projectile == null`，含霰弹）在主机侧不存在任何播放路径；`projectile != null` 的枪是唯一例外。**

---

## 5. 最小可自动化 Red 观察点

原则：只读、无副作用、不改行为；不伪造 Red；缺证据就记 UNKNOWN。

### 5.1 推荐观测点（按优先级）

| 编号 | 入口/出口 | 记录内容 | 为什么最小 |
|---|---|---|---|
| **R1** | `ClientInstanceMethod.Invoke(NetId, ENetReliability, List<ITransportConnection>)` 入口（`ClientInstanceMethod.cs:110-114`） | `methodIndex/方法名`、`netId`、`reliability`、`resolvedRecipients`（入参 List 的 steamID 快照）、`hostSteamId = Provider.client`、`hostIncluded`、`ownerIncluded`、`recipientCount`、`frame`、`time` | 所有 `ClientInstanceMethod` RPC 的唯一入口；`resolvedRecipients` 就是「实际 Invoke 入参」；读取 List 不改变发送行为 |
| **R2** | `UseableGun.playGunshot()` 入口（`UseableGun.cs:739`） | `netId`、`isLocalPlayer`、`ownerSteamId`、`clipName`、`clipNull`、`callerKind`（由 R1 关联） | 直接量化「主机本地叶是否被调用」（`nativeHostLocalLeafCount`） |
| **R3** | `UseableGun.PlayOneShot` 提交点（`UseableGun.cs:763`） | 同上 + `volume/pitch/maxDistance` | 区分「调用到叶」与「叶真正提交给音频引擎」（`nativeHostActualSubmitCount`） |
| R4（可选） | `SteamChannel.GatherRemoteClientConnectionsWithinSphereExcludingOwner` 出口（`SteamChannel.cs:160-179`） | 返回 List 的 steamID 快照、Host 是否在其中 | 分离「helper 返回集合」；本版本中它与 R1 的入参是同一 List 实例（SPF 不修改它，只读），两快照当前恒等，见 §6 第 6 条 |

### 5.2 Red 判据（可自动化断言）

一次「客机普通枪、服务端实际执行了 `fire()`」的样本满足以下全部条件时，应判 Red：

```
captureComplete
&& successfulRemoteOrdinaryFire          // 由 R2/R3 与 fire 服务端分支关联
&& hostInRadius                          // gather 的距离条件在 R4 复算
&& !hostInResolvedRecipients             // R1
&& nativeHostLocalLeafCount == 0         // R2
&& nativeHostActualSubmitCount == 0      // R3
```

正控（必须同时采集，防止把「本来就该排除」误判为 Bug）：Host 自己开枪 → `hostInResolvedRecipients` 也应为 false（owner 排除），但 `nativeHostLocalLeafCount ≥ 1`；若正控叶计数为 0，说明观测点本身失效，先停。

反例（不得判 Red）：`IsEquipAnimationFinished == false` 导致的收端未播放（`UseableGun.cs:873`）；距离/遮挡/rolloff；`hostInRadius == false`。

### 5.3 自动化边界

R1–R3 是纯读探针，可用 Harmony Postfix/Prefix 实现，且不需要改任何既有分支；「自动跑完 10 发并断言」仍需要一个能驱动 1H+1G 的测试编排（Unturned 无 headless 客户端），因此**探针可自动化，端到端用例当前不可全自动**——这一点与规格书 §7.1/§7.3 的判断一致。

---

## 6. 与 `bug2-audio-leaf-spec.md` 逐条对照

| # | 规格书条目 | 判定 | 依据 / 需要修正处 |
|---|---|---|---|
| 1 | §1 唯一确认缺陷：Guest 普通开枪 → Host 无枪声；其他声音正常；Guest↔Guest UNKNOWN | **一致** | 读码：普通枪在 Host 上无叶调用、无 RPC 收件；其它声音走不同链路（spec §3 已列） |
| 2 | §3-H1「本机覆盖缺口」：remote helper 排 Host + 远端 server 不 `shoot` | **一致（机制被代码证实）** | `SteamChannel.cs:166-168` 排 Host；`UseableGun.cs:958` 在 Host 上为 false。是否**唯一**原因仍需 R1–R3 实测（见 §7） |
| 3 | §3-H2「枪专属 source/clip/curve 失效」 | **未证实 / 需降级为待排除项** | 读码显示 Host 上 source 确实创建于 `thirdModel`（`3276-3278`），clip 来自 `equippedGunAsset.shoot`（`741`），rolloff 为 Custom（`3284-3285`）。但当前根本没有调用者，**「失效」没有独立证据**；补声后仍需真机验证可听性 |
| 4 | §3-H3「收端/武器状态门」：`ReceivePlayShoot` 需实例 + `IsEquipAnimationFinished` | **一致（作为潜在门）** | `UseableGun.cs:869-875`；但它是「补声之后」才会成为约束的门，不是当前缺声的原因 |
| 5 | §4.1 `HOST_COVERAGE = hostInResolvedRecipients \|\| (nativeHostLocalLeafCount>0 && nativeHostActualSubmitCount>0)` | **一致** | 两个分量分别对应 R1 与 R2/R3，可精确实现 |
| 6 | §4.1 三份快照分离（helperReturned / resolved / applied） | **需修正** | 本版本 SPF 的 `376-469` 只读该 List、不修改，`helperReturned` 与 `resolved` 是**同一实例、内容恒等**；能独立观测的只有 `resolved`（R1）与 `applied`（SPF 实际 `tc.Send`/`InvokeLoopback` 处）。建议把三快照改成「resolved（Invoke 入口）/ applied（SPF 派发出口）/ leaf（playGunshot）」三段 |
| 7 | §4.2 P0–P6 采样点、ShotKey 七元组 | **基本一致，补充两点** | ① `P2` 的 `hostFireOrdinal` 应在 `fire()` 服务端广播点（`912`）之前分配；② `P5` 的 `Random.Range` 在 `757`，必须与 `PlayOneShot` 同段保存/恢复 `UnityEngine.Random.state`（spec §6.3 已写，读码确认位置正确） |
| 8 | §6.1 补声锚点 = `fire` 服务端成功 `SendPlayShoot` 处，须先于耐久递减，只调一次原生 `playGunshot` | **一致** | `912-914` 位于 `958` 分支前、`1201-1218` 耐久递减之前；在该点之后调用一次 `playGunshot()` 满足「先于耐久、只一次」。注意 `912` 的 `Invoke` 在空列表时是 no-op，不会二次触发 |
| 9 | §7.3 S1–S4 只能 1H+1G 真机；Guest↔Guest 需 1H+2G | **一致** | 读码无法替代听感与收件集合实测；3 端收件集合是否变化只能 1H+2G |
| 10 | spec 未覆盖：`projectile != null` 的枪在 Host 已有声（`1316-1332 → 509-514`） | **需补充** | 这是唯一现存的 Host 侧播放路径；实现补声时必须按 `projectile` 分流，避免榴弹类双声 |
| 11 | spec 未覆盖：哨戒炮 / Mod 事件钩子 | **需补充（排除项）** | `InteractableSentry` 为独立实体；`UseableGunEventHook` 无音频、SPF 未订阅 |
| 12 | spec 未覆盖：SPF `IsP2PHostMode` 门 | **需补充** | `SteamPlayerIsLocalServerHostPatch.cs:94` 仅在 `HostManager.IsP2PHostMode`（`HostManager.cs:44`）时纠正 `IsLocalServerHost`；非 P2P 主机模式下走 vanilla 值，观测点必须以 `HostManager.ShouldProcessClientHostListen()`（`1606-1614`）为前置条件 |

---

## 7. 只能由实机确认的结论

**1 Host + 1 Guest（本 Bug 报告形态）**

1. Host 侧「完全无声」而不是「音量极低/被 mixer 吞掉」——读码只能证明无调用路径。
2. `playGunshot()` 若被补调，`thirdModel` 上 source 的 Custom rolloff 在该距离是否可听、是否与 Host 本机枪声音量可比。
3. `ReceivePlayShoot` 的 `IsEquipAnimationFinished` 在 Host 上对客机武器的实际时序（装备动画是否已结束）。
4. 补声后是否出现双声/回声（Host 本机叶 + 补调叶）。

**1 Host + 2 Guests（3 端）**

5. 3 端时 Host 是否仍不在 `resolvedRecipients` 内（读码预期仍不在，但 `Provider.clients` 实际内容未实测）。
6. Guest↔Guest 是否互相能听到枪声（当前 UNKNOWN；SPF 的 `SendAndLoopbackIfAnyAreLocal` 改写只影响「本地/远端」判定，不改变集合成员）。
7. 3 端下 vanilla 与 SPF 的派发差异是否造成其它 RPC 的行为变化（本报告只审了 `SendPlayShoot` 这一条链）。

---

## 8. 复现命令与证据

```bash
cd "D:/Agent-工作目录/DevelopMyUNMultiplayerModAndModloader/SteamP2PFriends-0.2.4-Experimental"
S=.scratch/bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp

# 1) 枪声叶唯一入口
grep -rn "PlayOneShot" "$S" | grep -i gun
grep -rn "playGunshot" "$S"

# 2) 收件人排除 Host
grep -n "IsLocalServerHost" "$S/Unturned/Provider/SteamChannel.cs" "$S/Unturned/Provider/SteamPlayer.cs"

# 3) SPF 是否改过枪声叶
grep -rn "PlayOneShot\|playGunshot\|ReceivePlayShoot" Core Platform

# 4) 本报告引用的文件指纹
sha256sum "$S/Unturned/Useable/UseableGun.cs" "$S/Unturned/Provider/SteamChannel.cs" \
  "$S/Unturned/Provider/SteamPlayer.cs" "$S/NetInvokable/ClientMethodHandle.cs" \
  "$S/NetInvokable/ClientInstanceMethod.cs" "$S/NetGen/NetInvokable/UseableGun_NetMethods.cs" \
  "$S/Unturned/Player/PlayerInput.cs" "$S/Unturned/Player/PlayerEquipment.cs" \
  "$S/Unturned/Managers/EffectManager.cs" Core/Patches/ClientMethodLoopbackPatch.cs \
  Core/Patches/SteamPlayerIsLocalServerHostPatch.cs Platform/Host/HostManager.cs
```

关键 grep 结果（本次实跑）：

- `playGunshot` 仅两处调用：`UseableGun.cs:513`（`project` 开头）与 `784`（`shoot`）。
- `PlayOneShot` 全 SDK 针对枪仅 `UseableGun.cs:763` 一处。
- `shoot()` 仅两处调用：`UseableGun.cs:873`（`ReceivePlayShoot`）与 `1197`（本机 owner 分支）。
- SPF `Core/`+`Platform/` 全库无 `PlayOneShot` / `playGunshot` / `ReceivePlayShoot` 命中（只有诊断补丁提及 `GatherRemoteClientConnections` 与 `IsLocalServerHost`）。

---

## 9. 不确定性与边界

1. **SDK 副本 vs 实机 ABI**：行号以仓库内副本为准（sha 已列）；实机 `Assembly-CSharp.dll` 若版本不同，行号可能偏移，但方法名与调用关系应一致。
2. **未运行游戏**：`Provider.clients` 在 Host 上的实际成员、`IsLocalServerHost` 的运行时值、`IsEquipAnimationFinished` 时序均未实测。
3. **未穷尽第三方 Mod**：若有第三方 Mod 订阅 `UseableGunEventHook` 并自行播放，属于本报告「不存在」结论的例外；SPF 自身未订阅。
4. **只审了开枪链**：近战/命中/消耗品/脚步的收件人逻辑未逐行复核（spec 已列，本报告仅引用）。
5. **不改代码**：本报告不提供补丁实现，只给观测点与判据；实现裁决归架构师。

---

## 10. 交付清单

| 文件 | 说明 |
|---|---|
| `.scratch/takeover-v05-20261106/t112-bug2-audio-readonly-forensics-2026-11-08.md` | 本报告（只读取证） |

未修改任何生产/测试代码；未写 `Release/`、`publishi/`、`Develop-Stage/`、`publish/`。
