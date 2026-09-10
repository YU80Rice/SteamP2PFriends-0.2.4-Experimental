# Audit Report Index（审计报告索引）

审计报告按 `audit/YYYY-MM-DD/` 归档，文件名格式见 `docs/agents/issue-tracker.md` 的
audit 小节。本索引提供「票据（Ticket）→ 交付报告」的查找映射。

## 结构基线 0.2.4.8（Ticket 01–12）

| Ticket | 交付/最终报告 | 补充与修复 |
|---|---|---|
| 01 U3-SDK Registration Trace 与注册基线 | `2026-08-26/Implementation-0.2.4.8-1154.md` | `2026-08-26/Implementation-0.2.4.8-1033.md`（结构基线第一批实施） |
| 02 Patch Registration Orchestrator | `2026-08-26/Implementation-0.2.4.8-1420.md` | — |
| 03 Domain Identity、Region Key 与 Bound Key | `2026-08-26/Implementation-0.2.4.8-1551.md` | — |
| 04 Core / Platform / Security 模块归属 | `2026-08-26/Implementation-0.2.4.8-1654.md` | `2026-08-26/ticket-04-static-metadata-snapshot.md`（独立产物快照） |
| 05 Resource / Collision 结构迁移 | `2026-08-26/Implementation-0.2.4.8-1825.md`（最终独立复核） | `…-1742.md`（交付）、`…-1800.md`（返工复核） |
| 06 Item / Zombie 结构迁移 | `2026-08-26/Implementation-0.2.4.8-1850.md` | — |
| 07 Animal / Structure / Barricade 结构迁移 | `2026-08-26/Implementation-0.2.4.8-1933.md` | `2026-08-26/Implementation-0.2.4.8-2334.md`（独立复核与阻塞修复） |
| 08 Evidence Class 测试结构与门禁 | `2026-08-27/Implementation-0.2.4.8-0915.md` | — |
| 09 Build Fingerprint 与独立产物证据 | `2026-08-27/Implementation-0.2.4.8-1234.md`（最终循环审计） | `2026-08-27/Implementation-0.2.4.8-1117.md`（实施与独立复核） |
| 10 Resource Production Control Seam | `2026-08-27/Implementation-0.2.4.8-Ticket10.md` | — |
| 11 Resource 旧 Authority Writer 退出与 Runtime Gate | `2026-08-27/Implementation-0.2.4.8-Ticket11.md` | `2026-08-27/RuntimeFix-0.2.4.8-0017.md`、`2026-08-28/RuntimeFix-0.2.4.8-0812.md`、`…-0916.md`、`…-1442.md`、`2026-08-29/RuntimeFix-0.2.4.8-2229.md`、`2026-09-03/Implementation-0.2.4.8-Ticket11-1123.md`（诊断结论落盘 + 取证埋点）、`2026-09-04/Forensics-0.2.4.8-Ticket11-H1-1046.md`（H1 裁决：trees-unavailable 时序根因 + 整批回滚铁证）、`2026-09-04/ReviewRecheck-0.2.4.8-0933.md`（第三方评审逐条只读复核）、`2026-09-04/Implementation-0.2.4.8-Ticket11-1133.md`（修复轮：单区域隔离 + foliage 暂缓重试，259/259 静态全绿）、`2026-09-07/Runtime-0.2.4.8-Ticket11-2343.md`（Runtime 验证：三端指纹一致、0 失败、SPI 主路径稳定，残留 R1=replication gate throw、R2=无树区域稳态；待独立审核 PASS 后关闭）、`2026-09-08/IndependentReview-0.2.4.8-Ticket11-0751.md`（独立审核 FAIL：DLL 指纹为路径绑定构建事件身份不可由 HEAD 复现，代码/测试/Standards 全 PASS；恢复路径=指纹机制修复+R1 合并票→重测→重审）、`2026-09-09/Runtime-0.2.4.8-Ticket11-2314.md`（Runtime 验收：新指纹 5DF5A1F3/2ff47d8f 三端一致绑定 7d0f5fd 首次实现可复现绑定、R1 容忍 4/4 生效零 throw；新缺陷 R4=deferred-only 区域退出事务 demand 不平衡致 4 次会话重建，登记为审核后首批修复票）、`2026-09-09/IndependentReview-0.2.4.8-Ticket11-2337.md`（独立审核 **PASS**：7 项门禁全过含跨路径确定性实验独立复验，Spec/Standards 双轴 CLEAN，关闭条件 4 成立；R4 登记为审核后首批修复票） |
| 12 Resource Seam 回滚-重试登记一致性（R4+R2） | `2026-09-11/Runtime-0.2.4.8-Ticket12-0031.md`（**Runtime 验收通过,票据 completed**:三端指纹绑定 `e878358` 逐字节一致;0 InvalidOperationException/0 事务回滚/0 M0 fault/0 会话重建（2314 轮 4+4 归零）;容忍路径实机 51 次 outcome=skipped 零 throw,失衡源定位为 `_acquireRetries` 单槽登记互吞（已无害化,可选后续票）;R2 attempts≤5 封顶实证 190 区,deferred 2317→1518;双轴 round 1 双 CLEAN） | `2026-09-10/Implementation-0.2.4.8-Ticket12-0027.md`（实施交付:R4 治本两处补偿+容忍兜底+R2 静默稳态+Ticket09 脚本编码修复;红→绿 263/263、四门禁 PASS、双轴 round 1 双 CLEAN;新指纹 BBBDCC25/cdac3a0a） |

## 历史阶段（0.2.4.0–0.2.4.4，M0–M4）

| 版本/阶段 | 交付/验收报告 |
|---|---|
| 0.2.4.0 / M0 | `2026-08-21/Implementation-0.2.4.0-1849.md` |
| 0.2.4.1 / M0–M1 | `2026-08-21/Implementation-0.2.4.1-2030.md`、`…-2141.md`（运行验收通过记录）；`RuntimeFix-0.2.4.1-1949/2008/2110.md` |
| 0.2.4.2 / M2 | `2026-08-21/Implementation-0.2.4.2-2209.md`；`RuntimeFix-0.2.4.2-2245/2359.md` |
| 0.2.4.3 / M3 | `2026-08-22/Implementation-0.2.4.3-0022.md`；`2026-08-24/RuntimeAcceptance-0.2.4.3-0105.md` |
| 0.2.4.4 / M4 | `2026-08-25/RuntimeAcceptance-0.2.4.4-0005.md` |

## 命名规则（前进）

- 新报告必须使用带票号名称：`Implementation-<ver>-<ticket>[-<HHMM>].md`、
  `RuntimeFix-<ver>-<ticket>[-<HHMM>].md`、`RuntimeAcceptance-<ver>-<ticket>[-<HHMM>].md`。
- 历史 HHMM-only 文件名冻结不改；其票号映射以上表与 `docs/architecture/migration-manifest.md`
  中的引用为准。
