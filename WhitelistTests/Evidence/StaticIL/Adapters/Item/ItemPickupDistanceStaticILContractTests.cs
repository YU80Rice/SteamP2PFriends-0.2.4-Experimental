using System;
using System.Linq;
using System.Reflection;
using SteamP2PFriends.Adapters.Item.Patches;

namespace SteamP2PFriends.WhitelistTests
{
    internal static class ItemPickupDistanceStaticILContractTests
    {
        internal static bool Test_PickupDistanceConsumesReceiveTakeItemRequest()
        {
            Type patch = typeof(SteamP2PFriendsPlugin).Assembly.GetType(
                "SteamP2PFriends.Adapters.Item.Patches.ItemAuthoritativePickupPatch");
            MethodInfo allow = patch?.GetMethod("AllowDistance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo register = patch?.GetMethod("RegisterManual", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo settlement = typeof(ItemSettlementConsumptionPatch).GetMethod(
                "RegisterManual", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (allow == null || register == null || settlement == null) return false;
            bool noClientVector = true;
            foreach (ParameterInfo parameter in allow.GetParameters())
            {
                if (parameter.ParameterType == typeof(UnityEngine.Vector3)) noClientVector = false;
            }
            MethodInfo lookup = typeof(SteamP2PFriends.Adapters.Item.ItemAuthoritativePickup).GetMethods()
                .First(method => method.Name == "TryRequest" && method.GetParameters().Any(parameter => parameter.Name == "instanceId"));
            int request = IlContractProbe.CountMethodCalls(allow, "SteamP2PFriends.Adapters.Item.ItemAuthoritativePickup", "TryRequest");
            int player = IlContractProbe.CountMethodCalls(allow, "SDG.Unturned.ServerInvocationContext", "GetPlayer");
            int point = IlContractProbe.CountMethodCalls(lookup, "SDG.Unturned.ItemData", "get_point");
            int instance = IlContractProbe.CountMethodCalls(lookup, "SDG.Unturned.ItemData", "get_instanceID");
            int host = IlContractProbe.CountMethodCalls(allow, "SteamP2PFriends.Host.HostManager", "get_IsP2PHostMode");
            int server = IlContractProbe.CountMethodCalls(allow, "SDG.Unturned.Provider", "get_isServer");
            int dedicated = IlContractProbe.CountMethodCalls(allow, "SDG.Unturned.Dedicator", "get_IsDedicatedServer");
            int ready = IlContractProbe.CountMethodCalls(allow, "SteamP2PFriends.Adapters.Item.ItemSettlementGate", "get_AllowsConsumption");
            int regions = IlContractProbe.CountMethodCalls(allow, "SDG.Unturned.ItemManager", "get_regions");
            int writes = IlContractProbe.CountMethodCalls(allow, "SDG.Unturned.PlayerInventory", "tryAddItem");
            int deletes = IlContractProbe.CountMethodCalls(allow, "System.Collections.Generic.List`1", "RemoveAt");
            int registered = IlContractProbe.CountMethodCalls(
                settlement, "SteamP2PFriends.Adapters.Item.Patches.ItemAuthoritativePickupPatch", "RegisterManual");
            bool literal = IlContractProbe.ContainsStringLiteral(register, "ReceiveTakeItemRequest");
            return noClientVector && request == 1 && player == 1 && point == 1 && instance == 1
                && host == 1 && server == 1 && dedicated == 1 && ready == 1 && regions == 1
                && writes == 0 && deletes == 0 && registered == 1 && literal;
        }
    }
}
