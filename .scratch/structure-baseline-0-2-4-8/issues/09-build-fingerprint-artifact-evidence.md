# 09: Build Fingerprint 与独立产物关联

**What to build:** 让用户测试日志能够自报告当前加载构建的身份，并让验收人员可以用交付 DLL 独立重算结果验证日志关联。

**Blocked by:** 08 / Evidence Class 测试结构与门禁

**Status:** completed

- [x] 运行时 Fingerprint 至少包含 `0.2.4.8`、AssemblyVersion/FileVersion、MVID、DLL SHA-256、插件 GUID 和共享 Case-ID。
- [x] 版本元数据由统一来源传播到程序集、日志、测试和审计输出。
- [x] 自报告 Fingerprint 与 Independent Artifact Verification 在文档和审计中明确区分。
- [x] 日志与交付 DLL 可通过同一 Case-ID 关联，用户无需手工计算 hash。
- [x] BuildArtifact 测试、Release 构建和独立审核通过。

## Ticket 09 交付证据

- 统一元数据来源：`Build/Version.props`；版本 `0.2.4.8`，发布通道 `Experimental`，插件 GUID `com.yu80rice.steamp2pfriends`，Default Case-ID `SPF-0.2.4.8-Experimental-StructureBaseline`。
- 运行时自报告实现：`Core/Build/BuildFingerprint.cs`、`SteamP2PFriendsPlugin.cs`；输出版本、AssemblyVersion、FileVersion、MVID、DLL SHA-256、插件 GUID、共享 Case-ID、Case-ID 来源和证据类型。
- 独立产物验证：`Tools/Verify-BuildFingerprintArtifact.ps1`；不调用运行时 Fingerprint，独立读取 DLL、重算 SHA-256，并验证版本、MVID、GUID 和 Case-ID 关联。
- 文档一致性门禁：`Tools/Verify-Ticket09Documentation.ps1`；从 `Build/Version.props` 读取元数据，核验 README、架构文档、Migration Manifest、Ticket 和审计报告。
- 当前 Release DLL 指纹：MVID `3d3b43c5-376d-4dfb-80b8-f4200ebf4911`；SHA-256 `AF4888FF44FCF412CE1BDEC4F320E1A55BB210087529CFC136EEB34A44B100B7`。
- 共享 Case-ID 日志：`audit/2026-08-27/Ticket09-SelfReported-Fingerprint.log`；与当前 DLL 使用同一 `Ticket09-Shared-Case-20260827`，用户无需手工计算 hash。
- Release 构建：主项目与测试项目均 `0 errors / 0 warnings`。
- 完整回归：单一测试入口 `192/192 PASS`，Evidence Class 布局为 `PureMemory 29 / StaticIL 11 / BuildArtifact 1 / Runtime 1`。
- 独立验证：默认 Case-ID 和带共享 Case-ID 日志的 `INDEPENDENT_ARTIFACT_VERIFICATION` 均 PASS；文档门禁输出 `TICKET09_DOCUMENTATION_METADATA_PASS`；`git diff --check` PASS。
- 双轴独立审核：Spec PASS、Standards PASS，无阻断项；最终报告：`audit/2026-08-27/Implementation-0.2.4.8-1234.md`。
- 实现提交：`027c100`（`feat(structure): close ticket 09 build fingerprint evidence`）。

## 范围与未决边界

- 本票未修改当前标签、已归档版本、P2P 通道、SteamID、配置键、Harmony 注册顺序、Authority Writer 或 Resource Production Control Seam。
- Runtime 仍为 `PENDING`；上述构建、静态、BuildArtifact 和文档证据不代表真实 Singleplayer、listen-host、U3DS 或 P2P 运行验收通过。
