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
using SteamP2PFriends.Patches;
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
        private bool ApplyV2AuditFixPatches()
        {
            return ApplyV2AuditFixPatches_Core();
        }

        /// <summary>
        ///
        /// 但 PatchAll 在处理泛型类 ClientStaticMethod&lt;...&gt;.Invoke 的 attribute 时静默失败，
        /// 导致两个 Prefix 都未注册到 vanilla 方法上（第九次-4 双机测试确认）。
        ///
        /// 修复方案：改为 manual registration，与 14 个 SteamGameServerNetworkingSockets wrapper patch 一致。
        ///
        ///   1. _harmony.GetPatchedMethods() + Harmony.GetPatchInfo() 双重验证
        ///   2. Server-side Prefix 签名验证（methodInfo.DeclaringType + GetParameters()）
        ///   3. 整体 try-catch 包裹，任一登记失败都会回传给当前注册阶段并触发 fail-closed
        /// </summary>
        private bool RegisterAssetIntegritySnapshotPatches()
        {
            bool ok = true;
            RoleLogger.Info("[Shared]", "[Diag] === v0.2.3.16 AssetIntegritySnapshot 手动登记（ServerInvokePrefix 参数名 arg1..arg6 匹配 vanilla）===");

            // ===== Client-side: Assets.ReceiveKickForHashMismatch =====
            try
            {
                System.Reflection.MethodInfo clientTarget = AccessTools.Method(
                    typeof(SDG.Unturned.Assets),
                    "ReceiveKickForHashMismatch",
                    new System.Type[] {
                        typeof(System.Guid), typeof(string), typeof(string),
                        typeof(byte[]), typeof(string), typeof(string)
                    });

                if (clientTarget == null)
                {
                    ok = false;
                    RoleLogger.Error("[Shared]",
                        "[ManualPatch] FAIL AssetIntegritySnapshot/Client: AccessTools.Method returned null");
                }
                else
                {
                    // 签名验证（审计员要求）：输出 DeclaringType + Parameters
                    RoleLogger.Info("[Shared]",
                        $"[ManualPatch] AssetIntegritySnapshot/Client target resolved: declaringType={clientTarget.DeclaringType?.FullName} " +
                        $"params=[{string.Join(", ", System.Array.ConvertAll(clientTarget.GetParameters(), p => p.ParameterType.Name + " " + p.Name))}]");

                    System.Reflection.MethodInfo clientPrefix = AccessTools.Method(
                        typeof(Patches.AssetIntegritySnapshotPatch),
                        "ClientReceiveKickPrefix");

                    if (clientPrefix == null)
                    {
                        ok = false;
                        RoleLogger.Error("[Shared]",
                            "[ManualPatch] FAIL AssetIntegritySnapshot/Client: prefix MethodInfo null");
                    }
                    else
                    {
                        _harmony.Patch(clientTarget, prefix: new HarmonyMethod(clientPrefix));

                        // 双重验证 1：Harmony.GetPatchInfo
                        HarmonyLib.Patches patchInfo = Harmony.GetPatchInfo(clientTarget);
                        int prefixCount = patchInfo?.Prefixes?.Count ?? 0;
                        RoleLogger.Info("[Shared]",
                            $"[ManualPatch] OK AssetIntegritySnapshot/Client 已手动登记 (prefixes={prefixCount})");

                        // 双重验证 2：_harmony.GetPatchedMethods() 包含检查
                        bool contains = false;
                        foreach (var pm in _harmony.GetPatchedMethods())
                        {
                            if (pm == clientTarget) { contains = true; break; }
                        }
                        if (contains)
                        {
                            RoleLogger.Info("[Shared]",
                                "[ManualPatch] 验证 OK: Assets.ReceiveKickForHashMismatch 已 patch（GetPatchedMethods 包含）");
                        }
                        else
                        {
                            ok = false;
                            RoleLogger.Error("[Shared]",
                                "[ManualPatch] 验证 FAIL: Assets.ReceiveKickForHashMismatch 未 patch（GetPatchedMethods 不包含）");
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"[ManualPatch] FAIL AssetIntegritySnapshot/Client: {ex}");
            }

            // ===== Server-side: ClientStaticMethod<Guid,string,string,byte[],string,string>.Invoke =====
            try
            {
                System.Type genericType = typeof(SDG.Unturned.ClientStaticMethod<
                    System.Guid, string, string, byte[], string, string>);

                System.Reflection.MethodInfo serverTarget = AccessTools.Method(
                    genericType,
                    "Invoke",
                    new System.Type[] {
                        typeof(SDG.NetTransport.ENetReliability),
                        typeof(SDG.NetTransport.ITransportConnection),
                        typeof(System.Guid), typeof(string), typeof(string),
                        typeof(byte[]), typeof(string), typeof(string)
                    });

                if (serverTarget == null)
                {
                    ok = false;
                    RoleLogger.Error("[Shared]",
                        "[ManualPatch] FAIL AssetIntegritySnapshot/Server: AccessTools.Method returned null");
                }
                else
                {
                    // 签名验证（审计员要求）：输出 DeclaringType + Parameters
                    RoleLogger.Info("[Shared]",
                        $"[ManualPatch] AssetIntegritySnapshot/Server target resolved: declaringType={serverTarget.DeclaringType?.FullName} " +
                        $"params=[{string.Join(", ", System.Array.ConvertAll(serverTarget.GetParameters(), p => p.ParameterType.Name + " " + p.Name))}]");

                    System.Reflection.MethodInfo serverPrefix = AccessTools.Method(
                        typeof(Patches.AssetIntegritySnapshotPatch),
                        "ServerInvokePrefix");

                    if (serverPrefix == null)
                    {
                        ok = false;
                        RoleLogger.Error("[Shared]",
                            "[ManualPatch] FAIL AssetIntegritySnapshot/Server: prefix MethodInfo null");
                    }
                    else
                    {
                        _harmony.Patch(serverTarget, prefix: new HarmonyMethod(serverPrefix));

                        // 双重验证 1：Harmony.GetPatchInfo
                        HarmonyLib.Patches patchInfo = Harmony.GetPatchInfo(serverTarget);
                        int prefixCount = patchInfo?.Prefixes?.Count ?? 0;
                        RoleLogger.Info("[Shared]",
                            $"[ManualPatch] OK AssetIntegritySnapshot/Server 已手动登记 (prefixes={prefixCount})");

                        // 双重验证 2：_harmony.GetPatchedMethods() 包含检查
                        bool contains = false;
                        foreach (var pm in _harmony.GetPatchedMethods())
                        {
                            if (pm == serverTarget) { contains = true; break; }
                        }
                        if (contains)
                        {
                            RoleLogger.Info("[Shared]",
                                "[ManualPatch] 验证 OK: ClientStaticMethod<...>.Invoke 已 patch（GetPatchedMethods 包含）");
                        }
                        else
                        {
                            ok = false;
                            RoleLogger.Error("[Shared]",
                                "[ManualPatch] 验证 FAIL: ClientStaticMethod<...>.Invoke 未 patch（GetPatchedMethods 不包含）");
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                ok = false;
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"[ManualPatch] FAIL AssetIntegritySnapshot/Server: {ex}");
            }

            return ok;
        }

        /// <summary>
        ///
        /// 登记以下修复 patch：
        ///
        /// 公共 Beta 始终登记完整的兼容性补丁集。
        /// </summary>
        private bool ApplyV2AuditFixPatches_Core()
        {
            RoleLogger.Info("[Shared]", "[Diag] === v2 审计放行后修复 patch 登记 ===");

            // 仅状态日志
            try
            {
                Patches.PlayerMovementInitializePlayerPrefixPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"PlayerMovementInitializePlayerPrefixPatch.RegisterManual 失败: {ex}");
            }

            // 完整兼容性补丁集。
                try
                {
                    Patches.SteamPlayerIsLocalServerHostPatch.RegisterManual(_harmony);
                }
                catch (System.Exception ex)
                {
                    _registrationStageFailed = true;
                    RoleLogger.Error("[Shared]", $"SteamPlayerIsLocalServerHostPatch.RegisterManual 失败: {ex}");
                }

                try
                {
                    Patches.PlayerUpdateGuardPatch.RegisterManual(_harmony);
                }
                catch (System.Exception ex)
                {
                    _registrationStageFailed = true;
                    RoleLogger.Error("[Shared]", $"PlayerUpdateGuardPatch.RegisterManual 失败: {ex}");
                }

                try
                {
                    Patches.GameplayReadyBitmaskPatch.RegisterManual(_harmony);
                }
                catch (System.Exception ex)
                {
                    _registrationStageFailed = true;
                    RoleLogger.Error("[Shared]", $"GameplayReadyBitmaskPatch.RegisterManual 失败: {ex}");
                }
            RoleLogger.Info("[Shared]", "[Diag] === v2 审计放行后修复 patch 登记完成 ===");
            return !_registrationStageFailed;
        }
    }
}
