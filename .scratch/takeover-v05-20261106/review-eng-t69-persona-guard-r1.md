# T70 窄范围复审：t69 `SteamPersonaDisplay` 判空修复（3 行）

- 审查对象：t69 delta（`Platform/UI/SteamPersonaDisplay.cs` +3/−1，`WhitelistTests/Evidence/Runtime/SteamPersonaDisplayPlayerIdGuardTests.cs` 新增，`WhitelistTests/Program.cs` +1 注册）
- 审查轮次：t70 round 1（attempt 2，attempt_id `0efda6e5-cfe5-471d-a74c-dea458860895`）
- 判定：**pass**（0 high / 0 blocker / 1 low，low 为边界提示，不阻塞）
- 方式：只读主树；变异在仓库外副本 `../_t70mut` 上做，逐字节还原后删除副本

## 0. 结论

这 3 行改动**确实消除了 NRE 触发点**，且没有引入吞异常、没有伪造返回值、没有把判定改成常量：

- 原写法 `sp.playerID != null` 会调用 SDK `SteamPlayerID.op_Inequality`，其实现直接解引用
  `playerID_0.steamID`（`.scratch/bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Provider/SteamPlayerID.cs:139-142`），
  `playerID` 为 null 时在**判空表达式本身**就抛 NRE；
- 新写法 `!ReferenceEquals(sp, null) && !ReferenceEquals(sp.playerID, null) && sp.playerID.steamID == steamId`
  不再触碰重载运算符，短路顺序也正确（先判 `sp`，再判 `playerID`，最后才取值）；
- `T69-01` 走的是生产私有方法的真实执行路径（反射调用 `TryGetConnectedCharacterName`，构造
  `[playerID=null, playerID=目标]` 两个 client），**不是断言常量、不是复制实现**；把该行改回
  `playerID != null` 会让它变红（实测 753/756，失败信息 `实际=<null>`）。

一处必须说清的语义：修复前 `playerID=null` 时 NRE 被方法内既有的 `catch { }` 吞掉（`SteamPersonaDisplay.cs:280`），
对外可观察结果不是崩溃、而是**整次查找返回 null**（调用方落到「未知玩家」）。修复后是「跳过该 client 继续扫描」，
因此在「null client 在前、目标 client 在后」时结果会由 null 变成真实角色名。**这是修复目标本身，返回的是真实匹配
client 的 `characterName`，不是伪造值**；仅此一处行为变化，其余输入形态逐项等价（§4）。

## 1. Delta 与字节锁定（本人实测）

```diff
-                    if (sp != null && sp.playerID != null && sp.playerID.steamID == steamId)
+                    // SteamPlayerID 重载了 ==/!=，运算符直接解引用 .steamID（SDK SteamPlayerID.cs:136-139），
+                    // 任一侧为 null 都会先抛 NullReferenceException。故这里必须用 ReferenceEquals 判空。
+                    if (!ReferenceEquals(sp, null) && !ReferenceEquals(sp.playerID, null) && sp.playerID.steamID == steamId)
                         return sp.playerID.characterName;
```

| 文件 | 字节 | sha256 |
| --- | --- | --- |
| `Platform/UI/SteamPersonaDisplay.cs` | 15454 | `2c2a2c761a7533322576807fa0ae86c6b50ab49ac61531df3d25430d96968687` |
| `WhitelistTests/Evidence/Runtime/SteamPersonaDisplayPlayerIdGuardTests.cs` | 5957 | `9245b08a00bd98a5…` |
| `WhitelistTests/Program.cs`（`:900` 注册 T69-01） | — | `f61df0d20f3e8bc2…` |
| `SteamP2PFriends.csproj` | — | `ed629ae7064015d5…`（**未变**，测试文件走 `Evidence\**\*.cs` 通配） |
| `bin/Release/SteamP2PFriends.dll` | 1496576 | `7168ace45a67cacd4578aac7789c11c38ed1d00af87317262d4b563628a3dd6f` |

delta 范围只有 `SteamPersonaDisplay.cs` 一行生产代码（`git diff` 仅此文件），无其它生产文件改动。

## 2. 三命令复跑（主树，实测）

| 命令 | 退出码 | 结果 |
| --- | --- | --- |
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 0 | 0 警告 0 错误；两次重建 DLL 均为 `7168ace4`（确定性） |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | 0 | 0 错误，4 条既有警告（CS0649×3、CS0219×1） |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | 1 | **754/756 PASS (Failed: 2)**，仅 GBA01/NIL01（控制台缺 Unity：`TypeInitializationException SDG.Unturned.SteamPlayer → NetReflection → ReflectionTypeLoadException`）；`PASS [Runtime] T69-01 Persona Null PlayerID Client Skipped (T69)` |

`[T14] SPF_DLL_SHA256=7168ace4…` 与磁盘一致，测试加载的就是被审字节。

## 3. NRE 机制是否真被消除

- SDK 侧：`SteamPlayerID.op_Equality` 为 `playerID_0.steamID == playerID_1.steamID`，`op_Inequality` 为 `!(a == b)`；
  任一侧为 null 时先解引用 → NRE。这是**运算符本身的缺陷**，任何 `x != null` / `x == null` 写法都会抛。
- 修复侧：`Platform/UI/SteamPersonaDisplay.cs:271` 不再使用 `!=`/`==` 运算符判空，改用 `ReferenceEquals`。
  全模块 `playerID == null` / `playerID != null` 的真实代码命中数现为 **0**（另 2 处命中均为注释：
  `Core/Patches/PlayerClothingLoadAppearanceFixPatch.cs:111`、`Platform/Diagnostics/RemotePlayerRenderProbe.cs:88`）。
- 短路顺序：`!ReferenceEquals(sp, null)` 先挡住 `sp` 为 null；`!ReferenceEquals(sp.playerID, null)` 再挡住
  `playerID` 为 null；此后才取 `steamID`。无「先取后判」的空隙。
- 无新增 `try/catch`：diff 里没有异常处理语句；方法原有的 `catch { }`（`:280`）在修复前就存在（见 O-01）。

## 4. 行为契约对照

| 输入 | 修复前可观察结果 | 修复后 | 判定 |
| --- | --- | --- | --- |
| `sp == null` | 跳过该 client | 跳过 | 等价（`SteamPlayer` 是普通类，`sp != null` 与 `ReferenceEquals` 语义相同） |
| `sp.playerID == null`，且后面有匹配 client | 循环首轮 NRE → 被 `catch { }` 吞 → **返回 null**（查找整体失败） | 跳过该 client，继续扫描 → 返回后面匹配 client 的 `characterName` | **不等价，且这正是修复目标**；返回值为真实匹配数据，非伪造值 |
| `sp.playerID == null`，且无人匹配 | 返回 null | 返回 null | 等价 |
| 首个匹配 client 正常 | 返回其 `characterName` | 同 | 等价 |
| 无人匹配 | 返回 null | 返回 null | 等价 |

因此验收里「若 `playerID` 为 null 应返回与原先可观察等价的结果」在多 client 场景下字面不成立——原始可观察值是
null（因为异常被吞），修复后是真实名字。**判断依据应是「返回值是否来自真实匹配，而非常量/默认值」**，这一点成立。

## 5. T69-01 是否自证

不是自证。`SteamPersonaDisplayPlayerIdGuardTests.Test_NullPlayerIdClientDoesNotAbortLookup`（`:27-85`）：

- 用 `FormatterServices.GetUninitializedObject(typeof(SteamPlayer))` 造 `ghost`（`_playerID` 保持 null）与 `valid`
  （`SetPrivateField` 写入 `new SteamPlayerID(new CSteamID(76561198000000123), 0, ...)`）；
- 反射替换 `Provider._clients` 为 `[ghost, valid]`，把 `_testConnectedNameProvider` 置 null、
  `_testBypassThreadAssert` 置 false（否则生产方法会直接 return null、绕过循环），再清缓存；
- **反射调用生产方法** `typeof(SteamPersonaDisplay).GetMethod("TryGetConnectedCharacterName", NonPublic|Static).Invoke(...)`，
  断言结果为 `"Alice"`；`finally` 里恢复 `_clients`、两个 seam 字段并清缓存。
- 该方法内部只有「`_testConnectedNameProvider` 直通」与「遍历 `Provider.clients`」两条取值路径，前者被置 null，
  因此 `"Alice"` 只能来自生产循环对 `valid` 的匹配 → 断言与生产实现同源，不可用常量蒙过。

## 6. 变异（副本 `../_t70mut`，已删除）

| 变异 | 改动 | DLL sha256（前 16） | 结果 | 失败用例与信息 |
| --- | --- | --- | --- | --- |
| M-1 | 该行改回 `if (sp != null && sp.playerID != null && sp.playerID.steamID == steamId)` | `9fc1ebb0aad505fa` | **753/756（Failed: 3）** | **T69-01**：`playerID=null 的 client 必须被跳过、后续匹配 client 必须命中；期望="Alice"，实际=<null>` |

还原：从字节备份恢复 `SteamPersonaDisplay.cs` → sha256 回 `2c2a2c76…`，重建 DLL 回 `7168ace4…`，复跑 **754/756**、
T69-01 PASS（副本内两次独立构建哈希一致，说明变异是唯一变量）。副本已 `rm -rf`，主树开工/收工哈希完全相同。

## 7. 边界

| 检查项 | 结果 |
| --- | --- |
| Bug3 五文件 | `7a82de8e` / `a89f40b9` / `3d5044f7` / `a4553385` / `5b0c4e9b` —— 与本审查官 t63 记录一致，未变 |
| Bug1 仪表文件 | `Bug1ProbeWindowBoundary.cs` `c3a98c00`、`Bug1ProbeRuntime.cs` `114742df`、`Bug1ProbeJudge.cs` `f41613c0`、`Bug1ProbePatch.cs` `22308d77` —— 与 t60 送审值不同，属 **t67（F1 修复）** 的产物（mtime 14:05–14:25），不是 t69 改动；t69 的 `git diff` 不含这些文件 |
| `SteamP2PFriends.csproj` | 未变（`ed629ae7`），新测试走通配 |
| Release/、publishi/ | 本轮无写入（未触碰） |

## 8. Findings

### O-01（low，边界提示，pre-existing）方法内既有 `catch { }` 仍在，且全仓尚有 48 处未判空的 `playerID.steamID`

- 位置：`Platform/UI/SteamPersonaDisplay.cs:280`（`catch { }`）
- 说明：这次 NRE 之所以表现为「静默查不到人名」而不是崩溃，就是因为这个宽 catch。修复去掉了触发点，但没有改变
  「此路径任何异常都静默」的形态；仓库里仍有 48 处 `playerID.steamID` 直接解引用（未判空、未用 `?.`），
  其中若干处在 `playerID` 为 null 时会同样抛 NRE。
- requiredFix（建议，不阻塞）：本票只证这 3 行正确；其余判空另开票统一处理。若要收紧，可把 `catch { }` 改为
  `catch (Exception ex) { _probeConnectedNameFailed++; LastError = ex.GetType().Name; }` 之类的一次性审计计数，
  避免再次「静默失败」。

## 9. pass 边界与复现

**本票只证**：`SteamPersonaDisplay.TryGetConnectedCharacterName` 的 3 行判空修复消除了该处的 NRE 触发，
行为变化仅限「跳过 null playerID 的 client、继续扫描」，且该修复可被 T69-01 证伪。
**本票不证**：其余同类判空已修（全仓仍有 48 处裸 `playerID.steamID`）；不证实机 UI 表现；不证 0.2.4.11 整体可交付。

```bash
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe   # 754/756，T69-01 PASS
# 变异：仓库外副本把该行改回 `sp.playerID != null` → 753/756，T69-01 红（DLL 9fc1ebb0…）
```

<!-- self_sha256(body)=ae5a935eaaaeb4e8e2b207d10ab418f55fb690bef1c0430516bf4ec9f2eff3f6 -->
