using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
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
        private static RegisteredTestExecution _registeredExecution;

        /// <summary>t145：离线宿主的主配置。生产由插件用 BepInEx 的 com.yu80rice.steamp2pfriends.cfg 绑定。</summary>
        private static BepInEx.Configuration.ConfigFile _testPluginConfig;

        private static int Main(string[] args)
        {
            InitializeRunLedger();
            InstallBattlEyeTypeResolutionStub();

            // t145：三个诊断模块改为从插件主配置读取 ConfigEntry；测试宿主先绑一个临时 cfg。
            // saveOnInit=false：不创建磁盘文件，只提供内存中的 ConfigEntry 与路径。
            _testPluginConfig = new BepInEx.Configuration.ConfigFile(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "spf-t145-" + System.Guid.NewGuid().ToString("N") + ".cfg"),
                false);
            SteamP2PFriends.Core.Config.PluginConfig.Bind(_testPluginConfig);

            Console.WriteLine("===============================================================");
            Console.WriteLine("=== SteamP2PFriends Modular TestRunner ===");
            Console.WriteLine("===============================================================");
            int total = 0, passed = 0, failed = 0;

            _currentEvidenceClass = EvidenceClass.PureMemory;
            Console.WriteLine("\n=== Evidence Class: PureMemory ===");
            RunTest("EvidenceClass Catalog + Registration Closure", () =>
                EvidenceClassCatalogTests.Test_All() && RegistrationClosureTests.Test_All(),
                ref total, ref passed, ref failed);
            RunTest("REG04 ProductionRequirements", RegistrationClosureTests.Test_REG04_ProductionRequirementsCloseWithDomainCatalog,
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
            RunTest("LOE10 ObserverReplicationIsolation", LifecycleOrchestrationEngineTests.Test_LOE10_ObserverReplicationFailureIsIsolated, ref total, ref passed, ref failed);
            RunTest("LOE11 ReleaseIdempotence", LifecycleOrchestrationEngineTests.Test_LOE11_CommitReleaseIsIdempotent, ref total, ref passed, ref failed);
            RunTest("LOE12 DiagnosticsCorrelation", LifecycleOrchestrationEngineTests.Test_LOE12_DiagnosticsCarryCorrelationFields, ref total, ref passed, ref failed);
            RunTest("LOE13 RegistrationClosure", LifecycleOrchestrationEngineTests.Test_LOE13_RegistrationClosureFreezesDomainSet, ref total, ref passed, ref failed);
            RunTest("LOE14 AcquireObservable", LifecycleOrchestrationEngineTests.Test_LOE14_SuccessfulAcquireIsObservable, ref total, ref passed, ref failed);
            RunTest("LOE15 FaultedDomainIsolation", LifecycleOrchestrationEngineTests.Test_LOE15_FaultedDomainDoesNotStallOthers, ref total, ref passed, ref failed);
            // 票 09-R1：共享生命周期诊断的身份路由契约（领域身份不丢失、不错标）。
            RunTest("LIR01 BridgeRejectsForeignDomain", LifecycleIdentityRoutingTests.Test_LIR01_BridgeRejectsForeignDomainAtFormattingExit, ref total, ref passed, ref failed);
            RunTest("LIR02 RoutingSinkPreservesDomain", LifecycleIdentityRoutingTests.Test_LIR02_RoutingSinkPreservesDomainPerRoute, ref total, ref passed, ref failed);
            RunTest("LIR03 DefaultLineReentryContract", LifecycleIdentityRoutingTests.Test_LIR03_DefaultLineCarriesReentryContract, ref total, ref passed, ref failed);
            RunTest("LIR04 DefaultSinkOnceKeyDedup", LifecycleIdentityRoutingTests.Test_LIR04_DefaultSinkDeduplicatesOnceKeys, ref total, ref passed, ref failed);
            RunTest("LOE16 ReentryAuthorityAndDomain", LifecycleOrchestrationEngineTests.Test_LOE16_ReentryCarriesAuthorityAndDomain, ref total, ref passed, ref failed);
            RunTest("LOE17 NativeStepFiveDemandOnly", LifecycleOrchestrationEngineTests.Test_LOE17_NativeStepFiveUpdatesDemandWithoutReplication, ref total, ref passed, ref failed);
            RunTest("LOE18 NativeStepFiveKeepsEngineIdentity", LifecycleOrchestrationEngineTests.Test_LOE18_NativeStepFiveDoesNotInvalidateEngineIdentity, ref total, ref passed, ref failed);
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
            RunTest("ARI11 BoundedFailedRetry", LifecycleOrchestrationEngineTests.Test_ARI11_BoundedFailedRetryDoesNotResetOnDuplicateCallback, ref total, ref passed, ref failed);
            RunTest("ARI12 ManualRecoveryIdentity", LifecycleOrchestrationEngineTests.Test_ARI12_ManualRecoveryChecksIdentityAndCooldown, ref total, ref passed, ref failed);
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
            RunTest("IT146 LateDisconnectKeepsReconnectedBinding", ItemObserverReplicationAdapterTests.Test_IT146_LateDisconnectKeepsReconnectedBinding, ref total, ref passed, ref failed);
            RunTest("IT147 DisconnectRequiresExactCoreOwnerAndTransport", ItemObserverReplicationAdapterTests.Test_IT147_DisconnectRequiresExactCoreOwnerAndTransport, ref total, ref passed, ref failed);
            RunTest("IT149 ProductionDisconnectKeepsReconnectedOwner", ItemObserverReplicationAdapterTests.Test_IT149_ProductionDisconnectKeepsReconnectedOwner, ref total, ref passed, ref failed);
            // 0.2.4.10 C-P2-Item-01: 全图准备与首次到访
            RunTest("GBA02 Admission Physical Identity", ItemPreparationAndFirstDemandTests.Test_GBA02_AdmissionRequiresSameCommittedPhysicalIdentity, ref total, ref passed, ref failed);
            RunTest("GBA03 Wait Preserves Failure Budget", ItemPreparationAndFirstDemandTests.Test_GBA03_AdmissionWaitDoesNotChangeFaultDeadlineOrBudget, ref total, ref passed, ref failed);
            RunTest("GBA04 Admission Failure Classification", ItemPreparationAndFirstDemandTests.Test_GBA04_AdmissionMapsToDeferredAndRejectedWithoutHalfPacket, ref total, ref passed, ref failed);
            RunTest("GBA01 Native First Observe Binding", ItemPreparationAndFirstDemandTests.Test_GBA01_NativeFirstObserveBindingAdmission, ref total, ref passed, ref failed);
            RunTest("NIL00 Native Fixture Preflight", ItemPreparationAndFirstDemandTests.Test_NIL00_NativeFixturePreflight, ref total, ref passed, ref failed);
            RunTest("NIL04 Real Identity Value and Reference", ItemPreparationAndFirstDemandTests.Test_NIL04_RealIdentityValueAndReferenceSemantics, ref total, ref passed, ref failed);
            RunTest("NIL01 Real Native Lookup Contract", ItemPreparationAndFirstDemandTests.Test_NIL01_RealNativeLookupContract, ref total, ref passed, ref failed);
            RunTest("IT01 EarlyUnpreparedDeferred", ItemPreparationAndFirstDemandTests.Test_IT01_EarlyOnlineOperationBeforePreparationIsDeferred, ref total, ref passed, ref failed);
            RunTest("IT02 FullMapPreparationOnce", ItemPreparationAndFirstDemandTests.Test_IT02_FullMapPreparationPreparesOnceIncludingEmptyRegions, ref total, ref passed, ref failed);
            RunTest("IT03 FirstDemandActivatesTiming", ItemPreparationAndFirstDemandTests.Test_IT03_FirstObserverDemandActivatesNaturalTimingWithoutModifyingDroppedAge, ref total, ref passed, ref failed);
            RunTest("IT04 SecondObserverBaseline", ItemPreparationAndFirstDemandTests.Test_IT04_SecondObserverReceivesIndependentBaselineWithoutRegenerationOrTimingReset, ref total, ref passed, ref failed);
            RunTest("IT05 CoreBindingRetainsData", ItemPreparationAndFirstDemandTests.Test_IT05_CoreSessionBindingRetainsPreparedDataWhileStaleEpochIsRejected, ref total, ref passed, ref failed);
            RunTest("IT06 NestedDroppedPreserved", ItemPreparationAndFirstDemandTests.Test_IT06_OriginTrackingPreservesNestedDroppedItemsDuringPreparation, ref total, ref passed, ref failed);
            RunTest("IT07 ExpiryAlignsWithU3DS", ItemPreparationAndFirstDemandTests.Test_IT07_ExpiryClassificationAlignsWithU3DS, ref total, ref passed, ref failed);
            RunTest("IT08 FailedPrepNotReady", ItemPreparationAndFirstDemandTests.Test_IT08_FailedPreparationDoesNotMarkReady, ref total, ref passed, ref failed);
            RunTest("IT09 DemandPolicyDeclaration", ItemPreparationAndFirstDemandTests.Test_IT09_ItemDemandPolicyDeclaration, ref total, ref passed, ref failed);
            RunTest("IT10 PortAndSharedEngine", ItemPreparationAndFirstDemandTests.Test_IT10_ItemExecutionPortAndSharedEngineIntegration, ref total, ref passed, ref failed);
            RunTest("IT11 StaleSessionAcquireRejected", ItemPreparationAndFirstDemandTests.Test_IT11_StaleOrUnboundSessionAcquireIsRejected, ref total, ref passed, ref failed);
            RunTest("IT12 StaleReleaseRejected", ItemPreparationAndFirstDemandTests.Test_IT12_StaleReceiptOrEpochReleaseIsRejected, ref total, ref passed, ref failed);
            RunTest("IT13 PrepBoundaryAndEpoch", ItemPreparationAndFirstDemandTests.Test_IT13_PreparationRejectsOutOfBoundsRegionsAndZeroEpoch, ref total, ref passed, ref failed);
            RunTest("IT14 ReceiptIdentityInvariants", ItemPreparationAndFirstDemandTests.Test_IT14_ReceiptFullIdentityAndEquality, ref total, ref passed, ref failed);
            RunTest("IT15 MetadataStoreCleanup", ItemPreparationAndFirstDemandTests.Test_IT15_MetadataStoreItemRemovalAndSessionCleanup, ref total, ref passed, ref failed);
            RunTest("IT16 WarehouseBridgePreparation", ItemPreparationAndFirstDemandTests.Test_IT16_ProductionSeamFullMapPreparationWithWarehouseBridge, ref total, ref passed, ref failed);
            RunTest("IT17 FirstDemandAgePreservation", ItemPreparationAndFirstDemandTests.Test_IT17_ProductionSeamFirstDemandTimingAndAgePreservation, ref total, ref passed, ref failed);
            RunTest("IT18 IndependentObserverBaselines", ItemPreparationAndFirstDemandTests.Test_IT18_ProductionSeamIndependentObserverBaselinesAndHostPresentation, ref total, ref passed, ref failed);
            RunTest("IT19 UnpreparedGateRejection", ItemPreparationAndFirstDemandTests.Test_IT19_ProductionSeamUnpreparedOperationGateRejection, ref total, ref passed, ref failed);
            RunTest("IT20 BridgeExceptionAndIdentity", ItemPreparationAndFirstDemandTests.Test_IT20_WarehouseBridgeExceptionPropagationAndIdentityCheck, ref total, ref passed, ref failed);
            RunTest("IT21 CreationPathAndNestedDrops", ItemPreparationAndFirstDemandTests.Test_IT21_CreationPathTrackingAndNestedDropPreservation, ref total, ref passed, ref failed);
            RunTest("IT22 MutualExclusionAndGates", ItemPreparationAndFirstDemandTests.Test_IT22_CandidateLegacyMutualExclusionAndPreparationGates, ref total, ref passed, ref failed);
            RunTest("IT23 NativeBridgeAndListSafety", ItemPreparationAndFirstDemandTests.Test_IT23_NativeBridgeValidationAndListReplacementSafety, ref total, ref passed, ref failed);
            RunTest("IT24 OriginTrackingAndTimingBinding", ItemPreparationAndFirstDemandTests.Test_IT24_StrictCreationPathTrackingAndNativeTimingBinding, ref total, ref passed, ref failed);
            RunTest("IT25 ActivateRegionTimingValidation", ItemPreparationAndFirstDemandTests.Test_IT25_ActivateRegionTimingFailsOnMissingRegionOrField, ref total, ref passed, ref failed);
            RunTest("IT26 TimingRollbackAndAcquireClassification", ItemPreparationAndFirstDemandTests.Test_IT26_ActivateRegionTimingRollbackAndAcquireFailureClassification, ref total, ref passed, ref failed);
            RunTest("IT27 CandidateAskItemsSend", ItemCandidateGuestBaselineSendTests.Test_IT27_CandidateOwnedAskItemsRunsAndRejectedSendDoesNotCommit, ref total, ref passed, ref failed);
            RunTest("IT28 CoDemandNaturalCycle", ItemPreparationAndFirstDemandTests.Test_IT28_OneObserverLeavingDoesNotStopTheOtherDemandCycle, ref total, ref passed, ref failed);
            RunTest("IT30 UnvisitedDroppedExpiry", ItemPreparationAndFirstDemandTests.Test_IT30_UnvisitedDroppedExpiresWithoutStartingNaturalCycle, ref total, ref passed, ref failed);
            RunTest("IT31 DemandRespawnEffect", ItemPreparationAndFirstDemandTests.Test_IT31_DemandRunsRespawnAndRecordsOnlyRealCreation, ref total, ref passed, ref failed);
            RunTest("IT32 NestedRespawnDrop", ItemPreparationAndFirstDemandTests.Test_IT32_NestedRespawnDropStaysDroppedAndIsNotReplaced, ref total, ref passed, ref failed);
            RunTest("IT33 HysteresisNotDemand", ItemPreparationAndFirstDemandTests.Test_IT33_HysteresisDoesNotResetAgeOrImpersonateDemand, ref total, ref passed, ref failed);
            RunTest("IT34 DistinctExpiryLimits", ItemPreparationAndFirstDemandTests.Test_IT34_NaturalAndDroppedUseDifferentU3dsLimits, ref total, ref passed, ref failed);
            RunTest("IT36 BoundedDroppedExpiry", ItemPreparationAndFirstDemandTests.Test_IT36_OneStepDoesNotClearEveryExpiredDrop, ref total, ref passed, ref failed);
            RunTest("IT37 DeclineAndModeException", ItemPreparationAndFirstDemandTests.Test_IT37_DeclinedRespawnDoesNotCommitTimeAndArenaSkipsExpiry, ref total, ref passed, ref failed);
            RunTest("IT38 ReentryKeepsTiming", ItemPreparationAndFirstDemandTests.Test_IT38_ReentryDoesNotRestartNaturalTiming, ref total, ref passed, ref failed);
            RunTest("IT39 ReturnClearsBeforeRestock", ItemPreparationAndFirstDemandTests.Test_IT39_ReturnClearsAllExpiredBeforeAnyRestock, ref total, ref passed, ref failed);
            RunTest("IT40 SurvivorAgeAndFailedRestock", ItemPreparationAndFirstDemandTests.Test_IT40_SurvivorAgeKeptAndFailedRestockStops, ref total, ref passed, ref failed);
            RunTest("IT41 ReturnWaitAndDeliver", ItemPreparationAndFirstDemandTests.Test_IT41_ReturnWaitsAndDeliversWithoutFailureBudget, ref total, ref passed, ref failed);
            RunTest("SFP03 RealFrameGateProgressesBoundedSettlement", ItemPreparationAndFirstDemandTests.Test_SFP03_RealFrameGateProgressesBoundedSettlement, ref total, ref passed, ref failed);
            RunTest("SFP05 FrameClockUsesOneDeltaPerFrame", ItemPreparationAndFirstDemandTests.Test_SFP05_FrameClockUsesOneDeltaPerFrame, ref total, ref passed, ref failed);
            RunTest("IT42 HysteresisReentrySettles", ItemPreparationAndFirstDemandTests.Test_IT42_HysteresisReentryStillSettlesBeforePresent, ref total, ref passed, ref failed);
            RunTest("IT43 LastDemandFinishesQuiet", ItemPreparationAndFirstDemandTests.Test_IT43_LastDemandLeavesFinishesWithoutList, ref total, ref passed, ref failed);
            RunTest("IT44 UnreadyPickupRejected", ItemPreparationAndFirstDemandTests.Test_IT44_UnreadyPickupDoesNotWriteOrDelete, ref total, ref passed, ref failed);
            RunTest("IT45 SharedSettlementBudget", ItemPreparationAndFirstDemandTests.Test_IT45_RegionsShareOneSettlementAction, ref total, ref passed, ref failed);
            RunTest("IT46 LaterGuestSameResult", ItemPreparationAndFirstDemandTests.Test_IT46_LaterGuestWaitsForSameResult, ref total, ref passed, ref failed);
            RunTest("IT47 StaleIdentityIsolates", ItemPreparationAndFirstDemandTests.Test_IT47_StaleRegionIdentityIsolatesTheJob, ref total, ref passed, ref failed);
            RunTest("IT48 DropDuringSettlement", ItemPreparationAndFirstDemandTests.Test_IT48_DropExpiresDuringSettlementWithoutResurrection, ref total, ref passed, ref failed);
            RunTest("IT49 WrapUpReentryAndNextReturn", ItemPreparationAndFirstDemandTests.Test_IT49_WrapUpReentryDoesNotRedoAndNextReturnRejudges, ref total, ref passed, ref failed);
            RunTest("IT50 StepFivePendingCommit", ItemPreparationAndFirstDemandTests.Test_IT50_StepFivePendingTransfersToCommittedJob, ref total, ref passed, ref failed);
            RunTest("IT51 NaturalCreationExceptionIsolated", ItemPreparationAndFirstDemandTests.Test_IT51_NaturalCreationExceptionIsIsolated, ref total, ref passed, ref failed);
            RunTest("IT52 CompletedRegionDoesNotHoldNextBaseline", ItemPreparationAndFirstDemandTests.Test_IT52_CompletedRegionDoesNotHoldNextBaseline, ref total, ref passed, ref failed);
            RunTest("IT53 StepFiveAdmissionUsesDeliveredSuppression", ItemPreparationAndFirstDemandTests.Test_IT53_StepFiveAdmissionUsesDeliveredSuppression, ref total, ref passed, ref failed);
            RunTest("IT54 StepFiveAdmissionKeepsNeighborIndependent", ItemPreparationAndFirstDemandTests.Test_IT54_StepFiveAdmissionKeepsNeighborIndependent, ref total, ref passed, ref failed);
            RunTest("IT55 StepFiveAdmissionPreparesSettlementBeforeDemand", ItemPreparationAndFirstDemandTests.Test_IT55_StepFiveAdmissionPreparesSettlementBeforeDemand, ref total, ref passed, ref failed);
            RunTest("IT56 MissingInputsDoNotFinishSettlement", ItemPreparationAndFirstDemandTests.Test_IT56_MissingInputsDoNotFinishSettlement, ref total, ref passed, ref failed);
            RunTest("IT59 MissingInputsDoNotFinishSettlement", ItemPreparationAndFirstDemandTests.Test_IT59_MissingInputsDoNotFinishSettlement, ref total, ref passed, ref failed);
            RunTest("IT57 NetworkedDeletionStillBroadcastsWhenGateClosed", ItemPreparationAndFirstDemandTests.Test_IT57_NetworkedDeletionStillBroadcastsWhenGateClosed, ref total, ref passed, ref failed);
            RunTest("IT58 UnpresentedDeletionKeepsNativeDestroyAction", ItemPreparationAndFirstDemandTests.Test_IT58_UnpresentedDeletionKeepsNativeDestroyAction, ref total, ref passed, ref failed);
            RunTest("IT60 MissingAssetDoesNotFinishSettlement", ItemPreparationAndFirstDemandTests.Test_IT60_MissingAssetDoesNotFinishSettlement, ref total, ref passed, ref failed);
            RunTest("IT133 ProductionNaturalInputsDistinguishEmptyFromMissing", ItemPreparationAndFirstDemandTests.Test_IT133_ProductionNaturalInputsDistinguishEmptyFromMissing, ref total, ref passed, ref failed);
            RunTest("IT134 ZeroSpawnPointRegionSettlesAndDelivers", ItemPreparationAndFirstDemandTests.Test_IT134_ZeroSpawnPointRegionSettlesAndDelivers, ref total, ref passed, ref failed);
            RunTest("IT135 UncertainReplicationFailureNotifiesOnceWithTrace", ItemPreparationAndFirstDemandTests.Test_IT135_UncertainReplicationFailureNotifiesOnceWithTrace, ref total, ref passed, ref failed);
            RunTest("IT136 UnconfirmedNoticeDeduplicatesPerObserverRegion", ItemPreparationAndFirstDemandTests.Test_IT136_UnconfirmedNoticeDeduplicatesPerObserverRegion, ref total, ref passed, ref failed);
            RunTest("IT137 HalfPacketExitReturnDeliversFreshBaseline", ItemPreparationAndFirstDemandTests.Test_IT137_HalfPacketExitReturnDeliversFreshBaseline, ref total, ref passed, ref failed);
            RunTest("IT138 SettlementConfirmedNotSentRetriesOnlyGuestDelivery", ItemPreparationAndFirstDemandTests.Test_IT138_SettlementConfirmedNotSentRetriesOnlyGuestDelivery, ref total, ref passed, ref failed);
            RunTest("IT139 ConfirmedNotSentExhaustionAndManualRestart", ItemPreparationAndFirstDemandTests.Test_IT139_ConfirmedNotSentExhaustionAndManualRestart, ref total, ref passed, ref failed);
            RunTest("IT140 ConfirmedNotSentRetryDeadlineStopsDelivery", ItemPreparationAndFirstDemandTests.Test_IT140_ConfirmedNotSentRetryDeadlineStopsDelivery, ref total, ref passed, ref failed);
            RunTest("IT141 UnknownDuringDeliveryRetryQuarantinesOnce", ItemPreparationAndFirstDemandTests.Test_IT141_UnknownDuringDeliveryRetryQuarantinesOnce, ref total, ref passed, ref failed);
            RunTest("IT142 FailureTraceReopensOnlyAfterRealObserverExit", ItemPreparationAndFirstDemandTests.Test_IT142_FailureTraceReopensOnlyAfterRealObserverExit, ref total, ref passed, ref failed);
            RunTest("IT143 ExitedDeliveryIsRevokedWithoutAffectingOtherGuest", ItemPreparationAndFirstDemandTests.Test_IT143_ExitedDeliveryIsRevokedWithoutAffectingOtherGuest, ref total, ref passed, ref failed);
            RunTest("IT144 ConnectionFailureCleanupUsesExactIdentity", ItemPreparationAndFirstDemandTests.Test_IT144_ConnectionFailureCleanupUsesExactIdentity, ref total, ref passed, ref failed);
            RunTest("IT145 ObserverExitFailureReportsIdentityAndStack", ItemPreparationAndFirstDemandTests.Test_IT145_ObserverExitFailureReportsIdentityAndStack, ref total, ref passed, ref failed);
            RunTest("IT148 StaleExitPreservesCurrentAndOtherGuestDemand", ItemPreparationAndFirstDemandTests.Test_IT148_StaleExitPreservesCurrentAndOtherGuestDemand, ref total, ref passed, ref failed);
            RunTest("IT61 ItemFailureRecoveryPolicy", ItemPreparationAndFirstDemandTests.Test_IT61_ItemFailureRecoveryPolicyIsBounded, ref total, ref passed, ref failed);
            RunTest("IT62 ExceptionRecoveryLocalRetry", ItemPreparationAndFirstDemandTests.Test_IT62_ExceptionRecoveryRetriesOnlyTheFailedRegion, ref total, ref passed, ref failed);
            RunTest("IT63 RecoveryCommandFailClosed", ItemRecoveryCommandTests.Test_IT63_RecoveryCommandParsesAndFailsClosed, ref total, ref passed, ref failed);
            RunTest("IT64 RecoveryCommandRemoteGate", ItemRecoveryCommandTests.Test_IT64_ApprovedRecoveryCommandPassesRemoteGate, ref total, ref passed, ref failed);
            RunTest("IT65 RecoveryCommandSessionIsolation", ItemRecoveryCommandTests.Test_IT65_HostSessionCommandSubscriptionIsEpochBound, ref total, ref passed, ref failed);
            RunTest("IT66 RecoveryManualRateLimit", ItemRecoveryCommandTests.Test_IT66_ManualRateLimitCannotFallThrough, ref total, ref passed, ref failed);
            RunTest("IT67 IsolatedRegionReentry", ItemPreparationAndFirstDemandTests.Test_IT67_IsolatedRegionReentryStartsFreshRecovery, ref total, ref passed, ref failed);
            RunTest("IT68 RecoveryTokenNormalization", ItemRecoveryCommandTests.Test_IT68_ManualRecoveryNormalizesCurrentConnectionToken, ref total, ref passed, ref failed);
            RunTest("IT69 HalfPacketObserverGate", ItemRecoveryCommandTests.Test_IT69_HalfPacketGateIsObserverAndRegionScoped, ref total, ref passed, ref failed);
            RunTest("IT70 RecoveryCommandFailClosed", ItemRecoveryCommandTests.Test_IT70_InvalidRecoveryCommandFailsClosed, ref total, ref passed, ref failed);
            RunTest("IT71 RecoveryCommandExceptionClosed", ItemRecoveryCommandTests.Test_IT71_RecoveryCallbackExceptionFailsClosed, ref total, ref passed, ref failed);
            RunTest("IT72 RecoveryCommandRegistrationGate", ItemRecoveryCommandTests.Test_IT72_UnregisteredRecoveryCommandIsBlocked, ref total, ref passed, ref failed);
            RunTest("IT73 IdentityInvalidationRecovery", ItemRecoveryCommandTests.Test_IT73_IdentityInvalidationDropsRecoveryChain, ref total, ref passed, ref failed);
            RunTest("IT74 RecoveryExactDisconnectToken", ItemRecoveryCommandTests.Test_IT74_DisconnectReleaseUsesExactToken, ref total, ref passed, ref failed);
            RunTest("IT75 CoreGenerationIdentity", ItemRecoveryCommandTests.Test_IT75_CoreGenerationIsSoleCandidateIdentity, ref total, ref passed, ref failed);
            RunTest("IT76 RegionReleaseIsolation", ItemRecoveryCommandTests.Test_IT76_RegionReleaseKeepsOtherRecoveryWaiter, ref total, ref passed, ref failed);
            RunTest("IT77 UnknownRegionItems", ItemRecoveryCommandTests.Test_IT77_UnknownRegionItemsStayUnconfirmed, ref total, ref passed, ref failed);
            RunTest("IT78 GuestReplicationIsolation", ItemPreparationAndFirstDemandTests.Test_IT78_GuestReplicationFailureStaysObserverLocal, ref total, ref passed, ref failed);
            RunTest("IT79 UncertainGuestSendQuarantine", ItemPreparationAndFirstDemandTests.Test_IT79_UncertainGuestSendIsQuarantinedNotRetried, ref total, ref passed, ref failed);
            RunTest("IT80 UnknownItemsNoRestart", ItemPreparationAndFirstDemandTests.Test_IT80_UnknownItemsDoNotRestartClearingOrRestock, ref total, ref passed, ref failed);
            RunTest("IT81 UnobservedAskItemsNotSafe", ItemPreparationAndFirstDemandTests.Test_IT81_UnobservedAskItemsIsNotSafeRetry, ref total, ref passed, ref failed);
            RunTest("IT82 AppliedCreateNotRedrawn", ItemPreparationAndFirstDemandTests.Test_IT82_AppliedCreateIsNotRedrawnAfterThrow, ref total, ref passed, ref failed);
            RunTest("IT83 ExhaustedSettlementNotice", ItemPreparationAndFirstDemandTests.Test_IT83_ExhaustedSettlementNotifiesCurrentWaiterOnce, ref total, ref passed, ref failed);
            RunTest("IT84 ManualRejectsOutsideProjection", ItemPreparationAndFirstDemandTests.Test_IT84_ManualRecoveryRejectsCallerOutsideProjection, ref total, ref passed, ref failed);
            RunTest("IT85 CoveringCallerManualRecovery", ItemPreparationAndFirstDemandTests.Test_IT85_CoveringCallerManualRecoveryDoesNotUseAnotherCooldown, ref total, ref passed, ref failed);
            RunTest("IT86 UnconfirmedNativeNotice", ItemPreparationAndFirstDemandTests.Test_IT86_UnconfirmedNativeResultNotifiesWithoutReplay, ref total, ref passed, ref failed);
            RunTest("IT87 SeamCooldownPerCaller", ItemPreparationAndFirstDemandTests.Test_IT87_SeamCooldownIsPerCoveringCaller, ref total, ref passed, ref failed);
            RunTest("IT88 SessionWriterCutover", ItemWriterCutoverTests.Test_IT88_SessionBoundarySelectsSingleItemAuthorityWriter, ref total, ref passed, ref failed);
            RunTest("IT89 FullMapCycleRetired", ItemWriterCutoverTests.Test_IT89_ListenHostFullMapCycleDoesNotDrive, ref total, ref passed, ref failed);
            RunTest("IT90 LegacyEligibilityRetired", ItemWriterCutoverTests.Test_IT90_OwnedSessionRejectsLegacyEligibilityAndFallback, ref total, ref passed, ref failed);
            RunTest("IT91 SessionSwitchIsolation", ItemWriterCutoverTests.Test_IT91_SessionSwitchDropsOldConnectionAndDoesNotRepeatPrepare, ref total, ref passed, ref failed);
            RunTest("SFP01 ResourceStartedEngineInitializesItemPerWorld", ItemWriterCutoverTests.Test_SFP01_ResourceStartedEngineStillInitializesItemPerWorld, ref total, ref passed, ref failed);
            RunTest("IT92 LegacyCycleStaysRetired", ItemWriterCutoverTests.Test_IT92_ListenHostLegacyCycleStaysRetiredAfterSessionEnd, ref total, ref passed, ref failed);
            RunTest("IT93 WorldChangeIsolation", ItemWriterCutoverTests.Test_IT93_WorldChangeIsolatesOldItemSessionWithoutFallback, ref total, ref passed, ref failed);
            RunTest("IT94 EligibilityStaysRetired", ItemWriterCutoverTests.Test_IT94_ListenHostEligibilityStaysRetiredWhenWriterUnowned, ref total, ref passed, ref failed);
            RunTest("IT95 OverDistancePickupRejected", ItemAuthoritativePickupTests.Test_IT95_OverDistanceRejectsBeforeInventoryWrite, ref total, ref passed, ref failed);
            RunTest("IT96 DistanceSquared400Allowed", ItemAuthoritativePickupTests.Test_IT96_DistanceSquared400RunsNativeOnce, ref total, ref passed, ref failed);
            RunTest("IT97 NonAuthorityOriginalPath", ItemAuthoritativePickupTests.Test_IT97_NonAuthorityKeepsOriginalPath, ref total, ref passed, ref failed);
            RunTest("IT98 UnreadyRegionPickupRejected", ItemAuthoritativePickupTests.Test_IT98_UnreadyRegionRejectsBeforeWrite, ref total, ref passed, ref failed);
            RunTest("IT99 GuestLeaseCannotBypass", ItemAuthoritativePickupTests.Test_IT99_GuestLeaseCannotBypassInstanceLookup, ref total, ref passed, ref failed);
            RunTest("IT100 NativeRejectionAfterDistance", ItemAuthoritativePickupTests.Test_IT100_LegalDistanceStillHonorsNativeRejectionOnce, ref total, ref passed, ref failed);
            RunTest("IT101 MissingInstanceIgnoresClientPoint", ItemAuthoritativePickupTests.Test_IT101_MissingInstanceIgnoresClientPoint, ref total, ref passed, ref failed);
            RunTest("IT102 HostPresentThenStepFivePending", ItemLocalPresentationPipelineTests.Test_IT102_SharedPresentThenRetiredStepFiveDuplicatesPending, ref total, ref passed, ref failed);
            RunTest("IT103 StepFiveThenHostPresentPending", ItemLocalPresentationPipelineTests.Test_IT103_RetiredStepFiveThenSharedPresentKeepsOnePending, ref total, ref passed, ref failed);
            RunTest("IT105 ReentryPresentPending", ItemLocalPresentationPipelineTests.Test_IT105_ReentryPresentKeepsOnePending, ref total, ref passed, ref failed);
            RunTest("IT121c NotifyFailureCancelsPending", ItemLocalPresentationPipelineTests.Test_IT121c_NotifyFailureStillCancelsPending, ref total, ref passed, ref failed);
            RunTest("IT121a NotStartedKeepsPending", ItemLocalPresentationPipelineTests.Test_IT121a_NotStartedKeepsPendingForRetry, ref total, ref passed, ref failed);
            RunTest("IT121b UncertainKeepsPending", ItemLocalPresentationPipelineTests.Test_IT121b_UncertainKeepsPendingForQuarantine, ref total, ref passed, ref failed);
            RunTest("IT121d BlockedRegionKeepsPending", ItemLocalPresentationPipelineTests.Test_IT121d_BlockedRegionSkipsButKeepsPending, ref total, ref passed, ref failed);
            RunTest("IT123 UnconfirmedSurvivesRegionExit", ItemLocalPresentationPipelineTests.Test_IT123_UnconfirmedSideEffectSurvivesRegionExit, ref total, ref passed, ref failed);
            RunTest("IT124 ProductionClassifierBoundary", ItemLocalPresentationPipelineTests.Test_IT124_ProductionClassifierJudgesRealBoundary, ref total, ref passed, ref failed);
            RunTest("IT125 IncrementDestroyPolicy", ItemLocalPresentationPipelineTests.Test_IT125_IncrementDestroyPolicyMatrix, ref total, ref passed, ref failed);
            RunTest("IT126 NonRetiredStepFivePassthrough", ItemLocalPresentationPipelineTests.Test_IT126_NonRetiredStepFiveKeepsNativePassthrough, ref total, ref passed, ref failed);
            RunTest("IT129 ClientPendingBatchContractPlural", ItemLocalPresentationPipelineTests.Test_IT129_ClientPendingBatchContractIsPlural, ref total, ref passed, ref failed);
            RunTest("IT130 OwnedBatchDefersOnStaleGeneration", ItemLocalPresentationPipelineTests.Test_IT130_OwnedBatchDefersOnStaleGeneration, ref total, ref passed, ref failed);
            RunTest("IT131 ClassifierPrecheckAndPostEntryFailure", ItemLocalPresentationPipelineTests.Test_IT131_ClassifierDistinguishesPrecheckAndPostEntryFailure, ref total, ref passed, ref failed);
            RunTest("IT132 PresentSharedSortOrderBeforeSideEffects", ItemLocalPresentationPipelineTests.Test_IT132_PresentSharedReadsSortOrderBeforeSideEffects, ref total, ref passed, ref failed);
            RunTest("E02A1 StepFiveFirstPresentsTargetB", R01E02SliceATests.Test_E02_A1_StepFiveFirstPresentsTargetB, ref total, ref passed, ref failed);
            RunTest("E02A2 SharedFirstStepFiveIdempotent", R01E02SliceATests.Test_E02_A2_SharedFirstThenStepFiveIdempotent, ref total, ref passed, ref failed);
            RunTest("E02A3 IdentityGuardsRejectAndWait", R01E02SliceATests.Test_E02_A3_IdentityGuardsRejectAndWait, ref total, ref passed, ref failed);
            RunTest("E02AC2 ProductionCombination", ItemLocalPresentationPipelineTests.Test_E02_AC2_ProductionCombination, ref total, ref passed, ref failed);
            RunTest("E02 ProbeLeafMechanical", ItemLocalPresentationPipelineTests.Test_E02_ProbeLeafMechanical, ref total, ref passed, ref failed);
            RunTest("E02B1 StepFiveFirstExitCleanupShared", R01E02SliceATests.Test_E02_B1_StepFiveFirstExitCleanupAndSharedDemand, ref total, ref passed, ref failed);
            RunTest("E02C1 NewInstanceIdLifecycle", R01E02SliceATests.Test_E02_C1_NewInstanceIdLifecycle, ref total, ref passed, ref failed);
            RunTest("R01 V1 BudgetedConsumptionAndReentry", R01E02SliceATests.Test_R01_V1_BudgetedConsumptionAndReentry, ref total, ref passed, ref failed);
            RunTest("R01 V1 ModeledPickupDoesNotResurrect", R01E02SliceATests.Test_R01_V1_ModeledPickupDoesNotResurrect, ref total, ref passed, ref failed);
            RunTest("R01 V1 AfterSnapshotFailureQuarantines", R01E02SliceATests.Test_R01_V1_AfterSnapshotFailureQuarantinesConsumer, ref total, ref passed, ref failed);
            RunTest("E02C2 InvokeAndClassifyMatrix", R01E02SliceATests.Test_E02_C2_InvokeAndClassifyMatrix, ref total, ref passed, ref failed);
            RunTest("E02C3 RegionInstanceQuarantineGate", R01E02SliceATests.Test_E02_C3_RegionInstanceQuarantineGate, ref total, ref passed, ref failed);
            RunTest("E02D1 PendingBatchShapeMatrix", R01E02SliceATests.Test_E02_D1_PendingBatchShapeMatrix, ref total, ref passed, ref failed);
            RunTest("E02D2 PendingWindowSharedDetection", ItemExecutionStaticILContractTests.Test_E02_D2_PendingWindowSharedDetection, ref total, ref passed, ref failed);
            RunTest("E02D3 SpawnSignatureNegativeShapes", R01E02SliceATests.Test_E02_D3_SpawnSignatureNegativeShapes, ref total, ref passed, ref failed);
            RunTest("E02D4 VerifyRegisteredNegativeProofs", R01E02SliceATests.Test_E02_D4_VerifyRegisteredNegativeProofs, ref total, ref passed, ref failed);
            RunTest("R1 VerifyRegisteredSiblingCoexist", R01E02SliceATests.Test_R1_VerifyRegisteredAllowsSameOwnerSiblingTranspilers, ref total, ref passed, ref failed);
            RunTest("R2A ReadModeWriterSource", R01E02SliceATests.Test_R2A_ReadModeWriterSource, ref total, ref passed, ref failed);
            RunTest("R2B PrefixStepFiveModeSplit", R01E02SliceATests.Test_R2B_PrefixStepFiveModeSplit, ref total, ref passed, ref failed);
            RunTest("R2C WrapperModeSplitClassification", R01E02SliceATests.Test_R2C_WrapperModeSplitAndDispatchClassification, ref total, ref passed, ref failed);
            RunTest("R2C DestroyModelReadFailureWaits", R01E02SliceATests.Test_R2C_DestroyModelReadFailureWaitsAfterCancel, ref total, ref passed, ref failed);
            RunTest("R2C DestroyPartialModelFailureWaits", R01E02SliceATests.Test_R2C_DestroyPartialModelFailureWaitsWithoutReplay, ref total, ref passed, ref failed);
            RunTest("R2C DestroyOrderCoexistence", R01E02SliceATests.Test_R2C_DestroyOrderAndModelPendingCoexistence, ref total, ref passed, ref failed);
            RunTest("R2C DispatchIncrementClassification", R01E02SliceATests.Test_R2C_DispatchIncrementClassification, ref total, ref passed, ref failed);
            RunTest("R3 StepFiveCommittedPhysicalBinding", R01E02SliceATests.Test_R3_StepFiveWaitsForCommittedPhysicalBinding, ref total, ref passed, ref failed);
            RunTest("R3 ConsumerCommittedPhysicalBinding", R01E02SliceATests.Test_R3_ConsumerWaitsForCommittedPhysicalBinding, ref total, ref passed, ref failed);
            RunTest("E02B2 GuestExitUnbindsOriginal", R01E02SliceATests.Test_E02_B2_GuestExitUnbindsOriginalRegions, ref total, ref passed, ref failed);
            RunTest("IT128 AdmissionRebuiltPerSession", ItemLocalPresentationPipelineTests.Test_IT128_AdmissionRebuiltPerSessionAndStaleRejected, ref total, ref passed, ref failed);
            RunTest("IT122 SingleIdentityDeleteKeepsOther", ItemLocalPresentationPipelineTests.Test_IT122_SingleIdentityDeleteKeepsOtherIdentity, ref total, ref passed, ref failed);
            RunTest("IT106 UnknownIdentityRejected", ItemLocalPresentationPipelineTests.Test_IT106_UnknownIdentityRejectsWithoutNativeElse, ref total, ref passed, ref failed);
            RunTest("IT107 HostStepFiveEnterOnce", ItemLocalPresentationPipelineTests.Test_IT107_HostStepFiveEnterPresentsOnceThroughSeam, ref total, ref passed, ref failed);
            RunTest("IT108 PartialConsumeAboveMinimum", ItemLocalPresentationPipelineTests.Test_IT108_PartialConsumeLeavesTailAboveMinimumBatch, ref total, ref passed, ref failed);
            RunTest("IT110 RemovePresentedClearsSameId", ItemLocalPresentationPipelineTests.Test_IT110_RemovePresentedClearsModelAndPendingOfThatId, ref total, ref passed, ref failed);
            RunTest("IT111 GuestStepFiveNoHostPresent", ItemLocalPresentationPipelineTests.Test_IT111_GuestStepFiveDoesNotPresentHost, ref total, ref passed, ref failed);
            RunTest("IT118 GuestStepFiveFirstBaselineOnce", ItemLocalPresentationPipelineTests.Test_IT118_GuestStepFiveFirstDeliversBaselineOnce, ref total, ref passed, ref failed);
            RunTest("IT112 SeamEnterThenStepFive", ItemLocalPresentationPipelineTests.Test_IT112_SeamEnterThenStepFiveDoesNotDuplicate, ref total, ref passed, ref failed);
            RunTest("IT113 PickupRejectKeepsPresentation", ItemLocalPresentationPipelineTests.Test_IT113_PickupRejectDoesNotClearPresentation, ref total, ref passed, ref failed);
            RunTest("IT114 FullBudgetConsume", ItemLocalPresentationPipelineTests.Test_IT114_FullBudgetConsumeClearsPending, ref total, ref passed, ref failed);
            RunTest("IT116 DropAfterPickupSingle", ItemLocalPresentationPipelineTests.Test_IT116_DropAfterPickupReenqueuesSinglePresentation, ref total, ref passed, ref failed);
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
            RunTest("CEP01 Execution Port Seam", CollisionExecutionPortTests.Test_CEP01_ExecutionPortSeam, ref total, ref passed, ref failed);
            RunTest("CEP02 Receipt Identity", CollisionExecutionPortTests.Test_CEP02_ReceiptIdentity, ref total, ref passed, ref failed);
            RunTest("CEP03 Receipt Replacement", CollisionExecutionPortTests.Test_CEP03_NewAcquireInvalidatesOldReceipt, ref total, ref passed, ref failed);
            RunTest("CEP04 Receipt Scoped Release", CollisionExecutionPortTests.Test_CEP04_ReleaseOnlyRevokesOwnedReceiptOverrides, ref total, ref passed, ref failed);
            RunTest("CEP05 Fail Closed Release", CollisionExecutionPortTests.Test_CEP05_StaleOrUncertainReleaseDoesNotDestructivelyDisable, ref total, ref passed, ref failed);
            RunTest("CEP06 Release Idempotence", CollisionExecutionPortTests.Test_CEP06_ReleaseIsIdempotent, ref total, ref passed, ref failed);
            RunTest("CEP07 Host Demand", CollisionExecutionPortTests.Test_CEP07_HostDemandBlocksFinalRelease, ref total, ref passed, ref failed);
            RunTest("CEP08 Override Domain", CollisionExecutionPortTests.Test_CEP08_OverrideSetExcludesResourceAndTrees, ref total, ref passed, ref failed);
            RunTest("CEP09 Stale Receipt", CollisionExecutionPortTests.Test_CEP09_StaleReceiptCannotReleaseNewAcquire, ref total, ref passed, ref failed);
            RunTest("CEP10 Release Receipt Diagnostic", CollisionExecutionPortTests.Test_CEP10_ReleaseDiagnosticNamesReceipt, ref total, ref passed, ref failed);
            RunTest("CEP11 Unowned Override Fail Closed", CollisionExecutionPortTests.Test_CEP11_UnownedOverrideFailsClosed, ref total, ref passed, ref failed);
            RunTest("CEP12 Release Rejection Observable", CollisionExecutionPortTests.Test_CEP12_ReleaseRejectionIsObservable, ref total, ref passed, ref failed);
            RunTest("CEP13 Demand Still Present", CollisionExecutionPortTests.Test_CEP13_DemandStillPresentRejectsRelease, ref total, ref passed, ref failed);
            RunTest("CEP14 Receipt Actual Generation", CollisionExecutionPortTests.Test_CEP14_ReceiptUsesActualStoreGeneration, ref total, ref passed, ref failed);
            RunTest("CEP15 Production Store Session Boundary", CollisionExecutionPortTests.Test_CEP15_ProductionStoreFollowsSessionBoundary, ref total, ref passed, ref failed);
            RunTest("CEP16 Empty Store Receipt Release", CollisionExecutionPortTests.Test_CEP16_EmptyStoreReceiptReleasesRegion, ref total, ref passed, ref failed);
            RunTest("CEP17 Lifecycle Tick Forward", CollisionExecutionPortTests.Test_CEP17_PortForwardsLifecycleTickToStore, ref total, ref passed, ref failed);
            RunTest("CEP18 NeverAcquired First Acquire", CollisionExecutionPortTests.Test_CEP18_NeverAcquiredRegionFirstAcquireSucceeds, ref total, ref passed, ref failed);
            RunTest("CEP19 Generation Source Bootstrap", CollisionExecutionPortTests.Test_CEP19_GenerationSourceBootstrapsAndAdvances, ref total, ref passed, ref failed);
            // 票 09-R1：Release/receipt 诊断的会话绑定与身份关联契约。
            RunTest("CEP20 Missing Receipt Explicit", CollisionExecutionPortTests.Test_CEP20_MissingReceiptIsMarkedExplicitly, ref total, ref passed, ref failed);
            RunTest("CEP21 Rejection Dual Identity", CollisionExecutionPortTests.Test_CEP21_RejectionCarriesCommandAndReceiptIdentities, ref total, ref passed, ref failed);
            RunTest("CEP22 Success Full Identity", CollisionExecutionPortTests.Test_CEP22_SuccessReleaseCarriesFullIdentity, ref total, ref passed, ref failed);
            RunTest("CEP23 CrossSession Generations", CollisionExecutionPortTests.Test_CEP23_SameNumericGenerationsAcrossSessionsStayDistinguishable, ref total, ref passed, ref failed);
            RunTest("CEP24 Real Gate Foreign Session", CollisionExecutionPortTests.Test_CEP24_RealIdentityGateDeniesForeignSessionCommand, ref total, ref passed, ref failed);
            RunTest("CEP25 Stale Receipt Dual Naming", CollisionExecutionPortTests.Test_CEP25_StaleReceiptRejectionNamesBothReceipts, ref total, ref passed, ref failed);

            RunTest("CSC01 IdenticalProjections", CollisionShadowComparatorTests.Test_CSC01_IdenticalProjectionsAreInBoth, ref total, ref passed, ref failed);
            RunTest("CSC02 HostAddedCoverage", CollisionShadowComparatorTests.Test_CSC02_HostAddedCoverageIsExpected, ref total, ref passed, ref failed);
            RunTest("CSC03 GuestMissingForbidden", CollisionShadowComparatorTests.Test_CSC03_AuthorizedGuestMissingRegionIsForbidden, ref total, ref passed, ref failed);
            RunTest("CSC04 PendingGuestExpected", CollisionShadowComparatorTests.Test_CSC04_PendingGuestCoverageExitIsExpected, ref total, ref passed, ref failed);
            RunTest("CSC05 LifecycleGapExpected", CollisionShadowComparatorTests.Test_CSC05_CanonicalLifecycleGapClosedIsExpected, ref total, ref passed, ref failed);
            RunTest("CSC06 StaticChurnForbidden", CollisionShadowComparatorTests.Test_CSC06_StaticDemandChurnIsForbidden, ref total, ref passed, ref failed);
            RunTest("CSC07 CrossObserverRelease", CollisionShadowComparatorTests.Test_CSC07_CrossObserverReleaseIsForbidden, ref total, ref passed, ref failed);
            RunTest("CSC08 UnattributedDemand", CollisionShadowComparatorTests.Test_CSC08_UnattributedDemandIsForbidden, ref total, ref passed, ref failed);
            RunTest("CSC09 OutOfBoundsRegion", CollisionShadowComparatorTests.Test_CSC09_OutOfBoundsRegionKeyIsForbidden, ref total, ref passed, ref failed);
            RunTest("CSC10 ForeignDomain", CollisionShadowComparatorTests.Test_CSC10_ForeignDomainFailsClosed, ref total, ref passed, ref failed);
            RunTest("CSC11 AggregateKeyAttribution", CollisionShadowComparatorTests.Test_CSC11_DifferencesCarryAggregateKeyAndAttribution, ref total, ref passed, ref failed);
            RunTest("CSC12 HostAndPendingGuest", CollisionShadowComparatorTests.Test_CSC12_CollisionPolicyIncludesHostAndDefersIneligibleGuest, ref total, ref passed, ref failed);
            RunTest("CSC13 DepartedObserverExpected", CollisionShadowComparatorTests.Test_CSC13_DepartedObserverCoverageExitIsExpected, ref total, ref passed, ref failed);
            RunTest("CSC14 UnexplainedLegacyForbidden", CollisionShadowComparatorTests.Test_CSC14_UnexplainedLegacyCoverageIsForbidden, ref total, ref passed, ref failed);
            RunTest("CSC15 DeferredNotDeparture", CollisionShadowComparatorTests.Test_CSC15_DeferredSampleIsNotDeparture, ref total, ref passed, ref failed);
            RunTest("CSC16 DeferredRetentionInBoth", CollisionShadowComparatorTests.Test_CSC16_DeferredRetentionStaysInBoth, ref total, ref passed, ref failed);
            RunTest("CSC17 DeferredClaimantExplains", CollisionShadowComparatorTests.Test_CSC17_DeferredClaimantExplainsRetainedRegion, ref total, ref passed, ref failed);

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
            // 票 09-R1：共享引擎诊断出口的领域路由接线门禁。
            RunTest("Diagnostics Wiring Routes By Domain", ResourceProductionControlStaticILContractTests.Test_ProductionDiagnosticsWiringRoutesByDomain,
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

            // 票 05：Collision 声明式 Demand Policy 与只读影子对照的结构门禁。
            RunTest("Collision Shadow StaticIL", CollisionShadowStaticILContractTests.Test_All,
                ref total, ref passed, ref failed);
            RunTest("Collision Policy Declaration", CollisionShadowStaticILContractTests.Test_CollisionDeclaresItsOwnDemandPolicy,
                ref total, ref passed, ref failed);
            RunTest("Collision Presence Eligibility", CollisionShadowStaticILContractTests.Test_CollisionPolicyReusesWorldPresenceEligibility,
                ref total, ref passed, ref failed);
            RunTest("Collision Shadow PureMemory", CollisionShadowStaticILContractTests.Test_ShadowPathIsPureMemoryWithoutRosterScan,
                ref total, ref passed, ref failed);
            RunTest("Collision Legacy Snapshot ReadOnly", CollisionShadowStaticILContractTests.Test_LegacySnapshotIsReadOnly,
                ref total, ref passed, ref failed);
            RunTest("Collision Shadow No Native Write", CollisionShadowStaticILContractTests.Test_ShadowPathDoesNotWriteNativeState,
                ref total, ref passed, ref failed);
            RunTest("Collision Legacy Writer And Shadow No Production Calls", CollisionShadowStaticILContractTests.Test_LegacyWriterAndShadowHaveNoProductionCalls,
                ref total, ref passed, ref failed);
            RunTest("Collision Shadow No Production Consumer", CollisionShadowStaticILContractTests.Test_ShadowComparisonHasNoProductionConsumer,
                ref total, ref passed, ref failed);
            RunTest("Collision Deferred Demand Domain Isolation", CollisionShadowStaticILContractTests.Test_DeferredDemandStaysInItsDomain,
                ref total, ref passed, ref failed);
            RunTest("Collision Cutover Retires Legacy Harmony", CollisionShadowStaticILContractTests.Test_CutoverRetiresLegacyHarmonyRegistration,
                ref total, ref passed, ref failed);
            // 票 06：Collision Execution Port 与 Acquisition Receipt 结构门禁。
            RunTest("Collision Execution StaticIL", CollisionExecutionStaticILContractTests.Test_All,
                ref total, ref passed, ref failed);
            RunTest("Collision Port Shape", CollisionExecutionStaticILContractTests.Test_PortShapeAndIdentity,
                ref total, ref passed, ref failed);
            RunTest("Collision Port No Roster Scan", CollisionExecutionStaticILContractTests.Test_PortHasNoRosterOrNativeScan,
                ref total, ref passed, ref failed);
            RunTest("Collision Receipt Domain", CollisionExecutionStaticILContractTests.Test_ReceiptAndOverrideDomain,
                ref total, ref passed, ref failed);
            RunTest("Collision No Production Registration", CollisionExecutionStaticILContractTests.Test_NoProductionRegistration,
                ref total, ref passed, ref failed);
            // 0.2.4.10 C-P2-Item-01: Item Execution Port 与 Demand Policy 结构门禁
            RunTest("Item Execution StaticIL", ItemExecutionStaticILContractTests.Test_All,
                ref total, ref passed, ref failed);
            RunTest("GBA05 Guest Binding Admission Wiring", ItemExecutionStaticILContractTests.Test_GuestBindingAdmissionWiring,
                ref total, ref passed, ref failed);
            RunTest("GBA06 Native Runner Protocol Rejects Invalid Evidence", RegisteredTestExecution.Test_ChildProtocolRejectsInvalidEvidence,
                ref total, ref passed, ref failed);
            RunTest("GBA07 Native Runner Pins Routes To Current Module", RegisteredTestExecution.Test_RouteRejectsSameNameFromOtherModule,
                ref total, ref passed, ref failed);
            RunTest("GBA08 Settlement Wait Retains Delivery Until Admission", ItemPreparationAndFirstDemandTests.Test_GBA08_SettlementWaitRetainsDeliveryUntilAdmission,
                ref total, ref passed, ref failed);
            RunTest("NIL02 FindClient Native Null Operators", ItemExecutionStaticILContractTests.Test_NIL02_LookupDoesNotCallNativeNullOperators,
                ref total, ref passed, ref failed);
            RunTest("NIL03 Bind Unbind Share Lookup", ItemExecutionStaticILContractTests.Test_NIL03_BindAndUnbindShareLookupWithoutCatch,
                ref total, ref passed, ref failed);
            RunTest("Item Port Shape", ItemExecutionStaticILContractTests.Test_PortShapeAndIdentity,
                ref total, ref passed, ref failed);
            RunTest("Item Demand Policy StaticIL", ItemExecutionStaticILContractTests.Test_DemandPolicyDeclaration,
                ref total, ref passed, ref failed);
            RunTest("Item Port No Roster Scan", ItemExecutionStaticILContractTests.Test_PortHasNoRosterOrSpatialIndexScan,
                ref total, ref passed, ref failed);
            RunTest("Item Preparation Authority Exists", ItemExecutionStaticILContractTests.Test_PreparationAuthorityAndMetadataStoreExist,
                ref total, ref passed, ref failed);
            RunTest("Item Receipt Domain StaticIL", ItemExecutionStaticILContractTests.Test_ReceiptDomain,
                ref total, ref passed, ref failed);
            RunTest("Item Production Coordinator Wiring", ItemExecutionStaticILContractTests.Test_ProductionCoordinatorWiresItemDemandAndLifecycle,
                ref total, ref passed, ref failed);
            RunTest("Item Native Action And Gate", ItemExecutionStaticILContractTests.Test_NativeActionInvocationsAndPreparationGate,
                ref total, ref passed, ref failed);
            RunTest("Item Demand Cycle Destroy Binding", ItemExecutionStaticILContractTests.Test_DemandCycleRemovalInvokesNativeDestroy,
                ref total, ref passed, ref failed);
            RunTest("Item Demand Cycle Native Binding", ItemExecutionStaticILContractTests.Test_DemandCycleBindsNativeRespawnAndSuppressesLegacyWriter,
                ref total, ref passed, ref failed);
            RunTest("Item Coordinator Native Actions", ItemExecutionStaticILContractTests.Test_CoordinatorCallsNativeBridgeAndPreparationChain,
                ref total, ref passed, ref failed);
            RunTest("Item Settlement Consumption Gate", ItemExecutionStaticILContractTests.Test_SettlementConsumptionBindsPickupIncrementAndHost,
                ref total, ref passed, ref failed);
            RunTest("Item Pickup Distance Binding", ItemPickupDistanceStaticILContractTests.Test_PickupDistanceConsumesReceiveTakeItemRequest,
                ref total, ref passed, ref failed);
            RunTest("Item Host Step5 Settlement Gate", ItemExecutionStaticILContractTests.Test_HostStepFiveGenerationUsesSettlementGate,
                ref total, ref passed, ref failed);
            RunTest("Item Step5 Destroy Gates", ItemExecutionStaticILContractTests.Test_StepFiveAndDestroyConsumeGatesAreBound,
                ref total, ref passed, ref failed);
            RunTest("Item Region Prefix Finalizer Registration", ItemExecutionStaticILContractTests.Test_RegionPrefixRegistrationIncludesOrderedFinalizer,
                ref total, ref passed, ref failed);
            RunTest("Item Region Prefix Legacy Exception Propagation", ItemExecutionStaticILContractTests.Test_RegionPrefixLegacyCallPropagatesExceptions,
                ref total, ref passed, ref failed);
            RunTest("Item Destroy Wrapper Mode-First Order", ItemExecutionStaticILContractTests.Test_DestroyWrapperReadsModeBeforeOwnedState,
                ref total, ref passed, ref failed);
            RunTest("Item Owned Pending Transpiler Chain", ItemExecutionStaticILContractTests.Test_OwnedPendingTranspilerBindsAfterDedicatedGateChain,
                ref total, ref passed, ref failed);
            RunTest("Item Settlement Registration PatchInfo", ItemExecutionStaticILContractTests.Test_SettlementRegistrationVerifiedThroughPatchInfo,
                ref total, ref passed, ref failed);

            _currentEvidenceClass = EvidenceClass.BuildArtifact;
            Console.WriteLine("\n=== Evidence Class: BuildArtifact ===");
            Console.WriteLine("  Self-reported Fingerprint: " + BuildFingerprint.Capture(typeof(SteamP2PFriendsPlugin).Assembly).ToLogString());
            RunTest("BuildArtifact Fingerprint Shape", BuildArtifactEvidenceTests.Test_All,
                ref total, ref passed, ref failed);
            RunTest("BuildArtifact Slice Identity", BuildArtifactEvidenceTests.Test_SliceIdentityIsPinnedToMigrationStage,
                ref total, ref passed, ref failed);
            RunTest("BuildArtifact Candidate Role", BuildArtifactEvidenceTests.Test_ShadowCandidateRoleIsDistinctFromCutoverCandidate,
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
            Console.WriteLine("  Bug3 自动化范围：状态机/负控/IL 契约/生产登记；真实 MENU→GAME 生命周期与 UI flags 仍需 SDK/真机");

            RunTest("Bug3-01 Run-10 Order Defer Then Authorize Once", () => { P2PGroupStateRecoveryRuntimeTests.Test_Run10Order_DefersThenAuthorizesExactlyOnce(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-02 Correct Order Pass Through Once", () => { P2PGroupStateRecoveryRuntimeTests.Test_CorrectOrder_PassThroughOnce(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-03 Second Session No State Reuse", () => { P2PGroupStateRecoveryRuntimeTests.Test_SecondSessionDifferentMap_NoStateReuse(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-04 Stale Map Mismatch Discarded", () => { P2PGroupStateRecoveryRuntimeTests.Test_StalePendingMapMismatch_DiscardedNotRestored(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-05 Disconnect During Pending No Restore", () => { P2PGroupStateRecoveryRuntimeTests.Test_DisconnectDuringPending_NoRestore(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-06 Destroyed Instance No Restore", () => { P2PGroupStateRecoveryRuntimeTests.Test_DestroyedInstance_NoRestore(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-07 Stale Callback After New Session", () => { P2PGroupStateRecoveryRuntimeTests.Test_StaleCallbackAfterNewSession_NoRestore(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-08 Eligibility Guest SP Dedicated", () => { P2PGroupStateRecoveryRuntimeTests.Test_EligibilityNegatives_GuestSingleplayerDedicated(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-09 Eligibility Local Host Identity", () => { P2PGroupStateRecoveryRuntimeTests.Test_EligibilityNegatives_LocalHostIdentity(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-10 Deferred Load IL Contract", () => { P2PGroupStateRecoveryRuntimeTests.Test_DeferredLoad_ExecutesNativeLoadOnceAndAvoidsForbiddenApis(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-11 Production Registration IL Chain", () => { P2PGroupStateRecoveryRuntimeTests.Test_ProductionRegistration_ReachedFromParentEntry(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-12 RegisterManual Live Patch Info", () => { P2PGroupStateRecoveryRuntimeTests.Test_RegisterManual_AppliesOwnerOriginalAndPatchMethod(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-13 Prefix Defers Through Real Glue (M-A gate)", () => { P2PGroupStateRecoveryRuntimeTests.Test_Prefix_DefersThroughRealGlue(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-14 Prefix Pass-Through Negative Controls", () => { P2PGroupStateRecoveryRuntimeTests.Test_Prefix_PassThroughForNegativeControls(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-15 Prefix Failure Rolls Back Pending (E3)", () => { P2PGroupStateRecoveryRuntimeTests.Test_PrefixFailure_RollsBackPending(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-16 Second Deferral Forces Native Pass (E1)", () => { P2PGroupStateRecoveryRuntimeTests.Test_SecondDeferral_ForcesNativePassAndRecovers(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-17 Second Deferral Leaves No Pending (E1)", () => { P2PGroupStateRecoveryRuntimeTests.Test_SecondDeferral_LeavesNoPending(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-18 Flags Notify Once After Load Success (E4)", () => { P2PGroupStateRecoveryRuntimeTests.Test_FlagsNotify_OnceAfterRealLoadSuccess(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-19 Flags Notify Skipped When Empty (E4)", () => { P2PGroupStateRecoveryRuntimeTests.Test_FlagsNotify_SkippedWhenPayloadEmpty(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-20 Flags Notify Degraded After Rollback (E4)", () => { P2PGroupStateRecoveryRuntimeTests.Test_FlagsNotify_DegradedAfterRollback(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-21 Flags Notify Second Session New Token (E4)", () => { P2PGroupStateRecoveryRuntimeTests.Test_FlagsNotify_SecondSessionNewTokenNotifiesAgain(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-22 Flags Notify Invariant Before Load Success (E4)", () => { P2PGroupStateRecoveryRuntimeTests.Test_FlagsNotify_InvariantWhenNotifiedBeforeLoadSuccess(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-23 Flags Notify Rejects Reentrant Load (E4)", () => { P2PGroupStateRecoveryRuntimeTests.Test_FlagsNotify_RejectsReentrantLoad(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-24 Flags Notify IL Contract (E4)", () => { P2PGroupStateRecoveryRuntimeTests.Test_FlagsNotify_DoesNotReloadQuestState(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-25 Registration Ready And Idempotent (E5)", () => { P2PGroupStateRecoveryRuntimeTests.Test_Registration_ReadyAndIdempotent(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-26 Registration Hot Reload Refused (E5)", () => { P2PGroupStateRecoveryRuntimeTests.Test_Registration_HotReloadDifferentIdRefused(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-27 Candidate DLL Hash Recorded (E6)", () => { P2PGroupStateRecoveryRuntimeTests.Test_CandidateDllHashRecorded(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-28 Flags Notify Old Token After Session Reset (E4)", () => { P2PGroupStateRecoveryRuntimeTests.Test_FlagsNotify_OldTokenAfterSessionReset_NoNotify(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-29 Boundary Menu Resets Pending (F2)", () => { P2PGroupStateRecoveryRuntimeTests.Test_BoundaryMenu_ResetsPendingAndEpoch(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-30 Boundary Shutdown Resets Pending (F2)", () => { P2PGroupStateRecoveryRuntimeTests.Test_BoundaryShutdown_ResetsPending(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-31 After Boundary Same Map Defers Again (F2)", () => { P2PGroupStateRecoveryRuntimeTests.Test_AfterBoundary_SameMap_DefersAgain(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-32 Save Deadline Forces Deferred Load Once (F1)", () => { P2PGroupStateRecoveryRuntimeTests.Test_SaveDeadline_ForcesDeferredLoadOnce(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-33 Flags Notify Stops At First Throwing Subscriber (t22)", () => { P2PGroupStateRecoveryRuntimeTests.Test_FlagsNotify_StopsAtFirstThrowingSubscriber(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-34 Eligibility Facts Reject Three Identity Violations (F7)", () => { P2PGroupStateRecoveryRuntimeTests.Test_Eligibility_FactsRejectThreeIdentityViolations(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-35 Publish No Group Does Not Change Rank (F7)", () => { P2PGroupStateRecoveryRuntimeTests.Test_Publish_NoGroup_DoesNotChangeRank(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-36 Invariant Violation Dedicated Ledger Field (F8)", () => { P2PGroupStateRecoveryRuntimeTests.Test_InvariantViolation_LandsInDedicatedLedgerField(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-37 Abandon Pending Empty Counts Noop (F8)", () => { P2PGroupStateRecoveryRuntimeTests.Test_AbandonPending_WhenEmpty_CountsNoop(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-38 Production Notify Wiring Is Judgeable (spec-F2)", () => { P2PGroupStateRecoveryRuntimeTests.Test_ProductionWiring_ExecuteDeferredLoadCallsNotifyFlags(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-39 Load Throws Degrades Without Retry (spec-F3)", () => { P2PGroupStateRecoveryRuntimeTests.Test_LoadThrows_DegradesAndDoesNotRetry(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-40 Reset Session Retires Old Lease (spec-F4)", () => { P2PGroupStateRecoveryRuntimeTests.Test_ResetSession_RetiresOldLeaseBeforeReset(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-41 Save Blocked Leaves Archive Untouched (t24/F1)", () => { P2PGroupStateRecoveryRuntimeTests.Test_SaveBlocked_LeavesArchiveFileUntouched(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-42 Not Ready Never Authorizes (t24/F2)", () => { P2PGroupStateRecoveryRuntimeTests.Test_NotReady_NeverAuthorizes(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-43 New Lease Completes, Other Archive Untouched (t24/F1)", () => { P2PGroupStateRecoveryRuntimeTests.Test_NewLease_CompletesAndSecondArchiveUntouched(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-44 Reset Session Keeps Dirty Object Barrier (t24/F1)", () => { P2PGroupStateRecoveryRuntimeTests.Test_ResetSession_KeepsDirtyObjectBarrier(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-45 Notify Throws Does Not Block Save (t22)", () => { P2PGroupStateRecoveryRuntimeTests.Test_NotifyThrows_DoesNotBlockSave(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-46 Null Native Group Table Blocks Save Flush (t24/F2)", () => { P2PGroupStateRecoveryRuntimeTests.Test_NullNativeGroupTable_BlocksSaveDeadlineFlush(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-47 Gate Null PlayerID No Throw Not Eligible (T31)", () => { P2PGroupStateRecoveryRuntimeTests.Test_Gate_NullPlayerIdIsNotEligibleAndDoesNotThrow(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-48 Gate Normal Host Still Defers + 12 Flags (T31)", () => { P2PGroupStateRecoveryRuntimeTests.Test_Gate_NormalHostStillDefers(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-49 SDK Operator On Null Throws (characterization, T31)", () => { P2PGroupStateRecoveryRuntimeTests.Test_Gate_SdkOperatorOnNullThrowsCharacterization(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug3-50 Production BuildEligibility Real Mapping (T32)", () => { P2PGroupStateRecoveryRuntimeTests.Test_Gate_ProductionBuildEligibilityRealMapping(); return true; }, ref total, ref passed, ref failed);
            RunTest("T69-01 Persona Null PlayerID Client Skipped (T69)", () => { SteamPersonaDisplayPlayerIdGuardTests.Test_NullPlayerIdClientDoesNotAbortLookup(); return true; }, ref total, ref passed, ref failed);

            Console.WriteLine("  Bug1 自动化范围：判定器纯函数用例（M1/AUTH/UNKNOWN/NO_THRESHOLD）；真实相位采样仍需 1H 实机");
            RunTest("Bug1-01 M1 Stall Catchup Canonical", () => { Bug1ProbeJudgmentTests.Test_M1StallCatchup_SyntheticCanonical(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-02 Auth Step Locked Same Stall", () => { Bug1ProbeJudgmentTests.Test_AuthStepLocked_SameStallDifferentAuthority(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-03 Smooth Segment No Threshold", () => { Bug1ProbeJudgmentTests.Test_SmoothSegment_NoThresholdEvent(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-04 Missing Frame Unknown", () => { Bug1ProbeJudgmentTests.Test_MissingFrame_UnknownWithFieldNames(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-05 Unpaired Sim Unknown", () => { Bug1ProbeJudgmentTests.Test_UnpairedSim_Unknown(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-06 Right Censored Unknown", () => { Bug1ProbeJudgmentTests.Test_RightCensored_Unknown(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-07 Duplicate Camera Unknown", () => { Bug1ProbeJudgmentTests.Test_DuplicateCameraSample_Unknown(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-08 Non Finite Unknown", () => { Bug1ProbeJudgmentTests.Test_NonFiniteSample_Unknown(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-09 Release At Boundary No Event", () => { Bug1ProbeJudgmentTests.Test_ReleaseExactlyAtBoundary_NoThresholdEvent(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-10 Two Small Displacements", () => { Bug1ProbeJudgmentTests.Test_TwoSmallDisplacements_NoStallCandidate(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-11 Authority Moves And Returns Is Not M1 (T71/v2)", () => { Bug1ProbeJudgmentTests.Test_AuthorityMovesAndReturns_IsNotM1(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-12 Yaw Shortest Delta Wrap", () => { Bug1ProbeJudgmentTests.Test_YawShortestDelta_WrapsAcrossZero(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-13 Default Off Register Silent", () => { Bug1ProbeJudgmentTests.Test_DefaultOff_RegisterManualInstallsNothing(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-14 Phase Enqueue Native Seq Depth (T35)", () => { Bug1ProbeRxPhaseTests.Test_PhaseEnqueue_RecordsNativeSeqAndDepth(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-15 Phase Enqueue Unclean Depth Silent (T35)", () => { Bug1ProbeRxPhaseTests.Test_PhaseEnqueue_UncleanDepthProducesNothing(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-16 Phase Enqueue Missing Seq Null (T35)", () => { Bug1ProbeRxPhaseTests.Test_PhaseEnqueue_MissingSeqStaysNull(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-17 Phase Reject Exclusive With Enqueue (T35)", () => { Bug1ProbeRxPhaseTests.Test_PhaseReject_OnlyWhenNoEnqueue(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-18 Phase Consume Links Rx By Packet (T35)", () => { Bug1ProbeRxPhaseTests.Test_PhaseConsume_LinksRxFromDequeuedPacket(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-19 Phase Consume Unknown Seq No Substitution (T35)", () => { Bug1ProbeRxPhaseTests.Test_PhaseConsume_UnknownSeqNoSubstitution(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-20 Manifest Runtime Identity Real (T35)", () => { Bug1ProbeRxPhaseTests.Test_Manifest_RuntimeIdentityIsReal(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-21 Production Wiring Four Phase Edges (T42)", () => { Bug1ProbeRxPhaseTests.Test_ProductionWiring_FourPhaseEdgesAreCalled(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-22 Runtime Seam Both Phases On Target Queue (T42)", () => { Bug1ProbeRxPhaseTests.Test_RuntimeWiring_TargetQueueProducesBothPhases(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-23 Rx Table Capacity Eviction Counted (T42)", () => { Bug1ProbeRxPhaseTests.Test_Ledger_CapacityEvictionIsCounted(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-24 Duplicate InputSeq Observable (T42)", () => { Bug1ProbeRxPhaseTests.Test_Ledger_DuplicateInputSeqIsObservable(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-25 Foreign Dequeue Produces Nothing (T45/R7-03)", () => { Bug1ProbeRxPhaseTests.Test_RuntimeWiring_ForeignDequeueProducesNothing(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-26 Phase Coverage Missing Enqueue Forces Unknown (T45)", () => { Bug1ProbeEntryTests.Test_PhaseCoverage_MissingEnqueueForcesUnknown(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-27 Phase Coverage Missing Dequeue Forces Unknown (T45)", () => { Bug1ProbeEntryTests.Test_PhaseCoverage_MissingDequeueForcesUnknown(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-28 Phase Coverage Both Present Keeps Judgment (T45)", () => { Bug1ProbeEntryTests.Test_PhaseCoverage_BothPresentKeepsJudgment(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-29 Phase Coverage Unset Keeps Synthetic Semantics (T45)", () => { Bug1ProbeEntryTests.Test_PhaseCoverage_UnsetKeepsSyntheticSemantics(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-30 Entry Rejects Already Running (T45)", () => { Bug1ProbeEntryTests.Test_Entry_RejectsAlreadyRunning(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-31 Entry Rejects Missing Target And Unresolved Cap (T45)", () => { Bug1ProbeEntryTests.Test_Entry_RejectsMissingTargetAndUnresolvedCap(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-32 Entry RunId And StepId Generated (T45)", () => { Bug1ProbeEntryTests.Test_Entry_RunIdAndStepIdAreGenerated(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-33 Entry Target Selector Matches Name Or SteamId (T45)", () => { Bug1ProbeEntryTests.Test_Entry_TargetSelectorMatchesNameOrSteamId(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-34 Native Speed Cap Sprint Formula (T45)", () => { Bug1ProbeEntryTests.Test_NativeSpeedCap_SprintFormula(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-35 Native Speed Cap Unresolved Refuses (T45)", () => { Bug1ProbeEntryTests.Test_NativeSpeedCap_UnresolvedRefuses(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-36 Null PlayerID Client Skipped Not Thrown (T48/R8-01)", () => { Bug1ProbeEntryRepairTests.Test_R8_01_NullPlayerIdClientIsSkipped(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-37 Selector Excludes Self Detects Ambiguity (T48/R8-01)", () => { Bug1ProbeEntryRepairTests.Test_R8_01_SelectorExcludesSelfAndDetectsAmbiguity(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-38 Production Path Phase Coverage Keeps Judgment (T48/F1)", () => { Bug1ProbeEntryRepairTests.Test_F1_ProductionPath_RecordsPhasesAndKeepsJudgment(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-39 Zero Records Forces Phase Coverage Unknown (T48/F1)", () => { Bug1ProbeEntryRepairTests.Test_F1_ZeroRecordsForcesUnknownPhaseCoverage(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-40 Install Failure Fails Closed Then Retry (T48/R8-02)", () => { Bug1ProbeEntryRepairTests.Test_R8_02_InstallFailureFailsClosedAndRetrySucceeds(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-41 Startup Retry Decision Pure Function (T48/R8-03)", () => { Bug1ProbeEntryRepairTests.Test_R8_03_StartupRetryDecision(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-42 Disable Uninstalls Detours Reopen OK (T48/R8-04)", () => { Bug1ProbeEntryRepairTests.Test_R8_04_DisableUninstallsDetoursAndReopenSucceeds(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-43 Refused Start Leaves No Run Directory (T48/F3)", () => { Bug1ProbeEntryRepairTests.Test_F3_RefusedStartLeavesNoRunDirectory(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-44 Walk And Sprint Caps Distinct (T48/F4)", () => { Bug1ProbeEntryRepairTests.Test_F4_WalkAndSprintCapsAreDistinct(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-45 Enable Failure Removes New Run Directory (T49/F1)", () => { Bug1ProbeEntryRepairTests.Test_T49_F1_EnableFailureRemovesNewRunDirectory(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-46 Retry Accounting Stops At Cap And Unhooks (T51/R9-01)", () => { Bug1ProbeEntryRepairTests.Test_R9_01_RetryAccountingStopsAtCapAndUnhooks(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-47 Retry Wiring Uses Accounting (T51/R9-01)", () => { Bug1ProbeEntryRepairTests.Test_R9_01_RetryWiringUsesAccounting(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-48 Start Exception Aborts To Not Enabled (T51/R9-02)", () => { Bug1ProbeEntryRepairTests.Test_R9_02_StartExceptionAbortsToNotEnabled(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-49 Start Has Exception Guard (T51/R9-02)", () => { Bug1ProbeEntryRepairTests.Test_R9_02_StartHasExceptionGuard(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-50 Start Core Routes Through EnablePreparedRun (T52/F1)", () => { Bug1ProbeEntryRepairTests.Test_T52_F1_StartCoreRoutesThroughEnablePreparedRun(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-51 Retry Outcome Is Adopted By Production (T52/F2,R10-01)", () => { Bug1ProbeEntryRepairTests.Test_T52_F2_RetryOutcomeIsAdopted(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-52 Abort Start Writes Back Config (R10-04)", () => { Bug1ProbeEntryRepairTests.Test_R10_04_AbortStartWritesBackConfig(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-53 EnablePreparedRun Result Is Consumed (R12-01)", () => { Bug1ProbeEntryRepairTests.Test_R12_01_EnablePreparedRunResultIsConsumed(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-54 Retry Outcome Argument Comes From Accounting (R12-02)", () => { Bug1ProbeEntryRepairTests.Test_R12_02_RetryOutcomeArgumentComesFromAccounting(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-55 Window Boundary Trims Head And Tail (T60)", () => { Bug1ProbeT60Tests.Test_T60_WindowBoundaryTrimsHeadAndTail(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-56 No Complete Frame Means No Trim (T60)", () => { Bug1ProbeT60Tests.Test_T60_WindowBoundaryDoesNotTrimWhenNoCompleteFrame(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-57 Interior Partial Stays Genuine (T60)", () => { Bug1ProbeT60Tests.Test_T60_InteriorPartialStaysGenuine(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-58 Interpolation Fields And Unavailable (T60)", () => { Bug1ProbeT60Tests.Test_T60_InterpolationComputesLerpAndUnavailable(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-59 Input Association Reasons (T60)", () => { Bug1ProbeT60Tests.Test_T60_InputAssociationReasons(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-60 EndStep Seals Window In Production Path (T60)", () => { Bug1ProbeT60Tests.Test_T60_EndStepSealsWindowInProductionPath(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-61 Real Trace Replay Seals Tail (T60)", () => { Bug1ProbeT60Tests.Test_T60_RealTraceReplaySealsTail(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-62 Session Reset Never Explains Tail (T65/F1)", () => { Bug1ProbeT65Tests.Test_T65_SessionResetNeverExplainsTail(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-62b Session Reset Stays Illegal (T65/F1)", () => { Bug1ProbeT65Tests.Test_T65_SessionResetStaysIllegalEvenWhenCaptureHealthy(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-63 Target Left Trims Tail (T65/F1)", () => { Bug1ProbeT65Tests.Test_T65_TargetLeftWithHealthyCaptureTrimsTail(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-64 Config Changed Does Not Explain Tail (T65/F1)", () => { Bug1ProbeT65Tests.Test_T65_ConfigChangedDoesNotExplainTail(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-65 Boundary Events Close Window (T65/F2)", () => { Bug1ProbeT65Tests.Test_T65_BoundaryEventsCloseWindow(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-66 Window Accounting Identity (T65/F3)", () => { Bug1ProbeT65Tests.Test_T65_WindowAccountingIdentity(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-67 Boundary Hook Subscriptions Exist (T65/F2)", () => { Bug1ProbeT65Tests.Test_T65_BoundaryHookSubscriptionsExist(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-68 Render Only Tail Is Stopped Mid Window (T65/F1)", () => { Bug1ProbeT65Tests.Test_T65_RenderOnlyTailIsStoppedMidWindow(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-69 Fail Closed Hook Never Explains Tail (T65/F1)", () => { Bug1ProbeT65Tests.Test_T65_FailClosedHookNeverExplainsTail(); return true; }, ref total, ref passed, ref failed);

            Console.WriteLine("  Bug1 M1 规则修订 M1-XZ-RELATIVE-v2（T71）：水平投影 + 落后见证 + 回补收敛，三维 span 仅旁证");
            RunTest("Bug1-70 R01 M1 Requires Lag And Convergence (T71)", () => { Bug1ProbeM1RuleTests.Test_R01_M1RequiresLagAndConvergence(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-71 R02 Auth Step Locked Same Step (T71)", () => { Bug1ProbeM1RuleTests.Test_R02_AuthStepLockedWhenRootAndCarrierMoveTogether(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-72 R03 Real Trace Candidate Is Auth Not M1 (T71)", () => { Bug1ProbeM1RuleTests.Test_R03_RealTraceCandidateIsAuthNotM1(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-73 R04 Vertical Only Has No Horizontal Candidate (T71)", () => { Bug1ProbeM1RuleTests.Test_R04_VerticalOnlyMotionHasNoHorizontalCandidate(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-74 R05a Carrier Ahead Is Not M1 (T71)", () => { Bug1ProbeM1RuleTests.Test_R05a_CarrierAheadOfAuthorityIsNotM1(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-75 R05b Release Away From Authority Is Not M1 (T71)", () => { Bug1ProbeM1RuleTests.Test_R05b_ReleaseAwayFromAuthorityIsNotM1(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-76 R06 Two Centimetre Gate Is Grid Based (T71)", () => { Bug1ProbeM1RuleTests.Test_R06_TwoCentimetreGateIsGridBased(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-77 R07 Synchronized Micro Motion Is Not M1 (T71)", () => { Bug1ProbeM1RuleTests.Test_R07_SynchronizedMicroMotionIsNotM1(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-78 R08 Shorter True M1 Not Masked (T71)", () => { Bug1ProbeM1RuleTests.Test_R08_ShorterTrueM1IsNotMaskedByLongerSegment(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-79 R09 Sim Only Motion Cannot Form M1 (T71)", () => { Bug1ProbeM1RuleTests.Test_R09_SimOnlyMotionCannotFormM1(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-80 R10 Unknown Precedence Over No Threshold (T71)", () => { Bug1ProbeM1RuleTests.Test_R10_UnknownPrecedenceOverNoThreshold(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-81 R11 Non Horizontal Gates Still Apply (T71)", () => { Bug1ProbeM1RuleTests.Test_R11_NonHorizontalGatesStillApply(); return true; }, ref total, ref passed, ref failed);

            // t85：G 门实验探针（默认关闭）里可离线判定的部分；真实蒙皮位移只能 1H+1G 实测。
            RunTest("Bug1-82 Gate Config Defaults Off And 0.10U (T85)", () => { Bug1GateProbeTests.Test_ConfigDefaultsAreOffAndTenCentimetres(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-83 Gate Target Selection Reuses Selector (T85)", () => { Bug1GateProbeTests.Test_TargetSelectionReusesSelectorSemantics(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-84 Gate Reason Code Mapping (T85)", () => { Bug1GateProbeTests.Test_ReasonCodeMapping(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-85 Gate Json Contract Fields (T85)", () => { Bug1GateProbeTests.Test_JsonContainsAllContractFields(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-86 Gate Displacement Quantiles (T85)", () => { Bug1GateProbeTests.Test_SummaryQuantiles(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-87 Gate Restore Bit Equality (T85)", () => { Bug1GateProbeTests.Test_BitEqualIsBitLevel(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-88 Gate Offline Cannot Pass (T85)", () => { Bug1GateProbeTests.Test_OfflineCannotProducePass(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-89 Gate Impure Component List Is Diagnosable (T85)", () => { Bug1GateProbeTests.Test_ImpureComponentListIsDiagnosable(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-90 Gate Whitelist Render Component Types (T90/R16-01)", () => { Bug1GateProbeTests.Test_WhitelistRenderComponentTypes(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-91 Gate Per Renderer Max Residual Decides (T90/T86-F1)", () => { Bug1GateProbeTests.Test_PerRendererMaxResidualDecides(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-92 Gate Snapshot Comparisons Are Real (T90/T86-F2)", () => { Bug1GateProbeTests.Test_SnapshotComparisonsAreReal(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-93 Gate Sample Indices Respect Hard Cap (T90/R16-05)", () => { Bug1GateProbeTests.Test_SampleIndicesRespectHardCap(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-94 Gate Profile Hash Changes With Model (T90/T86-F5)", () => { Bug1GateProbeTests.Test_ProfileHashChangesWithModel(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-95 Gate Production Wiring Uses Logic Comparisons (T90/T86-F2)", () => { Bug1GateProbeTests.Test_ProductionWiringUsesLogicComparisons(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-96 Gate Transform Edges Are Gated Per Edge (T92-F1)", () => { Bug1GateProbeTests.Test_T92_TransformEdgesAreGatedPerEdge(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-97 Gate Transform Edge Comparisons Are Wired (T92-F1)", () => { Bug1GateProbeTests.Test_T92_TransformEdgeComparisonsAreWired(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-98 Gate Pose Plan Covers Subtree Bones Collider Chain (T95/F7)", () => { Bug1GateProbeTests.Test_T95_PoseNodePlanCoversSubtreeBonesAndColliderChain(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-99 Gate Pose Comparison Is Bit Level (T95/F7)", () => { Bug1GateProbeTests.Test_T95_PoseComparisonIsBitLevel(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-100 Gate Six Cell Classification (T95/F6)", () => { Bug1GateProbeTests.Test_T95_SixCellClassification(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-101 Gate Coverage Evaluator Rules (T95/F6)", () => { Bug1GateProbeTests.Test_T95_CoverageEvaluatorRules(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-102 Gate Production Wiring Uses Pose Coverage (T95)", () => { Bug1GateProbeTests.Test_T95_ProductionWiringUsesPoseCoverage(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-103 Gate Heading Stability Counts Samples (T98/R17-02)", () => { Bug1GateProbeTests.Test_T98_HeadingStabilityCountsSamples(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-104 Gate Pose Walk Is Bounded (T98/R17-01)", () => { Bug1GateProbeTests.Test_T98_PoseWalkIsBounded(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-105 Gate Restore Gate Is Bit Level (T98/R17-03)", () => { Bug1GateProbeTests.Test_T98_RestoreGateIsBitLevel(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-106 Gate Budget Counts Only Valid Samples (T98/R17-04)", () => { Bug1GateProbeTests.Test_T98_BudgetCountsOnlyValidSamples(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-107 Gate Mode Note Is Split (T98/F3)", () => { Bug1GateProbeTests.Test_T98_ModeNoteIsSplit(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-108 Gate Coverage Json Records Stance And Yaw (T98)", () => { Bug1GateProbeTests.Test_T98_CoverageJsonRecordsStanceAndYaw(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-109 Gate Pose Truncation Is Fail Closed (T104/R18-01)", () => { Bug1GateProbeTests.Test_T104_PoseTruncationIsFailClosed(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-110 Gate Total Sample Budget (T104/R18-03)", () => { Bug1GateProbeTests.Test_T104_TotalSampleBudget(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-111 Gate Mode Notes Cannot Mix (T104/R18-05)", () => { Bug1GateProbeTests.Test_T104_ModeNotesCannotMix(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-112 Il Probe Accepts Debug Negation (T104/R18-04)", () => { Bug1GateProbeTests.Test_T104_IlProbeAcceptsDebugNegation(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-113 Retry Budget Is Per Trigger (T110/R21-01)", () => { Bug1GateProbeTests.Test_R21_RetryBudgetIsPerTrigger(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug1-114 Enum Trigger Is Guarded (T114/R21-02)", () => { Bug1GateProbeTests.Test_R21_EnumTriggerIsGuarded(); return true; }, ref total, ref passed, ref failed);

            RunTest("Bug2-01 Anchor Locates Unique SendPlayShoot Call Site (T116)", () => { GunshotHostAudioTests.Test_AnchorLocatesUniqueSendPlayShootCallSite(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-02 Compensation Leaf Invoked Once (T116)", () => { GunshotHostAudioTests.Test_CompensationLeafInvokedOnce(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-03 Negative Projectile Excluded (T116)", () => { GunshotHostAudioTests.Test_Negative_ProjectileExcluded(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-04 Negative Host Shooter Excluded (T116)", () => { GunshotHostAudioTests.Test_Negative_HostShooterExcluded(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-05 Negative Guest Owner Path Excluded (T116)", () => { GunshotHostAudioTests.Test_Negative_GuestOwnerPathExcluded(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-06 Negative Turret Excluded (T116)", () => { GunshotHostAudioTests.Test_Negative_TurretExcluded(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-07 Negative Disabled Does Nothing (T116)", () => { GunshotHostAudioTests.Test_Negative_DisabledDoesNothing(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-08 Negative Anchor Mismatch Fails Closed (T116)", () => { GunshotHostAudioTests.Test_Negative_AnchorMismatchFailsClosed(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-09 Dedup Same Key Twice Only One Leaf (T116)", () => { GunshotHostAudioTests.Test_Dedup_SameKeyTwiceOnlyOneLeaf(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-10 Dedup Multi Pellet Single Fire One Leaf (T116)", () => { GunshotHostAudioTests.Test_Dedup_MultiPelletSingleFireOneLeaf(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-11 Dedup Different Fires Each One Leaf (T116)", () => { GunshotHostAudioTests.Test_Dedup_DifferentFiresEachOneLeaf(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-12 Failed Leaf Not Retried (T116)", () => { GunshotHostAudioTests.Test_FailedLeafNotRetried(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-13 Rng State Restored After Leaf (T116)", () => { GunshotHostAudioTests.Test_RngStateRestoredAfterLeaf(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-14 Ledger State Machine (T116)", () => { GunshotHostAudioTests.Test_LedgerStateMachine(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-15 Prefix Finalizer Stack Association (T116)", () => { GunshotHostAudioTests.Test_PrefixFinalizerStackAssociation(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-16 Gate Line Contains All Facts And Decision (T116)", () => { GunshotHostAudioTests.Test_GateLineContainsAllFactsAndDecision(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-17 Config Default Is Off (T116)", () => { GunshotHostAudioTests.Test_ConfigDefaultIsOff(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-18 Native Leaf Observer Blocks Compensation (T124)", () => { GunshotHostAudioTests.Test_NativeLeafObserverBlocksCompensation(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-19 Production Leaf Resolution (T124)", () => { GunshotHostAudioTests.Test_ProductionLeafResolution(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-20 Hot Enable Disable Cycle (T124)", () => { GunshotHostAudioTests.Test_HotEnableDisableCycle(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-21 Aborted After Anchor Marked (T124)", () => { GunshotHostAudioTests.Test_AbortedAfterAnchorMarked(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-22 Ledger Purge By Generation (T124)", () => { GunshotHostAudioTests.Test_LedgerPurgeByGeneration(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-23 Real Fire Transpiler Insertion (T124)", () => { GunshotHostAudioTests.Test_RealFireTranspilerInsertion(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-24 Real Harmony Failure Leaves No Patch (T127)", () => { GunshotHostAudioTests.Test_RealHarmonyFailureLeavesNoPatch(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-25 Leaf Resolver Null Fails Closed (T127)", () => { GunshotHostAudioTests.Test_LeafResolverNullFailsClosed(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-26 Build Facts From State All Gates (T127)", () => { GunshotHostAudioTests.Test_BuildFactsFromStateAllGates(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-27 Ledger Queue Rebuilt After Purge (T127)", () => { GunshotHostAudioTests.Test_LedgerQueueRebuiltAfterPurge(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-28 Anchor Plus One Not Branch Target (T127)", () => { GunshotHostAudioTests.Test_AnchorPlusOneNotBranchTarget(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-29 Supplemental Leaf Depth Guard (T127)", () => { GunshotHostAudioTests.Test_SupplementalLeafDepthGuard(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-30 Initialize Returns Semantic Bool (T129)", () => { GunshotHostAudioTests.Test_InitializeReturnsSemanticBool(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-31 Postfix Verify Rejection Fails Closed (T129)", () => { GunshotHostAudioTests.Test_PostfixVerifierRejectionFailsClosed(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-32 Collect Wiring From Sdk Sources (T129)", () => { GunshotHostAudioTests.Test_CollectWiringFromSdkSources(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-33 Diagnostics Stage Propagates Module Failure (T129)", () => { GunshotHostAudioTests.Test_DiagnosticsStagePropagatesModuleFailure(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-34 Diagnostics Stage Adopts Module Result (T133)", () => { GunshotHostAudioTests.Test_DiagnosticsStageAdoptsModuleResult(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-35 Audio Source Reflection Contract (T133)", () => { GunshotHostAudioTests.Test_AudioSourceReflectionContract(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-36 AudioSourcePresent Requires Resolved Source (T136)", () => { GunshotHostAudioTests.Test_AudioSourcePresentRequiresResolvedSource(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-37 SessionEpoch Fallback Observable (T136)", () => { GunshotHostAudioTests.Test_SessionEpochFallbackObservable(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-38 Diagnostic Stage Production Mode Sections (T140)", () => { GunshotHostAudioTests.Test_DiagnosticStageProductionModeSections(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-39 Diagnostic Stage Production Branch Enters All Sections (T144)", () => { GunshotHostAudioTests.Test_DiagnosticStageProductionBranchEntersAllSections(); return true; }, ref total, ref passed, ref failed);
            RunTest("Bug2-40 Diagnostic Stage Null Seam Guard (T155)", () => { GunshotHostAudioTests.Test_DiagnosticStageNullSeamGuard(); return true; }, ref total, ref passed, ref failed);
            RunTest("T145-01 Main Config Defaults And Keys (T145)", () => { DiagnosticConfigConsolidationTests.Test_MainConfigDefaultsAndKeys(); return true; }, ref total, ref passed, ref failed);
            RunTest("T145-02 Startup Config Line Format (T145)", () => { DiagnosticConfigConsolidationTests.Test_StartupConfigLineFormat(); return true; }, ref total, ref passed, ref failed);
            RunTest("T145-03 Modules Use Main Config Only (T145)", () => { DiagnosticConfigConsolidationTests.Test_ModulesDoNotCreateIndependentConfigFiles(); return true; }, ref total, ref passed, ref failed);
            RunTest("T145-04 Gunshot Hot Toggle From Main Config (T145)", () => { DiagnosticConfigConsolidationTests.Test_GunshotHotToggleFromMainConfig(); return true; }, ref total, ref passed, ref failed);
            RunTest("T145-05 Startup Line Truth Table (T153)", () => { DiagnosticConfigConsolidationTests.Test_FormatLineTruthTable(); return true; }, ref total, ref passed, ref failed);
            RunTest("T145-06 Startup Line Entry Wiring (T153)", () => { DiagnosticConfigConsolidationTests.Test_FormatLineWiring(); return true; }, ref total, ref passed, ref failed);
            RunTest("T145-07 Diagnostics Switches Follow Entries (T157)", () => { DiagnosticConfigConsolidationTests.Test_DiagnosticsSwitchesFollowEntries(); return true; }, ref total, ref passed, ref failed);

            Console.WriteLine("\n===============================================================");
            Console.WriteLine($"=== Final Result: {passed}/{total} PASS (Failed: {failed}) ===");
            bool dispatchComplete = _registeredExecution.ValidateCompleteness();
            Console.WriteLine("=== native runtime routes: NIL01=" + _registeredExecution.LookupCount +
                "; GBA01=" + _registeredExecution.BindingCount + (_registeredExecution.Configured
                    ? " (each required exactly once) ===" : " (not configured) ==="));
            Console.WriteLine("===============================================================");
            return failed == 0 && dispatchComplete ? 0 : 1;
        }

        private static void InitializeRunLedger()
        {
            _registeredExecution = new RegisteredTestExecution();
            _registeredExecution.BeginRun(typeof(Program).Assembly);
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

                AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
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
            int ordinal = total;
            DateTimeOffset started = DateTimeOffset.Now;
            var result = new RegisteredTestExecution.Result();
            try
            {
                if (_registeredExecution.Execute(test, out result))
                {
                    passed++;
                    result.Outcome = "PASS";
                    Console.WriteLine("  PASS [" + _currentEvidenceClass + "] " + name);
                }
                else
                {
                    failed++;
                    result.Outcome = "FAIL";
                    Console.WriteLine("  FAIL [" + _currentEvidenceClass + "] " + name);
                }
            }
            catch (Exception ex)
            {
                failed++;
                result.Detail = ex.ToString();
                Console.WriteLine("  FAIL [" + _currentEvidenceClass + "] " + name + ": " + ex);
            }
            finally
            {
                _registeredExecution.AppendResult(ordinal, _currentEvidenceClass, name, test,
                    result, started, DateTimeOffset.Now);
            }
        }
    }
}
