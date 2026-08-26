# 07: Animal、Structure 与其他领域结构迁移

**What to build:** 让 Animal、Structure、Barricade 及其他已存在领域完成可追踪的目录、命名空间和注册归属整理。

**Blocked by:** 02 / Patch Registration Orchestrator 拆分；03 / Domain Id、Region Key 与 Bound Key 规范化

**Status:** completed

- [x] Animal、Structure、Barricade、Vehicle 和其他领域的归属状态逐项记录。
- [x] 已确认归属的内容完成结构迁移；无法证明归属的内容保留并标记 Pending。
- [x] 不改变原生生命周期、网络协议、补丁元数据或当前生产 Authority Writer。
- [x] 领域测试、静态注册证据和 Migration Manifest 与实际结构一致。
- [x] Release 构建、回归测试和独立审核通过。

交付证据：`docs/architecture/animal-structure-ownership.md`、
`WhitelistTests/StaticIL/AnimalStructureOwnershipStaticILContractTests.cs`、
`audit/2026-08-26/Implementation-0.2.4.8-2334.md`。
