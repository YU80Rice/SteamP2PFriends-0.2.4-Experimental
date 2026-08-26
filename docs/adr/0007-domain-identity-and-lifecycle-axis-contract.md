# 0007: Domain Identity 与 Lifecycle Axis Contract

状态：Accepted（Ticket 03，0.2.4.8）

## Context

跨领域注册、空间索引和 Zombie 导航曾经可以通过显示名称、裸 `int` 或裸 `byte` 传递身份。这样会把二维世界区域与一维导航 Bound 混为一谈，也会让会话、连接、区域重建和实体复用的生命周期语义进入同一组原始数值，增加错误复用和过期事件污染的风险。

Ticket 03 需要在不改变既有 Harmony target、注册顺序、P2P 协议、存档格式和生产行为的前提下，建立可逐步迁移的结构基线。

## Decision

1. `DomainId` 是跨模块使用的不可变机器身份；`DomainIds` 是当前领域身份的单一来源。`DisplayName` 只用于诊断和日志，不参与注册或协议判断。
2. `RegionKey` 是二维世界区域的唯一类型，集中负责坐标与 Packed 编码边界；禁止向 `int` 提供隐式转换。
3. `BoundKey` 是 Zombie/Animal 导航 Bound 的唯一类型，`255` 表示无有效 Bound；禁止向 `byte` 提供隐式转换。`RegionKey` 与 `BoundKey` 不互相转换。
4. `BarricadeKey` 表示 `RegionKey + plant` 的复合身份，避免同一区域不同 plant 的状态碰撞。
5. `SessionEpoch`、`ConnectionGeneration`、`RegionGeneration` 和 `EntityGeneration` 的语义保持正交；本票据在 `LeaseTicket` 建立 `SessionEpoch` 与 `RegionGeneration` 的类型接缝，其余领域的计数器按后续批次逐步迁移。
6. `LeaseTicket`、Spatial Index、MultiObserver shadow ledger 以及 Zombie/Animal 的空间身份和快照 Bound 接缝使用上述身份类型；Resource Production Control Seam 不在本决策中迁移。

## Consequences

- 编译器和 StaticIL 契约可以阻止 Region/Bound 的错误混用，并锁定 LeaseTicket 的生命周期轴类型；未迁移领域仍由后续批次负责扩大类型接缝。
- 原生边界仍可保留 U3-SDK 的 `int`/`byte` 形状，但转换点可审计，既有行为保持不变。
- 尚未迁移的领域可能继续在 patch 和领域 ledger 边界使用原生生命周期数值；它们进入新的 Core/SPI 接缝前必须显式转换。
- 本 ADR 不把身份类型的存在等同于运行时行为验证；SP、U3DS 和 P2P Runtime 仍需独立证据。

## Verification

- PureMemory：`WhitelistTests/Core/IdentityContractTests.cs`。
- StaticIL：`WhitelistTests/StaticIL/IdentityStaticILContractTests.cs`，覆盖隐式转换禁用、Zombie Bound 字段以及 LeaseTicket 生命周期轴类型。
- BuildArtifact：主项目与测试项目 Release 构建必须为 0 errors / 0 warnings。
- Runtime：本 Ticket 保持 Pending，不由 PureMemory、StaticIL 或构建结果替代。

## Scope Boundary

本决策不改变插件 GUID、配置键、SteamID、P2P 频道、存档格式、Harmony owner/priority/target、注册顺序、生产 Authority Writer 或当前标签；已归档版本保持冻结。
