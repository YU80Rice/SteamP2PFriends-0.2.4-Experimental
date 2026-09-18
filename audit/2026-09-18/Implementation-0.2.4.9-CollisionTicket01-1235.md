# Ticket 01 实施与交付报告：0.2.4.9 迁移基线 + Resource 表征门

- **日期**：2026-09-18 12:35
- **基线**：HEAD `4593770`（docs: archive 09-17 join triage and reported-bug notes）
- **票据**：`.scratch/collision-migration-slice/issues/01-version-and-resource-characterization.md`（规格 `.scratch/collision-migration-slice/spec.md`）
- **范围**：Collision Migration Slice 实施票 01——Metadata Source 授予 `0.2.4.9`，把 Resource 已验收行为锁成迁入共享引擎前后的等价门。Collision 接线归票 05/06/08，切片 Runtime 验收归票 09；本票不宣称任何 Runtime PASS。
- **规格对表**：`docs/adr/0009–0014` 与规格「实现决策」「测试决策」两节；票面七项清单。

## 1. 结论

票面七项静态闭环。Metadata Source 授予 `0.2.4.9`（发布通道仍 `Experimental`，默认 Case-ID 由 `SPF-0.2.4.8-Experimental-StructureBaseline` 换为 `SPF-0.2.4.9-Experimental-CollisionSlice`）；Resource 表征门建立（新增 6 项接缝表征 + 1 项空间投影表征 + 1 项半径来源契约 + 1 项切片身份门 + 1 项独立核验门，共 10 项，280→290）；生产 Writer 未变、Collision 未接线；BuildArtifact 可被独立脚本核验（实测 PASS）；Release 双构建 0 error / 0 warning；唯一入口测试 290/290 PASS；三项 PowerShell 门禁 PASS；**双轴审查 round 1–5 逐轮新实例，最终双 CLEAN**。不创建 git tag、不发 GitHub Release。

## 2. 变更清单（修改 11 文件 +465/−14；无新增源码文件）

| 文件 | 变更 |
|---|---|
| `Build/Version.props` | 版本 `0.2.4.8`→`0.2.4.9`；默认 Case-ID 末段 `StructureBaseline`→`CollisionSlice`；通道不变 |
| `README.md` | 徽章与「当前版本」表同步 0.2.4.9；新增「当前阶段」行；「已验证」行加限定语（属 0.2.4.8 已验收构建、本版不重新背书）；PENDING 段落补充 0.2.4.9 自身未取三端日志 |
| `docs/architecture/build-fingerprint-artifact-evidence.md` | 元数据表同步；补 Case-ID 末段标识切片的说明 |
| `docs/architecture/migration-manifest.md` | 新增 Batch 12：0.2.4.9 Collision Migration Slice 基线（变更清单 + 证据类状态 + 必须保留到 Runtime 的验收项） |
| `Tools/Verify-Ticket09Documentation.ps1` | 当前版本展示文档按 `Build/Version.props` 校验；0.2.4.8 历史记录按冻结常量校验（见 §7-3） |
| `WhitelistTests/Evidence/PureMemory/MultiObserver/SpatialObserverIndexTests.cs` | 新增 SPI05：二维投影形状（切比雪夫方形）与世界边界裁剪 |
| `WhitelistTests/Evidence/PureMemory/Adapters/Resource/ResourceProductionControlSeamTests.cs` | 新增 M6P36 生产半径方形投影、M6P37 滞回常量与窗口、M6P38/M6P39 两类失败的重试节奏与上限、M6P40 成功路径保留他人重试资格、M6P41 同区引用计数租约；新增探针辅助 `ProbeAcquireRetrySchedule` 与 Fake 的 `CaptureRegionStateCalls` 计数 |
| `WhitelistTests/Evidence/StaticIL/ResourceProductionControlStaticILContractTests.cs` | 新增半径来源契约（读 `LevelGround.RESOURCE_REGIONS` / `Regions.WORLD_SIZE`，不读 `ItemManager.ITEM_REGIONS`）；抽取 `CountIlTokens` 供方法调用与字段读取共用 |
| `WhitelistTests/Evidence/BuildArtifact/BuildArtifactEvidenceTests.cs` | 新增切片身份门与独立核验门；`QuotePowerShell` 改双引号并抽出 `RunVerifierScript`（见 §3 末条） |
| `WhitelistTests/Program.cs` | 注册 10 项新测试；横幅目标数 255→290 |
| 生产源码（Resource/Collision 补丁、适配器、协调器） | **未触碰** |

## 3. 红→绿链（TDD 闭环，逐轮实测）

1. **RED**：新增 `BuildArtifact Slice Identity`（冻结本阶段版本、通道与默认 Case-ID 字面量）在 0.2.4.8 构建下实测 **FAIL**——288/289，红点恰为身份门。
2. **GREEN**：Metadata Source 授予 `0.2.4.9` 后 **289/289 PASS**，自报指纹 `version=0.2.4.9 assemblyVersion=0.2.4.9 fileVersion=0.2.4.9 caseId=SPF-0.2.4.9-Experimental-CollisionSlice`。
3. **如实具名的定性**：本轮其余 9 项（SPI05、M6P36–M6P41、半径来源契约、独立核验门）自始为绿——它们锁的是**既有已验收行为**（表征门性质），不是本轮的修复对象；本轮真实的 red→green 只发生在版本身份门。
4. **扰动负控制（证明节奏门不是同义反复）**：把生产常量 `DeferredAcquireRetryInterval` 2.0f→1.0f、`FailedAcquireRetryInterval` 10.0f→5.0f 后重跑，**M6P38 与 M6P39 实测 FAIL**（288/290）；恢复常量并及时重建测试项目后 **290/290 PASS**。该实验同时暴露两件事：① 探针把实际重试间隔夹在 (0.9g, 1.1g] 内，改快/改慢都会失败；② 独立核验门会因「被测程序集 ≠ 交付 DLL」而失败（实验中测试项目重建滞后时 `BuildArtifact Independent Verifier` 如实 FAIL）。
5. **存量门禁缺陷修复**：既有 `BuildArtifact Rejects Incomplete Log` 原先把 PowerShell 的 `-File` 路径用单引号包裹，实测退出码 `-196608`（`不支持给定路径的格式`）——脚本**从未真正执行**，任何非零退出都被判为「拒绝成功」，属恒真空过。本轮改双引号并抽出 `RunVerifierScript`，脚本真实执行（拒绝场景实测 `exit=1`），新增的独立核验门实测 `exit=0` 且输出含 `INDEPENDENT_ARTIFACT_VERIFICATION_PASS`。此为存量缺陷，非本票引入，在 §8 具名。

## 4. 验证矩阵（静态门禁，全部实测）

| 门禁 | 结果 |
|---|---|
| 主 DLL Release Rebuild | 0 个错误 / 0 个警告（`warnaserror+`） |
| 测试 exe Release Rebuild | 0 个错误 / 0 个警告 |
| 全套测试（唯一入口） | **290/290 PASS**（原 280 + 新增 10） |
| `Tools/Verify-BuildFingerprintArtifact.ps1` | PASS（`INDEPENDENT_ARTIFACT_VERIFICATION_PASS`） |
| `Tools/Verify-EvidenceClassLayout.ps1` | PASS（`EVIDENCE_CLASS_LAYOUT_PASS`） |
| `Tools/Verify-Ticket09Documentation.ps1` | PASS（`TICKET09_DOCUMENTATION_METADATA_PASS`，5 文档：当前版本 3 + 冻结 0.2.4.8 历史 2） |
| `git diff --check` | CLEAN |

## 5. 产物身份（可复现构建）

| 产物 | SHA-256 | MVID |
|---|---|---|
| `bin/Release/SteamP2PFriends.dll` | `8942D86E0852779A384105F975EA79577F99469609132D47B21C1EAF0DF25297` | `4355d36b-1b9d-4301-a3eb-7d9b3ae142b4` |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `92241F1F5CED7625C498C6701581F72E9573586D4C02D89A2AD17B591ACCBF8B` | `52a85a3c-e50f-4d30-984d-c0bf7f3fa5d6` |

版本 `0.2.4.9`，插件 GUID `com.yu80rice.steamp2pfriends`，默认 Case-ID `SPF-0.2.4.9-Experimental-CollisionSlice`；扰动实验后恢复重建得到与实验前**逐字节相同**的指纹（同上 MVID/哈希），可复现性延续 Ticket 11 结论。按票面第 2 项，**未创建 git tag、未发 GitHub Release**。

## 6. 表征矩阵（票面第 3 项逐条映射）

| 票面要求 | 表征测试 |
|---|---|
| Demand Policy / 空间形状 / 世界裁剪 | `SPI05`（切比雪夫方形：内点 49、角点 16、边缘 28；半径 0 单区）、`M6P36`（生产半径 3 → 7×7 全部建租约）、`M6P01`（同区并集只 acquire 一次）、`M6P41`（引用计数：一人离开不清拆，最后一人离开才进滞回） |
| 半径（来源与数值） | `Test_ProductionRadiusComesFromVanillaObjectRegionSource`（StaticIL：读 `LevelGround.RESOURCE_REGIONS`、`Regions.WORLD_SIZE`，**不**读 `ItemManager.ITEM_REGIONS`——半径不得升格为共享默认值）、`M6P36`（数值 3 的投影结果） |
| 滞回 | `M6P37`（领域常量 `DefaultHysteresisSeconds == 2.0f` 且窗口内保留、到点释放）、`M6P02`、`M6P03`（窗口内重入取消释放且不二次 acquire）、`M6P06`（同帧重入） |
| generation | `M6P05`（generation 流入快照与释放）、`M6P08`（陈旧 generation 推迟释放）、`M6P24`/`M6P25`（generation 回退保留租约/待释放）、`M6P28`（acquire 后回退失败闭合）、`M6P29`（退出回退保留已存租约） |
| retry | `M6P38`（暂缓类 2/4/8/16/32/32，含静默稳态仍按节奏重试）、`M6P39`（一般失败 10/20/40/60/60）、`M6P31`/`M6P33`（失败隔离与回滚恢复登记）、`M6P32`/`M6P34`（暂缓重试成功后补齐、deferred-only 退出不误减 demand）、`M6P40`（成功路径保留他人重试资格，且重试只补自己的复制贡献） |
| 补偿 | `M6P11`–`M6P23`：进入/退出复制失败回滚、区域快照捕获失败隔离、断连补偿、释放补偿与拒绝不补偿、快照内容精确还原 |
| 故障结果 | `M6P22`（会话初始化失败保持未激活）、`M6P26`/`M6P27`（会话收尾失败清态并置 repair-required）、`M6P30`（repair-required 拒绝新会话）、`M6P35`（无 demand 的过时退出容忍不抛） |

## 7. Seam gaps 与取舍（全部具名，无静默跳过）

1. **单槽互吞不锁**：两个观察者同区都失败时，`_acquireRetries` 仍是区域单槽、后写者覆盖先写者。规格 `spec.md:62` 已把它定性为「已被容忍无害化的单槽互吞仍是正式切换阻塞项」，归属票 04。故 `M6P40` 只在摘要中具名该边界、不做断言：正向锁它会与票 04 修复冲突，负向断言则等于「锁缺陷」。Spec 轴 round 3/4/5 均确认该处置正确。
2. **身份门使用字面量**：`Test_SliceIdentityIsPinnedToMigrationStage` 以字面量冻结 `0.2.4.9` / `Experimental` / `SPF-0.2.4.9-Experimental-CollisionSlice`，代价是版本推进时需显式更新一处；收益是「本阶段切片」在 BuildArtifact 类中可被独立断言。曾评估解析 `Build/Version.props` 作为「来源门」，为不给测试宿主新增 `System.Xml.Linq` 引用而放弃——props↔产物↔文档的一致链由两个 PowerShell 门禁独立承担（§4）。
3. **文档门禁与版本解耦**：`Verify-Ticket09Documentation.ps1` 原先把「当前版本」串要求施加到 5 份文档，其中 `.scratch/structure-baseline-0-2-4-8/issues/09-…md` 与 `audit/2026-08-27/Implementation-0.2.4.8-1234.md` 是 0.2.4.8 冻结历史记录；把新版本号写进历史审计等于篡改历史。故拆成两类：当前展示文档按 `Build/Version.props` 校验，历史记录按冻结常量 `0.2.4.8` / `SPF-0.2.4.8-Experimental-StructureBaseline` 校验；判定逻辑与 `TICKET09_DOCUMENTATION_METADATA_PASS` 标记不变，历史审计中的引用继续有效。
4. **影子/正式候选指纹区分不在本票**：票面七项无此要求，切片规格把它归到影子与切换候选票（`issues/05-…md:14`、`07-…md`），`migration-manifest.md` Batch 12 已写明归属；Spec 轴 round 3 复核确认归属正确，不构成票 01 遗漏。
5. **测试宿主未新增依赖**：新增门禁全部落在既有引用与既有能力内（反射读 IL、调用既有 PowerShell 门禁脚本），未改测试项目引用集。

## 8. 判断性坏味道与存量缺陷（全部具名；双轴确认不阻断）

1. **存量缺陷（已修）**：`-File` 单引号导致既有 BuildArtifact 测试空过，见 §3-5。修复顺带移除了该测试与新门禁之间的重复调用代码。
2. **README 表头限定语略臃肿**（Standards round 3）：语义必要（区分 0.2.4.8 已验收 Runtime 与本版未验收），措辞可后续挪到脚注；延期。
3. **`ProbeAcquireRetrySchedule` 四参数簇**（Standards round 2/3/4）：仅两个调用点的测试内探针，未达封装阈值；延期。
4. **IL 断言中的裸字符串类型名**（Standards round 2）：沿用该文件既有 `CountMethodCalls` 风格；延期。
5. **`migration-manifest.md` 承载多类批次信息**（Standards round 1）：符合该文件既有「批次总账」结构；延期。

## 9. 双轴审查链（每轮全新实例，无延续、无复用）

| 轮 | 本轮增量 | Standards | Spec |
|---|---|---|---|
| 1 | 全量首轮（10 测试 + 版本 + 文档 + 门禁） | CLEAN（1 延期：IL 遍历重复） | 2 项：retry 门过松；身份门非独立核验 |
| 2 | 修复上轮 2 项 + 抽取去重 | CLEAN | 2 项：指纹区分属他票（裁定归属 05/07）；README 读作重新背书 Runtime |
| 3 | README 限定语 + M6P40 补断言 | CLEAN（1 条可读性观察） | 1 项：M6P40 名称超出证据范围 |
| 4 | M6P40 改名与摘要收窄 | CLEAN | 1 项：仍残留「按观察者独立存储」暗示 |
| 5 | M6P40 名称与摘要再收窄（只陈述可观察结果） | CLEAN | CLEAN |

每轮均为全新实例、独立上下文；round 5 双轴 CLEAN，审查链闭合。

## 10. Runtime 判读口径（移交票 09）

- `0.2.4.9` 构建本身尚无三端运行日志；本票不宣称 Runtime PASS，也不重新背书 0.2.4.8 的 Resource Runtime 结论。
- Collision 切片三端运行必须使用**正式切换候选**、三端同一 SHA-256；影子 DLL 日志不得当作该条 PASS。
- 表征门（`M6P36`–`M6P41`、`SPI05`、半径来源契约）在票 02/03 迁入共享引擎前后必须保持绿，任何语义漂移视为迁移失败。


