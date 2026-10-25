using SDG.Unturned;
using System;
using System.Reflection;

namespace SteamP2PFriends.Adapters.Item
{
    public struct ItemStepFiveFacts
    {
        public bool Dedicated;
        public bool IsServer;
        public bool CenterSafe;
        public bool Retired;
        public bool LocalHost;
        public bool IdentityKnown;
        public bool ConnectionCurrent;
        public bool ProjectionAvailable;
        public bool SameHostObject;
        public ulong BoundObserver;
        public ulong BoundToken;
    }

    public static class ItemLocalPresentationDispatch
    {
        internal static ItemStepFiveResult DispatchStepFive(
            IItemStepFiveNative native, object playerKey, byte x, byte y,
            ItemProductionControlSeam seam, System.Action legacyAdmission)
        {
            if (native == null || seam == null) return ItemStepFiveResult.RunOriginal;
            ItemModeRaw mode = native.ReadMode();
            if (!mode.IsCandidate)
            {
                legacyAdmission?.Invoke();
                return ItemStepFiveResult.RunOriginal;
            }
            ItemStepFiveAdmission admission = seam.Admission;
            if (admission == null) return ItemStepFiveResult.Waiting;
            var bound = BuildBoundRequest(native, playerKey, x, y, seam);
            return bound.HasValue
                ? admission.Admit(bound.GetValueOrDefault(), BuildProjection(native, playerKey, x, y))
                : ItemStepFiveResult.Waiting;
        }

        private static ItemBoundStepFiveRequest? BuildBoundRequest(
            IItemStepFiveNative native, object playerKey, byte x, byte y,
            ItemProductionControlSeam seam)
        {
            ItemBindingRaw binding = native.ReadBinding(playerKey);
            if (binding?.CallbackOwner == null || binding.CallbackOwnerSteamId == 0UL)
                return null;
            if (!seam.TryGetConnectionGeneration(binding.CallbackOwnerSteamId, out ulong token) || token == 0UL)
                return null;
            if (binding.CommittedToken == 0UL || binding.CommittedToken != token)
                return null;
            var request = new ItemStepFiveRequest(
                binding.CallbackOwnerSteamId, token,
                new SteamP2PFriends.Core.Identity.RegionKey(x, y),
                seam.DemandPolicy.Radius, seam.DemandPolicy.WorldSize,
                binding.CallbackOwnerSteamId == binding.ProviderClientSteamId);
            if (seam.ValidateCurrentBinding(binding, request.ObserverId,
                    request.ConnectionToken, request.Host) != ItemStepFiveResult.Accepted)
                return null;
            return new ItemBoundStepFiveRequest(request, seam.SessionEpoch, playerKey);
        }

        private static IItemStepFiveLoadedProjection BuildProjection(
            IItemStepFiveNative native, object playerKey, byte x, byte y)
        {
            ItemBindingRaw binding = native.ReadBinding(playerKey);
            Action<byte, byte> projectCell = binding?.ProjectCell;
            return new ItemStepFiveLoadedProjection(region =>
            {
                if (projectCell == null)
                    throw new InvalidOperationException(
                        "step5 projection cell writer unavailable");
                projectCell(region.X, region.Y);
            });
        }

        /// <summary>E02 §8/R2C 共同入口：ReceiveItem 增量分流（mode-first）。owned 已知
        /// networked 才 EnqueueIncrement；已知未 networked 不排队也不回原生旧 append
        /// （由后续权威 snapshot 接续）；view 读取异常局部等待/诊断，不冒称成功。</summary>
        internal static ItemStepFiveResult DispatchIncrement(
            IItemStepFiveNative native, IItemLocalPresentationView view,
            byte x, byte y, ushort id, byte amount, byte quality, byte[] state,
            UnityEngine.Vector3 point, uint instanceId, bool playEffect)
        {
            ItemModeRaw mode = native.ReadMode();
            if (!mode.IsCandidate) return ItemStepFiveResult.RunOriginal;
            // null view ≠ 已知未 networked：按未知状态等待
            if (view == null) return ItemStepFiveResult.Waiting;
            bool networked;
            try
            {
                networked = view.GetNetworked(x, y);
            }
            catch (Exception error)
            {
                SteamP2PFriends.Shared.RoleLogger.Warn("[Host]",
                    "[ItemPresentation] increment state unreadable region=(" + x + "," + y
                    + ") instance=" + instanceId
                    + " waiting: " + error.GetType().Name + " " + error.Message);
                return ItemStepFiveResult.Waiting;
            }
            if (!networked)
            {
                // 已知未 networked：不排队、不回原生；权威 snapshot 后续接续
                SteamP2PFriends.Shared.RoleLogger.Info("[Host]",
                    "[ItemPresentation] increment deferred to snapshot region=(" + x + "," + y
                    + ") instance=" + instanceId + " not networked");
                return ItemStepFiveResult.Accepted;
            }
            new ItemLocalPresentationPipeline(view).EnqueueIncrement(
                x, y, id, amount, quality, state, point, instanceId, playEffect);
            return ItemStepFiveResult.Accepted;
        }

        /// <summary>E02 §8/R2C 共同入口：ReceiveDestroy 分流——先撤目标 ID 的真实 raw
        /// pending，再做可能失败的模型/region 读取；缺 region 不先回落原生，目标
        /// model+pending 并存均应清除。rawPending 由 wrapper 从 ItemManager 静态读取，
        /// fixture 注入外部存储。</summary>
        internal static ItemStepFiveResult DispatchDestroy(
            IItemStepFiveNative native, IItemLocalPresentationView view,
            byte x, byte y, uint instanceId, bool playEffect,
            System.Collections.IList rawPending = null)
        {
            ItemModeRaw mode = native.ReadMode();
            if (!mode.IsCandidate) return ItemStepFiveResult.RunOriginal;
            // 先撤 raw pending（真实目标 ID）——取消读取/执行失败属边界故障：局部
            // Waiting+诊断，不继续模型清理，也不让异常逃出 owned 事件
            try
            {
                if (rawPending != null)
                    ItemLocalPresentationPipeline.CancelPendingForInstance(rawPending, instanceId);
                else
                    ItemLocalPresentationPipeline.CancelPendingForInstance(instanceId);
            }
            catch (Exception cancelError)
            {
                SteamP2PFriends.Shared.RoleLogger.Warn("[Host]",
                    "[ItemPresentation] destroy pending cancel failed region=(" + x + "," + y
                    + ") instance=" + instanceId
                    + " waiting: " + cancelError.GetType().Name + " " + cancelError.Message);
                return ItemStepFiveResult.Waiting;
            }
            if (view == null) return ItemStepFiveResult.Waiting;
            bool regionReady;
            try
            {
                regionReady = view.RegionReady(x, y);
            }
            catch (Exception error)
            {
                SteamP2PFriends.Shared.RoleLogger.Warn("[Host]",
                    "[ItemPresentation] destroy region unreadable region=(" + x + "," + y
                    + ") instance=" + instanceId
                    + " pending cleared, model cleanup deferred: "
                    + error.GetType().Name + " " + error.Message);
                return ItemStepFiveResult.Waiting;
            }
            if (!regionReady)
            {
                SteamP2PFriends.Shared.RoleLogger.Info("[Host]",
                    "[ItemPresentation] destroy region not ready region=(" + x + "," + y
                    + ") instance=" + instanceId + " pending cleared, model cleanup deferred");
                return ItemStepFiveResult.Accepted;
            }
            try
            {
                new ItemLocalPresentationPipeline(view).RemovePresentedInstance(
                    x, y, instanceId, playEffect);
                return ItemStepFiveResult.Accepted;
            }
            catch (Exception error)
            {
                SteamP2PFriends.Shared.RoleLogger.Warn("[Host]",
                    "[ItemPresentation] destroy model cleanup failed region=(" + x + "," + y
                    + ") instance=" + instanceId
                    + " pending cleared, model cleanup unconfirmed: "
                    + error.GetType().Name + " " + error.Message);
                return ItemStepFiveResult.Waiting;
            }
        }

        /// <summary>E02 §8/§9：Update wrapper 与 fixture 共同的 owned 消费入口——同一
        /// budget/分类/提交/隔离门；无身份时 owned 暂缓（保留 pending 并诊断）。</summary>
        internal static ConsumeBatchResult ConsumeOwnedBatch(
            IItemStepFiveNative native, object playerKey, ItemProductionControlSeam seam,
            IItemLocalPresentationView view, MethodInfo spawn, object receiver,
            Func<bool> budgetStillAvailable, int minimumActions,
            System.Collections.IList rawPending)
        {
            if (native == null || seam == null || view == null)
            {
                SteamP2PFriends.Shared.RoleLogger.Warn("[Host]",
                    "[ItemPresentation] owned batch deferred: consumer args unavailable");
                return ConsumeBatchResult.Empty;
            }
            ItemModeRaw mode = native.ReadMode();
            if (!mode.IsCandidate)
                return ConsumeBatchResult.Empty;
            ItemBindingRaw binding = native.ReadBinding(playerKey);
            if (binding?.CallbackOwner == null || binding.CallbackOwnerSteamId == 0UL)
            {
                SteamP2PFriends.Shared.RoleLogger.Warn("[Host]",
                    "[ItemPresentation] owned batch deferred: identity unavailable (waiting for stage/commit)");
                return ConsumeBatchResult.Empty;
            }
            if (!seam.TryGetConnectionGeneration(binding.CallbackOwnerSteamId, out ulong token)
                || token == 0UL)
            {
                SteamP2PFriends.Shared.RoleLogger.Warn("[Host]",
                    "[ItemPresentation] owned batch deferred: core token unavailable");
                return ConsumeBatchResult.Empty;
            }
            bool host = binding.CallbackOwnerSteamId == binding.ProviderClientSteamId;
            ItemStepFiveResult bindingResult = seam.ValidateCurrentBinding(
                binding, binding.CallbackOwnerSteamId, token, host);
            if (bindingResult != ItemStepFiveResult.Accepted)
            {
                SteamP2PFriends.Shared.RoleLogger.Warn("[Host]",
                    "[ItemPresentation] owned batch deferred: committed physical binding not current; result="
                    + bindingResult + " committed=" + binding.CommittedToken);
                return ConsumeBatchResult.Empty;
            }
            if (ItemSettlementGate.BlocksRegionForObserver == null)
            {
                SteamP2PFriends.Shared.RoleLogger.Warn("[Host]",
                    "[ItemPresentation] owned batch deferred: region gate not wired");
                return ConsumeBatchResult.Empty;
            }
            var pipeline = new ItemLocalPresentationPipeline(view);
            ConsumeBatchResult result = pipeline.ConsumePendingBatch(
                budgetStillAvailable, minimumActions,
                pending => SteamP2PFriends.Adapters.Item.Patches.ItemOwnedPresentationRuntimePatch
                    .ClassifyForConsumption(view, pending, spawn, receiver),
                pending => ItemSettlementGate.BlocksRegionForObserver(
                    binding.CallbackOwnerSteamId, token, pending.RegionX, pending.RegionY)
                    || ItemSettlementGate.UnconfirmedSideEffectForRegion != null
                    && ItemSettlementGate.UnconfirmedSideEffectForRegion(
                        pending.RegionX, pending.RegionY, pending.InstanceId));
            if (result.Stop == EConsumeOutcome.Uncertain && result.StoppedItem != null)
                ItemSettlementGate.MarkUnconfirmedSideEffect?.Invoke(
                    binding.CallbackOwnerSteamId, token, result.StoppedItem.RegionX,
                    result.StoppedItem.RegionY, result.StoppedItem.InstanceId);
            return result;
        }
        public static ItemStepFiveHandoff AdmitOwnedStepFive(
            ItemStepFiveFacts facts,
            ItemStepFiveAdmission admission,
            IItemStepFiveLoadedProjection loaded,
            byte x,
            byte y,
            int radius,
            int worldSize)
        {
            ItemStepFiveHandoff decision = Decide(facts);
            if (!facts.Retired) return ItemStepFiveHandoff.RunOriginal;
            if (decision != ItemStepFiveHandoff.Handled) return decision;
            if (admission == null || loaded == null || !facts.IdentityKnown || !facts.ConnectionCurrent)
                return ItemStepFiveHandoff.Rejected;
            admission.Admit(new ItemStepFiveRequest(
                facts.BoundObserver, facts.BoundToken,
                new SteamP2PFriends.Core.Identity.RegionKey(x, y),
                radius, worldSize, facts.LocalHost && facts.SameHostObject), loaded);
            return ItemStepFiveHandoff.Handled;
        }

        public static ItemStepFiveHandoff Decide(ItemStepFiveFacts facts)
        {
            if (facts.Dedicated || !facts.IsServer || !facts.CenterSafe || !facts.Retired)
                return ItemStepFiveHandoff.RunOriginal;
            if (!facts.IdentityKnown || !facts.ConnectionCurrent || !facts.ProjectionAvailable)
                return ItemStepFiveHandoff.Rejected;
            if (facts.LocalHost && !facts.SameHostObject)
                return ItemStepFiveHandoff.Rejected;
            return ItemStepFiveHandoff.Handled;
        }

        public static Player ResolveCurrentHostPlayer()
        {
            try
            {
                if (Provider.clients == null) return null;
                ulong providerClientId = Provider.client.m_SteamID;
                if (providerClientId == 0UL) return null;
                if (!MultiObserver.MultiObserverShadowCoordinator.TryReadCommittedItemConnection(
                    providerClientId, out object committedOwner, out object committedTransport,
                    out ulong committedToken) || committedToken == 0UL)
                    return null;
                for (int i = 0; i < Provider.clients.Count; i++)
                {
                    SteamPlayer client = Provider.clients[i];
                    if (client?.playerID?.steamID.m_SteamID != providerClientId) continue;
                    Player player = client.player;
                    if (player?.channel == null || !player.channel.IsLocalPlayer
                        || !object.ReferenceEquals(player.channel.owner, client))
                        return null;
                    if (!object.ReferenceEquals(committedOwner, client)
                        || !object.ReferenceEquals(committedTransport, client.transportConnection))
                        return null;
                    return player;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Host player lookup failed.", ex);
            }
            return null;
        }

        public static bool TryReadBoundGeneration(Player player, out ulong observerId, out ulong token)
        {
            observerId = 0UL;
            token = 0UL;
            SteamPlayer owner = player?.channel?.owner;
            if (owner?.playerID == null || owner.transportConnection == null) return false;
            observerId = owner.playerID.steamID.m_SteamID;
            if (observerId == 0UL) return false;
            bool bound = false;
            try
            {
                if (Provider.clients == null) return false;
                for (int i = 0; i < Provider.clients.Count; i++)
                {
                    SteamPlayer client = Provider.clients[i];
                    if (client == null || !object.ReferenceEquals(client.transportConnection, owner.transportConnection)) continue;
                    if (client.playerID == null || client.playerID.steamID.m_SteamID != observerId) continue;
                    bound = true;
                    break;
                }
            }
            catch { return false; }
            if (!bound) return false;
            token = MultiObserver.MultiObserverShadowCoordinator.GetItemConnectionGeneration(observerId);
            return token != 0UL;
        }
    }
}
