# U3-SDK Registration Trace

状态：Ticket 01-04 已完成；注册编排与模块归属已接入，运行时仍待验证

## 1. 对照基线与证据范围

- 对照仓库：`D:\Agent-工作目录\U3-SDK`
- 对照 commit：`ea7b4973af5ba10f62baad2bfde36ab2e5b060eb`
- 插件分支：`codex/structure-baseline-0.2.4`
- 插件版本：`0.2.4.8` / `Experimental`
- 本记录描述“注册调用发生在哪里、依赖哪个原生生命周期位置、当前证据到哪里为止”，不把注册调用顺序误写成 Harmony 的最终执行顺序。

本次静态核对得到：

- 插件入口执行 1 次 `Harmony.PatchAll(Assembly)`；
- 当前显式 `RegisterManual` / `RegisterAtomically` 调用点共 65 个；
- Ticket 02 以 7 个 U3-SDK 阶段包裹这些调用，并由 Registration Closure 关闭适配器登记；
- Ticket 01 原始追踪快照 SHA-256：`CC91C15BFEF18403C35B3DE7DCEF69CA46ADAA43A024D98BB1F5FB3F50E4CD32`，共 71 条编号追踪行；Ticket 04 的迁移入口映射见 §1.1；
- SteamNetworkingSockets wrapper 使用 15 个 Prefix helper 调用和 2 个 Postfix helper 调用；
- 插件入口直接订阅 `Provider.onEnemyDisconnected` 与 `Provider.onClientDisconnected` 两个回调；
- Route B 的 `Provider.onServerConnected` / `Provider.onServerDisconnected` 订阅在首次成功的游戏线程 Update 中延迟安装。

### 1.1 Ticket 04 迁移入口映射

下方规范矩阵中的“源码调用位置”保留 Ticket 01 的 `PatchRegistry.cs` 行号作为历史调用
锚点；Ticket 04 后实际登记入口已经拆分到以下文件。行号锚点用于追溯原始调用顺序，
不表示仓库中仍存在旧的 `PatchRegistry.cs` 权威实现。

| Registration Trace | Ticket 04 后实际登记入口 | 责任范围 |
|---|---|---|
| `U3-REG-01-Wrapper` | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationTransportModules.cs:26` | SteamNetworkingSockets wrapper 与区域 wrapper 的手工登记 |
| `U3-REG-02-InternalDiagnostics` | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs:26` | internal NetMessages、生命周期诊断及原有诊断登记调用块 |
| `U3-REG-03-RouteB` | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationSecurityModules.cs:11` | Route B 准入、界面和命令权限登记 |
| `U3-REG-04-AssetAndAudit` | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs:9`、`Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationAuditModules.cs:26` | 资产完整性与审计修复登记 |
| `U3-REG-05-WorldSyncAndAdapters` | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDomainModules.cs:15` | 世界同步、重置回调、生命周期/复制适配器登记 |
| `U3-REG-06-Probes` | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs:311` | 世界同步 probes 及其原有登记顺序 |
| `U3-REG-07-ClosureAndVerification` | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationOrchestration.cs:20-121`、`Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationVerification.cs:42`、`Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationCriticalVerification.cs:26` | 阶段编排、Registration Closure、注册后验证 |

按矩阵 Order 的当前源码入口索引如下；这是 Ticket 04 后的权威位置，覆盖矩阵的全部 65
个显式注册单元：Orders 01-11 → `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationTransportModules.cs:26-194`；
Orders 12-57 → `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs:26-539`；
Orders 58-60 → `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationSecurityModules.cs:11-19`；
Orders 61-64 → `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationAuditModules.cs:26-240`；
Order 65 → `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDomainModules.cs:15-44`。
矩阵中的旧 `PatchRegistry.cs:<line>` 仅是 Ticket 01 的历史锚点，不能作为当前源码入口读取。

## 2. 插件实际注册调用链

以下顺序来自当前源码的调用链，而不是架构图中的理想分层：

```text
BepInEx Awake
  → 配置绑定、日志与 Control Plane 初始化
  → SteamRuntime.EnsureInitialized
  → Harmony.PatchAll(当前程序集)
  → ApplyManualWrapperPatches
  → ApplyManualDiagnosticPatches
       → RegisterWorldSyncDiagnosticPatches
  → Route B 静态准入/界面/命令补丁
  → Asset Integrity 手工补丁
  → V2 audit-fix 手工补丁
  → Session reset callback 与 Barricade lifecycle 原子注册
  → Unity log bridge / SNS probe 初始化
  → Redaction self-test
  → Route B、Unified Connect、Single-Port 注册自检
  → Critical Patch / Harmony owner / signature 自检
  → P2P lobby 与 probe 初始化
  → Provider 断开回调订阅
  → 首次成功的游戏线程 Update：Route B 生命周期回调延迟安装
  → Registration Closure 的目标状态仍待 Ticket 02 正式实现
```

### 2.1 PatchAll 注册层

`PatchAll` 在 `Awake` 中运行一次，扫描当前程序集内声明了 Harmony 特性的补丁。它负责覆盖常规可解析的静态目标，包括玩家、Provider、管理器、UI、传输和部分诊断目标；它不提供可依赖的业务顺序，也不保证泛型 `ClientStaticMethod<...>.Invoke` 等复杂目标都成功登记。

当前源码已经明确把下列类型转为手工注册或由手工注册兜底：

- internal 类型目标，例如 `NetMessages`、Provider 内部处理器及部分内部消息处理器；
- 泛型 wrapper 目标，例如资产完整性 `ClientStaticMethod<...>.Invoke`；
- 需要显式签名、owner、priority 或 Prefix/Postfix 成对验证的目标；
- 需要在特定原生生命周期前后保持相对关系的区域同步、连接和诊断目标。

因此，`PatchAll 已执行` 只能证明扫描动作发生，不能证明所有生产补丁已经登记。

### 2.2 SteamNetworkingSockets wrapper 注册

`ApplyManualWrapperPatches` 先登记 SteamNetworkingSockets 与 callback wrapper：

- `CreateListenSocketIP` Prefix；
- `CreateListenSocketP2P` Prefix；
- `AcceptConnection` Prefix/Postfix；
- `CloseConnection` Prefix；
- `SetConnectionPollGroup` Prefix/Postfix；
- `CreatePollGroup` Prefix；
- `DestroyPollGroup` Prefix；
- `ReceiveMessagesOnPollGroup` Prefix；
- `CloseListenSocket` Prefix；
- `SendMessageToConnection` Prefix；
- `ReceiveMessagesOnConnection` Prefix；
- `GetConnectionInfo` Prefix；
- `SetConnectionName` Prefix；
- 两个 `Callback<...>.CreateGameServer` Prefix。

这组目标位于 Steamworks/传输 native boundary，不对应单一 U3-SDK Manager 事件。其调用链依赖服务端/客户端传输初始化、监听 socket、连接接受、poll group 和消息收发。插件通过 Harmony owner 与目标方法反射结果核验登记；实际 native 回调时序仍属于运行时事实。

`TryManualPatch` 和 `TryManualPatchPostfix` 不是普通的无验证快捷入口，而是这组 wrapper 的实际登记器。每次调用都执行：目标类型/方法解析 → patch 方法解析 → 检查本插件同一 owner 与同一 MethodInfo 的精确重复 → `Harmony.Patch` → 读取 Harmony 元数据验证本插件 patch 已存在。当前调用清单为 15 个 Prefix 和 2 个 Postfix：

| 调用目标 | Patch 类型 | priority | 依赖/验证 |
|---|---|---|---|
| `CreateListenSocketIP`、`CreateListenSocketP2P` | Prefix | 默认 | `SteamUserP2PRedirectPatch`；helper 后验 owner |
| `AcceptConnection` | Prefix + Postfix | 默认 | Prefix/Postfix 必须分别存在；Postfix 专门由 helper 兜底 |
| `CloseConnection` | Prefix | 默认 | `SteamUserP2PRedirectPatch`；helper 后验 owner |
| `SetConnectionPollGroup` | Prefix + Postfix | 默认 | Prefix/Postfix 必须分别存在 |
| `CreatePollGroup`、`DestroyPollGroup`、`ReceiveMessagesOnPollGroup` | Prefix | 默认 | `SteamUserP2PRedirectPatch`；helper 后验 owner |
| `CloseListenSocket`、`SendMessageToConnection`、`ReceiveMessagesOnConnection` | Prefix | 默认 | `SteamUserP2PRedirectPatch`；helper 后验 owner |
| `GetConnectionInfo`、`SetConnectionName` | Prefix | 默认 | `SteamUserP2PRedirectPatch`；helper 后验 owner |
| 两个 `Callback<...>.CreateGameServer` | Prefix | 默认 | `CallbackCreateGameServerRedirectPatch`；helper 后验 owner |

上表的 `默认` 仅表示 helper 没有显式设置 priority；它不表示 Harmony 最终执行顺序已被静态推导。该执行顺序仍需读取最终 Harmony patch metadata 或通过 Runtime Evidence 验证。

### 2.2.1 显式注册元数据快照（规范矩阵）

本矩阵以插件真实调用链为 Order，而不是按文件行号或词法分组。Order 是“登记调用发生顺序”，不是 Harmony 最终执行顺序；Harmony 的最终排序仍需读取运行时 metadata。每行是一个显式 `RegisterManual` 或 `RegisterAtomically` 调用单元，并保留该单元展开后的全部目标、patch method、patch type、owner、priority、依赖和证据等级。

统一字段约定：`owner=com.yu80rice.steamp2pfriends` 表示由插件的 `HARMONY_ID` 创建的 Harmony 实例；`default` 表示源码未设置 priority；`S` 表示源码/静态登记证据，`H` 表示源码中存在 Harmony owner/signature 后验核验，`C` 表示回调登记而非 Harmony patch，`R-Pending` 表示真实目标执行和跨端行为仍需运行时证据。

| Order | 实际阶段 | 当前源码入口（历史锚点另见注释） | 精确 target 方法 | patch type / patch method | owner / priority | before/after 与独立依赖 | 证据等级 |
|---:|---|---|---|---|---|---|---|
| 01 | Wrapper/Region | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationTransportModules.cs` | `ClientMethodHandle.SendAndLoopbackIfLocal(ENetReliability,ITransportConnection,NetPakWriter)`；`SendAndLoopbackIfAnyAreLocal(ENetReliability,List<ITransportConnection>,NetPakWriter)`；`SendAndLoopback(ENetReliability,List<ITransportConnection>,NetPakWriter)` | Prefix / `SendAndLoopbackIfLocal_Prefix`、`SendAndLoopbackIfAnyAreLocal_Prefix`、`SendAndLoopback_Prefix` | com.yu80rice.steamp2pfriends / default | 无显式 before/after；依赖 `ClientMethodHandle.InvokeLoopback(NetPakWriter)` 精确解析 | S+H+R-Pending |
| 02 | Wrapper/Region | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationTransportModules.cs` | `BarricadeManager.onRegionUpdated`；`BarricadeManager.SendRegion` | Transpiler / `OnRegionUpdated_Transpiler`；Prefix / `SendRegion_Prefix` | com.yu80rice.steamp2pfriends / default | 无显式 before/after；原生区域 step 2，依赖 `PlayerMovement.onRegionUpdated` | S+H+R-Pending |
| 03 | Wrapper/Region | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationTransportModules.cs` | `StructureManager.onRegionUpdated`；`StructureManager.askStructures(ITransportConnection,byte,byte,float)` | Transpiler / `OnRegionUpdated_Transpiler`；Prefix / `AskStructures_Prefix` | com.yu80rice.steamp2pfriends / default | 无显式 before/after；原生区域 step 1，依赖 `PlayerMovement.onRegionUpdated` | S+H+R-Pending |
| 04 | Wrapper/Region | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationTransportModules.cs` | `ItemManager.onRegionUpdated`（Transpiler 目标）；`ItemManager.onRegionUpdated(Player,byte,byte,byte,byte,byte,ref bool)`（Prefix 目标）；`ItemManager.askItems(ITransportConnection,byte,byte,float)` | Transpiler / `OnRegionUpdated_Transpiler`；Prefix / `OnRegionUpdated_Prefix`、`AskItems_Prefix`；Postfix / `AskItems_Postfix`；Finalizer / `AskItems_Finalizer` | com.yu80rice.steamp2pfriends / onRegion default；ask Prefix First、Postfix/Finalizer Last | 同一 `onRegionUpdated` 的不同签名是独立 target；原生区域 step 5，依赖 `ItemObserverReplicationAdapter` 与 `ItemGenerationAuthorityAdapter` | S+H+R-Pending |
| 05 | Wrapper/Region | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationTransportModules.cs` → `Adapters/Resource/Patches/ResourceManagerRegionSyncPatch.cs` | `ResourceManager.onRegionUpdated`；`ResourceManager.SendResources_Write(NetPakWriter,byte,byte)` | Transpiler / `OnRegionUpdated_Transpiler`；Prefix / `SendResources_Write_Prefix` | com.yu80rice.steamp2pfriends / default | 无显式 before/after；原生区域 step 3，依赖 `PlayerMovement.onRegionUpdated` | S+H+R-Pending |
| 06 | Wrapper/Region | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationTransportModules.cs` | `ObjectManager.onRegionUpdated`；`ObjectManager.askObjects(ITransportConnection,byte,byte)` | Transpiler / `OnRegionUpdated_Transpiler`；Prefix / `AskObjects_Prefix` | com.yu80rice.steamp2pfriends / default | 无显式 before/after；原生区域 step 4，依赖 `PlayerMovement.onRegionUpdated` | S+H+R-Pending |
| 07 | Wrapper/Region | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationTransportModules.cs` | `LevelObject.UpdateActiveAndRenderersEnabled()`；`LevelObjects.Update()` | Postfix / `UpdateActiveAndRenderersEnabled_Postfix`、`LevelObjectsUpdate_Postfix` | com.yu80rice.steamp2pfriends / root Last；update default | 无显式 before/after；依赖 LevelObject/LevelObjects Unity update | S+H+R-Pending |
| 08 | Wrapper/Region | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationTransportModules.cs` → `Adapters/Resource/Patches/LevelGroundRemoteTreeCollisionPatch.cs` | `ResourceSpawnpoint.SetIsActiveInRegion(bool)` | Prefix / `SetIsActiveInRegion_Prefix` | com.yu80rice.steamp2pfriends / default | 无显式 before/after；依赖 Resource spawnpoint 激活 | S+R-Pending |
| 09 | Wrapper/Region | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationTransportModules.cs` → `Adapters/Resource/Patches/ResourceManagerHarvestReplicationPatch.cs` | `ResourceManager.ServerSetResourceDead(byte,byte,ushort,Vector3)`；`ServerSetResourceAlive(byte,byte,ushort)` | Postfix / `ServerSetResourceDead_Postfix`、`ServerSetResourceAlive_Postfix` | com.yu80rice.steamp2pfriends / default | 无显式 before/after；依赖 Resource mutation | S+R-Pending |
| 10 | Wrapper/Region | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationTransportModules.cs` | `BarricadeManager.dropBarricade(Barricade,Transform,Vector3,float,float,float,ulong,ulong)`；`BarricadeManager.damage(Transform,float,float,bool,CSteamID,EDamageOrigin)` | Postfix / `DropBarricade_Postfix`、`Damage_Postfix` | com.yu80rice.steamp2pfriends / default | 两个 target 在同一调用块中按源码先后登记；依赖 Barricade mutation | S+R-Pending |
| 11 | Wrapper/Region | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationTransportModules.cs` | `StructureManager.dropStructure(SDG.Unturned.Structure,Vector3,float,float,float,ulong,ulong)`；`StructureManager.damage(Transform,Vector3,float,float,bool,CSteamID,EDamageOrigin)` | Postfix / `DropStructure_Postfix`、`Damage_Postfix` | com.yu80rice.steamp2pfriends / default | 无显式 before/after；依赖 Structure mutation | S+R-Pending |
| 12 | Diagnostic/Transport | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `NetMessages.SendMessageToClient(EClientMessage,ENetReliability,ITransportConnection,ClientWriteHandler)`；`SendMessageToClients(EClientMessage,ENetReliability,List<ITransportConnection>,ClientWriteHandler)` | Prefix+Finalizer / `SendMessageToClient_Prefix`+`SendMessageToClient_Finalizer`；`SendMessageToClients_List_Prefix`+`SendMessageToClients_List_Finalizer` | com.yu80rice.steamp2pfriends / default | 无显式 before/after；依赖 internal `NetMessages.ClientWriteHandler` 反射解析 | S+R-Pending |
| 13 | Diagnostic/Transport | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `ClientMessageHandler_Verify.ReadMessage(NetPakReader)`；`ServerMessageHandler_Authenticate.ReadMessage(ITransportConnection,NetPakReader)`；`NetMessages.SendMessageToServer(EServerMessage,ENetReliability,ClientWriteHandler)`；`ClientMessageHandler_Verify.WriteEconomyDetails(NetPakWriter)` | Prefix+Finalizer / `Verify_Prefix`+`Verify_Finalizer`、`Authenticate_Prefix`+`Authenticate_Finalizer`、`SendToServer_Prefix`+`SendToServer_Finalizer`；Postfix / `Authenticate_Postfix`；Prefix / `WriteEconomyDetails_Prefix` | com.yu80rice.steamp2pfriends / default | 独立依赖：`GetVerifyTargetMethod` 解析 `ClientMessageHandler_Verify.ReadMessage(NetPakReader)`；`GetAuthenticateTargetMethod` 解析 `ServerMessageHandler_Authenticate.ReadMessage(ITransportConnection,NetPakReader)`；`GetSendAuthenticateTargetMethod` 解析 `NetMessages.ClientWriteHandler` nested type；`GetWriteEconomyDetailsTargetMethod` 解析 `WriteEconomyDetails(NetPakWriter)`；无对 Order 12 的登记先后依赖 | S+H+R-Pending |
| 14 | Diagnostic/Transport | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `ClientMessageHandler_Accepted.ReadMessage(NetPakReader)` | Postfix / `ReadMessage_Postfix` | com.yu80rice.steamp2pfriends / default | 无显式 before/after；依赖 Accepted handler internal type | S+R-Pending |
| 15 | Diagnostic/Accept | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `Provider.SendInitialGlobalState(SteamPlayer)`；`PhysicsMaterialNetTable.Send(ITransportConnection)`；`LightingManager.SendInitialGlobalState(SteamPlayer)`；`VehicleManager.SendInitialGlobalState(SteamPlayer)`；`AnimalManager.SendInitialGlobalState(ITransportConnection)`；`LevelManager.SendInitialGlobalState(SteamPlayer)`；`ZombieManager.SendInitialGlobalState(SteamPlayer)`；`Player.SendInitialPlayerState(SteamPlayer)`；`Player.SendInitialPlayerState(List<ITransportConnection>)`；`Provider.AddClientToThirdpartyAntiCheat(ITransportConnection,SteamPlayerID,SteamPlayer)` | Prefix+Finalizer / 每个 target 对应 `*_Prefix`、`*_Finalizer` | com.yu80rice.steamp2pfriends / default | 无显式 before/after；依赖 `Provider.accept` 初始状态发送链 | S+R-Pending |
| 16 | Diagnostic/Player Init | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | 15 个组件 `InitializePlayer()`：`PlayerClothing`、`PlayerInventory`、`PlayerLife`、`PlayerSkills`、`PlayerCrafting`、`PlayerStance`、`PlayerMovement`、`PlayerLook`、`PlayerInteract`、`PlayerAnimator`、`PlayerEquipment`、`PlayerInput`、`PlayerVoice`、`PlayerWorkzone`、`PlayerQuests`；`Player.InitializePlayerStart()` | Prefix+Finalizer / `Component_InitializePlayer_Prefix`+`Component_InitializePlayer_Finalizer`；`PlayerStart_Prefix`+`PlayerStart_Finalizer` | com.yu80rice.steamp2pfriends / default | 组件列表遵循源码数组；依赖 `Player.InitializePlayer` | S+R-Pending |
| 17 | Diagnostic/Reject | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `Provider.reject(CSteamID,ESteamRejection)`；`reject(CSteamID,ESteamRejection,string)`；`reject(ITransportConnection,ESteamRejection)`；`reject(ITransportConnection,ESteamRejection,string)`；`Provider.kick(CSteamID,string)`；`Provider.refuseGarbageConnection(CSteamID,string)`；`refuseGarbageConnection(ITransportConnection,string)` | Prefix / `Reject_CSteamID_Prefix`、`Reject_CSteamID_Explanation_Prefix`、`Reject_Transport_Prefix`、`Reject_Transport_Explanation_Prefix`、`Kick_Prefix`、`Refuse_CSteamID_Prefix`、`Refuse_Transport_Prefix` | com.yu80rice.steamp2pfriends / default | 每个 overload 独立解析；依赖 rejection path | S+R-Pending |
| 18 | Diagnostic/Queue | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `ClientMessageHandler_QueuePositionChanged.ReadMessage(NetPakReader)` | Postfix / `Postfix` | com.yu80rice.steamp2pfriends / default | 无显式 before/after；依赖 client loading queue | S+R-Pending |
| 19 | Diagnostic/Initial State | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `LightingManager.ReceiveInitialLightingState(uint,uint,uint,byte,byte,Guid,float,NetId,int)`；`VehicleManager.ReceiveMultipleVehicles()`；`BarricadeManager.ReceiveMultipleBarricades()`；`StructureManager.ReceiveMultipleStructures()`；`PlayerInventory.ReceiveInventory()`；`PlayerLife.ReceiveLifeStats(byte,byte,byte,byte,byte,bool,bool)`；`PlayerClothing.ReceiveClothingState()` | Prefix+Postfix+Finalizer / 各 target 的 `Prefix`、`Postfix`、`Finalizer` | com.yu80rice.steamp2pfriends / default | 7 个 target 各自独立核验；依赖 initial-state receive 链 | S+H+R-Pending |
| 20 | Diagnostic/Local Loop | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `PlayerInput.FixedUpdate()`；`PlayerMovement.simulate()`；`PlayerMovement.simulate(uint,int,bool,bool,Vector3,Quaternion,float,float,float,float,float)`；`PlayerMovement.simulate(uint,int,int,int,float,float,bool,bool,float)` | Prefix / `FixedUpdatePrefix`、`SimulatePrefix` | com.yu80rice.steamp2pfriends / default | 三个 simulate overload 独立解析；依赖 Unity local movement loop | S+R-Pending |
| 21 | Diagnostic/Disconnect | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `Provider.RequestDisconnect(string)` | Prefix+Postfix / `Prefix`、`Postfix` | com.yu80rice.steamp2pfriends / default | 无显式 before/after；依赖客户端主动断开路径 | S+R-Pending |
| 22 | Diagnostic/Visibility | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `PlayerClothing.ReceiveClothingState()`；`PlayerEquipment.ReceiveSlot()`；`ReceiveUpdateState()`；`ReceiveEquip()`；`PlayerClothing.sendUpdateShirtQuality()` | Prefix / 既有 `InitialStateReceiveDiagnosticPatch.PlayerClothingHooks.Prefix`（D-Vis-1，仅复核不新增）；`PlayerEquipmentHooks.ReceiveSlotPrefix`、`ReceiveUpdateStatePrefix`、`ReceiveEquipPrefix`；`PlayerClothingHooks.SendUpdateShirtQualityPrefix` | com.yu80rice.steamp2pfriends / default | D-Vis-1 是 existing-patch verification，不是新增 Harmony patch；依赖 clothing/equipment lifecycle | S+H+R-Pending |
| 23 | Diagnostic/Visibility | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `PlayerInput.ReceiveSimulateMispredictedInputs()`；`PlayerAnimator.ReceiveLean()`；`PlayerAnimator.ReceiveGesture()` | Prefix / `PlayerInputHooks.Prefix`、`PlayerAnimatorHooks.ReceiveLeanPrefix`、`ReceiveGesturePrefix` | com.yu80rice.steamp2pfriends / default | 无显式 before/after；依赖 remote input/animator receive | S+R-Pending |
| 24 | Diagnostic/Transport | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `SteamChannel.GetOwnerTransportConnection()` | Postfix / `SteamChannelTransportHooks.Postfix` | com.yu80rice.steamp2pfriends / default | 无显式 before/after；依赖 transport owner lookup | S+R-Pending |
| 25 | Diagnostic/Player State | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `PlayerMovement.tellState(Vector3,byte,byte)` | Prefix / `Hooks.TellStatePrefix` | com.yu80rice.steamp2pfriends / default | 无显式 before/after；依赖 player state send | S+R-Pending |
| 26 | Diagnostic/Visibility | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `PlayerAnimator.InitializePlayer()`；`PlayerAnimator.NotifyClothingIsVisible()`；`PlayerAnimator.onLifeUpdated()`；`PlayerClothing.ReceiveClothingState()` | Postfix / `Hooks.InitializePlayerPostfix`、`Hooks.NotifyClothingIsVisiblePostfix`、`Hooks.ReceiveClothingStatePostfix`；Prefix / `Hooks.NotifyClothingIsVisiblePrefix`、`Hooks.OnLifeUpdatedPrefix` | com.yu80rice.steamp2pfriends / default | 独立依赖：四个 target 必须分别由 `AccessTools.Method` 解析；同一 `NotifyClothingIsVisible` target 内仅有 Prefix→原方法→Postfix 的局部关系；无跨登记单元 before/after | S+R-Pending |
| 27 | Diagnostic/UI | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `LoadingUI.Update()` | Postfix / `Hooks.UpdatePostfix` | com.yu80rice.steamp2pfriends / default | 无显式 before/after；依赖 client loading UI | S+R-Pending |
| 28 | Diagnostic/Visibility | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `Player.InitializePlayer()`；`PlayerClothing.ReceiveClothingState()`；`Player.OnDestroy()` | Postfix / `Hooks.InitializePlayerPostfix`、`Hooks.ReceiveClothingStatePostfix`、`Hooks.OnDestroyPostfix` | com.yu80rice.steamp2pfriends / default | 无显式 before/after；依赖 clothing loading lifecycle | S+R-Pending |
| 29 | Diagnostic/Player Init | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `Player.InitializePlayer()` | Prefix+Postfix / `Hooks.InitializePlayerPrefix`、`Hooks.InitializePlayerPostfix` | com.yu80rice.steamp2pfriends / default | 无显式 before/after；依赖 Player init stage | S+R-Pending |
| 30 | Diagnostic/Unity Bridge | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | 无 Harmony target；`Application.logMessageReceivedThreaded` 回调 | Callback / `OnUnityLogForTagError` | n/a / n/a | 与 UnityLogBridge 并列订阅；依赖 Unity log callback 生命周期 | C+R-Pending |
| 31 | Diagnostic/Player State | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `PlayerMovement.tellState(Vector3,byte,byte)` | Postfix / `Hooks.TellStateCallerPostfix` | com.yu80rice.steamp2pfriends / default | 无显式 before/after；依赖 caller stack trace | S+R-Pending |
| 32 | Diagnostic/Transport | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `NetMessages.SendMessageToClient(EClientMessage,ENetReliability,ITransportConnection,ClientWriteHandler)` | Postfix / `Hooks.SendMessageToClientDeliveryPostfix` | com.yu80rice.steamp2pfriends / default | 与 Order 12 的 Prefix/Finalizer 共存；依赖 internal client writer 解析 | S+R-Pending |
| 33 | Diagnostic/Player Ready | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `Player.InitializePlayer()`；`PlayerQuests.InitializePlayer()` | Postfix / `Hooks.InitializePlayerReadyPostfix`、`Hooks.QuestsInitializePlayerPostfix` | com.yu80rice.steamp2pfriends / default | 无显式 before/after；依赖 PlayerQuests 最后组件语义 | S+R-Pending |
| 34 | Diagnostic/Player Timing | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `Player.InitializePlayer()`；`PlayerClothing.InitializePlayer()`；`PlayerMovement.InitializePlayer()`；`PlayerAnimator.InitializePlayer()`；`PlayerQuests.InitializePlayer()` | Prefix / `Hooks.InitializePlayerPrefix`；Postfix / `Hooks.ClothingPostfix`、`MovementPostfix`、`AnimatorPostfix`、`QuestsPostfix`、`InitializePlayerPostfix` | com.yu80rice.steamp2pfriends / default | 独立依赖：`RegisterOne` 对 6 个 `(targetType, InitializePlayer)` 组合分别解析；阶段关系依赖 U3-SDK `Player.InitializePlayer` 的原生组件调用顺序，而不是登记顺序或 Harmony 全局排序；无跨单元 before/after | S+R-Pending |
| 35 | Diagnostic/Player Broadcast | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `PlayerManager.Update()`；`PlayerManager.sendPlayerStates()` | Transpiler / `Hooks.UpdateTranspiler`；Prefix / `Hooks.SendPlayerStatesPrefix` | com.yu80rice.steamp2pfriends / default | 无显式 before/after；依赖 PlayerManager broadcast | S+H+R-Pending |
| 36 | Diagnostic/Visibility | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `Player.InitializePlayer()` | Postfix / `Hooks.InitializePlayerPostfix` | com.yu80rice.steamp2pfriends / default | 与其他 Player.InitializePlayer Postfix 共存；依赖 remote player state | S+H+R-Pending |
| 37 | Diagnostic/Player Broadcast | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `PlayerManager.Update()`；`sendPlayerStates()`；`ReceivePlayerStates()` | Postfix / `Hooks.UpdatePostfix`、`SendPlayerStatesPostfix`、`ReceivePlayerStatesPostfix`；Prefix / `Hooks.SendPlayerStatesPrefix`；Finalizer / `Hooks.SendPlayerStatesFinalizer` | com.yu80rice.steamp2pfriends / default | 同一 target 的 Prefix/Postfix/Finalizer 分别核验；依赖 PlayerManager broadcast | S+R-Pending |
| 38 | WorldSync/Item | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `ItemManager.onRegionUpdated()`；`dropItem()`；`askItems()`；`ReceiveItem()` Prefix+Postfix；`ReceiveItems()` | Prefix / `OnRegionUpdated_Prefix`、`DropItem_Prefix`、`AskItems_Prefix`、`ReceiveItem_Prefix`、`ReceiveItems_Prefix`；Postfix / `ReceiveItem_Postfix` | com.yu80rice.steamp2pfriends / default | 每个 identity target 独立核验；依赖 Item Manager lifecycle | S+H+R-Pending |
| 39 | WorldSync/Item | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `ItemManager.generateItems(byte,byte)` | Prefix / `Prefix` First；Postfix / `Postfix` Last；Finalizer / `Finalizer` Last | com.yu80rice.steamp2pfriends / explicit First/Last/Last | 同一 target 内 Prefix→vanilla→Postfix/Finalizer；依赖 item generation gate | S+H+R-Pending |
| 40 | WorldSync/Inventory | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `ItemManager.generateItems` Prefix/Postfix/Finalizer；`ItemManager.askItems` Prefix；`ItemManager.ReceiveTakeItemRequest` Prefix/Postfix；`PlayerInventory.ReceiveDragItem` Prefix/Postfix；`ReceiveSwapItem` Prefix/Postfix；`ReceiveDropItem` Prefix/Postfix；`ReceiveItemAdd` Prefix/Postfix；`ReceiveItemRemove` Prefix/Postfix | 对应 `GenerateItems_Prefix/Postfix/Finalizer`、`AskItems_Prefix`、`ReceiveTakeItemRequest_Prefix/Postfix`、`ReceiveDragItem_Prefix`、`InventoryOperation_Postfix`、`ReceiveSwapItem_Prefix`、`ReceiveDropItem_Prefix`、`ReceiveItemAdd_Prefix`、`ReceiveItemRemove_Prefix` | com.yu80rice.steamp2pfriends / default | 独立依赖：16 个 `(targetType,targetName,parameter array,patch MethodInfo,patch type)` 组合由 `InventoryWorldAuthorityProbe.Register` 分别解析并由 `Verify` 分别复核；另需 `WorldSyncDiagnosticCore.RegisterSessionResetCallback(ResetForSession)` 成功；与 Order 39 无登记先后依赖 | S+H+R-Pending |
| 41 | WorldSync/Resource | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` → `Adapters/Resource/Patches/ResourceManagerWorldSyncDiagnosticPatch.cs` | `ResourceManager.onRegionUpdated()`；`SendResources_Write(NetPakWriter,byte,byte)`；`ReceiveResources()` Prefix+Postfix | Prefix / `OnRegionUpdated_Prefix`、`SendResources_Write_Prefix`、`ReceiveResources_Prefix`；Postfix / `ReceiveResources_Postfix` | com.yu80rice.steamp2pfriends / default | 每个 identity target 独立核验；依赖 Resource Manager lifecycle | S+H+R-Pending |
| 42 | WorldSync/Object | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `ObjectManager.onRegionUpdated()`；`askObjects(ITransportConnection,byte,byte)`；`ReceiveObjects()` Prefix+Postfix | Prefix / `OnRegionUpdated_Prefix`、`AskObjects_Prefix`、`ReceiveObjects_Prefix`；Postfix / `ReceiveObjects_Postfix` | com.yu80rice.steamp2pfriends / default | 每个 identity target 独立核验；依赖 Object Manager lifecycle | S+H+R-Pending |
| 43 | WorldSync/Object Binary | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | Debug-only：`ObjectManager.toggleObjectBinaryState`；`ReceiveToggleObjectBinaryStateRequest` Prefix/Postfix/Finalizer；`GatherRemoteClientConnections`；`ReceiveObjectBinaryState` Prefix/Postfix/Finalizer；`LevelObject.UpdateActiveAndRenderersEnabled`；`Player.ReceiveTeleport`；`PlayerMovement.OnControllerColliderHit`；`PlayerInput.ReceiveSimulateMispredictedInputs` | 对应 `ToggleObjectBinaryState_Prefix`、`Authority_Prefix/Postfix/Finalizer`、`GatherRecipients_Postfix`、`Receive_Prefix/Postfix/Finalizer`、`Activation_Postfix`、`ReceiveTeleport_Prefix`、`ControllerColliderHit_Prefix`、`Misprediction_Prefix` | Debug: com.yu80rice.steamp2pfriends / default；Release: n/a（`#if !DEBUG` 为 release-noop） | 独立依赖：Debug 构建必须满足 12 个 target/patch identity；Release 分支只返回 `release-noop`，不登记这些 target；与其他 WorldSync 单元无登记先后依赖 | S+H+R-Pending |
| 44 | WorldSync/Vehicle | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `VehicleManager.Update()`；`sendVehicleStates()`；`spawnVehicleInternal()`；`ReceiveMultipleVehicles()`；`ReceiveVehicleStates()` | Prefix / `Update_Prefix`、`SendVehicleStates_Prefix`、`SpawnVehicleInternal_Prefix`、`ReceiveMultipleVehicles_Prefix`、`ReceiveVehicleStates_Prefix` | com.yu80rice.steamp2pfriends / default | 每个 identity target 独立核验；依赖 Vehicle Manager Update/replication | S+H+R-Pending |
| 45 | WorldSync/Animal | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `AnimalManager.Update()`；`sendAnimalStates()`；`spawnAnimal()`；`ReceiveMultipleAnimals()`；`ReceiveAnimalStates()` | Prefix / `Update_Prefix`、`SendAnimalStates_Prefix`、`SpawnAnimal_Prefix`、`ReceiveMultipleAnimals_Prefix`、`ReceiveAnimalStates_Prefix` | com.yu80rice.steamp2pfriends / default | 每个 identity target 独立核验；依赖 Animal Manager Update/replication | S+H+R-Pending |
| 46 | WorldSync/Zombie | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `ZombieManager.updateRegionsAndSendZombieStates()`；`onBoundUpdated(Player,byte,byte)`；`SendZombiesToPlayer()`；`SendZombies_Write()`；`ReceiveZombies()` Prefix+Postfix；`ReceiveZombieStates()` | Prefix / `UpdateRegionsAndSendZombieStates_Prefix`、`OnBoundUpdated_Prefix`、`SendZombiesToPlayer_Prefix`、`SendZombies_Write_Prefix`、`ReceiveZombies_Prefix`、`ReceiveZombieStates_Prefix`；Postfix / `ReceiveZombies_Postfix` | com.yu80rice.steamp2pfriends / default | 每个 identity target 独立核验；依赖 Zombie Manager Update/Bound | S+H+R-Pending |
| 47 | WorldSync/Zombie | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `ZombieManager.onBoundUpdated(Player,byte,byte)` | Prefix / `OnBoundUpdated_Prefix` | com.yu80rice.steamp2pfriends / Low | 源码要求在 Order 46 的 Zombie diagnostic Prefix 后登记；依赖同一 target 的 priority 关系 | S+H+R-Pending |
| 48 | WorldSync/Zombie | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `ZombieManager.onBoundUpdated(Player,byte,byte)` | Prefix / `OnBoundUpdated_Prefix` VeryLow；Postfix / `OnBoundUpdated_Postfix` High；Finalizer / `OnBoundUpdated_Finalizer` High | com.yu80rice.steamp2pfriends / VeryLow/High/High | 在 Order 47 之后登记；三种 patch type 独立核验；依赖 Bound lifecycle | S+H+R-Pending |
| 49 | WorldSync/Zombie | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `ZombieManager.updateRegionsAndSendZombieStates()` | Transpiler / `UpdateRegionsAndSendZombieStates_Transpiler` | com.yu80rice.steamp2pfriends / default | 无显式 before/after；依赖 Zombie Manager update | S+H+R-Pending |
| 50 | WorldSync/Vehicle | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `VehicleManager.Update()`；`InteractableVehicle.OnUpdate(...)` | Transpiler / `Update_Transpiler`；Postfix / `OnUpdate_Postfix` | com.yu80rice.steamp2pfriends / default | 两个 target 独立核验；依赖 Vehicle Manager Update | S+H+R-Pending |
| 51 | WorldSync/Animal | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `AnimalManager.Update()` | Transpiler / `Update_Transpiler` | com.yu80rice.steamp2pfriends / default | 无显式 before/after；依赖 Animal Manager Update | S+H+R-Pending |
| 52 | WorldSync/Transport | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `NetMessages.SendMessageToClient(EClientMessage,ENetReliability,ITransportConnection,ClientWriteHandler)` | Prefix / `SendMessageToClient_Prefix` | com.yu80rice.steamp2pfriends / default | 与 Order 12/32 共存；依赖 PlayerConnected loopback 分支 | S+H+R-Pending |
| 53 | WorldSync/UI | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `PlayerUI.updatePauseTimeScale()` | Prefix / `UpdatePauseTimeScale_Prefix` | com.yu80rice.steamp2pfriends / default | 无显式 before/after；依赖 Unity frame/time scale | S+H+R-Pending |
| 54 | WorldSync/Vehicle | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `VehicleManager.enterVehicle(InteractableVehicle)`；`VehicleManager.ReceiveEnterVehicleRequest()` | Prefix / `EnterVehicle_Prefix`、`ReceiveEnterVehicleRequest_Prefix` | com.yu80rice.steamp2pfriends / default | 两个 target 独立核验；依赖 vehicle interaction | S+R-Pending |
| 55 | WorldSync/Barricade Diagnostic | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `UseableBarricade.startPrimary()` Prefix+Postfix；`check()`；`checkSpace()`；`checkClaims()`；`ReceiveBarricadeNone(ServerInvocationContext&,Vector3,float,float,float)` Prefix+Postfix+Finalizer；`simulate(uint,bool)`；`build()` Prefix+Postfix；`BarricadeManager.dropBarricade(Barricade,Transform,Vector3,float,float,float,ulong,ulong)` Prefix+Postfix | 对应 `StartPrimaryPrefix/Postfix`、`CheckPostfix`、`CheckSpacePostfix`、`CheckClaimsPostfix`、`ReceiveBarricadeNonePrefix/Postfix/Finalizer`、`SimulatePostfix`、`BuildPrefix/Postfix`、`DropBarricadePrefix/Postfix` | com.yu80rice.steamp2pfriends / default | 独立依赖：DP-1..DP-7 的 target type 为 `UseableBarricade`（DP-8 为 `BarricadeManager`），每个 `(methodName,paramTypes,hook,patchType)` 由 `WorldSyncDiagnosticCore.RegisterIdentityPatch` 独立核验；静态字段反射必须先成功，否则 fail-closed；与 Order 65 的 Barricade lifecycle 原子登记无先后依赖 | S+H+R-Pending |
| 56 | WorldSync/Zombie Diagnostic | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `ZombieManager.SendZombies_Write(NetPakWriter,byte)`；`ReceiveZombies(ClientInvocationContext&)`；`SendZombieStates_Write(NetPakWriter,byte)`；`ReceiveZombieStates(ClientInvocationContext&)`；`onBoundUpdated(Player,byte,byte)` Prefix+Postfix；`sendZombieDead(Zombie,Vector3,ERagdollEffect)`；`sendZombieAlive(Zombie,byte,byte,byte,byte,byte,byte,Vector3,byte)`；`ReceiveZombieDead(byte,ushort,Vector3,ERagdollEffect)`；`ReceiveZombieAlive(byte,ushort,byte,byte,byte,byte,byte,byte,Vector3,byte)`；`ZombieRegion.destroy(...)` | Postfix / `SendZombiesWritePostfix`、`ReceiveZombiesPostfix`、`SendZombieStatesWritePostfix`、`ReceiveZombieStatesPostfix`、`OnBoundUpdatedPostfix`、`SendZombieDeadPrefix`、`SendZombieAlivePrefix`、`ReceiveZombieDeadPrefix`、`ReceiveZombieAlivePrefix`；Prefix / `OnBoundUpdatedPrefix`、`DP8_7_Destroy_Prefix` | com.yu80rice.steamp2pfriends / default | ZombieRegion.destroy target 独立使用 `ZombieRegion`；依赖 entity/state mapping | S+H+R-Pending |
| 57 | WorldSync/Player Culling | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | `PlayerManager.SendPlayerStates_Write(NetPakWriter,ushort,SteamPlayer)`；`PlayerManager.ReceivePlayerStates(ClientInvocationContext&)`；`PlayerMovement.tellState(Vector3,byte,byte)` | Prefix / `DP1_SendPlayerStatesWrite_Prefix`、`DP3_TellState_Prefix`；Postfix / `DP2_ReceivePlayerStates_Postfix` | com.yu80rice.steamp2pfriends / default | 三个 identity target 独立核验；依赖 PlayerManager/Movement | S+H+R-Pending |
| 58 | Route B | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationSecurityModules.cs` | 动态枚举的 `ServerInvocationContext&` RPC Receive 目标与带 `player` 所有权的 Receive 目标 | Prefix / `ContextPrefix`、`OwnerPrefix`；目标集合由源码 predicate 枚举，不是固定单一 MethodInfo | com.yu80rice.steamp2pfriends / default | 在所有 Wrapper/Diagnostic 注册完成后调用；依赖 ServerInvocationContext、SteamCall validation；目标集合和真实执行仍 R-Pending | S+H+R-Pending |
| 59 | Route B | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationSecurityModules.cs` | `PlayerDashboardInformationUI.OnCreatePlayerEntry(SteamPlayer)`；`OnCreatePlayerEntryWithGrouping(SteamPlayer)` | Postfix / `Postfix` | com.yu80rice.steamp2pfriends / default | 在 Order 58 同一 Route B 阶段内后登记；依赖 Dashboard UI lifecycle | S+H+R-Pending |
| 60 | Route B | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationSecurityModules.cs` | `ChatManager.process(SteamPlayer,string,bool)` | Prefix / `Prefix` | com.yu80rice.steamp2pfriends / default | 在 Order 59 后登记；依赖 ChatManager command path | S+H+R-Pending |
| 61 | V2 Fix | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationAuditModules.cs` | `PlayerMovement.InitializePlayer()`（Harmony attribute target） | Prefix / `Prefix`；`RegisterManual` 本身只输出状态，不再执行第二次 Harmony patch | com.yu80rice.steamp2pfriends / `[HarmonyPriority(Priority.High)]` | 必须先由 `PatchAll` 扫描并登记；依赖 Player.InitializePlayer chain；Order 是确认调用，不是新 patch | S+R-Pending |
| 62 | V2 Fix | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationAuditModules.cs` | `SteamPlayer` 精确 ABI 构造器（源码 `SteamPlayerConstructorParameterTypes`） | Postfix / `Postfix` | com.yu80rice.steamp2pfriends / default | 依赖 `Provider.addPlayer` 创建对象；构造器签名变化时 fail-closed | S+R-Pending |
| 63 | V2 Fix | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationAuditModules.cs` | `Player.InitializePlayer()`；`PlayerMovement.Update()`；`PlayerLook.Update()`；`PlayerInput.FixedUpdate()`；`PlayerStance.Update()` | Prefix / `InitializePlayerStatePatch.Prefix`；Postfix / `InitializePlayerStatePatch.Postfix`；Finalizer / `InitializePlayerStatePatch.Finalizer`；Update guards / `UpdateGuardCommon<T>.Prefix` | com.yu80rice.steamp2pfriends / default | 同一 `Player.InitializePlayer` 内 Prefix/Postfix/Finalizer；四个组件 target 独立解析；依赖 P2P gate 与 IsLocalPlayer 排除 | S+R-Pending |
| 64 | V2 Fix | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationAuditModules.cs` | `PlayerClothing.InitializePlayer()`；`PlayerInventory.InitializePlayer()`；`PlayerLife.InitializePlayer()`；`PlayerStance.InitializePlayer()`；`PlayerMovement.InitializePlayer()`；`PlayerLook.InitializePlayer()`；`PlayerInteract.InitializePlayer()`；`PlayerInput.InitializePlayer()` | Postfix / `BitmaskPostfixCache<T>.Postfix`（每个 T 为闭包后的独立 MethodInfo） | com.yu80rice.steamp2pfriends / default | 组件顺序按源码 `RegisterComponentPostfix` 调用；依赖 LocalComponentsInitialized 信号 | S+R-Pending |
| 65 | Session/Lifecycle | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDomainModules.cs` | `UseableBarricade.equip()`；`UseableBarricade.checkClaims()` | Transpiler / `BarricadeLifecycleTranspiler.Equip_Transpiler`、`BarricadeLifecycleTranspiler.CheckClaims_Transpiler` | com.yu80rice.steamp2pfriends / `Priority.Normal`（源码显式）；原子登记并在失败时回滚 | 独立依赖：先由 `WorldSyncDiagnosticCore.RegisterSessionResetCallback(BarricadeLifecycleHelper.ResetHitLogs)` 完成并由 `MarkResetCallbackRegistered` 标记；随后 `CacheAllMethodInfos` 必须解析两个无参 target、两个 Transpiler 和两个 Helper；`PrecheckBothPatternsWithIL` 通过后才调用两个 `harmony.Patch`；任一登记或 `VerifyAll` 失败都执行 `RollbackBoth` | S+H+R-Pending |

矩阵核验规则：`Order` 取 Ticket 02 前 `ApplyAllPatchesAndDiagnostics` 的实际调用阶段和阶段内顺序；矩阵中的 `PatchRegistry.cs:<line>` 是 Ticket 01 的历史调用锚点，不是当前权威实现。Ticket 04 后的真实入口按阶段映射到 §1.1 的 `Core/Registration/*` 文件；target 与 patch method 取对应注册类的 `AccessTools`、`RegisterIdentityPatch`、`HarmonyMethod` 或 Harmony attribute。未标注显式 priority 的项只能写 `default`，不得写成 `First`。例如 `BarricadeManagerRegionSyncPatch` 的 `onRegionUpdated` Transpiler 在源码为普通 `new HarmonyMethod(transpiler)`，不能记为 `Prefix First`。

下列 2.3–2.7 是按领域组织的说明，不重新定义 Order；需要判断真实登记顺序时，以本矩阵和第 2 节调用链为准。

### 2.3 区域与领域同步注册

`ApplyManualWrapperPatches` 随后登记当前旧生产路径上的区域与领域补丁：

- `ClientMethodLoopbackPatch`：客户端方法本地 loopback 分流；
- `BarricadeManagerRegionSyncPatch`：区域更新与 `SendRegion`；
- `StructureManagerRegionSyncPatch`：区域更新与 `askStructures`；
- `ItemManagerRegionSyncPatch`：区域更新与 `askItems`；
- `Adapters.Resource.Patches.ResourceManagerRegionSyncPatch`：区域更新与资源发送 writer；
- `ObjectManagerRegionSyncPatch`：区域更新与 `askObjects`；
- `Adapters.Collision.Patches.LevelObjectRemoteCollisionPatch`：远端静态物体碰撞覆盖；
- `Adapters.Resource.Patches.LevelGroundRemoteTreeCollisionPatch`：远端树木/矿石碰撞覆盖；
- `Adapters.Resource.Patches.ResourceManagerHarvestReplicationPatch`：资源采伐及状态复制；
- `BarricadeStateReplicationPatch` 与 `StructureStateReplicationPatch`：建筑状态复制。

这些补丁的共同原生锚点不是“插件注册时刻”，而是 `PlayerMovement.onRegionUpdated` 被触发后，原生各 Manager 已经在自身 `Start` 中订阅该事件的处理路径。当前 U3-SDK 的区域 step 语义为：Structure step 1、Barricade step 2、Resource step 3、Object step 4、Item step 5，随后由 `PlayerMovement` 继续推进 step index。该顺序是 U3-SDK 的实际回调顺序；Harmony patch 的优先级仍决定同一目标内部的 Prefix/Transpiler/Postfix 执行关系。

### 2.4 V2 修复补丁（主题说明）

`ApplyV2AuditFixPatches` 登记以下具有玩家初始化/就绪语义的补丁：

- `PlayerMovementInitializePlayerPrefixPatch`；
- `SteamPlayerIsLocalServerHostPatch`；
- `PlayerUpdateGuardPatch`；
- `GameplayReadyBitmaskPatch`。

这些目标依赖玩家 GameObject 已由原生 `Provider.addPlayer` 创建，或依赖 `Player.InitializePlayer` 的组件初始化阶段。它们不能被解释为“客户端已经完成准入”；准入、初始化和隔离在 U3-SDK 中是不同阶段。

### 2.5 Route B 注册（主题说明）

Route B 静态补丁在前述注册后登记：

- `P2PQuarantineActionGatePatch`；
- `Patch_PlayerDashboardPlayersUI`；
- `P2PListenHostCommandPermissionPatch`。

其余准入目标由 `PatchAll` 或相应手工验证覆盖，包括白名单检查、伤害门、输入门和连接路由。Route B 的 Provider 生命周期回调不是在 `Awake` 中立即安装，而是在首次成功进入插件 `Update`、确认游戏线程后调用 `P2PApprovalManager.InstallProviderLifecycleHooks`。

该延迟有明确原生依据：U3-SDK 的 `Provider.onServerConnected` 位于接受连接序列末尾，晚于玩家创建、初始全局状态发送、`InitializePlayer` 和初始玩家状态发送。因此当前 trace 记录为“回调订阅延迟安装”，不把它提前描述成连接前隔离。

### 2.6 资产完整性注册（主题说明）

`RegisterAssetIntegritySnapshotPatches` 对两个目标执行手工解析、签名输出、Harmony Patch 和双重登记验证。它们不计入 65 个 `RegisterManual/RegisterAtomically` 调用单元，但属于同一 `Awake` 编排阶段的独立直接 `_harmony.Patch`：

- `Assets.ReceiveKickForHashMismatch(Guid,string,string,byte[],string,string)` → Prefix `AssetIntegritySnapshotPatch.ClientReceiveKickPrefix`（登记入口 `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationAuditModules.cs`）；
- `ClientStaticMethod<Guid,string,string,byte[],string,string>.Invoke(ENetReliability,ITransportConnection,Guid,string,string,byte[],string,string)` → Prefix `AssetIntegritySnapshotPatch.ServerInvokePrefix`（登记入口 `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationAuditModules.cs`）。

这两个目标属于资产/协议边界，不由 `PatchAll` 的成功日志覆盖。服务端目标的泛型声明类型和参数签名必须继续作为独立证据保存。

### 2.7 诊断注册（主题说明）

`ApplyManualDiagnosticPatches` 先登记连接、消息、初始化、玩家可见性、传输和 UI 诊断，再调用 `RegisterWorldSyncDiagnosticPatches` 登记世界同步诊断及现有 P0-C/P0-D 观测补丁。当前成员包括：

- 连接与消息：`NetMessagesSendDiagnosticPatch`、`AuthHandshakeJournalPatch`、`ClientAcceptedHandlerDiagnosticPatch`、`ProviderAcceptStageDiagnosticPatch`、`ProviderRejectDiagnosticPatch`、`QueuePositionChangedDiagnosticPatch`、`InitialStateReceiveDiagnosticPatch`、`NetMessageDeliveryPathDiagnosticPatch`；
- 玩家初始化与可见性：`PlayerComponentInitializeDiagnosticPatch`、`LocalRegionProgressDiagnosticPatch`、`DisconnectTracerPatch`、`PlayerClothingVisibilityDiagnosticPatch`、`PlayerLookAnimatorDiagnosticPatch`、`PlayerMovementTellStateDiagnosticPatch`、`PlayerMovementTellStateCallerDiagnosticPatch`、`PlayerAnimatorSmrEnabledDiagnosticPatch`、`PlayerIsLoadingClothingDiagnosticPatch`、`PlayerInitializePlayerStageDiagnosticPatch`、`PlayerInitializePlayerStagedTimingDiagnosticPatch`、`PlayerLifecycleReadyDiagnosticPatch`、`PlayerManagerBroadcastPatch`、`RemotePlayerClothingVisibleBridgePatch`、`PlayerManagerBroadcastDiagnosticPatch`；
- 平台与 UI：`SteamChannelTransportDiagnosticPatch`、`LoadingUIUpdateDiagnosticPatch`、`UnityTagErrorSourceDiagnosticPatch`；
- 世界同步：`ItemManagerWorldSyncDiagnosticPatch`、`AuthoritativeItemGenerationGatePatch`、`InventoryWorldAuthorityProbe`、`ResourceManagerWorldSyncDiagnosticPatch`、`ObjectManagerWorldSyncDiagnosticPatch`、`Issue7ObjectBinaryStateDiagnosticPatch`、`VehicleManagerWorldSyncDiagnosticPatch`、`AnimalManagerWorldSyncDiagnosticPatch`、`ZombieManagerWorldSyncDiagnosticPatch`；
- 世界同步补充与诊断：`ZombieManagerP0DGenerateZombiesPatch`、`ZombieLifecyclePatch`、`ZombieManagerP0C1SendZombieStatesPatch`、`VehicleManagerP0C1ReplicationPatch`、`AnimalManagerP0C2SendAnimalStatesPatch`、`NetMessagesPlayerConnectedLoopbackPatch`、`PlayerUIPauseTimeScalePatch`、`VehicleEnterDiagnosticPatch`、`UseableBarricadeDiagnosticPatch`、`ZombieEntityMappingDiagnosticPatch`、`PlayerManagerCullingDiagnosticPatch`。

诊断补丁的注册结果会聚合到 `DiagnosticBuildValid`，但诊断 PASS 只表示目标/owner/签名等注册条件满足，不表示真实 Host/Guest 行为已经通过。

## 3. U3-SDK 原生生命周期锚点

### 3.1 Manager Start 与事件订阅

U3-SDK 管理器在自身 `Start` 中订阅原生事件：

| 原生模块 | 订阅位置 | 订阅内容 | 对插件的意义 |
|---|---:|---|---|
| `AnimalManager` | `1074-1079` | `Level.onLevelLoaded` | 动物初始世界状态和更新入口依赖地图加载 |
| `ZombieManager` | `1823-1828` | `Level.onLevelLoaded`、`Level.onPostLevelLoaded` | 丧尸区域/边界生命周期依赖地图生命周期 |
| `ResourceManager` | `824-828` | `Level.onLevelLoaded` | Resource 初始状态依赖地图加载 |
| `ItemManager` | `1273-1278` | `Level.onLevelLoaded` | Item 区域状态依赖地图加载 |
| `ObjectManager` | `1074-1078` | `Level.onLevelLoaded` | Object 区域状态依赖地图加载 |
| `StructureManager` | `1112-1117` | `Level.onLevelLoaded` | Structure 区域状态依赖地图加载 |
| `VehicleManager` | `2943-2950` | `Level.onPrePreLevelLoaded`、`Level.onPostLevelLoaded`、`Provider.onServerDisconnected` | 载具初始化和断开清理有独立前后置阶段 |

Resource、Item、Object、Structure 以及 Barricade 还在自身 `Start` 或玩家关联流程中向 `PlayerMovement.onRegionUpdated` 添加 handler。Barricade 的特殊链条是 `BarricadeManager.Start` 订阅 `Level.onPreLevelLoaded` 和 `Player.onPlayerCreated`，之后在 `onPlayerCreated` 中把自身 `onRegionUpdated` 加到玩家 movement；其原生锚点为 `BarricadeManager.cs:2852`、`2911-2930`。插件对这些 handler 的 patch 必须保留原生 Manager、玩家创建时机与 step 的关系。

### 3.2 地图加载

`Level` 触发地图加载事件并调用订阅者。当前已确认的锚点为 `Level.cs:1989` 和 `Level.cs:2274`。因此：

- 在插件 `Awake` 中完成 Harmony 登记，不等于地图已经加载；
- 对 `onLevelLoaded` 或 `onPostLevelLoaded` 的补丁只有在原生事件触发时才执行；
- Registration Trace 必须区分“补丁已登记”和“地图生命周期已到达”。

### 3.3 玩家区域更新

`PlayerMovement` 在区域变化时循环处理 `updateRegionIndex < 6`，在 `PlayerMovement.cs:1870` 调用 `onRegionUpdated`，并在 handler 允许时推进 index。当前领域 step 映射如下：

| Step | 原生 handler | 插件登记对象 | 领域责任 |
|---:|---|---|---|
| 1 | `StructureManager.onRegionUpdated` | `StructureManagerRegionSyncPatch` | Structure |
| 2 | `BarricadeManager.onRegionUpdated` | `BarricadeManagerRegionSyncPatch` | Barricade |
| 3 | `ResourceManager.onRegionUpdated` | `ResourceManagerRegionSyncPatch` | Resource |
| 4 | `ObjectManager.onRegionUpdated` | `ObjectManagerRegionSyncPatch` | Object/Collision |
| 5 | `ItemManager.onRegionUpdated` | `ItemManagerRegionSyncPatch` | Item |
| 6 | 原生循环结束后的 Bound/后续逻辑 | Zombie/其他边界补丁按各自目标生效 | Zombie Bound 等 |

插件不应在后续拆分中把“领域在 Registry 中的文本顺序”当作这个原生 step 顺序的替代品。

### 3.4 客户端连接入口

U3-SDK `Provider.connect` 位于 `Provider.cs:1702-1760`：

1. 保存连接参数与 advertisement；
2. 根据 advertisement 或参数链接 lobby；
3. 保存密码并计算密码 hash；
4. 建立客户端连接状态、ping 和 Workshop 等待状态；
5. 继续进入原生连接/认证流程。

插件的 SteamID、直连和 DNS 路由补丁必须分别映射到这个入口及其上游 UI/transport 路径。把插件直接调用 `Provider.connect` 解释为已经完成 advertisement、Workshop 校验或服务器准入是不成立的。

### 3.5 服务端接受连接

U3-SDK `Provider.accept` 的关键调用链位于 `Provider.cs:4876-4970`：

```text
addPlayer
  → 新旧客户端 PlayerConnected 广播
  → Accepted 消息
  → SendInitialGlobalState
  → newClient.player.InitializePlayer
  → 各客户端初始 Player State
  → onServerConnected
```

其中 `addPlayer` 创建玩家并加入客户端集合，`SendInitialGlobalState` 会触发多个全局领域状态发送，`InitializePlayer` 初始化玩家组件，最后才触发 `onServerConnected`。因此：

- `onServerConnected` 是接受流程的末尾回调，不是握手前 admission gate；
- Route B 在该回调中登记 Pending 的时机必须与信息面、行为面和初始化面分别描述；
- 任何后续提前隔离的设计都必须以新的 U3-SDK 目标和静态/运行时证据证明，不能从当前回调名称推断。

### 3.6 断开与清理

U3-SDK 声明 `Provider.onServerDisconnected` 并在 `broadcastServerDisconnected` 中调用，锚点为 `Provider.cs:3962-3975`；`VehicleManager` 还在自身 Start 中订阅该事件。插件存在多层断开订阅，必须分别记录：

| 插件模块 | 事件 | 注册状态/职责 |
|---|---|---|
| `SteamP2PFriendsPlugin` | `Provider.onEnemyDisconnected`、`Provider.onClientDisconnected` | `Awake` 直接订阅，`OnDestroy` 直接移除并转发到断线清理 |
| `P2PJoinManager` | `Provider.onClientConnected`、`Provider.onClientDisconnected` | `Initialize` 在 P2P 入口可用后订阅，`Shutdown` 对称移除 |
| `P2PApprovalManager` | `Provider.onServerConnected`、`Provider.onServerDisconnected` | 首次成功游戏线程 Update 延迟安装，生命周期卸载时移除 |
| `HostManager` | `Provider.onEnemyConnected`、`Provider.onServerHosted` | 主机启动路径订阅，终止/卸载路径移除 |
| `SessionDisconnectDispatcher` | `Provider.onEnemyDisconnected`、`Provider.onClientDisconnected` | 提供统一清理实现，但当前源码扫描未发现插件入口调用 `SessionDisconnectDispatcher.Initialize`；其“声明存在”不能写成“运行时已订阅” |

断开 trace 必须区分原生 Provider 广播、实际已安装的插件 handler、仅声明但未调用的初始化方法，以及各领域清理职责。

断开 trace 必须区分：

- 原生 Provider 断开广播；
- 插件的连接生命周期清理；
- 各领域区域计数、generation 和缓存清理；
- Route B Pending/批准状态清理。

### 3.7 Manager Update

Zombie、Animal、Resource、Item、Object、Structure 和 Vehicle 的原生 `Update` 不是注册事件，而是 Unity 主循环中的运行时消费点。插件对这些方法的 patch 只能证明补丁已挂到 Update；是否实际执行、执行频率、listen-host 与 dedicated 分支差异仍需 Runtime Evidence。

## 4. 已确认的映射与当前边界

| 插件注册阶段 | 原生锚点 | 静态结论 | 仍需运行时验证 |
|---|---|---|---|
| `PatchAll` | 程序集扫描 | 扫描调用发生 | 每个特性目标是否成功登记、Harmony 最终顺序 |
| Steam wrapper helper | Steamworks native boundary | 目标和 helper 可解析并登记 | native callback、transport 初始化顺序 |
| Region Sync patches | Manager `onRegionUpdated` | 目标与 step 映射可由源码确认 | 玩家移动时真实调用、多个观察者覆盖结果 |
| Asset Integrity manual patches | 资产接收/泛型 Invoke | 手工目标解析与双重验证存在 | 真实握手、hash mismatch 和 Workshop 交互 |
| Route B static patches | whitelist/damage/input/UI | 静态目标自检存在 | accept 末尾到隔离建立之间的真实时序 |
| Route B lifecycle hooks | `Provider.onServerConnected` / `onServerDisconnected` | 首次游戏线程 Update 延迟安装 | 首次连接是否早于/晚于 hooks 安装、重连竞态 |
| Manager lifecycle patches | Level events、Manager Update | 原生订阅锚点已确认 | Unity 对象实例化和 Start 相对顺序 |

## 5. 不能由当前静态材料推出的结论

以下项目保持 Runtime Pending，不用文档顺序代替运行时事实：

1. BepInEx 插件 `Awake` 与各 U3-SDK Manager `Start` 在实际游戏实例中的相对时序；
2. Harmony 对同一原生目标的最终排序，尤其是同 priority 且无显式 before/after 的补丁；
3. `PatchAll` 扫描顺序以及复杂泛型/内部目标的实际成功率；
4. `PlayerMovement.onRegionUpdated` 的真实 delegate 调用时序与异常传播效果；
5. Provider 接受连接期间，所有初始全局状态发送与 Route B Pending 建立的双端可见结果；
6. 首次 Update 延迟安装 Route B lifecycle hooks 与首次连接/断线之间的竞态；
7. Manager Update 在 listen-host、多观察者和远端区域增长后的实际负载；
8. Registration Trace 完成后是否已形成真正不可变的 Registration Closure；该状态属于 Ticket 02。
9. `SessionDisconnectDispatcher.Initialize` 是否由未来编排器接管并成为唯一断开订阅入口；当前源码只确认其实现存在，未确认其已被调用。

## 6.1 版本元数据旁证

当前启动日志版本从已加载程序集的 `AssemblyName.Version` 读取；插件特性、AssemblyInfo 和统一构建元数据均为 `0.2.4.8`。因此：

- 本 Ticket 不擅自修改启动日志消费者；
- Ticket 09 必须把该消费者纳入 Build Fingerprint/Metadata Source 收敛；
- 在 Ticket 09 完成前，不得仅凭启动日志中的版本字段判断加载产物版本。

## 6.2 Ticket 07 迁移后的注册入口映射

Ticket 07 只改变已确认领域补丁的物理目录与编译命名空间；Registration Trace 的 U3-SDK
原生顺序、目标、patch type、owner、priority 和阶段顺序保持不变。当前入口映射如下：

| 原生领域 | 当前补丁入口 | 编排/验证入口 | 状态 |
|---|---|---|---|
| Animal | `SteamP2PFriends.Adapters.Animal.Patches.*` | `PatchRegistrationLegacyDiagnostics`、`PatchRegistrationCriticalVerification` | Confirmed |
| Structure | `SteamP2PFriends.Adapters.Structure.Patches.StructureManagerRegionSyncPatch` 与 `P0EBarricadeLifecycle.*` | `PatchRegistrationTransportModules`、`PatchRegistrationDomainModules`、关键验证 | Confirmed |
| Barricade | `SteamP2PFriends.Adapters.Structure.Patches.BarricadeManagerRegionSyncPatch` 与 `P0EBarricadeLifecycle.*` | `PatchRegistrationTransportModules`、`PatchRegistrationDomainModules`、关键验证 | Confirmed |
| Vehicle | `SteamP2PFriends.Core.Patches.Vehicle*` | 既有 Registration Trace 入口 | Pending，未迁移 |
| Object | `SteamP2PFriends.Core.Patches.Object*` | 既有 Registration Trace 入口 | Pending，未迁移 |
| OtherPending | `SteamP2PFriends.Core.Patches.*` | 既有跨领域 Registration Trace 入口 | Pending，按受控跨领域规则保留 |

结构 StaticIL 接缝验证上述 Confirmed 类型各只有一个编译入口，并验证 Vehicle 没有被错误
创建 `Adapters.Vehicle` 并行入口。该接缝不能替代最终 Harmony Runtime 排序和四环境运行证据。

## 7. Ticket 01 交付判定

- [x] 固定 U3-SDK commit 与关键原生生命周期锚点。
- [x] 记录 `PatchAll`、手工 wrapper、领域注册、诊断注册、Route B 延迟注册和关闭路径。
- [x] 建立手工注册点与 U3-SDK 原生调用链的分组映射。
- [x] 明确 Harmony 注册、原生事件消费和 Runtime Evidence 的边界。
- [x] 记录未确认时序为 Pending，没有用抽象架构顺序补齐事实。
- [x] 证明本 ticket 只更新追踪/审计资料，不改变生产代码、协议、配置、标签或归档版本。

## 8. 后续 Ticket 依赖

Ticket 02 可以基于本记录拆分 Patch Registration Orchestrator，但必须继续保留：

- U3-SDK commit 与原生行号锚点；
- `PatchAll` 与手工注册的区别；
- Region step 与 Manager Start 订阅关系；
- Provider accept 序列及 `onServerConnected` 的末尾位置；
- 所有 Pending 的运行时边界。
