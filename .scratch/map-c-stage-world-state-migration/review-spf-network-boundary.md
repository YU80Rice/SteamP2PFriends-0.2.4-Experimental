# SPF 与功能模组通信范围纠正审查链

- 日期：2026-10-07；插件基线 HEAD=`57e3c4e4411b4f8831f0bf6d489b9fea9922c47c`。
- 来源：用户指出“为什么要接入LMN……这不是联机插件SPF吗？你是不是搞混了”。
- 范围：ADR 0021、CONTEXT 两个职责术语、地图/背景接收/运行边界票、原生首轮调查的 LMN 范围语句、同步补查的适用范围与审查链，以及本记录。
- AGENTS.md、CONTRIBUTING.md、跨图 Ticket 4、既有 ADR 0015–0020 不修改；不把用户澄清延伸为关闭所有通信讨论或禁止 SPF 将来使用自定义消息。

## 独立审查

| 轮次 | Standards | Spec | 处理 |
|---|---|---|---|
| R1 | CLEAN（阻断/气味 0） | CLEAN（阻断 0） | 两个全新 fork_turns=none 独立上下文完成范围纠正复核 |

Codex 没有原生 subagent_type 字段；prompt 明示 reviewer 角色，不宣称 native zcode 调度。

## 纠正与边界

- 原生源码事实与此前基线/发送完成限制继续有效；撤回的是主 agent 对 LMN 规则适用对象及本 P2 必须接入的推断。
- `GAP-C-P2-LMN-CONTRACT` 因不在当前范围而撤回，不是通过 LMN SDK 验收关闭。
- Q8–Q10 未获裁定；不将重写传输假设引出的加载/拾取门控当已批准需求。
- 当前 Item 仍迁入共享控制面并应用已接受周期政策，继续复用原生仓库/协议/交互；实施 TDD 与三端运行门禁保留。
- 所有改动仅文档，无生产源码、测试、构建、部署、发布或 Runtime PASS。

## 文档核验

- 当前 33 个相关 Markdown：局部链接错误 0、whitespace 诊断 0；CR 作为合法行尾，未修改仓库配置。
- HEAD 与基线一致；tracked diff 仍仅 CONTEXT 术语变更，没有生产、测试或构建变更。
- 本轮 apply_patch 目标未包含 ADR 0015–0020、AGENTS/CONTRIBUTING 或跨图 Ticket 4。未保存这些文件的本轮前置指纹，因此不宣称未跟踪历史文件已完成 hash 前后比对；“未修改”描述本轮操作范围。
