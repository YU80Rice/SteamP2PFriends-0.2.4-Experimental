using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SteamP2PFriends.Core.Ownership
{
    internal enum ModuleOwnershipKind
    {
        Core,
        Platform,
        Security,
        DomainAdapter,
        ControlledCrossDomain
    }

    /// <summary>
    /// 可审计的模块归属记录。物理根目录是仓库结构的权威入口；兼容 namespace
    /// 可以在后续批次单独迁移，不得因此产生第二个生产入口。
    /// </summary>
    internal sealed class ModuleOwnershipRecord
    {
        internal ModuleOwnershipRecord(string moduleId, ModuleOwnershipKind kind,
            string physicalRoot, string authorityType, string registrationTrace,
            string crossDomainReason)
        {
            ModuleId = moduleId ?? string.Empty;
            Kind = kind;
            PhysicalRoot = physicalRoot ?? string.Empty;
            AuthorityType = authorityType ?? string.Empty;
            RegistrationTrace = registrationTrace ?? string.Empty;
            CrossDomainReason = crossDomainReason ?? string.Empty;
        }

        public string ModuleId { get; }
        public ModuleOwnershipKind Kind { get; }
        public string PhysicalRoot { get; }
        public string AuthorityType { get; }
        public string RegistrationTrace { get; }
        public string CrossDomainReason { get; }
        public bool IsControlledCrossDomain => Kind == ModuleOwnershipKind.ControlledCrossDomain;
    }

    /// <summary>
    /// Ticket 04 的单一结构归属目录。
    ///
    /// 该目录不执行注册，也不承载任何生产状态；它只把“谁拥有入口、入口在哪、
    /// 是否需要跨领域兼容位置、由哪一个 Registration Trace 覆盖”固定成可测试数据。
    /// </summary>
    internal static class ModuleOwnershipCatalog
    {
        private static readonly IReadOnlyList<ModuleOwnershipRecord> _records =
            new ReadOnlyCollection<ModuleOwnershipRecord>(new List<ModuleOwnershipRecord>
            {
                new ModuleOwnershipRecord("Core.ControlPlane", ModuleOwnershipKind.Core,
                    "Core/ControlPlane", "MultiObserverShadowCoordinator",
                    "U3-REG-05-WorldSyncAndAdapters", string.Empty),
                new ModuleOwnershipRecord("Core.Identity", ModuleOwnershipKind.Core,
                    "Core/Identity", "DomainIds",
                    "U3-REG-05-WorldSyncAndAdapters", string.Empty),
                new ModuleOwnershipRecord("Core.Lifecycle", ModuleOwnershipKind.Core,
                    "Core/Lifecycle", "SteamP2PFriendsPlugin",
                    "U3-REG-07-ClosureAndVerification", string.Empty),
                new ModuleOwnershipRecord("Core.Registration", ModuleOwnershipKind.Core,
                    "Core/Registration", "PatchRegistrationOrchestrator",
                    "U3-REG-07-ClosureAndVerification", string.Empty),
                new ModuleOwnershipRecord("Core.CrossDomainPatches", ModuleOwnershipKind.ControlledCrossDomain,
                    "Core/Patches", "ControlledCrossDomainPatchSet",
                    "U3-REG-02-InternalDiagnostics;U3-REG-03-RouteB;U3-REG-04-AssetAndAudit;U3-REG-05-WorldSyncAndAdapters;U3-REG-06-Probes",
                    "这些补丁跨越 Provider、玩家生命周期、世界同步和诊断观察点，当前无法证明属于单一领域；统一保留在 Core/Patches，且只允许由 Registration Orchestrator 注册。"),
                new ModuleOwnershipRecord("Platform.Client", ModuleOwnershipKind.Platform,
                    "Platform/Client", "P2PJoinManager",
                    "U3-REG-01-Wrapper;U3-REG-03-RouteB", string.Empty),
                new ModuleOwnershipRecord("Platform.Host", ModuleOwnershipKind.Platform,
                    "Platform/Host", "HostManager",
                    "U3-REG-03-RouteB;U3-REG-05-WorldSyncAndAdapters", string.Empty),
                new ModuleOwnershipRecord("Platform.Transport", ModuleOwnershipKind.Platform,
                    "Platform/Transport", "ExplicitDnsDirectIpService",
                    "U3-REG-01-Wrapper", string.Empty),
                new ModuleOwnershipRecord("Platform.UI", ModuleOwnershipKind.Platform,
                    "Platform/UI", "P2PNativeMenuUI",
                    "U3-REG-03-RouteB;U3-REG-05-WorldSyncAndAdapters", string.Empty),
                new ModuleOwnershipRecord("Platform.Diagnostics", ModuleOwnershipKind.Platform,
                    "Platform/Diagnostics", "RoleLogger",
                    "U3-REG-02-InternalDiagnostics;U3-REG-04-AssetAndAudit;U3-REG-06-Probes",
                    string.Empty),
                new ModuleOwnershipRecord("Security", ModuleOwnershipKind.Security,
                    "Security", "P2PApprovalManager",
                    "U3-REG-03-RouteB", string.Empty),
                new ModuleOwnershipRecord("Adapters", ModuleOwnershipKind.DomainAdapter,
                    "Adapters", "RegistrationClosure",
                    "U3-REG-05-WorldSyncAndAdapters", string.Empty)
            });

        public static IReadOnlyList<ModuleOwnershipRecord> Snapshot => _records;

        internal static bool HasUniqueModuleIds()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var authorities = new HashSet<string>(StringComparer.Ordinal);
            foreach (ModuleOwnershipRecord record in _records)
            {
                if (!ids.Add(record.ModuleId) || !authorities.Add(record.ModuleId + "|" + record.AuthorityType))
                    return false;
                if (record.IsControlledCrossDomain && string.IsNullOrEmpty(record.CrossDomainReason))
                    return false;
            }
            return true;
        }

        internal static bool HasSingleRegistrationAuthority()
        {
            int orchestrators = 0;
            int closures = 0;
            foreach (ModuleOwnershipRecord record in _records)
            {
                if (string.Equals(record.AuthorityType, "PatchRegistrationOrchestrator", StringComparison.Ordinal))
                    orchestrators++;
                if (string.Equals(record.AuthorityType, "RegistrationClosure", StringComparison.Ordinal))
                    closures++;
            }
            return orchestrators == 1 && closures == 1;
        }

        internal static bool HasRegistrationTraceCoverage()
        {
            var required = new HashSet<string>(StringComparer.Ordinal)
            {
                "U3-REG-01-Wrapper",
                "U3-REG-02-InternalDiagnostics",
                "U3-REG-03-RouteB",
                "U3-REG-04-AssetAndAudit",
                "U3-REG-05-WorldSyncAndAdapters",
                "U3-REG-06-Probes",
                "U3-REG-07-ClosureAndVerification"
            };

            foreach (ModuleOwnershipRecord record in _records)
            {
                foreach (string trace in record.RegistrationTrace.Split(';'))
                    required.Remove(trace);
            }
            return required.Count == 0;
        }
    }
}
