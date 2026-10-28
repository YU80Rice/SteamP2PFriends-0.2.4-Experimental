# Q8/Q9 正常原生体验与局部兜底审查链

- 日期：2026-10-07；插件基线 HEAD=`57e3c4e4411b4f8831f0bf6d489b9fea9922c47c`。
- 范围：问询票 Q8/Q9/Q10 后续 Comments、ADR 0022、CONTEXT Item Recovery、地图/同步事实索引、本轮原生加载补查与本记录。
- Spec 来源：用户 Q8 接受推荐且要求按原生，Q9 采取推荐，Q10 询问 U3DS 准备并表示推荐似乎稳定；用户强调它们更像兜底。

## 独立审查

| 轮次 | Standards | Spec | 处理 |
|---|---|---|---|
| R1 | CLEAN（阻断/气味 0） | CLEAN（阻断 0） | 两个全新 fork_turns=none 独立上下文完成复核 |

Codex 无原生 subagent_type 字段；prompt 明示 reviewer 角色，不冒称 native zcode 调度。

## 边界

- 正常路径沿用原生自动发送、分预算模型呈现和合法交互，不引入每区整基线/全部模型 ACK 等待。
- Q8/Q9 仅作为未就绪/失败/超时的局部兜底，期限/尝试/提示/重试入口仍待规格化。
- Q10 尚未另行裁定 SPF 开房/进图等待策略；服务器全图数据准备、客机地图加载、区域模型显示分别说明。
- 补查是源码事实，不承诺零延迟/固定帧数，也不宣称已实机证明原生体验。
- ADR 0019–0021 不改写，不新增 LMN 依赖；`GAP-C-P2-IMPLEMENTATION-TDD` 保留。
- 本轮仅规划文档，无生产、测试、构建、部署或 Runtime PASS。

## 文档核验

- 当前 36 个相关 Markdown：局部链接错误 0、whitespace 诊断 0；CR 作为合法行尾，未修改仓库配置。
- HEAD 与基线一致；tracked diff 仍仅 CONTEXT 术语变更，没有生产、测试或构建变更。
