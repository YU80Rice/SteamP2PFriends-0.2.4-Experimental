# SteamP2PFriends 0.2.4.4 (M4) 验收指纹清单 (ACCEPTANCE-MANIFEST)

- **阶段**：Multi-Observer M4 `ZombieSnapshotAdapter`
- **版本**：`0.2.4.4`
- **验收日期**：2026-08-25 00:05 (Asia/Shanghai)
- **自动化测试基线**：`114/114 PASS`
- **运行验收结论**：**PASS (通过并冻结)**

---

## 1. 冻结二进制 SHA-256 指纹

| 构件 | 相对路径 | 大小 (Bytes) | SHA-256 哈希值 |
|---|---|---|---|
| **Debug DLL** | `bin/Debug/SteamP2PFriends.dll` | 1,005,568 | `AF6F887F6764BF4C57497D9D72FFA11ED6F23A914B88255DAB282D1F55148457` |
| **Release DLL** | `bin/Release/SteamP2PFriends.dll` | 935,424 | `F42EF144C136AAC0395BCF97BD4BEC59BCD479DAE3798AF4A8B37336FF864509` |

---

## 2. 运行验收证据链

| 节点角色 | 诊断包来源目录 | 退出码 | 关键事件证明 |
|---|---|---|---|
| **房主主机 (Host)** | `acceptance-evidence/UMM-诊断包_20260824_234906` | 0 | `M4-ZombieSnapshot capability=ReliableEnqueueBaseline`, `acquire-commit bound=0 generation=1->2->3`, `release-commit demand=0 hysteresis=2.0s` |
| **虚拟机客机 (VM Guest)** | `acceptance-evidence/UMM-诊断包_20260824_234908` | 0 | `CLIENT_AUTHENTICATE_SEND`, `CLIENT_STATE -> Connected`, `acquire-observed` |
| **用户客机 (User Guest)** | `acceptance-evidence/UMM-诊断包_20260824_235308` | 0 | `CLIENT_ACCEPTED_RECEIVED`, `CLIENT_STATE -> Connected`, `DisconnectClean` |

---

## 3. 验收准则达标状态

1. [x] 三端运行版本与 SHA-256 100% 一致 (`0.2.4.4`)
2. [x] 远区首次生成 33 只僵尸带全套外观服装 (`acquire-commit bound=0 generation=1 zombies=0->33`)
3. [x] 多观察者边界横跳隔离断路器生效 (`demand-mismatch action=quarantine-no-counter-rewrite`, `release-cancel reason=demand-quarantined`)
4. [x] 滞回释放平稳执行 (`release-commit demand=0 hysteresis=2.0s`)
5. [x] 区域回访世代正确递增 (`acquire-commit generation=2`, `generation=3`)
6. [x] 客机断线精确注销观察者 (`ObserverRemoved observer=...0289`, `observer=...2479`)
7. [x] 房主停服会话平稳递增终结 (`session-end nextEpoch=4`)
8. [x] 全端 0 崩溃、0 空引用、0 致命报错
