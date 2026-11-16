# 规格轴复审 r3：t92（G 门探针 · 三条变换边逐条判定 + note 更正）

- 审查人：reviewer-spec（t93，attempt 2 `57b1805d-1ad5-403b-a9d2-42a74ccbf00b`；attempt 1 `3f60bbc4…` 已作废）
- 被审票：t92（ds-probe2，completed）
- 基准判据：t76 §2（纯节点门 / 刚性位移门 / 覆盖可逆门）、本票 8 条 acceptance、t91 的 F1（high）
- 结论：**pass**。t91 的唯一阻塞项（acceptance ③：root/Third/Skeleton 逐条可失败）已被真实关闭——三条边各有独立比较、独立拒绝码，本审查官逐边删掉任一比较后对应用例都变红；本轮另记 1 条 low 残余（"保留调用但丢弃结果"离线不可区分），它不违反本票字面判据，也不阻塞交付。
- 契约提示：t92 的 `inScope` 被解析成 `Bug1GateProbe.cs` + 伪路径（`root/third/skeleton`、`GATE.js`），故其 `changedPaths` 只报了 1 个文件；实际还改了 `Bug1GateProbeLogic.cs`、`Bug1GateProbeTests.cs`、`Program.cs`。本报告按**实际字节**审。

## 1. 被审字节绑定

本机实测（2026-11-08 02:38 取样）：

| 文件 | 字节 | 行数 | sha256 |
| --- | --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1GateProbe.cs` | 42626 | 955 | `f376f2a18bc1fc60e952829cf8179d6f794e2f5646cd48d970928288d06c296f` |
| `Platform/Diagnostics/Patches/Bug1GateProbeLogic.cs` | — | 805 | `07261a091b60908f75ab42b11a3db36b344d9a09ac0e363cc021f8ad0dcb09b7` |
| `WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs` | — | 619 | `13eb30a3a3f714665909607bd10fc8568c9832804aaba3f11b9722e7b3a0e848` |
| `WhitelistTests/Program.cs` | — | 1103 | `0fbb4eacdfc971882756a5a992a9e556e7f2356775389d7b862d112d3796fc18` |
| `SteamP2PFriends.csproj` | 27575 | — | `af991c70de6bd822de0fd50d4a7f74496c52f2156e7377dacdd2a294f16d912d`（未改） |
| `bin/Release/SteamP2PFriends.dll`（两处 bin 同哈希） | 1540096 | — | `388f7334aae6e0c2eab221c5963a0e51119c2f5bad729636cc5723505053f3ea` |

与 t92 自报逐项一致（Probe `f376f2a1`、Logic `07261a09`、Tests `13eb30a3`、Program `0fbb4eac`、DLL `388f7334`）；行数一处差异：t92 报 Probe 954 行，本机 `wc -l` 为 955（文件末尾无换行差异所致，字节与哈希一致，不影响判定）。

**本判定只对上述字节有效。**

## 2. 逐条 acceptance

### ① G 门判定语义与 t76 §2 逐条对照（BakeMesh 真实顶点、偏差比阈值、p10/p50/p90 在生产路径）——**passed**（沿用 t91 结论，本轮核对未回退）

- `BakeSample`（`Bug1GateProbe.cs:710` 起）仍走 `BakeMesh` + `localToWorldMatrix.MultiplyPoint3x4`；判定输入仍是烘焙点差（`MeasureAxis`）。
- p10/p50/p90 仍在生产 `Aggregate`（`:588`，`:612` 写 `P10U`）计算并由 `BuildJson` 导出（`Bug1GateProbeLogic.cs:506-508`）。
- 逐 renderer max 判定与 epsilon 门未动：`EPSILON_UNSPECIFIED` 仍在（`Logic:51/199`），Bug1-84/Bug1-91 在 782/784 中全绿。

### ② 「不可只看 transform」被真正执行、无旁路——**passed**

- `Bug1GateProbe.cs` 全文件 grep `PositionChanged` **0 命中**；偏差比只来自烘焙顶点差，transform 比较只作独立不变性门。

### ③ 不变性四项逐项存在且可失败（root/Third/Skeleton、collider bounds、raycast；删任一项必红）——**passed（本轮关闭 t91-F1）**

- 三条边逐条比较（`Bug1GateProbe.cs:490-492`）：
  - `result.RootUnchanged = TransformsEqual(rootBefore, rootAfter);`
  - `result.ThirdUnchanged = TransformsEqual(thirdBefore, thirdAfter);`
  - `result.SkeletonUnchanged = TransformsEqual(skeletonBefore, skeletonAfter);`
  并用 `TransformsAvailable`（`:489-491`）把"任一侧快照不可用"与"变了"分开。
- 判定侧三条边各有独立拒绝码且 fail-closed 顺序正确（`Logic:228` 不可用→`UNAVAILABLE_TRANSFORM_SNAPSHOT`；`:234/:239/:244` → `ROOT_CHANGED`/`THIRD_CHANGED`/`SKELETON_CHANGED`）。常量定义在 `Logic:61-64`。
- 导出可核：`BuildJson` 新增 `transforms_available` / `root_unchanged` / `third_unchanged` / `skeleton_unchanged`（`Logic:519-522`），事后读 GATE.json 能判断是哪条边被动了。
- 用例：`Bug1-96`（`Bug1GateProbeTests.cs:437-468`）对四条分支逐条断言并附一条"三边干净→PASS"的正控，避免"永远 FAIL"式的假绿；`Bug1-97`（`:473-481`）把 IL 断言从聚合 `>=1` 改为 `TransformsEqual >= 3`。
- **本审查官逐边实测**（见 §3 D1/D2/D3）：删掉 root、Third、Skeleton 任一条比较，`Bug1-97` 都变红且消息点名"实际 2"；三条边各自承重，不是共用一条。
- collider bounds / raycast 命中仍分别有 `CollidersEqual`/`RaycastsEqual` 与可用性门（`Bug1GateProbe.cs:498-503`、`Logic:252-266`），t91 的 MA/MB 变异结论不因本轮改动失效。

### ④ 每帧可逆还原、位级相等、无累计偏移路径——**passed**

- `finally { Restore(modelNodes, originals); }` 仍在（`:431-434`）；`Restore` 用绝对 `originals[i]`；`RestoreExact` 逐分量 `BitEqual`（`:436-443`）；`SurfaceRestored` 重烘焙回基线（`:445-465`）。t92 未改这段。

### ⑤ fail-closed：拒绝码与 t76 §2.1 一致并给出组件清单——**passed**

- `CollectImpureComponentTypes`（`:356`）+ `FormatImpureComponents`（`:361`）仍在使用；`!pureNode → IMPURE_RENDER_NODE`（`Logic:218`）；空可见集合仍走 `NO_VISIBLE_SURFACE`。

### ⑥ 默认关闭与零副作用、失败不留半开——**passed**

- Enable 默认 false（`:87`）、`Camera.onPreCull` 成对挂/摘（`:240-258`）、`_ranOnce` 单次、目标离场有界重试、`Emit` 写失败改 `OUTPUT_FAILED` 并重建 JSON（`:860-878`）。t92 未触碰这些路径。

### ⑦ 口径诚实：离线不能证明 GATE_RESULT、报告不得说成闸门通过——**passed**

- t92 报告 §8 明确区分"自动化可证 5 项"与"仍需 1H+1G 5 项"，并主动写出"保留调用但丢弃结果离线不可区分"的残余边界（与我 D4 实测一致，见 §5）。
- 生产 note 由"没碰骨头由 Skeleton 位级快照 + collider + raycast 覆盖"改为准确表述（`:531-535`）：只声明 root / Third / Skeleton **根**三条变换未变，并明写"Skeleton 根不变不等于子骨骼没被动过——子骨骼 pose 覆盖不在本探针范围内，由后续轮处理"。t91-F1 的"note 冒充已覆盖"问题已消除。
- `single_frame_single_pose`、`epsilon_source=unspecified` 两条 note 仍在（`:530`、`:537`），未拿离线绿灯冒充 GATE_RESULT。

### ⑧ 自跑至少三条变异并逐字节还原——**passed**（本审查官跑 5 条，含 1 条自选）

见 §3；还原后源文件回到 `f376f2a1…`、DLL 回到 `388f7334…`。

## 3. 变异矩阵（本审查官实跑）

副本 `D:/Agent-工作目录/DevelopMyUNMultiplayerModAndModloader/_t93iso`（tar 复制，排除 `.git/bin/obj/.scratch/Release/publishi`），脚本 `.scratch/takeover-v05-20261106/_t93_mut.sh` + `_t93_mut.py`，日志 `D:/tmp/t93-mut.log`。每条：改 `Bug1GateProbe.cs` → `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` → 重建测试项目 → 覆盖两处 DLL → 跑 exe。

| # | 变异（文件:行） | DLL sha256 | 计数 | 红名单 |
| --- | --- | --- | --- | --- |
| M0 | 基线 | `388f7334…` | 782/784 | 仅 GBA01/NIL01（环境） |
| D1 | `Bug1GateProbe.cs:490` 删 root 边 → `RootUnchanged = true;` | `2e6fd16c…` | 781/784 | **Bug1-97**（"…≥3，实际 2"） |
| D2 | `:491` 删 Third 边 | `c88b3e02…` | 781/784 | **Bug1-97**（实际 2） |
| D3 | `:492` 删 Skeleton 边 | `79786969…` | 781/784 | **Bug1-97**（实际 2） |
| D4 | `:490` 保留 `TransformsEqual` 调用但丢弃结果（`bool _unusedRootEdge = …; RootUnchanged = true;`） | `6ccb8856…` | **782/784** | **无新增** |
| R0 | 还原 | `388f7334…` | 782/784 | 仅 GBA01/NIL01 |

还原核对：`Bug1GateProbe.cs` 回 `f376f2a18bc1fc60…`，DLL 逐字节回 `388f7334…`。

读法：D1/D2/D3 证明三条边**各自承重**（任一缺失即红，且消息给出边数），acceptance ③ 的"删任一项必红"成立；D4 暴露的是"调用保留、结果被丢弃"这一更强的回归形态离线抓不到（IL 只数调用边数），见 §5。

## 4. 与 t91 的逐项核销

| t91 项 | 本轮状态 |
| --- | --- |
| F1（high）Skeleton 快照从不参与判定；root/Third 无法逐项钉死；note 冒充覆盖 | **关闭**：三边独立比较 + 独立拒绝码 + 导出 + IL `>=3` + Bug1-96 逐边用例；note 已改为准确表述 |
| F1 必改建议 (a)+(b) | 均采纳，(c) 未被需要 |
| 残留 F6（单帧单姿态）/ F7（子骨骼 pose 哈希） | 仍未做，t92 未扩；note 已明写"子骨骼 pose 覆盖不在本探针范围" |

## 5. Finding（1 条 low，不阻塞）

### T93-F1（low）"保留调用但丢弃结果"的回归形态离线不可区分

- 现象：D4 把 `result.RootUnchanged = TransformsEqual(rootBefore, rootAfter);` 改成"调用照做、结果丢弃"，`Bug1-97` 的 `CountMethodCalls >= 3` 依然满足，`Bug1-96` 只测纯函数 `Decide`，于是 782/784 全绿。即"接线还在、语义被抽掉"这类回归不会被本轮任何用例抓住。
- 判断：这**不违反**本票 acceptance ⑧ 与 ③ 的字面判据（判据要求的是"删掉任一项→红"，已满足），t92 也已在报告 §8 主动声明该边界，因此不阻塞；登记的目的是防止下游把"782/784 全绿"读成"三条边语义被完全钉住"。
- 可选修法（若要更强）：给 `RunGate` 的收口段加一个最小 seam（注入 `Func<snapshot,snapshot,bool>` 比较器或可替换的 `Bug1GateInvariance` 组装器），再用一条行为用例断言"root 快照被替换→`ROOT_CHANGED`"；或在 IL 断言之外追加"`RootUnchanged` 的赋值来源必须是 `TransformsEqual` 的调用结果"的静态检查。当前探针只有 Unity 内可跑，纯离线做 seam 需先决定是否接受测试缝进入生产类。

## 6. 残留与 UNKNOWN（不得视为通过）

| 项 | 状态 | 归属 |
| --- | --- | --- |
| G 门数学结论（真实整体平移、不撕裂、epsilon 具体取值） | 未测量 | 1H+2G 真机 + 决策者定 epsilon |
| BakeMesh 在目标 Unity/Mono 的签名与可读性、采样上限是否够 | 未测量 | 真机 |
| 过滤后的真实可见集合（enabled/activeInHierarchy 之外仍有隐藏手段） | 未测量 | 真机 |
| collider/raycast 在真实玩家上的稳定性（raycast 可能命中地形） | 未测量 | 真机 |
| 白名单是否过严（纯节点工程件被误拒） | 未测量 | 真机 |
| t86-F6 单帧单姿态、t86-F7 子骨骼 pose 哈希 | 未做，note 已如实声明 | Architect 裁决是否降级/另票 |
| T93-F1（D4 残余） | 已知、不阻塞 | 若下游要求"语义也钉死"再开票 |

## 7. 边界声明

- 只判"是否满足需求契约与验收判据"，不评风格；未改主树任何文件（变异全部在仓库外副本 `_t93iso` 完成，R0 后源文件与 DLL 均回到 §1 哈希）。
- 782/784 为本审查官独立复跑，2 条失败为既有环境缺失（GBA01/NIL01 需 Unity 运行时），与本轮改动无关。
- **pass ≠ G 门成立**：本报告只证明"判定链与不变性门被真实接线、可失败、默认关闭、离线不放行"，不证明 0.10U 偏移下真实蒙皮几何能整体平移，也不构成"最小渲染插值路线可行"的结论；后者必须由 1H+2G 采集的 GATE.json + 画面核对决定。F6/F7 仍未满足 t76 §2.2 的字面要求，不得因本票 pass 视为已满足。

---

（自绑定：本行以上正文 11603 字节 / sha256 `7ad5a05c23816e05050e2a42a0054c066ee03212cadccfd16174669d9250cdcb`；本判定只对 §1 所列字节有效。复审时间 2026-11-08 02:50，主树字节未被本人修改。）
