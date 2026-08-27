# Ticket 11：Resource 旧 Authority Writer 退出与 Runtime Gate 交付审计报告

## 一、结论

**判定：FAIL / implemented-pending-runtime。**

Ticket 11 的静态退休接缝已实现并通过构建、完整自动化测试、Evidence Class 布局和独立 DLL 产物核验；但独立审核未形成 PASS，且规格要求的真实 Runtime 证据缺失。因此不得关闭 Ticket 11，不得将 Resource Migration Slice 标记完成，也不得据此推进其他领域迁移。

## 二、需求与源码溯源

| 需求 | 当前证据 | 结论 |
| --- | --- | --- |
| Migration Manifest 记录旧 Authority Writer 退出/禁用/删除 | `docs/architecture/migration-manifest.md` Batch 11；`Adapters/Resource/ResourceRegionLifecycleAdapter.cs` 已删除 `OnObserverRelease` | 静态确认 |
| 旧释放入口不再由 Resource 生产调用 | `WhitelistTests/Evidence/StaticIL/ResourceAuthorityRetirementStaticILContractTests.cs`；`ResourceDomainAdapter.OnRelease` 唯一调用 `CommitRelease` | StaticIL PASS |
| 原生 `ResourceManager` writer 不再独立承担权威 | U3-SDK 原生数据/协议执行器仍保留；当前无 Runtime 因果证据 | 未确认，阻断 |
| Host、Guest、多观察者、重连、离开/重新进入共享 Case-ID Runtime 证据 | 当前仓库未提供本次 `0.2.4.8` 的成对原始运行日志 | 未确认，阻断 |
| Resource 碰撞、采伐、租约释放、generation、双端复制 Runtime Gate | 当前只有 PureMemory/StaticIL/BuildArtifact 证据 | 未确认，阻断 |
| Host/Guest 日志与当次 DLL 的 SHA-256、MVID、版本、GUID 关联 | 当前只有构建级独立产物核验，没有对应 Host/Guest Runtime 日志 | 未确认，阻断 |

本轮没有修改 P2P 通道、SteamID、配置键、插件 GUID、Harmony target、注册顺序、原生网络协议或已归档版本。

## 三、代码变更

1. `Adapters/Resource/ResourceRegionLifecycleAdapter.cs`
   - 删除旧的 `OnObserverRelease` 静态入口。
   - 保留 `ScheduleRelease`、`TryCommitRelease` 和带 session/generation 校验的 `CommitRelease`。
2. `WhitelistTests/Evidence/StaticIL/ResourceAuthorityRetirementStaticILContractTests.cs`
   - 新增静态契约，断言旧入口不存在、`ResourceDomainAdapter.OnRelease` 恰好调用一次 `CommitRelease`，并且生产接缝类型唯一。
3. `WhitelistTests/Program.cs`
   - 将 Ticket 11 StaticIL 契约接入唯一测试入口。
4. `docs/architecture/migration-manifest.md`
   - 记录旧租约释放路径删除、当前接缝、原生 `ResourceManager` 保留边界与 Runtime Pending。
5. `.scratch/structure-baseline-0-2-4-8/issues/11-resource-authority-retirement-runtime.md`
   - 状态更新为 `implemented-pending-runtime`，补齐本轮证据和阻断结论。

## 四、构建、测试与产物核验

| 门禁 | 命令/结果 |
| --- | --- |
| 主项目 Release | `dotnet build SteamP2PFriends.csproj -c Release --no-restore`；0 errors / 0 warnings |
| 测试项目 Release | `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release --no-restore`；0 errors / 0 warnings |
| 完整测试 | `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe`；`204/204 PASS`，Failed: 0 |
| Evidence Class 布局 | `Tools/Verify-EvidenceClassLayout.ps1`；`PureMemory 30 / StaticIL 13 / BuildArtifact 1 / Runtime 1`，`EVIDENCE_CLASS_LAYOUT_PASS` |
| 独立 DLL 核验 | `Tools/Verify-BuildFingerprintArtifact.ps1 -Path bin/Release/SteamP2PFriends.dll`；`INDEPENDENT_ARTIFACT_VERIFICATION_PASS` |
| 差异检查 | `git diff --check`；通过（仅有 Git 的 LF/CRLF 转换提示） |

### 当前插件 DLL 独立指纹

| 字段 | 值 |
| --- | --- |
| Version / AssemblyVersion / FileVersion | `0.2.4.8` |
| MVID | `87329b03-8e73-4219-b88b-7b3da2cfcc3e` |
| SHA-256 | `B1B63DD3B69E562509A3279E5D856060FFB9EC30564DD982EE30B325C4B8DE95` |
| Plugin GUID | `com.yu80rice.steamp2pfriends` |
| Default Case-ID | `SPF-0.2.4.8-Experimental-StructureBaseline` |
| Metadata source | `Build/Version.props` |

以上是验收侧对当前 Release DLL 的独立读取/重算结果，不是 Runtime 自报告，也不替代 Host/Guest 日志。

## 五、独立审核记录

本轮按 Spec/Standards 双轴启动两个只读子智能体。两者在限定等待时间内均未返回正式结论，随后均已关闭；未将超时或关闭解释为 PASS。

确定性阻断如下：

1. 缺少同一 Case-ID、同一当次 DLL 下的 Host、Guest、多观察者、重连及离开/重新进入原始运行日志。
2. 缺少 Resource 碰撞、采伐、2 秒滞回释放、generation 防护、快照/增量复制的双端 Runtime 因果证据。
3. 删除 `OnObserverRelease` 只能证明旧租约释放入口退出，不能单凭静态证据证明 U3-SDK `ResourceManager` 原生数据/协议 writer 已退出独立权威地位。

## 六、后续 Runtime Gate

使用本报告 Case-ID 或一次新的共享 Case-ID，在同一 DLL 下归档 Host 与每个 Guest 的完整日志，并用独立脚本关联 DLL。至少覆盖：

- Host 单独运行、单 Guest、多个观察者同区/跨区；
- 观察者离开后 2 秒释放、滞回期间重新进入；
- Guest 重连、Connection Generation、Resource Region Generation、Session Reset；
- 远端资源碰撞、采伐/重生、区域重建、初始快照、增量复制；
- 日志中的版本、FileVersion、MVID、SHA-256、插件 GUID 与 DLL 独立核验值一致。

在这些证据和独立审核 PASS 之前，Ticket 11 保持 `implemented-pending-runtime`，Resource Migration Slice 不关闭。
