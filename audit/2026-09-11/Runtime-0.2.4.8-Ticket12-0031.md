# Ticket 12 Runtime 验收审计:R4 归零(0 fault/0 会话重建),容忍路径实机生效 51 次,R2 封顶实证

- **日期**:2026-09-11 00:31
- **基线**:HEAD `e878358`(Ticket 12 实施提交,R4 治本+兜底+R2)
- **票据**:`.scratch/structure-baseline-0-2-4-8/issues/12-resource-seam-rollback-retry-consistency.md`(`implemented-pending-runtime`)
- **证据来源**:2026-09-11 00:22–00:24 三端诊断包(1 Host + 2 Guest):
  - 客机 1:`…\UMM-v2.2.1-win-x64\UMM-诊断包_20260911_002210\LogOutput.log`
  - 客机 2:`…\UMM-v2.2.1-win-x64\UMM-诊断包_20260911_002228.zip`(已解压核验)
  - 主机:`…\UMM-v2.2.1-win-x64\UMM-诊断包_20260911_002242\LogOutput.log`
- **DLL 指纹关联(先决关,不一致即停)**:三端 BuildFingerprint 自报行**逐字节一致**,且与 `e878358` 构建记录一致——`mvid=cdac3a0a-afe2-4054-9172-afd316d85570`、`dllSha256=BBBDCC25AAD64E87E2B61A96559D9CD07988BF81468D4B76C574EEF5249B3DC3`、`version=0.2.4.8`、`pluginGuid=com.yu80rice.steamp2pfriends`、`caseId=SPF-0.2.4.8-Experimental-StructureBaseline`(同 `Implementation-0.2.4.8-Ticket12-0027.md` §5)。**PASS。**

## 1. 结论

**R4 验收达成:三端 0 次 `InvalidOperationException`、0 次 `ObserverUpdate/ObserverRemove failed`、0 次事务回滚、0 次 M0 fault、0 次 fault-recovery 会话重建**——Runtime-2314 的 4 次 fault/4 次重建(epoch 1→3→5→7)归零,全程 sessionEpoch=1 单会话,主机唯一一次 session-end 为 `reason=host-session-ended:DisconnectCompleted`(优雅结束)。R4 兜底容忍路径实机触发 **51 次,全部 `outcome=skipped`、零伴随 throw**,按设计吸收了全部过时退出。R2 静默稳态实证:`attempts=5` 出现 190 次、**`attempts=6+` 为 0**(封顶生效),deferred 总量 2317→1518。全部不回归指标与 2314 轮持平或更好。

**用户体感口径**:本轮无资源异常报告;容忍路径仅为主机诊断流中的 skipped 记录,本包未见任何状态破坏证据(0 throw/0 回滚,三态一致性由跳过语义保持)。

## 2. R4 主预期核验(引用行数,不猜测)

| 检查项 | 客机 1 | 客机 2 | 主机 | 上轮(2314) |
|---|---|---|---|---|
| `InvalidOperationException` | 0 | 0 | 0 | 4 组 fault |
| `ObserverUpdate failed` / `ObserverRemove failed` | 0 | 0 | 0 | 4 |
| `transactionRolledBack` | 0 | 0 | 0 | ≥4 |
| `M0 fault` | 0 | 0 | **0** | 4 |
| `session-end reason=fault-recovery` | 0 | 0 | **0**(唯一 session-end=`host-session-ended:DisconnectCompleted`,L16760,优雅) | 4 |
| `RegionExit … reason=stale-exit-without-demand`(容忍,允许) | 0* | 0* | **51,全部 `outcome=skipped`、零 throw、全部 sessionEpoch=1** | 路径不存在(直接 underflow) |

\* 客机无容忍日志为预期——Resource 生产接缝仅主机运行(与历轮一致;客机 ResourceObs 流仅 CollisionActivation 等客户端事件,无生产接缝/容忍类事件)。

**容忍触发的根因定位(本轮新结论,回填 R4 全貌)**:51 次触发与事务回滚无关(本轮 `transactionRolledBack=0`),真正的失衡制造源是 **`_acquireRetries` 的单槽登记互吞**——该字典按区域单键存储(`Dictionary<RegionKey, AcquireRetry>`),注释宣称"登记按观察者归属"但**同区域第二个观察者 deferred 时 `ScheduleAcquireRetry` 直接覆写第一个观察者的登记**;被吞登记的观察者退出该区域时 head-check(`leavingRetry.ObserverId == observerId`)不匹配 → 走完整退出路径 → demand=0 → 旧代码 underflow throw(R4 的 4 次 fault)、新代码兜底容忍跳过。**佐证**:2314 轮 fault 区域 (24,29)/(29,30) 恰在本轮容忍触发区域清单中重现;容忍行 `regionGeneration=0`(区域从未被任何人 lease,deferred-only 特征);同一退出批次内正常区域(SnapshotRemove+LeaseReleaseScheduled)与容忍区域混合,符合"移动退出批次、部分区域登记被互吞"的形态。
**影响评估**:互吞的实际损害=被吞观察者丢失该区域的重试资格(退出重进即自愈,重进会重新登记),demand/replication/spatialIndex 因容忍跳过而保持一致,**无状态破坏**;但 51 次/轮的容忍日志与"重试资格互吞"仍是可改进项。

## 3. R2 静默稳态核验

| 指标 | 本轮 | 上轮(2314) |
|---|---|---|
| `LeaseAcquire deferred` 总量 | **1518** | 2317 |
| `attempts=5`(到达封顶) | 190 次 | —(无 attempts 字段) |
| `attempts=6+` | **0** | — |
| 单区域序列样本(region=(30,30),L2417-2628) | attempts=1,2,3,4,5 后静默 | 逐条刷屏 |

R2 语义核验:封顶后仅日志静默、重试仍登记推进为代码事实(`ScheduleAcquireRetry` 无次数上限,封顶后按 32s 退避继续登记);本包未观测到封顶后成功样本(到达 `attempts=5` 的区域其后均未 success,静默期成功与失败不可由日志区分)——R2 的日志主张以 attempts 封顶与 deferred 降量为准,符合"仅计数不逐条打 Info"。

## 4. 不回归指标(主机为主,客机 replication 单列)

| 检查项 | 结果 | 证据 |
|---|---|---|
| `region-snapshot-failed` | ✅ 0(三端) | grep 0 |
| `LeaseAcquire` success/failed | ✅ 294 / **0** | 主机 |
| 采伐复制 | ✅ HarvestDead accepted 10 | 主机(上轮 7/7,本轮量更大) |
| 滞回/重入 | ✅ LeaseReleaseScheduled 356 / LeaseRelease success 486 / LeaseReentry 85 | 主机 |
| SnapshotRemove failed / SnapshotEnqueue rejected | ✅ 0 / 0 | 主机 |
| ObserverDisconnect | ✅ success 4(优雅) | 主机 |
| 碰撞 | ✅ CollisionActivation 10090(主机)/6782(客机1)/4632(客机2) | 三端 |
| 双端复制 | ✅ 客机 SnapshotReceive 各 2、DeltaReceive 1/9;主机 DeltaReceive 12,其中 `reason=host-does-not-decode-client-delta` 12(正确 skip) | 三端 |
| 会话单调性 | ✅ 全程 sessionEpoch=1,结束 nextEpoch=2 | 主机 |

## 5. 对 Ticket 12 关闭条件的状态

| # | 票面条件 | 状态 |
|---|---|---|
| 1 | 治本:retry 登记清除纳入补偿 | ✅ 静态闭环(M6P33 红→绿)+ Runtime 0 回滚 0 fault |
| 2 | 兜底:过时退出容忍不抛出 | ✅ 实机生效 51 次,0 throw,`reason=stale-exit-without-demand` 可见 |
| 3 | 回归测试 ①②③ | ✅ M6P33/34/35,263/263 |
| 4 | Release 重建+静态门禁+新指纹登记 | ✅ 2337 同口径四门禁 PASS,新指纹三端绑定 `e878358` |
| 5 | Runtime 验证(0 fault/0 重建/容忍日志可见) | ✅ **本轮达成**(§1/§2) |

**遗留(不阻塞本票,建议后续票)**:① `_acquireRetries` 单槽互吞(§2 根因)——如需按观察者公平保留重试资格,需把登记键改为 (region, observer) 二元组,属 acquire 语义变更,按票据边界未纳入本票;容忍路径已使其无害化。② 51 次/轮容忍日志量可选降级(NoticeOnce/配额),非必需。

## 6. 双轴独立审查链与审计纪律

**审查链(round 1,全新并行实例)**:Standards 轴 **CLEAN**(关键计数全部独立复算一致,单槽互吞机制与代码事实相符;3 条判断性精度意见:①§3 封顶后成功系代码事实而非日志实证、②§2 客机脚注措辞、③§1 体感句——已按意见修正措辞后收录本报告);Spec 轴 **CLEAN**(6 项核对全 ✅,无 findings)。两轴终局均 CLEAN,票据第 5 项据此达成。

**审计纪律**:
- 三端日志全文 grep 取证,未修改任何日志或包文件;客机 2 zip 解压至兄弟目录 `UMM-诊断包_20260911_002228_extracted`(包旁,非仓库内)。
- 指纹先决关未过即停的规则本轮未触发(三端一致)。
- Standards 轴 3 条判断性意见为措辞精度修正,不改变任何数据与裁决;修正内容已逐条落本报告。
