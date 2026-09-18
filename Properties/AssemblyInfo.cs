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
// 候选角色进程序集元数据：影子候选与正式切换候选因此有不同产物身份（SHA-256 / MVID），
// 两类候选的验收日志不得混用。
[assembly: AssemblyMetadata("SteamP2PFriendsCandidateRole", BuildMetadata.CandidateRole)]
// 仅授权纯单元测试程序集访问内部实现；测试不启动 Unturned 或 Steam API。
[assembly: InternalsVisibleTo("SteamP2PFriends.WhitelistTests")]
