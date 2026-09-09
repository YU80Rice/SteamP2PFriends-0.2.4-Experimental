# Ticket 12 实施与交付报告:Resource Seam 回滚-重试登记一致性(R4 治本+兜底)与 R2 静默稳态

- **日期**:2026-09-10 00:27
- **基线**:HEAD `c4e34b5`(Ticket 12 开票提交)
- **票据**:`.scratch/structure-baseline-0-2-4-8/issues/12-resource-seam-rollback-retry-consistency.md`
- **范围**:R4 治本+过时退出容忍兜底+R2 静默稳态+两个可并票项(2337 §四 BOM、§六坏味道评估)
- **Runtime 验证归属**:本票。交付后停等用户 1H+2G 三端实测(复测剧本见 §8),证据回传后按 Runtime-2314 同口径审计。

## 1. 结论

R4 与 R2 全部按票面完成并通过红→绿闭环、263/263 全绿、四项静态门禁与双轴独立审查(Standards/Spec 双 CLEAN,round 1)。新交付物指纹见 §5。**本报告落盘前不宣称 Resource 领域 fault-free——Runtime 证据未到位,票据维持实施中状态。**

## 2. 变更清单(4 文件,+142/-5)

| 文件 | 变更 |
|---|---|
| `Adapters/Resource/ResourceProductionControlSeam.cs` | ① R4 治本(尾部):`ProcessSingleRegionEntry` 尾部清除本观察者 acquire 重试登记时登记补偿 `() => _acquireRetries[region] = ownRetry`,事务回滚即恢复登记;② R4 治本(同缺陷类超集):`ProcessExited` 头部撤销登记同样纳入补偿(Spec 审查确认为服务同一一致性目标的合理超集,非范围蔓延);③ R4 兜底:`ProcessExited` 对 demand<=0 且无本观察者登记的过时退出,记 `RegionExit outcome=skipped reason=stale-exit-without-demand`(封闭枚举)后跳过,不抛 underflow;不动其他观察者的有效登记;④ R2:新增 `DeferredAcquireQuietAttempts=5`,deferred 连续重试达到阈值后降级静默稳态(仅计数不逐条 Info),可见日志补 `attempts=` 字段;重试间隔/清除语义不变 |
| `WhitelistTests/Evidence/PureMemory/Adapters/Resource/ResourceProductionControlSeamTests.cs` | 新增 M6P33/M6P34/M6P35(见 §3) |
| `WhitelistTests/Program.cs` | 唯一测试入口注册 3 个新测试(259→263 注册数,单一入口纪律保持) |
| `Tools/Verify-Ticket09Documentation.ps1` | 补 UTF-8 BOM(解析期修复)+ 文档读取改 `[IO.File]::ReadAllText(…, UTF8)`(内容期修复,与 2337 §四镜像口径一致);PS5.1 原样运行 PASS |

未触及:Harmony/频道/GUID/`Build/Version.props`/结构不变量/acquire 隔离与滞回语义;`DecrementDemand` 在需求为正路径的 fail-fast 保留(seam:820-823)。

## 3. 红→绿链(TDD 闭环)

1. **RED**(修复前实测):M6P33 **FAIL——逐帧复现 Runtime-2314 §3 故障链**:`UpdateObserver` 回滚吞掉登记(无补偿)→ 第三次重连 `ProcessExited` 对该区域走完整路径 → `DecrementDemand` underflow(seam:795 ← ProcessExited:578 ← UpdateObserver:275),与实机 4 次 M0 会话重建的根因链一致;M6P35 FAIL(容忍路径不存在,underflow 抛出);M6P34 为既有正确行为回归锁(绿,登记存在时撤销分支本就接住 deferred-only 退出)。
2. **GREEN**(修复后):M6P33/M6P34/M6P35 全 PASS,全套 **263/263**(原 260+新增 3,0 FAIL)。

测试设计口径:
- **M6P33** 回滚恢复登记:半径 1 九区域、(11,10) acquire 失败入登记 → 重试到期且本区域可成功 → 同事务断连失败制造整批回滚 → 断言登记恢复(`PendingAcquireRetryCount==1`)+demand/spatialIndex/token 一致 → 下一次 Update 重试成功补齐 lease(`Acquires.Count==10`)。
- **M6P34** 回归锁:deferred-only 区域随 `RemoveObserver` 整体退出,不抛、不产生该区域 pending release,其余 8 区域正常滞回(`Exited==8`/`PendingReleaseCount==8`)。
- **M6P35** 兜底容忍:反射清除登记伪造 R4 失衡残留(历史回滚时代产物/未知路径),过时退出不再 underflow,会话保持 active。

## 4. 验证矩阵(静态门禁,全部实测)

| 门禁 | 结果 |
|---|---|
| 主 DLL Release Rebuild | 0 errors / 0 warnings |
| 测试 exe Release Rebuild | 0 errors / 0 warnings |
| 全套测试(唯一入口) | **263/263 PASS** |
| `Tools/Verify-BuildFingerprintArtifact.ps1` | PASS(INDEPENDENT_ARTIFACT_VERIFICATION_PASS) |
| `Tools/Verify-EvidenceClassLayout.ps1` | PASS(EVIDENCE_CLASS_LAYOUT_PASS) |
| `Tools/Verify-Ticket09Documentation.ps1`(PS5.1 原样) | PASS(5 文档 21 必需串,TICKET09_DOCUMENTATION_METADATA_PASS) |
| `git diff --check` | CLEAN |

## 5. 产物身份(新指纹,可复现构建)

构建机制与 7d0f5fd 一致(portable PDB + PathMap,本轮未触碰构建配置,跨路径可复现性延续 2337 §二独立复验结论):

| 产物 | SHA-256 | MVID |
|---|---|---|
| `bin/Release/SteamP2PFriends.dll` | `BBBDCC25AAD64E87E2B61A96559D9CD07988BF81468D4B76C574EEF5249B3DC3` | `cdac3a0a-afe2-4054-9172-afd316d85570` |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `86C4D5E6028159A85A8C00DA0EB9423915AF0C661EA6296AB73321CF084D17D3` | `4050716b-db3e-4427-a1f6-add1a0d878ea` |

**Provenance 更新(2337 §九-5)**:Ticket 11 期间实测登记的测试 exe 指纹 `32B9B7B5…`/`b19b0910…` 自本票起被上表取代(源码已变,旧指纹不再可由 HEAD 复现);主 DLL 指纹由 `5DF5A1F3…`/`2ff47d8f…` 同步更新。上表为本轮**唯一有效身份**,Runtime 证据绑定以本表为准。

## 6. Seam gaps 与取舍(全部具名,无静默跳过)

1. **PureMemory 无法断言日志内容**:`RoleLogger` 未初始化时 `_logger?.Log…` 为 no-op,测试宿主无 sink。处置:容忍路径/R2 降级均以状态断言为主(沿用 M6S11/R1 先例),日志行为(`reason=stale-exit-without-demand` 可见、deferred 每区域 ≤5 条)列入 §8 复测剧本由 Runtime 复核。
2. **R4-④(2314 §3 可选项)未采纳**:`DecrementDemand` 失败路径 message 埋点未实施——兜底容忍已消除该 underflow 的可达性,埋点失去观测对象;票面未列入此项。
3. **ProcessExited 头部撤销纳入补偿(超集)**:票面治本只点名尾部;头部撤销属同一缺陷类(登记清除游离于补偿之外),不修则留下同构失衡源。Spec 轴确认"直接服务同一一致性目标,不构成范围蔓延"。
4. **脚本显式 UTF-8 读取(超一行 BOM)**:仅补 BOM 时 PS5.1 解析期已过但内容期失败——五份文档本体无 BOM,`Get-Content` 按 ANSI 读取破坏中文 Ordinal 匹配。改为显式 UTF-8 读取(2337 §四镜像已验证的口径)方达成票面验收"PS5.1 原样运行 PASS"。Spec 轴确认属同一编码缺陷的必要修复。

## 7. 判断性坏味道(全部延期,理由具名)

**2337 §六两条**:
1. Divergent Change(提交捆绑多类关注点)——延期理由:本票捆绑本身即票面"可并票项"授权(R4+R2+两小项);后续票保持单一关注点。
2. Duplicated Code(两 csproj PathMap/DebugType 重复)——延期理由:抽共享 props 需触碰构建身份配置并重证跨路径可复现,与本轮 fault 修复同轮进行恰是坏味道①形态;建议独立小票处理。

**本轮新增两条(Standards 轴 round 1 点名,均可延期)**:
3. `ResourceProductionControlSeam.cs` 尾部/头部两处「Remove+补偿写回」同构——两行重复,拆助手不值本轮;随下一次该文件结构轮收口。
4. M6P35 反射触碰 `_acquireRetries` 私有表示——票据允许的伪造残留手段;若后续需要更多残留场景,可评估正式测试缝(如 internal 观测器)。

## 8. Runtime 复测剧本(1H+2G,归属本票)

1. **部署**:将 `bin/Release/SteamP2PFriends.dll`(SHA-256 `BBBDCC25…B3DC3`)覆盖三端 `Unturned\BepInEx\plugins\SteamP2PFriends.dll`(覆盖前备份);三端各执行 `Get-FileHash <dll> -Algorithm SHA256` 与上值比对,不一致即停。
2. **场景**:按既有 SOP 1 Host + 2 Guest 进图,重点覆盖**客机反复进出无树/未烘焙区域 + 断线重连 + 更换区域**(Runtime-2314 的 4 次 fault 全部发生在重连/退出瞬间);正常采伐/移动/离开各留证据。
3. **预期(UMM 诊断包审计口径,同 Runtime-2314)**:
   - `InvalidOperationException`/`ObserverUpdate failed` **0 次**;`M0 fault` 0 次;`session-end reason=fault-recovery` 会话重建 **0 次**;
   - R4 兜底可观测:`event=RegionExit … outcome=skipped reason=stale-exit-without-demand` 允许出现(容忍路径生效标记),不得伴随任何 throw;
   - R2 降级生效:单区域 deferred 日志 `attempts=` 计至 5 后静默(每区域每会话 ≤5 条),deferred 总量从 2317/轮大幅下降;
   - `LeaseAcquire success`/采伐 accepted/滞回-重入/双端复制行为与 2314 轮持平(不回归)。
4. **证据回传**:三端 UMM 诊断包;回传后按 Runtime-2314 同口径审计落盘 `audit/2026-09-10/Runtime-0.2.4.8-Ticket12-*.md`,三端指纹须与本报告 §5 逐字节一致(不一致即停)。

## 9. 双轴独立审查链(output-review-loop 闭环记录)

- **Round 1**(全新并行实例,互不可见):
  - Standards 轴:**CLEAN**,无成文硬违规、无阻断 findings;判断性坏味道 3 条点名(§7-3/4/历史两条)。
  - Spec 轴:**CLEAN**,票面 9 项逐条 ✅,无缺口/偏差/范围蔓延;确认头部补偿超集与脚本 UTF-8 读取在票面意图内。
- 无修复循环;两轴最终裁决均 CLEAN,产物身份(§5)在本轮闭环后授予。

## 10. 审计纪律

- 实施全程工作区未提交(审查对象=未提交增量 diff);审查与实施互不污染,审查官全程只读。
- 大写入分批执行:测试逐方法插入(3 个 Edit)、实现 5 个 Edit(每处 ≤60 行)、批次间自动连续推进,符合 `docs/agents/large-write-batching.md`。
- 本报告之外未改动任何审计文件;票据状态更新与本报告登记(`audit/README.md`)随本票提交进行。
