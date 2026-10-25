using System;
using System.Collections.Generic;
using System.Reflection;
using SDG.Unturned;
using SteamP2PFriends.Adapters.Item;
using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver;
using SteamP2PFriends.MultiObserver.Demand;
using SteamP2PFriends.MultiObserver.Lifecycle;
using SteamP2PFriends.MultiObserver.SPI;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// E02 A 片：radius1/world64 强 fixture 下的 step5-first 正向、共享先行幂等与
    /// 身份 Rejected/Waiting 行为。身份一律走生产 stage/commit，不手填结论。
    /// </summary>
    internal static class R01E02SliceATests
    {
        /// <summary>E02 §5：组合测试经由此读取 harness view 的 pending/models。</summary>
        internal interface IComboHarnessView
        {
            System.Collections.IList Pending { get; }
            System.Collections.IEnumerable Models { get; }
        }

        internal static FixtureHarness AcquireComboHarness() => new FixtureHarness();

        internal sealed class TestView : IItemLocalPresentationView
        {
            public R01E02Fixture.NativeStorage Storage;
            public Func<System.Collections.IList> PendingReader;
            public Func<byte, byte, System.Collections.IList> ModelsReader;
            public Action<ModelView> RemovedAction;
            public Action<object> DestroyAction;
            public readonly List<PendingView> PendingItems = new List<PendingView>();
            public readonly Dictionary<(byte, byte), List<ModelView>> ModelsByRegion =
                new Dictionary<(byte, byte), List<ModelView>>();
            public int PresentSharedCalls;

            public bool RegionReady(byte x, byte y) => true;
            public IReadOnlyList<ItemView> ReadCurrentItems(byte x, byte y)
            {
                var items = new List<ItemView>();
                if (Storage == null) return items;
                List<ItemData> source = Storage.Regions[x, y].items;
                foreach (ItemData data in source)
                {
                    if (data == null) continue;
                    items.Add(new ItemView
                    {
                        InstanceId = data.instanceID,
                        AssetId = data.item?.id ?? 0,
                        Amount = data.item?.amount ?? 0,
                        Quality = data.item?.quality ?? 0,
                        State = data.item?.state
                    });
                }
                return items;
            }
            public System.Collections.IList Pending => PendingReader != null
                ? PendingReader()
                : PendingItems;
            public System.Collections.IList Models(byte x, byte y)
            {
                if (ModelsReader != null) return ModelsReader(x, y);
                if (ModelsByRegion.TryGetValue((x, y), out List<ModelView> list)) return list;
                return EmptyModels;
            }
            public System.Collections.IEnumerable AllModels
            {
                get
                {
                    foreach (List<ModelView> list in ModelsByRegion.Values)
                        foreach (ModelView model in list)
                            yield return model;
                }
            }
            private static readonly List<ModelView> EmptyModels = new List<ModelView>();
            public bool GetNetworked(byte x, byte y) =>
                Storage != null && Storage.Regions[x, y].isNetworked;
            public void SetNetworked(byte x, byte y, bool value)
            {
                Storage.Regions[x, y].isNetworked = value;
            }
            public bool ProjectionAvailable(byte x, byte y) =>
                x < R01E02Fixture.WorldSize && y < R01E02Fixture.WorldSize;
            public void SetLoaded(byte x, byte y, bool value)
            {
                Storage.Loaded[x, y].isItemsLoaded = value;
            }
            public float ItemSortOrder(ItemView item) => 0f;
            public void DestroyUnity(object modelHandle) { DestroyAction?.Invoke(modelHandle); }
            public void NotifyRemoved(ModelView model) { RemovedAction?.Invoke(model); }
            public void PlayRemovalEffect(ModelView model) { }
            public void FinishDeferredDestroy(byte x, byte y) { }
        }

        internal sealed class RawNative : IItemStepFiveNative
        {
            public Func<ItemModeRaw> ModeReader;
            public Func<object, ItemBindingRaw> BindingReader;
            public ItemModeRaw ReadMode() => ModeReader != null ? ModeReader() : default;
            public ItemBindingRaw ReadBinding(object playerKey) =>
                BindingReader != null ? BindingReader(playerKey) : null;
        }

        internal static bool Test_E02_A1_StepFiveFirstPresentsTargetB()
        {
            var harness = new FixtureHarness();
            harness.Begin(R01E02Fixture.HostId);
            ulong token = harness.StageAndCommitIdentity(
                R01E02Fixture.HostId, harness.HostOwner, harness.HostTransport);
            if (token == 0UL) return false;
            harness.Native.ModeReader = harness.ReadHostMode;
            harness.Native.BindingReader = harness.ReadHostBinding;
            harness.Seam.UpdateObserver(R01E02Fixture.HostId, token,
                R01E02Fixture.CenterA, R01E02Fixture.CenterA, true);
            int aDemand = 0;
            for (int x = R01E02Fixture.CenterA - 1; x <= R01E02Fixture.CenterA + 1; x++)
                for (int y = R01E02Fixture.CenterA - 1; y <= R01E02Fixture.CenterA + 1; y++)
                    if (harness.Seam.GetDemand(new RegionKey((byte)x, (byte)y)) > 0) aDemand++;
            if (aDemand != 9)
            {
                Console.WriteLine("E02A1 aDemand=" + aDemand + " token=" + token
                    + " observers=" + harness.Seam.ObserverCount
                    + " demandRegions=" + harness.Seam.DemandRegionCount
                    + " leases=" + harness.Seam.ActiveLeaseCount);
                return false;
            }
            harness.Seam.Flush(0f);
            harness.Seam.Flush(0f);
            ItemStepFiveResult result;
            harness.Bridge.Storage.Regions[R01E02Fixture.CenterB, R01E02Fixture.CenterB].items.Add(
                R01E02Fixture.MakeItem(1001U, 276, 1, 100));
            harness.Bridge.Storage.Regions[R01E02Fixture.CenterB, R01E02Fixture.CenterB].items.Add(
                R01E02Fixture.MakeItem(1002U, 16, 1, 100));
            int bBefore = harness.Port.HostPresentationCount;
            result = ItemLocalPresentationDispatch.DispatchStepFive(
                harness.Native, harness.HostPlayer, R01E02Fixture.CenterB, R01E02Fixture.CenterB,
                harness.Seam, null);
            if (result != ItemStepFiveResult.Accepted)
            {
                Console.WriteLine("E02A1 result=" + result + " bindingReads=" + harness.BindingReads);
                return false;
            }
            harness.Seam.Flush(0f);
            harness.Seam.Flush(0f);
            int bPresented = harness.Port.HostPresentationCount - bBefore;
            if (bPresented != 9)
            {
                Console.WriteLine("E02A1 bPresented=" + bPresented);
                return false;
            }
            if (!harness.View.GetNetworked(R01E02Fixture.CenterB, R01E02Fixture.CenterB)
                || harness.View.Pending.Count != 2)
            {
                Console.WriteLine("E02A1 bCenter networked="
                    + harness.View.GetNetworked(R01E02Fixture.CenterB, R01E02Fixture.CenterB)
                    + " pending=" + harness.View.PendingItems.Count);
                return false;
            }
            return harness.BindingReads > 0;
        }

        internal static bool Test_E02_A2_SharedFirstThenStepFiveIdempotent()
        {
            var harness = new FixtureHarness();
            harness.Begin(R01E02Fixture.HostId);
            ulong token = harness.StageAndCommitIdentity(
                R01E02Fixture.HostId, harness.HostOwner, harness.HostTransport);
            if (token == 0UL) return false;
            harness.Native.ModeReader = harness.ReadHostMode;
            harness.Native.BindingReader = harness.ReadHostBinding;
            harness.Bridge.Storage.Regions[R01E02Fixture.CenterB, R01E02Fixture.CenterB].items.Add(
                R01E02Fixture.MakeItem(1001U, 276, 1, 100));
            harness.Bridge.Storage.Regions[R01E02Fixture.CenterB, R01E02Fixture.CenterB].items.Add(
                R01E02Fixture.MakeItem(1002U, 16, 1, 100));
            harness.Seam.UpdateObserver(R01E02Fixture.HostId, token,
                R01E02Fixture.CenterB, R01E02Fixture.CenterB, true);
            harness.Seam.Flush(0f);
            harness.Seam.Flush(0f);
            int presented = harness.Port.HostPresentationCount;
            if (presented != 9 || harness.View.Pending.Count != 2) return false;
            ItemStepFiveResult result = ItemLocalPresentationDispatch.DispatchStepFive(
                harness.Native, harness.HostPlayer, R01E02Fixture.CenterB, R01E02Fixture.CenterB,
                harness.Seam, null);
            if (result != ItemStepFiveResult.Accepted) return false;
            harness.Seam.Flush(0f);
            if (harness.Port.HostPresentationCount != presented
                || harness.View.Pending.Count != 2) return false;
            return true;
        }

        /// <summary>E02 B/§4：Guest 自己 step5-first A→B，原 A 九区真实 unbinder 正向各一次。</summary>
        internal static bool Test_E02_B2_GuestExitUnbindsOriginalRegions()
        {
            string fail = null;
            var harness = new FixtureHarness();
            harness.Begin(R01E02Fixture.HostId);
            ulong guestToken = harness.StageAndCommitIdentity(
                R01E02Fixture.GuestId, harness.GuestOwner, harness.GuestTransport);
            if (guestToken == 0UL) fail = "guest token=0";
            if (fail == null)
            {
                harness.Native.ModeReader = harness.ReadHostMode;
                harness.Native.BindingReader = harness.ReadGuestBinding;
                // Guest 先驻 A
                harness.Seam.UpdateObserver(R01E02Fixture.GuestId, guestToken,
                    R01E02Fixture.CenterA, R01E02Fixture.CenterA, true);
                harness.Seam.Flush(0f);
                harness.Seam.Flush(0f);
                if (harness.UnbinderTargets.Count != 0)
                    fail = "early unbinder=" + harness.UnbinderTargets.Count;
            }
            if (fail == null)
            {
                // Guest step5-first 到 B（共同入口；guest 身份经生产 stage/commit）
                ItemStepFiveResult result = ItemLocalPresentationDispatch.DispatchStepFive(
                    harness.Native, harness.GuestPlayer, R01E02Fixture.CenterB,
                    R01E02Fixture.CenterB, harness.Seam, null);
                if (result != ItemStepFiveResult.Accepted)
                    fail = "guest dispatch=" + result;
            }
            if (fail == null)
            {
                harness.Seam.Flush(0f);
                harness.Seam.Flush(0f);
                if (harness.UnbinderTargets.Count != 9)
                    fail = "unbinderCount=" + harness.UnbinderTargets.Count
                        + " targets=" + string.Join(";", harness.UnbinderTargets);
            }
            if (fail == null)
                foreach (RegionKey unbound in harness.UnbinderTargets)
                {
                    int dx = Math.Abs(unbound.X - R01E02Fixture.CenterA);
                    int dy = Math.Abs(unbound.Y - R01E02Fixture.CenterA);
                    if (Math.Max(dx, dy) > 1) fail = "wrong-unbind-target=" + unbound;
                }
            if (fail == null)
            {
                // 另一观察者（Host）仍在 B，其需求不受 Guest 退出影响
                int bDemand = 0;
                for (int x = R01E02Fixture.CenterB - 1; x <= R01E02Fixture.CenterB + 1; x++)
                    for (int y = R01E02Fixture.CenterB - 1; y <= R01E02Fixture.CenterB + 1; y++)
                        if (harness.Seam.GetDemand(new RegionKey((byte)x, (byte)y)) > 0) bDemand++;
                if (bDemand != 9) fail = "bDemand-after-guest-exit=" + bDemand;
            }
            if (fail != null)
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "e02b2-fail.txt"), fail);
            return fail == null;
        }

        /// <summary>E02 B/Red2：真实 step5-first A→B，A 九区 exit=0（Host cleaner 实际
        /// 目标坐标），普通 Observe 到同一 B 不再额外 exit/enter；另一观察者共享需求不清零。</summary>
        /// <summary>E02 §8.1：真实新 ID 组合——A=2001 拾取经 nativeTransaction 外部槽
        /// （删权威 A+记录入包+DispatchDestroy），丢下 B=2002 经 DispatchIncrement，
        /// 消费后 A=0、B 恰好 1 份有效模型。</summary>
        internal static bool Test_E02_C1_NewInstanceIdLifecycle()
        {
            string fail = null;
            var harness = new FixtureHarness();
            harness.Begin(R01E02Fixture.HostId);
            ulong token = harness.StageAndCommitIdentity(
                R01E02Fixture.HostId, harness.HostOwner, harness.HostTransport);
            harness.Native.ModeReader = harness.ReadHostMode;
            harness.Native.BindingReader = harness.ReadHostBinding;
            byte bx = R01E02Fixture.CenterB;
            harness.Bridge.Storage.Regions[bx, bx].items.Add(
                R01E02Fixture.MakeItem(2001U, 276, 1, 100));
            harness.Seam.UpdateObserver(R01E02Fixture.HostId, token, bx, bx, true);
            harness.Seam.Flush(0f);
            harness.Seam.Flush(0f);
            int pendingShared = CountPendingOf(harness, 2001U);
            if (pendingShared != 1) fail = "shared pending=" + pendingShared;
            // 拾取事务：外部权威输入删 A、记录入包，真实删除事件走 DispatchDestroy
            if (fail == null)
            {
                bool executed = false;
                bool picked = SteamP2PFriends.Adapters.Item.ItemAuthoritativePickup.TryRequest(
                    true, true, UnityEngine.Vector3.zero, 2001U,
                    harness.Bridge.Storage.Regions[bx, bx].items,
                    UnityEngine.Vector3.zero, true, true,
                    () =>
                    {
                        executed = true;
                        harness.Bridge.Storage.Regions[bx, bx].items.RemoveAt(0);
                        harness.Native.ModeReader = harness.ReadHostMode;
                        ItemStepFiveResult destroy = ItemLocalPresentationDispatch.DispatchDestroy(
                            harness.Native, harness.View, bx, bx, 2001U, false,
                            (System.Collections.IList)harness.RawPending);
                        return destroy == ItemStepFiveResult.Accepted;
                    });
                if (!picked || !executed) fail = "pickup picked=" + picked;
            }
            if (fail == null)
            {
                harness.Seam.Flush(0f);
                if (CountPendingOf(harness, 2001U) != 0
                    || harness.Bridge.Storage.Regions[bx, bx].items.Count != 0)
                    fail = "after-pickup pendingA=" + CountPendingOf(harness, 2001U)
                        + " warehouseA=" + harness.Bridge.Storage.Regions[bx, bx].items.Count;
            }
            // 丢下 B=2002：外部原生事件数据经 DispatchIncrement（共同入口）
            if (fail == null)
            {
                harness.Bridge.Storage.Regions[bx, bx].items.Add(
                    R01E02Fixture.MakeItem(2002U, 16, 1, 100));
                ItemStepFiveResult inc = ItemLocalPresentationDispatch.DispatchIncrement(
                    harness.Native, harness.View, bx, bx, 16, 1, 100,
                    new byte[0], UnityEngine.Vector3.zero, 2002U, false);
                if (inc != ItemStepFiveResult.Accepted) fail = "increment=" + inc;
            }
            if (fail == null)
            {
                harness.Seam.Flush(0f);
                if (CountPendingOf(harness, 2002U) != 1)
                    fail = "increment pendingB=" + CountPendingOf(harness, 2002U);
            }
            // 最终模型腿：经同一共同 consumer（ConsumeOwnedBatch）真实消费/分类/提交
            if (fail == null)
            {
                var receiver = new SpawnReceiver(harness.View);
                MethodInfo spawn = typeof(SpawnReceiver).GetMethod("SpawnItem",
                    BindingFlags.Instance | BindingFlags.Public);
                harness.View.ModelsByRegion[(bx, bx)] = new List<ModelView>();
                ConsumeBatchResult consumed = ItemLocalPresentationDispatch.ConsumeOwnedBatch(
                    harness.Native, harness.HostPlayer, harness.Seam, harness.View,
                    spawn, receiver, () => true, 1,
                    (System.Collections.IList)harness.RawPending);
                if (consumed.Stop != EConsumeOutcome.Consumed || consumed.Consumed < 1)
                    fail = "consume stop=" + consumed.Stop + " count=" + consumed.Consumed;
            }
            if (fail == null)
            {
                // 最终态断言：A 全 0、B pending 提交且有效模型恰 1、不复活
                int modelB = 0;
                foreach (ModelView model in harness.View.AllModels)
                    if (model.InstanceId == 2002U && model.Handle != null) modelB++;
                if (CountPendingOf(harness, 2001U) != 0
                    || CountPendingOf(harness, 2002U) != 0
                    || harness.Bridge.Storage.Regions[bx, bx].items.Count != 1
                    || modelB != 1)
                    fail = "final pendingA=" + CountPendingOf(harness, 2001U)
                        + " pendingB=" + CountPendingOf(harness, 2002U)
                        + " warehouseB=" + harness.Bridge.Storage.Regions[bx, bx].items.Count
                        + " modelB=" + modelB;
            }
            if (fail == null)
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "e02c1-ok.txt"),
                    "pendingA=0 pendingB=1 warehouseB=1");
            }
            else
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "e02c1-fail.txt"), fail);
            }
            return fail == null;
        }

        /// <summary>E02 §8.2：真实 receiver 九参实例方法经 MethodInfo 的四路分类。</summary>
        internal sealed class SpawnReceiver
        {
            public int Invocations;
            public bool ThrowAfterAction;
            public bool SkipRegistration;
            public Exception Failure;
            public readonly TestView View;
            public byte LastX;
            public byte LastY;

            public SpawnReceiver(TestView view) { View = view; }

            public void SpawnItem(byte x, byte y, ushort assetId, byte amount, byte quality,
                byte[] state, UnityEngine.Vector3 point, uint instanceId, bool playEffect)
            {
                Invocations++;
                LastX = x;
                LastY = y;
                // 真实 receiver 语义：创建模型（SkipRegistration=外部动作未达创建）
                if (!SkipRegistration)
                    View.ModelsByRegion[(x, y)].Add(new ModelView
                    {
                        InstanceId = instanceId,
                        Handle = new object()
                    });
                if (ThrowAfterAction)
                    throw Failure ?? new InvalidOperationException("post-spawn effect failure");
            }
        }

        internal static bool Test_R01_V1_BudgetedConsumptionAndReentry()
        {
            Func<bool, bool> run = stepFiveFirst =>
            {
                var harness = new FixtureHarness();
                harness.Begin(R01E02Fixture.HostId);
                ulong token = harness.StageAndCommitIdentity(
                    R01E02Fixture.HostId, harness.HostOwner, harness.HostTransport);
                harness.Native.ModeReader = harness.ReadHostMode;
                harness.Native.BindingReader = harness.ReadHostBinding;
                byte x = R01E02Fixture.CenterB;
                harness.Bridge.Storage.Regions[x, x].items.Add(R01E02Fixture.MakeItem(7001U, 276, 1, 100));
                harness.Bridge.Storage.Regions[x, x].items.Add(R01E02Fixture.MakeItem(7002U, 16, 1, 100));
                harness.Seam.UpdateObserver(R01E02Fixture.HostId, token,
                    R01E02Fixture.CenterA, R01E02Fixture.CenterA, true);
                harness.Seam.Flush(0f); harness.Seam.Flush(0f);
                if (stepFiveFirst && ItemLocalPresentationDispatch.DispatchStepFive(
                    harness.Native, harness.HostPlayer, x, x, harness.Seam, null) != ItemStepFiveResult.Accepted) return false;
                if (!stepFiveFirst) harness.Seam.UpdateObserver(R01E02Fixture.HostId, token, x, x, true);
                harness.Seam.Flush(0f); harness.Seam.Flush(0f);
                if (stepFiveFirst) harness.Seam.UpdateObserver(R01E02Fixture.HostId, token, x, x, true);
                else if (ItemLocalPresentationDispatch.DispatchStepFive(
                    harness.Native, harness.HostPlayer, x, x, harness.Seam, null) != ItemStepFiveResult.Accepted) return false;
                harness.Seam.Flush(0f); harness.Seam.Flush(0f);
                if (CountPendingOf(harness, 7001U) != 1 || CountPendingOf(harness, 7002U) != 1) return false;
                harness.View.ModelsByRegion[(x, x)] = new List<ModelView>();
                var receiver = new SpawnReceiver(harness.View);
                MethodInfo spawn = typeof(SpawnReceiver).GetMethod("SpawnItem");
                var raw = (System.Collections.IList)harness.RawPending;
                ConsumeBatchResult partial = ItemLocalPresentationDispatch.ConsumeOwnedBatch(
                    harness.Native, harness.HostPlayer, harness.Seam, harness.View, spawn, receiver, () => false, 1, raw);
                int Models(uint id) => harness.View.ModelsByRegion[(x, x)].FindAll(m => m.InstanceId == id && m.Handle != null).Count;
                if (partial.Consumed != 1 || CountPendingOf(harness, 7001U) + Models(7001U) != 1
                    || CountPendingOf(harness, 7002U) + Models(7002U) != 1) return false;
                harness.Seam.UpdateObserver(R01E02Fixture.HostId, token, x, x, true);
                if (ItemLocalPresentationDispatch.DispatchStepFive(harness.Native, harness.HostPlayer,
                    x, x, harness.Seam, null) != ItemStepFiveResult.Accepted) return false;
                harness.Seam.Flush(0f);
                if (CountPendingOf(harness, 7001U) + Models(7001U) != 1
                    || CountPendingOf(harness, 7002U) + Models(7002U) != 1) return false;
                ConsumeBatchResult full = ItemLocalPresentationDispatch.ConsumeOwnedBatch(
                    harness.Native, harness.HostPlayer, harness.Seam, harness.View, spawn, receiver, () => true, 1, raw);
                harness.Seam.UpdateObserver(R01E02Fixture.HostId, token, x, x, true);
                harness.Seam.Flush(0f);
                return full.Consumed == 1 && receiver.Invocations == 2
                    && CountPendingOf(harness, 7001U) == 0 && CountPendingOf(harness, 7002U) == 0
                    && Models(7001U) == 1 && Models(7002U) == 1;
            };
            return run(false) && run(true);
        }

        internal static bool Test_R01_V1_ModeledPickupDoesNotResurrect()
        {
            var h = new FixtureHarness();
            h.Begin(R01E02Fixture.HostId);
            ulong token = h.StageAndCommitIdentity(R01E02Fixture.HostId, h.HostOwner, h.HostTransport);
            h.Native.ModeReader = h.ReadHostMode;
            h.Native.BindingReader = h.ReadHostBinding;
            byte x = R01E02Fixture.CenterB;
            h.Bridge.Storage.Regions[x, x].items.Add(R01E02Fixture.MakeItem(8001U, 276, 1, 100));
            h.Seam.UpdateObserver(R01E02Fixture.HostId, token, x, x, true);
            h.Seam.Flush(0f); h.Seam.Flush(0f);
            h.View.ModelsByRegion[(x, x)] = new List<ModelView>();
            var receiver = new SpawnReceiver(h.View);
            MethodInfo spawn = typeof(SpawnReceiver).GetMethod("SpawnItem");
            var raw = (System.Collections.IList)h.RawPending;
            if (ItemLocalPresentationDispatch.ConsumeOwnedBatch(h.Native, h.HostPlayer, h.Seam,
                h.View, spawn, receiver, () => true, 1, raw).Consumed != 1) return false;
            bool picked = SteamP2PFriends.Adapters.Item.ItemAuthoritativePickup.TryRequest(
                true, true, UnityEngine.Vector3.zero, 8001U, h.Bridge.Storage.Regions[x, x].items,
                UnityEngine.Vector3.zero, true, true, () =>
                {
                    h.Bridge.Storage.Regions[x, x].items.RemoveAt(0);
                    return ItemLocalPresentationDispatch.DispatchDestroy(h.Native, h.View,
                        x, x, 8001U, false, raw) == ItemStepFiveResult.Accepted;
                });
            h.Seam.UpdateObserver(R01E02Fixture.HostId, token, x, x, true);
            h.Seam.Flush(0f);
            int models = 0;
            foreach (ModelView model in h.View.AllModels)
                if (model.InstanceId == 8001U && model.Handle != null) models++;
            return picked && receiver.Invocations == 1 && h.Bridge.Storage.Regions[x, x].items.Count == 0
                && CountPendingOf(h, 8001U) == 0 && models == 0;
        }

        internal static bool Test_R01_V1_AfterSnapshotFailureQuarantinesConsumer()
        {
            Func<bool, bool> run = throwsInside =>
            {
                var h = new FixtureHarness(); h.Begin(R01E02Fixture.HostId);
                ulong token = h.StageAndCommitIdentity(R01E02Fixture.HostId, h.HostOwner, h.HostTransport);
                h.Native.ModeReader = h.ReadHostMode; h.Native.BindingReader = h.ReadHostBinding;
                byte x = R01E02Fixture.CenterB;
                h.Bridge.Storage.Regions[x, x].items.Add(R01E02Fixture.MakeItem(8101U, 276, 1, 100));
                h.Seam.UpdateObserver(R01E02Fixture.HostId, token, x, x, true);
                h.Seam.Flush(0f); h.Seam.Flush(0f); h.View.ModelsByRegion[(x, x)] = new List<ModelView>();
                int reads = 0;
                h.View.ModelsReader = (mx, my) => ++reads == 1
                    ? (System.Collections.IList)h.View.ModelsByRegion[(mx, my)]
                    : throw new InvalidOperationException("post-snapshot unavailable");
                var receiver = new SpawnReceiver(h.View) { ThrowAfterAction = throwsInside,
                    Failure = new InvalidOperationException("original spawn failure") };
                MethodInfo spawn = typeof(SpawnReceiver).GetMethod("SpawnItem");
                var raw = (System.Collections.IList)h.RawPending;
                ConsumeBatchResult first = ItemLocalPresentationDispatch.ConsumeOwnedBatch(
                    h.Native, h.HostPlayer, h.Seam, h.View, spawn, receiver, () => true, 1, raw);
                ConsumeBatchResult retry = ItemLocalPresentationDispatch.ConsumeOwnedBatch(
                    h.Native, h.HostPlayer, h.Seam, h.View, spawn, receiver, () => true, 1, raw);
                return first.Stop == EConsumeOutcome.Uncertain && first.StoppedItem?.InstanceId == 8101U
                    && retry.Consumed == 0 && receiver.Invocations == 1 && CountPendingOf(h, 8101U) == 1;
            };
            var view = new TestView(); view.ModelsByRegion[(2, 2)] = new List<ModelView>();
            int reads = 0; view.ModelsReader = (x, y) => ++reads == 1
                ? (System.Collections.IList)view.ModelsByRegion[(x, y)]
                : throw new InvalidOperationException("post-snapshot unavailable");
            var receiver = new SpawnReceiver(view) { ThrowAfterAction = true };
            var original = new InvalidOperationException("original spawn failure"); receiver.Failure = original;
            ItemSpawnResult classified = SteamP2PFriends.Adapters.Item.Patches.ItemOwnedPresentationRuntimePatch.InvokeAndClassify(
                view, NewPending(8102U, 2, 2), typeof(SpawnReceiver).GetMethod("SpawnItem"), receiver);
            return classified.Progress == ItemSpawnProgress.Uncertain
                && ReferenceEquals(classified.Failure, original) && run(false) && run(true);
        }

        internal static bool Test_E02_C2_InvokeAndClassifyMatrix()
        {
            string fail = null;
            string outcome = null;
            MethodInfo spawn = typeof(SpawnReceiver).GetMethod("SpawnItem",
                BindingFlags.Instance | BindingFlags.Public);
            // ① 缺 MethodInfo 预检失败，调用 0
            if (fail == null)
            {
                var view = new TestView();
                var receiver = new SpawnReceiver(view);
                view.ModelsByRegion[(2, 2)] = new List<ModelView>();
                var result = SteamP2PFriends.Adapters.Item.Patches.ItemOwnedPresentationRuntimePatch
                    .InvokeAndClassify(view, NewPending(4001U, 2, 2), null, receiver);
                outcome = "case1=" + result.Progress;
                if (result.Progress != SteamP2PFriends.Adapters.Item.ItemSpawnProgress.NotStarted
                    || receiver.Invocations != 0)
                    fail = outcome;
            }
            // ② 记录一次外部作用后抛、无模型 → Uncertain
            if (fail == null)
            {
                var view = new TestView();
                var receiver = new SpawnReceiver(view) { ThrowAfterAction = true, SkipRegistration = true };
                view.ModelsByRegion[(2, 2)] = new List<ModelView>();
                var result = SteamP2PFriends.Adapters.Item.Patches.ItemOwnedPresentationRuntimePatch
                    .InvokeAndClassify(view, NewPending(4002U, 2, 2), spawn, receiver);
                outcome += "; case2=" + result.Progress;
                if (result.Progress != SteamP2PFriends.Adapters.Item.ItemSpawnProgress.Uncertain
                    || receiver.Invocations != 1
                    || result.Failure == null)
                    fail = outcome;
            }
            // ③ 登记模型后抛 → Registered + Failure
            if (fail == null)
            {
                var view = new TestView();
                var receiver = new SpawnReceiver(view) { ThrowAfterAction = true };
                view.ModelsByRegion[(2, 2)] = new List<ModelView>();
                var result = SteamP2PFriends.Adapters.Item.Patches.ItemOwnedPresentationRuntimePatch
                    .InvokeAndClassify(view, NewPending(4003U, 2, 2), spawn, receiver);
                outcome += "; case3=" + result.Progress;
                if (result.Progress != SteamP2PFriends.Adapters.Item.ItemSpawnProgress.Registered
                    || result.Failure == null
                    || receiver.Invocations != 1)
                    fail = outcome;
            }
            // ④ 调用前已有同 ID 有效模型 → 调用 0，不新增
            if (fail == null)
            {
                var view = new TestView();
                var receiver = new SpawnReceiver(view);
                view.ModelsByRegion[(2, 2)] = new List<ModelView>
                {
                    new ModelView { InstanceId = 4004U, Handle = new object() }
                };
                var result = SteamP2PFriends.Adapters.Item.Patches.ItemOwnedPresentationRuntimePatch
                    .InvokeAndClassify(view, NewPending(4004U, 2, 2), spawn, receiver);
                outcome += "; case4=" + result.Progress;
                // 覆盖纠正：before-existing 有效模型 → 去重提交语义（Registered、调用 0）
                if (result.Progress != SteamP2PFriends.Adapters.Item.ItemSpawnProgress.Registered
                    || receiver.Invocations != 0
                    || view.ModelsByRegion[(2, 2)].Count != 1)
                    fail = outcome;
            }
            if (fail != null)
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "e02c2-fail.txt"),
                    fail + " | " + outcome);
            else
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "e02c2-ok.txt"),
                    outcome);
            return fail == null;
        }

        /// <summary>E02 D/§6 反例：同九参但 static、同九参但非 void 返回 → Unsupported。</summary>
        internal static bool Test_E02_D3_SpawnSignatureNegativeShapes()
        {
            string fail = null;
            var inspect = typeof(SteamP2PFriends.Adapters.Item.Patches.ItemSettlementConsumptionPatch)
                .GetMethod("InspectPendingBatchShape",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            var shapeEnum = typeof(SteamP2PFriends.Adapters.Item.Patches.ItemSettlementConsumptionPatch)
                .GetNestedType("PendingBatchShape", BindingFlags.NonPublic | BindingFlags.Public);
            var paramType = typeof(ItemManager).Assembly
                .GetType("SDG.Unturned.ItemInstantiationParameters", true);
            if (fail == null)
            {
                var asm = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
                    new AssemblyName("E02ShapeStatic"), System.Reflection.Emit.AssemblyBuilderAccess.Run);
                var module = asm.DefineDynamicModule("E02ShapeStatic");
                var type = module.DefineType("SynthStaticSpawn", TypeAttributes.Public);
                type.DefineField("pendingInstantiations",
                    typeof(List<>).MakeGenericType(paramType),
                    FieldAttributes.Static | FieldAttributes.Public);
                type.DefineField("instantiationTimer", typeof(System.Diagnostics.Stopwatch),
                    FieldAttributes.Public);
                var spawn = type.DefineMethod("spawnItem",
                    MethodAttributes.Static | MethodAttributes.Public, typeof(void),
                    new[] { typeof(byte), typeof(byte), typeof(ushort), typeof(byte),
                        typeof(byte), typeof(byte[]), typeof(UnityEngine.Vector3),
                        typeof(uint), typeof(bool) });
                spawn.GetILGenerator().Emit(System.Reflection.Emit.OpCodes.Ret);
                var update = type.DefineMethod("Update", MethodAttributes.Public,
                    typeof(void), Type.EmptyTypes);
                update.GetILGenerator().Emit(System.Reflection.Emit.OpCodes.Ret);
                object shape = inspect.Invoke(null, new object[] { type.CreateType() });
                if (!Equals(shape, Enum.Parse(shapeEnum, "Unsupported")))
                    fail = "static-shape=" + shape;
            }
            if (fail == null)
            {
                var asm = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
                    new AssemblyName("E02ShapeNonVoid"), System.Reflection.Emit.AssemblyBuilderAccess.Run);
                var module = asm.DefineDynamicModule("E02ShapeNonVoid");
                var type = module.DefineType("SynthNonVoidSpawn", TypeAttributes.Public);
                type.DefineField("pendingInstantiations",
                    typeof(List<>).MakeGenericType(paramType),
                    FieldAttributes.Static | FieldAttributes.Public);
                type.DefineField("instantiationTimer", typeof(System.Diagnostics.Stopwatch),
                    FieldAttributes.Public);
                var spawn = type.DefineMethod("spawnItem", MethodAttributes.Public,
                    typeof(bool), new[] { typeof(byte), typeof(byte), typeof(ushort),
                        typeof(byte), typeof(byte), typeof(byte[]),
                        typeof(UnityEngine.Vector3), typeof(uint), typeof(bool) });
                var il = spawn.GetILGenerator();
                il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4_0);
                il.Emit(System.Reflection.Emit.OpCodes.Ret);
                var update = type.DefineMethod("Update", MethodAttributes.Public,
                    typeof(void), Type.EmptyTypes);
                update.GetILGenerator().Emit(System.Reflection.Emit.OpCodes.Ret);
                object shape = inspect.Invoke(null, new object[] { type.CreateType() });
                if (!Equals(shape, Enum.Parse(shapeEnum, "Unsupported")))
                    fail = "nonvoid-shape=" + shape;
            }
            if (fail != null)
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "e02d3-fail.txt"), fail);
            return fail == null;
        }

        /// <summary>E02 D4 反例的 managed 原方法（纯内存可 JIT，承载真实 Harmony patch）；
        /// 参数形状与 AllowPickup(byte,byte) 对齐以支持注入。</summary>
        internal static class D4PatchTarget
        {
            public static void ManagedOriginal(byte x, byte y) { }

            /// <summary>D4 反例用无关 transpiler（签名兼容 IEnumerable&lt;CodeInstruction&gt;）。</summary>
            public static System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction>
                UnrelatedTranspiler(
                    System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction> instructions)
            {
                foreach (var instruction in instructions) yield return instruction;
            }
        }

        /// <summary>D4 反例用错误 prefix：同形状但非预期方法。</summary>
        internal static class D4WrongPrefix
        {
            public static bool WrongPrefix(byte x, byte y) => false;
        }

        /// <summary>D4 反例用正确方法：同形状（用于 priority 反证）。</summary>
        internal static class D4RightPrefix
        {
            public static bool RightPrefix(byte x, byte y) => true;
        }

        /// <summary>R1 fixture：另一条合法 sibling Transpiler（与 dedicated gate 同构，
        /// 签名兼容、owner 相同、默认优先级，但方法不同于 expected）。</summary>
        internal static class R1SiblingTranspiler
        {
            public static System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction>
                SiblingTranspiler(
                    System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction> instructions)
            {
                foreach (var instruction in instructions) yield return instruction;
            }
        }

        /// <summary>R1 fixture：本票 required Transpiler（expected 本体）。</summary>
        internal static class R1RequiredTranspiler
        {
            public static System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction>
                RequiredTranspiler(
                    System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction> instructions)
            {
                foreach (var instruction in instructions) yield return instruction;
            }
        }

        /// <summary>E02 D/§6 反例：真实 Harmony 下 method 错配与 priority 错配各自被拒。</summary>
        internal static bool Test_E02_D4_VerifyRegisteredNegativeProofs()
        {
            string fail = null;
            var harmony = new HarmonyLib.Harmony("e02-d4-negative-proofs");
            MethodInfo original = typeof(D4PatchTarget).GetMethod("ManagedOriginal",
                BindingFlags.Static | BindingFlags.Public);
            MethodInfo wrongMethod = typeof(D4WrongPrefix).GetMethod("WrongPrefix",
                BindingFlags.Static | BindingFlags.Public);
            MethodInfo rightMethod = typeof(D4RightPrefix).GetMethod("RightPrefix",
                BindingFlags.Static | BindingFlags.Public);
            var verify = typeof(SteamP2PFriends.Adapters.Item.Patches.ItemSettlementConsumptionPatch)
                .GetMethod("VerifyRegistered",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (original == null || wrongMethod == null || rightMethod == null || verify == null)
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "e02d4-fail.txt"),
                    "guard: original=" + (original != null) + " wrong=" + (wrongMethod != null)
                    + " right=" + (rightMethod != null) + " verify=" + (verify != null));
                return false;
            }
            // ① 同 owner Prefix 集合内注册**错误方法** → method mismatch
            harmony.Patch(original, prefix: new HarmonyLib.HarmonyMethod(wrongMethod, priority: 400));
            try
            {
                verify.Invoke(null, new object[] { harmony, original, rightMethod,
                    SteamP2PFriends.Adapters.Item.Patches.ItemSettlementConsumptionPatch
                        .EExpectedPatchKind.Prefix });
                fail = "wrong-method not rejected";
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "e02d4-fail.txt"), fail);
            }
            catch (System.Reflection.TargetInvocationException tie)
                when (tie.InnerException != null
                    && tie.InnerException.Message.Contains("not registered")
                    && tie.InnerException.Message.Contains("ownRegistered=[WrongPrefix]"))
            {
            }
            catch (Exception error)
            {
                fail = "case1 unexpected: " + (error is System.Reflection.TargetInvocationException tie
                    ? (tie.InnerException?.Message ?? tie.Message) : error.Message);
            }
            finally
            {
                HarmonyLib.Harmony.UnpatchID(harmony.Id);
            }
            // ② 同 owner 正确方法但 priority 999 → priority mismatch
            if (fail == null)
            {
                try
                {
                    harmony.Patch(original, prefix: new HarmonyLib.HarmonyMethod(rightMethod,
                        priority: 999));
                    try
                    {
                        verify.Invoke(null, new object[] { harmony, original, rightMethod,
                            SteamP2PFriends.Adapters.Item.Patches.ItemSettlementConsumptionPatch
                                .EExpectedPatchKind.Prefix });
                        fail = "wrong-priority not rejected";
                        System.IO.File.WriteAllText(
                            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "e02d4-fail.txt"),
                            fail);
                    }
                    catch (System.Reflection.TargetInvocationException tie)
                        when (tie.InnerException != null
                            && tie.InnerException.Message.Contains("priority mismatch"))
                    {
                    }
                }
                catch (Exception error)
                {
                    fail = "case2 unexpected: " + (error is System.Reflection.TargetInvocationException tie2
                        ? (tie2.InnerException?.Message ?? tie2.Message) : error.Message);
                }
                finally
                {
                    HarmonyLib.Harmony.UnpatchID(harmony.Id);
                }
            }
            // ③ 同 owner 正确 prefix + **无关 transpiler 共存** → 不得提前 throw，
            //    VerifyRegistered 仍按 prefix 集合正确返回
            if (fail == null)
            {
                try
                {
                    MethodInfo transpilerMethod = typeof(D4PatchTarget).GetMethod(
                        nameof(D4PatchTarget.UnrelatedTranspiler),
                        BindingFlags.Static | BindingFlags.Public);
                    MethodInfo prefixMethod = typeof(D4RightPrefix).GetMethod("RightPrefix",
                        BindingFlags.Static | BindingFlags.Public);
                    harmony.Patch(original,
                        prefix: new HarmonyLib.HarmonyMethod(prefixMethod, priority: 400),
                        transpiler: new HarmonyLib.HarmonyMethod(transpilerMethod, priority: 400));
                    verify.Invoke(null, new object[] { harmony, original, prefixMethod,
                        SteamP2PFriends.Adapters.Item.Patches.ItemSettlementConsumptionPatch
                            .EExpectedPatchKind.Prefix });
                    // 通过 = 无关 transpiler 共存不影响 prefix 校验 ✓
                }
                catch (Exception error)
                {
                    fail = "case3 unexpected: " + (error is System.Reflection.TargetInvocationException tie3
                        ? (tie3.InnerException?.Message ?? tie3.Message) : error.Message);
                }
                finally
                {
                    HarmonyLib.Harmony.UnpatchID(harmony.Id);
                }
            }
            if (fail != null)
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "e02d4-fail.txt"), fail);
            return fail == null;
        }

        /// <summary>R1（r12 规范）：同 target/同 owner/同 kind 装两条合法 Transpiler，
        /// sibling 无论登记次序先后，required 校验都必须通过；对应生产链
        /// PatchRegistrationLegacyDiagnostics 先 dedicated gate 后 settlement 的真实顺序。</summary>
        internal static bool Test_R1_VerifyRegisteredAllowsSameOwnerSiblingTranspilers()
        {
            string fail = null;
            var harmony = new HarmonyLib.Harmony("r1-sibling-coexist");
            MethodInfo original = typeof(D4PatchTarget).GetMethod("ManagedOriginal",
                BindingFlags.Static | BindingFlags.Public);
            MethodInfo sibling = typeof(R1SiblingTranspiler).GetMethod("SiblingTranspiler",
                BindingFlags.Static | BindingFlags.Public);
            MethodInfo required = typeof(R1RequiredTranspiler).GetMethod("RequiredTranspiler",
                BindingFlags.Static | BindingFlags.Public);
            var verify = typeof(SteamP2PFriends.Adapters.Item.Patches.ItemSettlementConsumptionPatch)
                .GetMethod("VerifyRegistered",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (original == null || sibling == null || required == null || verify == null)
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "r1-fail.txt"),
                    "guard: original=" + (original != null) + " sibling=" + (sibling != null)
                    + " required=" + (required != null) + " verify=" + (verify != null));
                return false;
            }
            // ① sibling 先注册（生产顺序：dedicated gate 先于 settlement），required 后注册 →
            //    当前 VerifyRegistered 遍历先遇到 sibling 即误抛 method mismatch
            try
            {
                harmony.Patch(original, transpiler: new HarmonyLib.HarmonyMethod(sibling));
                harmony.Patch(original, transpiler: new HarmonyLib.HarmonyMethod(required));
                verify.Invoke(null, new object[] { harmony, original, required,
                    SteamP2PFriends.Adapters.Item.Patches.ItemSettlementConsumptionPatch
                        .EExpectedPatchKind.Transpiler });
            }
            catch (Exception error)
            {
                fail = "sibling-first unexpected: " + (error is System.Reflection.TargetInvocationException tie
                    ? (tie.InnerException?.Message ?? tie.Message) : error.Message);
            }
            finally
            {
                HarmonyLib.Harmony.UnpatchID(harmony.Id);
            }
            // ② 交换次序：required 先注册，sibling 后注册 → 同样必须通过
            if (fail == null)
            {
                try
                {
                    harmony.Patch(original, transpiler: new HarmonyLib.HarmonyMethod(required));
                    harmony.Patch(original, transpiler: new HarmonyLib.HarmonyMethod(sibling));
                    verify.Invoke(null, new object[] { harmony, original, required,
                        SteamP2PFriends.Adapters.Item.Patches.ItemSettlementConsumptionPatch
                            .EExpectedPatchKind.Transpiler });
                }
                catch (Exception error)
                {
                    fail = "required-first unexpected: " + (error is System.Reflection.TargetInvocationException tie2
                        ? (tie2.InnerException?.Message ?? tie2.Message) : error.Message);
                }
                finally
                {
                    HarmonyLib.Harmony.UnpatchID(harmony.Id);
                }
            }
            if (fail != null)
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "r1-fail.txt"), fail);
            return fail == null;
        }

        /// <summary>R2A：真实生产 reader 的模式读取映射。外部 static（Dedicator/Provider/
        /// HostManager）经反射设置真实字段；writer 来源由协调器真实状态驱动——SP 形态
        /// （writer=false、server=true、p2p=false）必须是非 Candidate，不得因 reader
        /// 硬编码 writerOwnsSession:true 而误判 owned。</summary>
        internal static bool Test_R2A_ReadModeWriterSource()
        {
            string fail = null;
            string trace = "";
            FieldInfo dedicated = typeof(SDG.Unturned.Dedicator).GetField("_isDedicated",
                BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo server = typeof(SDG.Unturned.Provider).GetField("_isServer",
                BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo hostMode = typeof(SteamP2PFriends.Host.HostManager).GetField("_hostMode",
                BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo p2p = typeof(SteamP2PFriends.Host.HostManager).GetField(
                "<IsP2PServerActive>k__BackingField",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (dedicated == null || server == null || hostMode == null || p2p == null)
                return false;
            object savedDedicated = dedicated.GetValue(null);
            object savedServer = server.GetValue(null);
            object savedHostMode = hostMode.GetValue(null);
            object savedP2p = p2p.GetValue(null);
            bool savedWriter = MultiObserverShadowCoordinator.ItemAuthorityWriterOwnsSession;
            var reader = new SteamP2PFriends.Adapters.Item.NativeItemStepFiveReader();
            try
            {
                MultiObserverShadowCoordinator.ReleaseItemAuthorityWriter();
                // ① SP 形态：writer=false、server=true、p2p=false → 非 Candidate
                dedicated.SetValue(null, false);
                server.SetValue(null, true);
                hostMode.SetValue(null, Enum.Parse(hostMode.FieldType, "None"));
                p2p.SetValue(null, false);
                SteamP2PFriends.Adapters.Item.ItemModeRaw sp =
                    reader.ReadMode();
                trace += "sp(writer=" + sp.WriterOwnsSession + " cand=" + sp.IsCandidate + ")";
                if (sp.IsCandidate || sp.WriterOwnsSession)
                    fail = "sp misjudged candidate: " + trace;
                // ② owned 正向：writer=true（真实 TrySelectItemAuthorityWriter）→ Candidate
                if (fail == null)
                {
                    bool selected = MultiObserverShadowCoordinator.TrySelectItemAuthorityWriter(
                        1111UL, "r2a|srv|PEI");
                    SteamP2PFriends.Adapters.Item.ItemModeRaw owned =
                        reader.ReadMode();
                    trace += " owned(selected=" + selected + " writer=" + owned.WriterOwnsSession
                        + " cand=" + owned.IsCandidate + ")";
                    if (!selected || !owned.WriterOwnsSession || !owned.IsCandidate)
                        fail = "owned positive broken: " + trace;
                }
            }
            finally
            {
                MultiObserverShadowCoordinator.ReleaseItemAuthorityWriter();
                dedicated.SetValue(null, savedDedicated);
                server.SetValue(null, savedServer);
                hostMode.SetValue(null, savedHostMode);
                p2p.SetValue(null, savedP2p);
                if (savedWriter && !MultiObserverShadowCoordinator.ItemAuthorityWriterOwnsSession)
                    MultiObserverShadowCoordinator.TrySelectItemAuthorityWriter(1111UL, "r2a|srv|PEI");
            }
            if (fail != null)
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "r2a-fail.txt"), fail);
            return fail == null;
        }

        /// <summary>R2B：真实 OnRegionUpdated_Prefix 的 step5 模式分流边界。owned
        /// (Candidate) 下 player/投影缺失（player=null）必须等待并抑制原生（return
        /// false），不得因外层 loadedRegions 条件不满足而放行；SP(non-Candidate) 保持
        /// 旧透传语义（return true）。模式经 R2A 真实 static 状态驱动生产 ReadMode。</summary>
        internal static bool Test_R2B_PrefixStepFiveModeSplit()
        {
            string fail = null;
            string trace = "";
            FieldInfo dedicated = typeof(SDG.Unturned.Dedicator).GetField("_isDedicated",
                BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo server = typeof(SDG.Unturned.Provider).GetField("_isServer",
                BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo hostMode = typeof(SteamP2PFriends.Host.HostManager).GetField("_hostMode",
                BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo p2p = typeof(SteamP2PFriends.Host.HostManager).GetField(
                "<IsP2PServerActive>k__BackingField",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (dedicated == null || server == null || hostMode == null || p2p == null)
                return false;
            object savedDedicated = dedicated.GetValue(null);
            object savedServer = server.GetValue(null);
            object savedHostMode = hostMode.GetValue(null);
            object savedP2p = p2p.GetValue(null);
            var patch = new ItemManagerRegionSyncPatch_Shim();
            bool savedWriter = MultiObserverShadowCoordinator.ItemAuthorityWriterOwnsSession;
            try
            {
                MultiObserverShadowCoordinator.ReleaseItemAuthorityWriter();
                dedicated.SetValue(null, false);
                server.SetValue(null, true);
                hostMode.SetValue(null, Enum.Parse(hostMode.FieldType, "None"));
                p2p.SetValue(null, false);
                // ① SP 形态：non-Candidate → 旧透传语义保留（return true）
                bool spResult = patch.InvokeStepFive(null, 2, 2);
                trace += "sp=" + spResult;
                if (!spResult) fail = "sp passthrough broken: " + trace;
                // ② owned 形态：真实 TrySelectItemAuthorityWriter → Candidate；
                //    player=null（投影缺失）→ 等待并抑制原生（return false）
                if (fail == null)
                {
                    bool selected = MultiObserverShadowCoordinator.TrySelectItemAuthorityWriter(
                        2222UL, "r2b|srv|PEI");
                    bool ownedResult = patch.InvokeStepFive(null, 2, 2);
                    trace += " owned(selected=" + selected + ")=" + ownedResult;
                    if (selected && ownedResult)
                        fail = "owned missing-projection ran original: " + trace;
                }
            }
            finally
            {
                MultiObserverShadowCoordinator.ReleaseItemAuthorityWriter();
                dedicated.SetValue(null, savedDedicated);
                server.SetValue(null, savedServer);
                hostMode.SetValue(null, savedHostMode);
                p2p.SetValue(null, savedP2p);
                if (savedWriter && !MultiObserverShadowCoordinator.ItemAuthorityWriterOwnsSession)
                    MultiObserverShadowCoordinator.TrySelectItemAuthorityWriter(2222UL, "r2b|srv|PEI");
            }
            if (fail != null)
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "r2b-fail.txt"), fail);
            return fail == null;
        }

        /// <summary>R2B shim：直接反射调用真实 OnRegionUpdated_Prefix 的 step5 路径
        /// （真实生产 wrapper 方法本体，无 fake 政策）；state/canIncrementIndex 按参数
        /// 原样传入并在调用后 Dispose。</summary>
        internal sealed class ItemManagerRegionSyncPatch_Shim
        {
            private readonly MethodInfo _prefix = typeof(
                SteamP2PFriends.Adapters.Item.Patches.ItemManagerRegionSyncPatch)
                .GetMethod("OnRegionUpdated_Prefix", BindingFlags.Static | BindingFlags.Public);

            internal bool InvokeStepFive(object player, byte x, byte y)
            {
                bool canIncrement = true;
                System.IDisposable state = null;
                try
                {
                    return (bool)_prefix.Invoke(null, new object[]
                        { player, x, y, (byte)5, canIncrement, state });
                }
                finally
                {
                    state?.Dispose();
                }
            }
        }

        /// <summary>R2C：真实增删 wrapper 的模式分流与共同 dispatch 分类。wrapper
        /// （ReceiveItem_Prefix/ReceiveDestroyItem_Prefix）直调实际方法本体；模式经
        /// R2A 真实 static 状态驱动生产 ReadMode；view/raw pending 用接口替身提供
        /// raw 外部事实（真实 ItemManager 静态面在测试 host 不可加载），分类与取消
        /// 顺序全部由生产 dispatch 执行。</summary>
        internal static bool Test_R2C_WrapperModeSplitAndDispatchClassification()
        {
            string fail = null;
            string trace = "";
            FieldInfo dedicated = typeof(SDG.Unturned.Dedicator).GetField("_isDedicated",
                BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo server = typeof(SDG.Unturned.Provider).GetField("_isServer",
                BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo hostMode = typeof(SteamP2PFriends.Host.HostManager).GetField("_hostMode",
                BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo p2p = typeof(SteamP2PFriends.Host.HostManager).GetField(
                "<IsP2PServerActive>k__BackingField",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (dedicated == null || server == null || hostMode == null || p2p == null)
                return false;
            object savedDedicated = dedicated.GetValue(null);
            object savedServer = server.GetValue(null);
            object savedHostMode = hostMode.GetValue(null);
            object savedP2p = p2p.GetValue(null);
            var wrapper = new ItemOwnedRuntimePatch_Shim();
            bool savedWriter = MultiObserverShadowCoordinator.ItemAuthorityWriterOwnsSession;
            try
            {
                MultiObserverShadowCoordinator.ReleaseItemAuthorityWriter();
                dedicated.SetValue(null, false);
                server.SetValue(null, true);
                hostMode.SetValue(null, Enum.Parse(hostMode.FieldType, "None"));
                p2p.SetValue(null, false);
                // ① SP(non-Candidate) 增删 wrapper 原样透传（return true）
                bool spInc = wrapper.InvokeReceiveItem();
                bool spDst = wrapper.InvokeReceiveDestroy();
                trace += "sp(inc=" + spInc + ",dst=" + spDst + ")";
                if (!spInc || !spDst) fail = "sp passthrough broken: " + trace;
                // ② owned(Candidate)：真实 TrySelectItemAuthorityWriter；owned 事件
                //    的增量由 wrapper→共同 dispatch 接管（非 RunOriginal 即抑制原生）；
                //    已知未 networked 的细分类在 Test_R2C_DispatchIncrementClassification
                //    用真实外部 view 数据分别断言
                if (fail == null)
                {
                    bool selected = MultiObserverShadowCoordinator.TrySelectItemAuthorityWriter(
                        3333UL, "r2c|srv|PEI");
                    bool ownedInc = wrapper.InvokeReceiveItem();
                    trace += " ownedInc(handled)=" + ownedInc;
                    if (selected && ownedInc)
                        fail = "owned increment ran original append: " + trace;
                }
            }
            finally
            {
                MultiObserverShadowCoordinator.ReleaseItemAuthorityWriter();
                dedicated.SetValue(null, savedDedicated);
                server.SetValue(null, savedServer);
                hostMode.SetValue(null, savedHostMode);
                p2p.SetValue(null, savedP2p);
                if (savedWriter && !MultiObserverShadowCoordinator.ItemAuthorityWriterOwnsSession)
                    MultiObserverShadowCoordinator.TrySelectItemAuthorityWriter(3333UL, "r2c|srv|PEI");
            }
            if (fail != null)
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "r2c-fail.txt"), fail);
            return fail == null;
        }

        /// <summary>R2C 删除顺序：真实 DispatchDestroy（生产分类/取消顺序）+ 接口替身
        /// 提供外部事实——目标 ID 同时有 model+pending 时两者均清除（不回原生）；
        /// view region 读取抛错时 raw pending 已先撤（顺序由生产代码执行）。</summary>
        internal static bool Test_R2C_DestroyModelReadFailureWaitsAfterCancel()
        {
            var harness = new FixtureHarness();
            harness.Native.ModeReader = () => new ItemModeRaw(
                dedicated: false, isServer: true, p2p: true, writerOwnsSession: true);
            var raw = (System.Collections.IList)harness.RawPending;
            Type entryType = raw.GetType().GetGenericArguments()[0];
            foreach (uint id in new uint[] { 5155U, 5156U })
            {
                object entry = Activator.CreateInstance(entryType);
                entryType.GetField("region_x").SetValue(entry, (byte)5);
                entryType.GetField("region_y").SetValue(entry, (byte)5);
                entryType.GetField("instanceID").SetValue(entry, id);
                raw.Add(entry);
            }
            harness.View.ModelsReader = (x, y) =>
                throw new InvalidOperationException("model read failed");
            bool escaped = false;
            ItemStepFiveResult result = ItemStepFiveResult.Accepted;
            try
            {
                result = ItemLocalPresentationDispatch.DispatchDestroy(
                    harness.Native, harness.View, 5, 5, 5155U, false, raw);
            }
            catch { escaped = true; }
            return !escaped && result == ItemStepFiveResult.Waiting && raw.Count == 1
                && (uint)entryType.GetField("instanceID").GetValue(raw[0]) == 5156U;
        }

        internal static bool Test_R2C_DestroyPartialModelFailureWaitsWithoutReplay()
        {
            var harness = new FixtureHarness();
            harness.Native.ModeReader = () => new ItemModeRaw(
                dedicated: false, isServer: true, p2p: true, writerOwnsSession: true);
            var raw = (System.Collections.IList)harness.RawPending;
            Type entryType = raw.GetType().GetGenericArguments()[0];
            foreach (uint id in new uint[] { 5157U, 5158U })
            {
                object entry = Activator.CreateInstance(entryType);
                entryType.GetField("region_x").SetValue(entry, (byte)5);
                entryType.GetField("region_y").SetValue(entry, (byte)5);
                entryType.GetField("instanceID").SetValue(entry, id);
                raw.Add(entry);
            }
            harness.View.ModelsByRegion[(5, 5)] = new List<ModelView>
            {
                new ModelView { InstanceId = 5157U, Handle = new object() },
                new ModelView { InstanceId = 5159U, Handle = new object() }
            };
            int notified = 0, destroyAttempts = 0;
            harness.View.RemovedAction = _ => notified++;
            harness.View.DestroyAction = _ =>
            {
                destroyAttempts++;
                throw new InvalidOperationException("unity destroy failed");
            };
            ItemStepFiveResult result = ItemLocalPresentationDispatch.DispatchDestroy(
                harness.Native, harness.View, 5, 5, 5157U, false, raw);
            int targetModels = 0, otherModels = 0;
            foreach (ModelView model in harness.View.ModelsByRegion[(5, 5)])
                if (model.InstanceId == 5157U) targetModels++;
                else if (model.InstanceId == 5159U) otherModels++;
            return result == ItemStepFiveResult.Waiting && notified == 1
                && destroyAttempts == 1 && raw.Count == 1
                && (uint)entryType.GetField("instanceID").GetValue(raw[0]) == 5158U
                && targetModels == 1 && otherModels == 1;
        }

        internal static bool Test_R2C_DestroyOrderAndModelPendingCoexistence()
        {
            string fail = null;
            string trace = "";
            var harness = new FixtureHarness();
            harness.Begin(R01E02Fixture.HostId);
            harness.Native.ModeReader = () => new ItemModeRaw(
                dedicated: false, isServer: true, p2p: true, writerOwnsSession: true);
            byte bx = R01E02Fixture.CenterB;
            // raw pending 与 view pending 各放一份目标 5150；模型区另一 ID 5151 不受影响
            var rawList = (System.Collections.IList)harness.RawPending;
            var paramType = rawList.GetType().GetGenericArguments()[0];
            object rawEntry = System.Activator.CreateInstance(paramType);
            paramType.GetField("region_x").SetValue(rawEntry, (byte)bx);
            paramType.GetField("region_y").SetValue(rawEntry, (byte)bx);
            paramType.GetField("instanceID").SetValue(rawEntry, 5150U);
            rawList.Add(rawEntry);
            harness.View.PendingItems.Add(new PendingView
            {
                RegionX = bx, RegionY = bx, InstanceId = 5150U, AssetId = 276,
                Amount = 1, Quality = 100, State = new byte[0]
            });
            harness.View.ModelsByRegion[(bx, bx)] = new List<ModelView>
            {
                new ModelView { InstanceId = 5150U, Handle = new object() },
                new ModelView { InstanceId = 5151U, Handle = new object() }
            };
            ItemStepFiveResult destroy = ItemLocalPresentationDispatch.DispatchDestroy(
                harness.Native, harness.View, bx, bx, 5150U, false, rawList);
            int modelTarget = 0;
            int modelOther = 0;
            foreach (ModelView model in harness.View.AllModels)
                if (model.InstanceId == 5150U) modelTarget++;
                else if (model.InstanceId == 5151U) modelOther++;
            // view pending 统计走生产视图（PendingReader 指向 raw 底层）
            int viewPending = 0;
            System.Collections.IList viewPendingList = harness.View.Pending;
            foreach (object pending in viewPendingList)
                if (pending is PendingView view && view.InstanceId == 5150U) viewPending++;
            trace = "destroy=" + destroy + " rawPending=" + rawList.Count
                + " viewPending=" + viewPending + " modelTarget=" + modelTarget
                + " modelOther=" + modelOther;
            if (destroy != ItemStepFiveResult.Accepted
                || rawList.Count != 0 || viewPending != 0
                || modelTarget != 0 || modelOther != 1)
                fail = "coexistence cleanup broken: " + trace;
            // fallible region 读取抛错：raw pending 必须已被撤（顺序证据）
            if (fail == null)
            {
                var throwing = new ThrowingRegionView();
                rawEntry = System.Activator.CreateInstance(paramType);
                paramType.GetField("region_x").SetValue(rawEntry, (byte)bx);
                paramType.GetField("region_y").SetValue(rawEntry, (byte)bx);
                paramType.GetField("instanceID").SetValue(rawEntry, 5152U);
                rawList.Add(rawEntry);
                ItemStepFiveResult waiting = ItemLocalPresentationDispatch.DispatchDestroy(
                    harness.Native, throwing, bx, bx, 5152U, false, rawList);
                trace += "; throwCase(waiting=" + waiting + " rawPending=" + rawList.Count + ")";
                if (waiting != ItemStepFiveResult.Waiting || rawList.Count != 0)
                    fail = "fallible-read order broken: " + trace;
            }
            // raw 取消本身抛错：收束为 Waiting+诊断，不继续模型清理（模型保留）
            if (fail == null)
            {
                harness.View.ModelsByRegion[(bx, bx)] = new List<ModelView>
                {
                    new ModelView { InstanceId = 5153U, Handle = new object() }
                };
                int modelBefore = 0;
                foreach (ModelView model in harness.View.AllModels)
                    if (model.InstanceId == 5153U) modelBefore++;
                ItemStepFiveResult cancelFail = ItemLocalPresentationDispatch.DispatchDestroy(
                    harness.Native, harness.View, bx, bx, 5153U, false, new ThrowingRemoveList());
                int modelAfter = 0;
                foreach (ModelView model in harness.View.AllModels)
                    if (model.InstanceId == 5153U) modelAfter++;
                trace += "; cancelFail(" + cancelFail + " modelBefore=" + modelBefore
                    + " modelAfter=" + modelAfter + ")";
                if (cancelFail != ItemStepFiveResult.Waiting
                    || modelAfter != modelBefore || modelAfter != 1)
                    fail = "cancel failure escaped owned event: " + trace;
            }
            // view null（非已知清理完成）→ Waiting
            if (fail == null)
            {
                ItemStepFiveResult nullView = ItemLocalPresentationDispatch.DispatchDestroy(
                    harness.Native, null, bx, bx, 5154U, false, rawList);
                trace += "; nullView(" + nullView + ")";
                if (nullView != ItemStepFiveResult.Waiting)
                    fail = "null view treated as known-complete: " + trace;
            }
            if (fail != null)
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "r2c2-fail.txt"), fail);
            return fail == null;
        }

        /// <summary>R2C：RemoveAt 抛错的 raw pending 替身（取消执行失败的外部事实）。
        /// 索引器返回真实 ItemInstantiationParameters（instanceID=5153），使生产
        /// CancelPendingForInstance 走到 RemoveAt 才抛错。</summary>
        internal sealed class ThrowingRemoveList : System.Collections.IList
        {
            private readonly object _entry;
            internal ThrowingRemoveList()
            {
                _entry = System.Activator.CreateInstance(
                    typeof(SDG.Unturned.ItemManager).Assembly
                        .GetType("SDG.Unturned.ItemInstantiationParameters", true));
                _entry.GetType().GetField("region_x").SetValue(_entry, (byte)0);
                _entry.GetType().GetField("region_y").SetValue(_entry, (byte)0);
                _entry.GetType().GetField("instanceID").SetValue(_entry, 5153U);
            }
            public int Count => 1;
            public bool IsFixedSize => false;
            public bool IsReadOnly => false;
            public bool IsSynchronized => false;
            public object SyncRoot => this;
            public object this[int index]
            {
                get => _entry;
                set { }
            }
            public int Add(object value) => 0;
            public void Clear() { }
            public bool Contains(object value) => false;
            public int IndexOf(object value) => -1;
            public void Insert(int index, object value) { }
            public void Remove(object value) { }
            public void RemoveAt(int index)
            {
                throw new InvalidOperationException("pending remove failed");
            }
            public void CopyTo(System.Array array, int index) { }
            public System.Collections.IEnumerator GetEnumerator()
            {
                yield return _entry;
            }
        }

        /// <summary>R2C：RegionReady 读取抛错的 view 替身（外部事实）。</summary>
        internal sealed class ThrowingRegionView : IItemLocalPresentationView
        {
            public bool RegionReady(byte x, byte y)
            {
                throw new InvalidOperationException("region read failed");
            }
            public IReadOnlyList<ItemView> ReadCurrentItems(byte x, byte y) =>
                new List<ItemView>();
            public System.Collections.IList Pending => new List<PendingView>();
            public System.Collections.IList Models(byte x, byte y) => new List<ModelView>();
            public bool GetNetworked(byte x, byte y) => false;
            public void SetNetworked(byte x, byte y, bool value) { }
            public bool ProjectionAvailable(byte x, byte y) => false;
            public void SetLoaded(byte x, byte y, bool value) { }
            public float ItemSortOrder(ItemView item) => 0f;
            public void DestroyUnity(object modelHandle) { }
            public void NotifyRemoved(ModelView model) { }
            public void PlayRemovalEffect(ModelView model) { }
            public void FinishDeferredDestroy(byte x, byte y) { }
        }

        /// <summary>R2C shim：反射直调真实 ReceiveItem_Prefix / ReceiveDestroyItem_Prefix
        /// 方法本体（生产 wrapper，无 fake 政策）。异常原样透出供测试观察。</summary>
        internal sealed class ItemOwnedRuntimePatch_Shim
        {
            private readonly MethodInfo _increment = typeof(
                SteamP2PFriends.Adapters.Item.Patches.ItemOwnedPresentationRuntimePatch)
                .GetMethod("ReceiveItem_Prefix", BindingFlags.Static | BindingFlags.Public);
            private readonly MethodInfo _destroy = typeof(
                SteamP2PFriends.Adapters.Item.Patches.ItemOwnedPresentationRuntimePatch)
                .GetMethod("ReceiveDestroyItem_Prefix", BindingFlags.Static | BindingFlags.Public);

            internal bool InvokeReceiveItem()
            {
                return (bool)_increment.Invoke(null, new object[]
                    { (byte)5, (byte)5, (ushort)16, (byte)1, (byte)100, new byte[0],
                      UnityEngine.Vector3.zero, 4242U, false });
            }

            internal bool InvokeReceiveDestroy()
            {
                return (bool)_destroy.Invoke(null, new object[]
                    { (byte)5, (byte)5, 4242U, false });
            }
        }

        /// <summary>R2C：生产 DispatchIncrement 四路分类——已知 networked=true 入队、
        /// 已知 false 不排队不回原生（Accepted/snapshot 接续）、读取抛错 Waiting、
        /// null view Waiting（不可折成 known-false）。外部 view 数据由替身提供，
        /// 分类全部由生产 dispatch 执行。</summary>
        internal static bool Test_R2C_DispatchIncrementClassification()
        {
            string fail = null;
            string trace = "";
            var native = new RawNative
            {
                ModeReader = () => new ItemModeRaw(
                    dedicated: false, isServer: true, p2p: true, writerOwnsSession: true)
            };
            var harness = new FixtureHarness();
            byte bx = R01E02Fixture.CenterB;
            // ① 已知 networked=true → Accepted 且入队（真实 EnqueueIncrement 走
            //    view.GetNetworked=true 分支，pending+1）
            var networked = new TestView { Storage = harness.Bridge.Storage };
            harness.Bridge.Storage.Regions[bx, bx].isNetworked = true;
            ItemStepFiveResult incTrue = ItemLocalPresentationDispatch.DispatchIncrement(
                native, networked, bx, bx, 16, 1, 100, new byte[0],
                UnityEngine.Vector3.zero, 6101U, false);
            int queuedTrue = 0;
            foreach (PendingView pending in networked.Pending)
                if (pending.InstanceId == 6101U) queuedTrue++;
            // ② 已知 networked=false → Accepted（不排队、不回原生）
            var notNetworked = new TestView { Storage = harness.Bridge.Storage };
            harness.Bridge.Storage.Regions[bx, bx].isNetworked = false;
            ItemStepFiveResult incFalse = ItemLocalPresentationDispatch.DispatchIncrement(
                native, notNetworked, bx, bx, 16, 1, 100, new byte[0],
                UnityEngine.Vector3.zero, 6102U, false);
            int queuedFalse = 0;
            foreach (PendingView pending in notNetworked.Pending)
                if (pending.InstanceId == 6102U) queuedFalse++;
            // ③ 读取抛错 → Waiting
            var throwing = new ThrowingNetworkedView();
            ItemStepFiveResult incThrow = ItemLocalPresentationDispatch.DispatchIncrement(
                native, throwing, bx, bx, 16, 1, 100, new byte[0],
                UnityEngine.Vector3.zero, 6103U, false);
            // ④ null view → Waiting（非 known-false）
            ItemStepFiveResult incNull = ItemLocalPresentationDispatch.DispatchIncrement(
                native, null, bx, bx, 16, 1, 100, new byte[0],
                UnityEngine.Vector3.zero, 6104U, false);
            trace = "true=" + incTrue + "/q" + queuedTrue
                + " false=" + incFalse + "/q" + queuedFalse
                + " throw=" + incThrow + " null=" + incNull;
            if (incTrue != ItemStepFiveResult.Accepted || queuedTrue != 1
                || incFalse != ItemStepFiveResult.Accepted || queuedFalse != 0
                || incThrow != ItemStepFiveResult.Waiting
                || incNull != ItemStepFiveResult.Waiting)
                fail = "classification broken: " + trace;
            if (fail != null)
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "r2c3-fail.txt"), fail);
            return fail == null;
        }

        /// <summary>R2C：GetNetworked 读取抛错的 view 替身（外部事实）。</summary>
        internal sealed class ThrowingNetworkedView : IItemLocalPresentationView
        {
            public bool RegionReady(byte x, byte y) => true;
            public IReadOnlyList<ItemView> ReadCurrentItems(byte x, byte y) =>
                new List<ItemView>();
            public System.Collections.IList Pending => new List<PendingView>();
            public System.Collections.IList Models(byte x, byte y) => new List<ModelView>();
            public bool GetNetworked(byte x, byte y)
            {
                throw new System.InvalidOperationException("networked read failed");
            }
            public void SetNetworked(byte x, byte y, bool value) { }
            public bool ProjectionAvailable(byte x, byte y) => false;
            public void SetLoaded(byte x, byte y, bool value) { }
            public float ItemSortOrder(ItemView item) => 0f;
            public void DestroyUnity(object modelHandle) { }
            public void NotifyRemoved(ModelView model) { }
            public void PlayRemovalEffect(ModelView model) { }
            public void FinishDeferredDestroy(byte x, byte y) { }
        }

        /// <summary>E02 §6/D 片：双 shape 探测——真实客户端 Lib、合成专用服形态、半缺成员。</summary>
        internal static bool Test_E02_D1_PendingBatchShapeMatrix()
        {
            string fail = null;
            var patchType = typeof(SteamP2PFriends.Adapters.Item.Patches.ItemSettlementConsumptionPatch);
            var inspect = patchType.GetMethod("InspectPendingBatchShape",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            var shapeEnum = typeof(SteamP2PFriends.Adapters.Item.Patches.ItemSettlementConsumptionPatch)
                .GetNestedType("PendingBatchShape", BindingFlags.NonPublic | BindingFlags.Public);
            // ① 真实客户端 Lib → Client
            object real = null;
            if (fail == null)
            {
                real = inspect.Invoke(null, new object[] { typeof(ItemManager) });
                if (!Equals(real, Enum.Parse(shapeEnum, "Client")))
                    fail = "real-shape=" + real;
            }
            // ② 合成：有 pending 无 timer/无 spawn → DedicatedWithoutBatch（合成类型需
            // pending+spawn+update 三者齐而 timer 缺失；合成 Update/spawn 签名匹配）
            if (fail == null)
            {
                var asm = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
                    new AssemblyName("E02ShapeSynth"), System.Reflection.Emit.AssemblyBuilderAccess.Run);
                var module = asm.DefineDynamicModule("E02ShapeSynth");
                var type = module.DefineType("SynthDedicated", TypeAttributes.Public);
                var paramType = typeof(ItemManager).Assembly
                    .GetType("SDG.Unturned.ItemInstantiationParameters", true);
                var pending = type.DefineField("pendingInstantiations",
                    typeof(List<>).MakeGenericType(paramType),
                    FieldAttributes.Static | FieldAttributes.Public);
                var spawn = type.DefineMethod("spawnItem", MethodAttributes.Public,
                    typeof(void), new[] { typeof(byte), typeof(byte), typeof(ushort),
                        typeof(byte), typeof(byte), typeof(byte[]),
                        typeof(UnityEngine.Vector3), typeof(uint), typeof(bool) });
                spawn.GetILGenerator().Emit(System.Reflection.Emit.OpCodes.Ret);
                var update = type.DefineMethod("Update", MethodAttributes.Public,
                    typeof(void), Type.EmptyTypes);
                update.GetILGenerator().Emit(System.Reflection.Emit.OpCodes.Ret);
                var built = type.CreateType();
                object synth = inspect.Invoke(null, new object[] { built });
                if (!Equals(synth, Enum.Parse(shapeEnum, "DedicatedWithoutBatch")))
                    fail = "synth-shape=" + synth;
            }
            // ③ 半缺（有 pending 有 timer 无 spawn）→ Unsupported
            if (fail == null)
            {
                var asm = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
                    new AssemblyName("E02ShapeSynth2"), System.Reflection.Emit.AssemblyBuilderAccess.Run);
                var module = asm.DefineDynamicModule("E02ShapeSynth2");
                var type = module.DefineType("SynthMixed", TypeAttributes.Public);
                type.DefineField("pendingInstantiations",
                    typeof(List<object>), FieldAttributes.Static | FieldAttributes.Public);
                type.DefineField("instantiationTimer", typeof(System.Diagnostics.Stopwatch),
                    FieldAttributes.Public);
                var built = type.CreateType();
                object mixed = inspect.Invoke(null, new object[] { built });
                if (!Equals(mixed, Enum.Parse(shapeEnum, "Unsupported")))
                    fail = "mixed-shape=" + mixed;
            }
            if (fail != null)
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "e02d1-fail.txt"), fail);
            return fail == null;
        }

        /// <summary>E02 §8.3 全链：真实 MethodInfo 外部作用后抛 → 共同 consumer Uncertain →
        /// 实际 Mark → 下一帧同 consumer 该 ID Invoke 计数不增；另一 region 合法项继续；
        /// 换 token 重绑不解除；C3a 保留 settlement 集合子证据。</summary>
        internal static bool Test_E02_C3_RegionInstanceQuarantineGate()
        {
            string fail = null;
            // 子证据：settlement 集合行为（新 scope 语义：Mark/Query 同 scope 引用命中）
            var settlement = new ItemReturnSettlement(
                null, new ItemMetadataStore(), _ => 0, () => 0f, () => 64,
                _ => false, _ => 0U, () => 1UL);
            object subScope = new object();
            settlement.MarkUnconfirmedSideEffect(1000UL, 101UL, new RegionKey(5, 5), 4001U, subScope);
            if (fail == null && !settlement.HasUnconfirmedSideEffect(new RegionKey(5, 5), 4001U, subScope))
                fail = "region/instance+scope query missing marked entry";
            if (fail == null && settlement.HasUnconfirmedSideEffect(new RegionKey(5, 6), 4001U, subScope))
                fail = "other region falsely quarantined";
            // query scope=null → owned 暂缓（true），legacy 2 参记录匹配按 region/instance
            if (fail == null && !settlement.HasUnconfirmedSideEffect(new RegionKey(5, 6), 4002U))
                fail = "query scope null did not defer";
            settlement.ResetIdentity();
            if (fail == null && !settlement.HasUnconfirmedSideEffect(new RegionKey(5, 5), 4001U, subScope))
                fail = "ResetIdentity released quarantine";
            // 全链：初始队列明确含 A/B；两帧均走共同 consumer；consumer 自己 Mark；
            // 下一帧 A 0 次、B 正常登记一次且 pendingB 提交；身份重绑后实际调 consumer。
            if (fail == null)
            {
                var harness = new FixtureHarness();
                harness.Begin(R01E02Fixture.HostId);
                ulong token = harness.StageAndCommitIdentity(
                    R01E02Fixture.HostId, harness.HostOwner, harness.HostTransport);
                harness.Native.ModeReader = harness.ReadHostMode;
                harness.Native.BindingReader = harness.ReadHostBinding;
                byte bx = R01E02Fixture.CenterB;
                MethodInfo spawn = typeof(SpawnReceiver).GetMethod("SpawnItem",
                    BindingFlags.Instance | BindingFlags.Public);
                // 专家修复：真实共享登记在前（PresentShared 重建空队列为合法初始态），
                // 权威增量随后入队，不再被首次共享撤销
                harness.Seam.UpdateObserver(R01E02Fixture.HostId, token, bx, bx, true);
                harness.Seam.Flush(0f);
                if (!harness.Seam.TryGetConnectionGeneration(R01E02Fixture.HostId, out ulong core)
                    || core != token) fail = "core token mismatch";
                if (fail == null && !harness.View.GetNetworked(bx, bx))
                    fail = "shared networked=false after first present";
                if (fail == null && CountPendingOf(harness, 4001U) != 0)
                    fail = "pre-increment pendingA!=0";
                // 原生事件的当前权威 ItemData A 写入同一 Storage（不是手填 pending）
                if (fail == null)
                {
                    harness.Bridge.Storage.Regions[bx, bx].items.Add(
                        R01E02Fixture.MakeItem(4001U, 276, 1, 100));
                    ItemStepFiveResult incA = ItemLocalPresentationDispatch.DispatchIncrement(
                        harness.Native, harness.View, bx, bx, 276, 1, 100,
                        new byte[0], UnityEngine.Vector3.zero, 4001U, false);
                    if (incA != ItemStepFiveResult.Accepted) fail = "increment A=" + incA;
                }
                if (fail == null && CountPendingOf(harness, 4001U) != 1)
                    fail = "post-increment pendingA=" + CountPendingOf(harness, 4001U);
                if (fail == null && ItemSettlementGate.BlocksRegionForObserver(
                    R01E02Fixture.HostId, token, bx, bx))
                    fail = "gate blocked A before consume";
                // 首帧真实 consumer：外部作用后抛无关联 → Uncertain → consumer 自行 Mark
                var aReceiver = new SpawnReceiver(harness.View)
                {
                    ThrowAfterAction = true, SkipRegistration = true
                };
                ConsumeBatchResult first = default(ConsumeBatchResult);
                if (fail == null)
                {
                    first = ItemLocalPresentationDispatch.ConsumeOwnedBatch(
                        harness.Native, harness.HostPlayer, harness.Seam, harness.View,
                        spawn, aReceiver, () => true, 1,
                        (System.Collections.IList)harness.RawPending);
                    if (first.Stop != EConsumeOutcome.Uncertain
                        || first.Consumed != 0
                        || first.StoppedItem == null
                        || first.StoppedItem.InstanceId != 4001U
                        || aReceiver.Invocations != 1)
                        fail = "first-frame stop=" + first.Stop
                            + " aInvocations=" + aReceiver.Invocations
                            + " stopped=" + (first.StoppedItem == null
                                ? "null" : first.StoppedItem.InstanceId.ToString());
                    if (fail != null)
                        System.IO.File.WriteAllText(
                            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "e02c3-fail.txt"),
                            fail);
                }
                if (fail == null && !ItemSettlementGate.UnconfirmedSideEffectForRegion(bx, bx, 4001U))
                {
                    fail = "consumer did not mark uncertain side effect"
                        + " count=" + ItemSettlementGate.UnconfirmedSideEffectCount
                        + " legacy=" + ItemSettlementGate.UnconfirmedSideEffect(
                            R01E02Fixture.HostId, token, 4001U)
                        + " firstStop=" + first.Stop
                        + " stopped=" + (first.StoppedItem == null
                            ? "null" : first.StoppedItem.InstanceId.ToString());
                    System.IO.File.WriteAllText(
                        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "e02c3-fail.txt"), fail);
                    return false;
                }
                // 第二帧：B=4002 在 (5,6)（同属 center(5,5)/radius1 已呈现九区）
                if (fail == null)
                {
                    harness.Bridge.Storage.Regions[bx, (byte)(bx + 1)].items.Add(
                        R01E02Fixture.MakeItem(4002U, 16, 1, 100));
                    ItemStepFiveResult incB = ItemLocalPresentationDispatch.DispatchIncrement(
                        harness.Native, harness.View, bx, (byte)(bx + 1), 16, 1, 100,
                        new byte[0], UnityEngine.Vector3.zero, 4002U, false);
                    if (incB != ItemStepFiveResult.Accepted) fail = "increment B=" + incB;
                }
                if (fail == null)
                {
                    harness.View.ModelsByRegion[(bx, (byte)(bx + 1))] = new List<ModelView>();
                    var bReceiver = new SpawnReceiver(harness.View);
                    ConsumeBatchResult consumed = ItemLocalPresentationDispatch.ConsumeOwnedBatch(
                        harness.Native, harness.HostPlayer, harness.Seam, harness.View,
                        spawn, bReceiver, () => true, 1,
                        (System.Collections.IList)harness.RawPending);
                    if (CountPendingOf(harness, 4001U) != 1)
                        fail = "A not preserved by region/instance gate="
                            + CountPendingOf(harness, 4001U);
                    if (fail == null && (CountPendingOf(harness, 4002U) != 0
                        || bReceiver.Invocations != 1))
                        fail = "B not consumed invocations=" + bReceiver.Invocations
                            + " pendingB=" + CountPendingOf(harness, 4002U)
                            + " stop=" + consumed.Stop + " consumed=" + consumed.Consumed;
                    if (fail != null)
                        System.IO.File.WriteAllText(
                            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "e02c3-fail.txt"),
                            fail);
                }
                // 身份真实换代：新物理 owner/transport → 新 token → 真实 UpdateObserver/Flush；
                // A 的权威输入仍在 Storage，重绑共享按真实仓库恢复 A 的 pending
                if (fail == null)
                {
                    object newOwner = new object();
                    object newTransport = new object();
                    ulong newToken = harness.StageAndCommitIdentity(
                        R01E02Fixture.GuestId, newOwner, newTransport);
                    harness.Native.BindingReader = playerKey =>
                        harness.ReadGuestBindingForOwner(newOwner, R01E02Fixture.GuestId,
                            newTransport);
                    if (newToken == 0UL || newToken == token) fail = "rebind token not advanced";
                    if (fail == null)
                    {
                        harness.Seam.UpdateObserver(R01E02Fixture.GuestId, newToken, bx, bx, true);
                        harness.Seam.Flush(0f);
                        if (!ItemSettlementGate.UnconfirmedSideEffectForRegion(bx, bx, 4001U))
                            fail = "rebind released quarantine";
                        if (fail == null && CountPendingOf(harness, 4001U) != 1)
                            fail = "rebind shared lost A pending="
                                + CountPendingOf(harness, 4001U);
                        if (fail == null)
                        {
                            var aReceiver2 = new SpawnReceiver(harness.View)
                            {
                                ThrowAfterAction = true, SkipRegistration = true
                            };
                            int beforeA = aReceiver2.Invocations;
                            ItemLocalPresentationDispatch.ConsumeOwnedBatch(
                                harness.Native, harness.HostPlayer, harness.Seam, harness.View,
                                spawn, aReceiver2, () => true, 1,
                                (System.Collections.IList)harness.RawPending);
                            if (aReceiver2.Invocations != beforeA)
                                fail = "post-rebind A invoked=" + aReceiver2.Invocations;
                            // 正向控制：换代后实际消费一个未隔离合法项（B'(4003)@(5,6)），
                            // 证明 A 零调用来自隔离门而非身份暂缓/队列清空
                            if (fail == null)
                            {
                                harness.Bridge.Storage.Regions[bx, (byte)(bx + 1)].items.Add(
                                    R01E02Fixture.MakeItem(4003U, 276, 1, 100));
                                ItemStepFiveResult incC = ItemLocalPresentationDispatch.DispatchIncrement(
                                    harness.Native, harness.View, bx, (byte)(bx + 1), 276, 1, 100,
                                    new byte[0], UnityEngine.Vector3.zero, 4003U, false);
                                if (incC != ItemStepFiveResult.Accepted)
                                    fail = "increment C=" + incC;
                            }
                            if (fail == null)
                            {
                                var cReceiver = new SpawnReceiver(harness.View);
                                ConsumeBatchResult control = ItemLocalPresentationDispatch.ConsumeOwnedBatch(
                                    harness.Native, harness.HostPlayer, harness.Seam, harness.View,
                                    spawn, cReceiver, () => true, 1,
                                    (System.Collections.IList)harness.RawPending);
                                if (CountPendingOf(harness, 4003U) != 0
                                    || cReceiver.Invocations != 1)
                                    fail = "post-rebind control item consumed="
                                        + CountPendingOf(harness, 4003U)
                                        + " invocations=" + cReceiver.Invocations
                                        + " stop=" + control.Stop;
                            }
                        }
                    }
                }
            }
            return fail == null;
        }

        private static PendingView NewPending(uint id, byte x, byte y)
        {
            return new PendingView
            {
                InstanceId = id, AssetId = 276, Amount = 1, Quality = 100,
                State = new byte[0], RegionX = x, RegionY = y,
                ShouldPlayEffect = false
            };
        }

        private static int CountPendingOf(FixtureHarness harness, uint id)
        {
            int count = 0;
            foreach (object entry in (System.Collections.IList)harness.RawPending)
            {
                if (entry == null) continue;
                uint instanceId = (uint)(entry.GetType().GetField("instanceID",
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic)
                    ?.GetValue(entry) ?? 0U);
                if (instanceId == id) count++;
            }
            return count;
        }

        internal static bool Test_E02_B1_StepFiveFirstExitCleanupAndSharedDemand()
        {
            string fail = null;
            var harness = new FixtureHarness();
            harness.Begin(R01E02Fixture.HostId);
            ulong token = harness.StageAndCommitIdentity(
                R01E02Fixture.HostId, harness.HostOwner, harness.HostTransport);
            if (token == 0UL) fail = "token=0";
            if (fail == null)
            {
                harness.Native.ModeReader = harness.ReadHostMode;
                harness.Native.BindingReader = harness.ReadHostBinding;
                harness.Seam.UpdateObserver(R01E02Fixture.HostId, token,
                    R01E02Fixture.CenterA, R01E02Fixture.CenterA, true);
                harness.Seam.Flush(0f);
                harness.Seam.Flush(0f);
                if (harness.CleanerTargets.Count != 0 || harness.UnbinderTargets.Count != 0)
                    fail = "early-exit cleaner=" + harness.CleanerTargets.Count
                        + " unbinder=" + harness.UnbinderTargets.Count;
            }
            ItemStepFiveResult result = ItemStepFiveResult.RunOriginal;
            if (fail == null)
            {
                result = ItemLocalPresentationDispatch.DispatchStepFive(
                    harness.Native, harness.HostPlayer, R01E02Fixture.CenterB,
                    R01E02Fixture.CenterB, harness.Seam, null);
                if (result != ItemStepFiveResult.Accepted)
                    fail = "dispatch=" + result + " bindingReads=" + harness.BindingReads;
            }
            if (fail == null)
            {
                harness.Seam.Flush(0f);
                harness.Seam.Flush(0f);
                if (harness.CleanerTargets.Count != 9)
                    fail = "cleanerCount=" + harness.CleanerTargets.Count
                        + " unbinder=" + harness.UnbinderTargets.Count
                        + " targets=" + string.Join(";", harness.CleanerTargets);
            }
            if (fail == null)
                foreach (RegionKey cleaned in harness.CleanerTargets)
                {
                    int dx = Math.Abs(cleaned.X - R01E02Fixture.CenterA);
                    int dy = Math.Abs(cleaned.Y - R01E02Fixture.CenterA);
                    if (Math.Max(dx, dy) > 1) fail = "wrong-target=" + cleaned;
                }
            int bDemand = 0;
            if (fail == null)
            {
                for (int x = R01E02Fixture.CenterB - 1; x <= R01E02Fixture.CenterB + 1; x++)
                    for (int y = R01E02Fixture.CenterB - 1; y <= R01E02Fixture.CenterB + 1; y++)
                        if (harness.Seam.GetDemand(new RegionKey((byte)x, (byte)y)) > 0) bDemand++;
                if (bDemand != 9) fail = "bDemand=" + bDemand;
            }
            if (fail == null)
            {
                int before = harness.CleanerTargets.Count;
                harness.Seam.UpdateObserver(R01E02Fixture.HostId, token,
                    R01E02Fixture.CenterB, R01E02Fixture.CenterB, true);
                harness.Seam.Flush(0f);
                if (harness.CleanerTargets.Count != before)
                    fail = "re-observe extra-exit cleaner=" + harness.CleanerTargets.Count;
            }
            if (fail == null)
            {
                ulong guestToken = harness.StageAndCommitIdentity(
                    R01E02Fixture.GuestId, harness.GuestOwner, harness.GuestTransport);
                harness.Seam.UpdateObserver(R01E02Fixture.GuestId, guestToken,
                    R01E02Fixture.CenterB, R01E02Fixture.CenterB, true);
                harness.Seam.Flush(0f);
                int bDemandShared = 0;
                for (int x = R01E02Fixture.CenterB - 1; x <= R01E02Fixture.CenterB + 1; x++)
                    for (int y = R01E02Fixture.CenterB - 1; y <= R01E02Fixture.CenterB + 1; y++)
                        if (harness.Seam.GetDemand(new RegionKey((byte)x, (byte)y)) > 0) bDemandShared++;
                if (bDemandShared != 9) fail = "bDemandShared=" + bDemandShared;
                if (harness.UnbinderTargets.Count != 0)
                    fail = (fail == null ? "" : fail + "; ")
                        + "unbinder=" + harness.UnbinderTargets.Count;
            }
            if (fail != null)
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "e02b1-fail.txt"),
                    fail);
            return fail == null;
        }

        internal static bool Test_R3_StepFiveWaitsForCommittedPhysicalBinding()
        {
            var harness = new FixtureHarness();
            harness.Begin(R01E02Fixture.HostId);
            ulong token = harness.StageAndCommitIdentity(
                R01E02Fixture.HostId, harness.HostOwner, harness.HostTransport);
            harness.Seam.UpdateObserver(R01E02Fixture.HostId, token,
                R01E02Fixture.CenterB, R01E02Fixture.CenterB, true);
            var currentOwner = new object();
            var currentPlayer = new object();
            var currentTransport = new object();
            harness.Native.ModeReader = harness.ReadHostMode;
            harness.Native.BindingReader = _ =>
            {
                ItemBindingRaw binding = harness.ReadHostBinding(harness.HostPlayer);
                binding.CallbackOwner = currentOwner;
                binding.CallbackPlayer = currentPlayer;
                binding.CallbackOwnerPlayer = currentPlayer;
                binding.CallbackTransport = currentTransport;
                binding.Clients[0].Owner = currentOwner;
                binding.Clients[0].Player = currentPlayer;
                binding.Clients[0].Connection = currentTransport;
                return binding;
            };
            RegionKey center = new RegionKey(R01E02Fixture.CenterB, R01E02Fixture.CenterB);
            int demandBefore = harness.Seam.GetDemand(center);
            bool loadedBefore = harness.Bridge.Storage.Loaded[center.X, center.Y].isItemsLoaded;
            ItemStepFiveResult result = ItemLocalPresentationDispatch.DispatchStepFive(
                harness.Native, currentPlayer, R01E02Fixture.CenterB,
                R01E02Fixture.CenterB, harness.Seam, null);
            if (token == 0UL || result != ItemStepFiveResult.Waiting
                || demandBefore == 0 || harness.Seam.GetDemand(center) != demandBefore
                || harness.Bridge.Storage.Loaded[center.X, center.Y].isItemsLoaded != loadedBefore)
            {
                Console.WriteLine("R3 wait token=" + token + " result=" + result
                    + " demand=" + demandBefore + " loaded="
                    + harness.Bridge.Storage.Loaded[center.X, center.Y].isItemsLoaded);
                return false;
            }
            ulong refreshed = harness.StageAndCommitIdentity(
                R01E02Fixture.HostId, currentOwner, currentTransport);
            if (refreshed == token) { Console.WriteLine("R3 token did not refresh"); return false; }
            harness.Seam.UpdateObserver(R01E02Fixture.HostId, refreshed,
                R01E02Fixture.CenterB, R01E02Fixture.CenterB, true);
            ItemStepFiveResult recovered = ItemLocalPresentationDispatch.DispatchStepFive(
                harness.Native, currentPlayer, R01E02Fixture.CenterB,
                R01E02Fixture.CenterB, harness.Seam, null);
            bool loaded = harness.Bridge.Storage.Loaded[center.X, center.Y].isItemsLoaded;
            if (recovered != ItemStepFiveResult.Accepted || !loaded)
                Console.WriteLine("R3 recovery token=" + refreshed + " result="
                    + recovered + " loaded=" + loaded);
            return recovered == ItemStepFiveResult.Accepted && loaded;
        }

        internal static bool Test_R3_ConsumerWaitsForCommittedPhysicalBinding()
        {
            var harness = new FixtureHarness();
            harness.Begin(R01E02Fixture.HostId);
            ulong token = harness.StageAndCommitIdentity(
                R01E02Fixture.HostId, harness.HostOwner, harness.HostTransport);
            byte bx = R01E02Fixture.CenterB;
            harness.Bridge.Storage.Regions[bx, bx].items.Add(
                R01E02Fixture.MakeItem(6161U, 16, 1, 100));
            harness.Seam.UpdateObserver(R01E02Fixture.HostId, token, bx, bx, true);
            harness.Seam.Flush(0f);
            harness.Seam.Flush(0f);
            harness.View.ModelsByRegion[(bx, bx)] = new List<ModelView>();
            var currentOwner = new object();
            var currentPlayer = new object();
            var currentTransport = new object();
            harness.Native.ModeReader = harness.ReadHostMode;
            harness.Native.BindingReader = _ =>
            {
                ItemBindingRaw binding = harness.ReadHostBinding(harness.HostPlayer);
                binding.CallbackOwner = currentOwner;
                binding.CallbackPlayer = currentPlayer;
                binding.CallbackOwnerPlayer = currentPlayer;
                binding.CallbackTransport = currentTransport;
                binding.Clients[0].Owner = currentOwner;
                binding.Clients[0].Player = currentPlayer;
                binding.Clients[0].Connection = currentTransport;
                return binding;
            };
            var receiver = new SpawnReceiver(harness.View);
            MethodInfo spawn = typeof(SpawnReceiver).GetMethod("SpawnItem");
            ConsumeBatchResult result = ItemLocalPresentationDispatch.ConsumeOwnedBatch(
                harness.Native, currentPlayer, harness.Seam, harness.View, spawn,
                receiver, () => true, 1, (System.Collections.IList)harness.RawPending);
            var raw = (System.Collections.IList)harness.RawPending;
            if (token == 0UL || result.Consumed != 0 || receiver.Invocations != 0
                || raw.Count != 1 || harness.View.ModelsByRegion[(bx, bx)].Count != 0)
            {
                Console.WriteLine("R3 consumer wait token=" + token + " consumed=" + result.Consumed
                    + " stop=" + result.Stop + " spawn=" + receiver.Invocations
                    + " pending=" + raw.Count + " models=" + harness.View.ModelsByRegion[(bx, bx)].Count);
                return false;
            }
            ulong refreshed = harness.StageAndCommitIdentity(
                R01E02Fixture.HostId, currentOwner, currentTransport);
            if (refreshed == token) return false;
            harness.Seam.UpdateObserver(R01E02Fixture.HostId, refreshed, bx, bx, true);
            ConsumeBatchResult recovered = ItemLocalPresentationDispatch.ConsumeOwnedBatch(
                harness.Native, currentPlayer, harness.Seam, harness.View, spawn,
                receiver, () => true, 1, raw);
            if (recovered.Consumed != 1 || receiver.Invocations != 1 || raw.Count != 0
                || harness.View.ModelsByRegion[(bx, bx)].Count != 1)
                Console.WriteLine("R3 consumer recovery token=" + refreshed + " consumed="
                    + recovered.Consumed + " stop=" + recovered.Stop + " spawn="
                    + receiver.Invocations + " pending=" + raw.Count + " models="
                    + harness.View.ModelsByRegion[(bx, bx)].Count);
            return recovered.Consumed == 1 && receiver.Invocations == 1 && raw.Count == 0
                && harness.View.ModelsByRegion[(bx, bx)].Count == 1
                && harness.View.ModelsByRegion[(bx, bx)][0].InstanceId == 6161U;
        }

        internal static bool Test_E02_A3_IdentityGuardsRejectAndWait()
        {
            // 1) 非 Candidate：mode-first 分流，binding 一次都不读。
            var nonCandidate = new FixtureHarness();
            nonCandidate.Begin(R01E02Fixture.HostId);
            nonCandidate.Native.ModeReader = () => new ItemModeRaw(
                dedicated: true, isServer: true, p2p: false, writerOwnsSession: false);
            nonCandidate.Native.BindingReader = nonCandidate.ReadHostBinding;
            ItemStepFiveResult runOriginal = ItemLocalPresentationDispatch.DispatchStepFive(
                nonCandidate.Native, nonCandidate.HostPlayer, R01E02Fixture.CenterB, R01E02Fixture.CenterB,
                nonCandidate.Seam, null);
            if (runOriginal != ItemStepFiveResult.RunOriginal || nonCandidate.BindingReads != 0)
                return false;
            // 2) 未提交身份的新对象：第一拍 Waiting，无副作用。
            var freshHarness = new FixtureHarness();
            freshHarness.Begin(R01E02Fixture.HostId);
            freshHarness.Native.ModeReader = freshHarness.ReadHostMode;
            freshHarness.Native.BindingReader = freshHarness.ReadHostBinding;
            ItemStepFiveResult waiting = ItemLocalPresentationDispatch.DispatchStepFive(
                freshHarness.Native, freshHarness.HostPlayer, R01E02Fixture.CenterB, R01E02Fixture.CenterB,
                freshHarness.Seam, null);
            if (waiting != ItemStepFiveResult.Waiting
                || freshHarness.Seam.GetDemand(new RegionKey(
                    R01E02Fixture.CenterB, R01E02Fixture.CenterB)) != 0)
                return false;
            // 3) 旧 epoch/旧 admission：Rejected，且在副作用前拒绝。
            var stale = new FixtureHarness();
            stale.Begin(R01E02Fixture.HostId);
            ulong token = stale.StageAndCommitIdentity(
                R01E02Fixture.HostId, stale.HostOwner, stale.HostTransport);
            stale.Native.ModeReader = stale.ReadHostMode;
            stale.Native.BindingReader = stale.ReadHostBinding;
            ItemStepFiveAdmission oldAdmission = stale.Seam.Admission;
            stale.Seam.EndSession();
            stale.Seam.BeginSession(new SessionEpoch(999UL));
            ulong token2 = stale.StageAndCommitIdentity(
                R01E02Fixture.HostId, stale.HostOwner, stale.HostTransport);
            stale.Seam.UpdateObserver(R01E02Fixture.HostId, token2,
                R01E02Fixture.CenterA, R01E02Fixture.CenterA, true);
            ItemBindingRaw staleBinding = stale.ReadHostBinding(stale.HostPlayer);
            var staleRequest = new ItemStepFiveRequest(R01E02Fixture.HostId, token2,
                new RegionKey(R01E02Fixture.CenterB, R01E02Fixture.CenterB),
                R01E02Fixture.WorldSize, R01E02Fixture.WorldSize, true);
            var staleBound = new ItemBoundStepFiveRequest(
                staleRequest, new SessionEpoch(R01E02Fixture.HostId), stale.HostPlayer);
            ItemStepFiveResult staleResult = oldAdmission.Admit(
                staleBound, new ItemStepFiveLoadedProjection(_ => { }));
            return staleResult == ItemStepFiveResult.Rejected
                && stale.Seam.GetDemand(new RegionKey(
                    R01E02Fixture.CenterB, R01E02Fixture.CenterB)) == 0;
        }

        internal sealed class FixtureHarness : IComboHarnessView
        {
            public readonly R01E02Fixture.FixtureBridge Bridge = new R01E02Fixture.FixtureBridge();
            public readonly TestView View = new TestView();
            public readonly object RawPending =
                Activator.CreateInstance(typeof(List<>)
                    .MakeGenericType(typeof(SDG.Unturned.ItemManager).Assembly
                        .GetType("SDG.Unturned.ItemInstantiationParameters", true)));
            public readonly object RawScratch =
                Activator.CreateInstance(typeof(List<>)
                    .MakeGenericType(typeof(SDG.Unturned.ItemManager).Assembly
                        .GetType("SDG.Unturned.ItemInstantiationParameters", true)));
            public readonly ItemPreparationAuthority Prep;
            public readonly ItemExecutionPort Port;
            public readonly DemandProjectionEngine Projection;
            public DemandPolicy Policy;
            public readonly ItemProductionControlSeam Seam;
            public readonly RawNative Native = new RawNative();
            public readonly object HostOwner = new object();
            public readonly object HostPlayer = new object();
            public readonly object HostTransport = new object();
            public readonly object GuestOwner = new object();
            public readonly object GuestPlayer = new object();
            public readonly object GuestTransport = new object();
            public int BindingReads;
            public bool ThrowBindingReads;
            public readonly List<RegionKey> CleanerTargets = new List<RegionKey>();
            public readonly List<RegionKey> UnbinderTargets = new List<RegionKey>();

            System.Collections.IList IComboHarnessView.Pending => View.Pending;
            System.Collections.IEnumerable IComboHarnessView.Models => View.AllModels;

            public FixtureHarness()
            {
                Prep = new ItemPreparationAuthority(Bridge);
                View.Storage = Bridge.Storage;
                View.PendingReader = () => new NativePendingList(() => (System.Collections.IList)RawPending);
                Port = new ItemExecutionPort(
                    Prep, new ItemMetadataStore(), _ => 1U, ItemLifecyclePolicy.Create(), () => 10f, null,
                    observerId => observerId == R01E02Fixture.HostId,
                    region => ItemLocalPresentationPipeline.PresentSharedRegion(View,
                        (byte)region.X, (byte)region.Y),
                    (observerId, token, regionKey) => { },
                    regionKey => CleanerTargets.Add(regionKey),
                    (observerId, token, regionKey) => UnbinderTargets.Add(regionKey),
                    isCandidateActive: () => true);
                Projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
                Policy = ItemDemandPolicy.Create(1, R01E02Fixture.WorldSize);
                Projection.Register(Policy);
                Seam = new ItemProductionControlSeam(
                    Prep, new ItemMetadataStore(), Port, Projection, Policy, 2f);
                Seam.BindOwnedNative(Native);
            }

            public void Begin(ulong epochValue)
            {
                var epoch = new SessionEpoch(epochValue);
                Seam.BeginSession(epoch);
                Port.OnSessionBegin((uint)epochValue);
                if (!Prep.PrepareFullMap(epoch, "E02-fixture", R01E02Fixture.WorldSize))
                    throw new InvalidOperationException("PrepareFullMap failed status=" + Prep.Status);
            }

            /// <summary>生产 identity stage/commit：对象重载得 staged 记录后走真实 Commit。</summary>
            public ulong StageAndCommitIdentity(ulong observerId, object owner, object transport)
            {
                var capture = new MultiObserverShadowCoordinator.CaptureResult();
                capture.StagedConnections =
                    new Dictionary<ulong, MultiObserverShadowCoordinator.ConnectionIdentity>();
                var staged = new Dictionary<ulong, MultiObserverShadowCoordinator.ConnectionIdentity>();
                ulong nextToken = ReadNextConnectionToken();
                if (!MultiObserverShadowCoordinator.TryGetStagedConnectionToken(
                        observerId, owner, transport, staged, ref nextToken, out ulong token,
                        out string failure))
                    throw new InvalidOperationException("stage failed: " + failure);
                capture.StagedConnections = staged;
                capture.StagedNextConnectionToken = nextToken;
                MultiObserverShadowCoordinator.CommitConnectionIdentities(
                    capture, new HashSet<ulong>());
                return staged[observerId].Token;
            }

            private static ulong ReadNextConnectionToken()
            {
                FieldInfo field = typeof(MultiObserverShadowCoordinator).GetField(
                    "_nextConnectionToken", BindingFlags.Static | BindingFlags.NonPublic);
                return field != null ? (ulong)field.GetValue(null) : 0UL;
            }

            public ItemModeRaw ReadHostMode() => new ItemModeRaw(
                dedicated: false, isServer: true, p2p: true, writerOwnsSession: true);

            public ItemBindingRaw ReadHostBinding(object playerKey)
            {
                BindingReads++;
                if (ThrowBindingReads) throw new InvalidOperationException("raw binding read failure");
                var clients = new List<ItemClientRaw>
                {
                    new ItemClientRaw
                    {
                        Owner = HostOwner, Player = HostPlayer, Connection = HostTransport,
                        SteamId = R01E02Fixture.HostId
                    },
                    new ItemClientRaw
                    {
                        Owner = GuestOwner, Player = GuestPlayer, Connection = GuestTransport,
                        SteamId = R01E02Fixture.GuestId
                    }
                };
                bool committed = MultiObserverShadowCoordinator.TryReadCommittedItemConnection(
                    R01E02Fixture.HostId, out object committedOwner, out object committedTransport,
                    out ulong committedToken);
                return new ItemBindingRaw
                {
                    CallbackPlayer = HostPlayer,
                    CallbackOwner = HostOwner,
                    CallbackOwnerPlayer = HostPlayer,
                    CallbackTransport = HostTransport,
                    CallbackIsLocalPlayer = true,
                    CallbackOwnerSteamId = R01E02Fixture.HostId,
                    ProviderClient = R01E02Fixture.HostId,
                    ProviderClientSteamId = R01E02Fixture.HostId,
                    Clients = clients,
                    LoadedWidth = R01E02Fixture.WorldSize,
                    LoadedHeight = R01E02Fixture.WorldSize,
                    HasLoadedCell = (x, y) => x < R01E02Fixture.WorldSize && y < R01E02Fixture.WorldSize,
                    CanProjectCell = (x, y) => x < R01E02Fixture.WorldSize && y < R01E02Fixture.WorldSize,
                    ProjectCell = (x, y) => View.SetLoaded(x, y, true),
                    CommittedOwner = committed ? committedOwner : null,
                    CommittedTransport = committed ? committedTransport : null,
                    CommittedToken = committedToken
                };
            }

            public ItemBindingRaw ReadGuestBinding(object playerKey)
            {
                return ReadGuestBindingForOwner(GuestOwner, R01E02Fixture.GuestId, GuestTransport);
            }

            /// <summary>E02 C3：可注入 owner/transport 的 guest raw reader（真实换代采样）。</summary>
            public ItemBindingRaw ReadGuestBindingForOwner(
                object owner, ulong steamId, object transport)
            {
                BindingReads++;
                if (ThrowBindingReads) throw new InvalidOperationException("raw binding read failure");
                var clients = new List<ItemClientRaw>
                {
                    new ItemClientRaw
                    {
                        Owner = HostOwner, Player = HostPlayer, Connection = HostTransport,
                        SteamId = R01E02Fixture.HostId
                    },
                    new ItemClientRaw
                    {
                        Owner = owner, Player = GuestPlayer, Connection = transport,
                        SteamId = steamId
                    }
                };
                bool committed = MultiObserverShadowCoordinator.TryReadCommittedItemConnection(
                    steamId, out object committedOwner, out object committedTransport,
                    out ulong committedToken);
                return new ItemBindingRaw
                {
                    CallbackPlayer = GuestPlayer,
                    CallbackOwner = owner,
                    CallbackOwnerPlayer = GuestPlayer,
                    CallbackTransport = transport,
                    CallbackIsLocalPlayer = false,
                    CallbackOwnerSteamId = steamId,
                    ProviderClient = R01E02Fixture.HostId,
                    ProviderClientSteamId = R01E02Fixture.HostId,
                    Clients = clients,
                    LoadedWidth = R01E02Fixture.WorldSize,
                    LoadedHeight = R01E02Fixture.WorldSize,
                    HasLoadedCell = (x, y) => x < R01E02Fixture.WorldSize && y < R01E02Fixture.WorldSize,
                    CanProjectCell = (x, y) => x < R01E02Fixture.WorldSize && y < R01E02Fixture.WorldSize,
                    ProjectCell = (x, y) => View.SetLoaded(x, y, true),
                    CommittedOwner = committed ? committedOwner : null,
                    CommittedTransport = committed ? committedTransport : null,
                    CommittedToken = committedToken
                };
            }
        }
    }
}
