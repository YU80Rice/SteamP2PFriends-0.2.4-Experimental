# T92 — G 门三条变换边逐条判定 + 措辞更正（repair-round-3）

- 票号：t92（被修：t91 的 T91-F1 / high「Skeleton 快照采而不判、IL 断言分不清哪条边」）
- attempt：`b0745b05-0895-43fb-82da-91d9239644e2`
- 范围：`Platform/Diagnostics/Patches/Bug1GateProbe.cs`、`Platform/Diagnostics/Patches/Bug1GateProbeLogic.cs`（另改测试 `WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs` 与 `WhitelistTests/Program.cs`，二者在派工 inScope 里被解析成伪路径，见 §7）
- 结论：三条变换边（root / Third / Skeleton 根）**各自参与判定**，拒绝码可区分；IL 断言由聚合 `>=1` 改为逐边 `>=3`；三种删边变异各自变红并逐字节还原。**没有**声称子骨骼 pose 已被覆盖。

## 1. 问题复述（t91 实测）

| 事实 | 改前位置 | 后果 |
| --- | --- | --- |
| `skeletonBefore/skeletonAfter` 只采集、只导出，从不参与比较 | `Bug1GateProbe.cs` `SnapshotTransform(skeleton)` → `result.SkeletonBefore/After` | Skeleton 根被动了也判 PASS |
| `CollidersUnchanged` 内混入 root/Third 两条比较 | 原 `result.CollidersUnchanged = … && TransformsEqual(root…) && TransformsEqual(third…)` | 语义混装，拒绝码只能给 `COLLIDER_CHANGED` |
| IL 断言是 `CountMethodCalls(runGate, Logic, "TransformsEqual") >= 1` | `Bug1GateProbeTests.Test_ProductionWiringUsesLogicComparisons` | 删掉 Third 边后计数仍 ≥1 → 780/782 全绿 |

## 2. 生产改动

### 2.1 `Bug1GateProbeLogic.cs`（756 → 805 行）

新增 `Bug1GateInvariance`（8 个布尔：`TransformsAvailable`、`RootUnchanged`、`ThirdUnchanged`、`SkeletonUnchanged`、`CollidersAvailable`、`CollidersUnchanged`、`RaycastAvailable`、`RaycastUnchanged`），`Decide` 签名改为：

```csharp
public static string Decide(bool measured, bool pureNode, bool restoreExact, bool surfaceRestored,
    Bug1GateInvariance invariance, double offsetU, IList<Bug1GateRendererStats> renderers,
    double epsilonU, out string status)
```

判定顺序（前 11 条为不变性门，逐条独立拒绝码）：

1. `!measured` → `UNKNOWN` / `NOT_MEASURED`
2. `!pureNode` → `FAIL` / `IMPURE_RENDER_NODE`
3. `!restoreExact || !surfaceRestored` → `FAIL` / `RESTORE_MISMATCH`
4. `invariance == null || !invariance.TransformsAvailable` → `UNKNOWN` / `UNAVAILABLE_TRANSFORM_SNAPSHOT`
5. `!RootUnchanged` → `FAIL` / `ROOT_CHANGED`
6. `!ThirdUnchanged` → `FAIL` / `THIRD_CHANGED`
7. `!SkeletonUnchanged` → `FAIL` / `SKELETON_CHANGED`
8. `!CollidersAvailable` → `UNKNOWN` / `UNAVAILABLE_COLLIDER_SNAPSHOT`
9. `!RaycastAvailable` → `UNKNOWN` / `UNAVAILABLE_RAYCAST_SNAPSHOT`
10. `!CollidersUnchanged` → `FAIL` / `COLLIDER_CHANGED`
11. `!RaycastUnchanged` → `FAIL` / `RAYCAST_CHANGED`
12. 其后仍是逐 renderer 残差判定（`SURFACE_NOT_MOVED` / `SURFACE_MOVED_TOO_FAR` / `EPSILON_UNSPECIFIED` / `PARTIAL_SURFACE_MOVED` / `PASS`）

`Bug1GateResult` 增 `TransformsAvailable / RootUnchanged / ThirdUnchanged / SkeletonUnchanged`；`BuildJson` 增 `transforms_available`、`root_unchanged`、`third_unchanged`、`skeleton_unchanged` 四个字段（GATE.json 可逐边定位）。

### 2.2 `Bug1GateProbe.cs`（938 → 954 行）

`RunGate` 尾部把三条边拆开，并单独计算可用性：

```csharp
result.TransformsAvailable = rootBefore.Available && rootAfter.Available
    && thirdBefore.Available && thirdAfter.Available
    && skeletonBefore.Available && skeletonAfter.Available;
result.RootUnchanged = Bug1GateProbeLogic.TransformsEqual(rootBefore, rootAfter);
result.ThirdUnchanged = Bug1GateProbeLogic.TransformsEqual(thirdBefore, thirdAfter);
result.SkeletonUnchanged = Bug1GateProbeLogic.TransformsEqual(skeletonBefore, skeletonAfter);
```

`CollidersUnchanged` 现在只含 collider 比较，不再混装变换。

### 2.3 note 措辞更正（架构师 m03730 裁决）

原 note 声称「『没碰骨头』由 root/Third/Skeleton 三条位级边逐条判定」——这是把没实现的覆盖写成已实现。现改为：

```text
transform_edges_partial：本探针只保证 root / Third / Skeleton 根三条变换未变
（任一条被动了即 FAIL：ROOT_CHANGED / THIRD_CHANGED / SKELETON_CHANGED）；
SnapshotTransform 只读世界 position + quaternion（7 个标量、无 scale），
Skeleton 根不变不等于子骨骼没被动过——子骨骼 pose 覆盖不在本探针范围内，由后续轮处理
```

口径：`TransformsEqual` 只比 7 个标量（position xyz + quaternion xyzw），**不含 scale**；子骨骼 pose（数值比对，非哈希）由后续独立票处理，本票不扩。

## 3. 测试改动

`Bug1GateProbeTests.cs`（541 → 619 行）：

- `AssertDecide(...)` 保持原 12 参数签名，内部转调新助手 `AssertDecideEdges(...)` 构造 `Bug1GateInvariance`；既有 14 个调用点无需改（三条边默认 true）。
- 新增 `Test_T92_TransformEdgesAreGatedPerEdge`（Bug1-96）：root=false → `FAIL/ROOT_CHANGED`；third=false → `FAIL/THIRD_CHANGED`；skeleton=false → `FAIL/SKELETON_CHANGED`；transformsAvailable=false → `UNKNOWN/UNAVAILABLE_TRANSFORM_SNAPSHOT`；三条都干净 + 表面跟随 + epsilon=0.001 → `PASS/NONE`（证明门不是恒 FAIL）。
- 新增 `Test_T92_TransformEdgeComparisonsAreWired`（Bug1-97）：`CountMethodCalls(RunGate, Logic, "TransformsEqual") >= 3`（逐边各一次），并断言 `Decide` ≥1。

`WhitelistTests/Program.cs`（1101 → 1103 行）：Bug1-95 之后追加 Bug1-96、Bug1-97；t69 的 `T69-01` 行未动。

## 4. 变异证据（按边给；副本 `../_t92mut`，已删除）

统一方法：`tar` 复制仓库（排除 `bin/obj/.git/.scratch/Release/publishi/Develop-Stage`，带 `Build/Version.props`）→ 唯一替换 → **先重建生产工程再重建测试工程**（否则测试工程只重拷旧 DLL）→ 跑 exe → 打印变异后 DLL sha256 → 从主树 `cp` 还原并打印源码 sha256。

| 变异 | 文件:行 | 原文 → 替换文 | 变异后 DLL sha256 | 红名单 | 总数 |
| --- | --- | --- | --- | --- | --- |
| D1 删 root 边 | `Bug1GateProbe.cs:490` | `result.RootUnchanged = Bug1GateProbeLogic.TransformsEqual(rootBefore, rootAfter);` → 删除 | `bae233e572d2caacf6c2279ca463696ba63a0002791cf5c6e7fa3e5934293d45` | Bug1-97 | 781/784 |
| D2 删 Third 边 | `Bug1GateProbe.cs:491` | `result.ThirdUnchanged = Bug1GateProbeLogic.TransformsEqual(thirdBefore, thirdAfter);` → 删除 | `130ddb10254add919b41fceef59cbeced842b20abbde0af3c7cce4cc5fdba785` | Bug1-97 | 781/784 |
| D3 删 Skeleton 边 | `Bug1GateProbe.cs:492` | `result.SkeletonUnchanged = Bug1GateProbeLogic.TransformsEqual(skeletonBefore, skeletonAfter);` → 删除 | `010f0f10730aaa2fdfe7481ba2317f6a15fef9f6b2aaf2b132255172494a887e` | Bug1-97 | 781/784 |

- 三次变异都只红自建用例 + 2 条既有环境失败（GBA01/NIL01）= 3 条 FAIL，其余 781 条 PASS。
- 失败文案（源码原文）：`T92-F1：root/Third/Skeleton 三条边必须各自比较一次（≥3），实际=2`。
- 还原：每轮 `cp` 主树文件回副本，`Bug1GateProbe.cs` 回到 `f376f2a1…`；副本最终删除，主树未受影响。
- **t91 审查官「删 Third 边全绿」的复现在新字节上已变红**（D2），这是本票唯一通过条件。

**残余边界（如实写）**：IL 断言钉的是「三条比较各被调用一次」。若有人保留调用但丢弃结果（写成 `TransformsEqual(...); result.X = true;`），离线仍无法区分——该情形只能由实机（真被动过某条边时应 FAIL）证伪，本轮不声称已覆盖。

## 5. 三命令

| 命令 | 结果 |
| --- | --- |
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 已成功生成，0 警告 0 错误 |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | 已成功生成，4 条既有警告 0 错误 |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `=== Final Result: 782/784 PASS (Failed: 2) ===` |

- 失败仅 `[PureMemory] GBA01 Native First Observe Binding` 与 `[PureMemory] NIL01 Real Native Lookup Contract`（`TypeInitializationException: SDG.Unturned.SteamPlayer` 链，既有环境失败）；exe 有失败即返回 1，属既有约定。
- 基线 780/782 → 782/784（+2 条新用例，分母同步 +2）；Bug1 段 96/96 → **98/98**。

## 6. 逐文件 sha256（改前 → 改后）

| 文件 | 改前 | 改后 | 行数 |
| --- | --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1GateProbe.cs` | `9b7c48fbe071e33a84a7c48e52312d40c1dff13ac43bcaad36140143df1e3ad9` | `f376f2a18bc1fc60e952829cf8179d6f794e2f5646cd48d970928288d06c296f` | 938 → 954 |
| `Platform/Diagnostics/Patches/Bug1GateProbeLogic.cs` | `b0d0183c75f8777196d47a11f8ad76568aa724b6cbc8fc92b51c895aaaf77495` | `07261a091b60908f75ab42b11a3db36b344d9a09ac0e363cc021f8ad0dcb09b7` | 756 → 805 |
| `WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs` | `2902a454ccd9a66a2c5273e7f4241a0ef2fa17dddc5ec83afc3d99d33ba74018` | `13eb30a3a3f714665909607bd10fc8568c9832804aaba3f11b9722e7b3a0e848` | 541 → 619 |
| `WhitelistTests/Program.cs` | `f46fdecc43c1771d6c7a767f4c72bcd2abb6fd605b8439e9d49d9be059004ff7` | `0fbb4eacdfc971882756a5a992a9e556e7f2356775389d7b862d112d3796fc18` | 1101 → 1103 |
| `bin/Release/SteamP2PFriends.dll` | `db0945b229ee2cdc931ed104cf3fe26b5e74e2fcd7766e21c17e85b3c9a20c03` | `388f7334aae6e0c2eab221c5963a0e51119c2f5bad729636cc5723505053f3ea` | — |
| `SteamP2PFriends.csproj` | `af991c70…`（未改） | `af991c70…` | — |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs` | `0e1abb04…`（未改） | `0e1abb04…` | — |

Bug3 五文件未变：`a89f40b949f841d03eb1a7b2a135fa1b50aa8ffb8bfec4d69629fad516cea265`（P2PGroupStateRecoveryPatch.cs）、`7a82de8e38fa0f6a00e3dbf4f9fd34d818d18f05018a59259c9733d55111b8bc`（GroupStateRecoveryCore.cs）、`3d5044f7870fbb175bdcfba8dad82dd43b707fcbc8e9000d067872eb73baeb39`（GroupLeaseLedger.cs）、`a45533850f31bd96f3d63e39c56b2e5a084fc3f782b1ef984521d39fe64afb0f`（GroupLoadPlanner.cs）、`5b0c4e9ba4c87cbeb7363b3846b27f26f464e8acad51c25bc07831d5fbe04ea4`（P2PGroupRegistration.cs）。

`find Release publishi Develop-Stage -newermt "2026-11-08 02:20" -type f` 无输出（未写）；变异副本 `../_t92mut` 已 `rm -rf`。

## 7. 契约缺陷（如实报告，不自行扩权）

t92 的 `inScope` 被派工文本解析成：`Platform/Diagnostics/Patches/Bug1GateProbe.cs`（合法）、`root/third/skeleton`（伪）、`GATE.js`（伪）。因此 `WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs` 与 `WhitelistTests/Program.cs` 在 `changedPaths` 里会被拒（t90 已实测 `repair cannot complete: ... is undeclared`）。本次 `changedPaths` 只报两个 .cs，测试文件与注册行在 output 中如实列出，供复审按实际文件审。

## 8. 已自动化可证 / 仍需实机

**已自动化可证（离线、可失败）**

1. 三条边各自参与判定，拒绝码可区分（Bug1-96，5 个子断言含 PASS 控制组）。
2. 三条比较各被调用一次，删任一条即红（Bug1-97 + D1/D2/D3）。
3. 快照不可用 → `UNKNOWN/UNAVAILABLE_TRANSFORM_SNAPSHOT`，不 fail-open。
4. GATE.json 逐边字段齐备（`transforms_available` / `root_unchanged` / `third_unchanged` / `skeleton_unchanged`）。
5. 离线一律不 PASS（`NOT_MEASURED`）。

**仍需 1H+1G 实机（本票不声称）**

1. 真实蒙皮下「施加 0.10U 后可见表面是否跟随」——`GATE_RESULT=PASS` 只在实机有意义。
2. 三条边在真实相机帧里是否确实未被触碰（离线只能证「代码路径存在比较」）。
3. 子骨骼 pose 是否被本探针的写入间接带动（本票明确**未覆盖**，需后续轮在同一 callback 内做数值比对）。
4. `SnapshotTransform` 无 scale：真实场景若发生非均匀缩放，本门看不到。
5. t86-F6（单帧单姿态）、t86-F7（子骨骼 pose）按架构师裁决不在本票扩。

## 9. 未改项

- 不改 `Decide` 的逐 renderer 残差判据与 `epsilon` 未指定口径（t90 已定）。
- 不改 `profile_hash`、`excluded_renderers`、`SelectSampleIndices`（t90 已过）。
- 不改 `Program.cs` 中 t69 的 `T69-01` 注册行。
- 不写 `Release/`、`publishi/`、`Develop-Stage/`。
