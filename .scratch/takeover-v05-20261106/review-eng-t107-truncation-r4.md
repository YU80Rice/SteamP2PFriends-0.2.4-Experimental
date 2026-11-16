# t108 工程轴复审：t107（R19-01 承重 + R19-02..04）第 4 轮

## 0 结论

**verdict = pass。**

t106 的 **R19-01 已真关闭**：截断→不可用的判定现在由「返回值 + 快照字段」直接断言，两个行为变异（判定分支恒假、`Available` 反转）各自只红 Bug1-109，且 `SnapshotPose` 侧无「调了却丢弃/反转」的旁路（`if (!ApplyPoseAvailability(...)) return snapshot;` 是唯一调用点，返回值为 false 时提前返回，截断集不会进入 `PosesEqual`）。
R19-03 的扩断言与其名字一致，R19-04 三文件已入暂存（`A `），无新增 untracked 生产/测试文件。

遗留四条 low（不改判定，建议随下一轮或交付说明处理）：①「唯一写入点」的措辞与代码不符（字段还有三处其它不可用原因写入，行为安全）；②节点上限只约束 skeleton 子树，plan 可因骨骼/碰撞体路径超过 512 而不置截断；③Bug1-107 与 Bug1-111 对同一条不变量重复断言（有意冗余，不互相掩盖）；④变异哈希与我复现的编辑文本不完全可对齐（红名单一致）。

## 1 字节绑定（本人实测，判定只对这些字节有效）

| 文件 | 行数 | 字节 | sha256（前 16） |
| --- | --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1GateProbe.cs` | 1670 | 77136 | `44b8115c2f162f72` |
| `WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs` | 1156 | 72555 | `7bd27b859cbda46e` |
| `Platform/Diagnostics/Patches/Bug1GateProbeLogic.cs` | 1416 | 69821 | `7a0c6d24de12934a`（未改） |
| `WhitelistTests/Evidence/StaticIL/IlContractProbe.cs` | 494 | 22708 | `73e11618f0db37ab`（未改） |
| `WhitelistTests/Program.cs` | 1118 | 153379 | `5dcd7b8d93499ae6`（未改） |
| `bin/Release/SteamP2PFriends.dll` | — | 1561088 | `dd75d956df26087e`（与 `WhitelistTests/bin/Release` 同哈希） |

与 t107 自报逐项一致，无偏差。

## 2 复跑（本人实测）

| # | 命令 | 退出码 | 结果 |
| --- | --- | --- | --- |
| 1 | `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 0 | 0 警告 0 错误，1.12s；DLL 复现 `dd75d956` |
| 2 | `dotnet build WhitelistTests/... -c Release -t:Rebuild` | 0 | 0 错误；4 条既有警告（CS0649×3 + CS0219） |
| 3 | `./WhitelistTests/bin/Release/...exe` | 1 | **797/799**；FAIL 仅 `GBA01`/`NIL01`（控制台缺 Unity 运行时）；`SPF_DLL_SHA256=dd75d956` 与磁盘一致 |

副本 `../_t108mut` 独立重建同样得 `dd75d956`、797/799，故变异基线可信。

## 3 自跑变异（副本 `../_t108mut`，`.cs.t108bak` 字节备份还原；每条还原后源回 `44b8115c`、DLL 回 `dd75d956`；副本已删）

| 变异 | 改动点 | 变异 DLL | 结果 | 新红用例 |
| --- | --- | --- | --- | --- |
| **MV-1** 截断分支恒假 | `Bug1GateProbe.cs:700` `\|\| !state.Truncated` → `\|\| true` | `6fbed7f9b180699c` | 796/799 | **仅 Bug1-109**（`Truncated=true 时 PoseSnapshotAvailable 必须返回 false`） |
| **MV-2** 截断时 Available 写 true | `:726` `snapshot.Available = false;` → `= true;` | `1d47b7db802c7888` | 796/799 | **仅 Bug1-109**（`截断快照的 Available 必须为 false（不得被反转）`） |
| **MV-B2** 深度门恒真 | `:675` `PoseDepthAllowed` 函数体 `return depth >= 0 && depth <= MaxPoseDepth;` → `return true;` | `b28cf475c3110eef` | 796/799 | **仅 Bug1-104** |
| **MV-Dparam** 覆盖路径改调单帧 note | `:798` `result.Notes.Add(BuildCoverageNote(_cellSamples.Count, validSamples));` → `Add(BuildSingleNote())` | `e9f577501fb63094` | 795/799 | **Bug1-107 + Bug1-111** |

MV-1/MV-2 的 DLL 哈希与 Captain 自跑结果逐位相同（`6fbed7f9` / `1d47b7db`），红名单也相同——这不是采信，是我独立复现后的吻合。

**MV-B2 的坑（记下来供后续轮次）**：先试「`if (!PoseDepthAllowed(depth))` → `if (false)`」，编译直接 **CS0162 无法访问的代码**（`SteamP2PFriends.csproj` 警告即错误），DLL 未产出；改为在 `PoseDepthAllowed` 函数体返回 `true` 才成立。t107 报的 `38cccb49` 与我这次 `b28cf475` 不同，是因为编辑点不同（他们可能改的是调用点），红名单一致（只红 Bug1-104），判定不受影响。

## 4 五个必答问题

### 4.1 证据链里还有没有「调用在、结果被丢或反转」

截断这条已经没有了。证据链现在是三层且互相独立：

1. `PoseSnapshotAvailable`（`:697-703`）与 `ApplyPoseAvailability`（`:715-729`）的**返回值**被直接断言（Bug1-109 `:1037-1048`）；
2. `snapshot.Available` / `UnavailableReason` **字段值**被直接断言（同上）；
3. 生产路径「调用存在」只作二层契约（`CountMethodCalls(SnapshotPose, ApplyPoseAvailability) >= 1`，`:1028-1030`）；且 `SnapshotPose:1307` 是**唯一调用点**并消费返回值（`if (!ApplyPoseAvailability(...)) return snapshot;`），grep 全仓无第二处调用。

仍存在**另一处同类形态（但不是 F-d 的重复）**：见 R20-02——`MaxPoseNodes` 只约束 `CollectSubtree` 收集的 skeleton 路径，`renderer.bones[]` 与碰撞体父链会继续追加进 `BuildPoseNodePlan`，plan 大小可以超过 512 而 `Truncated` 仍为 false、快照仍 Available。这是「上限只盖住了遍历，不盖住产物」，与 F-d 同族但影响面小（骨骼数量由模型决定，非外部可控）。

### 4.2 `ApplyPoseAvailability` 是否唯一写入点；else 分支 reason 与返回值是否可能不一致

**不是字面意义上的唯一写入点**。`Bug1PoseSnapshot.Available / UnavailableReason` 在本文件另有 3 处写入：

- `:1351-1352` `missing:<path>`（plan 里的节点在 map 中找不到）
- `:1358-1359` `unreadable:<path>`（`ReadPoseEntry` 返回 null）
- `:1366-1367` `empty-node-set`

但这三处都在 `:1307` 的截断提前返回**之后**才可能执行，因此不可能覆盖截断结论；它们自身也都是「置 false + 同时给 reason」，与返回值语义一致（这三个方法没有返回值，不存在返回值/字段不一致问题）。`ApplyPoseAvailability` 的两个分支同样成对写：true → `Available=true` + `Reason=null`；false → `Available=false` + `Reason=reason`（`reason` 由 `PoseSnapshotAvailable` 在截断时赋值，非截断时保持 null）。**结论：不存在「返回值与字段不一致」的可达路径；但「唯一写入点」这一表述只对「截断判定」成立，对字段不成立**（R20-01）。

### 4.3 Bug1-109 是否覆盖 Truncated=false 的正向分支

覆盖了。负向（Truncated=true）：`!PoseSnapshotAvailable`、reason 文本、`ShouldMarkPoseUnavailable==true`、`ApplyPoseAvailability==false`、`!snapshot.Available`、reason 落盘。
正向（Truncated=false，`:1050-1060`）：`PoseSnapshotAvailable==true`、`reason` 为空、`ShouldMarkPoseUnavailable==false`、`ApplyPoseAvailability==true`、`snapshot.Available==true`、`UnavailableReason` 为空。两侧对称，不存在「只测负向、正向恒真也能过」的空转。

### 4.4 Bug1-111 与 Bug1-107 的职责是否重叠到互相掩盖

有重叠，但不互相掩盖。两条都断言 `FinishCoverage → BuildCoverageNote >= 1` 且 `BuildSingleNote == 0`（`Tests:973-977`、`:1105-1113`），所以 MV-D 会同时红两条。区别在于：Bug1-107 是「模式 note 已拆分」的最小证据（含 `BuildCoverageNote` 文本含采样数、`BuildSingleNote` 文本含 `single_frame_single_pose`）；Bug1-111 额外钉 `RunGate → BuildSingleNote >= 1`、`RunGate → BuildCoverageNote == 0`，以及「单帧 note 不得含六格口径」。删掉 107 仍有 111 守同一条不变量，删掉 111 仍有 107 守覆盖侧——这是**冗余**（有意为之，t107 也这样说明），不是一方绿了就把另一方掩盖。名字「Mode Notes Cannot Mix」现在与断言一致。

### 4.5 报告哈希与实测是否一致

一致（§1）。唯一差异是 MV-B2 的**变异 DLL** 哈希（`38cccb49` vs 我 `b28cf475`），原因是编辑点不同（我改函数体，他们改调用点），红名单相同，不影响判定；建议今后变异表同时记录「文件:行 + 改前/改后文本」，否则哈希无法逐字节复现。

## 5 findings（全部 low，不阻塞）

- **R20-01（low）** `Platform/Diagnostics/Patches/Bug1GateProbe.cs:1305-1307` 与 `:715-729`：注释/交付说明称 `ApplyPoseAvailability` 是 `Available/UnavailableReason` 的「唯一写入点」，实际字段另有 `:1351-1352`、`:1358-1359`、`:1366-1367` 三处写入（missing/unreadable/empty-node-set）。行为安全（截断提前返回），但表述会误导后续维护者以为改这三处不影响可用性判定。**requiredFix**：把措辞改成「截断判定的唯一写入点」，或在三处写入加注释说明它们只在未截断时可达。
- **R20-02（low）** `Platform/Diagnostics/Patches/Bug1GateProbe.cs:1308-1345` + `Bug1GateProbeLogic.cs` `BuildPoseNodePlan`：`MaxPoseNodes` 只在 `CollectSubtree` 内按 skeleton 路径数判定；`renderer.bones[]`（`AddPath` 循环 `:1326-1329`）与碰撞体父链（`AddColliderChain`，仅自身 64 深度守卫）不受该上限约束，最终 plan 可能 >512 节点而 `Truncated=false`。**requiredFix**：在 `BuildPoseNodePlan` 之后按 `plan.Count` 再判一次，超限时同样 `Truncated=true`（Collected=plan.Count），并把 `pose_truncated` 的原因区分成 `walk`/`plan` 两种；或在实机说明写明「512 只保证遍历有界，产物上限由模型骨骼数决定」。
- **R20-03（low）** `WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs:973-977` 与 `:1105-1113`：Bug1-107 与 Bug1-111 对同一条不变量重复断言，MV-D 双红。**requiredFix**：无需改代码；建议在 111 的注释里写明「与 107 冗余是故意的：107 守覆盖侧拆分，111 守两侧互斥」，避免后人误删其中一条时以为少了一层保护。
- **R20-04（low）** 变异证据可复现性：MV-B2 的 DLL 哈希与 t107 自报不同（编辑点差异）。**requiredFix**：变异表固定记录「文件:行 + 原文 + 替换文 + 编译期注意事项（如 `if (false)` 触发 CS0162）」。

## 6 pass 边界

- 本票只判**离线证据链是否承重**。797/799 ≠ G 门成立 ≠ Bug1 已修复。
- 仍需 1H+2G：BakeMesh 真实位移量级、六格能否被正常游玩覆盖、`root`/`Third`/骨骼在真实物理帧是否静止、`2 × SampleIntervalSeconds` 的稳定性窗口是否合适。
- 未验证：真实模型下 `renderer.bones.Length` 与碰撞体数量的量级（R20-02 的实际触发概率）。

## 7 复现命令

```bash
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild          # 0 警 0 错，DLL dd75d956
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe    # 797/799，仅 GBA01/NIL01
# MV-1：Bug1GateProbe.cs:700 `|| !state.Truncated` → `|| true`；重建后 cp 到 WhitelistTests/bin/Release → 796/799，仅 Bug1-109
# MV-2：:726 `snapshot.Available = false;` → `= true;` → 796/799，仅 Bug1-109
# MV-B2：:675 `return depth >= 0 && depth <= MaxPoseDepth;` → `return true;` → 796/799，仅 Bug1-104
# MV-D：:798 `Add(BuildCoverageNote(...))` → `Add(BuildSingleNote())` → 795/799，Bug1-107 + Bug1-111
```

## 8 R19-02 / R19-04 复核

- **R19-04 收口**：`Bug1GateProbe.cs`、`Bug1GateProbeLogic.cs`、`Bug1GateProbeTests.cs` 均为 `A `（已暂存、未提交），本轮无新增 untracked 生产/测试文件。仓库里仍有 11 个**早于本票**的 untracked 文件（`Bug1ProbeControl.cs`、`Bug1ProbeEntryLogic.cs`、`Bug1ProbeNativeSpeed.cs`、`Bug1ProbeTargetSelector.cs`、`Bug1ProbeWindowBoundary.cs`、`Bug1ProbeEntryRepairTests.cs`、`Bug1ProbeEntryTests.cs`、`Bug1ProbeM1RuleTests.cs`、`Bug1ProbeT60Tests.cs`、`Bug1ProbeT65Tests.cs`、`SteamPersonaDisplayPlayerIdGuardTests.cs`），属历史欠账，建议单独开一条清理票。
- **R19-02 登记**：`Release/0.2.4.11-preview5/测试说明.md` 仍无「连续 2 次 = 2 × SampleIntervalSeconds」，包内 DLL 仍是 `7d625908`；出包时必须带上该口径并更新哈希。

<!-- self_sha256(body)=5fda8b59d68ef177eb2aaa6871c40149512b2632cfac3b57b483a409b06efd38 -->

