# Ticket 04 静态元数据与产物快照

## 快照范围

本快照对应 `0.2.4.8 / Experimental`、分支 `codex/structure-baseline-0.2.4` 的 Ticket 04
候选。它是构建产物与源码结构的独立记录，不是运行时验证，也不把插件自报告 Fingerprint
当作独立 hash 证据。

## Release 产物

| 产物 | 路径 | AssemblyVersion | FileVersion | MVID | SHA-256 |
|---|---|---|---|---|---|
| 插件 DLL | `bin/Release/SteamP2PFriends.dll` | `0.2.4.8` | `0.2.4.8` | `1d8655ce-f957-47a4-a2b5-079dc1172da7` | `26E15E51239AD06AEBB322FEFCE52EC9FD571FF18986CA609004B31597C57371` |
| 测试 EXE | `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `0.0.0.0` | `0.0.0.0` | `aa07e1bb-8878-49a2-a760-11e2eb68661e` | `C9C84BF1F0DDC49AEADBE4FF4B507C7AFF26C4EBECF49B5653189F5B4F61787C` |

插件 GUID：`com.yu80rice.steamp2pfriends`。构建元数据源：`Build/Version.props`。

## 静态结构门禁

- `ModuleOwnershipStaticILContractTests.Test_All` 已编入测试入口，验证模块 ID 唯一、唯一
  Registration Orchestrator/Closure、七个 Registration Trace 阶段覆盖，以及编译产物中的
  Security/Registration 关键类型唯一性。
- 源代码树的同名 `.cs` 基线检查无重复文件名；`Core/Patches` 是唯一受控跨领域补丁目录，
  `Security/Patches` 是准入补丁唯一物理入口。
- Registration Trace 的 71 行矩阵包含 65 个显式 `RegisterManual/RegisterAtomically` 注册单元，
  每行静态记录 `order`、精确 Harmony target、patch type/method、owner、priority、依赖和
  证据等级；这组字段是本票的静态元数据快照，不是运行时自报告。

### 模块与静态注册元数据覆盖

| 物理模块 | 唯一物理入口 | 静态注册入口 | Trace 覆盖 | 静态字段 |
|---|---|---|---|---|
| Core / ControlPlane | `Core/ControlPlane/` | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDomainModules.cs` | `U3-REG-05`，矩阵 Orders 01-57 的世界同步/适配器项 | order、target、patch type/method、owner、priority |
| Core / Registration | `Core/Registration/` | `PatchRegistrationOrchestrator` | `U3-REG-01..07`，矩阵阶段入口映射 | stage order、owner、priority、Harmony target 摘要 |
| Core / Cross-Domain | `Core/Patches/` | `PatchRegistrationTransportModules`、`PatchRegistrationLegacyDiagnostics` | `U3-REG-01/02/04/06`，矩阵 Orders 01-57 | order、target、patch type/method、owner、priority |
| Platform / Transport | `Platform/Transport/` | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationTransportModules.cs` | `U3-REG-01`、`U3-REG-02/06` Transport rows | order、target、owner、priority |
| Platform / Diagnostics | `Platform/Diagnostics/` | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs`、`PatchRegistrationLegacyDiagnostics` | `U3-REG-02/04/06`，矩阵 Diagnostic rows | order、target、patch type/method、owner、priority |
| Security | `Security/` | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationSecurityModules.cs` | `U3-REG-03`，矩阵 Orders 58-60 | order、target、patch type/method、owner、priority |

静态覆盖结论：上述模块的注册元数据均落在同一份 Registration Trace 矩阵中，未另设第二
份 target/owner/priority/order 真相；`ModuleOwnershipStaticILContractTests` 再验证模块 ID、
Orchestrator、Closure 和 Security/Registration 编译类型的唯一性。`order` 表示源码注册调用
顺序；Harmony 在真实游戏中对最终补丁的运行排序仍是 Runtime 证据，不能由本快照替代。

### 65 个注册单元的当前入口绑定

| Order 范围 | 当前源码文件与范围 | 覆盖字段 |
|---|---|---|
| 01-11 | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationTransportModules.cs:26-194` | 每个单元的 order、target、patch type/method、owner、priority |
| 12-57 | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs:26-539` | 每个单元的 order、target、patch type/method、owner、priority |
| 58-60 | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationSecurityModules.cs:11-19` | 每个单元的 order、target、patch type/method、owner、priority |
| 61-64 | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationAuditModules.cs:26-240` | 每个单元的 order、target、patch type/method、owner、priority |
| 65 | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDomainModules.cs:15-44` | order、target、patch type/method、owner、priority、原子回滚约束 |

因此，静态快照覆盖 Core、Platform/Transport、Platform/Diagnostics、Security 及受控
跨领域补丁的全部当前注册入口；Registration Trace 中的 65 行仍保留旧文件行号，只为
保持 Ticket 01 的可追溯性，当前入口以本表和 §1.1 为准。

## 复核命令

```text
dotnet msbuild SteamP2PFriends.csproj /t:Rebuild /p:Configuration=Release /v:minimal
dotnet msbuild WhitelistTests/SteamP2PFriends.WhitelistTests.csproj /t:Rebuild /p:Configuration=Release /v:minimal
WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe
pwsh -ExecutionPolicy Bypass -File audit/2026-08-26/Verify-Ticket04Metadata.ps1
git diff --check
```

结果：主项目与测试项目 `0 errors / 0 warnings`；测试 `184/184 PASS`；`git diff --check`
通过。SHA-256 为验收侧对文件重新计算的独立值。
MVID 使用该报告目录中的 `Verify-Ticket04Metadata.ps1` 通过 PE metadata 读取器提取，
不依赖加载游戏程序集或其运行时依赖。

## 证据边界

本快照不证明 Singleplayer、listen-host、U3DS 或 P2P Runtime 行为；这些路径保持 Pending。
Resource Production Control Seam 和旧 Authority Writer 也不在本票迁移范围内。
