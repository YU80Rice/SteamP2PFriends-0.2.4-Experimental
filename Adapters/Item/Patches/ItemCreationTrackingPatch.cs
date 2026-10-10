using HarmonyLib;
using SDG.Unturned;
using SteamP2PFriends.Shared;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SteamP2PFriends.Adapters.Item.Patches
{
    public static class ItemCreationTrackingPatch
    {
        private static readonly Type[] DropItemParamTypes =
        {
            typeof(SDG.Unturned.Item),
            typeof(Vector3),
            typeof(bool),
            typeof(bool),
            typeof(bool)
        };

        private static readonly Type[] GenerateItemsParamTypes =
        {
            typeof(byte),
            typeof(byte)
        };

        public static bool RegisterManual(Harmony harmony)
        {
            try
            {
                if (harmony == null) return false;
                MethodInfo dropOriginal = AccessTools.Method(typeof(ItemManager), "dropItem", DropItemParamTypes);
                MethodInfo dropPrefix = AccessTools.Method(typeof(ItemCreationTrackingPatch), nameof(DropItem_Prefix));
                MethodInfo dropPostfix = AccessTools.Method(typeof(ItemCreationTrackingPatch), nameof(DropItem_Postfix));
                if (dropOriginal != null && dropPostfix != null)
                {
                    harmony.Patch(dropOriginal, prefix: dropPrefix != null ? new HarmonyMethod(dropPrefix) : null, postfix: new HarmonyMethod(dropPostfix));
                }

                MethodInfo genOriginal = AccessTools.Method(typeof(ItemManager), "generateItems", GenerateItemsParamTypes);
                MethodInfo genPrefix = AccessTools.Method(typeof(ItemCreationTrackingPatch), nameof(GenerateItems_Prefix));
                MethodInfo genPostfix = AccessTools.Method(typeof(ItemCreationTrackingPatch), nameof(GenerateItems_Postfix));
                if (genOriginal != null && genPrefix != null && genPostfix != null)
                {
                    harmony.Patch(genOriginal, prefix: new HarmonyMethod(genPrefix), postfix: new HarmonyMethod(genPostfix));
                }
                return true;
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]", $"ItemCreationTrackingPatch.RegisterManual failed: {ex}");
                return false;
            }
        }

        public static void DropItem_Prefix(SDG.Unturned.Item item, Vector3 point, out int __state)
        {
            __state = -1;
            try
            {
                if (!Regions.tryGetCoordinate(point, out byte x, out byte y)) return;
                ItemRegion[,] regions = ItemManager.regions;
                if (regions != null && x < regions.GetLength(0) && y < regions.GetLength(1))
                {
                    var list = regions[x, y]?.items;
                    if (list != null) __state = list.Count;
                }
            }
            catch { }
        }

        public static void DropItem_Postfix(SDG.Unturned.Item item, Vector3 point, bool isDropped, int __state)
        {
            try
            {
                if (!Regions.tryGetCoordinate(point, out byte x, out byte y)) return;
                ItemRegion[,] regions = ItemManager.regions;
                if (regions == null || x >= regions.GetLength(0) || y >= regions.GetLength(1)) return;
                List<ItemData> list = regions[x, y]?.items;
                if (list != null && list.Count > 0)
                {
                    int start = (__state >= 0 && __state < list.Count) ? __state : (__state < 0 ? list.Count - 1 : list.Count);
                    for (int i = start; i < list.Count; i++)
                    {
                        ItemData dropped = list[i];
                        if (dropped != null && dropped.instanceID != 0U)
                            ItemOriginTrackingSeam.NotifyItemDropped(dropped.instanceID);
                    }
                }
            }
            catch { }
        }

        public static void GenerateItems_Prefix(byte x, byte y, out HashSet<uint> __state)
        {
            __state = new HashSet<uint>();
            try
            {
                ItemRegion[,] regions = ItemManager.regions;
                if (regions != null && x < regions.GetLength(0) && y < regions.GetLength(1))
                {
                    var list = regions[x, y]?.items;
                    if (list != null)
                    {
                        for (int i = 0; i < list.Count; i++)
                            if (list[i] != null && list[i].instanceID != 0U)
                                __state.Add(list[i].instanceID);
                    }
                }
            }
            catch { }
        }

        public static void GenerateItems_Postfix(byte x, byte y, HashSet<uint> __state)
        {
            try
            {
                ItemRegion[,] regions = ItemManager.regions;
                if (regions != null && x < regions.GetLength(0) && y < regions.GetLength(1))
                {
                    var list = regions[x, y]?.items;
                    if (list != null)
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            ItemData it = list[i];
                            if (it != null && it.instanceID != 0U && (__state == null || !__state.Contains(it.instanceID)))
                            {
                                if (!ItemOriginTrackingSeam.IsTrackedDropped(it.instanceID) && !it.isDropped)
                                {
                                    ItemOriginTrackingSeam.NotifyNaturalItem(it.instanceID);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }
    }
}
