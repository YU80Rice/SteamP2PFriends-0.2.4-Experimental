# SteamP2PFriends M1 阶段验收清单

## 阶段身份

- 阶段归档名称：`SteamP2PFriends-0.2.4.1`
- 阶段：Multi-Observer M1 `ItemGenerationAuthorityAdapter`
- 验收结论：PASS
- 验收日期：2026-08-21
- 下一阶段：M2（尚未开始构建）

## 归档指纹

| 文件 | 长度 | SHA-256 |
| --- | ---: | --- |
| `bin/Debug/SteamP2PFriends.dll` | 980992 | `1F02A37DF9F944A9AD33B3CD32283ABF2AFB697934E5C0A0DE532E2813CF509A` |
| `bin/Release/SteamP2PFriends.dll` | 912896 | `4866DA40F019C3F535BF9127EB04FB209C34337CB829772035B8AD24789C6E08` |
| `SteamP2PFriendsPlugin.cs` | 203258 | `88BABDB612AE2E68E9E7E7C7626900F91EA637E3318F3924C77837DFF418F5D7` |
| `MultiObserver/ItemGenerationAuthorityAdapter.cs` | 4727 | `09FCF23153F12D5932EC719DCEA1F9A6839BF7B63CD3ACCFC78039AAE911424E` |
| `Patches/ItemManagerRegionSyncPatch.cs` | 24849 | `8CE0F3C49F4538B3355A2DD98980D63B49E60B9D9B525CCC5A33F4F21B9A20BE` |
| `Patches/AuthoritativeItemGenerationGatePatch.cs` | 12364 | `EA4A5492BC798E1F5872C3AEF8A4B307C1B1051E38E112C786A9477B00D24DBF` |
| `acceptance-evidence/UMM-诊断包_20260821_204907/LogOutput.log` | 20679 | `8826CE09D4D9634AEDF6EBD18CEFAB182A95CB424A3BC3769A68BEACABBD11B5` |
| `acceptance-evidence/UMM-诊断包_20260821_204939/LogOutput.log` | 70387 | `5A87864775A3B973CAD2FD07BB511D35779C635EE9D6EE5E623ECB4889F6FBEB` |

## 验收范围

- 远端观察者在原生 step 5 区域需求路径触发 M1，随后调用原生 `askItems`。
- 待审核客机可看见远区生成物品，但软隔离不能拾取。
- 审核通过后，同一位置保留同一物品实例，不发生重生或替换。
- 客机审批后拾取锤子，房主回到桌面后该锤子不可见。
- 已提交区域的后续需求均被单写者 gate skip；房主回区不覆盖、不重生。
- 双端从同一候选文件夹手动部署，开发者人工确认哈希相同；UMM 原始包未内嵌该哈希字段。

完整验收记录：`audit/2026-08-21/Implementation-0.2.4.1-2141.md`。

## 归档边界

本目录冻结 M1 的源码、Debug/Release 构建、审计记录与双端 UMM 原始包。它不包含 M2 源码或任何后续版本号。
