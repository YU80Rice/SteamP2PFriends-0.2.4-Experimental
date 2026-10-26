using SDG.Unturned;
using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver.Lifecycle;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends.Adapters.Item
{
    public sealed class ItemReturnSettlement
    {
        public const int ActionsPerStep = 1;

        private readonly INativeItemWarehouseBridge _bridge;
        private readonly ItemMetadataStore _store;
        private readonly Func<RegionKey, int> _demand;
        private readonly Func<float> _now;
        private readonly Func<byte> _worldSize;
        private readonly Func<RegionKey, bool> _activated;
        private readonly Func<RegionKey, uint> _generation;
        private readonly Func<ulong> _session;
        private readonly Func<Exception, EDomainFailureKind> _classifyFailure;
        private readonly FailedRetryController _retries;
        private readonly HashSet<RegionKey> _departed = new HashSet<RegionKey>();
        private readonly HashSet<RegionKey> _isolated = new HashSet<RegionKey>();
        private readonly HashSet<RegionKey> _identityBlocked = new HashSet<RegionKey>();
        private readonly ItemFailureRecovery _recovery = new ItemFailureRecovery();
        private readonly ItemObserverRecoveryGate _halfPacketGate = new ItemObserverRecoveryGate();
        private readonly HashSet<UnconfirmedItem> _unconfirmed = new HashSet<UnconfirmedItem>();
        private readonly HashSet<string> _unconfirmedNoticeKeys = new HashSet<string>();
        private readonly HashSet<RegionKey> _worldCommitted = new HashSet<RegionKey>();
        private readonly HashSet<RegionKey> _delivered = new HashSet<RegionKey>();
        private readonly HashSet<DeliveredWaiter> _deliveredWaiters = new HashSet<DeliveredWaiter>();
        private readonly Dictionary<DeliveryWaiterKey, PendingDelivery> _deliveries =
            new Dictionary<DeliveryWaiterKey, PendingDelivery>();
        private readonly List<DeliveryWaiterKey> _deliveryOrder = new List<DeliveryWaiterKey>();
        private readonly Dictionary<RegionKey, List<Waiter>> _pending = new Dictionary<RegionKey, List<Waiter>>();
        private readonly Dictionary<RegionKey, Job> _jobs = new Dictionary<RegionKey, Job>();
        private readonly List<RegionKey> _order = new List<RegionKey>();
        private int _cursor;
        private int _deliveryCursor;

        public ItemReturnSettlement(
            INativeItemWarehouseBridge bridge,
            ItemMetadataStore store,
            Func<RegionKey, int> demand,
            Func<float> now,
            Func<byte> worldSize,
            Func<RegionKey, bool> activated,
            Func<RegionKey, uint> generation,
            Func<ulong> session,
            Func<Exception, EDomainFailureKind> classifyFailure = null,
            FailedRetryController retries = null)
        {
            _bridge = bridge;
            _store = store;
            _demand = demand;
            _now = now;
            _worldSize = worldSize;
            _activated = activated;
            _generation = generation;
            _session = session;
            _classifyFailure = classifyFailure;
            _retries = retries;
        }

        public bool IsBusy(RegionKey region) => _jobs.ContainsKey(region);
        public bool BlocksConsumption(RegionKey region) => IsBusy(region) || _isolated.Contains(region) || _identityBlocked.Contains(region);
        public bool BlocksConsumption(ulong observer, ulong token, RegionKey region) =>
            BlocksConsumption(region) || _halfPacketGate.IsBlocked(observer, token, region)
            || _deliveries.ContainsKey(new DeliveryWaiterKey
                { Session = _session(), Generation = _generation(region), Region = region, Observer = observer, Token = token });

        public void ReleaseHalfPacketForExitedRegion(
            ulong observer, ulong token, RegionKey region)
        {
            _halfPacketGate.ReleaseRegion(observer, token, region);
            _unconfirmedNoticeKeys.Remove(region.X + ":" + region.Y + ":" + observer + ":" + token);
        }

        public bool QuarantineHalfPacket(ulong observer, ulong token, RegionKey region)
        {
            if (!_halfPacketGate.Block(observer, token, region)) return false;
            DiagnosticSink?.Invoke($"event=recovery-notice type=possible-half-packet target={observer} "
                + $"connection={token} region={region} attempt=1 budget={ItemFailureRecovery.MaxRetries}/{ItemFailureRecovery.MaxElapsedSeconds:0}s");
            try { RecoveryExhausted?.Invoke(observer, token, region, true); }
            catch (Exception) { }
            return true;
        }

        /// <summary>
        /// 已进入原生副作用但结果未知：按“区域/实例”持久隔离，代次或会话重建都不解除。
        /// 区域退出释放只清 half-packet 门，不清这里，避免重入后把不确定项当安全项重建。
        /// </summary>
        /// <summary>E02 §8.3：隔离记录补物理作用域——native regions 数组对象引用由实际
        /// adapter 读取（非 Core epoch/测试自增代次）；无 native 来源时 owned 暂缓。</summary>
        public void MarkUnconfirmedSideEffect(ulong observer, ulong token, RegionKey region,
            uint instanceId, object nativeRegionsScope = null)
        {
            _unconfirmed.Add(new UnconfirmedItem
            {
                RegionX = region.X, RegionY = region.Y, Instance = instanceId,
                NativeScope = nativeRegionsScope
            });
            LastFault = $"region={region} instance={instanceId} outcome=unconfirmed reason=local-side-effect";
            string noticeKey = region.X + ":" + region.Y + ":" + observer + ":" + token;
            if (!_unconfirmedNoticeKeys.Add(noticeKey)) return;
            DiagnosticSink?.Invoke($"event=recovery-notice type=result-unconfirmed target={observer} "
                + $"connection={token} region={region} instance={instanceId} attempt=1 "
                + $"budget={ItemFailureRecovery.MaxRetries}/{ItemFailureRecovery.MaxElapsedSeconds:0}s");
            try { RecoveryUnconfirmed?.Invoke(observer, token, region); }
            catch (Exception) { }
        }

        public bool HasUnconfirmedSideEffect(ulong observer, ulong token, uint instanceId)
        {
            foreach (UnconfirmedItem item in _unconfirmed)
            if (item.Instance == instanceId) return true;
            return false;
        }

        /// <summary>E02 §8.3：按现有结构完整 key（region+instance）与物理 scope 匹配查询。
        /// 语义：query scope=null（owned 无 native 来源）→ 暂缓（返回 true，不做万能匹配）；
        /// 记录 scope=null（legacy 旧 Mark）→ 按 region/instance 命中；两者非 null →
        /// 必须同数组引用才命中。</summary>
        public bool HasUnconfirmedSideEffect(RegionKey region, uint instanceId,
            object nativeRegionsScope = null)
        {
            if (nativeRegionsScope == null) return true;
            foreach (UnconfirmedItem item in _unconfirmed)
                if (item.Instance == instanceId && item.RegionX == region.X
                    && item.RegionY == region.Y
                    && (item.NativeScope == null
                        || ReferenceEquals(item.NativeScope, nativeRegionsScope)))
                    return true;
            return false;
        }

        public int UnconfirmedSideEffectCount => _unconfirmed.Count;

        public void EnsureForDemand(RegionKey region)
        {
            if (_isolated.Contains(region) || _identityBlocked.Contains(region)) return;
            if (_activated(region) && _demand(region) > 0 && _departed.Contains(region)
                && !_jobs.ContainsKey(region) && !WorldResultStillValid(region))
            {
                _jobs[region] = new Job { Session = _session(), Generation = _generation(region) };
                _order.Add(region);
            }
            if (_jobs.TryGetValue(region, out Job job) && _pending.TryGetValue(region, out List<Waiter> pending))
            {
                job.Waiters.AddRange(pending);
                _pending.Remove(region);
            }
        }
        public bool BlocksWarehouse(RegionKey region) => IsBusy(region) || _isolated.Contains(region) || _identityBlocked.Contains(region);
        public bool IsIsolated(RegionKey region) => _isolated.Contains(region);
        public bool IsReturnPending(RegionKey region) => _departed.Contains(region);
        public bool WasDeliveredFor(RegionKey region, ulong observer, ulong token)
        {
            return _deliveredWaiters.Contains(new DeliveredWaiter
            {
                Region = region, Observer = observer, Token = token
            });
        }
        public bool WasDelivered(RegionKey region) => _delivered.Contains(region);
        public bool NeedsSettlement(RegionKey region) => _activated(region) && _departed.Contains(region)
            && !_isolated.Contains(region) && !_identityBlocked.Contains(region)
            && _demand(region) > 0
            && (_jobs.ContainsKey(region) || _pending.ContainsKey(region));
        public int ActiveJobCount => _jobs.Count;
        public Action<ulong, ulong, RegionKey, bool> Deliver { get; set; }
        public Func<ulong, ulong, bool> ConnectionCurrent { get; set; }
        public string LastFault { get; private set; }
        public Action<ulong, ulong, RegionKey, bool> RecoveryExhausted { get; set; }
        public Action<string> DiagnosticSink { get; set; }
        public Action<ulong, ulong, RegionKey, Exception> ObserverDeliveryFailure { get; set; }
        internal Func<ulong, ulong, RegionKey, ItemGuestAdmission> DeliveryAdmission { get; set; }
        public Action<ulong, ulong, RegionKey> ObserverFailureChainCompleted { get; set; }
        public Action<ulong, ulong> ObserverDisconnected { get; set; }

        public bool Hold(RegionKey region, ulong observer, ulong token, bool host)
        {
            if (_isolated.Contains(region)) return true;
            if (_deliveries.ContainsKey(new DeliveryWaiterKey
                { Session = _session(), Generation = _generation(region), Region = region, Observer = observer, Token = token })) return true;
            if (!_jobs.TryGetValue(region, out Job job))
            {
                if (!_departed.Contains(region)) return false;
                _delivered.Remove(region);
                if (!_pending.TryGetValue(region, out List<Waiter> pending))
                    _pending[region] = pending = new List<Waiter>();
                pending.RemoveAll(waiter => waiter.Observer == observer);
                pending.Add(new Waiter { Observer = observer, Token = token, Host = host });
                return true;
            }
            for (int i = 0; i < job.Waiters.Count; i++)
            {
                if (job.Waiters[i].Observer != observer) continue;
                if (job.Waiters[i].Token == token) return true;
                job.Waiters.RemoveAt(i);
                break;
            }
            job.Waiters.Add(new Waiter { Observer = observer, Token = token, Host = host });
            return true;
        }

        public void CommitPending()
        {
            var regions = new List<RegionKey>(_pending.Keys);
            for (int i = 0; i < regions.Count; i++)
            {
                RegionKey region = regions[i];
                if (_demand(region) <= 0) continue;
                if (_isolated.Contains(region) || WorldResultStillValid(region))
                {
                    _pending.Remove(region);
                    continue;
                }
                if (!_jobs.ContainsKey(region))
                {
                    _jobs[region] = new Job { Session = _session(), Generation = _generation(region) };
                    _order.Add(region);
                }
                for (int w = 0; w < _pending[region].Count; w++)
                {
                    Waiter waiter = _pending[region][w];
                    _jobs[region].Waiters.RemoveAll(existing => existing.Observer == waiter.Observer);
                    _jobs[region].Waiters.Add(waiter);
                }
                _pending.Remove(region);
            }
        }

        public void RegisterPending(RegionKey region, ulong observer, ulong token, bool host)
        {
            if (!_activated(region) || !_departed.Contains(region)
                || _isolated.Contains(region) || _identityBlocked.Contains(region)) return;
            if (_jobs.TryGetValue(region, out Job job))
            {
                _pending.Remove(region);
                job.Waiters.RemoveAll(waiter => waiter.Observer == observer);
                job.Waiters.Add(new Waiter { Observer = observer, Token = token, Host = host });
                return;
            }
            if (!_pending.TryGetValue(region, out List<Waiter> pending))
                _pending[region] = pending = new List<Waiter>();
            pending.RemoveAll(waiter => waiter.Observer == observer);
            pending.Add(new Waiter { Observer = observer, Token = token, Host = host });
        }

        public void ReleaseObserver(ulong observer, ulong token)
        {
            _recovery.RemoveWaiter(observer, token);
            _halfPacketGate.ReleaseObserver(observer, token);
            var deliveryKeys = new List<DeliveryWaiterKey>(_deliveryOrder);
            for (int i = 0; i < deliveryKeys.Count; i++)
                if (deliveryKeys[i].Observer == observer
                    && IsReleaseTokenMatch(deliveryKeys[i].Token, token))
                {
                    ObserverFailureChainCompleted?.Invoke(observer, token, deliveryKeys[i].Region);
                    RemoveDelivery(deliveryKeys[i]);
                }
            ObserverDisconnected?.Invoke(observer, token);
            string noticeSuffix = ":" + observer + ":" + token;
            _unconfirmedNoticeKeys.RemoveWhere(key => key.EndsWith(noticeSuffix, StringComparison.Ordinal));
            foreach (List<Waiter> pending in _pending.Values)
                pending.RemoveAll(waiter => waiter.Observer == observer
                    && IsReleaseTokenMatch(waiter.Token, token));
            var empty = new List<RegionKey>();
            foreach (var pair in _pending)
                if (pair.Value.Count == 0) empty.Add(pair.Key);
            for (int i = 0; i < empty.Count; i++) _pending.Remove(empty[i]);
            for (int i = 0; i < _order.Count; i++)
                if (_jobs.TryGetValue(_order[i], out Job job))
                    job.Waiters.RemoveAll(waiter => waiter.Observer == observer
                        && IsReleaseTokenMatch(waiter.Token, token));
        }

        internal static bool IsReleaseTokenMatch(ulong waiterToken, ulong releasedToken) =>
            waiterToken != 0UL && waiterToken == releasedToken;

        internal static bool TryCountConfirmedItems(
            IReadOnlyList<ItemData> items, out int count)
        {
            if (items == null)
            {
                count = 0;
                return false;
            }
            count = items.Count;
            return true;
        }

        internal static bool TryValidateManualRecoveryToken(
            ulong requestedToken, Func<ulong, bool> current,
            out ulong normalizedToken)
        {
            normalizedToken = 0UL;
            if (requestedToken == 0UL || current != null && !current(requestedToken)) return false;
            normalizedToken = requestedToken;
            return true;
        }

        public EItemRecoveryRequestResult RequestManualRecovery(ulong observer, ulong token, RegionKey region)
        {
            if (!TryValidateManualRecoveryToken(token,
                value => ConnectionCurrent == null || ConnectionCurrent(observer, value),
                out token))
                return EItemRecoveryRequestResult.StaleConnection;
            if (!_activated(region) || _demand(region) <= 0 || _identityBlocked.Contains(region))
                return EItemRecoveryRequestResult.NotEligible;
            if (CallerCoversRegion != null && !CallerCoversRegion(observer, token, region))
                return EItemRecoveryRequestResult.NotEligible;
            var deliveryKey = new DeliveryWaiterKey
                { Session = _session(), Generation = _generation(region), Region = region, Observer = observer, Token = token };
            if (_deliveries.TryGetValue(deliveryKey, out PendingDelivery delivery))
            {
                if (_retries == null || !_retries.RequestManual(delivery.RetryKey, _now()))
                    return EItemRecoveryRequestResult.NotEligible;
                delivery.AttemptId++;
                delivery.ManualInitialPending = true;
                delivery.ExhaustionNotified = false;
                ObserverFailureChainCompleted?.Invoke(observer, token, region);
                return EItemRecoveryRequestResult.Started;
            }
            if (_retries != null && _jobs.TryGetValue(region, out Job paused) && paused.Paused)
            {
                if (paused.Unavailable) return EItemRecoveryRequestResult.NotEligible;
                if (!_retries.RequestManual(WorkKey(region, paused), _now()))
                    return EItemRecoveryRequestResult.NotEligible;
                paused.NoticeChain++;
                paused.FirstFailureLogged = false;
                paused.Paused = false;
                paused.Unavailable = false;
                _isolated.Remove(region);
                if (!_order.Contains(region)) _order.Add(region);
                return EItemRecoveryRequestResult.Started;
            }
            if (_retries != null) return EItemRecoveryRequestResult.NotEligible;
            return _recovery.RequestManual(region, observer, token, _now(), ConnectionCurrent);
        }

        public void ResetIdentity()
        {
            var deliveryKeys = new List<DeliveryWaiterKey>(_deliveryOrder);
            for (int i = 0; i < deliveryKeys.Count; i++) RemoveDelivery(deliveryKeys[i]);
            _jobs.Clear();
            _order.Clear();
            _departed.Clear();
            _isolated.Clear();
            _identityBlocked.Clear();
            _delivered.Clear();
            _deliveredWaiters.Clear();
            _deliveryOrder.Clear();
            _deliveryCursor = 0;
            _deliveries.Clear();
            _pending.Clear();
            _recovery.Clear();
            _halfPacketGate.Clear();
            _unconfirmedNoticeKeys.Clear();
        }

        public void ReleaseWaiter(RegionKey region, ulong observer, ulong token)
        {
            _recovery.RemoveWaiter(region, observer, token);
            if (_pending.TryGetValue(region, out List<Waiter> pending))
            {
                pending.RemoveAll(waiter => waiter.Observer == observer
                    && IsReleaseTokenMatch(waiter.Token, token));
                if (pending.Count == 0) _pending.Remove(region);
            }
            if (_jobs.TryGetValue(region, out Job job))
                job.Waiters.RemoveAll(waiter => waiter.Observer == observer
                    && IsReleaseTokenMatch(waiter.Token, token));
        }

        public void ReleaseExitedRegion(RegionKey region, ulong observer, ulong token)
        {
            ReleaseWaiter(region, observer, token);
            var deliveryKeys = new List<DeliveryWaiterKey>(_deliveryOrder);
            for (int i = 0; i < deliveryKeys.Count; i++)
                if (deliveryKeys[i].Region == region && deliveryKeys[i].Observer == observer
                    && IsReleaseTokenMatch(deliveryKeys[i].Token, token)) RemoveDelivery(deliveryKeys[i]);
            ReleaseHalfPacketForExitedRegion(observer, token, region);
            ObserverFailureChainCompleted?.Invoke(observer, token, region);
        }

        public void Refresh()
        {
            byte size = _worldSize();
            for (byte x = 0; x < size; x++)
            {
                for (byte y = 0; y < size; y++)
                {
                    var region = new RegionKey(x, y);
                    if (!_activated(region) && !IsBusy(region)) continue;
                    Note(region, _demand(region));
                }
            }
        }

        public void Note(RegionKey region, int demand)
        {
            if (!_activated(region)) return;
            if (_isolated.Contains(region))
            {
                if (demand <= 0) _departed.Add(region);
                else if (_departed.Contains(region) && !_jobs.ContainsKey(region)
                    && !_recovery.IsIsolated(region))
                {
                    _isolated.Remove(region);
                    _identityBlocked.Remove(region);
                }
                else return;
            }
            if (demand <= 0)
            {
                _identityBlocked.Remove(region);
                _departed.Add(region);
                return;
            }
            if (_identityBlocked.Contains(region) || !_departed.Contains(region) || _jobs.ContainsKey(region)) return;
            if (WorldResultStillValid(region)) return;
            _jobs[region] = new Job { Session = _session(), Generation = _generation(region) };
            _order.Add(region);
        }

        private bool WorldResultStillValid(RegionKey region)
        {
            if (!_worldCommitted.Contains(region)) return false;
            ItemCycleThresholds thresholds = ItemCycleThresholds.Resolve();
            if (thresholds != null
                && _now() - _bridge.ReadLastRespawn((byte)region.X, (byte)region.Y) > thresholds.RespawnTime)
            {
                _worldCommitted.Remove(region);
                return false;
            }
            return true;
        }

        public int Consume(int budget)
        {
            if (budget <= 0) return 0;
            PrepareDueRecoveries();
            int used = ConsumeDeliveries(budget);
            if (used >= budget || _order.Count == 0) return used;
            ItemCycleThresholds thresholds = ItemCycleThresholds.Resolve();
            if (thresholds == null || !thresholds.NativeCyclePermitted) return used;
            int examined = 0;
            while (used < budget && examined < _order.Count)
            {
                RegionKey region = _order[_cursor % _order.Count];
                _cursor++;
                examined++;
                if (!_jobs.TryGetValue(region, out Job job)) continue;
                if (job.Session != _session() || _generation(region) != job.Generation)
                {
                    Drop(region);
                    _recovery.Invalidate(region);
                    _departed.Remove(region);
                    _identityBlocked.Add(region);
                    LastFault = $"region={region} outcome=rejected reason=identity-invalidated";
                    continue;
                }
                if (job.Clearing)
                {
                    if (RemoveOne(region, thresholds)) { used++; continue; }
                    if (HasExpiredNatural(region, thresholds)) continue;
                    job.Clearing = false;
                    if (!_bridge.AreNaturalInputsAvailable((byte)region.X, (byte)region.Y,
                        out string inputFailure))
                    {
                        _isolated.Add(region);
                        Drop(region);
                        LastFault = $"region={region} outcome=unavailable reason={inputFailure}";
                        return used;
                    }
                    try { job.RestockBudget = Shortage(region, thresholds); }
                    catch (Exception ex)
                    {
                        HandleFailure(region, ex, "spawn-point-read-exception", job);
                        return used;
                    }
                }
                bool complete;
                try
                {
                    complete = job.RestockBudget <= 0 || job.RestockUsed >= job.RestockBudget
                        || Count(region) >= Desired(region, thresholds);
                }
                catch (Exception ex)
                {
                    HandleFailure(region, ex, "region-items-read-exception", job);
                    return used;
                }
                if (complete)
                {
                    Finish(region, job, true);
                    continue;
                }
                ItemNaturalCreationOutcome outcome;
                try { outcome = _bridge.CreateOneNatural((byte)region.X, (byte)region.Y, _now()); }
                catch (Exception ex)
                {
                    HandleFailure(region, ex, "natural-create-exception", job);
                    return used;
                }
                if (outcome == ItemNaturalCreationOutcome.MissingInputs
                    || outcome == ItemNaturalCreationOutcome.MissingAsset)
                {
                    _isolated.Add(region);
                    Drop(region);
                    string reason = outcome == ItemNaturalCreationOutcome.MissingAsset
                        ? "missing-natural-asset" : "missing-natural-inputs";
                    LastFault = $"region={region} outcome=unavailable reason={reason}";
                    return used;
                }
                job.RestockUsed++;
                if (outcome == ItemNaturalCreationOutcome.Created)
                    job.RestockSucceeded = true;
                AdoptNew(region, _now());
                used++;
            }
            return used;
        }

        private int ConsumeDeliveries(int budget)
        {
            if (_retries == null || _deliveryOrder.Count == 0) return 0;
            int used = 0;
            int examined = 0;
            while (used < budget && examined < _deliveryOrder.Count)
            {
                DeliveryWaiterKey key = _deliveryOrder[_deliveryCursor % _deliveryOrder.Count];
                _deliveryCursor++;
                examined++;
                if (!_deliveries.TryGetValue(key, out PendingDelivery delivery)) continue;
                if (!DeliveryIdentityCurrent(key, delivery))
                {
                    ObserverFailureChainCompleted?.Invoke(key.Observer, key.Token, key.Region);
                    RemoveDelivery(key);
                    continue;
                }
                if (!delivery.Host && DeliveryAdmission != null)
                {
                    ItemGuestAdmission admission = DeliveryAdmission(key.Observer, key.Token, key.Region);
                    if (admission == ItemGuestAdmission.Waiting)
                    {
                        delivery.AwaitingAdmission = true;
                        _retries.RefreshExhaustion(delivery.RetryKey, _now());
                        continue;
                    }
                    if (admission == ItemGuestAdmission.Rejected)
                    {
                        RemoveDelivery(key);
                        continue;
                    }
                }
                bool manualInitial = delivery.ManualInitialPending;
                bool admissionOnlyWait = delivery.AwaitingAdmission
                    && delivery.AttemptId == 0 && !manualInitial;
                if (!manualInitial && !admissionOnlyWait
                    && !_retries.TryClaimAttempt(delivery.RetryKey, _now()))
                {
                    NotifyDeliveryExhausted(key, delivery);
                    continue;
                }
                delivery.AwaitingAdmission = false;
                delivery.ManualInitialPending = false;
                used++;
                try { Deliver?.Invoke(key.Observer, key.Token, key.Region, delivery.Host); }
                catch (Exception ex) { HandleDeliveryFailure(key, delivery, ex, manualInitial); continue; }
                CompleteDelivery(key, delivery);
            }
            return used;
        }

        private bool DeliveryIdentityCurrent(DeliveryWaiterKey key, PendingDelivery delivery) =>
            delivery.Session == _session() && delivery.Generation == _generation(key.Region)
            && _activated(key.Region) && _demand(key.Region) > 0
            && !_identityBlocked.Contains(key.Region)
            && (ConnectionCurrent == null || ConnectionCurrent(key.Observer, key.Token))
            && (CallerCoversRegion == null || CallerCoversRegion(key.Observer, key.Token, key.Region));

        private void NotifyDeliveryExhausted(DeliveryWaiterKey key, PendingDelivery delivery)
        {
            if (delivery.ExhaustionNotified || !_retries.IsExhausted(delivery.RetryKey)) return;
            delivery.ExhaustionNotified = true;
            LastFault = $"region={key.Region} observer={key.Observer} outcome=delivery-retries-exhausted";
            DiagnosticSink?.Invoke($"event=delivery-recovery-exhausted target={key.Observer}/{key.Token} region={key.Region} "
                + $"job={delivery.Session}:{delivery.Generation}:{delivery.NoticeChain} attempt={delivery.AttemptId} "
                + $"budget={ItemFailureRecovery.MaxRetries}/{ItemFailureRecovery.MaxElapsedSeconds:0}s");
            try { RecoveryExhausted?.Invoke(key.Observer, key.Token, key.Region, false); }
            catch (Exception) { }
        }

        private void HandleDeliveryFailure(DeliveryWaiterKey key, PendingDelivery delivery,
            Exception exception, bool manualInitial)
        {
            bool safeToRetry = exception is ItemObserverReplicationFailureException failure
                && failure.SafeToRetry;
            ObserverDeliveryFailure?.Invoke(key.Observer, key.Token, key.Region, exception);
            if (safeToRetry)
            {
                if (manualInitial)
                {
                    _retries.Complete(delivery.RetryKey);
                    _retries.ReportActualFault(delivery.RetryKey, ++delivery.AttemptId, _now());
                }
                else
                {
                    _retries.RetryFailed(delivery.RetryKey, _now());
                    _retries.ReportActualFault(delivery.RetryKey, ++delivery.AttemptId, _now());
                }
                LastFault = $"region={key.Region} observer={key.Observer} outcome=confirmed-not-sent";
                NotifyDeliveryExhausted(key, delivery);
                return;
            }
            LastFault = $"region={key.Region} observer={key.Observer} outcome=delivery-result-unknown";
            if (ObserverDeliveryFailure == null) QuarantineHalfPacket(key.Observer, key.Token, key.Region);
            RemoveDelivery(key);
        }

        private void CompleteDelivery(DeliveryWaiterKey key, PendingDelivery delivery)
        {
            _retries.Complete(delivery.RetryKey);
            RemoveDelivery(key);
            _deliveredWaiters.Add(new DeliveredWaiter
                { Region = key.Region, Observer = key.Observer, Token = key.Token });
            _delivered.Add(key.Region);
            ObserverFailureChainCompleted?.Invoke(key.Observer, key.Token, key.Region);
        }

        private void QueueDeliveryAdmissionWait(Waiter waiter, RegionKey region, Job job)
        {
            var key = new DeliveryWaiterKey
            {
                Session = job.Session, Generation = job.Generation, Region = region,
                Observer = waiter.Observer, Token = waiter.Token
            };
            if (_deliveries.TryGetValue(key, out PendingDelivery existing))
            {
                existing.AwaitingAdmission = true;
                return;
            }
            var delivery = new PendingDelivery
            {
                Session = job.Session, Generation = job.Generation, NoticeChain = job.NoticeChain,
                SourceAttemptId = job.AttemptId, Host = waiter.Host, AttemptId = 0,
                AwaitingAdmission = true
            };
            delivery.RetryKey = DeliveryWorkKey(key, delivery);
            if (_retries == null) return;
            _deliveries.Add(key, delivery);
            _deliveryOrder.Add(key);
        }

        private void QueueDeliveryRetry(Waiter waiter, RegionKey region, Job job, Exception exception)
        {
            var key = new DeliveryWaiterKey
                { Session = job.Session, Generation = job.Generation, Region = region,
                    Observer = waiter.Observer, Token = waiter.Token };
            if (_deliveries.ContainsKey(key)) return;
            var delivery = new PendingDelivery
            {
                Session = job.Session, Generation = job.Generation, NoticeChain = job.NoticeChain,
                SourceAttemptId = job.AttemptId, Host = waiter.Host, AttemptId = 1
            };
            delivery.RetryKey = DeliveryWorkKey(key, delivery);
            if (_retries == null)
            {
                try { RecoveryExhausted?.Invoke(waiter.Observer, waiter.Token, region, false); }
                catch (Exception) { }
                return;
            }
            _deliveries.Add(key, delivery);
            _deliveryOrder.Add(key);
            _retries.ReportActualFault(delivery.RetryKey, delivery.AttemptId, _now());
            DiagnosticSink?.Invoke($"event=delivery-confirmed-not-sent region={region} job={job.Session}:{job.Generation}:{job.NoticeChain} "
                + $"target={waiter.Observer}/{waiter.Token} sourceAttempt={delivery.SourceAttemptId} attempt={delivery.AttemptId} "
                + $"budget={ItemFailureRecovery.MaxRetries}/{ItemFailureRecovery.MaxElapsedSeconds:0}s "
                + $"exception={exception} stack={exception?.StackTrace ?? "unavailable"}");
            ObserverDeliveryFailure?.Invoke(waiter.Observer, waiter.Token, region, exception);
        }

        private static string WorkKey(RegionKey region, Job job) =>
            "item-settlement:" + job.Session + ":" + region.X + ":" + region.Y + ":" + job.Generation;

        private static string DeliveryWorkKey(DeliveryWaiterKey key, PendingDelivery delivery) =>
            "item-delivery:" + delivery.Session + ":" + key.Region.X + ":" + key.Region.Y
            + ":" + delivery.Generation + ":" + key.Observer + ":" + key.Token;

        private void RemoveDelivery(DeliveryWaiterKey key)
        {
            if (!_deliveries.TryGetValue(key, out PendingDelivery delivery)) return;
            if (_retries != null) _retries.Revoke(delivery.RetryKey);
            _deliveries.Remove(key);
            _deliveryOrder.Remove(key);
            if (_deliveryCursor >= _deliveryOrder.Count) _deliveryCursor = 0;
        }

        private void ResumePausedJobs()
        {
            if (_retries == null) return;
            var regions = new List<RegionKey>(_jobs.Keys);
            for (int i = 0; i < regions.Count; i++)
            {
                Job job = _jobs[regions[i]];
                if (!job.Paused) continue;
                if (job.Unavailable) { PollExhaustion(regions[i], job); continue; }
                string key = WorkKey(regions[i], job);
                if (job.Session != _session() || _generation(regions[i]) != job.Generation)
                {
                    _retries.Revoke(key);
                    continue;
                }
                if (_demand(regions[i]) <= 0 || !_retries.TryClaimAttempt(key, _now()))
                {
                    PollExhaustion(regions[i], job);
                    continue;
                }
                job.Paused = false;
                job.AttemptId++;
                _isolated.Remove(regions[i]);
                if (!_order.Contains(regions[i])) _order.Add(regions[i]);
            }
        }

        private void PrepareDueRecoveries()
        {
            ResumePausedJobs();
            IReadOnlyList<RegionKey> regions = _recovery.SnapshotRegions();
            for (int i = 0; i < regions.Count; i++)
            {
                RegionKey region = regions[i];
                if (_identityBlocked.Contains(region))
                {
                    _recovery.Invalidate(region);
                    continue;
                }
                bool exhaustedNow;
                if (!_recovery.TryBeginAutomatic(region, _now(), out exhaustedNow))
                {
                    if (exhaustedNow) NotifyRecoveryExhausted(region, possibleHalfPacket: false);
                    continue;
                }
                if (_demand(region) <= 0)
                {
                    _recovery.Complete(region);
                    _isolated.Remove(region);
                    continue;
                }
                if (_jobs.ContainsKey(region)) continue;
                var job = new Job { Session = _session(), Generation = _generation(region), Clearing = true };
                if (_recovery.TryReadProgress(region, out bool clearing, out int budget,
                    out int used, out bool succeeded))
                {
                    job.Clearing = clearing;
                    job.RestockBudget = budget;
                    job.RestockUsed = used;
                    job.RestockSucceeded = succeeded;
                }
                if (_recovery.TryGetWaiters(region, out IReadOnlyList<ItemFailureRecovery.Waiter> waiters))
                    for (int w = 0; w < waiters.Count; w++)
                        job.Waiters.Add(new Waiter { Observer = waiters[w].ObserverId,
                            Token = waiters[w].ConnectionToken, Host = waiters[w].Host });
                _isolated.Remove(region);
                _jobs[region] = job;
                _order.Add(region);
            }
        }

        public Func<ulong, ulong, RegionKey, bool> CallerCoversRegion { get; set; }
        public Action<ulong, ulong, RegionKey> RecoveryUnconfirmed { get; set; }

        private void PollExhaustion(RegionKey region, Job job)
        {
            if (job == null || job.Session != _session() || _generation(region) != job.Generation) return;
            if (job.Unavailable) { NotifyWaiters(region, job, true); return; }
            if (_retries == null || !_retries.IsExhausted(WorkKey(region, job))) return;
            LastFault = "exhausted-waiters=" + job.Waiters.Count;
            NotifyWaiters(region, job, false);
        }

        private void NotifyWaiters(RegionKey region, Job job, bool unconfirmed)
        {
            for (int i = 0; i < job.Waiters.Count; i++)
            {
                Waiter waiter = job.Waiters[i];
                if (ConnectionCurrent != null && !ConnectionCurrent(waiter.Observer, waiter.Token)) continue;
                if (CallerCoversRegion != null && !CallerCoversRegion(waiter.Observer, waiter.Token, region)) continue;
                string mark = job.NoticeChain + ":" + waiter.Observer + ":" + waiter.Token + ":" + unconfirmed;
                if (!job.Notified.Add(mark)) continue;
                DiagnosticSink?.Invoke($"event=recovery-notice type={(unconfirmed ? "result-unconfirmed" : "recovery-exhausted")} "
                    + $"target={waiter.Observer} connection={waiter.Token} region={region} job={job.Session}:{job.Generation}:{job.NoticeChain} "
                    + $"attempt={job.AttemptId} budget={ItemFailureRecovery.MaxRetries}/{ItemFailureRecovery.MaxElapsedSeconds:0}s");
                try
                {
                    if (unconfirmed) RecoveryUnconfirmed?.Invoke(waiter.Observer, waiter.Token, region);
                    else RecoveryExhausted?.Invoke(waiter.Observer, waiter.Token, region, false);
                }
                catch (Exception) { }
            }
        }

        private void NotifyRecoveryExhausted(RegionKey region, bool possibleHalfPacket)
        {
            if (RecoveryExhausted == null) return;
            if (!_recovery.TryGetWaiters(region, out IReadOnlyList<ItemFailureRecovery.Waiter> waiters)) return;
            for (int i = 0; i < waiters.Count; i++)
                try { RecoveryExhausted(waiters[i].ObserverId, waiters[i].ConnectionToken,
                    region, possibleHalfPacket); }
                catch (Exception) { }
        }

        private void HandleFailure(RegionKey region, Exception exception, string reason, Job failedJob)
        {
            EDomainFailureKind kind = _classifyFailure == null
                ? EDomainFailureKind.Failed
                : _classifyFailure(exception);
            if (kind != EDomainFailureKind.Failed)
                Drop(region);
            else
            {
                if (failedJob != null && !failedJob.FirstFailureLogged)
                {
                    failedJob.FirstFailureLogged = true;
                    string target = failedJob.Waiters.Count == 0 ? "none" :
                        failedJob.Waiters[0].Observer + "/" + failedJob.Waiters[0].Token;
                    DiagnosticSink?.Invoke($"event=settlement-first-failure region={region} job={failedJob.Session}:{failedJob.Generation}:{failedJob.NoticeChain} "
                        + $"target={target} attempt={failedJob.AttemptId} budget={ItemFailureRecovery.MaxRetries}/{ItemFailureRecovery.MaxElapsedSeconds:0}s "
                        + $"exception={exception} stack={exception?.StackTrace ?? "unavailable"}");
                }
                bool unconfirmed = reason == "region-items-read-exception"
                    || exception.Message == "region-items-unavailable";
                _isolated.Add(region);
                if (_retries != null && failedJob != null)
                {
                    _order.Remove(region);
                    failedJob.Paused = true;
                    failedJob.Unavailable = unconfirmed;
                    if (!unconfirmed)
                    {
                        _retries.RetryFailed(WorkKey(region, failedJob), _now());
                        _retries.ReportActualFault(WorkKey(region, failedJob), failedJob.AttemptId, _now());
                    }
                    PollExhaustion(region, failedJob);
                }
                else Drop(region);
                var waiters = new List<ItemFailureRecovery.Waiter>();
                if (failedJob != null)
                    for (int i = 0; i < failedJob.Waiters.Count; i++)
                    {
                        Waiter waiter = failedJob.Waiters[i];
                        waiters.Add(new ItemFailureRecovery.Waiter(
                            waiter.Observer, waiter.Token, waiter.Host));
                    }
                bool scheduled = false;
                bool exhaustedNow = false;
                if (unconfirmed) _recovery.HoldUnconfirmed(region, _now());
                else if (_retries == null)
                    scheduled = false;
                _recovery.SetWaiters(region, waiters);
                if (failedJob != null)
                    _recovery.RememberProgress(region, failedJob.Clearing, failedJob.RestockBudget,
                        failedJob.RestockUsed, failedJob.RestockSucceeded);
                if (!scheduled && exhaustedNow) NotifyRecoveryExhausted(region, possibleHalfPacket: false);
            }
            LastFault = $"region={region} outcome={kind.ToString().ToLowerInvariant()} reason={reason} type={exception.GetType().Name}";
        }

        private bool RemoveOne(RegionKey region, ItemCycleThresholds thresholds)
        {
            byte x = (byte)region.X;
            byte y = (byte)region.Y;
            IReadOnlyList<ItemData> items = _bridge.GetRegionItems(x, y);
            if (items == null) return false;
            float now = _now();
            for (int i = 0; i < items.Count; i++)
            {
                ItemData item = items[i];
                if (item == null || item.instanceID == 0U) continue;
                if (!_store.TryGetMetadata(item.instanceID, out ItemMetadataRecord record)
                    || record.Origin != ItemOriginKind.Natural) continue;
                if (now - item.lastDropped <= thresholds.DespawnNaturalTime) continue;
                if (!_bridge.TryRemoveItem(x, y, item.instanceID)) continue;
                _store.RemoveItem(item.instanceID);
                return true;
            }
            return false;
        }

        private bool HasExpiredNatural(RegionKey region, ItemCycleThresholds thresholds)
        {
            IReadOnlyList<ItemData> items = _bridge.GetRegionItems((byte)region.X, (byte)region.Y);
            if (items == null) return false;
            for (int i = 0; i < items.Count; i++)
            {
                ItemData item = items[i];
                if (item == null || !_store.TryGetMetadata(item.instanceID, out ItemMetadataRecord record)) continue;
                if (record.Origin == ItemOriginKind.Natural && _now() - item.lastDropped > thresholds.DespawnNaturalTime)
                    return true;
            }
            return false;
        }

        private int Shortage(RegionKey region, ItemCycleThresholds thresholds)
        {
            int desired = Desired(region, thresholds);
            int current = Count(region);
            return desired > current ? desired - current : 0;
        }

        private int Desired(RegionKey region, ItemCycleThresholds thresholds)
        {
            int points = _bridge.CountSpawnPoints((byte)region.X, (byte)region.Y);
            if (points <= 0) return 0;
            if (_now() - _bridge.ReadLastRespawn((byte)region.X, (byte)region.Y) <= thresholds.RespawnTime) return Count(region);
            return (int)(points * thresholds.SpawnChance);
        }

        private int Count(RegionKey region)
        {
            IReadOnlyList<ItemData> items = _bridge.GetRegionItems((byte)region.X, (byte)region.Y);
            if (!TryCountConfirmedItems(items, out int count))
                throw new InvalidOperationException("region-items-unavailable");
            return count;
        }

        private void AdoptNew(RegionKey region, float now)
        {
            IReadOnlyList<ItemData> items = _bridge.GetRegionItems((byte)region.X, (byte)region.Y);
            if (items == null) return;
            for (int i = 0; i < items.Count; i++)
            {
                ItemData item = items[i];
                if (item == null || item.instanceID == 0U) continue;
                if (_store.TryGetMetadata(item.instanceID, out _)) continue;
                if (ItemOriginTrackingSeam.IsTrackedDropped(item.instanceID))
                    _store.RegisterItem(item.instanceID, region, ItemOriginKind.Dropped, item.lastDropped, item.lastDropped);
                else
                {
                    ItemOriginTrackingSeam.NotifyNaturalItem(item.instanceID);
                    _store.RegisterItem(item.instanceID, region, ItemOriginKind.Natural, now, now);
                }
            }
        }

        private void Finish(RegionKey region, Job job, bool deliver)
        {
            if (job.RestockSucceeded)
                _bridge.CommitRespawnTime((byte)region.X, (byte)region.Y, _now());
            _worldCommitted.Add(region);
            if (_retries != null) _retries.Complete(WorkKey(region, job));
            var waiters = job.Waiters;
            bool send = deliver && _demand(region) > 0;
            Drop(region);
            _recovery.Complete(region);
            _isolated.Remove(region);
            LastFault = null;
            if (!send) return;
            _departed.Remove(region);
            bool delivered = false;
            for (int i = 0; i < waiters.Count; i++)
            {
                Waiter waiter = waiters[i];
                if (ConnectionCurrent != null && !ConnectionCurrent(waiter.Observer, waiter.Token)) continue;
                try
                {
                    Deliver?.Invoke(waiter.Observer, waiter.Token, region, waiter.Host);
                }
                catch (Exception ex)
                {
                    if (ex is ItemGuestBindingAdmissionException admissionFailure)
                    {
                        if (admissionFailure.Admission == ItemGuestAdmission.Waiting)
                            QueueDeliveryAdmissionWait(waiter, region, job);
                        else if (admissionFailure.Admission == ItemGuestAdmission.Rejected)
                            RemoveDelivery(new DeliveryWaiterKey
                            {
                                Session = job.Session, Generation = job.Generation, Region = region,
                                Observer = waiter.Observer, Token = waiter.Token
                            });
                        if (admissionFailure.Admission != ItemGuestAdmission.Accepted) continue;
                    }
                    if (ex is ItemObserverReplicationFailureException failure && failure.SafeToRetry)
                    {
                        QueueDeliveryRetry(waiter, region, job, ex);
                        continue;
                    }
                    LastFault = $"region={region} observer={waiter.Observer} outcome=delivery-failed "
                        + "reason=possible-half-packet type=" + ex.GetType().Name;
                    if (QuarantineHalfPacket(waiter.Observer, waiter.Token, region))
                        DiagnosticSink?.Invoke($"event=delivery-first-failure region={region} job={job.Session}:{job.Generation}:{job.NoticeChain} "
                            + $"target={waiter.Observer}/{waiter.Token} attempt={job.AttemptId} "
                            + $"budget={ItemFailureRecovery.MaxRetries}/{ItemFailureRecovery.MaxElapsedSeconds:0}s "
                            + $"exception={ex} stack={ex.StackTrace ?? "unavailable"}");
                    ObserverDeliveryFailure?.Invoke(waiter.Observer, waiter.Token, region, ex);
                    continue;
                }
                _deliveredWaiters.Add(new DeliveredWaiter
                {
                    Region = region, Observer = waiter.Observer, Token = waiter.Token
                });
                ObserverFailureChainCompleted?.Invoke(waiter.Observer, waiter.Token, region);
                delivered = true;
            }
            if (delivered) _delivered.Add(region);
        }

        private void Drop(RegionKey region)
        {
            _jobs.Remove(region);
            _order.Remove(region);
        }

        private struct Waiter
        {
            public ulong Observer;
            public ulong Token;
            public bool Host;
        }

        private struct DeliveryWaiterKey : IEquatable<DeliveryWaiterKey>
        {
            public ulong Session;
            public uint Generation;
            public RegionKey Region;
            public ulong Observer;
            public ulong Token;
            public bool Equals(DeliveryWaiterKey other) => Session == other.Session
                && Generation == other.Generation && Region == other.Region
                && Observer == other.Observer && Token == other.Token;
            public override bool Equals(object obj) => obj is DeliveryWaiterKey other && Equals(other);
            public override int GetHashCode() => Session.GetHashCode() ^ Generation.GetHashCode()
                ^ Region.GetHashCode() ^ Observer.GetHashCode() ^ Token.GetHashCode();
        }

        private sealed class PendingDelivery
        {
            public ulong Session;
            public uint Generation;
            public int NoticeChain;
            public int SourceAttemptId;
            public bool Host;
            public string RetryKey;
            public int AttemptId;
            public bool ExhaustionNotified;
            public bool ManualInitialPending;
            public bool AwaitingAdmission;
        }

        private struct UnconfirmedItem : IEquatable<UnconfirmedItem>
        {
            public byte RegionX;
            public byte RegionY;
            public uint Instance;
            public object NativeScope;

            public bool Equals(UnconfirmedItem other) => RegionX == other.RegionX
                && RegionY == other.RegionY && Instance == other.Instance;
            public override bool Equals(object obj) => obj is UnconfirmedItem other && Equals(other);
            public override int GetHashCode() =>
                RegionX.GetHashCode() ^ RegionY.GetHashCode() ^ Instance.GetHashCode();
        }

        private struct DeliveredWaiter : IEquatable<DeliveredWaiter>
        {
            public RegionKey Region;
            public ulong Observer;
            public ulong Token;

            public bool Equals(DeliveredWaiter other) => Region == other.Region
                && Observer == other.Observer && Token == other.Token;
            public override bool Equals(object obj) => obj is DeliveredWaiter other && Equals(other);
            public override int GetHashCode() => Region.GetHashCode() ^ Observer.GetHashCode() ^ Token.GetHashCode();
        }

        private sealed class Job
        {
            public ulong Session;
            public uint Generation;
            public bool Clearing = true;
            public int RestockBudget;
            public int RestockUsed;
            public bool RestockSucceeded;
            public bool Paused;
            public bool Unavailable;
            public bool FirstFailureLogged;
            public int AttemptId = 1;
            public int NoticeChain;
            public readonly HashSet<string> Notified = new HashSet<string>();
            public readonly List<Waiter> Waiters = new List<Waiter>();
        }
    }
}
