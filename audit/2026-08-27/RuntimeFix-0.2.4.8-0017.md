# 缺陷修复执行报告 - 0.2.4.8 / Ticket 11

## 一、问题定位与修复策略

- **问题**：三端旧诊断包中的游戏行为均成功，但日志只有原生 Resource 写入/采伐记录，没有证明 Resource SPI 与真实 Resource 数据面形成可观察闭环。
- **根因**：`ResourceDomainAdapter.OnReplicationTick` 为空；`SendResources_Write`、`ReceiveResources`、`ServerSetResourceDead/Alive` 未将真实入口事件回接 `ResourceSnapshotAdapter`；Resource 控制摘要也未暴露 demand、lease 和 pending release 状态。
- **修复策略**：保留 `ResourceManager` 原生状态编码和协议执行器，只把其真实入口桥接到 Resource 复制账本；由 `ResourceProductionControlSeam` 保持唯一租约权威，并补齐 Acquire、Release、滞回、重入、generation、快照、增量和 stale 证据日志。

## 二、核心代码变更

- `Adapters/Resource/ResourceSnapshotAdapter.cs`
  - 增加原生快照写入、接收、增量计数及 stale generation 计数。
  - 增加 generation 单调保护。
  - 合法的新代次本地增量推进观察者快照代次和序列号；倒退代次拒绝。
- `Adapters/Resource/ResourceDomainAdapter.cs`
  - 将 SPI 生命周期、观察者进入/退出、断线和复制 Tick 接入日志与复制账本。
  - 明确 `leaseAuthority=ResourceProductionControlSeam`。
- `Adapters/Resource/ResourceProductionControlSeam.cs`
  - 输出 LeaseReleaseScheduled、LeaseReleaseDeferred 和 LeaseReentry 证据。
  - 输出 Resource demand region、active lease、pending release 摘要字段。
- `Adapters/Resource/Patches/ResourceManagerRegionSyncPatch.cs`
  - 在真实 `SendResources_Write` 入口记录 SPI 关联观察者数量，并明确原生 `stateEncoder` 身份。
- `Adapters/Resource/Patches/ResourceManagerWorldSyncDiagnosticPatch.cs`
  - 在真实 `ReceiveResources` 入口记录快照接收事件。
- `Adapters/Resource/Patches/ResourceManagerHarvestReplicationPatch.cs`
  - 在 dead/alive 入口记录 Resource 原生增量并推进 SPI 代次账本。
- `WhitelistTests/Evidence/PureMemory/Adapters/Resource/ResourceSnapshotAdapterTests.cs`
  - 新增 `M6S08 ResourceNativeDataPlane` 回归测试。
- `WhitelistTests/Evidence/StaticIL/ResourceProductionControlStaticILContractTests.cs`
  - 新增三个真实入口到 ResourceSnapshotAdapter 的接线断言。
- `WhitelistTests/Program.cs`
  - 将新回归测试接入唯一测试入口。

## 三、编译与自测状态

| 门禁 | 命令/结果 |
| :--- | :--- |
| 主项目 Release | `dotnet build SteamP2PFriends.csproj -c Release --no-restore`；0 errors / 0 warnings |
| 测试项目 Release | `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release --no-restore`；0 errors / 0 warnings |
| 完整测试 | `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe`；`205/205 PASS`，Failed: 0 |
| Evidence Class | `Tools/Verify-EvidenceClassLayout.ps1`；`PureMemory 30 / StaticIL 13 / BuildArtifact 1 / Runtime 1`，PASS |
| DLL 独立核验 | `Tools/Verify-BuildFingerprintArtifact.ps1 -Path bin/Release/SteamP2PFriends.dll`；PASS |
| 差异检查 | `git diff --check`；PASS |

## 四、本次 Release DLL 指纹

| 字段 | 值 |
| :--- | :--- |
| Version / AssemblyVersion / FileVersion | `0.2.4.8` |
| MVID | `7eea6336-2222-4b6c-b4ff-0f0a4718f493` |
| DLL SHA-256 | `6ED2E77F4CBDBA35BDAC17ADF53DEA61A115018D897374549FFFD292C4371FC9` |
| Plugin GUID | `com.yu80rice.steamp2pfriends` |
| Default Case-ID | `SPF-0.2.4.8-Experimental-StructureBaseline` |
| Metadata source | `Build/Version.props` |

以上是当前 `bin/Release/SteamP2PFriends.dll` 的独立产物核验值；它尚未与本次修复后的 Host/Guest 新运行日志关联。

## 五、独立审核记录

| 审核项 | 判定 | 说明 |
| :--- | :--- | :--- |
| Spec 轴 | 未通过门禁 | 独立审核代理在限定等待时间内未返回结构化结论，已关闭；超时不解释为 PASS |
| Standards 轴 | 未通过门禁 | 独立审核代理在限定等待时间内未返回结构化结论，已关闭；超时不解释为 PASS |
| 自动化测试 | PASS | 205/205 PASS |
| Release 构建 | PASS | 主项目与测试项目 0 errors / 0 warnings |

## 六、最终结论

本次代码修复已完成并提交于当前分支提交 `33ee21f`，但 Ticket 11 **不得关闭**：

1. 本次修复后的 DLL 尚未部署到 Host、Guest A、Guest B 并重新采集共享 Case-ID 运行日志；
2. Resource 碰撞、采伐、LeaseRelease 的 2 秒滞回、重入、Connection Generation、Region Generation、快照和增量复制尚未取得新 DLL 绑定的 Runtime 证据；
3. 独立 Spec/Standards 审核未形成 PASS。

下一步必须使用上述新 DLL 和新的共享 Case-ID，重复既有 1 Host + 2 Guest 流程并提交三端诊断包；不得沿用旧 DLL 的 Runtime 结果。
