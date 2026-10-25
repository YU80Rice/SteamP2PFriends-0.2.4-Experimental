using System;
using System.Collections;
using System.Collections.Generic;
using SDG.Unturned;
using SteamP2PFriends.Adapters.Item;
using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver.Demand;
using SteamP2PFriends.MultiObserver.Lifecycle;

namespace SteamP2PFriends.WhitelistTests
{
    internal static class ItemLocalPresentationPipelineTests
    {
        /// <summary>E02：native leaf 机械测试（独立命名，非 AC2 组合入口）。</summary>
        internal static bool Test_E02_ProbeLeafMechanical()
        {
            var detail = new List<string>();
            NativePendingBatchLeafProbe.ProbeResult probe =
                NativePendingBatchLeafProbe.Build(detail);
            foreach (string line in detail) Console.WriteLine("E02PROBE " + line);
            if (probe.Consume == null) return false;
            var storage = new R01E02Fixture.NativeStorage();
            storage.Regions[5, 5].items.Add((SDG.Unturned.ItemData)MakeItemData(1001U, 276));
            storage.Regions[5, 5].items.Add((SDG.Unturned.ItemData)MakeItemData(1002U, 16));
            object pending = Activator.CreateInstance(NativePendingBatchLeafProbe.ParamListType);
            object scratch = Activator.CreateInstance(NativePendingBatchLeafProbe.ParamListType);
            probe.Consume(storage.Regions[5, 5], pending, scratch, (byte)5, (byte)5, 37f,
                storage.Loaded[5, 5], _ => { });
            int pendingAfter = ((System.Collections.IList)pending).Count;
            Console.WriteLine("E02PROBE pendingAfter=" + pendingAfter
                + " loaded=" + storage.Loaded[5, 5].isItemsLoaded
                + " networked=" + storage.Regions[5, 5].isNetworked);
            return pendingAfter == 2 && storage.Loaded[5, 5].isItemsLoaded
                && storage.Regions[5, 5].isNetworked;
        }

        internal static bool Test_E02_AC2_ProductionCombination()
        {
            // E02 §5 组合核心（当前版装配）：真实生产 shared/step5 入口 + 唯一性断言。
            // 断言文本与旧版逐字相同：after-step5 每 ID 恰好 1 份 pending+有效模型。
            var harness = R01E02SliceATests.AcquireComboHarness();
            harness.Begin(R01E02Fixture.HostId);
            ulong token = harness.StageAndCommitIdentity(
                R01E02Fixture.HostId, harness.HostOwner, harness.HostTransport);
            if (token == 0UL) return false;
            harness.Native.ModeReader = harness.ReadHostMode;
            harness.Native.BindingReader = harness.ReadHostBinding;

            // 权威两项先入 B（共享呈现的数据源；PrepareFullMap 后投放）
            harness.Bridge.Storage.Regions[R01E02Fixture.CenterB, R01E02Fixture.CenterB]
                .items.Add((SDG.Unturned.ItemData)MakeItemData(1001U, 276));
            harness.Bridge.Storage.Regions[R01E02Fixture.CenterB, R01E02Fixture.CenterB]
                .items.Add((SDG.Unturned.ItemData)MakeItemData(1002U, 16));

            harness.Seam.UpdateObserver(R01E02Fixture.HostId, token,
                R01E02Fixture.CenterB, R01E02Fixture.CenterB, true);
            harness.Seam.Flush(0f);
            harness.Seam.Flush(0f);

            Console.WriteLine("E02AC2 current wrapper/core=ItemLocalPresentationPipeline.PresentSharedRegion"
                + " warehouse=" + harness.Bridge.Storage.Regions[R01E02Fixture.CenterB,
                    R01E02Fixture.CenterB].items.Count);
            Console.WriteLine("E02AC2 after-shared pending=" + harness.View.Pending.Count
                + " ids=1/1 loaded=" + harness.Bridge.Storage.Loaded[
                    R01E02Fixture.CenterB, R01E02Fixture.CenterB].isItemsLoaded);
            if (CountPendingId(harness, 1001U) != 1 || CountPendingId(harness, 1002U) != 1)
                return false;

            ItemStepFiveResult result = ItemLocalPresentationDispatch.DispatchStepFive(
                harness.Native, harness.HostPlayer, R01E02Fixture.CenterB, R01E02Fixture.CenterB,
                harness.Seam, null);
            bool runOriginal = result == ItemStepFiveResult.RunOriginal;
            Console.WriteLine("E02AC2 owned=" + result
                + " prefix-run-original=" + runOriginal);
            if (result != ItemStepFiveResult.Accepted) return false;
            harness.Seam.Flush(0f);
            harness.Seam.Flush(0f);

            Console.WriteLine("E02AC2 after-step5 pending=" + harness.View.Pending.Count
                + " id1001=" + CountPendingId(harness, 1001U)
                + " id1002=" + CountPendingId(harness, 1002U)
                + " loaded=" + harness.Bridge.Storage.Loaded[
                    R01E02Fixture.CenterB, R01E02Fixture.CenterB].isItemsLoaded);
            Console.WriteLine("E02AC2 uniqueness="
                + (CountPendingAndValidModelId(harness, 1001U) == 1
                    && CountPendingAndValidModelId(harness, 1002U) == 1));
            return CountPendingAndValidModelId(harness, 1001U) == 1
                && CountPendingAndValidModelId(harness, 1002U) == 1;
        }

        internal static int CountPendingId(R01E02SliceATests.IComboHarnessView harness, uint id)
        {
            int count = 0;
            foreach (object entry in (System.Collections.IList)harness.Pending)
            {
                if (entry == null) continue;
                var viewType = entry.GetType();
                uint instanceId = 0U;
                if (viewType.Name == "PendingView")
                {
                    instanceId = (uint)(viewType.GetProperty("InstanceId")
                        ?.GetValue(entry) ?? 0U);
                }
                else
                {
                    var paramType = entry.GetType();
                    instanceId = (uint)(paramType.GetField("instanceID",
                        System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.NonPublic)
                        ?.GetValue(entry) ?? 0U);
                }
                if (instanceId == id) count++;
            }
            return count;
        }

        internal static int CountPendingAndValidModelId(
            R01E02SliceATests.IComboHarnessView harness, uint id)
        {
            int count = CountPendingId(harness, id);
            foreach (ModelView model in harness.Models)
                if (model.InstanceId == id && model.Handle != null) count++;
            return count;
        }

        private static object MakeItemData(uint instanceId, ushort assetId)
        {
            var item = (SDG.Unturned.Item)System.Runtime.Serialization.FormatterServices
                .GetUninitializedObject(typeof(SDG.Unturned.Item));
            var itemFlags = System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic;
            // 确切字段/属性非 null 断言：缺字段立即失败，不 ?.SetValue 静默丢属性
            var idField = typeof(SDG.Unturned.Item).GetField("_id", itemFlags);
            if (idField == null) throw new MissingFieldException(
                typeof(SDG.Unturned.Item).FullName, "_id");
            idField.SetValue(item, assetId);
            SetItemField(item, itemFlags, "amount", (byte)1);
            SetItemField(item, itemFlags, "quality", (byte)100);
            SetItemField(item, itemFlags, "state", new byte[0]);
            var data = (SDG.Unturned.ItemData)System.Runtime.Serialization.FormatterServices
                .GetUninitializedObject(typeof(SDG.Unturned.ItemData));
            typeof(SDG.Unturned.ItemData).GetField("_item", itemFlags)?.SetValue(data, item);
            typeof(SDG.Unturned.ItemData).GetField("_instanceID", itemFlags)
                ?.SetValue(data, instanceId);
            return data;
        }

        private static void SetItemField(object item, System.Reflection.BindingFlags flags,
            string name, object value)
        {
            var field = typeof(SDG.Unturned.Item).GetField(name, flags);
            if (field == null) throw new MissingFieldException(
                typeof(SDG.Unturned.Item).FullName, name);
            field.SetValue(item, value);
        }

        internal static bool Test_IT102_SharedPresentThenRetiredStepFiveDuplicatesPending()
        {
            var view = new RecordingView();
            view.Items.Add(Item(1001U, 276));
            view.Items.Add(Item(1002U, 16));
            if (view.Loaded) return false;
            var pipeline = new ItemLocalPresentationPipeline(view);
            pipeline.PresentShared(2, 2);
            if (!view.Loaded) return false;
            ItemProductionControlSeam seam = CreateOwnedSeam(view, 102UL, 1000UL, 101UL);
            ItemStepFiveHandoff handoff = AdmitStepFiveThroughAdmission(seam.Admission, 2, 2);
            if (handoff != ItemStepFiveHandoff.Handled) return false;
            if (view.Items.Count != 2) return false;
            bool oneEach = Copies(view, 1001U) == 1 && Copies(view, 1002U) == 1
                && view.PendingItems.Count == 2 && view.ModelItems.Count == 0;
            if (!oneEach)
            {
                System.Console.WriteLine("IT102 warehouse=" + view.Items.Count
                    + " pending=" + view.PendingItems.Count
                    + " models=" + view.ModelItems.Count
                    + " id1001=" + Copies(view, 1001U)
                    + " id1002=" + Copies(view, 1002U)
                    + " source=shared-present+native-step5-append");
            }
            return oneEach;
        }

        private static ItemProductionControlSeam CreateOwnedSeam(
            RecordingView view, ulong epoch, ulong observer, ulong token)
        {
            var bridge = new ItemPreparationAndFirstDemandTests.TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            var port = new ItemExecutionPort(
                prep, new ItemMetadataStore(), _ => 1U, ItemLifecyclePolicy.Create(), () => 10f, null,
                observerId => observerId == observer,
                region => ItemLocalPresentationPipeline.PresentSharedRegion(view, (byte)region.X, (byte)region.Y));
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(0, 2);
            proj.Register(policy);
            var seam = new ItemProductionControlSeam(prep, new ItemMetadataStore(), port, proj, policy, 2f);
            seam.BeginSession(new SessionEpoch(epoch));
            port.OnSessionBegin((uint)epoch);
            prep.PrepareFullMap(new SessionEpoch(epoch), "r01-owned", 2);
            seam.UpdateObserver(observer, token, 1, 1, true);
            return seam;
        }

        private static ItemStepFiveHandoff AdmitStepFiveThroughAdmission(
            ItemStepFiveAdmission admission, byte centerX, byte centerY)
        {
            var facts = new ItemStepFiveFacts();
            facts.Retired = true;
            facts.IsServer = true;
            facts.CenterSafe = true;
            facts.LocalHost = true;
            facts.IdentityKnown = true;
            facts.ConnectionCurrent = true;
            facts.ProjectionAvailable = true;
            facts.SameHostObject = true;
            facts.BoundObserver = 1000UL;
            facts.BoundToken = 101UL;
            return ItemLocalPresentationDispatch.AdmitOwnedStepFive(
                facts, admission,
                new ItemStepFiveLoadedProjection(_ => { }), centerX, centerY, 1, 8);
        }

        internal static bool Test_IT111_GuestStepFiveDoesNotPresentHost()
        {
            var view = new RecordingView();
            view.ReadyX = 0;
            view.ReadyY = 0;
            view.Items.Add(Item(1001U, 276));
            var bridge = new ItemPreparationAndFirstDemandTests.TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            var port = new ItemExecutionPort(
                prep, new ItemMetadataStore(), _ => 1U, ItemLifecyclePolicy.Create(), () => 10f, null,
                observerId => observerId == 1000UL,
                region => ItemLocalPresentationPipeline.PresentSharedRegion(view, (byte)region.X, (byte)region.Y));
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(0, 2);
            proj.Register(policy);
            var seam = new ItemProductionControlSeam(prep, new ItemMetadataStore(), port, proj, policy, 2f);
            seam.BeginSession(new SessionEpoch(111UL));
            if (!prep.PrepareFullMap(new SessionEpoch(111UL), "r01-guest", 2)) return false;
            seam.UpdateObserver(2001UL, 201UL, 1, 1, true);
            var facts = new ItemStepFiveFacts();
            facts.Retired = true;
            facts.IsServer = true;
            facts.CenterSafe = true;
            facts.IdentityKnown = true;
            facts.ConnectionCurrent = true;
            facts.ProjectionAvailable = true;
            facts.BoundObserver = 2001UL;
            facts.BoundToken = 201UL;
            ItemStepFiveHandoff handoff = ItemLocalPresentationDispatch.AdmitOwnedStepFive(
                facts, seam.Admission,
                new ItemStepFiveLoadedProjection(_ => { }), 0, 0, 0, 2);
            if (handoff != ItemStepFiveHandoff.Handled) return false;
            return view.PendingItems.Count == 0 && port.HostPresentationCount == 0;
        }

        internal static bool Test_IT118_GuestStepFiveFirstDeliversBaselineOnce()
        {
            int guestBaselines = 0;
            var bridge = new ItemPreparationAndFirstDemandTests.TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            var port = new ItemExecutionPort(
                prep, new ItemMetadataStore(), _ => 1U, ItemLifecyclePolicy.Create(), () => 10f, null,
                observerId => observerId == 1000UL,
                region => { },
                (observerId, token, region) => guestBaselines++);
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(0, 3);
            proj.Register(policy);
            var seam = new ItemProductionControlSeam(prep, new ItemMetadataStore(), port, proj, policy, 2f);
            var epoch = new SessionEpoch(118UL);
            seam.BeginSession(epoch);
            if (!prep.PrepareFullMap(epoch, "r01-guest-first", 3)) return false;
            seam.UpdateObserver(2001UL, 201UL, 0, 0, true);
            int before = guestBaselines;
            var facts = new ItemStepFiveFacts();
            facts.Retired = true;
            facts.IsServer = true;
            facts.CenterSafe = true;
            facts.IdentityKnown = true;
            facts.ConnectionCurrent = true;
            facts.ProjectionAvailable = true;
            facts.BoundObserver = 2001UL;
            facts.BoundToken = 201UL;
            ItemStepFiveHandoff handoff = ItemLocalPresentationDispatch.AdmitOwnedStepFive(
                facts, seam.Admission,
                new ItemStepFiveLoadedProjection(_ => { }), 2, 2, 0, 3);
            if (handoff != ItemStepFiveHandoff.Handled) return false;
            if (guestBaselines != before + 1)
            {
                System.Console.WriteLine("IT118 guestBaselinesBefore=" + before
                    + " afterStepFive=" + guestBaselines + " hostPresent=" + port.HostPresentationCount);
                return false;
            }
            seam.UpdateObserver(2001UL, 201UL, 2, 2, true);
            return guestBaselines == before + 1 && port.HostPresentationCount == 0;
        }

        internal static bool Test_IT107_HostStepFiveEnterPresentsOnceThroughSeam()
        {
            var view = new RecordingView();
            view.ReadyX = 0;
            view.ReadyY = 0;
            view.Items.Add(Item(1001U, 276));
            view.Items.Add(Item(1002U, 16));
            var pipeline = new ItemLocalPresentationPipeline(view);
            var bridge = new ItemPreparationAndFirstDemandTests.TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            var port = new ItemExecutionPort(
                prep, new ItemMetadataStore(), _ => 1U, ItemLifecyclePolicy.Create(), () => 10f, null,
                observerId => observerId == 1000UL,
                region => ItemLocalPresentationPipeline.PresentSharedRegion(view, (byte)region.X, (byte)region.Y));
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(0, 2);
            proj.Register(policy);
            var seam = new ItemProductionControlSeam(prep, new ItemMetadataStore(), port, proj, policy, 2f);
            var epoch = new SessionEpoch(107UL);
            seam.BeginSession(epoch);
            if (!prep.PrepareFullMap(epoch, "r01-host", 2)) return false;
            seam.UpdateObserver(1000UL, 101UL, 1, 1, true);
            var request = new ItemStepFiveRequest(1000UL, 101UL, new RegionKey(0, 0), 0, 2, true);
            seam.Admission.Admit(request, new ItemStepFiveLoadedProjection(_ => { }));
            int once = view.PendingItems.Count;
            seam.Admission.Admit(request, new ItemStepFiveLoadedProjection(_ => { }));
            return once == 2 && view.PendingItems.Count == 2
                && Copies(view, 1001U) == 1 && Copies(view, 1002U) == 1;
        }

        internal static bool Test_IT110_RemovePresentedClearsModelAndPendingOfThatId()
        {
            var view = new RecordingView();
            view.ModelItems.Add(new ModelView { InstanceId = 1001U, Handle = new object() });
            view.PendingItems.Add(new PendingView { RegionX = 2, RegionY = 2, InstanceId = 1001U });
            view.PendingItems.Add(new PendingView { RegionX = 2, RegionY = 2, InstanceId = 1001U });
            view.PendingItems.Add(new PendingView { RegionX = 2, RegionY = 2, InstanceId = 1002U });
            new ItemLocalPresentationPipeline(view).RemovePresentedInstance(2, 2, 1001U, true);
            return view.ModelItems.Count == 0 && view.PendingItems.Count == 1
                && view.PendingItems[0].InstanceId == 1002U;
        }

        internal static bool Test_IT112_SeamEnterThenStepFiveDoesNotDuplicate()
        {
            var view = new RecordingView();
            view.ReadyX = 0;
            view.ReadyY = 0;
            view.Items.Add(Item(1001U, 276));
            view.Items.Add(Item(1002U, 16));
            var pipeline = new ItemLocalPresentationPipeline(view);
            var bridge = new ItemPreparationAndFirstDemandTests.TestNativeItemWarehouseBridge();
            var prep = new ItemPreparationAuthority(bridge);
            var port = new ItemExecutionPort(
                prep, new ItemMetadataStore(), _ => 1U, ItemLifecyclePolicy.Create(), () => 10f, null,
                observerId => observerId == 1000UL,
                region => ItemLocalPresentationPipeline.PresentSharedRegion(view, (byte)region.X, (byte)region.Y));
            var proj = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(0, 2);
            proj.Register(policy);
            var seam = new ItemProductionControlSeam(prep, new ItemMetadataStore(), port, proj, policy, 2f);
            seam.BeginSession(new SessionEpoch(112UL));
            if (!prep.PrepareFullMap(new SessionEpoch(112UL), "r01-reverse", 2)) return false;
            seam.UpdateObserver(1000UL, 101UL, 0, 0, true);
            int afterEnter = view.PendingItems.Count;
            var request = new ItemStepFiveRequest(1000UL, 101UL, new RegionKey(0, 0), 0, 2, true);
            seam.Admission.Admit(request, new ItemStepFiveLoadedProjection(_ => { }));
            pipeline.EnqueueIncrement(0, 0, 45, 1, 100, new byte[0], default(UnityEngine.Vector3), 1003U, false);
            return afterEnter == 2 && view.PendingItems.Count == 3
                && Copies(view, 1001U) == 1 && Copies(view, 1002U) == 1 && Copies(view, 1003U) == 1;
        }

        internal static bool Test_IT113_PickupRejectDoesNotClearPresentation()
        {
            var view = new RecordingView();
            view.PendingItems.Add(new PendingView { RegionX = 2, RegionY = 2, InstanceId = 1001U });
            view.Networked = true;
            int clears = 0;
            bool rejected = !ItemAuthoritativePickup.TryRequest(
                true, true, new UnityEngine.Vector3(0f, 0f, 0f), true,
                new UnityEngine.Vector3(30f, 0f, 0f), default(UnityEngine.Vector3), true, true,
                () => { clears++; return true; });
            bool accepted = ItemAuthoritativePickup.TryRequest(
                true, true, new UnityEngine.Vector3(0f, 0f, 0f), true,
                new UnityEngine.Vector3(1f, 0f, 0f), default(UnityEngine.Vector3), true, true,
                () =>
                {
                    new ItemLocalPresentationPipeline(view).RemovePresentedInstance(2, 2, 1001U, false);
                    return true;
                });
            return rejected && clears == 0 && accepted && view.PendingItems.Count == 0;
        }

        internal static bool Test_IT108_PartialConsumeLeavesTailAboveMinimumBatch()
        {
            var view = new RecordingView();
            ushort[] assets = { 276, 16, 276, 16, 276, 16, 276 };
            for (int i = 0; i < assets.Length; i++)
                view.PendingItems.Add(new PendingView { RegionX = 2, RegionY = 2, InstanceId = 3000U + (uint)i, AssetId = assets[i] });
            int created = 0;
            new ItemLocalPresentationPipeline(view).ConsumePendingBatch(
                () => false, 5, item => { created++; return EConsumeOutcome.Consumed; });
            return created == 5 && view.PendingItems.Count == 2
                && view.PendingItems[0].InstanceId == 3005U
                && view.PendingItems[1].InstanceId == 3006U;
        }

        internal static bool Test_IT114_FullBudgetConsumeClearsPending()
        {
            var view = new RecordingView();
            for (int i = 0; i < 6; i++)
                view.PendingItems.Add(new PendingView { RegionX = 2, RegionY = 2, InstanceId = 4000U + (uint)i, AssetId = (ushort)(i % 2 == 0 ? 276 : 16) });
            int created = 0;
            new ItemLocalPresentationPipeline(view).ConsumePendingBatch(
                () => true, 5, item => { created++; return EConsumeOutcome.Consumed; });
            return created == 6 && view.PendingItems.Count == 0;
        }

        internal static bool Test_IT116_DropAfterPickupReenqueuesSinglePresentation()
        {
            var view = new RecordingView();
            view.Networked = true;
            view.Items.Add(Item(2001U, 276));
            var pipeline = new ItemLocalPresentationPipeline(view);
            pipeline.PresentShared(2, 2);
            int firstPending = view.PendingItems.Count;
            bool accepted = ItemAuthoritativePickup.TryRequest(
                true, true, new UnityEngine.Vector3(0f, 0f, 0f), true,
                new UnityEngine.Vector3(1f, 0f, 0f), default(UnityEngine.Vector3), true, true,
                () =>
                {
                    pipeline.RemovePresentedInstance(2, 2, 2001U, false);
                    return true;
                });
            int afterPickup = view.PendingItems.Count;
            pipeline.EnqueueIncrement(2, 2, 276, 1, 100, new byte[0],
                new UnityEngine.Vector3(1f, 64f, 1f), 2001U, false);
            ConsumeBatchResult consumed = pipeline.ConsumePendingBatch(
                () => true, 5,
                pending =>
                {
                    view.ModelItems.Add(new ModelView { InstanceId = pending.InstanceId, Handle = new object() });
                    return EConsumeOutcome.Consumed;
                });
            bool modelLeg = consumed.Consumed == 1 && view.PendingItems.Count == 0
                && view.ModelItems.Count == 1;
            pipeline.RemovePresentedInstance(2, 2, 2001U, false);
            return accepted && firstPending == 1 && afterPickup == 0
                && modelLeg && Copies(view, 2001U) == 0 && view.ModelItems.Count == 0;
        }

        internal static bool Test_IT106_UnknownIdentityRejectsWithoutNativeElse()
        {
            var facts = new ItemStepFiveFacts();
            facts.Dedicated = false;
            facts.IsServer = true;
            facts.CenterSafe = true;
            facts.Retired = true;
            facts.LocalHost = true;
            facts.IdentityKnown = false;
            facts.ConnectionCurrent = false;
            facts.ProjectionAvailable = true;
            facts.SameHostObject = false;
            return ItemLocalPresentationDispatch.Decide(facts) == ItemStepFiveHandoff.Rejected;
        }

        internal static bool Test_IT103_RetiredStepFiveThenSharedPresentKeepsOnePending()
        {
            var view = new RecordingView();
            view.ReadyX = 0;
            view.ReadyY = 0;
            view.Items.Add(Item(1001U, 276));
            view.Items.Add(Item(1002U, 16));
            ItemProductionControlSeam seam = CreateOwnedSeam(view, 103UL, 1000UL, 101UL);
            var facts = new ItemStepFiveFacts();
            facts.Retired = true;
            facts.IsServer = true;
            facts.CenterSafe = true;
            facts.LocalHost = true;
            facts.IdentityKnown = true;
            facts.ConnectionCurrent = true;
            facts.ProjectionAvailable = true;
            facts.SameHostObject = true;
            facts.BoundObserver = 1000UL;
            facts.BoundToken = 101UL;
            ItemStepFiveHandoff first = ItemLocalPresentationDispatch.AdmitOwnedStepFive(
                facts, seam.Admission,
                new ItemStepFiveLoadedProjection(_ => { }), 0, 0, 0, 2);
            int once = view.PendingItems.Count;
            ItemStepFiveHandoff second = ItemLocalPresentationDispatch.AdmitOwnedStepFive(
                facts, seam.Admission,
                new ItemStepFiveLoadedProjection(_ => { }), 0, 0, 0, 2);
            return first == ItemStepFiveHandoff.Handled && second == ItemStepFiveHandoff.Handled
                && once == 2 && view.PendingItems.Count == 2
                && Copies(view, 1001U) == 1 && Copies(view, 1002U) == 1
                && view.ModelItems.Count == 0;
        }

        internal static bool Test_IT122_SingleIdentityDeleteKeepsOtherIdentity()
        {
            var view = new RecordingView();
            view.Networked = true;
            view.PendingItems.Add(Pending(2001U, 2, 2));
            view.PendingItems.Add(Pending(2002U, 2, 2));
            view.ModelItems.Add(new ModelView { InstanceId = 2001U, Handle = new object() });
            view.ModelItems.Add(new ModelView { InstanceId = 2002U, Handle = new object() });
            var pipeline = new ItemLocalPresentationPipeline(view);
            pipeline.RemovePresentedInstance(2, 2, 2001U, true);
            bool aGone = Copies(view, 2001U) == 0;
            int modelsOfA = 0, modelsOfB = 0;
            for (int i = 0; i < view.ModelItems.Count; i++)
            {
                if (view.ModelItems[i].InstanceId == 2001U) modelsOfA++;
                if (view.ModelItems[i].InstanceId == 2002U) modelsOfB++;
            }
            return aGone && modelsOfA == 0 && modelsOfB == 1
                && Copies(view, 2002U) == 1 && view.Networked
                && view.DeferredFinishCalls == 0;
        }

        internal static bool Test_IT121c_NotifyFailureStillCancelsPending()
        {
            var view = new RecordingView();
            view.Networked = true;
            view.PendingItems.Add(Pending(2001U, 2, 2));
            view.ModelItems.Add(new ModelView { InstanceId = 2001U, Handle = new object() });
            view.ThrowOnNotify = true;
            var pipeline = new ItemLocalPresentationPipeline(view);
            bool threw = false;
            try { pipeline.RemovePresentedInstance(2, 2, 2001U, true); }
            catch (System.InvalidOperationException) { threw = true; }
            return threw && Copies(view, 2001U) == 0;
        }

        internal static bool Test_IT121a_NotStartedKeepsPendingForRetry()
        {
            var view = new RecordingView();
            view.PendingItems.Add(Pending(2001U, 2, 2));
            int calls = 0;
            ConsumeBatchResult result = new ItemLocalPresentationPipeline(view).ConsumePendingBatch(
                () => true, 5, item => { calls++; return EConsumeOutcome.NotStarted; });
            return calls == 1 && result.Consumed == 0 && result.Stop == EConsumeOutcome.NotStarted
                && result.StoppedItem != null && result.StoppedItem.InstanceId == 2001U
                && Copies(view, 2001U) == 1;
        }

        internal static bool Test_IT121b_UncertainKeepsPendingForQuarantine()
        {
            var view = new RecordingView();
            view.PendingItems.Add(Pending(2001U, 2, 2));
            view.PendingItems.Add(Pending(2002U, 2, 2));
            int calls = 0;
            ConsumeBatchResult result = new ItemLocalPresentationPipeline(view).ConsumePendingBatch(
                () => true, 5, item =>
                {
                    calls++;
                    return item.InstanceId == 2001U ? EConsumeOutcome.Consumed : EConsumeOutcome.Uncertain;
                });
            return calls == 2 && result.Consumed == 1 && result.Stop == EConsumeOutcome.Uncertain
                && result.StoppedItem != null && result.StoppedItem.InstanceId == 2002U
                && Copies(view, 2001U) == 0 && Copies(view, 2002U) == 1;
        }

        internal static bool Test_IT121d_BlockedRegionSkipsButKeepsPending()
        {
            var view = new RecordingView();
            view.PendingItems.Add(Pending(2001U, 2, 2));
            view.PendingItems.Add(Pending(2002U, 3, 3));
            int calls = 0;
            ConsumeBatchResult result = new ItemLocalPresentationPipeline(view).ConsumePendingBatch(
                () => true, 5,
                item => { calls++; return EConsumeOutcome.Consumed; },
                item => item.RegionX == 2);
            return calls == 1 && result.Consumed == 1 && result.Stop == EConsumeOutcome.Consumed
                && Copies(view, 2001U) == 1 && Copies(view, 2002U) == 0;
        }

        internal static bool Test_IT124_ProductionClassifierJudgesRealBoundary()
        {
            var view = new RecordingView();
            PendingView pending = Pending(2001U, 2, 2);
            EConsumeOutcome threw = SteamP2PFriends.Adapters.Item.Patches.ItemOwnedPresentationRuntimePatch
                .CreateOrClassify(view, pending, _ => throw new InvalidOperationException("native after entry"));
            EConsumeOutcome noAssociation = SteamP2PFriends.Adapters.Item.Patches.ItemOwnedPresentationRuntimePatch
                .CreateOrClassify(view, pending, _ => { });
            EConsumeOutcome associated = SteamP2PFriends.Adapters.Item.Patches.ItemOwnedPresentationRuntimePatch
                .CreateOrClassify(view, pending,
                    _ => view.ModelItems.Add(new ModelView { InstanceId = 2001U, Handle = new object() }));
            return threw == EConsumeOutcome.Uncertain
                && noAssociation == EConsumeOutcome.Uncertain
                && associated == EConsumeOutcome.Consumed;
        }

        internal static bool Test_IT132_PresentSharedReadsSortOrderBeforeSideEffects()
        {
            var view = new RecordingView { ThrowOnSortOrder = true };
            view.Items.Add(new ItemView
            {
                InstanceId = 3003U, AssetId = 1, Amount = 1, Quality = 100,
                State = new byte[0]
            });
            view.PendingItems.Add(Pending(3001U, 2, 2));
            view.ModelItems.Add(new ModelView { InstanceId = 3002U, Handle = new object() });
            bool threw = false;
            try { SteamP2PFriends.Adapters.Item.ItemLocalPresentationPipeline.PresentSharedRegion(view, 2, 2); }
            catch (InvalidOperationException) { threw = true; }
            return threw && view.PendingItems.Count == 1 && view.ModelItems.Count == 1 && !view.Networked;
        }

        internal static bool Test_IT131_ClassifierDistinguishesPrecheckAndPostEntryFailure()
        {
            var blocked = new RecordingView { Projection = false };
            PendingView pending = Pending(2001U, 2, 2);
            bool spawned = false;
            EConsumeOutcome notStarted = SteamP2PFriends.Adapters.Item.Patches.ItemOwnedPresentationRuntimePatch
                .CreateOrClassify(blocked, pending, _ => spawned = true);
            var postEntryView = new RecordingView();
            EConsumeOutcome postEntry = SteamP2PFriends.Adapters.Item.Patches.ItemOwnedPresentationRuntimePatch
                .CreateOrClassify(postEntryView, pending, _ =>
                {
                    postEntryView.ModelItems.Add(new ModelView { InstanceId = 2001U, Handle = new object() });
                    throw new InvalidOperationException("effect after registration");
                });
            return notStarted == EConsumeOutcome.NotStarted && !spawned
                && postEntry == EConsumeOutcome.Consumed;
        }

        internal static bool Test_IT130_OwnedBatchDefersOnStaleGeneration()
        {
            // 覆盖纠正：ShouldDeferOwnedBatch 已并入共同 consumer 的身份门
            // （ConsumeOwnedBatch 的 committed!=core → Empty 暂缓）；IT130 改测
            // TryGetConnectionGeneration 不可用/不一致时 owned 不消费的真实门组合。
            var projection = new DemandProjectionEngine(new ObserverSpatialAuthority());
            var policy = ItemDemandPolicy.Create(0, 2);
            projection.Register(policy);
            var seam = new ItemProductionControlSeam(
                new ItemPreparationAuthority(
                    new ItemPreparationAndFirstDemandTests.TestNativeItemWarehouseBridge()),
                new ItemMetadataStore(),
                new ItemExecutionPort(
                    new ItemPreparationAuthority(
                        new ItemPreparationAndFirstDemandTests.TestNativeItemWarehouseBridge()),
                    new ItemMetadataStore(), _ => 1U, ItemLifecyclePolicy.Create()),
                projection,
                policy, 2f);
            // 未 BeginSession：engine 无 token → 暂缓语义成立
            bool noTokenDefers = !seam.TryGetConnectionGeneration(1000UL, out _);
            seam.BeginSession(new SessionEpoch(130UL));
            bool registeredToken = seam.TryGetConnectionGeneration(1000UL, out _);
            return noTokenDefers && !registeredToken;
        }

        internal static bool Test_IT129_ClientPendingBatchContractIsPlural()
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Static;
            Type manager = typeof(ItemManager);
            var shape = SteamP2PFriends.Adapters.Item.Patches.ItemSettlementConsumptionPatch
                .InspectPendingBatchShape(manager);
            bool client = shape == SteamP2PFriends.Adapters.Item.Patches
                .ItemSettlementConsumptionPatch.PendingBatchShape.Client;
            bool singularAbsent = manager.GetField("pendingInstantiation", flags) == null;
            bool pluralPresent = manager.GetField("pendingInstantiations", flags) != null;
            return client && singularAbsent && pluralPresent;
        }

        internal static bool Test_IT126_NonRetiredStepFiveKeepsNativePassthrough()
        {
            var facts = new ItemStepFiveFacts
            {
                Retired = false,
                Dedicated = false,
                IsServer = true,
                CenterSafe = true
            };
            int admitted = 0;
            bool passthrough = SteamP2PFriends.Adapters.Item.Patches.ItemManagerRegionSyncPatch
                .HandleNativeStepFive(facts, new ItemStepFiveLoadedProjection(_ => { }),
                    () => admitted++, 8, 2, 2);
            var retired = facts;
            retired.Retired = true;
            bool retiredPassthrough = SteamP2PFriends.Adapters.Item.Patches.ItemManagerRegionSyncPatch
                .HandleNativeStepFive(retired, new ItemStepFiveLoadedProjection(_ => { }),
                    () => admitted++, 8, 2, 2);
            return passthrough && admitted == 1 && !retiredPassthrough;
        }

        internal static bool Test_IT128_AdmissionRebuiltPerSessionAndStaleRejected()
        {
            var view = new RecordingView();
            ItemProductionControlSeam seam = CreateOwnedSeam(view, 5UL, 1000UL, 101UL);
            ItemStepFiveAdmission first = seam.Admission;
            seam.EndSession();
            bool cleared = ItemSettlementGate.StepFiveAdmission == null;
            seam.BeginSession(new SessionEpoch(6UL));
            bool restored = ReferenceEquals(ItemSettlementGate.StepFiveAdmission, seam.Admission)
                && !ReferenceEquals(seam.Admission, first);
            first.Admit(new ItemStepFiveRequest(1000UL, 101UL,
                    new RegionKey(2, 2), 1, 8, true),
                new ItemStepFiveLoadedProjection(_ => { }));
            return cleared && restored && seam.ProjectedDemandRegionCount == 0;
        }

        internal static bool Test_IT123_UnconfirmedSideEffectSurvivesRegionExit()        {
            var settlement = new ItemReturnSettlement(
                null, new ItemMetadataStore(), _ => 0, () => 0f, () => 0,
                _ => false, _ => 0U, () => 1UL);
            var region = new RegionKey(2, 2);
            settlement.MarkUnconfirmedSideEffect(1000UL, 101UL, region, 2001U);
            bool marked = settlement.HasUnconfirmedSideEffect(1000UL, 101UL, 2001U);
            settlement.ReleaseHalfPacketForExitedRegion(1000UL, 101UL, region);
            bool survives = settlement.HasUnconfirmedSideEffect(1000UL, 101UL, 2001U);
            bool otherItemFree = !settlement.HasUnconfirmedSideEffect(1000UL, 101UL, 2002U);
            bool otherTokenBlocked = settlement.HasUnconfirmedSideEffect(1000UL, 102UL, 2001U);
            settlement.ResetIdentity();
            bool survivesIdentityReset = settlement.HasUnconfirmedSideEffect(1000UL, 102UL, 2001U);
            return marked && survives && otherItemFree && otherTokenBlocked && survivesIdentityReset
                && settlement.UnconfirmedSideEffectCount == 1;
        }

        internal static bool Test_IT125_IncrementDestroyPolicyMatrix()
        {
            var patch = typeof(SteamP2PFriends.Adapters.Item.Patches.ItemOwnedPresentationRuntimePatch);
            var increment = patch.GetMethod("ShouldEnqueueIncrement",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var destroy = patch.GetMethod("ShouldSuppressDestroy",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (increment == null || destroy == null) return false;
            bool Increment(bool retired, bool host, bool networked) =>
                (bool)increment.Invoke(null, new object[] { retired, host, networked });
            bool Destroy(bool retired, bool host, bool ready) =>
                (bool)destroy.Invoke(null, new object[] { retired, host, ready });
            return Increment(true, true, true)
                && !Increment(true, false, true)
                && !Increment(true, true, false)
                && !Increment(false, true, true)
                && Destroy(true, true, true)
                && !Destroy(true, false, true)
                && !Destroy(true, true, false)
                && !Destroy(false, true, true);
        }

        private static PendingView Pending(uint id, byte x, byte y)
        {
            return new PendingView
            {
                RegionX = x,
                RegionY = y,
                InstanceId = id,
                AssetId = 276,
                Amount = 1,
                Quality = 100,
                State = new byte[0]
            };
        }

        internal static bool Test_IT105_ReentryPresentKeepsOnePending()
        {
            var view = new RecordingView();
            view.Items.Add(Item(1001U, 276));
            view.Items.Add(Item(1002U, 16));
            var pipeline = new ItemLocalPresentationPipeline(view);
            pipeline.PresentShared(2, 2);
            pipeline.Unpresent(2, 2);
            pipeline.PresentShared(2, 2);
            return view.Items.Count == 2
                && Copies(view, 1001U) == 1
                && Copies(view, 1002U) == 1
                && view.PendingItems.Count == 2
                && view.ModelItems.Count == 0;
        }

        private static ItemView Item(uint id, ushort asset)
        {
            return new ItemView { InstanceId = id, AssetId = asset, Amount = 1, Quality = 100, State = new byte[0] };
        }

        private static int Copies(RecordingView view, uint id)
        {
            int count = 0;
            for (int i = 0; i < view.PendingItems.Count; i++)
                if (view.PendingItems[i].InstanceId == id) count++;
            return count;
        }

        private sealed class RecordingView : IItemLocalPresentationView
        {
            public readonly List<ItemView> Items = new List<ItemView>();
            public readonly List<PendingView> PendingItems = new List<PendingView>();
            public readonly List<ModelView> ModelItems = new List<ModelView>();
            public bool Loaded;
            public bool Networked;
            public bool Projection = true;
            public byte ReadyX = 2;
            public byte ReadyY = 2;

            public bool RegionReady(byte x, byte y) => x == ReadyX && y == ReadyY;
            public IReadOnlyList<ItemView> ReadCurrentItems(byte x, byte y) =>
                RegionReady(x, y) ? Items : EmptyItems;
            private static readonly List<ItemView> EmptyItems = new List<ItemView>();
            public IList Pending => PendingItems;
            public IList Models(byte x, byte y) => ModelItems;
            public bool GetNetworked(byte x, byte y) => Networked;
            public void SetNetworked(byte x, byte y, bool value) => Networked = value;
            public bool ProjectionAvailable(byte x, byte y) => Projection;
            public void SetLoaded(byte x, byte y, bool value) => Loaded = value;
            public float ItemSortOrder(ItemView item)
            {
                if (ThrowOnSortOrder) throw new InvalidOperationException("sort order unavailable");
                return 0f;
            }
            public void DestroyUnity(object modelHandle) { }
            public bool ThrowOnNotify;
            public bool ThrowOnSortOrder;
            public void NotifyRemoved(ModelView model)
            {
                if (ThrowOnNotify) throw new System.InvalidOperationException("item drop association missing");
            }
            public void PlayRemovalEffect(ModelView model) { }
            public int DeferredFinishCalls;
            public void FinishDeferredDestroy(byte x, byte y) { DeferredFinishCalls++; }
        }
    }
}
