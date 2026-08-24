using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("SteamP2PFriends")]
[assembly: AssemblyDescription("SteamP2PFriends v0.2.4.2 Multi-Observer M2 item replication experiment.")]
[assembly: AssemblyCompany("YU80Rice")]
[assembly: AssemblyProduct("SteamP2PFriends")]
#if DEBUG
[assembly: AssemblyConfiguration("MultiObserver-M2-Experimental")]
#endif
[assembly: AssemblyCopyright("MIT")]
[assembly: ComVisible(false)]
[assembly: Guid("b3c4d5e6-f7a8-9012-3456-789abcdef012")]
[assembly: AssemblyVersion("0.2.4.2")]
[assembly: AssemblyFileVersion("0.2.4.2")]
// 仅授权纯单元测试程序集访问内部实现；测试不启动 Unturned 或 Steam API。
[assembly: InternalsVisibleTo("SteamP2PFriends.WhitelistTests")]
