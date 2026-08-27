# Ticket 09：Build Fingerprint 与独立产物证据

## 目的

让加载中的 `SteamP2PFriends.dll` 在启动日志中自报告完整身份，并让验收人员可以对交付 DLL 独立重算同一组产物证据。运行时自报告不等同于独立验证，也不能把静态产物证据升级为真实游戏 Runtime 通过。

## 统一元数据来源

`Build/Version.props` 是当前实验版本的唯一构建元数据源，当前值为：

| 字段 | 值 |
|---|---|
| Version | `0.2.4.8` |
| Release channel | `Experimental` |
| Plugin GUID | `com.yu80rice.steamp2pfriends` |
| Default Case-ID | `SPF-0.2.4.8-Experimental-StructureBaseline` |

主项目和 `WhitelistTests` 项目都在 `CoreCompile` 前从该 props 生成本项目的 `BuildMetadata` 常量；各自的 `AssemblyInfo.cs` 用这些常量设置 AssemblyVersion、AssemblyFileVersion、InformationalVersion 和 AssemblyMetadata。`BepInPlugin`、运行时 Fingerprint、测试和审计记录均消费同一来源，测试还验证两项目的版本值一致。

## Runtime Self-Reported Fingerprint

`SteamP2PFriends.Core.Build.BuildFingerprint.Capture` 从已加载程序集和 DLL 文件读取并输出：

- `version`、`assemblyVersion`、`fileVersion`；
- `mvid`；
- `dllSha256`；
- `pluginGuid`；
- `caseId` 与 `caseIdSource`；
- `buildCaseId`（DLL 内嵌的构建关联键）；
- `evidence=self-reported`。

插件在 `Awake` 完成日志边界初始化后输出 `[BuildFingerprint]`；若快照不完整或采集异常，会将 `BuildFingerprintValid` 和最终 `DiagnosticBuildValid` 置为 false，使既有 P2P 入口保持 fail-closed。`STEAMP2PFRIENDS_CASE_ID` 可由 Host 与 Guest 共同设置为同一个安全字符集 Case-ID；未设置或格式非法时使用嵌入构建的 Default Case-ID。该环境变量不是新的 BepInEx 配置键。

## Independent Artifact Verification

`Tools/Verify-BuildFingerprintArtifact.ps1` 从 `Build/Version.props` 取得验收期望值，同时只读取交付 DLL 并独立取得 FileVersion、程序集版本、MVID、AssemblyMetadata 中的插件 GUID 和 SHA-256，输出 `evidence=independent-artifact-verification`。它不读取运行时日志，也不调用 `BuildFingerprint.Capture`，因此不能被自报告结果自证。

默认 Case-ID 已嵌入程序集元数据。若一次双端场景使用外部 `STEAMP2PFRIENDS_CASE_ID`，验收人员应将同一值传给脚本的 `-ExpectedCaseId`，并通过 `-LogPath` 提供包含该 Case-ID、版本、GUID 和 DLL SHA-256 的自报告日志；这些字段必须出现在同一条 Fingerprint 日志记录中，且与独立重算 hash 同时匹配时，脚本才输出关联通过。缺少日志或只传入一个 Case-ID 会失败，脚本仍会同时保留 DLL 内嵌 Default Case-ID。

## 测试接缝与证据边界

| 接缝 | 位置 | 证明 |
|---|---|---|
| Fingerprint 字段完整性 | `WhitelistTests/Evidence/BuildArtifact/BuildArtifactEvidenceTests.cs` | 版本、FileVersion、MVID、GUID、Case-ID 与 DLL hash 可读取 |
| hash 独立重算 | 同上 | 测试侧重新读取文件流计算 SHA-256，并与 Fingerprint 比对 |
| 共享 Case-ID | 同上 | 验证合法环境 Case-ID 被双方可复用地读取，非法值回退到构建元数据 |
| 交付 DLL 独立复核 | `Tools/Verify-BuildFingerprintArtifact.ps1` | 不依赖运行时自报告，输出独立产物证据 |

BuildArtifact PASS 只证明当前交付产物的身份可追踪；Runtime 仍需真实 SP、listen-host、U3DS 和 P2P 日志及共享 Case-ID 验收。
