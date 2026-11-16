# T116 Bug2 Host 枪声最小实现报告（0.2.4.11 开发线）

> 结论边界：本报告只证明「门控 / 账本 / 调用点 / 不重试 / RNG 恢复」的自动化层。**不声称 Bug2 已修复，也不声称实机可听性通过**——真实听感、三端互听、Mod 兼容仍是 UNKNOWN，见 §7。

## 1. 实现摘要

按 `architecture-decision-t115-gunshot-equivalence.md` §3–§5 的最小方案：Host 上取开枪客机 A 的 `UseableGun` 实例，调用其**原有** `playGunshot()`（SDK `UseableGun.cs:739-765`，唯一 `PlayOneShot` 在 `:763`），不复制 clip/衰减、不新建 AudioSource、不发 RPC。

新增模块目录 `Core/Patches/GunshotHostAudio/`（4 个实现文件 + 1 个 IL 扫描器），默认关闭，全部失败路径 fail-closed。

## 2. 实际 IL 锚点证据（原方法 `UseableGun.fire()`）

用探针（`D:/tmp/probe116`，引用 `Assembly-CSharp.dll` 与 `bin/Release/SteamP2PFriends.dll`，反射调用生产 `GunshotIlScan.Read`）实测：

- `fire()` 共 **1496** 条 IL 指令。
- 唯一锚点：**index 82 / offset 0x010E**，`callvirt Void Invoke(SDG.Unturned.NetId, SDG.NetTransport.ENetReliability, System.Collections.Generic.List\`1[SDG.NetTransport.ITransportConnection])`。
- 锚点前 11 条（index 71 / 0x00E7）为 `ldsfld SDG.Unturned.ClientInstanceMethod SendPlayShoot`；再前为 `ldarg.0`、`GetNetId()`、`ldc.i4.1`、`channel`、`transform.position`、`INSANE`、`GatherRemoteClientConnectionsWithinSphereExcludingOwner`。
- 锚点之后（0x0113–0x0119）：`ldarg.0` → `get_realtimeSinceStartup()` → `stfld lastShot`；随后 `channel.IsLocalPlayer` 分支、`InvokeModHookShotFiredEvents`、projectile 判断、`IncrementShotCountForRechamber`、barrel 判断。
- 耐久修改在锚点之后：`set_quality` 于 index 179 / 0x0265 与 185 / 0x0278，`sendUpdateQuality()` 于 index 189 / 0x0288。

因此 transpiler 把 `ldarg.0; call TryCompensateAfterSend` 插在锚点指令之后，**恰好位于 `SendPlayShoot.Invoke` 正常返回之后、`lastShot`/耐久修改之前**。匹配规则（`GunshotHostAudioPatch.TryLocateAnchor`，:140）：call/callvirt 到 `ClientInstanceMethod.Invoke`、3 参、`params[2]==typeof(List<ITransportConnection>)`，且前 24 条内必须有 `ldsfld UseableGun::SendPlayShoot`；候选必须恰好 1 个，否则 `VerifyAnchorBeforePatch`（:218）拒绝启用并 fail-closed。

> 注：测试宿主无法用 Harmony `PatchProcessor.GetCurrentInstructions` 读 `fire()`——MonoMod 会解析方法内全部 token，而 `fire()` 引用了宿主缺失的 `UnityEx` 程序集（`FileNotFoundException: UnityEx`）。故生产侧改用自带的只读扫描器 `GunshotIlScan`（只读 `GetMethodBody().GetILAsByteArray()`，token 解析失败返回 null），真实 Unity 运行时仍走 Harmony 插入 `CodeInstruction`。

## 3. 改动清单（file:line）

| 文件 | 内容 |
|---|---|
| `Core/Patches/GunshotHostAudio/GunshotHostAudioPolicy.cs` | `GunshotGateFacts`（15 布尔 + CollectionError）、`GunshotFireKey`、`GunshotAttemptState`、`GunshotHostAudioLedger`、`IsEligible`（:162）、`FormatGateLine`（:185） |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioRuntime.cs` | `OnFireEnter`（:69）、`TryCompensateAfterSend`（:86）、`OnFireExit`（:194）、`Collect`（:217）、RNG 测试缝（:55）、`ResetForTest` |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioPatch.cs` | `RegisterManual`（:34）、`TryLocateAnchor`（:140）、`VerifyAnchorBeforePatch`（:218）、`Fire_Prefix`（:238）、`Fire_Finalizer`（:244）、`Fire_Transpiler`（:251） |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioControl.cs` | BepInEx 配置 `[Bug2HostGunshot] Enable=false`；`ConfigFile(path, saveOnInit:false)` → 缺省不写盘 |
| `Core/Patches/GunshotHostAudio/GunshotIlScan.cs` | 只读 IL 扫描器（测试宿主无 UnityEx 时仍可定位锚点） |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs:84` | `GunshotHostAudioControl.Initialize(_harmony)`，try/catch，失败 ok=false |
| `SteamP2PFriends.csproj:288` | 新增 `GunshotIlScan.cs` Compile 条目 |
| `WhitelistTests/Evidence/Runtime/GunshotHostAudioTests.cs` | Bug2-01..17（17 条） |
| `WhitelistTests/Program.cs:1023-1039` | 注册 Bug2-01..17 |

## 4. 验收对应

| 验收 | 证据 |
|---|---|
| Red-first：删补偿调用必红 | 变异 `// leaf(gun);` → Bug2-02 `FAIL: compensation leaf calls=0 expected=1`（§5） |
| 排除面负控 | Bug2-03 projectile、Bug2-04 Host 自己、Bug2-05 非 P2P Host 门控（Guest owner 路径）、Bug2-06 turret |
| 去重/账本 | Bug2-09 同键两次一次、Bug2-10 单 fire 多 pellet 一次、Bug2-11 不同 fire 各一次、Bug2-12 叶失败不重试、Bug2-14 状态机、Bug2-15 Prefix/Finalizer 栈关联 |
| RNG | Bug2-13：仅额外叶保存/恢复；save/restore 各 1 次；恢复后序列一致 |
| 默认关闭 | Bug2-07 `Enable=false` 零 patch 零叶；Bug2-17 缺省 false；`saveOnInit:false` 零写盘 |
| 集成面 | grep 新模块无 `ReceivePlayShoot()/shoot()/project()/SendAndLoopback` 调用（仅注释）；无新 RPC；插入点在锚点后、耐久前（§2） |
| 三命令 | §6 |
| 变异自跑并还原 | §5，逐字节 sha256 校验 |
| 不写 Release/publishi/Develop-Stage；不动 Platform/Diagnostics 与 Bug3 五文件 | `git status` 无新增产物；Bug3 五文件 sha256 不变（Patch `a89f40b9…` 等） |

## 5. 变异表（均自跑，-t:Rebuild，逐字节还原）

| # | 做法 | 变异源码 sha256 | 变异 DLL sha256 | 新增红用例 | 还原后 |
|---|---|---|---|---|---|
| M1 | `GunshotHostAudioRuntime.cs` 的 `leaf(gun);` 注释掉 | `01f61e945d4d18d60bd902fe6d0e85b10b82b13aae6bfcf3804749bb2c6c30b3` | `2d5bfba66504516f40e80f7687d473a73f4ccef51ceb3b287f0b49bde7fca9dc` | Bug2-02（`compensation leaf calls=0 expected=1`），连带 09/10/11/12/15 红，810/818 | 源码 `fa534c0f…`，DLL `45c00a52…`，816/818 |
| M2 | `GunshotHostAudioPolicy.IsEligible` 删 `facts.P2PHostMode &&` | `abf1f5d56e9e81d9641c1d639827eaccf1e5e8c514d61d542f083fbae31d571f` | `e8f1b57a29e96a3890dfa1eb74641d387c7694d96c3cd4ad4f66d897f96889a0` | Bug2-05（`negative control must not compensate`），815/818 | 源码 `527329c4…`，DLL `45c00a52…`，816/818 |

脚本：`D:/tmp/mut_t116.py`、`D:/tmp/mut_t116_gate.py`；日志：`D:/tmp/t116_mut.log`、`D:/tmp/t116_mut_gate.log`。

## 6. 三命令结果（-t:Rebuild）

```
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
  → 0 个警告 / 0 个错误
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
  → 0 个错误 / 4 个既有警告
WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe
  → 816/818 PASS（仅 GBA01、NIL01 既有环境失败）；Bug2-01..17 全 PASS
```

## 7. 未闭环（UNKNOWN，不得视为已验收）

1. 真实听感：Host 是否真能听到、音量/衰减是否与 U3DS 等价——未做实机 1H+1G。
2. 三端互听：1H+2G 下 B 是否仍恰好一声、无重复——未做实机。
3. Mod 兼容：未知 Mod 自定义音效出现本地叶时的「立即停补并记录」路径未在真实 Mod 环境验证。
4. 原生 `playGunshot()` 的 pitch 随机数在真实 Unity 下对后续射击随机序列的影响只做了逻辑等价证明（保存/恢复 `Random.state`），未做帧级实机对比。

## 8. 文件 sha256（本轮终态）

```
7eab9950cf6d1082aec67cf19d40fa9d54601e0c28f1b100bfa4e1452fdbfe71  GunshotHostAudioControl.cs
6f8923421d2f5c2865e1eb276771ada469923c3bf35ab4fd1970d7bd7eb23b23  GunshotHostAudioPatch.cs
527329c4f43cf01931bf547ea5c4e1374b8cc6091649a88f498b6d2de576eea7  GunshotHostAudioPolicy.cs
fa534c0f69d68c66d171cd3d0ec4fb6ccc46bd3c65eff748ee72b19c29e4a960  GunshotHostAudioRuntime.cs
ef512ba0a8fea0540b248fdb1588e3c969b672df2e652edd4f1094c8f8dda1e9  GunshotIlScan.cs
321d3ef3ff5aee0c104e2b379c94830ed3f8031f75bc081f555c99e581504c18  PatchRegistrationDiagnosticsModules.cs
e327865c031521fde970c139dc31c15eb02fcb648ebf9bd3ce75f46f0b4a68e5  SteamP2PFriends.csproj
822e20e3a1a1f6adf5aca0059c896b1f43fb891bf7a114c6c9b9a3d4960514fb  GunshotHostAudioTests.cs
8291f50dd249c4532efa423ecf2e5393d543f292068159e45a50987cf4efc899  WhitelistTests/Program.cs
```
