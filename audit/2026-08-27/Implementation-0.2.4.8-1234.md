# Ticket 09 最终循环审计报告：Build Fingerprint 与独立产物证据

- 版本：`0.2.4.8` / `Experimental`
- 分支：`codex/structure-baseline-0.2.4`
- 审计固定点：`89bb83a`
- 元数据来源：`Build/Version.props`
- 插件 GUID：`com.yu80rice.steamp2pfriends`
- Default Case-ID：`SPF-0.2.4.8-Experimental-StructureBaseline`
- Runtime：`PENDING`；本报告不代表真实 SP、listen-host、U3DS 或 P2P 验收通过

## 一、审计范围与修复

本轮只审计 Ticket 09：运行时自报告 Fingerprint、统一构建元数据、共享 Case-ID、独立 DLL 验证、BuildArtifact 证据和文档一致性。未修改当前标签、已归档版本、P2P 通道、SteamID、配置键、Harmony 注册顺序、Authority Writer 或 Resource Production Control Seam。

初次 Spec 复审发现 README 未明确区分旧版本运行证据；已将相关章节改为“历史运行证据（不属于 0.2.4.8 当前验收）”，并明确当前 Runtime 仍为 Pending。

初次 Standards 复审发现两个阻断项：

1. 注册验证日志/返回值使用旧的 `allOk`，可能与 Fingerprint fail-closed 状态矛盾；已统一改为最终的 `DiagnosticBuildValid`。
2. 文档与审计输出缺少由 `Build/Version.props` 驱动的一致性门禁；已新增 `Tools/Verify-Ticket09Documentation.ps1`，校验 README、架构文档、Migration Manifest、Ticket 和本最终报告。

## 二、当前构建产物证据

| 字段 | 当前值 |
|---|---|
| Version / AssemblyVersion / FileVersion | `0.2.4.8` |
| MVID | `3d3b43c5-376d-4dfb-80b8-f4200ebf4911` |
| DLL SHA-256 | `AF4888FF44FCF412CE1BDEC4F320E1A55BB210087529CFC136EEB34A44B100B7` |
| Plugin GUID | `com.yu80rice.steamp2pfriends` |
| Shared Case-ID | `Ticket09-Shared-Case-20260827` |
| Embedded Default Case-ID | `SPF-0.2.4.8-Experimental-StructureBaseline` |

自报告日志：`audit/2026-08-27/Ticket09-SelfReported-Fingerprint.log`。该日志与当前 DLL 使用同一共享 Case-ID；独立脚本重新读取 DLL 并重算 SHA-256，不调用运行时 Fingerprint。

## 三、最终验证

```text
dotnet msbuild SteamP2PFriends.csproj /t:Rebuild /p:Configuration=Release /nologo
dotnet msbuild WhitelistTests/SteamP2PFriends.WhitelistTests.csproj /t:Rebuild /p:Configuration=Release /nologo
WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe
pwsh -NoProfile -ExecutionPolicy Bypass -File Tools/Verify-EvidenceClassLayout.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File Tools/Verify-BuildFingerprintArtifact.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File Tools/Verify-BuildFingerprintArtifact.ps1 -ExpectedCaseId Ticket09-Shared-Case-20260827 -LogPath audit/2026-08-27/Ticket09-SelfReported-Fingerprint.log
pwsh -NoProfile -ExecutionPolicy Bypass -File Tools/Verify-Ticket09Documentation.ps1
git diff --check
```

结果：主项目/测试项目 Release 构建 `0 errors / 0 warnings`；回归 `192/192 PASS`；证据布局 `PureMemory 29 / StaticIL 11 / BuildArtifact 1 / Runtime 1`；默认和外部 Case-ID 独立产物验证均 `INDEPENDENT_ARTIFACT_VERIFICATION_PASS`；文档一致性 `TICKET09_DOCUMENTATION_METADATA_PASS`；`git diff --check` PASS。

## 四、双轴独立审核循环

| 轮次 | Spec | Standards | 处理 |
|---|---|---|---|
| 初审 | FAIL | PASS | 修复 README 历史证据边界 |
| 第二轮 | PASS | FAIL | 修复 DiagnosticBuildValid 日志/返回值和文档元数据一致性门禁 |
| 最终轮 | PASS | PASS | 无阻断项 |

非阻断建议：两个 `.csproj` 的 BuildMetadata 生成目标可在后续提取为共享 `.targets`；进程级 Case-ID 测试在未来并行化时应增加隔离；后续 DLL 或源码变化后必须重新生成同一 Case-ID 的新日志证据。

## 五、最终结论

Ticket 09 的 Spec 与 Standards 双轴循环审计均 PASS，BuildArtifact、Release 构建、192 项回归、独立产物核验和文档一致性门禁均通过。Runtime 仍为 `PENDING`，不升级为真实游戏运行验收 PASS。待用户宣布 Ticket 09 正式关闭。
