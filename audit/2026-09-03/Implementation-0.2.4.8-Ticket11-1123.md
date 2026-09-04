# Ticket 11：Resource 旧 Authority Writer 退出与运行时验收 — 运行时诊断结论落盘 + 取证埋点（步骤②）

- **日期**：2026-09-03 11:23
- **分支**：`codex/structure-baseline-0.2.4`（HEAD `340918a`，工作区干净）
- **票据**：`.scratch/structure-baseline-0-2-4-8/issues/11-resource-authority-retirement-runtime.md`（状态 `implemented-pending-runtime`）
- **任务指令**：`/implement /tdd`（Ticket 11），步骤①落盘诊断结论、②最小取证埋点（`ProcessEntered` catch 把 `ex.Message` 写入 reason）、③回归测试先行（`CaptureRegionState` 遇 null 区域/空树列表/单区域失败不整批回滚）、④容错修复、⑤构建与静态门禁、⑥/code-review、⑦提交。

---

## 0. 决策记录（用户裁决：先取证后修复，两轮）

- **Seams 确认**（tdd 纪律：test only at pre-agreed seams）：
  1. **PureMemory 主机 seam**：`ResourceProductionControlSeamTests`（使用 `FakeResourceAdapters`），新增 M6P31 容错回归测试——A 区 `(33,29)` 成功 acquire 保留、B 区 `(33,34)` 失败跳过/隔离、不整批回滚、下次重试。
  2. **StaticIL 契约 seam**：`Test_FailureClassificationDoesNotParseExceptionText` 保持断言「`ProcessEntered` 内零 `get_Message`/`get_StackTrace` 调用」；`ex.Message` 承载须经独立 static helper，使 `ProcessEntered` EDG 无 Message 读取调用。
- **推进顺序（用户选定「先取证后修复（两轮）」）**：
  - **本轮（取证轮）**：① 落盘诊断结论；② 取证埋点（helper 承载 `ex.Message`，作为持久可观测性改进）；③ M6P31 回归测试先行（先红）；构建后**重新采集三端日志**确认 H1/H2。
  - **下轮（修复轮）**：依据取证结论，④ 实施单区域 Acquire 失败跳过/隔离容错修复（绿）；⑤ 构建与静态门禁；⑥ /code-review；⑦ 提交。

> 说明：取证埋点会让 `ex.Message` 进入日志，属新增可观测性改进（非临时调试代码），随提交保留。StaticIL 契约已同步升级守护 helper 边界。

---

## 1. 运行时诊断结论（源自前序只读诊断，2026-09-02 三端日志）

诊断包（只读采集，本次未改动）：
- Host：`…\UMM-v2.2.0-win-x64\UMM-诊断包_20260902_233412`
- VM Guest：`…\UMM-诊断包_20260902_233545`
- User Guest：`…\UMM-诊断包_20260902_233611.zip`

### 事实（F1–F7）

| # | 事实 | 出处 | 结论 |
|---|---|---|---|
| F1 | 会话可建立（SessionBegin/LifecycleBegin 成功） | Host 日志 | 不阻塞闭环 |
| F2 | 每次 Acquire 均死 `region-snapshot-failed exception=InvalidOperationException`，50/50 fault 全部失败，退避 83.9→1554.8s | Host 日志 | 决定性故障模式 |
| F3 | 失败点位于 `CaptureRegionState→CaptureNativeRegionState`，发生在 `OnAcquire` 之前（regionGeneration=0） | Host 日志 + 源码定位 | 故障在 acquire 快照阶段 |
| F4 | 单区域失败触发整批回滚：(33,29)–(33,33) 成功 gen 0→1 后 (33,34) 失败 → `SnapshotRemove` 全清 | Host 日志 | 整批回滚放大损失 |
| F5 | SPI 全程无 lease、无 summary 行 | Host 日志 | 生产路径未建立 |
| F6 | `CollisionActivation…reason=no-remote-demand path=Native spiActive=true` 放行原生禁用 | Host 日志 | 碰撞补丁因 SPI 空转转回原生 |
| F7 | 两客机日志无 SPI 事件 | Guest 日志 | 客户端侧未观测到生产 |

### 假设排序（概率由高到低）

- **H1（最高）**：`CaptureNativeRegionState` 遇 null 区域 / trees 列表不可用（`native-resource-trees-unavailable`）——原生 `ResourceManager` 数据结构在 Listen-Host 下无法回溯。
- **H2**：trees 列表内含 null 元素（`native-resource-tree-null index=N`）。
- **H3（低）**：坐标越界（`region out of bounds`）。
- **H4**：容错设计缺陷（整批回滚 + 指数退避 → 永久瘫痪）——H4 不是触发根因，而是放大因子。

### 根因链（最终目标修复）

```
Acquire 前 CaptureNativeRegionState 异常（H1/H2）
  → 单区域失败 → UpdateObserver catch → RestoreState + RunCompensations 整批回滚（F4）
  → ShadowFaultBackoff 指数退避（1<<exp, cap 60s, 50/50 fault）→ IsRegionActive 恒 false（F5）
  → 碰撞补丁 covered=false → 原生资源禁用（F6）
  → Host 单独端看不到树/矿（F7）
```

唯一 Authority Writer、P2P 协议、Harmony target/order、配置键、GUID `com.yu80rice.steamp2pfriends`、归档冻结均不变。

---

## 2. 取证埋点设计（本轮代码变更 #1）

**约束冲突**：StaticIL 契约 `Test_FailureClassificationDoesNotParseExceptionText`（L160–166）断言 `ProcessEntered` 内**零 IL 调用** `Exception.get_Message`。因此 `ex.Message` 必须经**独立 static helper**承载，helper 内读 `ex.Message` 并返回格式化字符串；`ProcessEntered` catch 只调用 `helper(ex)`，EDG 无 `get_Message` 调用。

**变更点**（`Adapters/Resource/ResourceProductionControlSeam.cs`）：
- 新增 static helper（含 `[MethodImpl(MethodImplOptions.NoInlining)]`，防内联把 `get_Message` 压回 `ProcessEntered` EDG），输入 `Exception`，返回含 `ex.GetType().Name` 与 `ex.Message` 的描述串。
- `ProcessEntered` catch（目前 L593–599）日志行由 `"… exception=" + ex.GetType().Name` 改为 `"… exception=" + DescribeAcquireFailure(ex)`。

> 该 helper 同时服务于步骤④容错修复的日志定位（记录到具体 region 与 Message），是持久的可观测性改进。

---

## 3. 回归测试先行（本轮代码变更 #2，先红）

- 新增 **M6P31**：`Test_M6P31_CaptureRegionFailureIsolationKeepsOtherRegions`（`ResourceProductionControlSeamTests`）。
  - 预置：`FakeLifecycleAdapter` 新增选择性失败注入——对特定 `RegionKey` 的 `CaptureRegionState` 抛 `InvalidOperationException`。
  - 场景：模拟 `UpdateObserver` 进入多个区域，(33,29) 正常、(33,34) snapshot 失败。
  - **期望（修复后绿）**：`(33,29)` acquire 保留（`IsLeased` true、lease generation 正常），`(33,34)` 隔离跳过，不整批回滚，后续可重试。
  - **当前行为（先红）**：`(33,34)` 失败 → `ProcessEntered` throw → `UpdateObserver` catch → `RestoreState` 把 `(33,29)` 的 lease 一并清空 → 断言 `IsLeased((33,29))` 为 false → **测试红**。
- 新增测试注册进 `WhitelistTests/Program.cs`。经核实 `EvidenceClassCatalogTests`/`RegistrationClosureTests` 仅校验 4 类分类布局与 closure 行为，不硬编码 Region 测试数，故 M6P31 新增无需改计数；ExecuteBanner 的 “Target: 255 PASS” 为展示文本非断言。StaticIL 契约 seam 保持存在（helper 约束版本）。

---

## 4. 证据指纹（取证埋点前基线 / 待重新采集）

- 当前 `bin/Release/SteamP2PFriends.dll`（取证埋点前基线，本报告签发时）：
  - 版本 `0.2.4.8`，MVID `8204775d-9fad-44a9-88b4-eca906422048`
  - SHA-256 `D0CEE669717C5B31FAA7C2F29AADB1104E8A8BC630027542AE77EBC9C35A049A`
  - Case-ID `SPF-0.2.4.8-Experimental-StructureBaseline`，GUID `com.yu80rice.steamp2pfriends`
- 取证埋点构建后：**新 DLL 产生新 MVID / SHA-256**，须重新采集三端日志并在交付报告中记录新指纹。
- 权威静态结论：`audit/2026-08-29/RuntimeFix-0.2.4.8-2229.md`（255/255 PASS 静态，Runtime PENDING）。

## 4a. 取证版构建结果（2026-09-03 11:25）

- 取证埋点后 Rebuild（主 `SteamP2PFriends.csproj` + `WhitelistTests\SteamP2PFriends.WhitelistTests.csproj`，Release）均 `0 errors`。
- 测试套件 **256/257 PASS，Failed: 1**——唯一失败为新增 **M6P31 ResourceCaptureFailureIsolation**（预期红，见 §3）；其余 255 项全部 PASS。
- **StaticIL 契约（互补取证锚点）全绿**：
  - `Resource Failure Classification` PASS → `ProcessEntered` EDG 内**零** `get_Message` 调用（分类边界不解析异常文本）。
  - `Resource Acquire Failure Helper Message`（本轮新增）PASS → 独立 helper `DescribeAcquireFailure` 恰好**含 1 次** `get_Message`（ex.Message 确实进入取证日志输出）、0 次 `get_StackTrace`。两条互补，共同保证取证信息进入可观测输出、又不污染 `ProcessEntered` 的分类边界。
- `git diff --check` PASS（无空白错误），`Verify-BuildFingerprintArtifact.ps1`（`INDEPENDENT_ARTIFACT_VERIFICATION_PASS`）。
- **取证版 DLL 新指纹**（`Verify-BuildFingerprintArtifact.ps1`，`INDEPENDENT_ARTIFACT_VERIFICATION_PASS`）：
  - 版本 `0.2.4.8`，**新 MVID `524d3392-f5c3-4a12-92d0-b00aaa2bfb8a`**
  - **新 SHA-256 `145BEFAA49444979F7A012ADFF2375815C2DBCA960A50A5CAEA864AB14300ADD`**
  - Case-ID `SPF-0.2.4.8-Experimental-StructureBaseline`、GUID `com.yu80rice.steamp2pfriends` 不变
- 变更集（`git status`）：`Adapters/Resource/ResourceProductionControlSeam.cs`（取证 helper）、`WhitelistTests/Evidence/PureMemory/Adapters/Resource/ResourceProductionControlSeamTests.cs`（M6P31 + 选择性注入）、`WhitelistTests/Program.cs`（M6P31 注册）、`audit/README.md`、`audit/2026-09-03/`（诊断文档）。
- **待办（外部协作）**：用取证版 DLL 现场重采集 Host + 双 Guest 三端日志，确认 `exception=` 后的 Message 归属 H1（null 区域 / trees-unavailable）或 H2（树内含 null）；随后依据取证结论实施步骤④容错修复并二次构建。

---

## 5. 本轮状态

- [x] 诊断结论落盘（本文档）
- [x] 取证埋点 helper（代码变更 #1）
- [x] StaticIL 互补取证锚点（ProcessEntered 不读 Message / helper 读 Message）——Green
- [x] M6P31 回归测试（代码变更 #2，红阶段达成：256/257，唯一失败为 M6P31）
- [x] 取证版构建 + 静态门禁（msbuild ×2、测试、git diff --check、指纹验证）
- [ ] 三端日志重采集（确认 H1/H2）——需现场运行取证版 DLL
- [ ] 步骤④容错修复（绿）——下轮
- [ ] /code-review —— 修复轮
- [ ] 提交到 `codex/structure-baseline-0.2.4` —— 修复轮

> 本轮为取证轮，不宣称 Runtime PASS。取证修复提交后，若无法现场重测，明确标注「取证修复已提交，待下一次 Host 单独动态测试验证」。