# 01: 建立 0.2.4.9 迁移基线并固定 Resource 表征行为

**What to build:** 程序集、日志和 Case-ID 都显示 `0.2.4.9-Experimental`，同时把当前已验收的资源需求政策、半径、滞回、代次、retry、补偿和故障结果锁成迁入共享引擎前后必须保持的行为门。不改变生产 Writer，不接线 Collision。

**Blocked by:** None (can start immediately)

**Status:** completed（静态闭环 `audit/2026-09-18/Implementation-0.2.4.9-CollisionTicket01-1235.md`：Metadata Source 授予 0.2.4.9、默认 Case-ID `SPF-0.2.4.9-Experimental-CollisionSlice`、表征门 10 项、Release 双 0/0、290/290 PASS、三门禁 PASS、双轴 round 1–5 最终双 CLEAN；Runtime 归票 09，本票不宣称 Runtime PASS）

- [x] Metadata Source 授予 `0.2.4.9`，发布通道仍为 Experimental；程序集版本、BepInPlugin、默认 Case-ID 与展示文档一致。
- [x] 不创建 git tag，不发 GitHub Release。
- [x] Resource 当前 Demand Policy、半径、滞回、generation、retry、补偿和故障结果有表征测试，作为迁入前后的行为等价门（映射见审计 §6；新增 M6P36–M6P41、SPI05、半径来源契约）。
- [x] 表征测试证明契约，不重新宣称 Resource Runtime PASS（README 明确 0.2.4.9 自身未取三端日志；「已验证」栏限定为 0.2.4.8 已验收构建）。
- [x] 生产 Writer 不变，Collision 不接线（未触碰任何生产源码）。
- [x] BuildArtifact 能独立核验 SHA-256 / MVID / 版本 / Case-ID（`Tools/Verify-BuildFingerprintArtifact.ps1` PASS，并新增测试内独立核验门；顺带修复既有测试因 `-File` 单引号而空过的存量缺陷）。
- [x] 双轴审查 CLEAN；Release 构建 0 error / 0 warning；静态门禁绿。
