# 0.2.4-Experimental 结构基线

状态：Ticket 01-04 源码批次完成；Runtime 与完整 StaticIL 门禁仍待执行

## 目标

本基线为 SteamP2PFriends 建立行为保持不变的仓库与插件结构。它先固定领域身份、版本来源、U3-SDK 对照基线、证据类别、注册追踪和迁移批次，再允许具体领域逐步迁移到 Production Control Seam。

## 本批次范围

- 版本值固定为 `0.2.4.8`，发布通道保持 `Experimental`；
- U3-SDK 对照 commit 固定为 `ea7b4973af5ba10f62baad2bfde36ab2e5b060eb`；
- 主项目与测试项目共同导入 `Build/Version.props`；
- 建立 Registration Trace 与 Migration Manifest；
- 按 Ticket 02-04 的迁移清单整理物理源码目录；兼容 namespace 暂不全量重命名；不接入 Resource 生产控制接缝；
- 不修复 Zombie、Item、Route B、连接路由或其他功能问题。

## 目标结构

```text
Core/
  ControlPlane/
  Identity/
  Lifecycle/
  Registration/
  Shared/
  Ownership/
  Patches/                 # 受控跨领域补丁唯一入口

Adapters/
  Resource/ Zombie/ Item/ Structure/ Animal/ Collision/

Platform/
  Client/ Host/ Transport/ UI/ Diagnostics/

Security/

Tests/
  PureMemory/
  StaticIL/
  BuildArtifact/
  Runtime/
```

当前已完成 `Core/Identity`、`Core/ControlPlane`、`Core/Shared`、`Platform/*`、`Security`、`Adapters/Animal`、`Adapters/Zombie` 的本批次归属整理。无法证明单一领域归属的补丁统一位于 `Core/Patches`，并由 `docs/architecture/module-ownership.md` 登记原因。

## 不变量

结构批次不得改变 Harmony target、owner、priority、执行顺序、P2P 频道、SteamID、存档、配置键、插件 GUID、状态机语义或当前发布标签。每个 Migration Slice 只能有一个 Authority Writer。

## 证据门禁

结构批次分别记录 `PureMemory`、`StaticIL`、`BuildArtifact` 和 `Runtime`。本批次要求前 3 类可识别并可复核；`Runtime` 可以未执行，但必须显式标记为未完成，不能由构建或 ledger PASS 代替。

## 相关记录

- [Registration Trace](./registration-trace.md)
- [Migration Manifest](./migration-manifest.md)
- [ADR-0006](../adr/0006-behavior-preserving-structure-baseline-and-resource-migration.md)
- [ADR-0008](../adr/0008-core-platform-security-module-ownership.md)
