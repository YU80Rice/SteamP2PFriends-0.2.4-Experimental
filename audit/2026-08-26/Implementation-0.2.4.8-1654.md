# Ticket 04 执行报告：Core / Platform / Security 模块归属

## 一、需求执行概述

完成 0.2.4.8 结构基线 Ticket 04：将 Core 控制面/共享边界、Platform 外部运行时接入、
Security 准入实现和受控跨领域补丁整理为单一物理权威入口；保持生产行为不变，不迁移
Resource Production Control Seam，不修改当前标签或已归档版本。

## 二、源码溯源清单

| 需求点 | 落实位置 |
|---|---|
| Core 控制面、共享、注册与受控跨领域补丁归属 | `Core/ControlPlane/`、`Core/Shared/`、`Core/Registration/`、`Core/Patches/` |
| Platform Client/Host/Transport/UI/Diagnostics 归属 | `Platform/Client/`、`Platform/Host/`、`Platform/Transport/`、`Platform/UI/`、`Platform/Diagnostics/` |
| Security 唯一准入入口 | `Security/P2PApprovalManager.cs`、`Security/P2PWhitelistService.cs`、`Security/Patches/` |
| 跨领域保留登记 | `Core/Ownership/ModuleOwnershipCatalog.cs`、`docs/architecture/module-ownership.md` |
| 注册入口追踪 | `docs/architecture/registration-trace.md` §1.1；实际入口为 `Core/Registration/*` |
| 静态结构契约 | `WhitelistTests/StaticIL/ModuleOwnershipStaticILContractTests.cs`，测试入口 `T04 ModuleOwnershipStaticIL` |
| 结构决策与术语 | `CONTEXT.md`、`docs/adr/0008-core-platform-security-module-ownership.md` |

## 三、代码变更清单

- 根级 `Patches/` 迁移至 `Core/Patches/`；`MultiObserver/`、`Shared/` 迁移至 `Core/*`。
- `Client/`、`Host/` 迁移至 `Platform/Client/`、`Platform/Host/`；Platform Transport/UI/Diagnostics
  目录作为物理归属边界保留。
- Route B 的 approval、whitelist 和准入补丁迁移至 `Security/`，未保留第二份生产实现。
- 新增 `Core/Ownership/ModuleOwnershipCatalog.cs` 及结构 StaticIL 契约测试。
- 更新 `.csproj` 编译路径、结构文档、迁移清单、Registration Trace、CONTEXT 与 ADR-0008。
- 新增 `docs/architecture/ticket-04-static-metadata-snapshot.md`，记录独立产物快照。

## 四、保持不变与范围边界

- P2P 通道、SteamID、配置键、插件 GUID `com.yu80rice.steamp2pfriends`、Route B 状态机和既有日志语义未改动。
- Registration Orchestrator 仍是唯一顶层注册编排入口；Registration Closure 仍是唯一适配器关闭入口。
- 旧兼容 namespace 暂保留以控制迁移风险；不创建第二份实现或第二个注册入口。
- Resource Production Control Seam 和旧 Authority Writer 未迁移；SP、U3DS、P2P Runtime 未执行，保持 Pending。

## 五、编译与自测状态

| 验证项 | 结果 |
|---|---|
| `dotnet msbuild SteamP2PFriends.csproj /t:Rebuild /p:Configuration=Release /v:minimal` | 0 errors / 0 warnings |
| `dotnet msbuild WhitelistTests/SteamP2PFriends.WhitelistTests.csproj /t:Rebuild /p:Configuration=Release /v:minimal` | 0 errors / 0 warnings |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `184/184 PASS` |
| `git diff --check` | PASS |
| 当前标签/归档版本 | 未修改 |

## 六、静态元数据与产物快照

| 产物 | AssemblyVersion | FileVersion | MVID | SHA-256 |
|---|---|---|---|---|
| `bin/Release/SteamP2PFriends.dll` | `0.2.4.8` | `0.2.4.8` | `1d8655ce-f957-47a4-a2b5-079dc1172da7` | `26E15E51239AD06AEBB322FEFCE52EC9FD571FF18986CA609004B31597C57371` |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `0.0.0.0` | `0.0.0.0` | `aa07e1bb-8878-49a2-a760-11e2eb68661e` | `C9C84BF1F0DDC49AEADBE4FF4B507C7AFF26C4EBECF49B5653189F5B4F61787C` |

以上 SHA-256 为验收侧对当前 Release 文件重新计算的独立值；它们不等同于运行时自报告。
MVID 由 `audit/2026-08-26/Verify-Ticket04Metadata.ps1` 使用 `System.Reflection.Metadata`
读取 PE metadata 重新提取；该脚本只读且不加载插件依赖。
完整快照见 [`ticket-04-static-metadata-snapshot.md`](../../docs/architecture/ticket-04-static-metadata-snapshot.md)。

## 七、独立审核记录

| 轮次 | 结果 | 处理 |
|---|---|---|
| 第 1 轮 Spec | FAIL | 指出 Trace 仍只有旧行号、快照证据不足、缺少 Ticket 04 独立报告；已补齐迁移入口映射、独立快照和本报告 |
| 第 1 轮 Standards | FAIL | 指出结构基线文档仍含旧“不移动源码”表述；已更新当前结构，并补充 CONTEXT 与 ADR-0008 |
| 第 2 轮 | Spec FAIL（1 项）；Standards FAIL（1 项） | Spec 要求静态快照明确覆盖各模块 target/owner/priority/order；Standards 要求快照与最终产物严格一致；已绑定 71 行/65 单元矩阵，并在最后一次构建后重新计算 MVID/SHA-256 |
| 第 3 轮 | Standards FAIL（2 项）；Spec FAIL（2 项） | 发现最终产物快照绑定/完整模块元数据说明仍需复核；已在最后一次构建后重算产物，并将 65 个注册单元按当前入口文件范围绑定；Trace 表已改为当前入口 |
| 第 4 轮 | PASS | Standards：无阻断项；Spec：产物元数据脚本复核一致，65 行矩阵与 Orders 01-65 均绑定当前 `Core/Registration` 入口 |

## 八、最终结论

Ticket 04 的结构、构建、回归测试、静态元数据快照和两轴独立审核均已完成并通过，
可提交当前分支。Runtime 验证不属于本轮已完成证据，继续保持 Pending。
