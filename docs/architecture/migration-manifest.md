# 0.2.4-Experimental Migration Manifest

## Batch 1：Structure Baseline and Metadata Source

状态：第一批完成，Runtime 未执行

分支：`codex/structure-baseline-0.2.4`

版本：`0.2.4.8` / `Experimental`

U3-SDK：`ea7b4973af5ba10f62baad2bfde36ab2e5b060eb`

### 变更清单

| 项目 | 状态 | 说明 |
|---|---|---|
| `Build/Version.props` | 新增 | 建立版本与 SDK commit 的构建元数据来源 |
| 主 `.csproj` 导入版本文件 | 已验证 | Release 构建成功，不改变现有编译项和输出路径 |
| 测试 `.csproj` 导入版本文件 | 已验证 | Release 构建成功，保持单项目、单测试入口 |
| `docs/architecture/structure-baseline.md` | 新增 | 目标结构与行为不变量 |
| `docs/architecture/registration-trace.md` | 新增 | U3-SDK 原生生命周期锚点 |
| 本文件 | 新增 | 批次追踪与证据状态 |
| 源码目录/namespace | 未开始 | 本批次明确不移动 |
| Resource 生产控制接缝 | 未开始 | 后续行为迁移阶段 |
| 旧归档版本 | 冻结 | 不回写、不修改 |
| 第三方复核报告 | 保留未跟踪 | 不纳入本批次提交 |

### 行为保持检查

- Harmony target/owner/priority/order：待构建后静态快照核对；
- 插件 GUID、配置键、协议和当前标签：未计划改变；
- 当前旧 Authority Writer：保持生产权威；
- Runtime：本批次未执行，不得宣称功能修复完成。

### 后续批次

1. 拆分 Patch Registration Orchestrator；
2. 按 Domain Ownership 迁移目录和 namespace；
3. 建立 Evidence Class 测试与 Build Fingerprint 门禁；
4. Resource 通过 Production Control Seam 完成行为迁移；
5. 旧 Authority Writer 退出并完成 Host/Client Runtime 验证。

## Batch 2：Patch Registration Orchestrator

状态：代码、构建与纯内存测试完成；Runtime 未执行；等待人工运行验收

### 变更清单

| 项目 | 状态 | 说明 |
|---|---|---|
| `Core/Lifecycle/SteamP2PFriendsPlugin.PatchRegistry.cs` | 已拆薄 | 仅保留生命周期相关逻辑与顶层注册委托，不再承载完整注册/验证实现 |
| `Core/Registration/PatchRegistrationOrchestrator.cs` | 新增 | 按既有 Registration Trace 编排阶段、关闭适配器注册和触发最终验证 |
| `Core/Registration/RegistrationClosure.cs` | 新增 | 纯内存 Domain Id、Lifecycle/Replication 角色、重复/未知/缺失/关闭后变更校验 |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationTransportModules.cs` | 新增 | Transport 手工注册模块；保留原调用块顺序 |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationSecurityModules.cs` | 新增 | Route B 安全注册模块 |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs` | 新增 | 资产审计与诊断阶段编排模块 |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | 新增 | 原有诊断补丁登记调用块，保持既有登记顺序 |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDomainModules.cs` | 新增 | 领域补丁、生命周期/复制适配器登记与 Closure 接入 |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationAuditModules.cs` | 新增 | 资产完整性与审计补丁登记实现 |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationHarmonyVerification.cs` | 新增 | Harmony target、owner、数量与签名验证辅助实现 |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationCriticalVerification.cs` | 新增 | 关键注册结果聚合与 fail-closed 诊断门 |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationVerification.cs` | 新增 | Route B、统一连接、单端口及附加注册验证入口 |
| `WhitelistTests/Core/RegistrationClosureTests.cs` | 新增 | 通过公共 Closure seam 覆盖角色、重复、未知、缺失、顺序冲突和不可变性 |
| `docs/architecture/registration-closure.md` | 新增 | Closure 接口、顺序和证据规则 |

### 行为保持与边界

- U3-SDK 注册顺序仍由 `registration-trace.md` 的实际调用链定义；编排阶段只包裹既有调用，不按抽象领域顺序重排 Harmony 登记。
- Harmony target、owner、priority、既有手工登记方法和注册后验证逻辑保持原实现；本批次未改 Patch target 或 patch method。
- `ResourceDomainAdapter` 仅进入适配器登记目录；Resource Production Control Seam、旧 Authority Writer 和生产写入权未迁移。
- `SteamP2PFriendsPlugin` 的存档复用、P2P 协议、SteamID、配置键、GUID、当前标签和归档版本均未修改。

### Evidence Gate

| Evidence Class | 结果 | 证据 |
|---|---|---|
| PureMemory | PASS | RegistrationClosure seam 场景并入现有测试入口 |
| StaticIL | PENDING | 需后续静态 target/IL 快照门禁确认 |
| BuildArtifact | PASS | 主项目、WhitelistTests Release 构建 0 errors / 0 warnings |
| Runtime | PENDING | 未执行 SP、U3DS 或 P2P 运行验证 |
