using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using SDG.NetPak;
using SDG.NetTransport;
using SDG.Unturned;
using SteamP2PFriends.Client;
using SteamP2PFriends.Core.Registration;
using SteamP2PFriends.Host;
using SteamP2PFriends.MultiObserver;
using SteamP2PFriends.Core.Patches;
using SteamP2PFriends.Shared;
using SteamP2PFriends.UI;
using Steamworks;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;



namespace SteamP2PFriends
{
    public partial class SteamP2PFriendsPlugin
    {
        private bool VerifyCriticalPatches(bool redactionSelfTestPassed)
        {
            bool allOk = true;
            // 若 Awake 中 UnityLogBridgePatch.Initialize 漏调或顺序错误，此处兜底
            if (!Core.Patches.UnityLogBridgePatch.IsSubscribed && !Core.Patches.UnityLogBridgePatch.IsFailed)
            {
                try
                {
                    Core.Patches.UnityLogBridgePatch.Initialize();
                    RoleLogger.Warn("[Shared]", "[Diag] VerifyCriticalPatches 防御性 Initialize D-11（Awake 未订阅）");
                }
                catch (System.Exception ex)
                {
                    allOk = false;
                    RoleLogger.Error("[Shared]", $"VerifyCriticalPatches 防御性 Initialize 失败: {ex}");
                }
            }

            try
            {
                Core.Patches.PlayerManagerBroadcastPatch.ReverifyOwnersAfterAllRegistrations(_harmony);
            }
            catch (System.Exception ex)
            {
                allOk = false;
                RoleLogger.Error("[Shared]", $"PlayerManagerBroadcastPatch.ReverifyOwnersAfterAllRegistrations 异常: {ex}");
            }
            try
            {
                Core.Patches.RemotePlayerClothingVisibleBridgePatch.ReverifyOwnersAfterAllRegistrations(_harmony);
            }
            catch (System.Exception ex)
            {
                allOk = false;
                RoleLogger.Error("[Shared]", $"RemotePlayerClothingVisibleBridgePatch.ReverifyOwnersAfterAllRegistrations 异常: {ex}");
            }

            RoleLogger.Diagnostic("[Shared]", "[PatchValidation] validating required P2P hooks and ownership.");

            if (!Core.Patches.AuthHandshakeJournalPatch.RegistrationValid)
            {
                RoleLogger.Error("[Shared]",
                    "[P2P-Connection] !!! DIAGNOSTIC BUILD INVALID: auth handshake/economy compatibility registration failed");
                allOk = false;
            }
            else
            {
                RoleLogger.Info("[Shared]",
                    "[P2P-Connection] OK auth handshake/economy compatibility registration verified");
            }

            if (!HarmonyCompatibilityAudit.InspectOwnedPatchedMethods(_harmony))
            {
                RoleLogger.Error("[Shared]",
                    "[Compat] !!! blocking foreign Harmony conflict found during full owned-target scan");
                allOk = false;
            }

            //   自检任一 FAIL 或异常都视为失败，强制 DiagnosticBuildValid=false
            if (!redactionSelfTestPassed)
            {
                RoleLogger.Error("[Shared]",
                    "[Diag] !!! DIAGNOSTIC BUILD INVALID: P0-3 脱敏自检未通过 (RedactionSelfTestPassed=false) " +
                    "审计 Critical-2 fail-closed 要求");
                allOk = false;
            }
            else
            {
                RoleLogger.Info("[Shared]",
                    "[Diag] OK P0-3 脱敏自检通过 (RedactionSelfTestPassed=true，fail-closed 已激活)");
            }

            allOk &= VerifyPatch(typeof(Provider), "accept",
                new System.Type[] {
                    typeof(SteamPlayerID), typeof(bool), typeof(bool), typeof(byte), typeof(byte), typeof(byte),
                    typeof(Color), typeof(Color), typeof(Color), typeof(Color), typeof(bool),
                    typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int),
                    typeof(int[]), typeof(string[]), typeof(string[]),
                    typeof(EPlayerSkillset), typeof(string), typeof(Steamworks.CSteamID), typeof(EClientPlatform)
                },
                "Provider.accept(internal overload, 25 args)", requirePrefix: true, requireFinalizer: true);

            allOk &= VerifyPatch(typeof(Provider), "addPlayer",
                new System.Type[] {
                    typeof(SDG.NetTransport.ITransportConnection), typeof(NetId), typeof(SteamPlayerID),
                    typeof(Vector3), typeof(byte),
                    typeof(bool), typeof(bool), typeof(int),
                    typeof(byte), typeof(byte), typeof(byte),
                    typeof(Color), typeof(Color), typeof(Color), typeof(Color), typeof(bool),
                    typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int),
                    typeof(int[]), typeof(string[]), typeof(string[]),
                    typeof(EPlayerSkillset), typeof(string), typeof(Steamworks.CSteamID), typeof(EClientPlatform)
                },
                "Provider.addPlayer(internal overload, 30 args)", requirePrefix: true, requirePostfix: true);

            // 公开 Beta 固定验证完整的兼容性补丁集。
                // 套1: InitializePlayerStatePatch（状态机所有者，Prefix 返回 bool）- Prefix/Postfix/Finalizer
                // 套2: PlayerInitializeDiagnosticPatch（纯观察，void Prefix）- Prefix/Postfix/Finalizer
                HarmonyLib.Patches initPatches = null;
                System.Reflection.MethodInfo initMethod = null;
                try
                {
                    initMethod = AccessTools.Method(typeof(Player), "InitializePlayer");
                    if (initMethod != null)
                    {
                        initPatches = Harmony.GetPatchInfo(initMethod);
                    }
                }
                catch (System.Exception ex)
                {
                    RoleLogger.Error("[Shared]", $"[Diag] Player.InitializePlayer GetPatchInfo 异常: {ex.Message}");
                    allOk = false;
                }

                int initPrefixCount = HarmonyCompatibilityAudit.CountOwned(initPatches?.Prefixes);
                int initPostfixCount = HarmonyCompatibilityAudit.CountOwned(initPatches?.Postfixes);
                int initFinalizerCount = HarmonyCompatibilityAudit.CountOwned(initPatches?.Finalizers);

                // 数量验证：必须各有 2 个（两套 patch）
                if (initPrefixCount < 2 || initPostfixCount < 2 || initFinalizerCount < 2)
                {
                    RoleLogger.Error("[Shared]",
                        $"[Diag] !!! Player.InitializePlayer patch 数量不足: " +
                        $"prefixes={initPrefixCount}(期望>=2) postfixes={initPostfixCount}(期望>=2) " +
                        $"finalizers={initFinalizerCount}(期望>=2)");
                    allOk = false;
                }

                if (initMethod != null &&
                    !HarmonyCompatibilityAudit.Inspect(initMethod, "Player.InitializePlayer"))
                {
                    RoleLogger.Error("[Shared]",
                        "[Compat] Player.InitializePlayer has a blocking foreign Harmony patch");
                    allOk = false;
                }

                // 精确方法验证：InitializePlayerStatePatch.Prefix/Postfix/Finalizer（状态机所有者）
                if (initPatches != null)
                {
                    if (!VerifyPatchMethod(initPatches.Prefixes,
                            typeof(SteamP2PFriends.Core.Patches.InitializePlayerStatePatch),
                            "Prefix", "Player.InitializePlayer (P0-E E-3 Prefix)", "Prefix")) allOk = false;
                    if (!VerifyPatchMethod(initPatches.Postfixes,
                            typeof(SteamP2PFriends.Core.Patches.InitializePlayerStatePatch),
                            "Postfix", "Player.InitializePlayer (P0-E E-3 Postfix)", "Postfix")) allOk = false;
                    if (!VerifyPatchMethod(initPatches.Finalizers,
                            typeof(SteamP2PFriends.Core.Patches.InitializePlayerStatePatch),
                            "Finalizer", "Player.InitializePlayer (P0-E E-3 Finalizer)", "Finalizer")) allOk = false;

                    // 精确方法验证：PlayerInitializeDiagnosticPatch.Prefix/Postfix/Finalizer（纯观察）
                    if (!VerifyPatchMethod(initPatches.Prefixes,
                            typeof(SteamP2PFriends.Core.Patches.PlayerInitializeDiagnosticPatch),
                            "Prefix", "Player.InitializePlayer (Diag Prefix)", "Prefix")) allOk = false;
                    if (!VerifyPatchMethod(initPatches.Postfixes,
                            typeof(SteamP2PFriends.Core.Patches.PlayerInitializeDiagnosticPatch),
                            "Postfix", "Player.InitializePlayer (Diag Postfix)", "Postfix")) allOk = false;
                    if (!VerifyPatchMethod(initPatches.Finalizers,
                            typeof(SteamP2PFriends.Core.Patches.PlayerInitializeDiagnosticPatch),
                            "Finalizer", "Player.InitializePlayer (Diag Finalizer)", "Finalizer")) allOk = false;
                }

                if (allOk)
                {
                    RoleLogger.Info("[Shared]",
                        $"[Diag] OK Player.InitializePlayer 双 patch 链验证通过 " +
                        $"(Prefix={initPrefixCount}/Postfix={initPostfixCount}/Finalizer={initFinalizerCount})");
                }

                // 验证 8 个组件的 InitializePlayer 上有 BitmaskPostfixCache<T>.Postfix 登记
                allOk &= VerifyBitmaskPostfix<PlayerClothing>("P1-G PlayerClothing");
                allOk &= VerifyBitmaskPostfix<PlayerInventory>("P1-G PlayerInventory");
                allOk &= VerifyBitmaskPostfix<PlayerLife>("P1-G PlayerLife");
                allOk &= VerifyBitmaskPostfix<PlayerStance>("P1-G PlayerStance");
                allOk &= VerifyBitmaskPostfix<PlayerMovement>("P1-G PlayerMovement");
                allOk &= VerifyBitmaskPostfix<PlayerLook>("P1-G PlayerLook");
                allOk &= VerifyBitmaskPostfix<PlayerInteract>("P1-G PlayerInteract");
                allOk &= VerifyBitmaskPostfix<PlayerInput>("P1-G PlayerInput");

                // SteamPlayer constructor
                System.Reflection.ConstructorInfo ctor = typeof(SteamPlayer).GetConstructor(new System.Type[] {
                    typeof(SDG.NetTransport.ITransportConnection), typeof(NetId), typeof(SteamPlayerID), typeof(Transform),
                    typeof(bool), typeof(bool), typeof(int), typeof(byte), typeof(byte), typeof(byte),
                    typeof(Color), typeof(Color), typeof(Color), typeof(Color), typeof(bool),
                    typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int),
                    typeof(int[]), typeof(string[]), typeof(string[]),
                    typeof(EPlayerSkillset), typeof(string), typeof(Steamworks.CSteamID), typeof(EClientPlatform)
                });
                if (ctor == null)
                {
                    RoleLogger.Error("[Shared]", "[Diag] !!! DIAGNOSTIC BUILD INVALID: SteamPlayer.ctor(29 args) 反射失败");
                    allOk = false;
                }
                else
                {
                    HarmonyLib.Patches patches = Harmony.GetPatchInfo(ctor);
                    int postfixCount = HarmonyCompatibilityAudit.CountOwned(patches?.Postfixes);
                    if (postfixCount == 0)
                    {
                        RoleLogger.Error("[Shared]",
                            $"[Diag] !!! DIAGNOSTIC BUILD INVALID: SteamPlayer.ctor Postfix 未登记 (P0-C)");
                        allOk = false;
                    }
                    else
                    {
                        bool compatible = HarmonyCompatibilityAudit.Inspect(ctor, "SteamPlayer.ctor");
                        allOk &= compatible;
                        bool methodOk = VerifyPatchMethod(patches?.Postfixes,
                            typeof(SteamP2PFriends.Core.Patches.SteamPlayerIsLocalServerHostPatch),
                            "Postfix", "SteamPlayer.ctor (P0-C Postfix)", "Postfix");
                        allOk &= methodOk;
                        if (compatible && methodOk)
                        {
                            RoleLogger.Info("[Shared]",
                                $"[Diag] OK SteamPlayer.ctor Postfix 已登记 (postfixes={postfixCount})");
                        }
                    }
                }
            // 实际登记：所有方法 Prefix + Finalizer（无 Postfix）
            // Provider.SendInitialGlobalState(SteamPlayer)
            allOk &= VerifyPatch(typeof(Provider), "SendInitialGlobalState",
                new System.Type[] { typeof(SteamPlayer) },
                "D-13 Provider.SendInitialGlobalState(SteamPlayer)", requirePrefix: true, requireFinalizer: true);
            // PhysicsMaterialNetTable.Send(ITransportConnection)
            allOk &= VerifyPatch(typeof(SDG.Unturned.PhysicsMaterialNetTable), "Send",
                new System.Type[] { typeof(SDG.NetTransport.ITransportConnection) },
                "D-13 PhysicsMaterialNetTable.Send", requirePrefix: true, requireFinalizer: true);
            // LightingManager.SendInitialGlobalState(SteamPlayer)
            allOk &= VerifyPatch(typeof(LightingManager), "SendInitialGlobalState",
                new System.Type[] { typeof(SteamPlayer) },
                "D-13 LightingManager.SendInitialGlobalState", requirePrefix: true, requireFinalizer: true);
            // VehicleManager.SendInitialGlobalState(SteamPlayer)
            allOk &= VerifyPatch(typeof(VehicleManager), "SendInitialGlobalState",
                new System.Type[] { typeof(SteamPlayer) },
                "D-13 VehicleManager.SendInitialGlobalState", requirePrefix: true, requireFinalizer: true);
            // AnimalManager.SendInitialGlobalState(ITransportConnection)
            allOk &= VerifyPatch(typeof(AnimalManager), "SendInitialGlobalState",
                new System.Type[] { typeof(SDG.NetTransport.ITransportConnection) },
                "D-13 AnimalManager.SendInitialGlobalState", requirePrefix: true, requireFinalizer: true);
            // LevelManager.SendInitialGlobalState(SteamPlayer)
            allOk &= VerifyPatch(typeof(LevelManager), "SendInitialGlobalState",
                new System.Type[] { typeof(SteamPlayer) },
                "D-13 LevelManager.SendInitialGlobalState", requirePrefix: true, requireFinalizer: true);
            // ZombieManager.SendInitialGlobalState(SteamPlayer)
            allOk &= VerifyPatch(typeof(ZombieManager), "SendInitialGlobalState",
                new System.Type[] { typeof(SteamPlayer) },
                "D-13 ZombieManager.SendInitialGlobalState", requirePrefix: true, requireFinalizer: true);
            // Player.SendInitialPlayerState(SteamPlayer)
            allOk &= VerifyPatch(typeof(Player), "SendInitialPlayerState",
                new System.Type[] { typeof(SteamPlayer) },
                "D-13 Player.SendInitialPlayerState(SteamPlayer)", requirePrefix: true, requireFinalizer: true);
            // Player.SendInitialPlayerState(List<ITransportConnection>)
            allOk &= VerifyPatch(typeof(Player), "SendInitialPlayerState",
                new System.Type[] { typeof(System.Collections.Generic.List<SDG.NetTransport.ITransportConnection>) },
                "D-13 Player.SendInitialPlayerState(List)", requirePrefix: true, requireFinalizer: true);
            // Provider.AddClientToThirdpartyAntiCheat(ITransportConnection, SteamPlayerID, SteamPlayer)
            allOk &= VerifyPatch(typeof(Provider), "AddClientToThirdpartyAntiCheat",
                new System.Type[] { typeof(SDG.NetTransport.ITransportConnection), typeof(SteamPlayerID), typeof(SteamPlayer) },
                "D-13 Provider.AddClientToThirdpartyAntiCheat", requirePrefix: true, requireFinalizer: true);
            // Provider.dismiss (ProviderDismissDiagnosticPatch) - Prefix + Postfix
            allOk &= VerifyPatch(typeof(Provider), "dismiss",
                new System.Type[] { typeof(Steamworks.CSteamID) },
                "D-13 Provider.dismiss", requirePrefix: true, requirePostfix: true);
            // Provider.RemoveClient (ProviderDismissDiagnosticPatch) - Prefix + Postfix
            allOk &= VerifyPatch(typeof(Provider), "RemoveClient",
                new System.Type[] { typeof(SteamPlayer) },
                "D-13 Provider.RemoveClient", requirePrefix: true, requirePostfix: true);

            allOk &= VerifyPatch(typeof(PlayerClothing), "InitializePlayer",
                "D-3b PlayerClothing.InitializePlayer", requireFinalizer: true);
            allOk &= VerifyPatch(typeof(PlayerInventory), "InitializePlayer",
                "D-3b PlayerInventory.InitializePlayer", requireFinalizer: true);
            allOk &= VerifyPatch(typeof(PlayerLife), "InitializePlayer",
                "D-3b PlayerLife.InitializePlayer", requireFinalizer: true);
            allOk &= VerifyPatch(typeof(PlayerSkills), "InitializePlayer",
                "D-3b PlayerSkills.InitializePlayer", requireFinalizer: true);
            allOk &= VerifyPatch(typeof(PlayerCrafting), "InitializePlayer",
                "D-3b PlayerCrafting.InitializePlayer", requireFinalizer: true);
            allOk &= VerifyPatch(typeof(PlayerStance), "InitializePlayer",
                "D-3b PlayerStance.InitializePlayer", requireFinalizer: true);
            allOk &= VerifyPatch(typeof(PlayerMovement), "InitializePlayer",
                "D-3b PlayerMovement.InitializePlayer", requireFinalizer: true);
            allOk &= VerifyPatch(typeof(PlayerLook), "InitializePlayer",
                "D-3b PlayerLook.InitializePlayer", requireFinalizer: true);
            allOk &= VerifyPatch(typeof(PlayerInteract), "InitializePlayer",
                "D-3b PlayerInteract.InitializePlayer", requireFinalizer: true);
            allOk &= VerifyPatch(typeof(PlayerAnimator), "InitializePlayer",
                "D-3b PlayerAnimator.InitializePlayer", requireFinalizer: true);
            allOk &= VerifyPatch(typeof(PlayerEquipment), "InitializePlayer",
                "D-3b PlayerEquipment.InitializePlayer", requireFinalizer: true);
            allOk &= VerifyPatch(typeof(PlayerInput), "InitializePlayer",
                "D-3b PlayerInput.InitializePlayer", requireFinalizer: true);
            allOk &= VerifyPatch(typeof(PlayerVoice), "InitializePlayer",
                "D-3b PlayerVoice.InitializePlayer", requireFinalizer: true);
            allOk &= VerifyPatch(typeof(PlayerWorkzone), "InitializePlayer",
                "D-3b PlayerWorkzone.InitializePlayer", requireFinalizer: true);
            allOk &= VerifyPatch(typeof(PlayerQuests), "InitializePlayer",
                "D-3b PlayerQuests.InitializePlayer", requireFinalizer: true);
            // Player.InitializePlayerStart (private, Player.cs:1542) - Prefix + Finalizer
            allOk &= VerifyPatch(typeof(Player), "InitializePlayerStart",
                new System.Type[0],
                "D-3b Player.InitializePlayerStart", requirePrefix: true, requireFinalizer: true);

            // Provider.reject 4 个重载
            allOk &= VerifyPatch(typeof(Provider), "reject",
                new System.Type[] { typeof(Steamworks.CSteamID), typeof(ESteamRejection) },
                "D-5 Provider.reject(CSteamID,ESteamRejection)", requirePrefix: true);
            allOk &= VerifyPatch(typeof(Provider), "reject",
                new System.Type[] { typeof(Steamworks.CSteamID), typeof(ESteamRejection), typeof(string) },
                "D-5 Provider.reject(CSteamID,ESteamRejection,string)", requirePrefix: true);
            allOk &= VerifyPatch(typeof(Provider), "reject",
                new System.Type[] { typeof(SDG.NetTransport.ITransportConnection), typeof(ESteamRejection) },
                "D-5 Provider.reject(ITransport,ESteamRejection)", requirePrefix: true);
            allOk &= VerifyPatch(typeof(Provider), "reject",
                new System.Type[] { typeof(SDG.NetTransport.ITransportConnection), typeof(ESteamRejection), typeof(string) },
                "D-5 Provider.reject(ITransport,ESteamRejection,string)", requirePrefix: true);
            // Provider.kick(CSteamID, string)
            allOk &= VerifyPatch(typeof(Provider), "kick",
                new System.Type[] { typeof(Steamworks.CSteamID), typeof(string) },
                "D-5 Provider.kick(CSteamID,string)", requirePrefix: true);
            // Provider.refuseGarbageConnection 2 个重载
            allOk &= VerifyPatch(typeof(Provider), "refuseGarbageConnection",
                new System.Type[] { typeof(Steamworks.CSteamID), typeof(string) },
                "D-5 Provider.refuseGarbageConnection(CSteamID,string)", requirePrefix: true);
            allOk &= VerifyPatch(typeof(Provider), "refuseGarbageConnection",
                new System.Type[] { typeof(SDG.NetTransport.ITransportConnection), typeof(string) },
                "D-5 Provider.refuseGarbageConnection(ITransport,string)", requirePrefix: true);

            // NetMessages.SendMessageToClient + SendMessageToClients (Prefix + Finalizer)
            allOk &= VerifyNetMessagesPatches();

            // 该方法由 ClientAcceptedHandlerDiagnosticPatch.RegisterManual 登记到 internal class
            // 自检通过 ReflectionUtil 反射内部类型，若失败已在 RegisterManual 阶段报错
            // 这里仅检查 Provider.accept 是否会被 ReadMessage 触发（已由 accept 自检覆盖）

            allOk &= VerifyPatch(typeof(Steamworks.SteamGameServerNetworkingSockets), "CloseConnection",
                "D-9 SteamGameServerNetworkingSockets.CloseConnection", requirePrefix: true);

            // ClientTransport / ServerTransport OnSteamNetConnectionStatusChanged 由 RouteDiagnosticsPatch 登记到具体重载
            // 这里检查 SteamGameServerNetworkingSockets 的 SteamUser API 域重定向方法都已登记。
            allOk &= VerifyPatch(typeof(Steamworks.SteamGameServerNetworkingSockets), "CreateListenSocketIP",
                "D-10/CreateListenSocketIP", requirePrefix: true);
            allOk &= VerifyPatch(typeof(Steamworks.SteamGameServerNetworkingSockets), "CreateListenSocketP2P",
                "D-10/CreateListenSocketP2P", requirePrefix: true);
            allOk &= VerifyPatch(typeof(Steamworks.SteamGameServerNetworkingSockets), "AcceptConnection",
                "D-10/AcceptConnection", requirePrefix: true, requirePostfix: true);
            allOk &= VerifyPatchMethodPair(
                typeof(Steamworks.SteamGameServerNetworkingSockets), "AcceptConnection", null,
                typeof(Core.Patches.SteamUserP2PRedirectPatch),
                nameof(Core.Patches.SteamUserP2PRedirectPatch.AcceptConnection_Prefix),
                nameof(Core.Patches.SteamUserP2PRedirectPatch.AcceptConnection_Postfix),
                "D-10/AcceptConnection precise method");
            allOk &= VerifyPatch(typeof(Steamworks.SteamGameServerNetworkingSockets), "SetConnectionPollGroup",
                "D-10/SetConnectionPollGroup", requirePrefix: true, requirePostfix: true);
            allOk &= VerifyPatchMethodPair(
                typeof(Steamworks.SteamGameServerNetworkingSockets), "SetConnectionPollGroup", null,
                typeof(Core.Patches.SteamUserP2PRedirectPatch),
                nameof(Core.Patches.SteamUserP2PRedirectPatch.SetConnectionPollGroup_Prefix),
                nameof(Core.Patches.SteamUserP2PRedirectPatch.SetConnectionPollGroup_Postfix),
                "D-10/SetConnectionPollGroup precise method");
            allOk &= VerifyPatch(typeof(Steamworks.SteamGameServerNetworkingSockets), "CreatePollGroup",
                "D-10/CreatePollGroup", requirePrefix: true);
            allOk &= VerifyPatch(typeof(Steamworks.SteamGameServerNetworkingSockets), "DestroyPollGroup",
                "D-10/DestroyPollGroup", requirePrefix: true);
            allOk &= VerifyPatch(typeof(Steamworks.SteamGameServerNetworkingSockets), "ReceiveMessagesOnPollGroup",
                "D-10/ReceiveMessagesOnPollGroup", requirePrefix: true);
            allOk &= VerifyPatch(typeof(Steamworks.SteamGameServerNetworkingSockets), "CloseListenSocket",
                "D-10/CloseListenSocket", requirePrefix: true);
            allOk &= VerifyPatch(typeof(Steamworks.SteamGameServerNetworkingSockets), "SendMessageToConnection",
                "D-10/SendMessageToConnection", requirePrefix: true);
            allOk &= VerifyPatch(typeof(Steamworks.SteamGameServerNetworkingSockets), "ReceiveMessagesOnConnection",
                "D-10/ReceiveMessagesOnConnection", requirePrefix: true);
            allOk &= VerifyPatch(typeof(Steamworks.SteamGameServerNetworkingSockets), "GetConnectionInfo",
                "D-10/GetConnectionInfo", requirePrefix: true);
            allOk &= VerifyPatch(typeof(Steamworks.SteamGameServerNetworkingSockets), "SetConnectionName",
                "D-10/SetConnectionName", requirePrefix: true);

            allOk &= VerifyPatch(typeof(Steamworks.Callback<Steamworks.SteamNetConnectionStatusChangedCallback_t>), "CreateGameServer",
                "Callback<ConnStatus>.CreateGameServer", requirePrefix: true);
            allOk &= VerifyPatch(typeof(Steamworks.Callback<Steamworks.SteamNetAuthenticationStatus_t>), "CreateGameServer",
                "Callback<AuthStatus>.CreateGameServer", requirePrefix: true);

            allOk &= VerifyPatch(typeof(SDG.NetTransport.SteamNetworkingSockets.ServerTransport_SteamNetworkingSockets), "Initialize",
                "ServerTransport.Initialize", requirePrefix: true);

            // ClientTransport_SteamNetworkingSockets.OnSteamNetConnectionStatusChanged - ClientSnsStatusDiagnosticPatch
            allOk &= VerifyPatch(typeof(SDG.NetTransport.SteamNetworkingSockets.ClientTransport_SteamNetworkingSockets),
                "OnSteamNetConnectionStatusChanged",
                "D-10 ClientTransport.OnSteamNetConnectionStatusChanged",
                requirePrefix: true, requirePostfix: true);
            //   ClientSnsStatusDiagnosticPatch.Prefix / .Postfix（private static，反射可访问 metadata）
            allOk &= VerifyPatchMethodPair(
                typeof(SDG.NetTransport.SteamNetworkingSockets.ClientTransport_SteamNetworkingSockets),
                "OnSteamNetConnectionStatusChanged", null,
                typeof(Core.Patches.ClientSnsStatusDiagnosticPatch),
                "Prefix", "Postfix",
                "D-10 ClientTransport.OnSteamNetConnectionStatusChanged precise method");
            // ServerTransport_SteamNetworkingSockets.OnSteamNetConnectionStatusChanged - ServerSnsStatusDiagnosticPatch
            allOk &= VerifyPatch(typeof(SDG.NetTransport.SteamNetworkingSockets.ServerTransport_SteamNetworkingSockets),
                "OnSteamNetConnectionStatusChanged",
                "D-10 ServerTransport.OnSteamNetConnectionStatusChanged",
                requirePrefix: true, requirePostfix: true);
            allOk &= VerifyPatchMethodPair(
                typeof(SDG.NetTransport.SteamNetworkingSockets.ServerTransport_SteamNetworkingSockets),
                "OnSteamNetConnectionStatusChanged", null,
                typeof(Core.Patches.ServerSnsStatusDiagnosticPatch),
                "Prefix", "Postfix",
                "D-10 ServerTransport.OnSteamNetConnectionStatusChanged precise method");

            // 1. LightingManager.ReceiveInitialLightingState (static, 9 args)
            allOk &= VerifyPatch(typeof(LightingManager), "ReceiveInitialLightingState",
                new System.Type[] {
                    typeof(uint), typeof(uint), typeof(uint), typeof(byte), typeof(byte),
                    typeof(System.Guid), typeof(float), typeof(NetId), typeof(int)
                },
                "P0-C LightingManager.ReceiveInitialLightingState", requirePrefix: true, requirePostfix: true, requireFinalizer: true);
            // 2. VehicleManager.ReceiveMultipleVehicles (static, in ClientInvocationContext)
            allOk &= VerifyPatch(typeof(VehicleManager), "ReceiveMultipleVehicles",
                "P0-C VehicleManager.ReceiveMultipleVehicles", requirePrefix: true, requirePostfix: true, requireFinalizer: true);
            // 3. BarricadeManager.ReceiveMultipleBarricades (static, in ClientInvocationContext)
            allOk &= VerifyPatch(typeof(BarricadeManager), "ReceiveMultipleBarricades",
                "P0-C BarricadeManager.ReceiveMultipleBarricades", requirePrefix: true, requirePostfix: true, requireFinalizer: true);
            // 4. StructureManager.ReceiveMultipleStructures (static, in ClientInvocationContext)
            allOk &= VerifyPatch(typeof(StructureManager), "ReceiveMultipleStructures",
                "P0-C StructureManager.ReceiveMultipleStructures", requirePrefix: true, requirePostfix: true, requireFinalizer: true);
            // 5. PlayerInventory.ReceiveInventory (instance, in ClientInvocationContext)
            allOk &= VerifyPatch(typeof(PlayerInventory), "ReceiveInventory",
                "P0-C PlayerInventory.ReceiveInventory", requirePrefix: true, requirePostfix: true, requireFinalizer: true);
            // 6. PlayerLife.ReceiveLifeStats (instance, 7 args)
            allOk &= VerifyPatch(typeof(PlayerLife), "ReceiveLifeStats",
                new System.Type[] {
                    typeof(byte), typeof(byte), typeof(byte), typeof(byte), typeof(byte), typeof(bool), typeof(bool)
                },
                "P0-C PlayerLife.ReceiveLifeStats", requirePrefix: true, requirePostfix: true, requireFinalizer: true);
            // 7. PlayerClothing.ReceiveClothingState (instance, in ClientInvocationContext)
            allOk &= VerifyPatch(typeof(PlayerClothing), "ReceiveClothingState",
                "P0-C PlayerClothing.ReceiveClothingState", requirePrefix: true, requirePostfix: true, requireFinalizer: true);

            try
            {
                System.Type qpcType = AccessTools.TypeByName("SDG.Unturned.ClientMessageHandler_QueuePositionChanged");
                if (qpcType == null)
                {
                    RoleLogger.Error("[Shared]", "[Diag] !!! P0-B ClientMessageHandler_QueuePositionChanged: TypeByName 返回 null");
                    allOk = false;
                }
                else
                {
                    allOk &= VerifyPatch(qpcType, "ReadMessage",
                        new System.Type[] { typeof(NetPakReader) },
                        "P0-B ClientMessageHandler_QueuePositionChanged.ReadMessage", requirePostfix: true);
                }
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[Diag] P0-B QueuePositionChanged 自检异常: {ex.Message}");
                allOk = false;
            }

            allOk &= VerifyPatch(typeof(PlayerInput), "FixedUpdate",
                "P0-D PlayerInput.FixedUpdate", requirePrefix: true);
            allOk &= VerifyPatch(typeof(PlayerMovement), "simulate",
                new System.Type[0],
                "P0-D PlayerMovement.simulate(0 args)", requirePrefix: true);
            allOk &= VerifyPatch(typeof(PlayerMovement), "simulate",
                new System.Type[] {
                    typeof(uint), typeof(int), typeof(bool), typeof(bool),
                    typeof(Vector3), typeof(Quaternion),
                    typeof(float), typeof(float), typeof(float), typeof(float), typeof(float)
                },
                "P0-D PlayerMovement.simulate(11 args, driving)", requirePrefix: true);
            allOk &= VerifyPatch(typeof(PlayerMovement), "simulate",
                new System.Type[] {
                    typeof(uint), typeof(int), typeof(int), typeof(int),
                    typeof(float), typeof(float),
                    typeof(bool), typeof(bool), typeof(float)
                },
                "P0-D PlayerMovement.simulate(9 args, walking)", requirePrefix: true);

            //   - Prefix: DisconnectTracerPatch.Prefix（在 vanilla teardown 前抓取 handle 状态）
            //   - Postfix: DisconnectTracerPatch.Postfix（记录 vanilla 调用方 reason）
            allOk &= VerifyPatch(typeof(Provider), "RequestDisconnect",
                new System.Type[] { typeof(string) },
                "P1-C Provider.RequestDisconnect(string)", requirePrefix: true, requirePostfix: true);
            //   DisconnectTracerPatch.Prefix / .Postfix（private static，反射可访问 metadata）
            allOk &= VerifyPatchMethodPair(
                typeof(Provider), "RequestDisconnect", new System.Type[] { typeof(string) },
                typeof(Core.Patches.DisconnectTracerPatch),
                "Prefix", "Postfix",
                "P1-C Provider.RequestDisconnect(string) precise method");

            //   双端 OnSteamNetAuthenticationStatusChanged 各加 Prefix，替换 m_debugMsg 为占位符
            //   封闭 vanilla Log 把原始 m_debugMsg 写入 Unity Player.log 的路径
            //   两个 patch 类均只有 Prefix（无 Postfix），用 VerifyPatch + VerifyPatchMethod 单独验证
            allOk &= VerifyPatch(typeof(SDG.NetTransport.SteamNetworkingSockets.ServerTransport_SteamNetworkingSockets),
                "OnSteamNetAuthenticationStatusChanged",
                "P0-B ServerTransport.OnSteamNetAuthenticationStatusChanged", requirePrefix: true);
            allOk &= VerifyAuthCallbackSafetyPatchMethod(
                typeof(SDG.NetTransport.SteamNetworkingSockets.ServerTransport_SteamNetworkingSockets),
                "OnSteamNetAuthenticationStatusChanged",
                typeof(Core.Patches.ServerAuthStatusCallbackSafetyPatch),
                "Prefix",
                "P0-B ServerTransport.OnSteamNetAuthenticationStatusChanged precise method");
            allOk &= VerifyPatch(typeof(SDG.NetTransport.SteamNetworkingSockets.ClientTransport_SteamNetworkingSockets),
                "OnSteamNetAuthenticationStatusChanged",
                "P0-B ClientTransport.OnSteamNetAuthenticationStatusChanged", requirePrefix: true);
            allOk &= VerifyAuthCallbackSafetyPatchMethod(
                typeof(SDG.NetTransport.SteamNetworkingSockets.ClientTransport_SteamNetworkingSockets),
                "OnSteamNetAuthenticationStatusChanged",
                typeof(Core.Patches.ClientAuthStatusCallbackSafetyPatch),
                "Prefix",
                "P0-B ClientTransport.OnSteamNetAuthenticationStatusChanged precise method");

            // 旧版关键 patch (informational only, 不参与 allOk)
            // 上面已经覆盖

            RoleLogger.Info("[Shared]",
                $"[Diag] v2 审计放行 patch 状态: " +
                $"SteamPlayerIsLocalServerHostPatch.Enabled={Core.Patches.SteamPlayerIsLocalServerHostPatch.Enabled}, " +
                $"PlayerUpdateGuardPatch.Enabled={Core.Patches.PlayerUpdateGuardPatch.Enabled}, " +
                $"PlayerMovementInitializePlayerPrefixPatch.Enabled={Core.Patches.PlayerMovementInitializePlayerPrefixPatch.Enabled}, " +
                $"GameplayReadyBitmaskPatch.Enabled={Core.Patches.GameplayReadyBitmaskPatch.Enabled}");

            if (!Core.Patches.UnityLogBridgePatch.IsSubscribed || Core.Patches.UnityLogBridgePatch.IsFailed)
            {
                RoleLogger.Error("[Shared]",
                    $"[Diag] !!! DIAGNOSTIC BUILD INVALID: D-11 Unity bridge 未订阅 " +
                    $"(IsSubscribed={Core.Patches.UnityLogBridgePatch.IsSubscribed}, " +
                    $"IsFailed={Core.Patches.UnityLogBridgePatch.IsFailed})");
                allOk = false;
            }
            else
            {
                RoleLogger.Info("[Shared]",
                    "[Diag] OK D-11 Unity logMessageReceivedThreaded bridge subscribed (blocking)");
            }

            // 不能只依赖 Harmony 元数据数量与 owner，必须同时检查 RegisterManual 返回值
            bool p0cAll = Core.Patches.InitialStateReceiveDiagnosticPatch.AllRegistrationsSucceeded;
            if (!p0cAll)
            {
                RoleLogger.Error("[Shared]",
                    $"[Diag] !!! DIAGNOSTIC BUILD INVALID: P0-C AllRegistrationsSucceeded=false " +
                    $"summary={Core.Patches.InitialStateReceiveDiagnosticPatch.RegistrationSummary}");
                allOk = false;
            }
            else
            {
                RoleLogger.Info("[Shared]",
                    $"[Diag] OK P0-C AllRegistrationsSucceeded=true " +
                    $"summary={Core.Patches.InitialStateReceiveDiagnosticPatch.RegistrationSummary}");
            }

            //   三个 ClientMethodHandle.SendAndLoopback* Prefix 必须全部手动登记成功，
            //   且 PatchMethod.DeclaringType==ClientMethodLoopbackPatch + 方法名匹配。
            //   任一缺失强制 DiagnosticBuildValid=false。
            //   AllRegistrationsSucceeded 是 RegisterManual 返回值的快照，
            //   精确自检是对 Harmony 元数据的独立验证（双保险）。
            //   AccessTools.DeclaredMethod 从 ClientMethodHandle 声明类型精确解析 private InvokeLoopback
            //   派生类型 GetMethod 找不到基类 private 方法，旧 ReflectionUtil.InvokeInstance 必失败
            bool loopbackRegOk = Core.Patches.ClientMethodLoopbackPatch.AllRegistrationsSucceeded;
            if (!loopbackRegOk)
            {
                RoleLogger.Error("[Shared]",
                    $"[Diag] !!! DIAGNOSTIC BUILD INVALID: ClientMethodLoopbackPatch AllRegistrationsSucceeded=false " +
                    $"summary={Core.Patches.ClientMethodLoopbackPatch.RegistrationSummary}");
                allOk = false;
            }
            else
            {
                RoleLogger.Info("[Shared]",
                    $"[Diag] OK ClientMethodLoopbackPatch AllRegistrationsSucceeded=true " +
                    $"summary={Core.Patches.ClientMethodLoopbackPatch.RegistrationSummary}");
            }

            //   VerifyInvokeLoopbackMethod 已在 RegisterManual 开头执行，
            //   此处仅读取结果做阻断门聚合。
            //   验证：DeclaringType==ClientMethodHandle + Name==InvokeLoopback + 参数==NetPakWriter + 返回 void
            bool invokeLoopbackOk = Core.Patches.ClientMethodLoopbackPatch.InvokeLoopbackResolved;
            if (!invokeLoopbackOk)
            {
                RoleLogger.Error("[Shared]",
                    $"[Diag] !!! DIAGNOSTIC BUILD INVALID: ClientMethodLoopbackPatch InvokeLoopbackResolved=false " +
                    $"summary={Core.Patches.ClientMethodLoopbackPatch.InvokeLoopbackSummary}");
                allOk = false;
            }
            else
            {
                RoleLogger.Info("[Shared]",
                    $"[Diag] OK ClientMethodLoopbackPatch InvokeLoopbackResolved=true " +
                    $"summary={Core.Patches.ClientMethodLoopbackPatch.InvokeLoopbackSummary}");
            }

            // 精确方法验证：3 个 Prefix 的 DeclaringType + Name 必须匹配
            // Own prefix identity must be exact; foreign patch policy is evaluated separately.
            allOk &= VerifyClientMethodLoopbackPrefix(
                typeof(ClientMethodHandle), "SendAndLoopbackIfLocal",
                new System.Type[] {
                    typeof(ENetReliability),
                    typeof(SDG.NetTransport.ITransportConnection),
                    typeof(NetPakWriter)
                },
                Core.Patches.ClientMethodLoopbackPatch.PrefixIfLocalName,
                "ClientMethodLoopback/IfLocal");

            allOk &= VerifyClientMethodLoopbackPrefix(
                typeof(ClientMethodHandle), "SendAndLoopbackIfAnyAreLocal",
                new System.Type[] {
                    typeof(ENetReliability),
                    typeof(System.Collections.Generic.List<SDG.NetTransport.ITransportConnection>),
                    typeof(NetPakWriter)
                },
                Core.Patches.ClientMethodLoopbackPatch.PrefixIfAnyAreLocalName,
                "ClientMethodLoopback/IfAnyAreLocal");

            allOk &= VerifyClientMethodLoopbackPrefix(
                typeof(ClientMethodHandle), "SendAndLoopback",
                new System.Type[] {
                    typeof(ENetReliability),
                    typeof(System.Collections.Generic.List<SDG.NetTransport.ITransportConnection>),
                    typeof(NetPakWriter)
                },
                Core.Patches.ClientMethodLoopbackPatch.PrefixSendAndLoopbackName,
                "ClientMethodLoopback/SendAndLoopback");

            //   - AllRegistrationsSucceeded=true
            //   - ReplacementCount=1（Transpiler 替换点精确 1 个）
            //   - SignatureResolved=true（onRegionUpdated private instance 7 args 签名匹配）
            //   - SendRegionPrefixRegistered=true（Prefix 登记成功）
            //   - TranspilerOwnerVerified=true（owner=com.yu80rice.steamp2pfriends + method=OnRegionUpdated_Transpiler + count=1）
            //   - PrefixOwnerVerified=true（owner=com.yu80rice.steamp2pfriends + method=SendRegion_Prefix + count=1）
            //   任一不满足强制 DiagnosticBuildValid=false
            bool barricadeRegionOk = SteamP2PFriends.Adapters.Structure.Patches.BarricadeManagerRegionSyncPatch.AllRegistrationsSucceeded;
            if (!barricadeRegionOk)
            {
                RoleLogger.Error("[Shared]",
                    $"[Diag] !!! DIAGNOSTIC BUILD INVALID: BarricadeManagerRegionSyncPatch " +
                    $"summary={SteamP2PFriends.Adapters.Structure.Patches.BarricadeManagerRegionSyncPatch.RegistrationSummary} " +
                    $"replacement={SteamP2PFriends.Adapters.Structure.Patches.BarricadeManagerRegionSyncPatch.ReplacementCount} " +
                    $"signature={SteamP2PFriends.Adapters.Structure.Patches.BarricadeManagerRegionSyncPatch.SignatureResolved} " +
                    $"sendRegionPrefix={SteamP2PFriends.Adapters.Structure.Patches.BarricadeManagerRegionSyncPatch.SendRegionPrefixRegistered} " +
                    $"transpilerOwner={SteamP2PFriends.Adapters.Structure.Patches.BarricadeManagerRegionSyncPatch.TranspilerOwnerVerified} " +
                    $"prefixOwner={SteamP2PFriends.Adapters.Structure.Patches.BarricadeManagerRegionSyncPatch.PrefixOwnerVerified}");
                allOk = false;
            }
            else
            {
                RoleLogger.Info("[Shared]",
                    $"[Diag] OK BarricadeManagerRegionSyncPatch: " +
                    $"replacement={SteamP2PFriends.Adapters.Structure.Patches.BarricadeManagerRegionSyncPatch.ReplacementCount}/1 " +
                    $"signature={SteamP2PFriends.Adapters.Structure.Patches.BarricadeManagerRegionSyncPatch.SignatureResolved} " +
                    $"sendRegionPrefix={SteamP2PFriends.Adapters.Structure.Patches.BarricadeManagerRegionSyncPatch.SendRegionPrefixRegistered} " +
                    $"transpilerOwner={SteamP2PFriends.Adapters.Structure.Patches.BarricadeManagerRegionSyncPatch.TranspilerOwnerSummary} " +
                    $"prefixOwner={SteamP2PFriends.Adapters.Structure.Patches.BarricadeManagerRegionSyncPatch.PrefixOwnerSummary}");
            }

            bool structureRegionOk = SteamP2PFriends.Adapters.Structure.Patches.StructureManagerRegionSyncPatch.AllRegistrationsSucceeded;
            if (!structureRegionOk)
            {
                RoleLogger.Error("[Shared]",
                    $"[Diag] !!! DIAGNOSTIC BUILD INVALID: StructureManagerRegionSyncPatch " +
                    $"summary={SteamP2PFriends.Adapters.Structure.Patches.StructureManagerRegionSyncPatch.RegistrationSummary} " +
                    $"replacement={SteamP2PFriends.Adapters.Structure.Patches.StructureManagerRegionSyncPatch.ReplacementCount} " +
                    $"signature={SteamP2PFriends.Adapters.Structure.Patches.StructureManagerRegionSyncPatch.SignatureResolved} " +
                    $"askStructuresPrefix={SteamP2PFriends.Adapters.Structure.Patches.StructureManagerRegionSyncPatch.AskStructuresPrefixRegistered} " +
                    $"transpilerOwner={SteamP2PFriends.Adapters.Structure.Patches.StructureManagerRegionSyncPatch.TranspilerOwnerVerified} " +
                    $"prefixOwner={SteamP2PFriends.Adapters.Structure.Patches.StructureManagerRegionSyncPatch.PrefixOwnerVerified}");
                allOk = false;
            }
            else
            {
                RoleLogger.Info("[Shared]",
                    $"[Diag] OK StructureManagerRegionSyncPatch: " +
                    $"replacement={SteamP2PFriends.Adapters.Structure.Patches.StructureManagerRegionSyncPatch.ReplacementCount}/1 " +
                    $"signature={SteamP2PFriends.Adapters.Structure.Patches.StructureManagerRegionSyncPatch.SignatureResolved} " +
                    $"askStructuresPrefix={SteamP2PFriends.Adapters.Structure.Patches.StructureManagerRegionSyncPatch.AskStructuresPrefixRegistered} " +
                    $"transpilerOwner={SteamP2PFriends.Adapters.Structure.Patches.StructureManagerRegionSyncPatch.TranspilerOwnerSummary} " +
                    $"prefixOwner={SteamP2PFriends.Adapters.Structure.Patches.StructureManagerRegionSyncPatch.PrefixOwnerSummary}");
            }

            bool p0S1S2Ok = Core.Patches.PlayerManagerBroadcastPatch.AllRegistrationsSucceeded;
            if (!p0S1S2Ok)
            {
                RoleLogger.Error("[Shared]",
                    $"[Diag] !!! DIAGNOSTIC BUILD INVALID: PlayerManagerBroadcastPatch " +
                    $"P0-S1={Core.Patches.PlayerManagerBroadcastPatch.P0S1_Registered} " +
                    $"P0-S2={Core.Patches.PlayerManagerBroadcastPatch.P0S2_Registered} " +
                    $"replacementCount={Core.Patches.PlayerManagerBroadcastPatch.P0S1_ReplacementCount} " +
                    $"P0S1_owner={Core.Patches.PlayerManagerBroadcastPatch.P0S1_TranspilerOwnerVerified} " +
                    $"P0S2_owner={Core.Patches.PlayerManagerBroadcastPatch.P0S2_PrefixOwnerVerified} " +
                    $"P0S2_reflection={Core.Patches.PlayerManagerBroadcastPatch.P0S2_ReflectionComplete}");
                allOk = false;
            }
            else
            {
                RoleLogger.Info("[Shared]",
                    $"[Diag] OK PlayerManagerBroadcastPatch: " +
                    $"P0-S1={Core.Patches.PlayerManagerBroadcastPatch.P0S1_Registered} " +
                    $"P0-S2={Core.Patches.PlayerManagerBroadcastPatch.P0S2_Registered} " +
                    $"replacement={Core.Patches.PlayerManagerBroadcastPatch.P0S1_ReplacementCount}/1 " +
                    $"P0S1_owner={Core.Patches.PlayerManagerBroadcastPatch.P0S1_TranspilerOwnerSummary} " +
                    $"P0S2_owner={Core.Patches.PlayerManagerBroadcastPatch.P0S2_PrefixOwnerSummary} " +
                    $"P0S2_reflection={Core.Patches.PlayerManagerBroadcastPatch.P0S2_ReflectionComplete}");
            }

            bool p0S3Ok = Core.Patches.RemotePlayerClothingVisibleBridgePatch.AllRegistrationsSucceeded;
            if (!p0S3Ok)
            {
                RoleLogger.Error("[Shared]",
                    $"[Diag] !!! DIAGNOSTIC BUILD INVALID: RemotePlayerClothingVisibleBridgePatch " +
                    $"P0-S3={Core.Patches.RemotePlayerClothingVisibleBridgePatch.P0S3_Registered} " +
                    $"P0S3_owner={Core.Patches.RemotePlayerClothingVisibleBridgePatch.P0S3_PostfixOwnerVerified} " +
                    $"P0S3_ownerSummary={Core.Patches.RemotePlayerClothingVisibleBridgePatch.P0S3_PostfixOwnerSummary} " +
                    $"P0S3_reflection={Core.Patches.RemotePlayerClothingVisibleBridgePatch.P0S3_ReflectionComplete}");
                allOk = false;
            }
            else
            {
                RoleLogger.Info("[Shared]",
                    $"[Diag] OK RemotePlayerClothingVisibleBridgePatch: " +
                    $"P0-S3={Core.Patches.RemotePlayerClothingVisibleBridgePatch.P0S3_Registered} " +
                    $"P0S3_owner={Core.Patches.RemotePlayerClothingVisibleBridgePatch.P0S3_PostfixOwnerSummary} " +
                    $"P0S3_reflection={Core.Patches.RemotePlayerClothingVisibleBridgePatch.P0S3_ReflectionComplete}");
            }

            bool p1S5Ok = Core.Patches.PlayerManagerBroadcastDiagnosticPatch.AllRegistrationsSucceeded;
            if (!p1S5Ok)
            {
                RoleLogger.Warn("[Shared]",
                    $"[Diag] WARN PlayerManagerBroadcastDiagnosticPatch P1-S5 登记不全（不阻断联机）: " +
                    $"P1-S5={Core.Patches.PlayerManagerBroadcastDiagnosticPatch.P1S5_Registered} " +
                    $"updatePost={Core.Patches.PlayerManagerBroadcastDiagnosticPatch.UpdatePostfixRegistered} " +
                    $"sendPre={Core.Patches.PlayerManagerBroadcastDiagnosticPatch.SendPrefixRegistered} " +
                    $"sendPost={Core.Patches.PlayerManagerBroadcastDiagnosticPatch.SendPostfixRegistered} " +
                    $"sendFinal={Core.Patches.PlayerManagerBroadcastDiagnosticPatch.SendFinalizerRegistered} " +
                    $"receivePost={Core.Patches.PlayerManagerBroadcastDiagnosticPatch.ReceivePostfixRegistered}");
            }
            else
            {
                RoleLogger.Info("[Shared]",
                    $"[Diag] OK PlayerManagerBroadcastDiagnosticPatch: P1-S5={Core.Patches.PlayerManagerBroadcastDiagnosticPatch.P1S5_Registered} " +
                    $"updatePost={Core.Patches.PlayerManagerBroadcastDiagnosticPatch.UpdatePostfixRegistered} " +
                    $"sendPre={Core.Patches.PlayerManagerBroadcastDiagnosticPatch.SendPrefixRegistered} " +
                    $"sendPost={Core.Patches.PlayerManagerBroadcastDiagnosticPatch.SendPostfixRegistered} " +
                    $"sendFinal={Core.Patches.PlayerManagerBroadcastDiagnosticPatch.SendFinalizerRegistered} " +
                    $"receivePost={Core.Patches.PlayerManagerBroadcastDiagnosticPatch.ReceivePostfixRegistered}");
            }

            //   审计 §5 要求：精确验证 Prefix 登记一次
            //   失败时聚合到 DiagnosticBuildValid=false（INVALID 门控）
            try
            {
                if (!Core.Patches.PlayerClothingLoadAppearanceFixPatch.VerifyRegistration())
                {
                    allOk = false;
                }
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[P0-S4] VerifyRegistration 整体异常: {ex.Message}");
                allOk = false;
            }

            //   - 目标类型、方法名、参数类型精确匹配
            //   - 预期 Prefix/Postfix owner 为本插件
            //   - own count 精确等于预期，缺失/重复均失败
            //   - 结果进入 DiagnosticBuildValid 阻断门
            //   任一链路失败即 DiagnosticBuildValid=false
            try
            {
                if (!Adapters.Item.Patches.ItemManagerWorldSyncDiagnosticPatch.VerifyRegistration())
                {
                    allOk = false;
                }
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[WorldSyncDiag/Item] VerifyRegistration 整体异常: {ex.Message}");
                allOk = false;
            }

            try
            {
                if (!Adapters.Item.Patches.AuthoritativeItemGenerationGatePatch.AllRegistrationsSucceeded
                    || !Adapters.Item.Patches.AuthoritativeItemGenerationGatePatch.VerifyRegistration())
                {
                    RoleLogger.Error("[Shared]",
                        $"[ItemAuthorityGate] DIAGNOSTIC BUILD INVALID: {Adapters.Item.Patches.AuthoritativeItemGenerationGatePatch.RegistrationSummary}");
                    allOk = false;
                }
                else
                {
                    RoleLogger.Info("[Shared]",
                        $"[ItemAuthorityGate] registration verified: {Adapters.Item.Patches.AuthoritativeItemGenerationGatePatch.RegistrationSummary}");
                }
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[ItemAuthorityGate] VerifyRegistration exception: {ex.Message}");
                allOk = false;
            }

            try
            {
                if (!Core.Patches.InventoryWorldAuthorityProbe.AllRegistrationsSucceeded
                    || !Core.Patches.InventoryWorldAuthorityProbe.VerifyRegistration())
                {
                    RoleLogger.Error("[Shared]",
                        $"[Alpha-AuthorityProbe] DIAGNOSTIC BUILD INVALID: {Core.Patches.InventoryWorldAuthorityProbe.RegistrationSummary}");
                    allOk = false;
                }
                else
                {
                    RoleLogger.Info("[Shared]",
                        $"[Alpha-AuthorityProbe] registration verified: {Core.Patches.InventoryWorldAuthorityProbe.RegistrationSummary}");
                }
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[Alpha-AuthorityProbe] VerifyRegistration exception: {ex.Message}");
                allOk = false;
            }

            try
            {
                if (!SteamP2PFriends.Adapters.Resource.Patches.ResourceManagerWorldSyncDiagnosticPatch.VerifyRegistration())
                {
                    allOk = false;
                }
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[WorldSyncDiag/Resource] VerifyRegistration 整体异常: {ex.Message}");
                allOk = false;
            }

            try
            {
                if (!Core.Patches.ObjectManagerWorldSyncDiagnosticPatch.VerifyRegistration())
                {
                    allOk = false;
                }
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[WorldSyncDiag/Object] VerifyRegistration 整体异常: {ex.Message}");
                allOk = false;
            }

            try
            {
                if (!Core.Patches.Issue7ObjectBinaryStateDiagnosticPatch.VerifyRegistration())
                {
                    RoleLogger.Error("[Shared]",
                        $"[Issue7/ObjectBinary] DIAGNOSTIC BUILD INVALID: {Core.Patches.Issue7ObjectBinaryStateDiagnosticPatch.RegistrationSummary}");
                    allOk = false;
                }
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[Issue7/ObjectBinary] VerifyRegistration exception: {ex.Message}");
                allOk = false;
            }

            try
            {
                if (!Core.Patches.VehicleManagerWorldSyncDiagnosticPatch.VerifyRegistration())
                {
                    allOk = false;
                }
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[WorldSyncDiag/Vehicle] VerifyRegistration 整体异常: {ex.Message}");
                allOk = false;
            }

            try
            {
                if (!SteamP2PFriends.Adapters.Animal.Patches.AnimalManagerWorldSyncDiagnosticPatch.VerifyRegistration())
                {
                    allOk = false;
                }
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[WorldSyncDiag/Animal] VerifyRegistration 整体异常: {ex.Message}");
                allOk = false;
            }

            try
            {
                if (!Adapters.Zombie.Patches.ZombieManagerWorldSyncDiagnosticPatch.VerifyRegistration())
                {
                    allOk = false;
                }
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[WorldSyncDiag/Zombie] VerifyRegistration 整体异常: {ex.Message}");
                allOk = false;
            }

            //   ZombieManagerP0DGenerateZombiesPatch：onBoundUpdated Prefix supplement
            //   聚合至 DiagnosticBuildValid 阻断门，失败强制 INVALID。
            try
            {
                if (!Adapters.Zombie.Patches.ZombieManagerP0DGenerateZombiesPatch.VerifyRegistration())
                {
                    allOk = false;
                }
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[P0-D/Zombie] VerifyRegistration 整体异常: {ex.Message}");
                allOk = false;
            }

            //   ZombieLifecyclePatch：onBoundUpdated Prefix(VeryLow) + Postfix(High) + Finalizer(High)
            //   含 ZombieLifecycleOwnerVerify 精确 owner/MethodInfo/Priority 自检。
            //   聚合至 DiagnosticBuildValid 阻断门，失败强制 INVALID。
            try
            {
                if (!Adapters.Zombie.Patches.ZombieLifecyclePatch.VerifyRegistration())
                {
                    allOk = false;
                }
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[P0-E-Zombie-v6.6] VerifyRegistration 整体异常: {ex.Message}");
                allOk = false;
            }

            try
            {
                bool m3Ready = Adapters.Zombie.Patches.ZombieManagerP0DGenerateZombiesPatch.AllRegistrationsSucceeded
                    && Adapters.Zombie.Patches.ZombieLifecyclePatch.AllRegistrationsSucceeded;
                Adapters.Zombie.ZombieRegionLifecycleAdapter.SetRegistrationReady(m3Ready);
                if (!m3Ready)
                {
                    RoleLogger.Error("[Shared]", "[MultiObserver/M3-Zombie] DIAGNOSTIC BUILD INVALID: legacy adapter hooks unavailable");
                    allOk = false;
                }
                else
                {
                    RoleLogger.Info("[Shared]", "[MultiObserver/M3-Zombie] registration verified capability=NativeDemand+HysteresisRelease+GenerationGuard");
                }

                RoleLogger.Info("[Shared]",
                    $"[Adapters/Zombie/M4-ZombieSnapshot] registration verified capability={Adapters.Zombie.ZombieSnapshotAdapter.Capability}");
            }
            catch (System.Exception ex)
            {
                Adapters.Zombie.ZombieRegionLifecycleAdapter.SetRegistrationReady(false);
                RoleLogger.Error("[Shared]", $"[MultiObserver/M3-Zombie] VerifyRegistration exception: {ex.Message}");
                allOk = false;
            }

            //   ZombieManagerP0C1SendZombieStatesPatch：updateRegionsAndSendZombieStates Transpiler
            //   聚合至 DiagnosticBuildValid 阻断门，失败强制 INVALID。
            try
            {
                if (!Adapters.Zombie.Patches.ZombieManagerP0C1SendZombieStatesPatch.AllRegistrationsSucceeded)
                {
                    RoleLogger.Error("[Shared]",
                        $"[P0-C-1/Zombie] !!! DIAGNOSTIC BUILD INVALID: summary={Adapters.Zombie.Patches.ZombieManagerP0C1SendZombieStatesPatch.RegistrationSummary} " +
                        $"replacement={Adapters.Zombie.Patches.ZombieManagerP0C1SendZombieStatesPatch.ReplacementCount} " +
                        $"signature={Adapters.Zombie.Patches.ZombieManagerP0C1SendZombieStatesPatch.SignatureResolved} " +
                        $"transpilerOwner={Adapters.Zombie.Patches.ZombieManagerP0C1SendZombieStatesPatch.TranspilerOwnerVerified}");
                    allOk = false;
                }
                else
                {
                    RoleLogger.Info("[Shared]",
                        $"[P0-C-1/Zombie] OK summary={Adapters.Zombie.Patches.ZombieManagerP0C1SendZombieStatesPatch.RegistrationSummary}");
                }
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[P0-C-1/Zombie] VerifyRegistration 整体异常: {ex.Message}");
                allOk = false;
            }

            //   VehicleManagerP0C1ReplicationPatch：Update Transpiler + OnUpdate Postfix
            //   聚合至 DiagnosticBuildValid 阻断门，失败强制 INVALID。
            try
            {
                if (!Core.Patches.VehicleManagerP0C1ReplicationPatch.AllRegistrationsSucceeded)
                {
                    RoleLogger.Error("[Shared]",
                        $"[P0-C-1/Vehicle] !!! DIAGNOSTIC BUILD INVALID: summary={Core.Patches.VehicleManagerP0C1ReplicationPatch.RegistrationSummary} " +
                        $"transpilerReplacement={Core.Patches.VehicleManagerP0C1ReplicationPatch.TranspilerReplacementCount} " +
                        $"signature={Core.Patches.VehicleManagerP0C1ReplicationPatch.UpdateSignatureResolved} " +
                        $"onUpdatePostfix={Core.Patches.VehicleManagerP0C1ReplicationPatch.OnUpdatePostfixRegistered} " +
                        $"transpilerOwner={Core.Patches.VehicleManagerP0C1ReplicationPatch.TranspilerOwnerVerified}");
                    allOk = false;
                }
                else
                {
                    RoleLogger.Info("[Shared]",
                        $"[P0-C-1/Vehicle] OK summary={Core.Patches.VehicleManagerP0C1ReplicationPatch.RegistrationSummary}");
                }
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[P0-C-1/Vehicle] VerifyRegistration 整体异常: {ex.Message}");
                allOk = false;
            }

            //   AnimalManagerP0C2SendAnimalStatesPatch：Update Transpiler
            //   聚合至 DiagnosticBuildValid 阻断门，失败强制 INVALID。
            try
            {
                if (!SteamP2PFriends.Adapters.Animal.Patches.AnimalManagerP0C2SendAnimalStatesPatch.AllRegistrationsSucceeded)
                {
                    RoleLogger.Error("[Shared]",
                        $"[P0-C-2/Animal] !!! DIAGNOSTIC BUILD INVALID: summary={SteamP2PFriends.Adapters.Animal.Patches.AnimalManagerP0C2SendAnimalStatesPatch.RegistrationSummary} " +
                        $"replacement={SteamP2PFriends.Adapters.Animal.Patches.AnimalManagerP0C2SendAnimalStatesPatch.ReplacementCount} " +
                        $"signature={SteamP2PFriends.Adapters.Animal.Patches.AnimalManagerP0C2SendAnimalStatesPatch.SignatureResolved} " +
                        $"transpilerOwner={SteamP2PFriends.Adapters.Animal.Patches.AnimalManagerP0C2SendAnimalStatesPatch.TranspilerOwnerVerified}");
                    allOk = false;
                }
                else
                {
                    RoleLogger.Info("[Shared]",
                        $"[P0-C-2/Animal] OK summary={SteamP2PFriends.Adapters.Animal.Patches.AnimalManagerP0C2SendAnimalStatesPatch.RegistrationSummary}");
                }
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[P0-C-2/Animal] VerifyRegistration 整体异常: {ex.Message}");
                allOk = false;
            }

            // M1: native observer-demand generation must be active before loaded-state commit.
            // The legacy P0-B-3/P0-B-6 full-map listen-host writers are not compiled.
            try
            {
                if (!Adapters.Item.ItemGenerationAuthorityAdapter.IsReady
                    || Adapters.Item.Patches.ItemManagerRegionSyncPatch.GenerationGateReplacementCount != 1)
                {
                    RoleLogger.Error("[Shared]",
                        $"[MultiObserver/M1-Item] DIAGNOSTIC BUILD INVALID: ready={Adapters.Item.ItemGenerationAuthorityAdapter.IsReady} " +
                        $"generationReplacement={Adapters.Item.Patches.ItemManagerRegionSyncPatch.GenerationGateReplacementCount}/1");
                    allOk = false;
                }
                else
                {
                    RoleLogger.Info("[Shared]",
                        "[MultiObserver/M1-Item] registration verified; legacy full-map listen-host generation retired");
                }
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[MultiObserver/M1-Item] VerifyRegistration exception: {ex.Message}");
                allOk = false;
            }

            // M2: native askItems is supervised by a per-observer reliable-enqueue ledger.
            try
            {
                if (!Adapters.Item.ItemObserverReplicationAdapter.IsReady
                    || !Adapters.Item.Patches.ItemManagerRegionSyncPatch.RegionPrefixRegistered
                    || !Adapters.Item.Patches.ItemManagerRegionSyncPatch.AskItemsPrefixRegistered
                    || !Adapters.Item.Patches.ItemManagerRegionSyncPatch.AskItemsPostfixRegistered
                    || !Adapters.Item.Patches.ItemManagerRegionSyncPatch.AskItemsFinalizerRegistered)
                {
                    RoleLogger.Error("[Shared]",
                        $"[MultiObserver/M2-Item] DIAGNOSTIC BUILD INVALID: ready={Adapters.Item.ItemObserverReplicationAdapter.IsReady} " +
                        $"regionPrefix={Adapters.Item.Patches.ItemManagerRegionSyncPatch.RegionPrefixRegistered} " +
                        $"askItems={Adapters.Item.Patches.ItemManagerRegionSyncPatch.AskItemsPrefixRegistered}/" +
                        $"{Adapters.Item.Patches.ItemManagerRegionSyncPatch.AskItemsPostfixRegistered}/" +
                        $"{Adapters.Item.Patches.ItemManagerRegionSyncPatch.AskItemsFinalizerRegistered}");
                    allOk = false;
                }
                else
                {
                    RoleLogger.Info("[Shared]",
                        $"[MultiObserver/M2-Item] registration verified capability={Adapters.Item.ItemObserverReplicationAdapter.Capability}");
                }
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[MultiObserver/M2-Item] VerifyRegistration exception: {ex.Message}");
                allOk = false;
            }

            //   NetMessagesPlayerConnectedLoopbackPatch：SendMessageToClient Prefix
            //   聚合至 DiagnosticBuildValid 阻断门，失败强制 INVALID。
            try
            {
                if (!Core.Patches.NetMessagesPlayerConnectedLoopbackPatch.AllRegistrationsSucceeded)
                {
                    RoleLogger.Error("[Shared]",
                        $"[P0-PlayerVisibility] !!! DIAGNOSTIC BUILD INVALID: summary={Core.Patches.NetMessagesPlayerConnectedLoopbackPatch.RegistrationSummary} " +
                        $"prefix={Core.Patches.NetMessagesPlayerConnectedLoopbackPatch.PrefixRegistered} " +
                        $"prefixOwner={Core.Patches.NetMessagesPlayerConnectedLoopbackPatch.PrefixOwnerVerified}");
                    allOk = false;
                }
                else
                {
                    RoleLogger.Info("[Shared]",
                        $"[P0-PlayerVisibility] OK summary={Core.Patches.NetMessagesPlayerConnectedLoopbackPatch.RegistrationSummary}");
                }
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[P0-PlayerVisibility] VerifyRegistration 整体异常: {ex.Message}");
                allOk = false;
            }

            //   PlayerUIPauseTimeScalePatch：PlayerUI.updatePauseTimeScale Prefix
            //   修复 24th 测试中 timeScale=0.00 持续 33.32s 导致客机世界停滞的问题。
            //   聚合至 DiagnosticBuildValid 阻断门，失败强制 INVALID。
            try
            {
                if (!Core.Patches.PlayerUIPauseTimeScalePatch.AllRegistrationsSucceeded)
                {
                    RoleLogger.Error("[Shared]",
                        $"[P0-D-ESC] !!! DIAGNOSTIC BUILD INVALID: summary={Core.Patches.PlayerUIPauseTimeScalePatch.RegistrationSummary} " +
                        $"prefix={Core.Patches.PlayerUIPauseTimeScalePatch.PrefixRegistered} " +
                        $"prefixOwner={Core.Patches.PlayerUIPauseTimeScalePatch.PrefixOwnerVerified}");
                    allOk = false;
                }
                else
                {
                    RoleLogger.Info("[Shared]",
                        $"[P0-D-ESC] OK summary={Core.Patches.PlayerUIPauseTimeScalePatch.RegistrationSummary}");
                }
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[P0-D-ESC] VerifyRegistration 整体异常: {ex.Message}");
                allOk = false;
            }

            //   VehicleEnterDiagnosticPatch：VehicleManager.enterVehicle + ReceiveEnterVehicleRequest Prefix
            //   仅诊断日志，不修改 vanilla 验证逻辑。
            //   聚合至 DiagnosticBuildValid 阻断门，失败强制 INVALID。
            try
            {
                if (!Core.Patches.VehicleEnterDiagnosticPatch.AllRegistrationsSucceeded)
                {
                    RoleLogger.Error("[Shared]",
                        $"[P0-C-1-V-a] !!! DIAGNOSTIC BUILD INVALID: summary={Core.Patches.VehicleEnterDiagnosticPatch.RegistrationSummary} " +
                        $"enterVehicle={Core.Patches.VehicleEnterDiagnosticPatch.EnterVehiclePrefixRegistered} " +
                        $"receiveEnterVehicleRequest={Core.Patches.VehicleEnterDiagnosticPatch.ReceiveEnterVehicleRequestPrefixRegistered}");
                    allOk = false;
                }
                else
                {
                    RoleLogger.Info("[Shared]",
                        $"[P0-C-1-V-a] OK summary={Core.Patches.VehicleEnterDiagnosticPatch.RegistrationSummary}");
                }
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[P0-C-1-V-a] VerifyRegistration 整体异常: {ex.Message}");
                allOk = false;
            }

            //   - ItemManagerRegionSyncPatch
            //   - ResourceManagerRegionSyncPatch
            //   - ObjectManagerRegionSyncPatch
            //   每个要求：signature=true, replacement=1/1, prefix=true, transpilerOwner=true, prefixOwner=true
            //   任一失败强制 DiagnosticBuildValid=false
            bool itemRegionOk = Adapters.Item.Patches.ItemManagerRegionSyncPatch.AllRegistrationsSucceeded;
            if (!itemRegionOk)
            {
                RoleLogger.Error("[Shared]",
                    $"[Diag] !!! DIAGNOSTIC BUILD INVALID: ItemManagerRegionSyncPatch " +
                    $"summary={Adapters.Item.Patches.ItemManagerRegionSyncPatch.RegistrationSummary} " +
                    $"sendReplacement={Adapters.Item.Patches.ItemManagerRegionSyncPatch.ReplacementCount} " +
                    $"generationReplacement={Adapters.Item.Patches.ItemManagerRegionSyncPatch.GenerationGateReplacementCount} " +
                    $"signature={Adapters.Item.Patches.ItemManagerRegionSyncPatch.SignatureResolved} " +
                    $"regionPrefix={Adapters.Item.Patches.ItemManagerRegionSyncPatch.RegionPrefixRegistered} " +
                    $"askItemsTransaction={Adapters.Item.Patches.ItemManagerRegionSyncPatch.AskItemsPrefixRegistered}/" +
                    $"{Adapters.Item.Patches.ItemManagerRegionSyncPatch.AskItemsPostfixRegistered}/" +
                    $"{Adapters.Item.Patches.ItemManagerRegionSyncPatch.AskItemsFinalizerRegistered} " +
                    $"transpilerOwner={Adapters.Item.Patches.ItemManagerRegionSyncPatch.TranspilerOwnerVerified} " +
                    $"prefixOwner={Adapters.Item.Patches.ItemManagerRegionSyncPatch.PrefixOwnerVerified}");
                allOk = false;
            }
            else
            {
                RoleLogger.Info("[Shared]",
                    $"[Diag] OK ItemManagerRegionSyncPatch: " +
                    $"sendReplacement={Adapters.Item.Patches.ItemManagerRegionSyncPatch.ReplacementCount}/1 " +
                    $"generationReplacement={Adapters.Item.Patches.ItemManagerRegionSyncPatch.GenerationGateReplacementCount}/1 " +
                    $"signature={Adapters.Item.Patches.ItemManagerRegionSyncPatch.SignatureResolved} " +
                    $"regionPrefix={Adapters.Item.Patches.ItemManagerRegionSyncPatch.RegionPrefixRegistered} " +
                    $"askItemsTransaction={Adapters.Item.Patches.ItemManagerRegionSyncPatch.AskItemsPrefixRegistered}/" +
                    $"{Adapters.Item.Patches.ItemManagerRegionSyncPatch.AskItemsPostfixRegistered}/" +
                    $"{Adapters.Item.Patches.ItemManagerRegionSyncPatch.AskItemsFinalizerRegistered} " +
                    $"transpilerOwner={Adapters.Item.Patches.ItemManagerRegionSyncPatch.TranspilerOwnerSummary} " +
                    $"prefixOwner={Adapters.Item.Patches.ItemManagerRegionSyncPatch.PrefixOwnerSummary}");
            }

            bool resourceRegionOk = SteamP2PFriends.Adapters.Resource.Patches.ResourceManagerRegionSyncPatch.AllRegistrationsSucceeded;
            if (!resourceRegionOk)
            {
                RoleLogger.Error("[Shared]",
                    $"[Diag] !!! DIAGNOSTIC BUILD INVALID: ResourceManagerRegionSyncPatch " +
                    $"summary={SteamP2PFriends.Adapters.Resource.Patches.ResourceManagerRegionSyncPatch.RegistrationSummary} " +
                    $"replacement={SteamP2PFriends.Adapters.Resource.Patches.ResourceManagerRegionSyncPatch.ReplacementCount} " +
                    $"signature={SteamP2PFriends.Adapters.Resource.Patches.ResourceManagerRegionSyncPatch.SignatureResolved} " +
                    $"sendResourcesWritePrefix={SteamP2PFriends.Adapters.Resource.Patches.ResourceManagerRegionSyncPatch.SendResourcesWritePrefixRegistered} " +
                    $"transpilerOwner={SteamP2PFriends.Adapters.Resource.Patches.ResourceManagerRegionSyncPatch.TranspilerOwnerVerified} " +
                    $"prefixOwner={SteamP2PFriends.Adapters.Resource.Patches.ResourceManagerRegionSyncPatch.PrefixOwnerVerified}");
                allOk = false;
            }
            else
            {
                RoleLogger.Info("[Shared]",
                    $"[Diag] OK ResourceManagerRegionSyncPatch: " +
                    $"replacement={SteamP2PFriends.Adapters.Resource.Patches.ResourceManagerRegionSyncPatch.ReplacementCount}/1 " +
                    $"signature={SteamP2PFriends.Adapters.Resource.Patches.ResourceManagerRegionSyncPatch.SignatureResolved} " +
                    $"sendResourcesWritePrefix={SteamP2PFriends.Adapters.Resource.Patches.ResourceManagerRegionSyncPatch.SendResourcesWritePrefixRegistered} " +
                    $"transpilerOwner={SteamP2PFriends.Adapters.Resource.Patches.ResourceManagerRegionSyncPatch.TranspilerOwnerSummary} " +
                    $"prefixOwner={SteamP2PFriends.Adapters.Resource.Patches.ResourceManagerRegionSyncPatch.PrefixOwnerSummary}");
            }

            bool objectRegionOk = Core.Patches.ObjectManagerRegionSyncPatch.AllRegistrationsSucceeded;
            if (!objectRegionOk)
            {
                RoleLogger.Error("[Shared]",
                    $"[Diag] !!! DIAGNOSTIC BUILD INVALID: ObjectManagerRegionSyncPatch " +
                    $"summary={Core.Patches.ObjectManagerRegionSyncPatch.RegistrationSummary} " +
                    $"replacement={Core.Patches.ObjectManagerRegionSyncPatch.ReplacementCount} " +
                    $"signature={Core.Patches.ObjectManagerRegionSyncPatch.SignatureResolved} " +
                    $"askObjectsPrefix={Core.Patches.ObjectManagerRegionSyncPatch.AskObjectsPrefixRegistered} " +
                    $"transpilerOwner={Core.Patches.ObjectManagerRegionSyncPatch.TranspilerOwnerVerified} " +
                    $"prefixOwner={Core.Patches.ObjectManagerRegionSyncPatch.PrefixOwnerVerified}");
                allOk = false;
            }
            else
            {
                RoleLogger.Info("[Shared]",
                    $"[Diag] OK ObjectManagerRegionSyncPatch: " +
                    $"replacement={Core.Patches.ObjectManagerRegionSyncPatch.ReplacementCount}/1 " +
                    $"signature={Core.Patches.ObjectManagerRegionSyncPatch.SignatureResolved} " +
                    $"askObjectsPrefix={Core.Patches.ObjectManagerRegionSyncPatch.AskObjectsPrefixRegistered} " +
                    $"transpilerOwner={Core.Patches.ObjectManagerRegionSyncPatch.TranspilerOwnerSummary} " +
                    $"prefixOwner={Core.Patches.ObjectManagerRegionSyncPatch.PrefixOwnerSummary}");
            }

            bool levelObjectCollisionOk = SteamP2PFriends.Adapters.Collision.Patches.LevelObjectRemoteCollisionPatch.AllRegistrationsSucceeded;
            if (!levelObjectCollisionOk)
            {
                RoleLogger.Error("[Shared]",
                    $"[Diag] !!! DIAGNOSTIC BUILD INVALID: LevelObjectRemoteCollisionPatch " +
                    $"summary={SteamP2PFriends.Adapters.Collision.Patches.LevelObjectRemoteCollisionPatch.RegistrationSummary} " +
                    $"rootPostfix={SteamP2PFriends.Adapters.Collision.Patches.LevelObjectRemoteCollisionPatch.RootActivationPostfixRegistered} " +
                    $"regionTrackerPostfix={SteamP2PFriends.Adapters.Collision.Patches.LevelObjectRemoteCollisionPatch.RegionTrackerPostfixRegistered}");
                allOk = false;
            }
            else
            {
                RoleLogger.Info("[Shared]",
                    $"[Diag] OK LevelObjectRemoteCollisionPatch: " +
                    $"summary={SteamP2PFriends.Adapters.Collision.Patches.LevelObjectRemoteCollisionPatch.RegistrationSummary}");
            }

            //   - UseableBarricadeDiagnosticPatch：8 DP（startPrimary/check/checkSpace/checkClaims/ReceiveBarricadeNone/simulate/build/dropBarricade）
            //   - ZombieEntityMappingDiagnosticPatch：7 DP（SendZombies/ReceiveZombies/SendZombieStates/ReceiveZombieStates/onBoundUpdated/sendZombieDead+Alive/ReceiveZombieDead+Alive）
            //   - PlayerManagerCullingDiagnosticPatch：3 DP（SendPlayerStates_Write Prefix/ReceivePlayerStates Postfix/tellState Prefix）
            //   任一失败强制 DiagnosticBuildValid=false，聚合至阻断门。
            try
            {
                if (!Core.Patches.P0EDiagnostic.UseableBarricadeDiagnosticPatch.AllRegistrationsSucceeded)
                {
                    RoleLogger.Error("[Shared]",
                        $"[P0-E-2-Diag] !!! DIAGNOSTIC BUILD INVALID: UseableBarricadeDiagnosticPatch " +
                        $"dp1={Core.Patches.P0EDiagnostic.UseableBarricadeDiagnosticPatch.DP1_StartPrimary_Registered} " +
                        $"dp2={Core.Patches.P0EDiagnostic.UseableBarricadeDiagnosticPatch.DP2_Check_Registered} " +
                        $"dp3={Core.Patches.P0EDiagnostic.UseableBarricadeDiagnosticPatch.DP3_CheckSpace_Registered} " +
                        $"dp4={Core.Patches.P0EDiagnostic.UseableBarricadeDiagnosticPatch.DP4_CheckClaims_Registered} " +
                        $"dp5={Core.Patches.P0EDiagnostic.UseableBarricadeDiagnosticPatch.DP5_ReceiveBarricadeNone_Registered} " +
                        $"dp5Finalizer={Core.Patches.P0EDiagnostic.UseableBarricadeDiagnosticPatch.DP5_Finalizer_Registered} " +
                        $"owner5Finalizer={Core.Patches.P0EDiagnostic.UseableBarricadeDiagnosticPatch.DP5_Finalizer_OwnerVerified} " +
                        $"ownerSummary=\"{Core.Patches.P0EDiagnostic.UseableBarricadeDiagnosticPatch.DP5_Finalizer_OwnerSummary}\" " +
                        $"dp6={Core.Patches.P0EDiagnostic.UseableBarricadeDiagnosticPatch.DP6_Simulate_Registered} " +
                        $"dp7={Core.Patches.P0EDiagnostic.UseableBarricadeDiagnosticPatch.DP7_Build_Registered} " +
                        $"dp8={Core.Patches.P0EDiagnostic.UseableBarricadeDiagnosticPatch.DP8_DropBarricade_Registered}");
                    allOk = false;
                }
                else
                {
                    RoleLogger.Info("[Shared]",
                        $"[P0-E-2-Diag] OK " +
                        $"dp1={Core.Patches.P0EDiagnostic.UseableBarricadeDiagnosticPatch.DP1_StartPrimary_Registered} " +
                        $"dp2={Core.Patches.P0EDiagnostic.UseableBarricadeDiagnosticPatch.DP2_Check_Registered} " +
                        $"dp3={Core.Patches.P0EDiagnostic.UseableBarricadeDiagnosticPatch.DP3_CheckSpace_Registered} " +
                        $"dp4={Core.Patches.P0EDiagnostic.UseableBarricadeDiagnosticPatch.DP4_CheckClaims_Registered} " +
                        $"dp5={Core.Patches.P0EDiagnostic.UseableBarricadeDiagnosticPatch.DP5_ReceiveBarricadeNone_Registered} " +
                        $"dp5Finalizer={Core.Patches.P0EDiagnostic.UseableBarricadeDiagnosticPatch.DP5_Finalizer_Registered} " +
                        $"owner5Finalizer={Core.Patches.P0EDiagnostic.UseableBarricadeDiagnosticPatch.DP5_Finalizer_OwnerVerified} " +
                        $"ownerSummary=\"{Core.Patches.P0EDiagnostic.UseableBarricadeDiagnosticPatch.DP5_Finalizer_OwnerSummary}\" " +
                        $"dp6={Core.Patches.P0EDiagnostic.UseableBarricadeDiagnosticPatch.DP6_Simulate_Registered} " +
                        $"dp7={Core.Patches.P0EDiagnostic.UseableBarricadeDiagnosticPatch.DP7_Build_Registered} " +
                        $"dp8={Core.Patches.P0EDiagnostic.UseableBarricadeDiagnosticPatch.DP8_DropBarricade_Registered}");
                }
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[P0-E-2-Diag] VerifyRegistration 整体异常: {ex.Message}");
                allOk = false;
            }
            try
            {
                if (!Adapters.Zombie.Patches.ZombieEntityMappingDiagnosticPatch.AllRegistrationsSucceeded)
                {
                    RoleLogger.Error("[Shared]",
                        $"[P0-E-1-Diag/Zombie] !!! DIAGNOSTIC BUILD INVALID: ZombieEntityMappingDiagnosticPatch " +
                        $"dp1={Adapters.Zombie.Patches.ZombieEntityMappingDiagnosticPatch.DP1_SendZombiesWrite_Registered} " +
                        $"dp2={Adapters.Zombie.Patches.ZombieEntityMappingDiagnosticPatch.DP2_ReceiveZombies_Registered} " +
                        $"dp3={Adapters.Zombie.Patches.ZombieEntityMappingDiagnosticPatch.DP3_SendZombieStatesWrite_Registered} " +
                        $"dp4={Adapters.Zombie.Patches.ZombieEntityMappingDiagnosticPatch.DP4_ReceiveZombieStates_Registered} " +
                        $"dp5={Adapters.Zombie.Patches.ZombieEntityMappingDiagnosticPatch.DP5_OnBoundUpdated_Registered} " +
                        $"dp6={Adapters.Zombie.Patches.ZombieEntityMappingDiagnosticPatch.DP6_SendZombieDead_Registered} " +
                        $"dp7={Adapters.Zombie.Patches.ZombieEntityMappingDiagnosticPatch.DP7_ReceiveZombieDead_Registered} " +
                        $"dp8_7={Adapters.Zombie.Patches.ZombieEntityMappingDiagnosticPatch.DP8_7_Destroy_Registered} " +
                        $"owner8_7={Adapters.Zombie.Patches.ZombieEntityMappingDiagnosticPatch.DP8_7_Destroy_OwnerVerified} " +
                        $"reflectionFailed={Adapters.Zombie.Patches.ZombieEntityMappingDiagnosticPatch.ReflectionFailed}");
                    allOk = false;
                }
                else
                {
                    RoleLogger.Info("[Shared]",
                        $"[P0-E-1-Diag/Zombie] OK " +
                        $"dp1={Adapters.Zombie.Patches.ZombieEntityMappingDiagnosticPatch.DP1_SendZombiesWrite_Registered} " +
                        $"dp2={Adapters.Zombie.Patches.ZombieEntityMappingDiagnosticPatch.DP2_ReceiveZombies_Registered} " +
                        $"dp3={Adapters.Zombie.Patches.ZombieEntityMappingDiagnosticPatch.DP3_SendZombieStatesWrite_Registered} " +
                        $"dp4={Adapters.Zombie.Patches.ZombieEntityMappingDiagnosticPatch.DP4_ReceiveZombieStates_Registered} " +
                        $"dp5={Adapters.Zombie.Patches.ZombieEntityMappingDiagnosticPatch.DP5_OnBoundUpdated_Registered} " +
                        $"dp6={Adapters.Zombie.Patches.ZombieEntityMappingDiagnosticPatch.DP6_SendZombieDead_Registered} " +
                        $"dp7={Adapters.Zombie.Patches.ZombieEntityMappingDiagnosticPatch.DP7_ReceiveZombieDead_Registered} " +
                        $"dp8_7={Adapters.Zombie.Patches.ZombieEntityMappingDiagnosticPatch.DP8_7_Destroy_Registered} " +
                        $"owner8_7={Adapters.Zombie.Patches.ZombieEntityMappingDiagnosticPatch.DP8_7_Destroy_OwnerVerified} " +
                        $"reflectionFailed={Adapters.Zombie.Patches.ZombieEntityMappingDiagnosticPatch.ReflectionFailed}");
                }
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[P0-E-1-Diag/Zombie] VerifyRegistration 整体异常: {ex.Message}");
                allOk = false;
            }
            try
            {
                if (!Core.Patches.P0EDiagnostic.PlayerManagerCullingDiagnosticPatch.AllRegistrationsSucceeded)
                {
                    RoleLogger.Error("[Shared]",
                        $"[P0-E-1-Diag/Culling] !!! DIAGNOSTIC BUILD INVALID: PlayerManagerCullingDiagnosticPatch " +
                        $"dp1={Core.Patches.P0EDiagnostic.PlayerManagerCullingDiagnosticPatch.DP1_SendPlayerStatesWritePrefix_Registered} " +
                        $"dp2={Core.Patches.P0EDiagnostic.PlayerManagerCullingDiagnosticPatch.DP2_ReceivePlayerStatesPostfix_Registered} " +
                        $"dp3={Core.Patches.P0EDiagnostic.PlayerManagerCullingDiagnosticPatch.DP3_TellStatePrefix_Registered}");
                    allOk = false;
                }
                else
                {
                    RoleLogger.Info("[Shared]",
                        $"[P0-E-1-Diag/Culling] OK " +
                        $"dp1={Core.Patches.P0EDiagnostic.PlayerManagerCullingDiagnosticPatch.DP1_SendPlayerStatesWritePrefix_Registered} " +
                        $"dp2={Core.Patches.P0EDiagnostic.PlayerManagerCullingDiagnosticPatch.DP2_ReceivePlayerStatesPostfix_Registered} " +
                        $"dp3={Core.Patches.P0EDiagnostic.PlayerManagerCullingDiagnosticPatch.DP3_TellStatePrefix_Registered}");
                }
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[P0-E-1-Diag/Culling] VerifyRegistration 整体异常: {ex.Message}");
                allOk = false;
            }

            //   BarricadeLifecycleRegistration 原子登记 + owner/priority/ReplacementApplied 自检
            //   失败 fail-closed（DiagnosticBuildValid=false），聚合至阻断门。
            //   仅两个 Transpiler：equip + checkClaims；不全局伪造 Dedicator.IsDedicatedServer。
            try
            {
                if (!SteamP2PFriends.Adapters.Structure.Patches.P0EBarricadeLifecycle.BarricadeLifecycleRegistration.DiagnosticBuildValid)
                {
                    RoleLogger.Error("[Shared]",
                        $"[5B-1B] !!! DIAGNOSTIC BUILD INVALID: BarricadeLifecycleRegistration " +
                        $"registrationSucceeded={SteamP2PFriends.Adapters.Structure.Patches.P0EBarricadeLifecycle.BarricadeLifecycleRegistration.IsRegistrationSucceeded} " +
                        $"rollbackAttempted={SteamP2PFriends.Adapters.Structure.Patches.P0EBarricadeLifecycle.BarricadeLifecycleRegistration.WasRollbackAttempted} " +
                        $"rollbackClean={SteamP2PFriends.Adapters.Structure.Patches.P0EBarricadeLifecycle.BarricadeLifecycleRegistration.IsRollbackClean} " +
                        $"equipReplacementApplied={SteamP2PFriends.Adapters.Structure.Patches.P0EBarricadeLifecycle.BarricadeLifecycleRegistration.EquipReplacementApplied} " +
                        $"checkClaimsReplacementApplied={SteamP2PFriends.Adapters.Structure.Patches.P0EBarricadeLifecycle.BarricadeLifecycleRegistration.CheckClaimsReplacementApplied}");
                    allOk = false;
                }
                else
                {
                    RoleLogger.Info("[Shared]",
                        $"[5B-1B] OK BarricadeLifecycleRegistration " +
                        $"registrationSucceeded={SteamP2PFriends.Adapters.Structure.Patches.P0EBarricadeLifecycle.BarricadeLifecycleRegistration.IsRegistrationSucceeded} " +
                        $"equipReplacementApplied={SteamP2PFriends.Adapters.Structure.Patches.P0EBarricadeLifecycle.BarricadeLifecycleRegistration.EquipReplacementApplied} " +
                        $"checkClaimsReplacementApplied={SteamP2PFriends.Adapters.Structure.Patches.P0EBarricadeLifecycle.BarricadeLifecycleRegistration.CheckClaimsReplacementApplied}");
                }
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[5B-1B] VerifyRegistration 整体异常: {ex.Message}");
                allOk = false;
            }

            try
            {
                if (!Stage76QuarantineRegistrationValid)
                {
                    RoleLogger.Error("[Shared]",
                        "[Stage7-6] !!! DIAGNOSTIC BUILD INVALID: quarantine admission/UI registration gate failed");
                    allOk = false;
                }
                else
                {
                    RoleLogger.Info("[Shared]",
                    "[P2P-Approval] OK Route B base gates; lifecycle hook installation is pending game-thread Update");
                }
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", "[Stage7-6] registration aggregate failed: " + ex.Message);
                allOk = false;
            }

            try
            {
                if (!Stage78UnifiedRegistrationValid)
                {
                    RoleLogger.Error("[Shared]",
                        "[Stage7-8] !!! DIAGNOSTIC BUILD INVALID: unified connect registration gate failed");
                    allOk = false;
                }
                else
                {
                    RoleLogger.Info("[Shared]",
                        "[Stage7-8] OK original connect route preserved; individual SteamID interception active");
                }
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", "[Stage7-8] registration aggregate failed: " + ex.Message);
                allOk = false;
            }

            try
            {
                if (!Stage92SinglePortRegistrationValid)
                {
                    RoleLogger.Error("[Shared]",
                        "[Stage9-2] !!! DIAGNOSTIC BUILD INVALID: single-port Direct-IP query projection gate failed");
                    allOk = false;
                }
                else
                {
                    RoleLogger.Info("[Shared]",
                        "[Stage9-2] OK single-port Direct-IP query projection active");
                }
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", "[Stage9-2] registration aggregate failed: " + ex.Message);
                allOk = false;
            }

            // makes the diagnostic build invalid.
            try
            {
                if (!VerifyStage10DeathCommitRegistration())
                {
                    allOk = false;
                }
                if (!VerifyStage10ChatAvatarRegistration())
                {
                    allOk = false;
                }
                if (!VerifyInventoryUiProjectionRegistration())
                {
                    allOk = false;
                }
                if (!Stage10WorldBroadcastActivationValid ||
                    P2PWorldStatusBroadcaster.ActivationState ==
                        P2PWorldStatusBroadcaster.EWorldBroadcastActivationState.Failed)
                {
                    RoleLogger.Error("[Shared]",
                        "[Stage10] !!! DIAGNOSTIC BUILD INVALID: world-status broadcast activation failed " +
                        "(deferred subscribe to PlayerLife.onPlayerDied failed)");
                    allOk = false;
                }
                else
                {
                    RoleLogger.Info("[Shared]",
                        "[Stage10] OK world-status broadcast activation state=" +
                        P2PWorldStatusBroadcaster.ActivationState);
                }
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", "[Stage10] activation aggregate failed: " + ex.Message);
                allOk = false;
            }

            DiagnosticBuildValid = allOk;

            try
            {
                HarmonyCompatibilityAudit.WriteReport(DiagnosticBuildValid);
            }
            catch (System.Exception ex)
            {
                // Compatibility evidence is diagnostic output. A filesystem failure must not change P2P behavior.
                RoleLogger.Warn("[Shared]", "[Compat] failed to write compatibility report: " + ex.Message);
            }

            // 原生 SNS 输出是可选日志源，不能影响连接、白名单或世界同步的功能门控。
            try
            {
                bool routeDiagOn = RouteDiagnostics != null && RouteDiagnostics.Value;
                bool verboseOn = VerboseLog != null && VerboseLog.Value;
                if (routeDiagOn && verboseOn)
                {
                    if (SteamP2PFriends.Client.NativeSnsLogProbe.EnableFailed)
                    {
                        RoleLogger.Warn("[Shared]",
                            $"[Diag] NativeSnsLogProbe 不可用，继续使用常规日志：" +
                            $"(IsEnabled={SteamP2PFriends.Client.NativeSnsLogProbe.IsEnabled}, " +
                            $"EnableFailed={SteamP2PFriends.Client.NativeSnsLogProbe.EnableFailed})");
                    }
                    else if (!SteamP2PFriends.Client.NativeSnsLogProbe.IsEnabled
                             && SteamP2PFriends.Client.NativeSnsLogProbe.WaitingForSteamworks)
                    {
                        RoleLogger.Info("[Shared]",
                            "[Diag] NativeSnsLogProbe 等待 Steamworks 初始化后重试。");
                    }
                    else if (!SteamP2PFriends.Client.NativeSnsLogProbe.IsEnabled)
                    {
                        RoleLogger.Warn("[Shared]",
                            $"[Diag] NativeSnsLogProbe 未启用，继续使用常规日志：" +
                            $"(IsEnabled={SteamP2PFriends.Client.NativeSnsLogProbe.IsEnabled}, " +
                            $"EnableFailed={SteamP2PFriends.Client.NativeSnsLogProbe.EnableFailed}, " +
                            $"WaitingForSteamworks={SteamP2PFriends.Client.NativeSnsLogProbe.WaitingForSteamworks})");
                    }
                    else
                    {
                        RoleLogger.Info("[Shared]",
                            "[Diag] NativeSnsLogProbe 已启用。");
                    }
                }
            }
            catch (System.Exception ex)
            {
                RoleLogger.Warn("[Shared]", $"[Diag] NativeSnsLogProbe 状态读取异常（不阻断）: {ex.Message}");
            }

            if (allOk)
            {
                RoleLogger.Info("[Shared]", "[Diag] 关键补丁自检通过 (DiagnosticBuildValid=true)");
            }
            else
            {
                RoleLogger.Error("[Shared]", "========================================");
                RoleLogger.Error("[Shared]", "!!! DIAGNOSTIC BUILD INVALID !!!");
                RoleLogger.Error("[Shared]", "!!! 关键 Patch 未登记、重复或存在策略阻断的冲突，已禁用所有 P2P 入口 !!!");
                RoleLogger.Error("[Shared]", "!!! Update/OnGUI 已挂起，禁止进入双机测试 !!!");
                RoleLogger.Error("[Shared]", "!!! 请检查 Harmony 目标签名 / 程序集版本 / compatibility report !!!");
                RoleLogger.Error("[Shared]", "========================================");
                //   新增 Item/Resource/Object RegionSync Transpiler + Prefix 精确 1/1 自检。
                //   目标：解除 listen server 模式下"主机不向远程客机发送 Item/Resource/Object RPC"的诅咒。
                //   INVALID 时仅通过 DiagnosticBuildValid 临时阻断所有 P2P 入口（OnGUI/Update 顶部
                //   if (!DiagnosticBuildValid) return; 已是硬门控），不再永久篡改 EnableP2PCoop.Value。
                //   原因：EnableP2PCoop.Value = false 会通过 BepInEx ConfigEntry setter 自动持久化到
                //   com.yu80rice.steamp2pfriends.cfg，单向不可逆，下次启动 VALID 时不会自动恢复 true，
                //   篡改了用户合法配置。INVALID 时 cfg 保持用户设定值，VALID 时自然恢复。
            }

            return allOk;
        }

        /// <summary>
        /// NetMessages 是 internal class，编译时无法 typeof()，运行时通过 AccessTools.TypeByName 反射。
        /// 实际登记：SendMessageToClient + SendMessageToClients (Prefix + Finalizer)
        /// </summary>
    }
}
