# 0.2.4 Multi-Observer 实验区

## 隔离边界

- 稳定基线：`../SteamP2PFriends`，版本 `0.2.3.70-beta.2`。
- 实验区：当前目录，版本 `0.2.4.1`。
- 两个目录各自构建到自身 `bin/`，实验构建不写入稳定目录、`publish/`、UMM 或 Unturned 插件目录。
- 两个 DLL 使用相同 BepInEx GUID，因此不得同时部署。

## 阶段版本门禁

- 一个架构阶段从首次构建到静态、自动化和对应双机运行验收全部通过前，保持同一对外版本号；返修通过 DLL SHA-256、Case ID 和归档报告区分，不升级版本号。
- 只有当前阶段被明确验收后，才可以为下一个阶段分配新版本号。阶段内的编译成功、单元测试通过或单端日志均不构成升级理由。
- 已经部署并产生运行证据的旧候选不得事后改写版本标签；其版本、哈希和日志必须原样保留，以免破坏追溯链。

## 当前阶段：M0 Shadow

M0 只建立并对照账本：

- `WorldPresenceObserverSet`：已创建 `Player` 的房主、已授权客机和待审核客机。
- `SessionEpoch`：整个 listen-host 世界会话代。
- `ConnectionGeneration`：在当前世界会话内全局单调分配；一人重连只获得新的连接代，不推进 `SessionEpoch`。
- Item `PerObserverRelevance`：依原生 `ITEM_REGIONS` 计算，只读对照 `isItemsLoaded` 和旧生成 gate 的 committed 状态。
- Zombie `FunctionalRegionDemand`：仅对 `LevelNavigation.checkSafe(bound)` 的 observer bound 引用计数，只读对照原生 `PlayerCountInRegion`；无导航 bound 的 observer 仍保留在 ObserverSet。

M0 明确不做：

- 不替代 P0-B/P0-D/P0-C-1/P0-E writer。
- 不调用世界生成、销毁或发包方法。
- 不改写加载位、审核状态、物理、动画或 AI。
- 不因影子差异而阻止旧同步链路。

## 后续接管门

M1 前必须先用当前 DLL 哈希完成双机 M0 诊断，证明：

1. 待审核玩家与已授权玩家均稳定进入 ObserverSet。
2. 单客机断线只清理自身 demand，不污染其他 observer。
3. Item 生产状态与逐玩家 loaded 状态已被正确分离。
4. Zombie shadow demand 与原生 `PlayerCountInRegion` 在稳定帧一致。

只有这些证据成立后，才可授权 M1 `ItemGenerationAuthorityAdapter`。

## 当前构建阶段：M1 ItemGenerationAuthorityAdapter

- 版本固定为 `0.2.4.1`，直至 M1 双机运行验收通过；返修只以 DLL SHA-256、Case ID 和报告区分。
- listen-host 的地图自然物品生产改由 `ItemManager.onRegionUpdated(step 5)` 原生观察者需求循环触发；每个观察者仅覆盖 `ITEM_REGIONS` 范围。
- 同步 `generateItems` 必须发生在原生 `isItemsLoaded=true` 与 `askItems` 之前；既有 `AuthoritativeItemGenerationGatePatch` 继续提供区域级单写者事务。
- 待审核玩家已经拥有原生 `Player`，因此属于世界观察者；审批状态不参与物品世界快照资格判断。
- U3DS 继续使用原版 `onLevelLoaded` 全地图生成，单机和房主本地玩家继续使用原版懒加载，不伪造 `Dedicator.IsDedicatedServer`。
- `ItemManagerP0B3PreGeneratePatch.cs` 与 `ItemManagerP0B6RegenerateOnLevelLoadedPatch.cs` 保留为历史源码，但不进入 M1 编译清单，也不再登记或调用。
