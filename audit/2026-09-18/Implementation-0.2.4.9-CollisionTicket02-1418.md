# Ticket 02 实施与交付报告：Resource 经声明式 Demand Policy 接入共享投影引擎

- **日期**：2026-09-18 14:18
- **基线**：HEAD `a1186d5`（feat(ticket01): grant 0.2.4.9 metadata source and pin the Resource characterization gate）
- **票据**：`.scratch/collision-migration-slice/issues/02-shared-demand-projection-for-resource.md`（规格 `.scratch/collision-migration-slice/spec.md`）
- **范围**：Collision Migration Slice 实施票 02——Control Plane 维护唯一 World Presence Observer 空间事实，共享 Demand Projection Engine 按领域声明式 Demand Policy 算出 typed Resource Demand；Resource 生产接缝改注入引擎并消费投影差异，不再持有自己的观察者索引。租约、滞回、retry、补偿仍留在接缝（票 03 迁编排引擎），生产 Writer 仍唯一。Collision 接线归票 05/06/08，切片 Runtime 验收归票 09；本票不宣称任何 Runtime PASS。

## 1. 结论

票面 9 项静态闭环。Control Plane 新增 `Demand` 模块（唯一空间事实 + 共享投影引擎 + 声明式政策 + typed demand）；Resource 接缝删除私有 `SpatialObserverIndex` 与 `worldSize`/`radius` 字段，改为注入 `DemandProjectionEngine` + `ResourceDemandPolicy`；协调器在唯一接线点建立权威与引擎、把样本资格交给政策判定。资格不合格按 Deferred Observer Demand 暂缓而非释放（原语就位，样本不可用路径归票 04）。租约/复制/生命周期调用图未变，票 01 表征门 10 项全部保持绿。**唯一入口测试 313/313 PASS**、Release 双构建 0 error / 0 warning、三门禁 PASS、`git diff --check` CLEAN；**双轴审查 round 1–4 逐轮新实例，round 4 双 CLEAN**。不创建 git tag，不发 GitHub Release，版本仍为票 01 授予的 `0.2.4.9`。

## 2. 变更清单（修改 7 文件 + 新增 11 文件）

| 文件 | 变更 |
|---|---|
| `Core/ControlPlane/Demand/ObserverPresence.cs` | 新增：唯一 World Presence Observer 空间事实（身份、连接代次、Region Key 中心、玩法资格） |
| `Core/ControlPlane/Demand/ObserverSpatialAuthority.cs` | 新增：唯一空间事实存储（提交幂等、读取、移除、清空） |
| `Core/ControlPlane/Demand/DemandPolicy.cs` | 新增：`EDemandRegionShape`（只实现切比雪夫方形，未声明形状失败闭合）+ 政策（DomainId、半径、世界尺寸、形状、半径来源、资格） |
| `Core/ControlPlane/Demand/DomainDemand.cs` | 新增：typed demand（Domain Id + Region Key + 观察者计数，缺 Domain Id 即拒绝） |
| `Core/ControlPlane/Demand/DomainDemandProjection.cs` | 新增：一次领域投影的差异形状（Domain Id、观察者、连接代次、进入/退出 Region Key） |
| `Core/ControlPlane/Demand/DemandProjectionEngine.cs` | 新增：注册（同域唯一实例）、提交事实并投影、移除、typed demand 查询、计数、投影区域读取与补偿还原、会话边界清理；每域一份 Domain Demand Projection State，共消费同一权威 |
| `Adapters/Resource/ResourceDemandPolicy.cs` | 新增：Resource 域政策声明（Domain Id、切比雪夫、半径来源 `LevelGround.RESOURCE_REGIONS`、资格＝玩法资格） |
| `Adapters/Resource/ResourceProductionControlSeam.cs` | 改：删私有索引与 `worldSize`/`radius` 字段，注入引擎+政策；构造期拒绝非 Resource 身份政策；新增 `DemandPolicy`/`ProjectedDemandRegionCount` 与带资格重载；会话收尾清 Control Plane 会话状态（含失活兜底，不动 repair-required） |
| `Core/ControlPlane/MultiObserverShadowCoordinator.cs` | 改：接线点建立唯一权威+引擎并注入；样本携带资格交由政策判定（不再自行丢样本）；`EndSessionIfNeeded` 无条件收尾 Control Plane 会话状态；汇总日志新增投影需求区域数与半径来源 |
| `Core/ControlPlane/Spatial/SpatialObserverIndex.cs` | 改：新增 `CountObserversPerRegion` / `CountObserversInRegion` / `TryGetConnectionToken`（计数现算，不另存会漂移的计数表） |
| `SteamP2PFriends.csproj` / `WhitelistTests/…csproj` | 改：登记新增编译项 |
| `WhitelistTests/Evidence/PureMemory/MultiObserver/DemandProjectionEngineTests.cs` | 新增：DPE01–DPE11 |
| `WhitelistTests/Evidence/PureMemory/Adapters/Resource/ResourceProductionControlSeamTests.cs` | 改：新增 M6P42–M6P46；既有 41 项改经测试宿主工厂 `CreateSeam` 接线，断言不变 |
| `WhitelistTests/Evidence/StaticIL/DemandProjectionStaticILContractTests.cs` | 新增：7 项结构契约 + 合成门 |
| `WhitelistTests/Evidence/StaticIL/IlContractProbe.cs` | 新增：IL 遍历共享探针（消除票 01 审计 §8-4 具名的跨文件重复） |
| `WhitelistTests/Evidence/StaticIL/ResourceProductionControlStaticILContractTests.cs` | 改：私有 IL 遍历改为转发共享探针（调用点/断言不变）；`UpdateObserver` 断言按 4 参重载消歧 |
| `WhitelistTests/Program.cs` | 改：注册 23 项新证据，目标数 290 → 313 |
| `docs/architecture/migration-manifest.md` | 改：新增 Batch 13（变更清单、证据类状态、不可变语义、移交票 04） |
| `.scratch/collision-migration-slice/issue.md` | 改：前沿 02 → 03；新增「票 02 → 票 04 的具名交接」段 |
| `.scratch/collision-migration-slice/issues/02-…md` | 改：9 项 checklist 勾选、状态置 `implemented-pending-runtime` |

## 3. 红→绿链（TDD 闭环，逐轮实测）

1. **RED-A（结构契约）**：先落地 `DemandProjectionStaticILContractTests.cs`（纯反射，可对未改动的生产程序集编译）并注册 → 实测 **291/296**：新增 6 项结构契约全 FAIL（唯一权威/接缝无索引/无原生依赖/无共享默认半径/引擎不调领域/Resource 自声明政策），旧 290 项全绿。
2. **RED-B（引擎行为）**：Control Plane 模块以「`Observe` 返回空投影」骨架落地并注册 DPE01–DPE10 → 实测 **294/306**：DPE 10 项中 9 项 FAIL（声明面校验 1 项自始为绿，因为它是纯构造校验）。
3. **GREEN-B**：实现 `DomainDemandProjectionState.Apply`（资格门 + 切比雪夫枚举/裁剪/去重/差异）→ DPE 全绿。过程中修正两处**测试自身**的错误期望（DPE03 误以为移动后共享区域计数回落到 1；DPE07 在同一次运行里把「资格恢复后」的状态用于断言「不合格时」的状态），并如实记录在此。
4. **RED-C（接缝契约）**：落地 `CreateSeam` 测试宿主工厂、M6P42–M6P45 与接缝调用点改名 → 测试项目**编译失败 15 处**（参数 4/5 类型不符、`ProjectedDemandRegionCount` 不存在、`UpdateObserver` 无 `gameplayAuthorized` 重载）。静态语言下这是新增契约的即时红点，如实具名（区别于票 01 的「测试运行时红」）。
5. **GREEN**：重写接缝（注入引擎+政策、删除私有索引）与协调器接线 → **308/310**；修两处（M6P45 断言 9 个区域应释放 9 次；既有 StaticIL 契约因新增重载导致 `GetMethod` 歧义）→ **310/310**。
6. **审查修复轮（见 §9）**：Round 1–4 的发现各自带来一个小增量：会话边界清理（+M6P46）、资格暂缓语义（DPE07 重写）、资格交由政策判定（+StaticIL 契约）、暂缓中代次失效（DPE11）→ 最终 **313/313**。
7. **扰动负控制（证明门禁非同义反复）**：
   - 给接缝加回一个私有 `SpatialObserverIndex` 字段 → `Demand Single Spatial Authority` 与合成门实测 **FAIL**（308/310）；
   - 把引擎半径写成常量 1（不读政策）→ **29 项 FAIL**（281/310），含票 01 表征门 M6P36–M6P41 与全部 Resource 接缝用例；
   - 两次扰动后恢复并重建，回到全绿。

## 4. 验证矩阵（静态门禁，全部实测）

| 门禁 | 结果 |
|---|---|
| 主 DLL Release Rebuild | 0 个错误 / 0 个警告（`warnaserror+`） |
| 测试 exe Release Rebuild | 0 个错误 / 0 个警告 |
| 全套测试（唯一入口） | **313/313 PASS**（票 01 后 290 + 新增 23） |
| `Tools/Verify-BuildFingerprintArtifact.ps1` | PASS（`INDEPENDENT_ARTIFACT_VERIFICATION_PASS`） |
| `Tools/Verify-EvidenceClassLayout.ps1` | PASS（`EVIDENCE_CLASS_LAYOUT_PASS`） |
| `Tools/Verify-Ticket09Documentation.ps1` | PASS（`TICKET09_DOCUMENTATION_METADATA_PASS`） |
| `git diff --check` | CLEAN |

## 5. 产物身份（可复现构建）

| 产物 | SHA-256 | MVID |
|---|---|---|
| `bin/Release/SteamP2PFriends.dll` | `55B51DACB2B92AD3226516C86C43E7D64EB14B887CFEBF6377EE985F473735A3` | `14512d43-fc6c-4b89-8c35-42f3b4d23131` |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `C281ED5CAC8505E8C56C3128427C5D772E42748DAE1EB7E614BD528B8FD3D51` | `f1c70cc7-e457-47f3-bc73-4344d58c05fa` |

版本 `0.2.4.9`，插件 GUID `com.yu80rice.steamp2pfriends`，默认 Case-ID `SPF-0.2.4.9-Experimental-CollisionSlice`（与票 01 相同，本票不改版本身份）。**未创建 git tag、未发 GitHub Release。**

## 6. 票面逐项映射

| 票面要求 | 落地证据 |
|---|---|
| Control Plane 维护唯一 World Presence Observer 空间事实 | `ObserverSpatialAuthority`（`Observe` 幂等、`TryGet`/`Remove`/`Clear`）；接线点构造一次并注入接缝；`Test_ControlPlaneOwnsSingleSpatialAuthority` 断言协调器各构造一次、接缝零权威字段/构造、引擎恰一份权威；DPE01 断言两个领域共用同一事实 |
| 投影引擎负责枚举/裁剪/去重/计数/进入退出差异 | `DemandProjectionEngine` + 每域 `DomainDemandProjectionState`；DPE02（切比雪夫方形 49/16/28/1）、DPE03（去重与计数、交集 2/边缘 1）、DPE04（移动 3 进 3 出、同位无差异）、DPE05（代次失效 9 进 9 出） |
| Resource 只声明资格、半径、切比雪夫政策与 Domain Id | `ResourceDemandPolicy.Create`（`Test_ResourceDomainDeclaresItsOwnDemandPolicy`：恰一次构造政策、恰一次读 `DomainIds.Resource`）；`Test_NoSharedDefaultRadiusInControlPlane` 断言 Demand 命名空间零原生常量读取；`Test_ProductionRadiusComesFromVanillaObjectRegionSource`（票 01）保持绿 |
| 输出 typed Resource Demand；身份 ≥ Domain Id + Region Key | `DomainDemand`（缺 Domain Id 构造即拒绝，DPE08）；`DomainDemandProjection` 携带 Domain Id；接缝只经 typed 投影取区域键；`Test_DPE06_DomainDemandIsNotGrantedAcrossDomains`（未注册政策拒绝、同域不同实例拒绝、他域无需求） |
| Resource 不再持有独立漂移的观察者索引 | 接缝零 `SpatialObserverIndex`/权威字段与构造（StaticIL 锁定，扰动实测 FAIL）；`M6P42` 断言事实与需求归 Control Plane；`M6P46` 断言会话边界清事实与投影 |
| Resource 仍是唯一生产 Writer | 租约/复制/生命周期调用图未变；票 01 的 `OnAcquire→TryCommitAcquire`、`OnRelease→CommitRelease`、`OnObserverRelease` 零调用等契约保持绿；`Test_ProjectionEngineDoesNotWriteDomainState` 断言投影引擎零领域调用 |
| 01 的表征行为保持 | M6P36–M6P41、SPI05、半径来源契约全部保持绿；半径扰动实测 29 项 FAIL 证明表征门仍有效；滞回 2.0s、retry 2/4/8/16/32 与 10/20/40/60、物化 demand 计数、事务补偿语义未改 |
| PureMemory 红先再绿；StaticIL 与 BuildArtifact 通过 | 见 §3（RED-A/RED-B/RED-C 与扰动）；StaticIL 7 项新契约 + 既有契约全绿；BuildArtifact 独立核验 PASS（§4/§5） |
| 标记 implemented-pending-runtime，不宣称投影迁移 Runtime | 票面状态、`issue.md` 前沿、`migration-manifest.md` Batch 13 三处一致；本报告不宣称任何 Runtime PASS |

## 7. Seam gaps 与取舍（全部具名，无静默跳过）

1. **静态语言的红点形态**：RED-C 是编译失败而非测试运行失败（新契约需要尚不存在的构造与成员）。如实具名，不冒充运行时红；票 01 的运行时红（版本门）在本票不复现，因为本票不改版本身份。
2. **投影计数与物化计数并存**：`resourceProjectedDemandRegions`（引擎按政策算出的需求区域数）与 `resourceDemandRegions`（接缝已物化租约的区域数）口径不同。引擎口计入 DCSE 的 `DemandRegionCount` 汇总消费；物化口是租约生命周期状态，随票 03 迁入编排引擎。两者分叉即「有投影未物化」（暂缓/失败重试路径），已在协调器注释与 Batch 13 具名。
3. **暂缓语义的边界**：资格不合格＝暂缓（保留贡献、不发退出），清理只由确认离开、连接代次失效、会话重置或有界恢复策略触发。**有界恢复策略不在本票**：它与样本不可用路径同属票 04（票面 checklist 第 1–2 项，`Blocked by: 03`）。
4. **不修改样本捕获路径**：`CaptureSamples` 的「单条坏记录冻结本拍」与「无效样本未进入 Deferred Observer Demand」是票 04 的准入阻塞项（`spec.md:61`），本票 `git diff` 未触碰该路径，并在共享引擎提供了其所需原语。
5. **`gameplayAuthorized` 重载的意义**：生产调用点当前恒真（候选资格在捕获层计算），但它是「资格由领域政策声明、由引擎执行」的唯一通道；StaticIL `Test_ResourceEligibilityIsDelegatedToPolicy` 断言协调器把 `get_GameplayAuthorized` 交给引擎而非自行丢样本。4 参重载保留以兼容既有调用点与契约。
6. **补丁计数与登记不变**：本票不新增/不删除 Patch、不改 Harmony 元数据、不改协议与配置键。

## 8. 判断性坏味道与存量缺陷（全部具名；双轴确认不阻断）

1. **`CountObserversPerRegion` 与 `CountObserversInRegion` 方法体近似**（Standards round 2/4）：两入口分别服务「全区计数」与「单区查询」，可按需合并为一个内部枚举；延期。
2. **`ToSpatialRelevanceDiff` 的 Middle Man 形状**（Standards round 4）：`DomainDemandProjection` 与 `SpatialRelevanceDiff` 字段同构，转换方法只为兼容接缝既有对外契约；待票 03 迁编排时一并收口；延期。
3. **`MultiObserverShadowCoordinator` 单轮承载多动因改动**（Standards round 2/3/4）：接线、日志字段、会话收尾、资格语义四类改动同文件同轮；该文件本就是唯一接线点，延期。
4. **`EDemandRegionShape` 单值枚举**（Standards round 4）：只有切比雪夫方形一个合法值，但票面明确要求「Resource 只声明……切比雪夫政策」，且未声明形状失败闭合（DPE08 锁定）；延期。
5. **`TryGetDemand` / `DomainDemand` 暂无生产消费**（Standards round 2/3）：typed demand 的读口是编排引擎（票 03）的输入，本票要求「输出 typed Resource Demand」且由 DPE06 锁定身份语义；延期至票 03。
6. **文档与代码一致性缺口（已修）**：Batch 13 变更表初版漏列 `ResourceProductionControlStaticILContractTests.cs` 的修改与 `WhitelistTests/Program.cs` 的注册行（Standards round 4 具名），本轮补齐。
7. **PASS 基数口径（已修）**：票面状态行一度记 `310/310` 而实际为 313（Standards round 3 具名），本轮统一为 313；`Program.cs` 目标数同步。

## 9. 双轴审查链（每轮全新实例，无延续、无复用）

| 轮 | 本轮增量 | Standards | Spec |
|---|---|---|---|
| 1 | 全量首轮（Control Plane 模块 + 接缝改写 + 23 项新证据 + 文档） | CLEAN（6 判断项，2 项优先：零消费表面、双计数日志字段） | **BLOCKING 2**：Pending Guest 被当作离开处理；会话重置未清唯一空间事实 |
| 2 | 会话边界清理（`DemandProjectionEngine.EndSession`）+ 删零消费 API + 约束注释 + `M6P46` | CLEAN（3 判断项） | **BLOCKING 2**：Pending Guest 仍成立（并指资格被固化进新共享面）；会话失配残留路径 |
| 3 | 资格不合格改为暂缓（Deferred Observer Demand）+ 资格交由政策判定 + StaticIL 新契约 | CLEAN（6 判断项 + 1 项文档计数不一致需澄清） | **BLOCKING 2**：样本捕获整体冻结（引 `spec.md:60`）；暂缓中连接代次变化未失效 |
| 4 | 代次失效边界修复 + 文档计数与 Batch 13 对齐 + 票 04 具名交接（`issue.md` / Batch 13） | **CLEAN**（0 硬违规；4 判断项 + 1 可延期文档缺口，已修） | **CLEAN**（0 BLOCKING；五项逐项「无差距」） |

每轮均为全新实例、独立上下文（未使用 SendMessage 续接任何上一轮实例）。round 4 双轴 CLEAN，审查链闭合。审查期间的两处**测试自身错误期望**（DPE03/DPE07）与一处**既有契约歧义**（`UpdateObserver` 反射 `GetMethod` 因新增重载而歧义）在 §3 具名并修正。

## 10. Runtime 判读口径（移交票 09）

- `0.2.4.9` 构建本身尚无三端运行日志；本票不宣称 Runtime PASS，也不重新背书 `0.2.4.8` 的 Resource Runtime 结论。
- 三端运行必须使用**正式切换候选**、三端同一 SHA-256；影子 DLL 日志不得当作该条 PASS。
- 票 01 表征门（M6P36–M6P41、SPI05、半径来源契约）与票 02 新增投影契约在票 03 迁入编排引擎前后必须保持绿，任何语义漂移视为迁移失败。
- 建议在三端日志中核对 `resourceProjectedDemandRegions` 与 `resourceDemandRegions`：分叉应能被解释为暂缓/失败重试路径，长期无解释分叉即为缺陷信号。

