using SDG.NetTransport;
using SDG.Unturned;
using SteamP2PFriends.Core.Identity;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends.Adapters.Item
{
    public interface INativeItemWarehouseBridge
    {
        bool AreInputsAvailable(byte worldSize, out string failureReason);
        void SubmitRegionGeneration(byte x, byte y);
        IReadOnlyList<ItemData> GetRegionItems(byte x, byte y);
        bool IsRegionAllocated(byte x, byte y);
        void PresentRegionToHost(byte x, byte y);
        void UnpresentRegionFromHost(byte x, byte y);
        void BindGuestBaseline(ulong observerId, ulong connectionToken, byte x, byte y);
        void UnbindGuestBaseline(ulong observerId, ulong connectionToken, byte x, byte y);
        bool ActivateRegionTiming(byte x, byte y, float activationTime, Func<uint, bool> isNaturalItem = null);
        bool TryRemoveItem(byte x, byte y, uint instanceId);
        int CountSpawnPoints(byte x, byte y);
        float ReadLastRespawn(byte x, byte y);
        bool TryApplyNativeRespawn(byte x, byte y, float now);
        bool TryCreateOneNatural(byte x, byte y, float now);
        bool CommitRespawnTime(byte x, byte y, float now);
        void BindOriginTracker(IItemOriginTracker tracker);
    }

    public enum ItemPreparationStatus : byte
    {
        NotStarted = 0,
        Preparing = 1,
        Ready = 2,
        Failed = 3,
        WaitingForInputs = 4
    }

    public sealed class DefaultNativeItemWarehouseBridge : INativeItemWarehouseBridge
    {
        private static readonly System.Reflection.MethodInfo GenerateItemsMethod =
            typeof(ItemManager).GetMethod("generateItems",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        private static readonly System.Reflection.FieldInfo ManagerField =
            typeof(ItemManager).GetField("manager",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        private static readonly System.Reflection.MethodInfo DestroyAllInRegionMethod =
            typeof(ItemManager).GetMethod("DestroyAllInRegion",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        private static readonly System.Reflection.MethodInfo CancelInstantiationsInRegionMethod =
            typeof(ItemManager).GetMethod("CancelInstantiationsInRegion",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        private static readonly System.Reflection.MethodInfo CancelInstantiationByInstanceIdMethod =
            typeof(ItemManager).GetMethod("CancelInstantiationByInstanceId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        private static readonly System.Reflection.MethodInfo AskItemsMethod =
            typeof(ItemManager).GetMethod("askItems",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null,
                new[] { typeof(SDG.NetTransport.ITransportConnection), typeof(byte), typeof(byte), typeof(float) },
                null);
        private static readonly System.Reflection.FieldInfo PendingInstantiationsField =
            typeof(ItemManager).GetField("pendingInstantiations",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        private static readonly System.Type ItemInstantiationParametersType =
            typeof(ItemManager).Assembly.GetType("SDG.Unturned.ItemInstantiationParameters");
        private static readonly System.Reflection.FieldInfo RegionXField = ItemInstantiationParametersType?.GetField("region_x");
        private static readonly System.Reflection.FieldInfo RegionYField = ItemInstantiationParametersType?.GetField("region_y");
        private static readonly System.Reflection.FieldInfo AssetIdField = ItemInstantiationParametersType?.GetField("assetId");
        private static readonly System.Reflection.FieldInfo AmountField = ItemInstantiationParametersType?.GetField("amount");
        private static readonly System.Reflection.FieldInfo QualityField = ItemInstantiationParametersType?.GetField("quality");
        private static readonly System.Reflection.FieldInfo StateField = ItemInstantiationParametersType?.GetField("state");
        private static readonly System.Reflection.FieldInfo PointField = ItemInstantiationParametersType?.GetField("point");
        private static readonly System.Reflection.FieldInfo InstanceIdField = ItemInstantiationParametersType?.GetField("instanceID");
        private static readonly System.Reflection.FieldInfo SortOrderField = ItemInstantiationParametersType?.GetField("sortOrder");
        private static readonly System.Reflection.FieldInfo ShouldPlayEffectField = ItemInstantiationParametersType?.GetField("shouldPlayEffect");
        private static bool AreInstantiationFieldsResolved =>
            RegionXField != null && RegionYField != null && AssetIdField != null
            && AmountField != null && QualityField != null && StateField != null
            && PointField != null && InstanceIdField != null && SortOrderField != null
            && ShouldPlayEffectField != null;
        private static readonly System.Reflection.FieldInfo LastDroppedField =
            typeof(ItemData).GetField("_lastDropped",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        private static readonly System.Reflection.MethodInfo RespawnItemsMethod =
            typeof(ItemManager).GetMethod("respawnItems",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        private static readonly System.Reflection.FieldInfo RespawnCursorXField =
            typeof(ItemManager).GetField("respawnItems_X",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        private static readonly System.Reflection.FieldInfo RespawnCursorYField =
            typeof(ItemManager).GetField("respawnItems_Y",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        private static readonly System.Reflection.FieldInfo SendDestroyItemField =
            typeof(ItemManager).GetField("SendDestroyItem",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        private static ItemRegion[,] SafeGetRegions()
        {
            try { return ItemManager.regions; }
            catch (Exception) { return null; }
        }

        public bool AreInputsAvailable(byte worldSize, out string failureReason)
        {
            if (LevelItems.spawns == null || LevelItems.spawns.GetLength(0) < worldSize)
            {
                failureReason = "LevelItems.spawns not ready";
                return false;
            }
            ItemRegion[,] regions = SafeGetRegions();
            if (regions == null || regions.GetLength(0) < worldSize)
            {
                failureReason = "ItemManager.regions not ready";
                return false;
            }
            if (ManagerField == null || ManagerField.GetValue(null) == null || GenerateItemsMethod == null)
            {
                failureReason = "ItemManager manager instance or generateItems reflection targets not found";
                return false;
            }
            failureReason = string.Empty;
            return true;
        }

        public void BindOriginTracker(IItemOriginTracker tracker)
        {
            ItemOriginTrackingSeam.SetActiveTracker(tracker);
        }

        public void SubmitRegionGeneration(byte x, byte y)
        {
            object mgr = ManagerField?.GetValue(null);
            if (mgr == null || GenerateItemsMethod == null)
            {
                throw new InvalidOperationException("ItemManager generateItems unavailable.");
            }

            ItemRegion[,] regions = SafeGetRegions();
            var preIds = new HashSet<uint>();
            List<ItemData> preItems = null;
            if (regions != null && x < regions.GetLength(0) && y < regions.GetLength(1))
            {
                preItems = regions[x, y]?.items;
                if (preItems != null)
                {
                    for (int i = 0; i < preItems.Count; i++)
                    {
                        var it = preItems[i];
                        if (it != null && it.instanceID != 0U)
                            preIds.Add(it.instanceID);
                    }
                }
            }

            GenerateItemsMethod.Invoke(mgr, new object[] { x, y });

            if (regions != null && x < regions.GetLength(0) && y < regions.GetLength(1))
            {
                var postItems = regions[x, y]?.items;
                if (postItems != null)
                {
                    var postIds = new HashSet<uint>();
                    for (int i = 0; i < postItems.Count; i++)
                        if (postItems[i] != null && postItems[i].instanceID != 0U)
                            postIds.Add(postItems[i].instanceID);

                    if (preItems != null && !ReferenceEquals(preItems, postItems))
                    {
                        for (int i = 0; i < preItems.Count; i++)
                        {
                            var dropped = preItems[i];
                            if (dropped != null && dropped.instanceID != 0U
                                && (dropped.isDropped || ItemOriginTrackingSeam.IsTrackedDropped(dropped.instanceID)))
                            {
                                if (postIds.Add(dropped.instanceID))
                                    postItems.Add(dropped);
                            }
                        }
                    }

                    for (int i = 0; i < postItems.Count; i++)
                    {
                        ItemData item = postItems[i];
                        if (item != null && item.instanceID != 0U && !preIds.Contains(item.instanceID))
                        {
                            if (!ItemOriginTrackingSeam.IsTrackedDropped(item.instanceID) && !item.isDropped)
                            {
                                ItemOriginTrackingSeam.NotifyNaturalItem(item.instanceID);
                            }
                        }
                    }
                }
            }
        }

        public IReadOnlyList<ItemData> GetRegionItems(byte x, byte y)
        {
            ItemRegion[,] regions = SafeGetRegions();
            if (regions != null && x < regions.GetLength(0) && y < regions.GetLength(1))
            {
                return regions[x, y]?.items;
            }
            return null;
        }

        public bool IsRegionAllocated(byte x, byte y)
        {
            ItemRegion[,] regions = SafeGetRegions();
            return regions != null && x < regions.GetLength(0) && y < regions.GetLength(1) && regions[x, y] != null;
        }

        private static UnityEngine.Vector3 GetCameraPositionSafe(out bool hasCamera)
        {
            try
            {
                var cam = MainCamera.instance;
                if (cam != null && cam.transform != null)
                {
                    hasCamera = true;
                    return cam.transform.position;
                }
            }
            catch
            {
                // 纯内存运行器或缺少引擎调用
            }
            hasCamera = false;
            return UnityEngine.Vector3.zero;
        }

        public void PresentRegionToHost(byte x, byte y)
        {
            if (!ItemSettlementGate.TryAuthorizeIncrement(x, y))
                throw new InvalidOperationException("Host item instantiation is waiting for settlement.");
            ItemRegion[,] regions = SafeGetRegions();
            if (regions == null || x >= regions.GetLength(0) || y >= regions.GetLength(1))
                throw new InvalidOperationException($"ItemManager.regions out of bounds or null for region ({x}, {y}).");
            ItemRegion region = regions[x, y];
            if (region == null)
                throw new InvalidOperationException($"ItemRegion ({x}, {y}) is null.");

            if (CancelInstantiationsInRegionMethod == null || DestroyAllInRegionMethod == null)
                throw new InvalidOperationException("ItemManager CancelInstantiationsInRegion or DestroyAllInRegion reflection targets not found.");

            CancelInstantiationsInRegionMethod.Invoke(null, new object[] { x, y });
            DestroyAllInRegionMethod.Invoke(null, new object[] { region });
            region.isNetworked = true;

            int itemCount = region.items?.Count ?? 0;
            if (itemCount > 0)
            {
                if (PendingInstantiationsField == null || ItemInstantiationParametersType == null || !AreInstantiationFieldsResolved)
                    throw new InvalidOperationException("ItemManager pendingInstantiations or ItemInstantiationParameters reflection targets not found for non-empty region presentation.");

                var pendingList = PendingInstantiationsField.GetValue(null) as System.Collections.IList;
                if (pendingList == null)
                    throw new InvalidOperationException("ItemManager pendingInstantiations list instance not found for non-empty region presentation.");

                var cameraPos = GetCameraPositionSafe(out bool hasCamera);

                for (int i = 0; i < region.items.Count; i++)
                {
                    ItemData data = region.items[i];
                    if (data?.item == null) continue;
                    float sortOrder = hasCamera ? (cameraPos - data.point).sqrMagnitude : 0f;
                    object param = Activator.CreateInstance(ItemInstantiationParametersType);
                    RegionXField.SetValue(param, x);
                    RegionYField.SetValue(param, y);
                    AssetIdField.SetValue(param, data.item.id);
                    AmountField.SetValue(param, data.item.amount);
                    QualityField.SetValue(param, data.item.quality);
                    StateField.SetValue(param, data.item.state);
                    PointField.SetValue(param, data.point);
                    InstanceIdField.SetValue(param, data.instanceID);
                    SortOrderField.SetValue(param, sortOrder);
                    ShouldPlayEffectField.SetValue(param, false);

                    int insertIndex = pendingList.Count;
                    for (int j = 0; j < pendingList.Count; j++)
                    {
                        float existingOrder = (float)SortOrderField.GetValue(pendingList[j]);
                        if (sortOrder < existingOrder) { insertIndex = j; break; }
                    }
                    pendingList.Insert(insertIndex, param);
                }
            }
        }

        public void UnpresentRegionFromHost(byte x, byte y)
        {
            ItemRegion[,] regions = SafeGetRegions();
            if (regions == null || x >= regions.GetLength(0) || y >= regions.GetLength(1))
                throw new InvalidOperationException($"ItemManager.regions out of bounds or null for region ({x}, {y}).");
            ItemRegion region = regions[x, y];
            if (region == null)
                throw new InvalidOperationException($"ItemRegion ({x}, {y}) is null.");

            CancelInstantiationsInRegionMethod?.Invoke(null, new object[] { x, y });
            DestroyAllInRegionMethod?.Invoke(null, new object[] { region });
            region.isNetworked = false;
        }

        public void BindGuestBaseline(ulong observerId, ulong connectionToken, byte x, byte y)
        {
            if (observerId == 0UL) throw new ArgumentOutOfRangeException(nameof(observerId));
            if (connectionToken == 0UL) throw new ArgumentOutOfRangeException(nameof(connectionToken));
            SteamPlayer client = FindClient(observerId);
            if (client == null) throw new InvalidOperationException($"Guest observer {observerId} not found in Provider.clients.");
            if (client.transportConnection == null) throw new InvalidOperationException($"Guest observer {observerId} transport null.");
            ulong currentToken = ItemObserverReplicationAdapter.EnsureConnectionToken(observerId, client.transportConnection);
            if (currentToken != connectionToken)
                throw new InvalidOperationException($"Guest observer {observerId} connectionToken mismatch: expected {connectionToken}, actual {currentToken}.");

            object mgr = ManagerField?.GetValue(null);
            if (mgr == null || AskItemsMethod == null) throw new InvalidOperationException("ItemManager askItems unavailable.");
            ItemObserverReplicationAdapter.DispatchCandidateGuestAskItems(
                () => AskItemsMethod.Invoke(mgr, new object[] { client.transportConnection, x, y, 0f }),
                () =>
                {
                    if (client.player?.movement?.loadedRegions == null) return;
                    if (x >= client.player.movement.loadedRegions.GetLength(0) || y >= client.player.movement.loadedRegions.GetLength(1)) return;
                    var lr = client.player.movement.loadedRegions[x, y];
                    if (lr != null) lr.isItemsLoaded = true;
                });
        }

        public bool ActivateRegionTiming(byte x, byte y, float activationTime, Func<uint, bool> isNaturalItem = null)
        {
            ItemRegion[,] regions = SafeGetRegions();
            if (regions == null || x >= regions.GetLength(0) || y >= regions.GetLength(1))
                throw new InvalidOperationException($"ItemManager.regions out of bounds or null for region ({x}, {y}).");
            ItemRegion region = regions[x, y];
            if (region == null)
                throw new InvalidOperationException($"ItemRegion ({x}, {y}) is null.");

            if (LastDroppedField == null)
                throw new InvalidOperationException("ItemData._lastDropped reflection field not found.");

            float originalRespawn = region.lastRespawn;
            var modifiedItems = new List<(ItemData item, float previousLastDropped)>();

            try
            {
                region.lastRespawn = activationTime;
                if (Math.Abs(region.lastRespawn - activationTime) > 0.001f)
                    throw new InvalidOperationException($"ItemRegion.lastRespawn verification failed for ({x}, {y}): expected {activationTime}, actual {region.lastRespawn}.");

                if (region.items != null)
                {
                    for (int i = 0; i < region.items.Count; i++)
                    {
                        ItemData data = region.items[i];
                        if (data == null) continue;
                        bool isNatural = isNaturalItem != null ? isNaturalItem(data.instanceID) : !data.isDropped;
                        if (isNatural)
                        {
                            modifiedItems.Add((data, data.lastDropped));
                            LastDroppedField.SetValue(data, activationTime);
                            if (Math.Abs(data.lastDropped - activationTime) > 0.001f)
                                throw new InvalidOperationException($"ItemData._lastDropped verification failed for instance {data.instanceID}: expected {activationTime}, actual {data.lastDropped}.");
                        }
                    }
                }
                return true;
            }
            catch
            {
                region.lastRespawn = originalRespawn;
                for (int i = 0; i < modifiedItems.Count; i++)
                {
                    try { LastDroppedField.SetValue(modifiedItems[i].item, modifiedItems[i].previousLastDropped); }
                    catch { }
                }
                throw;
            }
        }

        public bool TryRemoveItem(byte x, byte y, uint instanceId)
        {
            if (instanceId == 0U) return false;
            ItemRegion[,] regions = SafeGetRegions();
            if (regions == null || x >= regions.GetLength(0) || y >= regions.GetLength(1)) return false;
            ItemRegion region = regions[x, y];
            if (region?.items == null) return false;
            int index = -1;
            for (int i = 0; i < region.items.Count; i++)
            {
                if (region.items[i] != null && region.items[i].instanceID == instanceId)
                {
                    index = i;
                    break;
                }
            }
            if (index < 0) return false;
            ItemData removed = region.items[index];
            region.items.RemoveAt(index);
            if (ContainsInstance(region.items, instanceId)) return false;
            CancelInstantiationByInstanceIdMethod?.Invoke(null, new object[] { instanceId });
            if (!ItemSettlementGate.TryAuthorizeIncrement(x, y))
                return true;
            object sender = SendDestroyItemField?.GetValue(null);
            if (!BroadcastDestroy(sender, x, y, instanceId))
            {
                region.items.Insert(index, removed);
                return false;
            }
            return !ContainsInstance(region.items, instanceId);
        }

        private static bool ContainsInstance(System.Collections.Generic.List<ItemData> items, uint instanceId)
        {
            for (int i = 0; i < items.Count; i++)
                if (items[i] != null && items[i].instanceID == instanceId) return true;
            return false;
        }

        private static bool BroadcastDestroy(object sender, byte x, byte y, uint instanceId)
        {
            if (sender == null) return false;
            var connections = Regions.GatherClientConnections(x, y, ItemManager.ITEM_REGIONS);
            var invoke = sender.GetType().GetMethod("Invoke", new[]
            {
                typeof(ENetReliability),
                typeof(System.Collections.Generic.List<ITransportConnection>),
                typeof(byte), typeof(byte), typeof(uint), typeof(bool)
            });
            if (invoke == null) return false;
            invoke.Invoke(sender, new object[] { ENetReliability.Reliable, connections, x, y, instanceId, false });
            return true;
        }

        public int CountSpawnPoints(byte x, byte y)
        {
            try
            {
                var spawns = LevelItems.spawns;
                if (spawns == null || x >= spawns.GetLength(0) || y >= spawns.GetLength(1)) return 0;
                return spawns[x, y] == null ? 0 : spawns[x, y].Count;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Unable to read native item spawn points.", ex);
            }
        }

        public float ReadLastRespawn(byte x, byte y)
        {
            ItemRegion[,] regions = SafeGetRegions();
            if (regions == null || x >= regions.GetLength(0) || y >= regions.GetLength(1)) return 0f;
            return regions[x, y] == null ? 0f : regions[x, y].lastRespawn;
        }

        public bool TryCreateOneNatural(byte x, byte y, float now)
        {
            try
            {
                var spawns = LevelItems.spawns;
                if (spawns == null || x >= spawns.GetLength(0) || y >= spawns.GetLength(1) || spawns[x, y] == null || spawns[x, y].Count == 0)
                    return false;
                ItemRegion[,] regions = SafeGetRegions();
                if (regions == null || x >= regions.GetLength(0) || y >= regions.GetLength(1) || regions[x, y]?.items == null)
                    return false;
                ItemSpawnpoint spawn = spawns[x, y][UnityEngine.Random.Range(0, spawns[x, y].Count)];
                if (!SafezoneManager.checkPointValid(spawn.point)) return false;
                for (int i = 0; i < regions[x, y].items.Count; i++)
                    if ((regions[x, y].items[i].point - spawn.point).sqrMagnitude < 4f) return false;
                ushort id = LevelItems.getItem(spawn);
                ItemAsset asset = Assets.find(EAssetType.ITEM, id) as ItemAsset;
                if (asset == null) return false;
                SDG.Unturned.Item item = new SDG.Unturned.Item(id, EItemOrigin.WORLD);
                UnityEngine.Vector3 point = spawn.point;
                bool shouldAllow = true;
                ItemManager.onServerSpawningItemDrop?.Invoke(item, ref point, ref shouldAllow);
                if (!shouldAllow) return false;
                var countField = typeof(ItemManager).GetField("instanceCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                uint next = countField == null ? 0U : (uint)countField.GetValue(null) + 1U;
                countField?.SetValue(null, next);
                regions[x, y].items.Add(new ItemData(item, next, point, false));
                return true;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Single natural item creation failed for ({x}, {y}).", ex);
            }
        }

        public bool TryApplyNativeRespawn(byte x, byte y, float now)
        {
            object manager = ManagerField?.GetValue(null);
            if (manager == null || RespawnItemsMethod == null || RespawnCursorXField == null || RespawnCursorYField == null)
                return false;
            ItemLegacyCycleGate.InOwnerStep = true;
            try
            {
                RespawnCursorXField.SetValue(null, x);
                RespawnCursorYField.SetValue(null, y);
                RespawnItemsMethod.Invoke(manager, null);
                return true;
            }
            catch (Exception) { return false; }
            finally { ItemLegacyCycleGate.InOwnerStep = false; }
        }

        public bool CommitRespawnTime(byte x, byte y, float now)
        {
            ItemRegion[,] regions = SafeGetRegions();
            if (regions == null || x >= regions.GetLength(0) || y >= regions.GetLength(1) || regions[x, y] == null)
                return false;
            regions[x, y].lastRespawn = now;
            return Math.Abs(regions[x, y].lastRespawn - now) <= 0.001f;
        }

        public void UnbindGuestBaseline(ulong observerId, ulong connectionToken, byte x, byte y)
        {
            if (observerId == 0UL) return;
            SteamPlayer client = FindClient(observerId);
            if (client?.player?.movement?.loadedRegions != null && x < client.player.movement.loadedRegions.GetLength(0) && y < client.player.movement.loadedRegions.GetLength(1))
            {
                var lr = client.player.movement.loadedRegions[x, y];
                if (lr != null) lr.isItemsLoaded = false;
            }
        }

        private static SteamPlayer FindClient(ulong observerId)
        {
            if (Provider.clients == null) return null;
            for (int i = 0; i < Provider.clients.Count; i++)
            {
                SteamPlayer sp = Provider.clients[i];
                if (sp?.playerID != null && sp.playerID.steamID.m_SteamID == observerId) return sp;
            }
            return null;
        }
    }

    public sealed class ItemPreparationAuthority : IItemOriginTracker
    {
        private readonly INativeItemWarehouseBridge _bridge;
        private readonly HashSet<RegionKey> _preparedRegions = new HashSet<RegionKey>();
        private readonly HashSet<uint> _trackedDropIds = new HashSet<uint>();
        private readonly HashSet<uint> _trackedUnknownIds = new HashSet<uint>();
        private readonly HashSet<uint> _trackedNaturalIds = new HashSet<uint>();
        private ItemPreparationStatus _status = ItemPreparationStatus.NotStarted;
        private string _worldIdentity;
        private SessionEpoch _sessionEpoch;
        private byte _worldSize;

        public ItemPreparationAuthority(INativeItemWarehouseBridge bridge = null)
        {
            _bridge = bridge ?? new DefaultNativeItemWarehouseBridge();
            _bridge.BindOriginTracker(this);
        }

        public INativeItemWarehouseBridge Bridge => _bridge;
        public ItemPreparationStatus Status => _status;
        public bool IsReady => _status == ItemPreparationStatus.Ready;
        public int PreparedRegionCount => _preparedRegions.Count;
        public byte WorldSize => _worldSize;
        public string WorldIdentity => _worldIdentity;
        public SessionEpoch SessionEpoch => _sessionEpoch;

        public void TrackNaturalItem(uint instanceId)
        {
            if (instanceId != 0U) _trackedNaturalIds.Add(instanceId);
        }

        public void TrackDroppedItem(uint instanceId)
        {
            if (instanceId != 0U) _trackedDropIds.Add(instanceId);
        }

        public void TrackUnknownItem(uint instanceId)
        {
            if (instanceId != 0U) _trackedUnknownIds.Add(instanceId);
        }

        public bool IsTrackedDropped(uint instanceId) =>
            instanceId != 0U && _trackedDropIds.Contains(instanceId);

        public bool IsTrackedNatural(uint instanceId) =>
            instanceId != 0U && _trackedNaturalIds.Contains(instanceId);

        public bool CanPerformOperation(RegionKey regionKey) => IsRegionPrepared(regionKey);

        public void EnsureRegionPrepared(RegionKey regionKey)
        {
            if (!IsRegionPrepared(regionKey))
            {
                throw new ItemRegionNotPreparedException($"Region ({regionKey.X}, {regionKey.Y}) is not prepared.");
            }
        }

        public void Reset(SessionEpoch sessionEpoch, string worldIdentity)
        {
            _status = ItemPreparationStatus.NotStarted;
            _preparedRegions.Clear();
            _trackedDropIds.Clear();
            _trackedUnknownIds.Clear();
            _trackedNaturalIds.Clear();
            _sessionEpoch = sessionEpoch;
            _worldIdentity = worldIdentity;
            _worldSize = 0;
        }

        public bool PrepareFullMap(
            SessionEpoch sessionEpoch,
            string worldIdentity,
            byte worldSize,
            ItemMetadataStore metadataStore = null,
            float currentTime = 0f)
        {
            if (sessionEpoch.Value == 0UL || worldSize == 0 || string.IsNullOrWhiteSpace(worldIdentity))
            {
                _sessionEpoch = sessionEpoch;
                _worldIdentity = worldIdentity;
                _worldSize = worldSize;
                _status = ItemPreparationStatus.Failed;
                return false;
            }

            if (_status == ItemPreparationStatus.Ready
                && _sessionEpoch.Equals(sessionEpoch)
                && string.Equals(_worldIdentity, worldIdentity, StringComparison.Ordinal)
                && _preparedRegions.Count == worldSize * worldSize)
            {
                return true;
            }

            if (_status == ItemPreparationStatus.Failed
                && _sessionEpoch.Equals(sessionEpoch)
                && string.Equals(_worldIdentity, worldIdentity, StringComparison.Ordinal))
            {
                return false;
            }

            if (_status == ItemPreparationStatus.Preparing) return false;

            if (!_bridge.AreInputsAvailable(worldSize, out _))
            {
                _sessionEpoch = sessionEpoch;
                _worldIdentity = worldIdentity;
                _worldSize = worldSize;
                _status = ItemPreparationStatus.WaitingForInputs;
                return false;
            }

            _sessionEpoch = sessionEpoch;
            _worldIdentity = worldIdentity;
            _worldSize = worldSize;
            _preparedRegions.Clear();
            _status = ItemPreparationStatus.Preparing;

            for (byte x = 0; x < worldSize; x++)
            {
                for (byte y = 0; y < worldSize; y++)
                {
                    if (!_bridge.IsRegionAllocated(x, y))
                    {
                        _status = ItemPreparationStatus.Failed;
                        return false;
                    }

                    IReadOnlyList<ItemData> preItems = _bridge.GetRegionItems(x, y);

                    try
                    {
                        _bridge.SubmitRegionGeneration(x, y);
                    }
                    catch
                    {
                        _status = ItemPreparationStatus.Failed;
                        return false;
                    }

                    IReadOnlyList<ItemData> postItems = _bridge.GetRegionItems(x, y);
                    if (postItems == null)
                    {
                        _status = ItemPreparationStatus.Failed;
                        return false;
                    }

                    var preIds = new HashSet<uint>();
                    if (preItems != null)
                    {
                        for (int i = 0; i < preItems.Count; i++)
                            if (preItems[i] != null) preIds.Add(preItems[i].instanceID);
                    }

                    if (preItems != null && postItems is IList<ItemData> postList && !ReferenceEquals(preItems, postItems))
                    {
                        var postIds = new HashSet<uint>();
                        for (int i = 0; i < postItems.Count; i++)
                            if (postItems[i] != null) postIds.Add(postItems[i].instanceID);

                        for (int i = 0; i < preItems.Count; i++)
                        {
                            ItemData pre = preItems[i];
                            if (pre != null && (pre.isDropped || _trackedDropIds.Contains(pre.instanceID)) && postIds.Add(pre.instanceID))
                                postList.Add(pre);
                        }
                    }

                    if (metadataStore != null)
                    {
                        var regionKey = new RegionKey(x, y);
                        for (int i = 0; i < postItems.Count; i++)
                            RegisterItemMetadata(metadataStore, postItems[i], regionKey, currentTime);
                    }

                    _preparedRegions.Add(new RegionKey(x, y));
                }
            }

            if (_preparedRegions.Count < worldSize * worldSize)
            {
                _status = ItemPreparationStatus.Failed;
                return false;
            }

            _status = ItemPreparationStatus.Ready;
            return true;
        }

        private void RegisterItemMetadata(
            ItemMetadataStore store,
            ItemData item,
            RegionKey regionKey,
            float currentTime)
        {
            if (item == null || store.TryGetMetadata(item.instanceID, out _)) return;
            ItemOriginKind origin;
            float creationTime;
            float timingBenchmark;
            if (item.instanceID != 0 && (_trackedDropIds.Contains(item.instanceID) || item.isDropped))
            {
                origin = ItemOriginKind.Dropped;
                creationTime = item.lastDropped;
                timingBenchmark = creationTime;
            }
            else if (item.instanceID != 0 && _trackedNaturalIds.Contains(item.instanceID))
            {
                origin = ItemOriginKind.Natural;
                creationTime = item.lastDropped;
                timingBenchmark = 0f;
            }
            else
            {
                origin = ItemOriginKind.Unknown;
                creationTime = item.lastDropped;
                timingBenchmark = creationTime;
            }
            store.RegisterItem(item.instanceID, regionKey, origin, creationTime, timingBenchmark);
        }

        public bool TryBeginPreparation(
            SessionEpoch sessionEpoch,
            string worldIdentity,
            byte worldSize,
            bool inputsAvailable)
        {
            if (sessionEpoch.Value == 0UL || worldSize == 0 || string.IsNullOrWhiteSpace(worldIdentity))
            {
                _status = ItemPreparationStatus.Failed;
                return false;
            }

            if (!inputsAvailable)
            {
                _status = ItemPreparationStatus.WaitingForInputs;
                return false;
            }

            if ((_status == ItemPreparationStatus.Ready || _status == ItemPreparationStatus.Preparing)
                && _sessionEpoch.Equals(sessionEpoch)
                && string.Equals(_worldIdentity, worldIdentity, StringComparison.Ordinal))
            {
                // 同一会话与地图已准备或正在准备
                return false;
            }

            _sessionEpoch = sessionEpoch;
            _worldIdentity = worldIdentity;
            _worldSize = worldSize;
            _preparedRegions.Clear();
            _status = ItemPreparationStatus.Preparing;
            return true;
        }

        public void MarkRegionPrepared(RegionKey regionKey)
        {
            if (_status == ItemPreparationStatus.Preparing)
            {
                if (regionKey.X < _worldSize && regionKey.Y < _worldSize)
                {
                    _preparedRegions.Add(regionKey);
                }
            }
        }

        public bool CompletePreparation(
            SessionEpoch sessionEpoch,
            string worldIdentity,
            bool success)
        {
            if (_status != ItemPreparationStatus.Preparing
                || !_sessionEpoch.Equals(sessionEpoch)
                || !string.Equals(_worldIdentity, worldIdentity, StringComparison.Ordinal))
            {
                return false;
            }

            if (!success)
            {
                _status = ItemPreparationStatus.Failed;
                return false;
            }

            int expectedTotal = _worldSize * _worldSize;
            if (_preparedRegions.Count < expectedTotal)
            {
                _status = ItemPreparationStatus.Failed;
                return false;
            }

            _status = ItemPreparationStatus.Ready;
            return true;
        }

        public bool IsRegionPrepared(RegionKey regionKey)
        {
            return _status == ItemPreparationStatus.Ready && _preparedRegions.Contains(regionKey);
        }

        public void BindCoreSession(SessionEpoch sessionEpoch, string worldIdentity)
        {
            if (sessionEpoch.Value == 0UL)
            {
                Reset(default(SessionEpoch), null);
                return;
            }

            // Core 绑定在同一会话与地图上保留已准备数据。
            if (_status == ItemPreparationStatus.Ready
                && _sessionEpoch.Equals(sessionEpoch)
                && string.Equals(_worldIdentity, worldIdentity, StringComparison.Ordinal))
            {
                return;
            }
            if (!_sessionEpoch.Equals(sessionEpoch)
                || !string.Equals(_worldIdentity, worldIdentity, StringComparison.Ordinal))
            {
                Reset(sessionEpoch, worldIdentity);
            }
        }
    }
}
