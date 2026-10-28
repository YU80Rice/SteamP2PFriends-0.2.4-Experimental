# Item 同步与 LMN 规格前补查审查链

- 日期：2026-10-07；插件固定 HEAD=`57e3c4e4411b4f8831f0bf6d489b9fea9922c47c`。
- 范围：本研究票、三份 Findings、本记录与地图完成后的索引；只读调查，不重新裁定 ADR 0019/0020 或跨图通信 HITL。
- 来源：用户以 ask-matt 询问下一步；两个 research context 分别调查当前 Item 同步与 LMN SDK 工作树，主 agent 综合。

## 独立审查

| 轮次 | Standards | Spec | 处理 |
|---|---|---|---|
| R1 | CLEAN（阻断/气味 0） | CLEAN（阻断 0） | 两个全新 fork_turns=none 独立上下文完成复核 |
| R2（范围纠正） | CLEAN（阻断/气味 0） | CLEAN（阻断 0） | 用户指出 SPF 与功能模组混淆后，两个全新上下文完成纠正复核；见 [范围纠正审查链](../review-spf-network-boundary.md) |

接口无原生 subagent_type 字段；prompt 明示 reviewer 角色，不冒称 native zcode 调度。

## 边界

- 新发现不等于实机复现；原生常规主线程可靠 lane 顺序不能被无证据描述为必现乱序。
- LMN HEAD/脏树/部署产物分开，不以版本字符串冻结脏树新增 API。
- 不把 Send void 返回或 M2 Commit 当作成功入队/客机完整应用。
- 问询推荐不作为用户答案；本研究关闭只表示事实补查完成，不关闭通信或运行职责 HITL。
- `GAP-C-P2-IMPLEMENTATION-TDD` 保留；后续范围纠正撤回 `GAP-C-P2-LMN-CONTRACT` 作为本 P2 阻塞，LMN 未因此获 SDK 验收。无生产、测试、构建或 Runtime PASS。

## 文档核验

- 当前 31 个相关 Markdown：局部链接错误 0、whitespace 诊断 0；CR 作为合法行尾，未修改仓库配置。
- HEAD 与基线一致；tracked diff 仍仅 CONTEXT 术语变更，没有生产、测试或构建变更。
