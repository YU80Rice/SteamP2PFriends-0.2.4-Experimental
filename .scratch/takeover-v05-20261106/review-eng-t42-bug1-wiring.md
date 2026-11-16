# T44 工程轴复审 r4：t42 Bug1 仪表修复轮（接线穿透与假绿）

复审对象：t42（Bug1 仪表修复轮）。审查官：reviewer-eng（工程轴）。attempt_id=4eedee96-729a-4059-a1c2-ecf6146886ab。
范围：只判工程正确性与「假绿是否真堵住」；`sim_call_id` 语义诚实性由规格轴 t43 判。

## 0. 结论

**verdict = pass（代码层）**。上一轮（t37）的指控是「删掉生产接线后全量仍 703/705 全绿」；本轮两条新用例**在自跑变异下都真红**，不是换了姿势的自证：

- 删 `Bug1ProbePatch` 对 `OnEnqueue/OnDequeue` 的两处调用 → 706/709，红 **Bug1-21**，失败消息点名「Postfix_Enqueue 未调用 Bug1ProbeRuntime.OnEnqueue，实际边数=0」；
- 去掉两侧队列身份过滤 → 706/709，红 **Bug1-22**，消息给出「应恰好 2 条，实际=3」。

**但当前交付包不可用（R7-01，交付层 blocker，非代码缺陷）**：`Release/0.2.4.11-preview2/` 的 DLL 仍是 `d9254a24…`（00:21 构建），而送审字节重建为 `77700e37…`；preview2 里没有相位仪表，用它上机采集会**静默缺全部相位行且不报错**。必须先按送审字节重新打包并复核，才可进入实机。

## 1. 被审字节锁（实测，全部与送审一致）

| 文件 | 字节 | sha256 |
| --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1ProbePatch.cs` | 8872 | `12f8cbf0e1b9960bc6fc9512aeb4538d367f63db67da2d32930501d3ded031fd` |
| `Platform/Diagnostics/Patches/Bug1ProbeRuntime.cs` | 41195 | `7f8a21f4396f6d11b6d5847f6ac53708b207fdd37f0dc792bf852258888e4301` |
| `Platform/Diagnostics/Patches/Bug1ProbeRxPhases.cs` | 16693 | `8d0d36fc94cb1d6b8ba98fb99e73c6e1691ccee04e2f7d928ad3830f1964ca4a` |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeRxPhaseTests.cs` | 20013 | `88371a2e789af84b006ba825e4e521a051bdbf27812b9b487cf09172372d1cb2` |
| `WhitelistTests/Program.cs` | 135010 | `ce65f375781e9a517d42442b2ee3ccba5d6e68fc8d7dde766728a6ffce21c0de` |
| `bin/Release/SteamP2PFriends.dll`（重建） | 1462272 | `77700e378483f7d62c08cb986f415f8cd1233e86a5c4c44c34bc0abc2aba1aa5` |

`Bug1ProbePatch.cs` 哈希与 t35 完全相同 → t42 **没有改接线文件**，只扩了 `Bug1ProbeRxPhases.cs`/`Bug1ProbeRuntime.cs` 与测试。
构建确定性：同一源码连续 `-t:Rebuild` ，DLL 恒为 `77700e37…`（三次一致，可用作字节锚点）。

## 2. 独立复跑（三条 verify，实测）

| # | 命令 | exit | 结果 |
| --- | --- | --- | --- |
| 1 | `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 0 | 0 警告 0 错误（1.2–1.4s） |
| 2 | `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | 0 | 0 错误 4 警告（既有 CS0649） |
| 3 | `./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | 1 | **707/709 PASS（Failed: 2）** |

Failed 两项均为既有环境缺失：`GBA01 Native First Observe Binding`、`NIL01 Real Native Lookup Contract`（`TypeInitializationException → SDG.Unturned.SteamPlayer → NetReflection → ReflectionTypeLoadException`，控制台无 Unity 运行时）。**Bug1 段 24/24 全绿**（Bug1-01..24，其中 21/22/23/24 标 T42）。与 Captain 复跑 707/709、Bug1 24/24、`77700e37…` 完全一致。

## 3. 变异（审查官自跑，逐字节还原）

脚本 `D:/tmp/mut44.py`（`MA|MB|MB2|restore`），改动前 `cp` 备份到 `D:/tmp/t44-*.bak`；**未使用 git**（本轮合同禁止 git 操作）。

| 变异 | 改动点 | 变异后 DLL sha256 | 结果 | 新红用例 |
| --- | --- | --- | --- | --- |
| M-A 删生产接线 | `Bug1ProbePatch.cs:176` 删 `OnEnqueue(__instance, item)`；`:182` 删 `OnDequeue(__instance, __result)` | `5f94e253e2673fc0…c84b82b` | 706/709（Failed:3） | **Bug1-21** |
| M-B 双侧去过滤 | `Bug1ProbeRuntime.cs:327` 与 `:348` 的 `!ReferenceEquals(queue,_serversidePackets)` 条件删除 | `2a1852ed34b5a502…c565253` | 706/709（Failed:3） | **Bug1-22** |
| M-B2 只去出队侧过滤 | 仅 `Bug1ProbeRuntime.cs:348` | `f66c56a869647e48…81791fe` | **707/709 全绿** | 无（见 R7-03） |
| 还原 | `cp` 回备份 | `77700e37…aba1aa5` | 707/709，Bug1 24/24 | — |

红名单原文（GBK 日志 `D:/tmp/reds-ma.txt`）：
`FAIL [Runtime] Bug1-21 Production Wiring Four Phase Edges (T42): … Bug1Probe: Postfix_Enqueue 未调用 Bug1ProbeRuntime.OnEnqueue，实际边数=0`

**判定**：M-A 与 M-B 都真红，且失败消息能定位到具体边/具体数量 → 上一轮「删接线仍全绿」的假绿已被堵死。M-B2 暴露一个**出队侧负控缺口**（低危，R7-03），说明覆盖面是针对入队侧写的，不是对称写的。

## 4. 逐条 acceptance

| # | 判据 | 结论 | 证据 |
| --- | --- | --- | --- |
| 1 | 三命令独立复跑，记 PASS / Bug1 条数 / DLL sha | **pass** | §2；707/709、Bug1 24/24、`77700e37…` |
| 2 | 核实 707/709 与 `77700e37…` | **pass** | 三次重建哈希恒同，构建确定性成立 |
| 3 | 删接线变异必红，并逐字节还原 | **pass** | M-A：706/709，红 Bug1-21；还原后 `12f8cbf0`/`77700e37` |
| 4 | 去过滤变异必红（否则 blocker） | **pass（部分）** | M-B 红 Bug1-22；**M-B2（仅出队侧）全绿** → R7-03 low，不构成 blocker |
| 5 | IL 契约健壮性：缺失/内联/类型不匹配 | **pass** | `IlContractProbe.CountMethodCalls`：方法 null→-1、无体→0、`ResolveMethod` 失败不计、畸形 IL→-1；断言 `>=1` 故全部 fail-closed。IL 读取在 JIT 前，内联不影响。`EnqueueTarget.DeclaringType == typeof(Queue<PlayerInputPacket>)` 直接断言，不匹配即 0→红并打印实际类型 |
| 6 | 测试缝安全性 | **pass** | `internal` + `InternalsVisibleTo`（`Properties/AssemblyInfo.cs:27`）；`__Test*` 全仓库仅测试文件调用；`__TestEnable` 不装 Harmony/不写盘/不造 Unity 对象；`seq.SetValue(packet,77u)` 写的是测试内 `new PlayerInputPacket()`，无全局状态；`finally __TestDisable()` 复位 |
| 7 | 热路径与副作用 | **pass** | 关闭态 `RegisterManual` 在 `Install()` 前 return，零 hook；开启态每包 1 次 bool 读 + 1 次反射读 + 1 次队列 `Count`；全部 hook 为 `void`、无 `return false`、不改参数与原生返回值 → 只读成立 |
| 8 | RUN_END 计数真实性 + 容量通路覆盖 | **pass** | `rx_guard_rejected/rx_evicted/rx_superseded/trace_dropped` 分别取 `GuardRejected/EvictedRxEntries/SupersededRxEntries/_traceDropped`，均运行时累加；容量用例 Bug1-23（capacity=2→`Evicted==1`、被淘汰 seq `rx_id:null`）、Bug1-24（`Superseded==1`）覆盖 |
| 9 | 扩张面与字节锁 | **pass（附条件）** | Bug3 五文件仍为 `a89f40b9/7a82de8e/3d5044f7/a4553385/5b0c4e9b`；csproj `10 0`、Program.cs `78 0` 纯追加；`Build/Version.props` 变更属发布票（20:01，早于本窗口）；Release/、publishi/ 未被本轮改写 —— 但内容陈旧，见 R7-01 |
| 10 | 可交付实机采集 | **有条件 pass** | 代码路径无「静默缺相位」缺陷（安装失败 `FailClosed`+Error、相位缺失会体现为 trace 无行且判据可查）；**但当前 preview2 包是旧字节，必须重打包后才能上机** |

补充：`Platform/Diagnostics/Patches/` 交付窗口内 mtime 只有 `Bug1ProbePatch.cs`/`Bug1ProbeRuntime.cs`（02:52，t42 自述的变异复原）与 `Bug1ProbeRxPhases.cs`（02:57）变动，三者哈希与送审一致；同目录 `Bug1ProbeJudge.cs`、`Bug1ProbeModel.cs` 仍停在 23:17–23:18，未被触碰。审查官为跑变异在 03:0x 重写了前两个文件并 `cp` 还原，**内容哈希不变、mtime 变**，特此披露以免后续误判为第三方改动。

## 5. Findings

### R7-01 blocker（交付层；不是代码缺陷）
`Release/0.2.4.11-preview2/SteamP2PFriends.dll`、`sha256.txt`、`manifest.json`（`dllSha256: D9254A24…`）、`SteamP2PFriends-0.2.4.11-preview2.zip` 四者互相自洽，但**全部是 `d9254a24…`（1457152B，00:21 构建）**，比送审 `77700e37…` 早：包里没有 t35 的相位仪表，也没有 t42 的接线/计数修复。若用它做 1H+1G 采集，trace 不会出现 `AFTER_ENQUEUE`/`AFTER_DEQUEUE`，且不会报任何错 —— 正是「静默缺相位」，采集结果不可用。
**requiredFix**：从 `77700e37…`（由送审源码 `-t:Rebuild` 重建）重新打包 preview3，同步更新 dll/sha256.txt/manifest/zip，并在新包上复跑三条命令确认 707/709；**在此之前不得用 preview2 上机**。

### R7-02 medium（交付完整性）
五个 Bug1 生产文件与 `Bug1ProbeRxPhaseTests.cs` 均为 `??` untracked（Platform/Diagnostics/Patches 整个目录不在 HEAD），而 `SteamP2PFriends.csproj` 已引用它们 → 干净检出无法编译，交付不可复现（与 Bug3 E6 同类问题第二次出现）。
**requiredFix**：`git add` 这 6 个文件后随本轮一起归档；若坚持只在工作区打包，manifest 必须记录六文件 sha256。

### R7-03 low（测试覆盖不对称）
`Bug1ProbeRuntime.cs:348` 的出队侧队列身份过滤零覆盖：只删这一侧（M-B2）→ 707/709 全绿。`Bug1ProbeRxPhaseTests.cs:217-220` 只对目标队列做了 `Dequeue`。
**requiredFix**：在 Bug1-22 里补 `foreign.Enqueue(packet); foreign.Dequeue(); OnDequeue(foreign, dequeued);` 并断言 `trace.Length` 仍为 2。

### R7-04 low（测试缝残留）
`Bug1ProbeRuntime.cs:643-674`：`__TestEnable` 绕过 `Enable()` 的 RunId/StepId/TargetSteamId/SpeedCap 校验；`__TestDisable` 清了 6 个静态字段但不置 `Config=null`、不清 `LastError` → 若后续用例不重新 enable 就读取，会看到上一用例的配置。
**requiredFix**：`__TestDisable` 内补 `Config=null; LastError=string.Empty;`。

### R7-05 low（可观测性）
`Bug1ProbePatch.cs:19,53,72,90,100` 的 `FailClosed` 只写不读，仓库内无消费者；登记失败时操作者只能翻日志才知道仪表没装上。
**requiredFix**：把 `FailClosed`（及失败阶段）并入注册结果或 RUN_BEGIN 行。

### R7-06 low（资源，非本轮引入）
`Bug1ProbeModel.cs:91` `MaxTraceRecords=131072` 按条数不按字节，且 `Bug1ProbeRuntime.cs:533` 只在 EndStep 落盘 → UTF-16 最坏可常驻 100MB+。长跑才会触及，`trace_dropped` 有计数，属 t35 既有。

### R7-07 low（反射退化静默）
`Bug1ProbeRuntime.cs:385-394` `IsDismissedRaw` 依赖 `_dismissedField` 反射，解析失败时静默退化为 `reject_reason:null`，没有用例覆盖该解析路径。
**requiredFix**：解析失败时记 `LastError="DISMISSED_FIELD_MISSING"` 并在 RUN_END 暴露。

## 6. 复现命令

```
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe   # 707/709
cp bin/Release/SteamP2PFriends.dll WhitelistTests/bin/Release/      # 变异时须同步，测试加载这份
python D:/tmp/mut44.py MA|MB|MB2|restore                            # 变异/还原
sha256sum Platform/Diagnostics/Patches/Bug1ProbePatch.cs            # 还原校验 12f8cbf0…
```

## 7. UNKNOWN / 仍需实机

- 相位行在**真实 Unity 运行时**是否确实按 1:1 出现（控制台无 Unity，Bug1-22 走的是测试缝）——需 1H+1G 实机；
- `Queue<T>.Enqueue/Dequeue` 为 BCL 泛型方法，Harmony detour 是否在所有 Unity/Mono 版本上都能挂上（`Verify` 只证注册，不证触发）——t35 既有 R6-05，未变；
- 实机 trace 的判据（缺相位=UNKNOWN）是否写进对外说明 —— 属规格轴/交付文档，本轮未判。

---
self_sha256: 350349f444efffd1d2c9ff4a5e8a8649866c00f227c8a4710b56f730ca381614
