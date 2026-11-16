# t77 改动计划：M1-XZ-RELATIVE-v2 规格轴补齐（T74-F1/F2/F3）

- 任务：t77（repair round 2），attempt 1，attempt_id `59f148fd-7d22-459c-80c5-d6c9adeb06c8`
- 来源：t74 规格轴复审 `.scratch/takeover-v05-20261106/review-spec-t73-r1.md`
- 被审基线（t73 交付，**尚未改动**）：`Bug1ProbeJudge.cs` `615080f4…`、
  `Bug1ProbeM1RuleTests.cs` `0b4b0191…`、DLL `81da407a…`
- 状态：**等 captain 放行**。t75 正在拿 t73 字节做工程轴复审，本计划不落任何生产/测试字节。

## 1. t74 findings 清单（逐条）

| 编号 | 严重度 | 指出的位置 | 事实 | 要求改成什么 |
| --- | --- | --- | --- | --- |
| T74-F1 | medium | `WhitelistTests/Evidence/Runtime/Bug1ProbeM1RuleTests.cs` 的 `Test_R07_SynchronizedMicroMotionIsNotM1`（`x = 0.0005 * i`，i=0..3） | 累计跨度 0.0015U **<** ε=0.001732U，R07 要证的「累计跨度大 ≠ 落后证据」没有被构造出来；`RenderRootSpanXZ <= ε` 只证明了小跨度 | 改成「累计 > 3ε 且逐帧 ≤ ε」（如 8 帧 × 0.0008U = 5.6mm），断言 `StrictM1Events==0`、`LagWitnessFrame==-1`、存在代表段、`CandidateRejections` 含 `LAG_WITNESS_MISSING@<release>`、`RenderRootSpanXZ > ε`；并补变异（M1 判据换成 `RenderRootSpanXZ > ε`）确认 R07 变红 |
| T74-F2 | low | `Platform/Diagnostics/Patches/Bug1ProbeJudge.cs` 的 `IsAuthStepLocked` | 只用 `HorizontalDistance(A_i, A_a) <= eps`（距段首），而 §3.4 原文是 `max‖A_i−A_j‖ ≤ eps`；对称抖动可让段内真实跨度 ≈2ε 仍判 AUTH | 改为段内 A 的最大成对水平距离（或等价逐轴 max−min 合成）≤ ε；补一条对称抖动夹具断言落 NON_M1 而非 AUTH |
| T74-F3 | low | `Test_R10_UnknownPrecedenceOverNoThreshold` | 只覆盖缺 RR / 相机重复 / 暂停 / 截断 4 类，未覆盖「关联失败」与「真实端点未解析」 | R10 内追加：①`HasLastInputSeq=false` → Reason 含 `INPUT_SEQ_MISSING`；②`ModelEndpointResolved=false` → Reason 含 `MODEL_ENDPOINT_UNRESOLVED`；各断言 `Label==Unknown` |

t74 §7 明确「不得因上述 findings 回退」的部分（水平投影、C_H 4 点/逐帧 ε、释放门严格 `>` 与 2cm 网格、落后见证三条件、回补收敛、全段遍历、M1/AUTH 互斥、R03 真实回放、原结果保留、§3.1 字段三处同源、`root_span` 删除、SIM 去布尔化、UNKNOWN 优先级、交付口径）本票一律不动。

## 2. 逐项改动（文件 → 位置 → 改前/改后）

### 2.1 `Bug1ProbeJudge.cs` — 仅 `IsAuthStepLocked`（T74-F2）

改前（逐帧与段首比）：

```csharp
Bug1Vec3 authorityStart = frames[a].APos.Quantized().Horizontal();
for (int i = a; i <= b; i++)
{
    if (Bug1Vec3.HorizontalDistance(frames[i].APos.Quantized().Horizontal(), authorityStart) > eps)
        return false;
}
```

改后（**段内真实最大成对水平距离**，O(n²)，按 m02645 不许用近似）：

```csharp
double span = 0d;
for (int i = a; i <= b; i++)
{
    Bug1Vec3 pi = frames[i].APos.Quantized().Horizontal();
    for (int j = i + 1; j <= b; j++)
    {
        double d = Bug1Vec3.HorizontalDistance(pi, frames[j].APos.Quantized().Horizontal());
        if (d > span) span = d;
    }
}
if (span > eps) return false;
```

- **不采用「逐轴 max−min 合成」**：`max(|Δx|,|Δz|)` 只是成对距离的下界，会把实际超 ε 的段判成 AUTH，方向是放宽而非收紧（m02645 明确否掉）。
- 段长通常几十点，O(n²) 不在热路径，不做近似优化。
- 只读 `APos`（§3.4 的 `A_i`），不引入 `ABeforeUpdate`，不改释放同拍判据。
- 预期回归面：R02（A 恒 0）、R03（真实 trace A 恒 0）、R08（长 AUTH 段 A 恒 0）、R11（yaw 跨零段 A=V 步进）全部仍 AUTH；对称抖动段由 AUTH 变 NON_M1（这正是 T74-F2 要求的方向）。
- 报告必须写明：**这条改动会让某些窗口标签从 AUTH 变为 NON_M1，依据是 §3.4 原文的 `max‖A_i−A_j‖≤eps`，不是扩语义**。

### 2.2 `Bug1ProbeM1RuleTests.cs` — R07 重写（T74-F1）

- `Window(9)`；帧 0..7：`x = 0.0008 * i`（逐帧 0.0008 ≤ ε，累计 0.0056 > 3ε=0.0052），A 与 V 同步；帧 8 释放到 0.08。
- 断言：`StrictM1Events==0`、`LagWitnessFrame==-1`、`HorizontalStallSegments==1`、`HorizontalCandidates==1`、
  `CandidateRejections` 含 `LAG_WITNESS_MISSING@8`、`RenderRootSpanXZ > EpsilonU`、`Label==NoThresholdEvent`。
- **与 t74 原文的差异（已由 m02645 裁决：不追加语义）**：t74 要求「存在代表段」，但按 v2 语义
  代表段只赋给 M1 或 AUTH 段，NON_M1 段不设代表（`CandidateA` 保持 -1）。本票改为断言
  `CandidateA==-1`（无代表）+ `HorizontalStallSegments==1`（停段存在），不为了「有代表」去改生产语义。

### 2.3 `Bug1ProbeM1RuleTests.cs` — R02 追加对称抖动夹具（T74-F2 配套）

- `Window(5)`；V 全程 0；A = 0, +0.0017, -0.0017, 0（每帧与段首差 0.0017 ≤ ε，但成对跨度 0.0034 > ε）；
  帧 4 释放 A=V=0.08。
- 断言：`Label==NoThresholdEvent`、`AuthSegments==0`、`NonM1Segments==1`、`StrictM1Events==0`、
  `CandidateRejections` 含 `LAG_WITNESS_MISSING@4`。
- 该夹具在改前（距段首口径）会落 AUTH，改后落 NON_M1 → 同时充当 T74-F2 的行为证明。

### 2.4 `Bug1ProbeM1RuleTests.cs` — R10 追加两条（T74-F3）

- ① 取 R02 形状（A=V=[0,0,0,0,0.08]，形成候选并落 AUTH 前先过关联校验），设 `Frames[2].HasLastInputSeq=false`
  → 断言 `Label==Unknown`、`Reason=="INPUT_SEQ_MISSING"`。
  （关联校验只在有候选时执行，故必须先构造出候选；无候选时短路到 NoThresholdEvent 属既有语义。）
- ② 同形状，设 `Frames[2].ModelEndpointResolved=false` → 断言 `Label==Unknown`、`Reason=="MODEL_ENDPOINT_UNRESOLVED"`。

### 2.5 不改的部分

- `WhitelistTests/Program.cs` **不在 t77 inScope**：新增夹具全部塞进已注册的 R02/R07/R10 方法内，
  用例编号仍为 Bug1-70…Bug1-81（共 82 条），不新增注册行。
- `Bug1ProbeRuntime.cs`、`Bug1ProbeModel.cs`、`SteamP2PFriends.csproj` 不动。

## 3. 变异计划（四条，均应在仓库外副本跑，逐字节还原）

| 变异 | 改法 | 应红 |
| --- | --- | --- |
| MV-1 | `ClassifyHorizontalMotion` 的 M1 判据换成 `if (ComputeRenderRootSpanXZ(frames, s-1, e) > eps)` | Bug1-77（R07） |
| MV-2 | `IsAuthStepLocked` 回退成「距段首 ≤ ε」 | Bug1-71（R02 对称抖动） |
| MV-3 | 删 `Judge` 里 `ValidateCandidateAssociations(...)` 调用 | Bug1-80（R10 ① INPUT_SEQ_MISSING） |
| MV-4 | 删 `ValidateFrames` 里 `!f.ModelEndpointResolved → MODEL_ENDPOINT_UNRESOLVED` 一行 | Bug1-80（R10 ②） |

## 4. 验收对照

| t77 acceptance | 落点 |
| --- | --- |
| R07 改为累计 > 3ε、逐帧 ≤ ε + 四条断言 + MV-1 必红 | §2.2 + §3 MV-1 |
| AUTH 改段内最大成对跨度 + 对称抖动落 NON_M1 | §2.1 + §2.3 + §3 MV-2 |
| R10 追加 INPUT_SEQ_MISSING / MODEL_ENDPOINT_UNRESOLVED | §2.4 + §3 MV-3/MV-4 |
| 三命令 `-t:Rebuild` 全绿（仅既有 GBA01/NIL01 失败） | 交件前跑 |
