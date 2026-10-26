# Item-GuestBindingAdmission 实施审计

## 当前结论

实现已限定提交，Release DLL及唯一ZIP完成指纹/清单核验并可供R04手测。GBA-R2-G1定向Red→Green、当前正式回归及GBA-R3双轴CLEAN均闭合；Runtime仍为 `implemented-pending-runtime`，不代表实机验收。

## 身份与证据

- 固定基线：`2aa91e1c9f6053d9998dd957d103470ccbcfebc2`，分支 `codex/structure-baseline-0.2.4`；构建者Vela `924e4179-a42e-471a-a8b3-8d527d3afa6c`，native `01a17922-6540-7901-afe9-9d8d7bfb019b`，launch/native均为gpt-6-luna/high。
- seams准备构建保留旧领域guard；真实Red来自真实`SteamPlayer`、真实Steam transport、真实身份stage/commit及真实`UpdateObserver→engine→port→BindGuestBaseline`。CoreCLR 10.0.9 x86返回exit 4，`sends=0 loaded=0`，指向`connection generation is stale`及错误的`possible-half-packet`隔离/通知。
- 当前修复同测Green：相同真实路径，`sends=1 loaded=1 isolated=False outcome=bound`；当前子协议确认生产`BindGuestBaselineCore`与GBA01方法MVID/token、x86 CoreCLR 10.0.9一致。
- 首轮双轴发现及其身份来源限制保存在 `.scratch/matt-orchestrate/item-guest-binding-admission-20261027/review-round-01/`。真实GBA01现在还验证错token/epoch/owner/transport零发送零loaded、未隔离，并验证旧Unbind不清理；GBA08实际走settlement Waiting→后续Accepted送达。runner使用当前模块精确路由并在每条正式ledger记录同一runId。
- 修复后正式Main：`WhitelistTests/run-integrated.ps1`，日志 `.scratch/matt-orchestrate/item-guest-binding-admission-20261027/integrated-run-review-fixes-02/`；runId `1db2a9cc-b33a-4b11-8a13-6570293e6303`；`632/632 PASS`，Framework本地630项，CoreCLR x86的NIL01和GBA01各1项，完整runId/连续ordinal ledger，exit 0。生产MVID `c608d1df-850d-4628-b580-3c46e89894b8`，测试MVID `ee8c515d-1da9-4bfb-a932-54657e4a37cb`。
- 主机探针使用本票隔离副本 `.scratch/matt-orchestrate/item-guest-binding-admission-20261027/native-host/`；旧NIL01 host及失败证据留在原运行目录。测试产物不是游戏依赖。

## 实现范围

准入读取协调器已提交的物理owner/transport/token及当前writer/ledger/Item epoch；Accepted才进入原生AskItems。缺事实Waiting走Deferred/现有delivery-only路径且不产生错误通知；确定冲突Rejected精确撤当前重试/投递。未知发送异常继续隔离。保留唯一writer、原生仓库/协议、R01/R02/R03/NIL与07距离合同。

新增GBA01–GBA08覆盖真实首次Observe/冲突拒绝/旧Unbind、物理引用/epoch判定、预算期限、Deferred/Rejected分类、接线、双runtime runner协议与模块拒绝、结算等待后续送达。未以历史624计数代替当前Main。

## 留项与交付门

- R04仍需根安排1 Host+2 Guest真实游戏及UMM诊断包，分别确认原生发送、Guest接收/物资交互、提示与退出/重连；僵尸掉落独立未确认。
- 第二轮双轴仍为BLOCKED，报告见 `.scratch/matt-orchestrate/item-guest-binding-admission-20261027/review-round-02/`。Standards指出Waiting分支的旧真实失败链到期时仍会触发耗尽通知，要求裁定与AC4“不通知”及“旧期限不重置/真实耗尽沿原合同”的并存语义；Spec指出旧Unbind测试未构造重连新投影且GBA08直接注入admission，尚未由协调器真实事实变更驱动生产reader。两轴都无法核实reviewer自身native/model身份，本轮不作为身份验证后的正式签核。按合同停技术编辑并请求常驻指导最小裁定；提交、Cutover指纹核验及独立ZIP未进行。

## GBA-R2-G1恢复候选

- 根采用B：Waiting刷新既有真实故障链到期事实，但不claim/通知/重置FirstFaultAt、次数或冷却；Accepted后旧链已耗尽则不自动续送，只发一次`possibleHalfPacket=false`耗尽通知，后续仍走合格人工恢复；无真实故障的纯Waiting恢复后自动交付。
- 唯一生产改动：`ItemReturnSettlement.ConsumeDeliveries` Waiting分支仍调用`RefreshExhaustion`并保留delivery，仅移除该分支的`NotifyDeliveryExhausted`调用。Confirmed-not-sent及unknown其他故障路径未改。
- GBA01真实通知Red：在CoreCLR 10.0.9 x86 native host，真实SteamPlayer/Steam transport、真实coordinator committed身份、`ConfigureGuestBindingBridge`生产reader、`seam`/engine/settlement和真实binder路径下，受控sender实际`NoteAskItemsOriginalRan(false)`触发SafeToRetry异常并由`Finish/QueueDeliveryRetry`创建key `item-delivery:1:0:0:1:76561198000000001:2`。真实Provider客户端暂缺使reader返回Waiting；超10秒后旧分支仍产生一次耗尽通知，测试返回false/host exit 4。这是等待通知时点Red，不是身份guard Red或fixture初始化失败。
- 同输入Green：保留`FirstFaultAt=1.5`、`RetriesStarted=0`、delivery `AttemptId=1`及到期exhausted事实，Waiting期间0通知/不发送/不loaded；恢复同一身份后Accepted仍不自动发送，耗尽通知恰好一次且`possibleHalfPacket=false`；随后经`seam.TryManualRecovery`成功恢复。GBA01同项测试exit 0；原双轴两轮BLOCKED历史保持不变。
- 新增GBA01补证：以同一Core分配的ConnectionIdentity对象暂时省略/原样恢复，实际`ReadGuestAdmission`观察Waiting→Accepted；真实A/TA→B/TB重连经seam送达，受控loaded投影正向确认B，旧TA Unbind与旧disconnect回调保留B token、demand和投影；真实Rejected经engine入口不增加retry或副作用。测试输入位于host实际运行目录`native-host/bin/Release/net10.0/input/`。
- GBA01最终定向Green与当前正式Main：`integrated-run-gba-r2-g1/`，runId `e61c06a2-8d95-4a81-a053-4bc45d016559`，`632/632 PASS`，Framework本地630、CoreCLR 10.0.9 x86 NIL01/GBA01各1；父子stderr为空、exit 0、唯一协议及逐行ledger闭合。两child实际MVID均为生产`2ec64d10-dc14-4c9c-afe2-6c22276e6d14`、测试`70033a4d-4063-4072-92da-e450a521a30c`；GBA01方法token `0x060000FE`、准入目标token `0x06000862`，实际host加载路径为`native-host/bin/Release/net10.0/input/`。
- 根从builder SubAgentActivity与四个独立native原文核实Round 1、2均为完整有效BLOCKED/BLOCKED、gpt-6-luna/high，累计2轮保留；详见`review-rounds-01-02-root-native-evidence.json`及R2 Spec完整文稿/根续记。专家`GBA-R2-G1`匹配满足根的有条件恢复授权；当时冻结候选等待新鲜native双轴第3轮。此前关于身份无法核实的文字保留为当时状态，不是当前根核实结论；Round 3最终结论见下节。
- GBA-R3 Standards/Spec审查者最初各自因缺少可见身份查询而暂记来源阻断；根随后以builder父rollout真实SubAgentActivity、各自session_meta/turn_context和原始final核验两位均为fresh gpt-6-luna/high。原实例仅作同轮followup澄清并分别给出最终CLEAN，未重扫仓库、重跑测试或改候选；双轴结论保持原技术审阅内容。证据及原文见`.scratch/matt-orchestrate/item-guest-binding-admission-20261027/review-round-03/`，原生来源见`root-native-provenance.json`，初始阻断及终判分别留档。
- Round 3双轴已CLEAN；Standards native `01a1798d-92f0-79c0-9e60-59430fb6bb05`、Spec native `01a1798d-c269-7833-a2bd-80a71bf83b6b`。原实例各自同轮澄清身份阻断后终判CLEAN；原始意见与原生来源证据完整保留于`review-round-03/`。
- 唯一实现提交：`0f94e1333b755bc2fe07d81e1cc2dbe32c74a0ba`。Release DLL位于`artifacts/Item-GuestBindingAdmission/0f94e13-20261027-0259/SteamP2PFriends.dll`；唯一final包为`artifacts/Item-GuestBindingAdmission/Item-GuestBindingAdmission-0f94e13-final.zip`。SHA-256=`133BBC899B4B0A032769715E2231F3F7940D8F4DE8C19ED29D81CB6C243C8826`，MVID=`2ec64d10-dc14-4c9c-afe2-6c22276e6d14`，版本/程序集/文件版本=`0.2.4.10`，Case-ID=`SPF-0.2.4.10-Experimental-ItemSlice-Cutover`，CandidateRole=`Cutover`。
- `Tools/Verify-BuildFingerprintArtifact.ps1` 对实际包内DLL返回`INDEPENDENT_ARTIFACT_VERIFICATION_PASS`；ZIP清单含插件DLL、简体中文README、artifact manifest与指纹核验记录，DLL SHA-256与外部交付DLL相同；没有捆入native测试host。安装回滚和最小R04步骤见包内`README.zh-CN.md`。
- R04游戏验证及僵尸掉落仍待实机。632/632为G1当前候选的正式Main，不由历史计数拼接，也不宣称Runtime通过。

## 根核验续记

根现依据builder父rollout实际`SubAgentActivity`与四个reviewer独立native `turn_context/final`核实R1/R2 Standards和Spec均是gpt-6-luna/high、有效完整BLOCKED轮次；R1 Standards `01a1795c-cfa4-7431-a49f-10132cfaf34e`，R1 Spec `01a1795c-f1de-78f1-9eb3-5b5888c1368b`，R2 Standards `01a17967-3b58-7ec1-a11f-9c567d10a513`，R2 Spec `01a17967-5eef-70d0-9462-4aacceb0c097`。详细证据为`.scratch/matt-orchestrate/item-guest-binding-admission-20261027/review-rounds-01-02-root-native-evidence.json`。以上当时身份观察原文保留，根核验不覆盖历史。完整R2 Spec final副本见`review-round-02/spec-review-native-full.md`。
- 不自动部署、替换安装、覆盖稳定包或发布。最终交付包提供备份替换与恢复步骤。
