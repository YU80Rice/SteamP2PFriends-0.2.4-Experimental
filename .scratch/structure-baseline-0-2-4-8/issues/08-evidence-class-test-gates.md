# 08: Evidence Class 测试结构与门禁

**What to build:** 让测试结果按 PureMemory、StaticIL、BuildArtifact 和 Runtime 分类，并为每类证据建立清晰的验证入口和发布解释。

**Blocked by:** 04 / Core、Platform、Security 所有权迁移；05 / Resource 与 Collision 结构迁移；06 / Item 与 Zombie 结构迁移；07 / Animal、Structure 与其他领域结构迁移

**Status:** ready-for-agent

- [x] 测试物理结构按四类 Evidence Class 组织，同时保持一个测试项目和一个测试入口。
- [x] PureMemory、StaticIL、BuildArtifact 和 Runtime 的证明范围不可互相升级替代。
- [x] 结构阶段 Runtime 可为 Pending，但审计与 Migration Manifest 必须明确记录。
- [x] 测试覆盖注册闭合、身份契约、空间租约、generation 和结构不变量。
- [x] Release 构建、完整回归测试、测试分类核验和独立审核通过。

交付证据：`docs/architecture/evidence-class-test-gates.md`、
`WhitelistTests/Evidence/EvidenceClass.cs`、
`WhitelistTests/Evidence/BuildArtifact/BuildArtifactEvidenceTests.cs`、
`WhitelistTests/Program.cs`、
`audit/2026-08-27/Implementation-0.2.4.8-<HHMM>.md`。
