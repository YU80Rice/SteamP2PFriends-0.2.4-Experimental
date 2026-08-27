# 09: Build Fingerprint 与独立产物关联

**What to build:** 让用户测试日志能够自报告当前加载构建的身份，并让验收人员可以用交付 DLL 独立重算结果验证日志关联。

**Blocked by:** 08 / Evidence Class 测试结构与门禁

**Status:** ready-for-agent

- [x] 运行时 Fingerprint 至少包含 `0.2.4.8`、AssemblyVersion/FileVersion、MVID、DLL SHA-256、插件 GUID 和共享 Case-ID。
- [x] 版本元数据由统一来源传播到程序集、日志、测试和审计输出。
- [x] 自报告 Fingerprint 与 Independent Artifact Verification 在文档和审计中明确区分。
- [x] 日志与交付 DLL 可通过同一 Case-ID 关联，用户无需手工计算 hash。
- [x] BuildArtifact 测试、Release 构建和独立审核通过。
