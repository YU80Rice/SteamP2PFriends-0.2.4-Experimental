# ADR 0004: 僵尸状态快照与外观同步适配器 (Zombie Snapshot & Appearance Adapter)

- **状态**：Accepted (已验收并冻结)
- **日期**：2026-08-25
- **决策者**：YU80Rice, AI Assistant

---

## 背景与问题
在 M3 阶段解决远端僵尸按需生成（0->1 Acquire）与 2 秒滞回释放（Hysteresis Release）后，远端僵尸的生命周期已经闭环。然而，僵尸的初始全量外观数据（`sendZombieClothes`）、逐观察者快照代数（`ObserverLoadedGeneration`）以及周期性位置/攻击同步（`SendZombieStates`）仍需要显式管理。

## 决策方案
1. 建立 `ZombieSnapshotAdapter` 与 `ZombieSnapshotReplicationLedger`；
2. 显式声明 `ReliableEnqueueBaseline` 能力，将全量外观快照与 `SessionEpoch`、`ConnectionGeneration`、`RegionGeneration` 强绑定；
3. 维护单调递增 `NextDeltaSequence`，杜绝过期/乱序增量状态穿透；
4. 区域重建（`RegionGeneration` 递增）或玩家重连（`ConnectionGeneration` 递增）时，自动触发全量状态与外观重发。

## 影响与后果
- 客机首次进入或重连进入远区时，100% 获得完整服装外观与变异种特征；
- 彻底替换遗留的 `P0-C-1` 状态写入补丁。
