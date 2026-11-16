# 规格轴复审 r2：t90（Bug1 G 门探针 · F1–F5 + R16 修复轮）

- 审查人：reviewer-spec（t91，attempt `0de92671-ef91-4fef-a4ea-6597ee0a0b9e`）
- 被审票：t90（ds-probe2，completed，attempt `e0c1f1…`）
- 基准判据：t76 §2（纯节点门 / 刚性位移门 / 覆盖可逆门）、t90 派工 acceptance 七条、t86 复审 F1–F5 与 R16 四条
- 结论：**needs_revision**。F1–F5 与 R16 的行为面基本修到位，但 acceptance ③（不变性四项"删任一项必红"）**未满足**：Skeleton 位级快照被采集并导出却**从未参与任何判定**，且生产侧只比较 root 与 Third 两条边、IL 断言为 `>=1` 无法逐项钉死——本审查官实测"删掉 Third 边"后 780/782 全绿。
- 说明：t90 的 `inScope` 在任务记录里被解析成 `max/p10/p50/p90`、`GATE.json` 之类的非路径片段，实际还改了 `WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs` 与 `WhitelistTests/Program.cs`（Bug1-90..95 注册）。本报告按**实际字节**审，不按 `inScope` 字面。

## 1. 被审字节绑定

本机实测（2026-11-08 02:05 前后取样），与 t90 报告 §7 的逐项哈希一致：

| 文件 | 字节 | sha256 |
| --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1GateProbe.cs` | 41394 | `9b7c48fbe071e33a84a7c48e52312d40c1dff13ac43bcaad36140143df1e3ad9` |
| `Platform/Diagnostics/Patches/Bug1GateProbeLogic.cs` | 36926 | `b0d0183c75f8777196d47a11f8ad76568aa724b6cbc8fc92b51c895aaaf77495` |
| `WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs` | 32554 | `2902a454ccd9a66a2c5273e7f4241a0ef2fa17dddc5ec83afc3d99d33ba74018` |
| `WhitelistTests/Program.cs` | 150049 | `f46fdecc43c1771d6c7a767f4c72bcd2abb6fd605b8439e9d49d9be059004ff7` |
| `SteamP2PFriends.csproj` | 27575 | `af991c70de6bd822de0fd50d4a7f74496c52f2156e7377dacdd2a294f16d912d`（与 t86 轮相同，未改） |
| `bin/Release/SteamP2PFriends.dll`（与 `WhitelistTests/bin/Release/` 同哈希） | 1527808 | `db0945b229ee2cdc931ed104cf3fe26b5e74e2fcd7766e21c17e85b3c9a20c03` |

**本判定只对上述字节有效。** 其中 `Bug1GateProbe.cs` / `Bug1GateProbeLogic.cs` / `Bug1GateProbeTests.cs` / `Program.cs` 四份是 t90 实际改动件；`SteamP2PFriends.csproj` 未动（无新增 Compile），注册仍走既有 `Bug1GateProbe.Initialize()`（`Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs:84`）。

## 2. 逐条 acceptance

### ① G 门语义与 t76 §2 逐条对照（BakeMesh 真实顶点、偏差比阈值、p10/p50/p90 落在生产路径）——**passed**

- 可见几何取的真实蒙皮顶点：`Bug1GateProbe.BakeSample`（`Bug1GateProbe.cs:710` 起）用 `renderer.BakeMesh` 后 `renderer.transform.localToWorldMatrix.MultiplyPoint3x4(vertices[i])` 得到世界点，与 t76 §2.2「比较实际蒙皮后的世界表面点」一致。
- 偏差比的分母/分子均在判定内产生：`MeasureAxis` 逐 renderer 算 `diff = moved - before`、`residual = |diff - delta|`、`movedBy = |diff|`（`Bug1GateProbe.cs:552-565`），`Bug1GateProbeLogic.MaxDeviationRatio = MaxResidualU / offset`（`Bug1GateProbeLogic.cs:313-320`）。
- p10/p50/p90 在生产侧算：`Aggregate`（`Bug1GateProbe.cs:590-618`）调 `summarize` 得 P10/P50/P90/Median，再由 `BuildJson` 导出（`Bug1GateProbeLogic.cs:482-485`）。它们现在只作报告字段，不参与判定——这与 t90 报告 §1"P10/P50/P90 只报告、由逐 renderer max 判定"的自述一致，且符合 acceptance ①「三项都要落在生产路径上」的字面要求。

### ② 证明"不能只看 transform"被真正执行——**passed**

- 偏差比只来自烘焙顶点：判定输入是 `Bug1GateRendererStats.MaxResidualU/MaxDisplacementU`，其唯一来源是 `MeasureAxis` 的烘焙点差；`Bug1GateProbe.cs` 全文件 grep `PositionChanged` **0 命中**，无"用 transform 位移冒充表面位移"的旁路。
- transform 比较只作独立不变性门（root/Third），不与位移成功混用；`restore` 判定另走 `RestoreExact`（逐分量 `BitEqual`，`Bug1GateProbe.cs:436-443`）与 `SurfaceRestored`（重烘焙回基线 `RestoreEpsilonU`，`:445-465`）。

### ③ 不变性四项在生产实现中存在且可失败（root/Third/Skeleton 位级快照、collider bounds、raycast 命中；删任一项必红）——**failed**

见 §4 F1。要点：Skeleton 快照只采集导出、从不比较；生产 `CollidersUnchanged` 只比 root 与 Third 两条边；IL 断言 `CountMethodCalls(...,"TransformsEqual") >= 1`（`Bug1GateProbeTests.cs:430-434`）无法区分是哪一条边，本审查官实测删掉 Third 边**全绿**。

### ④ 每帧可逆还原、localPosition 与原生值位级相等、无累计偏移路径——**passed**

- 每轴测量后 `finally { Restore(modelNodes, originals); }`（`Bug1GateProbe.cs:431-434`），`Restore` 用 `originals[i]`（绝对原始值）写回，不是增量写回（`:568-575`），因此不存在"两次测量叠加偏移"的路径。
- 还原后逐分量 `BitEqual`（`:436-443`），任一不等即 `RestoreExact=false`→`RESTORE_MISMATCH` FAIL（`Bug1GateProbeLogic.cs:219`）。
- 另有第二道"表面回基线"门 `SurfaceRestored`（`:445-465`，阈值 `RestoreEpsilonU=1e-4`），比单一 transform 判等更强。

### ⑤ fail-closed：拒绝码触发条件与 t76 §2.1 一致、拒绝时给出组件清单——**passed**

- 纯节点门由生产 `CollectImpureComponentTypes(CollectComponentTypeNames(modelNodes))` 计算（`Bug1GateProbe.cs:356`），`!pureNode → FAIL/IMPURE_RENDER_NODE`（`Bug1GateProbeLogic.cs:214`），触发条件为"子树内出现非白名单组件"。
- 拒绝时给清单：`FailureDetail = FormatImpureComponents(impure)`（`Bug1GateProbe.cs:361`），输出形如 `components=A,B`、空集 `(none)`（`Bug1GateProbeLogic.cs:192-202`），下游可直接判"哪个组件不准入"。
- 空可见表面单独成码：`renderers null/0 → FAIL/NO_VISIBLE_SURFACE`（`:245`），不把空集合当通过。

### ⑥ 默认关闭与零副作用、开启失败不留半开——**passed**

- cfg 默认 `false`（`Bug1GateProbe.cs:87` `enabled = file.Bind(..., "Enable", false, ...)`）。未开启时不挂钩：`Camera.onPreCull` 只在 `HookCamera()`（`:240-249`）挂、`UnhookCamera()`（`:253-258`）摘，`OnEnabledChanged` 关闭分支同时 `UnhookCamera(); UnhookRetryHook(); _target=null`（`:107-115`）。
- 单次执行有 `_ranOnce` 守卫（`:45/:126/:268`），重复 arm 走 `REFUSED/ALREADY_RAN` 且不重复测量。
- 失败不留半开：`OnPreCull` 内异常→`Fail(UnexpectedException)+Emit`（`:273-285`）；目标首帧前离场→`HandleTargetGone` 写 GATE.json + 有界重试 `MaxArmRetries`（`:265-303`），耗尽后明确放弃并给原因。
- `Emit` 写盘失败会把 `Status=FAIL/OUTPUT_FAILED` 并重建 JSON 后打日志（`:860-878`），日志与 JSON 同源，不会出现"日志说 PASS、文件没写"。

### ⑦ 口径诚实：离线不能证明 GATE_RESULT；Bug1-88 真钉死"离线全绿也不得 PASS"——**passed**

- t90 报告 §9 明言离线只能证明"判定链能在假数据上跑通"，GATE_RESULT 必须 1H+1G 实测；未把 780/782 写成闸门通过。
- 本审查官实测 X3（epsilon 未指定时改为 PASS）→ 778/782 红 `Bug1-84`+`Bug1-91`，说明"缺参数不得 PASS"确被用例钉住；离线口径用例 Bug1-88 仍在（本轮未删）。

### ⑧ 自跑至少三条变异并逐字节还原——**passed**（本审查官另跑四条）

见 §3。四条变异各自变红、还原后 DLL 与源文件均回到送审字节。

## 3. 变异矩阵（本审查官实跑，非采信自述）

副本 `D:/Agent-工作目录/DevelopMyUNMultiplayerModAndModloader/_t91iso`（tar 复制、排除 `.git/bin/obj`），脚本 `D:/tmp/t91_mut.sh`，日志 `D:/tmp/t91-mut.log`；每条：改字节 → `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` → 重建测试项目 → 覆盖两处 DLL → 跑 exe。

| # | 变异 | 结果 DLL sha256 | 计数 | 红名单 |
| --- | --- | --- | --- | --- |
| M0 | 无（基线） | `db0945b2…` | 780/782 | 仅 GBA01/NIL01（环境） |
| X1 | `Bug1GateProbe.cs:488-491` 删 `&& TransformsEqual(thirdBefore, thirdAfter)` | `23666b3f…` | **780/782** | **无新增** |
| X2 | `Bug1GateProbeLogic.cs:372` `TransformsEqual` 首行改成 `if (left != null) return true;` | `d4921835…` | 779/782 | Bug1-92 |
| X3 | `Bug1GateProbeLogic.cs:287-296` epsilon 未指定改成直接 PASS | `1ec4a877…` | 778/782 | Bug1-84、Bug1-91 |
| X4 | `Bug1GateProbe.cs:356` 生产纯节点门改 `new List<string>()` | `8f8ecdde…` | 779/782 | Bug1-95 |
| R0 | 还原 | `db0945b2…` | 780/782 | 仅 GBA01/NIL01 |

还原核对：`Bug1GateProbe.cs` 回 `9b7c48fb…`、`Bug1GateProbeLogic.cs` 回 `b0d0183c…`，DLL 逐字节回 `db0945b2…`。

读法：X2/X3/X4 说明"比较函数本体""epsilon 不得放行""生产纯节点门"三条是被钉住的；X1 说明"哪一条 transform 边被比较"没有被钉住——这正是 §4 的依据。

## 4. Finding

### F1（high）Skeleton 位级快照从不参与判定；root/Third 两条边也无法逐项钉死

- 现象一：生产采集了两份骨骼快照——基线期 `skeletonBefore`（`Bug1GateProbe.cs:392`）与收口期 `skeletonAfter`（`:469`），并写入结果 `result.SkeletonBefore/After`（`:478-479`）；`BuildJson` 也把 `skeleton_before/skeleton_after` 导进 GATE.json（`Bug1GateProbeLogic.cs:499-500`），但判定表达式只用到 root 与 third：

  ```csharp
  result.CollidersUnchanged = available
      && Bug1GateProbeLogic.CollidersEqual(...)
      && Bug1GateProbeLogic.TransformsEqual(rootBefore, rootAfter)
      && Bug1GateProbeLogic.TransformsEqual(thirdBefore, thirdAfter);   // Bug1GateProbe.cs:488-491
  ```

  `SkeletonBefore/SkeletonAfter` 没有任何比较消费点（全文件仅 `:478-479` 赋值与 `Logic:499-500` 导出）。
- 现象二：同文件 `:516-517` 的 note 声称"真正该守的『没碰骨头』由 Skeleton 位级快照 + collider bounds + raycast 覆盖"，与其上方 :488-491 的实现**不一致**——note 把未实现的覆盖写成了已实现。
- 现象三：`Bug1GateProbeTests.cs:396-408` 的 `AssertWired(...)` 调用与 `:430-434` 的实现，对 `TransformsEqual` 只断言 `CountMethodCalls(runGate, ..., "TransformsEqual") >= 1`。只要还有一条边在调用就满足，无法区分 root / Third，也无法覆盖 Skeleton（它压根没有调用点）。实测 X1 删掉 Third 边后 **780/782 全绿**，即"删任一项必红"不成立。
- 影响：t76 §2.2 要求"测试前后原骨骼 pose 哈希必须相同"（本项目退化为 Skeleton transform 位级快照作为可判代理），这是 G 门成立的必要条件之一。当前实现即使骨骼被动过、或仅 root 被动过而 Third 未动，GATE.json 仍可能给出 PASS；而且因为快照字段存在，事后读 GATE.json 的人会以为该项已被验证——比"没做"更危险。
- 必改（任一）：
  1. 把 Skeleton 纳入判定：`TransformsEqual(skeletonBefore, skeletonAfter)` 进 `CollidersUnchanged` 或独立成 `skeleton_unchanged` 字段并在 `Decide` 中参与 FAIL；
  2. 把 IL 断言改成逐边精确计数（例如 `CountMethodCalls("TransformsEqual") >= 3`，或对 root/third/skeleton 三个调用点分别按实参定位），并补一条"骨骼被动过 → 判 FAIL"的行为用例（可用 seam 替换 Skeleton 快照）；
  3. 更正 `:516-517` 的 note，使其与实现一致（若决定不做骨骼门，需 Architect 在 t76 上书面降级并写进 GATE.json 的 notes，而不是让 note 冒充已覆盖）。

## 5. 已确认通过的行为面（供下游不必重复核）

- 逐 renderer 判定已落地：`Decide` 遍历 `renderers`，`SampleCount<=0` 跳过，`MaxResidualU > offset` 或 `MaxDisplacementU < offset*0.5` → `SURFACE_NOT_MOVED`，`> offset*2` → `SURFACE_MOVED_TOO_FAR`，`epsilonU>0 && MaxResidualU > epsilonU` → `PARTIAL_SURFACE_MOVED`（`Bug1GateProbeLogic.cs:258-280`）。单 renderer 撕裂（0.04 残差）不再被全局中位数掩盖，Bug1-91 覆盖。
- epsilon 语义诚实：未指定（cfg 默认 0）→ `UNKNOWN/EPSILON_UNSPECIFIED`，并在 JSON 里带 `epsilon_source`（`:287-296`、`Bug1GateProbeLogic.cs:476-477`）。X3 证明这条会被守住。
- 隐藏 renderer 已排除且给出原因：`CollectVisibleRenderers` 过滤 `sharedMesh==null||vertexCount<=0`→`empty-mesh`、`!renderer.enabled`→`renderer-disabled`、`!activeInHierarchy`→`gameobject-inactive`，写 `excluded_renderers[]`（`Bug1GateProbe.cs:647-681`），并有 IL 断言 `get_enabled`/`get_activeInHierarchy` 各 ≥1（测试 `:422-425`）。
- 快照可用性 fail-closed：`ColliderAvailable/RaycastAvailable` 分别来自采集结果，`!available` 在 `Decide` 里先于 unchanged 判定返回 `UNKNOWN/UNAVAILABLE_*`（`Bug1GateProbeLogic.cs:225-234`），GATE.json 导出 `collider_available/raycast_available`（`:490-493`）。比较函数本身"任一侧不可用→false"（`:372-410`）。
- profile 指纹存在：`ComputeProfileHash(BuildProfiles(renderers, third))`（`Bug1GateProbe.cs:420`），profile 含 path/sharedMesh 名/materials/bones 数/rootBone 路径/节点 collider（enabled）等（`Logic:470-530` 区段），并把 `profile_hash` 写进 GATE.json（`:494`），换装/换模型后指纹可变（测试 `:274`）。
- 接线顺序：collider/raycast before → 基线烘焙 → 每轴测量 → finally 还原 → 还原核对/表面回基线 → 事后三快照 → `Decide`（`Bug1GateProbe.cs:394-510` 顺序），`Decide` 用的是 `result.EpsilonU`（用户 cfg）而不是硬编码。

## 6. 残留与 UNKNOWN（不得视为通过）

| 项 | 状态 | 归属 |
| --- | --- | --- |
| t86-F6 单帧单姿态（仅 X/Z 两轴、单姿势） | 仍未做；t76 §2.2 要求"至少两个姿态/朝向" | Architect 裁决是否降级，或另票补姿态矩阵 |
| t86-F7 骨骼 pose 哈希未做 | 仍未做；t85 起声明"有意不做" | 同上；另见 §4 F1（Skeleton 快照是现有替代物，但当前未参与判定） |
| G 门数学结论（真实整体平移、不撕裂） | 未测量 | 需 1H+2G 真机；离线 780/782 不能证明 |
| BakeMesh 在目标 Unity/Mono 版本可读、顶点数在采样上限内 | 未测量 | 真机 |
| collider/raycast 快照在真实玩家上的稳定性（含 raycast 是否命中地形） | 未测量 | 真机 |
| 白名单是否过严（纯节点工程件是否被误拒） | 未测量 | 真机 |
| `UNKNOWN/EPSILON_UNSPECIFIED` 后决策者如何填 epsilon | 流程未定 | Architect/队长 |

## 7. 边界声明

- 本报告只判"是否满足需求契约与架构裁决"，不评代码风格；未改任何生产代码或测试代码（变异全部在仓库外副本完成并已还原，DLL 与源文件均回到 §1 的哈希）。
- 本轮未重跑 t90 自述的三命令的完整组合以外的构建；780/782 是本审查官在送审 DLL 上的独立复跑结果，与 t90 自述一致；两条失败均为既有环境缺失（GBA01/NIL01 需 Unity 运行时）。
- `needs_revision` 的阻塞项只有 F1（acceptance ③）。F1 修好后仍需双轴复审当前字节，方可谈"G 门探针可交付"；即使 F1 关闭，**G 门是否成立仍未测量**，本报告不构成任何 G 门通过的结论。

---

（自绑定：本行以上正文 15415 字节 / sha256 `e9588203dc11164060cc96d11771b77feefe77b2a4f449e78faf9bb0e05c1292`；追加本行后全文 15652 字节。复审时间 2026-11-08 02:18，主树字节未被本人修改。）
