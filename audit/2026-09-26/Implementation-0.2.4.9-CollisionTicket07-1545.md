# Implementation 0.2.4.9 — Collision Ticket 07

日期：2026-09-26
票据：`.scratch/collision-migration-slice/issues/07-cutover-readiness-go-nogo.md`
规格：`.scratch/collision-migration-slice/spec.md`
候选角色：`ReadOnlyShadow`

## 1. 结论

本票完成 Collision 正式切换准入证据汇总，并作出分层 Go / No-Go 结论：

- **Control-Plane Readiness Gate 的静态与已观测影子子集：GO（证据闭合）**。票 04 的静态准入不变量仍成立；票 05 的只读影子三端运行覆盖 canonical samples、Host demand、差异稳定性和无错误 Release，且没有发现新的禁止差异；票 06 的 Execution Port、Acquisition Receipt、身份门与局部故障隔离证据保持通过。
- **会话恢复证据：NOT PROVEN**。票 05 影子包没有第二连接代次的“退出—等待—重进”场景，因此本票不把会话恢复标为已通过；它必须在票 09 正式 `Cutover` Runtime 中补齐。
- **Collision 正式切换资格：NO-GO（当前生产切换不解锁）**。当前证据仍是 `ReadOnlyShadow` 候选；正式切换候选、旧 Writer 调用归零和会话边界原子切换尚未完成，归票 08；正式 1 Host + 2 Guest Runtime 与会话恢复尚未完成，归票 09。
- **受控移交**：票 07 的准入门 GO 允许票 08 开始实现会话边界切换与旧 Writer 退役；这不等于当前生产切换已获准，也不等于票 09 Runtime 已通过。
- **禁止动作**：本票不切换 Collision Authority Writer，不退役旧 `RemoteCoverage` Writer，不把影子日志升级为正式 Runtime PASS，不宣称票 09 已完成。

No-Go 是有意的安全结论，不是准入证据失败：它表示“允许进入票 08 的受控实现阶段”，不表示“可以在当前构建中切换生产写入者”。票 08 完成后，票 09 才能使用正式 `Cutover` 候选完成 Runtime 验收。在此之前，旧 Collision Writer 仍是唯一生产写入者。

## 2. 本轮验证基线与红→绿口径

本票没有新增生产代码，也没有新增第二个行为接缝。票 04–06 已经把本票所需的行为与结构门落在唯一 Lifecycle Orchestration Engine 接缝上；本轮先重编插件，再重编测试宿主（测试宿主通过 `WhitelistTests.csproj` 的 `HintPath` 引用刚生成的插件 DLL），然后运行唯一入口。

验证命令的第一次尝试被 Git Bash 的 PowerShell 变量/大括号解析拦截，未执行构建；第二次尝试被 MSYS 参数转换拦截（`/t`、`/p` 被改写，产生 MSB1008）。两次均不是源码结果，也未计入验证轮次。随后改用 PowerShell 子进程分阶段执行，得到以下有效结果：

| 门 | 结果 | 原始证据 |
|---|---|---|
| 插件 Release Rebuild | 0 error / 0 warning | `.scratch/collision-migration-slice/evidence/ticket07-final-plugin-build.log` |
| 测试宿主 Release Rebuild | 0 error / 0 warning | `.scratch/collision-migration-slice/evidence/ticket07-final-tests-build.log` |
| 唯一测试入口 | **404/404 PASS** | `.scratch/collision-migration-slice/evidence/ticket07-final-tests-run.log` |
| Evidence Class Layout | PASS | `.scratch/collision-migration-slice/evidence/ticket07-evidence-layout.log` |
| Independent Artifact Verification | PASS | `.scratch/collision-migration-slice/evidence/ticket07-build-fingerprint.log` |
| Ticket09 文档元数据门 | PASS | `.scratch/collision-migration-slice/evidence/ticket07-documentation.log` |
| `git diff --check` | 无 diff 错误（仅既存 CRLF 提示） | `.scratch/collision-migration-slice/evidence/ticket07-diff-check.log` |

第二次 Release Rebuild 也成功，指纹记录在 `.scratch/collision-migration-slice/evidence/ticket07-double-rebuild-identity.log`。因此本报告使用当前源码冻结后的新身份，不复用票 06 报告中的历史身份。

## 3. 票 04：共享控制面准入不变量

票 04 的不变量在本轮唯一入口与 StaticIL 阶段继续通过：

- `ARI01–ARI04` / `SAM01–SAM03`：单条坏样本只暂缓自己；Deferred Observer Demand 保留既有贡献，不被当作确认离开，不触发 destructive Release。
- `ARI05–ARI06`：retry 绑定区域、观察者与连接代次，陈旧连接代次没有写入资格，不再使用区域单槽互吞。
- `ARI07–ARI08` / `SIG01–SIG03`：会话身份不确定时阻断写入与破坏性释放；恢复窗口有界，耗尽后显式熔断；恢复要求新会话重建，旧 epoch/generation 无资格。
- `ARI09–ARI10`：故障隔离到 Domain Id + Region Key + Transition；单区域/单领域失败不清理其它领域，持续故障通过有界心跳并写恢复闭环。
- StaticIL `ControlPlaneReadinessStaticILContractTests`：retry 事务粒度、Deferred 不释放、身份门、逐条样本准入、共享外层不结束会话、故障心跳和共享故障通道全部 PASS。

票 05 影子运行未报告新的 `collisionShadowForbidden`，因此不回写票 04；票 04 的 Runtime 仍不在本票升级为通过。

## 4. 票 05：Collision 只读影子证据

票 05 的静态与 Runtime 证据闭合，但其候选角色仍是 `ReadOnlyShadow`：

- 2026-09-25 的 1 Host + 2 Guest 影子验收报告确认三端身份逐项一致，主机 25/25 条汇总 `collisionShadowForbidden=0`。
- 可见逐条差异均为 `HostAddedCoverage / Expected / host-local-player`；未见授权 Guest 缺区、静止抖动、跨观察者错误释放、凭空需求、越界键或异域需求。
- 该影子 Runtime 已证明影子路径可观察且无禁止差异，但**不证明正式 Writer 已接管**，也不证明旧 Writer 调用为零。
- 影子报告明确本包没有 Guest 退出后等待再重进的第二连接代次；因此本票只把 canonical samples、Host demand、差异稳定性与无错误 Release 判为已观测通过，**不把会话恢复升级为已通过**。会话恢复必须由票 09 的正式 `Cutover` Runtime 补齐。
- StaticIL 继续锁定：Collision 政策来自 canonical observer facts（含 Host）、影子不扫描 `Provider.clients`、旧快照只读、影子不写原生状态、旧 Writer 仍是唯一生产写入者、比较器只有一个只读消费者。

因此票 05 对本票的结论是 **Shadow evidence（已观测子集）= GO；会话恢复 = NOT PROVEN；Cutover evidence = NOT PROVEN**。

## 5. 票 06：Execution Port 与 Acquisition Receipt 证据

票 06 的静态/纯内存闭环在本轮 404/404 回归中保持通过：

- `CollisionExecutionPort` 经 `IDomainExecutionPort` 形状接入共享编排接缝；端口不扫描观察者、不重算需求、不持有第二套编排状态机。
- 成功 Acquire 生成只读、身份绑定的 `CollisionAcquisitionReceipt`，携带 Domain、Region、Session Epoch、Region Generation、Acquire Generation 与实际 Collision Override。
- 新 Acquire 使旧 Receipt 失效；Release 必须匹配当前 Receipt、身份与 Active Demand 门。
- 无 Receipt、陈旧代次、身份不确定、需求仍在或无法证明整体所有权时，不执行 destructive disable；原子所有权撤销失败保留 Receipt 并写可观测拒绝原因。
- Receipt 不包含 Resource、ResourceSpawnpoint 或可采集树操作；Collision 执行集合不写树/矿。
- StaticIL `CollisionExecutionStaticILContractTests` 与注册契约通过，仍证明当前端口没有生产 `LifecycleOrchestrationEngine.Register` 调用；因此本票不把编译存在误读为生产接管。

票 06 的证据支持准入门，但它明确把真实 Unturned Override 接线、正式切换和 Runtime 归票 08/09；本票不扩大其证明范围。

## 6. 当前构建身份

独立核验脚本对当前插件产物报告：

```text
version=0.2.4.9
candidateRole=ReadOnlyShadow
caseId=SPF-0.2.4.9-Experimental-CollisionSlice-ReadOnlyShadow
pluginGuid=com.yu80rice.steamp2pfriends
dllSha256=B7C6BE2CD9F17864C3288DB6639552A559D17F9235535C78A92D59A7DDF02182
mvid=b4fbc814-a1d2-47b1-acd9-475390ee1110
```

第二次重建得到相同插件 SHA-256/MVID，以及相同测试宿主 SHA-256/MVID：

```text
pluginSha256=B7C6BE2CD9F17864C3288DB6639552A559D17F9235535C78A92D59A7DDF02182
pluginMvid=b4fbc814-a1d2-47b1-acd9-475390ee1110
testSha256=CA6648402A3055AC739B83184213249A182D0E2D66FB76D614565B16C4CA0113
testMvid=56f04a65-202b-4195-9eef-75c3d6cede30
```

该身份是当前审核增量的身份，不是正式切换候选身份。票 05 的三端日志使用的 `ReadOnlyShadow` 影子身份仍只作影子证据；票 08 必须按其票面冻结 `Cutover` 候选并重新授予身份，不能把本报告中的 SHA-256 当作正式候选。

## 7. Go / No-Go 判定矩阵

| 票 07 要求 | 证据 | 判定 |
|---|---|---|
| 04 静态准入不变量仍成立，并吸收 05 新阻塞 | 404/404、Readiness StaticIL；票 05 影子 Forbidden=0 | **GO** |
| 影子 Runtime canonical samples、Host demand、差异稳定性、会话恢复且无错误 Release | 三端影子报告；Forbidden=0；**会话恢复（第二连接代次）未覆盖，保留票 09** | **GO（已观测影子子集）；会话恢复 NOT PROVEN** |
| Execution Port 身份拒绝、Receipt、局部故障隔离 | CEP01–CEP13、Collision Execution StaticIL、404/404 | **GO（静态/纯内存范围）** |
| 正式切换候选与会话边界唯一 Writer | 当前仍 `ReadOnlyShadow`；票 08 尚未实施 | **NO-GO（票 07 准入 GO，允许受控进入票 08）** |
| 旧 Writer 生产调用为零 | 影子期旧 Writer 仍是唯一生产写入者；票 08 尚未实施 | **NO-GO（归票 08）** |
| 正式 1 Host + 2 Guest Runtime 与第二连接代次会话恢复 | 只有影子 Runtime；正式候选尚未部署，影子包没有第二连接代次 | **NOT PROVEN（归票 09）** |
| 不切换 Authority Writer、不退役旧 Writer、不宣称最终 Runtime PASS | 本报告边界与票据约束 | **GO（边界保持）** |

**总裁决：Control-Plane Readiness Gate 的静态与已观测影子子集 = GO（允许受控进入票 08）；会话恢复证据 = NOT PROVEN（票 09）；当前生产 Collision Cutover = NO-GO。**

## 8. 未闭合项与下一票边界

以下项目不是本票失败，而是正式切换必须继续保持 No-Go 的具名条件：

1. 票 08 必须在本票准入 GO 的边界内开始受控实现：在插件启动或新 Session Epoch 一次性选择唯一 `Cutover` Writer，禁止进程内回退、按区域/玩家/比例切流；票 07 不在当前构建中执行这次切换。
2. 票 08 必须把真实 Collision Execution Port 接入正式生产写入图，并通过调用图证明旧 `RemoteCoverage` 扫描、刷新、旧集合和生产影子比较退出。
3. 票 09 必须在票 08 完成后使用正式 `Cutover` 候选，在三端同一 SHA-256/MVID/版本/Case-ID 下验收 Host 独在、Guest 远区、双 Guest 同/跨区、滞回重入、断线重连、**第二连接代次退出—等待—重进会话恢复**、陈旧 Receipt、旧 Writer 调用为零和 Release 指向 Receipt。
4. 票 05 影子包未覆盖第二连接代次的退出—等待—重进；票 09 不得用影子日志替代该正式场景。
5. 任一正式候选 Runtime 或调用图证据失败，必须维持旧 Writer 的安全边界并回到对应票据修复；不得在同一会话中切回旧 Writer 作为运行时回滚。

## 9. 双轴审查链

本票共完成四轮双轴审查；每轮均由全新 `standards-reviewer` / `spec-reviewer` 实例并行执行，未续接上一轮实例：

| 轮 | 本轮增量与发现 | Standards | Spec |
|---|---|---|---|
| 1 | 初始证据汇总；Spec 指出会话恢复被整项标为完成、双轴尚未闭合 | CLEAN | BLOCKING 2 |
| 2 | 将会话恢复降为 `NOT PROVEN`，并同步票面/审计/索引/manifest | CLEAN | BLOCKING 2：票面整项勾选越界；将票 09 误写成票 08 启动前置 |
| 3 | 拆分票面未完成的第二连接代次会话恢复项；将 07 准入 GO 定义为允许 08 受控实现；清除 manifest 的“禁止进入 08”残留 | CLEAN | BLOCKING 1：manifest 变更清单仍残留旧阻塞措辞 |
| 4 | 修复 manifest 变更清单与正式裁决用词，重跑文档门禁与 diff 检查 | CLEAN | CLEAN |

第 4 轮两轴均为全新实例、同一当前工作树上的最终判词：Standards 无硬性违规（仅记录跨文档重复与 manifest 档案体量的判断性 smell），Spec 无缺失、无范围蔓延、无错误实现或依赖循环。审查链现已闭合；本报告身份仅覆盖本票的审计/文档增量，不授予正式 Collision Cutover 或 Runtime PASS。

## 10. 交付边界

本票只交付准入证据汇总与 Go/No-Go 审计结论；不新增生产类型、不改变 Harmony 元数据、不改变协议或配置、不切换 Authority Writer、不退役旧 Writer、不宣称正式 Collision Runtime PASS。票 07 的准入 GO 允许票 08 进入受控实现阶段；票 09 仍为正式 Runtime、会话恢复与最终关单入口。
