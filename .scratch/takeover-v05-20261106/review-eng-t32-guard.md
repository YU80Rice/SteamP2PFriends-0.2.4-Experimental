# T34 工程轴第 6 轮（窄）：守卫是否真能红 + 字节锁是否完好

- 复审任务：t34｜被审任务：t32｜审查官：reviewer-eng｜范围：**只判守卫有效性与字节锁**
- 结论：**verdict = pass**（R5-01 闭合；字节锁完好；本轮不重审行为正确性——t31 已判）
- 本轮为跑变异临时改过 `P2PGroupStateRecoveryPatch.cs` 与 `GroupStateRecoveryCore.cs`，全部 `cp` 还原并核 sha256；
  未做任何 git 操作，未留下持久修改。

## 1. 字节锁（送审字节 vs t31 基线）

| 文件 | 字节 | sha256（前 16） | 与 t31 相比 |
| --- | --- | --- | --- |
| `Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs` | 59744 | **a89f40b9**49f841d0 | 未变 |
| `.../GroupStateRecoveryCore.cs` | 21614 | **7a82de8e**38fa0f6a | 未变 |
| `.../GroupLeaseLedger.cs` | 5861 | **3d5044f7**870fbb17 | 未变 |
| `.../GroupLoadPlanner.cs` | 1344 | **a4553385**0f31bd96 | 未变 |
| `.../P2PGroupRegistration.cs` | 1790 | **5b0c4e9b**a4c87cbe | 未变 |
| `WhitelistTests/Evidence/Runtime/P2PGroupStateRecoveryRuntimeTests.cs` | 93841 | **536128dc**3c21876a | 81438B → +12403B（t32 守卫） |
| `WhitelistTests/Program.cs` | 132817 | **f2f386c8**3e15753d | 132593B → +224B（注册 Bug3-50） |
| `bin/Release/SteamP2PFriends.dll` | 1457152 | **d9254a24**61009dad | 未变（生产未重建即为送审产物；重建后仍同哈希） |

**改动只落在测试侧**：五个生产文件哈希逐一与 t31 相同；`csproj`、`Assets/`、`Build/` 无变化。
被审字节与 t32 自述的两处测试哈希完全一致。

## 2. 基线复跑（新字节，不拿 695/697 当基准）

| # | 命令 | exit | 结果 |
| --- | --- | --- | --- |
| 1 | `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 0 | 0 错误 0 警告；DLL 仍 `d9254a24…`（确定性构建未破） |
| 2 | `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | 0 | 0 错误 4 警告（既有 CS0649） |
| 3 | `./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | 1 | **696/698 PASS (Failed: 2)** |

- Failed 仅 `[PureMemory] GBA01 Native First Observe Binding`、`NIL01 Real Native Lookup Contract`（控制台缺 Unity，既有环境项）。
- Bug3 段 **50 条**（t31 的 49 条 + 新增 Bug3-50），全绿；总数 697 → 698、通过 695 → 696，与"仅新增 1 条"自洽。
- 日志打印 `[T14] SPF_DLL_SHA256=d9254a24…`，确认跑的是送审 DLL。

## 3. 自跑变异（守卫是否真能红）

脚本 `D:/tmp/mut31.py`（M1b/M2，Patch.cs）与 `D:/tmp/mut34.py`（M2'，Core.cs），每次替换前断言命中次数 = 1，
结束后 `cp` 回拷备份并核 sha256。流程：改字节 → 生产 `-t:Rebuild` → 同步 DLL 到测试 bin → 跑全量 → 解析 → 还原。

| 变异 | 改动点 | 变异后 DLL sha256（前 16） | 结果 | 红掉的用例 |
| --- | --- | --- | --- | --- |
| 基线 | — | d9254a24 61009dad | 696/698（Failed 2 = 环境） | — |
| **M1b** | `Patch.cs:962` `HasBoundPlayerId(owner)` → `owner != null && owner.playerID != null`（preview1 每局 NRE 原样回归） | **7a3de5a1** b58586dc | **694/698** | **Bug3-47**（`IL 守卫失败[1/2]`，get_playerID 实际=1）+ **Bug3-50**（NullReferenceException） |
| **M2** | `Patch.cs:966` `Provider.user.m_SteamID` → `+ 1UL`（生产恒不合格） | **9623690d** 915b41f3 | **695/698** | **Bug3-50**（ProviderUserId 实际 76561198000000002） |
| **M2'** | `Core.cs:65` 删 `&& OwnerSteamId == ProviderUserId`（门控恒放行方向） | **afe81624** f05c7bae | **695/698** | **Bug3-09**（Eligibility Local Host Identity：owner≠provider 仍被放行） |
| 还原 | — | d9254a24 61009dad | 696/698 | — |

- 三条变异覆盖两个方向：**判空回归**（崩溃）与**门控映射失准**（恒不合格 / 恒放行），每条都有用例真红。
- M1b 的两条红各有分工：IL 守卫抓"生产不再走 helper"，Bug3-50 抓"真实映射抛异常"——互为兜底，
  即使有人把 IL 断言里的类型名写错导致 `== 0` 空过，Bug3-50 仍会因 NRE 变红。
- 与 t32 自述比对：M1b/M2 的 DLL 哈希、计数、红名单**完全一致**（7a3de5a1 / 694/698、9623690d / 695/698）；
  M2' 自述被截断，我实测红的是 Bug3-09。本轮未出现自述失真。

## 4. 守卫设计的最小核对（不重审行为）

1. **双向断言**：IL 守卫同时要求 `get_playerID == 0`（负向）与 `HasBoundPlayerId >= 1`（正向）。
   只有负向时，方法/类型名拼错会"空过"；正向对照 + Bug3-50 行为断言堵住了这个洞。
2. **行为兜底**：Bug3-50 用 `FormatterServices` 造对象、注入 `m_CachedPtr=1` 绕过 Unity fake-null，
   真实执行 `BuildEligibility` 并逐项断言 11 个映射字段——这是本轮能抓住 M2/M2' 的原因。
3. **无顺序污染**：Bug3-50 在 `finally` 中还原 `_hostMode`/`_isP2PServerActive`/`_isServer`/`_user`/`_modeConfigData`/`_clients`
   六个静态字段；基线全量 696/698 与单条顺序无关（Bug3-50 位于 Runtime 段末尾，且还原完整）。
4. **不依赖真实 Unity**：断言中显式承认 `PlayerIsLocalInstance=false`（测试进程无本地玩家），
   只断言能映射的字段——这是诚实的边界，不是把环境限制写成 Green。

## 5. 附：t31 的 R5-03 系误报（本次顺带更正）

t31 报告 R5-03 称 `Provider.user.m_SteamID` 可能 NRE。本轮核对 SDK：`Provider.user` 是
**`CSteamID` 结构体**（`.scratch/bug2-sdk-longpaths-20261030/.../Unturned/Provider/Provider.cs:470`
`public static CSteamID user => _user;`，`_user` 同类型），值类型不可能为 null，R5-03 不成立。
我在 t31 上追加了 evidence_note 更正；t31 的 verdict 不受影响（needs_revision 依据是 R5-01，已由 t32 闭合）。

## 6. 复现命令

```bash
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
cp bin/Release/SteamP2PFriends.dll WhitelistTests/bin/Release/SteamP2PFriends.dll
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe        # 696/698，Failed=GBA01/NIL01

cp Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs /d/tmp/p31.pristine.cs
cp Core/Patches/P2PGroupStateRecovery/GroupStateRecoveryCore.cs  /d/tmp/p34core.pristine.cs
python /d/tmp/mut31.py M1b      # 然后 M2；mut34.py 跑 M2'
sha256sum Core/Patches/P2PGroupStateRecovery/*.cs                     # a89f40b9 / 7a82de8e
dotnet build SteamP2PFriends.csproj -c Release && sha256sum bin/Release/SteamP2PFriends.dll   # d9254a24
```

> `core.autocrlf=true`：还原只能用 `cp` 回拷备份，不要用 `git checkout --`（会改写为 CRLF，破坏字节锁）。

<!-- self-integrity: sha256(本行以上全部 6814 字节，不含本行) = 220b4315989305619d51753bb241d86b78d8720fb6c5b54cac4379fa3c6a54e1 -->

