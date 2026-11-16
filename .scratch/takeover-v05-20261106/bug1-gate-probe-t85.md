# T85 · G 门实验探针（Model_0/1 纯渲染叶偏移）实现报告

- 任务：t85 / attempt 2 / `eb3f9921-64aa-4e5f-8a28-83848f0f5899`
- 规格来源：`.scratch/takeover-v05-20261106/bug1-minimal-render-interpolation-t76.md` §2（准入门 G 三段）
- 结论口径：**本票只做闸门实验与测量，不实现插值。** `GATE_RESULT` 只在真实 1H+1G 下有效；离线一律 `UNKNOWN`，不得写成「可行」或「G 门通过」。

## 1. 范围与实现位置

| 交付项 | 实现位置 | 说明 |
| --- | --- | --- |
| 独立 cfg 节 `[Bug1Gate]`、默认关闭 | `Platform/Diagnostics/Patches/Bug1GateProbe.cs:60-82` | `SteamP2PFriends.Bug1Gate.cfg`，键 `Enable`(false)/`TargetPlayer`("")/`OffsetU`(0.10)/`Samples`(64)；`BindConfig` 抽成 internal，离线用例走同一条 Bind 路径 |
| 未开启零日志、零帧回调 | `Bug1GateProbe.cs:52-57`、`:150-176` | 未开启时只做 cfg 绑定与 `SettingChanged` 订阅；`Camera.onPreCull` 仅在 `Arm` 成功后订阅，且测完立刻退订 |
| 只写 `Model_0/1` 的 `localPosition` | `Bug1GateProbe.cs:337-372`、`:398-409` | `nodes` 只收 `Third/Model_0`、`Third/Model_1`；从不触碰 `Third`/`Skeleton`/`movement` |
| 本帧内可逆还原 + 位级相等 | `Bug1GateProbe.cs:341-372`、`:374-390` | 每次施加后 `finally` 还原；随后用 `BitEqual(float,float)` 逐分量比对位模式，失败即 `RESTORE_MISMATCH` |
| 可见几何（BakeMesh 真实蒙皮） | `Bug1GateProbe.cs:520-576` | 反射找 `SkinnedMeshRenderer.BakeMesh(Mesh,bool)`（回退单参），`mesh.vertices` → `renderer.transform.localToWorldMatrix.MultiplyPoint3x4`；**不使用 transform/bounds 代替** |
| 刚性位移门（X/Z 各 0.10U） | `Bug1GateProbe.cs:337-372`、`Bug1GateProbeLogic.cs:105-160` | X、Z 各施加一次并各自还原；偏差比与 p10/p50/p90 判定在 `Decide` |
| 不变性（root/Third/Skeleton/Collider/Raycast） | `Bug1GateProbe.cs:311-320`、`:392-408`、`:596-682` | 前后快照逐字段 `BitEqual`；collider 记录 name/type/enabled/center/size；raycast 记录命中体与距离 |
| 纯节点门 | `Bug1GateProbe.cs:487-518` | 白名单：Transform/Renderer/MeshFilter/Animator/Animation/LODGroup；其余组件（Collider/Rigidbody/Joint/CharacterController/AudioSource/Camera/…）一律 `IMPURE_RENDER_NODE` 并报出类型名 |
| fail-closed | `Bug1GateProbe.cs:220-260`、`:322-336`、`:430-470` | 目标/Third/Model/表面/BAKE 任一步失败立即 `FAIL` + 可读码；`RunGate` 顶层 `catch` → `UNEXPECTED_EXCEPTION`，`finally` 一定还原 |
| 目标解析复用 selector | `Bug1GateProbe.cs:180-206` | `Bug1ProbeTargetSelector.SnapshotAll/Select` + `MapSelectionError`，不另造一套 |
| `GATE_RESULT` + JSON | `Bug1GateProbe.cs:612-640`、`Bug1GateProbeLogic.cs:196-268` | 日志行 `GATE_RESULT=PASS|FAIL reason=… json=…`，落盘 `BepInEx/diagnostics/spf-bug1-gate/<runId>/GATE.json` |

## 2. 文件与哈希

| 文件 | 行数 | 改前 sha256 | 改后 sha256 |
| --- | --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1GateProbe.cs` | 698 | （新增） | `c767683ee542271855f1c2cc68d6a260fa15d0f906fc87b660e7e1f2efac0da0` |
| `Platform/Diagnostics/Patches/Bug1GateProbeLogic.cs` | 399 | （新增） | `e75885dba05c989f408966085211cbb32dec402b46852e6601cae2bbc38e464c` |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs` | 95 | `ba5b7a73b520e807037e38d8ddbfac82953322f0a0899e85a990ef3e6a9eb0eb` | `0e1abb045df2ade1639949db3ac2e25650b80eb78ab19fbfcd8e87d9120fcc1b` |
| `SteamP2PFriends.csproj` | 411 | `ed629ae7064015d55d2e1bee15b01ba1eb6a7a565dcdb80ac36892499350b948` | `af991c70de6bd822de0fd50d4a7f74496c52f2156e7377dacdd2a294f16d912d` |
| `WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs` | 365 | （新增） | `dc482c3aefbc2c89c78c561693e5e470ca311ba5731e7e786fd4fec506c6b434` |
| `WhitelistTests/Program.cs` | 1095 | `71d5268ad401ae842ee97c71ba368f590a1554ba86b0f7dae8087c9949095c8e` | `999497d44413ba26fc31184a13b7a476d67af5456e7706e6a8c9197bc10081bb` |
| `bin/Release/SteamP2PFriends.dll` | — | `7d625908…`（t83 交付态） | `d49a8d63538976e32761b9c155fb00ca918a2df91821e70479315fa61c8a8cb0` |

未改动：`Bug1ProbeJudge.cs`、`Bug1ProbeRuntime.cs`、`Bug1ProbeModel.cs`、`Bug1ProbeControl.cs`、`Bug1ProbeTargetSelector.cs`（只调用）。

## 3. 离线可证 vs 实机待验

**已自动化可证（7 条用例，Bug1-82…Bug1-88）**

| 用例 | 断言 |
| --- | --- |
| Bug1-82 | 节名 `Bug1Gate`、`Enable=false`、`TargetPlayer=""`、`OffsetU=0.10`、`Samples=64`；二次绑定复用同一 `ConfigEntry`（证明没有另起 section） |
| Bug1-83 | 空选择器只选唯一远端、排除自己；两名远端 → `TARGET_PLAYER_AMBIGUOUS`；名字/SteamID64 命中；`NotFound → TARGET_PLAYER_NOT_FOUND`；`HasPlayerId=false` 的 client 被跳过不抛异常 |
| Bug1-84 | `Decide` 11 个分支：未测量→UNKNOWN/NOT_MEASURED、非纯节点、位级还原、表面未回基线、collider、raycast、零样本、半位移、超上限、部分表面、全满足→PASS/NONE |
| Bug1-85 | `BuildJson` 含 30 个契约键（含 collider `center`/`size` 三元组、raycast hit/distance、failure_detail） |
| Bug1-86 | 分位数 p10<p50<p90、p50 线性插值、NaN/Inf 过滤、空集合边界 |
| Bug1-87 | `BitEqual` 位级：相邻 float/double 不判等，`+0` 与 `-0` 不判等 |
| Bug1-88 | **离线不得 PASS**：`measured=false` 即使所有布尔为 true、统计完美也返回 UNKNOWN/NOT_MEASURED；同参数 `measured=true` 为 PASS（控制组） |
| Bug1-89 | `IMPURE_RENDER_NODE` 的 `failure_detail` 带**组件类型名清单**：`components=CapsuleCollider,Rigidbody`、单个类型完整、空清单 `components=(none)`、null 按空处理（不是只报数量） |

**仍需 1H+1G 实机（离线无法证明，本票一律不写「通过」）**

1. `BakeMesh` 在真实 Unturned 运行时是否可调用、`mesh.vertices` 是否可读（mesh 未标记 readable 会抛 → `BAKE_FAILED`）。
2. 只改 `Model_0/1.localPosition` 时，世界蒙皮表面是否真的整体平移 0.10U（t76 §2 的原理性反例：`R×Σw_j(R⁻¹B_jP_j)v`，可能被抵消）；是否出现 `SURFACE_NOT_MOVED` / `PARTIAL_SURFACE_MOVED`。
3. 「没碰骨头」的实机确认（本实现**有意不做**全骨骼 pose 哈希，理由见 §6；改由 root/Third/Skeleton 位级快照 + Collider bounds + 受控 Raycast 三项覆盖）。
4. Collider world bounds 与受控 `Physics.Raycast` 在偏移前后是否逐字段一致（离线只测了比较函数本身）。
5. 纯节点白名单是否适配真实模型层级（若真实 `Model_0` 上挂着白名单外组件，会 fail-closed 报 `IMPURE_RENDER_NODE`，需要按实机 trace 调整白名单——这是有意的严格，不是缺陷）。
6. `GATE_RESULT=PASS|FAIL` 只在真实 1H+1G 下有效；离线进程不会触发 `Camera.onPreCull`，因此**没有任何离线产物可以声称 G 门成立**。

## 4. 变异证据（仓库外副本 `../_t85mut`，逐字节还原后删除）

每条变异都「先 build 生产、再 build 测试、再跑 exe」；副本源码最终与主树零差异（唯一额外项是复制时多出的 `Build/Build` 目录，已随副本删除）。

| 变异 | 文件:行 | 原文 → 替换文 | 预期红 | 实测红 | 变异后 DLL |
| --- | --- | --- | --- | --- | --- |
| MV-1 | `Bug1GateProbe.cs:72` | `"Enable", false,` → `"Enable", true,` | Bug1-82 | **Bug1-82**（772/775） | `55bc6f7aed5639847e87222ac5721f2895e3f198da29ae4162c1e1beb63b7526` |
| MV-2 | `Bug1GateProbeLogic.cs:115` | `if (!measured)` → `if (false && !measured)` | Bug1-88 | **Bug1-84 + Bug1-88**（771/775；Bug1-84 的未测量分支同被改坏） | `f2448341558bc462ee59b93464de544c18264a9fa450c381af9eee8816a4a229` |
| MV-3 | `Bug1GateProbeLogic.cs:181` | `if (Status == Selected) return null;` → `if (Status == Selected \|\| Status == Ambiguous) return null;` | Bug1-83 | **Bug1-83**（772/775） | `ae5495aa4564d2a03c0082023baf961e9b5b1e87b1f4af388e6fc5801d98438e` |
| MV-4 | `Bug1GateProbeLogic.cs:213` | 删掉 `"deviation_ratio":…` 整行 | Bug1-85 | **Bug1-85**（772/775） | `248078898d51500228cba228f4746e06d0c70e487af57f91fd2c316948f303e9` |
| MV-5 | `Bug1GateProbeLogic.cs:85` | `if (IsNaN \|\| IsInfinity) continue;` → 注释掉 | Bug1-86 | **Bug1-86**（772/775） | `09e28d4bbd298fa1778b2b103f83712050d278cc04f5daad4c83f05120b6b101` |
| MV-6 | `Bug1GateProbeLogic.cs:189-190` | `BitEqual(float)` 实现 → `=> a == b;` | Bug1-87 | **Bug1-87**（772/775；`+0`/`-0` 与位模式断言先红） | `656fe9b1f8eab31c12ffc5300f9a21bffd1c9fdda4515ec3a9b837794b284f13` |
| MV-7 | `Bug1GateProbeLogic.cs` `FormatImpureComponents` | 类型名循环 → `builder.Append(typeNames.Count);`（只报数量） | Bug1-89 | **Bug1-89**（773/776，`components=2` 与 `components=CapsuleCollider,Rigidbody` 不符） | `7db5c1c08b4bf70d44a618519dcf5606f417aa7c81cbb232485a30afbe79789a` |

每条变异只红 1–2 条自建用例，其余 Bug1 用例（共 90 条）与全量其余用例保持绿；每轮 `cp` 回主树文件后源码哈希回到 `c767683e…`（探针）/`e75885db…`（逻辑），MV-7 的副本也已逐字节还原并删除。

## 5. 三命令（主树终态实测）

```
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
  → 已成功生成。0 个警告，0 个错误

dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
  → 已成功生成。4 个警告（既有），0 个错误

WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe
  → === Final Result: 774/776 PASS (Failed: 2) ===
  → FAIL 仅 [PureMemory] GBA01 Native First Observe Binding、[PureMemory] NIL01 Real Native Lookup Contract（既有环境失败）
  → Bug1 段 90/90 PASS（原 82 + 新增 8：Bug1-82…Bug1-89）
```

exe 退出码为 1，属既有约定（有失败即 1）。

## 6. 边界与已知限制（如实）

- **未实现插值**：本探针不写任何逐帧偏移，不改变玩家表现；只在 `onPreCull` 里做一次测量后立刻退订。
- **不做全骨骼 pose 哈希（有意为之，不是缺口）**：t76 §2.2 提到「原骨骼 pose 哈希前后相同」，本实现**有意不做**这一项，理由是它要么恒真、要么必然抖——① 我们只在**同一帧内**写 `Model_0/1.localPosition` 并立即还原，中间没有任何 `Animator` 更新，此刻比全骨骼 pose 一定是「相同」，属于自证；② 跨帧比则会被动画本身（Idle/走/跑切换）干扰，得到假红。真正要守的命题是「我们没碰到骨头」，这一点已由 `Skeleton` 的 position/rotation 位级快照、Collider world bounds 逐字段比对、受控 `Physics.Raycast` 前后一致三项覆盖；如果实机仍担心骨骼被动，正确做法是查 `Skeleton` 子节点数量/层级与快照，而不是比 pose 哈希。
- **`IMPURE_RENDER_NODE` 带组件类型名清单**：`failure_detail` 输出 `components=CapsuleCollider,Rigidbody` 这类类型名（去重、按出现顺序；读取失败记 `<read-failed:TypeName>`，null 组件记 `<null-component>`），不是只报数量——实机撞上时可直接判断「按类型放宽白名单」还是「纯叶方案不成立」。
- **一次性测量**：`_ranOnce` 保证每进程只测一次；重新测量需重启进程（避免中途配置变化污染证据）。
- **纯节点白名单可能过严**：真实模型若在 `Model_0/1` 上挂白名单外组件会 fail-closed；这是 t76 §2.1「未知组件直接不准入」的要求，调整须以实机 trace 为依据。
- **`Object.Destroy(mesh)`**：BakeMesh 用的临时 Mesh 走 `Destroy`（运行时延迟销毁）；若频繁测量需改为对象池，本票一次性测量不涉及。
- **raycast 命中体**：受控射线从玩家前上方朝身体方向打，若被地形/其它玩家先挡，会记录到别的 collider——前后一致仍算通过（判据是「不变」而非「命中目标」），但解读时需看 `hit` 名字。
- **目标解析**：复用 `Bug1ProbeTargetSelector`，空选择器要求「恰有一名远端」；不满足即 `REFUSED` + 有界重试（最多 3 次，level-loaded/player-created）。
- **边界核验**：Bug3 五文件哈希未变（`a89f40b9…`/`7a82de8e…`/`3d5044f7…`/`a4553385…`/`5b0c4e9b…`）；`Release/`、`publishi/` 未被写（`find -newermt "2026-11-08 00:20"` 无输出）；变异副本 `../_t85mut` 已删除。

## 7. 结论

- 探针本体、配置入口、纯逻辑判定、离线用例与变异证据均已落地；三命令全绿（仅两条既有环境失败）。
- **G 门是否成立仍未测量**：需要 1H+1G 实机跑一次并回传 `GATE.json`；在拿到该产物前，任何「Model_0/1 叶偏移可行」的说法都不成立。

## 8. 实机步骤（1H+1G，闸门只需一次）

1. **前置**：Host 侧装好含本探针的构建；确认 `BepInEx/config/SteamP2PFriends.Bug1Gate.cfg` 存在且 `[Bug1Gate] Enable = false`（默认关闭，加载时不得有任何 `SPF-Bug1Gate` 日志）。
2. **开启**：把 `Enable = true`，`TargetPlayer` 留空（要求视野内恰有一名远端客机）或填该客机名字/SteamID64；保存后看日志出现 `ARMED`（或 `REFUSED ROLE_NOT_HOST` + 后续 `RETRY`——启动期开启属正常路径）。
3. **动作**：让**客机在 Host 视野内持续行走 10 秒**即可，**不需要跑**（闸门只验 X/Z 各 0.10U 的刚性位移与还原，与速度档无关）。测量在下一帧 `onPreCull` 自动触发一次，随后立即退订。
4. **取产物**：`BepInEx/diagnostics/spf-bug1-gate/<runId>/GATE.json` + 日志里的 `GATE_RESULT=… reason=… samples=… median_u=…`。
5. **判读**：
   - `PASS` → 纯叶位移路线在本版本 SDK 上成立，可进入 t76 §3 的插值实现（本票不做）。
   - `SURFACE_NOT_MOVED` / `PARTIAL_SURFACE_MOVED` → 纯叶方案不成立（蒙皮被 `R⁻¹` 抵消或只部分顶点跟随），按 t76 §2 停止，转 T61 隔离 view proxy，不许轮流试 `Third`/`Skeleton`/空父节点。
   - `IMPURE_RENDER_NODE` → **也是有效结论**：说明 `Model_0/1` 上挂着白名单外组件（`failure_detail` 会列出类型名），纯叶方案在当前层级不成立。**不许为了让它 PASS 去放宽白名单**；若确需放宽，必须单独走一轮并说明理由与影响面。
   - `COLLIDER_CHANGED` / `RAYCAST_CHANGED` → 偏移污染了物理或命中，方案不可用。
   - `RESTORE_MISMATCH` → 位级还原失败，先修还原再谈可行性。
6. **回传**：`GATE.json` 原文 + 对应 `trace.jsonl`（若同 run 有 Bug1 仪表产物）与日志片段；离线侧不再重复声明任何结论。
