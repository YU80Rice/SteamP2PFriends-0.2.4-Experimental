using HarmonyLib;
using SDG.NetTransport;
using System;
using SDG.Unturned;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SteamP2PFriends.Adapters.Item.Patches
{
    public static class ItemSettlementConsumptionPatch
    {
        internal static bool AllowPickup(byte x, byte y) =>
            ItemSettlementGate.TryAuthorizePickup(x, y);

        internal static bool AllowIncrement(byte arg1, byte arg2) =>
            ItemSettlementGate.TryAuthorizeIncrement(arg1, arg2);

        internal static bool AllowAskItems(byte x, byte y) =>
            ItemSettlementGate.TryAuthorizeIncrement(x, y);

        public static void RegisterManual(Harmony harmony)
        {
            MethodInfo pickup = AccessTools.Method(typeof(ItemManager), "ReceiveTakeItemRequest");
            MethodInfo ask = AccessTools.Method(typeof(ItemManager), "askItems",
                new[] { typeof(ITransportConnection), typeof(byte), typeof(byte), typeof(float) });
            Type sendItem = typeof(ClientStaticMethod<byte, byte, ushort, byte, byte, byte[], Vector3, uint, bool>);
            MethodInfo increment = sendItem.GetMethod("Invoke", new[]
            {
                typeof(ENetReliability), typeof(List<ITransportConnection>),
                typeof(byte), typeof(byte), typeof(ushort), typeof(byte), typeof(byte),
                typeof(byte[]), typeof(Vector3), typeof(uint), typeof(bool)
            });
            harmony.Patch(pickup, new HarmonyMethod(typeof(ItemSettlementConsumptionPatch), nameof(AllowPickup)));
            ItemAuthoritativePickupPatch.RegisterManual(harmony);
            harmony.Patch(ask, new HarmonyMethod(typeof(ItemSettlementConsumptionPatch), nameof(AllowAskItems)));
            if (increment != null)
                harmony.Patch(increment, new HarmonyMethod(typeof(ItemSettlementConsumptionPatch), nameof(AllowIncrement)));
        }
    }
}
