using System;
using System.Reflection;
using System.Reflection.Emit;
using SteamP2PFriends.Core.Build;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// 模块化测试启动器 (Modular TestRunner)
    /// 统一按 Evidence Class 分层组织并执行全量回归测试套件。
    /// </summary>
    internal static class Program
    {
        private static EvidenceClass _currentEvidenceClass = EvidenceClass.PureMemory;

        private static int Main(string[] args)
        {
            InstallBattlEyeTypeResolutionStub();

            Console.WriteLine("===============================================================");
            Console.WriteLine("=== SteamP2PFriends Modular TestRunner (Target: 360 PASS) ===");
            Console.WriteLine("===============================================================");
            int total = 0, passed = 0, failed = 0;

            _currentEvidenceClass = EvidenceClass.PureMemory;
            Console.WriteLine("\n=== Evidence Class: PureMemory ===");
            RunTest("EvidenceClass Catalog + Registration Closure", () =>
                EvidenceClassCatalogTests.Test_All() && RegistrationClosureTests.Test_All(),
                ref total, ref passed, ref failed);

            #region 1. Core & Logging Policy Tests (4 Tests)
            Console.WriteLine("\n--- [PureMemory / Domain 1/8: Core & Diagnostics Policy] ---");
            RunTest("LOG1 Markers", LoggingPolicyTests.Test_LegacyDiagnosticMarkersAreClassified, ref total, ref passed, ref failed);
            RunTest("LOG2 Defaults", LoggingPolicyTests.Test_VerboseToggleIsAtomicAndDefaultsOff, ref total, ref passed, ref failed);
            RunTest("LOG3 Labels", LoggingPolicyTests.Test_LegacyLabelsAreRemovedAtOutputBoundary, ref total, ref passed, ref failed);
            RunTest("LOG4 Tags", LoggingPolicyTests.Test_InternalDiagnosticTagsAreRemovedFromOperationalText, ref total, ref passed, ref failed);
            RunTest("ID Contract", IdentityContractTests.Test_All, ref total, ref passed, ref failed);
            #endregion

            #region 2. MultiObserver Control Plane & Spatial Index Tests (18 Tests)
            Console.WriteLine("\n--- [PureMemory / Domain 2/8: MultiObserver Control Plane & Spatial Index] ---");
            RunTest("SPI01 Grid2DDiff", SpatialObserverIndexTests.Test_SPI01_Grid2DDiffCalculation, ref total, ref passed, ref failed);
            RunTest("SPI02 Bound1DDiff", SpatialObserverIndexTests.Test_SPI02_Bound1DDiffCalculation, ref total, ref passed, ref failed);
            RunTest("SPI03 DisconnectReleasesAll", SpatialObserverIndexTests.Test_SPI03_ObserverDisconnectReleasesAll, ref total, ref passed, ref failed);
            RunTest("SPI04 ReconnectInvalidation", SpatialObserverIndexTests.Test_SPI04_ReconnectTokenInvalidatesOldState, ref total, ref passed, ref failed);
            RunTest("SPI05 Grid2DShapeClipping", SpatialObserverIndexTests.Test_SPI05_Grid2DProjectionIsChebyshevAndWorldClipped, ref total, ref passed, ref failed);
            RunTest("M01 PendingIsObserver", MultiObserverShadowTests.Test_M01_PendingAuthorizationDoesNotRemoveWorldPresence, ref total, ref passed, ref failed);
            RunTest("M02 DemandRefCount", MultiObserverShadowTests.Test_M02_OverlappingDemandUsesReferenceCounts, ref total, ref passed, ref failed);
            RunTest("M03 ConnectionGeneration", MultiObserverShadowTests.Test_M03_ConnectionGenerationIsSessionMonotonic, ref total, ref passed, ref failed);
            RunTest("M04 DisconnectEpoch", MultiObserverShadowTests.Test_M04_ObserverDisconnectDoesNotAdvanceSessionEpoch, ref total, ref passed, ref failed);
            RunTest("M05 EdgeRelevance", MultiObserverShadowTests.Test_M05_EdgeRelevanceIsBounded, ref total, ref passed, ref failed);
            RunTest("M06 DuplicateObserver", MultiObserverShadowTests.Test_M06_DuplicateObserverIsIgnoredWithoutDoubleDemand, ref total, ref passed, ref failed);
            RunTest("M07 MovementDemand", MultiObserverShadowTests.Test_M07_MovementTransfersDemandAtomically, ref total, ref passed, ref failed);
            RunTest("M08 SessionIdentity", MultiObserverShadowTests.Test_M08_UniqueHostSessionReplacesSameMapWithoutIntermediateTick, ref total, ref passed, ref failed);
            RunTest("M09 IncompleteCapture", MultiObserverShadowTests.Test_M09_IncompleteCaptureCannotRemoveObserver, ref total, ref passed, ref failed);
            RunTest("M10 LifecycleSequence", MultiObserverShadowTests.Test_M10_LifecycleSequenceIsMonotonicAcrossRemoveAndReadd, ref total, ref passed, ref failed);
            RunTest("M11 FaultBackoff", MultiObserverShadowTests.Test_M11_ShutdownGateAndFaultBackoffAreBounded, ref total, ref passed, ref failed);
            RunTest("M12 StaleLoaded", MultiObserverShadowTests.Test_M12_StaleLoadedCountIsPreserved, ref total, ref passed, ref failed);
            RunTest("M13 ObserverCapacity", MultiObserverShadowTests.Test_M13_ObserverCapacityFailsClosed, ref total, ref passed, ref failed);
            RunTest("M14 InvalidZombieBound", MultiObserverShadowTests.Test_M14_InvalidZombieBoundDoesNotCreateFunctionalDemand, ref total, ref passed, ref failed);
            RunTest("M15 ConnectionGenerationOverflow", MultiObserverShadowTests.Test_M15_ConnectionGenerationOverflowFailsClosed, ref total, ref passed, ref failed);
            // 票 02：共享投影引擎按声明式 Demand Policy 从唯一观察者事实算出 typed Domain Demand。
            RunTest("DPE01 SharedFactSingle", DemandProjectionEngineTests.Test_DPE01_SharedFactIsSingleAndIdempotent, ref total, ref passed, ref failed);
            RunTest("DPE02 ChebyshevClipping", DemandProjectionEngineTests.Test_DPE02_EnumerationIsChebyshevAndWorldClipped, ref total, ref passed, ref failed);
            RunTest("DPE03 OverlapCount", DemandProjectionEngineTests.Test_DPE03_OverlappingObserversDedupAndCount, ref total, ref passed, ref failed);
            RunTest("DPE04 EnterExitDiff", DemandProjectionEngineTests.Test_DPE04_EnterExitDiffFollowsMovement, ref total, ref passed, ref failed);
            RunTest("DPE05 ConnectionInvalidation", DemandProjectionEngineTests.Test_DPE05_ConnectionGenerationInvalidatesOldRegions, ref total, ref passed, ref failed);
            RunTest("DPE06 DomainIsolation", DemandProjectionEngineTests.Test_DPE06_DomainDemandIsNotGrantedAcrossDomains, ref total, ref passed, ref failed);
            RunTest("DPE07 EligibilityDeferral", DemandProjectionEngineTests.Test_DPE07_IneligibleObserverDefersInsteadOfReleasing, ref total, ref passed, ref failed);
            RunTest("DPE08 DeclarationsFailClosed", DemandProjectionEngineTests.Test_DPE08_DeclarationsFailClosed, ref total, ref passed, ref failed);
            RunTest("DPE09 RemovalReleases", DemandProjectionEngineTests.Test_DPE09_RemovalReleasesDomainDemand, ref total, ref passed, ref failed);
            RunTest("DPE10 RestoreProjection", DemandProjectionEngineTests.Test_DPE10_RestoreObserverRegionsRebuildsProjection, ref total, ref passed, ref failed);
            RunTest("DPE11 DeferredGenerationChange", DemandProjectionEngineTests.Test_DPE11_DeferredObserverGenerationChangeReleases, ref total, ref passed, ref failed);
            // 票 03：共享生命周期编排引擎拥有需求聚合、Acquire/Release、滞回、身份校验、
            // retry、补偿调度与局部故障隔离；领域只经 Domain Execution Port 执行原生操作。
            RunTest("LOE01 TypedDemandSharedProjection", LifecycleOrchestrationEngineTests.Test_LOE01_ConsumesTypedDemandFromSharedProjection, ref total, ref passed, ref failed);
            RunTest("LOE02 DemandAggregationAcquireOnce", LifecycleOrchestrationEngineTests.Test_LOE02_AggregatesDemandAndAcquiresOnce, ref total, ref passed, ref failed);
            RunTest("LOE03 HysteresisSchedulesRelease", LifecycleOrchestrationEngineTests.Test_LOE03_LastExitSchedulesHysteresisRelease, ref total, ref passed, ref failed);
            RunTest("LOE04 ReentryCancelsRelease", LifecycleOrchestrationEngineTests.Test_LOE04_ReentryInsideHysteresisCancelsRelease, ref total, ref passed, ref failed);
            RunTest("LOE05 RegionGenerationIdentity", LifecycleOrchestrationEngineTests.Test_LOE05_RegionGenerationIdentityGatesRelease, ref total, ref passed, ref failed);
            RunTest("LOE06 SessionEpochIdentity", LifecycleOrchestrationEngineTests.Test_LOE06_SessionEpochGatesPendingReleases, ref total, ref passed, ref failed);
            RunTest("LOE07 RetryFollowsDomainPolicy", LifecycleOrchestrationEngineTests.Test_LOE07_RetryFollowsDomainPolicyAndClearsOnSuccess, ref total, ref passed, ref failed);
            RunTest("LOE08 SingleRegionIsolation", LifecycleOrchestrationEngineTests.Test_LOE08_SingleRegionFailureIsIsolated, ref total, ref passed, ref failed);
            RunTest("LOE09 CrossDomainIsolation", LifecycleOrchestrationEngineTests.Test_LOE09_DomainFailureDoesNotCrossDomains, ref total, ref passed, ref failed);
            RunTest("LOE10 TransactionRollback", LifecycleOrchestrationEngineTests.Test_LOE10_TransactionRollbackRestoresStateAndProjection, ref total, ref passed, ref failed);
            RunTest("LOE11 ReleaseIdempotence", LifecycleOrchestrationEngineTests.Test_LOE11_CommitReleaseIsIdempotent, ref total, ref passed, ref failed);
            RunTest("LOE12 DiagnosticsCorrelation", LifecycleOrchestrationEngineTests.Test_LOE12_DiagnosticsCarryCorrelationFields, ref total, ref passed, ref failed);
            RunTest("LOE13 RegistrationClosure", LifecycleOrchestrationEngineTests.Test_LOE13_RegistrationClosureFreezesDomainSet, ref total, ref passed, ref failed);
            RunTest("LOE14 AcquireObservable", LifecycleOrchestrationEngineTests.Test_LOE14_SuccessfulAcquireIsObservable, ref total, ref passed, ref failed);
            RunTest("LOE15 FaultedDomainIsolation", LifecycleOrchestrationEngineTests.Test_LOE15_FaultedDomainDoesNotStallOthers, ref total, ref passed, ref failed);
            // 票 04：共享控制面正式切换准入不变量（暂缓不冻结/不释放、身份有界恢复或熔断、
            // retry 事务粒度、故障隔离单元与有界心跳）。
            RunTest("ARI01 DeferredSampleDoesNotFreeze", LifecycleOrchestrationEngineTests.Test_ARI01_UnusableSampleDefersOnlyThatObserver, ref total, ref passed, ref failed);
            RunTest("ARI02 DeferredNeverReleases", LifecycleOrchestrationEngineTests.Test_ARI02_DeferredDemandNeverReleases, ref total, ref passed, ref failed);
            RunTest("ARI03 DeferredHeartbeatBounded", LifecycleOrchestrationEngineTests.Test_ARI03_DeferredHeartbeatIsBoundedAndCloses, ref total, ref passed, ref failed);
            RunTest("ARI04 DeferredStillCountsDemand", LifecycleOrchestrationEngineTests.Test_ARI04_DeferredContributionStillCountsAsDemand, ref total, ref passed, ref failed);
            RunTest("ARI05 RetryTransactionScope", LifecycleOrchestrationEngineTests.Test_ARI05_AcquireRetryIsTransactionScoped, ref total, ref passed, ref failed);
            RunTest("ARI06 StaleRetryNoEligibility", LifecycleOrchestrationEngineTests.Test_ARI06_StaleConnectionRetryHasNoWriteEligibility, ref total, ref passed, ref failed);
            RunTest("ARI07 IdentityGatesWrites", LifecycleOrchestrationEngineTests.Test_ARI07_IdentityUncertaintyGatesWritesAndKeepsLeases, ref total, ref passed, ref failed);
            RunTest("ARI08 IdentityCircuitBreak", LifecycleOrchestrationEngineTests.Test_ARI08_IdentityCircuitBreakRequiresNewSession, ref total, ref passed, ref failed);
            RunTest("ARI09 FaultUnitDomainRegionTransition", LifecycleOrchestrationEngineTests.Test_ARI09_FaultUnitIsDomainRegionTransition, ref total, ref passed, ref failed);
            RunTest("ARI10 FaultHeartbeatBounded", LifecycleOrchestrationEngineTests.Test_ARI10_FaultHeartbeatIsBoundedAndClosesOnRecovery, ref total, ref passed, ref failed);
            RunTest("SAM01 PerRecordAdmission", ObserverSampleAdmissionTests.Test_SAM01_SingleUnusableRecordOnlyDefersItsObserver, ref total, ref passed, ref failed);
            RunTest("SAM02 BatchRejection", ObserverSampleAdmissionTests.Test_SAM02_BatchFailureDefersEveryone, ref total, ref passed, ref failed);
            RunTest("SAM03 UnknownIdentityAndDuplicates", ObserverSampleAdmissionTests.Test_SAM03_UnknownIdentityAndDuplicatesKeepOtherRecords, ref total, ref passed, ref failed);
            RunTest("SIG01 IdentityRecoversInWindow", SessionIdentityGateTests.Test_SIG01_RecoversInsideWindow, ref total, ref passed, ref failed);
            RunTest("SIG02 IdentityCircuitBreak", SessionIdentityGateTests.Test_SIG02_CircuitBreaksAfterWindowWithBoundedHeartbeat, ref total, ref passed, ref failed);
            RunTest("SIG03 CircuitRecoveryResync", SessionIdentityGateTests.Test_SIG03_CircuitBrokenRecoveryRequiresResynchronization, ref total, ref passed, ref failed);
            #endregion

            #region 3. Adapters: Item Domain Tests (21 Tests)
            Console.WriteLine("\n--- [PureMemory / Domain 3/8: Adapters / Item Domain (M1 + M2)] ---");
            RunTest("G1 FirstCommit", AuthorityGenerationGateTests.Test_G1_FirstCommitBlocksSecondProducer, ref total, ref passed, ref failed);
            RunTest("G2 Abort", AuthorityGenerationGateTests.Test_G2_AbortAllowsRetry, ref total, ref passed, ref failed);
            RunTest("G3 Reset", AuthorityGenerationGateTests.Test_G3_ResetInvalidatesOldEpoch, ref total, ref passed, ref failed);
            RunTest("G4 Preparing", AuthorityGenerationGateTests.Test_G4_PreparingRejectsReentry, ref total, ref passed, ref failed);
            RunTest("M1I01 LocalVanilla", ItemGenerationAuthorityAdapterTests.Test_M1I01_LocalObserverPreservesVanillaWhenAdapterNotReady, ref total, ref passed, ref failed);
            RunTest("M1I02 DedicatedAuthority", ItemGenerationAuthorityAdapterTests.Test_M1I02_DedicatedRemotePreservesFullMapAuthority, ref total, ref passed, ref failed);
            RunTest("M1I03 RegistrationGate", ItemGenerationAuthorityAdapterTests.Test_M1I03_ListenRemoteRequiresRegistration, ref total, ref passed, ref failed);
            RunTest("M1I04 RemoteEligibility", ItemGenerationAuthorityAdapterTests.Test_M1I04_UnrelatedRemoteCannotGenerate, ref total, ref passed, ref failed);
            RunTest("M1I05 PendingObserver", ItemGenerationAuthorityAdapterTests.Test_M1I05_AuthorizationIsNotPartOfWorldPresenceDecision, ref total, ref passed, ref failed);
            RunTest("M2I01 ReliableCommit", ItemObserverReplicationAdapterTests.Test_M2I01_ReliableReturnCommitsExactlyOnce, ref total, ref passed, ref failed);
            RunTest("M2I02 AbortRetry", ItemObserverReplicationAdapterTests.Test_M2I02_ExceptionAbortAllowsRetry, ref total, ref passed, ref failed);
            RunTest("M2I03 RelevanceReentry", ItemObserverReplicationAdapterTests.Test_M2I03_RelevanceExitReentryResendsSameGeneration, ref total, ref passed, ref failed);
            RunTest("M2I04 Reconnect", ItemObserverReplicationAdapterTests.Test_M2I04_ReconnectInvalidatesOldBaselineAndToken, ref total, ref passed, ref failed);
            RunTest("M2I05 OverlappingObservers", ItemObserverReplicationAdapterTests.Test_M2I05_OverlappingObserversHaveIndependentBaselines, ref total, ref passed, ref failed);
            RunTest("M2I06 DifferentRegions", ItemObserverReplicationAdapterTests.Test_M2I06_DifferentRegionsDoNotCrossCommit, ref total, ref passed, ref failed);
            RunTest("M2I07 NewWorldGeneration", ItemObserverReplicationAdapterTests.Test_M2I07_NewWorldGenerationResendsWithoutRelevanceExit, ref total, ref passed, ref failed);
            RunTest("M2I08 ExplicitCapability", ItemObserverReplicationAdapterTests.Test_M2I08_CapabilityIsExplicitReliableEnqueue, ref total, ref passed, ref failed);
            RunTest("M2I10 LoadedRollback", ItemObserverReplicationAdapterTests.Test_M2I10_LoadedProjectionRollbackRequiresCurrentConnection, ref total, ref passed, ref failed);
            RunTest("M2I11 ExactDisconnect", ItemObserverReplicationAdapterTests.Test_M2I11_ExactDisconnectInvalidatesEvenReusedConnectionToken, ref total, ref passed, ref failed);
            #endregion

            #region 4. Adapters: Zombie Domain Tests (22 Tests)
            Console.WriteLine("\n--- [PureMemory / Domain 4/8: Adapters / Zombie Domain (M3 + M4)] ---");
            RunTest("M3Z01 FirstAcquire", ZombieRegionLifecycleAdapterTests.Test_M3Z01_FirstAcquireCreatesOneGeneration, ref total, ref passed, ref failed);
            RunTest("M3Z02 GenerationCommit", ZombieRegionLifecycleAdapterTests.Test_M3Z02_RepeatedAcquireAdvancesOnlyOnRealCommit, ref total, ref passed, ref failed);
            RunTest("M3Z03 ReleaseHysteresis", ZombieRegionLifecycleAdapterTests.Test_M3Z03_ReleaseUsesHysteresis, ref total, ref passed, ref failed);
            RunTest("M3Z04 DemandCancelsRelease", ZombieRegionLifecycleAdapterTests.Test_M3Z04_DemandCancelsRelease, ref total, ref passed, ref failed);
            RunTest("M3Z05 StaleSession", ZombieRegionLifecycleAdapterTests.Test_M3Z05_StaleSessionCannotRelease, ref total, ref passed, ref failed);
            RunTest("M3Z06 StaleGeneration", ZombieRegionLifecycleAdapterTests.Test_M3Z06_StaleRegionGenerationCannotRelease, ref total, ref passed, ref failed);
            RunTest("M3Z07 DemandMismatch", ZombieRegionLifecycleAdapterTests.Test_M3Z07_NativeDemandIsComparedWithoutRewrite, ref total, ref passed, ref failed);
            RunTest("M3Z08 QuarantineRecovery", ZombieRegionLifecycleAdapterTests.Test_M3Z08_MismatchRecoveryClearsQuarantine, ref total, ref passed, ref failed);
            RunTest("M3Z09 BoundIsolation", ZombieRegionLifecycleAdapterTests.Test_M3Z09_BoundsAreIndependent, ref total, ref passed, ref failed);
            RunTest("M3Z10 ReleaseIdempotence", ZombieRegionLifecycleAdapterTests.Test_M3Z10_CommitReleaseIsIdempotent, ref total, ref passed, ref failed);
            RunTest("M3Z11 NativeDemandIsolation", ZombieRegionLifecycleAdapterTests.Test_M3Z11_NativeDemandMismatchDoesNotChangeLedgerGeneration, ref total, ref passed, ref failed);
            RunTest("M3Z12 QuarantineReleaseGate", ZombieRegionLifecycleAdapterTests.Test_M3Z12_QuarantineBlocksReleaseUntilDemandRecovers, ref total, ref passed, ref failed);
            RunTest("M4Z01 InitialSnapshot", ZombieSnapshotAdapterTests.Test_M4Z01_InitialSnapshotEnqueued, ref total, ref passed, ref failed);
            RunTest("M4Z02 StaleGenerationResync", ZombieSnapshotAdapterTests.Test_M4Z02_StaleGenerationForcesResync, ref total, ref passed, ref failed);
            RunTest("M4Z03 AbortRetry", ZombieSnapshotAdapterTests.Test_M4Z03_ExceptionAbortAllowsRetry, ref total, ref passed, ref failed);
            RunTest("M4Z04 RelevanceReentry", ZombieSnapshotAdapterTests.Test_M4Z04_RelevanceExitAndReentry, ref total, ref passed, ref failed);
            RunTest("M4Z05 ReconnectInvalidation", ZombieSnapshotAdapterTests.Test_M4Z05_ReconnectInvalidatesOldSnapshotToken, ref total, ref passed, ref failed);
            RunTest("M4Z06 OverlappingSnapshots", ZombieSnapshotAdapterTests.Test_M4Z06_OverlappingObserversHaveIndependentSnapshots, ref total, ref passed, ref failed);
            RunTest("M4Z07 BoundsDoNotCrossCommit", ZombieSnapshotAdapterTests.Test_M4Z07_DifferentBoundsDoNotCrossCommit, ref total, ref passed, ref failed);
            RunTest("M4Z08 ExplicitCapability", ZombieSnapshotAdapterTests.Test_M4Z08_ExplicitCapabilityIsReliableEnqueue, ref total, ref passed, ref failed);
            RunTest("M4Z09 DeltaSequenceMonotonic", ZombieSnapshotAdapterTests.Test_M4Z09_DeltaSequenceMonotonic, ref total, ref passed, ref failed);
            RunTest("M4Z10 ExactDisconnect", ZombieSnapshotAdapterTests.Test_M4Z10_ExactDisconnectCleansObserver, ref total, ref passed, ref failed);

            RunTest("DG1 EligibilityTruthTable", ZombieRespawnDedicatedGateEligibilityTests.Test_ZG_P1_EligibilityTruthTable, ref total, ref passed, ref failed);
            RunTest("DG2 ListenHostPeiNotBlocked", ZombieRespawnDedicatedGateEligibilityTests.Test_ZG_P2_ListenHostBeaconFreeNormalPeiNotBlocked, ref total, ref passed, ref failed);
            RunTest("DG3 ListenHostPartStatesFailClosed", ZombieRespawnDedicatedGateEligibilityTests.Test_ZG_P3_ListenHostPartStatesFailClosed, ref total, ref passed, ref failed);
            RunTest("DG4 GuardPassObservation", ZombieRespawnDedicatedGateEligibilityTests.Test_ZG_P4_GuardPassObservationFromIndexRotation, ref total, ref passed, ref failed);
            RunTest("IGP3 ItemEligibilityListenHostVsSingleplayer", ItemUpdateDedicatedGateEligibilityTests.Test_IGP3_EligibilityListenHostVsSingleplayer, ref total, ref passed, ref failed);
            RunTest("IGP1 ItemDespawnObservation", ItemUpdateDedicatedGateEligibilityTests.Test_IGP1_DespawnObservationFromCountDelta, ref total, ref passed, ref failed);
            RunTest("IGP2 ItemRespawnObservation", ItemUpdateDedicatedGateEligibilityTests.Test_IGP2_RespawnObservationFromCountDeltaAndWindow, ref total, ref passed, ref failed);
            #endregion

            #region 5. Adapters: Animal Domain Tests (17 Tests)
            Console.WriteLine("\n--- [PureMemory / Domain 5/8: Adapters / Animal Domain (M5)] ---");
            RunTest("M5A01 FirstAcquire", AnimalRegionLifecycleAdapterTests.Test_M5A01_FirstAcquireCreatesOneGeneration, ref total, ref passed, ref failed);
            RunTest("M5A02 GenerationCommit", AnimalRegionLifecycleAdapterTests.Test_M5A02_GenerationCommitAdvancesMonotonically, ref total, ref passed, ref failed);
            RunTest("M5A03 ReleaseHysteresis", AnimalRegionLifecycleAdapterTests.Test_M5A03_ReleaseUsesHysteresisDeadline, ref total, ref passed, ref failed);
            RunTest("M5A04 DemandCancelsRelease", AnimalRegionLifecycleAdapterTests.Test_M5A04_DemandCancelsRelease, ref total, ref passed, ref failed);
            RunTest("M5A05 StaleSession", AnimalRegionLifecycleAdapterTests.Test_M5A05_StaleSessionCannotRelease, ref total, ref passed, ref failed);
            RunTest("M5A06 StaleGeneration", AnimalRegionLifecycleAdapterTests.Test_M5A06_StaleGenerationCannotRelease, ref total, ref passed, ref failed);
            RunTest("M5A07 BoundIsolation", AnimalRegionLifecycleAdapterTests.Test_M5A07_BoundsAreIndependent, ref total, ref passed, ref failed);
            RunTest("M5A08 ReleaseIdempotence", AnimalRegionLifecycleAdapterTests.Test_M5A08_CommitReleaseIsIdempotentAndAdvancesGeneration, ref total, ref passed, ref failed);
            RunTest("M5A09 DisconnectCleansLease", AnimalRegionLifecycleAdapterTests.Test_M5A09_DisconnectCleansObserverState, ref total, ref passed, ref failed);
            RunTest("M5A10 DemandMismatchQuarantine", AnimalRegionLifecycleAdapterTests.Test_M5A10_DemandMismatchTriggersQuarantine, ref total, ref passed, ref failed);
            RunTest("M5S01 InitialSnapshot", AnimalSnapshotAdapterTests.Test_M5S01_InitialSnapshotEnqueued, ref total, ref passed, ref failed);
            RunTest("M5S02 DuplicateIgnored", AnimalSnapshotAdapterTests.Test_M5S02_DuplicateSnapshotIgnored, ref total, ref passed, ref failed);
            RunTest("M5S03 StaleGenerationResync", AnimalSnapshotAdapterTests.Test_M5S03_StaleGenerationForcesResync, ref total, ref passed, ref failed);
            RunTest("M5S04 ReconnectInvalidation", AnimalSnapshotAdapterTests.Test_M5S04_ReconnectInvalidatesOldSnapshotToken, ref total, ref passed, ref failed);
            RunTest("M5S05 OverlappingSnapshots", AnimalSnapshotAdapterTests.Test_M5S05_OverlappingObserversHaveIndependentSnapshots, ref total, ref passed, ref failed);
            RunTest("M5S06 BoundsDoNotCrossCommit", AnimalSnapshotAdapterTests.Test_M5S06_BoundsDoNotCrossCommit, ref total, ref passed, ref failed);
            RunTest("M5S07 DisconnectCleansObserver", AnimalSnapshotAdapterTests.Test_M5S07_DisconnectCleansObserver, ref total, ref passed, ref failed);
            #endregion

            #region 6. Adapters: Collision & Resource Domain Tests (22 Tests)
            Console.WriteLine("\n--- [PureMemory / Domain 6/8: Adapters / Collision & Resource Domains (M6)] ---");
            RunTest("M6C01 FirstAcquire", LevelObjectCollisionAdapterTests.Test_M6C01_FirstAcquireActivatesRegion, ref total, ref passed, ref failed);
            RunTest("M6C02 ReleaseHysteresis", LevelObjectCollisionAdapterTests.Test_M6C02_ReleaseUsesHysteresisDeadline, ref total, ref passed, ref failed);
            RunTest("M6C03 DemandCancelsRelease", LevelObjectCollisionAdapterTests.Test_M6C03_DemandCancelsRelease, ref total, ref passed, ref failed);
            RunTest("M6C04 CommitRelease", LevelObjectCollisionAdapterTests.Test_M6C04_CommitReleaseAdvancesGenerationAndDeactivates, ref total, ref passed, ref failed);
            RunTest("M6C05 StaleSession", LevelObjectCollisionAdapterTests.Test_M6C05_StaleSessionCannotCommitRelease, ref total, ref passed, ref failed);
            RunTest("M6C06 StaleGeneration", LevelObjectCollisionAdapterTests.Test_M6C06_StaleGenerationCannotCommitRelease, ref total, ref passed, ref failed);
            RunTest("M6C07 MultipleRegions", LevelObjectCollisionAdapterTests.Test_M6C07_MultipleRegionsAreIsolated, ref total, ref passed, ref failed);
            RunTest("M6C08 DisconnectCleanup", LevelObjectCollisionAdapterTests.Test_M6C08_DisconnectCleansObserverState, ref total, ref passed, ref failed);

            RunTest("M6R01 TreeOreAcquire", ResourceRegionLifecycleAdapterTests.Test_M6R01_FirstAcquireActivatesRegion, ref total, ref passed, ref failed);
            RunTest("M6R02 ResourceHysteresis", ResourceRegionLifecycleAdapterTests.Test_M6R02_ReleaseUsesHysteresisDeadline, ref total, ref passed, ref failed);
            RunTest("M6R03 ResourceDemandCancel", ResourceRegionLifecycleAdapterTests.Test_M6R03_DemandCancelsRelease, ref total, ref passed, ref failed);
            RunTest("M6R04 ResourceCommitRelease", ResourceRegionLifecycleAdapterTests.Test_M6R04_CommitReleaseAdvancesGenerationAndDeactivates, ref total, ref passed, ref failed);
            RunTest("M6R05 ResourceStaleSession", ResourceRegionLifecycleAdapterTests.Test_M6R05_StaleSessionCannotCommitRelease, ref total, ref passed, ref failed);
            RunTest("M6R06 ResourceStaleGeneration", ResourceRegionLifecycleAdapterTests.Test_M6R06_StaleGenerationCannotCommitRelease, ref total, ref passed, ref failed);
            RunTest("M6R07 ResourceIsolation", ResourceRegionLifecycleAdapterTests.Test_M6R07_MultipleRegionsAreIsolated, ref total, ref passed, ref failed);
            RunTest("M6R08 ResourceDisconnect", ResourceRegionLifecycleAdapterTests.Test_M6R08_DisconnectCleansObserverState, ref total, ref passed, ref failed);

            RunTest("M6S01 ResourceInitialSnapshot", ResourceSnapshotAdapterTests.Test_M6S01_InitialSnapshotEnqueued, ref total, ref passed, ref failed);
            RunTest("M6S02 ResourceDuplicateIgnored", ResourceSnapshotAdapterTests.Test_M6S02_DuplicateSnapshotIgnored, ref total, ref passed, ref failed);
            RunTest("M6S03 ResourceStaleGenResync", ResourceSnapshotAdapterTests.Test_M6S03_StaleGenerationForcesResync, ref total, ref passed, ref failed);
            RunTest("M6S04 ResourceReconnectToken", ResourceSnapshotAdapterTests.Test_M6S04_ReconnectInvalidatesOldSnapshotToken, ref total, ref passed, ref failed);
            RunTest("M6S05 ResourceOverlapping", ResourceSnapshotAdapterTests.Test_M6S05_OverlappingObserversHaveIndependentSnapshots, ref total, ref passed, ref failed);
            RunTest("M6S06 ResourceDisconnectClean", ResourceSnapshotAdapterTests.Test_M6S06_DisconnectCleansObserver, ref total, ref passed, ref failed);
            RunTest("M6S07 ResourceStaleDisconnect", ResourceSnapshotAdapterTests.Test_M6S07_StaleDisconnectDoesNotClearReconnectedObserver, ref total, ref passed, ref failed);
            RunTest("M6S08 ResourceNativeDataPlane", ResourceSnapshotAdapterTests.Test_M6S08_NativeDataPlaneEventsAreRecordedThroughResourceLedger, ref total, ref passed, ref failed);
            RunTest("M6S09 ResourceOldBaseline", ResourceSnapshotAdapterTests.Test_M6S09_OldBaselineCannotOverwriteNewerBaseline, ref total, ref passed, ref failed);
            RunTest("M6S10 ResourceGenerationOverflow", ResourceSnapshotAdapterTests.Test_M6S10_RegionGenerationOverflowFailsClosed, ref total, ref passed, ref failed);
            RunTest("M6S11 ResourceStaleRemoveTolerated", ResourceSnapshotAdapterTests.Test_M6S11_StaleSnapshotRemovalToleratedWithoutThrow, ref total, ref passed, ref failed);

            RunTest("M6H01 HarvestDeadGen", ResourceHarvestReplicationTests.Test_M6H01_ResourceDeadAdvancesGeneration, ref total, ref passed, ref failed);
            RunTest("M6H02 HarvestMultiDead", ResourceHarvestReplicationTests.Test_M6H02_MultipleResourcesDeadTrackedIndependently, ref total, ref passed, ref failed);
            RunTest("M6H03 HarvestAliveRevive", ResourceHarvestReplicationTests.Test_M6H03_ResourceAliveRevivesAndAdvancesGeneration, ref total, ref passed, ref failed);
            RunTest("M6H04 HarvestSnapshotDead", ResourceHarvestReplicationTests.Test_M6H04_SnapshotIncludesDeadList, ref total, ref passed, ref failed);
            RunTest("M6H05 HarvestDeltaSeq", ResourceHarvestReplicationTests.Test_M6H05_DeltaSequenceAdvancesOnHarvest, ref total, ref passed, ref failed);
            RunTest("M6H06 HarvestReconnect", ResourceHarvestReplicationTests.Test_M6H06_ReconnectGetsUpdatedGeneration, ref total, ref passed, ref failed);
            RunTest("M6H07 HarvestIsolation", ResourceHarvestReplicationTests.Test_M6H07_DifferentRegionsHarvestIsolated, ref total, ref passed, ref failed);
            RunTest("M6H08 HarvestSessionReset", ResourceHarvestReplicationTests.Test_M6H08_SessionResetClearsDeadResources, ref total, ref passed, ref failed);

            RunTest("M6P01 ResourceUnionAcquire", ResourceProductionControlSeamTests.Test_M6P01_ObserverUnionAcquiresOnce, ref total, ref passed, ref failed);
            RunTest("M6P02 ResourceReleaseHysteresis", ResourceProductionControlSeamTests.Test_M6P02_LastObserverSchedulesTwoSecondRelease, ref total, ref passed, ref failed);
            RunTest("M6P03 ResourceReentry", ResourceProductionControlSeamTests.Test_M6P03_ReentryCancelsReleaseWithoutSecondAcquire, ref total, ref passed, ref failed);
            RunTest("M6P04 ResourceReconnectReset", ResourceProductionControlSeamTests.Test_M6P04_ReconnectAndSessionResetInvalidateState, ref total, ref passed, ref failed);
            RunTest("M6P05 ResourceGenerationFlow", ResourceProductionControlSeamTests.Test_M6P05_RegionGenerationFlowsIntoSnapshotAndRelease, ref total, ref passed, ref failed);
            RunTest("M6P06 ResourceSameFrameReentry", ResourceProductionControlSeamTests.Test_M6P06_AdvanceBeforeFlushAllowsSameFrameReentry, ref total, ref passed, ref failed);
            RunTest("M6P07 ResourceRealAdapterSeam", ResourceProductionControlSeamTests.Test_M6P07_RealResourceAdapterHighLevelSeam, ref total, ref passed, ref failed);
            RunTest("M6P08 ResourceStaleGeneration", ResourceProductionControlSeamTests.Test_M6P08_StaleRegionGenerationDelaysRelease, ref total, ref passed, ref failed);
            RunTest("M6P09 ResourceReleaseRetry", ResourceProductionControlSeamTests.Test_M6P09_ReleaseFailureRetainsRetryableState, ref total, ref passed, ref failed);
            RunTest("M6P11 ResourceReplicationEnterRollback", ResourceProductionControlSeamTests.Test_M6P11_ReplicationEnterFailureDoesNotCommitDemand, ref total, ref passed, ref failed);
            RunTest("M6P12 ResourceReplicationExitRollback", ResourceProductionControlSeamTests.Test_M6P12_ReplicationExitFailureRetainsDemand, ref total, ref passed, ref failed);
            RunTest("M6P13 ResourceMultiRegionEnterCompensation", ResourceProductionControlSeamTests.Test_M6P13_MultiRegionEnterFailureCompensates, ref total, ref passed, ref failed);
            RunTest("M6P14 ResourceMultiRegionExitCompensation", ResourceProductionControlSeamTests.Test_M6P14_MultiRegionExitFailureCompensates, ref total, ref passed, ref failed);
            RunTest("M6P15 ResourceAcquireFailureIsolated", ResourceProductionControlSeamTests.Test_M6P15_AcquireFailureAfterSideEffectIsIsolated, ref total, ref passed, ref failed);
            RunTest("M6P16 ResourceGenerationReaderFailure", ResourceProductionControlSeamTests.Test_M6P16_GenerationReaderFailureRetainsReleaseState, ref total, ref passed, ref failed);
            RunTest("M6P17 ResourceDisconnectCompensation", ResourceProductionControlSeamTests.Test_M6P17_DisconnectFailureRestoresObserverState, ref total, ref passed, ref failed);
            RunTest("M6P18 ResourceReleaseCompensation", ResourceProductionControlSeamTests.Test_M6P18_ReleaseFailureAfterSideEffectIsCompensated, ref total, ref passed, ref failed);
            RunTest("M6P19 ResourceReleaseRejection", ResourceProductionControlSeamTests.Test_M6P19_ReleaseRejectionDoesNotRunCompensation, ref total, ref passed, ref failed);
            RunTest("M6P20 ResourceSnapshotRestore", ResourceProductionControlSeamTests.Test_M6P20_DisconnectFailureRestoresSnapshotContent, ref total, ref passed, ref failed);
            RunTest("M6P21 ResourceAcquireSideEffectIsolated", ResourceProductionControlSeamTests.Test_M6P21_AcquireFailureSideEffectIsolated, ref total, ref passed, ref failed);
            RunTest("M6P22 ResourceSessionInitFailClosed", ResourceProductionControlSeamTests.Test_M6P22_SessionInitializationFailureStaysInactive, ref total, ref passed, ref failed);
            RunTest("M6P23 ResourceReplicationRestore", ResourceProductionControlSeamTests.Test_M6P23_ReplicationFailureRestoresExactSnapshot, ref total, ref passed, ref failed);
            RunTest("M6P24 ResourceGenerationRegressionRetained", ResourceProductionControlSeamTests.Test_M6P24_AdvanceTimeRetainsLeaseOnGenerationRegression, ref total, ref passed, ref failed);
            RunTest("M6P25 ResourceReleaseGenerationRegressionRetained", ResourceProductionControlSeamTests.Test_M6P25_FlushRetainsPendingReleaseOnGenerationRegression, ref total, ref passed, ref failed);
            RunTest("M6P26 ResourceSessionEndLifecycleFailure", ResourceProductionControlSeamTests.Test_M6P26_EndSessionClearsStateWhenLifecycleCleanupThrows, ref total, ref passed, ref failed);
            RunTest("M6P27 ResourceSessionEndReplicationFailure", ResourceProductionControlSeamTests.Test_M6P27_EndSessionClearsStateWhenReplicationCleanupThrows, ref total, ref passed, ref failed);
            RunTest("M6P28 ResourceAcquireGenerationRegression", ResourceProductionControlSeamTests.Test_M6P28_AcquireGenerationRegressionFailsClosed, ref total, ref passed, ref failed);
            RunTest("M6P29 ResourceExitGenerationRegression", ResourceProductionControlSeamTests.Test_M6P29_ExitGenerationRegressionRetainsStoredLease, ref total, ref passed, ref failed);
            RunTest("M6P30 ResourceRepairRequiredLock", ResourceProductionControlSeamTests.Test_M6P30_RepairRequiredRejectsNewSession, ref total, ref passed, ref failed);
            RunTest("M6P31 ResourceCaptureFailureIsolation", ResourceProductionControlSeamTests.Test_M6P31_CaptureRegionFailureIsolationKeepsOtherRegions, ref total, ref passed, ref failed);
            RunTest("M6P32 ResourceSnapshotUnavailableDeferredRetry", ResourceProductionControlSeamTests.Test_M6P32_SnapshotUnavailableDefersRetryThenAcquires, ref total, ref passed, ref failed);
            RunTest("M6P33 ResourceRollbackRestoresRetryRegistration", ResourceProductionControlSeamTests.Test_M6P33_RollbackRestoresAcquireRetryRegistration, ref total, ref passed, ref failed);
            RunTest("M6P34 ResourceDeferredOnlyRegionExit", ResourceProductionControlSeamTests.Test_M6P34_DeferredOnlyRegionExitSkipsDemand, ref total, ref passed, ref failed);
            RunTest("M6P35 ResourceStaleExitTolerated", ResourceProductionControlSeamTests.Test_M6P35_StaleExitWithoutDemandIsTolerated, ref total, ref passed, ref failed);
            // 票 01 表征门(Collision Migration Slice 迁入前行为等价基线,SPF-0.2.4.9)
            RunTest("M6P36 ResourceProductionRadiusShape", ResourceProductionControlSeamTests.Test_M6P36_ProductionRadiusProjectsChebyshevSquare, ref total, ref passed, ref failed);
            RunTest("M6P37 ResourceHysteresisConstant", ResourceProductionControlSeamTests.Test_M6P37_HysteresisWindowComesFromResourceConstant, ref total, ref passed, ref failed);
            RunTest("M6P38 ResourceDeferredRetrySchedule", ResourceProductionControlSeamTests.Test_M6P38_DeferredAcquireRetryScheduleDoublesThenCapsAt32, ref total, ref passed, ref failed);
            RunTest("M6P39 ResourceFailedRetrySchedule", ResourceProductionControlSeamTests.Test_M6P39_FailedAcquireRetryScheduleDoublesThenCapsAt60, ref total, ref passed, ref failed);
            RunTest("M6P40 ResourceRetryQualificationKept", ResourceProductionControlSeamTests.Test_M6P40_SuccessPathKeepsOtherObserverRetryQualification, ref total, ref passed, ref failed);
            RunTest("M6P41 ResourceOverlapRetainsLease", ResourceProductionControlSeamTests.Test_M6P41_OverlappingObserversRetainLeaseUntilLastExit, ref total, ref passed, ref failed);
            // 票 02：Resource 接缝经声明式 Demand Policy 消费共享投影(不再持有私有观察者索引)。
            RunTest("M6P42 ResourceSharedProjection", ResourceProductionControlSeamTests.Test_M6P42_FactsAndDemandLiveInControlPlane, ref total, ref passed, ref failed);
            RunTest("M6P43 ResourcePolicyIdentity", ResourceProductionControlSeamTests.Test_M6P43_SeamRejectsForeignDomainPolicy, ref total, ref passed, ref failed);
            RunTest("M6P44 ResourceEligibilityPolicy", ResourceProductionControlSeamTests.Test_M6P44_EligibilityComesFromResourcePolicy, ref total, ref passed, ref failed);
            RunTest("M6P45 ResourceRemovalClearsProjection", ResourceProductionControlSeamTests.Test_M6P45_RemovalClearsFactAndProjection, ref total, ref passed, ref failed);
            RunTest("M6P46 ResourceSessionBoundaryClearsFact", ResourceProductionControlSeamTests.Test_M6P46_SessionBoundaryClearsSharedFact, ref total, ref passed, ref failed);
            RunTest("M6O01 ResourceObservabilityFields", ResourceObservabilityTests.Test_M6O01_FormatsRequiredResourceFields, ref total, ref passed, ref failed);
            RunTest("M6O02 ResourceFallbackExplicit", ResourceObservabilityTests.Test_M6O02_FallbackAndSkippedAreExplicit, ref total, ref passed, ref failed);
            RunTest("M6O03 ResourceReceiveDecisionBoundary", ResourceObservabilityTests.Test_M6O03_NativeReceiveDoesNotInventAcceptance, ref total, ref passed, ref failed);
            RunTest("M6O04 ResourceControlledObservationValues", ResourceObservabilityTests.Test_M6O04_PathAndOutcomeRejectUnknownValues, ref total, ref passed, ref failed);
            RunTest("M6O05 WorldSyncReflectionReasons", ResourceObservabilityTests.Test_M6O05_WorldSyncReflectionFailuresHaveReasons, ref total, ref passed, ref failed);
            RunTest("M6O06 WorldSyncNullRegionFailClosed", ResourceObservabilityTests.Test_M6O06_WorldSyncNullRegionFailsClosed, ref total, ref passed, ref failed);
            RunTest("M6O07 ObservationBeforeQuota", ResourceObservabilityTests.Test_M6O07_IncompleteObservationPrecedesQuota, ref total, ref passed, ref failed);
            RunTest("M6O08 ReceiveBeforeQuota", ResourceObservabilityTests.Test_M6O08_ReceiveCompletenessPrecedesQuota, ref total, ref passed, ref failed);
            RunTest("M6O09 HarvestNativePostcondition", ResourceObservabilityTests.Test_M6O09_HarvestValidatesNativePostcondition, ref total, ref passed, ref failed);
            RunTest("M6O10 HarvestRegistrationIdempotent", ResourceHarvestRegistrationStaticILContractTests.Test_BlackBoxRegistrationIsIdempotentAndUnique, ref total, ref passed, ref failed);
            RunTest("M6O11 ConnectionOverflowTeardown", ResourceProductionControlStaticILContractTests.Test_ClientConnectionGenerationFailureRequestsTeardown, ref total, ref passed, ref failed);
            RunTest("M6O12 SessionEndCleanupFinally", ResourceProductionControlStaticILContractTests.Test_CoordinatorSessionEndClearsAfterResourceFailure, ref total, ref passed, ref failed);
            RunTest("M6O13 ResourceEndLogAfterCleanup", ResourceProductionControlStaticILContractTests.Test_ResourceDomainEndLogsSuccessAfterCleanup, ref total, ref passed, ref failed);
            RunTest("M6O14 DeltaRejectPerCall", ResourceProductionControlStaticILContractTests.Test_DeltaWriteUsesPerCallRejectDelta, ref total, ref passed, ref failed);

            RunTest("M6R09 ResourceSessionEnd", ResourceRegionLifecycleAdapterTests.Test_M6R09_EndSessionClearsResourceState, ref total, ref passed, ref failed);

            RunTest("M7B01 BarricadeAcquire", BarricadeRegionLifecycleAdapterTests.Test_M7B01_FirstAcquireCreatesGeneration, ref total, ref passed, ref failed);
            RunTest("M7B02 BarricadePlace", BarricadeRegionLifecycleAdapterTests.Test_M7B02_PlaceBarricadeAdvancesGeneration, ref total, ref passed, ref failed);
            RunTest("M7B03 BarricadeDamage", BarricadeRegionLifecycleAdapterTests.Test_M7B03_DamageBarricadeAdvancesGeneration, ref total, ref passed, ref failed);
            RunTest("M7B04 BarricadeDeltaSeq", BarricadeRegionLifecycleAdapterTests.Test_M7B04_UpdateStateAdvancesDeltaSequence, ref total, ref passed, ref failed);
            RunTest("M7B05 BarricadeHysteresis", BarricadeRegionLifecycleAdapterTests.Test_M7B05_ReleaseUsesHysteresisDeadline, ref total, ref passed, ref failed);
            RunTest("M7B06 BarricadeStaleSession", BarricadeRegionLifecycleAdapterTests.Test_M7B06_StaleSessionCannotRelease, ref total, ref passed, ref failed);
            RunTest("M7B07 BarricadeReconnect", BarricadeRegionLifecycleAdapterTests.Test_M7B07_ReconnectInvalidationResync, ref total, ref passed, ref failed);
            RunTest("M7B08 BarricadeDisconnect", BarricadeRegionLifecycleAdapterTests.Test_M7B08_DisconnectCleansObserverState, ref total, ref passed, ref failed);

            RunTest("M7S01 StructureAcquire", StructureRegionLifecycleAdapterTests.Test_M7S01_FirstAcquireCreatesGeneration, ref total, ref passed, ref failed);
            RunTest("M7S02 StructurePlace", StructureRegionLifecycleAdapterTests.Test_M7S02_PlaceStructureAdvancesGeneration, ref total, ref passed, ref failed);
            RunTest("M7S03 StructureDamage", StructureRegionLifecycleAdapterTests.Test_M7S03_DamageStructureAdvancesGeneration, ref total, ref passed, ref failed);
            RunTest("M7S04 StructureSalvage", StructureRegionLifecycleAdapterTests.Test_M7S04_SalvageStructureAdvancesGeneration, ref total, ref passed, ref failed);
            RunTest("M7S05 StructureHysteresis", StructureRegionLifecycleAdapterTests.Test_M7S05_ReleaseUsesHysteresisDeadline, ref total, ref passed, ref failed);
            RunTest("M7S06 StructureStaleSession", StructureRegionLifecycleAdapterTests.Test_M7S06_StaleSessionCannotRelease, ref total, ref passed, ref failed);
            RunTest("M7S07 StructureReconnect", StructureRegionLifecycleAdapterTests.Test_M7S07_ReconnectInvalidationResync, ref total, ref passed, ref failed);
            RunTest("M7S08 StructureDisconnect", StructureRegionLifecycleAdapterTests.Test_M7S08_DisconnectCleansObserverState, ref total, ref passed, ref failed);
            #endregion

            #region 7. Adapters: Security & Whitelist Tests (32 Tests)
            Console.WriteLine("\n--- [PureMemory / Domain 7/8: Adapters / Security Domain (Whitelist & Route B)] ---");
            RunTest("WL1 Bootstrap", WhitelistServiceTests.Test_Bootstrap_Success, ref total, ref passed, ref failed);
            RunTest("WL2 BootstrapSaveFailure", WhitelistServiceTests.Test_Bootstrap_SaveFailure_NoDisconnect, ref total, ref passed, ref failed);
            RunTest("WL3 BootstrapLoadFailure", WhitelistServiceTests.Test_Bootstrap_LoadFailure_NoDisconnect, ref total, ref passed, ref failed);
            RunTest("WL4 BootstrapContainsFailure", WhitelistServiceTests.Test_Bootstrap_ContainsFailure_NoDisconnect, ref total, ref passed, ref failed);
            RunTest("WL5 AddSaveFailure", WhitelistServiceTests.Test_Add_SaveFailure_GatewayOnce, ref total, ref passed, ref failed);
            RunTest("WL6 AddLoadFailure", WhitelistServiceTests.Test_Add_LoadFailure_GatewayOnce, ref total, ref passed, ref failed);
            RunTest("WL7 AddContainsFailure", WhitelistServiceTests.Test_Add_ContainsFailure_GatewayOnce, ref total, ref passed, ref failed);
            RunTest("WL8 AddSnapshotFailure", WhitelistServiceTests.Test_Add_SnapshotFailure_GatewayOnce, ref total, ref passed, ref failed);
            RunTest("WL9 RemoveSaveFailure", WhitelistServiceTests.Test_Remove_SaveFailure_GatewayOnce, ref total, ref passed, ref failed);
            RunTest("WL9a RevokeCommitBeforeKick", WhitelistServiceTests.Test_ApprovalRevoke_CommitBeforeTargetedKick, ref total, ref passed, ref failed);
            RunTest("WL9b RevokeFailureNoDisconnect", WhitelistServiceTests.Test_ApprovalRevoke_SaveFailureDoesNotDisconnect, ref total, ref passed, ref failed);
            RunTest("WL9c NativeContainsRaw", WhitelistServiceTests.Test_NativeContains_UsesPhysicalWhitelistMembership, ref total, ref passed, ref failed);
            RunTest("WL10 RemoveNoOp", WhitelistServiceTests.Test_Remove_NoOp_NoSave_NoDisconnect, ref total, ref passed, ref failed);
            RunTest("WL11 RemoveSnapshotFailure", WhitelistServiceTests.Test_Remove_SnapshotFailure_GatewayOnce, ref total, ref passed, ref failed);
            RunTest("WL12 AddSelf", WhitelistServiceTests.Test_Add_Self_Rejected, ref total, ref passed, ref failed);
            RunTest("WL13 RemoveSelf", WhitelistServiceTests.Test_Remove_Self_Rejected, ref total, ref passed, ref failed);
            RunTest("WL14 AddInvalidLocal", WhitelistServiceTests.Test_Add_InvalidLocalUser_Rejected, ref total, ref passed, ref failed);
            RunTest("WL15 RemoveInvalidLocal", WhitelistServiceTests.Test_Remove_InvalidLocalUser_Rejected, ref total, ref passed, ref failed);
            RunTest("WL16 JudgeEqualsLocal", WhitelistServiceTests.Test_Add_JudgeId_Equals_LocalUser, ref total, ref passed, ref failed);
            RunTest("WL17 PersistenceFault", WhitelistServiceTests.Test_PersistenceFault_Blocks_Second_Mutate_And_Reset_Restores, ref total, ref passed, ref failed);
            RunTest("B1 HandshakePermit", RouteBApprovalTests.Test_B1_HandshakePermitIsScopedAndRejectable, ref total, ref passed, ref failed);
            RunTest("B2 WorldEntry", RouteBApprovalTests.Test_B2_NewWorldEntryBecomesPendingQuarantine, ref total, ref passed, ref failed);
            RunTest("B3 TrustedVisitor", RouteBApprovalTests.Test_B3_TrustedVisitorSkipsQuarantine, ref total, ref passed, ref failed);
            RunTest("B4 ConcurrentGuests", RouteBApprovalTests.Test_B4_ConcurrentGuestsAreDeduplicatedAndBounded, ref total, ref passed, ref failed);
            RunTest("B5 Approve", RouteBApprovalTests.Test_B5_ApprovePersistsThenReleases, ref total, ref passed, ref failed);
            RunTest("B6 PersistFailure", RouteBApprovalTests.Test_B6_PersistenceFailureRetainsQuarantine, ref total, ref passed, ref failed);
            RunTest("B7 DisconnectReject", RouteBApprovalTests.Test_B7_DisconnectAndRejectCleanState, ref total, ref passed, ref failed);
            RunTest("B8 TimeoutLayout", RouteBApprovalTests.Test_B8_TimeoutKicksOnceAndPKeyLayoutIsStable, ref total, ref passed, ref failed);
            RunTest("B9 Revoke", RouteBApprovalTests.Test_B9_RevokeRemovesWhitelistAndKicks, ref total, ref passed, ref failed);
            RunTest("B10 RevokePersistFailure", RouteBApprovalTests.Test_B10_RevokePersistenceFailureDoesNotKick, ref total, ref passed, ref failed);
            RunTest("B12 InputSanitizer", RouteBApprovalTests.Test_B12_InputSanitizerPreservesNetworkProgress, ref total, ref passed, ref failed);
            #endregion

            #region 8. Platform: UI, Gate & Diagnostic Tests (27 Tests)
            Console.WriteLine("\n--- [PureMemory / Domain 8/8: Platform UI, Readiness & Compatibility] ---");
            RunTest("E1 EntryEarlyMenu", P2PEntryReadinessGateTests.Test_E1_EarlyMenuCannotExposeEntry, ref total, ref passed, ref failed);
            RunTest("E2 EntryLifecycleFailure", P2PEntryReadinessGateTests.Test_E2_FailedLifecycleCannotExposeEntry, ref total, ref passed, ref failed);
            RunTest("E3 EntryIdempotentReset", P2PEntryReadinessGateTests.Test_E3_SuccessIsIdempotentAndResetFailsClosed, ref total, ref passed, ref failed);
            RunTest("E4 HandshakeCompatibilityGate", P2PEntryReadinessGateTests.Test_E4_HandshakeCompatibilityFailureCannotExposeEntry, ref total, ref passed, ref failed);
            RunTest("P1 PersonaEmpty", SteamPersonaDisplayTests.Test_v4_P1_Normalize_Empty_Fallback, ref total, ref passed, ref failed);
            RunTest("P2 PersonaControls", SteamPersonaDisplayTests.Test_v4_P2_Normalize_ControlChars_Stripped, ref total, ref passed, ref failed);
            RunTest("P3 PersonaTruncate", SteamPersonaDisplayTests.Test_v4_P3_Normalize_Truncates_32, ref total, ref passed, ref failed);
            RunTest("P4 PersonaValid", SteamPersonaDisplayTests.Test_v4_P4_Normalize_Valid_Preserved, ref total, ref passed, ref failed);
            RunTest("P5 PersonaFormat", SteamPersonaDisplayTests.Test_v4_P5_FormatPlayer_KeepsSteamId_AndFallback, ref total, ref passed, ref failed);
            RunTest("P6 PersonaInvalid", SteamPersonaDisplayTests.Test_v4_P6_GetRemoteDisplayName_InvalidId_Fallback, ref total, ref passed, ref failed);
            RunTest("IUI1 Exact", InventoryUiProjectionTests.Test_IUI1_ExactProjectionNoRepair, ref total, ref passed, ref failed);
            RunTest("IUI2 StaleRendered", InventoryUiProjectionTests.Test_IUI2_StaleRenderedJarDetected, ref total, ref passed, ref failed);
            RunTest("IUI3 StalePending", InventoryUiProjectionTests.Test_IUI3_StalePendingJarDetected, ref total, ref passed, ref failed);
            RunTest("IUI4 Identity", InventoryUiProjectionTests.Test_IUI4_IdentityNotValueEquivalence, ref total, ref passed, ref failed);
            RunTest("JR1 SteamIdPortClassifiesAsSteamP2P", UnifiedJoinAddressClassifierTests.Test_JR1_SteamIdWithOptionalPortClassifiesAsSteamP2P, ref total, ref passed, ref failed);
            RunTest("JR2 ForbiddenSuffixesStayVanilla", UnifiedJoinAddressClassifierTests.Test_JR2_ForbiddenSuffixesStayVanilla, ref total, ref passed, ref failed);
            RunTest("JR3 DirectIpAndDnsKeepOwnPortStripping", UnifiedJoinAddressClassifierTests.Test_JR3_DirectIpAndDnsKeepOwnPortStripping, ref total, ref passed, ref failed);
            RunTest("JR4 SteamIdRouteCarriesDirectPagePassword", SessionPasswordTests.Test_JR4_SteamIdRouteCarriesDirectPagePassword, ref total, ref passed, ref failed);
            RunTest("JR5 HostMenuInputResolvesSessionPassword", SessionPasswordTests.Test_JR5_HostMenuInputResolvesSessionPassword, ref total, ref passed, ref failed);
            RunTest("JR6 RuntimePasswordClearedOnSessionEnd", SessionPasswordTests.Test_JR6_RuntimePasswordClearedOnSessionEnd, ref total, ref passed, ref failed);
            #endregion

            _currentEvidenceClass = EvidenceClass.StaticIL;
            Console.WriteLine("\n=== Evidence Class: StaticIL ===");
            RunTest("ID StaticIL", IdentityStaticILContractTests.Test_All, ref total, ref passed, ref failed);
            RunTest("Resource/Collision StaticIL", ResourceCollisionOwnershipStaticILContractTests.Test_All, ref total, ref passed, ref failed);
            RunTest("Resource Production Control StaticIL", ResourceProductionControlStaticILContractTests.Test_All,
                ref total, ref passed, ref failed);
            RunTest("Resource Authority Retirement StaticIL", ResourceAuthorityRetirementStaticILContractTests.Test_All,
                ref total, ref passed, ref failed);
            RunTest("Resource Harvest Registration StaticIL", ResourceHarvestRegistrationStaticILContractTests.Test_All,
                ref total, ref passed, ref failed);
            RunTest("Resource Harvest Four Hook State", ResourceHarvestRegistrationStaticILContractTests.Test_FourHookRegistrationStateIsExplicit,
                ref total, ref passed, ref failed);
            RunTest("Resource Harvest Identity State", ResourceHarvestRegistrationStaticILContractTests.Test_BlackBoxRegistrationStateContainsIdentity,
                ref total, ref passed, ref failed);
            RunTest("Resource Harvest Rollback State", ResourceHarvestRegistrationStaticILContractTests.Test_BlackBoxPartialFailureRollsBackAppliedAndFailedHook,
                ref total, ref passed, ref failed);
            RunTest("Resource Harvest Unpatch Failure State", ResourceHarvestRegistrationStaticILContractTests.Test_UnpatchFailureIsNotReportedAsVerifiedClean,
                ref total, ref passed, ref failed);
            RunTest("Resource Harvest Rollback Knowledge", ResourceHarvestRegistrationStaticILContractTests.Test_RollbackResidualKnowledgeIsExplicit,
                ref total, ref passed, ref failed);
            RunTest("Resource Generation Reader No Guess", ResourceProductionControlStaticILContractTests.Test_GenerationReadDoesNotUseFallbackGuess,
                ref total, ref passed, ref failed);
            RunTest("Resource Radius Source No Shared Default", ResourceProductionControlStaticILContractTests.Test_ProductionRadiusComesFromVanillaObjectRegionSource,
                ref total, ref passed, ref failed);
            RunTest("Resource Failure Classification", ResourceProductionControlStaticILContractTests.Test_FailureClassificationDoesNotParseExceptionText,
                ref total, ref passed, ref failed);
            RunTest("Resource Acquire Failure Helper Message", ResourceProductionControlStaticILContractTests.Test_AcquireFailureHelperEmbedsExceptionMessage,
                ref total, ref passed, ref failed);
            RunTest("Resource Single Region Entry Classification", ResourceProductionControlStaticILContractTests.Test_SingleRegionEntryDoesNotParseExceptionText,
                ref total, ref passed, ref failed);
            RunTest("Resource Native Snapshot Fail Closed", ResourceProductionControlStaticILContractTests.Test_NativeSnapshotNullResourceListFailsClosed,
                ref total, ref passed, ref failed);
            RunTest("Resource Native Restore Atomic", ResourceProductionControlStaticILContractTests.Test_NativeRestoreValidatesBeforeApplyAndCanRollback,
                ref total, ref passed, ref failed);
            RunTest("Item/Zombie StaticIL", ItemZombieOwnershipStaticILContractTests.Test_All, ref total, ref passed, ref failed);
            RunTest("T04 ModuleOwnershipStaticIL", ModuleOwnershipStaticILContractTests.Test_All,
                ref total, ref passed, ref failed);
            RunTest("T07 AnimalStructureOwnershipStaticIL", AnimalStructureOwnershipStaticILContractTests.Test_All,
                ref total, ref passed, ref failed);
            RunTest("M1I06 CurrentU3IL", ItemGenerationAuthorityStaticILTests.Test_M1I06_CurrentU3IlMatchesExactlyOneGenerationGate,
                ref total, ref passed, ref failed);
            RunTest("M2I09 CurrentU3ReplicationGate", ItemObserverReplicationStaticILTests.Test_M2I09_CurrentU3IlUsesM2ReplicationGate,
                ref total, ref passed, ref failed);
            RunTest("ZG1 RespawnDedicatedCallSiteUnique", ZombieRespawnDedicatedGateStaticILTests.Test_ZG1_CurrentU3IlHasExactlyOneDedicatedCallSite,
                ref total, ref passed, ref failed);
            RunTest("ZG2 RespawnGateTranspilerContract", ZombieRespawnDedicatedGateStaticILTests.Test_ZG2_TranspilerReplacesExactlyTheGuardCall,
                ref total, ref passed, ref failed);
            RunTest("IG1 ItemUpdateDedicatedCallSiteUnique", ItemUpdateDedicatedGateStaticILTests.Test_IG1_CurrentU3IlHasExactlyOneDedicatedCallSite,
                ref total, ref passed, ref failed);
            RunTest("IG2 ItemUpdateGateTranspilerContract", ItemUpdateDedicatedGateStaticILTests.Test_IG2_TranspilerReplacesExactlyTheGuardCall,
                ref total, ref passed, ref failed);
            RunTest("B11 AuthoritativeGates", RouteBApprovalStaticILTests.Test_B11_PendingActionAndCommandGatesAreAuthoritative,
                ref total, ref passed, ref failed);
            RunTest("Harmony HC1 Observer", HarmonyCompatibilityAuditTests.Test_ObserverPatch_IsRecordedWithoutBlocking,
                ref total, ref passed, ref failed);
            RunTest("Harmony HC2 ForeignTranspiler", HarmonyCompatibilityAuditTests.Test_ForeignTranspiler_OnOwnTranspiledTarget_Blocks,
                ref total, ref passed, ref failed);
            RunTest("Harmony HC3 TransportExclusive", HarmonyCompatibilityAuditTests.Test_P2PTransportTargets_RemainExclusive,
                ref total, ref passed, ref failed);
            RunTest("Inventory IUI5 Reflection", InventoryUiProjectionStaticILTests.Test_IUI5_ReflectionContractExact,
                ref total, ref passed, ref failed);
            RunTest("Inventory IUI6 Production", InventoryUiProjectionStaticILTests.Test_IUI6_ProductionPostfixesActivate,
                ref total, ref passed, ref failed);
            RunTest("Collision RC1 AnimationRestore", RemoteCollisionAnimationPolicyTests.Test_RC1_CullingPolicyIsSavedAndRestored,
                ref total, ref passed, ref failed);
            RunTest("Collision RC2 PolicyBeforeActivation", RemoteCollisionAnimationPolicyTests.Test_RC2_CullingPolicyPrecedesRootActivation,
                ref total, ref passed, ref failed);

            // 票 02：共享投影引擎与声明式 Demand Policy 的结构门禁。
            RunTest("Demand Projection StaticIL", DemandProjectionStaticILContractTests.Test_All,
                ref total, ref passed, ref failed);
            RunTest("Demand Single Spatial Authority", DemandProjectionStaticILContractTests.Test_ControlPlaneOwnsSingleSpatialAuthority,
                ref total, ref passed, ref failed);
            RunTest("Demand No Native Dependency", DemandProjectionStaticILContractTests.Test_ProjectionEngineHasNoNativeOrUnityDependency,
                ref total, ref passed, ref failed);
            RunTest("Demand No Shared Default Radius", DemandProjectionStaticILContractTests.Test_NoSharedDefaultRadiusInControlPlane,
                ref total, ref passed, ref failed);
            RunTest("Demand Engine Does Not Write Domain", DemandProjectionStaticILContractTests.Test_ProjectionEngineDoesNotWriteDomainState,
                ref total, ref passed, ref failed);
            RunTest("Demand Resource Policy Declaration", DemandProjectionStaticILContractTests.Test_ResourceDomainDeclaresItsOwnDemandPolicy,
                ref total, ref passed, ref failed);
            RunTest("Demand Eligibility Delegated To Policy", DemandProjectionStaticILContractTests.Test_ResourceEligibilityIsDelegatedToPolicy,
                ref total, ref passed, ref failed);

            // 票 03：共享生命周期编排引擎拥有编排状态机，Resource 只经领域执行端口操作原生状态。
            RunTest("Lifecycle Orchestration StaticIL", LifecycleOrchestrationStaticILContractTests.Test_All,
                ref total, ref passed, ref failed);
            RunTest("Lifecycle Seam Holds No State Machine", LifecycleOrchestrationStaticILContractTests.Test_SeamHoldsNoOrchestrationState,
                ref total, ref passed, ref failed);
            RunTest("Lifecycle Engine Reaches Native Only Via Port", LifecycleOrchestrationStaticILContractTests.Test_EngineReachesNativeStateOnlyThroughPort,
                ref total, ref passed, ref failed);
            RunTest("Lifecycle Engine Consumes Typed Demand", LifecycleOrchestrationStaticILContractTests.Test_EngineConsumesTypedDemandWithoutReprojectingSpatialFacts,
                ref total, ref passed, ref failed);
            RunTest("Lifecycle Resource Policy Declaration", LifecycleOrchestrationStaticILContractTests.Test_ResourceDeclaresItsOwnLifecyclePolicy,
                ref total, ref passed, ref failed);
            RunTest("Lifecycle Single Domain Registration", LifecycleOrchestrationStaticILContractTests.Test_SingleDomainRegistrationSiteIsResource,
                ref total, ref passed, ref failed);
            RunTest("Lifecycle No Domain Branch", LifecycleOrchestrationStaticILContractTests.Test_EngineHasNoDomainBranch,
                ref total, ref passed, ref failed);

            // 票 04：共享控制面正式切换准入不变量的结构门禁。
            RunTest("Control Plane Readiness StaticIL", ControlPlaneReadinessStaticILContractTests.Test_All,
                ref total, ref passed, ref failed);
            RunTest("Readiness Retry Transaction Scope", ControlPlaneReadinessStaticILContractTests.Test_AcquireRetryIsTransactionScoped,
                ref total, ref passed, ref failed);
            RunTest("Readiness Deferred Demand Never Releases", ControlPlaneReadinessStaticILContractTests.Test_DeferredDemandNeverReleases,
                ref total, ref passed, ref failed);
            RunTest("Readiness Session Identity Gate", ControlPlaneReadinessStaticILContractTests.Test_SessionIdentityGateIsPureAndBounded,
                ref total, ref passed, ref failed);
            RunTest("Readiness Per-Record Sample Admission", ControlPlaneReadinessStaticILContractTests.Test_SampleAdmissionIsPerRecord,
                ref total, ref passed, ref failed);
            RunTest("Readiness Outer Catch Does Not End Session", ControlPlaneReadinessStaticILContractTests.Test_SharedOuterCatchDoesNotEndSession,
                ref total, ref passed, ref failed);
            RunTest("Readiness Bounded Fault Heartbeat", ControlPlaneReadinessStaticILContractTests.Test_PersistentFaultHeartbeatIsNotOneShot,
                ref total, ref passed, ref failed);
            RunTest("Readiness Zombie Fault Bypasses Log Quota", ControlPlaneReadinessStaticILContractTests.Test_ZombieFaultClosureBypassesSharedLogQuota,
                ref total, ref passed, ref failed);
            RunTest("Readiness Shared Fault Channel", ControlPlaneReadinessStaticILContractTests.Test_SharedFaultChannelIsBoundedAndCloses,
                ref total, ref passed, ref failed);

            _currentEvidenceClass = EvidenceClass.BuildArtifact;
            Console.WriteLine("\n=== Evidence Class: BuildArtifact ===");
            Console.WriteLine("  Self-reported Fingerprint: " + BuildFingerprint.Capture(typeof(SteamP2PFriendsPlugin).Assembly).ToLogString());
            RunTest("BuildArtifact Fingerprint Shape", BuildArtifactEvidenceTests.Test_All,
                ref total, ref passed, ref failed);
            RunTest("BuildArtifact Slice Identity", BuildArtifactEvidenceTests.Test_SliceIdentityIsPinnedToMigrationStage,
                ref total, ref passed, ref failed);
            RunTest("BuildArtifact Independent Verifier", BuildArtifactEvidenceTests.Test_VerifierConfirmsBuiltArtifactIdentity,
                ref total, ref passed, ref failed);
            RunTest("BuildArtifact Shared Case-ID", BuildArtifactEvidenceTests.Test_CaseIdOverrideIsShared,
                ref total, ref passed, ref failed);
            RunTest("BuildArtifact Case-ID Fallback", BuildArtifactEvidenceTests.Test_InvalidCaseIdFallsBackToBuildMetadata,
                ref total, ref passed, ref failed);
            RunTest("BuildArtifact Test Metadata", BuildArtifactEvidenceTests.Test_TestAssemblyConsumesVersionMetadata,
                ref total, ref passed, ref failed);
            RunTest("BuildArtifact Full Log Identity", BuildArtifactEvidenceTests.Test_VerifierRequiresFullLogIdentity,
                ref total, ref passed, ref failed);
            RunTest("BuildArtifact Rejects Incomplete Log", BuildArtifactEvidenceTests.Test_VerifierRejectsIncompleteLogIdentity,
                ref total, ref passed, ref failed);

            _currentEvidenceClass = EvidenceClass.Runtime;
            Console.WriteLine("\n=== Evidence Class: Runtime ===");
            Console.WriteLine("  PENDING Runtime evidence: " + RuntimeEvidenceStatus.RequiredScenarios);

            Console.WriteLine("\n===============================================================");
            Console.WriteLine($"=== Final Result: {passed}/{total} PASS (Failed: {failed}) ===");
            Console.WriteLine("===============================================================");
            return failed == 0 ? 0 : 1;
        }

        /// <summary>
        /// PureMemory 资格契约（DG1-DG3）需要真实执行生产路径
        /// IsDedicatedOrP2PHost() → HostManager.ShouldProcessClientHostListen()
        /// → Provider._isServer/_isConnected。Provider..cctor 仅向四个
        /// BattlEye 类类型静态字段写入 null（battlEyeClientInitData/RunData、
        /// battlEyeServerInitData/RunData，IL 实测），JIT 解析其类型即可，
        /// 不调用任何 BattlEye 成员；BattlEye.dll 是游戏运行时组件，不在本仓
        /// Libs 快照、也非插件编译依赖。该 resolver 只在测试进程内合成仅含
        /// 四个类类型壳的动态程序集，使生产资格函数得以真实执行。
        /// 作用域：仅响应 BattlEye 程序集名；不落盘、不进 Libs、不随插件 DLL
        /// 打包、不改变游戏内真实解析。
        /// </summary>
        private static void InstallBattlEyeTypeResolutionStub()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, eventArgs) =>
            {
                AssemblyName name;
                try { name = new AssemblyName(eventArgs.Name); }
                catch { return null; }
                if (name.Name != "BattlEye") return null;

                AssemblyBuilder assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(
                    new AssemblyName("BattlEye, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null"),
                    AssemblyBuilderAccess.Run);
                ModuleBuilder module = assembly.DefineDynamicModule("BattlEye");

                TypeBuilder client = module.DefineType(
                    "BattlEye.BEClient", TypeAttributes.Public | TypeAttributes.Class);
                TypeBuilder clientInit = client.DefineNestedType("BECL_GAME_DATA", TypeAttributes.NestedPublic);
                TypeBuilder clientRun = client.DefineNestedType("BECL_BE_DATA", TypeAttributes.NestedPublic);

                TypeBuilder server = module.DefineType(
                    "BattlEye.BEServer", TypeAttributes.Public | TypeAttributes.Class);
                TypeBuilder serverInit = server.DefineNestedType("BESV_GAME_DATA", TypeAttributes.NestedPublic);
                TypeBuilder serverRun = server.DefineNestedType("BESV_BE_DATA", TypeAttributes.NestedPublic);

                // 嵌套类型必须先于宿主类型定型，否则宿主程序集内解析不到嵌套 TypeRef
                clientInit.CreateType();
                clientRun.CreateType();
                client.CreateType();
                serverInit.CreateType();
                serverRun.CreateType();
                server.CreateType();
                return assembly;
            };
        }

        private static void RunTest(string name, Func<bool> test, ref int total, ref int passed, ref int failed)
        {
            total++;
            try
            {
                if (test())
                {
                    passed++;
                    Console.WriteLine("  PASS [" + _currentEvidenceClass + "] " + name);
                }
                else
                {
                    failed++;
                    Console.WriteLine("  FAIL [" + _currentEvidenceClass + "] " + name);
                }
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine("  FAIL [" + _currentEvidenceClass + "] " + name + ": " + ex);
            }
        }
    }
}
