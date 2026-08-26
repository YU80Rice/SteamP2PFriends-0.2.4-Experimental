# Ticket 03 实施交付报告：Domain Identity、Region Key 与 Bound Key

## 一、需求执行概述

在 `codex/structure-baseline-0.2.4` 分支完成 Ticket 03 的行为保持型身份整理。跨模块注册改用不可变 `DomainId`，二维空间改用 `RegionKey`，一维导航改用独立 `BoundKey`；`LeaseTicket` 建立生命周期轴类型接缝，Zombie/Animal 的空间与快照接缝保持 Bound 隔离。未迁移 Resource Production Control Seam，未改变功能 Authority Writer。

## 二、源码溯源清单

| 需求点 | 落实位置 |
|---|---|
| Domain Id 与显示名称分离 | `Core/Identity/DomainId.cs`、`MultiObserver/SPI/ILifecycleDomainAdapter.cs`、`MultiObserver/SPI/IStateReplicationAdapter.cs` |
| 禁止外部任意构造身份 | `Core/Identity/DomainId.cs:13` 使用 `internal` 构造器；`WhitelistTests/StaticIL/IdentityStaticILContractTests.cs` 验证无公共字符串构造器 |
| 二维 Region Key 集中编码 | `Core/Identity/RegionKey.cs`、`MultiObserver/Spatial/SpatialObserverIndex.cs`、各 Region 领域适配器 |
| 一维 Bound Key 与 Region Key 隔离 | `Core/Identity/BoundKey.cs`、`MultiObserver/SPI/IBoundStateReplicationAdapter.cs`、`Adapters/Zombie/*`、`Adapters/Animal/*` |
| 复合结构身份 | `Core/Identity/BarricadeKey.cs`、`Adapters/Structure/*` |
| 生命周期轴语义正交 | `Core/Identity/LifecycleAxes.cs`、`MultiObserver/SPI/LeaseTicket.cs`；其余领域计数器保留为后续批次迁移项 |
| PureMemory/StaticIL 接缝 | `WhitelistTests/Core/IdentityContractTests.cs`、`WhitelistTests/StaticIL/IdentityStaticILContractTests.cs`、现有 Spatial/Registration 测试入口 |
| 架构记录 | `docs/architecture/domain-identity.md`、`docs/architecture/migration-manifest.md`、`docs/adr/0007-domain-identity-and-lifecycle-axis-contract.md` |

## 三、代码、测试与文档变更

- 新增 `Core/Identity` 的五个值对象/身份源文件。
- 新增 Bound 状态复制 SPI，并将 Animal、Zombie、Resource、Structure、Collision、Item 及 MultiObserver 相关接缝改为显式类型。
- 移除 Region/Bound 的裸整数隐式使用，保留 U3-SDK patch 边界的显式 `int`/`byte` 转换。
- 新增身份 PureMemory 与编译后形状 StaticIL 契约测试，测试入口目标更新为 183 项。
- 更新 Ticket 03、领域身份文档、迁移清单、结构基线和 ADR 0007。

明确未纳入：用户提供的第三方复核报告、其他 Ticket 文件、临时 `.scratch/ticket03-symbols.txt`、既有未提交的 Ticket 01/02 审计报告。

## 四、编译与测试状态

| 门禁 | 命令/结果 |
|---|---|
| 主项目 Release | `dotnet build SteamP2PFriends.csproj --configuration Release --no-restore`；0 errors / 0 warnings |
| 测试项目 Release | `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj --configuration Release --no-restore`；0 errors / 0 warnings |
| 自动化测试 | `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe`；`183/183 PASS` |
| 差异检查 | `git diff --check`；PASS |
| 插件产物 SHA-256 | `B0D2C996FA5B36C2F4E015E6B9C175F5FEC23233634352FADCDDE140788C8729` |
| 测试产物 SHA-256 | `16E26E0EA2D2020BD510C8A228D51E5108CFA17D7E5379A821394652F8EE9E42` |

## 五、独立审核记录

| 轮次 | Standards | Spec | 结果 |
|---|---|---|---|
| 第一轮 | PASS | FAIL | Spec 发现 DomainId 可被外部任意构造，且 ADR 对生命周期轴范围表述过宽 |
| 第二轮 | PASS | PASS | DomainId 构造器收紧为 internal、StaticIL 契约补齐，ADR 0007 收窄为本票据实际接缝 |

非阻断建议：`MultiObserver/Spatial/SpatialObserverIndex.cs` 的原生 `byte bound` 参数可在后续边界清理中改为 `BoundKey`；当前保留是为了不扩大本票据对 U3-SDK patch 边界的行为变更。

## 六、Evidence Class 结论

- PureMemory：PASS。身份编码、sentinel、结构复合键、显示名称分离及现有空间/注册接缝通过测试。
- StaticIL：PASS（Ticket 03 身份契约）。验证无 Region/Bound 隐式转换、无公开任意 DomainId 构造器、Zombie Bound 字段、LeaseTicket 生命周期轴和 SPI 参数形状。
- BuildArtifact：PASS。主项目和测试项目 Release 构建无错误无警告，并记录独立产物哈希。
- Runtime：PENDING。尚未执行 SP、U3DS 或 P2P Host/Guest 运行验证；本票据不宣称功能运行时验收通过。

## 七、保持不变与范围边界

- Harmony target、owner、priority、注册顺序和注册后验证保持 Ticket 01/02 基线；Registration Closure 仍是顶层登记接缝。
- P2P 频道、SteamID、存档、配置键、插件 GUID、状态机语义、当前标签和已归档版本未修改。
- Resource Production Control Seam、旧 Authority Writer 和资源生产行为未迁移。
- 新构建版本为 `0.2.4.8`；历史归档版本冻结。

## 八、最终结论

Ticket 03 已完成源码、PureMemory、StaticIL、BuildArtifact 和独立 Standards/Spec 审核闭环，可提交当前分支。Runtime 仍是后续人工验证门禁，不影响本票据的结构基线交付结论。
