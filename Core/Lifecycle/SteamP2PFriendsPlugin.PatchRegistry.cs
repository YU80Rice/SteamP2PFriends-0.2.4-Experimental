using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using SDG.NetPak;
using SDG.NetTransport;
using SDG.Unturned;
using SteamP2PFriends.Client;
using SteamP2PFriends.Core.Registration;
using SteamP2PFriends.Host;
using SteamP2PFriends.MultiObserver;
using SteamP2PFriends.Core.Patches;
using SteamP2PFriends.Shared;
using SteamP2PFriends.UI;
using Steamworks;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

using SteamP2PFriends.Security;

namespace SteamP2PFriends
{
    public partial class SteamP2PFriendsPlugin
    {
        private void ApplyAllPatchesAndDiagnostics()
        {
            PatchRegistrationOrchestrator orchestrator = new PatchRegistrationOrchestrator();
            string failure;
            if (!orchestrator.Execute(CreatePatchRegistrationPlan(), out failure))
            {
                DiagnosticBuildValid = false;
                RoleLogger.Error("[Shared]", "[Registration] Registration Orchestrator 失败: " + failure);
            }
        }

        private bool EnsureRouteBLifecycleHooksOnGameThread()
        {
            if (P2PApprovalManager.LifecycleHooksInstalled) return true;
            if (_routeBLifecycleRegistrationAttempted) return false;

            _routeBLifecycleRegistrationAttempted = true;
            try
            {
                ThreadUtil.assertIsGameThread();
                P2PApprovalManager.InstallProviderLifecycleHooks();
                Stage76QuarantineRegistrationValid = VerifyRouteBRegistration(requireLifecycleHooks: true);
                if (!EntryReadiness.TryMarkRouteBLifecycleReady(
                        P2PApprovalManager.LifecycleHooksInstalled, Stage76QuarantineRegistrationValid))
                    throw new System.InvalidOperationException("Route B lifecycle registration gate failed");

                RoleLogger.Info("[Shared]",
                    "[P2P-Approval] Route B lifecycle hooks installed on game thread; P2P entry enabled");
                MenuPlaySingleplayerUIPatch.EnsureMultiplayerButton();
                return true;
            }
            catch (System.Exception ex)
            {
                Stage76QuarantineRegistrationValid = false;
                DiagnosticBuildValid = false;
                EntryReadiness.Reset();
                try { P2PApprovalManager.UninstallProviderLifecycleHooks(); }
                catch (System.Exception cleanupEx)
                {
                    RoleLogger.Warn("[Shared]",
                        "[P2P-Approval] lifecycle cleanup failed: " + cleanupEx.GetType().Name);
                }
                RoleLogger.Error("[Shared]",
                    "[P2P-Approval] Route B lifecycle registration failed on game thread; P2P entry disabled: " +
                    ex.GetType().Name);
                return false;
            }
        }

        private void ShutdownAllComponents()
        {
            P2PLobbyManager.Shutdown();
            ClientLobbyListener.Shutdown();
            P2PJoinManager.Shutdown();
            SteamP2PFriends.Client.ExplicitDnsDirectIpService.Shutdown();
            try { P2PWorldStatusBroadcaster.Shutdown(); }
            catch (System.Exception wbEx) { RoleLogger.Warn("[Shared]", "[WorldBroadcast] Shutdown 异常: " + wbEx.GetType().Name); }
            Core.Patches.UnityLogBridgePatch.Shutdown();
            SteamP2PFriends.Shared.ConnectionLifecycleTracker.Shutdown();
            SteamP2PFriends.Client.NativeSnsLogProbe.Disable();
            SteamP2PFriends.Host.RemotePlayerRenderProbe.Shutdown();
            SteamP2PFriends.Client.ClientRemotePlayerRenderProbe.Shutdown();
            MultiObserverShadowCoordinator.Shutdown();
            Core.Patches.UnityTagErrorSourceDiagnosticPatch.Shutdown();
            Core.Patches.PlayerLifecycleReadyDiagnosticPatch.Shutdown();
            Core.Patches.BarricadeManagerRegionSyncPatch.ResetAll();
            Core.Patches.StructureManagerRegionSyncPatch.ResetAll();
            Core.Patches.ItemManagerRegionSyncPatch.ResetAll();
            Core.Patches.ResourceManagerRegionSyncPatch.ResetAll();
            Core.Patches.ObjectManagerRegionSyncPatch.ResetAll();
            Core.Patches.LevelObjectRemoteCollisionPatch.ResetAll();
            SteamP2PFriends.Host.RemotePlayerRenderProbe.ResetAll();
            SteamP2PFriends.Client.ClientRemotePlayerRenderProbe.ResetAll();
            Core.Patches.WorldSyncDiagnosticCore.ResetAll();
            try { P2PNativeMenuUI.Destroy(); } catch (System.Exception ex) { RoleLogger.Warn("[Shared]", $"[P2P] P2PNativeMenuUI.Destroy 异常: {ex.Message}"); }
            try { P2PQuarantineClientView.Destroy(); } catch (System.Exception ex) { RoleLogger.Warn("[Shared]", $"[P2P] P2PQuarantineClientView.Destroy 异常: {ex.Message}"); }
            try { P2PApprovalManager.UninstallProviderLifecycleHooks(); } catch (System.Exception ex) { RoleLogger.Warn("[Shared]", $"[P2P] P2PApprovalManager.Uninstall 异常: {ex.Message}"); }
            EntryReadiness.Reset();
            try { MenuPlaySingleplayerUIPatch.DestroyMultiplayerButton(); } catch (System.Exception ex) { RoleLogger.Warn("[Shared]", $"[P2P] MultiplayerButton.Destroy 异常: {ex.Message}"); }
            _harmony?.UnpatchSelf();
            RoleLogger.Info("[Shared]", "[P2P] SteamP2PFriends 已卸载");
        }

        private void TickActiveComponents()
        {
            try
            {
                SteamP2PFriends.Client.NativeSnsLogProbe.RetryEnableIfSteamworksReady();
                SteamGameServerCallbacksWatcher.Tick();
                HasCheatsGuardWatcher.Tick();
                HostManager.TickListen();
                P2PLobbyManager.Tick();
                P2PJoinManager.Tick();
                SteamP2PFriends.Client.ExplicitDnsDirectIpService.Tick();

                if (HostManager.IsP2PHostMode && Provider.isServer && Provider.isWhitelisted)
                {
                    SteamPersonaDisplay.DrainObservedCharacterNamesOnMainThread();
                    P2PApprovalManager.Tick();
                }

                if (SteamP2PFriends.Shared.P2PClientUiEnvironment.CanTouchClientUi())
                {
                    P2PNativeMenuUI.Tick();
                    if (!HostManager.IsP2PHostMode)
                    {
                        P2PQuarantineClientView.Tick();
                    }
                }
                NativeLoadingGateDumper.Tick();
                SteamP2PFriends.Shared.ConnectionLifecycleTracker.Tick();
                SteamP2PFriends.Client.NativeSnsLogProbe.Tick();
                SteamP2PFriends.Host.RemotePlayerRenderProbe.Tick();
                SteamP2PFriends.Client.ClientRemotePlayerRenderProbe.Tick();
                Core.Patches.RemotePlayerClothingVisibleBridgePatch.Tick();
            }
            catch (System.Exception ex)
            {
                RoleLogger.Warn("[Shared]", $"[P2P-Update] Tick 异常: {ex.Message}");
            }
        }

    }
}
