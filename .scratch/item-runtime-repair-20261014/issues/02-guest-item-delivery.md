# R02 — Guest 正常进区自动取得物品，真实失败才提示

**What to build:** 客机入服及独自到访远处区域后，当前权威物品经原生清单自动交付并可交互；正常准备、空区域和分帧等待不误触发失败提示。真正失败按既有有限恢复处理，提示可追溯且不重复刷屏。

**Blocked by:** None — can start immediately；调度安排在 R01 后不构成逻辑阻塞边。

**Label:** ready-for-human
**Status:** implemented-pending-runtime - R02 implementation and static verification complete; cumulative round 4 Standards and Spec are CLEAN. Guest Runtime acceptance remains pending.
**Version:** SPF 0.2.4.10 Experimental / 实机返修 R02

## 已知事实与证据边界

- 用户确认 Guest 入服立刻收到大量区域提示，进入远处城镇无物品；后补充安全重试/可能半包两种提示印象都有，文字很长，无法回忆逐条原文或占比。
- Host 日志确认 Guest 初始九区处于 waiting-settlement，远区发生 Acquire 和原生区域更新；这不证明已发送或收到物品。
- 两端诊断包缺少对应聊天正文、首次结算异常和完整发送/接收因果链；不能认定提示一定是去重错误或实际半包。
- 已分配但物资点数量为零与输入尚未建立是不同状态；静态分类缺口尚未证实造成该城镇无物品。

## Acceptance criteria

- [ ] 实际生产链贯穿有效 Guest 需求→准备/返区结算→完成门→原生基线交付；完成后不要求玩家再移动或 Host 到场才能推进。
- [ ] 修复前运行可触达生产判定的 Red，覆盖首次进入、返区、有物资点区域、合法零物资点区域及输入缺失；不以布尔 fake 替代原生输入分类。
- [ ] 合法空区可完成空基线并保留当前实际掉落；输入缺失保持未就绪，不能伪装为合法空区或吞掉作业。
- [ ] 正常长结算、等待同一次结果和原生分帧呈现不消耗异常预算、不发送错误提示；等待者在完成后取得当时权威清单。
- [ ] 对首次真实异常、确认未发送、结果未知及可能半包分别验证；只续做确认未完成步骤，不重抽已完成物资或盲重发清单。
- [ ] 保留首次尝试加最多 3 次自动重试、最多 10 秒及既有人工恢复身份/需求校验；不得提高上限或直接关闭聊天掩盖故障。
- [ ] 使用限量关联记录连接首次异常/堆栈、区域作业、共享预算、通知类型与发送目标，能区分九区各一次与同区同链重复；生产定向提示同链同观察者同区不重复。
- [ ] 真正可能半包时沿用完整物品加载覆盖离区/返区；确认未发送的失败保留安全当前区域恢复，正常玩家不必执行恢复命令。
- [ ] 单观察者失败不拖累其他观察者、区域或领域；保留唯一 Writer、Core 身份、原生协议和 07 权威拾取距离边界。
- [ ] 完成必要回归、全新独立 Standards/Spec 双 CLEAN、限定提交、delivery/audit 和真实主会话回报；源码 Green 不代表 Guest 实机交付通过。

## 协作与非目标

连续两轮完整双轴任一非 CLEAN 时暂停并直接求救专家；经专家 G1 与主调度登记后，本票仅恢复一个全新双轴复审窗口，仍阻塞即停止并回根。必要的跨票生产入口协调同步主调度。若查证 R01 的交付实际成为前置，再登记真实依赖，不预先制造线性边。无 LMN、新 ACK、原地 reset 或旧 Writer 回退。

**Approval:** 用户于 2026-10-14 批准四票拆分并授权自动逐票调度。


## Approved continuation: R02-AC7-Collision

Scope is limited to exact identity matching in `ItemExecutionPort.ClearObserverFailuresForConnection`, its collision regression and required ticket/delivery/audit evidence. Preserve every existing region/failure-kind entry for the disconnected observer/token while retaining colliding identities. Do not migrate other key families or alter G1 delivery retry behavior.

The pre-fix production-path collision is captured by IT144: disconnect observer 5/token 7 must clear its failures in multiple regions and kinds without clearing observer 7/token 7 at region Y=5. The complete R02 candidate from `3fb2eb5e87ab3eddbc6782fcc708418a7d11d52a` passed the one fresh independent Standards/Spec round authorized for this increment (R02 cumulative round 4): both axes are CLEAN. Reviewer reports and native provenance are recorded under `.scratch/matt-orchestrate/item-runtime-repair-20261025-vela/messages/`. Static acceptance is complete; Guest Runtime remains pending under the separate human test plan.
