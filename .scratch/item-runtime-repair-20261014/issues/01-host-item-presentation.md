# R01 — Host 刷新物品只有一份有效呈现

**What to build:** 房主看到的每个权威刷新物品只有一份可交互的地面呈现；拾取、入包、丢下及区域重入不遗留光标或附近栏能识别却无法交互的幽灵副本。铁铲只是示例，覆盖多种刷新物品。

**Blocked by:** None — can start immediately；不存在对 R02/R03 的强制依赖。

**Label:** ready-for-human
**Status:** implemented-pending-runtime — R01 源码/静态交付已完成；真实 1 Host + 2 Guest Runtime 仍待人工验收。
**Version:** SPF 0.2.4.10 Experimental / 实机返修 R01

## 已知事实与证据边界

- 用户在 1 Host + 1 Guest 测试中确认：Host 所见所有刷新物品均伴随幽灵副本；不能限定为铁铲资产问题。
- 失败候选为提交 01692ecc4aa0d2041c3f56a718cb7ab96ec1154b；领取时另记实际 HEAD 和受保护的用户增量。
- 专家确认候选条件分支与共享本地呈现存在组合验证缺口；本次重复物品身份和根因尚未确认。
- 权威仓库重复与本地队列/模型重复必须分别判断，不能预设幽灵一定与真实物品使用同一身份。

## Acceptance criteria

- [x] 同一实际生产入口覆盖共享 Host 呈现→原生区域更新、原生区域更新→共享进入两种顺序；测试替身仅替代 Unity/网络等外部动作，不替代生产分支或物品政策。
- [x] 在修复前实际运行针对重复呈现行为的 Red；记录命令、失败输出、生产入口和边界，不接受孤立 getter 或 fake 呈现次数充当组合复现。
- [x] 对照原生权威物品、待实例化队列和 Host 模型关联，覆盖多种资产、队列未消费/部分消费/全消费、重入及刷新增量；每份当前权威物品至多一份有效呈现。
- [x] 拾取成功后模型与待实例化项不会残留或复活，入包再丢下产生正常当前物品；Guest 区域事件不会额外驱动 Host 本地呈现。
- [x] 根据 Red 定位的层修复生成或呈现交接，不建立第二物品仓库、不恢复旧 Writer、不以资产特例或全图清理掩盖问题。
- [x] 如现场物理状态无法自动复现，明确记录缺口；限量关联模型身份、权威身份与队列来源，不以接缝 Green 宣称现场幽灵消失。
- [x] 保留原生增删/拾取事务、唯一 Item Authority Writer、共享身份/结算完成门、Resource/Collision 和 SP/Guest/U3DS 行为及 07 距离边界。
- [x] 完成必要回归、全新独立 Standards/Spec 双 CLEAN、限定提交、delivery/audit 和真实主会话回报；源码状态仅为 implemented-pending-runtime。

## 协作与非目标

同票返修复用构建会话；连续两轮完整双轴任一非 CLEAN 时暂停并直接向常驻专家求救，主窗口接收状态。不改已交付票的历史结论，不部署或授予 Runtime PASS。

**Approval:** 用户于 2026-10-14 批准四票拆分并授权自动逐票调度。

## R01 实施交付（2026-10-25）

源码/静态验收、原候选双轴审查、恢复候选 Release 构建与 602 项 Whitelist 回归已完成；审查历史、null 探针处置、产物身份和未完成 Runtime 边界见 [`delivery`](01-host-item-presentation-delivery.md) 与 [`audit`](../../../audit/2026-10-25/Implementation-0.2.4.10-R01.md)。幽灵实体是否消失尚未由人工候选复测确认；本票保持 `implemented-pending-runtime`，不得据此部署或报告 Runtime PASS。
