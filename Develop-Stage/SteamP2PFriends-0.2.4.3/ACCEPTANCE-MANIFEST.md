# SteamP2PFriends M3 阶段验收清单

## 阶段身份

- 阶段归档名称：`SteamP2PFriends-0.2.4.3`
- 阶段：Multi-Observer M3 `ZombieRegionLifecycleAdapter`
- 验收结论：PASS
- 验收日期：2026-08-24
- 下一阶段：M4（尚未开始构建）

## 归档指纹

| 文件 | 长度 | SHA-256 |
| --- | ---: | --- |
| `bin/Debug/SteamP2PFriends.dll` | 1005568 | `722EFBFCB292334824E98519CAC6F2E102C0BBDCC1AD954F7251BE870A062C1A` |
| `bin/Release/SteamP2PFriends.dll` | 935424 | `898C2F439BE8121F2E6EBA1647E382A14405B703E44D285B64F00B17D6D28A95` |

## 验收范围与核心事实

- **三端部署一致性**：房主（`005454`）、虚拟机客机（`005449`）、用户客机（`005459`）均加载 `SteamP2PFriends 0.2.4.3`，哈希一致。
- **0->1 权威按需生成**：客机进入远端城镇（Bound 7 与 Bound 10）成功触发 `acquire-commit`，分别生成 40 只与 39 只僵尸，房主本地未受性能影响。
- **多观察者并集保活**：双客机在同区域（Bound 10）活动时保持实体存活，未触发二次生成或实体销毁。
- **2 秒有界滞回释放**：所有观察者离开区域后，成功进入 2.0 秒滞回倒计时，并在需求归零后平稳执行 `release-commit demand=0 hysteresis=2.0s` 销毁远区实体。
- **边界横跳与隔离断路器**：客机跨边界移动期间触发 `demand-mismatch action=quarantine-no-counter-rewrite` 与 `release-cancel reason=demand-quarantined`，有效阻止了中途误销毁。
- **断线隔离与回访机制**：单客机断线精确释放自身 demand，其他在线客机区域不受影响；房主回访已释放区域平稳触发 `acquire-observed source=local-vanilla`。
- **无致命异常**：三端日志经全量扫描，0 崩溃、0 空引用、0 循环丢包。

完整运行验收记录：`audit/2026-08-24/RuntimeAcceptance-0.2.4.3-0105.md`。

## 归档边界

本目录冻结 M3 的源码、Debug/Release 构建、审计记录和三端 UMM 原始诊断包。它不包含 M4 源码或后续版本号。
