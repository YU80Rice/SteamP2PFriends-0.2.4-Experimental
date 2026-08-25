# M2 R07/R08 补测与阶段验收报告 - SteamP2PFriends 0.2.4.2

## 一、最终结论

`0.2.4.2` 的 M2 阶段验收完成，R01-R10 全部 PASS，可归档为 `Develop-Stage/SteamP2PFriends-0.2.4.2`。本次补测覆盖此前唯一阻断项 R07（双客机同区）与 R08（双客机异区）；版本号保持 `0.2.4.2`，不进入 M3。

## 二、证据身份与部署边界

| 角色 | 原始日志 |
| --- | --- |
| 客机 | `UMM-诊断包_20260821_233451/LogOutput.log` |
| 房主及多观察者会话 | `UMM-诊断包_20260821_233509/LogOutput.log` |
| UMM 压缩归档 | `UMM-诊断包_20260821_233438.zip` |

- 客机日志第 14 行、房主日志第 14 行均加载 `SteamP2PFriends 0.2.4.2`。
- 两端 DLL 由开发者从同一文件夹手动复制，哈希一致；UMM 原始包未提供可独立复算的 SHA-256 字段，故该项以部署者确认作为证据。
- 当前 Debug 构建 SHA-256：`3A83295E685EAD79BDA64A8816FCBDF821C1CBDFE0B9DE37E0692E2EEE483ED3`。

## 三、R07/R08 运行证据

### R07 双客机同区

- 房主日志 673-679：第二客机以独立 SteamID `76561199130814523` 完成握手并进入 `clients=3`，同时加入待审核队列；这证明审核状态不是连接硬阻断。
- 房主日志 682：`remotePlayers=2 activeRegions=96`，两名远端观察者同时存在。
- 房主日志 698-718：第二 observer 使用独立 `connectionGeneration=2` 提交 `(30..32)` 区域的 M1/M2 基线。
- 房主日志 719-725：该 observer 先为 `authorized=False`，审批后转为 `authorized=True`，并明确记录 `observer presence unchanged`；审批没有销毁或重建玩家观察者。

### R08 双客机异区

- 房主日志 747、750、765：第二 observer 在 `(27,33)` 与 `(37,36)` 等不同区域发生 relevance change，仍保持其独立观察者账本。
- 房主日志 750-764：跨区域移动期间对已提交区域全部记录 `ItemAuthorityGate skip ... state=Committed`，同时继续提交该 observer 的 M2 baseline；没有二次随机生成路径。
- 房主日志 767：`clients=3 ... observers=3 ... itemDemandRegions=18`；日志 872-894 又记录两名远端观察者扩大到 `activeRegions=98`、`zombieDemandBounds=3`，验证并集观察者模型在异区同时工作。

### 生命周期与清理

- 房主日志 775、820、901、905：断线按 SteamID 精确执行 `OnEnemyDisconnected` 与 `ObserverRemoved`，没有跨 observer 清理。
- 房主日志 808、839：重连后建立新的 observer generation，既有区域继续 `skip Committed`，未重生地图物品。
- 客机日志 133-136：连接状态到达 `Connected`；未出现 M2 reject/abort、`DIAGNOSTIC BUILD INVALID` 或 `ItemRegionSync` 异常。

## 四、逐项判定

| 范围 | 判定 | 证据边界 |
| --- | --- | --- |
| R01-R06 | PASS | 上一轮单客机验收报告已记录，本轮未回归破坏。 |
| R07 双客机同区 | PASS | `clients=3`、`remotePlayers=2`、多 observer 独立基线与审批保持 presence。 |
| R08 双客机异区 | PASS | 独立 observer 在多个区域移动，M2 按 observer/region 分账，已提交区域 skip。 |
| R09-R10 | PASS | 开发者人工确认，且本轮运行未出现相关异常。 |

R04/R09 的实例级拾取、掉落和移除仍未由日志中的 instance ID 指纹独立证明；这不阻断本阶段，因为本轮用户、用户朋友及虚拟机均确认无异常，且 M2 的多观察者门禁已由双客机日志闭合。

## 五、构建与审计

- Debug/Release 编译：0 errors / 0 warnings。
- 自动化测试：`92/92 PASS`。
- 独立静态审核：PASS。
- 本报告不修改业务代码，仅补录运行证据；因此不重复触发代码编译。

## 六、最终状态与后续边界

M2 已验收完成并可归档。归档内容冻结 `0.2.4.2` 源码、Debug/Release 构建、静态审计报告及本次双客机 UMM 原始证据。M3 尚未构建，须另行授权后开始。
