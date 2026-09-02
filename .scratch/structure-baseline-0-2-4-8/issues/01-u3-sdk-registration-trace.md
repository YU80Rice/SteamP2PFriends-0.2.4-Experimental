# 01: U3-SDK Registration Trace 与注册基线

**What to build:** 让维护者能够从插件注册点追溯到 U3-SDK 的真实原生生命周期位置，并据此复核当前注册基线。

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [x] 固定并记录 U3-SDK 对照版本及所有已确认的原生注册/生命周期锚点。
- [x] 完成手工注册点、Harmony 注册点与原生调用链的映射；未确认项明确标记 Pending。
- [x] 记录 target、owner、priority、执行顺序和依赖条件的可复核快照。
- [x] 证明本 ticket 不改变当前生产注册行为、插件标签、配置、协议或归档版本。
- [x] 生成可供后续 Patch Registration Orchestrator 拆分使用的 Registration Trace 结果。

完成证据：`docs/architecture/registration-trace.md` 已建立 65 行逐项规范矩阵；主项目/测试项目 Release 构建均为 0 errors / 0 warnings，测试为 181/181 PASS，`git diff --check` 通过。静态登记证据与 Runtime Pending 保持分离。
