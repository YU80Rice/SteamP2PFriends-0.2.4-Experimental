# 05: Resource 与 Collision 结构迁移

**What to build:** 让 Resource 与 Collision 代码、补丁、适配器和测试具有明确的领域所有权，为 Resource 生产接缝迁移提供稳定结构，但暂不改变生产 writer。

**Blocked by:** 02 / Patch Registration Orchestrator 拆分；03 / Domain Id、Region Key 与 Bound Key 规范化

**Status:** ready-for-human

- [x] Resource 与 Collision 的领域边界、命名空间和注册归属明确。
- [x] 目录整理不改变当前 Harmony 目标、补丁顺序、碰撞、采伐和状态复制行为。
- [x] 旧生产 Authority Writer 保持唯一权威，Production Control Seam 暂不接线。
- [x] Resource 现有 PureMemory/StaticIL 测试仍通过，迁移记录包含未决项。
- [x] Release 构建、回归测试和独立审核通过。

完成证据：

- 领域归属与跨领域理由：`docs/architecture/resource-collision-ownership.md`；
- 迁移清单与 Evidence Gate：`docs/architecture/migration-manifest.md` 的 Batch 5；
- 注册入口与 U3-SDK 顺序映射：`docs/architecture/registration-trace.md`；
- 结构 StaticIL 接缝：`WhitelistTests/StaticIL/ResourceCollisionOwnershipStaticILContractTests.cs`；
- 交付与最终复核记录：`audit/2026-08-26/Implementation-0.2.4.8-1825.md`（接续并勘误初版证据汇总）；
- 复核构建：主项目和测试项目 Release 均为 0 errors / 0 warnings，全量测试 `185/185 PASS`，`git diff --check` 通过。

范围说明：Runtime（Singleplayer、listen-host、U3DS、P2P）仍为 Pending；本票不接线 Resource Production Control Seam，不提前退休旧 Authority Writer。
