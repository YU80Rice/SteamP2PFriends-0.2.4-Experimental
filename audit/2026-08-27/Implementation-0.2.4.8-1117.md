# Ticket 09 实施与独立审核报告

- 版本：`0.2.4.8` / `Experimental`
- 日期：2026-08-27
- 分支：`codex/structure-baseline-0.2.4`
- 固定基线：`89bb83a`（Ticket 08）
- 范围：Build Fingerprint、统一元数据传播、Case-ID 与独立产物证据
- Runtime：真实游戏 SP、listen-host、U3DS、P2P 仍保持 `PENDING`

## 一、需求执行概述

完成 0.2.4.8 的构建级元数据收敛、运行时自报告 Fingerprint、共享 Case-ID 关联接缝和验收侧 Independent Artifact Verification。未修改当前标签、已归档版本、P2P 协议、SteamID、生产 Authority Writer 或 Resource Production Control Seam。

## 二、源码溯源清单

| 需求 | 落实位置 | 结果 |
|---|---|---|
| Fingerprint 包含版本、Assembly/FileVersion、MVID、DLL SHA-256、GUID、Case-ID | `Core/Build/BuildFingerprint.cs`、`SteamP2PFriendsPlugin.cs` | PASS；Awake 输出自报告字段 |
| 统一元数据源 | `Build/Version.props`、两项目的生成目标、`Properties/AssemblyInfo.cs`、`WhitelistTests/Properties/AssemblyInfo.cs` | PASS；插件和测试 EXE 均为 `0.2.4.8` |
| 实际加载身份校验 | `BuildFingerprint.Capture` | PASS；版本来自 AssemblyMetadata/Assembly，GUID 来自 BepInPlugin，hash 来自加载 DLL |
| Self-reported 与 Independent 分离 | `docs/architecture/build-fingerprint-artifact-evidence.md`、验证脚本 | PASS；两种证据标签不同 |
| Case-ID 关联 | `STEAMP2PFRIENDS_CASE_ID`、`Tools/Verify-BuildFingerprintArtifact.ps1 -ExpectedCaseId -LogPath` | PASS；无日志不会通过，日志需同时匹配 Case-ID、hash、版本和 GUID |
| BuildArtifact/Release/回归门禁 | `WhitelistTests/Evidence/BuildArtifact/*`、构建与测试命令 | PASS |

## 三、代码变更清单

新增：

- `Core/Build/BuildFingerprint.cs`
- `WhitelistTests/Properties/AssemblyInfo.cs`
- `Tools/Verify-BuildFingerprintArtifact.ps1`
- `docs/architecture/build-fingerprint-artifact-evidence.md`
- `audit/2026-08-27/Ticket09-SelfReported-Fingerprint.log`

修改：

- `Build/Version.props`
- `Properties/AssemblyInfo.cs`
- `SteamP2PFriends.csproj`
- `SteamP2PFriendsPlugin.cs`
- `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationCriticalVerification.cs`
- `WhitelistTests/SteamP2PFriends.WhitelistTests.csproj`
- `WhitelistTests/Evidence/BuildArtifact/BuildArtifactEvidenceTests.cs`
- `WhitelistTests/Program.cs`
- `docs/architecture/evidence-class-test-gates.md`
- `docs/architecture/migration-manifest.md`
- `.scratch/structure-baseline-0-2-4-8/issues/09-build-fingerprint-artifact-evidence.md`
- `README.md`

明确排除：工作区中既有的 `.scratch` Ticket 10/11、第三方 `ARCHITECTURE-REVIEW-0.2.4-Experimental.md` 和 2026-08-26 历史 audit 材料均未纳入本票提交；它们仍属于用户保留材料。

## 四、构建与测试验证

### 4.1 Release 构建

```text
dotnet msbuild SteamP2PFriends.csproj /t:Rebuild /p:Configuration=Release /nologo
dotnet msbuild WhitelistTests/SteamP2PFriends.WhitelistTests.csproj /t:Rebuild /p:Configuration=Release /nologo
```

结果：PASS，主项目和测试项目均 `0 errors / 0 warnings`。

### 4.2 完整回归与 Case-ID 接缝

```text
WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe
```

结果：`192/192 PASS`，`Failed: 0`。测试入口同时验证默认 Case-ID 回退、外部共享 Case-ID 覆盖以及测试程序集版本元数据。使用 `STEAMP2PFRIENDS_CASE_ID=Ticket09-Shared-Case-20260827` 的输出已绑定到本报告日志文件。

### 4.3 结构与独立产物验证

```text
pwsh -NoProfile -ExecutionPolicy Bypass -File Tools/Verify-EvidenceClassLayout.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File Tools/Verify-BuildFingerprintArtifact.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File Tools/Verify-BuildFingerprintArtifact.ps1 -ExpectedCaseId Ticket09-Shared-Case-20260827 -LogPath audit/2026-08-27/Ticket09-SelfReported-Fingerprint.log
git diff --check
```

结果：布局 `EVIDENCE_CLASS_LAYOUT_PASS`；默认和带日志的外部 Case-ID 验证均 `INDEPENDENT_ARTIFACT_VERIFICATION_PASS`；diff 检查通过。未提供日志时外部 Case-ID 验证按设计失败。

### 4.4 最终 Release 产物

| 产物 | Version | FileVersion | MVID | SHA-256 |
|---|---|---|---|---|
| `bin/Release/SteamP2PFriends.dll` | `0.2.4.8` | `0.2.4.8` | `900cad98-63fd-46d0-8ac6-f10e49f53145` | `D8157B3140DAB7B6E448807DF4A9341C0FCEE60F054BE0ED3EAB3ED7AB95E21F` |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `0.2.4.8` | `0.2.4.8` | 测试程序集独立 MVID | `011FE80DAC488D262EA8CF99F2EEB54A7A78D058302CB9A510B5B5440F52360D` |

## 五、独立审核记录

| 审核轴 | 轮次 | 结论 | 处理 |
|---|---:|---|---|
| Spec | 初审 | FAIL | 修复外部 Case-ID 误判、实际程序集身份读取、验证脚本元数据来源和旧证据引用 |
| Standards | 第 1 次 | FAIL | 修复测试程序集未独立消费统一元数据、旧审计缺失、HARMONY_ID 漂移、Fingerprint 未接 fail-closed |
| Spec | 最终复审 | PASS | 同一日志行关联、Case-ID、hash、版本和 GUID 已核验 |
| Standards | 最终复审 | PASS | 无阻断项；重复 MSBuild 目标仅为非阻断建议 |

最终双轴独立审核结论：`PASS`。构建产物、日志与 Case-ID 关联证据已通过执行门禁；字段必须来自同一条 Fingerprint 日志记录，缺失日志不能伪造外部 Case-ID 通过；Runtime 仍正确保持 Pending。

## 六、偏离与妥协说明

无行为偏离。为兼容旧式 .NET Framework 项目，程序集属性仍位于各自唯一 `AssemblyInfo.cs`，但所有版本值和构建身份值均来自 `Build/Version.props` 生成的项目级常量。两个项目的 MSBuild 生成目标存在结构重复，属于后续可维护性建议，不阻塞本 Ticket。

## 七、最终结论

Ticket 09 的 Build Fingerprint、统一版本传播、共享 Case-ID 关联、BuildArtifact 测试、Release 构建和双轴独立审核均通过，可以提交当前分支。该结论不等同于真实游戏 Runtime 验收通过。
