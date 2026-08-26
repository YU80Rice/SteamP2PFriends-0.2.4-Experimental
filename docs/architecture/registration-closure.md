# Registration Closure

## 目的

Registration Closure 是当前插件会话中适配器注册完成并转为不可变的时点。它不负责领域行为，也不替代 Harmony 注册实现；它只为注册身份、角色、顺序和关闭状态提供一个可审计的深模块接口。

## 公共接缝

`SteamP2PFriends.Core.Registration.RegistrationClosure` 暴露：

- `TryRegisterLifecycle(ILifecycleDomainAdapter, out string)`；
- `TryRegisterReplication(IStateReplicationAdapter, out string)`；
- `TryClose(out string)`；
- `IsClosed`、`Snapshot` 和关闭后的角色读取。

Lifecycle 与 Replication 是两个独立角色。同一领域可以由同一个适配器实例同时承担两个角色，但不能依靠隐式推断；两个角色都必须显式登记。

## 失败规则

关闭前必须明确拒绝：

- 未声明的 Domain Id；
- 同一 Domain Id 的重复角色；
- Replication 先于 Lifecycle 的注册顺序冲突；
- 要求的角色缺失；
- Closure 关闭后的任何追加登记。

Patch 阶段另由 `PatchRegistrationStageCatalog` 按 `registration-trace.md` 与 Ticket 01 固定 commit 的阶段基线验证递增 Order、category、trace id、Harmony owner、priority 和 target 摘要；现有 `VerifyCriticalPatches` 继续负责实际 Harmony metadata 的注册后验证。

## 当前登记范围

本批次登记 Item、Resource、Building、Zombie、Animal 的 Lifecycle/Replication 角色，以及 Collision 的 Lifecycle 角色。登记本身不调用适配器生产行为，不接管 Resource Production Control Seam。

## 证据边界

Closure 测试属于 `PureMemory` Evidence Class，只证明注册状态机和接口契约。它不能证明 Unity/Harmony 回调已执行、SP/U3DS/P2P 行为正确，也不能替代 `StaticIL`、`BuildArtifact` 或 `Runtime` 证据。
