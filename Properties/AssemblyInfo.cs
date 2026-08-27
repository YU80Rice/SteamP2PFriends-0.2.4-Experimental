using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SteamP2PFriends.Core.Build;

[assembly: AssemblyTitle("SteamP2PFriends")]
[assembly: AssemblyDescription("SteamP2PFriends v" + BuildMetadata.Version + " Multi-Observer M7 barricade and structure replication experiment.")]
[assembly: AssemblyCompany("YU80Rice")]
[assembly: AssemblyProduct("SteamP2PFriends")]
#if DEBUG
[assembly: AssemblyConfiguration("MultiObserver-M7-Experimental")]
#endif
[assembly: AssemblyCopyright("MIT")]
[assembly: ComVisible(false)]
[assembly: Guid("b3c4d5e6-f7a8-9012-3456-789abcdef012")]
[assembly: AssemblyVersion(BuildMetadata.Version)]
[assembly: AssemblyFileVersion(BuildMetadata.Version)]
[assembly: AssemblyInformationalVersion(BuildMetadata.VersionIdentity)]
[assembly: AssemblyMetadata("SteamP2PFriendsVersion", BuildMetadata.Version)]
[assembly: AssemblyMetadata("SteamP2PFriendsReleaseChannel", BuildMetadata.ReleaseChannel)]
[assembly: AssemblyMetadata("SteamP2PFriendsPluginGuid", BuildMetadata.PluginGuid)]
[assembly: AssemblyMetadata("SteamP2PFriendsDefaultCaseId", BuildMetadata.DefaultCaseId)]
// 仅授权纯单元测试程序集访问内部实现；测试不启动 Unturned 或 Steam API。
[assembly: InternalsVisibleTo("SteamP2PFriends.WhitelistTests")]
