# SteamP2PFriends 0.2.4.10 实机测试包

本包用于手动 R04 实机验证；不会自动安装或发布。只含插件 DLL，不含测试 host。

## 安装与回滚

1. 关闭 Host 和所有 Guest 的游戏。
2. 在每台测试机上备份当前 `SteamP2PFriends.dll`；不要覆盖稳定版目录。
3. 将本包中的 DLL 放回原插件所在位置，核对文件名、版本 `0.2.4.10`、GUID `com.yu80rice.steamp2pfriends` 和 MVID `2ec64d10-dc14-4c9c-afe2-6c22276e6d14`。
4. 通过 UMM 开始新的测试会话。玩家只进行游戏操作并提交 UMM 诊断包，无需阅读 BepInEx 窗口或日志。
5. 若遇到问题，关闭所有游戏，把备份的旧 DLL 放回原位置，再启动游戏。

## 最小 R04 步骤（1 Host + 2 Guest）

1. Host 进入世界，在一个物资区域确认物品可见、可拾取。
2. Guest 1 首次加入，确认无本票导致的九区“可能半包”提示，并检查物资交互。
3. Guest 2 加入同一区域；两位 Guest 各自拾取物品并丢下，确认物品显示和交互正常。
4. Guest 1 移动到远区后退出，Guest 2 留在房间；Host 进入新区，确认 Host 与 Guest 2 仍能继续活动。
5. Guest 1 重连并检查当前区域物资；Host 结束世界后新开一个世界，Guest 1 首次加入，重复检查首次交付。
6. 通过 UMM 导出诊断包并交给维护者，注明每步操作顺序、区域和出现的提示/物资现象。请勿手工注入发送失败。

僵尸掉落、实际网络发送/接收及游戏运行时表现仍须分别判定；本自动化通过不等于 Runtime 通过。

## 构建身份

- 来源提交：`0f94e1333b755bc2fe07d81e1cc2dbe32c74a0ba`
- 构建：Release；产物原始写入时间：2026-10-27 02:45:52（Asia/Shanghai）；Case-ID：`SPF-0.2.4.10-Experimental-ItemSlice-Cutover`
- Target Framework：.NET Framework 4.7.2；PE 架构：MSIL（AnyCPU）
- 必需依赖由现有 Unturned/UMM/BepInEx 环境提供：BepInEx、Assembly-CSharp、Harmony、Steamworks.NET、SDG 与 UnityEngine 程序集；不随测试 host 一同分发。
- 文件版本/程序集版本：`0.2.4.10`；MVID：`2ec64d10-dc14-4c9c-afe2-6c22276e6d14`
- SHA-256：`133BBC899B4B0A032769715E2231F3F7940D8F4DE8C19ED29D81CB6C243C8826`
- 自动回归：`632/632 PASS`，runId `e61c06a2-8d95-4a81-a053-4bc45d016559`；实机 R04 与僵尸掉落仍 pending。
