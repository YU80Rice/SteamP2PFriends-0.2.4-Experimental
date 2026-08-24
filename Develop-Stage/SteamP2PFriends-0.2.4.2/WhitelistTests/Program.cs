using System;

namespace SteamP2PFriends.WhitelistTests
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            Console.WriteLine("=== SteamP2PFriends Route B regression tests ===");
            int total = 0, passed = 0, failed = 0;

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
            RunTest("B11 AuthoritativeGates", RouteBApprovalTests.Test_B11_PendingActionAndCommandGatesAreAuthoritative, ref total, ref passed, ref failed);
            RunTest("B12 InputSanitizer", RouteBApprovalTests.Test_B12_InputSanitizerPreservesNetworkProgress, ref total, ref passed, ref failed);
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

            RunTest("HC1 Observer", HarmonyCompatibilityAuditTests.Test_ObserverPatch_IsRecordedWithoutBlocking, ref total, ref passed, ref failed);
            RunTest("HC2 ForeignTranspiler", HarmonyCompatibilityAuditTests.Test_ForeignTranspiler_OnOwnTranspiledTarget_Blocks, ref total, ref passed, ref failed);
            RunTest("HC3 TransportExclusive", HarmonyCompatibilityAuditTests.Test_P2PTransportTargets_RemainExclusive, ref total, ref passed, ref failed);
            RunTest("LOG1 Markers", LoggingPolicyTests.Test_LegacyDiagnosticMarkersAreClassified, ref total, ref passed, ref failed);
            RunTest("LOG2 Defaults", LoggingPolicyTests.Test_VerboseToggleIsAtomicAndDefaultsOff, ref total, ref passed, ref failed);
            RunTest("LOG3 Labels", LoggingPolicyTests.Test_LegacyLabelsAreRemovedAtOutputBoundary, ref total, ref passed, ref failed);
            RunTest("LOG4 Tags", LoggingPolicyTests.Test_InternalDiagnosticTagsAreRemovedFromOperationalText, ref total, ref passed, ref failed);

            RunTest("G1 FirstCommit", AuthorityGenerationGateTests.Test_G1_FirstCommitBlocksSecondProducer, ref total, ref passed, ref failed);
            RunTest("G2 Abort", AuthorityGenerationGateTests.Test_G2_AbortAllowsRetry, ref total, ref passed, ref failed);
            RunTest("G3 Reset", AuthorityGenerationGateTests.Test_G3_ResetInvalidatesOldEpoch, ref total, ref passed, ref failed);
            RunTest("G4 Preparing", AuthorityGenerationGateTests.Test_G4_PreparingRejectsReentry, ref total, ref passed, ref failed);

            RunTest("IUI1 Exact", InventoryUiProjectionTests.Test_IUI1_ExactProjectionNoRepair, ref total, ref passed, ref failed);
            RunTest("IUI2 StaleRendered", InventoryUiProjectionTests.Test_IUI2_StaleRenderedJarDetected, ref total, ref passed, ref failed);
            RunTest("IUI3 StalePending", InventoryUiProjectionTests.Test_IUI3_StalePendingJarDetected, ref total, ref passed, ref failed);
            RunTest("IUI4 Identity", InventoryUiProjectionTests.Test_IUI4_IdentityNotValueEquivalence, ref total, ref passed, ref failed);
            RunTest("IUI5 Reflection", InventoryUiProjectionTests.Test_IUI5_ReflectionContractExact, ref total, ref passed, ref failed);
            RunTest("IUI6 Production", InventoryUiProjectionTests.Test_IUI6_ProductionPostfixesActivate, ref total, ref passed, ref failed);

            RunTest("RC1 AnimationRestore", RemoteCollisionAnimationPolicyTests.Test_RC1_CullingPolicyIsSavedAndRestored, ref total, ref passed, ref failed);
            RunTest("RC2 PolicyBeforeActivation", RemoteCollisionAnimationPolicyTests.Test_RC2_CullingPolicyPrecedesRootActivation, ref total, ref passed, ref failed);

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

            RunTest("M1I01 LocalVanilla", ItemGenerationAuthorityAdapterTests.Test_M1I01_LocalObserverPreservesVanillaWhenAdapterNotReady, ref total, ref passed, ref failed);
            RunTest("M1I02 DedicatedAuthority", ItemGenerationAuthorityAdapterTests.Test_M1I02_DedicatedRemotePreservesFullMapAuthority, ref total, ref passed, ref failed);
            RunTest("M1I03 RegistrationGate", ItemGenerationAuthorityAdapterTests.Test_M1I03_ListenRemoteRequiresRegistration, ref total, ref passed, ref failed);
            RunTest("M1I04 RemoteEligibility", ItemGenerationAuthorityAdapterTests.Test_M1I04_UnrelatedRemoteCannotGenerate, ref total, ref passed, ref failed);
            RunTest("M1I05 PendingObserver", ItemGenerationAuthorityAdapterTests.Test_M1I05_AuthorizationIsNotPartOfWorldPresenceDecision, ref total, ref passed, ref failed);
            RunTest("M1I06 CurrentU3IL", ItemGenerationAuthorityAdapterTests.Test_M1I06_CurrentU3IlMatchesExactlyOneGenerationGate, ref total, ref passed, ref failed);

            RunTest("M2I01 ReliableCommit", ItemObserverReplicationAdapterTests.Test_M2I01_ReliableReturnCommitsExactlyOnce, ref total, ref passed, ref failed);
            RunTest("M2I02 AbortRetry", ItemObserverReplicationAdapterTests.Test_M2I02_ExceptionAbortAllowsRetry, ref total, ref passed, ref failed);
            RunTest("M2I03 RelevanceReentry", ItemObserverReplicationAdapterTests.Test_M2I03_RelevanceExitReentryResendsSameGeneration, ref total, ref passed, ref failed);
            RunTest("M2I04 Reconnect", ItemObserverReplicationAdapterTests.Test_M2I04_ReconnectInvalidatesOldBaselineAndToken, ref total, ref passed, ref failed);
            RunTest("M2I05 OverlappingObservers", ItemObserverReplicationAdapterTests.Test_M2I05_OverlappingObserversHaveIndependentBaselines, ref total, ref passed, ref failed);
            RunTest("M2I06 DifferentRegions", ItemObserverReplicationAdapterTests.Test_M2I06_DifferentRegionsDoNotCrossCommit, ref total, ref passed, ref failed);
            RunTest("M2I07 NewWorldGeneration", ItemObserverReplicationAdapterTests.Test_M2I07_NewWorldGenerationResendsWithoutRelevanceExit, ref total, ref passed, ref failed);
            RunTest("M2I08 ExplicitCapability", ItemObserverReplicationAdapterTests.Test_M2I08_CapabilityIsExplicitReliableEnqueue, ref total, ref passed, ref failed);
            RunTest("M2I09 CurrentU3ReplicationGate", ItemObserverReplicationAdapterTests.Test_M2I09_CurrentU3IlUsesM2ReplicationGate, ref total, ref passed, ref failed);
            RunTest("M2I10 LoadedRollback", ItemObserverReplicationAdapterTests.Test_M2I10_LoadedProjectionRollbackRequiresCurrentConnection, ref total, ref passed, ref failed);
            RunTest("M2I11 ExactDisconnect", ItemObserverReplicationAdapterTests.Test_M2I11_ExactDisconnectInvalidatesEvenReusedConnectionToken, ref total, ref passed, ref failed);

            Console.WriteLine("=== Result: " + passed + "/" + total + " PASS ===");
            return failed == 0 ? 0 : 1;
        }

        private static void RunTest(string name, Func<bool> test, ref int total, ref int passed, ref int failed)
        {
            total++;
            try
            {
                if (test())
                {
                    passed++;
                    Console.WriteLine("PASS " + name);
                }
                else
                {
                    failed++;
                    Console.WriteLine("FAIL " + name);
                }
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine("FAIL " + name + ": " + ex);
            }
        }
    }
}
