using System;
using System.Collections.Generic;
using System.Reflection;
using SDG.Unturned;
using SteamP2PFriends.Adapters.Item;
using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver.Demand;
using SteamP2PFriends.MultiObserver.Lifecycle;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// E02 §3.1 最小强 fixture：一份原生存储（regionArray[64,64]/loadedArray[64,64]），
    /// bridge 与 view 读同一存储；身份经生产 stage/commit；不复制原生填参/插入算法。
    /// </summary>
    internal static class R01E02Fixture
    {
        internal const ulong HostId = 1000UL;
        internal const ulong GuestId = 2001UL;
        internal const byte WorldSize = 64;
        internal const byte CenterA = 2;
        internal const byte CenterB = 5;

        internal static readonly BindingFlags All = BindingFlags.Instance
            | BindingFlags.Public | BindingFlags.NonPublic;

        internal sealed class NativeStorage
        {
            public readonly ItemRegion[,] Regions = new ItemRegion[WorldSize, WorldSize];
            public readonly LoadedRegion[,] Loaded = new LoadedRegion[WorldSize, WorldSize];

            public NativeStorage()
            {
                for (byte x = 0; x < WorldSize; x++)
                    for (byte y = 0; y < WorldSize; y++)
                    {
                        var region = (ItemRegion)System.Runtime.Serialization.FormatterServices
                            .GetUninitializedObject(RegionType);
                        typeof(ItemRegion).GetField("items", All)?.SetValue(
                            region, new List<ItemData>());
                        typeof(ItemRegion).GetField("_drops", All)?.SetValue(
                            region, new List<ItemDrop>());
                        typeof(ItemRegion).GetField("isNetworked", All)?.SetValue(region, false);
                        typeof(ItemRegion).GetField("isPendingDestroy", All)?.SetValue(region, false);
                        Regions[x, y] = region;
                        var loaded = (LoadedRegion)System.Runtime.Serialization.FormatterServices
                            .GetUninitializedObject(LoadedRegionType);
                        typeof(LoadedRegion).GetField("isItemsLoaded", All)?.SetValue(loaded, false);
                        Loaded[x, y] = loaded;
                    }
            }

            public static readonly Type RegionType = typeof(SDG.Unturned.ItemManager).Assembly
                .GetType("SDG.Unturned.ItemRegion", true);
            public static readonly Type LoadedRegionType = typeof(SDG.Unturned.ItemManager)
                .Assembly.GetType("SDG.Unturned.LoadedRegion", true);
        }

        internal sealed class FixtureBridge : INativeItemWarehouseBridge
        {
            public readonly NativeStorage Storage = new NativeStorage();
            public int HostPresentCalls;

            public bool AreInputsAvailable(byte worldSize, out string failureReason)
            {
                failureReason = string.Empty;
                return true;
            }

            public void SubmitRegionGeneration(byte x, byte y) { }

            public IReadOnlyList<ItemData> GetRegionItems(byte x, byte y) =>
                Storage.Regions[x, y].items;

            public bool IsRegionAllocated(byte x, byte y) => true;

            public void PresentRegionToHost(byte x, byte y) => HostPresentCalls++;

            public void UnpresentRegionFromHost(byte x, byte y) { }

            public void BindGuestBaseline(ulong observerId, ulong connectionToken, byte x, byte y) { }

            public void UnbindGuestBaseline(ulong observerId, ulong connectionToken, byte x, byte y) { }

            public bool ActivateRegionTiming(byte x, byte y, float activationTime,
                Func<uint, bool> isNaturalItem = null) => true;

            public bool TryRemoveItem(byte x, byte y, uint instanceId)
            {
                List<ItemData> list = Storage.Regions[x, y].items;
                for (int i = 0; i < list.Count; i++)
                    if (list[i] != null && list[i].instanceID == instanceId)
                    {
                        list.RemoveAt(i);
                        return true;
                    }
                return false;
            }

            public int CountSpawnPoints(byte x, byte y) => 1;

            public bool AreNaturalInputsAvailable(byte x, byte y, out string failureReason)
            {
                failureReason = string.Empty;
                return true;
            }

            public float ReadLastRespawn(byte x, byte y) => 0f;

            public bool TryApplyNativeRespawn(byte x, byte y, float now) => true;

            public bool TryCreateOneNatural(byte x, byte y, float now) => true;

            public ItemNaturalCreationOutcome CreateOneNatural(byte x, byte y, float now) =>
                ItemNaturalCreationOutcome.Created;

            public bool CommitRespawnTime(byte x, byte y, float now) => true;

            public void BindOriginTracker(IItemOriginTracker tracker) { }

            public object ReadNativeRegionsScope() => Storage.Regions;
        }

        internal static ItemData MakeItem(uint instanceId, ushort assetId, byte amount, byte quality)
        {
            var data = (ItemData)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(ItemDataType);
            var item = (Item)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(ItemType);
            SetField(item, "_id", assetId);
            SetField(item, "amount", amount);
            SetField(item, "quality", quality);
            SetField(item, "state", new byte[0]);
            SetField(data, "_item", item);
            SetField(data, "_instanceID", instanceId);
            SetField(data, "_isDropped", false);
            return data;
        }

        internal static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, All);
            if (field == null) throw new MissingFieldException(
                target.GetType().FullName, name);
            field.SetValue(target, value);
        }

        internal static readonly Type ItemDataType = typeof(SDG.Unturned.ItemManager).Assembly
            .GetType("SDG.Unturned.ItemData", true);
        internal static readonly Type ItemType = typeof(SDG.Unturned.ItemManager).Assembly
            .GetType("SDG.Unturned.Item", true);
    }
}
