# Implementation-0.2.4.10-R01

- **工单：** R01 — Host 刷新物品只有一份有效呈现
- **状态：** `implemented-pending-runtime`；triage `ready-for-human`
- **候选基线：** HEAD `01692ecc4aa0d2041c3f56a718cb7ab96ec1154b`，branch `codex/structure-baseline-0.2.4`；提交只含 ownership 清单内完整 R01 候选及本票记录，不含保护/无关改动。
- **范围：** 完整继承 R01（R1、R2A/B/C、R3、V1），不是仅接替后的 diff；14 个继承 tracked 与 14 个 untracked 源测候选。原用户文件 `CONTEXT.md`、`Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDomainModules.cs`、`Develop-Stage/README.md`、`README.md`、`docs/architecture/migration-manifest.md` 及无关 untracked 均未纳入。
- **恢复对照边界：** post-review 仅披露 1 行生产试改、2 个人工 null 方法及其 2 条注册；授权恢复逐项局部撤销并保留日志。`20261025` run 的 `baseline/tracked.diff` 是 R3 实施前接替快照，不冒充原 602 冻结状态；原冻结依据是 review/freeze 记录和当时完整候选，恢复判定按已披露的全部 post-review 增量与当前三处 readback，不以该旧快照误判 R3 差异。

## 源码结果

- R2C 在删除末端模型移除失败时已撤 pending、保留其他 ID，返回 Waiting 并记诊断；不回滚部分副作用、不重新发起原生删除。
- R3 在 owned step-five admission 和 common consumer 复用 committed 物理身份校验。缺新提交关联则等待并保留 pending；旧 callback/epoch/token 不借新 token 补绑。真实 Host 由当前 Provider.client 对应对象辨识，Guest 负向验证保留。
- V1 覆盖两种入场顺序、budget 实际部分/全部消费、重入/增量、已建模拾取与新 ID、两条 after-snapshot common-consumer 异常。现有 AC2 原始组合 Red 证据复用，不重复构造。
- 未引入第二仓库/Writer、额外身份表/恢复机或新协议；Core身份、共享结算门、原生增删/拾取事务、Resource/Collision、SP/Guest/U3DS 与 07 距离边界保持。

## 原票八项 AC 核对

1. **AC1 PASS：** 同一生产入口覆盖两种事件顺序，测试使用真实 dispatch/consumer，外部替身仅承担外部动作。
2. **AC2 PASS：** 保留原先实际组合 Red（真实 pending 2→4，exit 1）与修复唯一性（exit 0），不是 isolated getter/fake count。
3. **AC3 PASS：** 多 ID、未/部分/全消费、重入、增量及有效模型唯一性由 R01 V1/既有 A1/A2 覆盖。
4. **AC4 PASS（静态）：** 已建模拾取不复活、入包再丢下新 ID 正常、Guest 不驱动 Host 本地呈现；玩家现场仍未知。
5. **AC5 PASS：** 修复限于现有生产呈现/身份接缝；无第二仓库、旧 Writer 回退、资产特例或全图清理。
6. **AC6 PASS（缺口如实登记）：** 纯 fixture 的 Host Resolver 因反射装载失败，现场 Ghost 是否存在/消失未知；不把 seam Green 说成现场复现。
7. **AC7 PASS：** 原生事务、唯一 Writer、Core 身份/共享结算门、其它领域/运行形态及 07 距离边界未越界。
8. **AC8 PASS（源码交付）：** 当前恢复候选构建、Whitelist、独立身份验证和双轴 CLEAN 均完成；提交与 delivery/audit 由本限定交付承载。Runtime 状态只记 pending。

## TDD、构建与验证

- 原 AC2：`.scratch/item-runtime-repair-20261023/ac2-native-red/a2-formal-red-green.md` 保存真实旧组合 pending 2→4 / exit 1 及修复后唯一性 / exit 0；其输入与结论仍适用。
- 续修的 7 项定向最终日志：均位于 `.scratch/matt-orchestrate/item-runtime-repair-20261025-vela/messages/`：`Test_R2C_DestroyModelReadFailureWaitsAfterCancel-final.log`、`Test_R2C_DestroyPartialModelFailureWaitsWithoutReplay-final.log`、`Test_R3_StepFiveWaitsForCommittedPhysicalBinding-final.log`、`Test_R3_ConsumerWaitsForCommittedPhysicalBinding-final.log`、`Test_R01_V1_BudgetedConsumptionAndReentry-final.log`、`Test_R01_V1_ModeledPickupDoesNotResurrect-final.log`、`Test_R01_V1_AfterSnapshotFailureQuarantinesConsumer-final.log`；均 exit 0。
- 恢复后生产构建：`dotnet msbuild SteamP2PFriends.csproj /t:Build /p:Configuration=Release /p:Platform=AnyCPU /nologo`，exit 0；无生产构建警告。测试构建同配置 exit 0；有 4 项编译警告（`NativePendingBatchLeafProbe.cs` 未用变量/未赋字段，`R01E02SliceATests.cs` 测试字段未赋），无错误。
- 新建的 Release Whitelist 实际执行：`WhitelistTests\bin\Release\SteamP2PFriends.WhitelistTests.exe`，exit 0，`602/602 PASS (Failed: 0)`；末尾 Runtime evidence 明确 PENDING。逐字输出存于 `.scratch/matt-orchestrate/item-runtime-repair-20261025-vela/messages/restore-whitelist-release-run.log`，两构建日志在相邻 `restore-*-release-build.log`。
- 独立验证命令 `Tools/Verify-BuildFingerprintArtifact.ps1 -Path bin/Release/SteamP2PFriends.dll -ExpectedCaseId SPF-0.2.4.10-Experimental-ItemSlice-Cutover -LogPath .scratch/matt-orchestrate/item-runtime-repair-20261025-vela/messages/restore-whitelist-release-run.log`，exit 0 / `INDEPENDENT_ARTIFACT_VERIFICATION_PASS`；完整输出 `.scratch/matt-orchestrate/item-runtime-repair-20261025-vela/messages/restore-artifact-independent-verification.log`。
- 恢复产物身份：版本/Assembly/FileVersion `0.2.4.10`；MVID `f7bb4053-3ce2-4169-98de-9a61892e5d77`；SHA-256 `D54BE52386021649ADF0A56E1BC32E2CD60C65A8A5035E55E3977F1C054D29E7`；GUID `com.yu80rice.steamp2pfriends`；Case-ID `SPF-0.2.4.10-Experimental-ItemSlice-Cutover`；role `Cutover`。身份只绑定本地恢复候选产物，不代表部署、发布或 Runtime 通过。

## 双轴审查和 null finding 撤回

- 累计第 5 个完整双轴轮次 / E02 第 2 轮；S01、E01、E01.R2、E02 与前四轮完整 NON-CLEAN 历史保留。第 5 轮原始 Spec NON-CLEAN 与同轮撤回正文都保留；最终结论以同一个原 Spec 实例明确答复为准。
- 历史报告仍保留在 `.scratch/item-runtime-repair-20261023/round1-reviews.md`、`round2-*-r3.md`、`round3-*-r5.md`、`round4-*-r11.md` 与本 run 的 round5 消息记录；本次未清零历史、未新开返修窗口、未领取 R02。
- Standards 独立 native `01a17197-0935-74b3-b25b-aa21f5279146`，`gpt-6-luna/high`，CLEAN、0 阻断；1 项 `ItemLocalPresentationDispatch` 职责较宽的非阻断观察。Spec 独立 native `01a17197-3210-7c73-a006-ddf54505e22f`，`gpt-6-luna/high`，对原冻结 602 候选最终 CLEAN、无其他阻断。
- 原生 turn_context 与父线程角色路径证据：`.scratch/matt-orchestrate/item-runtime-repair-20261025-vela/messages/reviewer-own-turn-context-provenance.json`、`reviewer-native-activity-provenance.json`；原始报告及澄清：同目录 `round5-standards-native-final.json`、`round5-spec-native-final.json`、`round5-spec-same-round-clarification-native.json`、`round5-spec-explicit-final-native.json`。
- Spec 的 sole null finding 已撤回：生产 `CaptureSamples` 的 Host/Guest 共享入口在 stage/commit 前拒绝 null transport；对象 helper 能存 null 不代表该状态可达。人工直接 `StageAndCommitIdentity(..., null)` 的两条新增探针不是生产契约证据。
- 按根授权只撤回 post-review 自有增量（授权 `msg-be204bb4-c735-4bba-89d4-bd3122cec79a`）：`ItemProductionControlSeam.cs` 单行恢复原 `SameReference`；删除上述两个人工 null 测试和 `Program.cs` 两条注册。readback 确认边界恢复；`r3-null-stepfive-red.log`、`r3-null-consumer-red.log`、对应 build log 与 `Test_R3_NullTransportCommittedBindingAllows{StepFive,Consumer}-green.log` 均继续留存（两条 named green 实际 exit 1），不计 Red/Green，也未纳入 602 候选。

## 静态缺口与交付界限

`ResolveCurrentHostPlayer` 直接 fixture 仍因 `ReflectionTypeLoadException` 无法加载 SteamPlayer/NetReflection；这是已命名静态覆盖缺口，不是业务 Red，不扫描/安装 Unity。当前幽灵实体、模型 ID 与队列来源是否在真实玩家环境收敛仍未知。R01 源码验收完成，但 Runtime 保持 PENDING；后续实机由人工候选复测进行，按 R04 1H1G 症状定位后执行原 06 1H2G 与独立 07 20m 验收。当前未部署、未 push、未 release。

**交付入口：** [R01 delivery](../../.scratch/item-runtime-repair-20261014/issues/01-host-item-presentation-delivery.md)；[原票](../../.scratch/item-runtime-repair-20261014/issues/01-host-item-presentation.md)。
