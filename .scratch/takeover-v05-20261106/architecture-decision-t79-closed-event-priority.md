# T79 右截断与已闭合 M1：已证事件保留，尾段单独未知

2026-11-07，gpt-architect。只读裁决；未改生产、运行测试/游戏或部署。下述为后续实现的唯一语义，不是现有DLL已具备的结果。

## 1. 唯一结论与适用边界

**正常窗口末尾的停段没有未来释放帧，只令该尾段 RIGHT_CENSORED；不得推翻更早证据完整、已闭合的 M1 或 AUTH 事件。** 有已闭合M1就报告M1及其真实候选三元组，同时明确尾段尚不能判、事件扫描不穷尽。若只有未闭合停段，仍UNKNOWN，不改成零事件通过。

这是**判定语义的范围/优先级勘误**，不是降低证据完整性门槛：“已经存在一个完整反例”不需要等未来观察结束才能成立；“完全没有反例”则不能由截断观察证明。否则用户每次走完停下，已见的顿挫都会被最后一次停车吞掉。

仅局部化“未来释放帧尚未出现”，不局部化真实采集失败。缺FRAME/原生相位、输入关联、相机重复、时钟/身份/端点错误、暂停/配置变化、未知尾部断采等仍保留既有UNKNOWN优先。本票不授权全面分段容错或裁掉坏帧制造有效窗口。

来源：[T75工程复审实证](review-eng-t73-m1xz-r1.md#L15-L16)中，已有m1=1却A/B/R=-1且Unknown；[规格轴旧优先级记录](review-spec-t73-r1.md#L127)。数学上的XZ、2cm、相对落后和收敛仍按[T71](architecture-decision-t71-m1-relative-motion.md)，不变阈值。

## 2. 必须同时表达事件事实与观察边界

保留 `m1_rule_revision=M1-XZ-RELATIVE-v2`；新增 `event_aggregation_revision=CLOSED_EVENT_PRIORITY-v1`，不能用同一未区分版本的结果覆盖历史文件。

| 字段 | 固定含义 |
|---|---|
| label / reason | 已完整验证的代表事件类别；若没有闭合事件且存在右截断，Unknown / RIGHT_CENSORED |
| valid | 所报告结论具有必需证据；有已闭合事件时可为true，**不再表示所有潜在停段都有释放帧** |
| strict_m1_events | 已闭合且完成全部必需验证的M1件数；不计尾部开放停段或未验证候选 |
| candidate_a / candidate_b / release_frame | 代表闭合事件的真实帧号；不可被尾段覆盖，不能拿尾段末帧充释放帧 |
| selected_event_complete | 代表事件的停段、释放、同点A/V、落后/收敛及关联证据齐全；无代表事件为false |
| event_scan_complete | 所需数据完整且没有尚未判定的右截断停段；有右截断一律false |
| right_censored_segments / right_censored_ranges | 截断停段数及每段a/b、release_frame=-1、reason=RIGHT_CENSORED；与正式候选分开 |
| strict_m1_events_is_lower_bound | 有右截断时true；计数只说明已确认件数，不能当整步完整总数 |
| closed_events | 每件已确认事件的kind/a/b/release与evidence_complete，供核对计数；可导出到同次事件索引，不要求复制全部帧或新增UI |

每个计入事件的证明域为其真实 `[a,release]`：引用相应原始帧/事件序号、角色/代次/相机与时钟、必需FRAME/原生相位、输入关联，以及T71同相位A/V与释放谓词。不能借尾段或窗口外SIM补齐。关联须在**每件计数前**验证，不能只验最后选出的最长代表段而让其它未验证段进入计数。`closed_events.Count(kind=M1)`必须等于`strict_m1_events`。

消费端未识别该aggregation revision、或缺`event_scan_complete`时，不得只看`valid=true`/M1计数0授予完整零事件通过；导出器和汇总器须同轮更新契约，不能靠文档脚注掩盖字段含义变化。

“正常停段未见释放”和“记录丢失导致未见释放”分开：只有前者采用局部右截断。`WindowTailUnexplained`、`PHASE_STOPPED_MID_WINDOW`、`CAPTURE_FAILURE_TAIL`等仍失败关闭。若外部`w.RightCensored=true`却不能定位对应尾段，或含义不明，维持UNKNOWN，不把它当可忽略警告。

若出现其它真正完整性失败，主结论仍Unknown/valid=false；原先扫描出的几何候选仅可作为未确认诊断，不挂在`strict_m1_events`里冒充确认事实。本次只改变正常右截断这一例外。

## 3. 可直接写成断言的标准组合

采用完整原生相位/输入关联的合成夹具，frame100…110、每帧dt=1/75s、cap=5U/s，Y/Z=0：

| frame | A.x | V.x |
|---|---:|---:|
| 100 | 0 | 0 |
| 101 | .02 | 0 |
| 102 | .04 | 0 |
| 103 | .06 | 0 |
| 104…110 | .08 | .08 |

前段[100,103]停住、104释放；104以后同点静止到采集正常结束，尾段[104,110]无未来释放。必须输出：

- `Label == Bug1Label.M1StallCatchup`；序列化`label="M1StallCatchup"`、`reason="M1_STALL_CATCHUP"`。
- `Valid == true`、`StrictM1Events == 1`、`CandidateA == 100`、`CandidateB == 103`、`ReleaseFrame == 104`。
- `selected_event_complete == true`、`event_scan_complete == false`、`strict_m1_events_is_lower_bound == true`。
- `right_censored_segments == 1`；唯一范围`a=104,b=110,release_frame=-1,reason="RIGHT_CENSORED"`，不得改成[100,110]。
- `closed_events`恰有M1(100,103,104)这一件。其所有同点位置、输入/原生关联与释放证据均来自100…104；105…110不参与其成立。

这是规格夹具，不是声称当前生产已经通过；现有原canonical正例是[0,3]/4，可等价平移100后追加六个静止点，原断言见[Bug1-01](../../WhitelistTests/Evidence/Runtime/Bug1ProbeJudgmentTests.cs#L95-L115)。

唯一汇总顺序：在其它完整性先验通过后，**闭合M1 > 闭合AUTH > 仅右截断Unknown > 完整且无已分类事件NoThresholdEvent**。M1优先于AUTH；同类代表仍选停段最长，等长选最早a、再最早release。全部合格件均计数，代表三元组不代表全部件。

闭合AUTH后有尾段时同理保留`AuthStepLocked`及其三元组、M1确认数0、扫描不穷尽；不能由此声称整步M1总数为0。只有正常右截断、无任何闭合事件时，`Label=Unknown,Reason=RIGHT_CENSORED,Valid=false,StrictM1Events=0,CandidateA=CandidateB=ReleaseFrame=-1`，并保留截断范围。

业务含义：已证M1一件足以否定该步“无M1”的验收目标；尾段未知不会免除该失败。反之0件且`event_scan_complete=false`不得授单步完整零事件PASS，更不能宣称M2画面平滑/Bug1已修。

## 4. 最小落地点与防回归范围

定位本轮读取的[Judge](../../Platform/Diagnostics/Patches/Bug1ProbeJudge.cs#L207-L261)：

1. `ValidateContext`里对裸`w.RightCensored`的全局否决（318附近）改为保留并定位截断范围；其它完整性门不动。定位失败仍UNKNOWN。
2. [ClassifyHorizontalMotion:429–434](../../Platform/Diagnostics/Patches/Bug1ProbeJudge.cs#L407-L434)遇无释放的尾段时记局部censored，不`MarkUnknown+return`丢掉先前闭合结果；完成代表事件填充与汇总。不可捏造release、借用尾段SIM、提前放行其它完整性失败。
3. [249–261](../../Platform/Diagnostics/Patches/Bug1ProbeJudge.cs#L243-L261)标签、计数、代表三元组从已验证闭合件一致生成；[508–532](../../Platform/Diagnostics/Patches/Bug1ProbeJudge.cs#L508-L532)代表选择保持M1优先。必要新增小型证据域/右截断DTO与导出字段，result/文本摘要一起说明不穷尽，不能只改内存字段而让交付摘要仍写Unknown或“完整零事件”。
4. 自然尾部删掉/隐藏警告、把所有Unknown取消、或仅把`Valid`硬改true均不合格。捕获范围/原始trace/result不改写。

| 用例编号 | 必须保留或改变的期望 |
|---|---|
| Bug1-01 / Bug1-70(R01) | 原闭合M1[0,3]/4不变；扩展增加“再接静止尾段”子例，必须保留M1与三元组，非Unknown |
| Bug1-06 | [纯右截断现例](../../WhitelistTests/Evidence/Runtime/Bug1ProbeJudgmentTests.cs#L195-L204)仍Unknown；补strict=0/三元组=-1/范围断言，不能把它改成PASS或删除 |
| Bug1-71(R02) | 同步闭合AUTH原期望不变；追加尾段应保留AUTH且扫描不完整 |
| Bug1-78(R08) | [较长AUTH+较短M1](../../WhitelistTests/Evidence/Runtime/Bug1ProbeM1RuleTests.cs#L335-L359)原代表8/11/12不变；再接尾段仍保留它，不被最长尾段遮蔽 |
| Bug1-80(R10) | [纯censored子例](../../WhitelistTests/Evidence/Runtime/Bug1ProbeM1RuleTests.cs#L388-L414)仍Unknown；缺相位/相机/暂停/输入关联/端点错误仍Unknown优先。新增“早M1+尾部真实缺相位”负控，不能套用本裁决豁免 |
| Bug1-72(R03) | 输入存在仍是原真实段AUTH/M1=0，保存原误报result不变；输入缺失必须明确未执行回放、不能PASS |
| Bug1-73/74/75/76/77/79/81 | XZ/2cm/落后/收敛、慢同步微动、SIM旁证及其它动作守卫不变；全套回归，不因优先级调整改变数学门 |

新增子例可挂既有ID并标明子例名，或由集成者分配未占用新ID后重新统计；这里不冒占队友的新编号。还需一个两件闭合M1+尾段：计数2、closed_events两件、代表按规则稳定；以及外部censor标志无法定位时不放行。

变异必须真实变红：恢复尾段`return`→早M1+尾段子例红；用尾段覆盖候选/捏造释放→三元组/范围断言红；去截断标志或把扫描完整硬置true→完整性字段断言红；跳过候选关联检查→故意损坏该事件关联的子例红。不能只断言`StrictM1Events==1`，那正是当前错误字节也会满足的假守卫。

## 5. 766/768、R03缺输入与SKIP的唯一记账口径

[历史工程报告](review-eng-t73-m1xz-r1.md#L40)和[规格复审](review-spec-t73-r1.md#L104)的766/768，是**当时真实trace存在并实际执行R03**的那次输入/字节结果：766 PASS、2 FAIL（GBA01/NIL01环境失败）、0 SKIP；exit1，不是全套CLEAN，不是本票新跑结果，也不自动适用于后续字节。

当前[R03:163–170](../../WhitelistTests/Evidence/Runtime/Bug1ProbeM1RuleTests.cs#L160-L181)已改为缺trace抛异常，且[runner:988–989](../../WhitelistTests/Program.cs#L975-L989)只有pass/fail维度。**本轮保持这种缺输入明确FAIL的最小方案，不要求为此新增SKIP框架。** 回放没执行的事实要显式记`R03 replay_executed=false / MISSING_TRACE_FIXTURE`，不能把“测试入口调用过”写成回放通过。

在固定旧768个注册项、其它结果完全相同的前提下：

| 场景 | 必须报出的完整账 |
|---|---|
| trace存在、身份正确并实际回放通过 | Registered=768, PASS=766, FAIL=2, SKIP=0；766/768 PASS（2环境失败），exit1 |
| trace缺失，当前runner保持FAIL | Registered=768, PASS=765, FAIL=3, SKIP=0；765/768 PASS，R03缺输入额外失败，exit1 |
| 以后正式runner真有SKIP支持且该项标SKIP | Registered=768, PASS=765, FAIL=2, SKIP=1；不是766/768，也不能仅写765/767掩盖未跑项 |

恒等式 `Registered=PASS+FAIL+SKIP+NotRun`；不把SKIP移到PASS，不从注册分母删去。若显示“执行项通过率”也必须并列注册总数、SKIP ID及原因，真实回放仍未验证，不得因SKIP获得R03验收。GBA01/NIL01不得为了漂亮数字改PASS。新增子例/注册ID或环境变化后按实测新N重算，绝不沿用765/766常量；每次绑定测试二进制、加载生产DLL、输入trace SHA/路径、报告和命令/exit。当前文档不替代T77的实际回归。

## 6. 取代关系与交付门

本裁决仅取代[T71](architecture-decision-t71-m1-relative-motion.md)及[T4](bug1-instrumentation-spec.md#L139-L149)中“右截断一律把整步已闭合事件作废”的解释，并收紧其报告完整性维度；不变同相位必要滞后、XZ、2cm、原速度帧界、不补样/不改原始记录，不把缺采当正常右截断。旧文件与旧判定保留，按新增aggregation revision重判。

将更早已闭合候选保留是生产判定与导出语义变化，**必须重新binding实际源/测试/生产DLL SHA/MVID，完成真实生产Judge子例Red→Green、上述变异红，以及独立工程/规格双轴复审**。不属于T57的生产零字节单轴例外；适用复审CLEAN前不得发新版诊断件。本票不改生产、不更换既有DLL、不把审查中的T77字节预先批准。
