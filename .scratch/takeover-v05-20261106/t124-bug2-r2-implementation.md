# T124 Bug2 修复轮 r2 报告（规格轴 t118 F1–F5 + 工程轴 t119 R23-01..R23-08）

> 结论边界：本报告只证明「原生叶是否播过可观测、生产 leaf 解析有覆盖、热开启可恢复、账本有界、补声后异常有标记、真实 fire 插入离线可验证」。**不声称 Bug2 已修复，也不声称实机可听性通过**——真实听感、三端互听、Mod 兼容、真机 Harmony 命中仍是 UNKNOWN，见 §5。

## 1. 摘要（A–H 逐条）

- **A（blocker，F1/R23-04）**：删除 `GunshotHostAudioRuntime.TryCompensateAfterSend` 里对 `RealSendAnchor`/`NativeLeafNotEntered`/`AssociationComplete` 的硬置。现在：
  - `RealSendAnchor` 来自 `FireFrame.AnchorReached`（只有 transpiler 在唯一 `SendPlayShoot.Invoke` 之后插入的调用才会置位）；
  - `AssociationComplete` 来自 `FireFrame.AssociationChecked`（`ReferenceEquals(frame.Gun, gun)` 勾稽通过后置位）；
  - `NativeLeafNotEntered` 来自 `playGunshot` Postfix 观察器（`OnNativeLeafObserved` 按「枪实例 + 当前 fire 帧」记录 `NativeLeafEntered`）；观察器未安装时该事实恒 false 并附 `CollectionError="native-leaf observer not installed"`（fail-closed，宁可没声不可二重声）。
  - 新用例 `Bug2-18`：原生叶已进入 → 不补偿；变异 A（观察结果恒 true）必红。
- **B（high，R23-02）**：`ResolveDefaultLeaf()` 改为 `internal static`，用例 `Bug2-19` 直接调用生产解析并断言 `leaf.Method == UseableGun.playGunshot`；变异 B（反射名 `playGunshotX`）必红。
- **C（high，R23-01/R23-03）**：`Control` 保存 `_harmony`，`SettingChanged` 走 `Apply(_harmony,"config-change")` 并检查返回值；`Apply` 改返回 `bool`，失败向上传播（`RoleLogger.Error`），成功前先 `Runtime.ClearFailClosed()`；`Patch.RegisterManual` 在 `harmony==null` 时直接 Fail 且**不落 `_attempted` 闩锁**；`Uninstall` 清闩锁。用例 `Bug2-20` 覆盖「关→开→关」、`RegisterManual(null)` 失败后重试成功。
- **D（medium，F2）**：补声后 fire 抛异常不再走无效迁移 `Completed→Fail`：`OnFireExit` 在 `frame.Compensated` 时 `faulted` 记 `AbortedAfterAnchorCount++` + gate `decision=AbortedAfterAnchor`，正常记 `SuccessfulShotCount++`；只有未补偿且 faulted 才 `Ledger.Fail`。用例 `Bug2-21`。
- **E（medium，F3）**：账本加插入顺序队列 + `MaxEntries=1024` 上限（超限淘汰最旧键）；`PurgeExcept(sessionEpoch, connectionGeneration)` 在连接世代变化时撤销旧键，`PurgeWeapon(netId, keepWeaponGeneration)` 在武器实例世代变化时撤销旧键。用例 `Bug2-22`：旧世代键被清理、当前世代去重不受影响、插入 `MaxEntries+16` 后 `Count<=MaxEntries`。
- **F（medium，R23-05）**：用例 `Bug2-23` 用**真实 `fire()`** 的 `GunshotIlScan` 列表转 `CodeInstruction` 后跑 `Fire_Transpiler`，断言插入 2 条、位置在锚点后（`ldarg.0` + `call TryCompensateAfterSend`）、并逐分支校验目标 offset 落在解码指令边界上。真机 Harmony 重写后的实际命中仍属 UNKNOWN（见 §5）。
- **G（medium，F4）**：`requirements-v2.md` 已登记 **S3 UNKNOWN（本轮自动化不可验证）**，与 S1/S2/S4 同级。
- **H（low）**：
  - R23-06：`ResolveDefaultLeaf` 只在成功时置 `_defaultLeafResolved=true`；失败 `LeafResolveFailures++`，不永久缓存 null；`ClearFailClosed()` 允许运行期恢复。
  - R23-07：gate 行删除重复的 `token=`（保留 `epoch=`）；锚点摘要改为 `idx=<n> off=0x<hex>`，detail 同样带 idx/offset。
  - R23-08：`git add` 了 `Core/Patches/GunshotHostAudio/`、`WhitelistTests/Evidence/Runtime/GunshotHostAudioTests.cs`、`WhitelistTests/Program.cs`、`SteamP2PFriends.csproj`、`PatchRegistrationDiagnosticsModules.cs`（未 commit）。

## 2. 改动清单（file:line）

| 文件 | 内容 |
|---|---|
| `Core/Patches/GunshotHostAudio/GunshotHostAudioPolicy.cs` | `MaxEntries`（:116）、队列淘汰（:130）、`PurgeExcept`（:147）、`PurgeWeapon`（:170）、`FormatGateLine`（:250，删 `token=`；`leafNotEntered` :267） |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioRuntime.cs` | `ClearFailClosed`（:81）、`SetNativeObserverInstalled`（:87）、`OnNativeLeafObserved`（:96）、`TryCompensateAfterSend`（:144；`AnchorReached` :167、`AssociationChecked` :169、三项事实 :179-193）、`OnFireExit`（`AbortedAfterAnchorCount` :306、`SuccessfulShotCount` :311）、`ResetForTest`（:404）、`PurgeWeapon`/`PurgeExcept` 挂钩（:424/:439）、`ResolveDefaultLeaf`（:491） |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioPatch.cs` | `InstallerForTest`（:36）、`playGunshot` 解析与安装（:91-110）、`NativeLeaf_Postfix`（:286）、`Uninstall`（:151）、锚点 detail（:229）、`AnchorSummary`（:271） |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioControl.cs` | `ApplyForTest`（:83）、`Apply` 返回 bool + `ClearFailClosed`（:115）、`_harmony`/`_testEnabled` |
| `WhitelistTests/Evidence/Runtime/GunshotHostAudioTests.cs` | `Bug2-18`（:358）、`Bug2-19`（:379）、`Bug2-20`（:395）、`Bug2-21`（:426）、`Bug2-22`（:446）、`Bug2-23`（:471）；真实 IL 分支边界检查（:486） |
| `WhitelistTests/Program.cs` | 注册 Bug2-18..23（:1040-1045） |
| `.scratch/takeover-v05-20261106/requirements-v2.md` | 新增 S3 UNKNOWN 登记 |

## 3. 变异表（全部 `-t:Rebuild`，逐字节还原）

| 变异 | 做法 | 变异源码 sha256 | 变异 DLL sha256 | 新增红用例 | 还原 |
|---|---|---|---|---|---|
| A（观察器结果恒 true） | `facts.NativeLeafNotEntered = !frame.NativeLeafEntered;` → `= true;` | `76404a6836dc20128b750e3f09026fa5bc4959467d0ea36795ca2d9576e2a5bc` | `0e35a5be9d1f4015e2a2f71f7e1f476bf82a716c0ba5fe9070fdf809bfa86296` | `Bug2-18 FAIL: native leaf already entered must not compensate, calls=1`（821/824） | Runtime 回 `4bc62d3a…`，DLL `a714d055…`，822/824 |
| B（生产解析名改错） | `AccessTools.Method(typeof(UseableGun), "playGunshot", …)` → `"playGunshotX"` | `012b5654b1db7c0cc1fa604f3289ef09c6097887930c67d55449beea455f953b` | `101e6656dd457b75d20e04382824c087f9b4c772334c950c9bb98e0b5fc55156` | `Bug2-19 FAIL: ResolveDefaultLeaf must resolve UseableGun.playGunshot`（821/824） | 同上 |

## 4. 三条验证命令（本轮终态，全部 `-t:Rebuild`）

1. `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` → **0 警告 0 错误**，DLL `a714d055657322bb21889518ac77bf188d1d4fa5311d25c46c31a29e7b71b141`（1584640 B）。
2. `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` → **0 错误**，4 条既有警告（`NativePendingBatchLeafProbe.cs:715 CS0219`、`:41 CS0649`、`R01E02SliceATests.cs:39/2021 CS0649`）。
3. `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` → **822/824 PASS**，仅 `GBA01`/`NIL01` 环境失败；Bug2-01..23 全 PASS。

> Bug2-23 首轮曾因把 `GunshotIlScan` 的「绝对目标 offset」误当 delta 而报 `branch target not on instruction boundary at idx=11`；修正为直接使用绝对目标（switch 用目标数组）后转绿，属测试解码语义修正，不改生产。

## 5. 未闭环（UNKNOWN，不得视为已验收）

1. 真实听感：Host 是否真能听到、音量/衰减是否与 U3DS 等价——未做实机 1H+1G。
2. 三端互听：1H+2G 下 B 是否仍恰好一声、无重复——未做实机。
3. Mod 兼容：未知 Mod 自定义音效出现本地叶时的「立即停补并记录」路径未在真实 Mod 环境验证。
4. 真机 Harmony 重写命中：离线已用真实 `fire()` 指令列表验证插入数量/位置/分支边界，但真实 Unity 运行时 Harmony 生成方法的实际执行未在实机验证。
5. 原生 `playGunshot()` 的 pitch 随机数对后续射击随机序列的帧级影响只做了逻辑等价证明（保存/恢复 `Random.state`），未做实机对比。
6. **S3 UNKNOWN**：同一真实 fire 入口的原生副作用对照（弹药/耐久/shot count/弹壳/projectile 次数）需 Unity-SDK 运行时观测；已登记于 `requirements-v2.md`，与 S1/S2/S4 同级。

## 6. 文件 sha256（本轮终态）

```
f65e995c5e9ef1383542445f93b66dd34ce9f6c33295f7972aa303c2d1cae671  GunshotHostAudioControl.cs
a74633f314bc2a772adb4dda8a483fda6e946fc6424bab841b00d33161adc905  GunshotHostAudioPatch.cs
064e67450fb59e61a67ac8fba13e38a951b08907581b3ba8be18b4b1c291df6d  GunshotHostAudioPolicy.cs
4bc62d3af706199ebb0b15956b28222f2cb248d1ddae9a8c97022dc91d3f9ac2  GunshotHostAudioRuntime.cs
ef512ba0a8fea0540b248fdb1588e3c969b672df2e652edd4f1094c8f8dda1e9  GunshotIlScan.cs
9fa0bac3eba4a4e03f4bd589e6ace078d91c93606bf9055a420e7f67bcebfbb5  GunshotHostAudioTests.cs
276f09a7e8e4f2a0c127d78e4409e2a1828dae0cb406d9a07f61a6d194208d77  WhitelistTests/Program.cs
e327865c031521fde970c139dc31c15eb02fcb648ebf9bd3ce75f46f0b4a68e5  SteamP2PFriends.csproj
321d3ef3ff5aee0c104e2b379c94830ed3f8031f75bc081f555c99e581504c18  PatchRegistrationDiagnosticsModules.cs
a714d055657322bb21889518ac77bf188d1d4fa5311d25c46c31a29e7b71b141  bin/Release/SteamP2PFriends.dll
```
