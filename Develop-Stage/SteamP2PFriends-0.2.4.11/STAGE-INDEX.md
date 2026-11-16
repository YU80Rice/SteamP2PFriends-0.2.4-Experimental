# SteamP2PFriends 0.2.4.11 阶段归档（发布包归档）

- 归档日期：2026-11-16（Asia/Shanghai）
- 身份：tag `0.2.4.11`，实现提交 `c3f96bc`（`feat(0.2.4.11): recover cross-session group identity and gate host gunshot audio`）
- 形式：**发布包归档，不是整树冻结快照**。按用户 2026-11-16 要求，本目录只放发布包、解包 DLL、说明与 manifest；整树源码与证据见仓库提交 `c3f96bc`。

## 内容

| 文件 | 说明 |
|---|---|
| `SteamP2PFriends-0.2.4.11.zip` | 发布包，只含 `BepInEx/plugins/SteamP2PFriends.dll` |
| `SteamP2PFriends.dll` | 同一 DLL，便于直接取用 |
| `manifest.json` | 版本、哈希、源码绑定、回归与环境失败 |
| `RELEASE_NOTES.zh-CN.md` | 面向用户的发布说明 |

## 校验

- DLL SHA-256：`01C8BB75CC4200F7750F68A1151E05CEAF1EFF1BF8B5777E25073D78636BB5CE`（1,593,856 字节）
- ZIP SHA-256：`79DC02B76B4C8EDCD47D497ACEA5174A4ECC646023703D633857880F8E197537`（523,693 字节）
- MVID：`819ae1d6-b106-47f8-8f04-8eb678e9c734`
- 自动化：846/848 PASS；GBA01、NIL01 为本机缺少 Unity SDK 运行时的既有环境失败。

## 本版结论

- 已交付：队伍跨局丢失（房主 OWNER 身份被清空）修复，主机侧生产日志已验证。
- 仍在修复中：主机视角客机移动顿挫（Bug1，无修复本体，仅默认关闭的六格闸门测量）、客机开枪声主机侧缺失（Bug2，补偿链路默认关闭）。
- 未闭环：Bug1 M1–M4、Bug2 S1–S4、Bug3 T1a/T1b/T2–T4、1 Host＋2 Guest 与 Mod 场景。

## 证据位置

- 工单、契约与双轴复审：仓库 `.scratch/takeover-v05-20261106/`。
- 本机测试包与测量说明：`Release/0.2.4.11-testkit-2/`（未纳入版本库）。
