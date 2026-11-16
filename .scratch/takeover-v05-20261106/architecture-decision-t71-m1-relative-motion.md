# T71 M1 误报裁决：同相位相对落后是必要条件，走/跑判据限定水平运动

2026-11-07／gpt-architect。只读裁决及原数据复算，未改生产/运行游戏。规则修订号 **M1-XZ-RELATIVE-v2**；以下是一套确定规则，不由实现者另选阈值或标签。

## 1. 唯一结论

**M1_STALL_CATCHUP必须有：停段内同一RENDER_READY的可见载体确实落后于权威位置，并在释放时向权威位置收敛。A/V始终重合的窗口，无论SIM内部曾怎样运动，永远不能判M1。**

本次frame11562–12215的M1是错误分类。原来的“全时域SIM+FRAME RootSpan三维跨度>epsilon即M1”把未显示到画面的1cm垂直微调当成呈现落后，且缺少A/V相对证据。**这是原T4判定契约自身的缺陷，不只归责Builder实现。**

走/跑子判据统一改为XZ水平投影；绝对事件尺度固定 **D_motion=0.020U（2cm）**。epsilon仍为原采样分辨率，本例epsilon_u=0.0017320508075688772U；原生速度cap与实际渲染dt不改。纯Y变化不参与这个走/跑候选，但保留原始Y数据供跳跃/落地/校正独立判定。

**本次原候选的正确事件归类是AUTH_STEP_LOCKED，不是M1，也不是NoThresholdEvent**：其后12216确有0.341643U水平释放，超过原速度帧界，且A/V同拍释放。不能为了撤销一个假M1把仍存在的权威步进候选也擦掉。它不授予Bug1 PASS。

## 2. 实际复算：分母、相位与释放帧

来源：[trace](../../../启动器/UnturnedModManager/publish/UMM-v2.2.1-win-x64/bug1-20261107T111121Z/HOST/trace.jsonl)、[result](../../../启动器/UnturnedModManager/publish/UMM-v2.2.1-win-x64/bug1-20261107T111121Z/HOST/result.json)。只读脚本：[t71_trace_audit.py](t71_trace_audit.py)。trace SHA-256=`f0627abd6e1da009bf30fe49ff91fa0128fa577614e89491afb5db37eb0580f8`。

| 复算对象 | 事实 |
|---|---|
| 全文件 | 18417行；RENDER_READY=5737、BEFORE_UPDATE=5736；after enqueue=1735、after dequeue=1735、SIM before/after各1735，另有RX_REJECT=1 |
| A/V对照 | 5737/5737个RENDER_READY的a_pos、v_pos逐坐标完全相同；不是5736，也不是11379对 |
| valid分母 | 总FRAME valid=true为11379；其中5643个RENDER_READY valid，94个无效实际在RENDER_READY。BEFORE_UPDATE记录a_pos_before_update，不记录v_pos，不能拿它另造一对A/V |
| 停段11562–12215 | 654个RENDER_READY的a_pos/v_pos X/Y/Z范围全部0；没有在渲染采样时刻出现权威先动而载体不动 |
| 同停段SIM | 248点；a_pos范围dx=dz=0、dy=0.01000213623046875U。旧判定量化后root_span≈0.010U，全部是内部Y变化，不是可见相对滞后 |
| release12216 | dx=0.2103271484、dz=-0.2692260742、dy=-0.1135864258U；XZ=0.3416433644U，三维=0.3600306439U，A/V仍相同 |
| 帧界 | release真实mono dt=0.0260173s；按cap4.5，B=4.5×dt+epsilon=0.1188099008U；水平release仍超过B，也超过2cm绝对门 |
| 当前旧结果 | valid=true、M1StallCatchup、strict_m1_events=1，n_expected=observed=5736、head_excluded=1。这个M1标签应撤销，保留原文件供审计，不能覆盖原始结果伪装它从未误报 |

model_pos全程缺失/null，interpolation_unavailable有该字段。可确认的是**第三人称载体v_pos与movement根a_pos在已采RENDER_READY上重合**；不能把11379个valid FRAME包装成11379次真实mesh/model_pos对照。该事实独立于分类器，因此撤销假M1不影响它；“所有骨骼/顶点都无插值”仍不是这组字段证明的命题。

新增lerp_factor_clamped恒1/remote_input_tick不刷新也不证明本地Lerp在远端实际执行。[T61分支证据](bug1-root-cause-t61.md#L1-L35)仍须适用：source中local Lerp受IsLocalPlayer保护，不能把诊断计算值当成实际写者执行记录。

## 3. 必要谓词：只在同一可见相位判相对落后

### 3.1 固定量与字段

先沿用完整性/身份/相位、camera、端点、input关联与固定条件验证。无效/缺采样仍UNKNOWN，不能把本次裁决当免检。取同一有效step的主相机RENDER_READY，以mono_ticks、event_seq排序；只比较同一行的a_pos、v_pos，禁止用SIM.a_before配RENDER_READY.v_pos。

设 `H(p)=(p.x,p.z)`，`A_i=H(a_pos_i)`、`V_i=H(v_pos_i)`，`eps=epsilon_u`，`D=0.020U`。它们须为经核验、同坐标系/同原点意义的权威根与可见载体。若另一个可见端点存在固定水平offset，先以独立绑定证据校准到同一原点；不能在每个窗口取初始A-V偷偷抵消真实落后，未证校准则UNKNOWN。

新增输出字段至少包括 `m1_rule_revision`、`motion_axes="XZ"`、`horizontal_motion_min_u=0.02`、`render_root_span_xz_u`、`sim_root_span_3d_u`、`lag_witness_frame`及该行A/V、`lag_before_release_u`、`lag_after_release_u`、候选拒绝原因。不能沿用一个无轴含义的root_span让审计者误会。

### 3.2 水平停段候选C_H

对每个最大连续小位移段 `[a,b]`：至少3个连续渲染帧间隔（4点），所有水平单步 `||V_i-V_(i-1)||≤eps`。保留原逐帧分辨率定义，不新增“累计跨度也必须≤eps”的硬门；停段累计范围另外记录，不能仅因总位移变大就把同步慢移误认成相对落后。

令释放帧 `r=b+1`，`dt_r=(mono_ticks_r-mono_ticks_b)/mono_frequency`，`B_r=native_speed_cap(step)×dt_r+eps`。只在 **`||V_r-V_b||>B_r` 且 `||V_r-V_b||≥D`** 时形成水平候选。相等于B不算；2cm绝对门是包含边界的≥。不得把dt换成RATE、平均FPS、录像帧长。

纯垂直release的水平距离为0，因此**完全不构成C_H**；只有Y的停段SIM噪声也不增加水平权威进展。边界未闭合按原RIGHT_CENSORED处理，不准因看不到release记零事件。

### 3.3 M1：相对落后证据必须出现在停段内，不在释放后补造

对有效C_H，必须同时满足以下两组：

- **落后见证**：存在 `i∈[a+1,b]`，取 `T_i=A_i-A_a`，满足 `||T_i||≥D`、`||A_i-V_i||>eps`，且 `dot(A_i-V_i,T_i)/||T_i||>eps`。它明确要求至少2cm已显示的权威水平推进，而且载体在该推进方向后方；仅“V!=A”可能是offset/领先，不充分。
- **向权威回补**：令 `L_b=||A_b-V_b||`、`L_r=||A_r-V_r||`；要求 `L_b-L_r>eps` 且 `dot(V_r-V_b,A_b-V_b)>0`。既要原速度帧界被越过，又要释放移动确实朝原来落后的方向、相对差距可测地收敛，不能把任意大跳都叫回补。

因此 `M1_STALL_CATCHUP = valid && C_H && lag_witness && error_converged`。**若全部同点A_i=V_i，则L_i=0，lag_witness必假，M1数学上不可能为真；提高/降低D都不能改变这一点。** SIM中的真实瞬时推进仍记原始事实，但不能替代这个相位内见证。

### 3.4 AUTH_STEP_LOCKED与无事件

有效C_H的停段内若 `max||A_i-A_j||≤eps`，且释放的两者增量匹配 `||(A_r-A_b)-(V_r-V_b)||≤eps`，归AUTH_STEP_LOCKED：停段根也没推进，随后根/载体同拍步进。它不是M1失败、也不是人物已平滑。

既不满足M1、也不满足AUTH的几何段，不硬塞进两者；记 `NON_M1_HORIZONTAL_PATTERN`原始候选/拒绝原因，继续搜索其它段。所有完整有效段均无两类事件才NO_THRESHOLD_EVENT；采样不足仍UNKNOWN。没有M1不能推出M2画面正常。

**原候选11562–12215明确满足AUTH条件**：render root spanXZ=0、V spanXZ=0，releaseA/V增量完全相同；也仍满足C_H。若修正后把这个具体事件再报M1，除非提交与当前原文件不同的、在停段内同RENDER_READY实际A/V分离且完成上述推进/收敛证据，否则不接受；仅展示SIM的1cm Y或重复旧root_span不是证据。

## 4. 2cm门的依据与防止为结论调参

选择固定水平**事件尺度**2cm，不用native_speed_cap×RATE的比例：后者会随技能/跑速增大，反而让更快的异常更容易被忽略。它也不是把记录分辨率eps改成2cm。

本精度q=0.001U时，2cm=20个记录网格，约11.55倍eps；远小于普通4.5U/s×0.08s=0.36U原生步长，也小于题设75FPS普通walk的0.06U均匀帧量。它把“毫米级数值/接地微调”与“厘米级水平推进事件”分开；**不冒称2cm是人眼通用感知极限**。在固定q网格上可用整数水平差的平方和与400比较，避免20格边界被浮点减法误落到19.999格；更换采样精度必须重新声明换算规则，不加结果导向容差。

这是一项明确、前置登记的规格修订，不是假装旧规格没变。当前假M1即便D=0也会因A/V没有相对落后被否定，Y轴又完全不参与C_H；因此2cm不是为了删除当前一个数值才挑的白名单。**原B、停段eps、有效性门全部保留，不许调高速度边界或加大eps换Green。**

代价明确：低于2cm的真实水平微小相对异常不在此M1事件尺度内，可保留微动统计与M2画面片段；不能把它们说成不存在。修订后的零M1只属于这套明示范围，不授Bug1完成PASS，也不以此回写“本轮真实游戏已经修好”。

## 5. 实施接缝与用例：不要只给旧最长段换标签

当前 [Judge:207–236](../../Platform/Diagnostics/Patches/Bug1ProbeJudge.cs#L207-L236) 找一个最长段后，直接用ComputeRootSpan>epsilon定M1；[FindLargestStallCandidate:381起](../../Platform/Diagnostics/Patches/Bug1ProbeJudge.cs#L381-L449) 使用三维V距离。应作以下受限改动，**本票不实现**：

1. 先做水平投影与C_H筛选，再按同点lag/收敛条件分型；SIM三维span仅作旁证输出，不能作为M1布尔开关。
2. 搜索全部最大连续水平停段并逐段验证，不能先选一个“最长但不合格”的待机段后停止，漏掉较短真M1。代表事件可以仍展示最长合格项，事件计数/ID必须真实；本裁决不要求新增UI。
3. 数据完整性/相位验证保持优先；原生输入/Root/模型/时钟都不改。修判定器不等于修角色顿挫。

Builder必须保留原ID覆盖并增加下列可变红用例（以下为规则用例名，不抢占共享Program里的正式Case-ID）：

| 规则用例 | 预期 |
|---|---|
| R01 原T4正例：V=[0,0,0,0,.08]；A=[0,.02,.04,.06,.08]，dt=1/75，cap5 | M1一次；落后见证在停段、release误差收敛 |
| R02 原同步步进例：A=V=[0,0,0,0,.08] | AUTH_STEP_LOCKED，不是M1/完成PASS |
| R03 本次raw回放：RR A/V同点、SIM仅Y微调、release水平.341643U | 原候选AUTH；strict_m1_events=0，不删除原误报结果 |
| R04 A=V仅Y方向运动：分别测0.01U与0.08U release，水平全0 | 两者均无C_H；0.08U例在dt=1/75、cap5时超过旧三维B，用来揭穿三维候选恢复变异；Y原始数据保留 |
| R05-a A=[0,.02,.04,.06,.08]，V=[1,1,1,1,.08]：释放往回回到A | 虽有大跳/误差收敛，但停段V在前，不得M1；删lag见证应使此例红 |
| R05-b 同样A，V=[0,0,0,0,-.08] | 真实落后却向外突跳，不是回补；删收敛/方向条件应使此例红 |
| R06 19/20/21个1mm水平网格推进与释放边界，分别让其它门成立 | D按≥20格；释放D边界例须取B低于19mm，避免被速度门遮住。B自身仍严格>，不加浮动“近似超过” |
| R07 A/V同步逐帧微动≤eps、累计超eps后同步大步，始终无相对差距 | 不得M1；累计span大也不是落后证据。保留候选/累计范围与拒绝原因 |
| R08 很长无效/微动待机段后有较短真M1 | 不能被最长段遮蔽；真M1仍被找到/计数 |
| R09 只有SIM水平或Y运动、RR始终A=V | 不能M1，禁止跨相位拼出落后见证 |
| R10 缺RR/关联/真实端点、相机重复、暂停/姿态条件不符、截断 | UNKNOWN先行，不能变NO_THRESHOLD_EVENT来凑绿 |
| R11 静止M3 3D漂移、跳跃/落地Y停顿、yaw跨359→0 | 仍走各自原判据，不被本XZ裁决免检 |

反例变异至少包含：只删lag见证→R05-a红（R02本来还受误差收敛门保护，不能谎称单删lag会使它红）；只删回补收敛/方向门→R05-b红；恢复旧SIM三维span>eps为充分分类开关→R03/R09红；恢复3D候选→R04的0.08U垂直release例红；放宽B或删完整性门→原边界/缺样用例红；只检查最长原始段→R08红。上述是待执行的变异契约，不是声称已经测红；R05-b针对移除整个回补布尔组，不声称已独立覆盖其中每个子条件。实现者须用其它门已成立的fixture逐门证明敏感性、记录实际红例，不能把被另一条件遮住的变异写成通过。单测必须走真实生产Judge，不能另写一份判定器假绿。

## 6. 取代关系与交付口径

本裁决自后续修复票引用起，**取代**[requirements-v2 §3.1第2条](requirements-v2.md#L39-L44)中走/跑“推进/停段/补步”的机器解释，加入XZ范围、同相位落后与收敛必要条件、2cm事件尺度；**取代**[T4 §5.1–5.3](bug1-instrumentation-spec.md#L131-L161)三维RootSpan>eps分类与原“两类无条件穷尽”的表述。§3.1若指T4共同记录字段章节，它的原始字段精度不改，只增加本规则revision/判据输出；不误删原始XYZ。

这是T71明确授权的协议勘误，不是Builder看到结果后自行调参：新strict M1范围比旧三维泛化规则更窄，旧/新数值PASS不可混称同一口径，原“不得事后放宽制造Green”仍适用。M1/M2/M3/M4业务目标不变；原M2跳跃/转向、M3静止/传送校正、反向回归不变。历史需求/T4文件不在本票覆盖，旧结果保留，按revision重判而非追认原Green。仅修定义与判定器不能把真实stutter消失当成果：第三载体与权威同拍的统计仍在，原生步进呈现问题仍需T61后续候选/真机检验。

这不是T57生产零变化的“单调加强测试”例外：既改变规格解释又将改变Judge生产字节，必须按新契约真实Red→Green、实际生产接线/变异必红、独立双轴后出新诊断件。未经审查不部署；本票仅交可执行裁决与只读数据校核，未运行SDK/游戏、未修改生产或原始数据。