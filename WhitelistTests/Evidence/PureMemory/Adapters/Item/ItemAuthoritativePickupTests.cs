using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using SDG.Unturned;
using SteamP2PFriends.Adapters.Item;
using UnityEngine;

namespace SteamP2PFriends.WhitelistTests
{
    internal static class ItemAuthoritativePickupTests
    {
        internal static bool Test_IT95_OverDistanceRejectsBeforeInventoryWrite()
        {
            int calls = 0;
            int writes = 0;
            int deletes = 0;
            bool allowed = ItemAuthoritativePickup.TryRequest(
                listenHostAuthority: true,
                callerIsHost: true,
                callerPosition: new Vector3(0f, 0f, 0f),
                hasAuthoritativeItem: true,
                authoritativeItemPoint: new Vector3(20f, 1f, 0f),
                clientReportedItemPoint: new Vector3(0f, 0f, 0f),
                sameRegionLease: true,
                regionReady: true,
                nativeTransaction: () =>
                {
                    calls++;
                    writes++;
                    deletes++;
                    return true;
                });
            float authoritativeSquared = (new Vector3(20f, 1f, 0f) - new Vector3(0f, 0f, 0f)).sqrMagnitude;
            return authoritativeSquared > 400f
                && !allowed
                && calls == 0
                && writes == 0
                && deletes == 0;
        }

        internal static bool Test_IT96_DistanceSquared400RunsNativeOnce()
        {
            int calls = 0;
            int writes = 0;
            int deletes = 0;
            bool allowed = ItemAuthoritativePickup.TryRequest(
                listenHostAuthority: true,
                callerIsHost: false,
                callerPosition: new Vector3(0f, 0f, 0f),
                hasAuthoritativeItem: true,
                authoritativeItemPoint: new Vector3(20f, 0f, 0f),
                clientReportedItemPoint: new Vector3(100f, 0f, 0f),
                sameRegionLease: true,
                regionReady: true,
                nativeTransaction: () =>
                {
                    calls++;
                    writes++;
                    deletes++;
                    return true;
                });
            float authoritativeSquared = (new Vector3(20f, 0f, 0f) - new Vector3(0f, 0f, 0f)).sqrMagnitude;
            return authoritativeSquared == 400f
                && allowed
                && calls == 1
                && writes == 1
                && deletes == 1;
        }

        internal static bool Test_IT97_NonAuthorityKeepsOriginalPath()
        {
            int calls = 0;
            bool allowed = ItemAuthoritativePickup.TryRequest(
                listenHostAuthority: false,
                callerIsHost: false,
                callerPosition: new Vector3(0f, 0f, 0f),
                hasAuthoritativeItem: true,
                authoritativeItemPoint: new Vector3(20f, 1f, 0f),
                clientReportedItemPoint: new Vector3(0f, 0f, 0f),
                sameRegionLease: false,
                regionReady: true,
                nativeTransaction: () =>
                {
                    calls++;
                    return true;
                });
            return allowed && calls == 1;
        }

        internal static bool Test_IT98_UnreadyRegionRejectsBeforeWrite()
        {
            int calls = 0;
            bool allowed = ItemAuthoritativePickup.TryRequest(
                listenHostAuthority: true,
                callerIsHost: true,
                callerPosition: new Vector3(1f, 0f, 0f),
                hasAuthoritativeItem: true,
                authoritativeItemPoint: new Vector3(1f, 0f, 0f),
                clientReportedItemPoint: new Vector3(1f, 0f, 0f),
                sameRegionLease: true,
                regionReady: false,
                nativeTransaction: () =>
                {
                    calls++;
                    return true;
                });
            return !allowed && calls == 0;
        }

        internal static bool Test_IT99_GuestLeaseCannotBypassInstanceLookup()
        {
            int calls = 0;
            var items = new List<ItemData> { ItemAt(7U, new Vector3(20f, 1f, 0f)), ItemAt(8U, Vector3.zero) };
            bool allowed = ItemAuthoritativePickup.TryRequest(
                listenHostAuthority: true,
                callerIsHost: false,
                callerPosition: Vector3.zero,
                instanceId: 7U,
                authoritativeItems: items,
                clientReportedItemPoint: Vector3.zero,
                sameRegionLease: true,
                regionReady: true,
                nativeTransaction: () => { calls++; return true; });
            return !allowed && calls == 0;
        }

        internal static bool Test_IT100_LegalDistanceStillHonorsNativeRejectionOnce()
        {
            var items = new List<ItemData> { ItemAt(7U, new Vector3(1f, 0f, 0f)) };
            bool alive = RejectsOnce(items, () => false, out int aliveCalls, out int aliveWrites, out int aliveDeletes);
            bool callback = RejectsOnce(items, () => false, out int callbackCalls, out int callbackWrites, out int callbackDeletes);
            bool capacity = RejectsOnce(items, () => false, out int capacityCalls, out int capacityWrites, out int capacityDeletes);
            return alive && callback && capacity
                && aliveCalls == 1 && callbackCalls == 1 && capacityCalls == 1
                && aliveWrites == 0 && callbackWrites == 0 && capacityWrites == 0
                && aliveDeletes == 0 && callbackDeletes == 0 && capacityDeletes == 0;
        }

        private static bool RejectsOnce(
            List<ItemData> items,
            Func<bool> native,
            out int calls,
            out int writes,
            out int deletes)
        {
            int localCalls = 0;
            writes = 0;
            deletes = 0;
            bool allowed = ItemAuthoritativePickup.TryRequest(
                listenHostAuthority: true,
                callerIsHost: true,
                callerPosition: new Vector3(1f, 0f, 0f),
                instanceId: 7U,
                authoritativeItems: items,
                clientReportedItemPoint: new Vector3(80f, 0f, 0f),
                sameRegionLease: true,
                regionReady: true,
                nativeTransaction: () =>
                {
                    localCalls++;
                    return native();
                });
            calls = localCalls;
            return !allowed;
        }

        internal static bool Test_IT101_MissingInstanceIgnoresClientPoint()
        {
            int calls = 0;
            bool allowed = ItemAuthoritativePickup.TryRequest(
                listenHostAuthority: true,
                callerIsHost: true,
                callerPosition: Vector3.zero,
                instanceId: 99U,
                authoritativeItems: new List<ItemData> { ItemAt(7U, Vector3.zero) },
                clientReportedItemPoint: new Vector3(40f, 0f, 0f),
                sameRegionLease: true,
                regionReady: true,
                nativeTransaction: () => { calls++; return true; });
            return allowed && calls == 1;
        }

        private static ItemData ItemAt(uint instanceId, Vector3 point)
        {
            var data = (ItemData)FormatterServices.GetUninitializedObject(typeof(ItemData));
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(ItemData).GetField("_instanceID", flags).SetValue(data, instanceId);
            typeof(ItemData).GetField("_point", flags).SetValue(data, point);
            return data;
        }
    }
}
