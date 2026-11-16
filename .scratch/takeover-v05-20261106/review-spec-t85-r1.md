# T86 规格轴复审：G 门实验探针（被审 t85）

审查官：reviewer-spec（规格轴，独立于 t85 实现者）
审查时间：2026-11-08 00:36–01:10（Asia/Shanghai）
判定基准：`.scratch/takeover-v05-20261106/architecture-decision-t76-*.md` §2（闸门语义）、t86 派工 acceptance 8 条
审查方式：只读主树 + 仓库外副本 `_t86iso` 做变异；**未修改主树任何字节**；未重跑 t85 自述的构建（Captain 已复跑，本轮独立复跑仅在副本内进行）

---

## 0. 结论摘要

**verdict = needs_revision（status=failed）**

探针的**骨架是对的**：可见几何确实取自 `BakeMesh` 蒙皮后顶点转世界坐标、还原用绝对值写回且有 finally 兜底、纯节点门与组件清单 fail-closed、默认关闭、离线不得 PASS 这条也被 Bug1-88 真钉死（我删掉该门后 Bug1-88 变红）。

但它在两处**没有兑现 t76 §2 与派工 acceptance**：

1. **判定阈值与 t76 §2.2 的判据不同**：t76 要求「每个被画 renderer 的整个快照/已证明覆盖的表面点组满足 `max||(p_after−p_before)−delta|| ≤ epsilon`」；实现改为「全样本中位数偏离 ≤15% 且 P10 ≥ 85%×offset」，且不按 renderer 分别判定、P90 计算后不参与判定。后果：约 10% 表面点不动、或单个 renderer 撕裂，仍可判 PASS。
2. **四项不变性在生产侧零用例绑定**：我把 `RaycastUnchanged`、`CollidersUnchanged`、`Transform` 位级比较分别改成恒真，三次都 **774/776 全绿**；把生产纯节点门的结果清空也全绿。派工 acceptance ③「删掉任一项时对应用例必须红」不成立。

另有四项中等问题（隐藏 renderer 计入可见表面、读快照异常时 fail-open、缺 t76 §2.1 要求的 profile 指纹、单一姿态单帧）。

**本判定不构成「G 门不成立」的结论**：G 门是否成立只能在真实 1H+1G 下测（本轮与 t85 均未测）。

---

## 1. 审查对象与字节绑定

| 文件 | 字节 | sha256 | mtime |
| --- | ---: | --- | --- |
| `Platform/Diagnostics/Patches/Bug1GateProbe.cs` | 31237 | `c767683ee542271855f1c2cc68d6a260fa15d0f906fc87b660e7e1f2efac0da0` | 11-08 00:38:32 |
| `Platform/Diagnostics/Patches/Bug1GateProbeLogic.cs` | 18800 | `e75885dba05c989f408966085211cbb32dec402b46852e6601cae2bbc38e464c` | 11-08 00:40:15 |
| `WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs` | 19382 | `dc482c3aefbc2c89c78c561693e5e470ca311ba5731e7e786fd4fec506c6b434` | 11-08 00:41:01 |
| `WhitelistTests/Program.cs` | 147959 | `999497d44413ba26fc31184a13b7a476d67af5456e7706e6a8c9197bc10081bb` | — |
| `SteamP2PFriends.csproj` | 27575 | `af991c70de6bd822de0fd50d4a7f74496c52f2156e7377dacdd2a294f16d912d` | — |
| 交付 DLL `bin/Release/SteamP2PFriends.dll`（两处同名同哈希） | 1527808 | `d49a8d63538976e32761b9c155fb00ca918a2df91821e70479315fa61c8a8cb0` | 11-08 00:49:24 |

注册点：`Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs:84` `Bug1GateProbe.Initialize()`；`:88` 失败仅 `RoleLogger.Error`（不 throw）。

**声明**：本判定只对上述字节有效。Captain 于 00:44 锁定了该批次（此前 `e8ab72a3…` 批次作废），我重新取样核对了全部 6 个哈希后才开始判定；副本内每次变异后都用重新构建的 DLL 跑测试，并逐字节还原（§4 末尾给出还原证明）。

---

## 2. t76 §2 闸门语义逐条对照

| t76 §2 条款 | 实现位置 | 判定 |
| --- | --- | --- |
| §2.1 两节点及子树不得含 Collider（含 disabled）/Rigidbody/Joint/CharacterController/骨骼判定节点/AudioSource/武器/相机/gameplay 组件，未知组件直接不准入 | `Bug1GateProbe.cs:288` 调 `FindImpureComponents`（`:491`），白名单在 `:512` 只放行 `Renderer`，其余类型名全部计入 → `PureNode=false` → Logic `Decide` 返回 `FAIL/IMPURE_RENDER_NODE` | **通过**（fail-closed，未知组件即拒绝） |
| §2.1 拒绝时给出足以判断的组件清单 | `Logic:192-202 FormatImpureComponents` → `components=A,B`；空为 `components=(none)`；`failure_detail` 落 GATE.json | **通过**（Bug1-89） |
| §2.1 可见表面集合至少含一个有非空 mesh/顶点的 renderer，空集合不算通过 | `:296` `GetComponentsInChildren<SkinnedMeshRenderer>(true)`；`:307` 空则 `FAIL/NO_VISIBLE_SURFACE` | **部分通过**：未过滤 `enabled`/`activeInHierarchy`（见 F3） |
| §2.1 记录两 renderer 完整路径/父链/mesh/material/bones/rootBone/Collider/gameplay 读取者，形成 profile 指纹 | GATE.json 仅有 `target`、`renderer_count`（`Logic:212-249`），无路径/父链/mesh/material/bones | **不通过**（F5） |
| §2.2 分别对 X 与 Z 施加 0.10U 并还原 | `:337` `localPosition = originals[n] + delta`，两轴各一轮；`DefaultOffsetU=0.10`（`Logic:18`） | **通过** |
| §2.2 至少两个姿态/朝向 | 仅 `OnPreCull` 单帧测一次 | **不通过**（F6） |
| §2.2 比较实际蒙皮后的世界表面点，而非 transform.position 或 bounds 中心 | `:524 TryBakeSample` → `:553 localToWorld.MultiplyPoint3x4`，位移量来自顶点差；判定只用 `magnitudes` | **通过**（无 transform/bounds 旁路） |
| §2.2 测试前后原骨骼 pose 哈希必须相同 | 只做 root/Third/Skeleton 的 position+rotation 位级快照（`:596`），**无骨骼 pose 哈希** | **不通过**（F7，t85 §6 已声明为有意偏离） |
| §2.2 每个 renderer 的快照或覆盖点组满足 `max||Δp−delta|| ≤ epsilon` | Logic `:150-165`：`deviation=|Median−offset|/offset > 0.15` → 失败；`P10 < offset×0.85` → `PARTIAL_SURFACE_MOVED`；**无 max 判据、无 per-renderer、无 epsilon** | **不通过**（F1，本轮最关键） |
| §2.2 物理 root/原骨骼/Collider 矩阵与 enabled 不变 | `:390` `CollidersEqual`（`:658`，比较 center/size/enabled `:628`）；`:396-398` `TransformsEqual`（`:686`） | 实现了，但**无用例绑定**（F2） |
| §2.2 受控射线结果不变 | `:648` `Physics.Raycast(..., 3f)` → `:392` `RaycastsEqual`（`:679`，比较 hit/point/distance） | 实现了，但**无用例绑定 + 异常 fail-open**（F2/F4） |
| §2.3 还原后表面回到基线 epsilon 内 | `:375` 还原后再次 `TryBakeSample`，`SurfaceRestored` 用 `RestoreEpsilonU=1e-4`（`Logic:27`） | **通过** |
| §2.3 无部分部件撕裂；不能只挑一个动了的中心点 | 每 renderer 采 `Samples`（默认 64）点，但判定用分位数而非 max → 撕裂可被中位数掩盖 | **不通过**（与 F1 同源） |
| §2 末 静态材料不得签 PASS | `Decide` 在 `stats.Count<=0` 或偏离超限时返回失败；离线 `measured=false` → `UNKNOWN` | **通过** |

---

## 3. 派工 acceptance 逐条判定

| # | 判据 | 判定 | 依据 |
| --- | --- | --- | --- |
| ① | BakeMesh 真实顶点 + 偏差比阈值 + p10/p50/p90 在生产路径 | **failed** | BakeMesh/世界点/分位数确在生产路径（`:524`/`:553`/`Logic Summarize`），GATE.json 也导出 p10/p50/p90（`Logic:225-227`）；但**判定阈值语义偏离 t76 §2.2 的 max≤epsilon**（F1），且 P90 只导出不参与判定。 |
| ② | 偏差比来自蒙皮后世界坐标差，无 transform 旁路 | **passed** | `TryBakeSample` 用 `BakeMesh` + `localToWorld`；`Transform` 快照只作不变性门，不参与位移量；全文件无 `bounds.center` 参与判定。 |
| ③ | 不变性四项存在且删任一项用例必红 | **failed** | 见 §4：MA（raycast→true）、MB（collider→true）、MC（transform 位级→true）三次均 774/776 全绿；MF（生产纯节点门清空）亦全绿。 |
| ④ | 每帧可逆还原 + 位级相等 + 无累计偏移路径 | **passed** | `:350-352` 每个 delta 轮次后 finally 写回 `originals[n]`；`:429-434` 外层 finally 再兜底（`n < originals.Count` 保守）；写回一律用绝对值 `originals[n]`，不累加；`TransformsEqual` 用 `BitEqual`（`Logic:205-208`，`BitConverter.ToInt32`）比位。 |
| ⑤ | fail-closed 与 §2.1 一致 + 组件清单 | **passed**（含限定） | `:512` 非 Renderer 一律 impure；未知组件名照样拒绝并进 `failure_detail`。限定：射线/碰撞快照读失败的 fail-open 属另一条（F4）。 |
| ⑥ | 默认关闭与零副作用 | **passed**（含限定） | `Logic:72` `Bind(...,"Enable", false, ...)`；未开启不挂 `Camera.onPreCull`（`:183` 守卫）、不产 GATE_RESULT 日志；`Initialize()` 只建 cfg + 订 `SettingChanged`（`:51-53`）。限定：arm 失败会留一个**有界**重试钩子（`MaxArmRetries=3`，`:30`），用尽或成功即摘钩——这是设计，不是半开泄漏，但属"开启失败后仍有短时残留钩子"，已在 t85 §3 披露。 |
| ⑦ | 口径诚实：离线不得 PASS | **passed** | `Bug1-88` 直测 `Decide(measured:false)` → `UNKNOWN/NOT_MEASURED`；我删掉该门（ME）后 Bug1-84+Bug1-88 变红（§4）。t85 报告 §7 明写「G 门是否成立仍未测量」，未见把离线绿灯写成闸门通过。 |
| ⑧ | 自跑 ≥3 条变异并逐字节还原，写明文件:行与预期红名单 | **passed** | 我自跑 7 条（6 条施加成功 + 1 条无效轮记录在案），§4 给全表；还原后 DLL 与源码逐字节回送审值。 |

**8 条中 3 条 failed（①②③），5 条 passed（含 2 条带限定）。**

---

## 4. 变异证据（本审查官自跑，仓库外副本 `_t86iso`）

方法：`tar` 复制主树（排除 `.git`/`bin`/`obj`/`.scratch`）→ 每条变异 `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` + 重建测试项目 + 覆盖 `WhitelistTests/bin/Release` 两处 DLL + 跑 exe；记录 DLL sha256、`Final Result` 与红名单；每条先恢复原始字节再施加下一条。脚本 `D:/tmp/t86_mut.sh`、`D:/tmp/t86_mut2.sh`，日志 `D:/tmp/t86-mut.log`、`D:/tmp/t86-mut2.log`。

| # | 变异（文件:行） | 变异后 DLL sha256 | 结果 | 新增红 |
| --- | --- | --- | --- | --- |
| M0 | 基线（送审字节） | `d49a8d63538976e32761b9c155fb00ca918a2df91821e70479315fa61c8a8cb0` | 774/776 | 无（仅 GBA01/NIL01 环境失败） |
| MA | `Bug1GateProbe.cs:392` `result.RaycastUnchanged = RaycastsEqual(...)` → `= true` | `09d543bb9ea4b07b7e257dabcff6ebbdcc2d25a6cb0add0e710d0deaaa4c8af0` | **774/776（全绿）** | **无** ⇒ 射线不变性无用例 |
| MB | `:390` `result.CollidersUnchanged = CollidersEqual(...)` → `= true` | `cb1e1c9561b257236c85c335dea1c37c0e85d1627425689ba233d4180a5c25ba` | **774/776（全绿）** | **无** ⇒ Collider 不变性无用例 |
| MC | `:396-398` 三个 `TransformsEqual(...)` → `bool transformsUnchanged = true;` | `eb2112a43895708812cb77f65f66484bb9c42ec7ab67d693d115b898a8dd8520` | **774/776（全绿）** | **无** ⇒ root/Third/Skeleton 位级快照无用例 |
| MD | `Bug1GateProbeLogic.cs:157` 的 `P10 < offset×(1−ratio) → PARTIAL_SURFACE_MOVED` 整块删除 | `80a9e0744c7fbe3a5242da293e1fde55ab6663127a9af4fa7e8a9443a82295b8` | 773/776 | Bug1-84（Decide 分支映射） |
| ME | `Logic:115` `if (!measured)` → `if (measured && !measured)` | `bf5d1f9741a5848b5cabbb8407049a353f2cc6404ab81149e60149900dfdcefb` | 772/776 | Bug1-84、**Bug1-88** ⇒ 离线 PASS 门是承重的 |
| MF | `:491` `FindImpureComponents` 返回前清空 `typeNames` | `822862bbe80e7cd7a48b77e6994054afe804e715685cc87efb7a0ce731012480` | **774/776（全绿）** | **无** ⇒ 生产纯节点门无用例（Bug1-84 直测 Decide、Bug1-89 只测格式化函数） |
| R0 | 还原 | `d49a8d63…`（逐字节回送审值） | 774/776 | — |

无效轮说明（不计入证据）：MC 首轮用旧批次的行文本定位，替换未命中（脚本提前退出，未构建未运行）；改用 `_t86_mut2.sh` 重跑得到上表结果。ME 首版写成 `if (false)`，被 `CS0162` 判错（本仓警告即错误），改为 `measured && !measured` 后成立——两者都记录在此，不当作"变异失败"。

还原证明：副本内 `Bug1GateProbe.cs` 回 `c767683e…`（31237B）、`Bug1GateProbeLogic.cs` 回 `e75885db…`（18800B）、重建 DLL 逐字节回 `d49a8d63…`、跑出 774/776（日志 R0 行）。主树全程未被写入（副本与主树路径不同，且主树六文件哈希在审查前后一致）。

**结论**：生产侧的四个"不变性"（射线、Collider、三个 Transform、纯节点门）目前全部**只能靠代码阅读相信**——没有任何一条离线用例会因它们被删而变红。这不是"测试少一条"，而是 acceptance ③ 的判据本身没有成立。

---

## 5. Findings

### T86-F1（high）判定阈值偏离 t76 §2.2 的 max≤epsilon

- **位置**：`Bug1GateProbeLogic.cs:150-165`（`Deviation = |Median−offset|/offset`，`:152` 超 `MaxDeviationRatio=0.15` 判失败；`:157` `P10 < offset×0.85` 判 `PARTIAL_SURFACE_MOVED`）；`Bug1GateProbe.cs:390-398`（统计全 renderer 合并成一组 magnitudes）。
- **问题**：t76 §2.2 的判据是「**每个被画 renderer** 的整个快照或已证明覆盖的表面点组，**均须**满足 `max||(p_after−p_before)−delta|| ≤ epsilon`」。实现用全样本中位数 + P10 分位，等价于允许约 10% 表面点不动、且允许单个 renderer 整体撕裂，只要多数点动到位就判 PASS。`P90` 算了、导出了（`Logic:227`），但**不参与判定**——恰恰是最能揭撕裂的那个量没进门。
- **后果**：G 门最需要否掉的形态（部分部件不动/撕裂）在实现里可以 PASS，与 §2 末「只部分顶点动 → 本最小路线失败」直接冲突。
- **requiredFix**：按 renderer 分别汇总，输出每个 renderer 的 `max/p10/p50/p90/deviation`；判定改为「每个 renderer 的 max 偏差 ≤ epsilon（epsilon 由 Architect 给出，或沿用 `RestoreEpsilonU` 但需书面确认量纲），且覆盖率 100% 采样点参与」；若 Architect 认为量化阈值不可行，须**书面修订 t76 §2.2** 并说明为何允许部分不动，不得由实现单方面改写判据。

### T86-F2（high）四项不变性在生产侧零用例绑定

- **位置**：`Bug1GateProbe.cs:390`（collider）、`:392`（raycast）、`:396-398`（transform）、`:288/:491/:512`（纯节点门）；测试侧 `WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs` 的 7 条只覆盖 `Logic.Decide`/`Summarize`/`FormatImpureComponents`/`BitEqual`/config/目标选择，**没有一条进 `RunGate`**。
- **证据**：MA/MB/MC/MF 四条变异全绿（§4）。
- **requiredFix**：把三个比较函数（`TransformsEqual`/`CollidersEqual`/`RaycastsEqual`）与 `FindImpureComponents` 的判定部分下沉到 `Bug1GateProbeLogic` 并写可失败单测；再加一条**生产接线**断言（`IlContractProbe.CountMethodCalls(RunGate, ..., "CollidersEqual"/"RaycastsEqual"/"TransformsEqual") >= 1`，或对 `FindImpureComponents` 的返回值做 seam 注入）。验收口径应是「把任一比较改成恒真即红」，不是「函数存在」。

### T86-F3（medium）隐藏/未启用 renderer 被当作可见表面

- **位置**：`Bug1GateProbe.cs:296` `GetComponentsInChildren<SkinnedMeshRenderer>(true)`（`true` = 含 inactive），`:299-306` 未检查 `renderer.enabled` 与 `activeInHierarchy`。
- **问题**：t76 §2.1 明确「当前空/隐藏的可选 Model 单列，不充作成功样本」。现在隐藏 renderer 也进 `renderers` 并贡献采样点，可能把「实际看不见的网格动了」算成 G 门通过。
- **requiredFix**：收节点时过滤 `renderer.enabled && renderer.gameObject.activeInHierarchy`，并把被排除的 renderer 记进 GATE.json（名字 + 原因），让"测了哪些渲染器"可审计。

### T86-F4（medium）快照读取失败时 fail-open

- **位置**：`Bug1GateProbe.cs:648` `Physics.Raycast(...)` 与 `ReadIntersection` 的 catch 返回空快照；`:658 CollidersEqual` / `:679 RaycastsEqual` 对「两侧都空」返回 true。
- **问题**：真实场景里 raycast 抛错或 collider 读取异常时，`colliders_unchanged=true` / `raycast_unchanged=true`，与"未变化"不可区分——恰是 t76 要求 fail-closed 的方向。
- **requiredFix**：快照带 `Available` 标志；任一侧不可用 → `UNKNOWN/UNAVAILABLE_*`（不得 PASS，也不得 FAIL 成"变了"）；并在 GATE.json 写 `collider_available/raycast_available`。

### T86-F5（medium）缺 t76 §2.1 要求的 profile 指纹

- **位置**：`Bug1GateProbeLogic.cs:212-249`（GATE.json 契约）只有 `target`、`renderer_count`、各统计量与 before/after 快照；`Bug1GateProbe.cs:448` 写 GATE.json。
- **问题**：t76 §2.1 要求记录两个 renderer 的完整路径、父链、mesh/material、bones/rootBone、Collider 及 gameplay 读取者绑定。现在**事后无法回答"这次测的是哪套模型/材质/骨骼"**——同一 player 换装、换模型、换 UMA/atlas 后，前后两份 GATE.json 无法比对，也无法排除"测的是隐藏备用模型"。
- **requiredFix**：至少记录每个参与 renderer 的层级路径、`sharedMesh.name`/`sharedMaterials[].name`、`bones.Length`、`rootBone` 路径、所在节点上的 Collider 类型与 enabled；写入 GATE.json 并对它们取一个 `profile_hash`。

### T86-F6（low）只有单帧单姿态

- **位置**：`Bug1GateProbe.cs:319-434`（一次 `RunGate` 内完成 X/Z 两轴测量）。
- **问题**：t76 §2.2 要求「至少两个姿态/朝向」。X/Z 是轴，不是姿态；单一姿态下"某个部件不动"可能只是该朝向恰好不可见。
- **requiredFix**：在真实 1H+1G 流程里跑至少两个朝向（例如面向相机/背向相机，或站姿与蹲姿），或由 Architect 书面降低要求。

### T86-F7（low）骨骼 pose 哈希缺失（已声明偏离）

- **位置**：`Bug1GateProbe.cs:596 SnapshotTransform` 只取 position+rotation；全文件无 bones pose 采样。
- **问题**：t76 §2.2 明文「测试前后原骨骼 pose 哈希必须相同」。t85 §6 已写明"有意不做全骨骼 pose 哈希"，但这是**对裁决的偏离**，需要书面追认；否则 root/Third/Skeleton 快照相同并不能排除"某个骨骼被单独挪过又挪回"或"骨骼整体漂移但 root 未动"。
- **requiredFix**：补 `bones[i].localPosition/localRotation` 的位级哈希（采样或全量），或由 Architect 在 t76 追认替代判据与理由。

---

## 6. 明确判 PASS 的部分（不得因上述 findings 被误读为全盘否定）

1. **可见几何确实来自蒙皮后顶点**：`TryBakeSample`（`:524`）用 `BakeMesh` 得到网格顶点，再 `localToWorld.MultiplyPoint3x4`（`:553`）转世界坐标；位移量 `magnitudes` 直接来自该点集，`transform`/`bounds.center` 均未参与 displacement 判定。这是最容易造假的一环，实现是诚实的。
2. **还原是可逆且位级的**：每个 delta 轮次 `finally` 用绝对值 `originals[n]` 写回（`:350-352`），外层再兜底一次（`:429-434`）；`TransformsEqual` 用 `BitConverter.ToInt32` 逐位比较（`Logic:205-208`）。**没有累计偏移路径**——写回从不 `+=`。
3. **纯节点门方向正确**：非白名单组件一律 impure（`:512`），未知组件不会因为"不认识"被放过；失败时给出组件类型清单（`Logic:192-202`、Bug1-89）。
4. **默认关闭、无静默副作用**：cfg 默认 `false`（`Logic:72`）；未开启不挂 `onPreCull`、不写 GATE.json、不打 GATE_RESULT；`Initialize()` 只做 cfg 绑定与 `SettingChanged` 订阅。arm 失败走有界重试（3 次）并按条件摘钩，属设计而非泄漏。
5. **离线口径诚实**：Bug1-88 钉死「离线全绿也不得 PASS」，我删门后它变红（ME）；t85 报告 §7 写明「G 门是否成立仍未测量」，并把六项实机待验列全。没有把离线通过说成闸门通过。

---

## 7. 仍需真实 1H+1G 的 UNKNOWN（本票与 t85 都不能关闭）

| # | 未验证项 | 为什么离线测不了 | 归属 |
| --- | --- | --- | --- |
| U1 | `SkinnedMeshRenderer.BakeMesh` 在 Mono/发布版运行时是否可读、返回的是否为蒙皮后顶点 | 离线无 Unity 渲染器，探针根本不会 `RunTarget` | 真机 1H+1G |
| U2 | 真实角色模型整体刚性平移时，表面点是否真按 `delta` 平移 | 需要真实蒙皮网格与骨骼 | 真机 |
| U3 | 骨骼 pose 是否整体不变（当前只有 root/Third/Skeleton 快照） | 同上；见 F7 | 真机 + Architect 追认 |
| U4 | collider/raycast 在真实关卡里的逐字段稳定（含地形、水、其它玩家） | 离线无物理场景 | 真机 |
| U5 | 纯节点白名单在真实 UMM/插件环境是否过严（NRE、其它 mod 挂组件） | 离线无第三方组件 | 真机 |
| U6 | GATE_RESULT 本身（整体结论） | 只能在真实场景产出 | 真机 |
| U7 | 可见/隐藏 renderer 的实际集合（F3 修好后才能确认测的是可见部件） | 依赖真实模型挂载 | 真机 + F3 修复 |

**注意**：即使真机跑出 `GATE_RESULT=PASS`，在 F1（阈值语义）、F5（profile 指纹）修好之前，也无法证明"每个被画 renderer 都整体平移"——本轮 findings 不是实机可替代的。

---

## 8. 边界声明

- 本票只判「探针是否忠实执行 t76 闸门规格」，**不判 Bug1 根因、不判 G 门是否成立**；`pass` 也不等于闸门通过，本轮为 `needs_revision` 更是如此。
- 我未修改主树任何文件；副本 `_t86iso` 在仓库外，收尾时删除。
- 我未重跑 Captain 的三命令（其已复跑 774/776），本轮独立复跑只在副本内进行，用作变异对照与还原验证。
- 报告中每个 `文件:行` 均在当前字节（§1 哈希）上 grep 复核过；若后续字节再变，本判定失效，需重新绑定。


---

自绑定：本文件正文（本节之前）21789 B / sha256 `8912126adeb7c0ec3d783aaa5923ad22adfb2059cbdfd2f3a03ed54cf44b12e1`；追加本行后全文以提交时记录为准。审查对象字节见 §1，仅对那些哈希有效。
