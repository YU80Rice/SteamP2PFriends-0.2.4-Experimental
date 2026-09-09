# 实机测试 SOP(SteamP2PFriends P2P 三端版,v1.0)

> **用途**:实施票交付候选(双轴 CLEAN)后,由主工作树会话给出部署指引,由**用户人工执行**三端实机测试并回传证据包;证据分析归主工作树会话,验收与关单归用户。
> 本 SOP 是 `docs/agents/real-machine-test-loop.md` 的本项目操作手册。
> 与源规程(`更好的UN体验/docs/agents/auto-rm-test-sop.md`,U3DS headless 自动化链)的关系:本项目产品路径为 **P2P listen-host,不使用 U3DS**;P2P 双端自动化需 VM/第二 Steam 账号编排,属 Tier-3 未建——现行形态为人工执行 + Agent 分析。

## 环境事实(已实测)

| 项 | 值 |
|---|---|
| 测试形态 | 1 Host(listen-host)+ 2 Guest(其中一台可为 VM) |
| 部署目标 | 各端 `Unturned\BepInEx\plugins\SteamP2PFriends.dll` |
| 候选产物 | 仓库 `bin/Release/SteamP2PFriends.dll`(仅双轴 CLEAN 后的构建可交付) |
| 指纹工具 | `Get-FileHash <dll> -Algorithm SHA256`(或独立脚本 `Tools/Verify-BuildFingerprintArtifact.ps1`) |
| 证据采集 | UMM 启动器 → UMM-诊断包(每端一个;Guest 可为 zip) |
| 日志主体 | 诊断包内 `LogOutput.log`;锚行前缀 `[ResourceObs]`、`[MultiObserver/*]`,自报告指纹行 `[BuildFingerprint] mvid=… dllSha256=…` |

## 执行序列(人工,不可跳步)

1. **部署**:候选 DLL → 三端 `plugins\`(覆盖前备份旧文件);**三端各验一次哈希**与 audit 记录比对——不一致即停。
2. **启动**:三端经 UMM 启动器启动;Host 创建/载入世界,两 Guest 随后连入。
3. **场景期**:按当票交付报告中的测试剧本执行(基准剧本见下);全程不重启游戏,保持会话连续。
4. **采集**:三端正常退出(先 Guest 后 Host)→ UMM 各导出诊断包。
5. **回传**:三端路径发给主工作树会话(直接粘贴路径;zip 亦可)。

### 基准测试剧本(v1.0,按当票裁剪)

1. Host 单独漫游 2–3 片新区域,各停 ~10 秒,确认资源可见(对照基线);
2. Guest1 连入 → 与 Host 同区域聚合 → 资源正常;
3. **核心场景**:Guest1 先至双方未去过的远区 → Host 后至 → 资源数秒内出现(日志形态:`region-snapshot-deferred` 后 `path=SPI outcome=success`);
4. Host 单独再进一片新区域(Guest1 留原地)→ 资源正常;
5. Guest2 连入 → 三端分散三个区域 → 各自资源可见;
6. Guest1 断线 → 5 秒后重连 → 重连成功、资源正常;
7. 滞回:Host 离开区域 <2 秒返回(资源仍在);>3 秒返回(资源重现)。

## Agent 侧分析序列(证据回传后)

1. 三端 `[BuildFingerprint]` 行与候选指纹**精确匹配**(MVID + SHA-256 + Case-ID);
2. `LeaseAcquire` outcome 分布(success/deferred/failed 计数)、`region-snapshot-failed` 必须为 0;
3. 滞回(`LeaseReleaseScheduled`/`LeaseReentry`)、重连(`ConnectionGeneration`)、快照下发(`SnapshotReceive`)因果链;
4. 异常锚行(`SnapshotRemove`/`SnapshotEnqueue`/`ObserverUpdate` outcome=failed、`fault=`)逐条定位;
5. 结论写 `audit/<date>/` 报告,异常进入修复循环(`docs/agents/real-machine-test-loop.md`)。

## 验收边界

本 SOP 产出 = **证据包与日志分析**;「工单关闭」「发布授权」仍为用户人工裁决。自动化消除的是采集与断言的人力,不是验收责任。
