# Item-NativeIdentityLookup 当前候选验证

## 当前候选与范围

- 固定基线：`fb2acbe31ceb786f3dd9e2dbebce185cc700ac9b`，分支 `codex/structure-baseline-0.2.4`。
- 生产仅改 `Adapters/Item/ItemPreparationAuthority.cs` 的现有 `FindClient(ulong)`；保留空容器早退、遍历顺序、首个匹配引用、`steamID.m_SteamID` 值比较及异常传播。
- 当前生产 DLL MVID：`88f119d8-9167-42c4-8e44-f560636aeca5`；当前 Whitelist EXE MVID：`8b9bf1fe-4fab-4bda-a688-5a9a66c4f7bf`；目标方法 token `0x06000862`。
- 当前批准的完整回归口径是一趟原 Framework `Program.Main`：623 项在 CLR `4.0.30319.42000` x86 原地执行，精确 NIL01 在 CoreCLR `10.0.9` x86 子进程执行；不拼接历史运行，也不按失败选择 runtime。

## 真实 Red → Green

以下 `phase2` / `phase3` 文件均位于 `.scratch/matt-orchestrate/item-native-identity-lookup-20261026/environment/worker/host/`：

- Red：`.scratch/matt-orchestrate/item-native-identity-lookup-20261026/environment/worker/host/phase2-sharedinternals-run.stdout.txt`、`phase2-sharedinternals-run.stderr.txt`、`phase2-sharedinternals-run.exit.txt`。未修改生产 MVID `115828db-b34e-420c-b38c-d44a60ef216e`；真实 `SteamPlayer` / `SteamPlayerID` 输入通过原生初始化后进入 `FindClient`（token `0x06000862`），`SteamPlayerID` 的重载空比较在目标方法内产生 `NullReferenceException`，退出码10。前置初始化失败记录 `evidence/NIL01-red-run-full.clean.log` 单独保留，不作为这次业务 Red。
- Green：`environment/worker/host/phase3-green-final.stdout.txt`、`.stderr.txt`、`.exit.txt`，实际 `TEST_RESULT=True` / exit 0；生产 MVID `88f119d8-9167-42c4-8e44-f560636aeca5`。使用同一正式 NIL01 方法及真实对象/输入。该记录是早期同测 Green；当前候选再次通过 phase15 完整 Main 中相同 NIL01。
- NIL04 复核：在真实 `SteamPlayerID` 上控制 `ReferenceEquals` 与 `==` / `!=` 的差异。新增断言捕获左右操作数含空 ID 的真实重载 `NullReferenceException`，而引用判空保持正常。定向日志：`.scratch/matt-orchestrate/item-native-identity-lookup-20261026/environment/worker/host/phase15-nil04/stdout.txt`、`stderr.txt`、`exit.txt`，exit 0；完整 Main 再次实际执行 NIL04。

## Release 构建与当前全量回归

构建命令与完整 stdout/stderr/exit/时间保存在 `.scratch/matt-orchestrate/item-native-identity-lookup-20261026/environment/worker/host/phase15-build/`：

1. `dotnet msbuild SteamP2PFriends.csproj /t:Build /p:Configuration=Release /v:minimal`，exit 0。
2. `dotnet msbuild WhitelistTests/SteamP2PFriends.WhitelistTests.csproj /t:Build /p:Configuration=Release /v:minimal`，exit 0。保留既有4条警告（CS0219、CS0649），未通过改配置隐藏。

完整新 Main 的完整命令、stdout、stderr、exit、开始/结束时间、子进程原始日志及624行登记结果均在 `.scratch/matt-orchestrate/item-native-identity-lookup-20261026/environment/worker/host/phase15-integrated-framework/`。验证结果 `verified-result.json`：624登记逐 ordinal/name/EvidenceClass 与624行预映射逐项吻合，624 PASS、0 FAIL、Main exit 0。Framework 本地623项；ordinal 103 的唯一 CoreCLR-child 为真实 NIL01，exit 0、唯一 `TEST_RESULT=True`，生产/测试 MVID 与父候选一致。NIL00–04均在本次 Main PASS。

另存环境失败历史而非覆盖：Framework 整套此前623/624的运行环境失败、CoreCLR 整套此前600/624的运行环境失败均不是本次结果；原始目录中的 phase7/旧失败日志保留。本次回归不是单一 CLR 的624项通过。

## 尚未验收

代码仍待本轮独立 Standards/Spec 双轴 CLEAN 与限定提交、正式测试包身份门。Runtime 为 `PENDING`：仍需 R04 Host/Guest 多端实机操作；僵尸掉落仍单独未证实。本记录不代表部署、发布或 Runtime 通过。
