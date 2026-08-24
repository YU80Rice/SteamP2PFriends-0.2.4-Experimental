# SteamP2PFriends 0.2.4.1 M1 实施报告

## 需求执行概述

在已验收并冻结的 M0 基础上实施 M1 `ItemGenerationAuthorityAdapter`：listen-host 不再在关卡加载时全地图补生成自然物品，而是在每个原生观察者的 `ItemManager.onRegionUpdated(step 5)` 需求范围内，同步调用原生 `generateItems`，并保证调用早于原生 `isItemsLoaded=true` 和 `askItems`。

本报告只证明 M1 源码、当前 U3 IL、编译、自动化和独立静态审核通过，不证明双机运行验收通过。

## M0 阶段归档

- 归档：`Develop-Stage/SteamP2PFriends-0.2.4.0`
- 文件数：260
- 冻结 M0 Release SHA-256：`9FF6D42166A46F1A73F8070DA96EB09C16944288043DDF69EC41E949D1FA69B0`
- 验收清单：`Develop-Stage/SteamP2PFriends-0.2.4.0/ACCEPTANCE-MANIFEST.md`
- 历史例外：归档规范名为 `0.2.4.0`，其中已验收 DLL 的原始元数据显示 `0.2.4.1`；未改写二进制或日志。

## 源码溯源清单

| 需求 | 实现位置 | 结果 |
| --- | --- | --- |
| 观察者需求驱动生成 | `MultiObserver/ItemGenerationAuthorityAdapter.cs:46-107` | 本地玩家保留原版；仅适格的 listen-host 远端观察者扩展生成资格 |
| 待审核玩家也是世界观察者 | `ItemGenerationAuthorityAdapter.cs:55-64` | 不读取审批状态；只验证原生远端非 loopback Player/transport |
| 生成早于 loaded/发送 | `Patches/ItemManagerRegionSyncPatch.cs:449-475` | 精确替换第二个 `IsLocalPlayer` 生成门，保留原生调用顺序 |
| 当前 U3 IL 精确匹配 | `WhitelistTests/ItemGenerationAuthorityAdapterTests.cs:41-66` | 对当前 `Assembly-CSharp.dll` 的原始 IL 断言 send=1、local calls=2、generation=1 |
| 单写者和异常重试 | `Patches/AuthoritativeItemGenerationGatePatch.cs:204-281`、`ItemGenerationAuthorityAdapter.cs:83-93` | 继续使用 Preparing/Commit/Abort；判定异常在 loaded commit 前抛出 |
| SP/U3DS 隔离 | `ItemGenerationAuthorityAdapter.cs:53-64,97-106` | SP local 保持 true；U3DS remote 保持 false，由原版全图生成负责 |
| 旧全图 writer 退役 | `SteamP2PFriends.csproj`、`SteamP2PFriendsPlugin.cs:1977-1999` | P0-B-3/P0-B-6 不进入 DLL，不登记、不调用 |
| 阶段版本冻结 | `Properties/AssemblyInfo.cs:6-16`、`EXPERIMENTAL-ARCHITECTURE.md:44-52` | M1 固定 `0.2.4.1`，返修不得变更版本 |

## 代码变更清单

- 新增 `MultiObserver/ItemGenerationAuthorityAdapter.cs`。
- 扩展 `Patches/ItemManagerRegionSyncPatch.cs` 的唯一 Transpiler，同时完成远端发送门和 M1 生成门，避免同 Harmony owner 重复 Transpiler。
- 从 `SteamP2PFriends.csproj` 移除 P0-B-3/P0-B-6 编译项；历史源码保留在工作区。
- 更新 `SteamP2PFriendsPlugin.cs` 的 M1 注册门、诊断门和启动指纹。
- 更新 `Host/HostManager.cs` 会话诊断配额重置，移除旧全图重生成重置依赖。
- 新增 6 项 M1 测试，并更新 README、CHANGELOG、AssemblyInfo 和实验架构说明。

## 编译验证记录

工具：`C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe`

| 构建 | 结果 | 耗时 |
| --- | --- | ---: |
| `SteamP2PFriends.csproj /t:Rebuild /p:Configuration=Debug` | 0 warnings / 0 errors | 1.17s |
| `SteamP2PFriends.csproj /t:Rebuild /p:Configuration=Release` | 0 warnings / 0 errors | 1.09s |
| `WhitelistTests.csproj /t:Rebuild /p:Configuration=Release` | 0 warnings / 0 errors | 0.92s |
| `SteamP2PFriends.WhitelistTests.exe` | 81/81 PASS | 3.90s |

最终候选指纹：

- Debug SHA-256：`1F02A37DF9F944A9AD33B3CD32283ABF2AFB697934E5C0A0DE532E2813CF509A`
- Release SHA-256：`4866DA40F019C3F535BF9127EB04FB209C34337CB829772035B8AD24789C6E08`
- Release 大小：912896 bytes
- Assembly/File version：`0.2.4.1`
- Release 类型检查：M1 adapter=true，P0-B-3=false，P0-B-6=false

## 子智能体审核记录

| 审核项 | 判定 | 说明 |
| --- | --- | --- |
| 需求符合性 | 通过 | 观察者需求驱动，待审核资格不参与世界快照判断 |
| IL 与栈平衡 | 通过 | `ldarg.1 + nop + call(Player)` 与原条件栈净变化一致；只命中第二个 local getter |
| 时序与状态一致性 | 通过 | 原生 generate 仍先于 loaded commit 和 askItems；单写者 gate 保留 |
| SP/U3DS 隔离 | 通过 | 未伪造 Dedicated，U3DS 不在逐玩家路径重复生成 |
| 失败语义 | 通过 | 判定异常有限日志后上抛，不能提交空 loaded 区域 |
| 旧 writer 退役 | 通过 | 工程 Include 为 0，最终 DLL 类型检查为 false |

最终判定：PASS。无阻断项。

非阻断建议中，配置说明已修正。注册失败后的显式单 patch 回滚暂不加入：当前冷启动中 `DiagnosticBuildValid=false` 会阻断全部 P2P 入口，热重载也由 `UnpatchSelf` 统一清理；在没有热重载需求前单独增加局部 Unpatch 会扩大注册状态机表面。

## 偏离与妥协说明

无需求偏离。M1 没有把每秒 M0 shadow Tick 作为生成触发器，而是复用原生 `onRegionUpdated(step 5)` 作为同步需求派发点；这是为了消除 shadow Tick 晚于 loaded commit/askItems 的确定性竞态，M0 账本继续承担观察与审计职责。

## 双机运行验收建议

1. 两端部署同一个 Debug DLL，记录 SHA-256 `1F02A37D...F509A` 和统一 Case ID。
2. 房主与客机在同一区域进入，验证地图自然物品仅生成一次、双方实例一致且拾取后权威移除。
3. 房主留在 A 区，待审核客机单独进入未访问 B 区，验证 B 区物品在审批前可见但交互仍受软隔离限制。
4. 审批客机后验证物品不重生、不换实例；客机拾取后双方一致。
5. 两个客机先后进入重叠区域，日志应显示 M1 allow，`ItemAuthorityGate` 对已提交区域 skip，不能二次生成。
6. 房主随后进入客机已访问区域，物品状态必须保持，不得由本地懒加载覆盖。
7. 收集两端完整 `LogOutput.log`，核对版本、DLL 哈希、M1 registration、generation replacement、gate commit/skip 和断开清理。

M1 只有在上述同哈希双机证据通过后，才能归档为 `Develop-Stage/SteamP2PFriends-0.2.4.1` 并进入 M2。
