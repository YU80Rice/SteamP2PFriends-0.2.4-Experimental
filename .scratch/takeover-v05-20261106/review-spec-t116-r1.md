# T118 规格轴复审：t116 Bug2 主机侧枪声补偿

> 审查对象：t116 交付的 9 个文件字节（见 §1）。
> 判定依据：`.scratch/takeover-v05-20261106/architecture-decision-t115-gunshot-equivalence.md`（19458B / `bbe572c230ac2d4c5481fbd6d329e1f326763badce54fd0ceff6d77470de212a`，111 行）、`requirements-v2.md` §4（Bug2 / S1–S4）。
> 审查方式：主树只读；变异在仓库外副本 `_t118iso` 完成后逐字节还原并删除。未评代码风格、命名、注释密度。

## 结论

**verdict = needs_revision（status=failed）**。

t116 的最小方案主体是对的：确实复用该 `UseableGun` 实例原有的 `playGunshot()`，确实插在真实 `SendPlayShoot.Invoke` 正常返回之后、耐久修改之前，模块内不存在任何发送面（无 `shoot()/project()/ReceivePlayShoot()`、无新增 RPC、无 `ClientMethodHandle` 改动、无 helper 收件集、无 dedicated 伪装）。自身的去重账本（claim-before-call）与「Host 自己开枪不补」两条不变量经我独立变异证明是承重的。

但它没有达到 t115 要求的**可证等价**，原因集中在同一点：t115 把「该枪该次 native leaf 尚未进入」「实际路由没有 Host loopback」列为补偿的前置条件，并要求用叶观察器去**测**；t116 把这两条改成在锚点处硬置 `true`（`GunshotHostAudioRuntime.cs:111-113`），模块内不存在任何 `playGunshot` 观察器。我把这一行删掉后跑全量测试：**816/818 全绿**（§4 MV-3）——即这条唯一让门控通过的条件既不可观测、也无用例绑定。这不是「实机待验」，是能力缺失。

因此本票不能判 pass。下列 F1 是阻塞项，F2–F5 是必须在同一轮收口的规格缺口。

## 1. 被审字节（本机实测，与 Captain 票面逐项一致，判定只对这些字节有效）

| 文件 | 行数 | sha256 |
|---|---|---|
| `Core/Patches/GunshotHostAudio/GunshotHostAudioControl.cs` | 98 | `7eab9950cf6d1082aec67cf19d40fa9d54601e0c28f1b100bfa4e1452fdbfe71` |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioPatch.cs` | 313 | `6f8923421d2f5c2865e1eb276771ada469923c3bf35ab4fd1970d7bd7eb23b23` |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioPolicy.cs` | 230 | `527329c4f43cf01931bf547ea5c4e1374b8cc6091649a88f498b6d2de576eea7` |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioRuntime.cs` | 430 | `fa534c0f69d68c66d171cd3d0ec4fb6ccc46bd3c65eff748ee72b19c29e4a960` |
| `Core/Patches/GunshotHostAudio/GunshotIlScan.cs` | 275 | `ef512ba0a8fea0540b248fdb1588e3c969b672df2e652edd4f1094c8f8dda1e9` |
| `WhitelistTests/Evidence/Runtime/GunshotHostAudioTests.cs` | 449 | `822e20e3a1a1f6adf5aca0059c896b1f43fb891bf7a114c6c9b9a3d4960514fb` |
| `WhitelistTests/Program.cs` | — | `8291f50dd249c4532efa423ecf2e5393d543f292068159e45a50987cf4efc899` |
| `SteamP2PFriends.csproj` | — | `e327865c031521fde970c139dc31c15eb02fcb648ebf9bd3ce75f46f0b4a68e5` |
| `bin/Release/SteamP2PFriends.dll` | — | `45c00a52491e72fcb0fb4cf3de9feb966a71e4d1b4fce6d5d6424a141633fd6f` |

## 2. 逐条对照 t115 §3–§5

| 裁决要求 | 实现定位 | 判定 |
|---|---|---|
| 复用该实例原有 `playGunshot()`（SDK `UseableGun.cs:739-765`，唯一 `PlayOneShot` 在 `:763`） | `Runtime.cs:378` `AccessTools.Method(typeof(UseableGun),"playGunshot",Type.EmptyTypes)` + `:384` 委托；未复制 clip/衰减、未新建 AudioSource | **pass** |
| 插在真实 `SendPlayShoot.Invoke` **正常返回后** | `Patch.cs:284-285` 在锚点指令 `anchor+1` 插入 `ldarg.0; call TryCompensateAfterSend`；锚点=唯一 `ClientInstanceMethod.Invoke(NetId,ENetReliability,List<ITransportConnection>)`（`Patch.cs:140-194`），前 24 条内须有 `ldsfld UseableGun::SendPlayShoot`（`:196-216`） | **pass** |
| **早于**该次 fire 的 barrel durability 修改 | 锚点 index 82 / 0x010E 早于 `set_quality`（179/185）；插入点不跨分支 | **pass**（依据 t116 §2 IL 实测，我未独立重测 IL） |
| 真实 IL 与锚点不匹配必须拒绝启用，不猜行号 | `Patch.cs:65-69` 装前 `VerifyAnchorBeforePatch`；`:86-91` 装后校验 `TranspilerInsertions==2` 否则 Unpatch+Fail；transpiler 内 `:269-273` 匹配失败抛异常→`RegisterManual` catch→`Fail` | **pass** |
| 不回发 owner / 不复制 shoot | 全模块 grep：`ReceivePlayShoot`/`.shoot(`/`.project(`/`SendAndLoopback` 仅出现在注释；无新 RPC、无 `ClientMethodHandle` 改动 | **pass** |
| 「实际路由中没有 Host loopback」 | 无任何判定；`GunshotFireKey`/`Collect` 都不看传入 L | **fail（F1）** |
| 「该枪该次 native leaf 尚未进入」 | `Runtime.cs:112` `facts.NativeLeafNotEntered = true;` 由调用点硬置；`Collect`（`:217-248`）从不赋该字段 | **fail（F1）** |
| 叶观察器识别 supplemental scope、缺失/覆盖不全/作用域冲突拒绝补偿（§4） | 模块内**无** `playGunshot` 的 prefix/postfix/观察器；grep 只有委托解析与日志字串 | **fail（F1）** |
| 同键 `Unclaimed→ClaimedBeforeCall→Completed/Failed`，**叶调用之前**认领，Failed 不重试 | `Policy.cs:114-124` TryClaim 先于 `Runtime.cs:161` `leaf(gun)`；`:147-157` Transition 仅允许从 ClaimedBeforeCall 出发 | **pass**（MV-1 证明承重） |
| 世代撤销旧键、不按超时重放（§4 L53） | `Ledger._states` 只有 `Reset()`（`Policy.cs:142-145`，仅测试调用）；无按 epoch/connGen/weaponGen 的淘汰 | **fail（F3）** |
| fire 在音频锚点之后异常 → `FIRE_ABORTED_AFTER_ANCHOR`，不计成功枪、不重试（§4 L53） | `Runtime.cs:194-212` OnFireExit 仅 `Ledger.Fail(frame.Key)`；而 Completed 状态下 `Fail` 是无效迁移（`Policy.cs:150`），既无标记也无成功分母 | **fail（F2）** |
| RNG 只包住额外叶 | `Runtime.cs:136-184` 保存/`finally` 恢复，仅围绕 `leaf(gun)`；不包真实 fire | **pass** |
| 默认关闭、零 patch | `Control.cs:22` `DefaultEnable=false`、`:43` `ConfigFile(path,false)`；`Patch.cs:37-40` 未开启直接 return true 不 patch | **pass** |
| 失败路径 fail-closed 不打断 fire | `Runtime.cs:186-191` 外层 catch→SetFailClosed；`LogGate` 自身 try/catch（`:397-409`） | **pass** |

## 3. 对两个重点问题的直接回答

**问题一：是否真的等价于 U3DS 可听性（三端各一次、开枪者不会听到两次、弹道枪不补）？**

- 「弹道枪不补」：有硬门控 `facts.ProjectileNull`（`Runtime.cs:235`，`asset.projectile == null`），并有负控 Bug2-03；Captain 的 M2 与我的复核一致（删该条 → 唯一红 Bug2-03）。
- 「开枪者不会听到两次」：靠 `ShooterNotLocal`（`Runtime.cs:230-232` 取 `gun.player.channel.IsLocalPlayer`）。我用 **MV-2** 把它硬置 true，**Bug2-04 立刻变红**（815/818），说明这条门是承重的。
- 「每人恰好一次」：**当前只有一半是可证的**。自身补偿的「一次」由账本保证且可失败（MV-1 删掉 claim 校验 → Bug2-09/10/12 三红）；但「原生这次没有播过」无法被观测——t116 在锚点处断言它没播过。一旦原生路径（或未知 Mod）这次已经为 Host 播过，本模块不会停补，就会出现第二次声音，而账本会记为 `Completed`、测试全绿。

**问题二：规格有没有被悄悄放宽？** 有，两处，且都能在代码里定位：

1. 把 §3 的「native leaf 尚未进入 / 无本机 loopback」从**观测条件**改写成**常量真**（`Runtime.cs:111-113`）。这正好是 Captain 点名的两件事之一（「unknown mod 叶出现即停」在实现中根本不存在），而实现报告 §7.3 把它写成「未在真实 Mod 环境验证」，容易被读成「能力已有、只是没测」——事实是能力不存在（F1）。
2. 「锚点失配 fail-closed 只覆盖启动期」这一点**没有**发生：装前 `VerifyAnchorBeforePatch`、装后 `TranspilerInsertions==2`、运行时转译器再次定位，三层都在注册期完成，运行期不再重定位（SDK 在进程内不会变，可接受）。

## 4. 独立复跑与变异（副本 `_t118iso`，`-t:Rebuild` 生产+测试，覆盖两处 DLL，跑全量 exe）

| 轮次 | 变异 | Runtime sha256 | DLL sha256 | 结果 |
|---|---|---|---|---|
| M0 | 无（送审字节） | `fa534c0f…` | `45c00a52…` | **816/818**，失败仅 GBA01/NIL01（环境），DLL 与送审逐字节一致 |
| MV-1 | 忽略账本认领结果（`TryClaim(...)` 不再拦） | `622efa9e…` | `33820beb…` | **813/818**，红 Bug2-09（calls=2）、Bug2-10（calls=8）、Bug2-12（attempts=2） |
| MV-2 | 在锚点硬置 `facts.ShooterNotLocal = true` | `314cedbe…` | `c1df92f1…` | **815/818**，唯一红 Bug2-04「negative control must not compensate」 |
| **MV-3** | 删 `facts.NativeLeafNotEntered = true;` | `6d9344d7…` | `87ab7db5…` | **816/818 全绿** |
| R0 | 还原 | `fa534c0f…` | `45c00a52…` | 逐字节回送审值 |

MV-3 是本票的关键证据：生产 `Collect()` 从不给 `NativeLeafNotEntered` 赋值，唯一让它为 true 的就是被删的那一行；删掉后测试一条都不红，说明「原生没播过」既没被测、也没被观测。Captain 的 M1/M2 结果与我独立复跑的基线口径一致（M1 的 DLL `16344964…`、810/818 我未重跑，但同一路径的 MV-1 已给出等价结论）。

## 5. Findings

| id | severity | 问题 | requiredFix |
|---|---|---|---|
| **T118-F1** | high | `GunshotHostAudioRuntime.cs:111-113` 把 `RealSendAnchor`/`NativeLeafNotEntered`/`AssociationComplete` 硬置 true；其中 `NativeLeafNotEntered` 在 `Collect()`（`:217-248`）中从不赋值，模块内也没有 `playGunshot` 观察器，因此「该枪该次 native leaf 尚未进入」「实际路由无 Host loopback」两条 t115 §3 条件（裁决 L32/L39）无法判定，§4 要求的「叶观察器…记录缺失/覆盖不全/作用域冲突即拒绝补偿，不以没日志推定零次」（L49）完全未实现。MV-3 证明该行可删且测试全绿 → 该条件未被任何用例绑定，真实环境中它只能靠硬编码成立。后果：原生或未知 Mod 本次已为 Host 播放时不会停补，产生第二次声音并记 `Completed`，直接背离「每人恰好一次」。 | 增加 native leaf 观察：对 `UseableGun.playGunshot` 加 Prefix/Prefix 记录 scope（native/supplemental）与进入次数，或用实际传入 L 判定 loopback；`NativeLeafNotEntered` 必须由观察结果赋值，观察缺失即 `NotEligible` 并记 UNKNOWN；补一条「同键 native leaf 已进入 → 不补偿（且保留 native）」的可失败用例。若 Architect 决定不做观察，必须书面把该条件降级为 UNKNOWN 并在 cfg 描述与本报告写明「无法检测，存在重复风险」。 |
| **T118-F2** | medium | t115 §4 L53 规定的 `FIRE_ABORTED_AFTER_ANCHOR` 未实现：`Runtime.cs:194-212` 在 faulted 时只调 `Ledger.Fail`，而 `Completed→Failed` 是无效迁移（`Policy.cs:147-157`），所以「已补声、随后 fire 抛异常」既无标记也不会被排除出成功分母；`GunshotHostAudioTests.cs:318-322` 走的正是这条路径，却只断言 `leafCalls==1`。 | 在 `OnFireExit` 记录 anchor 是否到达/是否已补偿；`faulted && compensated` → 记 `FIRE_ABORTED_AFTER_ANCHOR`（独立计数 + 日志 + 不计成功枪），不得回滚已播声音；补对应用例。 |
| **T118-F3** | medium | t115 §4 L53「正常生命周期退出/重连/换枪按世代撤销旧键」未实现：`GunshotHostAudioLedger._states` 只有 `Reset()`（`Policy.cs:142-145`，仅测试用），`_weaponGenerations`/`_lastWeaponInstanceHash`/`_connectionGeneration` 只增不淘汰 → 每发一条字典记录无界增长，旧世代键永久驻留。 | 在 `ConnectionGeneration()`/`WeaponGenerationFor()` 换代时清理旧世代键（或改为有界 LRU，只保留最近 N 个 ordinal），并补一条「换代后旧键被清除且旧键重放被拒」的用例。 |
| **T118-F4** | medium | `requirements-v2.md` §4 的 **S3**（沿同一真实 fire 入口比较修复前后弹药消耗、shot count、耐久、弹壳、projectile 次数，不新增重复发射）既无自动化/SDK 证据，也未在报告 §7 的 UNKNOWN 清单中登记；报告 §4 用「grep 新模块无 shoot()/project() 调用」代替。S1/S2/S4 已如实列为 UNKNOWN，唯独 S3 被略过。 | 把 S3 加入 UNKNOWN 清单并写明需要 Unity-SDK/真机对照，或补 SDK 级对照证据（同一 fire 事件前后原生副作用计数）。 |
| **T118-F5** | low | 运行时热开启必失败：`Control.cs:70-80` 的 `OnSettingChanged` 调 `Apply(null, ...)`，`Patch.cs:52-56` 遇 `harmony==null` 直接 `Fail("harmony=null")` 并置 `FailClosed`；而 `Runtime.SetEnabled(false)` 不清 `FailClosed`（`Runtime.cs:58-67`），因此在 BepInEx 配置界面把 Enable 打开只会得到一条 FAIL_CLOSED 日志、功能不生效，必须重启。报告与控制台描述都未声明「需重启」。 | 保存 `_harmony` 供 `SettingChanged` 复用，或明确禁用热开启并提示「需重启」，且该路径不得把 `FailClosed` 永久置位；并在报告/配置描述中写明。 |

## 6. 已判 PASS 的部分（不得因为本票 needs_revision 而回退）

1. 调用面干净：只调该实例原有 `playGunshot()`，没有复制 clip/衰减、没有新建 AudioSource、没有 `shoot()/project()/ReceivePlayShoot()`、没有新增 RPC、没有改 `ClientMethodHandle` 分派、没有把 Host 塞进 helper 收件集、没有伪装 dedicated。
2. 锚点工程正确：唯一候选 + 装前验证 + 装后 `TranspilerInsertions==2` + 转译器二次定位，栈平衡（`ldarg.0` 与被调方法一参相抵）。IL 位置（0x010E 早于 0x0265）与裁决要求一致。
3. 去重账本正确且承重：claim-before-call、迁移唯一、Failed 不重试；MV-1 证明删掉即红。
4. 「Host 自己开枪不补」承重：MV-2 证明删门即红。
5. RNG 只在额外叶周围保存/恢复，不包真实 fire；用例断言 save/restore 各一次且后续序列不变。
6. 默认关闭与零副作用：cfg 默认 false、`saveOnInit:false`、未开启不 patch 不产日志（Bug2-07/17）。
7. 报告口径总体诚实：标题与 §7 明写「不声称 Bug2 已修复」「实机听感/三端/Mod/RNG 帧级为 UNKNOWN」。

## 7. 仍需 1H+1G / 1H+2G 才能关闭的项（归属后续真机票）

1. S1：Host 侧 10 发连发对应声音全覆盖、漏 0、重复 0。
2. S2：Host 实际音频终端（`playGunshot`/`AudioSource.PlayOneShot`）与 shot 一次关联、声源位置与射手原生声源一致。
3. S3：同一 fire 入口的原生副作用对照（见 F4）。
4. S4：Host→Guest 与两端自听、七类既有声音无新增漏声/重复。
5. Guest A↔Guest B：本轮拓扑不可验证，仍 UNKNOWN（requirements 已声明）。
6. 未知 Mod 同枪本地叶的真实行为（依赖 F1 先具备观察能力）。

## 8. 边界声明

- 本判定只对 §1 列出的 9 个字节有效；主树在我审查期间未被我修改（副本 `_t118iso` 内变异，已逐字节还原并删除）。
- 我未重跑 `dotnet build` 之外的三条命令以外的任何构建脚本；未写 `Release/`、`publishi/`、`Develop-Stage/`。
- 我未验证 `Provider.isConnected` 在 listen-host 上是否恒为 true（`Runtime.cs:225` 的门控）——该项属 SDK/真机核对，列入 §7 的观察点，不作为本票 finding。
- **pass ≠ Bug2 已修复**：即使 F1–F5 全部关闭，本票最多也只能证明「自动化层可判」，S1/S2/S4 与 Guest↔Guest 仍须真机。
