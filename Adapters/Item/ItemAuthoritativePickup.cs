using System;
using System.Collections.Generic;
using SDG.Unturned;
using UnityEngine;

namespace SteamP2PFriends.Adapters.Item
{
    public static class ItemAuthoritativePickup
    {
        public static bool TryRequest(
            bool listenHostAuthority,
            bool callerIsHost,
            Vector3 callerPosition,
            uint instanceId,
            IList<ItemData> authoritativeItems,
            Vector3 clientReportedItemPoint,
            bool sameRegionLease,
            bool regionReady,
            Func<bool> nativeTransaction)
        {
            bool hasAuthoritativeItem = false;
            Vector3 authoritativeItemPoint = default(Vector3);
            if (authoritativeItems != null)
            {
                for (int i = 0; i < authoritativeItems.Count; i++)
                {
                    ItemData data = authoritativeItems[i];
                    if (data != null && data.instanceID == instanceId)
                    {
                        hasAuthoritativeItem = true;
                        authoritativeItemPoint = data.point;
                        break;
                    }
                }
            }

            return TryRequest(
                listenHostAuthority,
                callerIsHost,
                callerPosition,
                hasAuthoritativeItem,
                authoritativeItemPoint,
                clientReportedItemPoint,
                sameRegionLease,
                regionReady,
                nativeTransaction);
        }

        public static bool TryRequest(
            bool listenHostAuthority,
            bool callerIsHost,
            Vector3 callerPosition,
            bool hasAuthoritativeItem,
            Vector3 authoritativeItemPoint,
            Vector3 clientReportedItemPoint,
            bool sameRegionLease,
            bool regionReady,
            Func<bool> nativeTransaction)
        {
            if (!listenHostAuthority)
            {
                return nativeTransaction();
            }

            if (!regionReady)
            {
                return false;
            }

            if (hasAuthoritativeItem
                && (authoritativeItemPoint - callerPosition).sqrMagnitude > 400f)
            {
                return false;
            }

            return nativeTransaction();
        }
    }
}
