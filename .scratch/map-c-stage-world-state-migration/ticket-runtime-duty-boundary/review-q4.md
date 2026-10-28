# Q4 原生规则补算精度决策文档审查链

- 日期：2026-10-07；插件基线 HEAD=`57e3c4e4411b4f8831f0bf6d489b9fea9922c47c`。
- 范围：问询票 Q4 Comments/状态、ADR 0018、CONTEXT 补算定义、地图 Notes 和本记录；不改写历史 Q1–Q3 与 ADR 0015–0017。
- Spec 来源：[原生职责复用与按需运行的边界是什么](ticket.md) 的 Q4 推荐 A 及用户“A，按原生规则”。

## 独立审查

| 轮次 | Standards | Spec | 处理 |
|---|---|---|---|
| R1 | 未完成（代理报告 usage limit） | 未启动（spawn 报告 agent thread limit reached） | 本轮无有效双轴 verdict，不计入 CLEAN 链 |
| R2 | CLEAN（阻断/气味 0） | CLEAN（阻断 0） | Q6 复核成功后，以两个全新上下文完成 Q4 与背景接收增量复核 |

Codex 接口没有原生 subagent_type 字段；各 prompt 明确声明 reviewer 角色，使用两个全新上下文，不宣称使用 zcode 原生代理接口。

`GAP-C-P2-Q4-DUAL-REVIEW` 随 R2 两轴 CLEAN 关闭；R1 的代理配额/线程失败记录保留，不计入有效审查。仅关闭文档复核缺口，不解除后续实施与运行门禁。

## 边界

- 按原生配置速率、状态条件和事件顺序补算；不要求复刻逐帧/卡顿偏差，不因此允许改变天气资格、供电条件或配置速率。
- 时钟、相位、舍入/阈值、调度延迟和验收容差仍待明确；未冻结 Item 生成/刷新或非时间型对象政策。
- 本轮仅问询文档，没有生产、测试、构建、部署、新 DLL 身份或 Runtime PASS；`GAP-C-P2-IMPLEMENTATION-TDD` 保留。

## 文档核验

- 当前 20 个相关 Markdown：局部链接错误 0、whitespace 诊断 0；当次检查将 CR 作为合法行尾，未修改仓库配置。
- HEAD 与基线一致，tracked diff 仍仅 CONTEXT 术语变更；本地自查不是独立审查 verdict。
- 直接核对 SDK InteractableGenerator.cs:254–283：OnEnable 重置 lastBurn；Update 在 elapsed > burn 时重置时间并最多 askBurn(1)，与 Q4 提问事实一致。

## 本轮收束

用户在本轮要求“这一轮结束后，我要向你同步一些信息”。因此完成 Q4 决策记录与本地核验后等待用户同步；Item 首次到访问询尚未提出或裁定。为该问题派出的只读事实补查代理也遭 usage limit，中间回报不作为完整受审查事实文档交付。
