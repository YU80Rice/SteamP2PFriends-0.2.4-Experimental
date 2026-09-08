using SteamP2PFriends.Adapters.Resource;
using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver.SPI;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends.WhitelistTests
{
    internal static class ResourceProductionControlSeamTests
    {
        internal static bool Test_M6P01_ObserverUnionAcquiresOnce()
        {
            var fake = new FakeResourceAdapters();
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));

            seam.UpdateObserver(100UL, 1001UL, 10, 10);
            seam.UpdateObserver(200UL, 2001UL, 10, 10);

            return seam.GetDemand(new RegionKey(10, 10)) == 2
                && fake.Lifecycle.Acquires.Count == 1
                && fake.Lifecycle.Acquires[0].ActiveDemandCount == 1
                && fake.Replication.Entered.Count == 2;
        }

        internal static bool Test_M6P02_LastObserverSchedulesTwoSecondRelease()
        {
            var fake = new FakeResourceAdapters();
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));
            seam.UpdateObserver(100UL, 1001UL, 10, 10);
            seam.RemoveObserver(100UL);

            seam.Tick(1.99f);
            bool heldDuringHysteresis = seam.IsLeased(new RegionKey(10, 10))
                && fake.Lifecycle.Releases.Count == 0;
            seam.Tick(0.01f);

            return heldDuringHysteresis
                && !seam.IsLeased(new RegionKey(10, 10))
                && fake.Lifecycle.Releases.Count == 1
                && Math.Abs(fake.Lifecycle.Releases[0].RegionKey.X - 10) == 0;
        }

        internal static bool Test_M6P03_ReentryCancelsReleaseWithoutSecondAcquire()
        {
            var fake = new FakeResourceAdapters();
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));
            seam.UpdateObserver(100UL, 1001UL, 10, 10);
            seam.RemoveObserver(100UL);
            seam.Tick(1.0f);
            seam.UpdateObserver(100UL, 1002UL, 10, 10);
            seam.Tick(2.0f);

            return seam.GetDemand(new RegionKey(10, 10)) == 1
                && seam.IsLeased(new RegionKey(10, 10))
                && fake.Lifecycle.Acquires.Count == 1
                && fake.Lifecycle.Releases.Count == 0
                && seam.ReentryCount == 1
                && fake.Replication.Entered.Count == 2;
        }

        internal static bool Test_M6P04_ReconnectAndSessionResetInvalidateState()
        {
            var fake = new FakeResourceAdapters();
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));
            seam.UpdateObserver(100UL, 1001UL, 10, 10);
            seam.UpdateObserver(100UL, 1002UL, 10, 10);

            bool reconnectObserved = fake.Replication.Exited.Count == 1
                && fake.Replication.Entered.Count == 2
                && fake.Replication.Exited[0].ConnectionToken == 1001UL
                && fake.Replication.Entered[1].ConnectionToken == 1002UL
                && fake.Lifecycle.Disconnects.Count == 1
                && fake.Lifecycle.Disconnects[0].ConnectionToken == 1001UL;

            seam.EndSession();
            return reconnectObserved
                && seam.ActiveLeaseCount == 0
                && seam.ObserverCount == 0
                && fake.Lifecycle.SessionEnds == 1
                && fake.Replication.ResetCount >= 2;
        }

        internal static bool Test_M6P06_AdvanceBeforeFlushAllowsSameFrameReentry()
        {
            var fake = new FakeResourceAdapters();
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));
            seam.UpdateObserver(100UL, 1001UL, 10, 10);
            seam.RemoveObserver(100UL);

            seam.AdvanceTime(2.0f);
            seam.UpdateObserver(100UL, 1002UL, 10, 10);
            seam.Flush(0f);

            return seam.IsLeased(new RegionKey(10, 10))
                && seam.GetDemand(new RegionKey(10, 10)) == 1
                && fake.Lifecycle.Releases.Count == 0;
        }

        internal static bool Test_M6P07_RealResourceAdapterHighLevelSeam()
        {
            var adapter = new ResourceDomainAdapter();
            adapter.OnSessionBegin(99U);
            var seam = new ResourceProductionControlSeam(
                adapter,
                adapter,
                ResourceRegionLifecycleAdapter.GetGeneration,
                64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(99UL));
            RegionKey regionKey = new RegionKey(5, 6);
            // 真实 adapter 在无原生边界(测试环境区域树字典为空)时抛
            // ResourceNativeSnapshotUnavailableException:按暂缓语义隔离,不再向调用方抛出。
            bool threw = false;
            try
            {
                seam.UpdateObserver(777UL, 5001UL, regionKey.X, regionKey.Y);
            }
            catch (Exception)
            {
                threw = true;
            }

            return !threw
                && !seam.IsLeased(regionKey)
                && seam.GetDemand(regionKey) == 0
                && seam.PendingReleaseCount == 0
                && seam.PendingAcquireRetryCount == 1;
        }

        internal static bool Test_M6P08_StaleRegionGenerationDelaysRelease()
        {
            var fake = new FakeResourceAdapters();
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));
            RegionKey regionKey = new RegionKey(10, 10);
            seam.UpdateObserver(100UL, 1001UL, regionKey.X, regionKey.Y);
            seam.RemoveObserver(100UL);

            fake.Generation = 2U;
            seam.Tick(2.0f);
            bool staleReleaseRejected = fake.Lifecycle.Releases.Count == 0
                && seam.PendingReleaseCount == 1
                && seam.IsLeased(regionKey);

            seam.Tick(2.0f);
            return staleReleaseRejected
                && fake.Lifecycle.Releases.Count == 1
                && fake.Lifecycle.Releases[0].RegionGeneration.Value == 2U;
        }

        internal static bool Test_M6P09_ReleaseFailureRetainsRetryableState()
        {
            var fake = new FakeResourceAdapters();
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));
            RegionKey regionKey = new RegionKey(10, 10);
            seam.UpdateObserver(100UL, 1001UL, regionKey.X, regionKey.Y);
            seam.RemoveObserver(100UL);
            fake.Lifecycle.ThrowOnRelease = true;

            bool didNotEscape = true;
            try { seam.Tick(2.0f); }
            catch { didNotEscape = false; }

            bool retained = seam.PendingReleaseCount == 1 && seam.IsLeased(regionKey);
            fake.Lifecycle.ThrowOnRelease = false;
            seam.Tick(2.0f);
            return didNotEscape && retained
                && seam.PendingReleaseCount == 0
                && !seam.IsLeased(regionKey)
                && fake.Lifecycle.Releases.Count == 2;
        }

        internal static bool Test_M6P11_ReplicationEnterFailureDoesNotCommitDemand()
        {
            var fake = new FakeResourceAdapters();
            fake.Replication.ThrowOnEntered = true;
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));
            RegionKey regionKey = new RegionKey(10, 10);

            bool threw = false;
            try { seam.UpdateObserver(100UL, 1001UL, regionKey.X, regionKey.Y); }
            catch (InvalidOperationException) { threw = true; }

            return threw
                && seam.GetDemand(regionKey) == 0
                && !seam.IsLeased(regionKey)
                && seam.PendingReleaseCount == 0;
        }

        internal static bool Test_M6P12_ReplicationExitFailureRetainsDemand()
        {
            var fake = new FakeResourceAdapters();
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));
            RegionKey regionKey = new RegionKey(10, 10);
            seam.UpdateObserver(100UL, 1001UL, regionKey.X, regionKey.Y);
            fake.Replication.ThrowOnExited = true;

            bool threw = false;
            try { seam.RemoveObserver(100UL); }
            catch (InvalidOperationException) { threw = true; }

            return threw
                && seam.GetDemand(regionKey) == 1
                && seam.IsLeased(regionKey)
                && seam.PendingReleaseCount == 0;
        }

        internal static bool Test_M6P13_MultiRegionEnterFailureCompensates()
        {
            var fake = new FakeResourceAdapters();
            fake.Replication.ThrowOnEnteredAfter = 1;
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 1, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));

            bool threw = false;
            try { seam.UpdateObserver(100UL, 1001UL, 10, 10); }
            catch (InvalidOperationException) { threw = true; }

            return threw
                && seam.GetDemand(new RegionKey(10, 10)) == 0
                && seam.ActiveLeaseCount == 0
                && seam.PendingReleaseCount == 0
                && fake.Replication.RestoreReplicationStateCalls >= 1;
        }

        internal static bool Test_M6P14_MultiRegionExitFailureCompensates()
        {
            var fake = new FakeResourceAdapters();
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 1, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));
            seam.UpdateObserver(100UL, 1001UL, 10, 10);
            fake.Replication.ThrowOnExitedAfter = 1;

            bool threw = false;
            try { seam.RemoveObserver(100UL); }
            catch (InvalidOperationException) { threw = true; }

            return threw
                && seam.ObserverCount == 1
                && seam.DemandRegionCount == 9
                && seam.GetDemand(new RegionKey(10, 10)) == 1
                && seam.ActiveLeaseCount == 9
                && fake.Replication.RestoreReplicationStateCalls >= 1;
        }

        internal static bool Test_M6P15_AcquireFailureAfterSideEffectIsIsolated()
        {
            var fake = new FakeResourceAdapters();
            fake.Lifecycle.ThrowOnAcquireAfterSideEffect = true;
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));
            RegionKey regionKey = new RegionKey(10, 10);

            bool threw = false;
            try { seam.UpdateObserver(100UL, 1001UL, regionKey.X, regionKey.Y); }
            catch (InvalidOperationException) { threw = true; }

            // OnAcquire 后段失败按单区域隔离语义处理:不抛、不整批回滚;本区域
            // acquire 补偿被撤销(不执行),区域无 lease 并进入重试登记。
            return !threw
                && seam.GetDemand(regionKey) == 0
                && !seam.IsLeased(regionKey)
                && seam.PendingReleaseCount == 0
                && fake.Lifecycle.Releases.Count == 0
                && fake.Lifecycle.RestoreRegionStateCalls == 0
                && seam.PendingAcquireRetryCount == 1;
        }

        internal static bool Test_M6P16_GenerationReaderFailureRetainsReleaseState()
        {
            var fake = new FakeResourceAdapters();
            bool failGenerationRead = false;
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication,
                _ =>
                {
                    if (failGenerationRead) throw new InvalidOperationException("generation reader unavailable");
                    return fake.Generation;
                },
                64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));
            RegionKey regionKey = new RegionKey(10, 10);
            seam.UpdateObserver(100UL, 1001UL, regionKey.X, regionKey.Y);
            seam.RemoveObserver(100UL);
            failGenerationRead = true;

            bool didNotEscape = true;
            try { seam.Tick(2.0f); }
            catch { didNotEscape = false; }

            return didNotEscape
                && seam.PendingReleaseCount == 1
                && seam.IsLeased(regionKey)
                && fake.Lifecycle.Releases.Count == 0;
        }

        internal static bool Test_M6P17_DisconnectFailureRestoresObserverState()
        {
            var fake = new FakeResourceAdapters();
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));
            RegionKey regionKey = new RegionKey(10, 10);
            seam.UpdateObserver(100UL, 1001UL, regionKey.X, regionKey.Y);
            fake.Lifecycle.ThrowOnDisconnectAfterSideEffect = true;

            bool threw = false;
            try { seam.UpdateObserver(100UL, 1002UL, regionKey.X, regionKey.Y); }
            catch (InvalidOperationException) { threw = true; }

            return threw
                && fake.Lifecycle.RestoreCalls == 1
                && seam.ObserverCount == 1
                && seam.GetDemand(regionKey) == 1
                && seam.IsLeased(regionKey)
                && seam.TryGetConnectionGeneration(100UL, out ulong token)
                && token == 1001UL;
        }

        internal static bool Test_M6P18_ReleaseFailureAfterSideEffectIsCompensated()
        {
            var fake = new FakeResourceAdapters();
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));
            RegionKey regionKey = new RegionKey(10, 10);
            seam.UpdateObserver(100UL, 1001UL, regionKey.X, regionKey.Y);
            seam.RemoveObserver(100UL);
            fake.Lifecycle.ThrowOnRelease = true;

            seam.Tick(2.0f);

            return seam.IsLeased(regionKey)
                && seam.PendingReleaseCount == 1
                && fake.Lifecycle.RestoreRegionStateCalls == 1
                && fake.Lifecycle.Acquires.Count == 1;
        }

        internal static bool Test_M6P20_DisconnectFailureRestoresSnapshotContent()
        {
            var ledger = new ResourceSnapshotReplicationLedger();
            RegionKey regionKey = new RegionKey(10, 10);
            ledger.ResetSession(7UL);
            if (!ledger.EnqueueInitialSnapshot(100UL, 1001UL, regionKey, 4U)) return false;
            ResourceSnapshotRecord before = default(ResourceSnapshotRecord);
            if (!ledger.TryGetSnapshot(100UL, regionKey, out before)) return false;

            ResourceObserverReplicationState state = ledger.CaptureObserverState(100UL);
            if (!ledger.OnObserverDisconnect(100UL, 1001UL)) return false;
            ledger.RestoreObserverState(100UL, state);

            return ledger.TryGetSnapshot(100UL, regionKey, out ResourceSnapshotRecord after)
                && after.SessionEpoch == before.SessionEpoch
                && after.ConnectionToken == before.ConnectionToken
                && after.RegionGeneration == before.RegionGeneration
                && after.DeltaSequence == before.DeltaSequence;
        }

        internal static bool Test_M6P21_AcquireFailureSideEffectIsolated()
        {
            var fake = new FakeResourceAdapters();
            fake.Lifecycle.ThrowOnAcquireAfterSideEffect = true;
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));
            RegionKey regionKey = new RegionKey(10, 10);
            uint initialGeneration = fake.Generation;

            try { seam.UpdateObserver(100UL, 1001UL, regionKey.X, regionKey.Y); }
            catch (InvalidOperationException) { }

            // 隔离语义:补偿被撤销而非执行(Fake 的 generation 副作用按 generation
            // 单调性保留),区域无 lease 并进入重试登记。
            return fake.Generation == initialGeneration + 1U
                && fake.Lifecycle.RestoreRegionStateCalls == 0
                && fake.Lifecycle.Releases.Count == 0
                && !seam.IsLeased(regionKey)
                && seam.PendingAcquireRetryCount == 1;
        }

        internal static bool Test_M6P22_SessionInitializationFailureStaysInactive()
        {
            var fake = new FakeResourceAdapters();
            fake.Lifecycle.ThrowOnSessionBegin = true;
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 0, 2.0f);

            bool threw = false;
            try { seam.BeginSession(new SessionEpoch(7UL)); }
            catch (InvalidOperationException) { threw = true; }

            return threw && !seam.IsSessionActive && seam.ObserverCount == 0
                && seam.ActiveLeaseCount == 0 && seam.PendingReleaseCount == 0;
        }

        internal static bool Test_M6P23_ReplicationFailureRestoresExactSnapshot()
        {
            var fake = new FakeResourceAdapters();
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));
            seam.UpdateObserver(100UL, 1001UL, 10, 10);
            fake.Replication.ThrowOnEntered = true;

            bool threw = false;
            try { seam.UpdateObserver(100UL, 1001UL, 11, 11); }
            catch (InvalidOperationException) { threw = true; }

            return threw
                && fake.Replication.RestoreReplicationStateCalls >= 2
                && fake.Replication.Entered.Count == 1
                && fake.Replication.Entered[0].RegionKey == new RegionKey(10, 10);
        }

        internal static bool Test_M6P24_AdvanceTimeRetainsLeaseOnGenerationRegression()
        {
            var fake = new FakeResourceAdapters { Generation = 5U };
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));
            RegionKey regionKey = new RegionKey(10, 10);
            seam.UpdateObserver(100UL, 1001UL, regionKey.X, regionKey.Y);
            fake.Generation = 4U;

            seam.AdvanceTime(0f);

            return seam.TryGetLease(regionKey, out ResourceProductionLease lease)
                && lease.RegionGeneration.Value == 5U;
        }

        internal static bool Test_M6P25_FlushRetainsPendingReleaseOnGenerationRegression()
        {
            var fake = new FakeResourceAdapters { Generation = 5U };
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));
            RegionKey regionKey = new RegionKey(10, 10);
            seam.UpdateObserver(100UL, 1001UL, regionKey.X, regionKey.Y);
            seam.RemoveObserver(100UL);
            fake.Generation = 4U;

            seam.Tick(2.0f);

            return seam.TryGetLease(regionKey, out ResourceProductionLease lease)
                && lease.RegionGeneration.Value == 5U
                && seam.PendingReleaseCount == 1
                && fake.Lifecycle.Releases.Count == 0;
        }

        internal static bool Test_M6P26_EndSessionClearsStateWhenLifecycleCleanupThrows()
        {
            var fake = new FakeResourceAdapters();
            fake.Lifecycle.ThrowOnSessionEnd = true;
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));
            seam.UpdateObserver(100UL, 1001UL, 10, 10);

            bool threw = false;
            try { seam.EndSession(); }
            catch (InvalidOperationException) { threw = true; }

            return threw && !seam.IsSessionActive && seam.ObserverCount == 0
                && seam.ActiveLeaseCount == 0 && seam.PendingReleaseCount == 0
                && seam.RepairRequired && fake.Replication.ResetCount >= 2;
        }

        internal static bool Test_M6P27_EndSessionClearsStateWhenReplicationCleanupThrows()
        {
            var fake = new FakeResourceAdapters();
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));
            seam.UpdateObserver(100UL, 1001UL, 10, 10);
            fake.Replication.ThrowOnReset = true;

            bool threw = false;
            try { seam.EndSession(); }
            catch (InvalidOperationException) { threw = true; }

            return threw && !seam.IsSessionActive && seam.ObserverCount == 0
                && seam.ActiveLeaseCount == 0 && seam.PendingReleaseCount == 0
                && seam.RepairRequired && fake.Lifecycle.SessionEnds == 1;
        }

        internal static bool Test_M6P28_AcquireGenerationRegressionFailsClosed()
        {
            var fake = new FakeResourceAdapters { Generation = 5U };
            int readCount = 0;
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication,
                _ => readCount++ == 0 ? 5U : 4U, 64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));

            bool threw = false;
            try { seam.UpdateObserver(100UL, 1001UL, 10, 10); }
            catch (ResourceAcquireRejectedException) { threw = true; }

            // generation 回退 = 区域曾重建,拒绝 lease 的 fail-closed 语义保留;
            // 但按单区域隔离处理:不抛、不回滚其他区域,进入重试登记。
            return !threw && seam.GetDemand(new RegionKey(10, 10)) == 0
                && !seam.IsLeased(new RegionKey(10, 10))
                && fake.Lifecycle.RestoreRegionStateCalls == 0
                && seam.PendingAcquireRetryCount == 1;
        }

        internal static bool Test_M6P29_ExitGenerationRegressionRetainsStoredLease()
        {
            var fake = new FakeResourceAdapters { Generation = 5U };
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));
            RegionKey regionKey = new RegionKey(10, 10);
            seam.UpdateObserver(100UL, 1001UL, regionKey.X, regionKey.Y);
            fake.Generation = 4U;
            seam.RemoveObserver(100UL);

            return seam.TryGetLease(regionKey, out ResourceProductionLease lease)
                && lease.RegionGeneration.Value == 5U
                && seam.PendingReleaseCount == 1;
        }

        internal static bool Test_M6P30_RepairRequiredRejectsNewSession()
        {
            var fake = new FakeResourceAdapters();
            fake.Lifecycle.ThrowOnSessionEnd = true;
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));
            bool endThrew = false;
            try { seam.EndSession(); }
            catch (InvalidOperationException) { endThrew = true; }

            bool beginThrew = false;
            try { seam.BeginSession(new SessionEpoch(8UL)); }
            catch (InvalidOperationException) { beginThrew = true; }

            return endThrew && beginThrew && seam.RepairRequired && !seam.IsSessionActive;
        }

        internal static bool Test_M6P31_CaptureRegionFailureIsolationKeepsOtherRegions()
        {
            var fake = new FakeResourceAdapters();
            RegionKey failedRegion = new RegionKey(11, 10);
            fake.Lifecycle.ThrowOnCaptureRegionStateFor = failedRegion;
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 1, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));
            RegionKey center = new RegionKey(10, 10);

            // 预取理想结果：快照失败的单一区域被隔离/跳过，其余区域 acquire 全部保留，
            // 不整批回滚，也不向调用方抛出（可随后续 Update 重试）。
            bool threw = false;
            try { seam.UpdateObserver(100UL, 1001UL, center.X, center.Y); }
            catch (InvalidOperationException) { threw = true; }

            // 成功区域以当前 generation 建立 lease（1123 §3：成功区 acquire 保留且代次正确）。
            bool isolated = !threw
                && seam.TryGetLease(center, out ResourceProductionLease centerLease)
                && centerLease.RegionGeneration.Value == fake.Generation
                && !seam.IsLeased(failedRegion)
                && seam.ActiveLeaseCount == 8
                && fake.Lifecycle.Acquires.Count == 8
                && fake.Lifecycle.RestoreRegionStateCalls == 0
                && seam.PendingAcquireRetryCount == 1;

            // 失败区的下次重试（1123 §3 原始验收）：一般失败 10s 到期重试成功后补齐
            // lease、清除重试登记，全程不产生补偿恢复。
            fake.Lifecycle.ThrowOnCaptureRegionStateFor = null;
            seam.AdvanceTime(10.0f);
            seam.UpdateObserver(100UL, 1001UL, center.X, center.Y);

            return isolated
                && seam.IsLeased(failedRegion)
                && seam.ActiveLeaseCount == 9
                && fake.Lifecycle.Acquires.Count == 9
                && seam.PendingAcquireRetryCount == 0
                && fake.Lifecycle.RestoreRegionStateCalls == 0;
        }

        internal static bool Test_M6P32_SnapshotUnavailableDefersRetryThenAcquires()
        {
            var fake = new FakeResourceAdapters();
            RegionKey deferredRegion = new RegionKey(11, 10);
            fake.Lifecycle.DeferOnCaptureRegionStateFor = deferredRegion;
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 1, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));
            RegionKey center = new RegionKey(10, 10);

            // 预取理想结果：原生 foliage 尚未生成的区域（快照不可用）被暂缓——
            // 不向调用方抛出（不计 fault、不触发指数退避）、不整批回滚，
            // 进入短延迟重试队列；延迟到期后的下一次 Update 重试成功并清除暂缓标记。
            bool threw = false;
            try { seam.UpdateObserver(100UL, 1001UL, center.X, center.Y); }
            catch (InvalidOperationException) { threw = true; }

            bool deferredIsolated = !threw
                && seam.IsLeased(center)
                && !seam.IsLeased(deferredRegion)
                && seam.PendingAcquireRetryCount == 1;

            seam.AdvanceTime(2.0f);
            fake.Lifecycle.DeferOnCaptureRegionStateFor = null;
            seam.UpdateObserver(100UL, 1001UL, center.X, center.Y);

            return deferredIsolated
                && seam.IsLeased(deferredRegion)
                && seam.PendingAcquireRetryCount == 0
                && fake.Lifecycle.Acquires.Count == 9
                && fake.Lifecycle.RestoreRegionStateCalls == 0;
        }

        internal static bool Test_M6P19_ReleaseRejectionDoesNotRunCompensation()
        {
            var fake = new FakeResourceAdapters();
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, _ => fake.Generation, 64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(7UL));
            RegionKey regionKey = new RegionKey(10, 10);
            seam.UpdateObserver(100UL, 1001UL, regionKey.X, regionKey.Y);
            seam.RemoveObserver(100UL);
            fake.Lifecycle.RejectRelease = true;

            seam.Tick(2.0f);

            return fake.Lifecycle.Acquires.Count == 1
                && seam.PendingReleaseCount == 1
                && seam.IsLeased(regionKey);
        }

        internal static bool Test_M6P05_RegionGenerationFlowsIntoSnapshotAndRelease()
        {
            var fake = new FakeResourceAdapters { Generation = 4U };
            var seam = new ResourceProductionControlSeam(
                fake.Lifecycle, fake.Replication, key => fake.Generation, 64, 0, 2.0f);
            seam.BeginSession(new SessionEpoch(9UL));
            RegionKey regionKey = new RegionKey(3, 4);
            seam.UpdateObserver(100UL, 1001UL, regionKey.X, regionKey.Y);
            bool generationObserved = seam.TryGetLease(regionKey, out ResourceProductionLease lease)
                && lease.RegionGeneration.Value == 4U;
            seam.RemoveObserver(100UL);
            seam.Tick(2.0f);

            return fake.Replication.Entered.Count == 1
                && generationObserved
                && fake.Lifecycle.Releases.Count == 1
                && fake.Lifecycle.Releases[0].RegionGeneration.Value == 4U;
        }

        private sealed class FakeResourceAdapters
        {
            internal uint Generation = 1U;
            internal readonly FakeLifecycleAdapter Lifecycle;
            internal readonly FakeReplicationAdapter Replication = new FakeReplicationAdapter();

            internal FakeResourceAdapters()
            {
                Lifecycle = new FakeLifecycleAdapter(
                    () => Generation,
                    value => Generation = value);
            }
        }

        private sealed class FakeLifecycleAdapter : ILifecycleDomainAdapter,
            IReversibleObserverDisconnectAdapter, IReversibleRegionLifecycleAdapter
        {
            internal readonly List<LeaseTicket> Acquires = new List<LeaseTicket>();
            internal readonly List<LeaseTicket> Releases = new List<LeaseTicket>();
            internal readonly List<DisconnectEvent> Disconnects = new List<DisconnectEvent>();
            internal int SessionEnds;
            internal bool ThrowOnRelease;
            internal bool RejectRelease;
            internal bool ThrowOnAcquireAfterSideEffect;
            internal bool ThrowOnDisconnectAfterSideEffect;
            internal RegionKey? ThrowOnCaptureRegionStateFor;
            internal RegionKey? DeferOnCaptureRegionStateFor;
            internal bool ThrowOnSessionBegin;
            internal bool ThrowOnSessionEnd;
            internal int RestoreCalls;
            internal int RestoreRegionStateCalls;
            private readonly Func<uint> _readGeneration;
            private readonly Action<uint> _writeGeneration;

            internal FakeLifecycleAdapter(Func<uint> readGeneration, Action<uint> writeGeneration)
            {
                _readGeneration = readGeneration;
                _writeGeneration = writeGeneration;
            }

            public DomainId DomainId => DomainIds.Resource;
            public string DisplayName => "Resource";
            public string Capability => "test";
            public void OnSessionBegin(uint sessionEpoch)
            {
                if (ThrowOnSessionBegin) throw new InvalidOperationException("test session begin failure");
            }
            public void OnSessionEnd()
            {
                SessionEnds++;
                if (ThrowOnSessionEnd) throw new InvalidOperationException("test session end failure");
            }
            public void OnAcquire(LeaseTicket ticket)
            {
                Acquires.Add(ticket);
                if (ThrowOnAcquireAfterSideEffect)
                {
                    _writeGeneration(_readGeneration() + 1U);
                    throw new InvalidOperationException("test acquire failure after side effect");
                }
            }
            public void OnRelease(LeaseTicket ticket)
            {
                Releases.Add(ticket);
                if (RejectRelease)
                    throw new ResourceReleaseRejectedException("test-release-rejected");
                if (ThrowOnRelease)
                {
                    _writeGeneration(_readGeneration() + 1U);
                    throw new InvalidOperationException("test release failure");
                }
            }
            public void OnTick(float deltaTime) { }
            public void OnObserverDisconnect(ulong observerId, ulong connectionToken)
            {
                Disconnects.Add(new DisconnectEvent(observerId, connectionToken));
                if (ThrowOnDisconnectAfterSideEffect)
                    throw new InvalidOperationException("test disconnect failure after side effect");
            }

            public object CaptureObserverDisconnectState(ulong observerId, ulong connectionToken)
            {
                return new object();
            }

            public void RestoreObserverDisconnectState(ulong observerId, ulong connectionToken, object state)
            {
                RestoreCalls++;
            }

            public object CaptureRegionState(RegionKey regionKey)
            {
                if (DeferOnCaptureRegionStateFor.HasValue
                    && regionKey == DeferOnCaptureRegionStateFor.Value)
                {
                    throw new ResourceNativeSnapshotUnavailableException(
                        "native-resource-trees-unavailable");
                }
                if (ThrowOnCaptureRegionStateFor.HasValue
                    && regionKey == ThrowOnCaptureRegionStateFor.Value)
                {
                    throw new InvalidOperationException("test capture region snapshot failure");
                }
                return new FakeRegionState(_readGeneration(), Acquires.Count, Releases.Count);
            }

            public void RestoreRegionState(RegionKey regionKey, object state)
            {
                FakeRegionState snapshot = state as FakeRegionState;
                if (snapshot == null) throw new InvalidOperationException("missing region state");
                _writeGeneration(snapshot.Generation);
                RestoreRegionStateCalls++;
            }
        }

        private sealed class FakeRegionState
        {
            internal FakeRegionState(uint generation, int acquireCount, int releaseCount)
            {
                Generation = generation;
                AcquireCount = acquireCount;
                ReleaseCount = releaseCount;
            }

            internal uint Generation { get; }
            internal int AcquireCount { get; }
            internal int ReleaseCount { get; }
        }

        private readonly struct DisconnectEvent
        {
            internal DisconnectEvent(ulong observerId, ulong connectionToken)
            {
                ObserverId = observerId;
                ConnectionToken = connectionToken;
            }

            internal ulong ObserverId { get; }
            internal ulong ConnectionToken { get; }
        }

        private sealed class FakeReplicationAdapter : IStateReplicationAdapter,
            IReversibleObserverReplicationAdapter
        {
            internal readonly List<ReplicationEvent> Entered = new List<ReplicationEvent>();
            internal readonly List<ReplicationEvent> Exited = new List<ReplicationEvent>();
            internal int ResetCount;
            internal bool ThrowOnEntered;
            internal bool ThrowOnExited;
            internal bool ThrowOnReset;
            internal int ThrowOnEnteredAfter = -1;
            internal int ThrowOnExitedAfter = -1;
            internal int RestoreReplicationStateCalls;

            public DomainId DomainId => DomainIds.Resource;
            public string DisplayName => "Resource";
            public void OnObserverEntered(ulong observerId, ulong connectionToken, RegionKey regionKey)
            {
                Entered.Add(new ReplicationEvent(observerId, connectionToken, regionKey, 0U));
                if (ThrowOnEntered || (ThrowOnEnteredAfter >= 0 && Entered.Count > ThrowOnEnteredAfter))
                    throw new InvalidOperationException("test replication enter failure");
            }
            public void OnObserverExited(ulong observerId, ulong connectionToken, RegionKey regionKey)
            {
                Exited.Add(new ReplicationEvent(observerId, connectionToken, regionKey, 0U));
                if (ThrowOnExited || (ThrowOnExitedAfter >= 0 && Exited.Count > ThrowOnExitedAfter))
                    throw new InvalidOperationException("test replication exit failure");
            }
            public void OnReplicationTick(float deltaTime) { }
            public void ResetReplication(uint sessionEpoch)
            {
                ResetCount++;
                if (ThrowOnReset) throw new InvalidOperationException("test replication reset failure");
            }

            public object CaptureObserverReplicationState(ulong observerId, ulong connectionToken)
            {
                return new FakeReplicationState(
                    new List<ReplicationEvent>(Entered),
                    new List<ReplicationEvent>(Exited));
            }

            public void RestoreObserverReplicationState(
                ulong observerId, ulong connectionToken, object state)
            {
                FakeReplicationState snapshot = state as FakeReplicationState;
                if (snapshot == null) throw new InvalidOperationException("missing replication state");
                Entered.Clear();
                Entered.AddRange(snapshot.Entered);
                Exited.Clear();
                Exited.AddRange(snapshot.Exited);
                RestoreReplicationStateCalls++;
            }
        }

        private sealed class FakeReplicationState
        {
            internal FakeReplicationState(
                List<ReplicationEvent> entered,
                List<ReplicationEvent> exited)
            {
                Entered = entered;
                Exited = exited;
            }

            internal List<ReplicationEvent> Entered { get; }
            internal List<ReplicationEvent> Exited { get; }
        }

        private readonly struct ReplicationEvent
        {
            internal ReplicationEvent(ulong observerId, ulong connectionToken, RegionKey regionKey, uint regionGeneration)
            {
                ObserverId = observerId;
                ConnectionToken = connectionToken;
                RegionKey = regionKey;
                RegionGeneration = regionGeneration;
            }

            internal ulong ObserverId { get; }
            internal ulong ConnectionToken { get; }
            internal RegionKey RegionKey { get; }
            internal uint RegionGeneration { get; }
        }
    }
}
