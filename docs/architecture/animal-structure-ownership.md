# Ticket 07：Animal、Structure、Barricade 与其他领域所有权

## 结论

本票只执行行为保持不变的结构迁移。已能由源码、注册调用方和既有测试接缝共同证明归属的内容进入领域目录；无法证明单一领域归属的内容保留在 `Core/Patches`，并以 `Pending` 状态登记。本票不接线 Resource Production Control Seam，也不改变任何生产 Authority Writer。

## 逐项归属状态

| 领域 | 状态 | 物理权威目录 | 权威入口/补丁 | Registration Trace | 处理 |
|---|---|---|---|---|---|
| Animal | Confirmed | `Adapters/Animal` | `AnimalDomainAdapter`、`Adapters/Animal/Patches/*` | `U3-REG-05-WorldSyncAndAdapters` | 已完成 namespace 与目录一致化 |
| Structure | Confirmed | `Adapters/Structure` | `BuildingDomainAdapter`、`StructureManagerRegionSyncPatch`、`P0EBarricadeLifecycle/*` | `U3-REG-05-WorldSyncAndAdapters` | 已迁移，旧 `Core.Patches` 类型不再编译 |
| Barricade | Confirmed | `Adapters/Structure` | `BuildingDomainAdapter`、`BarricadeManagerRegionSyncPatch`、`BarricadeLifecycle*` | `U3-REG-05-WorldSyncAndAdapters` | 与 Structure 共享 Building 适配器，但补丁入口独立记录 |
| Vehicle | Pending | `Core/Patches` | `VehicleManagerP0C1ReplicationPatch`、`VehicleManagerWorldSyncDiagnosticPatch`、`VehicleEnterDiagnosticPatch` | `U3-REG-05-WorldSyncAndAdapters` / `U3-REG-06-Probes` | 不迁移；需要后续证明生命周期、交互与世界同步的单一边界 |
| Object | Pending | `Core/Patches` | `ObjectManagerRegionSyncPatch`、`ObjectManagerWorldSyncDiagnosticPatch`、`Issue7ObjectBinaryStateDiagnosticPatch` | `U3-REG-05-WorldSyncAndAdapters` | 不迁移；区域同步、世界诊断和二进制状态观察尚未形成可证明的单一适配器边界 |
| Resource | Confirmed（前票） | `Adapters/Resource` | `ResourceDomainAdapter` 与 Resource patches | `U3-REG-05-WorldSyncAndAdapters` | 本票不改动；Production Control Seam 保持未接线 |
| Collision | Confirmed（前票） | `Adapters/Collision` | `LevelObjectCollisionAdapter` 与 collision patch | `U3-REG-05-WorldSyncAndAdapters` | 本票不改动 |
| Item | Confirmed（前票） | `Adapters/Item` | `ItemDomainAdapter` 与 Item patches | `U3-REG-05-WorldSyncAndAdapters` | 本票不改动 |
| Zombie | Confirmed（前票） | `Adapters/Zombie` | `ZombieDomainAdapter` 与 Zombie patches | `U3-REG-05-WorldSyncAndAdapters` | 本票不改动 |
| 其他跨领域诊断/审计 | Pending | `Core/Patches` | `PendingOtherDomainPatchSet` | `U3-REG-02` / `U3-REG-04` / `U3-REG-06` | 保留并登记原因，不伪造领域归属 |

## 已迁移文件边界

- `BarricadeManagerRegionSyncPatch` 和 `StructureManagerRegionSyncPatch` 迁入 `Adapters/Structure/Patches`。
- `P0EBarricadeLifecycle/*` 迁入 `Adapters/Structure/Patches/P0EBarricadeLifecycle`。
- `AnimalManagerP0C2SendAnimalStatesPatch` 和 `AnimalManagerWorldSyncDiagnosticPatch` 的 namespace 与既有 `Adapters/Animal/Patches` 物理目录对齐。
- `Core/Registration/*`、Host 复位、断线清理和关键验证只改为指向上述唯一入口；未建立兼容副本。

## 保持不变

- U3-SDK 原生 Structure step 1、Barricade step 2、Animal 生命周期与既有调用链不变。
- Harmony target、patch method、owner、priority、注册顺序及注册后验证不变。
- P2P 通道、SteamID、配置键、插件 GUID、日志语义、网络协议和生产 Authority Writer 不变。
- Resource Production Control Seam 未接线，结构迁移不宣称功能修复或 Runtime 通过。

## 测试与证据接缝

- 领域 PureMemory 测试继续通过原有入口；未复制生产逻辑测试。
- `AnimalStructureOwnershipStaticILContractTests` 验证领域 FullName 唯一、Animal/Structure/Barricade 旧权威类型缺失、Vehicle/Object 保持 Pending、Registration Trace 覆盖，以及迁移入口的 HarmonyPatch/owner/Registration Closure 形状。
- `docs/architecture/registration-trace.md` 的 U3-SDK 顺序与当前 Registration 模块保持对应；最终 Harmony 执行与 Singleplayer、listen-host、U3DS、P2P Runtime 仍需独立验证。
