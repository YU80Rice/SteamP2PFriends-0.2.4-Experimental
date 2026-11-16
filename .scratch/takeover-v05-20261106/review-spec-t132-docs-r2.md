# T134 规格轴文档复审：t132 是否关闭 T131-F1～F4

- 审查官：reviewer-spec（规格轴独立复审，round 2）
- 任务：t134（attempt 2 `4ffa5c32-bf32-40e4-8503-97a124d999d6`），被审 t132
- 判定：**pass**——四条文档 finding 全部关闭；另附 1 条 low 附注（行号锚点相对当前候选已漂移，不阻塞）
- 边界：**只判文档条款是否闭合**。pass ≠ Bug2 已修复 ≠ 主机能听见 ≠ S1–S4 通过 ≠ 0.2.4.11 可出包。本轮未读生产实现做功能审查、未跑构建/测试、未改任何文件。

## 1. 被审字节（本机 `sha256sum` 实测，与派工票面逐字一致）

| 文件 | 实测 sha256 | 行数 |
|---|---|---|
| `.scratch/takeover-v05-20261106/requirements-v2.md` | `a6011822eb0e3713e6ba0c8321dfda6dca179793a0d7d53eba7c4d0d98fe48b9` | 139 |
| `.scratch/takeover-v05-20261106/bug2-audio-leaf-spec.md` | `1987be40f3c7648cc07555dcc2d26d572563c191fe412788fe52fa3f9ac73157` | 248 |

本判定只对上述两个字节有效。t132 自报的写入后哈希（同值）与 `t132-requirements-r4-sync.md` §1 一致。

## 2. 逐条判定

### T131-F1（high，注册失败传播条款缺失）→ **关闭**

- 条款位置：`requirements-v2.md:118`，新增 **R3-REG [自动化] 注册失败传播**，落在 `## 7. 必须保留的回归与关闭粒度` 的 R1/R2/R3 之后，位置合理（与 Bug3 注册链同一节、同粒度）。
- 可判违反（逐项可反证，不是形容词）：
  1. 诊断阶段 `U3-REG-06-Probes` 内枪声模块初始化**返回 `false` 或抛异常**，必须向上传播为该 stage `ok=false`；
  2. 由注册计划**记录该阶段失败**；
  3. **不得 catch 后静默继续并把 stage 记为 ok**；
  4. 枪声模块成功**也不得把该阶段前序 `ok=false` 改回 true**；
  5. 本条**不改变其他 stage 的独立结论**。
- 自动化覆盖点明确：`Bug2-33`（`ApplyGunshotModuleOutcome` 的失败传播）——已在 `WhitelistTests/Program.cs:1055` 实名注册（`Bug2-33 Diagnostics Stage Propagates Module Failure (T129)`）。
- 不越界授予听感：条款末句「该条是注册链验收，不授予 S1–S4 或任何听感 PASS」。
- 无引入越界：条款只约束注册链布尔传播，未触碰音频终点、未把 stage ok 与可听性挂钩。

### T131-F2（medium，测试缝未登记、默认值表述不准确）→ **关闭**

- 需求侧：`requirements-v2.md:75-77` 新增 `### 4.1 测试缝与生产配置边界（T132 / T131-F2）`，指向规格 §9 逐项登记；`:77` 明确「**仅测试用、生产默认 null（可注入引用/委托；两个 bool 跳过开关默认 false）**，不构成受支持的配置面；**属性/方法不是可空字段**」，并给出清理责任（`finally` 卸载、`ResetForTest`、清理未被覆盖的注入值）。
- 规格侧：`bug2-audio-leaf-spec.md:216-248`（§9 T132 测试缝登记），19 项逐条给出「所属类型与成员 / 默认或只读定位 / 仅用于测试的能力」三列。
- **t132 自报的事实更正已落地且准确**：`spec:218` 明写「共有 13 个可写静态字段（**11 个引用/委托、2 个 bool**），另列 1 个只读属性及 5 个清理/测试方法；**不能把所有成员统称为 13 个 nullable 字段**」；`:238` 对 `Control.ConfigPathForTest` 标注「只读属性…不是 nullable 注入项」。
- 我独立按成员名核对了实现（`grep -n "ForTest"` 覆盖 `Core/Patches/GunshotHostAudio/*.cs` 与 `Core/Registration/...PatchRegistrationDiagnosticsModules.cs`）：可写静态字段恰 **13** 个——`Patch` 3 个引用（`FireTargetForTest:39`、`PlayGunshotTargetForTest:40`、`PostfixVerifierForTest:46`）+ 2 个 bool（`SkipAnchorVerifyForTest:41`、`SkipFirePatchForTest:42`）、`Runtime` 7 个引用（`FactsProviderForTest:64`、`LeafInvokerForTest:65`、`LeafResolverForTest:68`、`AudioSourceProbeForTest:71`、`RandomStateSaveForTest:74`、`RandomStateRestoreForTest:75`、`CollectObserverForTest:355`）、`Diagnostics` 1 个引用（`GunshotModuleInitializerForTest:105`）→ **11 引用 + 2 bool**，与文档一致；只读属性 1 个（`Control.ConfigPathForTest:31`）；方法 5 个（`Runtime.ResetForTest:488`、`Control.ResetForTest:71`、`Control.ApplyForTest:88`、`Policy.Reset:234`、`Patch.ResetForTest:203`）→ 合计 **19**，与文档一致。
- **完整性**：全仓 `ForTest` 命中集合与文档 19 项一一对应，无漏登记成员，也无文档虚构成员。
- 三个可改变安装路径的缝**被点名**且两处一致：`requirements:77` 与 `spec:245`（安装路径红线）同列 `SkipFirePatchForTest` / `SkipAnchorVerifyForTest` / `FireTargetForTest`，并写明「不得被当成受支持配置」「假目标/跳过校验/注入回查成功…不能据此宣称真实生产 Harmony 命中、真实 fire 行为或 S1–S4 Green」。
- 表述准确性抽查：文档没有把「生产方法完全不读测试缝」写成事实，反而显式声明「源码确实含这些字段的判空/默认值及测试分支，不能写成『生产方法完全不读取测试缝』」（`spec:222`），与代码 `Patch:71/79/87/116/126`、`Runtime:184/215/229/276/388/588` 的实际读取一致。

### T131-F3（medium，UNKNOWN 区缺项 + S3 标签自相矛盾）→ **关闭**

- Mod 自定义音效 UNKNOWN：`requirements-v2.md:71` 新增「**Mod 自定义音效 / 第三方重复播放路径 UNKNOWN（未验证）**」，写明「出现本地原生叶时停补并记录的行为，尚未在真实 Mod 环境验证…不外推为所有 Mod 已兼容」。
- 观察器不可实机验证：`requirements-v2.md:69` 保留并强化「原生 playGunshot 观察器不可实机验证（t127/R24-01 登记，UNKNOWN）」，给出机理（`ShooterNotLocal=true` 硬门 + SDK 仅本地 channel 调 `playGunshot()`）与「安全网而非可实机验证路径」的定性；该机理与本轮 t131 独立核对的 SDK 事实（`UseableGun.cs:513/784/958/914`）一致。
- S3 标签不再矛盾：`requirements-v2.md:67` 现为「待 **[Unity-SDK 集成]** 真实原生副作用对照关闭；若届时只能随真机 1H+1G 会话执行，则**同时标注 [真机 1H+1G]**，仍须给出该原生对照端点，不以听感代替副作用观测」，与 `:22-26` 的手段定义（[Unity-SDK 集成] 需真实播放/呈现终端观测；[真机 1H+1G] 为真实双端）自洽；全文再无 `[自动化/Unity-SDK]` 式的 S3 旧标签（`grep -n "S3"` 仅命中 `:56/:64/:67`，无旧标签残留）。
- 两条 UNKNOWN 均保留「取得前不得声称通过 / 保持 UNKNOWN」的关闭条件，未被改写成已覆盖。

### T131-F4（low，回查守护引用错配）→ **关闭**

- `requirements-v2.md:69` 现文：「自动化层只能验证其安装/回查/守卫行为（Bug2-20/29/31；**其中回查守护专门由 Bug2-31 覆盖，Bug2-29 为补偿叶深度守卫**）」；`spec:246` 同步写「回查专门项是 Bug2-31，不是 Bug2-20/29」。
- 与实名实现一致（`WhitelistTests/Program.cs`）：`Bug2-29 Supplemental Leaf Depth Guard (T127)`（`:1051`）、`Bug2-30 Initialize Returns Semantic Bool (T129)`（`:1052`）、`Bug2-31 Postfix Verify Rejection Fails Closed (T129)`（`:1053`）、`Bug2-33 Diagnostics Stage Propagates Module Failure (T129)`（`:1055`）。回查（`Harmony.GetPatchInfo` 拒绝路径）确由 Bug2-31 承担，`Bug2-29` 确为 `_supplementalLeafDepth` 深度守卫——语义区分清晰，且未再把两者混称。

## 3. 越界口径扫描（两文档全文）

正则扫描 `已修复|修复完成|能听见|听得到|可发布|可出包|可以出包|验收通过|无缺陷|已解决`：

- 命中 3 处，均为**否定式或历史说明**，无一处宣称交付：
  - `requirements:3`「不是修复完成、发布或实机通过声明」；
  - `requirements:106` 引用原票时「禁止要求退队重入作为修复完成」；
  - 其余命中落在「不能宣称正常 / 不得声称 S3 通过 / 保持 UNKNOWN」等否定语境（`:65/:67/:69/:71/:118/:139`）。
- 未见把 UNKNOWN 写成通过、未见把 `[自动化]` 绿灯写成听感通过、未见把注册链 stage ok 与 Bug2 修复结论挂钩。

## 4. 附注（low，不阻塞）：行号锚点相对当前候选已漂移

`spec:218` 声明「行号为**本次**只读源码定位」，而本轮实测代码已在本票之后被改动（`Runtime` 561→695 行、`Patch` 361→413、`Policy` 294→322、`Control` 136→141、`Diagnostics`→132；t132 报告 §1 亦已声明「测试缝表注明定位是本次读取时的 r4 源码，不为后续并发实现授予匹配结论」）。因此 19 项中 **10 项的 `#L` 锚点已偏移**：

| 成员 | 文档行号 | 当前实际 |
|---|---|---|
| `Runtime.FactsProviderForTest` / `LeafInvokerForTest` / `LeafResolverForTest` / `AudioSourceProbeForTest` / `RandomStateSaveForTest` / `RandomStateRestoreForTest` | 59 / 60 / 63 / 66 / 69 / 70 | 64 / 65 / 68 / 71 / 74 / 75 |
| `Runtime.CollectObserverForTest` | 350 | 355 |
| `Runtime.ResetForTest()` | 483 | 488 |
| `Diagnostics.GunshotModuleInitializerForTest` | 101 | 105 |
| `Patch.ResetForTest()` | 207 | 203 |

（`Patch` 五字段 39/40/41/42/46、`Control` 31/71/88、`Policy.Reset:234` 仍准确。）

- 不据此判不通过：成员名逐项精确且 t132 已显式声明行号只对当时快照有效，本票验收条目要求的是「逐项登记 + 默认值表述准确 + 三个安装路径缝点名」，均已满足。
- 建议（下一次文档触碰时顺手处理，不必单独开票）：把 19 项行号锚点重钉到当前候选字节并在 §9 头部记录该候选的源码 SHA，或干脆改为「成员名 + 类型」而不带行号，避免读者点进错误行。

## 5. 判定边界

- 本轮**未评审**测试强度、未复核 `Bug2-33` 用例本身是否可失败、未验证 S1–S4 任何手段、未运行构建/测试/exe。
- R3-REG 是**注册链验收**，其存在不构成 Bug2 修复证据；S3/Mod/观察器三条仍为 UNKNOWN，需 [Unity-SDK 集成] 或真机 1H+1G/1H+2G 关闭。
- 本 pass 仅表示：T131 四条文档缺口已按裁决闭合，且未引入新的越界表述。

self_sha256(body)=9ca551e7c39e2dd4c40b61289ccf3fc7ad837412c65efe6929beafc8bcd024d8
