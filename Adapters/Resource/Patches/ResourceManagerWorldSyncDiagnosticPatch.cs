using HarmonyLib;
using SDG.Unturned;
using SteamP2PFriends.Adapters.Resource;
using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.Core.Patches;
using SteamP2PFriends.MultiObserver;
using SteamP2PFriends.Shared;
using System;
using System.Reflection;

namespace SteamP2PFriends.Adapters.Resource.Patches
{
    /// <summary>
    /// ResourceManager 世界同步链路五段证据诊断。
    ///
    /// 五段证据闭环：
    ///   1. 源事件：onRegionUpdated step=3
    ///   2. 资格/目标：player.movement.loadedRegions[x,y].isResourcesLoaded + Regions.checkSafe
    ///   3. 发送入口：SendResources.Invoke（ClientStaticMethod 字段，无法直接 patch，
    ///      但 onRegionUpdated 内的发送条件分支已覆盖；同时 patch SendResources_Write 反映真实写入）
    ///   4. 客机 Receive 入口：ReceiveResources
    ///   5. Receive 后状态/拒绝门控：ResourceManager.regions[x,y].isNetworked
    ///
    ///
    /// vanilla 源码（U3-SDK ResourceManager.cs）：
    ///   - onRegionUpdated: L713（step 3 SendResources.Invoke 调用点 L772）
    ///   - SendResources 字段: L477（ClientStaticMethod）
    ///   - SendResources_Write: L530 `private static void SendResources_Write(NetPakWriter writer, byte x, byte y)`
    ///   - ReceiveResources: L478（静态，in ClientInvocationContext）
    /// </summary>
    public static class ResourceManagerWorldSyncDiagnosticPatch
    {
        private const string PointPrefix = "[WorldSyncDiag/Resource]";
        private const string LoopbackTransportFullName = "SDG.NetTransport.Loopback.TransportConnection_Loopback";

        // 由 RegisterManual 与 VerifyRegistration 共用。
        //   - onRegionUpdated(Player, byte, byte, byte, byte, byte, ref bool)
        //   - SendResources_Write(NetPakWriter, byte, byte) - private static
        //   - ReceiveResources(in ClientInvocationContext)
        private static readonly System.Type[] VanillaOnRegionUpdatedParamTypes =
        {
            typeof(Player),
            typeof(byte), typeof(byte),
            typeof(byte), typeof(byte),
            typeof(byte),
            typeof(bool).MakeByRefType()
        };
        private static readonly System.Type[] VanillaReceiveResourcesParamTypes =
        {
            typeof(SDG.Unturned.ClientInvocationContext).MakeByRefType()
        };

        public static bool OnRegionUpdatedPrefixRegistered { get; private set; }
        public static bool ReceiveResourcesPrefixRegistered { get; private set; }
        public static bool ReceiveResourcesPostfixRegistered { get; private set; }
        public static bool AllRegistrationsSucceeded =>
            OnRegionUpdatedPrefixRegistered && ReceiveResourcesPrefixRegistered
            && ReceiveResourcesPostfixRegistered;

        /// <summary>
        /// 3 个 hook 精确、幂等的 identity-based 手动登记；SendResources_Write
        /// 由 ResourceManagerRegionSyncPatch 唯一拥有。
        ///
        /// </summary>
        public static bool RegisterManual(Harmony harmony)
        {
            RoleLogger.Info("[Shared]", "[WorldSyncDiag/Resource] === 手动登记 3 个 hook（P0-R1～R8 identity-based 幂等）===");

            var patchType = typeof(ResourceManagerWorldSyncDiagnosticPatch);

            bool r1, r3, r4;
            try
            {
                r1 = WorldSyncDiagnosticCore.RegisterIdentityPatch(
                    harmony, typeof(ResourceManager), "onRegionUpdated", VanillaOnRegionUpdatedParamTypes,
                    AccessTools.Method(patchType, "OnRegionUpdated_Prefix"),
                    HarmonyPatchType.Prefix, "Resource.onRegionUpdated.Pre");
            }
            catch (System.Exception ex) { RoleLogger.Error("[Shared]", $"[WorldSyncDiag/Resource] onRegionUpdated.Pre 登记异常: {ex}"); r1 = false; }
            LogRegistrationResult("Resource.onRegionUpdated.Pre", r1,
                "target=ResourceManager.onRegionUpdated patch=OnRegionUpdated_Prefix");

            try
            {
                r3 = WorldSyncDiagnosticCore.RegisterIdentityPatch(
                    harmony, typeof(ResourceManager), "ReceiveResources", VanillaReceiveResourcesParamTypes,
                    AccessTools.Method(patchType, "ReceiveResources_Prefix"),
                    HarmonyPatchType.Prefix, "Resource.ReceiveResources.Pre");
            }
            catch (System.Exception ex) { RoleLogger.Error("[Shared]", $"[WorldSyncDiag/Resource] ReceiveResources.Pre 登记异常: {ex}"); r3 = false; }
            LogRegistrationResult("Resource.ReceiveResources.Pre", r3,
                "target=ResourceManager.ReceiveResources patch=ReceiveResources_Prefix");

            try
            {
                r4 = WorldSyncDiagnosticCore.RegisterIdentityPatch(
                    harmony, typeof(ResourceManager), "ReceiveResources", VanillaReceiveResourcesParamTypes,
                    AccessTools.Method(patchType, "ReceiveResources_Postfix"),
                    HarmonyPatchType.Postfix, "Resource.ReceiveResources.Post");
            }
            catch (System.Exception ex) { RoleLogger.Error("[Shared]", $"[WorldSyncDiag/Resource] ReceiveResources.Post 登记异常: {ex}"); r4 = false; }
            LogRegistrationResult("Resource.ReceiveResources.Post", r4,
                "target=ResourceManager.ReceiveResources patch=ReceiveResources_Postfix");

            bool all = r1 && r3 && r4;
            RoleLogger.Info("[Shared]",
                $"[WorldSyncDiag/Resource] RegisterManual 结果: onRegionUpdated.Pre={r1} " +
                $"ReceiveResources.Pre={r3} ReceiveResources.Post={r4} all={all}");
            ResourceObservability.Info("[Shared]", "WorldSyncRegistration", "-", 0UL, 0UL, 0U,
                all ? "Native" : "Fallback", false, all ? "success" : "failed",
                $"onRegionUpdated={r1} receiveResourcesPre={r3} receiveResourcesPost={r4} " +
                "sendResourcesWrite=owned-by-ResourceRegionSync");
            return all;
        }

        /// <summary>
            /// 与 RegisterManual 使用同一套 Type[]；SendResources_Write 的验证归属
            /// 在 ResourceManagerRegionSyncPatch。
        /// </summary>
        public static bool VerifyRegistration()
        {
            try
            {
                var patchType = typeof(ResourceManagerWorldSyncDiagnosticPatch);

                MethodInfo onRegionUpdatedPre = AccessTools.Method(patchType, "OnRegionUpdated_Prefix");
                MethodInfo receiveResourcesPre = AccessTools.Method(patchType, "ReceiveResources_Prefix");
                MethodInfo receiveResourcesPost = AccessTools.Method(patchType, "ReceiveResources_Postfix");

                OnRegionUpdatedPrefixRegistered = WorldSyncDiagnosticCore.IsPatchRegistered(
                    typeof(ResourceManager), "onRegionUpdated", onRegionUpdatedPre, HarmonyPatchType.Prefix, VanillaOnRegionUpdatedParamTypes);
                ReceiveResourcesPrefixRegistered = WorldSyncDiagnosticCore.IsPatchRegistered(
                    typeof(ResourceManager), "ReceiveResources", receiveResourcesPre, HarmonyPatchType.Prefix, VanillaReceiveResourcesParamTypes);
                ReceiveResourcesPostfixRegistered = WorldSyncDiagnosticCore.IsPatchRegistered(
                    typeof(ResourceManager), "ReceiveResources", receiveResourcesPost, HarmonyPatchType.Postfix, VanillaReceiveResourcesParamTypes);

                LogRegistrationResult("Resource.onRegionUpdated.Pre", OnRegionUpdatedPrefixRegistered,
                    "verification=true target=ResourceManager.onRegionUpdated patch=OnRegionUpdated_Prefix");
                LogRegistrationResult("Resource.ReceiveResources.Pre", ReceiveResourcesPrefixRegistered,
                    "verification=true target=ResourceManager.ReceiveResources patch=ReceiveResources_Prefix");
                LogRegistrationResult("Resource.ReceiveResources.Post", ReceiveResourcesPostfixRegistered,
                    "verification=true target=ResourceManager.ReceiveResources patch=ReceiveResources_Postfix");

                if (!AllRegistrationsSucceeded)
                {
                    RoleLogger.Error("[Shared]",
                        $"[WorldSyncDiag/Resource] !!! 注册验证失败: " +
                        $"onRegionUpdated.Pre={OnRegionUpdatedPrefixRegistered} " +
                        $"ReceiveResources.Pre={ReceiveResourcesPrefixRegistered} " +
                        $"ReceiveResources.Post={ReceiveResourcesPostfixRegistered} " +
                        $"(owner={SteamP2PFriendsPlugin.HARMONY_ID}, identity-based, 共用 VanillaXxxParamTypes)");
                    return false;
                }

            RoleLogger.Info("[Shared]",
                $"[WorldSyncDiag/Resource] OK 3 个 hook 均已注册 (owner={SteamP2PFriendsPlugin.HARMONY_ID}, identity-based, SendResources_Write 由 ResourceRegionSync 唯一拥有)");
                return true;
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[WorldSyncDiag/Resource] VerifyRegistration 异常: {ex.Message}");
                OnRegionUpdatedPrefixRegistered = false;
                ReceiveResourcesPrefixRegistered = ReceiveResourcesPostfixRegistered = false;
                return false;
            }
        }

        // ============= 1. 源事件：onRegionUpdated step=3 =============
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ResourceManager), "onRegionUpdated")]
        public static void OnRegionUpdated_Prefix(
            Player player,
            byte old_x, byte old_y,
            byte new_x, byte new_y,
            byte step,
            ref bool canIncrementIndex)
        {
            try
            {
                if (step != 3)
                {
                    ResourceObservability.Info("[Host]", "RegionEntry", $"({new_x},{new_y})", 0UL, 0UL, 0U,
                        "Native", false, "skipped", "reason=step-not-3 step=" + step);
                    return;
                }

                ulong steamId;
                string steamIdReason;
                bool steamIdRead = TryReadSteamId(player, out steamId, out steamIdReason);
                string maskedId = WorldSyncDiagnosticCore.MaskSteamId(steamId);

                bool isDedicated = Dedicator.IsDedicatedServer;
                bool isResourcesLoaded;
                string resourcesLoadedReason;
                bool resourcesLoadedRead = TryReadRegionResourcesLoaded(
                    player, new_x, new_y, out isResourcesLoaded, out resourcesLoadedReason);
                bool checkSafe;
                string checkSafeReason;
                bool checkSafeRead = TryReadRegionsCheckSafe(new_x, new_y, out checkSafe, out checkSafeReason);
                bool observationComplete = IsObservationComplete(
                    steamIdRead, resourcesLoadedRead, checkSafeRead);

                if (!observationComplete)
                {
                    LogIncompleteRegionObservation(
                        new_x, new_y, steamIdRead, steamIdReason,
                        resourcesLoadedRead, resourcesLoadedReason,
                        checkSafeRead, checkSafeReason,
                        steamId);
                    return;
                }

                if (!WorldSyncDiagnosticCore.TryAcquirePlayerQuota(steamId, "Resource.onRegionUpdated.step3",
                    WorldSyncDiagnosticCore.PerPlayerPointLimit, out int count))
                {
                ResourceObservability.QuotaSuppressed(
                        "[Host]", "RegionEntry", $"({new_x},{new_y})",
                        MultiObserverShadowCoordinator.ResourceSessionEpoch,
                        MultiObserverShadowCoordinator.GetResourceConnectionGeneration(steamId),
                        ResourceSnapshotAdapter.GetRegionGeneration(new Core.Identity.RegionKey(new_x, new_y)),
                        ResourceObservability.NativePath(MultiObserverShadowCoordinator.IsResourceProductionActive),
                        MultiObserverShadowCoordinator.IsResourceProductionActive,
                        "Resource.onRegionUpdated.step3");
                    return;
                }

                bool spiActive = MultiObserverShadowCoordinator.IsResourceProductionActive;
                ResourceObservationPath path = ResourceObservability.NativePath(spiActive);
                ResourceObservability.Info(
                    "[Host]", "RegionEntry", $"({new_x},{new_y})",
                    MultiObserverShadowCoordinator.ResourceSessionEpoch,
                    MultiObserverShadowCoordinator.GetResourceConnectionGeneration(steamId),
                    ResourceSnapshotAdapter.GetRegionGeneration(new Core.Identity.RegionKey(new_x, new_y)),
                    path, spiActive, "observed",
                    $"count={count} player={maskedId} isDedicated={isDedicated} " +
                    $"checkSafe={(checkSafeRead ? checkSafe.ToString().ToLowerInvariant() : "unknown")} " +
                    $"isResourcesLoaded={(resourcesLoadedRead ? isResourcesLoaded.ToString().ToLowerInvariant() : "unknown")} " +
                    $"steamIdRead={steamIdRead} resourcesLoadedRead={resourcesLoadedRead} checkSafeRead={checkSafeRead} " +
                    $"steamIdReason={steamIdReason} resourcesLoadedReason={resourcesLoadedReason} checkSafeReason={checkSafeReason} nativeEntry=true");
            }
            catch (System.Exception ex)
            {
                ResourceObservability.Error("[Shared]", "RegionEntry", "-", 0UL, 0UL, 0U,
                    "Fallback", false, "failed", "exception=" + ex.GetType().Name);
            }
        }

        // ============= 2. 客机 Receive 入口：ReceiveResources =============
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ResourceManager), "ReceiveResources")]
        public static void ReceiveResources_Prefix(ref bool __state)
        {
            __state = false;
            try
            {
                int receiveCount = ResourceSnapshotAdapter.RecordNativeSnapshotReceive();
                // ReceiveResources 签名为 (in ClientInvocationContext context)，无法直接读取 x/y
                // 在 Prefix 中仅记录调用事件，Postfix 中读取 regions 状态变化
                if (!WorldSyncDiagnosticCore.TryAcquireQuota("Resource.ReceiveResources", out int count))
                {
                    ResourceObservability.QuotaSuppressed("[Guest]", "SnapshotReceive", "-",
                        ResourceSnapshotAdapter.CurrentSessionEpoch, ResourceSnapshotAdapter.LocalConnectionGeneration, 0U,
                        "Native", false, "Resource.ReceiveResources");
                    return;
                }

                ResourceObservability.Info("[Guest]", "SnapshotReceive", "-",
                    ResourceSnapshotAdapter.CurrentSessionEpoch, ResourceSnapshotAdapter.LocalConnectionGeneration, 0U,
                    "Native", false, "observed",
                    $"count={count} nativeReceiveCount={receiveCount} generationDecision=not-exposed");
            }
            catch (System.Exception ex)
            {
                ResourceObservability.Error("[Shared]", "SnapshotReceive", "-", 0UL, 0UL, 0U,
                    "Fallback", false, "failed", "exception=" + ex.GetType().Name);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ResourceManager), "ReceiveResources")]
        public static void ReceiveResources_Postfix()
        {
            try
            {
                int networkedCount;
                string networkedReason;
                bool networkedRead = TryCountNetworkedResourceRegions(out networkedCount, out networkedReason);
                if (!networkedRead)
                {
                    ResourceObservability.Error("[Guest]", "SnapshotReceivePostfix", "-",
                        ResourceSnapshotAdapter.CurrentSessionEpoch,
                        ResourceSnapshotAdapter.LocalConnectionGeneration, 0U,
                        "Fallback", false, "failed",
                        "reason=native-region-observation-failed networkedReason=" + networkedReason +
                        " quotaCheck=not-run failClosed=true");
                    return;
                }

                if (!WorldSyncDiagnosticCore.TryAcquireQuota("Resource.ReceiveResources.Postfix", out int count))
                {
                    ResourceObservability.QuotaSuppressed("[Guest]", "SnapshotReceivePostfix", "-",
                        ResourceSnapshotAdapter.CurrentSessionEpoch, ResourceSnapshotAdapter.LocalConnectionGeneration, 0U,
                        "Native", false, "Resource.ReceiveResources.Postfix");
                    return;
                }

                ResourceObservability.Info("[Guest]", "SnapshotReceivePostfix", "-",
                    ResourceSnapshotAdapter.CurrentSessionEpoch, ResourceSnapshotAdapter.LocalConnectionGeneration, 0U,
                    networkedRead ? "Native" : "Fallback", false, networkedRead ? "success" : "failed",
                    $"count={count} totalNetworkedRegions={(networkedRead ? networkedCount.ToString() : "unknown")} " +
                    $"networkedRead={networkedRead} networkedReason={networkedReason} generationDecision=not-exposed");
            }
            catch (System.Exception ex)
            {
                ResourceObservability.Error("[Shared]", "SnapshotReceivePostfix", "-", 0UL, 0UL, 0U,
                    "Fallback", false, "failed", "exception=" + ex.GetType().Name);
            }
        }

        /// <summary>
        /// 反射读取 ResourceManager.regions 字段并统计 isNetworked=true 的区域数。
        /// ResourceManager.regions 可能为 internal/private static，无法直接访问。
        /// </summary>
        private static bool TryCountNetworkedResourceRegions(out int total, out string reason)
        {
            total = 0;
            try
            {
                var field = typeof(ResourceManager).GetField("regions",
                    BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
                if (field == null)
                {
                    reason = "regions-field-missing";
                    return false;
                }
                var regions = field.GetValue(null) as Array;
                if (regions == null || regions.Rank != 2)
                {
                    reason = "regions-array-unavailable";
                    return false;
                }

                int len0 = regions.GetLength(0);
                int len1 = regions.GetLength(1);
                for (int i = 0; i < len0; i++)
                {
                    for (int j = 0; j < len1; j++)
                    {
                        var region = regions.GetValue(i, j);
                        if (region == null)
                        {
                            reason = "native-region-null region=" + i + "," + j;
                            total = 0;
                            return false;
                        }
                        var isNetworkedField = region.GetType().GetField("isNetworked",
                            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                        if (isNetworkedField == null)
                        {
                            reason = "isNetworked-field-missing region=" + i + "," + j;
                            total = 0;
                            return false;
                        }
                        if ((bool)isNetworkedField.GetValue(region))
                        {
                            total++;
                        }
                    }
                }
                reason = "none";
                return true;
            }
            catch (System.Exception ex)
            {
                total = 0;
                reason = "read-failed:" + ex.GetType().Name;
                return false;
            }
        }

        // ============= 安全读取辅助 =============

        private static bool TryReadSteamId(Player player, out ulong steamId, out string reason)
        {
            steamId = 0UL;
            try
            {
                if (player == null) { reason = "player-null"; return false; }
                if (player.channel == null) { reason = "channel-null"; return false; }
                if (player.channel.owner == null) { reason = "owner-null"; return false; }
                if (player.channel.owner.playerID == null) { reason = "player-id-null"; return false; }
                steamId = player.channel.owner.playerID.steamID.m_SteamID;
                if (steamId == 0UL) { reason = "steam-id-zero"; return false; }
                reason = "none";
                return true;
            }
            catch (System.Exception ex)
            {
                reason = "read-failed:" + ex.GetType().Name;
                return false;
            }
        }

        private static bool TryReadRegionResourcesLoaded(
            Player player, byte x, byte y, out bool value, out string reason)
        {
            value = false;
            try
            {
                if (player == null) { reason = "player-null"; return false; }
                var movement = player.movement;
                if (movement == null) { reason = "movement-null"; return false; }

                var field = typeof(PlayerMovement).GetField("_loadedRegions",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null) { reason = "loaded-regions-field-missing"; return false; }

                var loadedRegions = field.GetValue(movement) as LoadedRegion[,];
                if (loadedRegions == null) { reason = "loaded-regions-null"; return false; }

                if (x < 0 || x >= loadedRegions.GetLength(0) || y < 0 || y >= loadedRegions.GetLength(1))
                {
                    reason = $"loaded-regions-out-of-range x={x} y={y}";
                    return false;
                }

                var region = loadedRegions[x, y];
                if (region == null) { reason = "loaded-region-null"; return false; }

                value = region.isResourcesLoaded;
                reason = "none";
                return true;
            }
            catch (System.Exception ex)
            {
                reason = "read-failed:" + ex.GetType().Name;
                return false;
            }
        }

        private static bool TryReadRegionsCheckSafe(byte x, byte y, out bool value, out string reason)
        {
            try
            {
                value = Regions.checkSafe((int)x, (int)y);
                reason = "none";
                return true;
            }
            catch (System.Exception ex)
            {
                value = false;
                reason = "read-failed:" + ex.GetType().Name;
                return false;
            }
        }

        private static bool IsObservationComplete(
            bool steamIdRead,
            bool resourcesLoadedRead,
            bool checkSafeRead)
        {
            return steamIdRead && resourcesLoadedRead && checkSafeRead;
        }

        private static void LogIncompleteRegionObservation(
            byte x,
            byte y,
            bool steamIdRead,
            string steamIdReason,
            bool resourcesLoadedRead,
            string resourcesLoadedReason,
            bool checkSafeRead,
            string checkSafeReason,
            ulong steamId)
        {
            bool spiActive = MultiObserverShadowCoordinator.IsResourceProductionActive;
            ResourceObservability.Error("[Host]", "RegionEntry", $"({x},{y})",
                MultiObserverShadowCoordinator.ResourceSessionEpoch,
                MultiObserverShadowCoordinator.GetResourceConnectionGeneration(steamId),
                ResourceSnapshotAdapter.GetRegionGeneration(new Core.Identity.RegionKey(x, y)),
                "Fallback", spiActive, "failed",
                $"reason=observation-incomplete steamIdRead={steamIdRead} steamIdReason={steamIdReason} " +
                $"resourcesLoadedRead={resourcesLoadedRead} resourcesLoadedReason={resourcesLoadedReason} " +
                $"checkSafeRead={checkSafeRead} checkSafeReason={checkSafeReason} " +
                "quotaCheck=not-run failClosed=true");
        }

        private static void LogRegistrationResult(string hook, bool succeeded, string detail)
        {
            ResourceObservability.Info("[Shared]", "WorldSyncHookRegistration", "-", 0UL, 0UL, 0U,
                succeeded ? "Native" : "Fallback", false,
                succeeded ? "success" : "failed",
                "hook=" + hook + " owner=" + SteamP2PFriendsPlugin.HARMONY_ID + " " + detail);
        }
    }
}
