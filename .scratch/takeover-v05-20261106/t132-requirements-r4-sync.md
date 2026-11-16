# T132 / T131 四项 finding：Bug2 r4 需求文档同步

- 日期：2026-11-08；作者：gpt-analyst；任务 t132，attempt `8634eb9d-ab1f-45a7-aa09-054719a2e060`。
- 角色与结论：需求文档同步完成；仅对下列文档新字节判定，不是生产实现审查、双轴 CLEAN、实机验收或部署许可。基线仍为 0.2.4.10，目标 0.2.4.11。
- 输入：[T131 规格轴报告](review-spec-t129-r1.md)，SHA256 `8fec313ee93e0bea10b660b4fd28084e507493c1d24815cc5e6c9dea90824b5e`；本票只处理其中 F1–F4 文档缺口。

## 1. 本票写入与字节绑定

| 文件 | 写入前 SHA256 | 写入后 SHA256 | 写入后大小 / 行数 |
|---|---|---|---|
| [requirements-v2.md](requirements-v2.md) | `874f3db8db788839222d21f74a6a2e1f97e244d942ae0f2556d7cbe8d1f40b53` | `a6011822eb0e3713e6ba0c8321dfda6dca179793a0d7d53eba7c4d0d98fe48b9` | 25278 B / 139 行 |
| [bug2-audio-leaf-spec.md](bug2-audio-leaf-spec.md) | `4de87f043ec4fa478ab015c9b7d520c3ce2e0b4f70d0c72024d933017a5c60bd` | `1987be40f3c7648cc07555dcc2d26d572563c191fe412788fe52fa3f9ac73157` | 48864 B / 248 行 |

除两份文档的定点修订外，仅新增本报告；没有写入生产源码、测试、发布目录或团队状态文件。历史 T1/T13/T17 的哈希保留为当时输入身份，不冒充本次新字节。本文自身 SHA256 由最终工具回执及任务结果提供，避免文内自引用哈希。

## 2. 四项验收与逐条命中

### T131-F1：注册失败向上传播

- **需求命中**：[requirements-v2.md:118](requirements-v2.md#L118)，`R3-REG [自动化] 注册失败传播`，独立可判违反的注册链验收条款。
- 明确诊断阶段 `U3-REG-06-Probes` 中枪声初始化返回 false 或抛异常，stage 必须 `ok=false`，由注册计划记录失败；不得 catch 后静默继续并记 ok。成功也不能把同阶段已有 false 改回 true；不改变其他 stage 的独立结论。
- 与 Bug3 阶段失败规则一致；注明 **Bug2-33 / ApplyGunshotModuleOutcome** 自动化覆盖点。没有借此关闭 S1–S4 或真实声音验收。
- **相关规格命中**：[bug2-audio-leaf-spec.md:248](bug2-audio-leaf-spec.md#L248) 同步上述需求与覆盖编号。

### T131-F2：完整登记与测试边界

- **需求入口命中**：[requirements-v2.md:75–77](requirements-v2.md#L75)；完整清单位于 [bug2-audio-leaf-spec.md:216–246](bug2-audio-leaf-spec.md#L216)。
- 任务原清单不是“13 个 nullable 字段”：13 个可写字段 = 11 个引用/委托（生产默认 null）+ 2 个 bool（生产默认 false），另有 1 个只读属性、4 个方法；补列 Patch.ResetForTest 清理依赖后，共 19 条登记、5 个方法。属性和方法不存在默认 null 语义。
- 全部明确**仅测试用、非受支持配置面**；不声称生产方法不读取这些字段，而规定生产不得赋非默认替身或调用测试入口。引用/委托默认 null，bool 默认 false，真实默认路径保持真实目标、校验与初始化。
- [bug2-audio-leaf-spec.md:244](bug2-audio-leaf-spec.md#L244) 单列三条红线：`SkipFirePatchForTest`、`SkipAnchorVerifyForTest`、`FireTargetForTest` 实质改变安装路径，**不得被当成受支持配置**。替身 Green 不替代真实安装/终点验收。
- [bug2-audio-leaf-spec.md:246](bug2-audio-leaf-spec.md#L246) 要求 finally 先卸载用例 patch 再 reset，Diagnostics 委托单独复 null，不把状态传入后续用例或生产；仅规定责任，不追授现有用例审查 PASS。

| 完整成员（ops_grep 命中） | 音频规格行号 |
|---|---|
| `GunshotHostAudioPatch.FireTargetForTest` | [224](bug2-audio-leaf-spec.md#L224) |
| `GunshotHostAudioPatch.PlayGunshotTargetForTest` | [225](bug2-audio-leaf-spec.md#L225) |
| `GunshotHostAudioPatch.SkipAnchorVerifyForTest` | [226](bug2-audio-leaf-spec.md#L226) |
| `GunshotHostAudioPatch.SkipFirePatchForTest` | [227](bug2-audio-leaf-spec.md#L227) |
| `GunshotHostAudioPatch.PostfixVerifierForTest` | [228](bug2-audio-leaf-spec.md#L228) |
| `GunshotHostAudioRuntime.FactsProviderForTest` | [229](bug2-audio-leaf-spec.md#L229) |
| `GunshotHostAudioRuntime.LeafInvokerForTest` | [230](bug2-audio-leaf-spec.md#L230) |
| `GunshotHostAudioRuntime.LeafResolverForTest` | [231](bug2-audio-leaf-spec.md#L231) |
| `GunshotHostAudioRuntime.AudioSourceProbeForTest` | [232](bug2-audio-leaf-spec.md#L232) |
| `GunshotHostAudioRuntime.RandomStateSaveForTest` | [233](bug2-audio-leaf-spec.md#L233) |
| `GunshotHostAudioRuntime.RandomStateRestoreForTest` | [234](bug2-audio-leaf-spec.md#L234) |
| `GunshotHostAudioRuntime.CollectObserverForTest` | [235](bug2-audio-leaf-spec.md#L235) |
| `GunshotHostAudioRuntime.ResetForTest()` | [236](bug2-audio-leaf-spec.md#L236) |
| `SteamP2PFriendsPlugin.GunshotModuleInitializerForTest` | [237](bug2-audio-leaf-spec.md#L237) |
| `GunshotHostAudioControl.ConfigPathForTest` | [238](bug2-audio-leaf-spec.md#L238) |
| `GunshotHostAudioControl.ResetForTest()` | [239](bug2-audio-leaf-spec.md#L239) |
| `GunshotHostAudioControl.ApplyForTest(Harmony harmony, bool enabled)` | [240](bug2-audio-leaf-spec.md#L240) |
| `GunshotHostAudioLedger.Reset()` | [241](bug2-audio-leaf-spec.md#L241) |
| `GunshotHostAudioPatch.ResetForTest()`（补列清理依赖） | [242](bug2-audio-leaf-spec.md#L242) |

### T131-F3：UNKNOWN 与关闭手段一致

- **Mod/第三方路径 UNKNOWN 命中**：[requirements-v2.md:71](requirements-v2.md#L71) 与 [bug2-audio-leaf-spec.md:203](bug2-audio-leaf-spec.md#L203)。缺真实 Mod/活跃 hooks 身份、同 shot 关联及停补记录；“安全网设计理由”和自动化守卫不能作为 Mod 路径覆盖证据。
- **S3 命中**：[requirements-v2.md:67](requirements-v2.md#L67)。离线只验证插入点/分支边界，真实原生副作用对照保持 UNKNOWN，待 **Unity-SDK 集成**关闭；若随真机 1H+1G 执行，同时记录真机标签与原生对照端点，不以听感代替弹药、shot count、耐久、弹壳、projectile 对照。
- **规格分层命中**：[bug2-audio-leaf-spec.md:179–181](bug2-audio-leaf-spec.md#L179)：S1/S4 可听结果是真机，S2/S3 真实 RNG/原生副作用是 Unity-SDK；随真机会话采集则双标签。不会将自动化标签当真机/SDK PASS。
- Guest A↔Guest B 互听与原生 playGunshot 观察器仍 UNKNOWN，没有扩展双人证据的结论范围。

### T131-F4：回查守护编号

- **需求命中**：[requirements-v2.md:69](requirements-v2.md#L69)：安装/回查/守卫集合引用 Bug2-20/29/31，明确“回查守护专门由 **Bug2-31** 覆盖”；Bug2-29 是补偿叶深度守卫，不再将 20/29 写成回查覆盖点。
- **规格命中**：[bug2-audio-leaf-spec.md:248](bug2-audio-leaf-spec.md#L248) 同步专门回查项与分层 UNKNOWN 边界。

## 3. 核对手段、结果与并发边界

- ops_grep 对两份文档执行条款/成员组合检索，完整返回 33 个命中（scanComplete=true、pageComplete=true）；上表及 F1/F3/F4 已列逐项行号。工具读取新字节后复核关键段落与修订 SHA256；`sha256sum` 与 `wc -c` 独立确认 §1 两份目标身份与大小。
- 未编译、未运行生产/测试程序、未部署、未实机采集。Bug2-33 在这里是需求覆盖点引用，不是本票新跑出的 Green。
- 曾误将空白检查扩大到全工作树：`git diff --check` 退出 2，输出含不在本票范围的既有/并发源码与测试 CRLF 尾空白；已调查为范围不当，没有修复或改写那些文件。随后 `git diff --check -- .scratch/takeover-v05-20261106/requirements-v2.md .scratch/takeover-v05-20261106/bug2-audio-leaf-spec.md` 退出 0；该命令对未跟踪文档可能无 diff，因此不拿它单独证明内容完整，也不宣称全树 CLEAN。
- 本票没有任何生产/测试写调用。只读源码抽样发现 [GunshotHostAudioPatch.cs](../../Core/Patches/GunshotHostAudio/GunshotHostAudioPatch.cs)、[Diagnostics 注册源码](../../Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs) 与 [GunshotHostAudioTests.cs](../../WhitelistTests/Evidence/Runtime/GunshotHostAudioTests.cs) 在读取开始与终检间有并发字节变化；不能把它们宣称为“全程哈希未变”，也没有回滚。测试缝表注明定位是本次读取时的 r4 源码，不为后续并发实现授予匹配/审查结论。下游请仅以 §1 文档身份复审本票；新生产候选仍须各自冻结、验证与双轴审查。

## 4. 交接判定

T131-F1/F2/F3/F4 的文档缺口均有可复核条款与命中；只关闭本票需求同步。后续生产候选、SDK 与真机 UNKNOWN、独立双轴 CLEAN、版本及发布指纹绑定均不由本票替代。
