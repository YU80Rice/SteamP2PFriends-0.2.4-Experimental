# Bug1 定向测量仪表规格 v1（Builder 执行输入）

> T4，2026-11-06，gpt-architect。**仅新增规格，不含生产实现、真实测量结果或独立双轴批准。** 本轮只允许写本文件；禁止改动生产源码、项目/测试工程、版本及两份冻结输入。后续Builder另获任务后实现仪表，不能把本规格当作已授权部署。

## 1. 冻结输入、目的与写入禁区

- [需求契约 §3](requirements-v2.md#L31-L48)，SHA-256 `17fc427c0e6ed115b680eedfa3119191c558609947051fc241facc4ebb94017e`：M1–M4及停帧补步协议为上位约束。
- [架构裁决 §3.3](architecture-v2.md#L78-L90)，SHA-256 `d1d7ab31e9c4de41c0678b69ac68976a2e4910511c340a29766978a374b42339`：先测Host权威根/可见模型，不直接做插值；剔除求和不是比率。
- 本规格细化观测，不规定修复算法，不要求摘除SPF/U3DS对照，不把SDK完整Editor启动定为唯一入口。真实Unity对象与原生入口不可达则记录具体边界为UNKNOWN。

目标实体：Host进程中的一个已Ready远端Guest；同时满足P2P host、server、非dedicated、目标owner非本机、`channel.IsLocalPlayer=false`、有效SteamID/NetId/connection generation。绑定SteamID字符串、world/session epoch、connection generation、Player/Movement/third/renderer实例；重连不能沿用旧实例记录。Host自身、未批准玩家、dead/vehicle/加载态不纳入走跑主段。只读取既有身份事实，不改变准入/Ready判定。

**权威 A**：原生远端walking `PlayerMovement.simulate(uint,int,int,int,float,float,bool,bool,float)` 执行前后的 `movement.transform.position` 及root yaw；这是服务端物理/判定根，不是网络包内Guest自报position。**呈现 V**：真实Host游戏相机将要绘制的该实体 `player.third` 稳定载体world position/yaw，且必须证明可见角色renderer由该载体/其子层级呈现。二者分别采样并存raw值；不能只记 `V-A`，不能把A线性插值成“连续权威”、不能改任一Transform/controller来改善测量。

禁止以骨骼、头/脚动画点、`Renderer.bounds.center` 或任意同名Transform替代V。动画会移动这些点而不代表整个人平移；取不到稳定third载体或不能证明其renderer归属，就记 `MODEL_ENDPOINT_UNRESOLVED`，不近似顶上。renderer矩阵用于核验实际呈现链，不将骨骼呼吸算漂移。世界坐标系为Unity当前world XYZ，单位U（游戏单位）；Y竖直、yaw度。不得混用localPosition、屏幕像素与world position。

### 1.1 后续实现位置与只读约束

后续实现归SPF现有**兼容/诊断层**，以独立窄模块+实际生产manual registrar接线，复用既有session reset；不属于控制面位置权威，也不新增LMN协议，参见[ADR0021](../../docs/adr/0021-spf-native-world-sync-and-gameplay-mod-channel-boundary.md)。本任务不创建这些源码文件。

可用Prefix/Postfix/只读调用点观察：不得return false跳过原生方法、改参数/返回值、二次simulate、读取/推进NetPakReader、取出队列元素、修改Unity执行顺序/物理/相机/目标帧率/timeScale。需要局部IL锚点才可观察队列消费时，必须完整签名+唯一锚点校验，失配关闭仪表并显式失败，不猜偏移；原生方法仍恰好执行一次。

已知定位资料：[原生输入收到并入队](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerInput.cs#L1397-L1487)、[出队至walking模拟](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerInput.cs#L1723-L1806)、[移动Update写third偏移](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerMovement.cs#L1804-L1838)。SDK有漂移，Builder必须核对本次实际游戏MVID/签名/字段，不把SDK行号当生产地址。

## 2. 采样相位与顺序

| kind / phase | 采样点 | 必记信息与保证 |
|---|---|---|
| `RUN_BEGIN` | 装载身份/预检完成、未开始有效段 | 冻结manifest、路径、阈值、源成员定位、ABI/注册owner+target+PatchMethod证据 |
| `RX / AFTER_ENQUEUE` | 原生ReceiveInputs已完成解析、序号资格检查且真实Enqueue之后 | 只读原包clientSimulationFrameNumber、包类型、队列深度；记录本地rx_id。入口/普通Postfix不等于成功入队 |
| `RX_REJECT` | 原生提前拒绝分支可辨时 | 原生拒绝原因与可得序号；不可得为null，不补读reader，不把拒绝计为丢包 |
| `CONSUME / AFTER_DEQUEUE` | 原生选择包出队后、调用simulate之前 | 原包clientSimulationFrameNumber、rx_id关联、server simulation、输入、原生queue_depth；不以最近收到的包冒充本次消费包 |
| `SIM / BEFORE,AFTER` | 上述真实walking simulate入口及正常返回 | 同一sim_call_id、A_before/A_after、server simulation、实际参数deltaTime/recov及输入；异常另记SIM_ERROR不伪造AFTER |
| `FRAME / BEFORE_UPDATE` | 目标原生PlayerMovement.Update Prefix | 本帧root/third前值、last completed sim_call_id；只读，不压制Update |
| `FRAME / RENDER_READY` | Host实际游戏相机cull/render之前，正常Update/LateUpdate之后 | 本帧最终A、V、renderer矩阵/状态、camera、时钟、上下文；相同frameCount只取指定主游戏相机一次，其他相机不充样本 |
| `CONTEXT / STEP_BEGIN,STEP_END,INVALIDATE` | 剧本标记、暂停/换组/距离越界/实例变化等真实变化时 | step_id、原因、单调时间、frame、旧新身份/状态；不能只在事后挑有效帧 |
| `RUN_END` | 停止采集、队列落盘完成 | 总数、phase覆盖、丢记录/缓冲溢出/写盘错误、完整性hash、结束原因 |

`RENDER_READY` 可由匹配渲染管线的主相机回调实现；必须用实际调用顺序证明晚于角色最终写入，不能仅凭“LateUpdate”名称假定最后。若相机回调后仍有未观测的角色写入，采样端点不成立，UNKNOWN。它代表送入本次渲染的pose，不代表GPU显示已完成，M2还需录像。每帧所有相位用event_seq排序；FixedUpdate可一帧多次，SIM不得合并为每帧最后一次。

## 3. JSONL记录字段契约（schema_version=`spf-bug1-probe/1`）

每行一个不可变记录；整数不得先转JS Number丢精度。`uint64`/SteamID/NetId/Stopwatch ticks以十进制字符串写JSON；`uint32`在范围内可用JSON整数。`vec3`为三个有限float64 JSON数，来自原生float32只读值；采用下面固定精度编码，不允许F2。全部null必须伴随 `unavailable[field]=reason`；必需字段缺失使相关窗口UNKNOWN。数组顺序固定XYZ。

### 3.1 所有记录共同字段

| 字段名 | 类型 / 单位 | 采样规则 |
|---|---|---|
| `schema_version,run_id,step_id,kind,phase` | string；无量纲 | run_id随机UUID；step_id来自预先冻结剧本；kind/phase见§2 |
| `event_seq,epoch,connection_generation` | uint64十进制串；序号 | event_seq仅本进程递增；epoch/generation读取既有事实，不由仪表推进；重置时分run |
| `role,target_steam_id,target_net_id,a_origin` | enum HOST/GUEST；string；string；enum SERVER_AUTHORITY/OBSERVER_REPLICA | 实际进程角色和目标，不从日志标签[Client]猜物理端；主Host窗A必须SERVER_AUTHORITY，反向Guest根标OBSERVER_REPLICA |
| `player_id,movement_id,third_id,renderer_id,camera_id` | int32?；Unity实例ID | 首次绑定后各行带同一bundle_id可代替重复ID，但manifest必须可解析；ID变化INVALIDATE |
| `frame_count,fixed_tick_id` | int64；帧/本地物理tick序号 | frame_count来自Unity；fixed_tick_id为只读观测序号，**不是inputseq** |
| `mono_ticks,mono_frequency,unscaled_s` | uint64串,uint64串,float64；tick、tick/s、s | 同机Stopwatch单调钟；Unity unscaled/realtime高精度入口，实际可用成员写manifest；每事件现场取值 |
| `time_scale,unity_delta_s,unscaled_delta_s` | float32转数；倍率、s、s | timeScale只读；保留Unity delta；判定真正帧间隔用RENDER_READY的mono差，不用0.08 |
| `p2p_host,is_server,is_dedicated,is_local_owner,ready,alive` | bool | 取样当时资格；失败/未知均不当作有效主段 |
| `valid,invalid_reasons,unavailable` | bool,string[],map<string,string> | 原始行保留；不可静默删帧、将null写0或用上一帧补值 |

### 3.2 RX、CONSUME、SIM专有字段

| 字段名 | 类型 / 单位 | 所在kind与采样点 |
|---|---|---|
| `rx_id,sim_call_id,consume_id` | uint64串?；本地事件ID | 对应事件生成；SIM前后共享sim_call_id；不得当作原生包序号 |
| `inputseq` | uint32?；**clientSimulationFrameNumber原生序号** | RX在真实Enqueue后，CONSUME/SIM从本次原生消费包关联；缺关联为null，不拿server simulation填它 |
| `server_simulation` | uint32?；原生模拟序号 | CONSUME/SIM取调用现场；与inputseq分别记录，不假定二者相等 |
| `packet_type,rx_id_link,queue_depth_before,queue_depth_after` | enum WALK/DRIVE/UNKNOWN；uint64串?；int32?；int32? | 入队/消费现场只读；关联用有界side table标记原包对象，不保留整包历史，不再次解析reader |
| `input_x,input_y,jump,sprint,stance,recov,sim_delta_s` | int32,int32,bool,bool,string,int32,float32秒 | SIM参数及现场原生姿态；RX可得字段也记，未知null；不从按键推定模拟已经发生 |
| `a_before,a_after,a_yaw_before,a_yaw_after` | vec3? U；float64? ° | SIM两条phase各写对应值；before与after离线按sim_call_id配对；root为真实movement根 |
| `native_speed_u_s,speed_source_id` | float64? U/s；string? | SIM读取当前原生walking speed及倍率资格；speed_source_id回指manifest的成员/配置推导证据 |
| `contact_flags,grounded,teleport_id,correction_id` | string[]?,bool?,string?,string? | SIM/事件只读；不能观测接触或原生传送/纠正边界时，对相应混淆因子标UNKNOWN |

当前SDK原包带clientSimulationFrameNumber；native ReceiveInputs会拒绝旧序号而后Enqueue，服务端消费另有simulation。Builder须对实际MVID核实字段与队列关联，RX总量/入口次数不充成功inputseq。相同client seq在不同epoch不关联；32位环绕须按原生会话规则处理，否则拆段UNKNOWN。Guest辅助端可记 `TX` 的同一client inputseq；仅用于跨端因果关联，**不得把两机unscaled/Stopwatch相减当单向延迟**。

### 3.3 FRAME及上下文字段

| 字段名 | 类型 / 单位 | 采样时机/含义 |
|---|---|---|
| `a_pos,a_yaw,v_pos,v_yaw,third_local_pos` | vec3 U；float64 °；vec3 U；float64 °；vec3 U | BEFORE_UPDATE与RENDER_READY现场都取；A/V同一world；local仅解释父变换，不用于世界位移判定 |
| `renderer_l2w,third_parent_id,renderer_enabled,active,onscreen,visible_source` | float64[16],int32?,bool,bool,bool?,string | RENDER_READY；矩阵/层级证明V是稳定角色载体；onscreen用实际相机投影/无遮挡记录，不能只依赖上一帧Renderer.isVisible |
| `camera_pos,camera_yaw,host_root_pos,distance_horizontal_u` | vec3 U,float64 °,vec3 U,float64 U | RENDER_READY；相机移动与角色world位移分开；距离是Host权威根到Guest权威根XZ |
| `last_sim_call_id,last_consume_id,last_inputseq,sim_calls_since_prev_render` | uint64串?,uint64串?,uint32?,int32 | RENDER_READY；只是观测链接，不复制成新SIM事件；本帧多次simulate保留全体 |
| `target_group_id,host_group_id,same_group,esc_open,focused,level_loaded` | string,string,bool,bool?,bool,bool | RENDER_READY与变化事件；组ID按数值，不用显示名；ESC源须核对实际原生UI，不只看timeScale |
| `native_speed_cap_u_s,epsilon_u,epsilon_yaw_deg` | float64? U/s,U,° | 冻结run manifest给定；无依据速度上限为null，该段UNKNOWN |
| `collect_cost_ticks,record_drop_count,buffer_used_bytes` | uint64串,uint64串,uint64串；tick/条/byte | 每次FRAME收集耗时与累计完整性；消费/落盘耗时另在RUN_END统计 |
| `event_name,event_id,step_start_mono,step_end_mono` | string?,string?,uint64串?,uint64串? | CONTEXT；pause/group/distance/teleport/correction/人工剧本标记；UTC只供视频/UMM关联 |

### 3.4 精度和原生速度上限冻结

位置编码网格固定 `q=0.001 U/axis`，取最近网格点；`epsilon_u=√3*q≈0.001732051 U`，这是三轴记录分辨率，不是实测容忍度，低于需求0.01上限。源float32坐标ULP不得大于q；记录源ULP预检与量化规则。范围/精度不满足则UNKNOWN，不调大ε。保留源float32位模式或round-trip数作旁证，判定始终使用同一固定编码序列；不从候选抖动幅度拟合ε。yaw网格 `0.01°`、epsilon_yaw_deg=0.01°，为新增采样协议精度，不是已测角阈值。

`native_speed_cap_u_s`须在运行前冻结：由实际MVID原生walking速度计算、姿态/技能/装备/插件倍率、水平地面摩擦和速度约束推导，保存成员名/IL或配置指纹及数值。参见[SDK speed属性与倍率](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerMovement.cs#L122-L129)及[walking速度约束](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerMovement.cs#L1222-L1269)。走/跑各有上限，原生现值逐SIM核对；不能以轨迹最大跳步/当前观察FPS/历史0.08秒推导它。中途配置或倍率变化整段失效；读取不到真实上限为UNKNOWN。

## 4. 完整窗口、混淆控制与实机动作脚本

### 4.1 预检与固定条件

开始前选平坦无遮挡直线路面，目标存活步行/跑步、无碰撞/水/梯/载具/负重变化；在隔离测试存档正常就绪后执行，不修队伍来凑条件。**两端均none、same_group=false** 为主测固定组态；记录实际ID并冻结，期间不邀请/退队。若当前存档不能保持该组态，可在开始前声明并冻结另一数值组态，整轮基线/候选必须相同，不能看结果后换组混算。不能把组名相同当groupID相同。

固定距离采用 **20 U，容许带[18,22] U**（新增实验控制，不是游戏可见距离常量）。Guest恒定前进，Host在平行直线以相同走/跑档随行观察，使间距留在带内；两端无互相碰撞，Host相机持续看到目标。若人工随行失败，窗口UNKNOWN并从头重采，不从整段裁剪出“好看的10秒”。相机world位姿照记，所以不能用屏幕位移判V是否停住。不得通过仪表移动Host/相机来维持距离。

同时读取实际版本原生culling距离/资格，20U必须处于明确不应被远距剔除的区域；不得只引用SDK576。若实际资格仍不明确，记 `CULLING_CONTEXT_UNRESOLVED`。可附逐recipient/逐entity的writer/sentinel观测，但不是主测必需；任何counts必须带recipient+entity+事件分母。禁算 `ΣculledPlayers.Count / writerCalls` 为剔除率，也禁用remoteUpdatesTotal=0代替输入/根/模型轨迹。

`esc_open=false && time_scale==1 && focused && level_loaded && ready` 才有效。ESC源缺失、timeScale非1、焦点丢失、暂停开始/结束、加载或身份变化一律INVALIDATE当前走/跑完整段；暂停结束后至少稳定1秒再重新STEP_BEGIN，仍按新窗口前后0.5秒剔除。原始无效记录和原因不得删除。历史七段暂停只作参照，本轮以自身相位时钟重采，不能套历史t值。原生teleport/correction作为单独事件段，不混进恒速M1分母。

### 4.2 窗口和样本数

| step_id | 原始采集/边界 | 样本要求 |
|---|---|---|
| `H_WALK_01` | 恒定走10秒，去开始/停止各0.5秒，主判9秒；前后额外各1秒上下文可留存 | 有效区间每个实际Host游戏相机render frame恰好一条RENDER_READY与对应BEFORE_UPDATE；全SIM前后/消费/RX，不降采样 |
| `H_RUN_01` | 同上，恒定跑；独立窗口，不拼接走段 | 同上；实际原生速度上限按跑档冻结 |
| `H_IDLE_01` | 停止过渡0.5秒结束后完整5秒 | 每帧稳定third位姿；记录待机动画状态，不用骨骼呼吸算漂移 |
| `H_JUMP_01..03` | 各一次真实跳跃，从起跳前1秒到原生落地后1秒；单次最长10秒 | 三次独立标注起/落地事件和帧号；不套平地水平速度上限判断垂直跳跃 |
| `H_TURN_01..03` | 各一次转向，变化前后各1秒，单次最长10秒 | 三次输入/权威yaw/Vyaw及最短角差；包含一次跨0/360°边界负控 |
| `H_TELEPORT_01,H_CORRECT_01` | 分别真实原生事件前1秒到原生完成/既定收敛边界后1秒；最长10秒等待 | 事件ID、目标、原生完成边界和前后完整帧；入口/边界不可执行=对应M3 UNKNOWN |
| `G_REVERSE_*` | Guest观察Host，同走/跑/跳/转/停脚本 | Guest呈现轨迹/录像；其观察端root必须标 `a_origin=OBSERVER_REPLICA`，不冒充Host服务端权威；M4和Host M1分别报告 |

10秒原始走/跑减各0.5秒即9秒主判；按需求文字固定，不以T2概述中的“有效10秒”悄悄替换。可另采更长旁证，但不能替代上述固定段。开始/结束采用mono时间，保留实际首末帧覆盖及边界帧，不要求人为精确落在渲染tick。

样本数不是预设“750行”：有效区间主相机应覆盖的首末frame为f0/f1，`N_expected=f1-f0+1`，必须恰好N条且逐帧无洞/无重复（本场景主相机持续渲染）。f0/f1须由STEP边界、独立BEFORE_UPDATE帧见证及相机持续启用事实核定，不能仅取已收到RENDER_READY的首尾而漏检边缘掉样；边界所属帧无法核定则UNKNOWN。至少5个连续位置样本才可构成三次小位移加随后补步；不足UNKNOWN。约75FPS时10秒原始约750帧、9秒有效约675帧仅为容量估算，不是玩家实测值或FPS门禁。RX/SIM样本数用真实事件计数，不按12.5Hz要求补齐125次；无SIM事件是观测结果，不造事件。

每步最长保留30秒；每端每run一次只追踪一个目标。建议环缓存上限131072条且64MiB，任一先满即停止本步、写原因并判UNKNOWN，不能覆盖旧帧后继续称完整。所有缺记录/未配对SIM/不存在的主相机帧/序号关联丢失均显式列出区间，**不内插、不前向填充、不重采样**。新采窗口是新step_id，不覆盖失败证据。

### 4.3 人工剧本与证据同步

执行顺序：核对两端DLL与游戏身份→Host原生开局/Guest正常加入并Ready→绑定目标并静态核验third层级/相机→预检1秒→按表走、跑、停、跳、转、合法事件→交换观察方向做M4→正常Guest先退出/Host后退出→导出两端UMM包和仪表目录。

控制命令规格为 `begin(run_id,step_id,target)`、`end(step_id)`、`stop(reason)` 的**本地诊断入口**；由Builder选择仓库已有配置/控制通路实际接线，不假装这些命令当前存在，不新增联机消息或用ESC菜单开关。STEP_BEGIN/END记录UTC、mono、frame并在常规日志打一行 `[SPF-Bug1Probe]` 标记；Host录屏从标记前开始，测试者记录动作编号。视频帧时间与mono用开始/结束两个可见/音频人工标记关联，报告误差，不把系统UTC当逐帧同步真值。视频低帧率/重复捕获帧不拿来填仪表缺帧，M2关联缺失为UNKNOWN。

## 5. 离线机器判定：先完整性，再事件，再因果标签

### 5.1 固定符号与有效性

对同一完整有效step按 `mono_ticks,event_seq` 排序主相机RENDER_READY：`F_i=(t_i,f_i,A_i,V_i,yawA_i,yawV_i)`，`dt_i=(ticks_i-ticks_{i-1})/frequency`，`dV_i=||V_i-V_{i-1}||₂`。坐标统一U、dt秒；速度阈值 `B_i=v_native_cap(step)*dt_i+epsilon_u`。**dt绝不能用SIM.deltaTime=0.08、平均FPS的倒数或录像帧长替代**。若同机mono倒退/非有限值/dt≤0、相机重复或任一必需相位缺样，该完整step为UNKNOWN，列出 `missing_fields/missing_frames/unpaired_sim_ids`。

valid(step)还要求：全区间身份与固定条件成立、原生速度/精度可证、RX→CONSUME→SIM关联可核、没有任何丢记录/越界/未闭合事件、指定观察camera不断帧、模型端点已核验。坏数据上仍可列“观测到的疑似事件”，但不签发完整窗口PASS。性能采集污染门见§6。

### 5.2 停帧补步候选及互斥标签

先找**最大**连续小位移段 `[a,b]`：每个 `i=a+1..b` 满足 `dV_i≤epsilon_u`，且 `b-a≥3`（至少三次连续渲染帧间小位移，至少四个位置点）。若紧随后一帧 `dV_{b+1}>B_{b+1}`，构成一次 `STALL_CATCHUP_CANDIDATE(a,b,b+1)`；相等不算超过。只按最大段计一次，不能以滑窗把同一次卡顿重复计数。输出a/b/release帧、时间、所有位移、B、V停段累计范围及source event IDs。边界未闭合记 `RIGHT_CENSORED` UNKNOWN，不能因少最后一帧认零失败；必要时用已采有效上下文证实闭合，不跨暂停/停止/传送拼接。

对每个候选，取停段时域 `[t_a,t_b]` 的**全部真实权威样本**：端点A、各FRAME A、每个SIM before/after（含同帧多次）。令 `root_span=max ||A_e-A_{e'}||₂`，e/e'遍历这些样本。不得根据持续输入补造中间A，也不得只比较起终点而漏掉中间移动又返回。

- `M1_STALL_CATCHUP := valid && candidate && root_span>epsilon_u`。权威在可见停段内确有位置改变，且可见随后越过单帧原生速度边界；这是需求M1失败事件，不能仅凭此宣判某插值算法错误。
- `AUTH_STEP_LOCKED := valid && candidate && root_span≤epsilon_u`。同一停段内权威根也未超出记录分辨率；包括原生固定步进使根与模型同拍释放。它是**合法诊断结果**，不是仪表实现失败，不是M1严格谓词成立；方向应转到上游输入/队列/模拟推进，而非强行保住“权威已连续推进”的叙事。
- `UNKNOWN := candidate && !valid`。包括缺SIM配对、root/third缺帧、inputseq无法关联、速度来源缺失、模型被骨骼/bounds替代、窗口截断、固定条件失效。输出具体字段和区间；若字段俱全但相位/端点证据矛盾，写 `ENDPOINT_OR_PHASE_CONTRADICTION`，不得编造缺字段或选择更喜欢的标签。

前两个标签对**同一个有效候选事件互斥且穷尽**：同一个root_span不可能既>ε又≤ε。UNKNOWN不是第三种已证根因，是证据不充分；不内插来消灭UNKNOWN。一个step可能有不同事件分别属两类，汇总必须逐事件列，不把整个step写成双标签。没有触发候选的完整有效step记 `NO_THRESHOLD_EVENT`，只表示本窗口未见该失败模式，不是另一因果标签或“Bug不存在”。

走/跑结果：有M1事件即该M1主段FAIL；只有AUTH_STEP_LOCKED时输出该标签及 `strict_m1_events=0`，**不授予Bug1完成PASS**，仍需主机画面M2及上游诊断；完整有效且零两类候选仅记 `NO_THRESHOLD_EVENT`，可按需求数字子项报告零事件，但不能替代M2。任何采样/窗口不足为UNKNOWN，不以已有少量好帧通过。

### 5.3 数值例与Builder必须实现的判定器红绿用例

下列都是**人工合成测量格式测试输入，不是实际游戏测得数据，也不是生产行为Green**：设ε=√3*0.001，v_native_cap=5 U/s、连续dt=1/75s，则补步阈值约0.068398718 U。

| 输入（X坐标，其他轴固定；五个render样本） | 预期输出 |
|---|---|
| V=[0,0,0,0,0.08]，A=[0,0.02,0.04,0.06,0.08]；SIM样本在停段证实上述推进 | 仅M1_STALL_CATCHUP；一次，不重复计滑窗 |
| V=[0,0,0,0,0.08]，A=[0,0,0,0,0.08]；停段完整SIM/输入关联、根确实没推进 | 仅AUTH_STEP_LOCKED；不能伪称M1，不能先做插值修复 |
| 同第一行，但缺frame2或某SIM.after | UNKNOWN；列missing frame或sim_call_id，禁止补值 |
| V每帧匀速0.02U，无三次停帧 | NO_THRESHOLD_EVENT，不是根因证明 |
| 仅两次小位移后补步 / 位移恰等B / 同一时刻多camera记录 / NaN | 前两者不触发本谓词；后两者UNKNOWN，不允许浮点“近似超过” |
| 停段内A先动后返原位，SIM已完整采到 | root_span必须>ε，不能仅比端点而错报AUTH_STEP_LOCKED |

判定器还必须先红后绿覆盖：阈值不可事后变大、缺源速度上限、事件末端截断、重复seq/跨generation、暂停跨接、固定组/距离越界、third错绑或只给bounds、renderer/camera消失、yaw359→0最短差、idle骨骼动但third不动、buffer overflow、合法teleport单独分类。正式注册/全套回归另按仓库流程；这些单测只验测量算法，真实SDK/实机仍须另跑。

### 5.4 附属判据与下一步分流

静止：完整有效5秒，`max_i ||V_i-V_first||₂≤ε` 才符合M3静止数字条件；超限FAIL，端点或姿态缺失UNKNOWN。yaw用 `abs(((y2-y1+180) mod 360)-180)`，角精度固定；跳/转各三次对权威变化而模型连续三帧冻结后集中更新的片段输出帧号及角差，**不虚构通用最大角速度/垂直速度阈值**，人工关联M2；需要角速度上限才能全自动闭环时保持该子项UNKNOWN另行事前定协议，不借走跑上限代替。

teleport/correction：记录真实原生入口、事件完成或既定收敛边界及目标，边界处 `||V-target||≤ε` 且后续静止不回旧轨迹；缺原生事件/边界UNKNOWN，不手改root制造测试。

上游分流只读输出RX间隔、client inputseq、消费queue等待、SIM间隔、root_span、V-A残差（注明父偏移），不预设80ms就是错误：RX有间断→输入接收/发送方向诊断；RX持续而消费/SIM停→队列/调度诊断；SIM持续而A锁住→模拟/接触/恢复分支诊断；A推进而V冻结→呈现写入顺序/动画覆盖诊断。单个Host trace不能证明网络丢包或原版插值缺陷。

**停止门**：数据既不支持M1也不支持AUTH_STEP_LOCKED时，停止一切平滑/传输/剔除修复。若UNKNOWN，下一张票是“补齐具体缺失端点/字段/窗口的仪表修复与同场景重采”；若完整数据为NO_THRESHOLD_EVENT但画面仍卡，下一张票是“相机提交pose—实际视频显示帧关联/渲染覆盖时序定向测量”；若画面也未复现，记录本轮未复现并派同固定协议扩大可复现样本票，不放宽阈值。有效AUTH_STEP_LOCKED下一票是“RX→消费→simulate→root上游推进诊断”，不是直接插值实现票；M1成立也仅批准进一步定位呈现写入者，不自动批准具体修复算法。

## 6. 开关、低扰动、落盘与身份

未来实现的配置契约：`Bug1Probe.Enabled=false` 默认；`TargetSteamId` 必须显式指定；`RunId/StepId/OutputRoot` 与容量/阈值在RUN_BEGIN冻结；仅本地诊断入口能开始/结束，不经游戏RPC。关闭时不分配逐帧对象、不写trace、不订阅额外渲染采样；已安装观察hook仅做廉价门控并原样放行。运行中改开关结束本step，标 `CONFIG_CHANGED`，不能把新旧半段拼接。reset/销毁时停止，不能写入已换epoch的实例。

逐帧/包热路径只复制必需标量到预分配有界结构；不反射扫描层级、不抓全堆栈、不 `LogInfo` 每帧、不同步刷盘、不在主线程序列化JSON。层级/成员在绑定时核验缓存，identity变化先停再绑定。后台仅序列化不可变副本，不触碰Unity对象；写盘慢/溢出停止本步并UNKNOWN，原生游戏继续原语义。不要catch吞掉原生异常；仪表自身异常停仪表并留故障，不能让其异常改变原生返回或处理队列。

建议固定污染门（新增仪表资格协议，非游戏需求/FPS优化目标）：匹配场景“仪表关/开”各重复三段，保存原始帧时分布；收集主线程耗时p95≤0.2ms，开/关中位与p95帧时恶化均≤5%，且开关不改变原生调用次数/输入序列/权威轨迹的同fixture对照。若超标/对照不可比，记 `PROBE_PERTURBATION_UNRESOLVED`，先优化/补测仪表，不拿该数据定根因；不可删慢帧再算指标。0.2ms/5%是事前预算，尚无实测结果，不可事后调大来保trace。

### 6.1 落盘契约（未来Builder须实现，不是当前已有文件）

默认输出根：`<Unturned>/BepInEx/diagnostics/spf-bug1/<run_id>/<HOST|GUEST>/`；BepInEx绝对根由运行时API解析，不能假定仓库cwd就是游戏目录。允许开始前显式选择有写权限的隔离目录，manifest记规范化绝对路径；路径不通就不开采集，不静默改写游戏存档/用户文档目录。

| 文件 | 内容 | 关闭与证据要求 |
|---|---|---|
| `manifest.json` | schema/run/step表、角色、UTC-mono锚点、actual游戏/SPF/仪表SHA256+MVID、基线commit/dirty差异、ABI/源成员、target/相机/third/renderer层级、阈值及原生配置证据 | RUN_BEGIN先写，结束写完整状态；哈希归run索引，不能自指hash |
| `trace-0001.jsonl` 等 | §3原始事件；分卷不丢event_seq，段落固定完整性边界 | append-only、结束flush并关闭；每卷SHA256、首末event_seq、条数/字节数入索引 |
| `result.json` | 每step完整性、N_expected/实际、全部事件帧号和互斥标签、M1/M3数字子项、UNKNOWN原因/缺字段、下一票路由 | 原始trace不可用时不产PASS；计算器版本/规则hash入文件 |
| `run-index.json` | 所有原始卷/manifest/result的SHA256、结束原因、buffer/drop/write-error计数 | 未有完整RUN_END或hash不符即证据不完整UNKNOWN |

常规日志只在开始/结束/错误写 `[SPF-Bug1Probe]` 一行，含run/step、绝对输出目录、身份、完整性结论，供[UMM SOP](../../docs/agents/auto-rm-test-sop.md#L18-L24)定位。**UMM是否自动包含此新目录尚未知**：Builder/集成者必须实际核对导出包内容，若不含则与每端UMM诊断包一起单独打包仪表目录、录像/动作表和hash索引；不能只交LogOutput而丢原始trace。不要收集聊天、token、完整packet payload或无关目录；只保留所需角色身份和诊断字段，分享按用户范围。

### 6.2 构建/部署门禁与双人对齐

本轮不部署。后续仪表是生产诊断仪器，必须按[输出审查循环](../../docs/agents/output-review-loop.md)完成自身红绿测试、正式唯一回归与独立双轴CLEAN；[实机循环](../../docs/agents/real-machine-test-loop.md#L36-L39)允许未CLEAN仅本地自用取证，不得给用户部署指令。CLEAN之后Captain按SOP发准确DLL路径、备份/核验指纹说明，人工1H+1G执行§4，不建U3DS/VM/账号设施，不自作主张开启第二游戏或操作用户会话。

仪表构建会改变DLL身份：manifest必须区分“0.2.4.10原件身份”“基于该源码、仅加只读仪表的诊断基线身份”“未来含修复候选身份”。不能把装探针后的新SHA冒称原0.2.4.10原件。Host/Guest各自加载的身份须与当轮审查构建记录精确对应，fingerprint不符立即停止；基线/候选重跑相同冻结配置/场景/脚本，只有声明过的改动不同。

1H+1G只关闭本Bug的定向观测列；仓库最终1H+2G回归门禁不变。反向Guest的A为观察端副本；若需要严格关联Host真实权威，另读取同run Host自己模拟日志并依原生seq/标记匹配，不发送新自定义测量包，也不将两端时钟直接相减。缺反向端点仅M4 UNKNOWN，不拿Host数据替代。

## 7. 能证明/不能证明与Builder交付清单

能证明：这个身份、窗口、输入和相机下，SIM是否真正执行/根何时变化、最终third载体何时变化；能给每次补步的render帧/时间/速度界、互斥根推进标签、暂停/组态/距离/完整性边界。能区分“接收/消费/模拟/根推进/最终pose”哪一段有观测分歧，并让相同trace离线重复判读得到同结果。

不能证明：某插值算法本身错、必须新增插值、网络真丢包、GPU/显示器的最终帧时、动画骨骼完整正确、多Guest正常、整个三Bug已修；`AUTH_STEP_LOCKED`也不证明原生固定模拟步设计错误或一定是输送故障。持续非零input不等于权威已移动；A/V相同不等于画面一定平滑。第三方摄像/视频时序仍须单独关联。

Builder完成后应提交以下相互独立结果，不能自审CLEAN：

1. **实现与接线**：独立只读模块、注册owner/target/方法身份、实际MVID/ABI核对、default-off与非目标负控、reset/溢出/卸载边界；提交变更路径与精确命令/退出码。是否放进现有诊断目录及哪些公共注册器改动由新实现票授权，本规格不授权越scope修改。
2. **判定器测试**：§5合成反例先Red后Green，双标签互斥/掉样UNKNOWN/不内插/速度与ε冻结等测试。它们只证明仪表判读器，不能冒充Bug1真实Red。
3. **真实集成**：匹配游戏DLL的Unity真实远端角色/相机/renderer端点观测，源坐标/时钟精度、输入序号全链、采集相位/低扰动实证；无法执行的具体ECall/许可/对象图边界写UNKNOWN，不再造假Transform。
4. **实机取证**：CLEAN后1H+1G各自UMM+trace+配置+动作/录像索引，走/跑/停/跳/转/原生事件/反向逐项；每step列总帧、缺帧、样本精度、两类事件数/帧号、UNKNOWN及下一票。不得用聚合剔除替代。

最终明确停止门已在§5.4：M1/锁根均不支持时先补仪表或渲染—视频时序测量，绝不先做插值；AUTH_STEP_LOCKED成立转上游推进诊断。所有新增协议阈值在RUN_BEGIN冻结并由基线/候选共享，变更须事前新版本规格/审查并从基线重采，**禁止看见候选结果后放宽阈值**。

## 8. 本T4范围与校验记录

本任务仅规格编写，因此不启动真实游戏、不跑生产Red→Green、不写源码/脚本或部署产物；诊断技能的执行循环明确留给后续仪表实现/SDK/真机票，不以规格自称完成反馈闭环。该跳过是任务范围限制，不豁免后续工程质量门禁。本文件提交后待独立双轴审查；两份冻结输入和历史票不修改。

已完整读回规格。校验本地链接11个均存在，两份冻结输入SHA与任务给定值完全一致；以Python仅验算§5合成数值例：ε=0.0017320508075688772、B=0.06839871747423554，推进例root_span=0.06、锁根例=0，互斥/边界断言通过，命令退出码0、标记 `SPEC_FORMULA_EXAMPLES_PASS`。这不是判定器实现测试或生产真实Red/Green。最终指纹在任务完成回执报告，不写进自身正文形成自指。
