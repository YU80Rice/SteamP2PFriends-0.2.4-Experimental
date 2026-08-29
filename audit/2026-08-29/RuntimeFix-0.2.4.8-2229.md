# Ticket 11 RuntimeFix 交付报告

## 一、问题定位与修复策略

本次为蓝屏中断后的 Ticket 11 续接。目标是修复前置独立审核发现的 Resource 事务、generation、注册闭合、连接失败清理和可观测性阻断；不进行实机动态测试，不关闭 Ticket 11。

根因与修复：

1. Resource seam 在 `AdvanceTime`、Acquire 后、最后观察者退出和 `Flush` 中未统一拒绝低于已记录值的 generation；现已保留旧 lease/pending 状态并记录 rejected/state-retained。
2. `EndSession` 外部清理异常可能留下半结束状态；现已逐项捕获 lifecycle/replication 异常，仍清空托管状态、退出 active，并锁存 `RepairRequired`。
3. ResourceDomainAdapter 曾在清理前打印 SessionEnd success；现已改为所有清理完成后才打印 success，失败逐项输出失败证据。
4. Harvest 四 Hook 曾缺少同 owner/patch method 的唯一闭合与幂等证明；现已增加注册前核验、精确唯一校验、幂等跳过、冲突 fail-closed 和回滚核验。
5. 客机 Resource connection generation 溢出后曾只改变状态；现已在已连接时进入受控 `RequestDisconnect`。
6. Delta 日志曾使用累计拒绝计数判断当前调用；现已改用本次调用前后计数差。

## 二、核心代码变更

涉及 Resource 生产接缝、领域适配器、快照/复制日志、Harvest 注册和连接清理：

- `Adapters/Resource/ResourceProductionControlSeam.cs`
- `Adapters/Resource/ResourceDomainAdapter.cs`
- `Adapters/Resource/ResourceSnapshotAdapter.cs`
- `Adapters/Resource/ResourceRegionLifecycleAdapter.cs`
- `Adapters/Resource/ResourceObservability.cs`
- `Adapters/Resource/Patches/ResourceManagerHarvestReplicationPatch.cs`
- `Adapters/Resource/Patches/ResourceManagerRegionSyncPatch.cs`
- `Adapters/Resource/Patches/ResourceManagerWorldSyncDiagnosticPatch.cs`
- `Core/ControlPlane/MultiObserverShadowCoordinator.cs`
- `Platform/Client/P2PJoinManager.cs`
- 与上述接缝对应的 PureMemory、StaticIL、BuildArtifact 测试及唯一测试入口。

未新增并行 Resource Authority Writer；原生 ResourceManager 数据面与协议职责、P2P 频道、SteamID、Harmony target/order、插件 GUID、配置键和历史归档版本保持不变。

## 三、TDD 与验证状态

### 3.1 新增回归接缝

- `M6P24`：AdvanceTime 遇 generation 回退保留 lease。
- `M6P25`：Flush 遇 generation 回退保留 pending release。
- `M6P26/M6P27`：lifecycle/replication 清理异常后 EndSession 仍清状态。
- `M6P28`：Acquire 后 generation 回退 fail-closed 并补偿。
- `M6P29`：最后观察者退出遇 generation 回退保留已存 lease generation。
- `M6P30`：RepairRequired 锁存后拒绝新会话。
- `M6O10`：Harvest 注册幂等且四 Hook 不重复。
- `M6O11`：连接 generation 溢出触发受控断开。
- `M6O12-M6O14`：会话清理 finally、SessionEnd 日志时序和 Delta 单次拒绝判定。

### 3.2 构建与测试

| 项目 | 结果 |
|---|---|
| 主项目 Release | PASS，0 errors / 0 warnings |
| 测试项目 Release | PASS，0 errors / 0 warnings |
| 唯一测试入口 | PASS，`255/255`，Failed: 0 |
| Evidence Class 布局 | PASS：PureMemory 31 / StaticIL 14 / BuildArtifact 1 / Runtime 1 |
| `git diff --check` | PASS，退出码 0；仅有 LF/CRLF 转换提示 |
| Runtime | PENDING，未执行动态测试 |

构建命令：

```text
dotnet msbuild .\SteamP2PFriends.csproj /t:Rebuild /p:Configuration=Release /v:minimal
dotnet msbuild .\WhitelistTests\SteamP2PFriends.WhitelistTests.csproj /t:Rebuild /p:Configuration=Release /v:minimal
.\WhitelistTests\bin\Release\SteamP2PFriends.WhitelistTests.exe
```

## 四、BuildArtifact 指纹

- Version：`0.2.4.8`
- AssemblyVersion：`0.2.4.8`
- FileVersion：`0.2.4.8`
- MVID：`8204775d-9fad-44a9-88b4-eca906422048`
- DLL SHA-256：`D0CEE669717C5B31FAA7C2F29AADB1104E8A8BC630027542AE77EBC9C35A049A`
- Plugin GUID：`com.yu80rice.steamp2pfriends`
- Shared Case-ID：`SPF-0.2.4.8-Experimental-StructureBaseline`
- Independent artifact verification：PASS

本报告中的上述指纹属于当前 Release DLL；后续动态测试必须重新提交同一 Case-ID 且绑定这一当次 DLL 的 Host/Guest 诊断包。用户无需手工计算 hash，但审计仍需独立重算并核对 MVID、版本和 GUID。

## 五、独立审核记录

| 轮次 | 结论 | 主要阻断 |
|---|---|---|
| 第 1 轮 | FAIL | Runtime Pending；连接溢出清理、Harvest 唯一性、generation 回退、EndSession 清理 |
| 第 2 轮 | FAIL | Runtime Pending；DomainAdapter 日志时序、Acquire/退出 generation、Delta 累计计数误报、RepairRequired 重开 |
| 第 3 轮 | PASS（静态代码审核） | 无阻断；确认上述修复、唯一 Authority Writer 和证据分类边界 |

每轮审核均使用全新独立实例；每次收到完整 PASS/FAIL 后立即关闭实例。审核期间源码、配置、构建产物和报告保持冻结。

## 六、最终结论

本轮代码修复、Release 构建、完整自动化测试、静态 Evidence Class 门禁、BuildArtifact 独立核验和独立静态审核均通过。

Ticket 11 **仍未关闭**：Runtime Gate 保持 `PENDING`。尚缺共享 Case-ID 绑定当前 DLL 的 1 Host + 2 Guest 动态证据，必须覆盖多观察者、远区碰撞/采伐、租约释放与 2 秒滞回、重新进入、Guest 重连、generation 防护、快照/增量复制及 Host/Guest 日志关联。取得完整证据并经 `diagnosing-bugs` 分析通过后，才可推进关闭票据。

提交记录：本报告与本轮相关变更将在当前分支提交；其他未跟踪用户资料不纳入提交。
