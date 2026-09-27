# RuntimeFix 0.2.4.9 — Collision Ticket 09 S3 修复轮

日期：2026-09-27
票据：`.scratch/collision-migration-slice/issues/09-shared-1h2g-runtime-acceptance.md`
状态：修复轮交付，Runtime 验收仍 **PENDING**（归票 09，审查 CLEAN 后通知用户复测）
版本：`0.2.4.9 / Experimental`｜正式候选：`CandidateRole=Cutover`｜Case-ID：`SPF-0.2.4.9-Experimental-CollisionSlice-Cutover`（不变）
最终身份：插件 DLL SHA-256 `7C0ADF80E3CF83C1402F227AB1DC0D6A4AAFFB045802C370CCBF17329A16CC7B`，MVID `a3c09f2b-af77-408e-9e3a-f6afdac5f195`（双次 Rebuild 一致，独立核验 PASS）
测试宿主 EXE SHA-256：`C5B283D2822C9C133F6F335E3393D59CC2A55E1ACCC4C3B737218FD199E893F7`（诊断返修前基线，词汇表返修随插件 DLL 同步重建）

## 1. 结论

票 09 实机情景 3 失败（仅客机在场区域门动画同步但碰撞不同步，隐形墙）根因修复完成：票 08 退役旧 `LevelObjectRemoteCollisionPatch`（其 postfix 挂在原版 `LevelObject.UpdateActiveAndRenderersEnabled` 上，每次原版刷新后把覆盖区物件翻回激活——持续强制）时，新 Collision 执行链缺少等价的每拍维护。本轮在既有生产接缝上补齐"生命周期维护拍"：引擎每拍（`LifecycleOrchestrationEngine.cs:1017`）调用 `port.OnLifecycleTick` → 转发 Store → `LevelObjectCollisionAdapter` 重申已拥有覆盖。红→绿 411/411，四轮双轴审查闭合双 CLEAN。

## 2. 根因链（实机证据 + 源码对照）

- 实机三端指纹一致（`6db49d84…`/`F055B34C…`，上轮启动修复候选），启动闭环 CLEAN（三端 `Registration Closure complete`、0 行 `DiagnosticBuildValid=false`）——启动问题确已解决，本缺陷独立存在。
- 主机 Lease 生命周期健康（success/deferred/skipped 分布正常、零 fault），客机端零 Lease 行（listen-host 主机权威架构，符合设计）。
- 门区域故障不在租约生命周期，而在**维护拍缺失**：原版按主机本地剔除策略反复 `SetActive(false)` 远区物件（旧 Writer 头注释明示该机制），切换后无任何组件再激活 → 门物件停用/再激活循环中碰撞体回关闭位姿，动画经 AlwaysAnimate + 状态复制保持打开 → 到场者撞隐形墙。

## 3. 变更清单

| 文件 | 变更 |
|---|---|
| `Adapters/Collision/CollisionExecutionPort.cs` | `ICollisionOverrideStore` 新增 `OnLifecycleTick(float)` 契约；端口转发实现 |
| `Adapters/Collision/LevelObjectCollisionAdapter.cs` | `OnLifecycleTick` 持续再断言：仅 `Ledger.IsRegionActive` 的区域、仅已拥有条目（根激活 + 门动画 AlwaysAnimate）；按区域聚合诊断（成功行全字段：domain/transition/region/sessionEpoch/regionGeneration/attempts/roots/animations/outcome/reason）；故障有界心跳（`scope=heartbeat`，前 3 条 + 每 600 次）；恢复闭环（`outcome=recovered reason=reassert-resumed`，pending 于 `OnSessionEnd` 清位防跨 epoch 误报）；异常 fail-safe |
| `WhitelistTests/.../CollisionExecutionPortTests.cs` | CEP17 端口转发契约；4 假体补空成员；SessionBoundaryStore 增 Ticks/LastDelta |
| `WhitelistTests/.../CollisionExecutionStaticILContractTests.cs` | `Test_ProductionStoreReassertsOwnedOverrides` 结构门（IL 必含 GameObject.SetActive / UnityEngine.Animation / Ledger.IsRegionActive） |
| `WhitelistTests/Program.cs` | CEP17 条目 + 目标 410→411 |

不变量：无新 Harmony 补丁、无旧 Writer 复活（退役壳 `RegisterManual` 仍返回 false）、无第二写入路径（再断言只作用于已拥有条目且经引擎每拍端口调用驱动）、已释放区域不触碰。

## 4. 红→绿链

1. 红：缝合脚手架（接口成员 + 空实现）编译后，CEP17 与 StaticIL 门双红 `409/411`（`.scratch/ticket09-s3-red-run.log`）。
2. 绿：端口转发 + 再断言实现后 `411/411 PASS (Failed: 0)`、exit=0（`.scratch/ticket09-s3-green2-run.log`）。
3. 诊断返修两轮（见 §6 审查链），每轮后 `411/411`、exit=0（`green3/green4/green5-run.log`）。

## 5. 验证矩阵

| Evidence Class / Gate | 结果 | 证据 |
|---|---|---|
| PureMemory | PASS | `411/411 PASS (Failed: 0)`，CEP17 红→绿 |
| StaticIL | PASS | 结构门（OnLifecycleTick 必含再断言三要素）；无 roster/原生扫描门保持 |
| BuildArtifact | PASS | 双次 Rebuild 身份一致 `7C0ADF80…`/`a3c09f2b…`；独立核验 PASS |
| Diff | PASS | `git diff --check` 无错误 |
| Runtime | **PENDING** | 1 Host + 2 Guest 复测（情景 3 重点）归票 09，审查 CLEAN 后通知 |

## 6. 双轴审查链（四轮，全新零上下文并行实例）

| 轮 | Standards | Spec | 具名修复 |
|---|---|---|---|
| 1 | CLEAN | FAIL | 维护拍诊断缺 §61 有界心跳/恢复闭环、缺 §68 关联字段 → 按区域聚合 + 有界心跳 + 恢复闭环 |
| 2 | CLEAN | FAIL | 缺显式 `transition=`/`attempts=` 字段 → 成功/故障/恢复三行补全字段 |
| 3 | CLEAN | FAIL | ① 故障/恢复行需 `scope=heartbeat` 区别于区域转换日志；② `_reassertRecoveryPending` 未在 OnSessionEnd 清位（跨 epoch 误报）→ 均修复 |
| 4 | **CLEAN** | **CLEAN** | —（可延期项：三端 Runtime Gate 归实机复测） |

## 7. 缝合缺口与延期项

- 适配器 Unity 耦合面（SetActive/cullingType 实际效果）PureMemory 不可达，由 StaticIL 结构门 + 实机复测覆盖——与仓库既有接缝先例一致。
- 三端 Runtime Gate（含情景 3 复测）归票 09 实机验收。
- 过程记录：本会话早段曾出现工具回执串扰，取证协议改为 git diff 对账 + 退出码 + 文件副作用；本报告全部数字经该协议复核。

## 8. 交付边界

不发 Release、不打 tag；主机部署与三端指纹核验指引随本报告交付；票 09 检查项待实机复测后勾选。
