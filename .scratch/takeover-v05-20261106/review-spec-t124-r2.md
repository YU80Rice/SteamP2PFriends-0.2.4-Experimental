# t126 规格轴复审：t124（Bug2 修复轮 r2）——F1–F5 与「恰好一次」三道防线

- 复审对象：t124（ds-fixer），Bug2 Host 侧枪声补偿第二轮修复
- 判定依据：`architecture-decision-t115-gunshot-equivalence.md`（§3–§5）、`requirements-v2.md`（§4 S1–S4）、t118 的 F1–F5
- 审查方式：只读主树 + 仓库外副本 `_t126iso` 做变异；主树源码与 DLL 复审前后逐字节未变（`bin/Release/SteamP2PFriends.dll` = `a714d055…`，与送审一致）
- 结论：**pass**（0 条 acceptance 失败），另记 **1 medium + 1 low** 残余风险，均不改变本轮判定

## 1. 字节绑定

本机实测（`sha256sum` + `wc -l`），与派工票面逐项一致：

| 文件 | 行数 | sha256 |
|---|---|---|
| `Core/Patches/GunshotHostAudio/GunshotHostAudioControl.cs` | 136 | `f65e995c5e9ef1383542445f93b66dd34ce9f6c33295f7972aa303c2d1cae671` |
| `GunshotHostAudioPatch.cs` | 361 | `a74633f314bc2a772adb4dda8a483fda6e946fc6424bab841b00d33161adc905` |
| `GunshotHostAudioPolicy.cs` | 294 | `064e67450fb59e61a67ac8fba13e38a951b08907581b3ba8be18b4b1c291df6d` |
| `GunshotHostAudioRuntime.cs` | 561 | `4bc62d3af706199ebb0b15956b28222f2cb248d1ddae9a8c97022dc91d3f9ac2` |
| `GunshotIlScan.cs` | 275 | `ef512ba0a8fea0540b248fdb1588e3c969b672df2e652edd4f1094c8f8dda1e9`（未改） |
| `WhitelistTests/Evidence/Runtime/GunshotHostAudioTests.cs` | 671 | `9fa0bac3eba4a4e03f4bd589e6ace078d91c93606bf9055a420e7f67bcebfbb5` |
| `WhitelistTests/Program.cs` | — | `276f09a7e8e4f2a0c127d78e4409e2a1828dae0cb406d9a07f61a6d194208d77` |
| `bin/Release/SteamP2PFriends.dll` | — | `a714d055657322bb21889518ac77bf188d1d4fa5311d25c46c31a29e7b71b141` |

被审报告：`.scratch/takeover-v05-20261106/t124-bug2-r2-implementation.md`（9195B）。本轮判定只对上述字节有效。

## 2. F1–F5 逐条判定

### F1（blocker）：三行硬置 → 实测观察器（**关闭**）

- 硬置已删除：`GunshotHostAudioRuntime.cs:177-195` 现为
  - `facts.RealSendAnchor = frame.AnchorReached;`（:181）
  - `facts.AssociationComplete = frame.AssociationChecked;`（:182）
  - `facts.NativeLeafNotEntered = !frame.NativeLeafEntered;`（:185，仅在 `_nativeObserverInstalled` 为真时）
  - 未安装观察器时代 `false` 并写 `CollectionError="native-leaf observer not installed"`（:190-194）→ fail-closed
- 观察器本体：`Runtime.cs:96-125 OnNativeLeafObserved`（`_supplementalLeafDepth > 0` 时直接返回，自己的补声叶不算原生；按 `ReferenceEquals(frame.Gun, gun)` 勾稽后置 `frame.NativeLeafEntered=true`）；安装点在 `Patch.cs:109-113`（`harmony.Patch(playGunshot, postfix: NativeLeaf_Postfix)`），`Patch.cs:285-289` 为 Postfix 实体
- 语义确认：`frame.NativeLeafEntered=true` ⇒ `NativeLeafNotEntered=false` ⇒ `IsEligible`（`Policy.cs:242`）不成立 ⇒ `decision=NotEligible`、不调用叶 —— 「原生已播过就停补」正确
- 主树已无任何 `facts.X = true` 硬置（grep 0 命中）
- 用例：`GunshotHostAudioTests.cs:358-377`（Bug2-18）断言 `calls==0`、`leafNotEntered=0`、`decision=NotEligible`、`NativeLeafObservationCount==1`
- 我的变异（M2，等价 Captain 的强制置真）：`facts.NativeLeafNotEntered = !frame.NativeLeafEntered;` → `= true;`，DLL `0e35a5be9d1f4015e2a2f71f7e1f476bf82a716c0ba5fe9070fdf809bfa86296`，**821/824，唯一新增红 Bug2-18** —— 与 t124 自报的变异 A 逐字节相同

### F2（medium）：`FIRE_ABORTED_AFTER_ANCHOR`（**关闭**）

- `Runtime.cs:288-323 OnFireExit`：`frame.Compensated && faulted` → `AbortedAfterAnchorCount++` + gate `decision=AbortedAfterAnchor`，并 `return`；`Compensated && !faulted` → `SuccessfulShotCount++`；只有「未补偿且 faulted」才 `Ledger.Fail(frame.Key)`（合法迁移 ClaimedBeforeCall→Failed）
- 不再出现 `Completed→Fail` 的无效迁移（`Policy.cs:212-221 Transition` 仍只接受 ClaimedBeforeCall 出发）
- 用例 `GunshotHostAudioTests.cs:426-444`（Bug2-21）断言 `leafCalls==1`、`AbortedAfterAnchorCount==1`、`SuccessfulShotCount==0`、gate 含 `decision=AbortedAfterAnchor`
- 计数是进程级静态字段，可被日志/统计读取；「进入可统计分母」成立

### F3（medium）：世代撤销旧键（**关闭**）

- `Policy.cs:114-141`：插入顺序队列 + `MaxEntries=1024`，超限淘汰最旧键；`PurgeExcept`（:147-165，按 sessionEpoch+connectionGeneration）、`PurgeWeapon`（:170-188，按 netId+weaponGeneration）
- 挂钩点：`Runtime.cs:421-427`（武器实例换代 → `PurgeWeapon`）、`:437-442`（客户端数量变化 → `PurgeExcept`）
- 用例 `GunshotHostAudioTests.cs:446-469`（Bug2-22）：旧世代键被清、当前世代去重仍生效、插入 `MaxEntries+16` 后 `Count<=MaxEntries`

### F4（medium）：requirements S3（**关闭**）

- `requirements-v2.md:65` 有一行独立登记「**S3 UNKNOWN（本轮自动化不可验证，与 S1/S2/S4 同级）**」，写明需要 Unity-SDK 运行时观测、离线 transpiler 验证不能替代、不得据此声称 S3 通过
- 位置正确：与 S1/S2/S4 同段落同级，不在脚注或附录里弱化
- 报告 §5 第 6 条同样列 S3，与 S1/S2/S4 并列（不是只写「已登记」了事）
- 唯一措辞瑕疵：该登记末句写「待真机 1H+1G 关闭」，而 S3 的关闭手段是 Unity-SDK 观测（1H+1G 是 S1/S4 的手段）——见 §7 备注，不影响判级

### F5（low）：热开启（**关闭**）

- `Control.cs:34-58 Initialize(harmony)` 保存 `_harmony`（:42）；`OnSettingChanged`（:90-105）改调 `Apply(_harmony, "config-change")` 并检查返回值；`Apply`（:107-134）返回 bool、enable 时先 `ClearFailClosed()`（:115）再 `RegisterManual`，失败向上返回并记 Error
- `Patch.cs:46-52`：`harmony == null` → `Fail("harmony=null")` 且**不落 `_attempted` 闩锁**；`_attempted=true` 只在安装成功后（:123）；`Uninstall`（:137-155）与 `ResetForTest`（:157-168）都清闩锁与观察器标志
- 用例 `GunshotHostAudioTests.cs:395-424`（Bug2-20）：关→开→关→再开成功、`!FailClosed`、`null harmony` 失败后仍可用真实 harmony 重试成功
- 因此不再需要「改配置需重启」的降级声明；实机热切换的真实时序仍属 UNKNOWN（见 §6）

## 3. 「每人恰好一次」三道防线

| 防线 | 实现 | 用例 | 我的独立验证 |
|---|---|---|---|
| 原生已播（观察器） | `Runtime.cs:96-125` + `:183-195`；`Patch.cs:109-113`、`:285-289` | Bug2-18（:358-377） | M2 强制置真 → 只红 Bug2-18（821/824） |
| 弹道枪（projectile） | `Collect` `:346 ProjectileNull = asset != null && asset.projectile == null`；`Policy.cs:237` 合取 | Bug2-03（`Tests:102` `NegativeNoLeaf` 断言 `projectileNull=0`） | 复用 t116 M2：删 `&& facts.ProjectileNull` → 只红 Bug2-03（已由 Captain 复跑，我核对门控链一致） |
| 开枪者本人（ShooterNotLocal） | `Collect` `:340-343`（`shooter.channel != null && !shooter.channel.IsLocalPlayer`）；`Policy.cs:235` | Bug2-04（`Tests:107`） | 复用 t118 MV-2（硬置 `ShooterNotLocal=true` → 815/818 唯一红 Bug2-04）；本轮结构未变 |

**哪一道仍只有实现没有用例：观察器的「安装」这一环。**
`Patch.cs:109-114` 的 `harmony.Patch(playGunshot, postfix: NativeLeaf_Postfix)` 没有任何用例绑定，且 `_nativeObserverInstalled` 是由控制流在 `:124` 直接置 true，而不是由「patch 是否真的挂上」导出。我的变异 M1（删掉 :109-114 整块）→ **822/824 全绿**（DLL `2bacb65ad708ad1e4120bb349dfb0096e2764653ade81666b75108a83c95dfb3`）。此时 `SetNativeObserverInstalled(true)` 仍执行，于是 `NativeLeafNotEntered` 恒为 true、补偿照常发生 —— **F1 的重复声缺陷会以「无任何用例变红」的方式静默回归**。这不是当前字节的行为缺陷（当前字节确实安装了观察器），而是防线最薄弱环节缺少回归保护，见 §7 T126-F1。

## 4. 本审查官变异矩阵（副本 `_t126iso`，脚本 `D:/tmp/t126_mut.sh`，日志 `D:/tmp/t126-mut.log`）

每条均 `-t:Rebuild` 生产 + 重建测试项目 + 覆盖两处 DLL + 跑全量 exe：

| 变异 | 做法 | 变异 DLL sha256 | 结果 | 新增红 |
|---|---|---|---|---|
| M0 基线 | — | `a714d055…` | 822/824（仅 GBA01/NIL01） | — |
| M1 删观察器安装 | 删 `Patch.cs:109-114` | `2bacb65a…` | **822/824（全绿）** | 无 ⇒ 安装边无绑定 |
| M2 观察结果恒真 | `:185` `!frame.NativeLeafEntered` → `true` | `0e35a5be…` | 821/824 | Bug2-18 |
| M3 去掉补声叶深度标记 | 删 `_supplementalLeafDepth++/--`（`:243-251`） | `56fa71ab…` | **822/824（全绿）** | 无 ⇒ 见 T126-F2 |
| R0 还原 | — | `a714d055…` | 822/824 | — |

- 副本与主树同源（tar 排除 `.git/bin/obj/Release/publishi/.scratch`），基线 DLL 与送审逐字节一致，说明 M1/M3 的「全绿」不是环境差异导致的
- 副本已删除；主树未做任何写入（`bin/Release/SteamP2PFriends.dll` 复审结束时仍为 `a714d055…`）

## 5. 越界核查（**未越界**）

模块内 grep（`GunshotHostAudio*.cs`）：

- 无 `ClientMethodHandle` 改动、无新增 RPC、无 `SendPlayShoot.Invoke` 的生产期调用（仅注释与 transpiler 定位）
- 无 `ReceivePlayShoot()` / `shoot()` / `project()` 调用（仅注释声明不调用）
- 无 helper 收件集写入、无 `SendAndLoopback`/`GatherRemoteClientConnections`/`InvokeLoopback` 调用
- 唯一副作用路径是 `leaf(gun)` → 原生 `playGunshot()`（`ResolveDefaultLeaf` `:493-520`，只在成功时缓存），插点为 `anchor+1 Ldarg_0` / `anchor+2 Call TryCompensateAfterSend`（`Patch.cs:332-334`），位于唯一 `SendPlayShoot.Invoke` 之后、早于 durability，栈净变化 0
- 锚点不匹配即抛（`:317-322`）→ `Fail` → fail-closed，不猜行号

## 6. 口径诚实与 UNKNOWN

报告 §3 结论边界与 §5 六条 UNKNOWN 覆盖了本票要求的四项并有扩展：

- 实机听感（Host 是否真能听到、音量/衰减）— UNKNOWN
- 三端互听（1H+2G 下 B 是否仍恰好一声）— UNKNOWN
- Mod 兼容（未知 Mod 自定义音效出现本地叶时停补）— UNKNOWN
- 真机 Harmony 命中（离线已用真实 `fire()` 指令列表验证插入，真实重写后执行未验）— UNKNOWN
- pitch 随机数对后续射击随机序列的帧级影响 — UNKNOWN
- S3 原生副作用对照 — UNKNOWN（已在 requirements 登记）

报告首行即写「不声称 Bug2 已修复，也不声称实机可听性通过」，未把 822/824 当成可听性 Green。**未发现夸大表述。**

## 7. 残余风险（不改变本轮 pass，但须有归属）

### T126-F1（medium）：观察器安装边无回归保护，且「已安装」标志不来自实际 patch 状态

- 位置：`GunshotHostAudioPatch.cs:109-114`（安装）+ `:124`（`SetNativeObserverInstalled(true)`）
- 问题：`_nativeObserverInstalled` 表示「注册流程走到这里」，不表示「`playGunshot` 上确实挂着 Postfix」。M1 实测删掉安装块后 822/824 全绿，而运行时会把「原生叶未进入」当成事实，重复声缺陷静默回归；这与 t115 §4「记录缺失即拒绝补偿，不以没日志推定零次」的精神相反（虽然当前字节行为正确）
- requiredFix（择一，建议前者）：①注册成功后用 `Harmony.GetPatchInfo(playGunshot)` 校验 owner/PatchMethod 含 `NativeLeaf_Postfix`，校验通过才 `SetNativeObserverInstalled(true)`，否则 `Fail("native-leaf observer not installed")`；②或补一条 IL 契约用例断言 `RegisterManual` 对 `playGunshot` 调用了 `harmony.Patch`（含 postfix 方法名），并把 M1 作为该用例的变异证据
- 归属：建议并入下一轮实现票；若 Captain/Architect 认为应作为残余风险接受，须在 `requirements-v2.md` 与本模块注释中写明「观察器安装无自动保护，删除后静默降级为重复补偿」

### T126-F2（low）：补声叶的 `_supplementalLeafDepth` 守卫无绑定

- 位置：`GunshotHostAudioRuntime.cs:243-251`（自增/自减）、`:100-103`（守卫）
- 问题：M3 删掉深度标记后 822/824 全绿。当前不产生双声（本键已 Claim，且 `Compensated` 已置），但会污染 `NativeLeafObservationCount` 与「该帧是否原生已播」的语义，一旦后续把「锚点后是否出现原生叶」用于判定，就会误报
- requiredFix：补一条用例——注册真实的 `LeafInvokerForTest` 使其内部回调 `OnNativeLeafObserved`，断言 `NativeLeafEntered` 仍为 false 且 `NativeLeafObservationCount==0`

### 备注（非 finding）

`requirements-v2.md:65` 末句「待真机 1H+1G 关闭」与 S3 的手段（Unity-SDK 观测）不一致；建议改为「待 Unity-SDK 观测关闭（可随 1H+1G 同轮执行）」，避免后续把 S3 的关闭权误记到听感测试。

## 8. pass 的边界

- 本判定的 pass 只针对**自动化层契约**：F1–F5 关闭、三道防线主体成立、不越界、口径诚实
- **不等于 Bug2 已修复**：S1/S2/S4 与 Guest↔Guest 互听仍须真机 1H+1G / 1H+2G；真实 Harmony 命中、Mod 兼容、音量为 UNKNOWN
- 不等于 0.2.4.11 可出包：T126-F1 建议在冻结前关闭，或由 Captain 书面接受为残余风险
- 台账层面仍有大量「verdict 覆盖的 revision 已不存在」告警未清，本票只对 §1 列出的字节负责

## 9. 自绑定

本报告仅对 §1 的字节集合有效。复审期间主树未改；变异全部在 `_t126iso` 完成并删除。

---

本文件正文（本行以上）13636 B，sha256 = `bcb607f66587b3aa4dca0b6feaf64bc206bcd2613234f58398d91e10e4803a55`。判定只对 §1 字节集合有效；正文哈希在上行给出，追加本附录不改变正文。
