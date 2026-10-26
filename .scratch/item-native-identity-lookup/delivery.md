# Item-NativeIdentityLookup 交付记录

- **交付状态**：`implemented-pending-runtime` / `ready-for-human`；仅提供人工测试候选，未部署或发布。
- **来源提交**：`bd451123f98626d9ea991b4de681dd1b826b9f2c`（分支 `codex/structure-baseline-0.2.4`）。
- **独立测试包**：`artifacts/Item-NativeIdentityLookup/bd45112-20261026-1030/`；便携包 `artifacts/Item-NativeIdentityLookup/Item-NativeIdentityLookup-bd45112-final.zip` 于2026-10-26 10:38:48 +08:00生成，包内文件清单已核对。
- **当前有效包**：仅以文件名 `Item-NativeIdentityLookup-bd45112-final.zip` 的便携包为交付包。目录中较早生成的 `Item-NativeIdentityLookup-bd45112-20261026-1030.zip` 是说明/清单尚未最终整理时留下的中间副本，不作为用户测试包；保留原样，不覆盖。
- **DLL**：`SteamP2PFriends.dll`；0.2.4.10 / Experimental；.NET Framework 4.7.2；MSIL/AnyCPU；1,377,792 bytes。
- **MVID**：`88f119d8-9167-42c4-8e44-f560636aeca5`。
- **SHA-256**：`15B55640134CD4B2F9195BB65C4C38D5FC4005F219E00BE85BAA7F0E9D23FF9F`。
- **Case-ID / role**：`SPF-0.2.4.10-Experimental-ItemSlice-Cutover` / `Cutover`。
- **产物门**：`Tools/Verify-BuildFingerprintArtifact.ps1` 对交付目录中的实际 DLL 返回 `INDEPENDENT_ARTIFACT_VERIFICATION_PASS`；`findclient-il-verification.txt` 对实际包内 DLL 确认 `FindClient` token `0x06000862` 的引用null分支与SteamID值匹配。
- **构建来源**：phase15 Release 生产构建，命令 `dotnet msbuild SteamP2PFriends.csproj /t:Build /p:Configuration=Release /v:minimal`，2026-10-26 10:01:15 +08:00，exit 0；phase15 后未改生产/测试源码，故复用同一 MVID，不为文档收尾重建。构建与验证完整日志在 `.scratch/matt-orchestrate/item-native-identity-lookup-20261026/environment/worker/host/phase15-build/`。
- **自动化**：正式Whitelist Main 624/624、Failed 0、exit 0；Framework本地623项，真实CoreCLR 10.0.9 x86子进程NIL01一项。四条已有构建警告如实保留。完整账本与日志见 `evidence/candidate-validation-phase15.md` 指向的 phase15 目录。
- **独立审查**：第3轮全新 Standards 与 Spec 原生审查均 CLEAN；第1/2轮历史原样保留。NIL-D01仅延期 Program职责拆分，不延期执行、身份或错误传播。原始final和native ID见 `evidence/review-round-3.md`、`evidence/review-raw-finals.md`。
- **依赖**：插件仅需本插件DLL；Harmony、BepInEx、Assembly-CSharp、Steamworks.NET、SDG与Unity程序集沿用游戏/BepInEx环境提供的共享依赖，不随包替换。隔离CoreCLR测试宿主不属于插件依赖。

## 用户下一步：人工R04

按包内 `README.zh-CN.md` 操作。每台机器关闭游戏，先备份原插件DLL，再只替换 `Unturned\BepInEx\plugins\SteamP2PFriends.dll`，按上述SHA逐端核验，通过UMM开启新会话。失败时正常退出并恢复各端备份；不在旧fault会话验证。

R04顺序：Host初区独自核物资→Guest加入→Guest先远区、Host随后进入新区→Guest退出后Host继续新区并由另一Guest继续观察；检查无重复/幽灵物资。保留06的双Guest/分帧/恢复/到期项目及07独立距离门。僵尸击杀/掉落**单独未证实**；源码/自动化交付不等于Runtime PASS。
