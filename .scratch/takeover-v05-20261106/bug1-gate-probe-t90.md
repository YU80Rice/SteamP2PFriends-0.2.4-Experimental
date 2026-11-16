# T90 — G 门判据回归 + 接线收口（repair round 2）

- 票号：t90（被修 t85，双轴复审 needs_revision：规格轴 T86-F1…F7、工程轴 R16-01…R16-06）
- 基线：t85 交付态 `Bug1GateProbeLogic.cs` `e75885db…`(399 行) / `Bug1GateProbe.cs` `c767683e…`(698 行) / `Bug1GateProbeTests.cs` `dc482c3a…`(365 行) / `Program.cs` `999497d4…`(1095 行) / DLL `d49a8d63…`；三命令 774/776（仅 GBA01/NIL01），Bug1 段 90/90
- 本轮终态：780/782（仅 GBA01/NIL01），Bug1 段 96/96，DLL `db0945b229ee2cdc931ed104cf3fe26b5e74e2fcd7766e21c17e85b3c9a20c03`

## 1. T86-F1（high）：判据回归 t76 §2.2 的逐 renderer 口径

**原文口径**（`.scratch/takeover-v05-20261106/bug1-minimal-render-interpolation-t76.md` §2.2）：对**每一个**被画的 renderer，取该 renderer 全部采样点的 `max‖(p_after − p_before) − delta‖`，要求 ≤ epsilon。

**改前**（t85，`Bug1GateProbeLogic.Decide`）：把全部 renderer 的点合并成一个 `magnitudes` 数组，判定用 `Deviation = |Median − offset| / offset > 0.15` 与 `P10 < offset × 0.85`。后果：约 10% 表面点不动、或单个 renderer 撕裂，仍可 PASS。

**改后**（本票）：
- 采样与残差按 renderer 分组：`Bug1GateRendererStats{Path, MeshName, BoneCount, RootBonePath, SampleCount, MaxResidualU, MaxDeviationRatio, MaxDisplacementU, P10, P50, P90}`；`RunGate` 里每个 renderer 独立 `BakeSample` 基线 + 独立 `MeasureAxis`。
- `Decide(...)` 逐 renderer 判：`MaxDisplacementU < offset×0.5 || MaxResidualU > offset` → `SURFACE_NOT_MOVED`；`MaxDisplacementU > offset×2` → `SURFACE_MOVED_TOO_FAR`；`epsilonU>0 && MaxResidualU>epsilonU` → `PARTIAL_SURFACE_MOVED`；全部通过才 `PASS`。
- P10/P50/P90 仍输出（`renderers[]` 与汇总层各一份），**只作报告，不参与判定**。

**epsilon 未指定**：t76 原文未给具体数值，故**不自选阈值**。新增 cfg 键 `Bug1Gate/MaxDeviationU`（float，默认 `0`）；`0` 时 `Decide` 返回 `UNKNOWN` + `EPSILON_UNSPECIFIED`，并照常输出原始 `max_residual_u`、`max_deviation_ratio = max_residual/offset`、`epsilon_source=unspecified`，由决策者定阈。设了值则 `epsilon_source=config:MaxDeviationU`。

| 场景（同一 offset=0.10U） | 改前 | 改后 |
| --- | --- | --- |
| renderer A 完美跟随（残差 0）、renderer B 残差 0.04 | 合并中位数 0.02 → `PASS` | B 超 epsilon → `PARTIAL_SURFACE_MOVED` |
| 全部 renderer 残差 0、epsilon 未指定 | `PASS` | `UNKNOWN/EPSILON_UNSPECIFIED`（原始 max_residual_u 照出） |
| 表面完全没动（位移 0、残差 = offset） | 中位数偏差 100% → FAIL | `SURFACE_NOT_MOVED` |

## 2. T86-F2（high）：比较下沉 + 生产接线钉死

- 下沉到 `Bug1GateProbeLogic`（可离线）：`TransformsEqual`（7 个 float 位级比较）、`CollidersEqual`（count/name/type/enabled/center/size 逐字段位级）、`RaycastsEqual`（hitName + distance 位级）、`CollectImpureComponentTypes`（白名单判定）、`IsWhitelistedRenderComponentType`。
- 生产侧 `Bug1GateProbe.RunGate` 只调用上述纯函数，不再自带比较实现（旧的三份 `CollidersEqual/RaycastsEqual/TransformsEqual` 与 `FindImpureComponent` 已删除）。
- IL 断言钉住（`Bug1GateProbeTests.Test_ProductionWiringUsesLogicComparisons`，Bug1-95）：`RunGate` → `CollectImpureComponentTypes` / `CollidersEqual` / `RaycastsEqual` / `TransformsEqual` / `ComputeProfileHash` / `Decide` / `Emit` / `CollectVisibleRenderers` 各 ≥1 条调用边；`CollectVisibleRenderers` 内必须读 `Renderer.enabled` 与 `GameObject.activeInHierarchy`；`OnPreCull` → `HandleTargetGone` ≥1 条边。
- 每条比较都有「改恒真/删调用 → 必红」证据（见 §5 变异 M2/M3/M4/M6/M9）。

## 3. T86-F3（medium）：可见表面过滤与排除审计

- `CollectVisibleRenderers(modelNodes, result)`：只收 `sharedMesh != null && vertexCount > 0 && renderer.enabled && gameObject.activeInHierarchy` 的 `SkinnedMeshRenderer`。
- 被排除的 renderer 写进 `GATE.json` 的 `excluded_renderers[]`（`{path, reason}`），reason 取 `empty-mesh` / `renderer-disabled` / `gameobject-inactive`，不静默丢弃。
- IL 断言（Bug1-95）保证 `get_enabled` / `get_activeInHierarchy` 读取与 `CollectVisibleRenderers` 调用点不被删掉。

## 4. T86-F4（medium）：快照不可用一律 fail-closed

- `Bug1GateTransformSnapshot` / `Bug1GateColliderSnapshot` / `Bug1GateRaycastSnapshot` 均加 `Available`。
- `SnapshotColliders(...)` / `RaycastProbe(...)` 读失败返回 `Available=false`，**不再返回空表**；`TransformsEqual` / `CollidersEqual` / `RaycastsEqual` 对任一侧不可用返回 `false`（不是 `true`）。
- `Decide` 对 `!collidersAvailable` → `UNKNOWN/UNAVAILABLE_COLLIDER_SNAPSHOT`，`!raycastAvailable` → `UNKNOWN/UNAVAILABLE_RAYCAST_SNAPSHOT`；GATE.json 写 `collider_available` / `raycast_available`。
- 即「读失败 ≠ 未变化」，方向已反过来。

## 5. T86-F5（medium）与工程轴 R16-01/02/05/06

- **F5 profile 指纹**：`Bug1GateRendererProfile{Path, MeshName, BoneCount, RootBonePath, MaterialNames, ColliderTypes}` → `ComputeProfileHash` 规范化拼接后取 SHA256（64 位十六进制），写 `GATE.json` 的 `profile_hash`；换 mesh / 骨骼 / 材质 / 层级路径任一变化，指纹必变（Bug1-94 断言 64 字符 + 同输入同值 + 四类变化各自变值）。
- **R16-01 白名单零覆盖**：`IsWhitelistedRenderComponentType` 纯函数化，8 个放行项（Transform/Renderer/MeshFilter/Animator/Animation/LODGroup/SkinnedMeshRenderer/MeshRenderer）+ 15 个拒绝项（Cloth/Rigidbody/BoxCollider/CapsuleCollider/CharacterController/AudioSource 等）各有断言；`CollectImpureComponentTypes` 另测去重、保序、`<read-failed:*>`/`<null-component>` 保留（Bug1-90）。
- **R16-02 目标在 Arm 与首帧之间离场**：`OnPreCull` 发现 target 失效 → `HandleTargetGone()`：写 `GATE.json`（`gate_result=FAIL`、`reason=TARGET_GONE_BEFORE_FIRST_SAMPLE`、notes 记 `retry n/3`），重置 `_ranOnce=false`，在 `MaxArmRetries=3` 内重新 `EnsureRetryHook()`；耗尽时 notes 记 `gave up after 3 retries` 并 `RoleLogger.Error`，日志与 JSON 同源。
- **R16-05 Samples 硬上限**：`SelectSampleIndices(vertexCount, maxSamples)` 用 `step = ceil(V/cap)`，返回点数恒 ≤ cap（Bug1-93 用 1000 顶点 × cap 64 断言 ≤64 且覆盖首尾）。
- **R16-06 Emit 单一口径**：先 `BuildJson`，写盘失败则把 `Status=FAIL/Reason=OUTPUT_FAILED` 后**重新** `BuildJson` 再打日志，避免「日志 FAIL、JSON PASS」。

## 6. 变异证据（仓库外副本 `../_t90mut`，已删除）

驱动方式：`tar` 复制仓库（排除 `bin/obj/.git/.scratch/Release/publishi/Develop-Stage`，带 `Build/Version.props`）→ 每条「唯一替换 → 先 `dotnet build` 生产 → 再 build 测试 → 跑 exe → 从主树 `cp` 还原并打印源码 sha256」。副本 `diff -r`（同排除项）与主树零差异（仅复制产生的 `Build/Build`）后 `rm -rf`。

| 变异 | 文件:行 | 原文 → 替换文 | 变异后 DLL sha256 | 红名单 | 总数 |
| --- | --- | --- | --- | --- | --- |
| M1（F1 退化为只看首个 renderer） | `Bug1GateProbeLogic.cs:258` | `Bug1GateRendererStats renderer = renderers[i];` 后插入 `if (i > 0) continue;` | `9e801730869b82d0c934eabf255248669da2c0f2c1dd9e035a3b424b3a7410f3` | Bug1-91 | 779/782 |
| M2（transform 比较恒真） | `Bug1GateProbeLogic.cs:372` | `return BitEqual(left.Px, right.Px)` → `return true \|\| BitEqual(left.Px, right.Px)` | `fad961c2b590ba9f325d94a88fec45da6f5ec8c7086206b388fc18c390450f0e` | Bug1-92 | 779/782 |
| M3（collider 比较恒真） | `Bug1GateProbeLogic.cs:386` | 函数体首行插入 `if (left != null) return true;` | `f70146e0f80195750b8c2dbe0d41ec508a06c6ddeca6bbd6e39a3db8a04351ba` | Bug1-92 | 779/782 |
| M4（raycast 比较恒真） | `Bug1GateProbeLogic.cs:410` | 函数体首行插入 `if (left != null) return true;` | `c2af00178e2007b715bfd9abd0ecdf8f6dbe5c034af72ede6f2e8746b4071a69` | Bug1-92 | 779/782 |
| M5（白名单整体短路） | `Bug1GateProbeLogic.cs:101` | 函数体首行插入 `if (typeName != null) return true;` | `034f3a7c5ee4b98fdab4f4c3be5a04fd6efcf9f45b7a8b37acce11ca1ee7ff39` | Bug1-90 | 779/782 |
| M6（纯节点门不调用 Logic） | `Bug1GateProbe.cs:356` | `Bug1GateProbeLogic.CollectImpureComponentTypes(CollectComponentTypeNames(modelNodes))` → `new List<string>()` | `224e0849490d8fab9a3fbbf46bd474c4b031aa83b0883ac9ab50c458b757bb8f` | Bug1-95 | 779/782 |
| M7（采样不受硬上限约束） | `Bug1GateProbeLogic.cs:183` | `int step = (vertexCount + cap - 1) / cap;` → `int step = 1;` | `8c59bb3ba15036567922391cb861b445f8d412eb123b035cee257e1bb43b6997` | Bug1-93 | 779/782 |
| M8（profile 指纹恒定） | `Bug1GateProbeLogic.cs:422` | 函数体首行插入 `if (profiles != null) return "constant";` | `54997523b8a70512aec42b3de83f53e12400ffe055176c48f74499d8a0a08f42` | Bug1-94 | 779/782 |
| M9（目标离场不写 JSON/不重试） | `Bug1GateProbe.cs:265` | `HandleTargetGone();` → `if (target != null) return;` | `6f076c0c5ef6c7180d009611e7048c40e41850828a7d7b7a2e391e38f89ef117` | Bug1-95 | 779/782 |

- 每条变异后 779/782 = 1 条自建用例红 + 2 条既有环境失败（GBA01/NIL01）。
- 还原后副本 `Bug1GateProbeLogic.cs` 回 `b0d0183c…`、`Bug1GateProbe.cs` 回 `9b7c48fb…`，与主树逐字节一致。
- 教训：`if (false)` / `return null;` 等恒假写法会触发 CS0162（本项目 TreatWarningsAsErrors），变异必须写成「运行期可达但不生效」。

## 7. 文件与哈希（改前 → 改后）

| 文件 | 改前行数 / sha256 | 改后行数 / sha256 |
| --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1GateProbeLogic.cs` | 399 / `e75885db…` | 756 / `b0d0183c75f8777196d47a11f8ad76568aa724b6cbc8fc92b51c895aaaf77495` |
| `Platform/Diagnostics/Patches/Bug1GateProbe.cs` | 698 / `c767683e…` | 938 / `9b7c48fbe071e33a84a7c48e52312d40c1dff13ac43bcaad36140143df1e3ad9` |
| `WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs` | 365 / `dc482c3a…` | 541 / `2902a454ccd9a66a2c5273e7f4241a0ef2fa17dddc5ec83afc3d99d33ba74018` |
| `WhitelistTests/Program.cs` | 1095 / `999497d4…` | 1101 / `f46fdecc43c1771d6c7a767f4c72bcd2abb6fd605b8439e9d49d9be059004ff7` |
| `SteamP2PFriends.csproj`（未改） | — / `af991c70…` | — / `af991c70de6bd822de0fd50d4a7f74496c52f2156e7377dacdd2a294f16d912d` |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs`（未改） | — / `0e1abb04…` | — / `0e1abb045df2ade1639949db3ac2e25650b80eb78ab19fbfcd8e87d9120fcc1b` |
| `bin/Release/SteamP2PFriends.dll` | 1527808 / `d49a8d63…` | `db0945b229ee2cdc931ed104cf3fe26b5e74e2fcd7766e21c17e85b3c9a20c03` |

`WhitelistTests/Program.cs` 只追加 6 行 `RunTest("Bug1-90…" …)`，未动 t69 的 `T69-01` 注册行。

## 8. 三命令（主树最终实测）

1. `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` → 已成功生成，**0 个警告 / 0 个错误**。
2. `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` → 已成功生成，**4 个既有警告 / 0 个错误**。
3. `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` → `=== Final Result: 780/782 PASS (Failed: 2) ===`，FAIL 仅 `[PureMemory] GBA01 Native First Observe Binding` 与 `[PureMemory] NIL01 Real Native Lookup Contract`（既有环境失败，TypeInitializationException 链）；Bug1 段 96/96（Bug1-01…Bug1-95，含本轮新增 Bug1-90…95）；exe exit=1 属既有约定（有失败即 1）。

**范围与哈希核验**：
- Bug3 五文件哈希未变：`a89f40b949f841d0`（`Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs`）/ `7a82de8e38fa0f6a`（`GroupStateRecoveryCore.cs`）/ `3d5044f7870fbb17`（`GroupLeaseLedger.cs`）/ `a45533850f31bd96`（`GroupLoadPlanner.cs`）/ `5b0c4e9ba4c87cbe`（`P2PGroupRegistration.cs`）。
- `find Release publishi Develop-Stage -newermt "2026-11-08 00:50" -type f` → 0 条（本轮未写发布目录）。
- 变异副本 `../_t90mut`、`../_t90ver` 均在仓库外，核验后删除；主树根目录无新增临时文件。

## 9. 仍需 1H+1G 实机（离线不可证）

离线进程没有 Unity 运行时，`GATE_RESULT` 在本机一律 `UNKNOWN/NOT_MEASURED`（Bug1-88 承重）。以下项目**只能**在真实 1 Host + 1 Guest 上确认：

1. `BakeMesh` 反射签名在 Mono/Unity 2021.3 下是否命中（回退单参路径是否触发）。
2. `Renderer.enabled` / `activeInHierarchy` 过滤后，实际可见表面集合是否覆盖第三人称角色全部被画部件（是否有 `excluded_renderers` 非空且合理）。
3. `Physics.Raycast` 探针在真实地图上是否稳定命中同一体（地形/植被遮挡会改变 `hitName`）。
4. Collider 快照在真实角色上是否可读（`Available` 是否为 true）；读不到时是否正确落 `UNKNOWN/UNAVAILABLE_*`。
5. `profile_hash` 在换装/UMA 重建后是否真的变化。
6. `TARGET_GONE_BEFORE_FIRST_SAMPLE` 重试路径在真实关卡时序下是否命中（Arm 与首帧之间的窗口是否真实存在）。
7. epsilon 需由决策者给定（`Bug1Gate/MaxDeviationU`）；在此之前所有真实测量都会停在 `EPSILON_UNSPECIFIED`。

**口径声明**：本轮只把判据改回 t76 §2.2 的 per-renderer max 口径并收口接线，**不构成「最小渲染插值路线可行」的结论**。只有真实 1H+1G 下每个被画 renderer 的 `max‖Δp−delta‖ ≤ epsilon` 成立，才能说「全部顶点跟随」；否则应照实记为「存在未跟随的 renderer，最小路线失败」。T86-F6（单帧单姿态）、T86-F7（骨骼 pose 哈希）按 captain 裁决**本票不扩**，留待实机/Architect 追认。

## 10. 未做与边界

- 未改 `Bug1ProbeRuntime`/`Judge`/判定逻辑、未调 EPS/B/完整性门（本票范围外）。
- 未改 `Release/`、`publishi/`、`Develop-Stage/`、Bug3 五文件、t69 的注册行。
- `Emit` 的写盘失败分支（R16-06）与 `HandleTargetGone` 的重试分支（R16-02）只做了代码级收口 + IL 断言，**没有**在离线环境触发真实 IO 失败/真实目标离场，属实机待验。


