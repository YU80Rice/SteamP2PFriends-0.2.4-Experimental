# t131 规格轴复审 t129：需求、UNKNOWN 清单与 r4 实现是否一致

审查人：reviewer-spec（规格轴，独立）
被审：t129（Bug2 r4：R25-01/02/03 补守护 + R25-05/06）
判定：**needs_revision**（2 条 acceptance 不成立，1 条部分成立，另有 1 条过期引用）
审查范围：只判「文档与需求是否与当前实现一致」，不评代码风格、不判用例强度（后者属 t130 工程轴）。
审查方式：只读。未修改任何源码，未运行构建（票面给了 Captain 复跑结果 832/834，本票不重复执行）。

---

## 1. 被审字节与文档绑定

| 对象 | 字节 | sha256 |
|---|---|---|
| `Core/Patches/GunshotHostAudio/GunshotHostAudioPatch.cs` | 361 行 | `6426e77f801d32f300641cb4f644d3af7137ed261054de62cdc87936b119b1c5` |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioRuntime.cs` | — | `6fb7888316bcb28180c668cd478112c2c3dad65611d18da27c72cb97a75ed4c3` |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioControl.cs` | 136 行 | `478d03c7caf876e7f8508566312f0ea49be1b88ba4be3f647c862e5386c57ec4` |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioPolicy.cs` | 294 行 | `9e0fa20e685f5314dc4683361e4f2d3db67145fc5f034bb3095341375a71ad11` |
| `Core/Patches/GunshotHostAudio/GunshotIlScan.cs` | 275 行 | `ef512ba0a8fea0540b248fdb1588e3c969b672df2e652edd4f1094c8f8dda1e9` |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs` | 128 行 | `de6b54972bddb4116583f89c18ee6aebd68f2ad3489c7022e3e5550e0ed04bf6` |
| `bin/Release/SteamP2PFriends.dll` | — | `44bc3d74ab2365e9ce78bdeafc85f2b7a1d5b9af72d78725a0886cbb27167662` |
| `.scratch/takeover-v05-20261106/requirements-v2.md` | 22850 B / 130 行 | `874f3db8db788839222d21f74a6a2e1f97e244d942ae0f2556d7cbe8d1f40b53` |
| `.scratch/takeover-v05-20261106/t129-bug2-r4-coverage.md` | 6909 B / 97 行 | `a52fdf0f…`（读时 revision） |
| `.scratch/takeover-v05-20261106/t127-bug2-r3-implementation.md` | — | t127 报告 |

判定只对上述字节有效。`git` 未改动；本报告是 `.scratch/` 下唯一新增文件。

---

## 2. 逐条判定

| # | acceptance | 判定 | 依据 |
|---|---|---|---|
| 1 | requirements-v2.md 的 S1/S2/S3/S4 与 UNKNOWN 清单与当前实现逐条对得上，无过期描述、无把 UNKNOWN 写成已验证 | **部分成立** | S1/S2/S4 与实现一致；UNKNOWN 区缺「Mod 兼容」一项、S3 收口手段与标签不自洽、:67 用例引用过期（见 §3.3、§3.4） |
| 2 | 「注册失败向上传播」在需求文档里有对应条目（不是只在代码里） | **不成立** | requirements-v2.md 全文 grep `注册失败`/`注册计划`/`ok=false`/`传播`/`ApplyGunshotModuleOutcome` **0 命中**；唯一记载在 t129 实现报告 §2 R25-03（见 §3.1） |
| 3 | 新增测试缝在需求/设计文档中有记录，且明确「仅测试用、生产默认 null」 | **不成立** | requirements-v2.md 与 `bug2-audio-leaf-spec.md`（41586 B）grep `ForTest`/`测试缝`/`仅测试`/`默认 null` **0 命中**；t129 报告写作「生产：新增测试缝」且未声明「仅测试用」（见 §3.2） |
| 4 | 观察器不可命中这一结构性事实在 UNKNOWN 区表述准确、无夸大 | **成立** | 我独立核了 SDK 调用链，与 :67 表述一致；「基本不会」的保留措辞恰好覆盖 Mod/第三方路径，未夸大（见 §3.5） |
| 5 | 禁止把 832/834 或双轴 pass 表述为 Bug2 已修复、主机可听见、0.2.4.11 可发布 | **成立** | 全 `.scratch/takeover-v05-20261106/*.md` grep `Bug2 已修复`/`主机能听见`/`可发布`/`可出包` 只命中否定式限定句；832/834 仅出现在 t129 报告的「结果摘要」表中，未与「修复」绑定（见 §3.6） |
| 6 | 报告末行 self_sha256(body)，不得改动被审源代码 | **成立** | 本报告末行给出；审查期间只读，`git status` 中生产/测试文件无新增改动 |

---

## 3. 判定明细

### 3.1 注册失败传播：需求层缺失（阻塞项）

代码事实（我实读）：`PatchRegistrationDiagnosticsModules.cs:85` `ok = ApplyGunshotModuleOutcome(ok, _harmony);`，`:107-` 的 `ApplyGunshotModuleOutcome` 在初始化返回 false 或抛异常时返回 false；该 false 经 `InitializeDiagnosticStage()` 返回给 `PatchRegistrationOrchestration.cs:44-46` 的第 6 阶段（`U3-REG-06-Probes`），成为该 stage 的失败。行为本身正确且有用例（Bug2-33）。

问题在文档侧：requirements-v2.md 的 Bug2 段（:50-69）只规定「现象 / 不在范围内 / S1–S4」，没有一条讲「诊断阶段里的枪声模块初始化失败，必须让注册计划看到 stage 失败」。全文可检索的「注册」只有 :101（历史 ticket 流程）、:109（R3 门控）、:115（工作区现状），与 R25-03 无关。

后果：这条行为目前只由 t129 实现报告的叙述保证。下一轮若有人把它改回「catch 后吞掉、stage 仍 ok」，需求侧没有可被判违反的条款；双轴审查也只能引用实现报告（可被覆盖的叙述）而非契约。对 dsh 这类「文档即契约」的流程，这是实质缺口。

### 3.2 测试缝未登记（阻塞项）

我实读到的测试缝（除 `ConfigPathForTest` 是只读属性、`Reset`/`ResetForTest`/`ApplyForTest` 是方法外，其余均为 `internal static` 字段，C# 默认值 null）：

- `GunshotHostAudioPatch`：`FireTargetForTest`(:39)、`PlayGunshotTargetForTest`(:40)、`SkipAnchorVerifyForTest`(:41)、`SkipFirePatchForTest`(:42)、`PostfixVerifierForTest`(:46)
- `GunshotHostAudioRuntime`：`FactsProviderForTest`(:59)、`LeafInvokerForTest`(:60)、`LeafResolverForTest`(:63)、`AudioSourceProbeForTest`(:66)、`RandomStateSaveForTest`(:69)、`RandomStateRestoreForTest`(:70)、`CollectObserverForTest`(:350)、`ResetForTest`(:483)
- `PatchRegistrationDiagnosticsModules`：`GunshotModuleInitializerForTest`(:101)
- `GunshotHostAudioControl`：`ConfigPathForTest`(:31)、`ResetForTest`(:71)、`ApplyForTest`(:88)
- `GunshotHostAudioLedger`：`Reset()`(:234)——测试用但**未**以 `ForTest` 命名

文档现状：requirements-v2.md 与 `bug2-audio-leaf-spec.md` 都没有「测试缝」概念；t129 报告 §2 的写法是「生产：新增测试缝 `PostfixVerifierForTest`（:46）」，指代码位于生产文件，但字面容易被读成「生产行为」。没有任何文档写明「仅测试写入、生产默认 null、用例结束后必须复位」。

其中 `SkipFirePatchForTest`/`SkipAnchorVerifyForTest`/`FireTargetForTest` 属于能实质性改变安装路径的开关，若不登记其边界，后续复用者很容易误把它们当成受支持的配置。

### 3.3 UNKNOWN 清单不完整

t127 报告 §7 明确列了「**Mod 自定义音效**：出现本地叶时停补并记录的行为未在真实 Mod 环境验证，UNKNOWN」。这条没有进入 requirements-v2.md 的 Bug2 UNKNOWN 区（:63-67）。而 :67 的观察器条目恰恰把「防 Mod/第三方重复播放」当作该安全网存在的理由——「理由是防 Mod」与「Mod 行为未验证」是同一件事的两面，只写前者会造成读者以为 Mod 路径已被覆盖。

### 3.4 S3 条目的手段标签不自洽

:54 的 S3 标签是 **[自动化 / Unity-SDK 集成]**；:65 却写「待真机 1H+1G 关闭」。§2（:24）定义 [Unity-SDK 集成] 为「需要真实 Unity 对象、原生游戏入口或播放/呈现终端的观测」，并明确「不以完整 Unity Editor 启动为默认前置」——即 SDK 观测不要求一次真实游玩会话。于是「用真机 1H+1G 关闭一个自动化/SDK 判据」在文档内部自相矛盾：要么收口手段写成「Unity-SDK 集成观测」，要么把标签改成与 1H+1G 一致。这一条不影响当前任何 PASS 判定（S3 本就是 UNKNOWN），但会让后续执行者误判关闭条件。

### 3.5 观察器不可达：表述准确（通过）

我独立核了 SDK（`.scratch/bug3-sdk-longpaths-20261030/.../Unturned/Useable/UseableGun.cs`，sha256 `19828172033b064972341126c10c5fe12c6f0a36cf318c7a578608659acc3da1`）：

- `playGunshot()` 只有两个调用点：`:513`（`project()`）与 `:784`（`shoot()`）。
- `fire()` 末尾 `:958` 仅在 `channel.IsLocalPlayer` 为真时调用 `shoot()`/`project()`。
- `ReceivePlayShoot()`（`:869-875`）会调 `shoot()`，但它由 `SendPlayShoot`（`:867`，`ClientInstanceMethod`）触发；发送目标为 `:914` 的 `channel.GatherRemoteClientConnectionsWithinSphereExcludingOwner(...)`——只含远程客户端、排除 owner。

因此 Host 侧持有的客机 `UseableGun` 实例在原生流程中不会走到 `playGunshot`，而补偿门又要求 `ShooterNotLocal=true`（`GunshotHostAudioPolicy.IsEligible` 第 7 项），两者不相交。:67 的「基本不会被触发」既没有夸大成「绝不」，也点明了它是 Mod/第三方安全网，措辞恰当，我判通过。

补充（不计 finding）：该条目写「自动化层只能验证其安装/回查/守卫行为（Bug2-20/29）」——见 §3.7 的引用过期问题。

### 3.6 口径扫描（通过）

对 `.scratch/takeover-v05-20261106/*.md` 全量扫描，`Bug2 已修复` / `主机能听见` / `可发布` / `可出包` 的命中只有两类：其他票的否定式边界声明，以及 t129 报告的「未声称 Bug2 已修复或可听性通过」。**没有**任何一处把 832/834、双轴 pass、DLL 哈希写成端到端结论。t129 报告 §7 与 requirements :69（「现有包不能给 S1/S2 授予 PASS」）方向一致。

### 3.7 过期引用（非阻塞）

:67 写「自动化层只能验证其安装/回查/守卫行为（Bug2-20/29）」。按 `Program.cs:1042-1051` 与 `:1053`：Bug2-29 是「Supplemental Leaf Depth Guard」(T127)，**回查**行为是 t129 新增的 Bug2-31「Postfix Verify Rejection Fails Closed」。故正确引用应为 Bug2-20/29/31（回查专门项是 31）。属于「无过期描述」这条 acceptance 的瑕疵，但不足以单独阻塞。

---

## 4. Findings

| id | severity | 问题 | requiredFix |
|---|---|---|---|
| **T131-F1** | **high** | r4 的「枪声模块初始化失败 → 诊断阶段 ok=false → 注册计划第 6 阶段失败」在 requirements-v2.md **没有任何对应条目**（全文 0 命中），只存在于 t129 实现报告与代码里。需求侧无法被判违反，后续回归只能引用可覆盖的叙述。 | 在 requirements-v2.md §4（Bug2）或 §7（R3）新增一条行为要求：诊断阶段 `U3-REG-06-Probes` 内，Host 枪声模块初始化返回 false 或抛异常时，必须使该 stage `ok=false` 并让注册计划记录该阶段失败，不得 catch 后静默继续；该失败不改变其他 stage 的独立结论；自动化由 Bug2-33 覆盖。 |
| **T131-F2** | **medium** | 全部 `*ForTest` 测试缝未在 requirements-v2.md 或 `bug2-audio-leaf-spec.md` 登记，也没有任何一处写明「仅测试用、`internal static`、生产默认 null、用例后复位」；t129 报告写作「生产：新增测试缝」，字面易被读成生产行为。其中 `SkipFirePatchForTest`/`SkipAnchorVerifyForTest`/`FireTargetForTest` 能实质改变安装路径。 | 在 `bug2-audio-leaf-spec.md` 增加「测试缝登记」小节，逐项列出 §3.2 的清单，注明：仅测试写入、生产默认 null、非公开配置项、`ResetForTest` 必须在 `finally` 调用；并声明「生产路径不读取这些字段，除 null 判空外无分支」。 |
| **T131-F3** | **medium** | UNKNOWN 清单不完整且内部不自洽：(a) t127 §7 的「Mod 自定义音效 UNKNOWN」未进 requirements；(b) S3 标签为 [自动化/Unity-SDK 集成] 却写「待真机 1H+1G 关闭」，与 §2 对 SDK 观测的定义矛盾。 | (a) 在 :63-67 增一条「Mod 自定义音效：出现本地叶时停补并记录，未在真实 Mod 环境验证，UNKNOWN；与观察器安全网条目互为条件」；(b) 把 :65 末句改为「待 Unity-SDK 集成观测关闭（若届时只能随真机会话执行，则同时标注 1H+1G）」，或把 S3 标签改为 [真机 1H+1G / Unity-SDK 集成]。 |
| **T131-F4** | low | :67 用例引用过期：把「回查」记作 Bug2-20/29，实际回查守护是 t129 的 Bug2-31，Bug2-29 是深度守卫。 | 改为（Bug2-20/29/31），可另注 Bug2-30 覆盖 Initialize 语义布尔。 |

---

## 5. 明确判通过的部分

- **S1/S2/S4 与实现、裁决一致**：S1（10 次成功单发、漏 0 重复 0、无弹药不计分母）、S2（观察 Host 实际播放终端、只进方法不算听见）、S4（Host→Guest 与两端自听各自记录 + 七类既有声音非回归）与 t115 §5 的分层验证口径一致，未见把 UNKNOWN 写成已验证。
- **门控与裁决可对应**：`GunshotHostAudioPolicy.IsEligible` 的 15 项（P2PHostMode/ListenHostActive/NotDedicated/Connected/ServerActive/LevelLoaded/ShooterNotLocal/RealSendAnchor/ProjectileNull/TurretFalse/WeaponInstanceValid/AudioSourcePresent/ClipPresent/NativeLeafNotEntered/AssociationComplete）是 t115 §3 的 12+ 项条件的逐项落地，无额外放宽项。文档未逐项列举（属实现细节，不计缺陷）。
- **观察器不可达论证准确**：见 §3.5，我按 SDK 源码独立复核了调用链，结论与 :67 一致。
- **口径无越界**：见 §3.6。
- **账本语义与「补偿叶调用前认领」**：`TryClaim` 在 `leaf(gun)` 之前，`Complete/Fail` 只从 `ClaimedBeforeCall` 迁移（`Policy.cs`），与需求「同一 fire 只补一次」一致；文档未展开，但也没有反向表述。

---

## 6. 边界声明

1. 本判定只对 §1 的字节集合有效；任一文件改动后需重审。
2. 本票不评用例强度、不评命名与结构（t130 工程轴职责）。§3.5 的 SDK 复核只用于验证文档表述，不构成对实现的工程评价。
3. 本票未运行构建与测试；832/834 引用自 t129 报告与 Captain 复跑，本报告不据此给任何 S1/S2/S4 结论。
4. **832/834 与双轴 pass 都**不足以说「Bug2 已修复」「Host 能听见」「0.2.4.11 可发布」。当前 S1/S2/S4、1H+2G 互听、真实 Harmony 命中、Mod 兼容、pitch RNG 仍为 UNKNOWN。
5. 本报告只新增一个 `.scratch/` 文件，未修改任何被审字节。
self_sha256(body): 3e278e8b8dda4fc70501eaa8608cbba3b048dabe7b79c6df5d0e7c26b760ba0a
