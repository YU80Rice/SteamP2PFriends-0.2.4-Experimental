# Ticket 11 修复轮交付:单区域 Acquire 隔离 + foliage 未烘焙暂缓重试(步骤④)

- **日期**:2026-09-04 11:33
- **分支**:`codex/structure-baseline-0.2.4`(基线 HEAD `340918a` + 取证轮未提交变更,本轮随修复一并提交)
- **票据**:`.scratch/structure-baseline-0-2-4-8/issues/11-resource-authority-retirement-runtime.md`(`implemented-pending-runtime`,维持)
- **前置依据**:`audit/2026-09-03/Implementation-0.2.4.8-Ticket11-1123.md`(步骤④定义)、`audit/2026-09-04/Forensics-0.2.4.8-Ticket11-H1-1046.md`(H1 裁决与修复方向)

---

## 1. 变更清单

| 文件 | 变更 |
|---|---|
| `Adapters/Resource/ResourceRegionLifecycleAdapter.cs` | 新增 `ResourceNativeSnapshotUnavailableException : InvalidOperationException`;`CaptureNativeRegionState`/`RestoreNativeRegionState`/`ValidateNativeRegionState` 三处 `native-resource-trees-unavailable` throw 换为新类型(message 文本不变) |
| `Adapters/Resource/ResourceProductionControlSeam.cs` | ① `ProcessEntered` 拆出 `ProcessSingleRegionEntry`(单区域 acquire→replication→demand);② acquire 失败两级 catch 隔离:快照不可用→暂缓重试(2s 温和倍增,cap 32s),其他异常→重试(10s 温和倍增,cap 60s),**不向调用方抛出**——消除 `UpdateObserver` 整批回滚与 coordinator `ShadowFaultBackoff` 指数退避瘫痪;③ 隔离时撤销本区域已登记补偿(防跨区域补偿串扰);④ `_acquireRetries` 重试登记按观察者归属,成功后条件清除;⑤ `ProcessExited` 对重试中区域撤销登记并跳过 demand 递减(防 demand underflow/失衡);⑥ `RemoveObserver`/`ClearManagedSessionState` 清理孤儿登记;⑦ 公共观测 `PendingAcquireRetryCount` |
| `WhitelistTests/.../ResourceProductionControlSeamTests.cs` | 新增 M6P32(`Test_M6P32_SnapshotUnavailableDefersRetryThenAcquires`);Fake 新增 `DeferOnCaptureRegionStateFor`;M6P31 追加重试登记断言;M6P07/15/21/28 按隔离语义升级 |
| `WhitelistTests/.../ResourceProductionControlStaticILContractTests.cs` | 新增 `Test_SingleRegionEntryDoesNotParseExceptionText`(分类边界迁移到 `ProcessSingleRegionEntry` 后的镜像契约) |
| `WhitelistTests/Program.cs` | 注册 M6P32、新契约;M6P15/21 展示名随语义更新 |

## 2. TDD 轨迹

1. **红(256/259)**:M6P31(既有预期红)+ M6P32(新)+ `Resource Single Region Entry Classification`(新)三个 FAIL,其余 256 PASS。
2. **绿**:隔离 + 重试队列实现后,首跑暴露 4 个旧契约失败(M6P07/15/21/28)——均为旧"失败即抛 + 整批回滚"语义的守护测试,按新语义逐条升级(见 §3 裁决记录)。
3. **复审修正后**:code-review 两轴发现多观察者登记互吞、无上限重试刷屏等问题,实施第二轮修正(按观察者归属 + 温和退避),**259/259 PASS(0 FAIL)**。

## 3. 裁决记录(回应 Forensics-1046 §4 开放点与 review 发现)

| 开放点/发现 | 裁决 |
|---|---|
| 重试间隔与上限 | 暂缓类 2s 起步温和倍增(2/4/8/16/32s,cap 32s);一般失败 10s 起步(10/20/40/60s,cap 60s)。**不设次数上限**:foliage 烘焙通常秒级,首试成功率极高;持续失败(如真无树区域)进入退避稳态,日志量可控;观察者离开(`ProcessExited`)与 `RemoveObserver`/会话结束自然清理登记 |
| 空树区域 vs 尚未生成 | **不区分**,统一暂缓语义:两者在 capture 时刻的正确行为一致(等待重试),真无树区域进入 32s 退避稳态。已知取舍:该类区域在观察范围内每 32s 一条 Info 日志 |
| capture 时机事件驱动 | 本轮不做(保持轮询重试);foliage/区域事件驱动作未来优化项 |
| acquire 后段失败的副作用保留 | OnAcquire 抛出时已发生的 adapter 副作用不再回滚(Fake 断言 `Generation==initial+1`):generation 单调性保证多走一格无害;区域进重试队列重新走完整 acquire |
| M6P28 generation 回退 | fail-closed 语义保留(无 lease),但改单区域隔离;重试时 `beforeAcquire` 重读新 generation,区域重建后自然恢复 |
| Restore/Validate 路径换新类型 | 保留(同根语义"区域树条目不存在");两路径均被 `catch (Exception)` 兜住,无行为影响 |
| 多观察者同区域互吞(Standards 轴发现) | 已修:登记按观察者归属(成功清除与离开撤销均只作用自己的登记),obs2 成功 acquire 后 obs1 重试经 `hasLease` 短路直接补齐 replication+demand |
| 重连 limbo(Spec 轴发现) | 复核为**不成立**:connectionChanged 时 `ApplyTargetRegions` 清空全部 ActiveRegions → 全区域走 Exited → 登记被 `ProcessExited` 撤销,重连后全区域重新 Entered |

## 4. 静态门禁(2026-09-04 11:33)

- 主项目 + 测试项目 Release Rebuild:0 errors;
- 唯一测试入口:**259/259 PASS,Failed: 0**(PureMemory + StaticIL + BuildArtifact;Runtime 类仍为 stub/PENDING);
- `git diff --check`:PASS;
- 独立产物验证:`Tools/Verify-BuildFingerprintArtifact.ps1` → `INDEPENDENT_ARTIFACT_VERIFICATION_PASS`;
- **修复版 DLL 指纹**:版本 0.2.4.8,**MVID `6ae0e666-882b-48ca-b6e2-024d79af9aae`**,**SHA-256 `B6A3ACC9131BFCD22564644140D2E874CB854BC5F8FE8D435B39DBC33C885986`**,Case-ID `SPF-0.2.4.8-Experimental-StructureBaseline`,GUID `com.yu80rice.steamp2pfriends` 不变。
- 该指纹替代取证版(`145BEFAA…`):旧日志与旧指纹的证据不得迁移到本构建。

## 5. /code-review 双轴聚合

- **Standards 轴**:无硬违规;判断性提示含 catch 两分支同形(保留:语义/级别/reason 有别,提取会复杂化 StaticIL 契约)、契约测试镜像复制(保留:两方法各自守护,拆分合并会让契约随重构漂移)、`evidence-class-test-gates.md` 计数快照过时(文档为 Ticket 08 时点快照,以测试入口实际输出为准)。
- **Spec 轴**:静态门禁独立复验一致(259/259、指纹、git diff --check);Runtime PENDING 纪律守住;开放点裁决与本报告 §3 补齐;Restore/Validate 换型与 `PendingAcquireRetryCount` 均已记录理由。

## 6. Runtime 边界(不宣称)

本报告为静态交付。修复效果(HOST 进入新区域不再瘫痪、foliage 烘焙后自动补 lease、多观察者场景)必须由 **1 Host + 2 Guest 同 DLL(SHA `B6A3ACC9…`)同 Case-ID 动态测试日志**证明后方可宣称;在此之前 Ticket 11 维持 `implemented-pending-runtime`,不关闭。
