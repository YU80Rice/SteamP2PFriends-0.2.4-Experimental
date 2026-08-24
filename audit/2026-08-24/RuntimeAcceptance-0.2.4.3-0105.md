# SteamP2PFriends 0.2.4.3 (M3) 运行时双机/三机验收审计报告

- **日期**：2026-08-24 01:05 (Asia/Shanghai)
- **阶段**：Multi-Observer M3 `ZombieRegionLifecycleAdapter`
- **版本**：`0.2.4.3`
- **判定结论**：**PASS (通过并冻结)**

---

## 1. 待测二进制与部署指引核验

| 角色 | 原始诊断包目录 | 插件版本 | DLL SHA-256 |
|---|---|---|---|
| **房主主机 (Host)** | `UMM-诊断包_20260824_005454` | `SteamP2PFriends 0.2.4.3` | `722EFBFCB292334824E98519CAC6F2E102C0BBDCC1AD954F7251BE870A062C1A` |
| **虚拟机客机 (VM Guest)** | `UMM-诊断包_20260824_005449` | `SteamP2PFriends 0.2.4.3` | `722EFBFCB292334824E98519CAC6F2E102C0BBDCC1AD954F7251BE870A062C1A` |
| **用户客机 (User Guest)** | `UMM-诊断包_20260824_005459` | `SteamP2PFriends 0.2.4.3` | `722EFBFCB292334824E98519CAC6F2E102C0BBDCC1AD954F7251BE870A062C1A` |

---

## 2. M3 关键生命周期事件链审查

### 2.1 适配器初始化与能力登记
* `[MultiObserver/M3-Zombie] registration verified capability=NativeDemand+HysteresisRelease+GenerationGuard`
* `[MultiObserver/M1-Item] registration verified; legacy full-map listen-host generation retired`
* `[MultiObserver/M2-Item] registration verified capability=ReliableEnqueueBaseline`
* `[Host] [MultiObserver/M0] session-begin epoch=1`

### 2.2 远端客机首次进入 Bound 7 (0->1 Acquire)
* 客机进入房主从未进入的 Bound 7：
  `[Host] [MultiObserver/M3-Zombie] acquire-commit bound=7 generation=1 source=remote-0to1 zombies=0->40 oldBound=0`
  * 证明：远端客机触发权威按需生成，成功生成 40 只僵尸，未发生二次重复生成。

### 2.3 离开 Bound 7 与 2 秒滞回释放 (Release Hysteresis)
* 客机离开 Bound 7：
  `[Host] [MultiObserver/M3-Zombie] release-scheduled bound=7 generation=1 deadline=345.278`
  `[Host] [MultiObserver/M3-Zombie] release-commit bound=7 generation=1 demand=0 hysteresis=2.0s`
  * 证明：滞回调度器在 2 秒后确认 demand 仍为 0，平稳执行 release 销毁远端实体。

### 2.4 重新回访 Bound 7 (世代递增)
* 客机重新进入 Bound 7：
  `[Host] [MultiObserver/M3-Zombie] acquire-commit bound=7 generation=2 source=remote-0to1 zombies=0->40 oldBound=0`
  * 证明：RegionGeneration 递增至 2，干净生产新代实体。

### 2.5 多客机占用 Bound 10 与边界横跳隔离断路器
* 双客机同时进入 Bound 10：
  `[Host] [MultiObserver/M3-Zombie] acquire-commit bound=10 generation=1 source=remote-0to1 zombies=0->39`
  * 跨边界移动时：
    `[Warning] [MultiObserver/M3-Zombie] demand-mismatch bound=10 native=2 observer=1 action=quarantine-no-counter-rewrite`
    `[Warning] [MultiObserver/M3-Zombie] release-cancel bound=10 reason=demand-quarantined`
  * 证明：M3 隔离断路器成功生效，在玩家踩线和网络不同步期间取消了过早的 Release，防止战斗中僵尸被误销毁。
* 最终离开 Bound 10：
  `[Host] [MultiObserver/M3-Zombie] release-commit bound=10 generation=1 demand=0 hysteresis=2.0s`

### 2.6 断线精确隔离与会话安全终结
* 单个客机断线：
  `[Host] [MultiObserver/M0] event=38/160 transition=ObserverRemoved observer=76561199...4523 connectionGeneration=3`
  * 证明：仅注销该断线玩家的 demand，其他在线观察者与区域不受影响。
* 停服正常终结：
  `[Host] [MultiObserver/M0] session-end nextEpoch=2 reason=host-session-ended:DisconnectCompleted`

---

## 3. 12 项 M3 验收准则对照矩阵

| 编号 | 准则 | 判定 |
|---|---|---|
| **C01** | 三端 DLL SHA-256 100% 一致 | ✅ **PASS** |
| **C02** | 房主单人基线与原生僵尸生成正常 | ✅ **PASS** |
| **C03** | 客机首次进入远端 Bound 0->1 成功 Acquire 并生成实体 | ✅ **PASS** |
| **C04** | 远区战斗期间僵尸不被误销毁，掉落与伤害正常 | ✅ **PASS** |
| **C05** | 第二客机进入同区不导致二次生成或重置实体 ID | ✅ **PASS** |
| **C06** | 第一客机离开、第二客机留下时区域不释放 | ✅ **PASS** |
| **C07** | 最后一人离开后经 2 秒滞回安全提交 Release | ✅ **PASS** |
| **C08** | 边界横跳触发 release-cancel 与断路器，不中断战斗 | ✅ **PASS** |
| **C09** | 单客机断线仅清理自身 demand，不污染其他玩家 | ✅ **PASS** |
| **C10** | 房主回访已释放区域平稳触发 acquire-observed | ✅ **PASS** |
| **C11** | 单机与 U3DS 运行保持原生分支 | ✅ **PASS** |
| **C12** | 全量日志提取无致命异常或崩溃 | ✅ **PASS** |

---

## 4. 结论与归档行动

* **验收结论**：**M3 全部 12 项准则 100% PASS**。
* **阶段状态**：已正式建立 `Develop-Stage/SteamP2PFriends-0.2.4.3` 冻结归档。
* **后续行动**：M3 验收正式关闭；等待授权启动 **M4（僵尸状态快照与实体代同步）**。
