# R02 交付摘要 — SPF 0.2.4.10

**State:** implemented-pending-runtime; **Label:** `ready-for-human`. Commit: `0979b62` (`Fix R02 observer failure identity cleanup`). Static implementation and the authorized fresh dual-axis review are complete; Guest Runtime acceptance remains pending.

## 修复

- 生产 natural-input 分类现在区分“输入未建立”与“已建立但零 spawn point”。合法空区按 `NoChange` 完成结算并可交付 Guest 当前原生基线；缺少 spawn list、ItemRegion 或 items list 仍不完成/不吞掉作业。
- 不确定发送/可能半包保留隔离，记录有界首异常、调用堆栈、region/observer/connection、attempt/budget 与通知类型/目标；同一失败链的重复回调不重复提示。
- 结算后确认未发送改为观察者级 delivery-only 续送：只重发当前基线，不重跑世界结算；复用既有 `FailedRetryController` 首次+最多3次、0.5/1/2秒及10秒预算，耗尽才给安全人工恢复提示。未知结果仍隔离，不盲重发。
- 真实 projection 退出、旧连接断开、session/region 身份失效撤销对应续送键；成功、真实退出或获准人工重开会准确关闭/重开该观察者首错 trace，engine 补偿退出不重开。
- 未确认本地副作用通知按 observer/connection/region 去重，观察者隔离；完整退出地区或断开连接后允许新链再次提示。
- 确认离开隔离地区时释放该观察者半包 gate；返区按现有共享结算完成门重新发送当前基线，不重放不确定副作用或抽取新物资。

## 验证

| 命令 | 结果 |
|---|---|
| `dotnet msbuild SteamP2PFriends.csproj /t:Build /p:Configuration=Release /v:minimal` | exit 0；G1-final Release production build |
| `dotnet msbuild WhitelistTests/SteamP2PFriends.WhitelistTests.csproj /t:Build /p:Configuration=Release /v:minimal` | exit 0；4 项既有 test-only warnings，无错误 |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | exit 0；614/614 PASS，Failed 0；Runtime 输出 PENDING |
| IT133、IT138–IT143 与既有 IT134–IT137 | IT138 在旧行为下 Red（整套 runner 607/608，唯一失败为 IT138），候选 Green；覆盖结算后 SafeToRetry、并行观察者、0.5/1/2 秒、次数/10秒、人工重开、转未知、真实退出重开 trace 和精确撤销。IT139–IT143 是按 G1 指示对已实现续送行为直接补测，未人为还原源码伪造 Red。完整结果见 `.scratch/matt-orchestrate/item-runtime-repair-20261025-vela/messages/R02-it138-red-run.log`、`R02-whitelist-run-G1-final.log`。 |

前两轮双轴发现测试编号重复、AC8 半包退出/返区闭环不足，以及 AC5 结算后 SafeToRetry 路径遗漏；G1 后第3轮 Standards 又发现连接清理 identity 碰撞，Spec CLEAN。经根授权的 AC7-Collision 最小修复后，本票累计第4轮（该增量唯一轮）全新独立双轴均 CLEAN。完整报告与 native provenance 见 `.scratch/matt-orchestrate/item-runtime-repair-20261025-vela/messages/R02-round4-{standards,spec}-report.md` 和 `R02-round4-reviewer-native-provenance.json`。

## 保留边界

历史日志不能确定原城镇缺物品由哪种 native 输入状态或哪类发送失败造成；本修复针对当前生产分类缺陷并补齐可观察诊断，不倒推旧现场根因。测试通过不等同 Guest Runtime PASS。仍需按人工 R04 计划先 1H1G 定位，再走原 1H2G 与独立 20m 门；未授权部署、push 或 release。


## R02-AC7-Collision continuation (2026-10-25)

IT144 now exercises the production disconnect cleanup path with the numeric collision `region Y=5 / observer=7 / token=7` against disconnect `observer=5 / token=7`. Before the fix it failed with 6 first-failure traces instead of 5 (runner exit 1); after exact identity matching it passes. The target connection's failure records across multiple regions and both failure kinds reopen; the colliding observer remains deduplicated.

Current Release checks: production build exit 0, Whitelist build exit 0, full runner exit 0 with **614/614 PASS**. Logs: `.scratch/matt-orchestrate/item-runtime-repair-20261025-vela/messages/R02-AC7-Collision-red3-run.log`, `R02-AC7-Collision-production-release.log`, `R02-AC7-Collision-whitelist-build.log`, `R02-AC7-Collision-whitelist-run.log`. Earlier 613/613 is the retained G1 result, not the current candidate result.

**Closure state:** `implemented-pending-runtime` / `ready-for-human`; Standards and Spec are both CLEAN on the complete R02 candidate. Round-4 reports preserve reviewer findings and boundaries. Guest Runtime remains pending; this static delivery is not a Runtime PASS.
