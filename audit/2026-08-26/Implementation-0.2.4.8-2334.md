# Ticket 07 独立复核与阻断修复报告

## 一、执行概述

- 任务：修复 Ticket 07 独立审核阻断，并完成 Animal、Structure、Barricade 与 Pending 领域结构整理的闭环复核。
- 版本：`0.2.4.8`。
- 分支：`codex/structure-baseline-0.2.4`。
- 最终提交：`2875610923f1d7adc07ca39fe6196e769ecc820e`（`test(structure): strengthen ticket 07 static evidence`）。
- 当前标签与已归档版本：未修改。
- 生产代码：未修改；本次提交只增强 Ticket 07 StaticIL 审计接缝。

## 二、问题定位与修复策略

上一轮独立审核发现四类静态证据不足：

1. `InlineSwitch` 解析缺少严格的计数、剩余长度和边界保护；
2. Identity 注册只凭字段名和整数值判断，未证明参数数组实际绑定到目标方法，也未证明 `HarmonyPatchType.Prefix` 的枚举语义；
3. Registration Closure 与 StageCatalog 的同对象数据流、构造来源和关闭失败路径证明不足；
4. `VerifyAll` 返回值与 `RollbackBoth` 的控制流绑定过宽。

已在 `WhitelistTests/StaticIL/AnimalStructureOwnershipStaticILContractTests.cs` 修复：

- 增加受控 `InlineSwitch` 边界解析；
- 校验 Identity 参数字段的声明类型、静态 `Type[]` 实值、`AnimalManager` 实际唯一目标签名，以及 `HarmonyPatchType.Prefix` 的底层值；
- 校验 StageCatalog 由同一 `PatchRegistrationPlan.HarmonyOwner` 构造、局部未被覆盖、两个 `TryClose` 均 fail-closed，并在 Closure/StageCatalog 之后执行 Plan Verify；
- 解析真实分支目标，证明 `VerifyAll=false` 的 fall-through 路径包含 `RollbackBoth`。

## 三、规格符合性

| Ticket 07 要求 | 结论 | 证据 |
|---|---|---|
| Animal、Structure、Barricade 已确认归属并完成结构迁移 | PASS | `Adapters/Animal`、`Adapters/Structure`；Module Ownership Catalog；Migration Manifest |
| Vehicle、Object、OtherPending 逐项登记 Pending | PASS | `docs/architecture/animal-structure-ownership.md`、StaticIL OtherPending 断言、Migration Manifest |
| 不改变生命周期、网络协议、Harmony 元数据、注册顺序、Authority Writer | PASS（静态） | 最终提交 diff 仅触及 StaticIL 测试；Registration Trace 与既有生产入口保持一致 |
| 领域测试、静态注册证据、Manifest 一致 | PASS | `187/187` 回归；Ticket 07 StaticIL；架构文档 |
| Runtime、SP、listen-host、U3DS、P2P 运行时验收 | PENDING | 本票未执行运行时场景，不以静态证据越权替代 |

## 四、构建与测试门禁

| 门禁 | 命令/结果 |
|---|---|
| 主项目 Release | `dotnet msbuild SteamP2PFriends.csproj /t:Rebuild /p:Configuration=Release`；0 errors / 0 warnings |
| 测试项目 Release | `dotnet msbuild WhitelistTests/SteamP2PFriends.WhitelistTests.csproj /t:Rebuild /p:Configuration=Release`；0 errors / 0 warnings |
| 回归测试 | `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe`；`187/187 PASS` |
| 差异检查 | `git diff --check`；PASS |

## 五、最终产物独立指纹

以下值由最终 Release 文件重新计算，均绑定提交 `2875610923f1d7adc07ca39fe6196e769ecc820e`：

| 产物 | SHA-256 | MVID | Assembly/File Version |
|---|---|---|---|
| `bin/Release/SteamP2PFriends.dll` | `575F8486C9D3F10B6291338244ECD89705E990CFFCF0B59633E240F778F9FE2A` | `0dbc1357-44ec-4115-946e-5e6aaf2a1bdf` | `0.2.4.8 / 0.2.4.8` |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `4842B05263BBB46C80A994BE194387ABA0E7BBF08B3EBBBAD0C0C88C6B584F85` | `a3e2c116-d572-44ae-b43b-cb18bf80a9c3` | `0.0.0.0 / 0.0.0.0` |

独立重算命令：`pwsh -NoProfile -ExecutionPolicy Bypass -File audit/2026-08-26/Verify-Ticket04Metadata.ps1`。

## 六、独立审核记录

### 第一轮

- Standards：FAIL。发现四类 StaticIL 证据强度问题。
- Spec：FAIL。发现旧报告指纹未绑定最终工作树。
- 修复：增强 StaticIL 解析与控制流证明；重新 Release 构建和回归测试。

### 第二轮

- Standards：PASS。确认四类阻断已真实修复，无生产行为变化或范围蔓延。
- Spec：FAIL（仅因修复已提交但当时尚未生成绑定当前提交的新交付报告）。
- 修复：提交 `2875610`，重新 Release 重建、回归、指纹复核，并生成本报告。

### 最终轮

- Standards：PASS。确认最终提交仅修改 StaticIL 测试，四类证据接缝有效。
- Spec：PASS。本报告已生成并绑定最终提交 `2875610923f1d7adc07ca39fe6196e769ecc820e`、最终 Release 指纹、`187/187` 回归结果及 Ticket 07 的结构/Manifest/Pending 证据；报告生成未改变源码或产物。

## 七、最终结论

Ticket 07 的代码与静态测试阻断已修复，最终 Release 构建、`187/187` 回归、指纹复核和 `git diff --check` 均已通过。结构迁移范围保持不变，Resource Production Control Seam 未接线，Vehicle/Object/OtherPending 仍为 Pending；Runtime、SP、listen-host、U3DS、P2P 仍需后续独立运行时验收。
