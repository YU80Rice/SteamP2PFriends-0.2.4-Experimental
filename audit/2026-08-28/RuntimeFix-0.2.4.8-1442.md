# 缺陷修复执行报告 - 0.2.4.8 / Ticket 11 动态测试前可观测性修复

## 一、问题定位与修复策略

根因复核确认：Resource 生产接缝的释放事务、观察者更新异常、Harvest Hook 注册状态和 Resource 日志边界不足以支持下一轮动态测试的因果判断，部分路径可能静默或把本地观察误报为网络接受。

本轮按 TDD 修复：先增加失败回归测试，再修正实现；未进行动态测试，未将 Runtime 标记为 PASS，也未关闭 Ticket 11。

主要修复：

- 新增统一 `ResourceObservability` 格式，输出 `caseId`、`role`、`domain`、`region`、`sessionEpoch`、`connectionGeneration`、`regionGeneration`、`path`、`spiActive`、`shadowOnly`、`outcome`。
- `ResourceProductionControlSeam.Flush` 仅在 `OnRelease` 成功后移除 pending release 和 lease；失败、会话不匹配时保留可重试状态并输出 `LeaseReleaseFailed`。
- Observer 更新/移除增加控制面快照恢复、外部 replication 逆向补偿和补偿失败后的 fail-closed 标记。
- Harvest 四个 Hook 增加逐项状态、当前项登记、失败回滚和残留核验路径。
- Guest Resource delta 日志改为 `observed`，对无法由当前原生签名取得的网络接受判定输出 `accepted=unknown`、`rejected=unknown`、`stale=unknown` 与 `decisionSource=not-available`。
- generation 失败、连接代次溢出和 SnapshotWrite Prefix 的证据边界得到明确化。
- `Verify-BuildFingerprintArtifact.ps1` 保持 `-Path`、`-ExpectedCaseId`、`-LogPath` 参数，并验证 MVID、AssemblyVersion、FileVersion、SHA-256、版本、插件 GUID 和 Case-ID。

## 二、核心代码变更对比

涉及文件：

- `Adapters/Resource/ResourceObservability.cs`
- `Adapters/Resource/ResourceProductionControlSeam.cs`
- `Adapters/Resource/ResourceSnapshotAdapter.cs`
- `Adapters/Resource/ResourceDomainAdapter.cs`
- `Adapters/Resource/ResourceRegionLifecycleAdapter.cs`
- `Adapters/Resource/Patches/LevelGroundRemoteTreeCollisionPatch.cs`
- `Adapters/Resource/Patches/ResourceManagerHarvestReplicationPatch.cs`
- `Adapters/Resource/Patches/ResourceManagerRegionSyncPatch.cs`
- `Adapters/Resource/Patches/ResourceManagerWorldSyncDiagnosticPatch.cs`
- `Core/Build/BuildFingerprint.cs`
- `Core/ControlPlane/MultiObserverShadowCoordinator.cs`
- `Core/ControlPlane/Spatial/SpatialObserverIndex.cs`
- `Platform/Client/P2PJoinManager.cs`
- `SteamP2PFriends.csproj`
- `Tools/Verify-BuildFingerprintArtifact.ps1`
- `WhitelistTests/Evidence/BuildArtifact/BuildArtifactEvidenceTests.cs`
- `WhitelistTests/Evidence/PureMemory/Adapters/Resource/ResourceObservabilityTests.cs`
- `WhitelistTests/Evidence/PureMemory/Adapters/Resource/ResourceProductionControlSeamTests.cs`
- `WhitelistTests/Evidence/StaticIL/ResourceHarvestRegistrationStaticILContractTests.cs`
- `WhitelistTests/Program.cs`

本轮新增回归入口包括：

- `M6P09` 释放失败保留可重试状态；
- `M6P11`/`M6P12` 单区域 replication enter/exit 失败回滚；
- `M6P13`/`M6P14` 多区域失败逆向补偿；
- `M6O03` 原生接收不臆造网络接受；
- Resource Harvest 四 Hook 状态契约。

## 三、编译与自测状态

| 审核项 | 结果 | 证据 |
| :--- | :--- | :--- |
| 主项目 Release 构建 | PASS | `SteamP2PFriends.csproj`，0 errors / 0 warnings |
| 测试项目 Release 构建 | PASS | `WhitelistTests/SteamP2PFriends.WhitelistTests.csproj`，0 errors / 0 warnings |
| 完整测试 | PASS | `218/218 PASS`，`Failed: 0` |
| Evidence Class | PASS | PureMemory、StaticIL、BuildArtifact 执行；Runtime 明确 PENDING |
| `git diff --check` | PASS | 无尾随空格错误；仅有 Git 换行转换提示 |
| 独立 BuildArtifact 核验 | PASS | `INDEPENDENT_ARTIFACT_VERIFICATION_PASS` |

当前 Release DLL 指纹：

| 字段 | 值 |
| :--- | :--- |
| 路径 | `bin/Release/SteamP2PFriends.dll` |
| Version | `0.2.4.8` |
| AssemblyVersion | `0.2.4.8` |
| FileVersion | `0.2.4.8` |
| MVID | `ee226b9d-472a-4b02-ac23-1b93e0e30138` |
| SHA-256 | `984ED15C9E59255FC70C1ED46B30503DC2C59E037FD2F4AE83CC78A30203024C` |
| Plugin GUID | `com.yu80rice.steamp2pfriends` |
| Case-ID | `SPF-0.2.4.8-Experimental-StructureBaseline` |

## 四、独立审核记录

审核遵守串行冻结协议：每轮均使用全新子智能体和自包含快照；审核期间未修改源码、配置、构建产物或报告；每次返回完整结论后立即关闭子智能体。

### 第 1 轮

判定：FAIL。

阻断项：Flush 会话不匹配删除状态；Observer 异常缺少事务恢复；Harvest 当前 Hook 回滚登记不完整；Guest 字段固定/伪成功；generation 原因不结构化；测试接缝过浅。

### 第 2 轮

判定：FAIL。

修复后仍发现：控制面恢复未补偿外部适配器；Guest 接收判定仍可能把本地回调视作 accepted；generation 读取/连接代次异常证据不完整；Harvest 外层异常回滚和逐项身份状态不足。

### 第 3 轮

判定：FAIL。

阻断项：

1. `ResourceProductionControlSeam` 的外部失败调用可能已产生副作用后抛异常，当前补偿登记无法覆盖该失败调用本身；`OnObserverDisconnect` 仍缺乏可逆补偿。
2. Harvest `harmony.Patch` 与 `applied.Add` 之间仍存在极窄异常窗口，且回滚后不能对四个固定目标逐项证明无残留。
3. 四个 Harvest Hook 只有布尔状态和聚合摘要，尚未逐项输出 owner、target 完整签名、patch method 和回滚残留状态。
4. `SendResources_Write_Prefix` 仍需在最终产物中确认只表示进入/观察，而非原生完成成功。
5. BuildArtifact 和 Harvest 测试仍有文本/方法名自证成分，缺少完整黑盒负例和隔离 Harmony 失败注入。
6. Flush 中 generation reader 异常未在所有路径统一转为 `LeaseReleaseFailed`；Resource release 拒绝原因尚未区分 session/generation/current-generation。
7. `BeginLocalConnection()` 的 overflow 返回值未被 `P2PJoinManager.OnClientConnected` 消费，连接流程仍可能继续。
8. `ResourceObservability.path` 与 outcome 仍是自由字符串，缺少受控值契约。

根据仓库门禁，同一核心阻断项已连续三轮未通过，循环停止，不再进行第四轮修复或审核。三轮审核均已关闭子智能体；不存在可采信的独立 PASS。

## 五、最终结论

**静态/自动化证据：通过。独立审核门禁：FAIL。Runtime：PENDING。Ticket 11：不得关闭。**

当前代码可供后续继续修复，但本报告不宣称 Resource Migration Slice 完成，不宣称 Multiple-Observer/SPI 已通过动态验收，也不把既有 Host/Guest 测试结果迁移到本次 DLL。

后续恢复工作前，优先解决第三轮列出的 8 项静态阻断，随后必须使用重新构建的同一 DLL、同一 Case-ID、同一 SHA-256/MVID 重新执行独立审核，审核 PASS 后才可进入新的动态测试准备阶段。
