# Ticket 05 实施与交付报告：Collision 声明式 Demand Policy 与只读影子验证

- **日期**：2026-09-18
- **基线**：HEAD `e5fb396`（docs(ticket04): close the review chain on zero-context verdicts）
- **票据**：`.scratch/collision-migration-slice/issues/05-collision-demand-policy-and-readonly-shadow.md`（规格 `.scratch/collision-migration-slice/spec.md`）
- **范围**：Collision Migration Slice 实施票 05——Collision 按原版物件区域半径政策从同一套 canonical 观察者事实（**含 Host**）算出 typed Collision Demand，并与旧 `RemoteCoverage` 覆盖做**可分类的只读对照**。**旧 Writer 仍是唯一生产写入者**；影子只用于发现准入问题，不当作正式切换证据。本票不产出 Execution Port、不产出 Acquisition Receipt、不切换 Authority Writer（分别归票 06/08）。

## 1. 结论

票面 10 项中 9 项静态闭环、第 9 项（只读 1H2G 影子 Runtime）**按票面要求仍待人工执行**，本票不宣称其通过、在影子日志回传前**不视为关单**。落地内容：①`CollisionDemandPolicy`（Domain Id = Collision、原版 `LevelObjects.OBJECT_REGIONS` 切比雪夫、资格沿用 World Presence Observer、**Host 亦在其列**、半径不升格为共享默认值）；②只读影子对照分类器（11 类：预期差异 4 类 / 禁止差异 7 类，全部差异按观察者贡献与聚合 Domain Id + Region Key 分类并带原因词，分类不出来即失败闭合；影子路径「确认离开才清理」，暂缓不等于离开）；③旧覆盖**只读快照**入口（快照法体零原生调用、零旧写入路径调用）；④协调器唯一接线点把 Collision 政策注册到**同一个**共享投影引擎与**同一份**观察者事实上，每拍跑只读对照并输出分类计数与有界心跳；⑤**影子候选与正式切换候选的构建指纹可区分**（候选角色进程序集元数据与默认 Case-ID，同一源码换角色即不同 SHA-256 / MVID）。**唯一入口测试 386/386 PASS**、Release 双构建 0 error / 0 warning 且两次指纹逐项一致、四门禁 PASS（含 `git diff --check`）、扰动负控制 19 轮逐项取证（1 轮无咬合已具名、1 轮首形态被 `warnaserror` 拦下后改形重测、1 轮取证脚本命中同形旁路已具名并重测）。**双轴审查已闭合**：第 1–4 轮逐轮两轴各一新实例并行派发，Spec 轴先后给出 5/4/4/2 项 BLOCKING（含六项实质缺陷：离开者未从 Collision 投影清理、暂缓样本被误判为离开、暂缓者保留需求未进对照帧、身份不可判定时仍断言离开、暂缓者缺认领导致假红、分类命名过强），逐条修复后由第 5 轮零上下文双轴复审收尾：**Standards CLEAN（0 硬违规）/ Spec CLEAN（无差距无偏离）**（见 §10）。未创建 git tag、未发 GitHub Release，版本仍为票 01 授予的 `0.2.4.9`。

## 2. 变更清单（修改 13 文件 + 新增 6 文件）

| 文件 | 变更 |
|---|---|
| `Adapters/Collision/CollisionDemandPolicy.cs` | 新增：Collision 域的 Demand Policy 声明（Domain Id、切比雪夫形状、半径来源字面量、资格=观察者事实的玩法资格）。只声明，不扫描名册、不持有位置、不内置半径 |
| `Adapters/Collision/CollisionShadowFrame.cs` | 新增：影子对照一拍输入（新侧当前/上一拍区域集合、旧侧覆盖、两侧认领者）与两种认领者类型（canonical 事实 / 旧侧身份+中心+当前是否合格） |
| `Adapters/Collision/CollisionShadowReport.cs` | 新增：差异分类枚举（4 预期 + 7 禁止）、处置枚举、差异（聚合键 Domain Id + Region Key，归因带观察者与连接代次，另带原因词 `Reason`）与报告（InBoth / Expected / Forbidden 计数） |
| `Adapters/Collision/CollisionShadowComparator.cs` | 新增：只读、无副作用、无跨拍状态的分类器——异域整帧失败闭合、越界区域键、Host 新增覆盖、旧侧无在场合格认领者、canonical 生命周期补齐、授权认领者缺区、静止抖动、跨观察者错误释放、凭空需求 |
| `Adapters/Collision/Patches/LevelObjectRemoteCollisionPatch.cs` | 已改：**只读** `CaptureShadowSnapshot`——抄出旧覆盖区域与旧侧远端玩家中心，编码转换留在适配器边界内；旧 Writer 的写入路径一字未动 |
| `Core/ControlPlane/MultiObserverShadowCoordinator.cs` | 已改：①`ConfigureResourceProduction` → `ConfigureControlPlane`（接线点现在同时注册 Resource 与 Collision 两条政策，世界尺寸只读一次，详见 §8）；②`RunCollisionShadow`（提交 canonical 事实 → 抄旧覆盖 → 分类 → 记录；**零原生写入调用**）；③禁止差异有界心跳 + 恢复闭环；④汇总日志新增 `collisionDemandRegions` / `collisionRadiusSource` / `collisionShadowInBoth` / `collisionShadowExpected` / `collisionShadowForbidden` / `collisionShadowReadOnly`；⑤会话边界与状态重置清影子基线 |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDomainModules.cs` | 已改：接线点调用改名（唯一调用点） |
| `Build/Version.props` | 已改：新增 `SteamP2PFriendsCandidateRole`（默认 `ReadOnlyShadow`），默认 Case-ID 追加角色后缀 |
| `Properties/AssemblyInfo.cs` | 已改：新增 `SteamP2PFriendsCandidateRole` 程序集元数据（角色进产物身份） |
| `Core/Build/BuildFingerprint.cs` | 已改：快照新增 `CandidateRole`（缺失即视为身份不完整，fail-closed），自报告行输出 `candidateRole` |
| `SteamP2PFriends.csproj` | 已改：登记 4 个新增编译项；生成的 `BuildMetadata` 新增 `CandidateRole` |
| `Tools/Verify-BuildFingerprintArtifact.ps1` | 已改：解析候选角色、校验产物内嵌角色与 `Build/Version.props` 一致、输出 `CandidateRole` |
| `Tools/Verify-Ticket09Documentation.ps1` | 已改：Case-ID 解析补上候选角色占位符（文档门禁与产物身份保持同一来源） |
| `WhitelistTests/Evidence/PureMemory/Adapters/Collision/CollisionShadowComparatorTests.cs` | 新增：CSC01–CSC17（见 §5） |
| `WhitelistTests/Evidence/StaticIL/CollisionShadowStaticILContractTests.cs` | 新增契约：政策自声明、资格沿用观察者事实、影子路径纯内存且不扫名册、旧覆盖快照只读、影子路径零原生写入、旧 Writer 仍是唯一生产写入者、比较器只有唯一只读消费点（含聚合门共 8 项） |
| `WhitelistTests/Evidence/BuildArtifact/BuildArtifactEvidenceTests.cs` | 已改：切片身份门更新为带候选角色的默认 Case-ID；新增候选角色可区分门 |
| `WhitelistTests/Program.cs` | 已改：注册 26 项新证据（CSC01–CSC17、Collision Shadow StaticIL 8 项、BuildArtifact 候选角色 1 项）；唯一入口目标数 360 → 386 |
| `docs/architecture/build-fingerprint-artifact-evidence.md` | 已改：候选角色字段、Case-ID 新结构、`candidateRole` 自报告字段 |
| `docs/architecture/migration-manifest.md` | 已改：新增 Batch 16（变更清单、证据类状态、新证据清单、扰动负控制、不可变语义、票 06/07/08 移交） |
| `.scratch/collision-migration-slice/issue.md` | 已改：前沿 05 → 06；新增票 05 → 06/07/08 的具名移交 |
| `.scratch/collision-migration-slice/issues/05-…md` | 已改：10 项逐条标注落地证据；第 9 项标注为待人工执行的影子 Runtime |

## 3. 红链与负控制（TDD 闭环，逐轮实测）

1. **RED（行为面）**：先落地 4 个纯类型骨架（数据完整、比较器法体为空报告）与 CSC01–CSC12，注册后实测 **361/372 PASS**：11 项 CSC 全 FAIL（CSC12 当时已绿——它锁的是政策声明与共享引擎的既有行为）。原始输出：`.scratch/collision-migration-slice/evidence/ticket05-red-csc.log`。
2. **GREEN（行为面）**：实现分类器后 **372/372 PASS**。分类器每个判定分支的红点另由 §4 的扰动逐项取得（P1–P7）。
3. **GREEN（结构面）**：落地旧覆盖只读快照、协调器接线与 8 项结构契约后 **380/380 PASS**；新增构建候选角色与对应 BuildArtifact 门后 **381/381 PASS**；审查轮 1 后拆分旧侧差异分类并补 CSC13/CSC14 后 **383/383 PASS**，审查轮 2 后把旧侧四态与被暂缓/离开的区分补进 CSC15 与确认离开清理后 **384/384 PASS**，审查轮 3 后把暂缓保留需求纳入对照帧并禁止在身份不可判定时断言离开（CSC16）后 **385/385 PASS**，审查轮 4 后为暂缓者合成暂缓认领（CSC17）后 **386/386 PASS**。结构门的红点由 §4 的扰动 P8–P11 取得，分类型与清理路径的红点由 P12/P14/P15 取得。
4. **审查修复轮引入的新门**：无（本轮审查修复若引入新门，将在 §10 逐轮具名）。

## 4. 扰动负控制（19 轮，逐次还原并复核回全绿）

| 扰动 | 回退的修复/门禁 | 实测 FAIL |
|---|---|---|
| P1 | 差异处置塌成「全部预期」（禁止差异不再失败闭合） | CSC03、CSC06、CSC07、CSC08、CSC09、CSC14（6） |
| P2 | Host 覆盖不再单独归类（并入 canonical 生命周期稳定性） | CSC02、CSC11（2） |
| P3a | 只关「当前 canonical 认领者」判定 | **无咬合**（386/386 全绿——两侧判定互为冗余，见 §8-2） |
| P3b | 只关「旧侧认领者」判定 | CSC03（1） |
| P3c | 两条判定同时关 | CSC03（1） |
| P4 | 取消时间轴对照（抖动与错误释放不再判定） | CSC06、CSC07（2） |
| P5 | 取消越界 Region Key 判定 | CSC09（1） |
| P6 | 取消异域需求失败闭合 | CSC10（1，首形态见下注） |
| P7 | 旧侧差异借用非零连接代次 | CSC04、CSC11、CSC13（3） |
| P8 | 影子路径开始读旧碰撞账本（把旧账本当权威） | Collision Shadow StaticIL 与合成门、Collision Shadow No Native Write（2） |
| P9 | Collision 资格改为自行判断授权 | Collision Shadow StaticIL、Collision Presence Eligibility（2） |
| P10 | 协调器不再把 Host 样本提交给投影（加本地玩家过滤） | Collision Shadow StaticIL、Collision Shadow Single Consumer（2，见下注） |
| P16 | 身份不可判定时仍把旧侧认领者断言为离开（不按暂缓处理） | Collision Shadow StaticIL、Collision Shadow Single Consumer（2） |
| P17 | 取消暂缓认领的合成（暂缓者的保留区域会被误报成「凭空需求」） | Collision Shadow StaticIL、Collision Shadow Single Consumer（2） |
| P11 | 只读快照顺手走旧刷新写入路径 | Collision Shadow StaticIL、Collision Legacy Snapshot ReadOnly（2） |
| P12 | 把暂缓态当成离开（原因词折叠，准入故障被误报成已离开） | CSC15（1） |
| P13 | 无法归因的旧覆盖不再失败闭合 | CSC14（1） |
| P14 | 取消影子路径的确认离开清理（离开者需求永久残留） | Collision Shadow StaticIL、Collision Shadow Single Consumer（2） |
| P15 | 取消缺席移除资格门（无条件按缺席清理） | Collision Shadow StaticIL、Collision Shadow Single Consumer（2） |

- P1 同时打到 6 项禁止差异门，证明「禁止差异必须为零」不是装饰；P2 证明 Host 归类有独立语义；P10 是本票最关键的负控制——去掉 Host 提交即变红，证明「含 Host」确实由门禁把守；P12 证明「暂缓不等于离开」有独立咬合；P14/P15 是审查轮 2 两项实质缺陷（离开者需求残留、缺席移除资格门）的负控制。
- **P6 首形态被编译器拦下**：最初写成 `if (false)` 由 `warnaserror` 报 CS0162（不可达代码）而不可编译，遂改为「守卫条件在异域帧下不成立但可编译」的等价形态重测（与票 04 P11 同类现象，如实具名）。
- **P10 首跑无咬合，实为取证脚本缺陷**：首版扰动模式命中了 Resource 侧同形样本闸门（同一 `seen.Add` 形态在协调器里出现两次），改动落在旁路而非影子路径，故 384/384 全绿。收紧为「含影子路径独有后继行」的唯一模式后重跑，实测红（2）。该次重跑在日志中具名，未被当作已通过计入。
- **P3a 无咬合**如实具名（见 §8-2）：两条「仍被在场合格认领者需要」的判定路径（当前 canonical 事实 / 旧侧记录）互为冗余，任一单独存在即维持不变量。
- 原始输出：`.scratch/collision-migration-slice/evidence/ticket05-perturbation.log`（可重放脚本 `ticket05-perturbation.py`，支持按轮重跑；脚本自带模式校验，避免再次出现同形旁路命中）。

## 5. 验证矩阵（静态门禁，全部实测）

| 门禁 | 结果 |
|---|---|
| 主 DLL Release Rebuild ×2 | 0 个错误 / 0 个警告；两次指纹逐项一致 |
| 测试 exe Release Rebuild ×2 | 0 个错误 / 0 个警告；两次指纹逐项一致 |
| 全套测试（唯一入口） | **386/386 PASS** |
| `Tools/Verify-BuildFingerprintArtifact.ps1` | PASS（`INDEPENDENT_ARTIFACT_VERIFICATION_PASS`，含候选角色一致性） |
| `Tools/Verify-EvidenceClassLayout.ps1` | PASS（`EVIDENCE_CLASS_LAYOUT_PASS`） |
| `Tools/Verify-Ticket09Documentation.ps1` | PASS（`TICKET09_DOCUMENTATION_METADATA_PASS`） |
| `git diff --check` | CLEAN |

### 新证据清单（26 项）

| 证据 | 锁定内容 |
|---|---|
| CSC01 | 两侧区域集合相同 → 零差异 + InBoth 计数；上一拍与当前相同不触发抖动判定 |
| CSC02 | Host 只出现在新侧 → 预期差异（HostAddedCoverage），归因到 Host 的观察者与连接代次，Guest 区域不受影响 |
| CSC03 | 授权 Guest 缺区 → 禁止差异（AuthorizedGuestMissingRegion），逐区列出并归因 |
| CSC04 | 旧侧独有且只被在场但不合格的观察者（Pending Guest）认领 → 预期差异（`ResourceCoupledCoverageExit`，原因词 `pending-guest`），逐区具名并单独计数 |
| CSC05 | 新侧独有且由在场合格 Guest 贡献 → 预期差异（canonical 生命周期更稳定），归因到该 Guest |
| CSC06 | 事实两拍未变而区域消失 → 禁止差异（StaticDemandChurn） |
| CSC07 | 一个 Guest 离开时把另一个事实未变、仍在场的 Guest 需要的区域一起放掉 → 禁止差异（CrossObserverRelease）；只由离开者独有的区域退出不算差异 |
| CSC08 | 新侧区域无任何认领者覆盖 → 禁止差异（UnattributedDemand，零归因） |
| CSC09 | Region Key 越出世界边界（X 或 Y ≥ 世界尺寸）→ 禁止差异（OutOfBoundsRegionKey） |
| CSC10 | 帧内区域集合来自别的领域 → 整帧失败闭合为禁止差异（ForeignDomain），不静默按相等处理 |
| CSC11 | 差异的聚合键是 Domain Id + Region Key，归因带观察者与连接代次；旧侧差异的代次为 0，不借用新侧代次 |
| CSC13 | 旧侧独有且只被已不在 canonical 事实里的旧条目认领 → 预期差异（`ResourceCoupledCoverageExit`，原因词 `departed-observer`） |
| CSC14 | 旧侧覆盖无法归因于旧 Writer 自己的任何认领者 → 禁止差异（UnexplainedLegacyCoverage，原因词 `no-legacy-claimant`），失败闭合 |
| CSC15 | 本拍样本不可用（暂缓）不等于离开：只被暂缓态认领者覆盖的旧侧区域 → 预期差异（原因词 `deferred-sample`），不是 `departed-observer`、也不是禁止差异 |
| CSC16 | 暂缓者的保留贡献落在 InBoth：协调器把它的投影区域算进新侧后两侧都有、零差异——保留的 Deferred Demand 不得被伪造成「新侧缺区」 |
| CSC17 | 暂缓观察者以「最后已知事实」合成暂缓认领后，其只在新侧的区域归类为保留的 Deferred Demand（预期、原因词 `deferred-contribution`、归因到该观察者），而不是「凭空需求」禁止差异 |
| CSC12 | Collision 政策从 canonical 事实投影：合格观察者（Host 亦在其列）贡献需求、Pending Guest 不贡献也不被当成离开、半径来源声明为原版物件区域常量 |
| Collision Policy Declaration | `CollisionDemandPolicy.Create` 唯一构造 `DemandPolicy`、显式取 `DomainIds.Collision`、含 `LevelObjects.OBJECT_REGIONS` 字面量、自身不读该原生常量 |
| Collision Presence Eligibility | 政策声明面读观察者事实的玩法资格，零 `Security` 命名空间引用（不在 Collision 里另判授权） |
| Collision Shadow PureMemory | 政策/帧/报告/比较器四类型零 Unturned/Unity 引用、零 `Provider.clients` 扫描、零第二份 `SpatialObserverIndex` |
| Collision Legacy Snapshot ReadOnly | 快照读旧覆盖与旧侧中心各一次、零原生调用、零旧补丁自身方法调用（不得顺手走刷新写入路径） |
| Collision Shadow No Native Write | 影子方法体零原生/Unity 写入调用、零旧 Writer 入口调用、零 Resource 生产接缝调用 |
| Collision Legacy Writer Sole Writer | 旧补丁与碰撞账本适配器都不消费新投影/比较器/政策（新投影无法借它们写入） |
| Collision Shadow Single Consumer | 比较器全程序集只有唯一调用点（协调器只读影子路径）；该路径提交观测一次、取投影区域一次、抄快照一次、构造帧一次、零名册扫描、`IsLocalPlayer` 只被读一次（用作 Host 归因，不是过滤）；并消费准入计划的缺席移除资格与暂缓集合、对确认缺席者走一次 `RemoveObserver`（审查轮 2 发现 1 的修复） |
| BuildArtifact Candidate Role | 候选角色进程序集元数据与默认 Case-ID，影子角色与切换角色的身份字符串不同、`IsComplete` 仍成立 |
| BuildArtifact Candidate Role | 影子候选与切换候选身份实测可区分（§6） |
| BuildArtifact Slice Identity（已改） | 0.2.4.9 / Experimental / `SPF-0.2.4.9-Experimental-CollisionSlice-ReadOnlyShadow` 字面量冻结 |

## 6. 产物身份（可复现构建，两次 Rebuild 指纹一致）

| 产物 | SHA-256 | MVID |
|---|---|---|
| `bin/Release/SteamP2PFriends.dll`（影子候选） | `8EE7B82B4288E2CA25395C7F55DB99910DCDE94D31A40BE371974D76BECEAF48` | `cc488eb4-94c6-4bb0-af4a-65fdecc0ef9b` |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `DD7C2D963DD93629AB0C6680BC16D7A39754809C7E75A57F4A67341CF49A9271` | `49291ff2-a1e5-42d4-a04b-1321e5401e9a` |

两次 Rebuild 的逐次身份（Run 1 / Run 2 的 SHA-256、MVID、大小与逐项机械比较）见 `.scratch/collision-migration-slice/evidence/ticket05-double-rebuild-identity.log`（本地证据：两次取值逐项 `IDENTICAL`）。

**候选角色可区分（票面第 7 项）**：同一份源码，`-p:SteamP2PFriendsCandidateRole=Cutover` 构建得到的产物身份为 SHA-256 `452DA8DF5BCBE4C793E69035C2E2C4E7DE20FDDFEDBE9F171406642D14C12ABD` / MVID `cc75c0ab-22a5-409d-9f27-5b94541570aa`，与影子候选逐项 `DIFFER`（大小相同，因差异只在元数据字符串）；可重放脚本 `ticket05-candidate-role-replay.sh`、原始输出 `ticket05-candidate-role.log`。角色同时驱动默认 Case-ID（`…-CollisionSlice-ReadOnlyShadow`），因此**两类候选的运行日志天然可分**；独立核验脚本对角色做同源校验（`CandidateRole = ReadOnlyShadow`，PASS）。

版本 `0.2.4.9`，插件 GUID `com.yu80rice.steamp2pfriends`，**未创建 git tag、未发 GitHub Release**。注：本仓 `Deterministic=true` 且构建包含源文本，因此任何生产源码改动（含注释）都会改变身份；§6 的取值在最后一轮源码改动之后重新采集，并在代码冻结后回填。

## 7. 票面逐项映射

| 票面要求 | 落地证据 |
|---|---|
| Collision Demand Policy 使用原版物件区域半径的切比雪夫投影作为行为保持基线，不升为共享默认半径 | `CollisionDemandPolicy.RadiusSource = "LevelObjects.OBJECT_REGIONS"`、形状 `ChebyshevSquare2D`、半径与世界尺寸由接线点从各自原生常量读入；Collision Policy Declaration 契约 + CSC12；`Demand` 命名空间「无共享默认半径」契约保持绿 |
| 消费 canonical observer facts，包含 Host；Guest 资格沿用 World Presence Observer | 影子路径提交每个有效样本（**不按 `IsLocalPlayer` 过滤**——Collision Shadow Single Consumer 断言 `IsLocalPlayer` 只被读一次且用于 Host 归因）；资格由政策读 `ObserverPresence.GameplayAuthorized`（Collision Presence Eligibility 断言零授权类型引用）；CSC02 / CSC12；扰动 P10 证明该门有咬合 |
| 产出 typed Collision Demand；不扫描客户端名册 | 投影经共享引擎产出 `DomainDemand`（Domain=Collision）；影子四类型零 `Provider.clients` 引用（Collision Shadow PureMemory）+ CSC12 的 typed demand 断言 |
| 不写 LevelObject、门、Collider 或可采集树 | Collision Shadow No Native Write（影子方法体零原生写入调用）；旧覆盖快照只读（Collision Legacy Snapshot ReadOnly）；扰动 P8/P11 取证 |
| 按观察者贡献和聚合 Domain Id + Region Key 对新旧投影做差异分类 | `CollisionShadowDifference` 携带 Domain Id + Region Key + 观察者/连接代次 + 原因词；CSC11；11 类分类枚举（4 预期 + 7 禁止）覆盖两侧独有与时间轴差异 |
| Host 新增覆盖、资源写入退出 Collision 等标为预期差异；授权 Guest 缺区、需求抖动、错误释放等标为禁止差异 | 预期四类：CSC02（Host 新增覆盖）、CSC04/CSC13/CSC15（`ResourceCoupledCoverageExit`，原因词区分 `pending-guest` / `departed-observer` / `deferred-sample`）、CSC05（canonical 生命周期）、暂缓贡献保留（枚举第 10 类）；禁止七类：CSC03（授权 Guest 缺区）、CSC06（静止抖动）、CSC07（跨观察者错误释放）、CSC08（凭空需求）、CSC09（越界键）、CSC10（异域需求）、CSC14（无法归因的旧覆盖）。决策票的「ResourceSpawnpoint 退出 Collision Writer」对应本枚举的 `ResourceCoupledCoverageExit`，见 §8-11 |
| 影子候选与正式候选构建指纹可区分 | §6：候选角色进程序集元数据 + 默认 Case-ID，双角色实测身份逐项 `DIFFER`；BuildArtifact Candidate Role 门 + 独立核验脚本同源校验 |
| PureMemory、StaticIL、独立影子 Build Artifact 通过 | §5：386/386 PASS、结构契约全绿、`Verify-BuildFingerprintArtifact.ps1` PASS（影子候选身份独立可核验） |
| 只读 1 Host + 2 Guest 影子 Runtime 用于发现和对照；不得替代正式切换 Runtime PASS | **待人工执行**（见 §11 口径）：本票交付影子候选、运行时可观测字段与对照判读口径；声明原文要求该 Runtime 不得替代正式切换 Runtime PASS，票 09 仍按正式切换候选验收 |
| 若影子暴露新的准入阻塞，回写 04，不在本票强行关单 | 影子尚未在真实三端运行；本票静态面未暴露新的控制面准入阻塞（票 04 的 ARI/SAM/SIG 门保持绿）。影子运行若暴露阻塞，按票面回写 04（§11 给出判读口径） |

## 8. Seam gaps 与取舍（全部具名，无静默跳过）

1. **协调器接线无纯内存宿主（沿用票 03/04 已具名的同类缺口）**：`MultiObserverShadowCoordinator` 依赖 Unity/Unturned 类型，PureMemory 无法构造其接线。因此「样本 → `ObserverPresence` → 提交投影 → 抄旧覆盖 → 构造帧 → 分类 → 记录」这条接线链**没有行为门直接覆盖**，由 7 项结构契约（提交次数、快照次数、帧构造次数、零名册扫描、`IsLocalPlayer` 只读一次、零原生写入、零 Resource 接缝调用）+ 代码级注释承担；行为面由 CSC01–CSC16 在纯比较器接缝上覆盖。扰动 P8/P9/P10/P11/P14/P15/P16 证明这些结构契约确有咬合。
2. **P3a 无咬合（纵深冗余，非缺口）**：「旧侧独有区域是否仍被合格认领者需要」有两条互相冗余的判定路径（当前 canonical 事实、旧侧记录），任一单独存在即维持不变量；只关当前侧一条时全绿（P3a），只关旧侧一条或两条同关时 CSC03 红（P3b/P3c）。冗余是刻意的：旧侧可能漏跟踪某个在场观察者，当前侧可能缺少旧侧的历史资格信息。
3. **`ConfigureResourceProduction` 改名 `ConfigureControlPlane`**：该接线点现在同时注册 Resource 与 Collision 两条政策，旧名已失实。改名波及 4 处测试引用（`DemandProjectionStaticILContractTests`、`LifecycleOrchestrationStaticILContractTests`、`ResourceProductionControlStaticILContractTests` 两处、`ResourceProductionControlSeamTests` 注释一处），断言与含义未变。
4. **世界尺寸只读一次**：接线点把 `Regions.WORLD_SIZE` 读入局部变量后传给两条政策，使票 01 的「半径来源」契约（`WORLD_SIZE` 恰好一次、`RESOURCE_REGIONS` 一次、`ITEM_REGIONS` 零次）在第二域接入后仍成立；Collision 的半径常量断言由本票新增的「Collision Policy Declaration」契约承担（该契约同时断言政策自身**不读** `LevelObjects.OBJECT_REGIONS`，半径只能由接线点读入）。
5. **`Regions.checkSafe` 与影子越界判定是两套口径**：旧 Writer 用 `Regions.checkSafe`（原生），影子用「Region Key >= 世界尺寸」自判（纯内存）。两者当前语义一致（世界尺寸内），但若原生实现改变，影子会先报越界差异——这是有意的：影子不得反向依赖原生谓词。旧侧越界键（旧 Writer 从不产生）也会被标记，用于暴露编码错误。
6. **影子对照的跨拍基线保存在协调器静态字段**：`_collisionShadowPreviousRegions` / `_collisionShadowPreviousClaims` 属 ADR 0012 允许的「仅用于验证的编排状态」。票 08 退役生产影子比较时，这两个字段与 `RunCollisionShadow`/`ReportCollisionShadow`/`StartCollisionShadowForbiddenHeartbeat`/`ResetCollisionShadow`/`CollisionShadowDemandRegionCount` 一并删除；`CollisionShadowStaticILContractTests` 的单消费点契约会把「影子仍被调用」暴露为红。
7. **候选角色目前只是构建身份，尚无运行时行为分支**：票面只要求「影子候选与正式候选构建指纹可区分」，因此 `CandidateRole` 进元数据与 Case-ID、并被独立核验脚本校验；「正式候选在启动/新会话确定唯一 Authority Writer」属票 08（其构建默认角色切换与运行时判定应在票 07/08 落地）。运行时不得据此把影子候选当切换候选使用：该约束由「两类候选日志不得混用」的验收口径与票 07 的 Go/No-Go 承担，本票不做运行时阻断。
8. **`CollisionShadowFrame`/`Report` 的数组字段**：帧以数组承载区域与认领者（而非集合），使纯分类器不持有可变状态、调用方每拍构造一次；帧不被缓存、不跨拍复用（上一拍只复用区域/认领者数组）。
9. **测试辅助 `Square()` 自己实现切比雪夫展开**：不调用 `SpatialObserverIndex.CalculateGrid2D`，避免用被测实现验证被测实现；世界尺寸固定 64，与政策参数一致。
10. **范围边界（本轮更新）**：随本票交付的证据件为 `.scratch/collision-migration-slice/evidence/` 下的 `ticket05-red-csc.log`、`ticket05-green-full.log`、`ticket05-perturbation.py`/`.log`、`ticket05-candidate-role-replay.sh`/`.log`、`ticket05-double-rebuild-replay.sh`/`ticket05-double-rebuild-identity.log`。工作树内以下未跟踪目录**不属于本票**、也不随本票提交：`.scratch/guest-join-disconnect-2026-09-17/`、`.scratch/create-room-failed-2026-09-18/`；`docs/agents/output-review-loop.md` 的工作区改动由用户本人作出，本票不改不提交。

11. **决策票差异类的落点映射（审查轮 1 Spec 发现 2 的处置；审查轮 3 收紧措辞）**：决策票列出的预期差异「`ResourceSpawnpoint` 退出 Collision Writer」对应枚举里的 `ResourceCoupledCoverageExit`——它就是「旧侧独有且无在场合格认领者」的那类覆盖，随旧 Writer 一起退出。**该类的判定条件只证明「这类覆盖不再由 Collision 覆盖」，不证明每个此类区域都实际发生过树/矿写入**（旧 Writer 只对变更区域刷新，且 `_refreshActiveState`/物件列表不可用时会在树分支前返回）；「可采集树/矿写入退出 Collision 操作集合」由结构事实承担：影子路径零树写入（静态契约 + 扰动 P11）、旧覆盖快照只读、资源侧写入留在旧补丁自己的路径上。区域集合口径下需要独立差异类的是观察者状态（原因词 `pending-guest` / `deferred-sample` / `departed-observer`）。决策票列出的三项禁止差异**不是区域集合差异类**，各自落点如下：「新路径依赖私有扫描」由静态契约承担（影子四类型零 `Provider.clients` 引用、零第二份 `SpatialObserverIndex`，扰动 P8 证明有咬合）；「单区域异常清空其它需求」由共享引擎既有门承担（票 04 的 ARI09 与票 03 的 LOE08/LOE09）；「旧 generation 残留」是**原生状态**议题，本票的 Collision 投影尚无 generation 轴，该风险由票 06 的 Acquisition Receipt/代次提交边界与既有 LOE05/LOE06 承担——本票不宣称覆盖它，并把该移交写进 §11 与 `issue.md`。
12. **「最高接缝」措辞澄清（审查轮 1 Spec 发现 1 的处置）**：`CollisionShadowComparatorTests` 的类注释原写「最高接缝 = Collision Shadow Comparator」，容易被读成建立了第二套**行为**接缝。已改为「本票新增的只读对照接缝，不是行为接缝」：唯一对外行为接缝仍是 Lifecycle Orchestration Engine，本票零 Collision 执行类型、零 Lifecycle Policy、零编排状态机，`Lifecycle Single Domain Registration` 契约保持绿。比较器是纯函数，只被协调器的只读路径调用一次（`Collision Shadow Single Consumer` 契约）。
13. **审查轮 1 Standards 判断项处置**：常量与字段命名撞车已修（`CollisionShadowForbiddenHeartbeat` → `CollisionShadowForbiddenHeartbeatPolicy`）；`ResetCollisionShadow` 的括号风格已与文件其余方法对齐；「策略文件与 `ResourceDemandPolicy` 结构近似」按仓库既有先例保留（第二域沿用第一域形态，第三域出现时再评估抽公共工厂）；「协调器体量继续增长」与「Feature Envy」记入 §9 延期。
14. **审查轮 1 Spec 发现 4（范围蔓延）的核对结论**：`docs/agents/output-review-loop.md` 的工作区改动与两个 `.scratch` 未跟踪目录在本票开工**之前**即已存在（会话开始时的 `git status` 快照可证），不是本票产物；本票不改动它们，且提交按显式路径清单进行（不含这三项）。审查者看到的是整个工作树而非本票增量，故此处如实具名而不是当作已消除。
17. **暂缓者保留的需求必须进对照帧（审查轮 3 Spec 发现 1 的修复，实质缺陷）**：首版新侧区域集合只由「本拍有样本的观察者」并集而成，暂缓观察者虽未被 `RemoveObserver`、其保留需求却不在帧里，于是被伪造成「新侧缺区」。已改为按**被跟踪观察者**（`CollisionShadowObservers`，含本拍暂缓者）逐一只读投影状态，使帧反映真实投影；CSC16 锁定「保留的 Deferred Demand 落在 InBoth」。
18. **身份不可判定时不得断言离开（审查轮 3 Spec 发现 1 的后半）**：本拍存在身份不可读记录时（`AllowAbsenceRemoval=false`）谁在场无法判定，此时旧侧认领者一律按暂缓处理而不是「已离开」；`Collision Shadow Single Consumer` 契约锁定缺席移除资格被读**两次**（把住移除动作 + 决定能否断言离开），扰动 P16 证明有咬合。
19. **「资源写入退出」措辞收紧（审查轮 3 Spec 发现 2 的处置）**：`ResourceCoupledCoverageExit` 的判定条件是「旧侧独有且无在场合格认领者」，因此**只宣称这类覆盖随旧 Writer 一起退出**，不宣称每个此类区域都实际发生过树/矿写入（旧 Writer 只对变更区域刷新，且 `_refreshActiveState`/物件列表不可用时会在树分支前返回）；枚举注释与 §8-11 已按此收紧。
20. **暂缓者必须有认领（审查轮 4 Spec 发现 1 的修复，实质缺陷）**：第 3 轮把暂缓者的保留区域纳入对照帧后，`claims` 仍只由本拍样本构造，于是暂缓者「只在新侧」的保留区域找不到认领者，会被判成 `UnattributedDemand`（禁止差异）——准入故障被误报成「投影凭空造需求」。已在协调器侧为暂缓观察者用**控制面最后已知事实**合成一条 `IsDeferred` 认领；分类器优先按暂缓来源给出 `DeferredContributionRetained`（原因词 `deferred-contribution`）而不是「本拍在场」的 canonical 稳定性，旧侧四态改由「暂缓集合优先」判定（暂缓者永不被读成合格认领者）。CSC17 + `Collision Shadow Single Consumer`（`Authority.TryGet` 一次、`CollisionShadowClaim` 构造两次）+ 扰动 P17 取证。
21. **CSC16/CSC17 不覆盖协调器接线（沿用 §8-1 的同类缺口）**：两门都是纯比较器行为门，只证明分类语义正确；「被跟踪观察者 → 投影区域 → 帧」与「暂缓者 → 合成认领」两条协调器链路没有纯内存宿主，由结构契约（读投影、读最后已知事实、构造两类认领的次数）+ 代码注释承担，扰动 P17 证明该结构契约有咬合。审查轮 4 的 Spec 指出 CSC16 不执行该链——此处如实具名，不当作已覆盖。

## 9. 判断项延期（全部具名；双轴确认不阻断）

1. **`CollisionShadowLogLimit` 与 `SessionEventLogLimit` 双配额**：逐条差异走会话事件配额（12 条/会话），禁止差异另有有界心跳（5s × 6 次 + 恢复闭环）。两者叠加后长会话中逐条差异会先静默、心跳仍持续——符合票 04 的「持续异常必须有界心跳」不变量，但两套配额的交互未做专门门禁；延期。
2. **影子每拍重建 `CollisionShadowClaim[]`/`RegionKey[]`**：1Hz、观察者数 ≤64，分配量可忽略；未做池化。
3. **`ClassifyTemporal` 的认领身份用线性查找**（`Contains`）而非哈希集：区域基数小且有界（≤世界尺寸²），可读性优先；若未来接第三域需重估。
4. **诊断行仍为裸 `reason=`/`kind=` 风格字符串**：沿用仓库既有形态。
5. **票 03/04 已具名的存量判断项**（`Report` 可选参数簇、`AdvanceTime`/`Flush` 守卫重复、`Tick` 变更原因累积、薄门面 Middle Man、测试三级反射、诊断键字符串拼接）本票未新增未恶化；`Tick` 因新增一行影子调用而略增，属票 04 已具名的同一根因（本票不重构 `Tick`）。
6. **`BuildMetadata.CandidateRole` 的生产消费者只有自报告指纹与汇总/启动日志**：角色真正驱动行为要等票 08；在此之前它是「身份字段」，与 `ReleaseChannel` 同类。
8. **`DispositionOf` 是「分类 → 处置」的显式 switch 映射**：新增差异类别时要同步枚举与映射两处（枚举注释已提示）；当前 12 类一一对应，未做属性化。
9. **Pending Guest 覆盖退出定为「预期」是本票的判断**：规格明确 Pending Guest 是否投影世界需求**不由本规格改写**，故影子只把它逐区具名并单独计数，供票 07 的 Go/No-Go 判读；该计数在影子运行后若持续非零，应由票 07 决定是否需要控制面改动（不是本票静默放行）。
10. **协调器影子分支的体量**（审查轮 1 Standards）：影子编排（提交/记录/心跳/重置/计数）约 100 行留在协调器内，属 ADR 0012 允许的「仅用于验证的编排状态」；票 08 退役影子时应整体删除，若里程碑提前需要下沉到独立类型，应在票 06/07 评估。
11. **`ReasonOf` 的 `default` 未显式穷尽四态**（审查轮 3 Standards）：当前 `default` 归为 `departed-observer`，将来新增第五态会被静默当成「离开」；同文件的 `DispositionOf` 已用「default 归为 Forbidden」的失败闭合策略，两者不对称。加态时应改为显式列全 + `default: throw`。
12. **原因词为裸字符串**（审查轮 3/5 Standards）：`Reason` 与 `ReasonOf` 返回值没有常量或枚举集中管理，测试与生产之间可能出现拼写漂移；仓库同类先例（`RadiusSource`）也是 `const string`，故不升级。
13. **`IsDeferred` 是可选布尔构造参数**（审查轮 5 Standards）：默认 `false` 语义正确且两个生产调用点都具名传参，但将来漏传具名参数编译器不会报警；新增调用点必须具名，或改用 `ForLive`/`ForDeferred` 两个静态工厂消除默认值歧义。
14. **`ClassifyNewOnly` 的 `deferredCovers` 与 `anyCovers` 两条分支殊途同归**（审查轮 5 Standards）：都产出 `DeferredContributionRetained`/`deferred-contribution`，语义不同但落点相同，读者易误读为笔误；应加注释或合并为单分支。
15. **细粒度 IL 计数契约需随实现演进同步维护**（审查轮 5 Standards）：`Collision Shadow Single Consumer` 现断言 5 个计数点（`Observe`/`GetActiveRegions`/`RemoveObserver`/`Authority.TryGet`/认领构造×2）。每条都对应一条「不得漏做/不得多做」的实质不变量，但无害重构（例如把 `AllowAbsenceRemoval` 缓存进局部变量、把两处认领构造抽成工厂）会误红，需在改动时同步更新契约。
16. **`RunCollisionShadow` 与 `ReconcileResourceProduction` 的风格不一致**（审查轮 4 Standards）：前者两次直接访问 `plan.AllowAbsenceRemoval`，后者缓存进局部变量；语义相同、风格不同。统一任一风格时须同步 §9-15 的计数契约。
7. **`Tools/Verify-Ticket09Documentation.ps1` 由本票修改**：它按 `Build/Version.props` 解析期望 Case-ID，本票给默认 Case-ID 追加了角色占位符，故必须同步解析，否则文档门禁会因「占位符未替换」而失败。改动只增加一行属性解析与一处 `Replace`，判定与输出标记未变。

## 10. 双轴审查实际轮次记录

每轮为两个全新实例、并行派发，`subagent_type` 分别为 `standards-reviewer` 与 `Spec-Reviewer`；不续接、不复用上一轮实例。

| 轮 | 本轮增量 | Standards | Spec |
|---|---|---|---|
| 1 | 全量首轮（4 个新增生产类型 + 旧覆盖只读快照 + 接线点改名与影子接线 + 候选角色构建身份 + CSC01–CSC12 + 8 项结构契约 + 文档） | **CLEAN**（0 硬违规；判断项 5：策略文件与 Resource 近似、协调器体量/Feature Envy、常量与字段命名撞车、`ResetCollisionShadow` 括号风格、`CaptureShadowSnapshot` 为 internal） | **BLOCKING 5**：①「最高接缝」措辞被读成第二套行为接缝；②旧侧差异分类无法证明其名（`ResourceWriteExitedCollision` 把 Pending Guest 与残留混为一类），且缺「旧 generation 残留/私有扫描/单区域异常」的落点；③票面第 9 项未完成（本票已如实标注待人工执行）；④范围外材料在工作树中（核对该材料先于本票存在）；⑤§1 提前宣称双轴收尾而 §10 为空 |
| 2 | 发现 1/2/5 的修复：拆分旧侧差异（`PendingGuestCoverageExit` / `DepartedObserverCoverageExit` / `UnexplainedLegacyCoverage`）+ 旧侧「在场/合格」两态 + CSC13/CSC14 + 扰动 P12/P13；类注释与审计措辞澄清；新增差异类落点映射；§1 去提前结论 | **CLEAN**（0 硬违规；判断项 2：两个 Classify 方法的 ids/tokens 累积模式重复、`LegacyClaim` 辅助的布尔实参顺序易误传，均建议延期） | **BLOCKING 4**：①离开者未从 Collision 投影清理（离开后需求永久残留 → 每拍被报成凭空需求）；②暂缓样本被两态误判成「已离开」（准入门故障被放行为预期差异）；③「Resource 写入退出 Collision」仍无对应可观测分类；④审计/manifest/issue 的数字与类别计数互相矛盾 |
| 3 | 发现 ①②③④ 的修复：影子路径改为「确认离开才清理」（消费准入计划、`RemoveObserver` 恰好一次）+ 旧侧认领者改四态（`ECollisionShadowLegacyState`，暂缓≠离开）+ 差异新增原因词 + `ResourceCoupledCoverageExit` 承载决策票的「资源写入退出」类 + CSC15 + 扰动 P14/P15；全部文档数字与类别计数统一为 384/384、4 预期 + 7 禁止、17 轮 | **CLEAN**（0 硬违规；判断项 3：`ReasonOf` 的 `default` 未显式穷尽四态、两处 ids/tokens 累积模式重复、原因词为裸字符串，均建议延期；并逐条核对了 §4/§8 关于 P10 首跑的自述与脚本/日志一致） | **BLOCKING 4**：①暂缓者保留的需求未进入对照帧（被伪造成「新侧缺区」），且身份不可判定时旧认领者仍被断言为离开；②`ResourceCoupledCoverageExit` 的判定条件不能证明每个此类区域发生过资源侧写入（措辞过强）；③manifest 仍写「新增 CSC01–CSC12」；④票面第 9 项仍缺 Runtime 证据（本票已如实标注） |
| 4 | 发现 ①②③ 的修复：新侧区域集合改按被跟踪观察者逐一只读投影（含暂缓者保留需求）+ 身份不可判定时一律按暂缓处理 + CSC16 + 扰动 P16；`ResourceCoupledCoverageExit` 措辞收紧；manifest/审计计数修正；全部文档统一为 385/385、18 轮 | **CLEAN**（0 硬违规；判断项 2：`RunCollisionShadow` 与 `ReconcileResourceProduction` 对 `AllowAbsenceRemoval` 的读取风格不一致、原因词为裸字符串，均建议延期；并复核了三段循环的顺序依赖与旧名残留） | **BLOCKING 2**：①暂缓观察者缺认领——其只在新侧的保留区域会被判成 `UnattributedDemand`（禁止差异）的假红；②manifest 与审计仍有旧计数（CSC01–CSC12 / 24 项 / 384） |
| 5 | 发现 ①② 的修复：为暂缓观察者合成 `IsDeferred` 认领（取控制面最后已知事实）+ 分类器与旧侧四态优先级调整 + CSC17 + 契约收紧（`Authority.TryGet` 一次、认领构造两次）+ 扰动 P17；全部文档统一为 386/386、26 项、19 轮 | **CLEAN**（0 硬违规；判断项 3：`IsDeferred` 为可选布尔默认值（建议新增调用点具名传参或拆工厂）、`deferredCovers` 与 `anyCovers` 两条分支殊途同归成同一分类（建议注释或合并）、细粒度 IL 计数需随实现演进同步维护；并逐条复核了「用控制面事实合成认领是否越界」（不越界：读的是同一个唯一事实源）、顺序依赖与作用域） | **CLEAN**（两项 BLOCKING 均已闭合：暂缓假红消除、数字一致；逐条核对了四种情形在新旧两侧的落点组合，未发现假红或把禁止差异吞成预期） |

**最终结论（授予产物身份）**：第 5 轮双轴（两个全新实例、并行派发）在**同一份工作树**上分别给出 **Standards CLEAN（0 硬违规；3 项判断项）** 与 **Spec CLEAN（无差距无偏离）**，审查对象 = §6 所列身份对应的冻结增量（这些源码改动之后未再修改生产代码）。据此：§6 的 SHA-256 / MVID 与候选角色区分结果即本票的**产物身份**，适用于 §5 全部门禁（唯一入口 386/386 PASS、`Verify-BuildFingerprintArtifact.ps1` PASS、`Verify-EvidenceClassLayout.ps1` PASS、`Verify-Ticket09Documentation.ps1` PASS、`git diff --check` CLEAN）与 `ticket05-double-rebuild-identity.log` / `ticket05-candidate-role.log` 的可重放记录；本票状态为静态闭环、`implemented-pending-runtime`，**第 9 项（只读 1H2G 影子 Runtime）待人工执行，本票不宣称通过、在影子日志回传前不视为关单**。

**轮次口径（如实记录）**：第 1 轮两轴均为全新实例、并行派发。Spec 第 1 轮的发现 3（票面第 9 项未完成）与发现 4（范围外材料）**不构成代码修复**：前者已如实标注为待人工执行、本票不宣称完成，后者已核对为本票开工前既存材料并按显式路径提交清单排除。发现 5（提前宣称）与发现 1（措辞）、发现 2（分类）已在本轮修复并交由第 2 轮零上下文复审。

## 11. Runtime 判读口径（影子运行移交人工执行；正式切换验收归票 09）

- 本票交付的是**只读影子候选**（`candidateRole=ReadOnlyShadow`，默认 Case-ID `SPF-0.2.4.9-Experimental-CollisionSlice-ReadOnlyShadow`）。三端（1 Host + 2 Guest）运行该候选时可核对：
  - `[MultiObserver/M0]` 汇总行新增 `collisionDemandRegions`（共享投影按 Collision 政策算出的需求区域数）、`collisionRadiusSource=LevelObjects.OBJECT_REGIONS`、`collisionShadowInBoth`/`collisionShadowExpected`/`collisionShadowForbidden`、`collisionShadowReadOnly=true`；
  - 逐条差异事件 `collision-shadow kind=… region=… disposition=… attributed=…`（有界配额内）；
  - 禁止差异持续出现时按有界心跳告警，清零时写 `collision-shadow-forbidden-cleared`。
- **可接受的影子观察**：`collisionShadowForbidden=0` 且 `collisionShadowExpected` 的差异能逐条解释为 Host 新增覆盖 / 资源写入退出 Collision / canonical 生命周期补齐。
- **必须回写 04 的现象**（票面最后一项）：授权 Guest 的 LevelObject 区域缺失、静止时无原因抖动、一个 Guest 离开释放另一仍在场需求、凭空需求、越界 Region Key、异域需求——即 `collisionShadowForbidden > 0` 且分类为上述类别时，不得在本票强行关单，应回写票 04 并重新评估准入。
- **影子不得当作正式切换证据**：影子 DLL 的日志不构成切票 08/09 的 Runtime PASS；票 09 的三端验收必须使用**正式切换候选**（`candidateRole=Cutover`，票 08 冻结其角色值与默认值）且三端同一 SHA-256。
- 本票不新增 Patch、不改 Harmony 元数据、不改协议与配置键；**旧 Collision Writer 仍是当时唯一的生产写入者**（影子路径零原生写入，由结构契约与扰动 P8/P11 锁定）。


