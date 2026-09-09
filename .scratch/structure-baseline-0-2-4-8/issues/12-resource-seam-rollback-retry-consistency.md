# 12: Resource Seam 回滚-重试登记一致性与过时退出容忍（R4+R2）

**What to build:** 根治 Runtime-2314 发现的 R4 缺陷——`ProcessSingleRegionEntry` 尾部清除本观察者 acquire 重试登记的操作不在事务补偿列表内，任何一次 `UpdateObserver` 事务回滚都会留下"区域在空间索引、无 demand、无 retry 登记"的失衡态；后续该区域退出/重连时 `ProcessExited` 走完整路径触发 `DecrementDemand` 的 demand underflow throw（seam:795）→ M0 会话重建。同时以对称 R1 的容忍语义兜底过时退出，并顺带处理可并票的 R2 与审核遗留登记项。

**Blocked by:** 11 / Resource 旧 Authority Writer 退出与运行时验收（已关闭）

**Status:** ready-for-agent

- [ ] 治本：`ProcessSingleRegionEntry` 尾部 retry 登记清除纳入事务补偿列表——事务回滚时恢复被清除的登记，保证 retry/demand/spatialIndex 三者一致。
- [ ] 兜底：`ProcessExited` 对 demand=0（或缺失）的过时退出对称 R1 做容忍——记日志（含 reason，沿用 outcome 封闭枚举）不抛出，消除该路径的会话重建源。
- [ ] 回归测试（PureMemory）：① 事务回滚后 retry 登记一致性（制造回滚场景，断言登记恢复）；② deferred-only 区域退出不再触发 underflow/fault；③ 兜底容忍路径的日志与状态断言。
- [ ] 一次 Release 重建 + 全静态门禁（260+ 全绿、指纹脚本、布局门禁、git diff --check），新指纹登记（主 DLL + 测试 exe `32B9B7B5…`/`b19b0910…` 的 provenance 更新，见 IndependentReview-2337 §九-5）。
- [ ] Runtime 验证（归属本票）：用户重跑 1H+2G，预期 0 次 M0 fault、0 次 fault-recovery 会话重建、重连后可见 `reason=` 容忍日志；UMM 诊断包回传后按 Runtime-2314 同口径审计落盘。

## 可并票项（同票顺带，均小改动）

- [ ] R2：deferred 连续 N 次后降级静默稳态（仅计数不逐条打 Info）——降低 2317 次/轮的日志量，不改变重试语义。
- [ ] `Tools/Verify-Ticket09Documentation.ps1` 补 UTF-8 BOM（存量解析期编码缺陷，IndependentReview-2337 §四；补 BOM 后用 PS5.1 原样运行验证 PASS）。
- [ ] Standards 判断性坏味道两条（IndependentReview-2337 §六）评估：如顺手则以共享 props 或注释说明收口，不顺手则记录延期理由。

## 范围与边界

- 不改 Harmony/频道/GUID/`Build/Version.props` 与结构不变量；不改 acquire 隔离与滞回语义。
- `DecrementDemand` 的 underflow fail-fast 语义在需求为正的路径上保留（本票只处理退出路径的过时场景兜底）。
- 本票完成前，Ticket 12 不得宣称 Resource 领域 fault-free——Runtime 证据未到位前维持 `ready-for-agent`/实施中状态。
