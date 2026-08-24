# M2 运行验收审计 - SteamP2PFriends 0.2.4.2

## 一、结论

本轮 `0.2.4.2` 的**双端单客机 M2 验收通过**：R01-R06、R09、R10 按开发者人工观察通过，且两端原始 `LogOutput.log` 对 M2 基线、待审核观察者、断线清理与重连代提升提供了关键佐证。

**M2 整体阶段尚未验收完成，不能进入 M3，也不能归档 `Develop-Stage/SteamP2PFriends-0.2.4.2`。** R07/R08 是多观察者模型的核心验收项，因本轮只有一个客机而未执行，必须保持 OPEN。

## 二、证据身份与边界

| 角色 | 原始日志 | 识别依据 |
|---|---|---|
| 客机 | `D:/Agent-工作目录/DevelopMyUNMultiplayerModAndModloader/启动器/UnturnedModManager/publish/UMM-v2.1.8-win-x64/UMM-诊断包_20260821_224527/LogOutput.log` | 连接到房主、`CLIENT_STATE ... Connected`、主动断开和被撤销踢出后重连 |
| 房主 | `D:/Agent-工作目录/DevelopMyUNMultiplayerModAndModloader/启动器/UnturnedModManager/publish/UMM-v2.1.8-win-x64/UMM-诊断包_20260821_224546/LogOutput.log` | `HOST_ACCEPT_RETURNED`、`ItemAuthorityGate`、`M2-Item commit`、`OnEnemyDisconnected` |

- 双端均加载 `SteamP2PFriends 0.2.4.2`，由用户确认从同一 DLL 手动复制部署、哈希一致。UMM 包不含 SHA-256 字符串，因此“同哈希”属于用户部署陈述，不是日志内可独立复算的事实。
- 本构建预期 Debug SHA-256 为 `3A83295E685EAD79BDA64A8816FCBDF821C1CBDFE0B9DE37E0692E2EEE483ED3`。
- 日志未启用物品实例级 pickup/remove/drop 指纹；因此 R04、R09 的“功能通过”采信开发者观察，不声称被日志逐实例证明。

## 三、逐项判定

| Case | 判定 | 日志与人工证据 |
|---|---|---|
| R01 启动指纹 | PASS | 两端均加载 `0.2.4.2`；两端均有 `M2-Item registration verified capability=ReliableEnqueueBaseline`。房主日志 32、103、105；客机日志 14、105。未发现 `DIAGNOSTIC BUILD INVALID` 或 M2 abort/reject。 |
| R02 待审核首次基线 | PASS | 房主 754-760 记录新连接进入 pending，765-770 在 `authorized=False` 前后触发原生 `askItems` 与 M2 `connectionGeneration=2` 提交，781 明确记录 observer `authorized=False`。说明审批状态未阻断世界基线。 |
| R03 审批不重生 | PASS | 开发者人工确认。日志 781 为 pending observer，785-790 为审批转为 authorized，期间没有新的 `ItemAuthorityGate commit`；授权变化只记录 `AuthorizationChanged`，与“不重生”契约一致。 |
| R04 权威拾取移除 | PASS（人工） | 开发者确认通过。当前原始日志没有 `ReceiveItem/DestroyItem` 或 instance ID 指纹，故无法由日志独立证明“同一实例已在房主端移除”。 |
| R05 离区重入 | PASS（人工，链路佐证） | 开发者确认通过；房主 714-730 显示移动到新的相关区域时，同 observer 继续按区域提交 M2 baseline。日志未携带“离开 B 后返回 B”的显式区域案例 ID，因此不把该点夸大为完全日志闭合。 |
| R06 断线重连 | PASS | 738 记录按 SteamID 精确清理 observer；754-760 重连并进入 pending；767、770 对同一 world generation 以 `connectionGeneration=2` 再次提交基线。客机 161-184 也记录被撤销踢出后再次连接并回到 `Connected`。 |
| R07 双客机同区 | OPEN | 未执行。需要第二个独立客机/Steam 账号同时在同一远区。 |
| R08 双客机异区 | OPEN | 未执行。需要第二个独立客机/Steam 账号分别在两个远区。 |
| R09 掉落增量旁路 | PASS（人工） | 开发者确认玩家/僵尸掉落增量未受影响；日志无 `dropItem`、`ReceiveItem(s)` 或实例 ID 探针，不能据此做实例级日志证明。 |
| R10 房主本地回归 | PASS（人工） | 开发者确认通过。房主日志显示本地 ItemAuthorityGate 正常提交；M2 仅对远端 observer 产生 commit，未见本地 observer 被 M2 接管。 |

## 四、关键运行时序

首次远端区域基线的正确顺序在房主日志 605-624 已出现：

1. M1 `allow` 进入原生观察者区域需求。
2. `ItemAuthorityGate` 对未生成区域 commit，或对既有区域 skip。
3. 原生 `ListenRegionSync/Item send` 调用 `askItems`。
4. M2 `ReliableEnqueueBaseline` commit 对同 observer/connectionGeneration/region 提交账本。

重连时，房主日志 738 -> 754-770 形成完整闭环：精确移除旧 observer，再建立新连接，并以 `connectionGeneration=2` 重发既有权威区域的基线。未发现 `M2-Item reject/abort`、`ItemRegionSync` 异常或注册失效。

## 五、残留风险与下一门禁

- **阻断进入 M3：R07/R08 未完成。** 这不是可由单客机替代的覆盖，因为 M2 的账本核心就是 observer 间隔离与并存。
- R04/R09 的运行功能已由开发者确认，但诊断日志缺少实例 ID/增量事件指纹。未来若出现物品重生、幽灵拾取或掉落串扰，应先补充该类观测，不能从本轮日志反推具体实例。
- 客机日志包含 `Error getting live config: Unable to complete SSL connection`，属于 Unity Live Config 网络错误；它发生在菜单/断线后，未与 M2 事务、P2P 接入或物品复制异常形成关联，不计为 M2 blocker。

独立子智能体复审与上述判定一致：单客机主链路 PASS；R04/R05/R09/R10 的部分细节仅有开发者现象证据、缺少实例级日志；R07/R08 仍为阶段阻断验收项。

## 六、最终状态

`0.2.4.2` 保持不变。M2 已关闭双端单客机验收项，待具备第二客机后仅补测 R07/R08；补测须继续使用同一 DLL 哈希并由 UMM 导出所有参与端日志。补测通过后，才可归档 `SteamP2PFriends-0.2.4.2` 并授权 M3。
