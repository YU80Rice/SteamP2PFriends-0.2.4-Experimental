# T151 配置契约追认：t145 合并主 cfg 与 T88 局部失效范围

2026-11-09，gpt-architect。任务 ID 为 t151，票面标题为 t153。本裁决书面追认 t145 的配置兼容性取舍，仅新增本文，不修改 T88 历史原文、需求文档或生产代码。

## 1. 生效结论与不迁移决定

**自 0.2.4.11 起，`SteamP2PFriends.Bug1Probe.cfg`、`SteamP2PFriends.Bug1Gate.cfg`、`SteamP2PFriends.Bug2HostGunshot.cfg` 三个独立配置不再被读取，旧值不迁移，也不作为主配置缺项时的回退来源。** 用户留下的旧文件不因此删除或改写；“不迁移”是旧值不再生效，不是把旧文件中的 true 写成 false。

三个开关统一绑定到插件主配置 `com.yu80rice.steamp2pfriends.cfg`，缺省值均为 false；从旧独立配置升级、而主配置中尚无这些项时，旧文件的 true 不会自动启用任何一项。**这是明示的破坏性兼容变更，不是无行为变化的重构。**

选择不迁移，是为了保持唯一主配置事实源，避免为旧文件名另引入一条可能“读错文件名就静默关闭”的兼容读取路径；这是一项明确取舍，并非声称迁移在技术上不可实现。

## 2. 对 T88 的逐句取代与关联条款解释

本文只取代“旧独立 cfg 仍控制启用”的契约，不整体废弃 [T88](architect-ruling-t88-release-diagnostics.md)。以下均以该历史文档当前行号定位；旧文档原字节保留，遇到冲突以本裁决为准。

| T88 位置 | 失效或需限定的原句 | 自 0.2.4.11 起的替代解释 |
|---|---|---|
| [44](architect-ruling-t88-release-diagnostics.md#L44) | “默认值不会覆盖用户旧配置。” | 不再包含三个旧独立文件值的继续生效承诺；保留并收窄为：不得覆盖主 cfg 中用户已保存的值，见 §3 |
| [44](architect-ruling-t88-release-diagnostics.md#L44) | “两个入口发现已有Enable=true会启动/尝试启动，不是每次升级都强制OFF。” | **作为旧独立 cfg 的行为说明作废**。旧文件 true 不会被发现或自动带入；主 cfg 的显式 true 仍须被尊重，但不豁免原有运行资格门 |
| [44](architect-ruling-t88-release-diagnostics.md#L44) | “升级部署前须检查SteamP2PFriends.Bug1Probe.cfg及SteamP2PFriends.Bug1Gate.cfg；发现true，部署者先停止发布动作，告知操作者确认关闭” | **作废该检查对象及由旧文件 true 触发的停止规则**。改查当前启动自证行中的主 cfg 绝对路径和三个实际布尔；不再检查已无效旧文件来判断开关 |
| [103](architect-ruling-t88-release-diagnostics.md#L103) | “必须同时解释旧配置不会自动重置” | **改为：必须明确解释旧配置不再生效、旧值不迁移，需在当前主 cfg 重新配置。** 不能继续以旧 true 会沿用为升级说明 |
| [83](architect-ruling-t88-release-diagnostics.md#L83)、[92](architect-ruling-t88-release-diagnostics.md#L92)、[101](architect-ruling-t88-release-diagnostics.md#L101) | “主要风险为旧cfg仍true”、已有 true 的升级检查、说明末句“升级前请确认两个诊断cfg未遗留Enable=true” | 涉及上述旧独立 cfg 的含义同步失效；风险与检查对象统一改为**当前主 cfg 的显式值及启动自证**。第 92 行“已有 true 配置”只可继续指当前主 cfg，不得指停读旧文件 |
| [42](architect-ruling-t88-release-diagnostics.md#L42)、[92](architect-ruling-t88-release-diagnostics.md#L92) | 新空配置生成“两个”诊断 Enable=false | 诊断原有默认 OFF 原则保留；t145 本轮合并范围为三个开关，均以主 cfg 首次绑定 false 检查，不另生成独立 cfg |

第 44 行禁止静默覆盖整份用户配置、主动显式开启的性质、不新增擦配置/nonce/禁用内核总开关等其余边界仍有效；只是配置归属与检查依据改成主 cfg。其他能力说明若因后续实现变化需更新，仍按对应实现与验收据实处理，本票不顺带追认。

## 3. 唯一部署/测试核对口径与用户值保护

部署或测试前，**启用配置的唯一核对口径是当前被测进程、本次启动的自证行**：`[Diagnostics] configPath=<绝对路径> bug1Probe=<bool> bug1Gate=<bool> bug2HostGunshot=<bool>`。`configPath` 必须指向本次实际使用的主配置绝对路径，不能拿另一进程、上一次日志或猜测安装目录里的同名文件代替。

三个布尔分别是三项当前实际生效的配置值：要声称某项已启用，对应值必须为 `True`；本轮说明声称三项均开启时，三者必须全部为 `True`。单项试验仍可只开所需项，其他项如实为 `False`，不新增“三项必须联动开启”的依赖。**任何 cfg 文件里写了 true 都不是启用自证**；自证行缺失、路径不符或值不符，先停止该项测试并修正配置/证据，不能写成已启用。

主 cfg 中用户已经保存的 `true` 必须被尊重，已经保存的 `false` 也不得被擅改；缺省 false 只用于首次生成/缺项绑定，不得在每次启动、升级或入口注册时强制写回覆盖。主 cfg 缺项不会从旧独立文件偷读 true；显式开启仍受该模块原有角色、目标、profile、一次预算等运行资格约束，配置 `True` 不代表已经执行成功。

升级说明及测试入口必须直接告知旧三个文件已停用且值不迁移，要求按上述自证行的路径在主 cfg 重新设置相应项，再核对实际布尔。不能只把这项破坏性兼容变更埋在报告尾部，也不能仅给出一行日志而不解释旧值失效。

## 4. 不变的安全内核与审查/验收边界

[T88:21–36](architect-ruling-t88-release-diagnostics.md#L21-L36)继续有效：关闭任意或全部上述开关，**不得关闭 Bug3 的当前 epoch/map Ready 授权、实例 NativeLoadCompleted/加载完成证明、拒绝不安全保存、once-only 执行/通知账本、真实注册失败传播及必要失败告警**。诊断关闭、诊断失败或证据 UNKNOWN 都不是保存放行条件，正常用户不用开启诊断来获得保存保护。

[T88:7–15](architect-ruling-t88-release-diagnostics.md#L7-L15)的同一主 Release 程序集/不拆诊断 flavor，以及 [T88:55–63](architect-ruling-t88-release-diagnostics.md#L55-L63)的同字节验证、独立审查和如实统计边界不变；G 的侵入性、授权与回滚安全门也不因配置合并而变化。

**本追认只改变配置契约，不授予任何 PASS。** 三布尔为 True 只证明对应配置已启用，不证明 G 闸门成立、Bug1 平滑改善、Bug2 S1–S4 或 Guest 互听通过，不证明 Bug3 真实机闭环，更不构成可发布授权；实现字节仍须完成适用双轴审查和原有真实验收。

## 5. 给需求角色的一句话增量要求

建议需求角色在 [requirements-v2](requirements-v2.md) 增加 `### 4.2`，保留全文其他条款，不由本票修改；增量要求为：**自 0.2.4.11 起三个旧独立 cfg 停读且不迁移，三个开关仅绑定主 cfg、缺省 false 且不得覆盖用户已保存值，部署/测试以当前启动 `[Diagnostics] configPath=<绝对路径> bug1Probe=<bool> bug1Gate=<bool> bug2HostGunshot=<bool>` 核对实际启用项并明示旧值失效的破坏性兼容变更，所有开关关闭仍保留 Bug3 安全内核，任何配置启用事实均不授予功能 PASS 或发布许可。**

末行摘要按 UTF-8 计算，覆盖末行之前的全部字节（包括末行前的换行），不含摘要末行自身。

self_sha256(body)=ba2c572ed29f0d9a9207b8143afbe8808b0e06b5c2bb471b4ccc8e0a54c9ea51
