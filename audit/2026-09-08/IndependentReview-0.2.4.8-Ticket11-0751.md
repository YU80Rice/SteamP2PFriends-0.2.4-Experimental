# Ticket 11 独立审核正式结论:FAIL(产物指纹不可复现)

> **归档说明**:本报告由独立审核实例(全新对话窗口)于 2026-09-08 出具,审核对象 HEAD `5475eb7`(分支 `codex/structure-baseline-0.2.4`)。主工作树会话于 2026-09-08 07:51 原样归档,内容未改动。

---

二进制对比拿到决定性证据:**256 个差异字节,集中在 PE 头时间戳与 RSDS 调试条目——两个 DLL 各自嵌入了构建时的绝对 PDB 路径**(`pdbonly`/Windows PDB 的经典路径依赖问题)。指纹机制已完全查明。清理临时目录、确认仓库零改动,然后出具正式结论:

# Ticket 11 独立审核正式结论:**FAIL**

审核对象 `5475eb7`(分支 `codex/structure-baseline-0.2.4`)。**代码实现、测试、Standards 合规全部通过,唯一失败项是独立复跑门禁中的产物指纹核对**——但该项直接决定 Runtime 证据链能否绑定到 HEAD,因此总体裁决为 FAIL,Ticket 11 本轮不得关闭。

---

## 一、独立复跑门禁(本机实测)

| 门禁 | 结果 | 证据 |
|---|---|---|
| msbuild Release Rebuild(主项目 + 测试项目)×3 | ✅ PASS | 三轮全部 `0 errors / 0 warnings`,产物正常输出 |
| 唯一测试入口 `WhitelistTests/Program.cs` | ✅ PASS | 实测 **259/259 PASS(Failed: 0)**,Runtime 类诚实输出 `PENDING`,与文档声明一致 |
| `git diff --check` / `git show --check 5475eb7` | ✅ PASS | 两项均无空白错误,exit 0 |
| `Tools/Verify-BuildFingerprintArtifact.ps1` | ✅ 脚本 PASS | 版本/程序集版本/FileVersion 均 `0.2.4.8`,GUID `com.yu80rice.steamp2pfriends`,Case-ID `SPF-0.2.4.8-Experimental-StructureBaseline` 全部正确 |
| **DLL 指纹核对(MVID `6ae0e666…` / SHA `B6A3ACC9…`)** | ❌ **FAIL** | HEAD 重建实测 **MVID `149fa6d3-a99c-486a-98de-1ef04b384a00`、SHA-256 `46795E26…79AA9`**,与修复轮记录不一致 |

## 二、指纹不匹配根因链(本次审核查明的新事实)

这不是"构建坏了",而是**产物身份机制本身绑定构建路径**,证据链完整:

1. **HEAD 在本机路径下完全确定性**:同一命令连续 Rebuild 三次,指纹逐字节一致(`149fa6d3/46795E26`)。
2. **源树字节级一致但指纹不同**:`git archive 5475eb7` 提取到 Temp 重建,`diff -r -q` 证明提取树与工作区**零差异**(含换行符,`autocrlf=true` 下 archive 同样输出 CRLF),但产物指纹变为 `1c896f95/C57AAFCE`——唯一变量是构建绝对路径。
3. **机制定位**:两 DLL 二进制对比仅 256 字节差异,集中在 PE 头时间戳与 RSDS 调试条目;两者各自嵌入了**构建时的绝对 PDB 路径**(Release 配置 `DebugType=pdbonly`,Windows PDB 的 CodeView 条目把 `obj\Release\SteamP2PFriends.pdb` 全路径写进 DLL)。因此 **SHA-256/MVID 是"路径绑定的一次性构建事件身份",不是源码提交的复现身份**。
4. **历史指纹同样不可复现**:9a126cd(8-29 轮,记录 `8204775d/D0CEE669`)经 archive 重建得 `127a32d4/483A7918`——所有历史指纹均为 provenance 记录。注意 1123 报告里的"取证埋点前基线" DLL 实为 8-29 遗留产物(2229 报告已记录同一指纹),非 340918a 构建。
5. **对证据链的影响**:9-07 三端 Runtime 日志绑定 `B6A3ACC9`,该二进制无法由 HEAD 重建复验等价性。按仓库自身铁律(1133 §4"旧日志与旧指纹的证据不得迁移到本构建"),Runtime 证据不能回贴到 HEAD 构建上 → 关闭条件第 5 条(独立审核)不成立,第 1/2 条仅对原构建事件成立、对 HEAD 不可验证。工具链(Roslyn 5.9.0-1.26357.3,8-11 起)、Libs(最晚 9-01)、Targeting Pack(2018)均已排除变化。

## 三、Spec 轴(子智能体审查 + 本人复核)

子智能体初始裁决 FAIL,但其唯一阻塞依据("注册 260 个测试 vs 声明 259")经我实测**推翻为误报**:260 是 `RunTest(` 原始出现次数(含 helper 方法定义本身),运行时实际总数即 259。纠正后各条结论:

- **A/B/C/F/H:PASS**——三处 `native-resource-trees-unavailable` throw 已换 `ResourceNativeSnapshotUnavailableException` 且 message 不变(`ResourceRegionLifecycleAdapter.cs:491-559`);`ProcessSingleRegionEntry` 拆分、两级 catch(2s cap 32s / 10s cap 60s、不向调用方抛出)、补偿撤销、按观察者登记、孤儿清理、`PendingAcquireRetryCount` 全部落实(`ResourceProductionControlSeam.cs:527-528,607-619,674-695,742-756`);StaticIL 镜像契约与 `DescribeAcquireFailure`(NoInlining)符合 1123 §2 约束;Forensics-1046 方向 A/B 落实、方向 C 按裁决未纳入且有记录。
- **G:PARTIAL(非阻塞)**——M6P31 未独立断言成功区域 generation 与后续重试(由 M6P32 覆盖暂缓重试路径),覆盖面窄于 1123 §3 原始验收定义,建议后续补强。
- 票据状态 `implemented-pending-runtime`、Runtime-2343 关闭条件表如实标注,无越权宣称。

## 四、Standards 轴(子智能体审查):PASS

Evidence Class 物理布局、唯一入口、简体中文、结构不变量(Harmony/频道/GUID/Version.props 未触碰)、实验区隔离(未写入 Develop-Stage)、Runtime 纪律(票据未关、Runtime 仍 PENDING)、票据只追加不篡改,全部 PASS。非阻塞/判断性发现三条:① 新审计类型 `Runtime-` 前缀与 `ReviewRecheck-0.2.4.8-0933.md` 缺票号,偏离 `docs/agents/issue-tracker.md` 前进命名枚举;② Runtime-2343 §4 将关闭条件 3/4 标 ✅ 偏乐观(碰撞/采伐未单独举证、R1 仍 throw);③ 未提交的 `audit/README.md` 改动是合规的索引登记(追加 2026-09-07 Runtime 报告条目)。

## 五、处置建议(按优先级)

1. **补建立可复现的产物身份**:将 Release 改为 Portable PDB(消除 DLL 内嵌绝对路径),或在指纹记录中强制登记构建路径 + 工具链版本作为 provenance 要素;二选一后重算指纹基线。
2. **重建 + 重新记录新指纹 → 重跑 1 Host + 2 Guest 三端动态测试**绑定新指纹后,再补跑一次独立审核,PASS 后方可关闭 Ticket 11。
3. R1(`SnapshotRemove` generation gate throw 改容忍 + message 埋点)、R2(deferred 静默稳态)按 Runtime-2343 建议立为关闭后首批新票。
4. Spec 轴 G 项(M6P31 断言补强)可随 R1 票顺带处理。

## 六、审计纪律披露

- 全程未修改任何被跟踪文件(结束时 `git status` 与开工时一致:仅原有的 `M audit/README.md` 与未跟踪 `audit/2026-09-07/`)。
- **需用户知悉**:门禁要求的 Rebuild 已覆盖 `bin/Release/SteamP2PFriends.dll`(gitignore 的构建产物,原 `B6A3ACC9` 字节已被替换;其指纹完整记录于审计文档,原 DLL 若需找回可从你的 UMM 部署目录复制)。

---

**主工作树会话附注(2026-09-08 07:51 归档时记录)**:审核报告所述工作树状态与本会话当前一致(`M audit/README.md` + 未跟踪 `audit/2026-09-07/`,其中含本报告与 `Runtime-0.2.4.8-Ticket11-2343.md`,均尚未提交)。审核员另转达用户在审核会话下达的**分批写入规则**(一回合一 Edit、单 Edit ≤60 行、只插一个测试方法、写完即停等"继续"、禁止整文件重写),已登记至主工作树会话记忆,后续实现/TDD 任务严格执行。
