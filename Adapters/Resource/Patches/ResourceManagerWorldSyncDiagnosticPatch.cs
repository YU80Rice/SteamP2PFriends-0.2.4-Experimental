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
        private static readonly System.Type[] VanillaSendResourcesWriteParamTypes =
        {
            typeof(SDG.NetPak.NetPakWriter),
            typeof(byte), typeof(byte)
        };
        private static readonly System.Type[] VanillaReceiveResourcesParamTypes =
        {
            typeof(SDG.Unturned.ClientInvocationContext).MakeByRefType()
        };

        public static bool OnRegionUpdatedPrefixRegistered { get; private set; }
        public static bool SendResourcesWritePrefixRegistered { get; private set; }
        public static bool ReceiveResourcesPrefixRegistered { get; private set; }
        public static bool ReceiveResourcesPostfixRegistered { get; private set; }
        public static bool AllRegistrationsSucceeded =>
            OnRegionUpdatedPrefixRegistered && SendResourcesWritePrefixRegistered
            && ReceiveResourcesPrefixRegistered && ReceiveResourcesPostfixRegistered;

        /// <summary>
        /// 4 个 hook 精确、幂等的 identity-based 手动登记。
        ///
        /// </summary>
        public static bool RegisterManual(Harmony harmony)
        {
            RoleLogger.Info("[Shared]", "[WorldSyncDiag/Resource] === 手动登记 4 个 hook（P0-R1～R8 identity-based 幂等）===");

            var patchType = typeof(ResourceManagerWorldSyncDiagnosticPatch);

            bool r1, r2, r3, r4;
            try
            {
                r1 = WorldSyncDiagnosticCore.RegisterIdentityPatch(
                    harmony, typeof(ResourceManager), "onRegionUpdated", VanillaOnRegionUpdatedParamTypes,
                    AccessTools.Method(patchType, "OnRegionUpdated_Prefix"),
                    HarmonyPatchType.Prefix, "Resource.onRegionUpdated.Pre");
            }
            catch (System.Exception ex) { RoleLogger.Error("[Shared]", $"[WorldSyncDiag/Resource] onRegionUpdated.Pre 登记异常: {ex}"); r1 = false; }

            try
            {
                r2 = WorldSyncDiagnosticCore.RegisterIdentityPatch(
                    harmony, typeof(ResourceManager), "SendResources_Write", VanillaSendResourcesWriteParamTypes,
                    AccessTools.Method(patchType, "SendResources_Write_Prefix"),
                    HarmonyPatchType.Prefix, "Resource.SendResources_Write.Pre");
            }
            catch (System.Exception ex) { RoleLogger.Error("[Shared]", $"[WorldSyncDiag/Resource] SendResources_Write.Pre 登记异常: {ex}"); r2 = false; }

            try
            {
                r3 = WorldSyncDiagnosticCore.RegisterIdentityPatch(
                    harmony, typeof(ResourceManager), "ReceiveResources", VanillaReceiveResourcesParamTypes,
                    AccessTools.Method(patchType, "ReceiveResources_Prefix"),
                    HarmonyPatchType.Prefix, "Resource.ReceiveResources.Pre");
            }
            catch (System.Exception ex) { RoleLogger.Error("[Shared]", $"[WorldSyncDiag/Resource] ReceiveResources.Pre 登记异常: {ex}"); r3 = false; }

            try
            {
                r4 = WorldSyncDiagnosticCore.RegisterIdentityPatch(
                    harmony, typeof(ResourceManager), "ReceiveResources", VanillaReceiveResourcesParamTypes,
                    AccessTools.Method(patchType, "ReceiveResources_Postfix"),
                    HarmonyPatchType.Postfix, "Resource.ReceiveResources.Post");
            }
            catch (System.Exception ex) { RoleLogger.Error("[Shared]", $"[WorldSyncDiag/Resource] ReceiveResources.Post 登记异常: {ex}"); r4 = false; }

            bool all = r1 && r2 && r3 && r4;
            RoleLogger.Info("[Shared]",
                $"[WorldSyncDiag/Resource] RegisterManual 结果: onRegionUpdated.Pre={r1} SendResources_Write.Pre={r2} " +
                $"ReceiveResources.Pre={r3} ReceiveResources.Post={r4} all={all}");
            ResourceObservability.Info("[Shared]", "WorldSyncRegistration", "-", 0UL, 0UL, 0U,
                all ? "Native" : "Fallback", false, all ? "success" : "failed",
                $"onRegionUpdated={r1} sendResourcesWrite={r2} receiveResourcesPre={r3} receiveResourcesPost={r4}");
            return all;
        }

        /// <summary>
        /// 与 RegisterManual 使用同一套 Type[]。
        /// </summary>
        public static bool VerifyRegistration()
        {
            try
            {
                var patchType = typeof(ResourceManagerWorldSyncDiagnosticPatch);

                MethodInfo onRegionUpdatedPre = AccessTools.Method(patchType, "OnRegionUpdated_Prefix");
                MethodInfo sendResourcesWritePre = AccessTools.Method(patchType, "SendResources_Write_Prefix");
                MethodInfo receiveResourcesPre = AccessTools.Method(patchType, "ReceiveResources_Prefix");
                MethodInfo receiveResourcesPost = AccessTools.Method(patchType, "ReceiveResources_Postfix");

                OnRegionUpdatedPrefixRegistered = WorldSyncDiagnosticCore.IsPatchRegistered(
                    typeof(ResourceManager), "onRegionUpdated", onRegionUpdatedPre, HarmonyPatchType.Prefix, VanillaOnRegionUpdatedParamTypes);
                SendResourcesWritePrefixRegistered = WorldSyncDiagnosticCore.IsPatchRegistered(
                    typeof(ResourceManager), "SendResources_Write", sendResourcesWritePre, HarmonyPatchType.Prefix, VanillaSendResourcesWriteParamTypes);
                ReceiveResourcesPrefixRegistered = WorldSyncDiagnosticCore.IsPatchRegistered(
                    typeof(ResourceManager), "ReceiveResources", receiveResourcesPre, HarmonyPatchType.Prefix, VanillaReceiveResourcesParamTypes);
                ReceiveResourcesPostfixRegistered = WorldSyncDiagnosticCore.IsPatchRegistered(
                    typeof(ResourceManager), "ReceiveResources", receiveResourcesPost, HarmonyPatchType.Postfix, VanillaReceiveResourcesParamTypes);

                if (!AllRegistrationsSucceeded)
                {
                    RoleLogger.Error("[Shared]",
                        $"[WorldSyncDiag/Resource] !!! 注册验证失败: " +
                        $"onRegionUpdated.Pre={OnRegionUpdatedPrefixRegistered} " +
                        $"SendResources_Write.Pre={SendResourcesWritePrefixRegistered} " +
                        $"ReceiveResources.Pre={ReceiveResourcesPrefixRegistered} " +
                        $"ReceiveResources.Post={ReceiveResourcesPostfixRegistered} " +
                        $"(owner={SteamP2PFriendsPlugin.HARMONY_ID}, identity-based, 共用 VanillaXxxParamTypes)");
                    return false;
                }

                RoleLogger.Info("[Shared]",
                    $"[WorldSyncDiag/Resource] OK 4 个 hook 均已注册 (owner={SteamP2PFriendsPlugin.HARMONY_ID}, identity-based, 共用 VanillaXxxParamTypes)");
                return true;
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[WorldSyncDiag/Resource] VerifyRegistration 异常: {ex.Message}");
                OnRegionUpdatedPrefixRegistered = SendResourcesWritePrefixRegistered = false;
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

                ulong steamId = 0UL;
                try { steamId = player?.channel?.owner?.playerID?.steamID.m_SteamID ?? 0UL; } catch { }
                string maskedId = WorldSyncDiagnosticCore.MaskSteamId(steamId);

                bool isDedicated = Dedicator.IsDedicatedServer;
                string isResourcesLoadedStr = ReadRegionResourcesLoaded(player, new_x, new_y);
                bool checkSafe = ReadRegionsCheckSafe(new_x, new_y);

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
                ResourceObservability.Info(
                    "[Host]", "RegionEntry", $"({new_x},{new_y})",
                    MultiObserverShadowCoordinator.ResourceSessionEpoch,
                    MultiObserverShadowCoordinator.GetResourceConnectionGeneration(steamId),
                    ResourceSnapshotAdapter.GetRegionGeneration(new Core.Identity.RegionKey(new_x, new_y)),
                    ResourceObservability.NativePath(spiActive), spiActive, "observed",
                    $"count={count} player={maskedId} isDedicated={isDedicated} checkSafe={checkSafe} " +
                    $"isResourcesLoaded={isResourcesLoadedStr} nativeEntry=true");
            }
            catch (System.Exception ex)
            {
                ResourceObservability.Error("[Shared]", "RegionEntry", "-", 0UL, 0UL, 0U,
                    "Fallback", false, "failed", "exception=" + ex.GetType().Name);
            }
        }

        // ============= 2. 发送入口：SendResources_Write（vanilla 私有静态） =============
        // SendResources.Invoke 内部会调用 SendResources_Write 写入区域资源数据
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ResourceManager), "SendResources_Write")]
        public static void SendResources_Write_Prefix(byte x, byte y)
        {
            try
            {
                if (!WorldSyncDiagnosticCore.TryAcquireQuota("Resource.SendResources_Write", out int count))
                {
                    ResourceObservability.QuotaSuppressed(
                        "[Host]", "SnapshotWrite", $"({x},{y})",
                        MultiObserverShadowCoordinator.ResourceSessionEpoch, 0UL,
                        ResourceSnapshotAdapter.GetRegionGeneration(new Core.Identity.RegionKey(x, y)),
                        ResourceObservability.NativePath(MultiObserverShadowCoordinator.IsResourceProductionActive),
                        MultiObserverShadowCoordinator.IsResourceProductionActive,
                        "Resource.SendResources_Write");
                    return;
                }

                bool spiActive = MultiObserverShadowCoordinator.IsResourceProductionActive;
                ResourceObservability.Info("[Host]", "SnapshotWrite", $"({x},{y})",
                    MultiObserverShadowCoordinator.ResourceSessionEpoch, 0UL,
                    ResourceSnapshotAdapter.GetRegionGeneration(new Core.Identity.RegionKey(x, y)),
                    spiActive ? "SPI" : "Fallback", spiActive, "observed",
                    $"count={count} isDedicated={Dedicator.IsDedicatedServer} nativeEntry=true");
            }
            catch (System.Exception ex)
            {
                ResourceObservability.Error("[Shared]", "SnapshotWrite", "-", 0UL, 0UL, 0U,
                    "Fallback", MultiObserverShadowCoordinator.IsResourceProductionActive,
                    "failed", "exception=" + ex.GetType().Name);
            }
        }

        // ============= 3. 客机 Receive 入口：ReceiveResources =============
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
                if (!WorldSyncDiagnosticCore.TryAcquireQuota("Resource.ReceiveResources.Postfix", out int count))
                {
                    ResourceObservability.QuotaSuppressed("[Guest]", "SnapshotReceivePostfix", "-",
                        ResourceSnapshotAdapter.CurrentSessionEpoch, ResourceSnapshotAdapter.LocalConnectionGeneration, 0U,
                        "Native", false, "Resource.ReceiveResources.Postfix");
                    return;
                }

                int networkedCount = CountNetworkedResourceRegions();

                ResourceObservability.Info("[Guest]", "SnapshotReceivePostfix", "-",
                    ResourceSnapshotAdapter.CurrentSessionEpoch, ResourceSnapshotAdapter.LocalConnectionGeneration, 0U,
                    "Native", false, networkedCount >= 0 ? "success" : "failed",
                    $"count={count} totalNetworkedRegions={networkedCount} generationDecision=not-exposed");
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
        private static int CountNetworkedResourceRegions()
        {
            try
            {
                var field = typeof(ResourceManager).GetField("regions",
                    BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
                if (field == null) return -1;
                var regions = field.GetValue(null) as Array;
                if (regions == null) return -1;

                int total = 0;
                int len0 = regions.GetLength(0);
                int len1 = regions.GetLength(1);
                for (int i = 0; i < len0; i++)
                {
                    for (int j = 0; j < len1; j++)
                    {
                        var region = regions.GetValue(i, j);
                        if (region == null) continue;
                        var isNetworkedField = region.GetType().GetField("isNetworked",
                            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                        if (isNetworkedField != null && (bool)isNetworkedField.GetValue(region))
                        {
                            total++;
                        }
                    }
                }
                return total;
            }
            catch { return -1; }
        }

        // ============= 安全读取辅助 =============

        private static string ReadRegionResourcesLoaded(Player player, byte x, byte y)
        {
            try
            {
                if (player == null) return "unknown(player=null)";
                var movement = player.movement;
                if (movement == null) return "unknown(movement=null)";

                var field = typeof(PlayerMovement).GetField("_loadedRegions",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null) return "unknown(_loadedRegions field not found)";

                var loadedRegions = field.GetValue(movement) as LoadedRegion[,];
                if (loadedRegions == null) return "unknown(loadedRegions=null)";

                if (x < 0 || x >= loadedRegions.GetLength(0) || y < 0 || y >= loadedRegions.GetLength(1))
                    return $"unknown(out_of_range x={x} y={y})";

                var region = loadedRegions[x, y];
                if (region == null) return "unknown(region=null)";

                return region.isResourcesLoaded.ToString().ToLowerInvariant();
            }
            catch (System.Exception ex)
            {
                return $"unknown(read-failed: {ex.GetType().Name})";
            }
        }

        private static bool ReadRegionsCheckSafe(byte x, byte y)
        {
            try { return Regions.checkSafe((int)x, (int)y); }
            catch { return false; }
        }
    }
}
