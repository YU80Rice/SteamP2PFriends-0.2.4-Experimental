using SDG.Unturned;
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace SteamP2PFriends.Adapters.Item
{
    internal sealed class NativePendingList : IList
    {
        private static readonly FieldInfo PendingField = typeof(ItemManager).GetField(
            "pendingInstantiations", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly Type ParamType = typeof(ItemManager).Assembly.GetType(
            "SDG.Unturned.ItemInstantiationParameters");

        private readonly Func<IList> _rawOverride;

        internal NativePendingList(Func<IList> readRaw)
        {
            _rawOverride = readRaw ?? throw new ArgumentNullException(nameof(readRaw));
        }

        internal NativePendingList()
        {
        }

        private IList Raw()
        {
            if (_rawOverride != null) return _rawOverride();
            IList list = PendingField?.GetValue(null) as IList;
            if (list == null || ParamType == null)
                throw new InvalidOperationException("ItemManager pendingInstantiations unavailable.");
            return list;
        }

        public int Count => Raw().Count;
        public bool IsFixedSize => false;
        public bool IsReadOnly => false;
        public bool IsSynchronized => false;
        public object SyncRoot => Raw().SyncRoot;

        public object this[int index]
        {
            get => From(Raw()[index]);
            set => Raw()[index] = To((PendingView)value);
        }

        public int Add(object value)
        {
            Raw().Add(To((PendingView)value));
            return Count - 1;
        }

        public void Insert(int index, object value) => Raw().Insert(index, To((PendingView)value));
        public void RemoveAt(int index) => Raw().RemoveAt(index);
        public void Clear() => Raw().Clear();
        public void CopyTo(Array array, int index) => throw new NotSupportedException();
        public bool Contains(object value) => IndexOf(value) >= 0;

        public int IndexOf(object value)
        {
            PendingView target = value as PendingView;
            if (target == null) return -1;
            IList raw = Raw();
            for (int i = 0; i < raw.Count; i++)
            {
                PendingView item = From(raw[i]);
                if (item.RegionX == target.RegionX && item.RegionY == target.RegionY
                    && item.InstanceId == target.InstanceId) return i;
            }
            return -1;
        }
        public void Remove(object value) => throw new NotSupportedException();
        public IEnumerator GetEnumerator() => Raw().GetEnumerator();

        private static readonly FieldInfo RegionXField = ParamType?.GetField("region_x");
        private static readonly FieldInfo RegionYField = ParamType?.GetField("region_y");
        private static readonly FieldInfo InstanceField = ParamType?.GetField("instanceID");
        private static readonly FieldInfo AssetField = ParamType?.GetField("assetId");
        private static readonly FieldInfo AmountField = ParamType?.GetField("amount");
        private static readonly FieldInfo QualityField = ParamType?.GetField("quality");
        private static readonly FieldInfo StateField = ParamType?.GetField("state");
        private static readonly FieldInfo PointField = ParamType?.GetField("point");
        private static readonly FieldInfo SortOrderField = ParamType?.GetField("sortOrder");
        private static readonly FieldInfo EffectField = ParamType?.GetField("shouldPlayEffect");

        private static PendingView From(object raw)
        {
            if (raw == null || ParamType == null) throw new InvalidOperationException("Pending instantiation missing.");
            return new PendingView
            {
                RegionX = (byte)RegionXField.GetValue(raw),
                RegionY = (byte)RegionYField.GetValue(raw),
                InstanceId = (uint)InstanceField.GetValue(raw),
                AssetId = (ushort)AssetField.GetValue(raw),
                Amount = (byte)AmountField.GetValue(raw),
                Quality = (byte)QualityField.GetValue(raw),
                State = (byte[])StateField.GetValue(raw),
                Point = (Vector3)PointField.GetValue(raw),
                SortOrder = (float)SortOrderField.GetValue(raw),
                ShouldPlayEffect = (bool)EffectField.GetValue(raw)
            };
        }

        private static object To(PendingView item)
        {
            if (item == null || ParamType == null) throw new InvalidOperationException("Pending view missing.");
            object raw = Activator.CreateInstance(ParamType);
            RegionXField.SetValue(raw, item.RegionX);
            RegionYField.SetValue(raw, item.RegionY);
            InstanceField.SetValue(raw, item.InstanceId);
            AssetField.SetValue(raw, item.AssetId);
            AmountField.SetValue(raw, item.Amount);
            QualityField.SetValue(raw, item.Quality);
            StateField.SetValue(raw, item.State);
            PointField.SetValue(raw, item.Point);
            SortOrderField.SetValue(raw, item.SortOrder);
            EffectField.SetValue(raw, item.ShouldPlayEffect);
            return raw;
        }
    }

    internal sealed class NativeModelList : IList
    {
        private readonly ItemRegion _region;
        public NativeModelList(ItemRegion region)
        {
            if (region == null || region.drops == null)
                throw new InvalidOperationException("Item region drops missing.");
            _region = region;
        }

        public int Count => _region.drops.Count;
        public bool IsFixedSize => false;
        public bool IsReadOnly => false;
        public bool IsSynchronized => false;
        public object SyncRoot => _region.drops;

        public object this[int index]
        {
            get
            {
                ItemDrop drop = _region.drops[index];
                if (drop == null) throw new InvalidOperationException("Item drop missing.");
                return new ModelView
                {
                    InstanceId = drop.instanceID,
                    Handle = drop.model == null ? null : drop.model.gameObject,
                    NativeDrop = drop
                };
            }
            set => throw new NotSupportedException();
        }

        public void RemoveAt(int index) => _region.drops.RemoveAt(index);
        public int Add(object value) => throw new NotSupportedException();
        public void Insert(int index, object value) => throw new NotSupportedException();
        public void Clear() => _region.drops.Clear();
        public void CopyTo(Array array, int index) => throw new NotSupportedException();
        public bool Contains(object value) => IndexOf(value) >= 0;

        public int IndexOf(object value)
        {
            ModelView target = value as ModelView;
            if (target == null) return -1;
            for (int i = 0; i < _region.drops.Count; i++)
            {
                ItemDrop drop = _region.drops[i];
                if (drop != null && drop.instanceID == target.InstanceId) return i;
            }
            return -1;
        }

        public void Remove(object value) => throw new NotSupportedException();
        public IEnumerator GetEnumerator() => throw new NotSupportedException();
    }
}
