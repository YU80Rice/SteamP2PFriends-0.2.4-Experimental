using SteamP2PFriends.Adapters.Resource;
using SteamP2PFriends.Core.Identity;
using System;

namespace SteamP2PFriends.WhitelistTests
{
    internal static class ResourceObservabilityTests
    {
        internal static bool Test_M6O01_FormatsRequiredResourceFields()
        {
            string line = ResourceObservability.Format(
                "[Host]",
                "LeaseAcquire",
                new RegionKey(3, 4).ToString(),
                7UL,
                11UL,
                13U,
                "SPI",
                true,
                "success",
                "demand=2");

            return ContainsAll(line,
                "caseId=",
                "role=Host",
                "domain=Resource",
                "region=(3,4)",
                "sessionEpoch=7",
                "connectionGeneration=11",
                "regionGeneration=13",
                "path=SPI",
                "spiActive=true",
                "outcome=success",
                "demand=2");
        }

        internal static bool Test_M6O02_FallbackAndSkippedAreExplicit()
        {
            string line = ResourceObservability.Format(
                "[Shared]",
                "ResourceUnavailable",
                "-",
                0UL,
                0UL,
                0U,
                "Fallback",
                false,
                "skipped",
                "reason=spi-not-active");

            return ContainsAll(line,
                "role=Shared",
                "domain=Resource",
                "region=-",
                "sessionEpoch=0",
                "connectionGeneration=0",
                "regionGeneration=0",
                "path=Fallback",
                "spiActive=false",
                "outcome=skipped",
                "reason=spi-not-active");
        }

        internal static bool Test_M6O03_NativeReceiveDoesNotInventAcceptance()
        {
            var ledger = new ResourceSnapshotReplicationLedger();
            ledger.ResetSession(9UL);
            ResourceDeltaReceiveObservation observation = ledger.RecordNativeDeltaReceive(
                new RegionKey(2, 3), 7UL);
            return !observation.DecisionAvailable
                && observation.SessionEpoch == 9UL
                && observation.ConnectionGeneration == 7UL
                && observation.DeltaSequence == 1U;
        }

        private static bool ContainsAll(string value, params string[] expected)
        {
            foreach (string item in expected)
            {
                if (value.IndexOf(item, StringComparison.Ordinal) < 0) return false;
            }

            return true;
        }
    }
}
