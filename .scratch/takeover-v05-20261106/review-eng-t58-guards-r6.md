# 工程轴复审 r6：t58（接线守卫加固，仅测试侧）

审查员：reviewer-eng。只读判定 + 变异全部在仓库外副本 `../_t59mut`（已 `rm -rf`）。
主树开工/收工两次取样一致：`Bug1ProbeControl.cs` 23724B/`53a6c36b…`、`Core+Platform` 聚合
`11935e2b…`、`bin/Release/SteamP2PFriends.dll` `9fe6a0c8…`。

## 0. 结论

**verdict = pass（1 medium + 2 low，均不阻塞交付）。**

t56 打回的两条 high/medium（R12-01/R12-02）**已被真正钉住**，且我用了「同一字节、前后对照」的硬证据：
t56 当时**全绿**的两个 DLL，现在都红在新增的 Bug1-53/Bug1-54 上——

| DLL sha256 | t56（旧守卫） | 现在（新守卫） | 红名单 |
| --- | --- | --- | --- |
| `894319bb32623c9a7163076f2fc89082aede20d6c9e356d9b8bb3434ecda60d` | 735/737，**0 条 Bug1 FAIL** | **736/739** | **Bug1-53** |
| `8d8f138eea221ce45e581c769027c94cd8ed5ad7d4b504199e8b3366d0b15a28` | 735/737，**0 条 Bug1 FAIL** | **736/739** | **Bug1-54** |

第二个哈希与 t56 审查报告 `review-eng-t54-wiring-r5.md:83` 记录的 M7 **逐字节相同**，即「同一颗字节，换守卫前绿、换守卫后红」，
不存在「换了个写法才碰巧红」的可能。生产侧零字节（§7），Core/ 与 Platform/ 一个字节没动。

仍存的缺口只有一条，且**不属于本票 scope**：守卫判「结果进入条件分支」，不判「失败必须拒绝启动」——
把 `return Refuse(...)` 改成「只打日志然后照常继续」仍会全绿（R13-01，medium，§5 有实测）。

## 1. 被审字节

| 文件 | 字节 | sha256 | 归属 |
| --- | --- | --- | --- |
| `WhitelistTests/Evidence/StaticIL/IlContractProbe.cs` | 21820 | `1e4f94d5f6c7654cfca54bd79740beedf0bf4c27bfd30b85608910b0e5c992fc` | 本票新增两条探针 |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeEntryRepairTests.cs` | 50803 | `028dab2d27b92b33b7cd6e9aa4ec8194b39c039da5fe298b7941d96510a45a69` | 本票新增 Bug1-53/54 |
| `WhitelistTests/Program.cs` | 141327 | `989e29d145df932a59564737ddbbe5b0b1906d8d523c810a4a2a940f21f4f987` | 注册两条用例 |
| `Platform/Diagnostics/Patches/Bug1ProbeControl.cs` | 23724 | `53a6c36bb809f7fc22a748823fbd2864d4bdc909cef76d39d75fc69cc55d617f` | **未变（与 t54 同）** |
| `SteamP2PFriends.csproj` | 27492 | `8b27ab5587b2656e0153cff2d2577c79d8d20bc2d5a5d544aadc572c732dd89e` | **未变** |
| `bin/Release/SteamP2PFriends.dll` | 1482752 | `9fe6a0c8570d2e047a0ae96aa94a831363e3e04f4f5ea12f1093c9dbbcc327e7` | **未变** |

`Core/` + `Platform/` 全部 `.cs`（排序后聚合）`11935e2b…`，与 t58 报告一致，也与 t56 时一致 → 生产零字节成立。

## 2. 三命令（主树收工实测）

| # | 命令 | 结果 |
| --- | --- | --- |
| 1 | `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | exit 0，**0 警告 0 错误**，DLL 复现 `9fe6a0c8…` |
| 2 | `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | exit 0，**0 错误**，4 条既有警告（CS0219×1、CS0649×3） |
| 3 | `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | exit 1 → **737/739 PASS (Failed: 2)** |

FAIL 仅 `GBA01`、`NIL01`（`TypeInitializationException SDG.Unturned.SteamPlayer → NetReflection → ReflectionTypeLoadException`，控制台缺 Unity）；`grep "FAIL \[Runtime\] Bug1"` = **0 行**，Bug1 段 **54/54**。

## 3. 四条关键变异（本票要证的那四条）

| 变异 | 改法（`Bug1ProbeControl.cs`） | 变异后 DLL | 结果 | 红名单 |
| --- | --- | --- | --- | --- |
| **M6**（返回值进日志、不参与判定） | `:185` → `bool enableOk = EnablePreparedRun(...); RoleLogger.Info(..., enableOk);`（`call; stloc`，无分支消费） | `7e1966f4c7f02e96408a5f8b3f04b1cc0a6c652713f2915b640dd194dbd5d0c6` | 736/739 | **Bug1-53**（仅此一条） |
| **M6′**（语句调用 + 无关分支） | `EnablePreparedRun(...); if (Environment.TickCount == int.MinValue) return Refuse(...);` | `894319bb32623c9a7163076f2fc89082aede20d6c9e356d9b8bb3434ecda60d` | 736/739 | **Bug1-53** |
| **M7**（传新建默认 outcome） | `:395` → `ApplyRetryOutcome(new Bug1RetryOutcome());`（保留 outcome 的另一处使用） | `e5228a1363b6f7d6dfdc426d4c70c83207386de6549e8c547b3625702dc78b4a` | 736/739 | **Bug1-54** |
| **M7′**（逐字节复现 t56 的 M7） | 同上，`outcome` 不再使用 | `8d8f138eea221ce45e581c769027c94cd8ed5ad7d4b504199e8b3366d0b15a28` | 736/739 | **Bug1-54** |

失败信息（控制台 GBK，此处按源码字符串还原）：

- M6 / M6′：`Bug1ProbeRepair: R12-01：EnablePreparedRun 的返回值必须参与条件判定（call 后 8 条内未见 brtrue/brfalse），实际=0`
- M7 / M7′：`Bug1ProbeRepair: R12-02：ApplyRetryOutcome 的实参必须来自 AccountRetryAttempt 的返回值（不得传新建默认值），实际=0`

四条都**只红新用例**，旧用例（Bug1-50/51）保持绿 —— 分工与 t58 报告 §4 一致，没有互相串味。
M6′ 与 M7′ 的哈希分别等于 t56 审查官 M6/M7 的哈希（`894319bb…` / `8d8f138e…`），是「同一颗字节翻红」的直接证据。

## 4. 五条既有变异回归（确认守卫之间没互相顶替）

| 变异 | DLL | 结果 | 红名单 |
| --- | --- | --- | --- |
| M1 直连 `Bug1ProbeRuntime.Enable` | `1b79cf3c395a640b3f021e4fd644050f9090f4f1155c53d4d10905bcf295ebd8` | 736/739 | **Bug1-50** |
| M2 `if (false && outcome.Unhook)` | `c25ee3afce7c4ef8d740bc8c810116127f0400358d7acc05a3d81f42a2123499` | 736/739 | **Bug1-51**（行为层） |
| M3 保留调用但永不摘钩 | `f1b0c7c4ae1c54fb2b1b2a0d8256aa0f2ea03211e5aecfbbd9fece76a8ee8d67` | 736/739 | **Bug1-51**（行为层） |
| M4 不调 `ApplyRetryOutcome` | `42dede8e3febddf33d3e1f76796c4efb329047ba5b3bf5f3738e1f831d06f86f` | 736/739 | **Bug1-51**（IL 层） |
| M5 去空判 + `Delete(path,true)` | `7f1bc5ad85daea05259e384746aabda5ae272734060ed3a06f60701bae7469cc` | 736/739 | **Bug1-45** |

五条全部只红对应那一条，与 t54/t58 记录一致（M2/M4/M5 哈希与 t58 完全相同，M1/M3 因我的文本略异而不同）。

## 5. 判据逐行审（`IlContractProbe.cs:312-434`）

### 5.1 `CountCallsWithBranchOnResult`（R12-01）

实现分两段：定位 `call` → `ResultFeedsConditionalBranch` 沿 IL 前进（跳过 `nop`，最多 `lookahead` 条非 nop）。
三种形态的实际行为我已逐条实测：

| 形态 | IL 形状 | 判据 | 实测 |
| --- | --- | --- | --- |
| 直接分支 | `call; brtrue/brfalse` | 采纳（count=1） | 生产原文（`if (!EnablePreparedRun(...))`）在 Release 下即此形态，Bug1-53 绿 ✅ |
| stloc/ldloc 暂存再分支 | `call; stloc L; ldloc L; brtrue` | 采纳 | M8 样本（`bool ok = ...; if (!ok) ...`）绿 ✅ |
| 丢弃 | `call; pop` | 不采纳（fail-closed） | M6′（`894319bb…`）红 Bug1-53 ✅ |
| 进日志不分支 | `call; stloc; ldstr; ...` | 不采纳 | M6（`7e1966f4…`）红 Bug1-53 ✅ |
| `== false` 写法 | Roslyn 直接折成 `call; brtrue` | 采纳 | M9（`bca911cd…`）绿 ✅ —— 说明「合法改写被误判红」的担心不成立 |

**`lookahead=8` 的来源**：注释给出的是「规范写法第 1 条指令即分支，8 条只用于容纳 stloc/ldloc 与 nop 填充」。
我核对了实际产物：本模块 Release IL 为 1 条（直接分支）或 3 条（stloc/ldloc/brtrue），8 有充分余量；
放宽到更大值只会削弱「即时控制决策」的语义，取值合理，不属于魔数。

**fail-closed 方向正确**：任何未识别形态（含 `pop`、字段中转、经方法中转）都判 0 → 用例红，不会静默放过。
代价是「合法但陌生的写法」会误红；M9 已验证最常见的 `== false` 不受影响，误红面很小。

### R13-01（medium）守卫判「结果进入分支」，不判「失败必须拒绝启动」——`if (!ok) 只打日志` 仍全绿

- 改动（副本实测）：`:185` → `bool enableOk = EnablePreparedRun(...); if (!enableOk) RoleLogger.Warn(...);`
  → DLL `751eacd5da83570ab9b711d7752f6d486a408428e09ee3602dfd47825500679e`，**737/739，0 条 Bug1 FAIL**。
- 语义后果：`Install` 失败只写一条 Warn，`TryStartCore` 继续打 `START_OK`、返回 true —— 与 R12-01 要防的伤害**同一后果**，
  只是把「丢弃」换成了「消费了但没拒绝」。
- 为什么不该按 R12-01 原样打回：t56 的判据目标是「返回值是否被采纳」，M8 形态**确实被读取了**；
  而「拒绝启动」属于成功语义，静态结构判据无法可靠表达。且这不是当前代码的缺陷（现码是 `return Refuse(...)`）。
- requiredFix（若要收口，成本约 20 行）：在 `ResultFeedsConditionalBranch` 命中后，继续解析该分支的**跳转目标**，
  要求目标块内出现对 `Refuse` 的调用或 `ldc.i4.0; ret`（新探针 `CountCallsWhoseFalseEdgeRefuses(target, "Refuse")`）；
  或用 `__TestInstallSeam = () => false` 的行为用例断言 `TryStart...` 返回 false（需先补 Provider/level 侧的 seam）。
- 建议：**本票不修**。作为已知边界记入交付说明；若后续要「守卫闭合到语义层」，再单独派生小票（避免本轮无限加码）。

### 5.2 `CountCallsWithArgumentFromProducer`（R12-02）

判据：目标 `call` 前第一条非 `nop` 要么是对 producer 的 `call`，要么是 `ldloc L` 且「`L` 最近一次 `stloc` 的前一条非 `nop`」是 producer 调用。

- 「先算后弃、再复用同局部」：`var o = Produce(); o = default; Consume(o);` —— 反向扫描遇到的**第一个** `stloc L` 是 `o = default`，
  其前一条不是 producer 调用 → 判 0（红）。**不会被绕过**（我按代码路径推演确认；未单独造样本，因为该形态在 C# 里需要显式覆盖赋值）。
- 「经字段中转」`Consume(_lastOutcome)`：前一条是 `ldsfld` → 判 0（红），fail-closed。
- 「直接内联」`Consume(Produce())`：前一条就是 producer `call` → 计入（正确）。
- 唯一可想到的近似态：`Consume(Condition ? Produce() : x)` 之类带分支的实参——当前生产没有，且形态变化会判红（安全方向）。
- 结论：判据方向与「fail-closed」声明一致，无「静默通过」路径；M7/M7′ 实测红。

## 6. 两条新守卫自身是否可空转

- Bug1-53 内嵌 `ProbeDiscardResult`（期望 0）与 `ProbeConsumeResult`（期望 1）两条自检；
  Bug1-54 内嵌 `ProbeFed`（期望 1）与 `ProbeDefaultFed`（期望 0）。**双向都有**，不是「只证恒真」。
- 我独立核对了自检的判别力：把探针判据改坏（例如让 `ResultFeedsConditionalBranch` 恒 `return true`），
  自检 `ProbeDiscardResult` 会读到 1 并触发 `Assert(... == 0)` 失败 —— 即自检确实能抓住「守卫空转」。
  （此路径我按代码推演确认：`ProbeDiscardResult` 的 IL 含 `pop`，恒真实现会使 count=1 ≠ 0。）
- 一点边界（**R13-02 low**）：自检样本编译在**测试程序集（Debug）**里，其 IL 形态与生产 Release 不同；
  生产侧的「否定样本」只能由变异提供（M6/M6′）。也就是说：自检证明「判据在 Debug 形态上双向有效」，
  而「在 Release 形态上有效」是靠 Bug1-53 的正向断言 + 本条票的变异证据共同证明的。已在报告中如实登记，不算缺陷。

## 7. 生产零字节与边界

| 对象 | 开工 | 收工 | 结论 |
| --- | --- | --- | --- |
| `Bug1ProbeControl.cs` | 23724B `53a6c36b…` | 23724B `53a6c36b…` | 未变 |
| `SteamP2PFriends.csproj` | `8b27ab55…` | `8b27ab55…` | 未变 |
| `Core/` + `Platform/` 全部 `.cs` 聚合 | `11935e2b…` | `11935e2b…` | 未变 |
| `bin/Release/SteamP2PFriends.dll` | `9fe6a0c8…` | `9fe6a0c8…` | 未变（确定性构建） |

- Bug3 五文件仍 `a89f40b9…`/`7a82de8e…`/`3d5044f7…`/`a4553385…`/`5b0c4e9b…`。
- `find Release publishi -newermt "2026-11-07 08:45" -type f` → 无输出。
- 本票只涉及 3 个测试侧文件；测试工程用 `Evidence\**\*.cs` 通配，未改 csproj、未新增文件。
- `IlContractProbe.cs` 属 `WhitelistTests/Evidence/StaticIL`（测试基建），**不属于** Out-of-scope 里的生产面；
  新增两个方法均为 `internal`，不改变该类的既有对外形状。

## 8. 口径与交付表述

- 报告 §首行 `spec_delta=not_rerun` 与 §10 两处均写明，并继承 `review-spec-t54-r4.md`（文件存在，14482B）。
- §9 四项实机 UNKNOWN 逐条保留（BCL detour、启动重试时序、isDismissed、SettingChanged 再入），且明确
  「不得因本票 pass 被写成已证」。
- 全文未出现「Bug1 仪表可采 / Bug1 已诊断 / 0.2.4.11 可出包」这类越界表述；§9 结尾的口径句是反向限定。

## 9. 本票 pass 的边界（请 captain 按此对外表述）

1. **证到**：守卫能在**静态结构层**发现「返回值被丢弃」（M6/M6′）与「outcome 被整体丢弃」（M7/M7′）两类回归，
   且只红新增用例，不误伤旧守卫；生产字节零变化。
2. **没证**：仪表在真实主机上能采到数据；Bug1 根因已定位；`isDismissed`/BCL detour/重试时序在 Mono 下成立。
3. **已知未覆盖**：`if (!ok) 只打日志` 形态（R13-01）仍会绿 —— 属语义层缺口，本票 scope 之外。

## 10. 复现命令

```bash
cd "D:/Agent-工作目录/DevelopMyUNMultiplayerModAndModloader/SteamP2PFriends-0.2.4-Experimental"

# 三命令
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe | tail -40

# 字节
sha256sum Platform/Diagnostics/Patches/Bug1ProbeControl.cs \
          WhitelistTests/Evidence/StaticIL/IlContractProbe.cs \
          WhitelistTests/Evidence/Runtime/Bug1ProbeEntryRepairTests.cs \
          WhitelistTests/Program.cs SteamP2PFriends.csproj bin/Release/SteamP2PFriends.dll
find Core Platform -name '*.cs' | sort | xargs sha256sum | sha256sum

# 变异（在仓库外副本；主树只读）
mkdir -p ../_t59mut && tar -C . --exclude=.git --exclude=.scratch --exclude=bin --exclude=obj \
  --exclude=Release --exclude=publishi --exclude=Assets -cf - . | tar -C ../_t59mut -xf -
cd ../_t59mut && dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild \
  && dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
# 副本首次构建应得 9fe6a0c8…；再用 python 脚本逐条替换 Bug1ProbeControl.cs 并以
#   cp bin/Release/SteamP2PFriends.dll WhitelistTests/bin/Release/
# 驱动 exe，解析 "FAIL [" 与 "=== Final Result"。
cd .. && rm -rf _t59mut
```

变异脚本（本轮实际使用，`D:/tmp/mut59.py`）按字典键 `M1..M7p` 做字面替换，每次断言替换次数为 1；M8/M9 为同脚本追加键。

## 11. 全量命令与哈希汇总

| 序号 | 命令 | exit | 输出要点 |
| --- | --- | --- | --- |
| C1 | 生产 `-t:Rebuild` | 0 | 0 警告 0 错误；DLL `9fe6a0c8…`（连跑两次同哈希） |
| C2 | 测试 `-t:Rebuild` | 0 | 0 错误；4 条既有警告 |
| C3 | `WhitelistTests.exe` | 1 | 737/739；Failed 2 = GBA01/NIL01；Bug1 54/54 |
| C4 | 副本首次构建（未变异） | 0 | 与主树同哈希 `9fe6a0c8…`，保证变异基线可信 |
| C5 | 8 次变异构建 + 8 次 exe | — | 见 §3/§4 表；全部 736/739，红名单单一 |
| C6 | `sha256sum` 主树收工核对 | 0 | Control `53a6c36b…`、聚合 `11935e2b…`、DLL `9fe6a0c8…` 与开工一致 |
| C7 | `find Release publishi -newermt "2026-11-07 08:45"` | 0 | 无输出（未写发布目录） |

副本已删除（`rm -rf ../_t59mut`），主树无 `_t59mut` 残留（`ls | grep -c _t59` = 0）。



<!-- self_sha256(body)=15ead64040cadfe99808165d955dea85bcbc6483cc9507f2c5c1f44f76891fed -->
