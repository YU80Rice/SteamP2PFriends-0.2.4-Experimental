using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace SteamP2PFriends.Adapters.Item
{
    /// <summary>
    /// 单项消费结果：只有明确未进入副作用阶段才允许重试；已进入但结果未知必须保留隔离，
    /// 不得凭预计源码位置推断“无副作用”。
    /// </summary>
    public enum EConsumeOutcome
    {
        Consumed,
        NotStarted,
        Uncertain
    }

    public readonly struct ConsumeBatchResult
    {
        public ConsumeBatchResult(int consumed, EConsumeOutcome stop, bool budgetExhausted, PendingView stoppedItem)
        {
            Consumed = consumed;
            Stop = stop;
            BudgetExhausted = budgetExhausted;
            StoppedItem = stoppedItem;
        }

        public static ConsumeBatchResult Empty =>
            new ConsumeBatchResult(0, EConsumeOutcome.Consumed, false, null);

        public int Consumed { get; }
        public EConsumeOutcome Stop { get; }
        public bool BudgetExhausted { get; }
        public PendingView StoppedItem { get; }
    }

    public sealed class ItemLocalPresentationPipeline
    {
        private readonly IItemLocalPresentationView _view;

        public ItemLocalPresentationPipeline(IItemLocalPresentationView view)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
        }

        public static void PresentSharedRegion(IItemLocalPresentationView view, byte x, byte y)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            new ItemLocalPresentationPipeline(view).PresentShared(x, y);
        }

        /// <summary>
        /// 无 Host 视图时也能撤销指定实例的待实例化项，避免删除后重入复活。
        /// </summary>
        public static int CancelPendingForInstance(uint instanceId)
        {
            var pending = new NativePendingList();
            int removed = 0;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                PendingView item = pending[i] as PendingView;
                if (item == null || item.InstanceId != instanceId) continue;
                pending.RemoveAt(i);
                removed++;
            }
            return removed;
        }

        /// <summary>E02 §8：存储读取内部重载——按 instanceId 从任意 raw pending 存储撤销。</summary>
        internal static int CancelPendingForInstance(IList rawPending, uint instanceId)
        {
            if (rawPending == null) return 0;
            int removed = 0;
            var paramType = rawPending.Count > 0 ? rawPending[0]?.GetType() : null;
            if (paramType == null) return 0;
            FieldInfo instanceField = paramType.GetField("instanceID",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (instanceField == null) return 0;
            for (int i = rawPending.Count - 1; i >= 0; i--)
            {
                object entry = rawPending[i];
                if (entry == null) continue;
                uint entryId = (uint)(instanceField.GetValue(entry) ?? 0U);
                if (entryId != instanceId) continue;
                rawPending.RemoveAt(i);
                removed++;
            }
            return removed;
        }

        public void PresentShared(byte x, byte y)
        {
            if (!_view.ProjectionAvailable(x, y))
                throw new InvalidOperationException("Host loaded projection unavailable.");
            IReadOnlyList<ItemView> items = _view.ReadCurrentItems(x, y);
            if (items == null) throw new InvalidOperationException("Host current items unavailable.");
            var sortOrders = new float[items.Count];
            for (int i = 0; i < items.Count; i++)
                sortOrders[i] = _view.ItemSortOrder(items[i]);
            CancelPending(x, y);
            DestroyModels(x, y);
            _view.SetNetworked(x, y, true);
            for (int i = 0; i < items.Count; i++)
                InsertPending(ToPending(items[i], x, y, sortOrders[i], false));
            _view.SetLoaded(x, y, true);
        }

        public void Unpresent(byte x, byte y)
        {
            CancelPending(x, y);
            DestroyModels(x, y);
            _view.SetNetworked(x, y, false);
        }

        public void EnqueueIncrement(byte x, byte y, ushort assetId, byte amount, byte quality, byte[] state, Vector3 point, uint instanceId, bool play)
        {
            if (!_view.GetNetworked(x, y)) return;
            var item = new ItemView
            {
                InstanceId = instanceId,
                AssetId = assetId,
                Amount = amount,
                Quality = quality,
                State = state,
                Point = point
            };
            InsertPending(ToPending(item, x, y, _view.ItemSortOrder(item), play));
        }

        public ConsumeBatchResult ConsumePendingBatch(
            Func<bool> budgetStillAvailable, int minActions,
            Func<PendingView, EConsumeOutcome> createModel,
            Func<PendingView, bool> isBlocked = null)
        {
            if (budgetStillAvailable == null) throw new ArgumentNullException(nameof(budgetStillAvailable));
            if (createModel == null) throw new ArgumentNullException(nameof(createModel));
            if (minActions < 1) throw new ArgumentOutOfRangeException(nameof(minActions));
            IList pending = _view.Pending;
            if (pending == null || pending.Count == 0) return ConsumeBatchResult.Empty;
            int consumed = 0;
            int index = 0;
            int scanned = 0;
            int initialCount = pending.Count;
            while (index < pending.Count && scanned < initialCount)
            {
                scanned++;
                PendingView item = pending[index] as PendingView;
                if (item == null) throw new InvalidOperationException("Pending item missing.");
                if (isBlocked != null && isBlocked(item)) { index++; continue; }
                if (consumed >= minActions && !budgetStillAvailable())
                    return new ConsumeBatchResult(consumed, EConsumeOutcome.Consumed, true, item);
                EConsumeOutcome outcome = createModel(item);
                if (outcome != EConsumeOutcome.Consumed)
                    return new ConsumeBatchResult(consumed, outcome, false, item);
                pending.RemoveAt(index);
                consumed++;
            }
            return new ConsumeBatchResult(consumed, EConsumeOutcome.Consumed, false, null);
        }

        public void RemovePresentedInstance(byte x, byte y, uint instanceId, bool play)
        {
            CancelPendingInstance(x, y, instanceId);
            IList models = _view.Models(x, y);
            bool played = false;
            if (models != null)
            {
                for (int i = 0; i < models.Count;)
                {
                    ModelView model = models[i] as ModelView;
                    if (model == null || model.InstanceId != instanceId) { i++; continue; }
                    _view.NotifyRemoved(model);
                    if (play && !played)
                    {
                        _view.PlayRemovalEffect(model);
                        played = true;
                    }
                    _view.DestroyUnity(model.Handle);
                    models.RemoveAt(i);
                }
            }
        }

        public void CancelPendingInstance(byte x, byte y, uint instanceId)
        {
            IList pending = _view.Pending;
            if (pending == null) return;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                PendingView item = pending[i] as PendingView;
                if (item != null && item.RegionX == x && item.RegionY == y && item.InstanceId == instanceId)
                    pending.RemoveAt(i);
            }
        }

        private void CancelPending(byte x, byte y)
        {
            IList pending = _view.Pending;
            if (pending == null) return;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                PendingView item = pending[i] as PendingView;
                if (item != null && item.RegionX == x && item.RegionY == y)
                    pending.RemoveAt(i);
            }
        }

        private void DestroyModels(byte x, byte y)
        {
            IList models = _view.Models(x, y);
            if (models != null)
            {
                for (int i = models.Count - 1; i >= 0; i--)
                {
                    ModelView model = models[i] as ModelView;
                    _view.DestroyUnity(model == null ? null : model.Handle);
                    models.RemoveAt(i);
                }
            }
            _view.FinishDeferredDestroy(x, y);
        }

        private void InsertPending(PendingView item)
        {
            IList pending = _view.Pending;
            int index = pending.Count;
            for (int i = 0; i < pending.Count; i++)
            {
                PendingView existing = pending[i] as PendingView;
                if (existing != null && item.SortOrder < existing.SortOrder)
                {
                    index = i;
                    break;
                }
            }
            pending.Insert(index, item);
        }

        private static PendingView ToPending(ItemView item, byte x, byte y, float sortOrder, bool play)
        {
            return new PendingView
            {
                RegionX = x,
                RegionY = y,
                InstanceId = item.InstanceId,
                AssetId = item.AssetId,
                Amount = item.Amount,
                Quality = item.Quality,
                State = item.State,
                Point = item.Point,
                SortOrder = sortOrder,
                ShouldPlayEffect = play
            };
        }
    }
}
