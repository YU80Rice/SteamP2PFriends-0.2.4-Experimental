using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("SteamP2PFriends")]
[assembly: AssemblyDescription("SteamP2PFriends v0.2.4.4 Multi-Observer M4 zombie snapshot experiment.")]
[assembly: AssemblyCompany("YU80Rice")]
[assembly: AssemblyProduct("SteamP2PFriends")]
#if DEBUG
[assembly: AssemblyConfiguration("MultiObserver-M4-Experimental")]
#endif
[assembly: AssemblyCopyright("MIT")]
[assembly: ComVisible(false)]
[assembly: Guid("b3c4d5e6-f7a8-9012-3456-789abcdef012")]
[assembly: AssemblyVersion("0.2.4.4")]
[assembly: AssemblyFileVersion("0.2.4.4")]
// 仅授权纯单元测试程序集访问内部实现；测试不启动 Unturned 或 Steam API。
[assembly: InternalsVisibleTo("SteamP2PFriends.WhitelistTests")]
