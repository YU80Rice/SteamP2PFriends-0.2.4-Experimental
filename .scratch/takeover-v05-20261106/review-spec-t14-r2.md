# T15 规格轴复审 r2 — Bug3 队伍身份跨局恢复（t14 修复轮）

- 复审角色：reviewer-spec（规格轴独立审查；不评命名/复杂度/注释密度等工程风格，不修改任何源码，不替对方写修复）
- 被审任务：t14（DeepSeek 修复轮，team.json status=completed）
- 复审任务：t15，attempt_id=`a31c4d27-cf0d-4cc7-b244-b94952b70ce7`
- 取样时间：2026-11-06 15:40:25（Asia/Shanghai）
- 上游契约：`.scratch/takeover-v05-20261106/requirements-v2.md`（T17 已绑定 21763B / SHA256 `17fc427c0e6ed115b680eedfa3119191c558609947051fc241facc4ebb94017e`）、`architecture-v2.md`、`review-eng-t3.md` §7（T12 Architect 裁决，206 行 / SHA256 `5e5f2ac2…`）
- 本报告自身哈希不内嵌（自引用会改变自身字节），交付哈希（大小 + sha256）随 Captain 消息与 task output 一并给出；§1 绑定的是**被审字节**。

---

## 0. 结论

**verdict = needs_revision（本票失败）**

一句话：E4 的补发实现本体、E3/E1/E2 的回滚与兜底、E5 的失败态主体，以及五项禁止项的遵守，**都已按 t14 objective 做到且证据可判**；但 Bug3 的用户级决定性判据 **T1a（同 fixture 真实存档下 none/MEMBER → 候选 α/OWNER）自 t3 起始终未交付**，自本轮的 28 条用例中依然零真实存档 fixture、零 α/OWNER 断言，故 Bug3 仍不可宣称 Green；另发现 4 条 E4/E5 层面的可修缺口（生产补发调用点未被用例绑定、原生 load 抛错不置 Degraded、租约账被会话边界抹除、注册中途失败不回滚已装补丁）。

| 编号 | 严重度 | 一句话 | 归属 |
| --- | --- | --- | --- |
| F1 | blocker | T1a 决定性自动化判据未交付（继承 t5-F1，非 t14 回归） | Bug3 整体 / 需新票或 Architect 重定义 |
| F2 | high | 删掉 `Patch:600` 的生产补发调用，28 条用例仍全绿 | t14（E4） |
| F3 | medium | 原生 `quests.load()` 抛错路径只记 LoadFailed、不置 Degraded（矩阵行⑩） | t14（E4） |
| F4 | medium | `ResetSession` 抹除旧租约账，与矩阵行⑤「旧账保留历史值」相反，且被用例固化为期望 | t14（E4） |
| F5 | medium | 注册中途订阅失败不回滚已安装的两个 patch，违反 §7.5 部分安装撤销 | t14（E5） |
| F6 | low | 矩阵行②（无队伍但有非空 flags/quests）无专测；`handler==null` 与空 payload 混记为 SkippedEmpty | t14（E4） |

判据统计（15 条，A4 拆为 a/b）：PASS 10、PARTIAL 1、FAIL 4。

---

## 1. 被审字节绑定

以下为本机实测（`stat` 大小 + `sha256sum` + mtime）。**本报告结论仅对这些字节有效**；若后续轮再改动其中任一文件，须重新取样并重新判定。

| # | 路径 | 字节 | sha256（全值） | mtime |
| --- | --- | --- | --- | --- |
| 1 | `Core/Patches/P2PGroupStateRecovery/GroupLeaseLedger.cs` | 4542 | `38f2cc84de6590dacb5310cd6487f8e88117898643cdb0ce6b9784337a805456` | 14:05:59 |
| 2 | `Core/Patches/P2PGroupStateRecovery/GroupLoadPlanner.cs` | 1344 | `a45533850f31bd96f3d63e39c56b2e5a084fc3f782b1ef984521d39fe64afb0f` | 14:06:17 |
| 3 | `Core/Patches/P2PGroupStateRecovery/GroupStateRecoveryCore.cs` | 14840 | `d7fad63c2a8960488bc4d11b7d8cf14ab5379607b87e35e72186b74441d30961` | 14:34:06 |
| 4 | `Core/Patches/P2PGroupStateRecovery/P2PGroupRegistration.cs` | 1790 | `5b0c4e9ba4c87cbeb7363b3846b27f26f464e8acad51c25bc07831d5fbe04ea4` | 14:06:41 |
| 5 | `Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs` | 37826 | `956f6a12c4fa8c9e82159ba09f7e6345e9d50dab673218c6c980655a3b1edaa8` | 15:39:25（见下注） |
| 6 | `SteamP2PFriends.csproj` | 27097 | `c4f4f7f52587dfe1a0c27f74a485a2d75f17c54e7e41fab701255aa396564b04` | 14:13:39 |
| 7 | `WhitelistTests/Evidence/Runtime/P2PGroupStateRecoveryRuntimeTests.cs` | 39047 | `2740f66f8a66cbcf4f3f16ba5522052343228f3a4c9c1bf3bec471d8ebcd468a` | 15:02:11 |
| 8 | `WhitelistTests/Program.cs` | 128012 | `b544f79884df6231a52547209c59cb9b99b45941dd60989ecee4ba9c17460952` | 15:01:16 |
| 9 | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationAuditModules.cs` | 11924 | `5d21c3a5d7f1e8f089c813dab5ecc425ac704a5605e4517c5958516fac656968` | 11:52:30 |

字节核对的两个记录：

1. **Patch.cs 的 mtime 变化但内容未变**：15:20 与 15:40 两次取样 sha256 都是 `956f6a12…`，期间 mtime 由 15:06 跳到 15:39:25；同时全仓 15:39:27 出现一次外部构建（`bin/Release/SteamP2PFriends.dll`、`obj/`、`WhitelistTests/bin/Release/`；exe 15:39:29）。按内容哈希判定，被审字节未变，构建时间晚于源码，可行。
2. **测试源码哈希不一致（需 Captain 确认）**：t14 自报 `P2PGroupStateRecoveryRuntimeTests.cs` 为 `340899f961f63ee6edd5399e627f1493d3d3d588ad618e9a6817a408ea78a281`，磁盘实测为 `2740f66f…`（39047B）。全仓库未找到以 `340899f9…` 为内容的文件。二者疑为「测试 DLL」或中间态；本报告按磁盘字节（`2740f66f…`）判定，请 Captain 明确 t14 报告里该哈希指向哪个产物，否则 E6「候选字节可追溯」在测试侧存在一处不可对齐。

---

## 2. 逐条验收判据

### A1 E4 补发是否只在「真实 load 正常返回 + 同 epoch/地图/实例 + payload 非空」时发生 — **PASS**

- 真实 load 正常返回才记成功：`P2PGroupStateRecoveryPatch.cs:569` 调 `quests.load()`，`:571` 在 try 内、返回后调用 `Core.NoteDeferredLoadSucceeded()`；`:575-587` 的 catch 只记 `NoteDeferredLoadFailed("native-load-threw")` 并 `return`，**不通知、不重试**（错误日志明确 "not retrying"）。
- 同租约三重键：`TryTakeDeferredLoad`（`GroupStateRecoveryCore.cs:212-234`）要求 epoch 与地图双等；`ExecuteDeferredLoad` 先复核 token（`Patch:557-560`）再复核 `Core.IsLeaseValid`（`Patch:562-566`，`Core:284-295` → `GroupLeaseLedger.cs` `SameLease` 要求 State!=None 且 epoch/token/map 全等）；load 之后 `Patch:593` 再复核一次。
- payload 非空：`Patch:687-694` 从原生已加载的 `flagsList.Count>0 || questsList.Count>0` 判定，不做存档解析。
- 至多一次：`TryNotifyFlagsCore`（`Patch:635-685`）先 `NotifyAttempts>0` 抑制（`:652-655`），再 `!Ledger.CanNotify()`（`NoteInvariantViolation("notify-before-load-success")`，`:656-661`），之后才 `NoteNotifyAttempted` 并调用委托。
- 顺序：`Patch:600-601` 先 `NotifyFlagsUpdated` 再 `PublishNativeGroupState`，符合 §7.3「建议 flags 通知在组发布之前」。
- 用例：Bug3-18（成功一次/二次被抑）、Bug3-19（空 payload 0 次）、Bug3-22（未成功就通知 → 不变式违规且 0 回调）、Bug3-21（二局新 token 再通知一次）。

### A2 空存档 / 空 payload 是否补发 0 且记 SkippedEmpty — **PASS**

- `Patch:647-651`：`!payloadNonEmpty || handler==null` → `NoteNotifySkippedEmpty()` → `GroupStateRecoveryCore.cs:317` → `GroupLeaseLedger.cs:121-125`（SkippedEmpty=true，State=Completed）。用例 Bug3-19 断言 0 回调 + SkippedEmpty + RealLoadSuccess==1。
- 空存档（无组）走原生默认：`Patch:702-718` 的 `PublishNativeGroupState` 在 `:706` `if (!quests.isMemberOfAGroup) return;`，不造组、不猜队伍、不 `ServerAssignToGroup`。此项无专测（见 F6）。

### A3 watchdog 降级是否 0 次且显式 Degraded — **PASS**

- 同租约第二次 Deferred：`Core:186-196` 清 pending、`_forcedPassThrough++`、`Ledger.MarkSkippedWatchdog("watchdog:repeat-deferral")`（`GroupLeaseLedger.cs:127-133`：SkippedWatchdog+Degraded+Abandoned）；返回 `ForcedPassThrough` → `Prefix` 记 Warn 并 `return true`，本次原生 load 照常执行，但模块账上永久降级，后续组表回调 `TryTakeDeferredLoad` 只会得到 None，不会再补发。
- 用例 Bug3-16（ForcedPassThrough + Degraded + NativeLoadExecutions==0）、Bug3-17（兜底后无 pending、FlushAuthorizations==0）。

### A4a 原生 load 部分执行后抛异常是否 0 次且显式 Degraded — **FAIL** → F3

- `Patch:575-587` catch → `Core:309-313` `NoteDeferredLoadFailed` → `GroupLeaseLedger.cs:91-96` `MarkLoadFailed(reason)`：只写 `State=LoadFailed` 与 `Reason`，**`Degraded` 保持 false**。矩阵行⑩要求「LoadFailed/Degraded」，且无任何用例走该路径（Bug3-20 覆盖的是 prefix 提交异常回滚，`Patch:481-499` + `AbandonPending("prefix-failed")`，该路径 `MarkAbandoned` 会置 Degraded，属 A4b）。

### A4b 异常回滚（prefix 提交失败）是否 0 次且显式 Degraded — **PASS**

- `Patch:481-499`：已提交 pending → `Core.AbandonPending("prefix-failed")` + 清 `_pendingQuests` + `NotePrefixRollback()` + Error + `return true`（把本次 load 交还原生）。`Core:246-256` + `Ledger:135-141` 置 Degraded+Abandoned。用例 Bug3-15（passed true、无 pending、PrefixRollbacks==1、TryTakeDeferredLoad==None、FlushAuthorizations==0）、Bug3-20（0 回调 + NotifyAttempts==0 + Degraded）。

### A5 三行计数矩阵是否按 epoch+map+token 记账，并写成三条可独立失败的用例 — **PARTIAL** → F4（另见 F3 的⑩变体缺失）

- 记账键：`Core:199-201` `_ledger.Begin(_epoch, mapName, playerToken)`；`SameLease` 三项全等。**满足**。
- 三条用例存在且各自断言不同计数：Bug3-28（旧 token 跨二局：`!IsLeaseValid(84,"PEI")`、0 回调、NotifyAttempts==0）、Bug3-21（二局新 token 正控：callback==2、NotifyAttempts==1、`Ledger.Token==85`）、Bug3-20（回滚后 0 + Degraded）。**形态满足**。
- **不满足**：矩阵行⑤要求「旧账保留历史值、本次增量 0」，而 `Core:145` `_ledger.Reset()` 在 `ResetSession` 中把旧租约账整体抹除（`GroupLeaseLedger.cs:41-58` `Reset()` 归零全部字段）；Bug3-28 在 `:329` 断言 `Ledger.Token != 84`，把「抹除」固化为期望值，与契约措辞相反。
- 另：矩阵行⑩（load 失败/降级）无对应用例；「三条可独立失败」未被变异证据逐条证伪（现有 E4 反例只红 Bug3-22）。

### A6 是否退化成进程级一次或玩家实例级一次 — **PASS**

- 无静态 once 标志；每次 deferred 都 `_ledger.Begin`（`Core:199-201`），通知资格取当前账（`Ledger.CanNotify` 要求 `State==LoadSucceeded && RealLoadSuccess==1 && NotifyAttempts==0`，`GroupLeaseLedger.cs:97-104`）。
- 二局正控：Bug3-21 用 token 85 重新授权并再次通知（callback 1→2），证明不是进程级一次；`ResetSession` 换 epoch 后旧 token 不再有效（`Core:284-295`）。

### A7 「恰好一次」是否仍成立（E3 后异常路径 load ≤1；E1 兜底不引二次 load/补发）— **PASS**

- E3 路径：`Patch:481-499` 撤销 pending 后 `return true`，原生只执行一次 load；随后 postfix `TryTakeDeferredLoad` → None（pending 已清）→ 不再 load。Bug3-15 断言 `FlushAuthorizations==0`。
- E1 路径：`ForcedPassThrough` 前已清 pending（`Core:186-196`），兜底后 `TryTakeDeferredLoad==None`（Bug3-17），不会再补发。
- IL 契约：Bug3-24 在 `ExecuteDeferredLoad` 内数到 `PlayerQuests.load` 恰 1、`InitializePlayer` 0、`save` 0、`changeRank` 1、`ServerAssignToGroup` 0、`ServerAssignToMainGroup` 0。

### A8 五条禁止项是否仍未违反 — **PASS**（定位检查方式见下）

| 禁止项 | 检查结果 | 定位 |
| --- | --- | --- |
| 未提前 `GroupManager.load` | 仅作 `AccessTools.Method` 目标与 `DeclaringType` 校验，无 `.load()` 调用 | `Patch:133-138`、`:260` |
| 未手写存档解析 | 5 个生产文件无 `StreamReader`/`BinaryReader`；payload 只读原生 `flagsList`/`questsList` | `Patch:687-694` |
| 未用 `ServerAssignToGroup` 冒充恢复 | 生产代码 0 处；IL 断言 0 | `Patch:702-718`；Bug3-24 |
| 未二次执行 `InitializePlayer` | 仅注释提及；IL 断言 0 | Bug3-24 |
| 未先 load 再重读 | `:569` load 一次，之后只读 `groupID/groupRank` 走 `changeRank` 回环发布 | `Patch:569`、`:702-718` |

### A9 T1 需求契约与 §4.3 一致性是否未被破坏（实质偏离须列 blocker）— **PASS**

- 门控 11 项未变（`Patch:721-750`）：`HostManager.IsP2PHostMode`、`Provider.isServer`、`Dedicator.IsDedicatedServer`、`player==Player.LocalPlayer`（引用相等）、`channel.IsLocalPlayer`、`owner.playerID != null`、`transportConnection is TransportConnection_Loopback`、`OwnerSteamId==ProviderUserId`、`OwnerSteamId!=0`、`MatchingLocalClientCount==1`、`DynamicGroupsAllowed`；另有 `CurrentSurvivalMapName`（`Patch:772-782`）排除非 SURVIVAL。
- 会话/地图就绪：`Postfix` 只处理 `level > BUILD_INDEX_MENU`（`Patch:506-537`），即在 GAME 组表回调后兑现；MENU/SETUP 边界经 `ResetSession`（`Core:138-147`）清 pending/ready/地图。空存档走原生默认（`:706`）。
- 状态发布：`changeRank(quests.groupRank)` 只走原生 `ReceiveGroupState` 回环，不 `members++`、不新增组包、不新增自定义频道。
- 结论：**无实质偏离**；T1a 未交付属需求侧缺口（A10/F1），不是与 §4.3 冲突。

### A10 T1a（同 fixture 真实存档 none/MEMBER → 候选 α/OWNER）— **FAIL** → F1

- 28 条 Bug3 用例全在纯内存对象/注入 resolver 上运行（`P2PGroupStateRecoveryRuntimeTests.cs:248-655`），无一条加载真实存档、无一条回放 `MENU → InitializePlayer/load → GAME`、无一条断言基线 `none/MEMBER` 与候选 `α/OWNER`。
- Bug3-01..12 是状态机/IL/登记契约；Bug3-13..28 是 t14 新增的回滚/兜底/账本/注册。
- t3 自报的端到端探针差异（反射为 True vs `BuildEligibility` 全 false）至今未定位，也没有替代证据；因此「`quests.load()` 会恢复 α/OWNER」目前只是对原生 SDK 的**阅读结论**，不是对生产补丁行为的观测。
- 归属：t14 objective 明确不含该条（只含 T6 三条 high + F2 + E4/E5/E6），故**不是 t14 回归**；但它是 Bug3 用户目标的决定性判据，t5 已以 blocker 记录，现状未变。

### A11 T1b/T2a/T3a/T4a 是否记 UNKNOWN + 解锁条件 — **PASS**

见 §5。本轮未新增对客机侧/真机的任何断言，实现也完全未触及 T2/T3/T4，符合「各 Bug 独立门禁、T1 Green 不得连带关闭」。

### A12 是否宣称端到端 Green — **PASS**

t14 只报构建/测试 Green 并列举 UNKNOWN；本报告亦不宣称 Bug3 Green。

### A13 §7.4 强制反例：移除生产补发或漏掉二局新 token 投递 → 授权正控必须红 — **FAIL** → F2

- 漏掉二局 token 会被 Bug3-21 抓住（正控存在）。
- 但**删除生产调用点不会红**：E4 全部用例直接调 `TryNotifyFlagsCore(payloadNonEmpty, handler, leaseValid, token, mapName)`（`Patch:635-685`）并自行构造账（`LeaseWithLoadSuccess`），而 `NotifyFlagsUpdated`（`Patch:604-629`）这个唯一生产入口在测试中**零引用**（grep 仅 `Patch:600` 一处调用）。删 `Patch:600` 或删整个 `NotifyFlagsUpdated` 后编译仍通过（私有未用方法不告警），28 条用例仍全绿。这使「生产补发接线存在」不可判，与 §7.4 明确要求的反例相悖。

### A14 §7.5 部分安装失败只撤销本模块精确 patch/委托 — **FAIL** → F5

- `Patch:141-142` 先安装 load prefix 与组表 postfix，`:151` 才 `SubscribeNativeEvents()`；订阅抛错时 `:166-170` 的 catch 只 `RecordRegistrationFailure` 再 rethrow，**未调用 `UnpatchOwned`**，导致：两个补丁留活、`State=Failed`、`_registered=false`、`_harmony=null`（后续无法清理）。`:142` 第二个 `Patch` 抛错同属此类（第一个留活）。
- 反例：`:144-149` 的 verify 失败路径**有**回滚（`UnpatchOwned`），说明作者已意识到该义务，但订阅失败分支漏掉。
- 用例覆盖：Bug3-25 只覆盖成功与同 Id 幂等，Bug3-26 只覆盖跨 Id 拒绝；无「安装后中途失败 → 不残留」用例。

---

## 3. Findings

### F1 — blocker：T1a 决定性自动化判据仍未交付

- file: `WhitelistTests/Evidence/Runtime/P2PGroupStateRecoveryRuntimeTests.cs`（全文件）；`P2PGroupStateRecoveryPatch.cs:721-750`（门控输入无真实宿主证据）
- problem: 需求 T1a 要求「继承真实存档、按 `MENU → PlayerQuests.InitializePlayer/load → GAME` 的组件顺序与真实 Host/P2P gate，先观察基线 none/MEMBER，再只更换候选 DLL 得到原 α/OWNER」。交付的 28 条用例没有任何真实存档 fixture、没有基线/候选对照、没有 α/OWNER 断言；t3 曾自报的反射 True 与内部 `BuildEligibility` 全 false 差异至今未定位。因此「主机身份跨局恢复」这一用户目标在当前字节上不可证，只剩对原生 SDK 的阅读推断。
- 影响：Bug3 不可宣称 Green；requirements-v2 §7「T1 组件 Green 不得连带关闭其余」在这里是反方向的同一原则——E4 修好不等于 T1 已验收。
- requiredFix: 二选一，且必须书面落盘：
  1) 由 Captain 建 **T1a 专用实现票**：同 fixture 真实存档 + 真实生命周期回放 + 只换候选 DLL 的 none/MEMBER → α/OWNER 对照，并先在旧 DLL 上取 Red；
  2) 由 Architect 书面将 T1a 重定义为「宿主边界不可执行」的 UNKNOWN，写明缺口、观测点与解锁条件（真机 1H+1G 或 SDK 脚手架），并在需求侧留痕。
  不得以本轮 674/676 或 E4 用例全绿代替该条。

### F2 — high：E4 生产补发调用点未被任何用例绑定

- file: `Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs:600`（`NotifyFlagsUpdated(quests, token, mapName)`），`:604-629`（方法体）
- problem: 所有 E4 用例直接调 `TryNotifyFlagsCore` 并合成账本，`NotifyFlagsUpdated` 零引用；删除 `:600` 后编译与 28 条用例均不报警。§7.4 要求「移除生产补发 → 授权正控必须红」。
- requiredFix: 增加一条经 `ExecuteDeferredLoad` **全路径**的用例（真实签入 deferred → 组表回调 Authorized → `quests.load` 正常返回 → 委托哨兵被调用恰一次），或在 Bug3-24 的 IL 断言里补「`ExecuteDeferredLoad` 调用 `NotifyFlagsUpdated` 恰 1 次」。

### F3 — medium：原生 load 抛错路径不置 Degraded（矩阵行⑩）

- file: `P2PGroupStateRecoveryPatch.cs:575-587`；`GroupStateRecoveryCore.cs:309-313`；`GroupLeaseLedger.cs:91-96`
- problem: `MarkLoadFailed` 只写 `State=LoadFailed` + `Reason`，`Degraded` 恒 false；矩阵行⑩要求「LoadFailed/Degraded」。且无该路径用例，E4 的降级语义在真实失败下不可判。
- requiredFix: 让 `MarkLoadFailed` 同时置 `Degraded=true`（或 Core 在 `NoteDeferredLoadFailed` 里显式 `MarkAbandoned("native-load-threw")`），并补一条用例：`quests.load` 抛异常 → 0 回调、0 重试、`Degraded` 为真。

### F4 — medium：`ResetSession` 抹除旧租约账，与矩阵行⑤相反且被用例固化

- file: `GroupStateRecoveryCore.cs:145`（`_ledger.Reset()`）；`GroupLeaseLedger.cs:41-58`；用例 `P2PGroupStateRecoveryRuntimeTests.cs:317-330`（`:329` 断言 `Ledger.Token != 84`）
- problem: §7.4 行⑤要求「旧 epoch/token 跨入二局：**旧账保留历史值不新增**、本次增量 0」。实现把旧账整体归零，测试还把「归零」写成期望，导致「旧租约不复活」只能靠「账不存在」证明，无法区分「保留历史但拒绝」与「记忆被清空」。
- requiredFix: 改为 append-only 租约历史（至少保留上一条账，`Begin` 追加新账而非 `Reset`），或由 Architect 明确改写 §7.4 行⑤措辞；两者必居其一，并让 Bug3-28 断言与新语义一致（例如旧账仍可读且 `State=Abandoned`，而新账 Token 为新值）。

### F5 — medium：注册中途失败不回滚已安装补丁（§7.5 部分安装撤销）

- file: `P2PGroupStateRecoveryPatch.cs:141-142`（安装）、`:151`（订阅）、`:166-170`（catch 只记录并 rethrow）
- problem: 订阅失败或第二个 `Patch` 抛错时，已安装的 prefix/postfix 留活，而注册状态已是 Failed 且 `_harmony=null`，后续无法 `UnpatchOwned`，`VerifyCurrent` 亦为 false——系统进入「补丁在生效但模块自认未注册」的分裂态。违反 §7.5「部分安装失败只撤销本模块精确 patch/委托」。
- requiredFix: 把 `:141-151` 包进 try，任何异常一律 `UnpatchOwned(harmony)` 后再记录失败并抛出；`RollbackFailed` 仅在撤销本身失败时使用，并置 `restartRequired=true`；补一条用例：令订阅抛错 → 断言 `Harmony.GetPatchInfo` 中本模块 patch 为 0、State=Failed、异常携带 LifecycleSubscribeFailed。

### F6 — low：行②无专测；`handler==null` 与空 payload 混记

- file: `P2PGroupStateRecoveryPatch.cs:647-651`（`(!payloadNonEmpty || handler==null) → NoteNotifySkippedEmpty`）
- problem: (a) 矩阵行②要求「无队伍但真实恢复非空 flags/quests 仍须通知」，实现结构上无 `isMemberOfAGroup` 门（满足），但无专测；(b) 有 payload 而无监听者被记成 SkippedEmpty，与「空存档/空 payload」语义混淆，会让「本应通知却没通知」在账上看起来像正常跳过。
- requiredFix: 补一条「`groupID=Nil` + 非空 payload → 通知 1 次、不造组、members 不增」的用例；把 `handler==null` 单列计数/原因（如 `SkippedNoListener`），不要并入 SkippedEmpty。

---

## 4. 已确认合规、须予肯定的部分

1. **E4 补发本体**：只在真实 load 正常返回后、同 lease 三重复核、payload 非空、至多一次；空 payload → SkippedEmpty；回调抛错 → NotificationFailed 且不重试；通知在组发布之前；未重跑 load/setFlag/Assign（IL 0）；通知期重入 load 被拒（`Patch:444-448`，Bug3-23）。
2. **t5-F2 已关闭**：延后恢复后确实补发一次真实 `onFlagsUpdated`（`Patch:600` 调用 `:604-629`），修复了 t3 的「空 flags 提前触发且不补发」。
3. **E3/E1/E2 落实且可失败**：prefix 异常回滚不吞 load（Bug3-15）、同租约二次 Deferred 强制放行并降级（Bug3-16/17）、决定收敛到 `GroupLoadPlanner.Decide`（`GroupLoadPlanner.cs`）且 M-A 变异红 13/14/15/23（t14 实跑）。
4. **E5 主体**：Ready 只在两 patch + 必要订阅 + 回滚/兜底就位后提交（`Patch:158-164`）；失败抛结构化 `P2PGroupRegistrationException`（feature/attempt/ownerId/mvid/original/patchMethod/stage/reason/rollbackStatus/restartRequired，`Patch:205-231`）；同 Id 需 `VerifyCurrent` 才复用；跨 Id 拒绝并要求重启（Bug3-26）。
5. **五项禁止项全部未违反**（A8 表）。
6. **§4.3 门控与租约语义未被破坏**（A9）。
7. **独立复跑结果与 t14 自报一致**：本轮实跑 `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe`（15:39 构建）→ exit 0、`674/676 PASS`、仅 NIL01/GBA01 因 native runtime routes 未配置失败、Bug3-01..28 全 PASS，并打印 `[T14] SPF_DLL_SHA256=e73363e5a2567fc8bbd4c21b059fc9617d217a45f4bcad87fe3dce893d31a2a7`（与 t14 自报未变异 DLL 一致）。

---

## 5. UNKNOWN 清单与解锁条件

| 项 | 现状 | 解锁条件 | 归属 |
| --- | --- | --- | --- |
| T1a 真实存档 α/OWNER | UNKNOWN，且是本轮 F1 | 同 fixture 真实存档 + 生命周期回放 + 只换 DLL 对照 | 新票（或 Architect 重定义） |
| T1b 真机菜单重进/进程重启 ×2 | UNKNOWN（未做） | 真机 1H+1G，B 入场前窗口内 Host 侧 groupID=α 且 rank=OWNER，无新 β/γ | 真机票 |
| T2a Guest 重连后本地 group/rank | UNKNOWN（实现未触及） | SDK 集成：断线重连后两端分别断言权威成员表与 Guest 本地字段 | 客机侧票 |
| T3a 客机退队与旧状态残留 | UNKNOWN（实现未触及） | 独立夹具（保留历史 α/β 损坏输入）+ 真实退队入口，Host 权威移除与 Guest 本地 none 两端分开断言 | 客机侧票 |
| T4a 队伍 ID 可见性/队友判定 | UNKNOWN（实现未触及） | 恢复后两端读取用于呈现的真实 group ID = α，退出后非同队 | 客机侧 / UI 票 |
| E1 无墙钟超时的残留风险 | 已声明（t14）：若本局 GAME 后缀永不到来且无第二次 load，仍会跳过该次 load | 记录 Degraded 已具备；若要求更强保证，需 Architect 裁决是否加超时（不得引入第二次 load/补发） | Architect |
| VendorUI 脏标记 / 任务成就 UI 实际刷新 | UNKNOWN（未观测） | SDK/真机观测 UI 实际刷新与脏标记 | 后续观测票 |

以上 UNKNOWN 均**不是**本票通过的依据；本票不宣称 Bug3 端到端 Green。

---

## 6. 复审边界与未做事项

- 只判规格轴是否「按契约做对」；未评命名、复杂度、注释密度、日志措辞等风格问题（由 standards 审查官负责）。
- 未修改任何源码、未 `git add`/`commit`、未编辑 team 状态文件。
- 未重跑 `dotnet build`（15:39 已由外部构建，源码哈希与 t14 自报一致；本报告 §4.7 的 exe 实跑即为对该构建产物的核验）。
- 本报告的 pass 结论不构成对 E4 之外 Bug3 需求的验收。

---

## 7. 给 Captain 的三条处置建议

1. **别把本轮当 Bug3 的 Green**：E4/E5 本体已合规，但 T1a 仍是 blocker。请就 F1 二选一决策：新建 T1a 实现票（推荐，先在旧 DLL 上取 Red），或让 Architect 书面把 T1a 降级为 UNKNOWN 并写解锁条件——二者都必须落盘，不能靠 captain 口头。
2. **E4/E5 的 4 条可修项可合并成一张小修复票**（F2 补生产补发全路径用例或 IL 断言、F3 load 失败置 Degraded + 用例、F4 租约账 append-only 或改契约措辞、F5 注册中途失败回滚 + 用例），预期一次构建 + 一次变异验证即可收口；F6 作为低优先附项。
3. **字节一致性**：请确认 t14 自报测试哈希 `340899f9…` 指向哪个产物；若无法对齐，E6「候选字节可追溯」需在下一轮补证，否则后续任何「字节一致」结论都会带一个缺口。

<!-- 交付绑定（可核验）：本行之前 26091 字节，sha256=af81c4045567ebf180efef0d6a0e24b3a598fcdadf7e67d1572c6f00a30e7fea；核验方式：截断到本行之前重新计算。 -->
