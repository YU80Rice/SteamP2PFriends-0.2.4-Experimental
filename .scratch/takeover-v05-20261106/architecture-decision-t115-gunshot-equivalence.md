# T115：SPF 与 U3DS 枪声可听性等价裁决

## 1. 最终定性：SPF 形态与 U3DS 不等价，Bug2 必须修复

本裁决按 Captain 于 2026-11-08 12:11 转达的用户最新基线修正：**原版不存在 SPF 提供的本地 listen-host 多人模式；验收参照是 U3DS 多人体验，而不是假设存在的“原版 listen-host”。** 在原生可听距离、正常武器/声音环境下，所有玩家都应能听到其他玩家开枪；自己的枪声由正常本地路径播放，不需要发回开枪者的 RPC。SPF 中 Guest 开枪而 Host 听不到，是产品真实缺陷，不能以 SDK 某分支也不播放来免责。

据此改写原票问题一：两端相同空列表分派只能说明**同一输入的代码行为相同**，不能证明产品等价或关闭 Bug2。三端 Guest→Guest 方向同样属于必须实现的体验，不再是可选项；尚未实测仍标 UNKNOWN，不得把“要求成立”写成“实测已通过”。范围是正常空间声音传播，不是绕过原生距离衰减把枪声广播成全地图音效。

本票接受 ds-analyst 已收齐的十二跳静态证据，不重做全链搜索。静态结论已足够选定局部兼容方案；本裁决只新增文档，不实施补音、不改路由、不运行游戏。沿用[原音频规格](bug2-audio-leaf-spec.md)的真实 Red 与副作用约束，以下冲突项以本裁决为准；旧文档保留历史，不覆盖。此文档完成不等于实现审查通过或实机 Green。

## 2. 两端、三端的机制差异与责任边界

令 H 为 Host 玩家，A 为开枪 Guest，B 为另一 Guest。固定为普通非 projectile 枪、正常成功 fire；L 是本次 SendPlayShoot **实际收到**的接收者列表。三端行另要求 B 在 helper 的范围内、身份有效且不是本地主机。下面是同一 L、同一角色事实下的分派语义对照，**不是可以运行的“无 SPF 原版三端联机”对照实验**。

| 场景 | helper 与原生枪体事实 | SDK 旧非 dedicated 分派 | SPF 现有分派 | 产品裁决 |
|---|---|---|---|---|
| 1H+1G，A 开枪 | owner A 被排除，IsLocalServerHost 的 H 被排除，L 为空；Host 上 A.channel.IsLocalPlayer=false，普通枪不进入本地 shoot | 空列表不触发 loopback，Host 缺本地枪声 | 也无 remote send/loopback，Host 缺本地枪声 | **SPF 可听性缺陷**；“代码同样不响”不是免责基线 |
| 1H+2G，A 开枪且 L=[B] | A/H 仍排除，B 是实际远程接收者 | 把任意非空连接误当本地，break 后 InvokeLoopback；有条件进入 Host 的 ReceivePlayShoot→shoot，但没有向 B 发送 | 向 B 的具体 transport.Send；列表没有 loopback 就不本机调用 | SPF 修正了旧分派的错误假设，但没有补齐 Host 声音，**整体体验仍不等价** |
| 1H+2G，B 不在 helper 范围或不可用 | L 可能仍为空 | 同空列表行 | 同空列表行 | 不得用三端人数推定非空列表、Host 补偿或 B 应收到远距离声音 |
| shooter 自己 / Host 自己开枪 | 正常 owner 预测和本地 shoot；不是 Host 上“非本地 A”的角色 | 保持原生 owner 路径 | 保持原生 owner 路径 | 不新增回发 owner，不增加自己的第二声 |

代码定位：[helper](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Provider/SteamChannel.cs#L160-L179)明确排除 Host；[原生旧分派](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/NetInvokable/ClientMethodHandle.cs#L97-L125)包含“不支持 listen server，所有非 dedicated 连接都本地”的假设；[SPF Prefix](../../Core/Patches/ClientMethodLoopbackPatch.cs#L378-L468)直接消费传入列表并区分真实 loopback 与远程连接，没有重新 Gather、也没有在这里再删除 Host。

因此“三端 Host 声音相对旧 SDK 条件 loopback 消失”是可解释的局部行为变化，但**不应恢复错误分派**来修复：那会丢掉 B 的远程消息，并把 Host 带回完整 shoot 的副作用。新形态必须同时保住正确远程路由并增加缺失的本地声音。此前“SPF 在 helper 层拆掉正常原版补偿”的指控仍不成立；它与“SPF 必须为自己提供的拓扑负责”不矛盾。

## 3. 最小方案：Host 上复用唯一原生音频叶，不增加 RPC

**决定补齐 Host 可听性。** 不需要独立声音系统、另建 AudioSource 或复制 clip/衰减计算。Host 上持有 A 的 UseableGun 实例，局部调用该实例已有的 `playGunshot()`；它在[739–765](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs#L739-L765)设置现有枪声音源，并在第 763 行唯一 `PlayOneShot` 位置播放。所谓“新增调用”仅指多一次受门控的 **Host 本机原生叶调用**，不是再发 SendPlayShoot，不是自己实现 PlayOneShot 的替代副本。

调用点限定为真实服务器 fire 中[原 SendPlayShoot.Invoke](../bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs#L912-L934)正常返回后、紧随其后的精确锚点；保留传入 L 原样，且必须早于该次 fire 的 barrel durability 修改。不能用 fire Postfix 才读取消音器末次耐久：原规格记录的 state[16] 在后半段已经可能变化。真实游戏 IL/生效 patch 与锚点不匹配必须拒绝启用，不能猜行号执行。

补偿适用条件同时成立：`HostManager.IsP2PHostMode`；`ShouldProcessClientHostListen()` 为真；非 dedicated、已连线且 server/地图就绪；真实当前世代的非本地 shooter；真实成功进入这次普通非 projectile fire 的发送锚点；当前枪/源/clip 可用；实际路由中没有 Host loopback；该枪该次 native leaf 尚未进入；装钩/关联完整。两个 Host 门控分别见[IsP2PHostMode](../../Platform/Host/HostManager.cs#L41-L44)与[listen gate](../../Platform/Host/HostManager.cs#L1606-L1614)，不能只用 `Provider.isServer` 或借用旧分派非 dedicated 条件。

| 排除项 | 必须保留的路径 / 原因 |
|---|---|
| `equippedGunAsset.projectile != null` | 远程 projectile 在原生 project 内已有音频叶；禁止再补一声，需独立负控。旧稿提到的例外在此升级为硬门控 |
| Host 本人、Guest 自己的本地 owner 路径 | 原生 `channel.IsLocalPlayer` 分支已经本地 shoot；不改预测、不给 owner 发消息 |
| turret、其他实体武器、Mod 自定义音效 | 不属于该普通 UseableGun 路径；已知 Mod hook 不是本次缺声补偿。不要推导为“所有服务器武器都缺声”。未知 Mod 出现同枪本地声音时先停补偿 |
| 原生或其他调用已进入 playGunshot / 存在本机 loopback | 任何 leaf 进入都禁止补偿；有进入但无实际听感，转查 clip/source/环境，不再加一次声音 |

禁止通过 `ReceivePlayShoot()`、`shoot()`、`project()`、补 Host 进 helper、强制 SendAndLoopback、伪装 dedicated 或全局改变 ClientMethodHandle 来补音。尤其 shoot 不只是声音，包含弹壳/枪口与 shot count 等行为；修复目标是补本地音频叶而不是重复整次射击。

## 4. 防重、P2 序号与不回发 owner 的硬契约

**P2 分配点改为实际 `UseableGun.fire()` Prefix 入口**，早于任何 helper/Invoke。在通过观测范围门控的真实调用上先分配单调 `hostFireOrdinal` 并压入当前调用栈；这是 fire 尝试序号，不是“成功射击计数”，允许失败形成序号空洞。发送锚点只把同一条记录升级为 `BROADCAST_REACHED`，整次 fire 正常返回后才归入成功分母；不足弹药/异常不得冒充成功。不能在首次音频叶、按键或 Postfix 才建键，否则无法关联发送前后的零次/重入。

主键为 `run/sessionEpoch + connectionGeneration + weaponInstanceGeneration + NetId + hostFireOrdinal`。同一 render frame、同一 simulation 或墙钟时间不能当唯一键；burst 中不同 fire 各有一键，单次 fire 的多 pellet 仍只有一声。P2 使用 Prefix 状态及 Finalizer 清理的栈关联，只记录、不改原异常；原网络 RPC 没有 shot ID，不新增 wire 字段。跨端只在本次隔离单发、顺序/武器/世代均唯一的条件下关联，模糊关联记 UNKNOWN。

每键只允许一次 `Unclaimed→ClaimedBeforeCall→Completed/Failed`。在补偿叶调用**之前**认领，重入与重复锚点不再执行；Failed 不重试。分别记 native 与 supplement 的 leaf/API 进入、正常返回及异常，原生 playGunshot 的一次进入即取消该键补偿，即使它随后没有播出。叶观察器须能识别当前 supplemental scope，不能把自己的进入当作另一次 native。记录缺失、覆盖不全或作用域冲突时拒绝补偿，不以“没日志”推定原生次数为零。

“Host 一次 / A 自己一次 / B 一次”靠三个**互不复制**的路径成立：H 只新增本机叶一次；A 保持自己的原生 owner shoot 一次，补偿函数不发送任何消息；B 保留原有 SendPlayShoot 的远程接收一次，不另转发、不另补 B。实际 L 仍排除 A；实际应用点必须证明没有朝 owner 的额外 Send/loopback。B 反过来开枪时按相同规则对调角色。调用音频叶不把 Host 变成 shooter owner，也不改变 RPC sender/recipient。

不能仅靠某个去重布尔值宣称对任意 Mod/未来调用“全球 exactly-once”。若补偿后意外出现同键 native leaf，**保留 native 行为**，记录重复、立即停用该候选，作为一次失败处理；禁止吞原生声音掩饰错误。若 fire 在音频锚点之后异常，则记录 `FIRE_ABORTED_AFTER_ANCHOR`，不计成功枪、不重试，并判候选失败；不存在把已播声音撤销的事务。正常生命周期退出/重连/换枪按世代撤销旧键，不按超时重放旧声音。

原生 playGunshot 的 pitch 会消耗 `UnityEngine.Random.Range`。只在额外音频叶周围保存 `Random.state`，`finally` 原样恢复；不得包住或重放真实 fire。SDK 集成必须证明随机状态及后续射击随机序列不变，弹药/shot count/耐久/弹壳/弹道与原先一致。最后一发消音器耐久、叶抛异常、重入、同帧连续 fire、换枪/重连必须有负控；“ammo 没多减”不足以证明无副作用。

## 5. 最小真实 Red 与 Green：两端一次、三端一次

下列是最小执行形态，不再寻找想象中的“原版 listen-host 基线”。沿用[原规格 §5–7](bug2-audio-leaf-spec.md#L92-L204)的环境、完整性与 S1–S4；观测窗口是测试协议，不变成产品超时。用普通单发、同枪、原生可听范围内无遮挡场景；先验证当前 listener/source/clip/volume/mute/对象状态正常，Host 自己同枪及已正常其他声音可听。固定输入、运行 game/SPF 完整身份及启用配置；观察模式不补音。

| 必需场次 | 修前最小真实 Red | 修后必要结果与反例 |
|---|---|---|
| 1H+1G | A 一次正常成功普通 fire，A 已本地听到；Host 对该次事件的可信 native leaf/API 观测完整为零且实际听不到，声音环境正控通过。一次即证明失败；按 S1 继续做 10 发明确分母 | H 每次恰好一声，A 自己仍恰好一声；H 开枪时 A 与 H 自己仍正常。缺一声或重复一声都失败，不把“Host 没在 helper”本身记 Red |
| 1H+2G | A/B/H 均在正常可听范围。A 一次成功单发，分别观察 H、A 自己、B；任一应听方向缺声或重复即真实失败。现有静态结论预测 H 缺声，但 **B 是否实测正常仍需记录** | A 连续 10 次隔离单发后，H=10、A self=10、B=10；再由 B 做同样一组，再验证 H→A/B 和各自 self。同时检查 owner 不在实际 remote sends。不能只看 Host 补到了就关闭三端互听 |

两端零叶/零 API 且音频环境、关联与装钩覆盖完整，是“缺少本地播放路径”的最小机制 Red；若已有 native leaf/API 进入但仍听不到，**产品仍然 Red**，但当前补偿候选不适用，应查源/环境/设备链而不是重复播放。若日志丢失、hook 未生效、源无效或正控失败，则不能下“零调用”机制结论，记录 UNKNOWN 并修复当前证据缺口；不能因此写产品 PASS。

三端需区分声明目标、具体 Send 正常返回、真正对端接收/叶和实际听感。`Unreliable` 发送返回不是交付保证；B 缺声时在真实接收路径定位丢包/ReceivePlayShoot gate/源，而不是用 Host 补偿伪造 B 成功，也不擅改可靠性。全部要求方向通过才可接受，未验证方向保持 UNKNOWN。

实施验证分层：自动化只证明门控、世代/一次账本、实际调用点 mutation 与失败不重试；SDK/匹配游戏依赖证明真实叶调用、锚点、耐久/RNG/射击副作用；真正两端/三端证明可听性和各端次数。CLR ECall 失败、模拟计数器或静态代码路径都不是可听性 Green。候选测试先审查后部署；一次补音命中不代替完整 S1–S4 和其他七类既有声音回归。

## 6. 与 Bug1 的关系：同类拓扑职责，不是同一修复

同步修正 Bug1 的措辞：**SPF 形态下 Host 看 Guest 的平移应与 Guest 看 Host 同样平滑**，不能说“服务器原生就是阶梯式，所以不算 Bug”。[T111](architecture-decision-t111-host-smoothing-plan.md)对本地输入与远端输入步进、非 server NSB、Host 远端缺少呈现平滑的解释仍是机制证据，不是验收免责；动画动作流畅也不自动证明世界位置平移流畅。

两 Bug 都是 SPF 把服务器和本地玩家观察者合在同一进程后，没有自动取得独立客户端那条呈现职责：Bug1 是连续位移呈现，Bug2 是离散声音事件。但“需要补本地呈现”不等于共享一个重播模块。Bug1 的延迟 buffer、模型叶资格 G、视觉时钟不能用于播放枪声；Bug2 不改变 input.tick、模拟 RATE、movement root、NSB 或碰撞骨骼，也不等 Bug1 才能修。

唯一必须联调的是时空与身份边界：同一 shooter/weapon/epoch，Host 的枪声仍使用原生音源/空间位置；不把 AudioSource 挂到延迟模型或为了声音移动 Third/骨骼，T111 的保护集合仍有效。联调同时检查站立/移动开枪、正常命中与声音方位/距离、其他未选玩家。两项单独通过也不代表组合状态已验收。

## 7. 修复顺序与 T111 的关系

保持 T111 的可并行、共享接线串行规则，不新造 Bug1→Bug2 的依赖。静态选型结束后，Bug2 下一步直接做最小真实 Red 闭环及窄叶实现/测试，**不再用两端 SDK 行为相同拖延实现，也不继续扩大假设搜索**。Bug1 同时推进当前 G 六格资格与独立纯核心工作；G 尚 UNKNOWN 不阻塞 Bug2，Bug2 某次人工场次不可用也不冻结 Bug1 的独立工作。

可交付候选都就绪时，先验证/交付较窄的 Bug2，再做 G 已合格的 Bug1 视觉候选；不要把这个默认优先级变成必须等所有音频人工验收结束才可写 Bug1 纯核心。csproj、registration、正式 runner、共享 bin/obj、manifest 与部署一次只由一个所有者集成；先分别审查相同候选字节，再组合回归。G 会主动改变模型位置，不能和音频基线同一采样窗口混跑。正式交付仍需最终 1H+2G，默认关闭的候选、诊断完善或文档完成均不叫修好了。

## 8. 原规格的逐项处置

以下编号是本裁决的可执行处置编号，不冒充 ds-analyst 未提供的原始 finding ID。未冲突的[原音频规格](bug2-audio-leaf-spec.md)继续有效；修订点明确覆盖旧稿中相应表述，不重写历史文件。

| 项 | 处置 | 对原规格的具体约束 |
|---|---|---|
| A1 实际事件链而非按键 | 采纳 | input/fire/Invoke/Receive/shoot/音频叶分层；成功 fire 与尝试分开，按键不算 shot |
| A2 接收者语义与实际播放分开 | 采纳并澄清 | Host 被 remote helper 排除本身不是错误；但不能据此免责。产品基线统一为 U3DS 所有方向可听，见 §1 |
| A3 只复用原生音频叶 | 采纳 | 本机 `playGunshot()`，不调用 shoot/project/ReceivePlayShoot，也不把 Host 硬塞进通用 helper |
| A4 每真实 fire 关联、账本与失败不重试 | 采纳 | 世代键、claim-before-call、重入/未知后来 native 的失败处理，见 §4；不靠时长去重吞合法连发 |
| A5 消音器采样、RNG 与射击副作用 | 采纳 | 原发送锚点返回后且耐久修改前；只隔离额外叶 Random.state，并验证真实随机后继与原生副作用 |
| A6 分层验证及所有方向回归 | 采纳 | 自动化≠SDK≠真机；两端 Host 缺声与三端 Guest 互听均是要求，不因静态路径符合预期而 PASS |
| R1 三份“独立接收者快照” | 修正 | 已核对 helperReturned 与 Invoke resolved 在此链中是同一列表别名、无中间改写；以一次实际 Invoke 的 L 为权威输入快照。helperKind/别名关系是来源元数据；applied 是具体 Send/loopback 的事件记录，不是第三份独立目标集合。通常不双采同一份数据。未来 ABI/外部 patch 破坏该前提需显式重开覆盖，不静默假设 |
| R2 H2 音源故障与 H1 并列根因 | 降级/修正 | 当前无独立证据把 source 故障列为同等级根因；先按已确认普通枪本机缺叶选型。source/listener/clip/环境仍必须作为 Red 排除项；真实 leaf 已进入但无声时，才按实际证据转诊 H2，禁止盲补 |
| B1 projectile 例外 | 补强为硬门控 | 原稿已有提及；远程 project 自带叶，不补，必须真实负控，不由“非本地枪”一概纳入 |
| B2 turret / Mod 例外 | 补充 | 不跨实体套用普通枪链；已知 Mod hook 不播放不代表所有 Mod 永远无补偿，未知本地叶出现即停补 |
| B3 实际 P2P Host 门控 | 补强 | 精确 `IsP2PHostMode` 加现有 listen gate/当前世代，不把纯 dedicated、单人、Guest 或 native passthrough 纳入 |
| B4 P2 序号创建时点 | 补充 | 真实 fire Prefix 入口先建 attempt ordinal；发送锚点和正常返回只升级状态，不事后补造关联，见 §4 |

明确废弃三种归因/修复结论：①“原版也不响，因此不是 Bug”；②“SPF 从 helper 删除 Host，恢复全局 loopback 即可”；③“Host 缺声只能与 Bug1 一起改原生服务器角色/时钟”。同时废弃把三份别名快照当作三重独立证明、把静态 H1 成立或 `PlayOneShot` 正常返回当作听感 Green 的用法。这里废弃的是结论/用法，不捏造原规格曾经采纳了它们。

## 9. 本票完成边界与下游交接

本票只交付架构裁决：已确定产品基线、双/三端路由解释、原生叶与门控、防重/不回发 owner、最小真实 Red、Bug1 同类但独立、T111 顺序及逐项处置。已接受的十二跳取证是静态证据；**当前未新增 runtime Red/Green、未证明 Host 真实听到、未证明 Guest 互听通过、未执行补音或改变任何生产代码**。

Builder 应直接用 §3–5 与现有规格落最小测试/实现，不再把代码对照当验收基线。真实 Red、候选无副作用/去重、工程与规格独立审查、同字节人工两端/三端闭环完成前，不部署为已修复发布。审查本裁决不等于替实现通过审查。
