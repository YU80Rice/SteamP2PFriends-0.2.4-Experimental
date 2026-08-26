using SteamP2PFriends.Core.Patches;
using SteamP2PFriends.Shared;
using System;

namespace SteamP2PFriends
{
    public partial class SteamP2PFriendsPlugin
    {
        private bool RegisterAssetAndAuditPatches()
        {
            _registrationStageFailed = false;
            try
            {
                Core.Patches.AssetIntegritySnapshotPatch.RuntimeProbe();
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]", $"AssetIntegritySnapshotPatch.RuntimeProbe 失败: {ex}");
                return false;
            }

            try
            {
                if (!RegisterAssetIntegritySnapshotPatches())
                    return false;
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]", $"RegisterAssetIntegritySnapshotPatches 整体异常（阻断注册阶段）: {ex}");
                return false;
            }

            return ApplyV2AuditFixPatches() && !_registrationStageFailed;
        }

        private bool InitializeDiagnosticStage()
        {
            bool ok = true;
            try
            {
                Core.Patches.UnityLogBridgePatch.Initialize();
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]", $"UnityLogBridgePatch.Initialize 失败: {ex}");
                ok = false;
            }

            try
            {
                SteamP2PFriends.Client.NativeSnsLogProbe.Enable(RouteDiagnostics.Value, VerboseLog.Value);
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]", $"NativeSnsLogProbe.Enable 失败: {ex}");
                ok = false;
            }
            return ok;
        }
    }
}
