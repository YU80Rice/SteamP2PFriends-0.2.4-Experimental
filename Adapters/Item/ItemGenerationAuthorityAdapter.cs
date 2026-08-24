using SDG.Unturned;
using SteamP2PFriends.Host;
using SteamP2PFriends.Shared;
using System;

namespace SteamP2PFriends.MultiObserver
{
    /// <summary>
    /// M1 adapter for the native ItemManager.onRegionUpdated demand path.
    /// It broadens only the native generateItems branch from the local observer to a
    /// validated listen-host remote observer. The native region loop, loaded flag,
    /// askItems serialization, and authoritative generation transaction remain owners
    /// of their respective state.
    /// </summary>
    internal static class ItemGenerationAuthorityAdapter
    {
        private const int DecisionLogLimit = 16;

        private static bool _registrationReady;
        private static int _allowLogCount;
        private static int _denyLogCount;

        internal static bool IsReady => _registrationReady;

        internal static void SetRegistrationReady(bool ready)
        {
            _registrationReady = ready;
            if (!ready)
            {
                _allowLogCount = 0;
                _denyLogCount = 0;
            }
        }

        internal static void ResetForSession()
        {
            _allowLogCount = 0;
            _denyLogCount = 0;
        }

        /// <summary>
        /// Called in place of the second Player.channel.IsLocalPlayer check in
        /// ItemManager.onRegionUpdated. Returning true executes the original synchronous
        /// generateItems call before vanilla commits isItemsLoaded and serializes askItems.
        /// </summary>
        public static bool ShouldGenerateForObserver(Player player)
        {
            try
            {
                ThreadUtil.assertIsGameThread();

                SteamChannel channel = player?.channel;
                bool isLocal = channel != null && channel.IsLocalPlayer;
                if (isLocal) return true;

                bool isDedicated = Dedicator.IsDedicatedServer;
                bool isP2PRemote = !isDedicated
                    && ListenRegionSyncEligibility.IsDedicatedOrP2PRemoteRecipient(player);
                bool allowed = Evaluate(
                    isLocal,
                    isDedicated,
                    _registrationReady,
                    isP2PRemote);

                if (allowed)
                {
                    if (_allowLogCount < DecisionLogLimit)
                    {
                        _allowLogCount++;
                        SafeInfo($"allow={_allowLogCount}/{DecisionLogLimit} observer="
                            + Mask(player) + " source=native-region-demand pending-allowed=true");
                    }
                }
                else if (isP2PRemote && _denyLogCount < DecisionLogLimit)
                {
                    _denyLogCount++;
                    SafeError($"deny={_denyLogCount}/{DecisionLogLimit} observer="
                        + Mask(player) + " reason=adapter-not-ready; native loaded commit must not be trusted");
                }

                return allowed;
            }
            catch (Exception ex)
            {
                if (_denyLogCount < DecisionLogLimit)
                {
                    _denyLogCount++;
                    SafeError($"deny={_denyLogCount}/{DecisionLogLimit} reason={ex.GetType().Name}: {ex.Message}");
                }
                // Do not let vanilla continue to isItemsLoaded=true/askItems after an
                // indeterminate generation decision. PlayerMovement keeps step 5 pending,
                // while the bounded log quota prevents an unbounded exception log flood.
                throw;
            }
        }

        internal static bool Evaluate(
            bool isLocalPlayer,
            bool isDedicatedServer,
            bool registrationReady,
            bool isP2PRemoteRecipient)
        {
            if (isLocalPlayer) return true;
            if (isDedicatedServer) return false;
            return registrationReady && isP2PRemoteRecipient;
        }

        private static string Mask(Player player)
        {
            try
            {
                ulong steamId = player?.channel?.owner?.playerID?.steamID.m_SteamID ?? 0UL;
                return steamId == 0UL ? "unknown" : DiagnosticMaskUtil.MaskSteamId(steamId);
            }
            catch
            {
                return "mask-error";
            }
        }

        private static void SafeInfo(string message)
        {
            try { RoleLogger.Info("[Host]", "[MultiObserver/M1-Item] " + message); } catch { }
        }

        private static void SafeError(string message)
        {
            try { RoleLogger.Error("[Shared]", "[MultiObserver/M1-Item] " + message); } catch { }
        }
    }
}
