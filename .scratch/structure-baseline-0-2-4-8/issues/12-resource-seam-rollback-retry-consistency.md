# 12: Resource Seam 回滚-重试登记一致性与过时退出容忍（R4+R2）

**What to build:** 根治 Runtime-2314 发现的 R4 缺陷——`ProcessSingleRegionEntry` 尾部清除本观察者 acquire 重试登记的操作不在事务补偿列表内，任何一次 `UpdateObserver` 事务回滚都会留下"区域在空间索引、无 demand、无 retry 登记"的失衡态；后续该区域退出/重连时 `ProcessExited` 走完整路径触发 `DecrementDemand` 的 demand underflow throw（seam:795）→ M0 会话重建。同时以对称 R1 的容忍语义兜底过时退出，并顺带处理可并票的 R2 与审核遗留登记项。

**Blocked by:** 11 / Resource 旧 Authority Writer 退出与运行时验收（已关闭）

**Status:** completed（Runtime 验收达成:三端 0 fault/0 会话重建/容忍路径 51 次零 throw;双轴 round 1 双 CLEAN;见 `audit/2026-09-11/Runtime-0.2.4.8-Ticket12-0031.md`。遗留:单槽登记互吞已无害化,后续票可选）

- [x] 治本：`ProcessSingleRegionEntry` 尾部 retry 登记清除纳入事务补偿列表——事务回滚时恢复被清除的登记，保证 retry/demand/spatialIndex 三者一致。（同缺陷类超集:`ProcessExited` 头部撤销登记亦纳入补偿,Spec 轴确认合理）
- [x] 兜底：`ProcessExited` 对 demand=0（或缺失）的过时退出对称 R1 做容忍——记日志（含 reason，沿用 outcome 封闭枚举）不抛出，消除该路径的会话重建源。
- [x] 回归测试（PureMemory）：① 事务回滚后 retry 登记一致性（M6P33,红测逐帧复现 Runtime-2314 underflow 链后转绿）；② deferred-only 区域退出不再触发 underflow/fault（M6P34 回归锁）；③ 兜底容忍路径的状态断言（M6P35,反射伪造残留）；日志内容由 Runtime 复核承载（RoleLogger 无测试 sink,既有 seam gap,具名于审计 §6）。
- [x] 一次 Release 重建 + 全静态门禁（263/263 全绿、指纹脚本、布局门禁、git diff --check），新指纹登记（主 DLL `BBBDCC25…`/`cdac3a0a…`；测试 exe `86C4D5E6…`/`4050716b…`,provenance 更新取代 `32B9B7B5…`/`b19b0910…`,见 IndependentReview-2337 §九-5 与审计 §5）。
- [x] Runtime 验证（归属本票）：用户重跑 1H+2G，预期 0 次 M0 fault、0 次 fault-recovery 会话重建、重连后可见 `reason=` 容忍日志；UMM 诊断包回传后按 Runtime-2314 同口径审计落盘。（✅ 2026-09-11 00:22–00:24 三端实测:0 InvalidOperationException/0 事务回滚/0 M0 fault/0 重建,sessionEpoch 全程 1;容忍路径 51 次 outcome=skipped 零 throw;R2 attempts≤5 封顶实证,deferred 2317→1518;审计 `audit/2026-09-11/Runtime-0.2.4.8-Ticket12-0031.md`,双轴双 CLEAN。新发现:失衡源为 `_acquireRetries` 单槽登记互吞,已被兜底无害化,登记为可选后续项）

## 可并票项（同票顺带，均小改动）

- [x] R2：deferred 连续 N 次后降级静默稳态（仅计数不逐条打 Info）——降低 2317 次/轮的日志量，不改变重试语义。（N=DeferredAcquireQuietAttempts=5,可见日志补 attempts= 字段;日志降量由 Runtime 复核）
- [x] `Tools/Verify-Ticket09Documentation.ps1` 补 UTF-8 BOM（存量解析期编码缺陷，IndependentReview-2337 §四；补 BOM 后用 PS5.1 原样运行验证 PASS）。（另将文档读取改显式 UTF-8——文档本体无 BOM,仅补 BOM 时 PS5.1 内容期按 ANSI 读取仍失败;同属该编码缺陷收口,见审计 §6-4）
- [x] Standards 判断性坏味道两条（IndependentReview-2337 §六）评估：均记录延期理由（收口会触碰构建身份配置/捆绑本身为票面授权形态）,见审计 §7;本轮 Standards 轴另点名两条判断性坏味道一并具名延期。

## 范围与边界

- 不改 Harmony/频道/GUID/`Build/Version.props` 与结构不变量；不改 acquire 隔离与滞回语义。
- `DecrementDemand` 的 underflow fail-fast 语义在需求为正的路径上保留（本票只处理退出路径的过时场景兜底）。
- 本票完成前，Ticket 12 不得宣称 Resource 领域 fault-free——Runtime 证据未到位前维持 `ready-for-agent`/实施中状态。
