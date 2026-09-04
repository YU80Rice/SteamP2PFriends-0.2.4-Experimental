# Ticket 11 取证轮裁决:H1 确认(native-resource-trees-unavailable)+ 根因链闭合

- **日期**:2026-09-04 10:46
- **分支**:`codex/structure-baseline-0.2.4`(HEAD `340918a` + 取证轮未提交变更)
- **票据**:`.scratch/structure-baseline-0-2-4-8/issues/11-resource-authority-retirement-runtime.md`(`implemented-pending-runtime`)
- **前置**:`audit/2026-09-03/Implementation-0.2.4.8-Ticket11-1123.md`(诊断 F1–F7、假设 H1–H4、取证埋点)
- **证据来源**:2026-09-04 用户手动动态测试双端诊断包(取证版 DLL,Host 单独 + 单 Guest 场景):
  - 主机:`…\UMM-v2.2.1-win-x64\UMM-诊断包_20260904_103321\LogOutput.log`(1.49 MB)
  - 虚拟机客机:`…\UMM-v2.2.1-win-x64\UMM-诊断包_20260904_103302\LogOutput.log`(355 KB)
- **DLL 指纹关联(双端一致)**:日志自报告 `mvid=524d3392-f5c3-4a12-92d0-b00aaa2bfb8a`、`dllSha256=145BEFAA49444979F7A012ADFF2375815C2DBCA960A50A5CAEA864AB14300ADD`、`caseId=SPF-0.2.4.8-Experimental-StructureBaseline`——与取证版构建记录(1123 报告 §4a)完全一致。

---

## 1. 裁决:H1 确认,H2/H3 排除

主机日志 **9 次** `event=LeaseAcquire … outcome=failed reason=region-snapshot-failed`,**全部** `exception=InvalidOperationException message=native-resource-trees-unavailable`,零次 `native-resource-tree-null`(H2 排除)、零次越界(H3 排除)。

| region | sessionEpoch | connectionGeneration | 行号 |
|---|---|---|---|
| (24,23) ×5 | 1/3/5/7/9 | 1–5 | 233/550/559/569/582 |
| (24,29) ×3 | 11/15/17 | 6/9/11 | 1104/3511/5140 |
| (34,36) ×1 | 13 | 7 | 2304 |

**成功对照(同日志)**:3 次 `event=LeaseAcquire … path=SPI outcome=success`(行 2298/2300/2302),region=(34,33)/(34,34)/(34,35),sessionEpoch=13——与失败行 (34,36) **同一会话**。

**用户实测现象(2026-09-04 口径修正 + 截图)**:
- 仅有主机时,主机进入区域可正常看到树木/矿石刷新;
- 客机与主机**同处一片区域**时,主机正常看到;
- 客机与主机**不在同一片区域**、或客机下线后,主机进入**新区域**看不到树木/矿石(截图证实:同位置仅剩草地石头)。

## 2. 根因链(源码 + U3-SDK 溯源,静态定位)

**抛出点**:`Adapters/Resource/ResourceRegionLifecycleAdapter.cs:477-479`(`CaptureNativeRegionState`):

```csharp
List<ResourceSpawnpoint> trees = LevelGround.GetTreesOrNullInRegion(regionKey.X, regionKey.Y);
if (trees == null)
    throw new InvalidOperationException("native-resource-trees-unavailable");
```

**原生 null 语义**(U3-SDK `Level/LevelGround.cs` 与 `Regions/RegionDictionary.cs`):
- `GetTreesOrNullInRegion` → `_regionTrees?.GetListOrNull(coord)`(`:169-177`);`GetListOrNull` 为纯字典查找,条目不存在返回 null(`RegionDictionary.cs:12-16`);
- 条目创建:仅两处 `GetOrAddList`——① `LevelGround.load` 读取 `Terrain/Trees.dat` 时为**内置树**建档(`:798-806`);② `addSpawn`(:424),含 **foliage 植被系统运行时渐进生成**(`Framework/Foliage/FoliageResourceInfoAsset.cs:85-99 addFoliage → LevelGround.addSpawn`,动态树);
- 条目销毁:`ReleaseListIfEmpty`——区域树被清空后条目被释放(`:411/485`)。

**结论**:「区域树条目不存在」是 Unturned 2025 版(foliage 重构后)的**常态时序状态**——主机(及任何观察者)尚未触达/烘焙的区域,`_regionTrees` 中没有该区域条目。SPI 的 `CaptureNativeRegionState` 在主机观察需求扩展到新区域的**瞬间**执行,抢在 foliage 烘焙之前 → null → throw。这不是数据损坏,是**快照时机与原生渐进生成机制的时序错配**。

**放大链(1123 报告 F4 的现行铁证,行 2298-2310 时间线)**:
1. 同批 Acquire:(34,33)/(34,34)/(34,35) 成功(SnapshotEnqueue)→ (34,36) 失败;
2. 整批回滚:`SnapshotRemove` 依次清除 (34,35)/(34,34)/(34,33) ——**1 个区域失败连带 3 个已成功区域全部丢弃**;
3. `ObserverUpdate` 失败 → `fault=1/16 attempt=7 retryAt=226.4 type=InvalidOperationException; legacy writers unchanged`;
4. session 重建:`session-end nextEpoch=… reason=fault-recovery` → epoch 奇数递增(1,3,5,…,17),与 `ShadowFaultBackoff` 退避(retryAt 132.3→226.4s)叠加;
5. SPI 全程无持久 lease → `IsRegionActive=false` → 碰撞补丁 covered=false;
6. 客机不在主机区域时覆盖变化 → `LevelObjectRemoteCollisionPatch.RefreshObjectsInRegion` 以 `IsRegionCovered`(只看远端覆盖,`LevelObjectRemoteCollisionPatch.cs:211-218`)判定 → **disable 主机区域的树**(`:598-602`);vanilla 脏区域模型不自愈(评审报告 §3.3)→ 主机持续看不到树;
7. 客机进入主机同区域时,覆盖/本地路径恢复 → 主机恢复正常(实测口径第 2 条)。

**客机侧对照**:客机日志 0 个 `LeaseAcquire`/`ObserverUpdate`(SPI 仅 Host 侧,与 F7 一致);`SnapshotReceive`/`SnapshotReceivePostfix` 通道工作正常;指纹与主机一致。

## 3. 对既有假设的修正

- 1123 报告 F7 的旧口径「Host 单独在 Alb 看不到资源」**修正为**:主机单独(无客机在场)时**正常**;失效场景是「客机在场但不同区域」或「客机下线后主机进入新区域」。失效的必要条件是**插件碰撞补丁曾以 covered=false disable 过该区域**(需要覆盖集发生过变化);纯主机单独游玩时覆盖集未变化,树由 vanilla 本地路径正常显示。
- H4(整批回滚 + 退避 → 永久瘫痪)确认成立,且已捕获现行日志铁证(§2 放大链第 2–4 步)。

## 4. 修复方向建议(供步骤④设计,待用户裁决后实施)

| # | 修复 | 目标 | 对应缺陷 |
|---|---|---|---|
| A | **步骤④ 单区域 Acquire 失败隔离**(已立案):`ProcessEntered`/`CaptureRegionState` 失败区域跳过并保留可重试状态,其余区域保留 | 消除整批回滚放大 | F4 / 评审 §3.5(M6P31 转绿) |
| B | **`trees-unavailable` 语义重定义**:区域树条目不存在 = 「原生尚未生成,暂缓快照」——该区域**挂起短延迟重试**(等 foliage 烘焙后 capture 成功),不计入 fault、不触发指数退避 | 消除时序错配导致的永久瘫痪 | H1 根因(本报告 §2) |
| C | (升级清单第二梯队,可与 A/B 同批)碰撞覆盖集并入本地玩家覆盖,或只 `enable()` 把 `disable()` 交还 vanilla | 消除「主机被误 disable」的可见性回归 | 评审 §3.3(本报告 §2 第 6 步) |

设计开放点(修复轮 TDD 前需定):B 中「暂缓重试」的重试间隔与上限、空树区域(真正无树)与「尚未生成」的区分是否需要区分对待、capture 时机是否改为 foliage/区域事件驱动。

## 5. 纪律声明

- 本报告为**静态取证 + 日志证据**裁决;Runtime 类结论(修复后 SPI 成为主驱动路径、多观察者场景回归)**保持 PENDING**,须以修复轮构建后 1 Host + 2 Guest 同 DLL 同 Case-ID 动态测试为准。
- 双端日志与取证版 DLL 的指纹关联已核验一致(§0);该指纹仅对应取证版,修复轮新构建产生新 MVID/SHA 后须重新采集三端日志。
