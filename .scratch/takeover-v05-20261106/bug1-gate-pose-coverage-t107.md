# t107 — Bug1 G 门 r4：截断→不可用的判定必须承重（R19-01 high + 三条 low）

修复轮 r4，收口 t106 复审 findings。基线 = t104 交付字节（797/799，Bug1 段 113/113，DLL `7aa5d444…`）。
本轮只做定向修补，未重写、未回退 t104 的判定与采样结构。

**口径声明（先写在最前）**：本报告的一切结论都是**离线证据链自洽**，即「删掉/反转被守护的判定后，自建用例会红」。
它**不**等于 G 门成立，**不**等于「Model_0/1 纯叶位移方案可行」，**不**等于 Bug1 已修复。真实蒙皮位移、六格在实机能否被
「正常玩一遍」覆盖、root/Third/骨骼在真实物理帧是否不动，全部仍需 1H+2G 实机。

---

## 1. 基线与本轮终态

| 文件 | 基线（t104） | 本轮终态 | 行数 |
| --- | --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1GateProbe.cs` | `6bf3eed7f9fefed116befcd0bbe13398b13a5ca685c3d39f4d8a1abc89f79ce5` (1636) | `44b8115c2f162f720798b9f42c083c5d17c3b0aeb53304fe941848045e70f12e` | 1670 |
| `Platform/Diagnostics/Patches/Bug1GateProbeLogic.cs` | `7a0c6d24de12934a5af92bc9e1bdffc89cf7595a8c7bba0eb16d9f2f67937c8d` | 同左（**本轮未改**） | 1416 |
| `WhitelistTests/Evidence/StaticIL/IlContractProbe.cs` | `73e11618f0db37ab7d21846457e548bfc12c0480067f6268de5d2032969a8e29` | 同左（**本轮未改**） | 494 |
| `WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs` | `649510febc1d8ab697362ac11fa6d62a46e1ba5b7cdf9555000447a0eb371b7b` (1108) | `7bd27b859cbda46ee606d054ba0e0237f154d45e046c8d28595709bcf5e143a6` | 1156 |
| `WhitelistTests/Program.cs` | `5dcd7b8d93499ae6ebdd160617fe41adb30a48f168ae8bb0b77b04324e785201` | 同左（**本轮未改**，无新增注册行） | 1118 |
| `bin/Release/SteamP2PFriends.dll` | `7aa5d4449188a13ec088cb5d56a819d0744b639b87b8395e0c824511c7b2d79e` | `dd75d956df26087e1594b98ac611074c06f7b53b33b02e9257588a9b11e5ec67` | — |

用例数不变（未增删 `Program.cs` 注册行），所以分母仍是 799。

---

## 2. R19-01（high）：截断→不可用的证据链现在承重

### 2.1 问题复核

t104 的生产改动是对的：`CollectSubtree` 输出 truncated 标志，`SnapshotPose` 把截断写成 `Available=false` +
`pose_truncated:<n>/<limit>`。但用例只断言了「纯函数边界 + 原因文本格式 + `SnapshotPose` 调过
`BuildPoseTruncatedReason` ≥1 次」——调用在、返回值被丢弃或反转，它都绿。t106 审查官据此自跑两个行为变异
（截断分支恒假 `054dcd83`、`Available=false→true` `0a49ad59`）均 797/799 全绿，属 F-d 类假绿的换位。

### 2.2 修法：判定点与写入点合一

`Platform/Diagnostics/Patches/Bug1GateProbe.cs:697-733` 新增三个纯静态成员（契约点名的 `ShouldMarkPoseUnavailable`
在列，语义互补）：

- `:697 PoseSnapshotAvailable(Bug1PoseWalkState state, out string reason)` —— 返回**可用性**：`null`/未截断 → true 且
  `reason=null`；截断 → false 且 `reason = BuildPoseTruncatedReason(state.Collected, MaxPoseNodes)`。
- `:706 ShouldMarkPoseUnavailable(Bug1PoseWalkState state, out string reason)` —— 契约命名，`=> !PoseSnapshotAvailable(...)`。
- `:715 ApplyPoseAvailability(Bug1PoseSnapshot snapshot, Bug1PoseWalkState state)` —— **唯一写入点**：可用则
  `Available=true`、`UnavailableReason=null`；不可用则 `Available=false`、`UnavailableReason=reason`；返回值与写入后的
  `snapshot.Available` 严格一致。

`SnapshotPose`（`:1307`）原来自己拼条件、自己赋值，现在只剩一行：

```csharp
// R19-01：截断集不能参与 PosesEqual——可用性只由 ApplyPoseAvailability 判定并写入；
// 这里不重复拼条件，调用点与判定点合一，避免「调了却丢弃/反转」的假绿。
if (!ApplyPoseAvailability(snapshot, skeletonWalk)) return snapshot;
```

即：条件拼接、`Available` 赋值、`UnavailableReason` 赋值三件事只有一处实现；`SnapshotPose` 不能再「调了却丢弃」，
也不能在别处另写一份判定。

### 2.3 用例直接断言返回值与字段

`WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs:1013 Test_T104_PoseTruncationIsFailClosed`（Bug1-109）：

- 截断侧：`Truncated=true, Collected=512` → `PoseSnapshotAvailable` 必须 **false**、`reason == "pose_truncated:512/512"`、
  `ShouldMarkPoseUnavailable` 必须 **true**；`new Bug1PoseSnapshot()` 经 `ApplyPoseAvailability` 后必须返回 **false**、
  `snapshot.Available` 必须 **false**、`UnavailableReason` 必须命中同一文本。
- 未截断侧：`Truncated=false` → `PoseSnapshotAvailable` 必须 **true**、`reason` 为空、`ShouldMarkPoseUnavailable` **false**；
  快照经 `ApplyPoseAvailability` 后必须返回 **true**、`Available=true`、`UnavailableReason` 为空。
- 原 IL 契约降为二层：`SnapshotPose → ApplyPoseAvailability ≥ 1`（不再作为主要证据）。

这样「截断分支恒假」会让第一组断言直接红；「`Available=false` 改成 true」会让 `snapshot.Available` 断言直接红。

---

## 3. R19-03（low）：用例名与断言范围对齐

选择**扩断言**而不是改名。理由：`Cannot Mix` 表达的是「两种模式互斥」这个不变量；改名会把这个不变量从用例里
丢掉，而它恰好是 t104 里唯一一条防止「覆盖模式被误写成单帧口径」的守卫。

`Bug1GateProbeTests.cs:1084 Test_T104_ModeNotesCannotMix`（Bug1-111）现在两侧都钉：

| 断言 | 期望 |
| --- | --- |
| `BuildCoverageNote(3,3)` 含「六格采样」 | 是 |
| `BuildCoverageNote(3,3)` 含 `single_frame_single_pose` | 否 |
| `BuildSingleNote()` 含 `single_frame_single_pose` | 是 |
| `BuildSingleNote()` 含「六格采样」 | **否**（新增） |
| `RunGate → BuildSingleNote` | ≥ 1 |
| `RunGate → BuildCoverageNote` | **== 0**（新增） |
| `FinishCoverage → BuildCoverageNote` | **≥ 1**（新增） |
| `FinishCoverage → BuildSingleNote` | **== 0**（新增） |

---

## 4. R19-02（low，只登记不修）

`Release/0.2.4.11-preview5/测试说明.md` 缺「连续 2 次 = 2 × `SampleIntervalSeconds`」的口径说明；该包内 DLL 为
`7d625908…`，早于 t104/t107 字节。属打包轮事项，本轮按约束**未改 `Release/`**，仅在此登记，供下一打包轮补齐。

---

## 5. R19-04（low，可交付性）：三个 untracked 文件已暂存

`git status --porcelain` 实测（改前）：

```
?? Platform/Diagnostics/Patches/Bug1GateProbe.cs
?? Platform/Diagnostics/Patches/Bug1GateProbeLogic.cs
?? WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs
 M WhitelistTests/Evidence/StaticIL/IlContractProbe.cs
MM WhitelistTests/Program.cs
```

`csproj` 已引用前三个文件，干净检出会编译失败。已执行（**只暂存、未提交**，未改历史）：

```
git add -- Platform/Diagnostics/Patches/Bug1GateProbe.cs \
           Platform/Diagnostics/Patches/Bug1GateProbeLogic.cs \
           WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs
```

暂存后状态：

```
A  Platform/Diagnostics/Patches/Bug1GateProbe.cs
A  Platform/Diagnostics/Patches/Bug1GateProbeLogic.cs
A  WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs
 M WhitelistTests/Evidence/StaticIL/IlContractProbe.cs
MM WhitelistTests/Program.cs
```

（`IlContractProbe.cs` / `Program.cs` 是已跟踪文件的既有改动，本轮未新增改动，故未一并 `add`。）

---

## 6. 变异证据（仓库外副本 `../_t107mut`，已逐字节还原并删除）

副本由 `tar` 复制主树（排除 `bin/obj/.git/.scratch/Release/publishi/Develop-Stage/publish`，带 `Build/Version.props`）。
每条流程：唯一替换 → 先 build 生产 → 再 build 测试 → 在 exe 所在目录运行并抓 stdout → 打印变异后 DLL sha256 →
从主树 `cp` 还原 → 打印还原后源码 sha256（四次均为 `44b8115c…`，与主树一致）。

| 变异 | 文件:行 | 原文 → 替换文 | 变异后 DLL sha256 | 红名单 | 总数 |
| --- | --- | --- | --- | --- | --- |
| MV-1 截断分支恒假 | `Bug1GateProbe.cs:700` | `if (ReferenceEquals(state, null) \|\| !state.Truncated) return true;` → `... \|\| state.Collected >= 0) return true;` | `6bd71cd750bc8ac7c85ab799d8593a8239a424f72231d50d106fee6e6d04f835` | **Bug1-109** | 796/799 |
| MV-2 Available 反转 | `Bug1GateProbe.cs:726-727` | `snapshot.Available = false;` + `snapshot.UnavailableReason = reason;` → `snapshot.Available = true;` + 同后行（两行锚点，避免误伤其它 6 处同名赋值） | `1d47b7db802c7888d4aa15f6336b7344a22d715cc2ddd98a9e8f81834abfd31e` | **Bug1-109** | 796/799 |
| MV-B2 深度门恒真（上轮） | `Bug1GateProbe.cs:1395` | `if (!PoseDepthAllowed(depth))` → `if (!(depth < Bug1GateProbeLogic.MaxPoseDepth + 1))` | `38cccb492469f52d51b5471f2ccc10b2420ddb40557191fc9cae279e9bac585f` | **Bug1-104** | 796/799 |
| MV-Dparam 覆盖路径改调单帧 note（上轮） | `Bug1GateProbe.cs:798` | `result.Notes.Add(BuildCoverageNote(_cellSamples.Count, validSamples));` → `result.Notes.Add(BuildSingleNote());` | `e9f577501fb630940e8950a1d997cf4b18f9af041160c95009a994c56f4a81fa` | **Bug1-107 + Bug1-111** | 795/799 |

每条变异都只让自建用例红，另加两条既有环境失败 `GBA01` / `NIL01`（与主树相同），无其它用例连带。

关于 MV-Dparam 同时红两条：这是 R19-03 扩断言的直接后果——Bug1-111 现在也钉
`FinishCoverage → BuildCoverageNote ≥ 1`，而该变异恰好把这条边换成 `BuildSingleNote`，所以两条用例同时红。
两者都是「覆盖 note 必须由覆盖路径写」这条不变量的守卫，属预期耦合，不是隐藏的跨用例依赖。
契约第 4 条只要求「仍各自变红」，已满足；MV-1 / MV-2 仍各自**只**红 Bug1-109。

---

## 7. 三命令（主树，最终字节）

| 命令 | 结果 |
| --- | --- |
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 已成功生成，**0 警告 0 错误** |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | 已成功生成，**0 错误**（4 条既有警告，均在本轮未触碰的文件） |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `=== Final Result: 797/799 PASS (Failed: 2) ===`，`exit=1`（既有约定：有失败即 1） |

失败项仅：

```
FAIL [PureMemory] GBA01 Native First Observe Binding
FAIL [PureMemory] NIL01 Real Native Lookup Contract: ... TypeInitializationException: SDG.Unturned.SteamPlayer
```

两条均为 t48 起就存在的环境失败（离线进程缺 Unity 运行时），与 t107 无关。Bug1 段 **113/113 PASS**（`grep -c` 实测）。

---

## 8. 边界与未做项

- 本轮**未**触碰 `Release/`、`publishi/`、`Develop-Stage/`、`publish/`：`find Release publishi Develop-Stage publish -newermt "2026-11-08 05:20" -type f` 无输出。
- Bug3 五文件哈希未变：`P2PGroupStateRecoveryPatch.cs a89f40b949f841d0`、`GroupStateRecoveryCore.cs 7a82de8e38fa0f6a`、
  `GroupLeaseLedger.cs 3d5044f7870fbb17`、`GroupLoadPlanner.cs a45533850f31bd96`、`P2PGroupRegistration.cs 5b0c4e9ba4c87cbe`。
- 未改 `Bug1GateProbeLogic.cs`、`IlContractProbe.cs`、`Program.cs`（R19-03 只在测试文件内扩断言，无新增注册行）。
- 未做、仍需实机/Architect：
  1. `BakeMesh` 真实蒙皮顶点在 0.10U 位移下的 `max‖Δp−delta‖` 与 epsilon（epsilon 仍未指定 → 生产只会给 `UNKNOWN/EPSILON_UNSPECIFIED`）；
  2. 六格（idle/walk/sprint × 两朝向）能否在「正常玩一遍」中被覆盖到 ≥3 有效样本；
  3. root/Third/骨骼/碰撞/射线在真实物理帧与动画更新下是否真的不动（离线只证明「同一帧内写+还原」的判定链）；
  4. 子骨骼 pose 位级覆盖在真实 `Animator` 更新下的稳定性。
- 因此：**本轮交付的是「判定承重」的自动化证据，不是 G 门结论，更不是 Bug1 根因结论。**
