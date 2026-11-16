# T87 工程轴复审（r1）— t85 G 门实验探针

- 审查对象：t85 交付的 G 门探针字节（**按 captain 通知的修订后新字节**，非 t85 报告表里的旧哈希）
- 审查人：reviewer-eng（attempt 35fd66e3-9236-4ad5-b6ff-5444c595c674）
- 范围：只做工程轴判定（取点/释放、写-还原时序、位级判等、白名单、目标解析与重试、默认关闭与失败回滚、变异承重、边界）；**不评需求**，不改任何源码
- 结论：**verdict = pass**，2 条 medium + 4 条 low；pass 边界见 §8

## 1. 字节锁（本人实测，与 captain 通知一致）

| 文件 | 行数 | 字节 | sha256 |
| --- | --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1GateProbe.cs` | 698 | 31237 | `c767683ee542271855f1c2cc68d6a260fa15d0f906fc87b660e7e1f2efac0da0` |
| `Platform/Diagnostics/Patches/Bug1GateProbeLogic.cs` | 399 | 18800 | `e75885dba05c989f408966085211cbb32dec402b46852e6601cae2bbc38e464c` |
| `WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs` | 365 | 19382 | `dc482c3aefbc2c89c78c561693e5e470ca311ba5731e7e786fd4fec506c6b434` |
| `WhitelistTests/Program.cs` | 1095 | 148845 | `999497d44413ba26fc31184a13b7a476d67af5456e7706e6a8c9197bc10081bb` |
| `Core/Registration/...PatchRegistrationDiagnosticsModules.cs` | — | — | `0e1abb04…`（与报告一致） |
| `SteamP2PFriends.csproj` | — | 27726 | `af991c70…`（与报告一致） |
| `bin/Release/SteamP2PFriends.dll` | — | 1527808 | `d49a8d63538976e32761b9c155fb00ca918a2df91821e70479315fa61c8a8cb0` |

- t85 报告表的 `1ebdb15c…` / `362c4385…` / `dada6aeb…` / `02eea8b4…` / `e8ab72a3…` 是**修订前**字节，以本表为准。
- 行数差异（682→698、383→399）由 captain 说明的两处诊断修订造成，不是实现漂移。
- 生产侧只新增这两个文件；`git status` 仍为 `??`（交付时的入库问题已在 Bug3/Bug1 前几轮记过，不重复开条）。

## 2. 三命令复跑（本人实测）

| 命令 | 退出码 | 结果 |
| --- | --- | --- |
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 0 | 0 警告 0 错误；DLL 复现 `d49a8d63`（确定性） |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | 0 | 0 错误；4 条既有警告 |
| `./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | 1 | **774/776 PASS (Failed: 2)**，失败仅 `GBA01`/`NIL01`（控制台缺 Unity 运行时）；`Bug1` 段 90/90，0 条 `FAIL [Runtime] Bug1` |

`SPF_DLL_SHA256=d49a8d63…` 与磁盘一致。**774/776 与 captain 的预期一致**（t85 报告里的 773/775 是修订前字节 + 无 Bug1-89 的旧口径）。

## 3. 变异（我在仓库外副本上自跑，逐字节还原）

副本 `../_t87mut`（与主仓同级，`..\Libs` 才解析；排除 `.git/.scratch/bin/obj/Release/publishi/Assets`），独立重建得 `d49a8d63`（与主树一致）。脚本 `D:/tmp/mut87.py`（字节级替换 + MATCH_COUNT 断言 + `.t87bak` 备份/还原）、`D:/tmp/drive87.sh`（apply→build→cp DLL→跑 exe→抓 FAIL 行→restore→rebuild→记哈希）。

| 变异 | 改动点 | 变异 DLL | 结果 | 新红用例 |
| --- | --- | --- | --- | --- |
| MV1 默认开启 | `Bug1GateProbe.cs` Bind `"Enable", false` → `true` | `87ce742bb3bc7df7` | 773/776 | **Bug1-82** |
| MV2 离线可用 | `Bug1GateProbeLogic.cs` `if (!measured)` → `if (false && !measured)` | `2e05c7217e4e37cf` | 772/776 | **Bug1-84, Bug1-88** |
| MV3 不滤 NaN/Inf | `Summarize` 的 NaN/Inf 过滤 → 恒假条件 | `dd807444637f2953` | 773/776 | **Bug1-86** |
| MV4 位级判等退化成 `==` | `BitEqual(float)` → `a == b` | `c56b16ae3c0c798a` | 773/776 | **Bug1-87** |
| MV5 组件清单恒为空 | `FormatImpureComponents` 首个判空 → 恒真返回 `components=(none)` | `8d9ad269550fa29d` | 773/776 | **Bug1-89**（新用例承重） |
| **MV6 白名单收集整体短路** | `FindImpureComponents` 首行 `if (nodes != null) return typeNames;` | `1534331181aabc1b` | **774/776 全绿** | 无 |
| **MV7 歧义映射回退分支置空** | `MapSelectionError` 三元 `ReasonTargetAmbiguous` → `null` | `7e709ad6a07e58ef` | **774/776 全绿** | 无 |
| **MV8 偏差比恒 0** | `DeviationRatio` 返回 `0d` | `b20056036b84511f` | **774/776 全绿** | 无 |

每条 restore 后重建均回 `d49a8d63`、三源文件回 `c767683e/e75885db/dc482c3a`；副本已删，主树与开工一致。

- t85 自报的 MV-1/MV-2（=我 MV1/MV2）红名单**完全一致**，哈希也一致（说明其变异文本与表格一致，这点比上一票好）。
- **MV6/MV7/MV8 三条全绿**是本轮最重要的发现：见 R16-01/R16-04/R16-03。

## 4. 逐项 acceptance

**① BakeMesh 取点与释放 — 通过（含 1 条 low）**
- `TryBakeSample`（`Bug1GateProbe.cs:524-569`）：每次调用 `new Mesh()`，`finally { UnityEngine.Object.Destroy(mesh); }`（:565-568）——**每次释放，无泄漏**；`Destroy` 延迟到帧尾，一轮测量最多 `4×rendererCount` 个临时 Mesh（一次性），可接受。
- 取点按顶点索引等距（:552-556），同一 mesh 同一 renderer 下**确定性**；`mesh` 被替换时用 `moved.Length != before.Length` 报 `MESH_CHANGED`（:345-346）。
- 上限是**软上限**：`step = max(1, V / max(1, samples))`，实际点数 = `ceil(V/step)`，可略高于配置值（V=1000/samples=64 → 67 点）；仍受 V 约束，不存在无界采样。JSON 的 `sample_count` 报的是真实 `stats.Count`，诚实（R16-05，low）。
- **注意**：BakeMesh 是 `SkinnedMeshRenderer` 专有；对 `MeshRenderer` 走 `Renderer` 白名单但不会被采到——代码只收集 `SkinnedMeshRenderer`（:296），而白名单放行 `Renderer` 泛型，二者口径不同但无害（`renderer_count==0` 时明确 `NO_VISIBLE_SURFACE`）。

**② 每帧写-还原的时序 — 通过**
- 写入点**唯一**：`Bug1GateProbe.cs:337` `nodes[n].localPosition = originals[n] + delta;`（仅 `Model_0/Model_1` 两个节点，两轴各写一次）。
- 还原点两处：轴循环内 `finally`（:350-353）+ 整个 `RunGate` 的外层 `finally`（:429-435，异常路径也恢复）。
- 还原后在**同一回调内**立即做位级校验（:357-369），再重烘一次表面比对（:371-386）；`root/third/skeleton` 快照前后比对（:309-311、:393-398）。
- 调用栈位置：`Camera.onPreCull → OnPreCull（一次性退订 :229）→ RunGate → 写/还原`——全部在主线程同一同步块内，动画（Update）与其它系统不可能插进写与还原之间；因此「同帧多处写同一节点」与「还原后被别人改」两类漂移在**该回调内**不成立。
- 未覆盖的残余风险：`Camera.onPreCull` 是否在 Unturned 渲染管线触发、以及 `BakeMesh` 在 `onPreCull` 内是否合法，属实机 UNKNOWN（见 §7）。

**③ 位级判等 — 通过**
- `BitEqual(float)`（`Logic:205-206`）用 `BitConverter.ToInt32(GetBytes(a))` 比较位模式；`double` 用 `DoubleToInt64Bits`（:208-209）。
- NaN：按位比较——同 payload 判等、不同 payload 判不等（位级语义正确）；±0：`0x00000000` vs `0x80000000` 判**不等**；非规格化数：按位精确比较，无 flush-to-zero。
- MV4 把 `BitEqual` 换成 `==` 只红 **Bug1-87**：说明该用例钉的是 `BitEqual` 的 `-0` 语义（`Test_BitEqualIsBitLevel` 断言 `!BitEqual(0.0f,-0.0f)`，`==` 会给 true）。**它证明的是 helper 语义，不证明生产还原路径真的用了它**——生产用法只能靠阅读（:360-362）确认，离线无法执行该路径（已被 Bug1-88 的「离线不得 PASS」兜住，不至于假绿）。

**④ 纯节点白名单 — 通过判定，但**收集侧零覆盖**（R16-01 medium）**
- 白名单：`Transform/Renderer/MeshFilter/Animator/Animation/LODGroup` 之外一律拒绝（`Bug1GateProbe.cs:511-518`）。`Renderer` 覆盖 `SkinnedMeshRenderer/MeshRenderer`；`Cloth`/`Rigidbody`/`Collider` 会被拒——对「纯渲染叶」这个实验前提是**正确的保守**，不应放宽（Cloth 会改蒙皮，Collider 会改物理边界，都污染 GATE 结论）。
- 误拒风险：Unturned 的 `Model_0/Model_1` 子树若挂了饰品/物理组件，会被判 impure 并 FAIL——但这是**实验的合法拒绝**，且现在会打印全部类型名（:288-292 → `FormatImpureComponents`），实机一次即可判定「放宽还是换方案」。
- 诊断格式（captain 特别要求逐行看）：`FormatImpureComponents`（`Logic:192-202`）——空/null → `components=(none)`；按出现顺序、仅对真实类型名去重（`Contains`）；null 元素 → `<null-name>`；`<read-failed:T>`（:500）与 `<null-component>`（:508）**不参与去重**，同一异常/同一 null 组件重复出现时会重复列出（cosmetic，不影响判读）；拼接全程 `StringBuilder`，除 OOM 外**不会抛**（在 FAIL 路径上安全）。采集端 `FindImpureComponents` 的 `GetComponentsInChildren<Component>` 包了 try/catch（:497-502）。
- **缺口**：MV6 把整个收集函数短路成空清单后 **774/776 全绿**——`pureNode` 这个门只被「格式化函数」的用例间接覆盖，真正的白名单判定（:288-292）没有任何用例能红。

**⑤ 目标解析与重试 — 通过（1 条 low）**
- 复用 `Bug1ProbeTargetSelector.SnapshotAll/Select`（`Bug1GateProbe.cs:140-142`），并 `MapSelectionError` 映射错误码（:143）；`clients[selection.SelectedIndex]` 之后再用 `ReferenceEquals` 判空（:150-155），不走 SDK 的 `SteamPlayerID ==`（上一轮的 NRE 教训已吸收）。
- 语义与 Bug1 一致：排除自己（`localSteamId`）、空选择器要求「唯一远端」、歧义报 `TARGET_PLAYER_AMBIGUOUS`；用例 `Test_TargetSelectionReusesSelectorSemantics` 直接调生产 selector 覆盖四种形态（含 null playerID 跳过）。
- 有界重试：`MaxArmRetries = 3`（:30），`TryRetry` 在 `_armRetries >= 3` 时 `RETRY_GIVEUP` + `UnhookRetry`（:188-197），成功也摘钩（:196）；`_armRetries` 只在 `TryRetry` 内自增，**不会无限重试**。
- low：`Provider.client` 为 null 时 `localSteamId=0`（:136-137），selector 无法排除「自己」；主机上 clients 里通常有自己 → 空选择器 + 仅自己在线时会误选自己并 FAIL `THIRD_UNRESOLVED`。属边缘（主机必有自己，且此时本来也没有远端可测）。

**⑥ 默认关闭与失败回滚 — 通过（1 条 medium）**
- 默认 `false` 且有用例钉住（`Test_ConfigDefaultsAreOffAndTenCentimetres` 走生产 `BindConfig`）；未开启时 `Initialize` 只绑 cfg + 订 `SettingChanged`，**不挂钩、不打日志**（唯一成本与注释一致，:23）。
- 失败不留半开：`Arm` 的三条拒绝路径都在 `HookCamera` 之前，不会留下相机钩子；`EnsureRetryHook` 的 `_retryHooked` 幂等，给弃时 `UnhookRetry`（:171-172）；`HookCamera` 成功才置 `_cameraHooked`（:210-212），`Disarm` 对称撤销（:127）。
- **medium（R16-02）**：`OnPreCull` 先把 `_ranOnce = true`（:231），再判 `_target` 为空就 `return`（:233-237），只打一条 WARN、**不写 GATE.json、不重试**。目标在 Arm 与首帧之间离场（或 `Third` 尚未生成）时，本进程再也测不了、且没有任何结果产物——操作者只看到一行 WARN，容易误判为「已跑过但没结果」。

**⑦ 交付口径 — 通过**
- 774/776、Bug1 90/90、DLL `d49a8d63`，与 captain 预期一致。
- `SteamP2PFriends.csproj` 只新增两条 Compile（`Bug1GateProbe.cs`、`Bug1GateProbeLogic.cs`），无重排、无新依赖；注册点 `PatchRegistrationDiagnosticsModules.cs:84` 在既有 try/catch 内（:88 记 Error）。
- 边界：Bug3 核心 `GroupStateRecoveryCore.cs` 仍 `7a82de8e`；Bug1 既有四文件 `9eeee011/bb53d368/79ef2f47/…` 未动；`Release/`、`publishi/` 在 00:00 后**零写入**（`find -newermt` 空）。

## 5. Findings

| id | severity | 位置 | 问题 | 最小修法 |
| --- | --- | --- | --- | --- |
| R16-01 | medium | `Bug1GateProbe.cs:288-292`、`:491-522` | 纯节点门的**收集/判定**零覆盖：MV6 短路成空清单后全绿。实验结论「纯渲染叶可行」依赖这个门，门被误改/误删时没有任何自动化会红（只有格式化函数被测）。 | 把谓词抽成纯函数 `IsWhitelistedComponentType(string typeName)`（吃 `Type.Name`），对 6 类放行 + `Cloth/Rigidbody/BoxCollider/CapsuleCollider` 拒绝各写一条离线断言；Unity 遍历保留不测。 |
| R16-02 | medium | `Bug1GateProbe.cs:231-237` | 目标在首帧前离场 → `_ranOnce=true` 后直接返回，只 WARN，**无 GATE.json、无重试**；本进程永久放弃。 | 该分支改为 `Emit(Fail(result, ReasonTargetNotFound, "target gone before measure"))`（或 `ReasonTargetGone`），并让 `_ranOnce` 在该分支复位以便重新 Arm。 |
| R16-03 | low | `Bug1GateProbeLogic.cs:167-172`、`Bug1GateProbe.cs:406` | `DeviationRatio` 生产零覆盖（MV8 恒 0 全绿）；它只影响 JSON 导出，`Decide` 内部另行计算，**不会翻转 PASS/FAIL**，但会让 GATE.json 与裁决不一致。 | 加一条 `DeviationRatio(0.10, Stats(0.13))==0.30` 的离线断言（或直接在 `Decide` 里复用该函数，消除双算）。 |
| R16-04 | low | `Bug1GateProbeLogic.cs:178-186` | `MapSelectionError` 的 `Ambiguous → ReasonTargetAmbiguous` 三元分支实际不可达（selector 总会先填 `ErrorCode`），MV7 置空全绿；用例只覆盖 `ErrorCode` 路径。歧义语义全靠 selector 的字符串。 | 在用例里造一个 `ErrorCode=null && Status=Ambiguous` 的 `Bug1TargetSelection` 钉该回退；或删掉三元、显式要求 `ErrorCode` 非空。 |
| R16-05 | low | `Bug1GateProbe.cs:552-556` | 采样数是软上限（`ceil(V/step)` 可高于配置值）；当前规模无害，但 `Samples` 配置项读起来像硬上限。 | 用「取前 N 个按步长命中的索引」或 `for (i=0;i<samples && i*step<V;i++)` 精确化；或注释写明「≤ V 的近似值」。 |
| R16-06 | low | `Bug1GateProbe.cs:446-468` | `Emit` 先 `BuildJson` 再做写盘；写盘失败时把 `result.Status` 改成 FAIL，但日志里拼的仍是**改前**的 `json`（可能出现 `GATE_RESULT=FAIL` 后面跟着 `"gate_result":"PASS"`）。 | 写盘失败分支里重建一次 `json = BuildJson(result)`，或将 `json` 延后到 try 之后再拼。 |

## 6. 已确认 / 待验证 / 已否定

- **已确认（离线可证）**：默认关闭；0.10U/64 默认值；五种契约原因码 + 补充码；JSON 字段集；分位数与 NaN/Inf 过滤；`BitEqual` 的 ±0/位级语义；离线必 UNKNOWN；组件清单格式（含 `(none)`）；selector 语义复用；六条硬变异（MV1-MV5）各自只红对应用例。
- **待验证（只能实机）**：`Camera.onPreCull + BakeMesh` 在 Unturned 里是否按预期触发/可读；真实蒙皮是否整体平移（而非仅骨骼局部）；`Model_0/Model_1` 子树是否真的白名单干净（实机若 FAIL，用 `components=…` 判读）；`Collider`/`Raycast` 在偏移-还原窗口内是否稳定；1H+1G 下的 `GATE_RESULT` 本身。
- **已否定/已澄清**：t85 报告的旧哈希与旧计数（773/775）对应修订前字节，作废；`RESTORE_MISMATCH` 不会因 NaN 误报（位级比较对 NaN payload 严格，`localPosition` 写回是同值拷贝，不产生新 NaN）。

## 7. 复现命令

```bash
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe
python D:/tmp/mut87.py MV<n>   # 需在副本 ../_t87mut 上执行
bash D:/tmp/drive87.sh MV1 MV2
```

## 8. pass 边界

- 本票判的是「探针**能不能上实机去测**」：代码路径、边界、失败语义与变异承重成立，故 pass。
- **不等于** G 门已被验证：`GATE_RESULT=PASS` 只在真实 1H+1G 下有意义，离线永远 UNKNOWN（Bug1-88 钉死）。
- **不等于** Bug1 已修复：本探针只做闸门实验，不实现任何插值；`Model_0/1` 位移是否可作为渲染补偿，取决于实机六项待验。
- R16-01/R16-02 不阻断上机，但上机前应知道：**纯节点门本身没有自动化保护**；目标首帧前离场时**不会留下 GATE.json**。

<!-- self_sha256(body)=704adc93c32836e423ddc1cc7960cbc748b819fc7d68f33370600711b49018e0 -->
