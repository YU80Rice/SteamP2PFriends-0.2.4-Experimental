# 0.2.4.11 known issues 与未闭环门禁

记录时间：2026-11-06。适用于 `0.2.4.11-preview1`（DLL `9E92987B…`，MVID `06176c2d-…`）。

## 一、必须写进交付说明的行为限制

### KI-1 极端窗口下本局进度不落盘（设计选择，非缺陷修复项）

条件：整局内**没有任何一次原生保存**触发延迟兑现的最后期限，且 `GroupManager.onLevelLoaded(GAME)` 后缀始终没有到来。
表现：该局 `PlayerQuests.load` 始终未执行（`wasLoadCalled=false`），原生 `save()` 首行为 no-op → **本局任务/旗帜进度不保存**；但旧 `Quests.dat` **原样保留，不会被默认值或空表覆盖**。
日志：`save-blocked:*` 与 `Degraded` 可检索。
判定依据：`architecture-rescue-t23-save-failure-direction.md` 裁决——宁可拒绝本局保存并显式 Degraded，也绝不写坏存档或把 Nil/MEMBER 落盘。
正常关卡流程不可达；若实机日志出现该行，请当作证据回传，而不是当作崩溃。

### KI-2 pending 被放弃后该实例本局不再 load（t26 R4-01）

条件：`not-ready-at-execute` / `token-mismatch` / `lease-changed`，或 prefix 异常走 blocked 分支。
表现：该实例本局既不再 load 也不再 save，仅一行 Warn → 与 KI-1 同样的「进度不落盘、不写坏档」。
限制：正常关卡流程不可达；已要求 `save-blocked:*` 日志可检索。

## 二、未闭环门禁（不宣称 Bug3 端到端 Green）

| 编号 | 内容 | 为什么没关 | 关闭方式 |
| --- | --- | --- | --- |
| T1a | 真实存档 `none/MEMBER → α/OWNER` | 46 条用例全部是纯内存 fixture，零真实存档、零真实原生 load | 用户 1H+1G 实机（同存档菜单重进×2、进程重启×2） |
| T1b/T2a/T3a/T4a | 客机侧观测与退队权威性 | 现有两包客机侧零仪表 | 需新建客机侧独立门禁票，不得用 T1 Green 连带关闭 |
| E4 | 延迟恢复后本机 quest flags 重发的实际表现 | 需 Unity 运行时 | SDK/真机验证 |
| t7 | Bug1 仪表三相位（`RX/AFTER_ENQUEUE`、`RX_REJECT`、`CONSUME/AFTER_DEQUEUE`） | 未实现，缺 `input_seq/rx_id/queue_depth` 关联 | 另开票补齐后再测 Bug1 |

## 三、本包不含的修复

- Bug1 主机视角客机移动顿挫：只有仪表规格书（`bug1-instrumentation-spec.md`），无生产改动。
- Bug2 客机开枪声：只有调查与补偿规格书（`bug2-audio-leaf-spec.md`），无生产改动。

## 四、验证快照

- 生产 build：`0 警告 0 错误`
- 测试 build：`0 错误 4 警告`（均为既有）
- 用例：`692/694 PASS`，Bug3 `46/46`，失败仅 `GBA01`/`NIL01`（NetReflection 类型初始化，控制台无 Unity 运行时）
- 双轴：工程轴 t26 `pass`（19206B / `a9e01b81…`）；规格轴 t18 `pass`（17871B / `2e29a186…`），r4 测试侧三项已由 t27 收口
