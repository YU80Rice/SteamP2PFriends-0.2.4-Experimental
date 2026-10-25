using System;
using System.Collections.Generic;
using SDG.Unturned;

namespace SteamP2PFriends.Adapters.Item
{
    /// <summary>
    /// E02 §4-1：生产 native reader。wrapper（ItemManagerRegionSyncPatch）把 Player
    /// 作为 object key 传入；本 adapter 读取真实 Provider/Player/Connections 原始值，
    /// 只装 ItemModeRaw/ItemBindingRaw，不做身份结论。
    /// </summary>
    internal sealed class NativeItemStepFiveReader : IItemStepFiveNative
    {
        internal static readonly NativeItemStepFiveReader Instance =
            new NativeItemStepFiveReader();

        public ItemModeRaw ReadMode()
        {
            bool dedicated;
            try { dedicated = Dedicator.IsDedicatedServer; } catch { dedicated = true; }
            bool isServer;
            try { isServer = Provider.isServer; } catch { isServer = false; }
            bool p2p;
            try { p2p = Host.HostManager.IsP2PHostMode; } catch { p2p = false; }
            // R2A：writer 来源读真实 ItemAuthorityWriterOwnsSession（协调器单一状态），
            // 不再硬编码 true——SP 的 server=true/p2p=false/writer=false 不误判 owned。
            // 读取失败按非 writer（与 Provider.clients 读取失败的保守语义一致）。
            bool writerOwns;
            try
            {
                writerOwns = MultiObserver.MultiObserverShadowCoordinator
                    .ItemAuthorityWriterOwnsSession;
            }
            catch { writerOwns = false; }
            return new ItemModeRaw(dedicated, isServer, p2p, writerOwns);
        }

        public ItemBindingRaw ReadBinding(object playerKey)
        {
            Player player = playerKey as Player;
            if (player == null) throw new ArgumentException(
                "playerKey is not a Player", nameof(playerKey));
            SteamChannel channel = player.channel;
            SteamPlayer owner = channel?.owner;
            if (owner == null) throw new InvalidOperationException(
                "step5 binding owner unavailable");

            var clients = new List<ItemClientRaw>();
            try
            {
                if (Provider.clients != null)
                    for (int i = 0; i < Provider.clients.Count; i++)
                    {
                        SteamPlayer client = Provider.clients[i];
                        if (client == null) continue;
                        clients.Add(new ItemClientRaw
                        {
                            Owner = client,
                            Player = client.player,
                            Connection = client.transportConnection,
                            SteamId = client.playerID?.steamID.m_SteamID ?? 0UL
                        });
                    }
            }
            catch (Exception)
            {
                throw new InvalidOperationException("step5 clients read failed");
            }

            ulong providerClient = 0UL;
            try { providerClient = Provider.client.m_SteamID; } catch { providerClient = 0UL; }

            bool committed = MultiObserver.MultiObserverShadowCoordinator
                .TryReadCommittedItemConnection(
                    owner.playerID.steamID.m_SteamID,
                    out object committedOwner, out object committedTransport,
                    out ulong committedToken);

            LoadedRegion[,] loadedRegions = player.movement.loadedRegions;
            if (loadedRegions == null) throw new InvalidOperationException(
                "step5 loadedRegions unavailable");

            return new ItemBindingRaw
            {
                CallbackPlayer = player,
                CallbackOwner = owner,
                CallbackOwnerPlayer = owner.player,
                CallbackTransport = owner.transportConnection,
                CallbackIsLocalPlayer = channel.IsLocalPlayer,
                CallbackOwnerSteamId = owner.playerID.steamID.m_SteamID,
                ProviderClient = providerClient,
                ProviderClientSteamId = providerClient,
                Clients = clients,
                LoadedWidth = loadedRegions.GetLength(0),
                LoadedHeight = loadedRegions.GetLength(1),
                HasLoadedCell = (x, y) =>
                    x < loadedRegions.GetLength(0) && y < loadedRegions.GetLength(1)
                    && loadedRegions[x, y] != null,
                CanProjectCell = (x, y) =>
                    x < loadedRegions.GetLength(0) && y < loadedRegions.GetLength(1)
                    && loadedRegions[x, y] != null,
                ProjectCell = (x, y) =>
                {
                    if (x < loadedRegions.GetLength(0) && y < loadedRegions.GetLength(1)
                        && loadedRegions[x, y] != null)
                        loadedRegions[x, y].isItemsLoaded = true;
                },
                CommittedOwner = committed ? committedOwner : null,
                CommittedTransport = committed ? committedTransport : null,
                CommittedToken = committedToken
            };
        }
    }
}
