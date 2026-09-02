# SteamP2PFriends 0.2.4-Experimental 架构评审

> 评审日期：2026-08-26
> 评审基线：`master` @ `2e6de00`（feat(structure): implement Ticket 05 M7 Barricade & Structure lifecycle，181/181 PASS）
> 对照源码：U3-SDK `D:\Dev\Codes\CSharp\Unturned\U3-SDK`
> 评审范围：`MultiObserver/`、`Adapters/`、Route B 审核链路、连接路由（SteamID / 直连 IP / FRP 域名）、与 U3DS 的世界同步对齐度

---

## 摘要

| 维度 | 结论 |
|---|---|
| Multi-Observer 控制面（SPI / SpatialObserverIndex / LeaseTicket） | **生产代码中零调用，全部为死代码**；181/181 测试与运行时行为无因果关系 |
| 真正生效的机制 | `*RegionSyncPatch` transpiler 组 + `ZombieRegionLifecycleAdapter`（唯一被 wire 的适配器） |
| 僵尸不刷新 | **根因未修**：`ZombieManager.respawnZombies` 的 listen-host 早退未被 patch |
| 物品/掉落物 | **已知未修**（项目注释自认）：`ItemManager.Update` L1203 despawn/respawn 门控保留 |
| 树木/物件碰撞 | 主路径有效，但存在**房主自身区域被误 disable** 的回归 |
| Route B | 逻辑成立，但存在 **fail-open**（Reject/Timeout/Revoke）、**隔离建立过晚**、**白名单开关中途关闭导致永久卡死** |
| 连接路由 | SteamID 路线**丢弃密码**；直连/DNS 路线**丢弃整个 SteamServerAdvertisement**；IPv6 整体不支持 |

---

## 一、最致命的问题：所谓「Multi-Observer 控制面」是一具空壳

### 1.1 文档描述 vs 代码事实

`docs/adr/0005-control-plane-data-plane-and-spatial-index-architecture.md`、`CONTEXT.md`、`EXPERIMENTAL-ARCHITECTURE.md` 描述的架构是：

```
SpatialObserverIndex 计算观察者空间并集
  → 广播 Enter/Exit diff
  → 颁发 LeaseTicket
  → 各 DomainAdapter 执行 OnAcquire / OnRelease
```

**这条链路在生产代码里一次都没有被执行过。**

### 1.2 证据

全仓 grep（排除 `Develop-Stage/`、`.scratch/`）：

| 组件 | 生产实例化 / 调用 | 唯一引用来源 |
|---|---|---|
| `SpatialObserverIndex` | **0** | `WhitelistTests/MultiObserver/SpatialObserverIndexTests.cs`（4 处 `new`） |
| `ZombieDomainAdapter` | **0** | 仅 `SteamP2PFriends.csproj:131` 编译项 |
| `AnimalDomainAdapter` | **0** | 仅 `.csproj:102` |
| `ResourceDomainAdapter` | **0** | 仅 `.csproj:113` |
| `ItemDomainAdapter` | **0** | 仅 `.csproj:109` |
| `BuildingDomainAdapter` | **0** | 仅 `.csproj:124` |
| `LevelObjectCollisionAdapter` | **0** | 仅 `.csproj:107` |
| `LeaseTicket` | 仅出现在上述从不被调用的 `OnAcquire/OnRelease` 形参中 | — |
| `RegisterLifecycleAdapter` / `RegisterReplicationAdapter` | **该方法根本不存在**（只存在于 `.scratch/.../spec.md:54`） | — |

### 1.3 连锁后果（死分支）

- `Adapters/Collision/LevelObjectCollisionAdapter.cs:125` `IsRemoteCollisionRequired(x,y)` 读取的 `_activeRegions` 只能由 `OnAcquire` 写入 → **恒返回 false**。
  因此 `Adapters/Collision/Patches/LevelObjectRemoteCollisionPatch.cs:198` 与 `:218` 的
  `|| LevelObjectCollisionAdapter.IsRemoteCollisionRequired(x, y)` 是死分支。

- `Adapters/Resource/ResourceRegionLifecycleAdapter.cs:189` `IsRegionActive(x,y)` 同理恒 false。
  `Adapters/Resource/Patches/LevelGroundRemoteTreeCollisionPatch.cs:79` 的第一个条件永远不成立，
  实际生效的是后半句 `LevelObjectRemoteCollisionPatch.IsRegionCovered(x, y)` —— 即**旧的私有轮询**。

  > `.scratch/map-m5-to-m8-world-sync-roadmap/ticket-02-.../ticket.md:15` 声称「彻底解决 `LevelObjectRemoteCollisionPatch` 私有轮询技术债」。
  > 实际结果是：在旧机制旁边并排放了一套没接线的新机制，旧机制继续独自承担全部工作。

- `AnimalRegionLifecycleAdapter.Tick()`（`Adapters/Animal/AnimalRegionLifecycleAdapter.cs:213`）
  与 `ResourceRegionLifecycleAdapter.Tick()`（`:281`）是空方法体 + 一行中文注释。

### 1.4 「181/181 PASS」的真实含义

M5A01–M5A10、M5S01–M5S07、M6/M7 系列测试打的全部是纯内存 `*Ledger` 类的代数性质
（generation 单调递增、租约滞回过期、epoch 隔离、断线清理）。

**这些性质从未被任何生产代码消费。**

这不是 bug，是**度量失真**——它会让路线图一直绿灯推进到 M8，而运行时行为一个字节都不会变。

### 1.5 真实生效的架构

实际跑起来的只有两套东西：

1. Harmony transpiler 组：`ItemManager` / `Resource` / `Object` / `Barricade` / `StructureManagerRegionSyncPatch`、
   `ZombieManagerP0C1SendZombieStatesPatch`、`AnimalManagerP0C2SendAnimalStatesPatch`、
   `VehicleManagerP0C1ReplicationPatch`、`ItemManagerP0B3PreGeneratePatch`
2. `ZombieRegionLifecycleAdapter` —— 唯一一个被 `Core/Lifecycle/SteamP2PFriendsPlugin.PatchRegistry.cs:1366`
   真正 wire 起来的适配器（通过 `SetRegistrationReady`）

---

## 二、公允评价：真正做对的两件事

### 2.1 `onRegionUpdated` 的单点 IL 替换 + 精确计数自检 ⭐

涉及文件：
- `Adapters/Item/Patches/ItemManagerRegionSyncPatch.cs`
- `Adapters/Resource/Patches/ResourceManagerRegionSyncPatch.cs`
- `Patches/ObjectManagerRegionSyncPatch.cs`
- `Patches/BarricadeManagerRegionSyncPatch.cs`
- `Patches/StructureManagerRegionSyncPatch.cs`

做法：不去全局伪造 `Dedicator.IsDedicatedServer`，而是精确定位 `onRegionUpdated` 中**唯一一处**
`Dedicator.IsDedicatedServer` getter 调用点，替换为
`ListenRegionSyncEligibility.IsDedicatedOrP2PRemoteRecipient(player)`，并强制：

- `replacementCount == 1`（多一个少一个都失败）
- transpiler owner 校验为本插件 HarmonyId
- 配套的 `SendRegion` / `askItems` / `askObjects` / `askStructures` / `SendResources_Write` Prefix 登记校验
- 任一失败 → `DiagnosticBuildValid = false` 熔断

这是对付 listen-host / dedicated 语义分叉的正确姿势：副作用面最小、可验证性最强。
**这套方法论应该成为全项目模板。**

### 2.2 `ZombieRegionLifecycleAdapter` 的 `isNetworked` 借出/归还 ⭐

U3-SDK `ZombieManager.cs:1448` `onBoundUpdated` 的原始行为：

```csharp
if (player.channel.IsLocalPlayer) {
    if (LevelNavigation.checkSafe(oldBound) && regions[oldBound].isNetworked) {
        regions[oldBound].destroy();          // 房主一走，僵尸就地销毁
        regions[oldBound].isNetworked = false;
    }
}
...
if (!player.movement.loadedBounds[newBound].isZombiesLoaded) {
    if (player.channel.IsLocalPlayer) {
        generateZombies(newBound);            // 只有本地玩家才「生成」
        regions[newBound].isNetworked = true;
    } else {
        SendZombiesToPlayer(...);             // 远端客机只「发送」，从不「生成」
    }
}
```

插件的修复（`Adapters/Zombie/ZombieRegionLifecycleAdapter.cs`）：

- `BeginLocalTransition`（:227）在原方法执行前把 `oldRegion.isNetworked = false`，
  使 vanilla 的 `destroy()` 条件不成立
- `CompleteLocalTransition`（:268）还原 `isNetworked` 并挂 2s 滞回租约，
  只有 `PlayerCountInRegion == 0` 且 generation/引用一致才真正 `destroy()`
- `TryAcquireForRemote`（:200）为远端客机补上 `generateZombies`
- 用 `RegionIdentity` 引用比对 + generation 双重校验防 ABA

**这是对「房主远离导致客机身边僵尸凭空消失」的正确修复。**

---

## 三、Multi-Observer 具体缺陷

### 3.1 M5 动物适配器在为一个不存在的机制建模

`Adapters/Animal/AnimalRegionLifecycleAdapter.cs` 以 `byte bound` 为键管理
「野生动物导航区域（Navmesh Bounds）的按需生成与滞回释放」。

但 U3-SDK `AnimalManager` **根本没有区域生成机制**：

- `_packs` 在 `onLevelLoaded`（`AnimalManager.cs:708`）一次性全图构建
- `SendInitialGlobalState`（`:410`）一次性全量下发所有动物
- `respawnAnimals`（`:647`）无任何 `Dedicator` 门控，listen host 正常工作
- 类中不存在任何 per-bound / per-region 的 `generate*` 方法

整个 M5 生命周期账本是把僵尸账本复制粘贴到一个**没有生命周期的领域**上。

真正的动物缺口只有 `sendAnimalStates` 的 dedicated 门（`AnimalManager.cs:1057`）——
那个已被 `Adapters/Animal/Patches/AnimalManagerP0C2SendAnimalStatesPatch.cs` 正确修复，与 M5 适配器无关。

### 3.2 两套互不兼容的 RegionKey 编码并存 ⚠️ 埋雷

| 位置 | 编码 |
|---|---|
| `MultiObserver/Spatial/SpatialObserverIndex.cs:219` `CalculateGrid2D` | `(x << 8) \| y` |
| `Adapters/Collision/LevelObjectCollisionAdapter.cs:50` `IsRegionActive(byte,byte)` | `(x << 8) \| y` |
| `Adapters/Resource/ResourceRegionLifecycleAdapter.cs:52,239,247,257,266` | `(x << 8) \| y` |
| `Adapters/Collision/Patches/LevelObjectRemoteCollisionPatch.cs:637` `EncodeRegion` | `x * Regions.WORLD_SIZE + y`（WORLD_SIZE = 64） |

目前因为 SPI 是死代码所以撞不上。一旦有人把 SPI 接线，
`LeaseTicket.RegionKey` 传进 `IsRegionCovered` 就是静默错位。

**接线之日即引爆之时。**

### 3.3 `RefreshObjectsInRegion` 会误伤房主自己的区域 ⚠️ 可复现回归

`Adapters/Collision/Patches/LevelObjectRemoteCollisionPatch.cs:589-604`：

```csharp
bool isCovered = IsRegionCovered(x, y);   // 只看「远端」覆盖，完全不看房主
for (...) {
    if (isCovered) tree.enable();
    else          tree.disable();
}
```

`IsRegionCovered`（`:211`）只查 `RemoteCoverage`，**不考虑本地玩家**。

**复现路径**：
1. 房主站在区域 R
2. 客机进入 R 的覆盖范围（R 加入 `RemoteCoverage`）
3. 客机离开 → R 进入 `ChangedCoverage`
4. `IsRegionCovered(R) == false` → 对房主脚下的树全部 `disable()`

**vanilla 不会自愈**：`LevelGround.tickRegionalVisibility`（`LevelGround.cs:1391`）
走的是 `RegionIncrementalVisibilityTracker` 的**脏区域**模型 ——
区域一旦 `NotifyRegionFinishedUpdating` 就不再重访，除非相机坐标变化
（`shouldInstantlyLoad` 要求坐标差 `sqrMagnitude >= 4`，即移动 2 个 region）。

结果：房主必须走开两个区域再回来，树木才恢复。

**修法**：`IsRegionCovered` 必须并上本地玩家的覆盖集；
或者更干脆——只做 `enable()`，把 `disable()` 完全交还给 vanilla tracker。

### 3.4 树木覆盖半径用错了模型

`LevelObjectRemoteCollisionPatch.AddCoverageAround`（`:532`）用
`LevelObjects.OBJECT_REGIONS` 作为方形半径，同时服务于**物件和树木**。

但 vanilla 树木用的是 `LevelGround.RegularTreeMaxDistance` 的**距离**模型
（`LevelGround.cs:1391` `regionTracker.MaxDistance`），
而资源同步用的又是另一套 `LevelGround.RESOURCE_REGIONS = 3`（`LevelGround.cs:109`）。

三套半径共用一个覆盖集，边界行为与 U3DS 不一致。

### 3.5 `MultiObserverShadowCoordinator` 的 fail-closed 过于粗暴

`MultiObserver/MultiObserverShadowCoordinator.cs:270-293`：

```csharp
if (observerId == 0UL || player == null || movement == null
    || transport == null || movement.loadedRegions == null
    || !activeObservers.Add(observerId)) {
    return Incomplete(result, "invalid-observer-record:index=" + index);
}
```

任何一个 `SteamPlayer` 记录不完整（玩家正在初始化时的常态）就丢弃**整批**样本。
玩家加入/离开的每一帧都会命中。

叠加 `MultiObserverShadowLedger.cs:487` 的 `Decrement` 在计数下溢时直接 `throw`
→ `HandleTickFailure` → `ShadowFaultBackoff` 指数退避最长 60s。

审计账本很容易长期处于「静默不工作」状态，而它只在首次输出一条去重日志。

### 3.6 `host-session-identity` 是一个永久静默开关

`MultiObserverShadowCoordinator.cs:106-116`：

```csharp
if (string.IsNullOrEmpty(currentSessionId)
    || string.IsNullOrEmpty(_hostSessionId)
    || !string.Equals(_hostSessionId, currentSessionId, StringComparison.Ordinal)) {
    SafeMismatch("host-session-identity", ...);
    return;                                   // 整个 reconcile 无限期挂起
}
```

`NotifyHostSessionStarted` 只在 `Host/HostManager.cs:1922` 一处调用。
任何一次错过（异常、时序竞态）都会让 M0 账本对整局游戏彻底失明，
而 `SafeMismatch` 的去重逻辑保证外部只看到一条日志。

---

## 四、与 U3DS 的对齐缺口（未发现的缺失）

以下为逐个扫描 U3-SDK 中 `Dedicator.IsDedicatedServer` 分支后得出的**尚未修复**项。

### 4.1 僵尸不刷新 —— 真正的根因还在 🔴 最高优先级

`ZombieManager.cs:1318-1326`：

```csharp
if (region.zombies.Count > 0) {
    if (!Dedicator.IsDedicatedServer) {
        if (!region.hasBeacon && Level.info.type != ELevelType.HORDE)
            return;            // ← listen host 上，非信标非 horde 区域永不重生僵尸
    }
    ...
}
```

全仓 grep：**插件没有任何一处 patch `respawnZombies`**。

现状拆解：

| 症状 | 状态 |
|---|---|
| 僵尸被房主远离而销毁 | ✅ 已修（M3 滞回租约） |
| 远端客机区域不生成僵尸 | ✅ 已修（`TryAcquireForRemote`） |
| 僵尸位置不同步到客机 | ✅ 已修（`ZombieManagerP0C1SendZombieStatesPatch`） |
| **僵尸被打死后永不重生** | ❌ **未修，vanilla 原样** |

**修法**：与 `ItemManagerRegionSyncPatch` 同款的单点 transpiler，
把 `respawnZombies` 里那个 `Dedicator.IsDedicatedServer` 调用点替换为
`ListenRegionSyncEligibility.IsDedicatedOrP2PHost()`。

### 4.2 掉落物不消失、物资不再生 —— 项目自己写在注释里承认了 🔴

`ItemManager.cs:1200-1206`：

```csharp
// Only despawn/respawn on servers,
// as singleplayer generates batches of items when you enter the area
if (!Dedicator.IsDedicatedServer) {
    return;               // 之后是 despawnItems() / respawnItems()
}
```

`Patches/ItemManagerP0B3PreGeneratePatch.cs:66` 的注释：

> 「不修改 L1203 OnUpdate despawn/respawn 门控（`!IsDedicatedServer` 提前 return 保留）」

后果：
- 地上的掉落物永不清理 → 长局必然内存/性能劣化
- 刷新点的物资永不再生 → 一次搜刮完就是死图

与 4.1 是同一类问题、同一种修法。

### 4.3 主机侧的 tick 切片差异（性能） 🟡

| 位置 | dedicated | listen host |
|---|---|---|
| `ZombieManager.cs:1757` `Update` | 每帧切 50 个僵尸 | `start = 0; end = tickingZombies.Count` —— **全量** |
| `AnimalManager.cs:1019` `Update` | 每帧切 25 个动物 | 全量 |

单机时区域少无所谓；接了 3–4 个客机、多个区域同时 acquire 之后，
房主帧率会被这两个全量循环吃掉。

**M3 让更多区域同时「活着」，等于放大了这个缺陷** ——
这是 Multi-Observer 带来的、尚未被评估的二阶成本。

### 4.4 载具物理不在 listen host 跑 🟡

`VehicleManager.cs:2852`：

```csharp
if (Dedicator.IsDedicatedServer) {
    foreach (InteractableVehicle vehicle in vehicles) {
        ...
        vehicle.OnUpdate(deltaTime);
    }
}
```

整个载具 `OnUpdate` 循环包在 dedicated 分支里。M8 载具域尚未开始，先记录。

---

## 五、Route B 审核机制的时序问题

### 5.1 白名单在「启用」时反而失效（设计前提，需明示）

- `Host/P2PApprovalManager.cs:29` `IsActiveP2PHost` 要求 `Provider.isWhitelisted == true`
- `Patches/Patch_ServerConnectValidation.cs:20-21` 在这个条件下把
  `SteamWhitelist.checkWhitelisted` 无条件改成 `true`

**净效果：开了白名单 == 没有白名单。**

这是 Route B 的设计前提（先放行握手、再世界内隔离），但必须明确写进用户文档 ——
房主不能以为勾了白名单就安全。

### 5.2 `onServerConnected` 是 accept 流程的最后一步，隔离建立得太晚 🔴

U3-SDK `Provider.cs:4876-4966` 的实际顺序：

```
addPlayer()                          ← Player GameObject 创建，加入 Provider.clients
  → PlayerConnected 广播（双向）
  → Accepted 消息
  → SendInitialGlobalState(newClient) ← 全图 barricade / structure / vehicle / object
  → newClient.player.InitializePlayer()
  → SendInitialPlayerState × N       ← 所有玩家位置与外观
  → onServerConnected?.Invoke()      ← ★ 隔离在这里才建立
```

后果：

1. **信息面已完全泄露**。未审核的陌生人在被标记为 Pending 之前，
   已经拿到了完整的世界快照。同一调用栈内他发不出 RPC，所以行为面安全；
   但 30 秒后踢掉也追不回来。

2. `Pending` 在 `InitializePlayer()` **之后**写入，意味着 `InitializePlayer` 触发的
   区域生成、`AuthoritativeItemGenerationGatePatch`、
   `ZombieRegionLifecycleAdapter.TryAcquireForRemote` 全部把这个未审核者当成合法观察者，
   为他生成实体、颁发区域。

### 5.3 Reject / Timeout / Revoke 三条路径都是 fail-open 🔴

三处都是**先摘 Pending，后 kick**：

| 位置 | 代码 |
|---|---|
| `P2PApprovalManager.cs:341` `RejectPlayer` | `if (... \|\| !Pending.TryRemove(...)) return false;` 然后 `SafeKick` |
| `P2PApprovalManager.cs:405` `Tick`（超时） | `if (now < deadline \|\| !Pending.TryRemove(...)) continue;` 然后 `SafeKick` |
| `P2PApprovalManager.cs:383` `RevokePlayer` | 白名单移除后 `SafeKick` |

`Provider.kick` 本身是同步的，但内部
`if (findClientForKickBanDismiss(steamID, out removeClient, out removalIndex) == false) return;`
—— **找不到就静默返回**。而 `SafeKick`（`:503`）把所有异常吞掉。

所以只要 kick 因任何原因没生效（玩家正在重连、`playerID` 不匹配、异常被吞），
这个人就**从 Pending 里出来了但还连着** ——
此后 `P2PQuarantineActionGatePatch.ShouldBlock` 返回 false，所有 RPC 门全开。
而 `RejectPlayer` 依然 `return true` 并回报「已拒绝并断开」。

**修法**：kick 成功之后才摘 Pending；或引入终态 `Denied`，
`ShouldBlock` 对 `Denied` 同样阻断，直到 `Provider.onServerDisconnected` 确认。

### 5.4 白名单开关中途关闭 = 待审玩家永久卡死 🔴

- `Tick()`（`:400`）、`ApprovePlayer`（`:292`）、`RejectPlayer`（`:336`）
  全都 `if (!_runtime.IsActiveP2PHost) return / return false`
- 而 `IsActiveP2PHost` 依赖 `Provider.isWhitelisted`
- 但 `P2PQuarantineActionGatePatch.ShouldBlock`（`:167`）**不看** `isWhitelisted`，
  只看 `IsP2PHostMode && Provider.isServer && IsPending`

房主中途关掉白名单后：

| 主体 | 状态 |
|---|---|
| 待审玩家 | 仍被完全冻结、无敌、不能交互 |
| 房主「允许」 | 失败：「当前不是活动 P2P 房主」 |
| 房主「拒绝」 | 同上 |
| 30s 超时 | 永不触发（`Tick` 已 return） |

**只能重启游戏。**

### 5.5 隔离信号用了全量覆写，且不重申 🟡

`P2PApprovalManager.cs:437-459` `SetQuarantineSignal`：

```csharp
EPluginWidgetFlags flags = player.pluginWidgetFlags;
EPluginWidgetFlags desired = enabled ? flags | QuarantineSignalFlag
                                     : flags & ~QuarantineSignalFlag;
player.setAllPluginWidgetFlags(desired);
```

问题：
- 任何其他代码（其他插件、vanilla 的重生/生命周期重置）调用
  `setAllPluginWidgetFlags` 都会把隔离位冲掉
- 没有任何周期性重申
- `QuarantineSignalMask = 0x80000000`（`:98`）是抢占的未定义位，
  将来 vanilla 一旦占用即冲突

（缓解：真正的权威门是服务端的 `ContextPrefix` / `OwnerPrefix` / damage guard，
这个位只是客机侧的预测抑制提示。）

### 5.6 每个游戏 RPC 都走一次未缓存反射 🟡 性能

`Adapters/Security/Patches/P2PQuarantineAdmissionPatches.cs:155-165`：

```csharp
private static SteamPlayer ExtractOwner(object instance) {
    PropertyInfo property = AccessTools.Property(instance.GetType(), "player");
    Player player = property?.GetValue(instance, null) as Player;
    return player?.channel?.owner;
}
```

这跑在**每一个** `ONLY_FROM_OWNER` 的 `Receive*` 上，
而 `RegisterManual`（`:31-58`）扫的是整个 Assembly-CSharp（上百个方法）。
每包一次 `GetProperty` + 反射 `GetValue`，装箱、无缓存。

**修法**：在 `RegisterManual` 阶段按 `DeclaringType` 预编译成
`Dictionary<Type, Func<object, Player>>`。

### 5.7 `ApprovePlayer` 白名单已落盘却返回 false 🟡

`P2PApprovalManager.cs:303-317`：

```csharp
if (!_whitelist.TryAdd(steamId, ApprovedTag, out feedback)) { ... return false; }
Pending.TryRemove(steamId.m_SteamID, out _);
SteamPlayer player = FindClient(steamId.m_SteamID);
if ((player == null && _testSignalCallback == null) || !SetQuarantineSignal(steamId, player, false)) {
    SafeKick(...);
    feedback = "白名单已写入，但解除隔离信号失败；已要求客机重连";
    return false;                       // ← 白名单已持久化，却报 false
}
```

调用方看到 false 会以为「批准失败」，但白名单**已经持久化** ——
下次重连直接 Trusted。语义不一致，UI 会误导房主。
同时 `TryAdd` 的原始 `feedback` 被覆盖。

### 5.8 容量检查在错误的位置 🟡

`CanPermitHandshake`（`:150`）不检查 `MaxPendingEntries`。

16 个待审名额满了，第 17 个人仍然走完完整握手、进世界、
拿到全量世界快照（见 5.2），然后才被踢（`:267-272`）。

容量应该在握手期就拒绝。

---

## 六、连接路由：SteamID / 直连 IP / FRP 域名

### 6.1 SteamID 路线把密码丢了 🔴 直接导致无法联机

`Client/P2PJoinManager.cs:544`：

```csharp
CSteamID hostSteamId = new CSteamID(_targetSteamId);
ServerConnectParameters parameters = new ServerConnectParameters(hostSteamId, string.Empty);
                                                                              // ↑ 硬编码空密码
```

`Platform/UI/Patches/MenuPlayConnectP2PRoutePatch.cs` 的 `Prefix` 在 SteamP2P 分支
（`:34-57`）里**在读 `passwordField` 之前就 return 了**（`:60` 才读）。

**后果：带密码的 P2P 房间，用 SteamID 永远进不去**，
客机只会看到 `ESteamConnectionFailureInfo.PASSWORD`。

**这是一行的修复。**

### 6.2 直连 / FRP 域名路线丢弃了整个 `SteamServerAdvertisement` 🔴

vanilla 直连流程：

```
MenuPlayConnectUI.connect(SteamConnectionInfo, ...)
  → matchmakingService.connect(info)          ← A2S 查询
  → MenuPlayServerInfoUI                      ← 服务器信息界面
  → Provider.connect(parameters, advertisement, expectedWorkshopItems)
```

插件两条路线都直接 `Provider.connect(parameters, null, null)`：
- `Platform/UI/Patches/MenuPlayConnectP2PRoutePatch.cs:85`（数字 IPv4）
- `Platform/Transport/ExplicitDnsDirectIpController.cs:83`（DNS）

对照 U3-SDK `Provider.cs:1702-1745`，丢失项逐条：

| 丢失项 | 后果 |
|---|---|
| `_currentServerAdvertisement = null` | 服务器名 / 地图 / 版本 / 玩家数 / mod 列表全部不可知，`MenuPlayServerInfoUI` 不出现 |
| `Lobbies.LinkLobby(address, queryPort)` 走 fallback 分支（`:1726`） | 单端口语义下 `queryPort == connectionPort`，与 U3DS 的 `port` / `port+1` 惯例不一致，收藏夹与 Steam 大厅关联可能对不上 |
| `lag(0)`（`:1737`） | ping 基线为 0，抖动补偿初值错误 |
| `_server = parameters.steamId`（直连时为 Nil） | 依赖 `Provider.server` 的下游逻辑取到空值 |
| `waitingForExpectedWorkshopItems = null`（`:1748`） | `doServerItemsMatchAdvertisement` 直接 `return true`（`Provider.cs:656`），**「服务器要求下载未广播的创意工坊物品」这条反滥用校验被完全跳过**。创意工坊下载本身仍工作（服务端在 connect 后主动下发），但校验没了 |
| 无版本 / 地图预检 | 版本不匹配要等到握手后才以 kick 形式暴露，错误信息不友好 |

**这就是「FRP 域名解析导致丢失信息」的准确技术描述。**

### 6.3 IPv6 被两条路线整体拒绝 🟡

| 位置 | 代码 |
|---|---|
| `Platform/Transport/UnifiedJoinAddressClassifier.cs:57` | `if (delimiter != host.IndexOf(':')) return false;` |
| `UnifiedJoinAddressClassifier.cs:104` | `if (colon != value.IndexOf(':')) return false;` |
| `ExplicitDnsDirectIpController.cs:429` | `if (addr.AddressFamily != AddressFamily.InterNetwork) continue;` |

任何含多个冒号的输入直接判死。纯 IPv6 的 FRP 节点 / 隧道无法使用，
且失败提示是「未找到有效 IPv4 地址」，用户看不懂。

### 6.4 `SteamID:端口` 粘贴后直接点连接会静默失败 🟡

`MenuPlayConnectP2PRoutePatch.Prefix` 跑在 vanilla `SplitHostIntoAddressAndPort()` **之前**
（vanilla 的拆分在 `onClickedConnectButton` 内第一行）。

若用户粘贴 `76561198xxxxxxxxx:27016` 后不失焦、直接点「连接」：

1. `Classify` → `ulong.TryParse("76561198xxxxxxxxx:27016")` 失败 → `Vanilla`
2. `TryBuildDirectIpEndpoint` → 不是 IPv4 → false
3. DNS 分支 → `TryBuildExplicitDnsEndpoint` 拆掉端口后 `Classify == SteamP2P` → 主动 return false（`:111`）
4. 落回 vanilla → `TryParseHostString` → 个人账号非 `BGameServerAccount` → 拿 0 地址去连 → 失败

**一个合法的 SteamID 因为多了个端口后缀就完全连不上，且没有任何提示。**

**修法**：在 `Classify` 里先剥离 `:port` 后缀再 `TryParse`。

### 6.5 DNS 解析策略偏弱 🟡

`ExplicitDnsDirectIpController.TrySelectFirstValidIpv4`（`:414`）：

- 取第一条可用 A 记录，无多候选回退 —— 第一个 A 不可达就直接失败
- 无重试、无缓存
- `TimeoutSeconds = 5f`（`:119`）对国内运营商 DNS 偏紧
- `UnifiedJoinAddressClassifier.IsValidAsciiDnsName`（`:124`）拒绝下划线 `_`，
  某些动态 DNS / 隧道服务商的主机名会被误判为「域名或端口无效」

### 6.6 `DirectIpSinglePortQueryPortPatch` 只是显示层补丁 🟡

`Platform/Transport/Patches/DirectIpSinglePortQueryPortPatch.cs` 把 `TryGetQueryPort`
的返回值投影回 R，但注释自己写了「display-only」（`:18`）。

真正的 `Lobbies.LinkLobby` / `SetServerIsFavorited` 用的仍是 connect 时的原始值。

单端口语义与 vanilla 的 `queryPort` / `connectionPort` 二元模型之间的
阻抗失配没有系统性解决，只是在最显眼的一处贴了膏药。

---

## 七、建议的优先级

按「每单位工作量恢复的 U3DS 语义」排序：

| # | 动作 | 工作量 | 收益 | 对应章节 |
|---|---|---|---|---|
| 1 | `ZombieManager.respawnZombies` 单点 transpiler（复用 `IsDedicatedOrP2PHost`） | 小 | **僵尸真的会刷新了** | 4.1 |
| 2 | `ItemManager.Update` L1203 同款 transpiler | 小 | 掉落物清理 + 物资再生 | 4.2 |
| 3 | `ServerConnectParameters(hostSteamId, passwordField.Text)` | 1 行 | 带密码的 P2P 房可进 | 6.1 |
| 4 | Route B 三条路径改成 kick 成功后才摘 Pending，或引入 `Denied` 终态 | 小 | 消除 fail-open | 5.3 |
| 5 | `Classify` 剥离 `:port` 后缀 | 小 | 修掉一类静默连不上 | 6.4 |
| 6 | `IsRegionCovered` 并入本地玩家覆盖，或移除 `tree.disable()` | 小 | 修掉房主端树木消失回归 | 3.3 |
| 7 | `ShouldBlock` / `ApprovePlayer` / `Tick` 解除对 `Provider.isWhitelisted` 的依赖 | 小 | 消除永久卡死 | 5.4 |
| 8 | **删掉或接线** `SpatialObserverIndex` + 全部 `*DomainAdapter` + 死账本 | 中 | 恢复测试信号的可信度 | 一 |
| 9 | `OwnerPrefix` 反射改预编译委托 | 中 | 主机 RPC 热路径性能 | 5.6 |
| 10 | 直连 / DNS 路线补上 A2S 预查询（复用 `matchmakingService`），或显式记录降级项 | 大 | 恢复 advertisement | 6.2 |

### 关于第 8 项的额外说明

如果 M5–M8 的路线图是认真的，**建议先把 `SpatialObserverIndex` 真正接进 `Plugin.Update`
并让至少一个域（Resource 最合适，因为它的碰撞/采伐已有生产需求）完整走通
Acquire → Release，再往下铺新域。**

现在的做法是每个 ticket 新增一个「实现了 SPI 接口但没人调用」的账本 + 十来个单测，
路线图会一直绿灯推进到 M8，而运行时行为一个字节都不会变。

ADR-0005 描述的架构和仓库里跑的代码，目前是两个不同的系统。

---

## 附录：关键 U3-SDK 参考位置

| 文件 | 行 | 内容 |
|---|---|---|
| `Managers/ZombieManager.cs` | 1318-1326 | `respawnZombies` listen-host 早退 |
| `Managers/ZombieManager.cs` | 1448-1493 | `onBoundUpdated` 本地/远端分叉 |
| `Managers/ZombieManager.cs` | 1653-1684 | `updateRegionsAndSendZombieStates` dedicated 门 |
| `Managers/ZombieManager.cs` | 1745-1760 | `Update` tick 切片差异 |
| `Managers/ItemManager.cs` | 1200-1206 | despawn/respawn dedicated 早退 |
| `Managers/AnimalManager.cs` | 647-700 | `respawnAnimals`（无 dedicated 门） |
| `Managers/AnimalManager.cs` | 708-838 | `onLevelLoaded` 全图 pack 构建（无区域生成） |
| `Managers/AnimalManager.cs` | 1019 / 1057 | tick 切片 / `sendAnimalStates` 门 |
| `Managers/ResourceManager.cs` | 740-780 | `onRegionUpdated` step 0 / step 3 |
| `Managers/VehicleManager.cs` | 2852 | `vehicle.OnUpdate` dedicated 门 |
| `Level/LevelGround.cs` | 107-109 | `RESOURCE_REGIONS` |
| `Level/LevelGround.cs` | 1391-1455 | `tickRegionalVisibility` 脏区域模型 |
| `Level/ResourceSpawnpoint.cs` | 138-150 | `SetIsActiveInRegion` |
| `Level/ResourceSpawnpoint.cs` | 253-283 | `UpdateActive` |
| `Regions/Regions.cs` | — | `WORLD_SIZE = 64` |
| `Provider/Provider.cs` | 1702-1760 | `connect` 与 advertisement 处理 |
| `Provider/Provider.cs` | 652-680 | `doServerItemsMatchAdvertisement` |
| `Provider/Provider.cs` | 4876-4970 | accept 序列与 `onServerConnected` 时机 |
| `UI/Menu/Play/MenuPlayConnectUI.cs` | 300-425 | `onClickedConnectButton` / `RefreshServerCodeInfo` |
