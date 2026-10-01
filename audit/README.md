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

## Collision Migration Slice（0.2.4.9，票 01–09）

| Ticket | 交付/最终报告 | 补充与修复 |
|---|---|---|
| 01 0.2.4.9 迁移基线 + Resource 表征门 | `2026-09-18/Implementation-0.2.4.9-CollisionTicket01-1235.md`（**completed**：版本身份授予、表征门建立、生产 Writer 未变；双轴 round 1–5 最终双 CLEAN；Runtime 归票 09） | — |
| 02 Resource 经声明式 Demand Policy 接入共享投影引擎 | `2026-09-18/Implementation-0.2.4.9-CollisionTicket02-1418.md`（**implemented-pending-runtime**：唯一空间事实 + 共享投影引擎落地，接缝不再持私有观察者索引；资格不合格＝暂缓；313/313 PASS、三门禁 PASS、双轴 round 1–4 最终双 CLEAN；Runtime 归票 09） | — |
| 03 Resource 经领域端口迁入共享生命周期编排引擎 | `2026-09-18/Implementation-0.2.4.9-CollisionTicket03-1908.md`（**implemented-pending-runtime**：共享编排引擎 + Domain Execution Port 落地，接缝退化为薄门面、原通用生命周期状态机退出生产权威；335/335 PASS、三门禁 PASS、扰动 12 组证伪、双轴 round 1–4 最终双 CLEAN；Runtime 归票 09） | — |
| 05 Collision 声明式 Demand Policy 与只读影子验证 | `2026-09-18/Implementation-0.2.4.9-CollisionTicket05-2225.md` + `2026-09-25/RuntimeAcceptance-0.2.4.9-CollisionTicket05-2355.md`（**completed**：三端 ReadOnlyShadow 指纹一致，25/25 条影子汇总 `collisionShadowForbidden=0`；正式切换 Runtime 仍归票 09） | — |
| 06 Collision Execution Port 与 Acquisition Receipt | `2026-09-26/Implementation-0.2.4.9-CollisionTicket06-2350.md`（**implemented-pending-runtime**：Collision Execution Port + Receipt + 独立 Lifecycle Policy；404/404 PASS、双次 Release Rebuild 身份一致、四门禁 PASS、双轴五轮最终双 CLEAN；正式切换 Runtime 归票 09） | — |
| 07 Collision 正式切换准入证据 | `2026-09-26/Implementation-0.2.4.9-CollisionTicket07-1545.md`（**静态与已观测影子子集 GO，允许受控进入 08；会话恢复 NOT PROVEN 归 09；当前正式 Collision Cutover NO-GO**：404/404 PASS、影子 Forbidden=0、Execution Port/Receipt 证据通过；本票不切换 Writer、不退役旧 Writer） | — |
| 08 会话边界原子切换与旧 Writer 退役 | `2026-09-26/Implementation-0.2.4.9-CollisionTicket08-1130.md`（**implemented-pending-runtime**：正式 Cutover 唯一 Writer、旧 Writer/影子生产调用与 Resource 旧谓词/树写入归零；RegionKey 粒度补偿与 Deferred Observer Demand 通过；409/409 PASS；最新双次 Cutover 指纹一致、独立核验 PASS；Runtime 归票 09；Standards/Spec 最终零上下文双轴 CLEAN） | — |
| 09 共享 1 Host + 2 Guest Runtime 验收与 Collision Slice 关单 | `2026-09-26/RuntimeFix-0.2.4.9-Ticket09-2341.md`（**修复轮一**：票 08 候选首次实机启动失败——Closure 要求清单残留 Collision 条目致 `DiagnosticBuildValid=false`；红→绿 410/410、双次身份一致、独立核验 PASS、双轴 CLEAN；指纹 `F055B34C…/6db49d84…`） | `2026-09-27/RuntimeFix-0.2.4.9-Ticket09-0938.md`（**修复轮二·S3**：维护拍补齐，红→绿 411/411、四轮双轴双 CLEAN、指纹 `7C0ADF80…/a3c09f2b…`；**其 §2 根因叙事已被下条取证推翻**）；`2026-09-27/Forensics-0.2.4.9-Ticket09-2051.md`（**取证定案**：S3 期间 Collision 租约从未成立——4293 条 Acquire 全部 `acquire-identity-rejected`、`regionGeneration=0`；缺陷=Ledger 代次源自举死锁，落 06/08 执行路径；下一刀从当前 HEAD 继续；Runtime PENDING 归本票） |

## Listen-Host Dedicated Gate 与 Join Routing 批（01–04 实施 + 共享 05 Runtime）

| Ticket | 交付/最终报告 | 补充与修复 |
|---|---|---|
| 01 僵尸重生 Listen-Host Dedicated Gate | `2026-09-12/Implementation-0.2.4.8-Ticket01-0027.md` + Runtime `2026-09-18/RuntimeAcceptance-0.2.4.8-Ticket05-0047.md`（**completed**,用户 2026-09-18 关单） | — |
| 02 物品周期生命周期 Listen-Host Dedicated Gate | 同上 Implementation-Ticket02-1043 + Runtime 0047（**completed**） | — |
| 03 SteamID:port 分类 | `2026-09-12/Implementation-0.2.4.8-Ticket03-1048.md` + Runtime `2026-09-18/RuntimeAcceptance-0.2.4.8-Ticket03-0105.md`（**completed**：地址栏整串 `SteamID:27016` → `route=SteamP2P` 进房） | — |
| 04 Listen-Host Session Password | Implementation-Ticket04-1123 + Runtime 0047 四态日志（**completed**） | — |
| 05 共享 1 Host + 2 Guest Runtime 验收 | `2026-09-18/RuntimeAcceptance-0.2.4.8-Ticket05-0047.md`（**completed**,用户关单；票 03 整串输入除外。不改 RELEASES） | `2026-09-17/RuntimeAcceptance-0.2.4.8-Ticket05-2320.md`（第一轮不构成 PASS） |

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
