# Ticket 06：Item / Zombie 领域所有权

## 结构基线

本票只执行行为保持不变的结构迁移。Item 与 Zombie 的适配器、补丁、生命周期辅助类型和测试入口必须按领域聚合；不接线新的生产 Authority Writer，也不把目录迁移描述成功能修复。

| 领域 | 适配器入口 | 补丁入口 | 身份与生命周期责任 |
|---|---|---|---|
| Item | `SteamP2PFriends.Adapters.Item` | `SteamP2PFriends.Adapters.Item.Patches` | Item 区域生成资格、区域快照复制、区域同步诊断、生成门和重生入口；继续使用原有 Item 区域/Session generation 语义 |
| Zombie | `SteamP2PFriends.Adapters.Zombie` | `SteamP2PFriends.Adapters.Zombie.Patches` | Bound 生命周期、滞回释放、Zombie 全量/增量快照、生成与状态复制、实体映射诊断 |

`BoundKey`、`RegionKey` 和生命周期轴仍由 `Core.Identity` 统一拥有；本票不复制领域身份类型。Zombie 的导航身份继续使用 `BoundKey`，Item 的原生 byte 区域转换仍停留在补丁边界，以保持 U3-SDK 参数形状和现有 generation 语义。

## 当前入口映射

Item：

- `ItemManagerRegionSyncPatch`；
- `ItemManagerWorldSyncDiagnosticPatch`；
- `AuthoritativeItemGenerationGatePatch`；
- `ItemManagerP0B3PreGeneratePatch`；
- `ItemManagerP0B6RegenerateOnLevelLoadedPatch`。

Zombie：

- `ZombieManagerWorldSyncDiagnosticPatch`；
- `ZombieManagerP0DGenerateZombiesPatch`；
- `ZombieLifecyclePatch`、`ZombieLifecycleState`、`ZombieLifecycleOwnerVerify`；
- `ZombieManagerP0C1SendZombieStatesPatch`；
- `ZombieEntityMappingDiagnosticPatch`。

以上类型各有一个编译产物 FullName，注册、复位、断线清理和关键验证只指向这些领域入口；未保留 `Core.Patches` 或 `Core.Patches.P0E*` 的兼容副本。

## 注册与行为不变量

`PatchRegistrationOrchestrator` 仍是唯一顶层注册入口，`RegistrationClosure` 仍是适配器注册关闭点。U3-SDK 原生 Item step 5、Zombie Bound 生命周期位置、现有 Harmony target、patch method、owner、priority、登记顺序和注册后验证逻辑保持不变。命名空间和物理路径变化不改变：

- Item 生成资格、区域快照、`askItems` 事务、Session reset 和 generation gate；
- Zombie Acquire、Hysteresis Release、Region/Bound 语义、Snapshot token、Connection/Region generation 和 delta sequence；
- P2P 频道、SteamID、配置键、插件 GUID、日志语义、当前标签和已归档版本。

旧 Item/Zombie 生产路径没有新增并行 Authority Writer。Resource Production Control Seam 也未接线。

## 测试接缝与证据

`WhitelistTests/StaticIL/ItemZombieOwnershipStaticILContractTests.cs` 是本票的结构接缝：验证领域适配器和补丁的唯一 FullName，验证旧 Core 命名空间权威类型不存在，并检查 `ModuleOwnershipCatalog` 的 Item/Zombie 记录。

现有 `WhitelistTests/Adapters/Item/*`、`WhitelistTests/Adapters/Zombie/*` 测试仅更新 using/归属路径，不复制行为测试。最终全量入口为 `186/186 PASS`；主项目与测试项目 Release 构建均为 0 errors / 0 warnings。

## 证据边界与未决项

- PureMemory：现有 Item/Zombie 测试通过；新增结构接缝为编译产物静态检查。
- StaticIL：Item/Zombie 唯一入口和旧命名空间缺失检查通过。
- BuildArtifact：Release 产物已重建；独立 hash/MVID 记录应在交付报告中绑定本次提交。
- Runtime：Singleplayer、listen-host、U3DS、P2P 的最终生命周期、状态复制、Harmony 实际排序仍需独立 Runtime Evidence；本票不以构建或静态测试替代运行时证据。

后续功能迁移必须继续遵循“先测试接缝、再迁移功能”的顺序，并保持每个领域单一 Authority Writer。
