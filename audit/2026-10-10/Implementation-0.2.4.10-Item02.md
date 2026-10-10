# C-P2-Item-02 实施与审查记录

- 工单：`.scratch/c-p2-item-02-demand-and-expiry/issue.md`
- 起点 HEAD：`752119f8fb5b88fbc5563ac6ec11966c8bd990ba`
- 状态：`implemented-pending-runtime`
- Runtime：未采证，不报 PASS。06 负责 1 Host + 2 Guest。

## 行为

候选会话消费共享投影的当前 Item 需求。需求大于 0 时，同一 `ItemRegion` 仓库按原生阈值做有界自然消失/补货；需求归零立即停止，2 秒滞回租约不冒充需求、不重置启计。实际掉落按创建时间与 `Despawn_Dropped_Time` 独立到期，含未访问区，每步最多删除一件。删除后调用 `SendDestroyItem`；补货调用原生 `respawnItems`。候选占用时跳过旧全图 `despawnItems`/`respawnItems`。

## Red → Green

| 测试 | Red | Green |
| --- | --- | --- |
| IT28 同区一人离开仍跑自然周期 | 461/462 | 通过 |
| IT30 未访问掉落到期 | 462/463 | 通过 |
| IT31 有需求补货以增项为准 | 463/464 | 通过 |
| StaticIL 原生绑定与旧 Writer 抑制 | 464/465 | 通过 |
| StaticIL 删除调用 SendDestroyItem | 471/472 | 通过 |

IT32–IT34、IT36–IT38 为规格锁，落地时已由上述实现满足。最终 `WhitelistTests.exe`：**472/472 PASS**。插件与测试项目 Release 构建 0 error。

## 审查

均经 Task 新实例，`subagent_type` 为 `standards-reviewer` / `spec-reviewer`。工具返回的实例 ID 没有附带模型或 variant 元数据；本环境 PATH 中无 `vself`，不能用会话元数据核对 `haowei-ai/grok-4.7` / high。正文自报不替代该元数据。

- Spec 第一轮 `ses_edb02702bffe3356c6RrP6bN2a`：删除未发 `SendDestroyItem`；滞回时钟质疑。
- 修复后 Spec `ses_edae1df0dffeSfo8P2xBp9Dv71`：**CLEAN**。滞回时钟质疑不成立：共享引擎由 `ResourceProduction.AdvanceTime` 在 reconcile 早退前按帧推进。
- Standards `ses_edaf0d267ffeBoq5iqGUIaMyf2` 与修复后 `ses_edae1eb46ffe3Zj41uKafiC0p6`：**CLEAN**。

可延期 smell：游标循环重复、来源分支重复、门控命名、Dedicated Gate 文件承载跳过前缀、协调器直接写静态门。不阻塞。

不授予新 Case-ID。
