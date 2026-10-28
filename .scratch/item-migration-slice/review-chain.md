# Item 规格草稿与 Q10 文档审查链

日期：2026-10-07；本轮是规划文档审查，不是生产/TDD/运行验收。

## 固定范围

- 插件 HEAD：`57e3c4e4411b4f8831f0bf6d489b9fea9922c47c`。
- 本轮文件：本目录 issue/spec/planning-inputs/ticket-plan/recovery-carrier-facts/review-chain，ADR0023/0024、CONTEXT 的 Item Preparation Readiness，以及原地图 Q10 裁定/草稿与恢复选择索引追加。
- 历史依据：ADR0019–0022、已审查原生事实、用户 Q10“是，确定”。旧文档中当时未决的记录保留。
- 测试入口、票图、数值仍为提案；用户未确认前不发布 ready-for-agent。恢复载体随后由用户选择A，见ADR0024；命令文字仍属工程提案。
- 没有修改生产源码、测试、DLL、稳定区或部署；无新 Case-ID/运行 PASS。

## 文档验证与审查

首轮文档验证覆盖42个相关Markdown，局部链接/空白错误均0；插件HEAD一致，tracked diff仅CONTEXT（含此前已审决策），新草稿显式纳入审查。后续新增恢复文档后须再检。

工具不支持 `subagent_type` 时在各独立任务提示中显式指定角色；这是工具限制说明，不将一个通用上下文伪装成两个独立角色。各轮新建两实例，不续用旧审查官。

### R1

- Standards：`/root/item_spec_draft_standards_r1`，CLEAN，阻断0。
- Spec：`/root/item_spec_draft_spec_r1`，BLOCKED，阻断1：01的准备/首次启计已需要正确来源与嵌套实际掉落保留，不能留至依赖01的02再解决。
- 调度说明：并行启动第二轴两次遇到agent thread limit，待槽位释放后另建全新Spec上下文；没有以旧实例续跑冒充新轴或宣称该轮并行成功。R1不构成最终CLEAN。
- 修正：首票纳入准备来源、嵌套掉落保留及年龄隔离；02验证后续持续创建/周期部分，缺口责任同步前移。准备动作的真实结果也归首票必要条件，03另验返区结果闭环。
- 新增：恢复载体源码补查、用户选择A及ADR0024；原生clear wire可复用但没有现成完整原地恢复语义，当前不增加reset接线。

### R2

- Standards：`/root/item_spec_draft_standards_r2`，CLEAN，阻断0、需延期smell0。
- Spec：`/root/item_spec_draft_spec_r2`，CLEAN，阻断0。
- 本轮两全新实例并行调度，独立读取固定范围；均确认首票必要来源/重入/计时条件前移、恢复A的原生边界，以及测试入口/工程数值/票图未批准的草稿状态。
- 最终文档检查覆盖44个相关Markdown，局部链接缺失0、空白错误0；插件HEAD仍匹配。tracked diff仅CONTEXT，其他显式规划输出作为untracked增量审查，没有生产源码或DLL变更。
- 结论：本轮规划增量双轴CLEAN。仅授予可交用户审阅的文档状态，不发布实施票、不授予TDD/BuildArtifact/Runtime PASS。以上结果和检查的行政回填属于两审查官许可范围。

## 未关闭门

`GAP-C-P2-IMPLEMENTATION-TDD` 及来源/时间、重入创建、原生结果、高层集成缺口由实施票解决；三端运行、部署指纹和冻结归档由最终候选验收。本图其他领域决策、跨图通信票及后续迁移顺序未关闭。
