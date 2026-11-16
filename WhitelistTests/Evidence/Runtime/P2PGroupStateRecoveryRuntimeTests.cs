using HarmonyLib;
using SDG.Unturned;
using SteamP2PFriends.Core.Patches.P2PGroupStateRecovery;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// Bug3 自动化证据（纯状态机 + 生产登记/IL 契约）。
    /// 覆盖：run-10 顺序的"延后→单次兑现"、正确顺序单次原生 load、空组表不猜队伍、
    /// 换局/换图/销毁/退服负控、Guest/单机/dedicated 负控、members 不增加、
    /// 生产父入口真实接线。真实 Unity 生命周期（MENU→GAME→UI flags）仍需 SDK/真机。
    /// </summary>
    internal static class P2PGroupStateRecoveryRuntimeTests
    {
        internal static void Test_Run10Order_DefersThenAuthorizesExactlyOnce()
        {
            var core = new GroupStateRecoveryCore();
            Assert(core.OnLoadRequested(11L, "PEI") == GroupLoadDecision.Deferred,
                "run-10 顺序下首次 load 必须被延后");
            Assert(core.HasPending, "延后后必须留下 pending");
            Assert(core.DeferredRequests == 1, "延后计数应为 1");

            core.NoteGroupTableLoaded("PEI");
            Assert(core.TryTakeDeferredLoad("PEI", out long token) == DeferredLoadOutcome.Authorized,
                "GAME 组表就绪后必须授权");
            Assert(token == 11L, "授权必须对应原玩家实例 token");
            Assert(!core.HasPending, "授权后 pending 必须清空");
            Assert(core.FlushAuthorizations == 1, "只允许兑现一次");
            Assert(core.TryTakeDeferredLoad("PEI", out _) == DeferredLoadOutcome.None,
                "二次兑现必须拒绝");
            Assert(core.NativeLoadExecutions == 0, "延后路径不得提前计入原生 load");
        }

        internal static void Test_CorrectOrder_PassThroughOnce()
        {
            var core = new GroupStateRecoveryCore();
            core.NoteGroupTableLoaded("PEI");
            Assert(core.OnLoadRequested(12L, "PEI") == GroupLoadDecision.PassThrough,
                "组表已就绪时必须按原生顺序放行");
            Assert(core.NativeLoadExecutions == 1, "放行必须且只计一次原生 load");
            Assert(!core.HasPending, "放行不得留下 pending");
            Assert(core.DeferredRequests == 0, "正常顺序不得产生延后");
        }

        internal static void Test_SecondSessionDifferentMap_NoStateReuse()
        {
            var core = new GroupStateRecoveryCore();
            Assert(core.OnLoadRequested(21L, "PEI") == GroupLoadDecision.Deferred, "第一局应延后");
            core.ResetSession("menu");
            Assert(!core.HasPending, "会话边界必须清空 pending");
            Assert(core.BoundaryResets == 1 && core.Epoch == 1, "会话边界必须递增 epoch");

            core.NoteGroupTableLoaded("Washington");
            Assert(core.TryTakeDeferredLoad("Washington", out _) == DeferredLoadOutcome.None,
                "上一局 pending 不得跨局兑现");
            Assert(core.OnLoadRequested(22L, "Washington") == GroupLoadDecision.PassThrough,
                "新局组表就绪后应直接放行");
            Assert(core.NativeLoadExecutions == 1 && core.FlushAuthorizations == 0,
                "新局只允许一次原生 load，不得有旧局兑现");
        }

        internal static void Test_StalePendingMapMismatch_DiscardedNotRestored()
        {
            var core = new GroupStateRecoveryCore();
            Assert(core.OnLoadRequested(31L, "PEI") == GroupLoadDecision.Deferred, "旧图 load 应延后");
            core.NoteGroupTableLoaded("Washington");
            Assert(core.TryTakeDeferredLoad("Washington", out long token) == DeferredLoadOutcome.Stale,
                "换图后的回调必须判定旧租约失效");
            Assert(token == 0L && !core.HasPending, "失效后不得授权、不得保留 pending");
            Assert(core.StaleFlushRejections == 1, "失效拒绝计数应为 1");
            Assert(core.FlushAuthorizations == 0, "失效租约绝不允许执行原生 load");
        }

        internal static void Test_DisconnectDuringPending_NoRestore()
        {
            var core = new GroupStateRecoveryCore();
            Assert(core.OnLoadRequested(41L, "PEI") == GroupLoadDecision.Deferred, "应先延后");
            Assert(core.AbandonPending("disconnect"), "退服必须能放弃 pending");
            Assert(!core.HasPending && core.AbandonedPending == 1, "退服后 pending 必须清空并计数");

            core.NoteGroupTableLoaded("PEI");
            Assert(core.TryTakeDeferredLoad("PEI", out _) == DeferredLoadOutcome.None,
                "退服后组表回调不得再恢复任何值");
            Assert(core.FlushAuthorizations == 0, "退服后不得执行原生 load");
        }

        internal static void Test_DestroyedInstance_NoRestore()
        {
            var core = new GroupStateRecoveryCore();
            Assert(core.OnLoadRequested(51L, "PEI") == GroupLoadDecision.Deferred, "应先延后");
            Assert(!core.OnPlayerDestroyed(52L), "其它实例销毁不得误清 pending");
            Assert(core.OnPlayerDestroyed(51L), "同实例销毁必须清空 pending");
            Assert(!core.HasPending && core.DestroyedPending == 1, "销毁后 pending 必须清空并计数");

            core.NoteGroupTableLoaded("PEI");
            Assert(core.TryTakeDeferredLoad("PEI", out _) == DeferredLoadOutcome.None,
                "实例销毁后组表回调不得恢复任何值");
        }

        internal static void Test_StaleCallbackAfterNewSession_NoRestore()
        {
            var core = new GroupStateRecoveryCore();
            Assert(core.OnLoadRequested(61L, "PEI") == GroupLoadDecision.Deferred, "第一局应延后");
            core.ResetSession("menu");
            core.NoteGroupTableLoaded("PEI");
            Assert(core.TryTakeDeferredLoad("PEI", out _) == DeferredLoadOutcome.None,
                "新局同图的组表回调不得兑现上一局 pending");
            Assert(core.FlushAuthorizations == 0, "旧回调不得触发延后 load");
        }

        internal static void Test_EligibilityNegatives_GuestSingleplayerDedicated()
        {
            GroupRecoveryEligibility baseline = Eligible();
            Assert(baseline.IsEligible, "全部正条件成立时应判定为合格（P2P listen host）");

            GroupRecoveryEligibility singleplayer = baseline;
            singleplayer.IsP2PHostMode = false;
            Assert(!singleplayer.IsEligible, "普通单机（非 P2P host）必须放行不干预");

            GroupRecoveryEligibility guest = baseline;
            guest.IsServer = false;
            Assert(!guest.IsEligible, "Guest（非 server）必须放行不干预");

            GroupRecoveryEligibility dedicated = baseline;
            dedicated.IsDedicatedServer = true;
            Assert(!dedicated.IsEligible, "dedicated 必须放行不干预");

            GroupRecoveryEligibility remoteClient = baseline;
            remoteClient.PlayerIsLocalInstance = false;
            Assert(!remoteClient.IsEligible, "非本地 Player 实例必须放行");
        }

        internal static void Test_EligibilityNegatives_LocalHostIdentity()
        {
            GroupRecoveryEligibility baseline = Eligible();

            GroupRecoveryEligibility remoteChannel = baseline;
            remoteChannel.ChannelIsLocalPlayer = false;
            Assert(!remoteChannel.IsEligible, "非本地 channel 必须放行");

            GroupRecoveryEligibility noSteamPlayer = baseline;
            noSteamPlayer.HasOwningSteamPlayer = false;
            Assert(!noSteamPlayer.IsEligible, "缺少 SteamPlayer 必须放行");

            GroupRecoveryEligibility notLoopback = baseline;
            notLoopback.HasLoopbackConnection = false;
            Assert(!notLoopback.IsEligible, "非 loopback 连接必须放行");

            GroupRecoveryEligibility zeroSteamId = baseline;
            zeroSteamId.OwnerSteamId = 0UL;
            Assert(!zeroSteamId.IsEligible, "零 SteamID 必须放行");

            GroupRecoveryEligibility providerMismatch = baseline;
            providerMismatch.ProviderUserId = 7UL;
            Assert(!providerMismatch.IsEligible, "owner SteamID 与本机不一致必须放行");

            GroupRecoveryEligibility zeroLocalClient = baseline;
            zeroLocalClient.MatchingLocalClientCount = 0;
            Assert(!zeroLocalClient.IsEligible, "找不到本地客户端必须放行");

            GroupRecoveryEligibility duplicateLocalClient = baseline;
            duplicateLocalClient.MatchingLocalClientCount = 2;
            Assert(!duplicateLocalClient.IsEligible, "多个同 SteamID 客户端必须放行");

            GroupRecoveryEligibility staticGroups = baseline;
            staticGroups.DynamicGroupsAllowed = false;
            Assert(!staticGroups.IsEligible, "未启用动态组必须放行");
        }

        internal static void Test_DeferredLoad_ExecutesNativeLoadOnceAndAvoidsForbiddenApis()
        {
            MethodInfo execute = AccessTools.Method(typeof(P2PGroupStateRecoveryPatch), "ExecuteDeferredLoad");
            Assert(execute != null, "必须存在延后兑现入口 ExecuteDeferredLoad");
            Assert(IlContractProbe.CountMethodCalls(execute, "SDG.Unturned.PlayerQuests", "load") == 1,
                "延后兑现路径必须且只能调用一次原生 Quests.load");
            Assert(IlContractProbe.CountMethodCalls(execute, "SDG.Unturned.PlayerQuests", "InitializePlayer") == 0,
                "不得二次执行完整 InitializePlayer");
            Assert(IlContractProbe.CountMethodCalls(execute, "SDG.Unturned.PlayerQuests", "save") == 0,
                "pending 期间不得提前调用 save 覆写存档");

            MethodInfo publish = AccessTools.Method(typeof(P2PGroupStateRecoveryPatch), "PublishNativeGroupState");
            Assert(publish != null, "必须存在原生组状态发布入口 PublishNativeGroupState");
            Assert(IlContractProbe.CountMethodCalls(publish, "SDG.Unturned.PlayerQuests", "changeRank") == 1,
                "早发 none 必须经原生 changeRank 发布路径纠正");
            Assert(IlContractProbe.CountMethodCalls(publish, "SDG.Unturned.PlayerQuests", "ServerAssignToGroup") == 0,
                "禁止用 ServerAssignToGroup 冒充恢复（会 members++）");
            Assert(IlContractProbe.CountMethodCalls(publish, "SDG.Unturned.PlayerQuests", "ServerAssignToMainGroup") == 0,
                "禁止用 ServerAssignToMainGroup 冒充恢复");
        }

        internal static void Test_ProductionRegistration_ReachedFromParentEntry()
        {
            MethodInfo parent = AccessTools.Method(typeof(global::SteamP2PFriends.SteamP2PFriendsPlugin),
                "ApplyV2AuditFixPatches_Core");
            Assert(parent != null, "必须存在生产注册父入口 ApplyV2AuditFixPatches_Core");
            Assert(IlContractProbe.CountMethodCalls(parent,
                    "SteamP2PFriends.Core.Patches.P2PGroupStateRecovery.P2PGroupStateRecoveryPatch",
                    "RegisterManual") == 1,
                "父入口必须真实调用本模块 RegisterManual 且仅一次，不得靠测试手动绑定");

            MethodInfo register = AccessTools.Method(typeof(P2PGroupStateRecoveryPatch), "RegisterManual");
            Assert(register != null, "必须存在 RegisterManual 生产登记入口");
            Assert(IlContractProbe.CountMethodCalls(register,
                    "SteamP2PFriends.Core.Patches.P2PGroupStateRecovery.P2PGroupStateRecoveryPatch",
                    "VerifyRegistered") == 1,
                "RegisterManual 必须执行 owner+original+PatchMethod 自检");

            MethodInfo hasPatch = AccessTools.Method(typeof(P2PGroupStateRecoveryPatch), "HasPatch");
            Assert(hasPatch != null, "必须存在 HasPatch 补丁信息校验辅助");
            Assert(IlContractProbe.CountMethodCalls(hasPatch, "HarmonyLib.Harmony", "GetPatchInfo") == 1,
                "HasPatch 必须读取 Harmony.GetPatchInfo 的真实补丁信息");
        }

        internal static void Test_RegisterManual_AppliesOwnerOriginalAndPatchMethod()
        {
            P2PGroupStateRecoveryPatch.ResetForTests();
            var harmony = new Harmony("spf.t3.group-recovery." + Guid.NewGuid().ToString("N"));
            try
            {
                P2PGroupStateRecoveryPatch.RegisterManual(harmony);
                Assert(P2PGroupStateRecoveryPatch.RegisteredForTests, "生产登记必须自报成功");

                Patches loadInfo = Harmony.GetPatchInfo(P2PGroupStateRecoveryPatch.LoadTargetForTests);
                Assert(HasOwnedPatch(loadInfo, harmony.Id, P2PGroupStateRecoveryPatch.LoadPrefixMethod, true),
                    "Quests.load 必须由本次 Harmony 实例登记为 prefix（owner+original+PatchMethod）");

                Patches groupInfo = Harmony.GetPatchInfo(P2PGroupStateRecoveryPatch.GroupTableTargetForTests);
                Assert(HasOwnedPatch(groupInfo, harmony.Id, P2PGroupStateRecoveryPatch.GroupTablePostfixMethod, false),
                    "GroupManager.onLevelLoaded 必须由本次 Harmony 实例登记为 postfix");
            }
            finally
            {
                harmony.UnpatchSelf();
                P2PGroupStateRecoveryPatch.ResetForTests();
            }
        }

        /// <summary>
        /// T14/E2 glue 级：合格本地 Host 的首次 load 必须由真实 Prefix_Load 阻止并登记 pending。
        /// M-A 变异（Prefix 首行 return true）会让本用例直接变红，而不是只测 seam。
        /// </summary>
        internal static void Test_Prefix_DefersThroughRealGlue()
        {
            P2PGroupStateRecoveryPatch.ResetForTests();
            int resolverCalls = 0;
            P2PGroupStateRecoveryPatch.EligibilityResolverForTests = _ => { resolverCalls++; return Eligible(); };
            P2PGroupStateRecoveryPatch.MapNameResolverForTests = () => "PEI";
            try
            {
                var quests = NewPlayerQuests();
                bool passed = P2PGroupStateRecoveryPatch.Prefix_Load(quests);
                Assert(!passed, "合格本地 Host 首次 load 必须被 Prefix 阻止（放行即 M-A 变异形态）; lastFailure="
                    + P2PGroupStateRecoveryPatch.LastPrefixFailure
                    + " rollbacks=" + P2PGroupStateRecoveryPatch.CoreForTests.PrefixRollbacks
                    + " deferred=" + P2PGroupStateRecoveryPatch.CoreForTests.DeferredRequests
                    + " native=" + P2PGroupStateRecoveryPatch.CoreForTests.NativeLoadExecutions
                    + " unnamed=" + P2PGroupStateRecoveryPatch.CoreForTests.UnnamedPassThrough
                    + " resolverCalls=" + resolverCalls
                    + " executing=" + P2PGroupStateRecoveryPatch.CoreForTests.ExecutingDeferredLoad
                    + " notifying=" + P2PGroupStateRecoveryPatch.CoreForTests.Notifying
                    + " questsNull=" + (quests == null));
                Assert(P2PGroupStateRecoveryPatch.PendingQuestsForTests, "Prefix 必须登记 pending 实例");
                Assert(P2PGroupStateRecoveryPatch.CoreForTests.HasPending, "账本必须留下 pending");
                Assert(P2PGroupStateRecoveryPatch.CoreForTests.DeferredRequests == 1, "延后计数应为 1");
            }
            finally
            {
                P2PGroupStateRecoveryPatch.EligibilityResolverForTests = null;
                P2PGroupStateRecoveryPatch.MapNameResolverForTests = null;
                P2PGroupStateRecoveryPatch.ResetForTests();
            }
        }

        /// <summary>T14/E2：其它形态（非 P2P host / 空地图 / 组表已就绪）必须放行，避免全量阻塞。</summary>
        internal static void Test_Prefix_PassThroughForNegativeControls()
        {
            P2PGroupStateRecoveryPatch.ResetForTests();
            try
            {
                GroupRecoveryEligibility guest = Eligible();
                guest.IsServer = false;
                P2PGroupStateRecoveryPatch.EligibilityResolverForTests = _ => guest;
                P2PGroupStateRecoveryPatch.MapNameResolverForTests = () => "PEI";
                var quests = NewPlayerQuests();
                Assert(P2PGroupStateRecoveryPatch.Prefix_Load(quests), "Guest 必须按原生顺序放行");

                P2PGroupStateRecoveryPatch.EligibilityResolverForTests = _ => Eligible();
                P2PGroupStateRecoveryPatch.MapNameResolverForTests = () => string.Empty;
                Assert(P2PGroupStateRecoveryPatch.Prefix_Load(quests), "非 SURVIVAL/空地图必须放行");
                Assert(P2PGroupStateRecoveryPatch.CoreForTests.UnnamedPassThrough == 1,
                    "空地图放行必须计入 UnnamedPassThrough");

                P2PGroupStateRecoveryPatch.MapNameResolverForTests = () => "PEI";
                P2PGroupStateRecoveryPatch.CoreForTests.NoteGroupTableLoaded("PEI");
                Assert(P2PGroupStateRecoveryPatch.Prefix_Load(quests), "组表已就绪必须按原生顺序放行");
                Assert(P2PGroupStateRecoveryPatch.CoreForTests.NativeLoadExecutions == 1,
                    "正确顺序只允许一次原生 load");
            }
            finally
            {
                P2PGroupStateRecoveryPatch.EligibilityResolverForTests = null;
                P2PGroupStateRecoveryPatch.MapNameResolverForTests = null;
                P2PGroupStateRecoveryPatch.ResetForTests();
            }
        }

        /// <summary>
        /// T14/E4 负控：上一局的 token 跨入本局后租约无效，补发必须为 0，
        /// 且账本按 token 记录（不得被进程级 once 或玩家实例级 once 吃掉/误发）。
        /// </summary>
        internal static void Test_FlagsNotify_OldTokenAfterSessionReset_NoNotify()
        {
            GroupStateRecoveryCore core = LeaseWithLoadSuccess(84L, "PEI");
            core.ResetSession("menu-second-session");
            int callbackCount = 0;
            FlagsUpdated handler = () => callbackCount++;
            bool leaseValid = core.IsLeaseValid(84L, "PEI");
            Assert(!leaseValid, "旧 token 在新局必须判定为无效租约");
            bool dispatched = P2PGroupStateRecoveryPatch.TryNotifyFlagsCore(true, handler, leaseValid, 84L, "PEI");
            Assert(!dispatched, "旧 token 跨局不得补发");
            Assert(callbackCount == 0, "旧 token 回调次数必须为 0");
            Assert(core.Ledger.NotifyAttempts == 0, "旧 token 不得增加 NotifyAttempts");
            Assert(core.Ledger.Token != 84L, "账本不得保留上一局 token");
        }

        /// <summary>
        /// T14/E3：Prefix 提交 pending 后若后续工作抛异常，catch 必须先回滚账再放行；
        /// 否则 Postfix 仍会 Authorized，导致原生 load 再执行一次。
        /// </summary>
        internal static void Test_PrefixFailure_RollsBackPending()
        {
            P2PGroupStateRecoveryPatch.ResetForTests();
            P2PGroupStateRecoveryPatch.EligibilityResolverForTests = _ => Eligible();
            P2PGroupStateRecoveryPatch.MapNameResolverForTests = () => "PEI";
            P2PGroupStateRecoveryPatch.CommitObserverForTests = () => throw new InvalidOperationException("t14-e3-injected");
            try
            {
                var quests = NewPlayerQuests();
                bool passed = P2PGroupStateRecoveryPatch.Prefix_Load(quests);
                // T24/F1 失败方向：已登记 pending 后再抛错，必须拒绝本次原生 load（不落到默认值路径）。
                Assert(!passed, "异常路径必须 fail-closed（拒绝原生 load）");
                Assert(P2PGroupStateRecoveryPatch.IsSaveBlockedForTests(quests),
                    "异常实例必须被写入安全表标记为 Blocked");
                Assert(!P2PGroupStateRecoveryPatch.PendingQuestsForTests, "异常后不得残留 pending 实例");
                Assert(!P2PGroupStateRecoveryPatch.CoreForTests.HasPending, "异常后账本不得残留 pending");
                Assert(P2PGroupStateRecoveryPatch.CoreForTests.PrefixRollbacks == 1, "必须记录一次回滚");
                Assert(P2PGroupStateRecoveryPatch.CoreForTests.TryTakeDeferredLoad("PEI", out _) == DeferredLoadOutcome.None,
                    "回滚后 GAME 后缀不得再授权（否则 load 会跑第二次）");
                Assert(P2PGroupStateRecoveryPatch.CoreForTests.FlushAuthorizations == 0, "回滚后不得有兑现");
            }
            finally
            {
                P2PGroupStateRecoveryPatch.CommitObserverForTests = null;
                P2PGroupStateRecoveryPatch.EligibilityResolverForTests = null;
                P2PGroupStateRecoveryPatch.MapNameResolverForTests = null;
                P2PGroupStateRecoveryPatch.ResetForTests();
            }
        }

        /// <summary>
        /// T24/F2：同租约第二次 load 请求不得再"强制放行"——组表未就绪时保持 Deferred，
        /// 只有 GAME 后缀登记 Ready 后才允许授权一次。
        /// </summary>
        internal static void Test_SecondDeferral_ForcesNativePassAndRecovers()
        {
            var core = new GroupStateRecoveryCore();
            Assert(core.OnLoadRequested(71L, "PEI") == GroupLoadDecision.Deferred, "首次仍应延后");
            Assert(core.OnLoadRequested(71L, "PEI") == GroupLoadDecision.Deferred,
                "同租约第二次请求在未 Ready 时必须继续延后，不得强制放行");
            Assert(core.DeferredRepeatRequests == 1, "重复延后必须可观测");
            Assert(core.HasPending, "重复请求不得清掉 pending");
            Assert(core.NativeLoadExecutions == 0, "未 Ready 不得授权任何原生 load");

            core.NoteGroupTableLoaded("PEI");
            Assert(core.TryTakeDeferredLoad("PEI", out long token) == DeferredLoadOutcome.Authorized,
                "Ready 后必须恰好授权一次");
            Assert(token == 71L, "授权 token 必须匹配");
            Assert(core.FlushAuthorizations == 1, "授权只允许一次");
            Assert(!core.HasPending, "授权后不得残留 pending");
        }

        /// <summary>T24/F2：重复延后后 pending 仍保留，Ready 时只兑现一次。</summary>
        internal static void Test_SecondDeferral_LeavesNoPending()
        {
            var core = new GroupStateRecoveryCore();
            core.OnLoadRequested(72L, "PEI");
            core.OnLoadRequested(72L, "PEI");
            Assert(core.HasPending, "未 Ready 前必须保留 pending，不得提前清账");
            Assert(core.TryTakeDeferredLoad("PEI", out _) == DeferredLoadOutcome.NotReady,
                "未 Ready 时授权必须被拒绝");
            Assert(core.FlushAuthorizations == 0, "未 Ready 不得产生兑现");
            Assert(core.HasPending, "拒绝授权后 pending 仍须保留");

            core.NoteGroupTableLoaded("PEI");
            Assert(core.TryTakeDeferredLoad("PEI", out long token) == DeferredLoadOutcome.Authorized,
                "Ready 后必须授权一次");
            Assert(token == 72L, "授权 token 必须匹配");
            Assert(!core.HasPending, "兑现后不得残留 pending");
            Assert(core.TryTakeDeferredLoad("PEI", out _) == DeferredLoadOutcome.None,
                "同一租约不得二次兑现");
        }

        /// <summary>
        /// T14/E4 正控：真实 load 正常返回且 payload 非空 → onFlagsUpdated 恰补发一次；
        /// 第二次请求被抑制（不得 NotifyAttempts&gt;1）；不变式 0≤Notify≤RealLoadSuccess≤1 成立。
        /// </summary>
        internal static void Test_FlagsNotify_OnceAfterRealLoadSuccess()
        {
            GroupStateRecoveryCore core = LeaseWithLoadSuccess(81L, "PEI");
            int callbackCount = 0;
            FlagsUpdated handler = () => callbackCount++;
            Assert(P2PGroupStateRecoveryPatch.TryNotifyFlagsCore(true, handler, true, 81L, "PEI"),
                "payload 非空且 load 成功时必须补发一次");
            Assert(callbackCount == 1, "onFlagsUpdated 必须恰好调用一次");
            Assert(core.Ledger.NotifyAttempts == 1 && core.Ledger.NotifySuccess == 1,
                "账本必须记录 1 次尝试/1 次成功");
            Assert(core.Ledger.RealLoadSuccess == 1 && core.Ledger.NotifyAttempts <= core.Ledger.RealLoadSuccess,
                "不变式 0≤NotifyAttempts≤RealLoadSuccess≤1 必须成立");

            Assert(!P2PGroupStateRecoveryPatch.TryNotifyFlagsCore(true, handler, true, 81L, "PEI"),
                "第二次补发请求必须被抑制");
            Assert(callbackCount == 1, "被抑制的第二次请求不得再次回调");
            Assert(core.Ledger.NotifyAttempts == 1 && core.DuplicateNotifySuppressed == 1,
                "抑制必须计数，且 NotifyAttempts 不得增长");
        }

        /// <summary>T14/E4：空存档/空 payload → SkippedEmpty，补发 0 次。</summary>
        internal static void Test_FlagsNotify_SkippedWhenPayloadEmpty()
        {
            GroupStateRecoveryCore core = LeaseWithLoadSuccess(82L, "PEI");
            int callbackCount = 0;
            FlagsUpdated handler = () => callbackCount++;
            Assert(!P2PGroupStateRecoveryPatch.TryNotifyFlagsCore(false, handler, true, 82L, "PEI"),
                "空 payload 不得补发");
            Assert(callbackCount == 0 && core.Ledger.NotifyAttempts == 0, "空 payload 必须 0 次尝试/0 次回调");
            Assert(core.Ledger.SkippedEmpty, "空 payload 必须记 SkippedEmpty");
            Assert(core.Ledger.RealLoadSuccess == 1, "load 成功本身仍应记账");
        }

        /// <summary>T14/E4：回滚/租约失效 → 0 次补发且显式 Degraded。</summary>
        internal static void Test_FlagsNotify_DegradedAfterRollback()
        {
            var core = new GroupStateRecoveryCore();
            Assert(core.OnLoadRequested(83L, "PEI") == GroupLoadDecision.Deferred, "前置：应延后");
            Assert(core.AbandonPending("prefix-failed"), "前置：异常回滚必须能放弃 pending");
            core.NoteGroupTableLoaded("PEI");
            int callbackCount = 0;
            FlagsUpdated handler = () => callbackCount++;
            Assert(!P2PGroupStateRecoveryPatch.TryNotifyFlagsCore(true, handler, false, 83L, "PEI"),
                "租约失效时不得补发");
            Assert(callbackCount == 0 && core.Ledger.NotifyAttempts == 0, "回滚路径必须 0 次补发");
            Assert(core.Ledger.Degraded, "回滚路径必须显式 Degraded");
        }

        /// <summary>
        /// T14/E4 正控（第二轮）：二局新 token + 真实授权成功 + flags 非空 → 必须再次补发 1 次。
        /// 断言按租约/token 记，不得做成进程级一次。
        /// </summary>
        internal static void Test_FlagsNotify_SecondSessionNewTokenNotifiesAgain()
        {
            GroupStateRecoveryCore core = LeaseWithLoadSuccess(84L, "PEI");
            int callbackCount = 0;
            FlagsUpdated handler = () => callbackCount++;
            Assert(P2PGroupStateRecoveryPatch.TryNotifyFlagsCore(true, handler, true, 84L, "PEI"), "第一局应补发");
            Assert(callbackCount == 1, "第一局补发 1 次");

            core.ResetSession("menu");
            Assert(core.OnLoadRequested(85L, "PEI") == GroupLoadDecision.Deferred, "二局新 token 应重新延后");
            core.NoteGroupTableLoaded("PEI");
            Assert(core.TryTakeDeferredLoad("PEI", out long token) == DeferredLoadOutcome.Authorized, "二局应授权");
            Assert(token == 85L, "二局 token 必须为新实例");
            core.NoteDeferredLoadEntered();
            core.NoteDeferredLoadSucceeded();
            Assert(P2PGroupStateRecoveryPatch.TryNotifyFlagsCore(true, handler, true, token, "PEI"),
                "二局新 token 必须获得自己的补发（不得被进程级 once 吃掉）");
            Assert(callbackCount == 2, "两局各补发一次");
            Assert(core.Ledger.NotifyAttempts == 1 && core.Ledger.Token == 85L,
                "账本必须按新租约记 1 次尝试");
        }

        private static PlayerQuests NewPlayerQuests()
        {
            return (PlayerQuests)FormatterServices.GetUninitializedObject(typeof(PlayerQuests));
        }

        /// <summary>
        /// 直接写 PlayerQuests 私有实例字段（DynamicMethod + stfld）。
        /// 不能用 FieldInfo.SetValue/SetValueDirect：二者都会触发 PlayerQuests 类型初始化，
        /// 而无 Unity 运行时的控制台里该类型 cctor 必失败。
        /// </summary>
        private static void SetInstanceFieldRaw(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert(field != null, "找不到字段 " + fieldName + "（ABI 变化需更新用例外壳）");
            var method = new System.Reflection.Emit.DynamicMethod(
                "spf_set_" + fieldName,
                typeof(void),
                new[] { typeof(object), typeof(object) },
                typeof(P2PGroupStateRecoveryRuntimeTests).Module,
                true);
            var il = method.GetILGenerator();
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
            il.Emit(System.Reflection.Emit.OpCodes.Castclass, field.DeclaringType);
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
            if (field.FieldType.IsValueType)
                il.Emit(System.Reflection.Emit.OpCodes.Unbox_Any, field.FieldType);
            else
                il.Emit(System.Reflection.Emit.OpCodes.Castclass, field.FieldType);
            il.Emit(System.Reflection.Emit.OpCodes.Stfld, field);
            il.Emit(System.Reflection.Emit.OpCodes.Ret);
            var setter = (Action<object, object>)method.CreateDelegate(typeof(Action<object, object>));
            setter(target, value);
        }

        /// <summary>
        /// 写指定声明类型的实例字段（DynamicMethod + stfld，含 public 字段）。
        /// 与 SetInstanceFieldRaw 同理：不得用 FieldInfo.SetValue，否则会触发声明类型的类型初始化；
        /// SDK 的 SteamPlayer/SteamChannel 等 cctor 在无 Unity 的控制台里会因 NetReflection 注册失败。
        /// </summary>
        private static void SetInstanceFieldOn(Type declaringType, object target, string fieldName, object value)
        {
            FieldInfo field = declaringType.GetField(
                fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert(field != null, "找不到字段 " + declaringType.FullName + "." + fieldName);
            var method = new System.Reflection.Emit.DynamicMethod(
                "spf_set_" + declaringType.Name + "_" + fieldName,
                typeof(void),
                new[] { typeof(object), typeof(object) },
                typeof(P2PGroupStateRecoveryRuntimeTests).Module,
                true);
            var il = method.GetILGenerator();
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
            il.Emit(System.Reflection.Emit.OpCodes.Castclass, field.DeclaringType);
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
            if (field.FieldType.IsValueType)
                il.Emit(System.Reflection.Emit.OpCodes.Unbox_Any, field.FieldType);
            else
                il.Emit(System.Reflection.Emit.OpCodes.Castclass, field.FieldType);
            il.Emit(System.Reflection.Emit.OpCodes.Stfld, field);
            il.Emit(System.Reflection.Emit.OpCodes.Ret);
            var setter = (Action<object, object>)method.CreateDelegate(typeof(Action<object, object>));
            setter(target, value);
        }

        /// <summary>
        /// T14/E4 不变式：真实 load 尚未成功就尝试补发（把通知移到成功判定之前的变异形态）→
        /// 必须被拒绝并计入 InvariantViolations，供反例变异变红。
        /// </summary>
        internal static void Test_FlagsNotify_InvariantWhenNotifiedBeforeLoadSuccess()
        {
            P2PGroupStateRecoveryPatch.ResetForTests();
            GroupStateRecoveryCore core = P2PGroupStateRecoveryPatch.CoreForTests;
            Assert(core.OnLoadRequested(86L, "PEI") == GroupLoadDecision.Deferred, "前置：应延后");
            core.NoteGroupTableLoaded("PEI");
            Assert(core.TryTakeDeferredLoad("PEI", out long token) == DeferredLoadOutcome.Authorized, "前置：应授权");
            core.NoteDeferredLoadEntered(); // 只进入，不标记成功
            int callbackCount = 0;
            FlagsUpdated handler = () => callbackCount++;
            Assert(!P2PGroupStateRecoveryPatch.TryNotifyFlagsCore(true, handler, true, token, "PEI"),
                "load 未成功时不得补发");
            Assert(callbackCount == 0, "load 未成功时不得回调");
            Assert(core.Ledger.NotifyAttempts == 0, "不得产生 NotifyAttempts");
            Assert(core.InvariantViolations == 1
                && core.LastInvariantViolation == "notify-before-load-success",
                "必须记录 notify-before-load-success 不变式违反");
        }

        /// <summary>
        /// T14/E4：受控通知期间回调重入 load 必须被拒绝并记录，不得复用 ExecutingDeferredLoad 放行。
        /// </summary>
        internal static void Test_FlagsNotify_RejectsReentrantLoad()
        {
            P2PGroupStateRecoveryPatch.ResetForTests();
            P2PGroupStateRecoveryPatch.EligibilityResolverForTests = _ => Eligible();
            P2PGroupStateRecoveryPatch.MapNameResolverForTests = () => "PEI";
            try
            {
                var quests = NewPlayerQuests();
                P2PGroupStateRecoveryPatch.CoreForTests.NoteGroupTableLoaded("PEI");
                P2PGroupStateRecoveryPatch.BeginNotifyForTests(quests);
                bool passed = P2PGroupStateRecoveryPatch.Prefix_Load(quests);
                Assert(!passed, "通知期间的重入 load 必须被拒绝; lastFailure="
                    + P2PGroupStateRecoveryPatch.LastPrefixFailure);
                Assert(P2PGroupStateRecoveryPatch.CoreForTests.ReentryRejections == 1,
                    "重入拒绝必须计数");
            }
            finally
            {
                P2PGroupStateRecoveryPatch.EndNotifyForTests("test-end");
                P2PGroupStateRecoveryPatch.EligibilityResolverForTests = null;
                P2PGroupStateRecoveryPatch.MapNameResolverForTests = null;
                P2PGroupStateRecoveryPatch.ResetForTests();
            }
        }

        /// <summary>T14/E4：补发路径不得重跑 load / 不得改 flags / 不得走成员计数 API。</summary>
        internal static void Test_FlagsNotify_DoesNotReloadQuestState()
        {
            MethodInfo notify = AccessTools.Method(typeof(P2PGroupStateRecoveryPatch), "TryNotifyFlagsCore");
            Assert(notify != null, "必须存在受控补发入口 TryNotifyFlagsCore");
            Assert(IlContractProbe.CountMethodCalls(notify, "SDG.Unturned.PlayerQuests", "load") == 0,
                "补发路径不得重跑原生 load");
            Assert(IlContractProbe.CountMethodCalls(notify, "SDG.Unturned.PlayerQuests", "setFlag") == 0,
                "补发路径不得改写 flags");
            Assert(IlContractProbe.CountMethodCalls(notify, "SDG.Unturned.PlayerQuests", "ServerAssignToGroup") == 0,
                "补发路径不得走成员计数 API");

            MethodInfo execute = AccessTools.Method(typeof(P2PGroupStateRecoveryPatch), "ExecuteDeferredLoad");
            Assert(IlContractProbe.CountMethodCalls(execute, "SDG.Unturned.PlayerQuests", "save") == 0,
                "延后兑现全程不得写存档");
            Assert(IlContractProbe.CountMethodCalls(execute, "SDG.Unturned.PlayerQuests", "InitializePlayer") == 0,
                "延后兑现不得二次 InitializePlayer");
        }

        /// <summary>
        /// T18/F2：MENU 边界必须清空 pending（旧实现零覆盖，M-C 清空处理器仍全绿）。
        /// </summary>
        internal static void Test_BoundaryMenu_ResetsPendingAndEpoch()
        {
            P2PGroupStateRecoveryPatch.ResetForTests();
            P2PGroupStateRecoveryPatch.EligibilityResolverForTests = _ => Eligible();
            P2PGroupStateRecoveryPatch.MapNameResolverForTests = () => "PEI";
            try
            {
                PlayerQuests quests = NewPlayerQuests();
                long epochBefore = P2PGroupStateRecoveryPatch.CoreForTests.Epoch;
                Assert(!P2PGroupStateRecoveryPatch.Prefix_Load(quests), "前置：合格 host 首次 load 应延后");
                Assert(P2PGroupStateRecoveryPatch.CoreForTests.HasPending, "前置：必须已登记 pending");

                P2PGroupStateRecoveryPatch.InvokeLevelLoadedForTests(Level.BUILD_INDEX_MENU);

                GroupStateRecoveryCore core = P2PGroupStateRecoveryPatch.CoreForTests;
                Assert(!core.HasPending, "MENU 边界后不得残留 pending");
                Assert(core.BoundaryResets == 1, "MENU 边界应记一次 BoundaryResets，实际=" + core.BoundaryResets);
                Assert(core.Epoch > epochBefore, "MENU 边界必须递增 epoch");
                Assert(!P2PGroupStateRecoveryPatch.PendingQuestsForTests,
                    "MENU 边界后不得保留待恢复实例");
            }
            finally
            {
                P2PGroupStateRecoveryPatch.ResetForTests();
            }
        }

        /// <summary>
        /// T18/F2：ServerShutdown 边界同样必须清空 pending（退服/关服不得跨局恢复）。
        /// </summary>
        internal static void Test_BoundaryShutdown_ResetsPending()
        {
            P2PGroupStateRecoveryPatch.ResetForTests();
            P2PGroupStateRecoveryPatch.EligibilityResolverForTests = _ => Eligible();
            P2PGroupStateRecoveryPatch.MapNameResolverForTests = () => "PEI";
            try
            {
                PlayerQuests quests = NewPlayerQuests();
                long epochBefore = P2PGroupStateRecoveryPatch.CoreForTests.Epoch;
                Assert(!P2PGroupStateRecoveryPatch.Prefix_Load(quests), "前置：首次 load 应延后");
                Assert(P2PGroupStateRecoveryPatch.CoreForTests.HasPending, "前置：必须已登记 pending");

                P2PGroupStateRecoveryPatch.InvokeServerShutdownForTests();

                GroupStateRecoveryCore core = P2PGroupStateRecoveryPatch.CoreForTests;
                Assert(!core.HasPending, "关服边界后不得残留 pending");
                Assert(core.BoundaryResets == 1, "关服边界应记一次 BoundaryResets，实际=" + core.BoundaryResets);
                Assert(core.Epoch > epochBefore, "关服边界必须递增 epoch");
            }
            finally
            {
                P2PGroupStateRecoveryPatch.ResetForTests();
            }
        }

        /// <summary>
        /// T18/F2：边界之后同图新局必须重新 Deferred，不得因上一局 "组表已就绪" 直接放行
        /// （M-C 清空边界处理器时的真实故障形态）。
        /// </summary>
        internal static void Test_AfterBoundary_SameMap_DefersAgain()
        {
            P2PGroupStateRecoveryPatch.ResetForTests();
            P2PGroupStateRecoveryPatch.EligibilityResolverForTests = _ => Eligible();
            P2PGroupStateRecoveryPatch.MapNameResolverForTests = () => "PEI";
            try
            {
                PlayerQuests first = NewPlayerQuests();
                Assert(!P2PGroupStateRecoveryPatch.Prefix_Load(first), "前置：首局必须延后");
                P2PGroupStateRecoveryPatch.CoreForTests.NoteGroupTableLoaded("PEI");
                Assert(P2PGroupStateRecoveryPatch.CoreForTests.TryTakeDeferredLoad("PEI", out _)
                    == DeferredLoadOutcome.Authorized, "前置：首局组表就绪应授权");

                P2PGroupStateRecoveryPatch.InvokeLevelLoadedForTests(Level.BUILD_INDEX_MENU);

                PlayerQuests second = NewPlayerQuests();
                bool passThrough = P2PGroupStateRecoveryPatch.Prefix_Load(second);
                Assert(!passThrough, "边界后同图新局必须重新延后，不得直接放行");
                GroupStateRecoveryCore core = P2PGroupStateRecoveryPatch.CoreForTests;
                Assert(core.HasPending, "边界后新局必须重新登记 pending");
                Assert(core.DeferredRequests == 2, "边界后应重新计数一次延后，实际=" + core.DeferredRequests);
            }
            finally
            {
                P2PGroupStateRecoveryPatch.ResetForTests();
            }
        }

        /// <summary>
        /// T18/F1：GAME 后缀永不出现时，save 前缀必须强制兑现一次原生 load 并记 Degraded，
        /// 否则 wasLoadCalled 恒 false → 原生 save 首行返回 → 整局进度静默丢失。
        /// </summary>
        internal static void Test_SaveDeadline_ForcesDeferredLoadOnce()
        {
            P2PGroupStateRecoveryPatch.ResetForTests();
            P2PGroupStateRecoveryPatch.EligibilityResolverForTests = _ => Eligible();
            P2PGroupStateRecoveryPatch.MapNameResolverForTests = () => "PEI";
            // T24/F2：save 截止点的兑现同样必须过 Ready 门（组表已登记 + native 表对象存在）。
            P2PGroupStateRecoveryPatch.NativeGroupTableProbeForTests = () => true;
            int loadInvocations = 0;
            P2PGroupStateRecoveryPatch.DeferredLoadInvokerForTests = _ => loadInvocations++;
            try
            {
                PlayerQuests quests = NewPlayerQuests();
                Assert(!P2PGroupStateRecoveryPatch.Prefix_Load(quests), "前置：首次 load 应延后");
                GroupStateRecoveryCore core = P2PGroupStateRecoveryPatch.CoreForTests;
                Assert(core.HasPending, "前置：必须已登记 pending");
                core.NoteGroupTableLoaded("PEI");

                bool allowSave = P2PGroupStateRecoveryPatch.Prefix_Save(quests);

                Assert(allowSave, "Ready 后 save 截止兑现成功，必须放行原生 save");
                Assert(loadInvocations == 1, "save 期限只能兑现一次原生 load，实际=" + loadInvocations);
                Assert(core.WatchdogForcedReleases == 1, "必须记一次强制放行，实际=" + core.WatchdogForcedReleases);
                Assert(core.Ledger.Degraded, "强制放行必须记 Degraded 供运行日志核对");
                Assert(!core.HasPending, "兑现后不得残留 pending");
                Assert(core.Ledger.RealLoadSuccess == 1, "正常返回后才算 RealLoadSuccess");
                Assert(P2PGroupStateRecoveryPatch.IsNativeLoadCompletedForTests(quests),
                    "成功后活对象必须拿到 NativeLoadCompleted 证明");
                Assert(P2PGroupStateRecoveryPatch.Prefix_Save(quests), "重复 save 不得阻塞");
                Assert(loadInvocations == 1, "重复 save 不得二次 load");
            }
            finally
            {
                P2PGroupStateRecoveryPatch.ResetForTests();
            }
        }

        /// <summary>
        /// T24/t22 回退：委托快照只做一次普通 multicast——A 成功、B 记录后抛错、C 不执行；
        /// 外层记一次 NotificationFailed，不重试；同 token 再通知被拒绝且计数不增长。
        /// 注意：语义是"首个抛错即中断后续"，不是逐订阅者隔离。
        /// </summary>
        internal static void Test_FlagsNotify_StopsAtFirstThrowingSubscriber()
        {
            GroupStateRecoveryCore core = LeaseWithLoadSuccess(90L, "PEI");
            int firstCalls = 0;
            int throwingCalls = 0;
            int thirdCalls = 0;
            FlagsUpdated first = () => firstCalls++;
            FlagsUpdated throwing = () =>
            {
                throwingCalls++;
                throw new InvalidOperationException("t22-subscriber-2");
            };
            FlagsUpdated third = () => thirdCalls++;
            FlagsUpdated multicast = first + throwing + third;

            bool dispatched = P2PGroupStateRecoveryPatch.TryNotifyFlagsCore(true, multicast, true, 90L, "PEI");

            Assert(!dispatched, "存在订阅者抛错时不得报成功");
            Assert(firstCalls == 1, "A 必须成功执行一次");
            Assert(throwingCalls == 1, "B 必须被执行一次并记录异常");
            Assert(thirdCalls == 0, "B 抛错后 C 不得执行（普通 multicast 快照语义）");
            Assert(core.Ledger.NotifyFailed == 1, "账本应记一次 NotificationFailed");
            Assert(core.Ledger.NotifyAttempts == 1, "只允许一次通知尝试");

            bool again = P2PGroupStateRecoveryPatch.TryNotifyFlagsCore(true, multicast, true, 90L, "PEI");
            Assert(!again, "已尝试后必须拒绝重复通知");
            Assert(firstCalls == 1 && throwingCalls == 1 && thirdCalls == 0, "不得重试或整批重发");
            Assert(core.Ledger.NotifyAttempts == 1, "重复请求不得增加尝试次数");
            P2PGroupStateRecoveryPatch.ResetForTests();
        }

        /// <summary>
        /// T18/F7：BuildEligibility 的纯数据层必须独立覆盖三类拒绝：
        /// owner.playerID 缺失、连接非 loopback、同 SteamID 客户端数≠1。
        /// </summary>
        internal static void Test_Eligibility_FactsRejectThreeIdentityViolations()
        {
            var baseline = new GroupRecoverySessionFacts
            {
                IsP2PHostMode = true,
                IsServer = true,
                IsDedicatedServer = false,
                PlayerIsLocalInstance = true,
                ChannelIsLocalPlayer = true,
                HasOwningSteamPlayer = true,
                HasLoopbackConnection = true,
                OwnerSteamId = 112589994979229698UL,
                ProviderUserId = 112589994979229698UL,
                MatchingLocalClientCount = 1,
                DynamicGroupsAllowed = true
            };
            Assert(baseline.Evaluate().IsEligible, "前置：完整 host 事实应合格");

            var noOwner = baseline;
            noOwner.HasOwningSteamPlayer = false;
            Assert(!noOwner.Evaluate().IsEligible, "owner.playerID 缺失必须拒绝");

            var notLoopback = baseline;
            notLoopback.HasLoopbackConnection = false;
            Assert(!notLoopback.Evaluate().IsEligible, "非 loopback 连接必须拒绝");

            var duplicateClients = baseline;
            duplicateClients.MatchingLocalClientCount = 2;
            Assert(!duplicateClients.Evaluate().IsEligible, "同 SteamID 客户端数≠1 必须拒绝");
        }

        /// <summary>
        /// T18/F7：空存档 / 无合法组时不得发布、不得猜默认队伍——changeRank 必须 0 次；
        /// 同时 HasRecoveredPayload 对空载荷必须为 false。
        /// </summary>
        internal static void Test_Publish_NoGroup_DoesNotChangeRank()
        {
            P2PGroupStateRecoveryPatch.ResetForTests();
            // T24/F2：发布用例的前置必须含"本 epoch 组表已就绪"，否则观察到的 0 次调用可能是未授权造成的假象。
            P2PGroupStateRecoveryPatch.NativeGroupTableProbeForTests = () => true;
            P2PGroupStateRecoveryPatch.CoreForTests.NoteGroupTableLoaded("PEI");
            int changeRankCalls = 0;
            P2PGroupStateRecoveryPatch.ChangeRankActionForTests = (_, __) => changeRankCalls++;
            try
            {
                PlayerQuests quests = NewPlayerQuests();
                Assert(!P2PGroupStateRecoveryPatch.HasRecoveredPayloadForTests(quests),
                    "空实例不应被判为已恢复载荷");
                P2PGroupStateRecoveryPatch.PublishNativeGroupStateForTests(quests, "PEI");
                Assert(changeRankCalls == 0, "无组时 changeRank 必须 0 次，实际=" + changeRankCalls);
            }
            finally
            {
                P2PGroupStateRecoveryPatch.ResetForTests();
            }
        }

        /// <summary>
        /// T18/F8：不变式违反必须独立入账（Ledger.InvariantViolation），
        /// 不得混进 Abandoned 字段，否则事后无法区分"被丢弃"与"代码越序"。
        /// </summary>
        internal static void Test_InvariantViolation_LandsInDedicatedLedgerField()
        {
            P2PGroupStateRecoveryPatch.ResetForTests();
            try
            {
                GroupStateRecoveryCore core = P2PGroupStateRecoveryPatch.CoreForTests;
                Assert(core.OnLoadRequested(91L, "PEI") == GroupLoadDecision.Deferred, "前置：登记 pending");
                core.NoteGroupTableLoaded("PEI");
                Assert(core.TryTakeDeferredLoad("PEI", out _) == DeferredLoadOutcome.Authorized, "前置：授权");
                core.NoteDeferredLoadEntered(); // 只进入，未正常返回 → 不得通知

                int notified = 0;
                FlagsUpdated handler = () => notified++;
                bool dispatched = P2PGroupStateRecoveryPatch.TryNotifyFlagsCore(true, handler, true, 91L, "PEI");

                Assert(!dispatched, "load 未成功返回时必须拒绝通知");
                Assert(notified == 0, "越序通知不得真正调用回调");
                Assert(core.InvariantViolations == 1, "必须记一次不变式违反，实际=" + core.InvariantViolations);
                Assert(core.Ledger.InvariantViolation == "notify-before-load-success",
                    "违反原因必须独立落在 Ledger.InvariantViolation，实际=" + core.Ledger.InvariantViolation);
                Assert(core.Ledger.NotifyAttempts == 0, "越序拒绝不得计入通知尝试");
            }
            finally
            {
                P2PGroupStateRecoveryPatch.ResetForTests();
            }
        }

        /// <summary>
        /// T18/F8：无 pending 时 AbandonPending 必须返回 false 并独立记 NoteAbandonNoop，
        /// 不得静默吞掉（TryNotifyFlagsCore 的 lease-changed 分支常走这里）。
        /// </summary>
        internal static void Test_AbandonPending_WhenEmpty_CountsNoop()
        {
            P2PGroupStateRecoveryPatch.ResetForTests();
            try
            {
                GroupStateRecoveryCore core = P2PGroupStateRecoveryPatch.CoreForTests;
                Assert(!core.AbandonPending("t18-noop"), "无 pending 时 AbandonPending 必须返回 false");
                Assert(core.AbandonNoops == 1, "必须记一次 abandon noop，实际=" + core.AbandonNoops);
                Assert(core.Ledger.AbandonNoops == 1, "账本也必须记 noop，实际=" + core.Ledger.AbandonNoops);
                Assert(core.LastReason == "abandon-noop:t18-noop", "noop 原因必须可追，实际=" + core.LastReason);
            }
            finally
            {
                P2PGroupStateRecoveryPatch.ResetForTests();
            }
        }

        private static GroupStateRecoveryCore LeaseWithLoadSuccess(long token, string map)
        {
            P2PGroupStateRecoveryPatch.ResetForTests();
            GroupStateRecoveryCore core = P2PGroupStateRecoveryPatch.CoreForTests;
            Assert(core.OnLoadRequested(token, map) == GroupLoadDecision.Deferred, "前置：首次 load 应延后");
            core.NoteGroupTableLoaded(map);
            Assert(core.TryTakeDeferredLoad(map, out long authorized) == DeferredLoadOutcome.Authorized,
                "前置：组表就绪后应授权");
            Assert(authorized == token, "前置：授权 token 应一致");
            core.NoteDeferredLoadEntered();
            core.NoteDeferredLoadSucceeded();
            return core;
        }

        private static bool HasOwnedPatch(Patches info, string ownerId, MethodInfo patchMethod, bool prefix)
        {
            if (info == null)
                return false;
            foreach (Patch patch in prefix ? info.Prefixes : info.Postfixes)
            {
                if (patch.owner == ownerId && patch.PatchMethod == patchMethod)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// T14/E5：真实登记必须到 Ready，且 Level 生命周期订阅已安装；同 Id 再次登记必须
        /// 核验现有补丁与订阅后复用，不得叠加第二条 prefix。
        /// </summary>
        internal static void Test_Registration_ReadyAndIdempotent()
        {
            P2PGroupStateRecoveryPatch.ResetForTests();
            var harmony = new Harmony("spf.t14.ready." + Guid.NewGuid().ToString("N"));
            try
            {
                P2PGroupStateRecoveryPatch.RegisterManual(harmony);
                Assert(P2PGroupStateRecoveryPatch.RegistrationStateForTests == P2PGroupRegistrationState.Ready,
                    "完整登记后必须进入 Ready");
                Assert(P2PGroupStateRecoveryPatch.LevelSubscribedForTests, "Level 生命周期订阅必须安装");
                Assert(P2PGroupStateRecoveryPatch.PlayerSubscribedOrUnavailableForTests,
                    "Player 订阅必须安装，或记录为环境不可用");
                Assert(CountOwnedPrefix(harmony.Id) == 1, "load prefix 必须恰好一条");

                P2PGroupStateRecoveryPatch.RegisterManual(harmony);
                Assert(P2PGroupStateRecoveryPatch.RegistrationStateForTests == P2PGroupRegistrationState.Ready,
                    "同 Id 再登记必须保持 Ready");
                Assert(CountOwnedPrefix(harmony.Id) == 1, "同 Id 复用不得叠加第二条 prefix");
            }
            finally
            {
                harmony.UnpatchSelf();
                P2PGroupStateRecoveryPatch.ResetForTests();
            }
        }

        /// <summary>
        /// T14/E5：已登记状态下换一个 Harmony 实例/Id（热重载形态）必须显式失败并要求重启，
        /// 不得静默跳过或叠加补丁。
        /// </summary>
        internal static void Test_Registration_HotReloadDifferentIdRefused()
        {
            P2PGroupStateRecoveryPatch.ResetForTests();
            var first = new Harmony("spf.t14.first." + Guid.NewGuid().ToString("N"));
            var second = new Harmony("spf.t14.second." + Guid.NewGuid().ToString("N"));
            try
            {
                P2PGroupStateRecoveryPatch.RegisterManual(first);
                bool refused = false;
                try
                {
                    P2PGroupStateRecoveryPatch.RegisterManual(second);
                }
                catch (P2PGroupRegistrationException ex)
                {
                    refused = true;
                    Assert(ex.Failure.Reason == P2PGroupRegistrationFailureReason.StaleOwnerOrHotReload,
                        "热重载必须归类为 StaleOwnerOrHotReload");
                    Assert(ex.Failure.RestartRequired, "热重载必须显式要求重启");
                }
                Assert(refused, "换 Harmony 实例重新登记必须显式失败，不得静默降级");
                Assert(P2PGroupStateRecoveryPatch.RegistrationStateForTests == P2PGroupRegistrationState.Failed,
                    "失败态必须可观测");
            }
            finally
            {
                first.UnpatchSelf();
                second.UnpatchSelf();
                P2PGroupStateRecoveryPatch.ResetForTests();
            }
        }

        /// <summary>T14/E6：每次运行打印候选生产 DLL 的 SHA-256，作为字节入档证据。</summary>
        internal static void Test_CandidateDllHashRecorded()
        {
            string path = typeof(P2PGroupStateRecoveryPatch).Assembly.Location;
            Assert(!string.IsNullOrEmpty(path) && File.Exists(path),
                "候选生产 DLL 必须真实存在（测试 HintPath 绑定）");
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                string hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
                Console.WriteLine("[T14] SPF_DLL_SHA256=" + hash + " path=" + path);
            }
        }

        /// <summary>
        /// t18/spec-F2：生产补发接线必须可判——ExecuteDeferredLoad 内必须有且仅有一次
        /// NotifyFlagsUpdated 调用；删掉该调用后本用例必须变红。
        /// </summary>
        internal static void Test_ProductionWiring_ExecuteDeferredLoadCallsNotifyFlags()
        {
            MethodInfo execute = AccessTools.Method(typeof(P2PGroupStateRecoveryPatch), "ExecuteDeferredLoad");
            Assert(execute != null, "必须存在 ExecuteDeferredLoad");
            int notifyCalls = IlContractProbe.CountMethodCalls(execute,
                "SteamP2PFriends.Core.Patches.P2PGroupStateRecovery.P2PGroupStateRecoveryPatch", "NotifyFlagsUpdated");
            Assert(notifyCalls == 1, "生产路径必须恰好接入一次 NotifyFlagsUpdated，实际=" + notifyCalls);
        }

        /// <summary>
        /// T24/F1：原生 load 在真实生产接缝上抛错 → 本次保存必须被拒绝（fail-closed），
        /// 同场景重复调用仍拒绝；账本 LoadFailed + Degraded，且不重试、不补发通知。
        /// </summary>
        internal static void Test_LoadThrows_DegradesAndDoesNotRetry()
        {
            P2PGroupStateRecoveryPatch.ResetForTests();
            P2PGroupStateRecoveryPatch.EligibilityResolverForTests = _ => Eligible();
            P2PGroupStateRecoveryPatch.MapNameResolverForTests = () => "PEI";
            P2PGroupStateRecoveryPatch.NativeGroupTableProbeForTests = () => true;
            int loadAttempts = 0;
            P2PGroupStateRecoveryPatch.DeferredLoadInvokerForTests = _ =>
            {
                loadAttempts++;
                throw new InvalidOperationException("t24-native-load-threw");
            };
            try
            {
                PlayerQuests quests = NewPlayerQuests();
                Assert(!P2PGroupStateRecoveryPatch.Prefix_Load(quests), "前置：首次 load 应延后");
                GroupStateRecoveryCore core = P2PGroupStateRecoveryPatch.CoreForTests;
                core.NoteGroupTableLoaded("PEI");

                Assert(!P2PGroupStateRecoveryPatch.Prefix_Save(quests),
                    "原生 load 抛错后必须拒绝本次保存（fail-closed）");
                Assert(!P2PGroupStateRecoveryPatch.Prefix_Save(quests),
                    "同一场景重复保存仍必须被拒绝");
                Assert(loadAttempts == 1, "原生 load 只允许尝试一次，实际=" + loadAttempts);
                Assert(core.Ledger.RealLoadSuccess == 0, "抛错不得记为 LoadSuccess");
                Assert(core.Ledger.State == GroupLeaseState.LoadFailed, "账本状态必须是 LoadFailed，实际=" + core.Ledger.State);
                Assert(core.Ledger.Degraded, "load 抛错必须置 Degraded");
                Assert(!core.Ledger.CanNotify(), "失败后不得补发 flags 通知");
                // t23 §5：load 失败后必须 0 次补发通知（账本证据，不只是 CanNotify 闸门）。
                Assert(core.Ledger.NotifyAttempts == 0, "load 失败后 NotifyAttempts 必须为 0，实际=" + core.Ledger.NotifyAttempts);
                Assert(P2PGroupStateRecoveryPatch.IsSaveBlockedForTests(quests),
                    "失败实例必须在写入安全表里保持 Blocked");
            }
            finally
            {
                P2PGroupStateRecoveryPatch.ResetForTests();
            }
        }

        /// <summary>
        /// T24/F1：保存被拒时不得触碰存档——写段哨兵 0 次，隔离临时文件字节与 mtime 均不变。
        /// </summary>
        internal static void Test_SaveBlocked_LeavesArchiveFileUntouched()
        {
            P2PGroupStateRecoveryPatch.ResetForTests();
            P2PGroupStateRecoveryPatch.EligibilityResolverForTests = _ => Eligible();
            P2PGroupStateRecoveryPatch.MapNameResolverForTests = () => "PEI";
            P2PGroupStateRecoveryPatch.NativeGroupTableProbeForTests = () => true;
            // 真实生产失败接缝：原生 load 在到期兑现时抛错 → 写安全证明永不完成 → save 必须被拒。
            P2PGroupStateRecoveryPatch.DeferredLoadInvokerForTests = _ =>
                throw new InvalidOperationException("t24-write-sentinel");
            string dir = Path.Combine(Path.GetTempPath(), "spf-t24-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string savePath = Path.Combine(dir, "Quests.dat");
            byte[] original = { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };
            File.WriteAllBytes(savePath, original);
            DateTime writtenAt = File.GetLastWriteTimeUtc(savePath);
            int writeSectionCalls = 0;
            try
            {
                PlayerQuests quests = NewPlayerQuests();
                // R3 前置：把实例置成"内存看起来可保存"（wasLoadCalled=true 且已有部分改动），
                // 证明拦截来自写入安全门，而不是 load 从未被调用这条巧合路径。
                // 用 SetValueDirect（TypedReference 写字段）而不是 FieldInfo.SetValue：
                // 后者会触发 PlayerQuests 类型初始化，在无 Unity 运行时的控制台里必然失败。
                // FieldInfo.SetValue / SetValueDirect 都会触发 PlayerQuests 类型初始化，
                // 在无 Unity 运行时的控制台里必然失败；用 DynamicMethod 直接 stfld 才只写字段。
                SetInstanceFieldRaw(quests, "wasLoadCalled", true);
                var partialFlags = new System.Collections.Generic.List<PlayerQuestFlag>
                {
                    new PlayerQuestFlag(1, 1)
                };
                // 同理用 DynamicMethod 写 backing field，避免触发类型初始化。
                SetInstanceFieldRaw(quests, "<flagsList>k__BackingField", partialFlags);
                Assert(!P2PGroupStateRecoveryPatch.Prefix_Load(quests), "前置：首次 load 应延后");
                P2PGroupStateRecoveryPatch.CoreForTests.NoteGroupTableLoaded("PEI");

                bool allowed = P2PGroupStateRecoveryPatch.Prefix_Save(quests);
                if (allowed)
                {
                    writeSectionCalls++;
                    File.WriteAllBytes(savePath, new byte[] { 0x00 });
                }
                Assert(!allowed, "load 失败的实例必须拒绝保存");
                Assert(writeSectionCalls == 0, "写段哨兵必须保持 0 次");

                byte[] after = File.ReadAllBytes(savePath);
                Assert(after.Length == original.Length, "存档字节长度不得改变");
                for (int i = 0; i < original.Length; i++)
                    Assert(after[i] == original[i], "存档第 " + i + " 字节不得改变");
                Assert(File.GetLastWriteTimeUtc(savePath) == writtenAt, "存档 mtime 不得改变");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
                P2PGroupStateRecoveryPatch.ResetForTests();
            }
        }

        /// <summary>
        /// T24/F2 Ready 否定：空地图或未完成本 epoch GAME 时授权必须拒绝且保留 pending；
        /// 只有非空地图 + 本 epoch 完成后才恰好授权一次。
        /// </summary>
        internal static void Test_NotReady_NeverAuthorizes()
        {
            var core = new GroupStateRecoveryCore();
            Assert(core.OnLoadRequested(73L, "PEI") == GroupLoadDecision.Deferred, "前置：应延后");

            Assert(core.TryTakeDeferredLoad("PEI", out _) == DeferredLoadOutcome.NotReady,
                "未完成 GAME 时必须拒绝授权");
            Assert(core.HasPending, "拒绝授权后 pending 必须保留");
            Assert(core.DeferredLoadNotReady == 1, "未就绪拒绝次数必须可观测");

            core.NoteGroupTableLoaded(string.Empty);
            Assert(!core.IsGroupsReadyFor(string.Empty), "空地图不得算就绪");
            Assert(core.TryTakeDeferredLoad("PEI", out _) == DeferredLoadOutcome.NotReady,
                "空地图登记不得放行 PEI 租约");

            core.NoteGroupTableLoaded("PEI");
            Assert(core.IsGroupsReadyFor("PEI"), "本 epoch + 非空地图必须算就绪");
            Assert(core.TryTakeDeferredLoad("PEI", out long token) == DeferredLoadOutcome.Authorized,
                "就绪后必须授权一次");
            Assert(token == 73L, "授权 token 必须匹配");
            Assert(core.FlushAuthorizations == 1, "授权只允许一次");
        }


        /// <summary>
        /// t18/spec-F4：ResetSession 清零账本前必须显式退役旧租约，
        /// 证明"新 epoch 的 0"是新租约的 0，而不是抹账造成的假象。
        /// </summary>
        internal static void Test_ResetSession_RetiresOldLeaseBeforeReset()
        {
            GroupStateRecoveryCore core = LeaseWithLoadSuccess(86L, "PEI");
            long epochBefore = core.Epoch;
            core.ResetSession("t18-second-session");

            Assert(core.RetiredLeases == 1, "必须记录一次租约退役，实际=" + core.RetiredLeases);
            Assert(core.LastRetiredToken == 86L, "退役 token 必须可核验，实际=" + core.LastRetiredToken);
            Assert(core.LastRetiredEpoch == epochBefore, "退役 epoch 必须可核验，实际=" + core.LastRetiredEpoch);
            Assert(core.Epoch == epochBefore + 1, "epoch 必须递增");
            Assert(core.Ledger.Token == 0L, "新 epoch 账本必须是新租约（token=0 表示未开始）");
            Assert(core.Ledger.State == GroupLeaseState.None, "新 epoch 账本状态必须是 None");
            P2PGroupStateRecoveryPatch.ResetForTests();
        }

        /// <summary>
        /// T24/F1 正控：新租约在新 epoch 成功后 save 放行；第二个存档文件保持原样（隔离）。
        /// </summary>
        internal static void Test_NewLease_CompletesAndSecondArchiveUntouched()
        {
            P2PGroupStateRecoveryPatch.ResetForTests();
            P2PGroupStateRecoveryPatch.EligibilityResolverForTests = _ => Eligible();
            P2PGroupStateRecoveryPatch.MapNameResolverForTests = () => "PEI";
            P2PGroupStateRecoveryPatch.NativeGroupTableProbeForTests = () => true;
            int loadInvocations = 0;
            P2PGroupStateRecoveryPatch.DeferredLoadInvokerForTests = _ => loadInvocations++;
            string otherPath = Path.Combine(Path.GetTempPath(), "spf-t24-other-" + Guid.NewGuid().ToString("N") + ".dat");
            File.WriteAllBytes(otherPath, new byte[] { 0x7A, 0x7B, 0x7C });
            DateTime otherStamp = File.GetLastWriteTimeUtc(otherPath);
            try
            {
                PlayerQuests quests = NewPlayerQuests();
                Assert(!P2PGroupStateRecoveryPatch.Prefix_Load(quests), "前置：首局应延后");
                GroupStateRecoveryCore core = P2PGroupStateRecoveryPatch.CoreForTests;
                core.NoteGroupTableLoaded("PEI");
                Assert(P2PGroupStateRecoveryPatch.Prefix_Save(quests), "首局成功兑现后必须放行 save");

                core.ResetSession("t24-second-session");
                Assert(P2PGroupStateRecoveryPatch.Prefix_Save(quests), "上一局已完成的对象在换局后仍可保存");
                Assert(loadInvocations == 1, "换局不得重复执行上一局 load");

                Assert(!P2PGroupStateRecoveryPatch.Prefix_Load(quests), "新 epoch 同图必须重新延后");
                core.NoteGroupTableLoaded("PEI");
                Assert(P2PGroupStateRecoveryPatch.Prefix_Save(quests), "新租约成功后必须放行");
                Assert(loadInvocations == 2, "新租约只允许再执行一次 load，实际=" + loadInvocations);
                Assert(File.GetLastWriteTimeUtc(otherPath) == otherStamp, "第二个存档文件 mtime 不得改变");
                Assert(File.ReadAllBytes(otherPath).Length == 3, "第二个存档文件不得被写入");
            }
            finally
            {
                try { File.Delete(otherPath); } catch { }
                P2PGroupStateRecoveryPatch.ResetForTests();
            }
        }

        /// <summary>
        /// T24/F1 持久屏障：pending 期间换局（ResetSession）不得抹掉活对象上的失败屏障；
        /// 旧脏对象在新 epoch 未完成自己的一次成功 load 前仍被拒绝保存。
        /// </summary>
        internal static void Test_ResetSession_KeepsDirtyObjectBarrier()
        {
            P2PGroupStateRecoveryPatch.ResetForTests();
            P2PGroupStateRecoveryPatch.EligibilityResolverForTests = _ => Eligible();
            P2PGroupStateRecoveryPatch.MapNameResolverForTests = () => "PEI";
            P2PGroupStateRecoveryPatch.NativeGroupTableProbeForTests = () => true;
            try
            {
                PlayerQuests quests = NewPlayerQuests();
                Assert(!P2PGroupStateRecoveryPatch.Prefix_Load(quests), "前置：首局应延后");
                GroupStateRecoveryCore core = P2PGroupStateRecoveryPatch.CoreForTests;
                // 走真实生产边界入口（MENU 关卡回调 → ResetBoundary → ResetSession），而不是直接调 Core，
                // 这样"边界是否抹掉活对象写入证明"才由生产胶水决定（T24 变异④必须能变红）。
                P2PGroupStateRecoveryPatch.InvokeLevelLoadedForTests(Level.BUILD_INDEX_MENU);
                Assert(P2PGroupStateRecoveryPatch.IsSaveBlockedForTests(quests),
                    "ResetSession 不得抹掉活对象上的写入失败屏障");
                Assert(!P2PGroupStateRecoveryPatch.Prefix_Save(quests),
                    "未完成成功 load 的旧脏对象必须继续拒绝保存");
                Assert(core.Ledger.RealLoadSuccess == 0, "旧脏对象不得凭空变成成功");
            }
            finally
            {
                P2PGroupStateRecoveryPatch.ResetForTests();
            }
        }


        /// <summary>
        /// T24/T22 不变式：正常 load 成功但监听者通知抛异常时，只记 NotificationFailed，
        /// 不得因此禁止本局保存（save 仍放行）。
        /// </summary>
        internal static void Test_NotifyThrows_DoesNotBlockSave()
        {
            P2PGroupStateRecoveryPatch.ResetForTests();
            P2PGroupStateRecoveryPatch.EligibilityResolverForTests = _ => Eligible();
            P2PGroupStateRecoveryPatch.MapNameResolverForTests = () => "PEI";
            P2PGroupStateRecoveryPatch.NativeGroupTableProbeForTests = () => true;
            int loadInvocations = 0;
            P2PGroupStateRecoveryPatch.DeferredLoadInvokerForTests = quests =>
            {
                loadInvocations++;
                // flagsList 是 public get / private set：用私有 setter 初始化，模拟"真的读到了存档 flag"。
                var flagList = new System.Collections.Generic.List<PlayerQuestFlag>
                {
                    new PlayerQuestFlag(1, 1)
                };
                typeof(PlayerQuests).GetProperty("flagsList").GetSetMethod(true)
                    .Invoke(quests, new object[] { flagList });
                quests.onFlagsUpdated = () => throw new InvalidOperationException("t24-notify-threw");
            };
            try
            {
                PlayerQuests quests = NewPlayerQuests();
                Assert(!P2PGroupStateRecoveryPatch.Prefix_Load(quests), "前置：应延后");
                GroupStateRecoveryCore core = P2PGroupStateRecoveryPatch.CoreForTests;
                core.NoteGroupTableLoaded("PEI");

                Assert(P2PGroupStateRecoveryPatch.Prefix_Save(quests),
                    "load 已成功，通知抛错不得禁止本局保存 (pending=" + core.HasPending
                    + " blocked=" + P2PGroupStateRecoveryPatch.IsSaveBlockedForTests(quests)
                    + " completed=" + P2PGroupStateRecoveryPatch.IsNativeLoadCompletedForTests(quests)
                    + " exec=" + core.ExecutingDeferredLoad + " notifying=" + core.Notifying
                    + " state=" + core.Ledger.State + " fail=" + core.Ledger.Reason + ")");
                Assert(loadInvocations == 1, "原生 load 只执行一次");
                Assert(core.Ledger.RealLoadSuccess == 1, "load 正常返回必须记成功");
                Assert(core.Ledger.NotifyFailed == 1, "通知抛错必须记 NotificationFailed");
                Assert(core.Ledger.NotifyAttempts == 1, "通知只允许尝试一次");
                Assert(P2PGroupStateRecoveryPatch.IsNativeLoadCompletedForTests(quests),
                    "通知失败不得回滚写入安全证明");
            }
            finally
            {
                P2PGroupStateRecoveryPatch.ResetForTests();
            }
        }

        /// <summary>
        /// T24/F2：native knownGroups 为 null（非 IDictionary）时，save 截止点也不得授权兑现；
        /// 只有真实组表存在后，才允许"兑现一次 + 放行保存"。
        /// </summary>
        internal static void Test_NullNativeGroupTable_BlocksSaveDeadlineFlush()
        {
            P2PGroupStateRecoveryPatch.ResetForTests();
            P2PGroupStateRecoveryPatch.EligibilityResolverForTests = _ => Eligible();
            P2PGroupStateRecoveryPatch.MapNameResolverForTests = () => "PEI";
            bool tablePresent = false;
            P2PGroupStateRecoveryPatch.NativeGroupTableProbeForTests = () => tablePresent;
            int loadInvocations = 0;
            P2PGroupStateRecoveryPatch.DeferredLoadInvokerForTests = _ => loadInvocations++;
            try
            {
                PlayerQuests quests = NewPlayerQuests();
                Assert(!P2PGroupStateRecoveryPatch.Prefix_Load(quests), "前置：应延后");
                GroupStateRecoveryCore core = P2PGroupStateRecoveryPatch.CoreForTests;
                core.NoteGroupTableLoaded("PEI");

                Assert(!P2PGroupStateRecoveryPatch.Prefix_Save(quests),
                    "native 组表为 null 时必须拒绝保存");
                Assert(core.FlushAuthorizations == 0, "组表为 null 时不得授权任何 load");
                Assert(loadInvocations == 0, "组表为 null 时不得执行原生 load");
                Assert(core.HasPending, "拒绝后 pending 必须保留，等待真正的 GAME 组表");

                tablePresent = true;
                Assert(P2PGroupStateRecoveryPatch.Prefix_Save(quests),
                    "真实组表就绪后必须兑现一次并放行保存");
                Assert(loadInvocations == 1, "只允许执行一次");
                Assert(core.FlushAuthorizations == 1, "只允许授权一次");
            }
            finally
            {
                P2PGroupStateRecoveryPatch.ResetForTests();
            }
        }

        /// <summary>
        /// Bug3 根因（T31）红→绿：主机本地玩家初始化早期 SteamPlayer.playerID 仍为 null。
        /// 旧实现用 SteamPlayerID 的 != 运算符直接判空，而该运算符会直接解引用
        /// playerID_0.steamID → NullReferenceException，判定路径被打断，延后加载从未生效。
        /// 判空必须发生在调用运算符之前：判定返回"不合格"，且不抛异常。
        /// </summary>
        internal static void Test_Gate_NullPlayerIdIsNotEligibleAndDoesNotThrow()
        {
            var owner = (SteamPlayer)FormatterServices.GetUninitializedObject(typeof(SteamPlayer));
            bool bound;
            ulong steamId;
            try
            {
                bound = P2PGroupStateRecoveryPatch.HasBoundPlayerId(owner);
                steamId = P2PGroupStateRecoveryPatch.BoundSteamIdOf(owner);
            }
            catch (Exception ex)
            {
                Assert(false, "playerID=null 的判定路径不得抛异常，实际抛出 "
                    + ex.GetType().FullName + ": " + ex.Message);
                return;
            }
            Assert(!bound, "playerID=null 必须判为未绑定（不合格）");
            Assert(steamId == 0UL, "playerID=null 时 SteamID 必须为 0");

            // T32 守卫（R5-01）：上面只证明 helper 安全，不能证明生产 BuildEligibility 真的走了 helper。
            // IL 契约：BuildEligibility 自身不得调用 SDK 的 SteamPlayer::get_playerID（其重载运算符
            // 直接解引用 playerID_0.steamID，null 即 NRE），且必须调用 ReferenceEquals 判空的 HasBoundPlayerId。
            MethodInfo buildEligibility = AccessTools.Method(typeof(P2PGroupStateRecoveryPatch), "BuildEligibility");
            Assert(!ReferenceEquals(buildEligibility, null), "生产 BuildEligibility 必须存在（ABI 变化需更新用例）");
            int playerIdGetterCalls = IlContractProbe.CountMethodCalls(buildEligibility,
                "SDG.Unturned.SteamPlayer", "get_playerID");
            Assert(playerIdGetterCalls == 0,
                "IL 守卫失败[1/2]：BuildEligibility 不得调用 SDG.Unturned.SteamPlayer::get_playerID"
                + "（SDK 运算符会解引用 null），实际调用次数=" + playerIdGetterCalls);
            int boundHelperCalls = IlContractProbe.CountMethodCalls(buildEligibility,
                "SteamP2PFriends.Core.Patches.P2PGroupStateRecovery.P2PGroupStateRecoveryPatch", "HasBoundPlayerId");
            Assert(boundHelperCalls >= 1,
                "IL 守卫失败[2/2]：BuildEligibility 必须调用 HasBoundPlayerId（ReferenceEquals 判空），"
                + "实际调用次数=" + boundHelperCalls);

            GroupRecoveryEligibility host = Eligible();
            host.HasOwningSteamPlayer = bound;
            host.OwnerSteamId = steamId;
            Assert(!host.IsEligible, "playerID=null 时完整 Host 事实必须判为不合格（放行原生 load）");
        }

        /// <summary>
        /// T31：修复后正常 Host（playerID 已绑定、loopback、唯一本地客户端、动态组开启）
        /// 仍必须进入 defer；门控行必须含 12 项布尔 + token/epoch/map + decision=Deferred。
        /// </summary>
        internal static void Test_Gate_NormalHostStillDefers()
        {
            const ulong hostId = 76561198000000001UL;
            var owner = (SteamPlayer)FormatterServices.GetUninitializedObject(typeof(SteamPlayer));
            SetInstanceFieldRaw(owner, "_playerID", new SteamPlayerID(
                new Steamworks.CSteamID(hostId), 0, "HOST", "HOST", "HOST",
                new Steamworks.CSteamID(0UL)));
            Assert(P2PGroupStateRecoveryPatch.HasBoundPlayerId(owner), "已绑定 playerID 必须判为存在");
            Assert(P2PGroupStateRecoveryPatch.BoundSteamIdOf(owner) == hostId, "SteamID 必须原样取出");

            GroupRecoveryEligibility host = Eligible();
            host.HasOwningSteamPlayer = P2PGroupStateRecoveryPatch.HasBoundPlayerId(owner);
            host.OwnerSteamId = P2PGroupStateRecoveryPatch.BoundSteamIdOf(owner);
            host.ProviderUserId = hostId;
            Assert(host.IsEligible, "正常 Host 必须判定为合格");

            var core = new GroupStateRecoveryCore();
            Assert(GroupLoadPlanner.Decide(host, "PEI", 77L, core) == GroupLoadDecision.Deferred,
                "正常 Host 首次 load 仍必须进入 defer");

            string line = P2PGroupStateRecoveryPatch.FormatGateLine(host, 77L, core.Epoch, "PEI", "Deferred");
            Assert(line.StartsWith("gate ", StringComparison.Ordinal), "门控行必须以 gate 开头: " + line);
            Assert(line.Contains("token=77") && line.Contains("epoch=0") && line.Contains("map=PEI")
                && line.Contains("decision=Deferred"), "门控行必须含 token/epoch/map/decision: " + line);
            Assert(CountGateFlags(line) == 12,
                "门控行必须含 12 项布尔，实际=" + CountGateFlags(line) + ": " + line);

            MethodInfo prefix = AccessTools.Method(typeof(P2PGroupStateRecoveryPatch), "Prefix_Load");
            Assert(IlContractProbe.CountMethodCalls(prefix,
                    "SteamP2PFriends.Core.Patches.P2PGroupStateRecovery.P2PGroupStateRecoveryPatch", "LogGate") >= 2,
                "Prefix_Load 的合格/不合格分支都必须输出门控日志");
            MethodInfo logGate = AccessTools.Method(typeof(P2PGroupStateRecoveryPatch), "LogGate");
            Assert(IlContractProbe.CountMethodCalls(logGate, "SteamP2PFriends.Shared.RoleLogger", "Info") == 1,
                "门控日志必须走 RoleLogger.Info（每次判定一条，不逐帧刷）");
        }

        /// <summary>
        /// T31 根因证据（特征化，不改 SDK）：SDG.Unturned.SteamPlayerID 的 ==/!= 运算符
        /// 直接解引用 playerID_0.steamID，对 null 调用必抛 NullReferenceException。
        /// 本用例只记录该 SDK 行为，证明生产判空为什么必须用 ReferenceEquals 而不是运算符。
        /// </summary>
        internal static void Test_Gate_SdkOperatorOnNullThrowsCharacterization()
        {
            SteamPlayerID absent = null;
            bool threw = false;
            try
            {
                _ = absent != null;
            }
            catch (NullReferenceException)
            {
                threw = true;
            }
            Assert(threw, "根因证据：SteamPlayerID 运算符在 null 上会抛 NRE（SDK 行为，不修改 SDK）");
            Assert(ReferenceEquals(absent, null), "ReferenceEquals 判空必须安全返回 true");
            Assert(!P2PGroupStateRecoveryPatch.HasBoundPlayerId(null), "owner=null 必须判为未绑定");
        }

        /// <summary>
        /// T32 守卫（R5-02）：真实执行生产 BuildEligibility，使「生产恒不合格 / 调用点回归」变红。
        /// 本机测试进程无法构造 Unity 本地玩家：Player..cctor 会触发 NetReflection 注册并因缺
        /// SDG.HostBans.Runtime 抛 TypeInitializationException（GBA01/NIL01 即该既有环境失败）。
        /// 因此 quests.player 保持 null，让生产代码 `player != null && ReferenceEquals(player, Player.LocalPlayer)`
        /// 短路，从而在不触发 Player cctor 的前提下真实执行其余全部判定：
        /// HostManager/Provider/Dedicator、channel.isOwner、owner.playerID、loopback 传输、Provider.clients 计数、动态组开关。
        /// 覆盖力：M1b（:962 改回 owner.playerID != null）会在生产 BuildEligibility 内抛 NRE；
        /// M2（Provider.user.m_SteamID + 1UL）会让 ProviderUserId != OwnerSteamId；
        /// return default 会让 HasOwningSteamPlayer=false —— 三类回归都会让本用例红。
        /// </summary>
        internal static void Test_Gate_ProductionBuildEligibilityRealMapping()
        {
            const ulong hostId = 76561198000000001UL;

            var owner = (SteamPlayer)FormatterServices.GetUninitializedObject(typeof(SteamPlayer));
            SetInstanceFieldOn(typeof(SteamPlayer), owner, "_playerID", new SteamPlayerID(
                new Steamworks.CSteamID(hostId), 0, "HOST", "HOST", "HOST", new Steamworks.CSteamID(0UL)));
            Type loopbackType = Type.GetType(
                "SDG.NetTransport.Loopback.TransportConnection_Loopback, Assembly-CSharp", false);
            Assert(!ReferenceEquals(loopbackType, null), "找不到 SDK loopback 传输类型（ABI 变化需更新用例）");
            object loopback = Activator.CreateInstance(loopbackType);
            SetInstanceFieldOn(typeof(SteamConnectedClientBase), owner, "<transportConnection>k__BackingField", loopback);

            var channel = (SteamChannel)FormatterServices.GetUninitializedObject(typeof(SteamChannel));
            SetInstanceFieldOn(typeof(SteamChannel), channel, "owner", owner);
            SetInstanceFieldOn(typeof(SteamChannel), channel, "isOwner", true);
            // Unity fake-null：SteamChannel 是 UnityEngine.Object，生产代码 `channel != null` 编译为
            // UnityEngine.Object::op_Inequality → CompareBaseObjects → IsNativeObjectAlive(m_CachedPtr != 0)。
            // FormatterServices 造出的对象 m_CachedPtr=0，会被 Unity 判为“已销毁的 null”；
            // 必须给一个非零缓存指针，才能让生产判定看到“存活”的 channel（本路径只做字段读取，不解引用原生指针）。
            Type unityObjectType = typeof(SteamChannel).BaseType;
            while (unityObjectType != null && unityObjectType.FullName != "UnityEngine.Object")
                unityObjectType = unityObjectType.BaseType;
            Assert(!ReferenceEquals(unityObjectType, null), "找不到 UnityEngine.Object 基类");
            SetInstanceFieldOn(unityObjectType, channel, "m_CachedPtr", new IntPtr(1));

            var quests = NewPlayerQuests();
            SetInstanceFieldRaw(quests, "_channel", channel);
            Assert(ReferenceEquals(quests.channel, channel), "fixture：quests._channel 写入必须可读回");
            Assert(channel.IsLocalPlayer, "fixture：channel.isOwner=true 必须让 IsLocalPlayer 为真");

            var config = (ModeConfigData)FormatterServices.GetUninitializedObject(typeof(ModeConfigData));
            var gameplay = (GameplayConfigData)FormatterServices.GetUninitializedObject(typeof(GameplayConfigData));
            SetInstanceFieldOn(typeof(GameplayConfigData), gameplay, "Allow_Dynamic_Groups", true);
            SetInstanceFieldOn(typeof(ModeConfigData), config, "Gameplay", gameplay);

            Type hostManagerType = typeof(SteamP2PFriends.Host.HostManager);
            FieldInfo hostModeField = hostManagerType.GetField("_hostMode", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo activeField = hostManagerType.GetField(
                "<IsP2PServerActive>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo serverField = typeof(Provider).GetField("_isServer", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo userField = typeof(Provider).GetField("_user", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo configField = typeof(Provider).GetField("_modeConfigData", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo clientsField = typeof(Provider).GetField("_clients", BindingFlags.Static | BindingFlags.NonPublic);
            Assert(hostModeField != null && activeField != null && serverField != null && userField != null
                && configField != null && clientsField != null,
                "生产 BuildEligibility 依赖的静态字段必须齐全（ABI 变化需更新用例）");

            object originalHostMode = hostModeField.GetValue(null);
            object originalActive = activeField.GetValue(null);
            object originalServer = serverField.GetValue(null);
            object originalUser = userField.GetValue(null);
            object originalConfig = configField.GetValue(null);
            object originalClients = clientsField.GetValue(null);
            try
            {
                hostModeField.SetValue(null, Enum.Parse(hostModeField.FieldType, "P2P"));
                activeField.SetValue(null, true);
                serverField.SetValue(null, true);
                userField.SetValue(null, new Steamworks.CSteamID(hostId));
                configField.SetValue(null, config);
                clientsField.SetValue(null, new System.Collections.Generic.List<SteamPlayer> { owner });

                GroupRecoveryEligibility eligibility;
                try
                {
                    eligibility = P2PGroupStateRecoveryPatch.BuildEligibility(quests);
                }
                catch (Exception ex)
                {
                    Assert(false, "生产 BuildEligibility 不得抛异常（playerID 已绑定/未绑定都必须先 ReferenceEquals 判空），实际 "
                        + ex.GetType().FullName + ": " + ex.Message);
                    return;
                }

                Assert(eligibility.IsP2PHostMode && eligibility.IsServer && !eligibility.IsDedicatedServer,
                    "生产 BuildEligibility 必须读到 HostManager/Provider/Dedicator 事实");
                Assert(eligibility.ChannelIsLocalPlayer,
                    "channel.isOwner=true 必须映射为 ChannelIsLocalPlayer（diag: channelLocal=" + channel.IsLocalPlayer
                    + ", sameChannel=" + ReferenceEquals(quests.channel, channel)
                    + ", host=" + eligibility.IsP2PHostMode + ", server=" + eligibility.IsServer
                    + ", dedicated=" + eligibility.IsDedicatedServer + ", playerLocal=" + eligibility.PlayerIsLocalInstance
                    + ", owner=" + eligibility.HasOwningSteamPlayer + ", loopback=" + eligibility.HasLoopbackConnection
                    + ", ownerId=" + eligibility.OwnerSteamId + ", providerId=" + eligibility.ProviderUserId
                    + ", count=" + eligibility.MatchingLocalClientCount + ", dyn=" + eligibility.DynamicGroupsAllowed + "）");
                Assert(eligibility.HasOwningSteamPlayer, "已绑定 playerID 必须映射为 HasOwningSteamPlayer=true");
                Assert(eligibility.OwnerSteamId == hostId,
                    "OwnerSteamId 必须等于 owner.playerID，实际=" + eligibility.OwnerSteamId);
                Assert(eligibility.ProviderUserId == hostId,
                    "ProviderUserId 必须等于 Provider.user.m_SteamID（M2 若被 +1 此处必红），实际="
                    + eligibility.ProviderUserId);
                Assert(eligibility.MatchingLocalClientCount == 1, "Provider.clients 唯一匹配必须计为 1");
                Assert(eligibility.HasLoopbackConnection, "loopback 传输必须映射为 HasLoopbackConnection=true");
                Assert(eligibility.DynamicGroupsAllowed, "modeConfigData.Gameplay.Allow_Dynamic_Groups 必须映射");
                Assert(!eligibility.PlayerIsLocalInstance,
                    "测试进程无 Unity 本地玩家（quests.player=null 短路 Player.LocalPlayer），该事实必须为 false");
                Assert(!eligibility.IsEligible,
                    "PlayerIsLocalInstance=false 时整体必须不合格（环境限制，不得放行 defer）");
            }
            finally
            {
                hostModeField.SetValue(null, originalHostMode);
                activeField.SetValue(null, originalActive);
                serverField.SetValue(null, originalServer);
                userField.SetValue(null, originalUser);
                configField.SetValue(null, originalConfig);
                clientsField.SetValue(null, originalClients);
            }
        }

        private static int CountGateFlags(string line)
        {
            int count = 0;
            string[] parts = line.Split(' ');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].EndsWith("=true", StringComparison.Ordinal)
                    || parts[i].EndsWith("=false", StringComparison.Ordinal))
                    count++;
            }
            return count;
        }

        private static int CountOwnedPrefix(string ownerId)
        {
            Patches info = Harmony.GetPatchInfo(P2PGroupStateRecoveryPatch.LoadTargetForTests);
            if (info == null)
                return 0;
            int count = 0;
            foreach (Patch patch in info.Prefixes)
            {
                if (patch.owner == ownerId && patch.PatchMethod == P2PGroupStateRecoveryPatch.LoadPrefixMethod)
                    count++;
            }
            return count;
        }

        private static GroupRecoveryEligibility Eligible()
        {
            return new GroupRecoveryEligibility
            {
                IsP2PHostMode = true,
                IsServer = true,
                IsDedicatedServer = false,
                PlayerIsLocalInstance = true,
                ChannelIsLocalPlayer = true,
                HasOwningSteamPlayer = true,
                HasLoopbackConnection = true,
                OwnerSteamId = 112589994979229698UL,
                ProviderUserId = 112589994979229698UL,
                MatchingLocalClientCount = 1,
                DynamicGroupsAllowed = true
            };
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException("P2PGroupStateRecovery: " + message);
        }
    }
}
