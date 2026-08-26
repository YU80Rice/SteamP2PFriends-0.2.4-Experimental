using SteamP2PFriends.Core.Patches;
using SteamP2PFriends.Shared;
using System;

using SteamP2PFriends.Security.Patches;

namespace SteamP2PFriends
{
    public partial class SteamP2PFriendsPlugin
    {
        private bool RegisterRouteBPatches()
        {
            try
            {
                P2PQuarantineActionGatePatch.RegisterManual(_harmony);
                Patch_PlayerDashboardPlayersUI.RegisterManual(_harmony);
                P2PListenHostCommandPermissionPatch.RegisterManual(_harmony);
                RoleLogger.Info("[Shared]",
                    "[P2P-Approval] Route B patches registered; lifecycle hooks waiting for game thread");
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]", "[Stage7-6] manual registration failed: " + ex);
                return false;
            }
            return true;
        }
    }
}
