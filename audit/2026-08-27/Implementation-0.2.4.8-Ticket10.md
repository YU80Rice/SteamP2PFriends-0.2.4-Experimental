# Ticket 10：Resource Production Control Seam 交付审计报告

## 一、需求执行概述

在 `0.2.4.8` 实验分支完成 Resource Production Control Seam。Resource 的区域需求、租约滞回、连接/区域 generation、会话重置以及快照/增量复制均通过既有 Domain Adapter 接缝表达；未执行 Ticket 11 的旧 Authority Writer 退出，Runtime 证据仍保持 PENDING。

## 二、源码溯源清单

| 需求点 | 落实位置 |
| --- | --- |
| Host、授权 Guest、多观察者需求并集 | `Adapters/Resource/ResourceProductionControlSeam.cs`：`UpdateObserver`、`ProcessEntered`、`ProcessExited` |
| Acquire 与 2 秒 Hysteresis Release | `ResourceProductionControlSeam.cs`：`AdvanceTime`、`Flush`；`ResourceRegionLifecycleAdapter.cs`：`ScheduleRelease`、`CommitRelease` |
| 重入与 Connection Generation | `ResourceProductionControlSeam.cs`：连接 token 变更路径；`ResourceSnapshotAdapter.cs`：token 校验 |
| Region Generation 与过期增量拒绝 | `ResourceProductionControlSeam.cs`：`ReadGeneration`；`ResourceSnapshotAdapter.cs`：`TryAdvanceDeltaSequence` |
| Session Reset 清理 | `ResourceDomainAdapter.cs`：`OnSessionEnd`；`ResourceRegionLifecycleAdapter.cs`：`EndSession`；`ResourceSnapshotAdapter.cs`：`ResetSession` |
| Resource 高层接缝 | `WhitelistTests/Evidence/PureMemory/Adapters/Resource/ResourceProductionControlSeamTests.cs`：`Test_M6P07_RealResourceAdapterHighLevelSeam` |
| StaticIL 注册/唯一 writer/接缝契约 | `WhitelistTests/Evidence/StaticIL/ResourceProductionControlStaticILContractTests.cs` |
| 迁移与 Runtime 状态 | `docs/architecture/migration-manifest.md`、`.scratch/structure-baseline-0-2-4-8/issues/10-resource-production-control-seam.md` |

## 三、代码变更清单

- 新增 `Adapters/Resource/ResourceProductionControlSeam.cs`。
- 扩展 Resource lifecycle、snapshot、domain adapter 与 Multi-Observer 协调路径。
- 新增 Resource Production Control 的 PureMemory/StaticIL 测试，并接入唯一测试入口。
- 更新 Migration Manifest 与 Ticket 10 状态记录。
- 未修改标签、历史归档版本或 Ticket 11 的 Authority Writer 退出逻辑。

本轮针对独立审核阻断项补充：

- `ResourceRegionLifecycleLedger.EndSession()` 清空 generation、release、active region 和 dead-resource 状态。
- `ResourceSnapshotReplicationLedger.OnObserverDisconnect()` 要求精确匹配当前 `connectionToken`；陈旧断线事件被拒绝。
- 新增 `M6S07` 陈旧断线测试与 `M6R09` 会话结束清理测试，并加强真实 Resource 高层接缝断言。

## 四、编译与测试验证

| 门禁 | 命令/结果 |
| --- | --- |
| 主项目 Release | `dotnet build SteamP2PFriends.csproj -c Release --no-restore`；0 errors / 0 warnings |
| 测试项目 Release | `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release --no-restore`；0 errors / 0 warnings |
| 完整测试入口 | `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe`；`203/203 PASS`，Failed: 0 |
| Evidence Class | PureMemory PASS；StaticIL PASS；BuildArtifact PASS；Runtime PENDING |
| 差异检查 | `git diff --cached --check`；通过，只有 LF/CRLF 转换提示 |

## 五、Build Fingerprint 与独立产物核验

- Version：`0.2.4.8`
- AssemblyVersion/FileVersion：`0.2.4.8`
- MVID：`c68e7b4a-f3df-4ac5-9890-b895a2fa22bd`
- DLL：`bin/Release/SteamP2PFriends.dll`
- Independent DLL SHA-256：`F24BA1C80960C4FBC402D8D22746E333B64F49395AAB58B13CB9463728D424B4`
- Plugin GUID：`com.yu80rice.steamp2pfriends`
- Case-ID：`SPF-0.2.4.8-Experimental-StructureBaseline`

日志中的 Fingerprint 属于自报告；上面的 DLL SHA-256 是交付产物的独立重新计算结果，二者不混同。用户可用同一 Case-ID 关联日志与 DLL。

## 六、独立审核记录

1. 第一轮双轴审核：FAIL。指出会话结束清理、陈旧 token 清理和规格路径识别问题；前两项已修复。规格实际路径为 `.scratch/structure-baseline-0-2-4-8/spec.md`，根级 `.scratch/spec.md` 不存在不构成项目规格缺口。
2. 第二轮双轴审核：两个审核子任务未在限定时间内返回最终结论，随后均关闭；不将其视为 PASS。
3. 第三轮定向独立审核：PASS，无阻断项；确认会话清理、token 校验、新增测试以及 Runtime PENDING 状态。

所有本轮审核子智能体均已关闭。无未解决的代码阻断项。

## 七、偏离与妥协说明

- Runtime（SP/listen-host/U3DS/P2P）尚未取得真实运行证据，按规格保持 PENDING；静态、纯内存和构建证据未被升级为 Runtime PASS。
- 未执行旧 Resource Authority Writer 退出，该行为属于 Ticket 11 范围。

## 八、QA 后续测试建议

- 使用本报告 Case-ID 采集 Host、授权 Guest 和多观察者运行日志。
- 验证观察者离开后的 2 秒释放、重入、断线重连、区域重建、碰撞、采伐、快照与增量复制。
- 以本报告列出的 DLL SHA-256 重新关联两端日志；任一 DLL 或生产源码变化后重新采集运行证据。

## 九、最终结论

Ticket 10 的代码、测试、构建和独立审核闭环通过；Resource Production Control Seam 可交付至真实运行时验证阶段。Runtime 仍为 PENDING，当前分支可提交。
