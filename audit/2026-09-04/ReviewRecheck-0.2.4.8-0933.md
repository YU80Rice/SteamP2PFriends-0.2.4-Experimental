# 第三方架构评审逐条复核报告(ReviewRecheck)

- **被评审对象**:`ARCHITECTURE-REVIEW-0.2.4-Experimental.md`(评审日期 2026-08-26,评审基线 `master` @ `2e6de00`,即 Ticket 05 落地后、Ticket 10/11 之前)
- **复核基线**:当前工作区 = 分支 `codex/structure-baseline-0.2.4` HEAD `340918a` + **刻意保留的未提交取证轮变更**(`Adapters/Resource/ResourceProductionControlSeam.cs` DescribeAcquireFailure 取证埋点、`WhitelistTests/` 三个测试文件、`audit/README.md`、未跟踪 `audit/2026-09-03/`)。本报告所有"当前证据"均以该工作区状态为准。
- **复核方法**:静态只读复核。对评审的每一条判决,在当前工作区重新定位符号与行号(评审引用的行号基于 `2e6de00`,已普遍过时,一律重新 grep/Read 定位),并用 `git log --oneline` / `git log -S` 追溯评审之后的修复归属(commit / Ticket / audit 报告)。
- **证据边界声明**:本报告为**静态复核**。Evidence Class 四类证据(PureMemory / StaticIL / BuildArtifact / Runtime)不可互相升级替代——凡评审断言运行时效果的条目(僵尸重生、掉落物再生、SPI 实际驱动、房主端树木不再消失等),即使当前接线齐全,也一律标注 **Runtime 待验证**,状态最高只给到"仅静态可判定",Runtime 类结论一律 **PENDING**。已知关键运行时事实(来自 `audit/2026-09-03/Implementation-0.2.4.8-Ticket11-1123.md` 三端日志诊断):Resource SPI 的 LeaseAcquire 每次死于 region-snapshot-failed `InvalidOperationException`(CaptureRegionState→CaptureNativeRegionState,OnAcquire 之前),单区域失败整批回滚+指数退避,SPI 主路径至今未在运行时走通一次完整 Acquire→Release;当前取证轮(埋点 DLL SHA `145BEFAA…`)已落盘,容错修复轮尚未开始。
- **U3-SDK 对照**:评审所引 `D:\Dev\Codes\CSharp\Unturned\U3-SDK` 行号为评审者环境;本次复核对照 `D:\Agent-工作目录\U3-SDK`(可读性见各条目注明)。

---

## 第一部分:逐条对照总表

### §〇 摘要表(6 行)

| # | 评审判决(2026-08-26) | 当前证据(文件:行,当前工作区) | 状态 |
|---|---|---|---|
| 1 | Multi-Observer 控制面(SPI/SpatialObserverIndex/LeaseTicket)生产零调用,全部死代码;测试与运行时无因果 | Ticket 10(commit `a7147aa`)将 SPI 接进生产:全仓唯一 `.OnAcquire(` 生产调用位于 `Adapters/Resource/ResourceProductionControlSeam.cs:598`,上游为 `MultiObserverShadowCoordinator` + `Core/ControlPlane/Spatial/SpatialObserverIndex.cs`;但**仅 Resource 单域**,且 2026-09-02 三端日志证明每次 LeaseAcquire 死于 region-snapshot-failed、SPI 全程无 lease(`audit/2026-09-03/Implementation-0.2.4.8-Ticket11-1123.md` F2/F5) | **部分修**(结构接线完成,Runtime 瘫痪中,修复轮未开始) |
| 2 | 真正生效的是 `*RegionSyncPatch` transpiler 组 + `ZombieRegionLifecycleAdapter` | 未回退,仍是主干:`Adapters/Item/Patches/ItemManagerRegionSyncPatch.cs`、`Adapters/Resource/Patches/ResourceManagerRegionSyncPatch.cs:384-401` 沿用 `ListenRegionSyncEligibility` 单点替换;Zombie 走自有生命周期路径(`Adapters/Zombie/ZombieRegionLifecycleAdapter.cs:202/230/271`) | 维持(评审描述至今准确) |
| 3 | 僵尸不刷新:根因 `respawnZombies` listen-host 早退未被 patch | 全仓 grep `respawnZombies` 无任何插件 patch(仅 U3-SDK 参考命中) | **未修** |
| 4 | 物品/掉落物 despawn/respawn 门控保留(项目注释自认) | `Adapters/Item/Patches/ItemManagerP0B3PreGeneratePatch.cs:68` 注释原样:「不修改 L1203 OnUpdate despawn/respawn 门控(!IsDedicatedServer 提前 return 保留)」 | **未修**(项目自认) |
| 5 | 树木/物件碰撞主路径有效,但存在房主自身区域被误 disable 的回归 | `Adapters/Collision/Patches/LevelObjectRemoteCollisionPatch.cs:211-218` `IsRegionCovered` 仍只看 `RemoteCoverage`(并叠加死分支条件),`:598-602` 仍 `enable()/disable()` 二元控制 | **未修** |
| 6 | Route B 三路径 fail-open;SteamID 路线丢密码;直连/DNS 丢整个 advertisement;IPv6 不支持 | `Security/P2PApprovalManager.cs:344→352` 仍先摘 Pending 后 kick;`Platform/Client/P2PJoinManager.cs:545` 仍 `new ServerConnectParameters(hostSteamId, string.Empty)`;`Platform/UI/Patches/MenuPlayConnectP2PRoutePatch.cs:85`、`Platform/Transport/ExplicitDnsDirectIpController.cs:83` 仍 `Provider.connect(parameters, null, null)`;`Platform/Transport/UnifiedJoinAddressClassifier.cs:57` 多冒号仍判死 | **未修** |

### §一 Multi-Observer 控制面空壳判决

**1.2 组件死代码表(8 项)**

| 组件 | 评审时判决 | 当前证据 | 状态 |
|---|---|---|---|
| `SpatialObserverIndex` | 生产 0 调用,仅测试 4 处 `new` | 现被 `Adapters/Resource/ResourceProductionControlSeam.cs` 生产引用(经 `MultiObserverShadowCoordinator` 驱动);归属 Ticket 10(`a7147aa`) | 部分修(仅 Resource 链路消费) |
| `ZombieDomainAdapter` | 仅 `.csproj` 编译项 | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDomainModules.cs:65` 已注册;但 SPI `OnAcquire` 无生产调用,Zombie 实际激活仍走自有 vanilla-patch 路径(`TryAcquireForRemote`/`SetRegistrationReady`,`SteamP2PFriendsPlugin.PatchRegistrationCriticalVerification.cs:963`) | 部分修(结构注册,行为路径未变) |
| `AnimalDomainAdapter` | 仅 `.csproj` | `PatchRegistrationDomainModules.cs:68` 已注册,无 SPI 驱动 | 部分修(结构注册,未驱动) |
| `ResourceDomainAdapter` | 仅 `.csproj` | 唯一被真实驱动的域(`ResourceProductionControlSeam.cs:598`);Runtime 瘫痪中(F2) | 部分修(接线完成,Runtime PENDING) |
| `ItemDomainAdapter` | 仅 `.csproj` | 实现 SPI(`Adapters/Item/ItemDomainAdapter.cs:13`),注册但无 `OnAcquire` 生产调用 | 部分修(结构注册,未驱动) |
| `BuildingDomainAdapter` / `LevelObjectCollisionAdapter` | 仅 `.csproj` / 0 引用 | `Adapters/Collision/LevelObjectCollisionAdapter.cs:112` 实现 SPI 并注册;`OnAcquire` 无生产调用 | 部分修(结构注册,未驱动) |
| `LeaseTicket` | 仅存在于从不被调用的形参中 | 已成为正式 SPI 类型 `Core/ControlPlane/SPI/LeaseTicket.cs`,被 Resource seam 真实颁发/消费 | 部分修(仅 Resource) |
| `RegisterLifecycleAdapter` / `RegisterReplicationAdapter`「方法根本不存在」 | 只存在于 `.scratch` 规格中 | 等价机制已实现:`Core/Registration/RegistrationClosure.cs:108` `TryRegisterLifecycle` + `PatchRegistrationDomainModules.cs:75` `RegisterLifecycle(MultiObserver.SPI.ILifecycleDomainAdapter)`;接口 `Core/ControlPlane/SPI/ILifecycleDomainAdapter.cs`、`IStateReplicationAdapter.cs`;归属 Ticket 10 | 随重构失效(以 SPI 接口 + RegistrationClosure 形式落地) |

**1.3 连锁死分支(3 条)**

| 评审判决 | 当前证据 | 状态 |
|---|---|---|
| `LevelObjectCollisionAdapter.IsRemoteCollisionRequired` 读 `_activeRegions`(只能由 `OnAcquire` 写)→ 恒 false,`LevelObjectRemoteCollisionPatch` 的 `||` 条件为死分支 | `Adapters/Collision/LevelObjectCollisionAdapter.cs:30/49/127` 结构原样,`OnAcquire` 仍无生产调用 → 恒 false;`IsRegionCovered`(`LevelObjectRemoteCollisionPatch.cs:218`)仍含此死条件 | **未修** |
| `ResourceRegionLifecycleAdapter.IsRegionActive` 恒 false;`LevelGroundRemoteTreeCollisionPatch.cs:79` 第一条件永不成立,实际靠旧私有轮询 | Ticket 10 后第一条件被真实驱动:`Adapters/Resource/Patches/LevelGroundRemoteTreeCollisionPatch.cs:85-86` `covered = ResourceRegionLifecycleAdapter.IsRegionActive(x,y) || LevelObjectRemoteCollisionPatch.IsRegionCovered(x,y)`;但运行时 SPI 无 lease(1123 报告 F5),实际生效的仍是第二条件旧轮询;F6 佐证(`CollisionActivation…path=Native spiActive=true`) | 部分修(结构接线,**Runtime 待验证且当前瘫痪**) |
| `AnimalRegionLifecycleAdapter.Tick()` 与 `ResourceRegionLifecycleAdapter.Tick()` 是空方法体 | Animal `Tick()` 仍为空方法体 + 一行中文注释(`Adapters/Animal/AnimalRegionLifecycleAdapter.cs:214-217`「运行时心跳与倒计时推进」) | **未修** |

**1.4 「181/181 PASS」度量失真** — 制度性回应已建立,Runtime 证据未落地:Ticket 08 建立四类 Evidence Class 与唯一测试入口(commit `61bbffc`、`89bb83a`;`docs/architecture/evidence-class-test-gates.md`),「无多机 Runtime 日志不得宣称 Runtime PASS」成为仓库铁律(Ticket 11 保持 `implemented-pending-runtime` 未关闭);但当前 257 项测试中 Runtime 类证据仍为 stub,PENDING。**部分修**。

**1.5 真实生效的架构** — 评审描述的两套生效机制(transpiler 组 + Zombie 自有适配器)仍是主干;Resource seam 是评审后新增的第三套,接线完成但运行时瘫痪(见 §〇 第 1 行)。**维持 + 部分修**。

### §二 公允评价(2 项)

| 评审判决 | 当前证据 | 状态 |
|---|---|---|
| 2.1 `onRegionUpdated` 单点 IL 替换 + 精确计数自检 ⭐ 应成为全项目模板 | 仍在且持续作为模板:`Adapters/Resource/Patches/ResourceManagerRegionSyncPatch.cs:384-401`(replacement 校验 + `ListenRegionSyncEligibility` 单点替换)、`Adapters/Item/Patches/ItemManagerRegionSyncPatch.cs:422` 同构 | 维持(评审认可项未被回退) |
| 2.2 `ZombieRegionLifecycleAdapter` 的 `isNetworked` 借出/归还 ⭐ 正确修复 | `Adapters/Zombie/ZombieRegionLifecycleAdapter.cs:202` `TryAcquireForRemote`、`:230` `BeginLocalTransition`、`:271` `CompleteLocalTransition` 原样 | 维持;注意 §四 4.1(`respawnZombies`)的相邻缺口仍未修 |

### §三 Multi-Observer 具体缺陷(6 条)

| # | 评审判决 | 当前证据 | 状态 |
|---|---|---|---|
| 3.1 | M5 动物适配器在为不存在的机制建模(U3-SDK `AnimalManager` 无区域生成机制) | `Adapters/Animal/AnimalRegionLifecycleAdapter.cs` 仍存在,`Tick()` 仍为空方法体(`:214-217`);`AnimalDomainAdapter` 已注册 SPI(`PatchRegistrationDomainModules.cs:68`)但无驱动。结构整理未解决「为不存在机制建模」的本质批评 | **未修** |
| 3.2 | 两套互不兼容的 RegionKey 编码并存(`(x<<8)\|y` vs `x*WORLD_SIZE+y`),「接线之日即引爆之时」 | Ticket 03 身份契约落地:统一类型 `Core/Identity/RegionKey.cs:8`(readonly struct,IEquatable);`(x<<8)\|y` 编码已从代码中消失;SPI/适配器层统一用 `RegionKey`。`Adapters/Collision/Patches/LevelObjectRemoteCollisionPatch.cs:637` `EncodeRegion`(`x*Regions.WORLD_SIZE+y`)仍存在,但封闭在 patch 内部 `RemoteCoverage` 集合中,与 SPI 无互传。引爆前提(Collision 域被 SPI 驱动)尚未出现 | **部分修**(类型统一完成,patch 内部遗留编码已隔离;Collision 域未驱动故雷未触发) |
| 3.3 | `RefreshObjectsInRegion` 只看远端覆盖不看房主,可复现「误伤房主脚下树木」回归;vanilla 脏区域模型不会自愈 | `Adapters/Collision/Patches/LevelObjectRemoteCollisionPatch.cs:211-218` `IsRegionCovered` 仍只查 `RemoteCoverage.Contains(...)` + 死分支 `IsRemoteCollisionRequired`;`:589` `isCovered = IsRegionCovered(x, y)`;`:598-602` 仍 `enable()/disable()` 二元 | **未修**(评审批评的回归原样存在) |
| 3.4 | 树木覆盖半径用错模型(`OBJECT_REGIONS` 方形半径同时服务物件与树木,vanilla 树木用距离模型) | `LevelObjectRemoteCollisionPatch.cs:537` `int radius = LevelObjects.OBJECT_REGIONS;` 原样 | **未修** |
| 3.5 | `MultiObserverShadowCoordinator` fail-closed 过于粗暴:单条记录不完整丢弃整批 + 计数下溢 throw → 指数退避长期静默 | 结构仍在(`Core/ControlPlane/MultiObserverShadowCoordinator.cs:266-388` 多处 catch;`SafeMismatch`/`capture-incomplete` 整批抑制 `:175`);且已被**运行时证实**:1123 报告 F4——单区域 `(33,34)` 失败 → 整批回滚(`RestoreState`+`RunCompensations`)→ `SnapshotRemove` 全清,叠加 `ShadowFaultBackoff` 退避 83.9→1554.8s,生产瘫痪 | **未修,已被运行时证实为当前主线缺陷** —— Ticket 11 步骤④修复轮的唯一目标 |
| 3.6 | `host-session-identity` 是永久静默开关(错过一次 `NotifyHostSessionStarted` 整局失明) | `Core/ControlPlane/MultiObserverShadowCoordinator.cs:139-145` `SafeMismatch("host-session-identity", …)` 结构原样,无恢复路径演进 | **未修** |

### §四 与 U3DS 的对齐缺口(4 条)

| # | 评审判决 | 当前证据 | 状态 |
|---|---|---|---|
| 4.1 | 僵尸被打死后永不重生:`respawnZombies` listen-host 早退未 patch(🔴 最高优先级) | 全仓 grep `respawnZombies` 无插件 patch | **未修**(评审后 9 天无动作;Runtime 类,修复后亦须多机验证) |
| 4.2 | 掉落物不消失、物资不再生:`ItemManager.Update` L1203 门控保留(🔴) | `Adapters/Item/Patches/ItemManagerP0B3PreGeneratePatch.cs:68` 注释原样自认 | **未修**(同上,Runtime 类) |
| 4.3 | 主机侧 tick 切片差异:listen-host 全量 tick 僵尸/动物(🟡 性能,M3 放大) | 无任何插件 patch 涉及;Multi-Observer 若激活将放大该成本 | **未修**(记录项) |
| 4.4 | 载具物理不在 listen host 跑:`VehicleManager.cs:2852` dedicated 门(🟡) | 无任何插件 patch 涉及;M8 载具域未开始 | **未修**(记录项,与评审时一致) |

### §五 Route B 审核机制的时序问题(8 条)

> 注:评审引用的 `Host/P2PApprovalManager.cs` 等路径已随结构基线整理迁移至 `Security/`、`Platform/`;以下行号均以当前文件为准。

| # | 评审判决 | 当前证据 | 状态 |
|---|---|---|---|
| 5.1 | 白名单在「启用」时反而失效(`IsActiveP2PHost` 要求 `isWhitelisted`,connect 校验被无条件放行)——设计前提,须明示文档 | `Security/P2PApprovalManager.cs:32` `IsActiveP2PHost => HostManager.IsP2PHostMode && Provider.isServer && Provider.isWhitelisted` 原样 | **未修**(文档明示义务亦未见落地) |
| 5.2 | `onServerConnected` 是 accept 最后一步,隔离建立过晚——信息面已全量泄露后才标 Pending | 未见结构性改动(无握手期预登记机制的代码证据) | **未修** |
| 5.3 | Reject/Timeout/Revoke 三路径 fail-open(先摘 Pending 后 kick,kick 静默失败则门全开) | `Security/P2PApprovalManager.cs:344` `RejectPlayer` 仍先 `Pending.TryRemove`(条件内)→ `:352` 才 `SafeKick`;超时路径与 `RevokePlayer`(`:386→394`)同构;无 `Denied` 终态 | **未修** |
| 5.4 | 白名单开关中途关闭 = 待审玩家永久卡死(`Tick/Approve/Reject` 依赖 `isWhitelisted`,而 `ShouldBlock` 不看) | `ShouldBlock`(`Security/Patches/P2PQuarantineAdmissionPatches.cs:171-176`)仍只看 `IsP2PHostMode && Provider.isServer && IsPending`;而 `Tick/ApprovePlayer/RejectPlayer` 全部经 `IsActiveP2PHost`(含 `isWhitelisted`,`:32`)——组合缺陷原样 | **未修** |
| 5.5 | 隔离信号用 `setAllPluginWidgetFlags` 全量覆写、无周期重申、`0x80000000` 抢占位 | `Security/P2PApprovalManager.cs:101` `QuarantineSignalMask = 0x80000000u` 原样;`SetQuarantineSignal` 仅在状态转换点调用(`:240/277/314/351/393`),无周期重申 | **未修** |
| 5.6 | 每个 RPC 走一次未缓存反射(`ExtractOwner` 每包 `AccessTools.Property`) | `Security/Patches/P2PQuarantineAdmissionPatches.cs:159-164` `ExtractOwner` 仍每包反射,无预编译委托缓存 | **未修**(性能,热路径) |
| 5.7 | `ApprovePlayer` 白名单已落盘却返回 false,语义不一致误导 UI | `Security/P2PApprovalManager.cs:309-319`:白名单 `TryAdd` 成功 → `SetQuarantineSignal` 失败 → `SafeKick("Approval completed; reconnect required.")` + `feedback="白名单已写入,但解除隔离信号失败…"` + `return false` —— 逻辑与评审描述原样 | **未修** |
| 5.8 | 容量检查在错误的位置(`CanPermitHandshake` 不查 `MaxPendingEntries`,第 17 人走完握手拿到全量快照才被踢) | `Security/P2PApprovalManager.cs:153-159` `CanPermitHandshake` 仍不查容量;`:253` 存在 `Pending.Count >= MaxPendingEntries` 的容量拒绝分支(其调用时机未逐行确认,是否仍在信息面泄露之后未能静态判定) | **未修**(`:253` 有改善迹象,待确认) |

### §六 连接路由(6 条)

| # | 评审判决 | 当前证据 | 状态 |
|---|---|---|---|
| 6.1 | SteamID 路线把密码丢了(`ServerConnectParameters` 硬编码空串,🔴 一行修复) | `Platform/Client/P2PJoinManager.cs:545` `new ServerConnectParameters(hostSteamId, string.Empty)` 原样 | **未修** |
| 6.2 | 直连/DNS 路线丢弃整个 `SteamServerAdvertisement`(`Provider.connect(parameters, null, null)`,🔴) | `Platform/UI/Patches/MenuPlayConnectP2PRoutePatch.cs:85`、`Platform/Transport/ExplicitDnsDirectIpController.cs:83` 两处仍传 `null, null` | **未修** |
| 6.3 | IPv6 被两条路线整体拒绝(多冒号判死 + 仅 `InterNetwork`) | `Platform/Transport/UnifiedJoinAddressClassifier.cs:57`、`:104` 多冒号仍判死;`ExplicitDnsDirectIpController.cs:429` 仍仅接受 IPv4 | **未修** |
| 6.4 | `SteamID:端口` 粘贴后直接连接静默失败(修法:`Classify` 先剥 `:port`) | `MenuPlayConnectP2PRoutePatch.cs:31` Prefix **先**调 `Classify(raw)`,`UnifiedJoinAddressClassifier.cs:23` 对 `76561198…:27016` 整串 `ulong.TryParse` 失败 → `Vanilla`;`:port` 剥离逻辑只存在于其后的 `TryBuildDirectIpEndpoint`(`:54-58`),救不了 SteamID 路线 | **未修**(剥离逻辑存在但位置不对,评审描述的失败链原样) |
| 6.5 | DNS 解析策略偏弱(单 A 记录、无重试、5s 超时偏紧、拒绝下划线) | `ExplicitDnsDirectIpController.cs:119` `TimeoutSeconds = 5f` 原样;`UnifiedJoinAddressClassifier.cs:124` `IsValidAsciiDnsName` 原样 | **未修** |
| 6.6 | `DirectIpSinglePortQueryPortPatch` 只是显示层补丁 | `Platform/Transport/Patches/DirectIpSinglePortQueryPortPatch.cs:18` 注释原样:「This projection is display-only.」 | **未修**(与评审时一致) |

### §七 优先级表 10 项总览

| # | 评审建议(2026-08-26) | 工作量 | 复核状态 |
|---|---|---|---|
| 1 | `respawnZombies` 单点 transpiler | 小 | **未修** |
| 2 | `ItemManager.Update` L1203 同款 transpiler | 小 | **未修** |
| 3 | `ServerConnectParameters` 传入真实密码(1 行) | 小 | **未修**(`P2PJoinManager.cs:545`) |
| 4 | Route B 三路径改「kick 成功后才摘 Pending」或引入 `Denied` 终态 | 小 | **未修** |
| 5 | `Classify` 剥离 `:port` 后缀 | 小 | **未修**(剥离存在于 `TryBuildDirectIpEndpoint:54-58`,但 `Classify` 入口未剥、调用顺序在前,失败链原样) |
| 6 | `IsRegionCovered` 并入本地玩家覆盖,或移除 `tree.disable()` | 小 | **未修** |
| 7 | `ShouldBlock`/`ApprovePlayer`/`Tick` 解除对 `isWhitelisted` 的依赖 | 小 | **未修** |
| 8 | 删掉或接线 SPI + 全部 DomainAdapter + 死账本 | 中 | **部分修 —— 评审后唯一实质进展**:Ticket 10 接线 Resource(`a7147aa`→`9a126cd`),SPI 接口/LeaseTicket/RegistrationClosure 正式化;其余 5 域注册未驱动;运行时瘫痪中(F2),详见文末专段 |
| 9 | `OwnerPrefix` 反射改预编译委托 | 中 | **未修**(`P2PQuarantineAdmissionPatches.cs:159-164`) |
| 10 | 直连/DNS 补 A2S 预查询恢复 advertisement | 大 | **未修** |

---

## 第二部分:当前可升级项清单

**排序框架**:沿用评审 §七「每单位工作量恢复的 U3DS 语义」;叠加本仓库节奏约束——每次 DLL 变化(新 MVID/SHA)都触发一轮三端日志重采,因此**小修复应尽量合并批次**,在 Ticket 11 修复轮之后的下一次动态测试中一并验证,避免每个小修复各付一次多机测试成本。

### 第一梯队:主线(与 Ticket 11 修复轮同体,先走完)
| 升级项 | 对应评审条目 | 依赖与验证 |
|---|---|---|
| 单区域 Acquire 失败跳过/隔离(消除整批回滚 + 退避瘫痪) | §3.5 | **就是 Ticket 11 步骤④**,M6P31(当前红)转绿;PureMemory 契约已就位,落地后必须 1 Host + 2 Guest 多机日志证明 SPI 成为主驱动路径。其余升级项不应插队到它之前 |

### 第二梯队:小工作量 × 高收益(建议合并为一个修复批次,与下一次动态测试同批验证)
| 升级项 | 对应评审条目 | 工作量 | 验证方式 |
|---|---|---|---|
| `respawnZombies` 单点 transpiler(复用 `IsDedicatedOrP2PHost` 模板) | §4.1 | 小 | StaticIL 契约(replacementCount==1)+ **必须多机 Runtime**(僵尸重生行为) |
| `ItemManager` L1203 同款(掉落物清理 + 物资再生) | §4.2 | 小 | 同上 |
| 密码一行修复(`P2PJoinManager.cs:545` 传入真实 `passwordField`) | §6.1 | 1 行 | 纯静态 + 带密码房间连接测试 |
| `IsRegionCovered` 并入本地玩家覆盖,或只 `enable()` 把 `disable()` 还给 vanilla tracker | §3.3 | 小 | **必须 Runtime**(房主端树木可见性);注意与 F2 根因区分:本条是覆盖集回归,F2 是 Acquire 快照失败,两者症状相似(Host 看不到资源)但机制不同 |
| Route B fail-open:`kick` 成功后才摘 `Pending`,或 `Denied` 终态 | §5.3 | 小 | PureMemory 状态机契约可覆盖大部分 |
| 白名单中途关闭卡死:`ShouldBlock`/`Tick`/`Approve` 解除 `isWhitelisted` 依赖 | §5.4 | 小 | PureMemory 状态机 + Runtime 手测 |
| `Classify` 入口剥离 `:port` | §6.4 | 小 | 纯函数单测(PureMemory) |

### 第三梯队:中工作量(第二梯队之后排期)
| 升级项 | 对应评审条目 | 工作量 | 验证方式 |
|---|---|---|---|
| `ExtractOwner` 反射改预编译委托(`Dictionary<Type, Func<object, Player>>`,注册期构建) | §5.6 | 中 | StaticIL + 性能对比 |
| Collision patch 内部 `EncodeRegion`(int 编码)与 `RegionKey` 的边界统一/文档化,防 Collision 域接线时引爆 | §3.2 | 中 | 纯静态(Ticket 03 契约延伸) |
| `host-session-identity` 增加恢复/重试路径(消除整局失明) | §3.6 | 中 | PureMemory + Runtime |
| 隔离信号周期重申 + widget 位冲突预案 | §5.5 | 中 | Runtime |
| 容量检查前置到握手期(`CanPermitHandshake` 查 `MaxPendingEntries`) | §5.8 | 中 | Runtime(确认第 17 人不再拿全量快照) |
| Animal 域纠偏:删除「为不存在机制建模」的 lifecycle,或按 vanilla 全图 pack 模型重新设计 | §3.1 | 中(含设计决策) | 先 `/grill-with-docs` 决策再实施;纯静态可验证删除 |

### 第四梯队:大工作量 / 记录项(按 Milestone 规划)
| 升级项 | 对应评审条目 | 说明 |
|---|---|---|
| 直连/DNS 补 A2S 预查询恢复 `SteamServerAdvertisement` | §6.2 | 大;恢复服务器信息 UI、工坊校验、版本预检 |
| IPv6 支持 | §6.3 | 中-大;含分类器、DNS 选择器、错误提示 |
| 主机 tick 切片对齐 dedicated(性能) | §4.3 | 与 Multi-Observer 激活程度挂钩,区域常驻越多越紧迫 |
| 载具域(M8,`VehicleManager` dedicated 门) | §4.4 | M8 未开始,先记录 |

### 专段:评审第 8 项(SPI 接线)的现状与兑现纲领剩余条件

评审原文要求:「先把 `SpatialObserverIndex` 真正接进 Plugin.Update,并让至少一个域(Resource 最合适)完整走通 Acquire → Release,再往下铺新域」。

**已完成(静态侧)**:
- SPI 接口正式化(`Core/ControlPlane/SPI/ILifecycleDomainAdapter.cs`、`IStateReplicationAdapter.cs`、`LeaseTicket.cs`)与注册闭合(`RegistrationClosure.cs:108`);
- Resource 全链接线:Ticket 10(`a7147aa`)→ 旧 release 入口退役(`755404c`)→ native data plane bridge(`33ee21f`)→ Ticket 11 静态修复链(`49b819f`/`67e3dd8`/`5049ec3`/`4597adc`/`9a126cd`);
- 容错回归测试 M6P31 已先行落红(预期),取证埋点已构建(取证版 DLL SHA `145BEFAA…`)。

**未兑现(运行时侧)**:
- Resource 域的 Acquire→Release **从未在真实游戏运行时走通一次**:2026-09-02 三端日志显示每次 LeaseAcquire 均 `region-snapshot-failed`(F2),单区域失败整批回滚(F4)、SPI 全程无 lease(F5)、碰撞补丁转回原生路径(F6)——即「接线完成、未通电」;
- 其余 5 域(Zombie/Animal/Item/Building/Collision)已注册进 closure 但无 SPI 驱动,评审「死账本」批评对这些域仍然成立。

**兑现纲领的剩余条件(顺序执行)**:
1. 取证日志定 H1/H2(等待 Host 回传日志,解析 `DescribeAcquireFailure` 嵌入的 message);
2. Ticket 11 步骤④容错修复(单区域隔离),M6P31 转绿;
3. 二次构建 + 全静态门禁 + `/code-review` + 提交;
4. 1 Host + 2 Guest 同 DLL 同 Case-ID 动态测试:证明 SPI 成为 Resource 主驱动路径(lease 真实颁发、碰撞补丁第一条件生效、2 秒滞回/generation 防护/快照增量复制通过、独立审核 PASS);
5. 上述全部成立后,Resource 作为模板域,才谈得上评审说的「再往下铺新域」。

---

## 统计摘要

- 逐条复核 39 条(§一 13 + §二 2 + §三 6 + §四 4 + §五 8 + §六 6;§〇 为汇总视图不计入):
  - **已修:0 条**
  - **部分修:11 条**(其中 7 条为「结构注册/接线完成但未驱动或 Runtime PENDING」;含 §七第 8 项 SPI 纲领的实质进展)
  - **未修:25 条**(含全部 §四 U3DS 缺口与全部 §五/§六 Route B、连接路由缺陷)
  - **随重构失效(等价落地):1 条**(`RegisterLifecycleAdapter` → SPI 接口 + `RegistrationClosure`)
  - **维持(评审认可项未回退):2 条**(§二 2.1/2.2)
- 结论:评审 9 天后,结构侧的回应(SPI 正式化 + Resource 单域接线 + Evidence Class 制度)是真实且可观的;但评审点名的运行时与产品级缺陷(僵尸重生、掉落物、Route B fail-open、连接路由、碰撞误伤房主)**一条都还未修**,而唯一接线的 Resource 域正卡在 Acquire 快照失败上等待取证与容错修复。当前工作重心(Ticket 11 取证轮→修复轮)正是第 8 项纲领能否兑现的关键路径。
