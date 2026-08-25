# SteamP2PFriends M3 实施报告 - 0.2.4.3

## 需求执行概述

依据 `ArchitectureMigration-WorldSync-MultiObserver-0.2.3.69-beta.2-1725.md` 实施 M3 `ZombieRegionLifecycleAdapter`。listen-host 以原生 `PlayerCountInRegion` 为首选功能需求事实源，复用原生 `generateZombies` 和发送链路，不新增 Zombie RPC；M0 ObserverSet 只负责交叉核对和隔离。

本轮只修改 `SteamP2PFriends-0.2.4-Experimental`。稳定工作区 `SteamP2PFriends` 未修改，版本固定为 `0.2.4.3`，未创建 `Develop-Stage/SteamP2PFriends-0.2.4.3`，未进入 M4。

## 源码溯源清单

| 蓝图要求 | 实现位置 |
| --- | --- |
| M3 单一僵尸区域生命周期 owner | `MultiObserver/ZombieRegionLifecycleAdapter.cs` |
| 原生需求优先、ObserverSet 对账 | `ZombieRegionLifecycleAdapter.ReconcileNativeRegions`、`ObserveDemand` |
| 首位远端观察者触发原生生成 | `ZombieRegionLifecycleAdapter.TryAcquireForRemote`、`RecoverNativeAcquire` |
| 最后观察者离区后 2 秒滞回释放 | `ZombieReleaseLease`、`ScheduleRelease`、`Tick` |
| Release 前验证会话代、区域代、区域身份和需求 | `ZombieRegionLifecycleLedger.CanCommitRelease`、`ZombieRegionLifecycleAdapter.Tick` |
| mismatch 隔离禁止破坏性释放 | `CompareDemand`、`IsQuarantined`、`Tick`、`ReconcileNativeRegions` |
| 生成失败/manager 缺失不伪提交 | `RecoverNativeAcquire` 返回值和 `ReconcileNativeRegions` fail-closed 分支 |
| 未激活零需求 bound 不建代、不释放 | `ReconcileNativeRegions` 的 `!region.isNetworked` 门禁 |
| P0-D/P0-E 降为兼容入口 | `Patches/ZombieManagerP0DGenerateZombiesPatch.cs`、`Patches/P0EZombieLifecycle/ZombieLifecyclePatch.cs` |
| 旧状态写入可恢复/回滚 | `Patches/P0EZombieLifecycle/ZombieLifecycleState.cs`、Adapter transition 方法 |
| 注册、Tick、会话重置 | `MultiObserver/MultiObserverShadowCoordinator.cs`、`SteamP2FriendsPlugin.cs` |

## 代码变更清单

- 新增 `MultiObserver/ZombieRegionLifecycleAdapter.cs`。
- 新增 `WhitelistTests/ZombieRegionLifecycleAdapterTests.cs`。
- 修改 P0-D/P0-E 僵尸生命周期补丁与状态载体，使 M3 ready 后委托 Adapter。
- 修改插件注册、主循环和项目文件，接入 M3。
- 修改版本及实验架构文档、README、CHANGELOG 为 `0.2.4.3` M3。
- 新增 M3Z01-M3Z12 生命周期账本回归；完整回归计数为 `104/104 PASS`。

## 编译验证记录

构建工具：Visual Studio Insiders MSBuild 18.9.1；目标框架 .NET Framework 4.7.2。

```powershell
& 'C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe' 'SteamP2PFriends.csproj' /t:Rebuild /p:Configuration=Debug /m /v:minimal
& 'C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe' 'SteamP2PFriends.csproj' /t:Rebuild /p:Configuration=Release /m /v:minimal
& 'C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe' 'WhitelistTests\SteamP2PFriends.WhitelistTests.csproj' /t:Rebuild /p:Configuration=Release /m /v:minimal
.\WhitelistTests\bin\Release\SteamP2PFriends.WhitelistTests.exe
```

结果：插件 Debug、插件 Release、测试 Release 均为 0 errors / 0 warnings；自动化测试 `104/104 PASS`。

最终产物：

| 配置 | 路径 | SHA-256 |
| --- | --- | --- |
| Debug | `bin/Debug/SteamP2PFriends.dll` | `722EFBFCB292334824E98519CAC6F2E102C0BBDCC1AD954F7251BE870A062C1A` |
| Release | `bin/Release/SteamP2PFriends.dll` | `898C2F439BE8121F2E6EBA1647E382A14405B703E44D285B64F00B17D6D28A95` |

注意：`WhitelistTests/bin/Debug` 是旧测试二进制，不作为本轮证据。本轮测试证据绑定 `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe`。

## 子智能体独立审核记录

| 轮次 | 判定 | 阻断及处理 |
| --- | --- | --- |
| 1 | FAIL | native-demand 恢复失败后仍可能伪提交 generation；改为 bool 成功契约，失败 continue。 |
| 2 | FAIL | `ZombieManager.instance == null` 边界仍可能伪提交；统一 `!isNetworked` fail-closed。 |
| 3 | FAIL | 零需求、未激活空 bound 会被登记并释放；新增未激活门禁。 |
| 4 | FAIL | quarantine 仅记录、不阻止 destroy；升级为调度和提交双重硬门禁。 |
| 5 | PASS | 阻断项 0；线程、代数、身份、隔离、P0-D/P0-E 委托均通过静态复核。 |

## 偏离与妥协说明

无架构偏离。M3 没有复制 U3DS 全图常驻模型，没有新增 Zombie RPC，也没有让 ObserverSet 直接重写原生计数。运行证据尚未完成，因此本产物仅是 M3 双机测试候选，不是阶段验收归档版。

## M3 双机测试流程

1. 双端从同一目录复制 Debug DLL，记录完全相同的 SHA-256；启动同一 Case ID 的房主与客机日志。
2. 房主单独加载地图，确认原生僵尸生成、战斗无异常，并记录启动注册日志。
3. 房主留在 A 区，客机单独进入房主从未进入的 B 区；确认出现 `M3-Zombie acquire-commit`，客机收到非空且可交互的僵尸。
4. 房主继续远离 B 区，客机留在 B 区战斗；确认无 `release-commit`、无销毁、无重刷，攻击、仇恨、击杀和掉落正常。
5. 第二客机进入 B 区；确认需求增加，但实体 ID、数量和 generation 不因第二观察者重建。
6. 第一客机离开、第二客机留在 B 区；确认区域不释放。
7. 最后一名客机离开；等待至少 2 秒，确认只发生一次 `release-commit`。
8. 在战斗中跨区域边界，并执行客机断线重连；确认只清理对应 observer，不影响其他玩家，旧 session/generation 不提交。
9. 房主返回 B 区；确认不产生重复实体代、不重刷已击杀僵尸，掉落状态保持一致。
10. 补测单机和 U3DS：应保持原生分支，不出现 M3 Adapter writer 日志。
11. 用 UMM 导出 Host/Client 完整诊断包，保留 Case ID、角色、时间窗、DLL 路径和 SHA-256；不要只提交摘要。

## 门禁结论

| 门 | 结论 |
| --- | --- |
| 源码实现 | PASS |
| Debug/Release 编译 | PASS，0 errors / 0 warnings |
| 自动化回归 | PASS，104/104 |
| 独立静态审核 | PASS，阻断项 0 |
| 双端部署一致性 | PENDING |
| listen-host 双机运行验收 | PENDING |
| 单机/U3DS 非回归 | PENDING |
| M3 阶段归档 | BLOCKED，等待本版本本哈希运行证据 |

最终结论：`0.2.4.3` M3 构建候选已完成，可移交运行验证；不得据此声明 M3 已验收完成或进入 M4。
