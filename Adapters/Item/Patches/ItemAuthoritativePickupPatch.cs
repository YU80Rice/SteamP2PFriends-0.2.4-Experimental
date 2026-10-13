using HarmonyLib;
using SDG.Unturned;
using SteamP2PFriends.Host;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SteamP2PFriends.Adapters.Item.Patches
{
    public static class ItemAuthoritativePickupPatch
    {
        internal static bool AllowDistance(in ServerInvocationContext context, byte x, byte y, uint instanceID)
        {
            bool listenHostAuthority = HostManager.IsP2PHostMode && Provider.isServer && !Dedicator.IsDedicatedServer;
            Player player = context.GetPlayer();
            bool callerIsHost = false;
            Vector3 callerPosition = default(Vector3);
            if (player == null)
            {
                listenHostAuthority = false;
            }
            else
            {
                callerIsHost = player.channel != null && player.channel.IsLocalPlayer;
                callerPosition = player.transform.position;
            }

            System.Func<byte, byte, bool> allows = ItemSettlementGate.AllowsConsumption;
            bool regionReady = allows == null || allows(x, y);
            ItemRegion[,] regions = ItemManager.regions;
            IList<ItemData> items = null;
            if (regions != null && Regions.checkSafe(x, y))
            {
                ItemRegion region = regions[x, y];
                if (region != null) items = region.items;
            }

            return ItemAuthoritativePickup.TryRequest(
                listenHostAuthority,
                callerIsHost,
                callerPosition,
                instanceID,
                items,
                default(Vector3),
                false,
                regionReady,
                () => true);
        }

        public static void RegisterManual(Harmony harmony)
        {
            MethodInfo pickup = AccessTools.Method(typeof(ItemManager), "ReceiveTakeItemRequest");
            harmony.Patch(pickup, new HarmonyMethod(typeof(ItemAuthoritativePickupPatch), nameof(AllowDistance)));
        }
    }
}
