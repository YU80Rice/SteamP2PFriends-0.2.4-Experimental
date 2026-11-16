# t129 Bug2 r4：三条半覆盖补守护（R25-01/02/03 + R25-05/06）

> 基线：t127 交付字节（829/831，仅 GBA01/NIL01）。
> 本轮只补「实现已改、但没有用例证明它承重」的守护；不改变 Bug2 的行为语义。
> 未声称 Bug2 已修复或可听性通过。

## 1. 结果摘要

| 项 | 结果 |
|---|---|
| 生产 build（`-t:Rebuild`） | 0 警告 0 错误 |
| 测试 build（`-t:Rebuild`） | 0 错误 4 既有警告 |
| exe | **832/834 PASS**，仅 GBA01/NIL01 既有环境失败 |
| Bug2 用例 | Bug2-01..33 全 PASS（新增 31/32/33） |
| 生产 DLL | `44bc3d74ab2365e9ce78bdeafc85f2b7a1d5b9af72d78725a0886cbb27167662` |
| 变异方式 | 仓库外兄弟副本 `SPF-t129mut` 内改字节 → `-t:Rebuild` → 跑 exe → 从主树回拷还原 → 校验 sha256 一致 → 删除副本 |

## 2. 三条 medium 的守护

### R25-01 回查守卫（`GunshotHostAudioPatch.cs`）

- 生产：新增测试缝 `PostfixVerifierForTest`（`:46`）；`RegisterManual` 在 `harmony.Patch(playGunshot, postfix)` 之后用
  `postfixInstalled = PostfixVerifierForTest != null ? PostfixVerifierForTest(...) : IsOurPostfixInstalled(...)`（`:116-118`），
  为 false 时 `Uninstall()` + `Fail("native leaf postfix not installed")`（`:119-124`）。
- 用例：`Test_PostfixVerifierRejectionFailsClosed`（`GunshotHostAudioTests.cs:676`，Bug2-31）。
  构造「Patch 已返回、回查看不到」：真实 Harmony 安装到 `FakeGun.FakeLeaf`，`PostfixVerifierForTest = (h,t,p) => false`。
  断言：`RegisterManual` 返回 false、`Installed=false`、`NativeObserverInstalled=false`、`FailureReason` 含原因、
  `GetPatchInfo(fakeLeaf)` 无我方 Postfix、`GetPatchInfo(fakeFire)` 无 patch、`FailClosed=true`。
- 反证：把 `if (!postfixInstalled)` 改成恒假（见 §3 M1）→ Bug2-31 红。

### R25-02 Collect 接线（`GunshotHostAudioRuntime.cs`）

- 生产：新增 `GunshotCollectSources`（15 个 SDK 原始值，`:331`）与 `CollectObserverForTest`（`:350`）。
  `Collect` 先把 SDK 读取结果装入 sources 并交给观察器（`:364-386`），再把 sources 逐项传给 `BuildFactsFromState`。
- 用例：`Test_CollectWiringFromSdkSources`（`GunshotHostAudioTests.cs:713`，Bug2-32）。
  令 `Provider._isConnected=true`、`Level._isLoaded=false`、`Provider._isServer=false`、`Dedicator._isDedicated=false`
  （try/finally 还原），调用 `Collect(null)`，断言观察器看到 `Connected=true`、`LevelLoaded=false`，且 facts 与 sources 一致。
- 反证：把 `LevelLoaded = Level.isLoaded` 换成 `Provider.isConnected`（见 §3 M2）→ Bug2-32 红。

### R25-03 注册失败传播（`PatchRegistrationDiagnosticsModules.cs`）

- 生产：诊断阶段的 gunshot 块抽成 `ApplyGunshotModuleOutcome(ok, harmony)`（`:107`），
  `InitializeDiagnosticStage` 用 `ok = ApplyGunshotModuleOutcome(ok, _harmony)`（`:85`）。
  任何 false/异常都返回 false；新增测试缝 `GunshotModuleInitializerForTest`（`:101`）。
- 用例：`Test_DiagnosticsStagePropagatesModuleFailure`（`GunshotHostAudioTests.cs:774`，Bug2-33）。
  断言：seam 返回 false 时 `ApplyGunshotModuleOutcome(true,null)==false`；返回 true 时保持 true；传入 false 保持 false；
  并用 IL 契约断言 `InitializeDiagnosticStage` 真的调用 `ApplyGunshotModuleOutcome`（≥1 次）。
- 反证：把 `if (!initializer(harmony))` 改成恒假（见 §3 M3）→ Bug2-33 红。

## 3. 变异表（逐字节 old→new，仓库外副本）

| 变异 | old（逐字节） | new（逐字节） | 变异 DLL sha256 | 新增红用例 | 还原 |
|---|---|---|---|---|---|
| M1 R25-01 | `if (!postfixInstalled)` | `if (false && !postfixInstalled)` | `a856c97b9d178cffe2fbe270b8876a22dfdcd7ae9bebf2061df5f1e7c6ed67ce` | Bug2-31（另 Bug2-30 同因红） | Patch 回拷后 `6426e77f…` == 主树 |
| M2 R25-02 | `LevelLoaded = Level.isLoaded,` | `LevelLoaded = Provider.isConnected,` | `a817a8b611c76ce5b3adec10c9183d70c8beb960eb5a51c2b8a1fdfecffd92f6` | Bug2-32 | Runtime 回拷后 `6fb78883…` == 主树 |
| M3 R25-03 | `if (!initializer(harmony))` | `if (false && !initializer(harmony))` | `e61116e344025a3faacfef30652e6a78183c09635277b836e6ecd2be81c9fcde` | Bug2-33 | Diagnostics 回拷后 `de6b5497…` == 主树 |

M1 的失败输出：`Bug2-31 ... postfix verify failure must refuse to enable`、`Bug2-30 ... module init failure must surface false`（两处都依赖回查拒绝）。
M2：`Bug2-32 ... levelLoaded must come from Level.isLoaded=false (got true: source was replaced)`。
M3：`Bug2-33 ... module failure must turn ok=true into false`。

> 关于哈希差异（R25-05）：同一语义变异若替换文本不同（整行 vs 片段、是否保留分号/空格），编译出的 DLL 哈希会不同。
> 上表给出本轮实际使用的逐字节替换文本，第三方按此复现应得到同一 DLL 哈希。

## 4. R25-06 改名

`Test_InitializeReturnsBool` → `Test_InitializeReturnsSemanticBool`（`GunshotHostAudioTests.cs:646`），
不再只断言签名：用 `PostfixVerifierForTest=false` 走 `ApplyForTest`，断言返回 false 且 `LastFailure` 含
`native leaf postfix not installed`。Program.cs 注册名同步改为 `Bug2-30 Initialize Returns Semantic Bool (T129)`。

## 5. 三命令

```
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe
```

输出：生产 0 警告 0 错误；测试 0 错误 4 既有警告；exe 832/834（GBA01/NIL01 既有环境失败）。

## 6. 文件 sha256

| 文件 | sha256 |
|---|---|
| `Core/Patches/GunshotHostAudio/GunshotHostAudioPatch.cs` | `6426e77f801d32f300641cb4f644d3af7137ed261054de62cdc87936b119b1c5` |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioRuntime.cs` | `6fb7888316bcb28180c668cd478112c2c3dad65611d18da27c72cb97a75ed4c3` |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs` | `de6b54972bddb4116583f89c18ee6aebd68f2ad3489c7022e3e5550e0ed04bf6` |
| `WhitelistTests/Evidence/Runtime/GunshotHostAudioTests.cs` | `78699cf2b87a9637a5f2eb635ccdded9902b0560f42e31b47b85979be3336016` |
| `WhitelistTests/Program.cs` | `14e825faf598249fb0cb1ddfe2082fa4cf96cc59db2c08d527cf06f8668fcee8` |
| `bin/Release/SteamP2PFriends.dll`（重建） | `44bc3d74ab2365e9ce78bdeafc85f2b7a1d5b9af72d78725a0886cbb27167662` |

## 7. 未闭环 / 说明

- 实机 1H+1G / 1H+2G 听感、Mod 兼容仍为 UNKNOWN（与 t127 一致，不在本轮范围）。
- 本轮未写 `Release/`、`publish/`、`publishi/`、`Develop-Stage/`；未改 `Platform/Diagnostics/`、
  `Core/Patches/P2PGroupStateRecovery/`；未触碰 `.scratch/reported-bugs-2026-09-17/issues/03-*.md` 的既有 ` M`。
- 仓库外副本 `SPF-t129mut` 已在三条变异与还原校验后删除。
