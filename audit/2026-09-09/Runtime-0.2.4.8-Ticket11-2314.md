# Ticket 11 Runtime 验收审计:新指纹三端绑定成立,R1 本体生效,暴露下一层 fault 源(R4)

- **日期**:2026-09-09 23:14
- **基线**:HEAD `7d0f5fd`(合并实施票:portable+PathMap 指纹修复 / R1 过时移除容忍 / M6P31 补强)
- **票据**:`.scratch/structure-baseline-0-2-4-8/issues/11-resource-authority-retirement-runtime.md`(`implemented-pending-runtime`)
- **证据来源**:2026-09-09 22:53–22:54 三端诊断包(1 Host + 2 Guest):
  - 主机:`…\UMM-v2.2.1-win-x64\UMM-诊断包_20260909_225427\LogOutput.log`
  - 虚拟机客机:`…\UMM-v2.2.1-win-x64\UMM-诊断包_20260909_225431\LogOutput.log`
  - 用户客机:`…\UMM-v2.2.1-win-x64\UMM-诊断包_20260909_225311.zip`(已解压核验)
- **DLL 指纹关联(三端一致,本轮核心)**:`mvid=2ff47d8f-e59a-4b5f-b7db-f7567df5e023`、`dllSha256=5DF5A1F34CEF605B33567ED76C4854A21C41F840236BC6F1AE33111639845375`、Case-ID `SPF-0.2.4.8-Experimental-StructureBaseline`——与 `7d0f5fd` 构建记录逐字节一致,且该指纹已实证跨路径可复现(同源码异路径构建 SHA/MVID 相同,见 7d0f5fd 提交信息)。**首次实现"指纹=源码提交复现身份"而非"路径绑定的一次性构建事件"**。

---

## 1. 结论

用户实测(权威口径):**树木资源全程正常,房主进入新区域资源显示正常**——修复有效性维持 9-07 轮结论。三端指纹绑定条件本轮完整成立。R1 修复本体经日志实证生效;但重连/退出事务仍有 4 次 `InvalidOperationException` fault(新根因,定名 **R4**),M0 会话重建未完全归零(4 次,上轮 7 次)。

## 2. 本轮取证亮点(修复有效性证据)

| 检查项 | 结果 | 证据 |
|---|---|---|
| 三端指纹一致 | ✅ | 三端 BuildFingerprint 自报行逐字节一致(见头部) |
| region-snapshot-failed | ✅ 0 次 | 三端均 0 |
| LeaseAcquire | ✅ success 516 / deferred 2317 / **failed 0** | `path=SPI` success 稳定;deferred 为 R2 已知稳态 |
| R1 容忍路径 | ✅ 4 次全部走容忍 | `event=SnapshotRemove … outcome=rejected … reason=stale-removal-tolerated`×4,**零 throw**(SnapshotRemove outcome=failed 0 次) |
| SnapshotEnqueue | ✅ 676 全 success | 0 rejected——域适配器 Enqueue throw 未触发 |
| 采伐复制 | ✅ 7/7 accepted | `HarvestDead … outcome=accepted … stateEncoder=ResourceManager.ServerSetResourceDead`,regionGeneration 推进到 2 |
| 碰撞/租约/滞回 | ✅ | CollisionActivation 6376;LeaseRelease 452;LeaseReleaseScheduled 287;LeaseReentry 5 |
| 双端复制 | ✅ | 两客机 SnapshotReceive 各 2 次;DeltaReceive 7 次(主机侧正确 skip:reason=host-does-not-decode-client-delta) |
| 重连/离开 | ✅ 事件齐全 | ObserverDisconnect 6 次 success;客机 ConnectionGeneration 2/3 次 |
| message 埋点就绪 | ✅ | 本轮新增的 `DescribeAcquireFailure` 埋点 catch 零触发(异常不来自 replication 段,见 §3) |

## 3. R4(新缺陷):deferred 区域退出事务 demand 不平衡 → M0 会话重建仍在

- **现象**:4 组完全同构的 fault——`SnapshotRemove 容忍 → SnapshotEnqueue success → ObserverUpdate failed(exception=InvalidOperationException, transactionRolledBack=True) → M0 fault=1/16 → session-end reason=fault-recovery`。sessionEpoch 1→3→5→7 共 4 次重建;涉及 (24,29)/(39,32)/(34,36)/(29,30) 四区域、4 个观察者-区域组合。
- **决定性证据**:4 个区域对该观察者的 LeaseAcquire 历史**全部 deferred-only(36/25/41/46 次,0 次 success)**——即 demand 从未计入、快照从未 enqueue 的区域直接进入了退出事务。
- **根因链(排除法定位于 seam:795)**:本轮埋点 catch(replication 段)零触发 + Enqueue 零 rejected + acquire 段隔离(零 skipped)→ `UpdateObserver` 事务内唯一可达的 `InvalidOperationException` 为 `DecrementDemand` 的 `"Resource region demand underflow."`(`ResourceProductionControlSeam.cs:795`)。按设计,deferred 区域退出应由 `ProcessExited` 的 retry 撤销分支 `continue` 接住(清登记、跳过 demand 递减)——实际未接住,走进了完整退出路径。
- **失衡成因(代码走查结论)**:`ProcessSingleRegionEntry` 尾部清除本观察者 retry 登记(`_acquireRetries.Remove`,seam:740-744)**不在事务补偿列表内**。任何一次事务回滚(`RestoreState` 恢复 demand/lease/spatialIndex)都不会恢复 retry 登记 → 留下"区域在空间索引、无 demand、无 retry 登记"的失衡态 → 后续退出/重连必触发 underflow。9-07 轮的 18 次 gate-throw 回滚即是失衡态的制造源;本轮 R1 修掉 SnapshotRemove throw 后,该层暴露为当前 fault 源。确切开裂序列(哪次回滚先制造失衡)需复现测试实锤,建议随 R4 修复票一并取证。
- **定性**:与 R1 同类——防护语义在工作(underflow fail-fast 本身正确),失败方式(throw → 会话重建)是工程缺陷;影响仍限于重连/离开瞬间的秒级恢复窗口(会话重建后 Resource 立即重新 success,用户无感,树正常)。
- **处置建议(R4 票,建议先于独立审核执行)**:① `ProcessSingleRegionEntry` 尾部的 retry 清除纳入补偿列表(回滚恢复,治本);② `ProcessExited` 对 demand=0 的退出对称 R1 做"过时退出容忍"(记日志不 throw,兜底);③ PureMemory 回归测试:回滚后 retry 登记一致性 + deferred 区域退出不再 fault;④ 可选:给 `DecrementDemand` 失败路径补 message 埋点,一次实机即可实锤确切序列。

## 4. R2 稳态提醒(既有,不阻塞)

deferred 2317 次(上轮 3052)——真无树区域 32s 退避稳态仍在按 1133 §3 预设运行,日志量可观但无害。R2 降级为静默稳态的建议维持低优先级,可与 R4 并票处理。

## 5. 对 Ticket 11 关闭条件的状态

| # | 条件 | 状态 |
|---|---|---|
| 1 | 三端同一 DLL、同一共享 Case-ID | ✅ 三端指纹一致且可复现绑定 `7d0f5fd` |
| 2 | 日志与 DLL SHA-256/MVID/版本/GUID 一致 | ✅ 全部 `5DF5A1F3…`/`2ff47d8f…`/`0.2.4.8`/`com.yu80rice.steamp2pfriends` |
| 3 | SPI 主路径 vs Native/Legacy 明确证明 | ✅ `path=SPI` success 516、0 failed;deferred 为预设稳态 |
| 4 | 唯一 Authority Writer、租约/generation/复制因果链 Runtime 通过 | ✅ 成立(采伐 7/7 accepted、滞回/重入正常;R4 的 throw 方式不构成"防护失效",同 9-07 轮对 R1 的定性) |
| 5 | 独立审核正式 PASS | ⏳ 待补跑(建议 R4 修复后以新 HEAD 重跑动态测试,再进独立审核) |
| 6 | 票据与 Migration Manifest 交付证据写回 | ⏳ 本报告为 Runtime 证据,写回随关闭流程 |

**建议路径**:本报告落盘 → 立并执行 R4 修复票(小改动)→ 重建+静态门禁 → 用户重跑 1H+2G(预期:0 fault、无会话重建)→ 新窗口独立审核 → PASS 关闭 Ticket 11。

## 6. 审计纪律披露

- 全程只读三端日志与工作树;未修改任何被跟踪文件;用户客机 zip 解压至系统临时目录,未入仓库。
- 本报告为归档类新建文件,按 `docs/agents/large-write-batching.md` 单一职责写入。
