# 04: Core、Platform、Security 所有权迁移

**What to build:** 让核心控制面、平台传输/UI/诊断和 Security 责任各自拥有清晰边界，同时保持现有 P2P 与准入行为。

**Blocked by:** 02 / Patch Registration Orchestrator 拆分；03 / Domain Id、Region Key 与 Bound Key 规范化

**Status:** completed

- [x] Core、Platform、Transport、Security 和 Diagnostics 的模块归属明确且无重复权威入口。
- [x] 跨领域补丁仅在无法证明领域归属时保留在受控跨领域位置，并登记原因。
- [x] P2P 通道、SteamID、配置键、插件 GUID、准入状态机和现有日志语义保持不变。
- [x] Registration Trace 能覆盖迁移后的注册入口。
- [x] Release 构建、回归测试、静态元数据快照和独立审核通过。

## 验收关单(2026-09-11,用户批量批准)

- Runtime 确认:P2P 连接、Route B 准入、SteamID 语义、配置键、插件 GUID 与日志字段在 2026-09-07/09-11 多轮三端测试中全部工作正常(`audit/2026-09-07/Runtime-0.2.4.8-Ticket11-2343.md`、`audit/2026-09-11/Runtime-0.2.4.8-Ticket12-0031.md`);独立审核 PASS(`audit/2026-09-09/IndependentReview-0.2.4.8-Ticket11-2337.md`)。
- 边界:经 Resource 链验收与三端回归**间接确认**;Security 专项(审核超时/撤销路径)未做三端矩阵。
- 状态:ready-for-human → completed(用户 2026-09-11 验收)。
