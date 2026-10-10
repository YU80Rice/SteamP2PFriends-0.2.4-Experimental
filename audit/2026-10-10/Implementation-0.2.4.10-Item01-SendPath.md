# SPF 0.2.4.10 票 01 候选 Guest 清单发送修复

状态：源码与自动化验证已闭合；Runtime 仍 PENDING。不授予 Runtime PASS，不领取票 02。

## 阻塞与修复

冻结报告 P1：Candidate 调用原生 `askItems` 时，现有 Harmony Prefix 的 `BeginBaseline` 无条件返回 false，原生方法体被跳过，桥仍把 `isItemsLoaded` 置真并计入绑定成功。

修复：候选自有发送进入 `DispatchCandidateGuestAskItems` 后再反射调用原生 `askItems`。Prefix 只放行该自有发送；旧触发在 Candidate 下仍返回 false，避免重复发包。Postfix 记录 `__runOriginal`。未观察到原生方法体执行或调用失败时抛出，不置 loaded，端口不计入绑定成功。U3DS、普通单机、Guest 与 Legacy 仍走原有放行/账本路径。未新增全区 ACK，未全局伪造 dedicated。

## Red / Green

- Red：IT27，`Guest baseline askItems did not execute`。当时全量 460/461。
- Green：IT27 通过。全量 `WhitelistTests\bin\Release\SteamP2PFriends.WhitelistTests.exe` 为 461/461，失败 0。Runtime 输出 PENDING。

构建：

`MSBuild SteamP2PFriends.csproj /p:Configuration=Release`

`MSBuild WhitelistTests\SteamP2PFriends.WhitelistTests.csproj /p:Configuration=Release`

## 有效审查链

进程重启后，Task 新建的两位审查会话元数据为 `model.id=grok-4.7`、`providerID=haowei-ai`、`variant=high`。消息 `data.model` 同样记录该三元组。`reasoningEffort` 字符串未单独入库；variant high 对应配置中的 reasoningEffort high。旧 gpt-6-luna 与 `opencode run` 回落会话不计入本链。

- Standards `ses_edb1de923ffeTkRt7dMNgEX5q6`，agent=standards-reviewer，parent=`ses_edb5d0635ffeGYpEO3Ruj8LQ4T`，reasoning tokens=11963。CLEAN。非阻断：loaded 位写入有重复形状。
- Spec `ses_edb1de0dcffetPd9EAcwvMCo51`，agent=spec-reviewer，同一 parent，reasoning tokens=25667。CLEAN。不报 Runtime PASS。

## 审查链

1. Standards `ses_edb4e6039ffekF53TtdfAtZNrl` 阻塞；Spec `ses_edb4e511fffenH554y7Ps24dJk` 阻塞。
2. 补真实 Harmony `askItems` Invoke、中文日志、StaticIL 绑定。Standards `ses_edb43eaf7ffesDQsFvMgT4R1Pw` 仍阻塞英文注释；Spec `ses_edb43dc24ffeC1fxvN1ZqFTsom` 仍阻塞手工 Postfix。
3. 译注释，拒绝路径改为真实 Invoke，IL 断言移入 StaticIL。Spec `ses_edb3cc391ffexbogq1EuRSdyt1` CLEAN。Standards `ses_edb3ccebfffePB52KwBfD1BQ05` 同轮复核后 CLEAN。

判断性气味未阻塞：Data Clumps、Divergent Change、Duplicated Code。LMN 按 execution-inputs 本切片不强制，复用原生 RPC。

## 身份

MVID `7a9de3a5-6314-4fe8-8968-a5f69355b1a9`

SHA-256 `5B626E495F12B08D5AA2D9CC43D14AF86511940B8C2A788CD289C0DC277C842F`

Case-ID `SPF-0.2.4.10-Experimental-ItemSlice-Cutover`

该身份对应审查后的 Release DLL，不是实机部署许可。
