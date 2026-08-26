using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using SDG.NetPak;
using SDG.NetTransport;
using SDG.Unturned;
using SteamP2PFriends.Client;
using SteamP2PFriends.Host;
using SteamP2PFriends.MultiObserver;
using SteamP2PFriends.Patches;
using SteamP2PFriends.Shared;
using SteamP2PFriends.UI;
using Steamworks;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
namespace SteamP2PFriends
{
    /// <summary>
    /// SteamP2PFriends 的 BepInEx 入口。
    /// 该插件仅支持 SteamUser P2P listen-host：房主客户端同时承担原版服务端和本地客户端。
    /// 不启动 U3DS，不修改全局 Dedicated Server 判定，也不伪造原版加载完成状态。
    /// </summary>
    [BepInPlugin("com.yu80rice.steamp2pfriends", "SteamP2PFriends", "0.2.4.8")]
    [BepInDependency("com.yu80rice.launchinventorytidy", BepInDependency.DependencyFlags.SoftDependency)]
    public partial class SteamP2PFriendsPlugin : BaseUnityPlugin
    {
        public const string HARMONY_ID = "com.yu80rice.steamp2pfriends";

        public static SteamP2PFriendsPlugin Instance { get; private set; }

        public static ConfigEntry<bool> EnableP2PCoop;
        public static ConfigEntry<string> ServerName;
        public static ConfigEntry<byte> MaxPlayers;
        public static ConfigEntry<EGameMode> LastRoomMode;
        public static ConfigEntry<bool> LastRoomCheats;
        public static ConfigEntry<bool> LastRoomPvp;
        public static ConfigEntry<bool> LastRoomKeepInventory;
        public static ConfigEntry<bool> LastRoomKeepSkills;
        public static ConfigEntry<bool> LastRoomKeepExperience;
        public static ConfigEntry<string> GSLT_Login_Token;
        public static ConfigEntry<bool> VerboseLog;
        public static ConfigEntry<bool> RouteDiagnostics;
        public static ConfigEntry<bool> EnableMultiObserverShadow;

        public static bool DiagnosticBuildValid { get; private set; } = true;

        internal static bool IsP2PEntryReady => EntryReadiness.IsReady(
            DiagnosticBuildValid, Patches.AuthHandshakeJournalPatch.RegistrationValid);

        /// <summary>
        /// 由 Awake 中 RunRedactionSelfTest 设置，VerifyCriticalPatches 聚合到 DiagnosticBuildValid 阻断门。
        /// </summary>
        public static bool RedactionSelfTestPassed { get; private set; }

        public static bool Stage76QuarantineRegistrationValid { get; private set; }
        public static bool Stage78UnifiedRegistrationValid { get; private set; }

        public static bool Stage92SinglePortRegistrationValid { get; private set; }

        public static bool Stage10WorldBroadcastActivationValid { get; private set; } = true;

        private Harmony _harmony;
        private bool _worldStatusActivationFailureLogged;
        private bool _routeBLifecycleRegistrationAttempted;
        private static readonly P2PEntryReadinessGate EntryReadiness = new P2PEntryReadinessGate();

        private void Awake()
        {
            EntryReadiness.Reset();
            DontDestroyOnLoad(this.gameObject);
            this.gameObject.hideFlags = HideFlags.HideAndDontSave;

            Instance = this;

            Core.Config.PluginConfig.Bind(Config);
            EnableP2PCoop = Core.Config.PluginConfig.EnableP2PCoop;
            ServerName = Core.Config.PluginConfig.ServerName;
            MaxPlayers = Core.Config.PluginConfig.MaxPlayers;
            LastRoomMode = Core.Config.PluginConfig.LastRoomMode;
            LastRoomCheats = Core.Config.PluginConfig.LastRoomCheats;
            LastRoomPvp = Core.Config.PluginConfig.LastRoomPvp;
            LastRoomKeepInventory = Core.Config.PluginConfig.LastRoomKeepInventory;
            LastRoomKeepSkills = Core.Config.PluginConfig.LastRoomKeepSkills;
            LastRoomKeepExperience = Core.Config.PluginConfig.LastRoomKeepExperience;
            GSLT_Login_Token = Core.Config.PluginConfig.GSLT_Login_Token;
            VerboseLog = Core.Config.PluginConfig.VerboseLog;
            RouteDiagnostics = Core.Config.PluginConfig.RouteDiagnostics;
            EnableMultiObserverShadow = Core.Config.PluginConfig.EnableMultiObserverShadow;

            RoleLogger.Initialize(Logger, VerboseLog.Value);
            MultiObserverShadowCoordinator.Initialize();

            // Bind configuration and enter Pending here; real subscription is deferred to Update.
            try
            {
                Stage10WorldBroadcastActivationValid = P2PWorldStatusBroadcaster.Initialize(Config);
                RoleLogger.Info("[Shared]",
                    "[WorldBroadcast] configuration bound; activation state=" +
                    P2PWorldStatusBroadcaster.ActivationState);
            }
            catch (System.Exception wbEx)
            {
                Stage10WorldBroadcastActivationValid = false;
                RoleLogger.Error("[Shared]", "[WorldBroadcast] initialize threw: " + wbEx.GetType().Name);
            }

            RoleLogger.Info("[Shared]",
                $"[Startup] version={typeof(SteamP2PFriendsPlugin).Assembly.GetName().Version} architecture=MultiObserver-M6C p2pEnabled={EnableP2PCoop.Value} " +
                $"verboseDiagnostics={VerboseLog.Value} routeDiagnostics={RouteDiagnostics.Value} " +
                $"multiObserverShadow={EnableMultiObserverShadow.Value} " +
                $"worldStatus={P2PWorldStatusBroadcaster.ActivationState}");

            try
            {
                SteamRuntime.EnsureInitialized();
                RoleLogger.Info("[Shared]", "[SteamRuntime] 初始化完成");
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"SteamRuntime 初始化失败: {ex}");
            }

            try
            {
                _harmony = new Harmony(HARMONY_ID);
                _harmony.PatchAll(typeof(SteamP2PFriendsPlugin).Assembly);
                RoleLogger.Info("[Shared]", "[Harmony] PatchAll 已执行（不保证所有 patch 登记成功）");
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"Harmony.PatchAll 失败: {ex}");
            }

            ApplyAllPatchesAndDiagnostics();

            if (!DiagnosticBuildValid)
            {
                RoleLogger.Error("[Shared]",
                    "[Shared] !!! DiagnosticBuildValid=false，跳过 P2P-Lobby/ClientLobbyListener/P2PJoinManager 初始化（P0-1 INVALID 门控）!!!");
                return;
            }

            try
            {
                P2PLobbyManager.Initialize();
                ClientLobbyListener.Initialize();
                P2PJoinManager.Initialize();
                RoleLogger.Info("[Shared]", "[P2P-Lobby] P2PLobbyManager + ClientLobbyListener + P2PJoinManager 已初始化");
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"Lobby 初始化失败: {ex}");
            }

            try { SteamP2PFriends.Host.RemotePlayerRenderProbe.Initialize(); }
            catch (System.Exception ex) { RoleLogger.Error("[Shared]", $"RemotePlayerRenderProbe.Initialize 失败: {ex}"); }

            try { SteamP2PFriends.Client.ClientRemotePlayerRenderProbe.Initialize(); }
            catch (System.Exception ex) { RoleLogger.Error("[Shared]", $"ClientRemotePlayerRenderProbe.Initialize 失败: {ex}"); }

            try
            {
                Provider.onEnemyDisconnected += OnEnemyDisconnectedHandler;
                RoleLogger.Info("[Shared]", "[Shared] 已订阅 Provider.onEnemyDisconnected（RegionSync/RenderProbe 计数代次复位 - 远端玩家断开）");
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"订阅 Provider.onEnemyDisconnected 失败: {ex}");
            }

            try
            {
                Provider.onClientDisconnected += OnClientDisconnectedHandler;
                RoleLogger.Info("[Shared]", "[Shared] 已订阅 Provider.onClientDisconnected（fallback - 本地客机断开）");
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"订阅 Provider.onClientDisconnected 失败: {ex}");
            }
        }

        /// <summary>
        /// 触发各 patch 的 OnClientDisconnected，清除已断线 SteamID 的计数/状态。
        /// </summary>
        private static void OnEnemyDisconnectedHandler(SteamPlayer player)
        {
            Core.Lifecycle.SessionDisconnectDispatcher.OnEnemyDisconnected(player);
        }

        private static void OnClientDisconnectedHandler()
        {
            Core.Lifecycle.SessionDisconnectDispatcher.OnClientDisconnected();
        }

        private void Update()
        {
            try
            {
                bool stage10Ok = P2PWorldStatusBroadcaster.TryActivateOnGameThread();
                Stage10WorldBroadcastActivationValid = stage10Ok;
                if (stage10Ok)
                {
                    _worldStatusActivationFailureLogged = false;
                }
                else
                {
                    DiagnosticBuildValid = false;
                    if (!_worldStatusActivationFailureLogged)
                    {
                        _worldStatusActivationFailureLogged = true;
                        RoleLogger.Error("[Shared]",
                            "[WorldBroadcast] activation failed on game thread; P2P entry disabled fail-closed");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Stage10WorldBroadcastActivationValid = false;
                DiagnosticBuildValid = false;
                if (!_worldStatusActivationFailureLogged)
                {
                    _worldStatusActivationFailureLogged = true;
                    RoleLogger.Error("[Shared]",
                        "[WorldBroadcast] activation threw: " + ex.GetType().Name);
                }
            }

            if (P2PWorldStatusBroadcaster.ShouldSuspendPluginUpdate)
                return;

            try
            {
                MultiObserverShadowCoordinator.Tick(EnableMultiObserverShadow?.Value == true);
                MultiObserver.ZombieRegionLifecycleAdapter.Tick();
            }
            catch (System.Exception ex)
            {
                MultiObserverShadowCoordinator.HandleTickFailure(ex);
            }

            if (!EnsureRouteBLifecycleHooksOnGameThread())
                return;

            if (!DiagnosticBuildValid)
            {
                try { SteamP2PFriends.Client.NativeSnsLogProbe.RetryEnableIfSteamworksReady(); }
                catch (System.Exception ex) { RoleLogger.Warn("[Shared]", $"[P2P-Update] NativeSnsLogProbe retry 异常: {ex.Message}"); }
                return;
            }
            if (!EnableP2PCoop.Value) return;

            ThreadUtil.assertIsGameThread();
            TickActiveComponents();
        }

        private void OnDestroy()
        {
            try
            {
                try { Provider.onClientDisconnected -= OnClientDisconnectedHandler; } catch { }
                try { Provider.onEnemyDisconnected -= OnEnemyDisconnectedHandler; } catch { }
                ShutdownAllComponents();
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[P2P-OnDestroy] 异常: {ex}");
            }
        }
    }
}
