# RuntimeFix 0.2.4.9 — Collision Ticket 09 修复轮

日期：2026-09-26
票据：`.scratch/collision-migration-slice/issues/09-shared-1h2g-runtime-acceptance.md`
状态：修复轮交付，Runtime 验收仍 **PENDING**（归票 09）
版本：`0.2.4.9 / Experimental`
正式候选：`CandidateRole=Cutover`
Case-ID：`SPF-0.2.4.9-Experimental-CollisionSlice-Cutover`（不变）
新指纹：插件 DLL SHA-256 `F055B34CF40FF3D73E4C946CD11BBBF0CBB64E2F98CCE69903ABDEEB92C95CFE`，MVID `6db49d84-844a-4aae-9bf3-0a759b027170`

## 1. 结论

票 08 切换候选（CC29F779…/a5465633…）在主机首次实机启动失败：Registration Closure 关闭时报告 `Domain Id 未登记: Collision`，`DiagnosticBuildValid=false`，P2P-Lobby/ClientLobbyListener/P2PJoinManager 初始化被 INVALID 门控跳过（诊断包 `UMM-诊断包_20260926_232024/LogOutput.log` 第 545–546 行）。根因是票 08 退役旧 Collision Writer 时删除了 `RegisterDomainAdapters()` 中的 Closure 登记，但未同步删除启动要求清单中的 Collision 要求条目——该启动装配路径正是票 08 审计 §7 记录的 PureMemory 缝合缺口。

本修复轮以红→绿循环消除该漂移：注册要求清单抽为生产唯一来源工厂并被启动装配消费，REG04 契约测试咬中真实生产清单，删除过期的 Collision 要求后全量回归 `410/410 PASS (Failed: 0)`。插件与测试宿主 Release 构建 0 error / 0 warning。双次 Rebuild 身份逐项一致，独立核验 `INDEPENDENT_ARTIFACT_VERIFICATION_PASS`。Standards/Spec 双轴最终 CLEAN（Runtime 验收显式延期归票 09）。

## 2. 根因

- `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationOrchestration.cs` 的内联要求清单含 `RegistrationRequirement(DomainIds.Collision, true, false)`；
- 票 08 commit `99eb3c2` 已从 `RegisterDomainAdapters()` 移除 `RegisterLifecycle(new LevelObjectCollisionAdapter())`（Collision 生命周期改由共享 `LifecycleOrchestrationEngine` 注册）；
- 启动时 `RegistrationClosure.TryClose` 因要求无槽位而失败 → `DiagnosticBuildValid=false` → 联机入口未初始化；
- WhitelistTests 409 项均未构造真实插件启动装配，静态 CLEAN 无法覆盖此路径——与票 08 §7「缝合缺口」记录一致。

## 3. 红→绿链

1. 行为等价重构：要求清单抽为 `Core/Registration/PatchRegistrationRequirements.Create()`，`CreatePatchRegistrationPlan()` 改为消费工厂（此时清单仍含 Collision）。
2. 新增 REG04（`WhitelistTests/Evidence/PureMemory/Core/RegistrationClosureTests.cs`）：用生产工厂构造 Closure，按生产登记目录（Item/Resource/Building/Zombie/Animal 生命周期+复制）登记后 `TryClose` 必须成功。独立 RunTest 条目接入单一入口，目标数 409→410。
3. 红测观察：`FAIL [PureMemory] REG04 ProductionRequirements`，`409/410 PASS (Failed: 1)`（`.scratch/ticket09-fix1-red-run.log`）——失败即生产清单闭合不了。
4. 修复：删除工厂中过期的 Collision 要求条目（生产改动 1 行）。
5. 绿测：重编插件 + 测试宿主后 `410/410 PASS (Failed: 0)`（`.scratch/ticket09-fix2-green-run.log`）。

## 4. 变更清单

| 文件 | 变更 |
|---|---|
| `Core/Registration/PatchRegistrationRequirements.cs` | 新增：注册要求清单生产唯一来源（Item/Resource/Building/Zombie/Animal 生命周期+复制；Collision 不再要求） |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationOrchestration.cs` | `CreatePatchRegistrationPlan()` 消费工厂，删除内联清单 |
| `SteamP2PFriends.csproj` | 登记新文件 Compile Include |
| `WhitelistTests/Evidence/PureMemory/Core/RegistrationClosureTests.cs` | 新增 REG04 契约 |
| `WhitelistTests/Program.cs` | REG04 独立条目 + 目标 410 |

不变量：共享引擎 `LifecycleOrchestrationEngine.Register` 生产注册仍为 2（Resource、Collision）；Patch Registration Closure（SPI 适配器目录）与共享引擎登记是两套机制，本轮只对齐前者与票 08 退役事实，未触碰执行路径。

## 5. 验证矩阵

| Evidence Class / Gate | 结果 | 证据 |
|---|---|---|
| PureMemory | PASS | `410/410 PASS (Failed: 0)`，REG04 红→绿 |
| 插件 Release 构建 | PASS | 0 error / 0 warning（warnaserror） |
| 测试宿主 Release 构建 | PASS | 0 error / 0 warning |
| BuildArtifact | PASS | 双次 Rebuild 身份一致；独立核验 PASS |
| Diff | PASS | `git diff --check` 仅 CRLF 提示 |
| Runtime | **PENDING** | 新指纹的 1 Host + 2 Guest 验收归票 09，用户复测后采集 |

## 6. 双次 Rebuild 身份（`/p:SteamP2PFriendsCandidateRole=Cutover`）

| 项 | Run 1 | Run 2 | 结论 |
|---|---|---|---|
| 插件 DLL SHA-256 | `F055B34CF40FF3D73E4C946CD11BBBF0CBB64E2F98CCE69903ABDEEB92C95CFE` | 同左 | 一致 |
| 插件 DLL MVID | `6db49d84-844a-4aae-9bf3-0a759b027170` | 同左 | 一致 |
| 测试宿主 EXE SHA-256 | `2C394FD5B513CC81CD0EDCE196199DAFB3C1CA1ECD78DC1B54C55D5384267499` | — | 记录 |

独立核验：`Tools/Verify-BuildFingerprintArtifact.ps1 -ExpectedCandidateRole Cutover` → `INDEPENDENT_ARTIFACT_VERIFICATION_PASS`（`.scratch/ticket09-fix-verifier.log`）。

## 7. 双轴审查链

- Round 1（全新零上下文并行实例）：
  - Standards：**CLEAN**（3 项 P3 判断题列为可延期观察项：工厂缓存、返回类型、三元组重复——均不阻塞）。
  - Spec：首判 **FAIL**，唯一 finding 为「Runtime Gate 未跑」。同轮澄清（当前实例，规则允许）确认其性质为交付后流程时序而非增量缺陷，且裁决原文已认定「必须延期至正式 Runtime 验收完成」→ 改判 **CLEAN**，延期项具名：Runtime 验收归票 09。
- 最终：双轴 **CLEAN**；延期项仅 Runtime PENDING 一项，已具名。

## 8. 交付边界与部署

- 本轮不宣称 Runtime PASS；票 09 检查项全部保持未勾选，待用户三端复测。
- 主机已部署新指纹候选并核对哈希；两台客机须由用户重新分发同一 DLL 并各验一次哈希。
- 回滚边界不变：结束会话并部署上一份已验收构建（`E:\Steam\steamapps\common\Unturned\SteamP2PFriends.dll.pre-ticket09-20260926` 为 09-20 旧版留档）。
