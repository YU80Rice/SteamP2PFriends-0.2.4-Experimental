using System.Linq;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// 测试证据的证明边界。不同 Evidence Class 不能互相升级替代。
    /// </summary>
    internal enum EvidenceClass
    {
        PureMemory,
        StaticIL,
        BuildArtifact,
        Runtime
    }

    internal sealed class EvidenceClassDefinition
    {
        internal EvidenceClassDefinition(EvidenceClass evidenceClass, string relativeRoot, string status)
        {
            EvidenceClass = evidenceClass;
            RelativeRoot = relativeRoot;
            Status = status;
        }

        internal EvidenceClass EvidenceClass { get; }
        internal string RelativeRoot { get; }
        internal string Status { get; }
    }

    internal static class EvidenceClassCatalog
    {
        internal static readonly EvidenceClassDefinition[] Definitions =
        {
            new EvidenceClassDefinition(EvidenceClass.PureMemory, "Evidence/PureMemory", "PASS"),
            new EvidenceClassDefinition(EvidenceClass.StaticIL, "Evidence/StaticIL", "PASS"),
            new EvidenceClassDefinition(EvidenceClass.BuildArtifact, "Evidence/BuildArtifact", "PASS"),
            new EvidenceClassDefinition(EvidenceClass.Runtime, "Evidence/Runtime", "PENDING")
        };

        internal static bool IsCompleteCatalog()
        {
            return Definitions.Length == 4 &&
                Definitions.Select(definition => definition.EvidenceClass).Distinct().Count() == 4 &&
                Definitions.Select(definition => definition.RelativeRoot).Distinct().Count() == 4 &&
                Definitions.Count(definition => definition.Status == "PENDING") == 1 &&
                Definitions.Single(definition => definition.EvidenceClass == EvidenceClass.Runtime).Status == "PENDING" &&
                Definitions.Where(definition => definition.EvidenceClass != EvidenceClass.Runtime)
                    .All(definition => definition.Status == "PASS");
        }
    }
}
