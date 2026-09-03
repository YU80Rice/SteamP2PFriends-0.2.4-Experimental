# Resource / Collision 领域所有权

> 源自结构基线 Ticket 05（已并入 0.2.4.8 基线），票号仅供历史追溯。

## 结构基线

本票只执行行为保持不变的结构迁移。Resource 与 Collision 的物理目录、编译命名空间、注册调用方和测试入口必须指向同一领域所有权；不改变生产行为。

| 领域 | 适配器入口 | 补丁入口 | 责任 |
|---|---|---|---|
| Resource | `SteamP2PFriends.Adapters.Resource` | `SteamP2PFriends.Adapters.Resource.Patches` | Resource 区域生命周期、快照、区域同步、世界同步诊断、采伐/存活状态复制，以及树木/矿石碰撞激活 |
| Collision | `SteamP2PFriends.Adapters.Collision` | `SteamP2PFriends.Adapters.Collision.Patches` | 静态 LevelObject 远端碰撞覆盖与动画策略恢复 |

### 当前入口映射

- `ResourceManagerWorldSyncDiagnosticPatch` → `Adapters.Resource.Patches`；
- `ResourceManagerRegionSyncPatch` → `Adapters.Resource.Patches`；
- `ResourceManagerHarvestReplicationPatch` → `Adapters.Resource.Patches`；
- `LevelGroundRemoteTreeCollisionPatch` → `Adapters.Resource.Patches`；
- `LevelObjectRemoteCollisionPatch` → `Adapters.Collision.Patches`。

`LevelGroundRemoteTreeCollisionPatch` 与 Collision 适配器存在调用关系，但它的原生 target 是
`ResourceSpawnpoint.SetIsActiveInRegion(bool)`，并且判定 Resource 区域生命周期，因此 Resource
是唯一领域归属；该跨领域依赖只允许通过 Collision patch 的公开区域覆盖判定发生，不复制状态。

## 注册与行为不变量

以上补丁仍由 `PatchRegistrationOrchestrator` 的 `U3-REG-01-Wrapper`/`U3-REG-05-WorldSyncAndAdapters`
调用链登记。原有 U3-SDK 区域顺序、Harmony target、patch method、owner、priority 和注册后验证保持不变。
Host 会话复位和断线清理改为引用领域命名空间下的同一实现，不产生第二个入口。

旧 Resource 生产 Authority Writer 仍保持唯一权威；`ResourceDomainAdapter` 仍只是 Registration
Closure 的适配器目录成员，`Production Control Seam` 尚未接线。该结构整理不证明 Singleplayer、
listen-host、U3DS 或 P2P Runtime 行为通过。

## 测试接缝与证据

`WhitelistTests/StaticIL/ResourceCollisionOwnershipStaticILContractTests.cs` 是本票的结构接缝，
通过编译产物验证五个领域补丁各有且仅有一个权威 FullName，并确认五个旧 `Core.Patches` 类型不存在。
Resource 既有 `ResourceRegionLifecycleAdapterTests`、`ResourceSnapshotAdapterTests` 和
`ResourceHarvestReplicationTests` 未复制，只验证原有 PureMemory 行为仍通过。

## 未决项

- Harmony 最终运行排序和各端实际执行仍需 Runtime Evidence；
- 旧 Authority Writer 的退出必须留到 Ticket 10/11，不能由本票提前处理；
- Resource Production Control Seam 的接线、生产状态迁移、旧 writer 退休和双端存档/联机验证均不在本票。
