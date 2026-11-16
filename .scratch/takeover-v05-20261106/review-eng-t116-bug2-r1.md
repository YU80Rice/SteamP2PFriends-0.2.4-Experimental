# T119 工程轴复审：t116 Bug2 Host 枪声补偿（r1）

> 审查对象：t116 交付的 `Core/Patches/GunshotHostAudio/`（5 文件）+ 测试 + 接线。
> 审查轴：只看工程正确性、边界、回归风险、可维护性、与既有惯例的一致性；不评是否满足用户需求。
> 结论：**needs_revision**（2 high / 3 medium / 3 low）。离线证据链大体成立（M1/M2 可复现、M4/M5 证明账本与 RNG 真承重），
> 但**运行期热开启路径必然失败**、**生产 leaf 解析零覆盖**（改名后 816/818 全绿）两处属于「静默失效、自动化全绿」，
> 与本会话已出现过的 8 次假绿同类，不得放行到实机采集。

## 1. 被审字节（本人实测，与 Captain 表一致）

| 文件 | 字节 | 行 | sha256（前 16） |
|---|---|---|---|
| `Core/Patches/GunshotHostAudio/GunshotHostAudioControl.cs` | 3396 | 98 | `7eab9950cf6d1082` |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioPatch.cs` | 11171 | 313 | `6f8923421d2f5c28` |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioPolicy.cs` | 9364 | 230 | `527329c4f43cf019` |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioRuntime.cs` | 14974 | 430 | `fa534c0f69d68c66` |
| `Core/Patches/GunshotHostAudio/GunshotIlScan.cs` | 9244 | 275 | `ef512ba0a8fea054` |
| `WhitelistTests/Evidence/Runtime/GunshotHostAudioTests.cs` | 20758 | 449 | `822e20e3a1a1f6ad` |
| `WhitelistTests/Program.cs` | 156994 | 1138 | `8291f50dd249c453` |
| `SteamP2PFriends.csproj` | 28133 | 416 | `e327865c031521fd` |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDomainModules.cs` | 3913 | 96 | `6ad2099835000cd9` |
| `bin/Release/SteamP2PFriends.dll` | 1581056 | — | `45c00a52491e72fc` |

补充：实际接线点在 `...PatchRegistrationDiagnosticsModules.cs:84`（`GunshotHostAudioControl.Initialize(_harmony)`，try/catch → `ok=false`），该文件 sha256 前 16 = `321d3ef3ff5aee0c`，与 t116 报告 §8 一致。

## 2. 独立复跑（主树，-t:Rebuild）

| 命令 | 结果 |
|---|---|
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | exit 0，0 警告 / 0 错误，DLL `45c00a52`（确定性） |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | exit 0，0 错误 / 4 既有警告 |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | exit 1，**816/818**，仅 `GBA01`、`NIL01`（`TypeInitializationException SDG.Unturned.SteamPlayer → NetReflection → ReflectionTypeLoadException`，控制台缺 Unity 环境） |

Bug2 段 17/17 PASS（`grep -c "PASS \[Runtime\] Bug2"` = 17）。
副本 `../_t119mut` 独立重建同样得 `45c00a52` / 816/818，作为变异基线。

## 3. 自跑变异（副本 `../_t119mut`，逐字节还原，副本已删除）

| # | 改动 | 变异 DLL | 结果 | 新增红用例 |
|---|---|---|---|---|
| M1 | `Runtime.cs:161` `leaf(gun);` → `//leaf(gun);` | `331124d9...` | **810/818** | **Bug2-02/09/10/11/12/15**（与 Captain 红名单逐条一致） |
| M2 | `Policy.cs:172` `&& facts.ProjectileNull` → `&& true` | `559f7c34...` | **815/818** | **只红 Bug2-03**（与 Captain 一致） |
| M3 | `Runtime.cs:378` 反射名 `"playGunshot"` → `"playGunshotX"` | `30f5259e...` | **816/818 全绿** | **无**（R23-02 证据） |
| M4 | `Policy.cs:114-124` 账本 `TryClaim` 去掉「已认领即拒绝」 | `4f507f21...` | **812/818** | Bug2-09/10/12/14 |
| M5 | `Runtime.cs:173` `if (rngCaptured)` → `if (rngCaptured && Environment.TickCount == int.MinValue)` | `3eeabc84...` | **815/818** | **只红 Bug2-13** |

还原核验：副本源码回 `fa534c0f` / `527329c4`，重建 DLL 回 `45c00a52`；主树五个文件与开工哈希一致，副本已 `rm -rf`。

判读：
- M1/M2 复现了 Captain 的红名单与计数（**哈希不同**：M1 我 `331124d9` / Captain `16344964` / t116 报告 `2d5bfba6`；M2 我 `559f7c34` / Captain `5116627b`）。红名单一致说明语义等价，但**证据不可逐字节复现**，因为替换文本未逐字公开（同 R23-07）。
- **M3 是全绿**：把生产 leaf 的反射名改错，17 条 Bug2 用例无一失败。生产声音的唯一来源（`playGunshot`）在离线世界里没有任何契约钉住。

## 4. 五问逐条判定

**① 生产 leaf（`ResolveDefaultLeaf`）有没有被证明解析到 `playGunshot`？——没有，且 M3 证明零覆盖。**
`Runtime.cs:368-393` 用 `AccessTools.Method(typeof(UseableGun), "playGunshot", Type.EmptyTypes)` + `Delegate.CreateDelegate` 取叶；
17 条用例全部通过测试缝 `LeafInvokerForTest`（`Runtime.cs:52`，由 `Begin()` 在 `Tests:377-388` 注入）绕开了这条路径，
`leaf == null` 分支（`Runtime.cs:128-134`，FailClosed + Fail(key)）」也无任何用例。
M3 把方法名改错后 816/818 全绿：**「声音到底还在不在」在离线世界完全不可知**。这与本项目历史的「调用存在但结果被丢弃」同型。

**② Bug2-01 的合成 IL 是否意味着真实 fire 插入完全无证据？——不是「完全」，但缺口未被如实列进 UNKNOWN。**
`Tests:31-51` **读的是真实 `UseableGun.fire` IL**（`GunshotIlScan.Read(fire)`），断言 `SendPlayShoot` 的 `ldsfld` 恰好 1 处、
`TryLocateAnchor` 在真实 IL 上命中、且锚点指令是 `ClientInstanceMethod.Invoke`——这部分是真实 IL 证据，值得肯定。
缺口有二：(a) `Tests:53-79` 的「插入」只在 3 条合成 `CodeInstruction` 上验证，未对真实 fire 的 `CodeInstruction` 列表跑 `Fire_Transpiler`
（即未证明真实 IL 插入后总指令数 +2、插入点无分支目标、MonoMod 能重新编译）；(b) 报告 §2 注里说明了扫描器为何替代 Harmony，
但 §7「未闭环（UNKNOWN）」并未把「真实 fire 经 Harmony 重写后的可编译性/命中」列为 UNKNOWN。
离线可加的间接证据确实存在：可用真实 `fire` 的 `CodeInstruction` 列表调 `Fire_Transpiler`（`CodeInstruction` 不解析 UnityEx），
断言 `TranspilerInsertions == 2`、`patched[anchor+1] == Ldarg_0`、`patched[anchor+2].operand == TryCompensateAfterSend`，
并检查无 `Br*` 目标落在 `anchor+1`。**本票未做，故缺口成立。**

**③ `hostFireOrdinal` 由谁建立？Prefix 不执行会不会退化成常量吃掉后续 fire？——不会吃掉，但会静默不补。**
ordinal 只在 `Fire_Prefix`（`Patch.cs:238-241`）→ `OnFireEnter`（`Runtime.cs:69-80`）里 `++_nextOrdinal` 建立。
若 Prefix 未执行（被别的 transpiler 改写、Prefix 抛异常、或 Harmony 未生效），`_frames` 为空，`TryCompensateAfterSend`
走 `Runtime.cs:95-99` 的 `NoFireFrame` 分支直接返回：**既不补声、也不写账本**，因此不存在「同一常量键吃掉后续 fire」的路径。
反过来，Enabled 但 `FailClosed` 时 `OnFireEnter` 压入 `default(FireFrame)`（Key 全 0），此时 `TryCompensate` 在 `:88` 已早退，
也不会误用零键；该行为由 Bug2-15（栈关联/弹出）与 Bug2-07 覆盖。
唯一残余边缘：若 fire 进行中配置被热改到 enabled，本枪会拿到零键——但热开启本身必然失败（见 R23-01），当前不可达。

**④ RNG 恢复是否只在 finally、`LogGate` 抛异常会不会漏恢复？——不会漏，M5 证明恢复真承重。**
捕获在叶调用之前（`Runtime.cs:136-157`），恢复在 `finally`（`:171-184`）；`LeafCallCount++`（`:163`）之后才 `LogGate`（`:164`），
而 `LogGate` 自身整体 try/catch（`:395-410`），即使它抛出也会落到 `:166` 的 catch，`finally` 仍执行恢复。
M5 把 `finally` 的条件改成恒假 → **只红 Bug2-13**，证明「恢复」不是装饰。
残余：恢复动作本身若抛异常，会从 finally 逃到外层 catch（`:186-191`）→ `SetFailClosed` 且整局不再补声（黏性），属可接受的 fail-closed（本次不单列 finding）。

**⑤ 默认关闭时 Control 会不会仍然 resolve leaf / 装配 Harmony？——不会 patch、不会 resolve leaf。**
`Control.Initialize`（`:31-56`）→ `Apply(false)`（`:82-93`）→ `SetEnabled(false)` + `Uninstall()`（只 `Unpatch`，`_harmony==null` 时空转）；
`Patch.RegisterManual` 首行 `if (!Enabled) return true;`（`Patch.cs:37-40`），**零 patch、零订阅、不触碰 `ResolveDefaultLeaf`**。
Bug2-07/17 覆盖了「未安装」与「cfg 默认 false」。注意：`Initialize` 仍会创建 `ConfigFile`（`saveOnInit:false`，不落盘），
并在 `Apply(false)` 里做一次 `AccessTools.Method(fire)` 查找——无副作用。

## 5. 其余验收核查

**账本（claim-before-call / Failed 不重试）**：`Policy.cs:107-158` 的三态迁移与 `Runtime.cs:121-125`（先 claim 再取叶）成立；
M4（去掉「已认领即拒绝」）→ 812/818 红 09/10/12/14，说明去重确实承重；叶抛异常 → `Fail` → 后续同键 `TryClaim` 返回 false（Bug2-12）。
**不双声**：`fire()` 仅在 `Provider.isServer` 分支发 `SendPlayShoot`（SDK `UseableGun.cs:912-914`），Host 上客机 A 的 `channel.IsLocalPlayer=false`，
不会走本地 `shoot()`（`SDK:918-923` 注释明确「本地控制时才在 Shoot() 里再调」），所以补一次正好一声；Host 自己的 fire 被
`ShooterNotLocal`（`Runtime.cs:230-232`）挡住（Bug2-04）。
**锚点位置**：插入在 `SendPlayShoot.Invoke` 之后、`lastShot`/耐久 `Random.value`（`SDK:941`）之前，
与「补声不得改变耐久 roll」的意图一致；RNG 保存/恢复（M5）是这条意图的唯一保障。
**fail-closed 三条**：锚点失配 → `RegisterManual` 返回 false + `SetFailClosed`（`Patch.cs:65-69`、`:305-311`，不 patch）；
leaf 解析失败 → `Fail` + FailClosed（`Runtime.cs:128-134`）；RNG 不可用 → `Fail` + FailClosed（`:151-157`）。
三条都只影响「补不补声」，不抛回 fire、不改返回值，符合「宁可没声也不能打断真实 fire」。但后两条**无用例**（见 R23-02/R23-06）。
**越界**：新模块内 `ReceivePlayShoot` / `ClientMethodHandle` / `SendAndLoopback` / `InvokeAndLoopback` / 新建 `ClientInstanceMethod`
grep 0 命中；`shoot()` / `project()` / `playGunshot()` 仅出现在注释；无新增 RPC、无 Host 注入 helper。
**性能**：每次 fire 一次 `Collect`（约 12 次静态读 + 一次反射字段缓存）+ 一次 `FormatGateLine`（StringBuilder 384）；
gate 日志上限 200 条（`Runtime.cs:28`、`:401-404`），不会逐帧刷屏。
**隐私**：gate 行只含布尔、epoch/connGen/weaponGen/netId/ordinal、地图名，无 SteamID/玩家名（`Policy.cs:185-218`）。
**交付完整性**：`Core/Patches/GunshotHostAudio/` 与 `WhitelistTests/Evidence/Runtime/GunshotHostAudioTests.cs` 目前是 `??` 未跟踪，
而 `SteamP2PFriends.csproj:284-288` 已引用 → 干净检出无法编译（R23-08，与本项目 R19-04 同类）。

## 6. Findings

### R23-01（high）运行期热开启必然失败，且失败被吞、之后不可恢复
- 位置：`GunshotHostAudioControl.cs:74`（`Apply(null, "config-change")`）→ `:88`（`RegisterManual(harmony)`）→
  `GunshotHostAudioPatch.cs:52-56`（`harmony == null` → `Fail("harmony=null")`）；
  `:42-45` 的 `_attempted` 闩锁 + `Runtime.SetFailClosed` 黏性（`Runtime.cs:63-67`）使之后任何 toggle 都返回 `Installed=false`。
- 证据：`Control.Initialize(Harmony)` 只在启动时把 harmony 传给 `Apply`，**从未缓存**；`OnSettingChanged` 传 `null`。
  Bug2-07/17 只测「关闭态不安装」，没有任何用例覆盖「关闭→开启」。
- 后果：用户在 ConfigurationManager 里打开开关 → 只看到 `!!! FAIL_CLOSED harmony=null`，功能永久不可用（需重启进程），
  且 `Apply` 丢弃返回值，上层 `ok` 仍为 true（叠加 R23-03）。
- requiredFix：`Control` 缓存 `Initialize` 收到的 `Harmony` 并在 config-change 时复用；`RegisterManual` 在 `harmony==null` 时不置
  `_attempted`（允许修好后重试）；补一条用例：`ResetForTest` → 注入 seam harmony → 模拟 enable → 断言 `Installed==true`。

### R23-02（high）生产 leaf 解析零覆盖：`playGunshot` 改名后 17 条用例全绿
- 位置：`GunshotHostAudioRuntime.cs:368-393`（`ResolveDefaultLeaf`）；`GunshotHostAudioTests.cs:377-388`（`Begin` 恒注入 `LeafInvokerForTest`）。
- 证据：M3 `"playGunshot"` → `"playGunshotX"` → DLL `30f5259e`、**816/818 全绿**；`leaf == null` 分支（`Runtime.cs:128-134`）无用例。
- 后果：SDK 方法改名/重载变化时，功能静默消失（只有运行期日志一条 FAIL_CLOSED），自动化与实机前评审都发现不了。
- requiredFix：加契约用例 `AccessTools.Method(typeof(UseableGun), "playGunshot", Type.EmptyTypes) != null`
  + 加一个 `ResolveLeafForTest` 缝，断言「解析失败 → FailClosed、不重试、不抛回 fire」。

### R23-03（medium）注册失败不向上传播
- 位置：`Control.cs:88`（`RegisterManual(harmony);` 返回值被丢弃）、`Control.cs:50`（`Apply` 后不检查 `Patch.Installed/FailureReason`）。
- 证据：`Patch.RegisterManual` 在锚点失配 / `TranspilerInsertions != 2` / harmony 为空时**返回 false 而不抛**（`Patch.cs:52-91`），
  因此 `DiagnosticsModules:81-89` 的 `try/catch(ok=false)` 捕不到；`Bug2-17` 也未断言失败可见。
- 后果：注册阶段「部分成功」被当成成功（与本会话 Bug3 的 E5 同型）。
- requiredFix：`Apply` 返回 bool；`Initialize` 在 `Apply` 返回 false 时 `RoleLogger.Error` 并让接入点把 `ok` 置 false。

### R23-04（medium）`Collect()` 的生产事实与 `NativeLeafNotEntered` 只是断言，不是测量
- 位置：`Runtime.cs:108-113`（测试事实优先，随后硬编码 `RealSendAnchor/NativeLeafNotEntered/AssociationComplete = true`）、
  `Runtime.cs:217-248`（`Collect` 唯一生产采集）；`AudioSourcePresent` 依赖私有字段（`SDK UseableGun.cs:3269-3292`，仅 `equip()` 且非 dedicated 时创建）。
- 证据：所有用例都用 `FactsProviderForTest`（`Tests:382`），`Collect` 零覆盖；`NativeLeafNotEntered` 无法被证伪。
- 后果：若 `gunshotAudioSource` 在某一帧为 null（第三人模型未加载等），补偿静默不触发，只有 gate 行 `audioSource=0` 可见；
  「不双声」也只是由 `ShooterNotLocal` 间接保证，`NativeLeafNotEntered=true` 不提供任何保护。
- requiredFix：至少补 `Collect(null)` 的行为用例（返回 default、不抛、`CollectionError` 为空）与「字段为 null → 不补偿」的断言；
  实机 gate 行核对 `audioSource=1` 并写入实机说明；把 `NativeLeafNotEntered` 注释改为「由 channel 判定推出」。

### R23-05（medium）真实 fire 经 Harmony 重写的可编译性/命中未证明，且未列入 UNKNOWN
- 位置：`Tests:53-79`（合成 `CodeInstruction` 插入）；报告 `t116-...md` §2 注（说明扫描器替代 Harmony）与 §7（未列此缺口）。
- 影响：若 MonoMod 在真实 fire 上因 UnityEx/分支目标失败，只会在实机第一次射击时暴露；而离线全绿会让人误以为「接线已验证」。
- requiredFix：补第 ② 节的真实 `CodeInstruction` 插入用例 + 分支目标检查；报告 §7 增加该 UNKNOWN。

### R23-06（low）`ResolveDefaultLeaf` 失败永久缓存 null；FailClosed 无运行期恢复
- 位置：`Runtime.cs:370-375`（`_defaultLeafResolved = true` 先置，失败也缓存）、`:63-67`（`SetFailClosed` 只置真）。
- 影响：首次解析失败（程序集加载时序等）→ 整局静音，只能重启；不含任何重试预算。
- requiredFix：失败时不置 `_defaultLeafResolved`（或按 tick 限次重试），日志带 attempt 计数。

### R23-07（low）gate 行 `token=` 与 `epoch=` 重复；锚点摘要把指令序号写成 IL_xx
- 位置：`Policy.cs:204-205`（两字段同为 `key.SessionEpoch`）；`Patch.cs:233`（`AnchorSummary = "IL_" + index`，index 是指令序号而非字节偏移，
  报告里却是「index 82 / 0x010E」并列）。
- 影响：日志读者会把 `token` 当成另一个键；锚点定位时易把 82 误读成偏移。
- requiredFix：删掉 `token=` 或在 `AnchorSummary` 写 `idx=82 off=0x010E`。

### R23-08（low，交付）新模块与测试文件 `??` 未入库，csproj 已引用
- 证据：`git status --porcelain` → `?? Core/Patches/GunshotHostAudio/`、`?? WhitelistTests/Evidence/Runtime/GunshotHostAudioTests.cs`。
- requiredFix：`git add`（或明确「只从工作区打包」并记录哈希）。

## 7. 结论边界（不得越过）

- 本票只判「这批字节的工程可放行性」，**不判 Bug2 是否修好、更不判可听性**：实机 1H+1G / 1H+2G、Mod 自定义音效、
  真实 Unity 帧级 RNG 对比仍是 UNKNOWN（与 t116 §7 一致），且本票新增两条必须写进实机说明的观察项：
  ① gate 行 `audioSource=1`、`projectileNull=1`、`decision=Compensate` 是否真的出现在每次客机射击；
  ② 热开启（ConfigurationManager 内勾选）在修复 R23-01 前**必然失败**，实机只能「关游戏改 cfg 再启动」。
- 自动化放行的前提是 R23-01/R23-02 修掉；在此之前，17/17 全绿不代表「补偿链存在」——M3 已证明它可以整体消失而全绿。
- 本票未修改任何源码；变异全部在 `../_t119mut` 完成并逐字节还原，副本已删除；主树五个文件与 DLL 哈希与开工一致。

## 8. 复现命令

```
# 基线
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild                 # 0/0，dll 45c00a52
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe             # 816/818（GBA01/NIL01 环境）

# 变异（副本内；每条后必须重建生产再 cp 到测试 bin，只 build 测试工程不会重编生产 DLL）
cd ../_t119mut
python D:/tmp/mut119.py M1 && dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild \
  && cp bin/Release/SteamP2PFriends.dll WhitelistTests/bin/Release/ \
  && ./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe   # 810/818，红 02/09/10/11/12/15
python D:/tmp/mut119.py restore
# M2 同理 → 815/818 只红 03；M3 → 816/818 全绿；M4 → 812/818；M5 → 815/818 只红 13
```

<!-- self_sha256(body)=d8964458687db5bfdbf4dd2769991594f37f92c4cf23d63e17be1c20cf4f5e84 -->
