using System.Linq;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// PureMemory 级别只验证分类目录本身，不把分类存在升级为 StaticIL、BuildArtifact 或 Runtime 证据。
    /// </summary>
    internal static class EvidenceClassCatalogTests
    {
        internal static bool Test_All()
        {
            return EvidenceClassCatalog.IsCompleteCatalog() &&
                EvidenceClassCatalog.Definitions.Single(definition => definition.EvidenceClass == EvidenceClass.PureMemory).RelativeRoot == "Evidence/PureMemory" &&
                EvidenceClassCatalog.Definitions.Single(definition => definition.EvidenceClass == EvidenceClass.StaticIL).RelativeRoot == "Evidence/StaticIL" &&
                EvidenceClassCatalog.Definitions.Single(definition => definition.EvidenceClass == EvidenceClass.BuildArtifact).RelativeRoot == "Evidence/BuildArtifact" &&
                EvidenceClassCatalog.Definitions.Single(definition => definition.EvidenceClass == EvidenceClass.Runtime).RelativeRoot == "Evidence/Runtime";
        }
    }
}
