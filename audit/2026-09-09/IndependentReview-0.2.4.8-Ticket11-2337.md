# Ticket 11 独立审核正式结论:PASS

> **归档说明**:本报告由独立审核实例(全新对话窗口)于 2026-09-09 23:37 出具。审核对象 HEAD `435f404`(分支 `codex/structure-baseline-0.2.4`,工作树全清),实施票增量 diff 为 `5475eb7...7d0f5fd`(HEAD 相对实施提交仅审计/文档索引变更,已核实)。本报告为审核实例在仓库内新建的唯一文件。

---

## 一、总裁决

**PASS。** 7 项独立复跑门禁全部通过;Spec/Standards 双轴审查官(全新实例、`subagent_type` 显式指定、并行调度)均返回 **CLEAN**;上轮 FAIL 唯一根因(DLL 指纹为路径绑定身份,pdbonly 内嵌绝对 PDB 路径)经本轮独立跨路径确定性实验实证已修复。Ticket 11 具备关闭条件,票据关闭、Migration Manifest 写回与 audit/README 索引登记由主工作树窗口执行,本实例只出裁决。

审核链:`audit/2026-09-08/IndependentReview-0.2.4.8-Ticket11-0751.md`(FAIL,路径绑定指纹)→ `7d0f5fd`(合并实施票:portable+PathMap / R1 容忍 / M6P31 补强 / M6S11)→ `audit/2026-09-09/Runtime-0.2.4.8-Ticket11-2314.md`(三端新指纹绑定)→ 本轮 2337(独立复跑 + 双轴审查)。

## 二、独立复跑门禁(本机实测)

| # | 门禁 | 结果 | 实测证据 |
|---|---|---|---|
| 1 | Release Rebuild 双项目 0E/0W | ✅ PASS | MSBuild(VS18 Insiders)主项目与测试项目 Rebuild 均成功,`-v:minimal` 输出零 error/warning 行;临时树另两轮构建同样零 error/warning |
| 2 | 唯一测试入口 260/260 | ✅ PASS | `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` 实测 **260/260 PASS (Failed: 0)**;Runtime 类诚实输出 `PENDING`;BuildArtifact 自报指纹与二进制逐项一致 |
| 3 | 指纹产物核验 | ✅ PASS | `Result: PASS` + `INDEPENDENT_ARTIFACT_VERIFICATION_PASS`;五项全命中:SHA-256 `5DF5A1F3…9845375`、MVID `2ff47d8f…7567df5e023`、版本 `0.2.4.8`(版本/程序集/FileVersion 三处)、GUID `com.yu80rice.steamp2pfriends`、Case-ID `SPF-0.2.4.8-Experimental-StructureBaseline` |
| 4 | **跨路径确定性实验** | ✅ PASS | 源树复制至仓库外兄弟目录(排除 bin/obj/.git,共享 `..\Libs`)→ 两项目 Rebuild → **两个程序集 SHA-256 与 MVID 逐字节一致**(详录见 §三);实验后临时目录已删除并确认不存在 |
| 5 | git diff --check | ✅ PASS | 工作树 vs HEAD、`git show --check 7d0f5fd`、`git show --check 435f404` 均 exit 0,无空白错误 |
| 6 | Evidence Class 布局 | ✅ PASS | `EVIDENCE_CLASS_LAYOUT_PASS`;实测 PureMemory 31 / StaticIL 14 / BuildArtifact 1 / Runtime 1 |
| 7 | Ticket09 文档核验 | ✅ PASS(语义等价口径) | 脚本 PS5.1 原样运行在**解析期**即失败(缺陷定性见 §四);显式 UTF-8 语义等价验证:5 文档、21 条必需字符串全字样命中,version/GUID/Case-ID 与 `Build/Version.props` 一致 |

## 三、上轮 FAIL 根因修复验证(门禁 4 详录)

主树(HEAD 435f404,Rebuild 后)与临时树(源树复制至 `…\DevelopMyUNMultiplayerModAndModloader\SteamP2PFriends-0.2.4-Experimental-CrossPathReview`,排除 bin/obj/.git)各 Rebuild 后比对:

| 程序集 | 比对项 | 主树 | 临时树 | 一致 |
|---|---|---|---|---|
| SteamP2PFriends.dll | SHA-256 | `5DF5A1F34CEF605B33567ED76C4854A21C41F840236BC6F1AE33111639845375` | 同左 | ✅ 逐字节 |
| SteamP2PFriends.dll | MVID | `2ff47d8f-e59a-4b5f-b7db-f7567df5e023` | 同左 | ✅ |
| SteamP2PFriends.WhitelistTests.exe | SHA-256 | `32B9B7B510A6B9EDA84E44CF4B898E13EFC2CB7714A7BCA20AA80A837E085F00` | 同左 | ✅ 逐字节 |
| SteamP2PFriends.WhitelistTests.exe | MVID | `b19b0910-544b-483a-ae86-3258a4ce6bdb` | 同左 | ✅ |

主 DLL 指纹与实施票记录、Runtime-2314 三端自报、门禁 3 预期值四方逐字节一致。**结论:Release `portable` PDB + `PathMap($(MSBuildProjectDirectory)=/_/)` 已使构建指纹从"路径绑定的一次性构建事件身份"变为"源码提交的复现身份"**,上轮 FAIL 根因消除。测试 exe 指纹为本轮首次登记的 provenance(见 §九-5)。

## 四、门禁 7 已知环境缺陷定性(存量,非本轮回归)

- 实测:脚本首 3 字节为 `24 45 72`(`$Er`),即 UTF-8 **无 BOM**;Windows PowerShell 5.1 按 ANSI(GBK)解析脚本,中文字符串字面量在**解析期**即破坏语法(报"表达式或语句中包含意外的标记")——实际失效点早于票面记载的 `Get-Content -Raw` 匹配期。
- 处置:按票据既定口径执行显式 UTF-8 语义等价验证——镜像脚本全部检查逻辑(5 文档 × 必需字符串、`Version.props` 变量替换、Ordinal 匹配),仅将文档读取改为 `[IO.File]::ReadAllText(…, [Text.Encoding]::UTF8)`,经 `-EncodedCommand`(UTF-16LE,绕开 bash→PS 编码链)执行,21 条全命中,PASS。
- 建议(不阻塞):后续小改动为脚本补 UTF-8 BOM 或在指纹记录中注明该脚本仅限 pwsh/UTF-8 环境执行。

## 五、Spec 轴(独立审查官报告)

> 逐项核对未发现遗漏、范围蔓延或错误实现,结论 **CLEAN**。
>
> - **可复现指纹**:Spec 要求"将 Release 改为 Portable PDB(消除 DLL 内嵌绝对路径)"(`audit/2026-09-08/IndependentReview-0.2.4.8-Ticket11-0751.md:48`)。两 csproj Release 均改为 `portable` 并加入 `PathMap`;Runtime 复核确认"同源码异路径构建 SHA/MVID 相同"(`Runtime-0.2.4.8-Ticket11-2314.md:10`),本轮门禁 4 独立复验成立,要求已闭环。
> - **R1 过时移除容忍**:Spec 要求"generation gate 拒绝改为'过时移除容忍'(记录日志不抛出)"(`Runtime-2343` R1 处置)。`ResourceDomainAdapter.cs:177-186` 已移除抛异常,仅记录 `stale-removal-tolerated`;新增 M6S11 确认旧 token 不抛出且新 token 复制状态保留。
> - **M6P31 补强**:`ResourceProductionControlSeamTests.cs:576-597` 同时断言成功区域 lease generation、失败区域 10s 到期重试成功、登记清除及全程零补偿恢复,覆盖 1133 §3 原始验收口径(上轮 G 项 PARTIAL 已消除)。
> - **日志埋点**:`ResourceProductionControlSeam.cs` 两处失败日志经 NoInlining `DescribeAcquireFailure` 承载 exception Message,符合 R1 处置建议与 StaticIL 分类契约;无额外未授权行为(R2/R4 已登记为后续票,不构成本 diff 缺失)。

## 六、Standards 轴(独立审查官报告)

> 成文硬违规:**无**。结论 **CLEAN**。
>
> - 本 diff 未改 Harmony/频道/GUID/`Build/Version.props`、未改票据 Status、未设第二测试入口。
> - **中文注释**:适配器/测试新增注释为简体中文(夹技术标识符),符合 `AGENTS.md`。
> - **M6S11**:落在 `WhitelistTests/Evidence/PureMemory/Adapters/Resource/`,由 `Program.cs` 唯一 `RunTest` 注册;符合 Ticket 08 单一测试入口纪律与 ADR 0006 Evidence Class 物理布局;未把 Runtime 门禁标成已过(Ticket 11 仍 `implemented-pending-runtime`)。
> - **PathMap**:仅两 csproj Release 组改动,未触碰结构不变量;对齐 `output-review-loop.md`"构建指纹必须是可复现身份(不依赖构建绝对路径)"。
> - **判断性坏味道(不阻断,点名延期)**:① 疑似 Divergent Change——同一提交捆指纹可复现、过时移除容忍、Seam 日志埋点、M6P31 断言加长四类关注点;② 疑似 Duplicated Code——两 csproj 的 PathMap/DebugType 重复(与既有分项目 Release 组结构一致,可接受)。

## 七、Runtime 证据核验与关闭条件 4 裁决

按 `Runtime-0.2.4.8-Ticket11-2314.md` 核验:三端(1H+2G)BuildFingerprint 自报行与 `7d0f5fd` 构建记录逐字节一致;R1 容忍路径 4/4 生效、SnapshotRemove failed 0;`region-snapshot-failed` 0;LeaseAcquire success 516 / deferred 2317 / failed 0;采伐 7/7 accepted 且 regionGeneration 推进;碰撞/租约释放/滞回/重入正常;双端 SnapshotReceive/DeltaReceive 正确(主机侧 skip=host-does-not-decode-client-delta);ObserverDisconnect 6 次 success。

**关闭条件 4(唯一 Authority Writer、租约/generation/复制因果链 Runtime 通过)裁决:成立。** R4(本报告期新缺陷:deferred 区域退出事务 demand 失衡 → `UpdateObserver` 事务内 `DecrementDemand` underflow throw → M0 会话重建 4 次)按 9-07 轮对 R1 的定性先例处置:**防护语义在工作(underflow fail-fast 本身正确),throw 失败方式是工程缺陷,不构成"防护失效"**;影响限于重连/离开瞬间的秒级恢复窗口(会话重建后 Resource 立即重新 success,树木资源用户无感)。

**R4 处置(如实记录)**:登记为**审核后首批修复票**;修复方向依 Runtime-2314 §3(retry 清除纳入事务补偿列表治本 + demand=0 过时退出对称 R1 容忍兜底 + PureMemory 回归测试);**R4 修复效果的 Runtime 验证归属 R4 票自身**(修复后重建 + 用户重跑 1H+2G,预期 0 fault、无会话重建),不回贴本票。R2(deferred 2317 次静默稳态)维持既有低优先级,可并票处理。

## 八、票据关闭条件对照(票面 5 项)

| 票面条件 | 本轮状态 | 依据 |
|---|---|---|
| 1. Migration Manifest 记录旧 Resource 租约释放 Writer 退出 | ✅(既有 [x],维持) | `migration-manifest.md` 在门禁 7 语义等价核验中命中 `'Runtime | PENDING'` 等必需串 |
| 2. 共享 Case-ID 的 Host/Guest/多观察者/重连/离开运行时证据 | ✅ | Runtime-2314:三端同指纹同 Case-ID;ObserverDisconnect 6 success;ConnectionGeneration 2/3 |
| 3. 碰撞/采伐/租约释放/generation 防护/双端复制通过 Runtime Gate | ✅ | Runtime-2314 §2:CollisionActivation 6376、HarvestDead 7/7、LeaseRelease 452/Scheduled 287、generation→2、双端复制事件齐全 |
| 4. Host/Guest 日志与当次 DLL SHA-256/MVID/版本/GUID 一致关联 | ✅ | Runtime-2314 三端自报行 + 本轮门禁 3/4 证实该指纹为**可复现身份**(上轮此项 FAIL 的直接修复闭环) |
| 5. 独立审核 PASS 后方可标记完成 | ✅ | 本报告 |

## 九、遗留与判断性事项(全部不阻塞)

1. **R4 修复票**(首批,先于下一轮动态测试):根治 `ProcessSingleRegionEntry` 尾部 retry 清除不在事务补偿列表的失衡态。
2. **R2**(deferred 静默稳态降级)低优先级,可与 R4 并票。
3. **门禁 7 脚本编码缺陷**(存量):建议补 BOM 或标注 pwsh/UTF-8 专用。
4. **Standards 轴判断性坏味道两条**(§六):按审查循环作为可延期项点名,随 R4 或后续票顺带评估。
5. **测试 exe 指纹 provenance**:`32B9B7B5…`/`b19b0910…` 为本轮首次实测登记(跨路径可复现已证),建议随 R4 票写入指纹记录。

## 十、审计纪律披露

- 全程只读纪律执行:除本报告外未在仓库内创建/修改任何文件;开工与收工 `git status` 一致(全清,exit 0)。
- 审核过程仅在仓库外创建临时兄弟目录(门禁 4 跨路径实验),实验后已删除并确认不存在。
- 门禁 1 要求的 Rebuild 覆盖了 `bin/Release` 构建产物;因指纹本轮已实证可复现,重建产物与既有基线逐字节一致,无历史字节损失。
- 门禁 2 测试入口为控制台 stdout 输出,未产生仓库内文件写入(已核实 `WhitelistTests/Program.cs` 无文件写调用)。
