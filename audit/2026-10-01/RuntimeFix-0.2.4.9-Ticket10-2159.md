# RuntimeFix-0.2.4.9-Ticket10-2159 — Collision 代次源自举：让租约首次 Acquire 成立

- **日期**：2026-10-01 21:59
- **票**：`.scratch/collision-migration-slice/issues/10-collision-generation-source-bootstrap.md`（ready-for-agent，用户点名版本）
- **落点**：06/08 执行路径交付中的代次接线缺陷（票 09 S3 取证定案 `audit/2026-09-27/Forensics-0.2.4.9-Ticket09-2051.md`：4293 条 `[CollisionExecution] outcome=rejected reason=acquire-identity-rejected`、统一签名 `regionGeneration=0 acquireGeneration=0`，Collision 零租约、门从未被接管）
- **分支**：`codex/structure-baseline-0.2.4`（从 HEAD `884baae` 继续，不回退仓库、不改 01–08 冻结范围）

## 1. 缺陷机理（已由红测证实）

引擎 Acquire 路径以 `beforeAcquire = port.ReadRegionGeneration(region)` 开票；生产端口代次源接 `LevelObjectCollisionAdapter.GetGeneration`（读静态 `LevelObjectCollisionLedger`）。Ledger 对从未 Acquire 的区域返回 0，`RegionGeneration.FromNative(0).IsDefined == false`，`CollisionExecutionPort.CanCommit` 的 `IsDefined` 门恒拒 → 首次 Acquire 永不成立 → 代次永远无法经 `Ledger.CommitAcquire` 推进 → 自举死锁。Resource 域无此问题：其提交路径 `ResourceRegionLifecycleAdapter.TryCommitAcquire` 接受 expected=0 并推进 0→1（`ResourceDomainAdapter.OnAcquire`）。

既有 CEP01–17 均以显式 `generation:7` 开票或恒返 7 的 lambda 作代次源，绕过自举路径——与启动装配路径同型的测试盲区。

## 2. 修复（读侧自举，最小刀）

`Adapters/Collision/LevelObjectCollisionAdapter.cs`：

- 新增 `public const uint InitialRegionGeneration = 1U`；
- `GetGeneration` 对 Ledger 无条目区域返回该已定义初始代次（读侧合成，只读呈现，不写 Ledger）。

红线核对：`CanCommit`（sessionEpoch、`IsDefined`、`reader==ticket`）一行未动；Ledger 本体未动（M6C01–08 冻结测试未动）；`OnLifecycleTick` 维护拍未动；无第二写入路径、未复活旧 Harmony 补丁、无运行中切流。取值 1 与 Ledger 首次 `CommitAcquire` 的落点（0+1）一致：首次提交即把该代次写入 Ledger，其后每次提交 +1。注释已写明：这是读侧自举，与 Resource 写入侧条件提交（`TryCommitAcquire` 接受 0 开票）语义不同，勿照搬为通用模式。

## 3. 测试接缝约束（具名，非静默跳过）

**PureMemory 宿主（.NET Framework）JIT 在编译含 Unity extern/icall 调用点的方法时即抛 `SecurityException: ECall 方法必须打包到系统模块中`，与该调用点运行时是否可达无关。** 实测：真实 Store 的 `OnSessionBegin` 无条件调用 `RestoreOwnedNativeState`（含 `Animation.cullingType` extern 调用点），在宿主中永远无法执行；空字典裸循环、托管属性读取均正常。推论：

- CEP18（端口接缝）用真实生产代次源 `LevelObjectCollisionAdapter.GetGeneration` 作 `generationReader` + 可证身份的 `FakeCollisionOverrideStore`，复刻引擎 `beforeAcquire→票据→TryAcquire`；缺陷在代次源与身份门，不在 Store。
- CEP19（代次源自举与推进）用真实适配器：`GetGeneration` 钉死已定义初始代次 1U；经生产提交入口 `ICollisionOverrideStore.Acquire`（执行端口过身份门后调用的正是它）首次提交落点 1U、再次提交 +1；其调用链（`Acquire→Ledger.CommitAcquire→AcquireNativeOverrides→LevelObjects.objects` 托管属性，宿主中为 null 提前返回）不含 extern 调用点，可纯内存执行。
- 引擎→端口→真实 Store 的全闭环在宿主不可执行，由 StaticIL 契约（端口形状、维护拍转发、会话边界）与票 09 实机诊断复测补齐。

## 4. TDD 链（红→绿，最终测试形态对 HEAD 双观察）

| 步骤 | 证据 | 结果 |
|---|---|---|
| 红测 v1（CEP18 真实 Store 形态） | `.scratch/ticket10-red-run.log` | FAIL 但红因是 ECall 接缝约束 → 改形并具名 |
| 红测 v2（改形后） | `.scratch/ticket10-red3-run.log` | CEP18/19 均以「never-acquired region must read a defined initial generation」FAIL，411/413 |
| Round 1 审查收敛后改断言/提交入口 | `.scratch/ticket10-review-diff2.txt` | 钉字面量 1U 与 +1；CEP19 改走 `Store.Acquire` |
| 最终形态红（对 HEAD，修复临时移出） | `.scratch/ticket10-red4-run.log` | CEP18/19 均红在自举断言，411/413 |
| 最终形态绿（修复恢复） | `.scratch/ticket10-green2-run.log`、`final2-run.log` | **413/413 PASS** |

构建：插件与测试宿主 Release 0 error / 0 warning。

## 5. 审查链（双轴全新实例，每轮两个新上下文并行）

- **Round 1**：Standards CLEAN（3 条具名可延期气味：CEP19 旁路 OnAcquire、断言 `>=` 过松、注释暗示可照搬）；Spec CLEAN（裁定「对齐 Resource 先例」= 行为语义对齐：首次 Acquire 成立并推进，而非字面返回值对齐）。三条气味当场收敛（见 §4 第三行）。
- **Round 2**：Standards CLEAN（收敛到位、无新违规、无新气味）；Spec CLEAN（无缺口、无偏差、无范围蔓延）。

## 6. 身份与门禁

- 双次 `-t:Rebuild` Release：SHA-256 `F6C46B82AF24A91098D2C96037CC44C1ABA00258DA69C39A87057366C1F9F083`、MVID `bcd76cfa-b191-43ec-89bc-989698897100` 两次一致（`.scratch/ticket10-rebuild1b-identity.txt`、`rebuild2b-identity.txt`）。
- `Tools/Verify-BuildFingerprintArtifact.ps1 -ExpectedCandidateRole Cutover` → **PASS**（Case-ID `SPF-0.2.4.9-Experimental-CollisionSlice-Cutover`，`.scratch/ticket10-verifier2.log`）。
- `git diff --check` PASS。
- 增量：`Adapters/Collision/LevelObjectCollisionAdapter.cs`（+14/-1）、`CollisionExecutionPortTests.cs`（CEP18/19 + 注释）、`WhitelistTests/Program.cs`（2 行注册）。

## 7. 交付与移交

- 候选：`bin/Release/SteamP2PFriends.dll`（§6 身份，CandidateRole=Cutover）。
- 部署与三端指纹核验按 `docs/agents/auto-rm-test-sop.md` 人工执行；随后进入**票 09 诊断复测**（三问：门区域 Collision Acquire 是否 success、门是否被记进所有权、客机在需求范围内是否超出房主相机剔除距离）。
- **票 09 维持 in-progress 不关单**；本票不关 Collision Slice（Runtime PASS 归票 09）。
