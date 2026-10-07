# Ticket 05 影子 Runtime 验收报告：Collision 只读影子

- **日期**：2026-09-25
- **票据**：`.scratch/collision-migration-slice/issues/05-collision-demand-policy-and-readonly-shadow.md`
- **候选角色**：`ReadOnlyShadow`
- **测试形态**：1 Host + 2 Guest，UMM 诊断包，人工执行
- **判定范围**：仅验收票 05 第 9 项只读影子 Runtime；不构成票 08/09 正式切换 Runtime PASS

## 1. 结论

票 05 第 9 项通过，可以将票状态从 `implemented-pending-runtime` 更新为 `completed`（静态闭环与只读影子 Runtime 均完成）。三端在启动日志中报告同一份 0.2.4.9 ReadOnlyShadow 候选身份；主机 25/25 条影子汇总均为 `collisionShadowForbidden=0`；**用户关于实体碰撞无问题的反馈属于辅助性的人工体验记录，不替代日志证据**；日志中旧碰撞激活无失败。

本轮没有暴露新的控制面准入阻塞，不回写票 04。票 06 的前置条件已满足，可以认领并实施；票 06 仍不得把自身编译产物当作生产 Authority Writer。

## 2. 产物身份门

候选身份（与票 05 静态审计 §6 一致）：

```text
version=0.2.4.9
mvid=cc488eb4-94c6-4bb0-af4a-65fdecc0ef9b
dllSha256=8EE7B82B4288E2CA25395C7F55DB99910DCDE94D31A40BE371974D76BECEAF48
caseId=SPF-0.2.4.9-Experimental-CollisionSlice-ReadOnlyShadow
candidateRole=ReadOnlyShadow
```

| 端 | 诊断包与指纹位置 | 结果 |
|---|---|---|
| Host | `D:\Agent-工作目录\DevelopMyUNMultiplayerModAndModloader\启动器\UnturnedModManager\publish\UMM-v2.2.1-win-x64\UMM-诊断包_20260925_234811\LogOutput.log:15` | 五项逐项匹配 |
| Guest 1 | `D:\Agent-工作目录\DevelopMyUNMultiplayerModAndModloader\启动器\UnturnedModManager\publish\UMM-v2.2.1-win-x64\UMM-诊断包_20260925_234843\LogOutput.log:15` | 五项逐项匹配 |
| Guest 2 | `UMM-诊断包_20260925_234743.7z`，解压临时目录 `C:\Users\The New Age\AppData\Local\Temp\umm-g2-20260925-234743\LogOutput.log:213` | 五项逐项匹配 |

本轮三端身份门通过；不会把不同候选角色或不同 SHA-256 的日志混入本结论。

## 3. 影子判读

主机 `[MultiObserver/M0] summary` 共 25 条，均包含：

```text
collisionShadowForbidden=0
collisionShadowReadOnly=true
collisionRadiusSource=LevelObjects.OBJECT_REGIONS
controlPlane=active
```

逐条 `collision-shadow` 事件受会话诊断配额限制，共见 12 条，全部为：

```text
kind=HostAddedCoverage disposition=Expected reason=host-local-player
```

代表性证据：主机 `LogOutput.log:1270`–`:1281`；首条区域为 `(24,31)`。未见 `disposition=Forbidden`、`collision-shadow-forbidden` 或 `collision-shadow-forbidden-cleared`。

影子汇总随观察者变化符合预期：

| 主机日志行 | 观察者 | InBoth | Expected | Forbidden | 判读 |
|---:|---:|---:|---:|---:|---|
| 1283 | 1 | 0 | 49 | 0 | Host 本地覆盖 |
| 5509 | 2 | 49 | 19 | 0 | Guest 1 加入 |
| 9318 | 3 | 86 | 19 | 0 | Guest 2 处于远区 |
| 17169–19125 | 3 | 68→62 | 0 | 0 | 投影与旧侧重合 |
| 22943 | 3 | 62 | 35 | 0 | Host 进入 Guest 2 原远区，属 Host 新增覆盖 |
| 29508 | 2 | 49 | 34 | 0 | Guest 2 离开 |
| 31291 | 1 | 0 | 49 | 0 | 仅 Host 留存，资源侧仍有 34 个滞回释放 |

旧碰撞激活计数为 `success=7302`、`skipped/already-active=10622`、`skipped/no-remote-demand=1845`、失败为 0。该统计是旧 Writer 的旁证，不改变“影子只读、旧 Writer 仍为唯一生产写入者”的边界。

## 4. 生命周期时间线

- Host 进入世界：`15:35:49.898Z`，主机 `LogOutput.log:1020`；影子会话开始于 `:1119`。
- Guest 1 加入：`15:37:14.663Z`，主机 `:3825` 接受，观察者在 `:5427` 加入。
- Guest 2 加入：`15:41:03.164Z`，主机 `:7754` 接受，观察者在 `:9053` 加入；首见远区中心在 `:9780`。
- Host 进入 Guest 2 原远区：主机 `:22760`、`:22914`、`:22943`；该阶段 83 次碰撞激活均无失败。
- Guest 2 断开：主机 `:29217` 移除客户端，`:29301`/`:29398` 完成观察者断开与移除。
- Guest 1 断开：主机 `:31046` 移除客户端，`:31140`/`:31144` 完成观察者断开与移除。
- 本包中 Guest 1 与 Guest 2 各只有一次连接代次；没有 Guest 1 退出后重新进入的第二次连接。因此本报告不宣称“退出—等待—重进”场景已在本包复现。

## 5. 其他验收旁证与限制

- `LeaseAcquire outcome=success` 442 次，无失败。
- `region-snapshot-failed=0`，`fault=` 为 0。
- `LeaseReleaseScheduled` 184 次，`LeaseRelease success` 276 次，`LeaseReentry success` 12 次。
- 资源侧出现 33 条 `RegionEntry outcome=failed`，均为 `observation-incomplete` / `read-failed:NullReferenceException` / `path=Fallback` / `failClosed=true`；未被影子分类为 Collision Forbidden，也未构成票 04 准入阻塞。
- 两端 Guest 的 `SnapshotReceive` 诊断出现 `suppressed reason=diagnostic-quota-exhausted`；因此本报告不把 Guest 资源快照补充诊断宣称为成功观察。
- Guest 2 用户名在本包中为 `LsacoTuo`，SteamID 为 `76561198263630289`；Host 为 `76561199030780228`，Guest 1 为 `76561199721762479`。地图与世界身份三端一致。

## 6. 票面映射与边界

- 第 9 项“只读 1 Host + 2 Guest 影子 Runtime”通过：三端同一候选身份，`collisionShadowForbidden=0`，可见差异均为可解释的 Host 新增覆盖。
- 未发现授权 Guest 缺区、静止需求抖动、跨观察者错误释放、凭空需求、越界键或异域需求；不回写票 04。
- 该结论只授予 `ReadOnlyShadow` 影子 Runtime 证据，不授予 Collision 正式切换资格。
- 票 06 可开始：应在共享 Lifecycle Orchestration Engine 上新增 Collision Domain Execution Port 与 Acquisition Receipt；执行端口不得扫描观察者或重算需求，且无 receipt、陈旧代次或身份不确定时不得执行 destructive disable。
- 票 08/09 仍需 `candidateRole=Cutover` 且三端同一 SHA-256 的正式候选与独立 Runtime 验收。

## 7. 证据来源

- Host：`UMM-诊断包_20260925_234811\LogOutput.log`
- Guest 1：`UMM-诊断包_20260925_234843\LogOutput.log`
- Guest 2：`UMM-诊断包_20260925_234743.7z`（临时解压目录见 §2）
- 静态实施报告：`audit/2026-09-18/Implementation-0.2.4.9-CollisionTicket05-2225.md`
- 实机规程：`docs/agents/real-machine-test-loop.md`、`docs/agents/auto-rm-test-sop.md`
