# 0.2.4.0 Multi-Observer M0 实验架构实施报告

## 需求执行概述

在不修改 `SteamP2PFriends` 稳定区 `0.2.3.70-beta.2` 的前提下，创建独立的
`SteamP2PFriends-0.2.4-Experimental` 工作区，并落地第一阶段 M0 只读影子账本。
M0 只观察 listen-host 的多玩家区域需求与原生状态差异，不接管世界生成、加载位、RPC、
审核、物理、动画或 AI writer。

## 稳定区隔离证明

实验区建立时的完整基线见 `BASELINE-0.2.3.70.md`。本轮最终复核：

| 稳定构建 | SHA-256 | 结果 |
| --- | --- | --- |
| `SteamP2PFriends/bin/Debug/SteamP2PFriends.dll` | `A575A0F72DB8C1C1837223F03A3973F51043044D4B5BEFA7035C9AC7B5365E37` | 与冻结基线一致 |
| `SteamP2PFriends/bin/Release/SteamP2PFriends.dll` | `B8282D8111438FDA896334B9CE7A6F10015550A6A70F3365B84F553AAA17F95A` | 与冻结基线一致 |

稳定区未引入 `MultiObserver` 源码或引用；实验构建没有写入稳定区、`publish/`、UMM 或游戏插件目录。

## 源码溯源清单

| 需求 | 落实位置 | 契约 |
| --- | --- | --- |
| 独立实验身份 | `SteamP2PFriendsPlugin.cs:25`、`Properties/AssemblyInfo.cs:6-16` | BepInEx/Assembly 版本 `0.2.4.0`，程序集配置为 `MultiObserver-M0-Experimental` |
| 会话级多观察者账本 | `MultiObserver/MultiObserverShadowLedger.cs:193-449` | SessionEpoch、全局单调 connection/lifecycle、Item/Zombie demand 引用计数 |
| 待审核玩家仍是世界观察者 | `MultiObserver/MultiObserverShadowCoordinator.cs:299-325` | 授权只记录为 `GameplayAuthorized`，不影响 ObserverSet |
| 显式 Host Session 身份 | `MultiObserver/MultiObserverShadowCoordinator.cs:101-118`、`Host/HostManager.cs:1857-1949` | GUID 缺失或不一致时禁止 Begin/Reconcile；无 `missing-session` 降级 |
| 完整批次捕获 | `MultiObserver/MultiObserverShadowCoordinator.cs:242-344` | 先复制、全量验证、暂存令牌；任何无效记录整批拒绝且不修改账本/Connections |
| stale loaded 检测 | `MultiObserver/MultiObserverShadowCoordinator.cs:447-477` | 扫描完整 `loadedRegions`，分离 desired-loaded 与 desired 范围外 stale-loaded |
| shutdown/fault 防御 | `MultiObserver/MultiObserverShadowLedger.cs:7-57`、`MultiObserver/MultiObserverShadowCoordinator.cs:71-238` | 原子 latch、消费后立即返回、主线程状态变更、故障退避和日志配额 |
| 状态有界 | `MultiObserver/MultiObserverShadowLedger.cs:252-257` | 每批最多 64 observer；会话结束清理 observer/demand/connection/mismatch |
| M0 只读边界 | `MultiObserver/MultiObserverShadowCoordinator.cs` | 仅读取 Provider、loaded flag、审批状态和既有生成账本；无 RPC/loaded/生成/审批/Unity 对象写操作 |

## 代码变更清单

- 新增 `MultiObserver/MultiObserverShadowLedger.cs`。
- 新增 `MultiObserver/MultiObserverShadowCoordinator.cs`。
- 新增 `WhitelistTests/MultiObserverShadowTests.cs`，登记 M01-M13。
- 新增 `BASELINE-0.2.3.70.md` 与 `EXPERIMENTAL-ARCHITECTURE.md`。
- 修改 `SteamP2PFriendsPlugin.cs`：实验版本、配置、初始化、Tick 与 Shutdown 接线。
- 修改 `Host/HostManager.cs`：Stage6A Session GUID 生命周期通知，通知与失败日志双层隔离。
- 修改 `Patches/AuthoritativeItemGenerationGatePatch.cs`：仅暴露只读 shadow 查询。
- 修改两个 `.csproj`、`Properties/AssemblyInfo.cs`、`README.md`、`CHANGELOG.md`。

## 编译验证记录

工具：`C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe`

| 目标 | 命令要点 | 结果 |
| --- | --- | --- |
| 插件 Debug | `SteamP2PFriends.csproj /t:Rebuild /p:Configuration=Debug` | PASS，0 errors / 0 warnings |
| 插件 Release | `SteamP2PFriends.csproj /t:Rebuild /p:Configuration=Release` | PASS，0 errors / 0 warnings |
| 测试 Release | `WhitelistTests/SteamP2PFriends.WhitelistTests.csproj /t:Rebuild /p:Configuration=Release` | PASS，0 errors / 0 warnings |
| 自动化 | `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `74/74 PASS` |

实验构建指纹：

| 构建 | 长度 | SHA-256 |
| --- | ---: | --- |
| Debug DLL | 989696 | `582D9A4FBF6D5C1CDD03184DF583673F121C8CE00C945701C57E18AADB518B78` |
| Release DLL | 921088 | `642462E92E0E80619A4D07A03A45FD77FF921C1E85515839719F335ABCFFB979` |

## 子智能体独立审核记录

| 轮次 | 判定 | 阻断项与处理 |
| --- | --- | --- |
| 1 | FAIL | 同地图重开 SessionEpoch、Provider 不完整捕获、生命周期序列、off-thread shutdown、故障刷屏；加入 Stage6A GUID、capture complete 门、会话级序列、shutdown request 与 fault backoff |
| 2 | FAIL | shutdown 同 Tick 重启、`missing-session`、范围外 stale loaded、部分 Upsert/按 SteamID tombstone；改为 latch、强 Session ID 门、全矩阵扫描、整批事务与会话级单调序列 |
| 3 | PASS | 无阻断项；确认只读边界、状态有界、主线程契约、稳定区隔离，独立重跑 `74/74 PASS` |

非阻断观察：M1 前应补 Coordinator 级注入测试；双机/压力诊断应记录完整 `loadedRegions`
扫描耗时；正式 writer 身份应将 uint 序列升级为更强的 epoch 组合身份。程序集配置属性已在
`Properties/AssemblyInfo.cs` 无条件声明，因此 Debug/Release 均继承实验标识。

## 偏离与妥协说明

无需求偏离。当前有意停留在 M0 shadow：没有提前实施 M1，也没有把历史 P0-B/P0-D/P0-C-1/P0-E
writer 替换为新架构。

## 运行证据边界与测试建议

本报告只证明源码、编译、自动化与独立静态审核 PASS，**不证明 0.2.4.0 双机运行 PASS**。
旧 `0.2.3.70-beta.2` 的任何双机结论不得继承到本 DLL 哈希。

下一门禁使用相同 Case ID 归档 Host/Client 两端日志，并绑定本报告 DLL 哈希，验证：

1. 待审核、已授权和房主均进入 ObserverSet，授权变化不改变 world presence。
2. 房主与客机分区后，Item desired/stale、authority committed 与 Zombie demand 日志符合预期。
3. 客机断开、重连和同地图重开产生正确 connection generation / SessionEpoch，且无残留 demand。
4. 记录 M0 Tick/全 `loadedRegions` 扫描耗时，检查 listen-host 帧尖峰。
5. M0 证据通过且用户另行授权后，才进入 M1 `ItemGenerationAuthorityAdapter`。

## 最终结论

`0.2.4.0` 独立实验区的 M0 只读架构已经完成编译与静态门禁，可移交双机诊断；
`0.2.3.70-beta.2` 稳定构建保持不变，M1 尚未授权。
