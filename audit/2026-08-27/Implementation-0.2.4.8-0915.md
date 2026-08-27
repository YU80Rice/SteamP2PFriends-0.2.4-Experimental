# Ticket 08 实施与独立审核报告

- 版本：0.2.4.8
- 日期：2026-08-27
- 分支：`codex/structure-baseline-0.2.4`
- 范围：Evidence Class 测试结构与门禁；仅调整测试物理归属、执行分类和相关文档
- Runtime：结构阶段保持 `PENDING`

## 一、需求执行概述

完成四类 Evidence Class 的物理测试结构，并保持一个测试项目和一个 `Program.Main` 入口。对此前混合测试中的程序集、Harmony、IL 和 U3 元数据断言进行了重新分类：纯逻辑断言留在 `PureMemory`，结构/产物断言归入 `StaticIL`。未修改生产源码、Resource Production Control Seam、当前标签或已归档版本。

## 二、源码溯源清单

| 需求点 | 落实位置 | 结果 |
|---|---|---|
| 四类物理 Evidence Class | `WhitelistTests/Evidence/{PureMemory,StaticIL,BuildArtifact,Runtime}` | PASS；布局为 `29/11/1/1` 个 C# 文件 |
| 一个测试项目、一个入口 | `WhitelistTests/SteamP2PFriends.WhitelistTests.csproj:138-139`、`WhitelistTests/Program.cs:14-18` | PASS；仅 `Program.cs` 与 `Evidence\**\*.cs` 编译，唯一 `Main` |
| PureMemory 与 StaticIL 不越权 | `WhitelistTests/Evidence/PureMemory/Platform/InventoryUiProjectionTests.cs`、`WhitelistTests/Evidence/StaticIL/Platform/*`、`StaticIL/Adapters/Item/*`、`StaticIL/Security/RouteBApprovalStaticILTests.cs` | PASS；IUI5/6、RC1/2、HC1/2/3、M1I06、M2I09、B11 已迁入 StaticIL |
| 注册闭合与结构不变量 | `WhitelistTests/Evidence/PureMemory/Core/RegistrationClosureTests.cs`、`WhitelistTests/Evidence/StaticIL/*.cs` | PASS；注册闭合在 PureMemory 执行，注册/入口形状在 StaticIL 执行 |
| BuildArtifact 证据 | `WhitelistTests/Evidence/BuildArtifact/BuildArtifactEvidenceTests.cs` | PASS；版本、FileVersion、MVID、GUID、SHA-256 可重算 |
| Runtime 独立边界 | `WhitelistTests/Evidence/Runtime/RuntimeEvidenceStatus.cs`、`WhitelistTests/Program.cs:271-272` | PASS；只输出 SP/listen-host/U3DS/P2P 的 Pending，不升级为通过 |

## 三、代码变更清单

新增或迁移至 `StaticIL`：

- `WhitelistTests/Evidence/StaticIL/Platform/HarmonyCompatibilityAuditTests.cs`
- `WhitelistTests/Evidence/StaticIL/Platform/RemoteCollisionAnimationPolicyStaticILTests.cs`
- `WhitelistTests/Evidence/StaticIL/Platform/InventoryUiProjectionStaticILTests.cs`
- `WhitelistTests/Evidence/StaticIL/Adapters/Item/ItemGenerationAuthorityStaticILTests.cs`
- `WhitelistTests/Evidence/StaticIL/Adapters/Item/ItemObserverReplicationStaticILTests.cs`
- `WhitelistTests/Evidence/StaticIL/Security/RouteBApprovalStaticILTests.cs`

从 `PureMemory` 拆出结构断言并保留纯逻辑断言：

- `WhitelistTests/Evidence/PureMemory/Platform/InventoryUiProjectionTests.cs`
- `WhitelistTests/Evidence/PureMemory/Adapters/Item/ItemGenerationAuthorityAdapterTests.cs`
- `WhitelistTests/Evidence/PureMemory/Adapters/Item/ItemObserverReplicationAdapterTests.cs`
- `WhitelistTests/Evidence/PureMemory/Security/RouteBApprovalTests.cs`
- `WhitelistTests/Program.cs`

同步文档：

- `docs/architecture/evidence-class-test-gates.md`
- `docs/architecture/migration-manifest.md`
- `.scratch/structure-baseline-0-2-4-8/issues/08-evidence-class-test-gates.md`

工作区中既有的其它未跟踪复核材料未纳入本次提交。

## 四、编译与测试验证

### 4.1 Release 构建

```text
dotnet msbuild SteamP2PFriends.csproj /t:Rebuild /p:Configuration=Release
dotnet msbuild WhitelistTests/SteamP2PFriends.WhitelistTests.csproj /t:Rebuild /p:Configuration=Release
```

结果：PASS，主项目和测试项目均 `0 errors / 0 warnings`。

### 4.2 物理布局核验

```text
pwsh -NoProfile -ExecutionPolicy Bypass -File Tools/Verify-EvidenceClassLayout.ps1
```

结果：PASS，输出 `EVIDENCE_CLASS_LAYOUT_PASS`；PureMemory `29`、StaticIL `11`、BuildArtifact `1`、Runtime `1`；单一项目与单一入口检查通过。

### 4.3 完整回归

```text
WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe
```

结果：PASS，`189/189 PASS`，`Failed: 0`。执行顺序为 PureMemory → StaticIL → BuildArtifact → Runtime；Runtime 输出 `PENDING`，不计入 PASS 数量。

### 4.4 产物独立证据

| 产物 | 版本 | SHA-256 | MVID |
|---|---|---|---|
| `bin/Release/SteamP2PFriends.dll` | `0.2.4.8` | `575F8486C9D3F10B6291338244ECD89705E990CFFCF0B59633E240F778F9FE2A` | `0dbc1357-44ec-4115-946e-5e6aaf2a1bdf` |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `0.2.4.8` | `7A5B1632D42FC2D71A16C92CE90B01AE60DEB74149CD81D8C44632F791682357` | 测试入口产物；不作为插件 Runtime 身份证据 |

### 4.5 Diff 检查

`git diff --check`：PASS，无空白错误。仅测试结构、入口和架构/票据文档发生变更；生产代码与 Resource Production Control Seam 未变更。

## 五、独立审核记录

审核按 Standards/Spec 双轴执行，重点复核此前发现的混合分类阻断项：

| 审核项 | 判定 | 证明位置 |
|---|---|---|
| 一个测试项目与一个入口 | PASS | `WhitelistTests/SteamP2PFriends.WhitelistTests.csproj`、`WhitelistTests/Program.cs` |
| 四类物理目录完整 | PASS | `Tools/Verify-EvidenceClassLayout.ps1`、布局核验输出 |
| 混合测试分类诚实 | PASS | PureMemory 不再包含 Mono.Cecil/Harmony Patch/IL 读取；静态断言在 `Evidence/StaticIL` |
| 测试数量与回归完整性 | PASS | 唯一入口 `189/189 PASS` |
| Registration Closure / Identity / Lease / generation / structure coverage | PASS | `RegistrationClosureTests` 与五组既有 StaticIL 契约及新增静态接缝 |
| Runtime 证明边界 | PASS | `RuntimeEvidenceStatus`；Runtime 仍 Pending |
| 范围控制 | PASS | 无生产源码、无 Resource Seam 接线、无标签/归档版本修改 |

独立审核结论：`PASS`。此前“IL/Harmony/程序集结构断言误标 PureMemory”的阻断项已关闭。

## 六、偏离与妥协说明

无行为偏离。为保持既有 `189` 项回归计数，`EvidenceClass Catalog` 与 `Registration Closure` 继续在同一个 PureMemory 门禁调用中执行；二者均属于纯内存闭合契约。没有复制测试实现，仅改变物理归属和执行阶段。

## 七、未决项与后续门禁

- Runtime 仍必须分别完成单人、listen-host、U3DS、P2P 的真实游戏验证，并绑定共享 Case-ID、Host/Client 日志和新产物哈希。
- BuildArtifact 的自报 hash 只能证明当前加载产物可重算；不能替代验收侧独立计算或 Runtime 日志证据。
- Resource Production Control Seam 和旧 Resource Authority Writer 保持原状，留给 Ticket 10/11。

## 八、最终结论

Ticket 08 的结构、分类、构建、回归、布局核验和独立审核均通过，可以提交当前分支。Runtime Pending 不阻塞本结构票关闭，但阻止将本批次解释为真实游戏功能验收通过。
