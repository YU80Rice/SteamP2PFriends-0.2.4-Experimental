# Implementation 0.2.4.9 — Collision Ticket 06

日期：2026-09-26
票据：`.scratch/collision-migration-slice/issues/06-collision-execution-port-and-acquisition-receipt.md`
状态：`implemented-pending-runtime`
版本：`0.2.4.9 / Experimental`
候选角色：`ReadOnlyShadow`

## 1. 结论

票 06 的 Collision Domain Execution Port、Collision Lifecycle Policy 与 Acquisition Receipt 已完成。Collision 执行端口通过共享 `DemandProjectionEngine → LifecycleOrchestrationEngine → IDomainExecutionPort` 最高行为接缝消费 typed Collision Demand；它不扫描观察者、不重算需求、不进入生产注册，也不成为生产 Authority Writer。

- 唯一测试入口：`404/404 PASS`。
- 插件 Release 构建：0 error / 0 warning。
- WhitelistTests Release 构建：0 error / 0 warning。
- `Verify-EvidenceClassLayout.ps1`：PASS。
- `Verify-BuildFingerprintArtifact.ps1`：PASS。
- `Verify-Ticket09Documentation.ps1`：PASS。
- `git diff --check`：PASS（仅有既存 CRLF 转换提示，无 diff 错误）。
- 双轴审查：第五轮 Standards CLEAN / Spec CLEAN；前四轮的 BLOCKING 均已修复并回归验证。
- 不宣称正式 Collision Runtime PASS；不打 tag；不发 Release。正式切换与 1 Host + 2 Guest Runtime 仍归票 08/09。

## 2. 变更清单

### 生产源码

- `Adapters/Collision/CollisionLifecyclePolicy.cs`：Collision 独立声明滞回、retry 与 heartbeat 政策，不借用 Resource 常量。
- `Adapters/Collision/CollisionAcquisitionReceipt.cs`：不可变 Collision Override、执行身份与 Acquisition Receipt；Receipt 深拷贝并以只读集合暴露实际 Override，Equals/hash 包含 Override 集合。
- `Adapters/Collision/CollisionExecutionPort.cs`：实现 `IDomainExecutionPort`；会话/区域代次/Domain/需求身份门；typed receipt Release；原子所有权撤销契约；Acquire/Release 诊断。
- `SteamP2PFriends.csproj`：登记上述三个生产编译项。

### PureMemory / StaticIL

- `WhitelistTests/Evidence/PureMemory/Adapters/Collision/CollisionExecutionPortTests.cs`：CEP01–CEP13，覆盖共享引擎接缝、Receipt 身份/replacement、typed Release、Host Demand、陈旧/不确定身份、ownership-unproven、需求仍在、Release 诊断。
- `WhitelistTests/Evidence/StaticIL/CollisionExecutionStaticILContractTests.cs`：端口形状、无 roster/native scan、Receipt/Override 粒度、原子撤销入口、零生产注册。
- `WhitelistTests/Evidence/StaticIL/LifecycleOrchestrationStaticILContractTests.cs`：Lifecycle Policy 构造数更新为 Resource + Collision 两个领域声明，引擎内部仍零构造。
- `WhitelistTests/Program.cs`：登记票 06 测试，恢复并保留票 05 的 M6C08 与 Shadow Single Consumer，Target 更新为 404。

## 3. 红→绿链

1. 首轮红测：未存在 `CollisionExecutionPort`、`ICollisionOverrideStore`、Receipt 与 Policy 时，测试宿主因缺失票 06 类型失败；生产基线 DLL 可独立重建。
2. 实现最小端口/Receipt/Policy 后，插件与测试宿主可编译；共享引擎接缝 CEP01、Host Demand CEP07 加入后保持绿。
3. 旧 Lifecycle Policy 单构造契约因第二领域声明变红，改为精确断言 Resource + Collision 两个声明、Lifecycle 引擎零构造后恢复。
4. 首轮双轴发现并修复：重复 StaticIL 阶段赋值；Release 缺少当前 Receipt/Acquire Generation 校验；Receipt 集合可变/相等性忽略 Override；Release 缺具体 Receipt 诊断。
5. 第二轮 Spec 发现并修复 ownership-unproven 会被跳过；改为保留 Receipt、非破坏性失败、可观测。
6. 第三轮 Spec 发现并修复逐项撤销可能部分破坏、早期拒绝静默；加入 `TryRevokeOwnedAtomically` 全量预检后提交，并统一身份拒绝诊断。
7. 第四轮 Spec 发现并修复 ActiveDemand 身份门与 Acquire 诊断缺口；Release 要求 `ActiveDemandCount == 0`，Acquire 成功/拒绝输出完整身份与结果/原因。
8. 第五轮双轴均 CLEAN；最终回归为 404/404。

## 4. 最终行为不变量

- Acquire Receipt 绑定 `DomainId.Collision`、`RegionKey`、`SessionEpoch`、`RegionGeneration`、`AcquireGeneration`，并列出实际 Collision Override。
- 新 Acquire 替换当前 Receipt；旧 Receipt 通过 typed `TryRelease(ticket, expectedReceipt)` 被拒绝，不能释放新 Acquire。
- Release 只对当前 Receipt 的 Override 执行 `TryRevokeOwnedAtomically`；所有权无法整体证明时不执行任何撤销，保留 Receipt 并写 `ownership-unproven`。
- 无 Receipt、陈旧 Session/Region Generation、身份不确定或 `ActiveDemandCount > 0` 时不提交破坏性 Release；拒绝均保留状态并写可观测诊断。
- Receipt 不含 Resource、ResourceSpawnpoint 或可采集树操作；端口不调用 `enable/disable`，不扫描 `Provider.clients`，不重算空间需求。
- Collision 端口没有生产 `LifecycleOrchestrationEngine.Register` 调用；现阶段旧 `LevelObjectRemoteCollisionPatch` 仍是唯一生产 Writer。

## 5. 验证矩阵

| Evidence Class / Gate | 结果 | 证据 |
|---|---|---|
| PureMemory | PASS | CEP01–CEP13、既有 LOE/ARI/CSC 与 Resource 回归；唯一入口 404/404 |
| StaticIL | PASS | Collision Execution StaticIL、Lifecycle/Registration/Shadow/Readiness 既有契约 |
| BuildArtifact | PASS | 两次 Release Rebuild 字节身份一致；独立核验 PASS |
| Runtime | PENDING | 正式切换 Runtime 归票 09；本票不把 Shadow Runtime 当正式 PASS |
| Layout | PASS | `EVIDENCE_CLASS_LAYOUT_PASS` |
| Metadata | PASS | `TICKET09_DOCUMENTATION_METADATA_PASS` |
| Diff | PASS | `git diff --check` |

## 6. 产物身份

两次 Release Rebuild 的 SHA-256 与 MVID 逐项一致：

- 插件 DLL：SHA-256 `4D40F0B68193843A5E38B0F422A344D0690DC2F7C2FCE0677D5DC027BC468306`；MVID `c560aaed-4f28-4c9f-b345-51f8a683f918`。
- 测试 EXE：SHA-256 `813108170FA7FCEB0DEE7D3223839A4575A7610BB8B2F2E3227772EEA015EC0B`。
- 版本/身份：`0.2.4.9`、`SPF-0.2.4.9-Experimental-CollisionSlice-ReadOnlyShadow`、CandidateRole=`ReadOnlyShadow`。

## 7. 负控制与扰动记录

- 尝试移除需求门、身份门、原子撤销调用并按“先重编插件、再重编测试宿主、再运行唯一入口”重放；这些直接文本扰动均在 `TreatWarningsAsErrors`/接口或编译结构阶段拦截，未形成有效真分支命中，**不计入验证轮次**，如实记录，不伪称 PASS。
- 有效行为证据由 CEP05、CEP09、CEP11、CEP12、CEP13 与 StaticIL 组合提供：陈旧 Receipt、ownership-unproven、无 Receipt/身份不确定、正需求 Release、零生产注册均被直接锁定。
- 所有扰动源码均在每次尝试后恢复；最终干净构建与 404/404 回归再次通过。

## 8. 双轴审查链

| 轮 | 本轮增量 | Standards | Spec |
|---|---|---|---|
| 1 | 初始端口/Receipt/Policy、CEP/StaticIL、入口接线 | BLOCKING：Program 重复阶段赋值 | BLOCKING：Release 未绑定当前 Receipt/Acquire Generation；Receipt 暴露可变 Override；缺具体 Receipt 诊断 |
| 2 | 重复赋值、Receipt 只读/相等性、typed Release/诊断修复 | CLEAN | BLOCKING：ownership-unproven 被跳过并当成功 |
| 3 | ownership-unproven 保留 Receipt、非破坏性失败、CEP11 | CLEAN | BLOCKING：逐项撤销可能部分破坏；早期拒绝静默 |
| 4 | 原子撤销契约、统一早期拒绝诊断 | CLEAN | BLOCKING：缺 ActiveDemand 门；Acquire 诊断不足；原子语义需明确 |
| 5 | ActiveDemand/Identity 门、Acquire 完整诊断、CEP13、最终回归 | CLEAN | CLEAN |

第 1–4 轮的 BLOCKING 均在下一轮修复并重新构建/回归；第五轮为全新实例双轴 CLEAN，审查链闭合。

## 9. 缝合缺口与延期项

- `MultiObserverShadowCoordinator` 与真实插件更新入口依赖 Unity/Unturned，本票 PureMemory 只覆盖共享引擎→端口行为，生产接线资格由 StaticIL 覆盖；本票不把 Collision 注册进生产协调器。
- `LevelObjectRemoteCollisionPatch` 仍是影子期唯一生产 Writer；正式切换、旧 Writer 调用归零、1 Host + 2 Guest 正式 Runtime 归票 08/09。
- 原子撤销语义通过 `ICollisionOverrideStore.TryRevokeOwnedAtomically` 契约下沉到原生适配器；本票提供纯内存假体和静态入口门，真实 Unturned Override 接线属于后续切换票，不在本票声称完成。

## 10. 交付边界

本票完成静态与纯内存实现并授予 `implemented-pending-runtime`。未改变 Collision 生产 Authority Writer，未改变版本/Case-ID 候选角色，未执行关卡运行时切换，不发 Release。下一前沿为票 07：Collision 正式切换准入 Go/No-Go 证据。
