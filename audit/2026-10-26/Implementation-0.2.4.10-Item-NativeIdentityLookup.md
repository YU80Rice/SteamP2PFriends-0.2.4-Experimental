# SteamP2PFriends 0.2.4.10 — Item-NativeIdentityLookup 实施审计

## 目标与边界

本票以 `.scratch/item-native-identity-lookup/issue.md` 与 `spec.md` 为合同，基线为 `fb2acbe31ceb786f3dd9e2dbebce185cc700ac9b` / `codex/structure-baseline-0.2.4`。生产仅改 `Adapters/Item/ItemPreparationAuthority.cs` 现有 private static `FindClient(ulong)` 的引用判空；保留 `Provider.clients` 原空早退、遍历顺序、第一条 `steamID.m_SteamID` 值匹配及返回对象。bind/unbind、owner/transport/Core代次、异常传播、一致性与生命周期合同未扩展。未改 SDK 运算符、其他生产方法、目标框架、游戏/Libs、掉落/生成概率或fault/repair-required行为。

## TDD 与验证证据

完整记录与日志见 [当前候选验证](../../.scratch/item-native-identity-lookup/evidence/candidate-validation-phase15.md)。

- **真实业务 Red**：生产未改候选 MVID `115828db-b34e-420c-b38c-d44a60ef216e`，目标 token `0x06000862`。真实 SteamPlayer/SteamPlayerID 输入经原生初始化后进入 FindClient，空引用比较触发真实 `NullReferenceException`，exit 10：`.scratch/matt-orchestrate/item-native-identity-lookup-20261026/environment/worker/host/phase2-sharedinternals-run.{stdout.txt,stderr.txt,exit.txt}`。
- **真实 Green**：同名正式 NIL01、真实查找输入，修后生产 MVID `88f119d8-9167-42c4-8e44-f560636aeca5`，`TEST_RESULT=True` / exit 0：同目录 `phase3-green-final.{stdout.txt,stderr.txt,exit.txt}`。早期 `NIL01-red-run-full.clean.log` 是另一条初始化失败历史，保留但不作为目标 Red。
- **NIL04 补强**：真实 SteamPlayerID 的 `ReferenceEquals` 对照与 `==` / `!=` 重载空比较捕获均通过；定向 exit 0，完整记录位于 `.scratch/matt-orchestrate/item-native-identity-lookup-20261026/environment/worker/host/phase15-nil04/`。
- **当前 Release 构建**：先后执行 `dotnet msbuild SteamP2PFriends.csproj /t:Build /p:Configuration=Release /v:minimal` 与 `dotnet msbuild WhitelistTests/SteamP2PFriends.WhitelistTests.csproj /t:Build /p:Configuration=Release /v:minimal`，均 exit 0。完整命令、stdout/stderr、exit及时间在 `.../environment/worker/host/phase15-build/`。Whitelist 仍有4条既有 CS0219/CS0649 warning，未隐藏或改配置。
- **当前完整回归**：同一次原始 `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` 正式 Main 完成624/624、Failed0、父exit0。623项在 Framework `4.0.30319.42000` x86本地执行；唯一 ordinal 103 / 精确 NIL01 在 CoreCLR `10.0.9` x86 子进程真实执行并exit0。连续624行 ledger 与预映射按ordinal/name/EvidenceClass逐项相符；父/子生产 MVID均为 `88f119d8-9167-42c4-8e44-f560636aeca5`，测试 MVID均为 `8b9bf1fe-4fab-4bda-a688-5a9a66c4f7bf`；子进程只输出一次 `TEST_RESULT=True`。NIL00–04均在本次Main完成。完整输入、命令、stdout/stderr、exit、结果ledger、子日志与核验记录在 `.scratch/matt-orchestrate/item-native-identity-lookup-20261026/environment/worker/host/phase15-integrated-framework/`。这是经批准的**一次Main内分运行时回归**，不称单一CLR独自通过624项。
- **保留失败历史**：早期Framework整套623/624失败、CoreCLR整套600/624失败及早期fixture初始化失败均保留在原路径，不拼接为通过、不覆盖。本轮新全量结果单独来自phase15。

## 具名延期

**NIL-D01，可延期：Program的单例分运行时分派/账本职责集中（Divergent Change / Middle Man观察）。** 当前正式Main为保持所有原登记与断言、原Framework测试上下文并执行真实类型NIL01，新增约175行账本、精确分派和子进程身份协议；未新增通用runner或生产接缝。保留其当前位置，延期独立职责模块提取。代价是未来账本格式、子进程协议和登记逻辑修改仍汇聚 `WhitelistTests/Program.cs`；不延期任何真实执行、错误传播、身份核验或完整登记要求。当前只有一个明确分派目标，既有方法分界及phase15同候选完整运行支持本次保留；为单例迁移文件只搬移复杂度并扩大本票测试改动。责任人为本票实施者 `08ddf56c-a3ee-41f1-a275-4ed3e0e06ad7`。若未来新增第二个跨运行时目标、可复用调用者或再次实质修改该协议，在那次扩展前重评独立模块边界；本票不自动开启后续重构任务。

NIL-D01只延期职责位置拆分，不豁免实现或行为合同。当前只有成功路径动态执行证据；child false、非零exit、缺失/重复结果协议、父子身份冲突与超时路径在当前源码中有显式拒绝逻辑，但未找到独立动态负协议验证记录，故记为“静态已核 / 动态未运行”，不宣称动态覆盖。

NIL00仅为原生fixture前置探测。若目标查找前初始化失败，预检/Main必须失败；不能称业务Red或业务PASS。Runtime未执行；僵尸掉落仍单独未证实。

## 双轴审查历史与当前门禁

三轮完整审查的原始报告与判定映射见 [第1轮](../../.scratch/item-native-identity-lookup/evidence/review-round-1.md)、[第2轮](../../.scratch/item-native-identity-lookup/evidence/review-round-2.md)、[第3轮](../../.scratch/item-native-identity-lookup/evidence/review-round-3.md) 及 [原始final](../../.scratch/item-native-identity-lookup/evidence/review-raw-finals.md)。

| 轮次 | Standards | Spec | 处理 |
|---|---|---|---|
| 1 | BLOCKING | BLOCKING | 两者把较早初始化失败误作唯一Red来源；后续有效目标NRE证据已更正。Spec还指出NIL04缺真实重载控制，已补充并验证。保留原判。 |
| 2 | BLOCKING | CLEAN | Standards唯一阻塞为NIL-D01未列为可延期的判断性smell，无硬性规范违规；Spec确认规格一致与当前phase15回归，要求NIL00只作预检。保留原判。 |
| 3 | **CLEAN** | **CLEAN** | 根批准的文档处置审查轮；两位全新原生审查者审查同一候选。Divergent Change / Middle Man 仅为判断性smell，NIL-D01已具名延期；不免除完整执行、身份核验或失败传播。 |

根接受NIL-D01处置并授权一次新鲜完整并行双轴；该决定见 `.scratch/matt-orchestrate/item-native-identity-lookup-20261026/dispatch/NIL-D01-disposition.md`。专家建议见 `.../environment/expert-round2-smell-disposition.md`。历史判断不回填为CLEAN；不可将第2轮Spec CLEAN与新一轮Standards结果拼接。第3轮若仍有阻塞，停止并回根，不自动开启额外审查轮。

## 交付状态

当前状态：**已实现 / 自动化验证通过 / 三轮历史完整保留 / 第3轮双轴CLEAN / 本票限定提交与独立DLL包已交付 / Runtime待用户实机验收**。phase15生产/测试MVID只标识已测候选。源码与测试未在phase15后变化；仅补审查与交付记录，复用phase15有效全量证据。源码提交为 `bd451123f98626d9ea991b4de681dd1b826b9f2c`。产物、安装/回滚步骤与R04待测项见 [交付记录](../../.scratch/item-native-identity-lookup/delivery.md)；独立包目录 `artifacts/Item-NativeIdentityLookup/bd45112-20261026-1030/`，便携包 `artifacts/Item-NativeIdentityLookup/Item-NativeIdentityLookup-bd45112-final.zip`。DLL SHA-256 `15B55640134CD4B2F9195BB65C4C38D5FC4005F219E00BE85BAA7F0E9D23FF9F`，MVID `88f119d8-9167-42c4-8e44-f560636aeca5`，Case-ID `SPF-0.2.4.10-Experimental-ItemSlice-Cutover`，role `Cutover`。实际包内DLL经独立指纹验证PASS，IL检查确认目标引用null分支、SteamID值匹配与首项返回；DLL为net472/MSIL，私有引用仅游戏/BepInEx共享依赖，无额外插件文件。未执行部署、Runtime或僵尸验收，不得宣称其通过。
