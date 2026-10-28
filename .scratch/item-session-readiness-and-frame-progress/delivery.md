# Item-SessionReadinessAndFrameProgress 交付

## 可测单DLL

- 文件：[`SteamP2PFriends.dll`](../../artifacts/Item-SessionReadinessAndFrameProgress/6526b7e-20261028-0035/SteamP2PFriends.dll)
- 安装/回滚：同目录[`README.md`](../../artifacts/Item-SessionReadinessAndFrameProgress/6526b7e-20261028-0035/README.md)。各端关闭游戏、备份旧DLL、只替换此插件、UMM开新会话；异常时正常退出后恢复备份。
- 来源实现提交：`6526b7e`（基线 `5c496c0b3c8aa61ba825348bec637071759223c0`）。提交后 Release 重建成功，完整命令与 UTC 见本run `postcommit-production-release.log`。
- 独立身份门：[`fingerprint-verification.txt`](../../artifacts/Item-SessionReadinessAndFrameProgress/6526b7e-20261028-0035/fingerprint-verification.txt) PASS；版本 `0.2.4.10`，Case `SPF-0.2.4.10-Experimental-ItemSlice-Cutover`，角色 Cutover，MVID `58277318-8897-4147-b9a2-9bb6fd3e9f49`，SHA-256 `02FA2AFD442D074FF584B01503AE8A1AC6FB33DE9CD55CC698CB8916924AC6C7`。
- PE/CLR：文件非空（1,387,008 bytes），Mono.Cecil读取为 `v4.0.30319`、I386、ILOnly、DLL（AnyCPU）；Assembly/File version均 `0.2.4.10`。身份元数据见同目录 `pe-metadata.txt`。

## 当前验证与实机后续

- 主候选当前正式 Main：635/635 PASS，exit 0；同 runId 连续 ledger、633 Framework-local + 2 CoreCLR-x86，并保留 NIL01/GBA01 两条真实runtime路由，见本run `full-suite-round2/`。
- SFP-R2-G1严格fixture隔离同测：RED首due后11项/0交付，GREEN 21帧内0项/1交付；完整运行记录见`experiments/b-final-fixture/`。第三轮 Standards 与 Spec 均原生独立 CLEAN，见`review-round-3-results.md`及来源JSON。
- Runtime仍未执行，R04视觉延迟未验收。主调度后续安排最小1H1G：Host初区→Guest首次加入→同区与新区观察/交互→Guest退出后Host继续新区→同进程再开世界Guest加入；分别记录数据就绪、队列/模型与拾取，bound不算完成。
- 后续1H2G及原06/07、僵尸留项保持OPEN；不随本票结案关闭。本DLL未安装、部署或发布。
