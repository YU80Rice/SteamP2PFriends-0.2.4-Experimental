using SDG.Unturned;
using SteamP2PFriends.Adapters.Collision.Patches;
using SteamP2PFriends.Client;
using SteamP2PFriends.Host;
using SteamP2PFriends.Core.Patches;
using SteamP2PFriends.Shared;
using Steamworks;
using System;

using SteamP2PFriends.Security;

namespace SteamP2PFriends.Core.Lifecycle
{
    /// <summary>
    /// 会话断线与状态复位调度中心 (SessionDisconnectDispatcher)
    /// 统一管理 Provider.onEnemyDisconnected 与 Provider.onClientDisconnected 事件，
    /// 负责精确清理断线玩家占用的各领域资源与诊断状态。
    /// </summary>
    public static class SessionDisconnectDispatcher
    {
        public static void Initialize()
        {
            try
            {
                Provider.onEnemyDisconnected += OnEnemyDisconnected;
                RoleLogger.Info("[Shared]", "[SessionDisconnectDispatcher] 已订阅 Provider.onEnemyDisconnected");
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]", $"订阅 Provider.onEnemyDisconnected 失败: {ex}");
            }

            try
            {
                Provider.onClientDisconnected += OnClientDisconnected;
                RoleLogger.Info("[Shared]", "[SessionDisconnectDispatcher] 已订阅 Provider.onClientDisconnected");
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]", $"订阅 Provider.onClientDisconnected 失败: {ex}");
            }
        }

        public static void Cleanup()
        {
            try
            {
                Provider.onEnemyDisconnected -= OnEnemyDisconnected;
                Provider.onClientDisconnected -= OnClientDisconnected;
            }
            catch { }
        }

        public static void OnEnemyDisconnected(SteamPlayer player)
        {
            try
            {
                ulong steamId = 0;
                try
                {
                    if (!ReferenceEquals(player, null) && !ReferenceEquals(player.playerID, null))
                    {
                        steamId = player.playerID.steamID.m_SteamID;
                    }
                }
                catch { }

                if (steamId != 0UL)
                {
                    try { LevelObjectRemoteCollisionPatch.RemoveRemotePlayer(steamId); }
                    catch (Exception collisionEx)
                    {
                        RoleLogger.Error("[Shared]", $"OnEnemyDisconnectedHandler(LevelObjectCollision) 异常: {collisionEx.Message}");
                    }

                    try { P2PWorldStatusBroadcaster.OnPlayerDisconnected(player); }
                    catch (Exception bdEx)
                    {
                        RoleLogger.Warn("[Host]", "[WorldBroadcast] disconnect forward failed: " + bdEx.GetType().Name);
                    }

                    try { P2PApprovalManager.ForgetDisconnected(new CSteamID(steamId)); }
                    catch (Exception qEx)
                    {
                        RoleLogger.Warn("[Host]", "[P2P-Approval] disconnect cleanup failed: " + qEx.GetType().Name);
                    }

                    try { ItemManagerRegionSyncPatch.OnEnemyDisconnected(steamId); }
                    catch (Exception itemEx)
                    {
                        RoleLogger.Error("[Shared]", $"OnEnemyDisconnectedHandler(M2 ItemObserver) 异常: {itemEx.Message}");
                    }
                }

                int p0s3RetryCount = 0;
                bool p0s3Contained = false;
                try
                {
                    p0s3RetryCount = RemotePlayerClothingVisibleBridgePatch.RetryStatesCount;
                    p0s3Contained = RemotePlayerClothingVisibleBridgePatch.ContainsRetryState(steamId);
                }
                catch { }
                RoleLogger.Info("[Shared]",
                    $"[P0-S3] E-4 OnEnemyDisconnectedHandler 入口观察 " +
                    $"steamId={DiagnosticMaskUtil.MaskSteamId(steamId)} contained={p0s3Contained} retryStatesCount={p0s3RetryCount}");

                try
                {
                    RemotePlayerClothingVisibleBridgePatch.RemoveRetryState(steamId);
                }
                catch (Exception ex)
                {
                    RoleLogger.Error("[Shared]", $"OnEnemyDisconnectedHandler(P0-S3 RemoveRetryState) 异常: {ex.Message}");
                }

                RoleLogger.Info("[Shared]",
                    $"[P1] onEnemyDisconnected 触发 steamId={DiagnosticMaskUtil.MaskSteamId(steamId)}（清除 RegionSync/RenderProbe 计数）");

                BarricadeManagerRegionSyncPatch.OnClientDisconnected();
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]", $"OnEnemyDisconnectedHandler(Barricade) 异常: {ex.Message}");
            }

            try { StructureManagerRegionSyncPatch.OnClientDisconnected(); }
            catch (Exception ex) { RoleLogger.Error("[Shared]", $"OnEnemyDisconnectedHandler(Structure) 异常: {ex.Message}"); }

            try { RemotePlayerRenderProbe.OnClientDisconnected(); }
            catch (Exception ex) { RoleLogger.Error("[Shared]", $"OnEnemyDisconnectedHandler(RenderProbe) 异常: {ex.Message}"); }

            try { ClientRemotePlayerRenderProbe.OnClientDisconnected(); }
            catch (Exception ex) { RoleLogger.Error("[Shared]", $"OnEnemyDisconnectedHandler(ClientRenderProbe) 异常: {ex.Message}"); }

            try { PlayerMovementTellStateDiagnosticPatch.OnClientDisconnected(); }
            catch (Exception ex) { RoleLogger.Error("[Shared]", $"OnEnemyDisconnectedHandler(D-Vis-9) 异常: {ex.Message}"); }

            try { PlayerMovementTellStateCallerDiagnosticPatch.OnClientDisconnected(); }
            catch (Exception ex) { RoleLogger.Error("[Shared]", $"OnEnemyDisconnectedHandler(D-Vis-15) 异常: {ex.Message}"); }

            try { NetMessageDeliveryPathDiagnosticPatch.OnClientDisconnected(); }
            catch (Exception ex) { RoleLogger.Error("[Shared]", $"OnEnemyDisconnectedHandler(D-Vis-16) 异常: {ex.Message}"); }

            try { SteamChannelTransportDiagnosticPatch.OnClientDisconnected(); }
            catch (Exception ex) { RoleLogger.Error("[Shared]", $"OnEnemyDisconnectedHandler(D-Vis-8) 异常: {ex.Message}"); }

            try { PlayerManagerBroadcastPatch.OnClientDisconnected(); }
            catch (Exception ex) { RoleLogger.Error("[Shared]", $"OnEnemyDisconnectedHandler(P0-S2) 异常: {ex.Message}"); }

            try { PlayerManagerBroadcastDiagnosticPatch.OnClientDisconnected(); }
            catch (Exception ex) { RoleLogger.Error("[Shared]", $"OnEnemyDisconnectedHandler(P1-S5) 异常: {ex.Message}"); }

            try { WorldSyncDiagnosticCore.OnClientDisconnected(); }
            catch (Exception ex) { RoleLogger.Error("[Shared]", $"OnEnemyDisconnectedHandler(WorldSyncDiag) 异常: {ex.Message}"); }
        }

        public static void OnClientDisconnected()
        {
            try { BarricadeManagerRegionSyncPatch.OnClientDisconnected(); }
            catch (Exception ex) { RoleLogger.Error("[Shared]", $"BarricadeManagerRegionSyncPatch.OnClientDisconnected 异常: {ex.Message}"); }

            try { StructureManagerRegionSyncPatch.OnClientDisconnected(); }
            catch (Exception ex) { RoleLogger.Error("[Shared]", $"StructureManagerRegionSyncPatch.OnClientDisconnected 异常: {ex.Message}"); }

            try { RemotePlayerRenderProbe.OnClientDisconnected(); }
            catch (Exception ex) { RoleLogger.Error("[Shared]", $"RemotePlayerRenderProbe.OnClientDisconnected 异常: {ex.Message}"); }

            try { SteamChannelTransportDiagnosticPatch.OnClientDisconnected(); }
            catch (Exception ex) { RoleLogger.Error("[Shared]", $"SteamChannelTransportDiagnosticPatch.OnClientDisconnected 异常: {ex.Message}"); }

            try { PlayerManagerBroadcastPatch.OnClientDisconnected(); }
            catch (Exception ex) { RoleLogger.Error("[Shared]", $"PlayerManagerBroadcastPatch.OnClientDisconnected 异常: {ex.Message}"); }

            try { PlayerManagerBroadcastDiagnosticPatch.OnClientDisconnected(); }
            catch (Exception ex) { RoleLogger.Error("[Shared]", $"PlayerManagerBroadcastDiagnosticPatch.OnClientDisconnected 异常: {ex.Message}"); }

            try { WorldSyncDiagnosticCore.OnClientDisconnected(); }
            catch (Exception ex) { RoleLogger.Error("[Shared]", $"WorldSyncDiagnosticCore.OnClientDisconnected 异常: {ex.Message}"); }
        }
    }
}
