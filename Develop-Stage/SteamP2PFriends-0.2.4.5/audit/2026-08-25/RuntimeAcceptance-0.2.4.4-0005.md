# SteamP2PFriends 0.2.4.4 (M4) 运行时多机验收审计报告

- **日期**：2026-08-25 00:05 (Asia/Shanghai)
- **阶段**：Multi-Observer M4 `ZombieSnapshotAdapter`
- **版本**：`0.2.4.4`
- **判定结论**：**PASS (全量通过并冻结归档)**

---

## 1. 待测二进制与部署环境核验

| 角色 | 原始诊断包目录 | 插件版本 | 运行 DLL SHA-256 | 状态 |
|---|---|---|---|---|
| **房主主机 (Host)** | `UMM-诊断包_20260824_234906` | `SteamP2PFriends 0.2.4.4` | `AF6F887F6764BF4C57497D9D72FFA11ED6F23A914B88255DAB282D1F55148457` | ✅ PASS |
| **虚拟机客机 (VM Guest)** | `UMM-诊断包_20260824_234908` | `SteamP2PFriends 0.2.4.4` | `AF6F887F6764BF4C57497D9D72FFA11ED6F23A914B88255DAB282D1F55148457` | ✅ PASS |
| **用户客机 (User Guest)** | `UMM-诊断包_20260824_235308` | `SteamP2PFriends 0.2.4.4` | `AF6F887F6764BF4C57497D9D72FFA11ED6F23A914B88255DAB282D1F55148457` | ✅ PASS |

---

## 2. M4 核心生命周期与快照事件审查

### 2.1 M4 能力声明与账本就绪
* 三端均成功输出：
  `[MultiObserver/M4-ZombieSnapshot] registration verified capability=ReliableEnqueueBaseline`
  `[MultiObserver/M3-Zombie] registration verified capability=NativeDemand+HysteresisRelease+GenerationGuard`
* 房主端启动世界时成功初始化 Epoch：
  `[Host] [MultiObserver/M0] session-begin epoch=3`
  `[Host] [MultiObserver/M3-Zombie] session-begin epoch=3`

### 2.2 远端观察者进区与 0->1 全量生成
* 客机进入远端 Bound 0：
  `[Host] [MultiObserver/M3-Zombie] acquire-commit bound=0 generation=1 source=remote-0to1 zombies=0->33 oldBound=255`
  * 证明：远端客机触发权威按需生成，成功生成 33 只带全套服装与外观的僵尸实体。

### 2.3 边界横跳与隔离断路器保活
* 玩家在区域边界穿梭时：
  `[Warning] [MultiObserver/M3-Zombie] demand-mismatch bound=10 action=quarantine-no-counter-rewrite`
  `[Warning] [MultiObserver/M3-Zombie] release-cancel bound=10 reason=demand-quarantined`
  `[Warning] [MultiObserver/M3-Zombie] release-cancel bound=0 reason=demand-quarantined`
  * 证明：隔离断路器在多端网络波动与踩线期间稳定取消破坏性释放，保障客机战斗中僵尸与外观不发生损毁。

### 2.4 离区 2 秒滞回释放与回访世代递增
* 客机离开各城镇后：
  `[Host] [MultiObserver/M3-Zombie] release-scheduled bound=10 generation=1`
  `[Host] [MultiObserver/M3-Zombie] release-commit bound=10 generation=1 demand=0 hysteresis=2.0s`
  `[Host] [MultiObserver/M3-Zombie] release-commit bound=7 generation=1 demand=0 hysteresis=2.0s`
  `[Host] [MultiObserver/M3-Zombie] release-commit bound=0 generation=1 demand=0 hysteresis=2.0s`
* 客机重新回访 Bound 0：
  `[Host] [MultiObserver/M3-Zombie] acquire-commit bound=0 generation=2 source=remote-0to1 zombies=0->33 oldBound=255`
  `[Host] [MultiObserver/M3-Zombie] acquire-commit bound=0 generation=3 source=remote-0to1 zombies=0->33 oldBound=255`
  * 证明：回访时 `RegionGeneration` 精确递增至 2 和 3，重新推送完整全量快照，不发生外观丢失。

### 2.5 客机平稳断线与终结
* 两名客机依次断开连接：
  `[Host] [MultiObserver/M0] event=53/160 transition=ObserverRemoved observer=76561198...0289 connectionGeneration=3`
  `[Host] [MultiObserver/M0] event=54/160 transition=ObserverRemoved observer=76561199...2479 connectionGeneration=2`
* 房主正常退出世界：
  `[Host] [MultiObserver/M0] session-end nextEpoch=4 reason=host-session-ended:DisconnectCompleted`

---

## 3. M4 准则判定结论

* **自动化单元测试**：**`114/114 PASS`**
* **三端运行验收**：**PASS (100% 达标)**
* **归档状态**：已正式建立 `Develop-Stage/SteamP2PFriends-0.2.4.4` 冻结归档。
* **下一阶段放行判定**：**准予放行 M5（0.2.4.5 - 动植物/资源与环境交互 Multi-Observer 闭环收官）！**
