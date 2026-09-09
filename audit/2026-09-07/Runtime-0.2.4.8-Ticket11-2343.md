# Ticket 11 Runtime 验证诊断:修复生效确认(H1 对策通过)+ 残留问题定位

- **日期**:2026-09-07 23:43
- **基线**:HEAD `5475eb7`(修复轮提交)
- **票据**:`.scratch/structure-baseline-0-2-4-8/issues/11-resource-authority-retirement-runtime.md`(`implemented-pending-runtime`)
- **证据来源**:2026-09-07 23:33 三端诊断包(1 Host + 2 Guest,按测试剧本执行):
  - 主机:`…\UMM-v2.2.1-win-x64\UMM-诊断包_20260907_233517\LogOutput.log`
  - 虚拟机客机:`…\UMM-诊断包_20260907_233519\LogOutput.log`
  - 用户客机:`…\UMM-v2.2.1-win-x64\UMM-诊断包_20260907_233302.zip`(已解压核验)
- **DLL 指纹关联(三端一致)**:`mvid=6ae0e666-882b-48ca-b6e2-024d79af9aae`、`dllSha256=B6A3ACC9131BFCD22564644140D2E874CB854BC5F8FE8D435B39DBC33C885986`、Case-ID `SPF-0.2.4.8-Experimental-StructureBaseline`——与修复轮构建记录完全一致。**关闭条件第 1、2 条成立**。

---

## 1. 结论:修复在 Runtime 生效

用户实测(权威口径):**主机进入新区域资源不再不刷新**——修复前(2026-09-02)100% 失败瘫痪,本次全程正常。

| 指标 | 修复前(Forensics-H1-1046,2026-09-02) | 本次(2026-09-07) |
|---|---|---|
| `region-snapshot-failed` | 9/9 全灭,50/50 fault | **0 次** |
| LeaseAcquire outcome | failed 100%,SPI 全程无 lease | **success 535 次 + deferred 3052 次,failed 0 次** |
| acquire 路径 | 死于 `CaptureNativeRegionState`,`OnAcquire` 从未执行 | `path=SPI success` 全程稳定 |
| 整批回滚 | 单区域失败连带全清 | 无(0 次 failed 即无回滚源) |
| 用户可见 | 客机离开后主机区域无树 | **全程正常** |

核心机制证据:`sessionEpoch` 奇数递增(1→13)对应 M0 层 7 次 fault-recovery 会话重建(见 §2 残留问题),但**每次重建后 Resource 在新会话立即重新 acquire 成功**((30,35) 等区域跨 7 个会话连续 `path=SPI success`)——这正是修复的韧性价值:**M0 层故障不再连坐 Resource 瘫痪**(修复前一次 fault = 132→226 秒全局瘫痪)。

## 2. 残留问题(新证据,均不阻塞本次结论)

### R1:replication 段 `SnapshotRemove` 被 connection-generation gate 拒绝 → M0 会话重建
- 现象:18 次 `MultiObserver/M0 fault=1/16 type=InvalidOperationException`、7 次 `session-end nextEpoch reason=fault-recovery`;每次 fault 前有 `event=SnapshotRemove … outcome=failed exception=InvalidOperationException`(如 (31,25) observer=7656…7596、(31,24) observer=7656…0228——恰为两台客机重连场景)。
- 源头:`Adapters/Resource/ResourceDomainAdapter.cs:185` "Resource snapshot removal rejected by **connection generation gate**"——observer 重连后,旧 connection token 的待移除快照被 generation gate 拒绝,以 throw 方式失败 → `ObserverUpdate` 事务回滚 → coordinator 计 fault → M0 会话重建。
- 定性:**generation 防护语义在工作**(拒绝过时操作是正确的),但失败方式(throw → 全局会话重建)是工程缺陷;影响限于重连瞬间的秒级恢复窗口,用户无感。**该日志未打 message**(仅 `exception=TypeName`)——取证盲区与当年 region-snapshot-failed 相同,需同步补 message 埋点。
- 处置建议:下一工作票——`SnapshotRemove` 的 generation gate 拒绝改为"过时移除容忍"(记录日志不抛出),并在 `SnapshotRemove`/`SnapshotEnqueue` 日志补 `DescribeAcquireFailure` 式 message 埋点(沿用 StaticIL helper 模式)。

### R2:真无树区域的重试稳态(设计取舍内,记录日志量)
- 现象:约 70 个区域(如 (30,34)、(29,34)、(37,24)–(32,24) 整排)累计 3052 次 `outcome=deferred`,每区域 42–50 次,零 success——这些区域在主机本地 `_regionTrees` 中**确实没有树条目**(无树地带),按温和退避进入稳态(2→4→8→16→32s)。
- 定性:**符合 Implementation-1133 §3 的预设计取舍**("真无树区域进入 32s 退避稳态")。行为无害,日志量可控但可观(3052 条 Info)。
- 处置建议(低优先级):deferred 连续 N 次后降级为静默稳态(仅计数,不再逐条打 Info);或引入"区域空树确认"标记后停止重试。

### R3:暂缓补租约形态说明
- 本次日志中 deferred 与 success 区域集合不相交:(23,28)/(24,28) 各 2 次 deferred 后观察者离开(登记撤销),其余 deferred 区域为 R2 稳态。"进入瞬间暂缓 1–2 次后补成功"的形态未在本次样本中出现——说明有树区域的树条目在主机到达时几乎总是已就绪(直接 success),H1 的时序窗口比取证时推测的更窄。修复的 Runtime 价值由此更准确地表述为:**消除 capture 异常的破坏性放大(整批回滚+全局退避)**,而非高频补租约。

## 3. 其余验收观察(对应测试剧本)

- 滞回:`LeaseReleaseScheduled` 181 次、`LeaseReentry` 7 次——滞回调度与重入取消正常工作;
- 重连:Guest1 重连产生 1 次 `ConnectionGeneration` 事件,链路正常(其引发的 R1 见上);
- 双端复制:两台客机各收到 2 次 `SnapshotReceive`,快照下发通道工作;
- 会话:`SessionBegin/End` 因果完整,无 repair-required 锁死。

## 4. 对 Ticket 11 关闭条件的状态

| # | 条件 | 状态 |
|---|---|---|
| 1 | 三端同一 DLL、同一共享 Case-ID | ✅ 三端指纹一致 |
| 2 | 日志与 DLL SHA-256/MVID/版本/GUID 一致 | ✅ 全部 `B6A3ACC9…`/`6ae0e666…` |
| 3 | SPI 主路径 vs Native/Legacy 明确证明 | ✅ `path=SPI` success 全程稳定;0 failed |
| 4 | 唯一 Authority Writer、租约/generation/复制因果链 Runtime 通过 | ✅ 成立(generation 防护在工作,R1 的 throw 方式列为后续改进,不构成"防护失效") |
| 5 | 独立审核正式 PASS | ⏳ **待补跑**(修复轮 `5475eb7` 的全新实例审核,门禁顺序要求) |
| 6 | 票据与 Migration Manifest 交付证据写回 | ✅ 1133 报告已写回;本报告补 Runtime 证据 |

**建议路径**:本报告落盘 → 补跑第 5 条独立审核(全新实例)→ PASS 后关闭 Ticket 11;R1/R2 立为关闭后的第一批新工作票(与推广域 Collision/Item 并行排期)。
