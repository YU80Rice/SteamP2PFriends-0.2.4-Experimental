# M2 架构实现报告 - SteamP2PFriends 0.2.4.2

## 一、需求执行概述

依照 Multi-Observer 蓝图完成 M2 `ItemObserverReplicationAdapter` 构建：listen-host 的地图物品初始快照仍只使用 U3 原生 `askItems`，新增逐观察者、逐连接代、逐区域、逐世界代的可靠入队基线账本；不接管拾取移除、玩家掉落或僵尸掉落增量 RPC。

本报告结论仅为“构建与静态审核通过”。`0.2.4.2` 尚未完成新哈希双端/多观察者运行验收，因此未创建 `Develop-Stage/SteamP2PFriends-0.2.4.2`，版本也必须在所有 M2 返修中保持不变。

## 二、源码溯源清单

| 蓝图条目 | 实现位置 | 契约 |
|---|---|---|
| 逐观察者基线账本 | `MultiObserver/ItemObserverReplicationAdapter.cs:84` | observer + connection generation + region + session/world generation |
| capability 明示 | `MultiObserver/ItemObserverReplicationAdapter.cs:272` | 固定 `ReliableEnqueueBaseline` |
| 相关性退出/重入 | `MultiObserver/ItemObserverReplicationAdapter.cs:131` | 只释放离开 `ITEM_REGIONS` 的 committed/preparing baseline |
| 原生发送门接管 | `Patches/ItemManagerRegionSyncPatch.cs:430-516` | 仅替换原 `Dedicator.IsDedicatedServer` 门，仍调用原生 `askItems` |
| 基线事务 | `Patches/ItemManagerRegionSyncPatch.cs:538-603` | Prefix prepare、Postfix reliable-return commit、Finalizer abort |
| loaded 兼容投影 | `MultiObserver/ItemObserverReplicationAdapter.cs:418-452,541-548` | 非正常返回只对当前连接 token 清 `isItemsLoaded`，允许下一 step 5 重试 |
| 权威生成代读取 | `Patches/AuthoritativeItemGenerationGatePatch.cs:314` | 只读公开 committed session/region generation，不引入第二 writer |
| 远端断线失效 | `SteamP2PFriendsPlugin.cs:357-388`、`Patches/ItemManagerRegionSyncPatch.cs:651` | `onEnemyDisconnected` 按 SteamID 精确删除 observer 与 connection binding |
| SP/U3DS 旁路 | `MultiObserver/ItemObserverReplicationAdapter.cs:304-312,360-368` | U3DS 原生 true；非 P2P host/internal caller 原生放行 |
| fail-closed 注册门 | `SteamP2PFriendsPlugin.cs:2001-2027` | relevance Prefix 和 askItems Prefix/Postfix/Finalizer 缺一即 INVALID |

## 三、代码变更清单

- 新增 `MultiObserver/ItemObserverReplicationAdapter.cs`。
- 新增 `WhitelistTests/ItemObserverReplicationAdapterTests.cs`，共 11 项 M2 专项测试。
- 修改 `Patches/ItemManagerRegionSyncPatch.cs`：M2 发送门、相关性 Prefix、askItems 三阶段事务、断线清理与精确 Harmony owner 自检。
- 修改 `Patches/AuthoritativeItemGenerationGatePatch.cs`：增加 committed generation 只读桥。
- 修改 `SteamP2PFriendsPlugin.cs`：M2 启动门禁及远端断线事件转发。
- 修改项目编译清单、测试入口、版本元数据、README、CHANGELOG 和实验架构说明。
- 未修改相邻稳定工作区 `SteamP2PFriends`；其 AssemblyVersion 仍为 `0.2.3.70`。
- M1 冻结归档 Debug DLL 仍为 `1F02A37DF9F944A9AD33B3CD32283ABF2AFB697934E5C0A0DE532E2813CF509A`。

## 四、核心代码变更对比

```diff
- if (Dedicator.IsDedicatedServer)
+ if (ItemObserverReplicationAdapter.ShouldReplicateForObserver(player))
  {
      askItems(player.channel.owner.transportConnection, x, y, sortOrder);
  }
```

```diff
- askItems Prefix 仅记录诊断日志
+ Prefix: 为 observer/connection/region/generation 准备 baseline transaction
+ Postfix: 原生 askItems 正常返回后提交 ReliableEnqueueBaseline
+ Finalizer: 异常时 Abort，并仅对当前 connection token 清 loaded 兼容位
```

## 五、编译与自动化验证

使用：`C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe`

| 验证项 | 命令/结果 |
|---|---|
| Debug Rebuild | `MSBuild SteamP2PFriends.csproj /t:Rebuild /p:Configuration=Debug`；0 errors / 0 warnings |
| Release Rebuild | `MSBuild SteamP2PFriends.csproj /t:Rebuild /p:Configuration=Release`；0 errors / 0 warnings |
| Tests Rebuild | `MSBuild WhitelistTests/SteamP2PFriends.WhitelistTests.csproj /t:Rebuild /p:Configuration=Release`；0 errors / 0 warnings |
| Tests Run | `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe`；`92/92 PASS` |

M2 专项覆盖：可靠返回单次提交、异常 Abort 后重试、离区重入、重连代失效、同区双 observer、异区隔离、新世界代、显式 capability、当前 U3 IL 发送门、loaded token 防旧连接回写、精确断线后连接对象复用。

最终产物：

- Debug DLL SHA-256：`3A83295E685EAD79BDA64A8816FCBDF821C1CBDFE0B9DE37E0692E2EEE483ED3`
- Release DLL SHA-256：`8B0B169EB6FA26BFF0CA9E161D97E17EBE1EA1C8D0D96C80EF56DA65A3C9C926`
- Test EXE SHA-256：`8DE4CBD8EA14ABC21B23A0DEF5A4C59612E80CAA76F410E9FD2FBA41A3D60D6F`

## 六、子智能体独立审核记录

| 轮次 | 判定 | 发现与处理 |
|---|---|---|
| 1 | FAIL | P0-B1：异常/skip 只 Abort ledger，原生已置真的 loaded 位会阻止重试。P0-B2：host 远端断线未精确清 M2 observer。 |
| 返修 | PASS | B1 增加当前 connection token 校验后的 loaded rollback；B2 从 `onEnemyDisconnected` 按 SteamID 精确清理；新增 M2I10/M2I11，回归升至 92/92。 |

独立审核最终结论：**PASS（静态门禁）**。未发现剩余 Blocker；该 PASS 不替代运行验收。

## 七、M2 运行测试流程

### 7.1 前置条件

1. 所有参与端只部署本报告 Debug DLL，现场计算 SHA-256，必须全部等于 `3A83295E685EAD79BDA64A8816FCBDF821C1CBDFE0B9DE37E0692E2EEE483ED3`。
2. 删除或移走其他同 GUID 的 SteamP2PFriends DLL；保留本轮完整 `LogOutput.log`，不要混入旧日志。
3. 房主开启一个持续不重启的 listen-host 会话。标记三个相距超过 `ITEM_REGIONS` 的区域：A（房主驻留）、B、C；B/C 在测试前不得由房主进入。
4. 正式覆盖“双客机同区/异区”需要 1 个房主进程 + 2 个独立客机进程/Steam 账号。只有房主+单客机的双机测试可关闭主链路，但不能单独关闭多 observer 验收项。
5. 每一步记录 UTC/本地时间、角色、区域和可识别物品；测试结束由 UMM 导出所有端诊断包。

### 7.2 用例与通过标准

| Case | 操作 | 必须通过的现象与日志 |
|---|---|---|
| M2-R01 启动指纹 | 启动两/三端并加入 | 各端版本 `0.2.4.2`；host 有 M1/M2 registration verified、capability `ReliableEnqueueBaseline`；无 DIAGNOSTIC BUILD INVALID |
| M2-R02 待审核首次基线 | 房主先离开 B 到 A；客机首次进入并在待审核状态出生于 B | 待审核客机可看到 B 的同一批地图自然物品；host 顺序出现 M1 authority commit/skip、原生 askItems、M2 commit；审批状态不得成为复制条件 |
| M2-R03 审批不重生 | 记录 B 中一个明显物品，房主批准客机 | 审批前后物品种类、位置、实例保持一致；不得重新随机生成 |
| M2-R04 权威拾取移除 | 客机拾取该物品，房主随后进入 B | 客机拾取成功；房主到达后原位置无该物品；不得出现一端仍可拾取的幽灵实例 |
| M2-R05 离区重入 | 客机从 B 移至 C，确保 B 退出相关性，再返回 B | 返回后收到 B 当前快照；R04 已拾取物品仍不存在；host 对 B 出现新的 M2 baseline commit，但不得再次 `generateItems`/重生地图物品 |
| M2-R06 断线重连 | 不重启房主世界，客机断线后用同账号重连并回到 B | host 记录精确 `OnEnemyDisconnected` 清理；重连获得新的 connectionGeneration 和 B baseline；R04 物品仍不重生 |
| M2-R07 双客机同区 | 两名客机同时位于房主不在的 B | 两个 observer 各自有 M2 commit；权威区域只生成一代；任一客机拾取后另一客机与房主均同步移除 |
| M2-R08 双客机异区 | 客机 1 留在 B，客机 2 独自进入 C | B/C 分别向正确 observer 提交基线；移动一人不得清理另一人的 committed baseline 或物品状态 |
| M2-R09 增量旁路 | 已批准客机丢弃背包物品；再击杀僵尸产生掉落，另一端拾取 | 玩家/僵尸掉落正常可见、可拾取、权威移除；日志不得把这些增量误记成地图 `askItems` baseline |
| M2-R10 房主本地回归 | 房主在 A 生成、拾取地图物品并使用背包 | 房主本地懒加载、拾取、背包操作正常；M2 不应为本地 observer 建账或阻断 |

若运行中自然出现 `[MultiObserver/M2-Item] reject ... askItems-exception` 或 `__runOriginal=false` 对应诊断，则必须继续停留该区域，验证下一轮 step 5 能重新出现 askItems/M2 commit。不要为制造该分支而注入会破坏游戏状态的运行时故障；其确定性契约已由 M2I02/M2I10 静态自动化覆盖。

### 7.3 运行验收 FAIL 条件

- 任一端 DLL 哈希不一致或无法证明。
- 客机独处 B/C 时无物品、物品与房主后来看到的不一致，或审批导致重生。
- 已拾取地图物品在离区重入/重连后重生。
- 两个 observer 互相覆盖 loaded/baseline 状态。
- 玩家/僵尸掉落增量被吞、重复或被错误归入地图 baseline。
- 出现 M2 reject/abort 后同区域永久不再调用 askItems。
- 任一 `DIAGNOSTIC BUILD INVALID`、Harmony owner/签名门失败或未处理异常。

## 八、偏离与妥协说明

无架构偏离。当前 `AuthoritativeItemGenerationGatePatch` 在 M1 没有会话中途区域重建，因此 session epoch 暂时同时作为 authoritative region generation；未来引入 mid-session rebuild 时必须拆分独立 region generation，不能沿用该简化。

## 九、最终结论

`SteamP2PFriends 0.2.4.2` M2 Debug 诊断候选已完成代码落地、编译闭环、`92/92` 自动化与独立静态审核 PASS，可移交上述运行流程。阶段状态仍为“等待运行验收”，不得归档或进入 M3。
