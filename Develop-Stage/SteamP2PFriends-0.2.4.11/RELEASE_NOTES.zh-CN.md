# SteamP2PFriends 0.2.4.11 实验预发布

本版修复**同一存档跨局重开后房主丢失队伍身份（队伍丢失）**的问题。主机视角的客机移动顿挫、客机开枪声在主机侧缺失两项**仍在修复中**：本版只交付定位结论、默认关闭的测量/补偿链路，以及完整回归，不代表这两项已解决。

## 更新内容

### 队伍丢失（已修复）

- 现象：同一世界退出后重开第二局，房主队伍身份被清空，队内成员关系丢失。
- 机制：原生 `PlayerQuests.load()` 早于组表加载执行，把 groupID 写成 `none`；随后的保存把清空状态写回存档。
- 修法：新增 `P2PGroupStateRecovery`，把首次原生加载延迟到组表就绪后**恰好执行一次**；该次加载完成前阻断本局保存（`save-blocked:*` 降级），避免清空状态落盘。
- 证据：2026-11-07 主机侧生产日志两局均出现 gate / Deferred / executed once / native group state published (OWNER)；50 条 Bug3 运行时用例全绿。
- 边界：主机侧身份恢复已验证；客机侧界面呈现、残留与退队，以及真实存档 fixture 仍在后续实机验收（T1a/T1b/T2–T4）。

### 视觉卡顿（仍在修复中）

- 机制已定位：主机侧服务端路径没有 `NetworkSnapshotBuffer` 插值，客机位置按原生 12.5Hz 步进；客机本地有插值，所以看起来平滑。
- 本版**未实施修复**，仅加入默认关闭的 `[Bug1Gate]` 六格闸门测量（0°/90° × 站/走/跑），用于验证"渲染叶偏移 0.1U"方案是否安全：顶点真动，root/third/skeleton 位级不变。
- 判定标准 M1–M4 仍为 UNKNOWN，尚未做真机测量。

### 开火音频丢失（仍在修复中）

- 已实现主机侧补偿链路 `GunshotHostAudio`：隔离门控、去重账本、原生 `playGunshot` 观察器安全网。
- **默认关闭**，需要在主配置手动开启：`[Bug2HostGunshot] Enable = true`；不改默认行为。
- 2 人局试听（S1/S2/S4）尚未完成；3 人互听、Mod 场景、弹药/耐久/弹壳/投射物（S3）仍未验收。

## 配置与诊断

- **破坏性变更**：三个旧独立 cfg 不再读取，值也不迁移；所有开关并入主配置 `BepInEx/config/com.yu80rice.steamp2pfriends.cfg`。
- 启动时输出自证行，便于确认实际生效配置：
  `[Diagnostics] configPath=<绝对路径> bug1Probe=<bool> bug1Gate=<bool> bug2HostGunshot=<bool>`
- Bug1 闸门测量与 Bug2 补偿均默认 `false`；需要复现时再显式开启。

## 验证情况

- 全量自动化：**846/848 PASS**；2 项失败为本机缺少 Unity SDK 运行时的既有环境失败（GBA01、NIL01），与本次改动无关。
- `SteamP2PFriends.dll` 由本次提交源码 `-t:Rebuild` 复现，SHA-256 与发布包一致（见下方文件校验）。
- 未闭环：Bug1 M1–M4、Bug2 S1–S4、Bug3 T1a/T1b/T2–T4、1 Host＋2 Guest 与 Mod 场景。

## 下载与安装

下载附件 **`SteamP2PFriends-0.2.4.11.zip`**，压缩包结构：

```text
BepInEx/
└── plugins/
    └── SteamP2PFriends.dll
```

1. 房主和所有客机退出游戏，分别备份现有的 `BepInEx/plugins/SteamP2PFriends.dll`。
2. 确认已安装适用的 BepInEx；本包只包含插件，不附带 BepInEx 或游戏程序集。
3. 将压缩包解压到 Unturned 游戏根目录，合并 `BepInEx` 目录并替换同名插件。不要保留多个版本的插件 DLL 同时加载。
4. 房主和所有客机使用同一份 DLL，再通过原有启动方式或 UMM 进入游戏。
5. 如需回滚，所有端退出游戏后恢复各自备份的 DLL。

本版沿用 **Experimental** 通道，不覆盖 0.2.3 旧实现仓库。插件目标为 `.NET Framework 4.7.2 / AnyCPU`，运行依赖由现有游戏与 BepInEx 环境提供。

## 文件校验

包内 `SteamP2PFriends.dll` 的 SHA-256：

```text
01C8BB75CC4200F7750F68A1151E05CEAF1EFF1BF8B5777E25073D78636BB5CE
```

压缩包 `SteamP2PFriends-0.2.4.11.zip` 的 SHA-256：

```text
79DC02B76B4C8EDCD47D497ACEA5174A4ECC646023703D633857880F8E197537
```

反馈问题时请提供房主和受影响客机的 UMM 诊断包，并说明操作顺序、连接方式与插件版本。

## 已知限制

- 视觉卡顿与开火音频缺失仍在修复中，本版不代表已解决。
- Bug2 补偿默认关闭；开启后仍需 2 人局试听确认，3 人局与 Mod 场景未验证。
- 配置为破坏性变更：旧 cfg 不迁移，需要重新在主配置中设置。
