# SteamP2PFriends M0 阶段验收清单

## 阶段身份

- 阶段归档名称：`SteamP2PFriends-0.2.4.0`
- 阶段：Multi-Observer M0 Shadow
- 验收结论：PASS
- 验收日期：2026-08-21
- 下一阶段：M1 `ItemGenerationAuthorityAdapter`（本归档不包含 M1）

## 历史版本例外

M0 阶段的规范版本应冻结为 `0.2.4.0`。由于阶段版本门禁建立前发生流程偏差，最终通过双机验收的 DLL 内嵌版本为 `0.2.4.1`。本目录按用户指定的阶段规范名 `SteamP2PFriends-0.2.4.0` 归档，但不修改 DLL、源码或日志中的原始版本，避免伪造运行证据。

因此：目录名表示阶段规范版本；下表 SHA-256 和原始日志表示实际已验收候选身份。两者不得混淆。

## 验收指纹

| 文件 | 长度 | SHA-256 |
| --- | ---: | --- |
| `bin/Debug/SteamP2PFriends.dll` | 990208 | `DC097B520B567E31CC7FC027CF2650BCA58DB95EF854D7B02E0F03E99FFF75EB` |
| `bin/Release/SteamP2PFriends.dll` | 921600 | `9FF6D42166A46F1A73F8070DA96EB09C16944288043DDF69EC41E949D1FA69B0` |
| `SteamP2PFriendsPlugin.cs` | 203750 | `2FABACE64D6D7A7061C344A3311F97251D7A98CF0ED9B10094F0A4194ED989F9` |
| `MultiObserver/MultiObserverShadowLedger.cs` | 19750 | `4B3156BDD29B79B239E912DD5ECCB3D488549FA2F97A93FCF881C0271C1543C9` |
| `MultiObserver/MultiObserverShadowCoordinator.cs` | 24088 | `A8411FD5D235712FB5C222F2A7A69A8530693A050E12B2131D83EC4C605A2A90` |
| `acceptance-evidence/UMM-诊断包_20260821_200042/LogOutput.log` | 33581 | `3258A4678847B821CB69A56DF3CE39DAA6F0F79588D2E2914C8FB506350AAEBB` |
| `acceptance-evidence/UMM-诊断包_20260821_200056/LogOutput.log` | 90111 | `7D3E1FD8126B6E7AF533935935E7F084134C3FA31E42DD8149243A746212574B` |

## 验收范围

- Host/Client 均加载同一 `0.2.4.1` 候选；Host 实机 Release 哈希与上表一致。
- 待审核玩家、已授权玩家与房主均进入 ObserverSet。
- 有效 navigation bound 参与 Zombie demand；无效 bound 不产生 `zombie-demand:255`。
- 授权变化不移除 world presence；撤销、超时、拒绝与正常断开均回收 demand。
- Item shadow demand 随远端进入/离开 `9 -> 18 -> 9`，无 Item mismatch。
- M0 保持 `shadowOnly=true`，不接管任何 world writer。

完整结论见 `audit/2026-08-21/RuntimeFix-0.2.4.1-2008.md`。
