# 实施审计 — SPF 0.2.4.10 R02

- **票据：** R02，Guest 进入或返回区域时自动交付当前 Item 基线；仅真实失败时提示。
- **基线：** 分支 `codex/structure-baseline-0.2.4`，HEAD `3fb2eb5e87ab3eddbc6782fcc708418a7d11d52a`；R01 是此前冻结交付，不作为 R02 审查证据。
- **R02提交记录：** `0979b62` 为审查通过的源码与测试实现提交；`1c93e7f` 记录最终审查状态；`1593155` 标记十项静态验收完成；后续记录修正提交为 `d34ceb9`、`2193557`、`e3fb2b7`。
- **状态：** R02 静态实现与审查已完成（`implemented-pending-runtime` / `ready-for-human`）；累计第 4 轮 Standards 和 Spec 均为 CLEAN；Guest Runtime 待验收。
- **票据：** `.scratch/item-runtime-repair-20261014/issues/02-guest-item-delivery.md` 保留原始全部 10 条验收标准；label 为 `ready-for-human`，status 为 `implemented-pending-runtime`。

## 发现与实现

当前生产 `DefaultNativeItemWarehouseBridge` 曾把已分配但零条目的 spawn list 当作 natural input 缺失，导致合法零点位区域无法完成返区结算。生产分类器现接受“输入已建立但为空”的状态，同时拒绝 spawn 输入缺失/越界及 `ItemRegion.items` 缺失；没有候选点时 `CreateOneNatural` 返回 `NoChange`。这确认了当前代码缺陷。历史 2026-10-13 诊断包未保存地图 spawn-list 状态及完整发送/提示链，因此本审计不把该缺陷认定为旧城镇症状的已证根因。

失败处理保留首个异常及有界的 region/job/observer/connection/attempt/budget/notice 信息；对发送结果未知或可能半包的情况隔离，对重复回调去重，并按 observer/connection/region 限定结果未知提示。世界结算提交且 job 删除后，确认未发送的基线会建立 observer/token/region 级 delivery-only 记录。`Consume` 在无结算和原生周期门之前处理该记录，复用同一 `FailedRetryController`，只调用当前 binder；不会重复清理/补货，也不缓存 ItemData。其他成功观察者仍保持已交付。结果未知时撤销自动重试并保留半包隔离；确认未发送且预算耗尽时只提示一次安全恢复，且仅可通过现有当前身份/覆盖资格/冷却授权重开。真实 projection 退出、断开、session 或 region generation 失效时精确撤销对应记录和重试键。引擎正常补偿退出不会重开 trace。未新增 ACK、reset、protocol、writer、warehouse 或重试策略。

## 验收证据映射

| AC | 静态证据与边界 |
|---|---|
| 1 | IT134 沿 `ItemProductionControlSeam` 获取/返区结算门到达 Guest 基线回调；IT137 覆盖结果未知失败、退出、返回、等待结算及新的基线回调。门禁完成后无需玩家移动或 Host 到场。 |
| 2 | IT133 通过共享生产 natural-input 分类器完成 Red→Green；IT134 覆盖结算行为。更早的 fake-only IT87 Red 仍保留，但明确不作为生产证据。 |
| 3 | IT133 将已建立但为空判为 ready，并将 spawn/region/items 缺失判为 unavailable；IT134 保留过期 natural 移除和无关掉落物。 |
| 4 | 既有 IT41/46/56/59/60 及完整 Whitelist 覆盖长时间结算等待、共享结果、输入缺失和 asset 行为。 |
| 5 | IT138 在世界提交后通过结算 `Finish` 驱动生产 `DispatchCandidateGuestAskItems`，证明确认未发送不属于半包；等待 0.5 秒后仅对目标 Guest 重试，共同需求的另一 Guest 只成功一次且不重发。IT139 检查 0.5/1/2 秒间隔、首次加最多 3 次及人工恢复；IT140 让正常结算前/待处理等待持续 40 秒且不启动失败时钟，再验证 10 秒期限。 |
| 6 | IT135 和 IT138–IT141 验证首错/堆栈、有界重试预算、按目标区分安全/未知提示，以及未知结果不重复重试；生产诊断保持限量。 |
| 7 | IT136 保留提示去重。IT142 证明同链重复事件及引擎补偿不会重开 trace、真实 projection 退出/重入会重开、成功交付会关闭该链；IT143 检查精确撤销待交付记录；IT144 复现并修复连接清理身份碰撞，同时保留目标 region/failure-kind 的重新登记能力和碰撞观察者的去重。 |
| 8 | IT137 证明可能半包隔离、真实区域退出、返区等待结算、新基线交付及最终解除阻塞。IT141 证明安全重试期间后续转为未知结果时只隔离一次并停止盲目重放。实际 native `ITEM_REGIONS` 字节仍需 Runtime 证据。 |
| 9 | 未修改 writer、Core identity、native protocol、pickup 或 20m 资格。IT138/139/143 验证单观察者 delivery retry 隔离；R01 证据仅作前序票据背景。 |
| 10 | 当前候选的 Release 构建和完整 614/614 Whitelist 通过记录见下文。R02 累计第3轮 Standards 的 P2 已在单独授权的 AC7-Collision 增量中修复；获准的第4轮双轴审查已完成且均为 CLEAN。源码 Green 不代表 Guest Runtime PASS。 |

## 验证

- 当前候选：生产 Release 构建 exit 0；Whitelist Release 构建 exit 0；runner exit 0，`614/614 PASS (Failed: 0)`；输出注明 Runtime 证据 PENDING：SP、listen-host、U3DS、P2P。
- 当前候选构建命令 `dotnet msbuild WhitelistTests/SteamP2PFriends.WhitelistTests.csproj /t:Build /p:Configuration=Release /v:minimal` — exit 0，4 项既有 test-only warnings，无错误。
- 历史 G1 候选运行命令 `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` — exit 0，`613/613 PASS (Failed: 0)`；该计数仅属于历史 G1 候选，输出中的 Runtime 证据仍为 PENDING：SP、listen-host、U3DS、P2P。当前候选计数为 614/614，见本节首项及 AC7-Collision 当前日志。
- G1 历史记录保存在 `.scratch/matt-orchestrate/item-runtime-repair-20261025-vela/messages/`；碰撞 Red/Green 和当前 Release 日志为 `R02-AC7-Collision-red3-run.log`、`R02-AC7-Collision-production-release.log`、`R02-AC7-Collision-whitelist-build.log`、`R02-AC7-Collision-whitelist-run.log`。
- 纯 host fixture 中无法构造 native `ItemManager.regions`（`ReflectionTypeLoadException`/`ECall`）；未将其计作业务 Red，也未通过安装/扫描 Unity 处理。IT133 覆盖 native bridge 共用的生产分类器；native transport 本身仍属于 Runtime 验收边界。

## 审查与交付门

第1轮使用两个全新独立 `gpt-6-luna/high` 上下文。Standards 为 NON-CLEAN：IT88–IT90 编号重复，已将用例改为 IT133–IT137。Spec 为 NON-CLEAN：AC8 缺少可能半包时退出/返区后交付基线的闭环，已增加 IT137 并取得 Red/Green。报告与 provenance：`.scratch/matt-orchestrate/item-runtime-repair-20261025-vela/messages/R02-round1-{standards,spec}-report.md`、`R02-reviewer-native-provenance.json`。

第2轮由两个全新独立 `gpt-6-luna/high` 上下文审查，结论仍为 NON-CLEAN。Standards 提出一项仅属判断的维护性 smell：`ItemReturnSettlement` 使用 region/observer/token 字符串键并重建匹配后缀来去重/释放提示（审查报告第1–4行）。该项明确延期为维护性后续工作；冒号分隔的数值身份在行为上仍充分，未发现已记录的硬规则违规。Spec 阻塞于 AC5：生产 `Finish` binder catch 忽略了 `SafeToRetry=true`；另指出 AC7 可能缺少首错 trace 重开路径。原始报告：`.scratch/matt-orchestrate/item-runtime-repair-20261025-vela/messages/R02-round2-{standards,spec}-report.md`；模型/effort native 证明：`R02-round2-reviewer-native-provenance.json`。

常驻专家提供 G1；其获准的唯一一轮全新双轴审查为累计第3轮。Standards 针对新的 AC7 连接清理身份碰撞给出 NON-CLEAN；Spec 为 CLEAN。根仅授权一个严格限量的 `R02-AC7-Collision` 增量及一轮全新第4轮复审。IT144 在修复前的生产断开路径取得 Red（6条 trace，预期5条）；仅将该故障集合替换为具名字段键并精确清理 observer/token 后取得 Green。此举不重开 G1 重试工作、不重置前三轮历史，也不把该缺陷延期为另一个结算提示键 smell。第4轮两轴均 CLEAN 后，票据状态更新为 `implemented-pending-runtime` / `ready-for-human`。

**交付：** `.scratch/item-runtime-repair-20261014/issues/02-guest-item-delivery-delivery.md`。第4轮双轴 CLEAN 后已记录限定提交及主调度 Vela 回执。未授权 push、部署、release 或 Runtime PASS。


## R02-AC7-Collision 增量证据与门禁

- 碰撞身份：断开 observer 5/token 7；保留 `region Y=5, observer=7, token=7`。修复前 IT144 沿真实 `OnObserverDisconnected -> ReleaseObserver -> ClearObserverFailuresForConnection` 路径复现首错 trace 6 条而非 5 条（runner exit 1）。同一测试确认目标连接在两个 region、两类 failure kind 的记录均清理，而碰撞 observer 仍保持去重。
- 最小修复：`_firstObserverFailureKeys` 现存储具名类型 `ObserverFailureKey`（region、observer、token、failure kind）；连接清理按 observer/token 字段精确筛选。未改变其他键族或 G1 策略。
- 修复后：Release 生产构建 exit 0；Whitelist Release 构建 exit 0；完整 runner exit 0，`614/614 PASS`。详细日志与此前 G1 的历史 613 项证据分开保留。
- 审查历史：第1、2轮 NON-CLEAN；第3轮 Standards NON-CLEAN / Spec CLEAN。第3轮 native 身份为 Standards `01a171fc-b5fd-7aa2-b80b-e6e19212fd3a` 与 Spec `01a171fc-da2b-7f22-9cf9-b0539f099734`，均为 `gpt-6-luna/high`；报告为 `messages/R02-round3-standards-report.md`、`R02-round3-spec-report.md`，provenance 为 `R02-round3-reviewer-native-provenance.json`。根仅授权对完整候选进行一轮新的独立双轴第4轮审查。
- 限定源码提交记录了本次审查通过的候选。未 push、部署或 release，也未报告 Runtime PASS；SP/listen-host/U3DS/P2P Runtime 证据仍待验收。


## 最终授权审查 — AC7-Collision / R02累计第4轮

- 冻结候选为从 `3fb2eb5e87ab3eddbc6782fcc708418a7d11d52a` 起的完整 R02 增量，排除受保护的用户改动及无关 untracked 文件。两个全新独立审查实例为 `/root/standards_r4` 与 `/root/spec_r4`；其实际 native rollout 均以构建者 native `01a171af-7956-7631-aac7-b1c24a6aa8fb` 为父会话。
- Standards：CLEAN，native `01a17207-c160-70e2-9779-1627e0b4bd4e`；Spec：CLEAN，native `01a17207-ed09-7c01-897f-fd60bdd55530`。两个 native `turn_context` 均确认模型/effort 为 `gpt-6-luna/high`。完整报告：`.scratch/matt-orchestrate/item-runtime-repair-20261025-vela/messages/R02-round4-standards-report.md`、`R02-round4-spec-report.md`；native 会话来源和 final-answer 行位置见 `R02-round4-reviewer-native-provenance.json`。
- Standards 确认第3轮唯一 P2 已由 observer/token 字段精确匹配修复；既有 `ItemReturnSettlement` 提示字符串键仍明确延期为维护性 smell，不构成新阻塞。Spec 确认 IT144 覆盖真实断开清理链，且 R02 AC1–10、G1 重试、结果未知、生命周期及 Runtime 边界均保留。
- 最终静态证据为 Release 生产构建 exit 0、Whitelist 构建 exit 0、runner exit 0（`614/614 PASS`，Failed 0）。原始日志位于 `.scratch/matt-orchestrate/item-runtime-repair-20261025-vela/messages/`。SP、listen-host、U3DS、P2P 的 Guest Runtime 仍为 PENDING；未进行玩家实机测试或部署。
- 累计审查历史保持不变：第1、2轮 NON-CLEAN；第3轮 Standards NON-CLEAN / Spec CLEAN；第4轮双轴 CLEAN。第4轮是 AC7-Collision 增量唯一获准的全新审查轮。此前主调度 Vela 交付回执为 `msg-876fb353-1e38-4217-ae7c-18344d6c6776`（`sent`）。
