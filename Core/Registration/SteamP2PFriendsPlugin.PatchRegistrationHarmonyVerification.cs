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



using Patches = SteamP2PFriends.Core.Patches;

namespace SteamP2PFriends
{
    public partial class SteamP2PFriendsPlugin
    {
        private static bool VerifyNetMessagesPatches()
        {
            try
            {
                System.Type netMessagesType = AccessTools.TypeByName("SDG.Unturned.NetMessages");
                if (netMessagesType == null)
                {
                    RoleLogger.Error("[Shared]", "[Diag] !!! D-5 NetMessages: TypeByName 返回 null");
                    return false;
                }

                System.Type clientWriteHandlerType = netMessagesType.GetNestedType("ClientWriteHandler");
                if (clientWriteHandlerType == null)
                {
                    RoleLogger.Error("[Shared]", "[Diag] !!! D-5 NetMessages.ClientWriteHandler: GetNestedType 返回 null");
                    return false;
                }

                bool ok = true;

                // NetMessages.SendMessageToClient(EClientMessage, ENetReliability, ITransportConnection, ClientWriteHandler)
                ok &= VerifyPatch(netMessagesType, "SendMessageToClient",
                    new System.Type[] {
                        typeof(EClientMessage), typeof(ENetReliability),
                        typeof(SDG.NetTransport.ITransportConnection), clientWriteHandlerType
                    },
                    "D-5 NetMessages.SendMessageToClient", requirePrefix: true, requireFinalizer: true);

                // NetMessages.SendMessageToClients(EClientMessage, ENetReliability, List<ITransportConnection>, ClientWriteHandler)
                ok &= VerifyPatch(netMessagesType, "SendMessageToClients",
                    new System.Type[] {
                        typeof(EClientMessage), typeof(ENetReliability),
                        typeof(System.Collections.Generic.List<SDG.NetTransport.ITransportConnection>), clientWriteHandlerType
                    },
                    "D-5 NetMessages.SendMessageToClients(List)", requirePrefix: true, requireFinalizer: true);

                return ok;
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[Diag] VerifyNetMessagesPatches 异常: {ex.Message}");
                return false;
            }
        }

        private static bool VerifyPatch(System.Type targetType, string methodName,
            System.Type[] paramTypes, string description,
            bool requirePrefix = false, bool requirePostfix = false, bool requireFinalizer = false)
        {
            try
            {
                if (targetType == null)
                {
                    RoleLogger.Error("[Shared]", $"[Diag] !!! {description}: targetType=null");
                    return false;
                }

                System.Reflection.MethodInfo method = AccessTools.Method(targetType, methodName, paramTypes);
                if (method == null)
                {
                    RoleLogger.Error("[Shared]",
                        $"[Diag] !!! {description}: AccessTools.Method 返回 null（方法未找到）type={targetType.FullName} argCount={paramTypes?.Length ?? 0}");
                    return false;
                }

                HarmonyLib.Patches patches = Harmony.GetPatchInfo(method);
                int prefixCount = HarmonyCompatibilityAudit.CountOwned(patches?.Prefixes);
                int postfixCount = HarmonyCompatibilityAudit.CountOwned(patches?.Postfixes);
                int finalizerCount = HarmonyCompatibilityAudit.CountOwned(patches?.Finalizers);

                bool ok = true;
                if (requirePrefix && prefixCount == 0)
                {
                    RoleLogger.Error("[Shared]", $"[Diag] !!! {description}: Prefix 未登记");
                    ok = false;
                }
                if (requirePostfix && postfixCount == 0)
                {
                    RoleLogger.Error("[Shared]", $"[Diag] !!! {description}: Postfix 未登记");
                    ok = false;
                }
                if (requireFinalizer && finalizerCount == 0)
                {
                    RoleLogger.Error("[Shared]", $"[Diag] !!! {description}: Finalizer 未登记");
                    ok = false;
                }

                if (!HarmonyCompatibilityAudit.Inspect(method, description))
                {
                    RoleLogger.Error("[Shared]", $"[Compat] !!! {description}: blocking foreign Harmony patch");
                    ok = false;
                }

                if (ok)
                {
                    RoleLogger.Info("[Shared]",
                        $"[Diag] OK {description}: ownPrefixes={prefixCount} ownPostfixes={postfixCount} ownFinalizers={finalizerCount}");
                }
                return ok;
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[Diag] VerifyPatch({description}) 异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 检查 Patches 列表中是否存在 patch.PatchMethod 来自 expectedDeclaringType 的 expectedMethodName。
        /// 泛型类型用 GetGenericTypeDefinition 比较。
        /// </summary>
        private static bool VerifyPatchMethod(
            System.Collections.Generic.IList<HarmonyLib.Patch> list,
            System.Type expectedDeclaringType,
            string expectedMethodName,
            string description,
            string kind)
        {
            if (list == null || list.Count == 0)
            {
                RoleLogger.Error("[Shared]",
                    $"[Diag] !!! {description}: {kind} 未登记（期望 {expectedDeclaringType.FullName}.{expectedMethodName}）");
                return false;
            }

            bool found = false;
            foreach (HarmonyLib.Patch p in list)
            {
                if (p.owner != HARMONY_ID) continue;
                System.Reflection.MethodInfo pm = p.PatchMethod;
                if (ReferenceEquals(pm, null)) continue;
                System.Type dt = pm.DeclaringType;
                if (ReferenceEquals(dt, null)) continue;

                bool typeMatch;
                if (expectedDeclaringType.IsGenericTypeDefinition)
                {
                    // 泛型类型比较：BitmaskPostfixCache<T> 的 DeclaringType 是 BitmaskPostfixCache<PlayerClothing> 等
                    typeMatch = dt.IsGenericType && dt.GetGenericTypeDefinition() == expectedDeclaringType;
                }
                else
                {
                    typeMatch = dt == expectedDeclaringType;
                }

                if (typeMatch && pm.Name == expectedMethodName)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                RoleLogger.Error("[Shared]",
                    $"[Diag] !!! {description}: {kind} 期望 {expectedDeclaringType.FullName}.{expectedMethodName} 未登记");
                return false;
            }
            return true;
        }

        /// <summary>
        ///   不仅检查 owner 和数量，还检查 patch.PatchMethod 的 DeclaringType 和 Name 是否匹配预期。
        ///   用于 5 组关键 patch：AcceptConnection / SetConnectionPollGroup / ClientSns D10 / ServerSns D10 / RequestDisconnect。
        ///   任一缺失或名字不匹配都返回 false。
        /// </summary>
        private static bool VerifyPatchMethodPair(
            System.Type targetType, string methodName, System.Type[] paramTypes,
            System.Type expectedPatchDeclaringType,
            string expectedPrefixName, string expectedPostfixName,
            string description)
        {
            try
            {
                if (targetType == null)
                {
                    RoleLogger.Error("[Shared]", $"[Diag] !!! {description}: targetType=null");
                    return false;
                }

                System.Reflection.MethodInfo method = AccessTools.Method(targetType, methodName, paramTypes);
                if (method == null)
                {
                    RoleLogger.Error("[Shared]",
                        $"[Diag] !!! {description}: AccessTools.Method 返回 null type={targetType.FullName} method={methodName}");
                    return false;
                }

                HarmonyLib.Patches patches = Harmony.GetPatchInfo(method);
                bool ok = true;

                // 精确方法验证（declaring type + method name）
                ok &= VerifyPatchMethod(patches?.Prefixes, expectedPatchDeclaringType, expectedPrefixName, description, "Prefix");
                ok &= VerifyPatchMethod(patches?.Postfixes, expectedPatchDeclaringType, expectedPostfixName, description, "Postfix");

                if (!HarmonyCompatibilityAudit.Inspect(method, description))
                {
                    RoleLogger.Error("[Shared]", $"[Compat] !!! {description}: blocking foreign Harmony patch");
                    ok = false;
                }

                if (ok)
                {
                    RoleLogger.Info("[Shared]",
                        $"[Diag] OK {description}: {expectedPatchDeclaringType.Name}.{expectedPrefixName} + " +
                        $"{expectedPostfixName} 精确验证通过");
                }
                return ok;
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[Diag] VerifyPatchMethodPair({description}) 异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        ///   用于 auth callback safety patch（只有 Prefix，无 Postfix）。
        ///   不仅检查 owner 和数量，还检查 patch.PatchMethod 的 DeclaringType 和 Name 是否匹配预期。
        ///   任一缺失或名字不匹配都返回 false。
        /// </summary>
        private static bool VerifyAuthCallbackSafetyPatchMethod(
            System.Type targetType, string methodName,
            System.Type expectedPatchDeclaringType,
            string expectedPrefixName,
            string description)
        {
            try
            {
                if (targetType == null)
                {
                    RoleLogger.Error("[Shared]", $"[Diag] !!! {description}: targetType=null");
                    return false;
                }

                System.Reflection.MethodInfo method = AccessTools.Method(targetType, methodName);
                if (method == null)
                {
                    RoleLogger.Error("[Shared]",
                        $"[Diag] !!! {description}: AccessTools.Method 返回 null type={targetType.FullName} method={methodName}");
                    return false;
                }

                HarmonyLib.Patches patches = Harmony.GetPatchInfo(method);
                bool ok = true;

                // 精确方法验证（declaring type + method name）
                ok &= VerifyPatchMethod(patches?.Prefixes, expectedPatchDeclaringType, expectedPrefixName, description, "Prefix");

                if (!HarmonyCompatibilityAudit.Inspect(method, description))
                {
                    RoleLogger.Error("[Shared]", $"[Compat] !!! {description}: blocking foreign Harmony patch");
                    ok = false;
                }

                if (ok)
                {
                    RoleLogger.Info("[Shared]",
                        $"[Diag] OK {description}: {expectedPatchDeclaringType.Name}.{expectedPrefixName} 精确验证通过");
                }
                return ok;
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[Diag] VerifyAuthCallbackSafetyPatchMethod({description}) 异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 总数触发误报，而是交给 HarmonyCompatibilityAudit 按 transport 关键目标策略裁决。
        /// </summary>
        private static bool VerifyClientMethodLoopbackPrefix(
            System.Type targetType, string methodName, System.Type[] paramTypes,
            string expectedPrefixName, string description)
        {
            try
            {
                if (targetType == null)
                {
                    RoleLogger.Error("[Shared]", $"[Diag] !!! {description}: targetType=null");
                    return false;
                }

                System.Reflection.MethodInfo method = AccessTools.Method(targetType, methodName, paramTypes);
                if (method == null)
                {
                    RoleLogger.Error("[Shared]",
                        $"[Diag] !!! {description}: AccessTools.Method 返回 null type={targetType.FullName} method={methodName} argCount={paramTypes?.Length ?? 0}");
                    return false;
                }

                HarmonyLib.Patches patches = Harmony.GetPatchInfo(method);
                int ownPrefixCount = HarmonyCompatibilityAudit.CountOwned(patches?.Prefixes);
                if (ownPrefixCount == 0)
                {
                    RoleLogger.Error("[Shared]",
                        $"[Diag] !!! {description}: own Prefix 未登记");
                    return false;
                }

                int exactMatchCount = 0;
                if (patches?.Prefixes != null)
                {
                    foreach (HarmonyLib.Patch p in patches.Prefixes)
                    {
                        if (p.owner != HARMONY_ID) continue;
                        System.Reflection.MethodInfo pm = p.PatchMethod;
                        if (ReferenceEquals(pm, null)) continue;
                        if (pm.DeclaringType == typeof(Core.Patches.ClientMethodLoopbackPatch)
                            && pm.Name == expectedPrefixName)
                        {
                            exactMatchCount++;
                        }
                    }
                }

                bool ok = true;

                // 检查 1: own total == 1. Other owners are classified separately.
                if (ownPrefixCount != 1)
                {
                    RoleLogger.Error("[Shared]",
                        $"[Diag] !!! {description}: ownPrefixCount={ownPrefixCount} 期望=1 (exactMatch={exactMatchCount})");
                    ok = false;
                }

                // 检查 2: exact == 1
                if (exactMatchCount != 1)
                {
                    RoleLogger.Error("[Shared]",
                        $"[Diag] !!! {description}: exactMatchCount={exactMatchCount} 期望=1 (期望 {typeof(Core.Patches.ClientMethodLoopbackPatch).FullName}.{expectedPrefixName})");
                    ok = false;
                }

                // SendAndLoopback* belongs to the exclusive P2P transport contract.
                if (!HarmonyCompatibilityAudit.Inspect(method, description))
                {
                    RoleLogger.Error("[Shared]",
                        $"[Compat] !!! {description}: blocking foreign Harmony patch");
                    ok = false;
                }

                if (ok)
                {
                    RoleLogger.Info("[Shared]",
                        $"[Diag] OK {description}: ownTotal=1 exact=1 ({typeof(Core.Patches.ClientMethodLoopbackPatch).Name}.{expectedPrefixName})");
                }
                return ok;
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[Diag] VerifyClientMethodLoopbackPrefix({description}) 异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 使用泛型方法以获取具体的 BitmaskPostfixCache<T> 类型。
        /// </summary>
        private static bool VerifyBitmaskPostfix<T>(string description)
        {
            try
            {
                System.Reflection.MethodInfo method = AccessTools.Method(typeof(T), "InitializePlayer");
                if (method == null)
                {
                    RoleLogger.Error("[Shared]", $"[Diag] !!! {description}: InitializePlayer 反射失败");
                    return false;
                }

                HarmonyLib.Patches patches = Harmony.GetPatchInfo(method);
                int postfixCount = HarmonyCompatibilityAudit.CountOwned(patches?.Postfixes);
                if (postfixCount == 0)
                {
                    RoleLogger.Error("[Shared]", $"[Diag] !!! {description}: Postfix 未登记");
                    return false;
                }

                if (!HarmonyCompatibilityAudit.Inspect(method, description))
                {
                    RoleLogger.Error("[Shared]", $"[Compat] !!! {description}: blocking foreign Harmony patch");
                    return false;
                }

                // 精确方法验证：BitmaskPostfixCache<T>.Postfix（泛型类型定义比较）
                System.Type expectedGenericType = typeof(SteamP2PFriends.Core.Patches.BitmaskPostfixCache<T>);
                bool found = false;
                foreach (HarmonyLib.Patch p in patches.Postfixes)
                {
                    if (p.owner != HARMONY_ID) continue;
                    System.Reflection.MethodInfo pm = p.PatchMethod;
                    if (ReferenceEquals(pm, null)) continue;
                    System.Type dt = pm.DeclaringType;
                    if (ReferenceEquals(dt, null)) continue;

                    if (dt.IsGenericType && dt.GetGenericTypeDefinition() == expectedGenericType.GetGenericTypeDefinition()
                        && pm.Name == "Postfix")
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    RoleLogger.Error("[Shared]",
                        $"[Diag] !!! {description}: BitmaskPostfixCache<{typeof(T).Name}>.Postfix 未登记");
                    return false;
                }

                RoleLogger.Info("[Shared]", $"[Diag] OK {description}: BitmaskPostfix Postfix 已登记");
                return true;
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[Diag] VerifyBitmaskPostfix({description}) 异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 使用 AccessTools.Method(Type, string) 反射（依赖 Harmony 模糊匹配）。
        /// 支持 require* 参数 + own-owner 验证 + 第三方兼容性策略，并返回 bool 参与 allOk 聚合。
        /// </summary>
        private static bool VerifyPatch(System.Type targetType, string methodName, string description,
            bool requirePrefix = false, bool requirePostfix = false, bool requireFinalizer = false)
        {
            try
            {
                if (targetType == null)
                {
                    RoleLogger.Error("[Shared]", $"[Diag] !!! {description}: targetType=null");
                    return false;
                }

                System.Reflection.MethodInfo method = AccessTools.Method(targetType, methodName);
                if (method == null)
                {
                    RoleLogger.Error("[Shared]",
                        $"[Diag] !!! {description}: AccessTools.Method 返回 null（方法未找到）type={targetType.FullName}");
                    return false;
                }

                HarmonyLib.Patches patches = Harmony.GetPatchInfo(method);
                int prefixCount = HarmonyCompatibilityAudit.CountOwned(patches?.Prefixes);
                int postfixCount = HarmonyCompatibilityAudit.CountOwned(patches?.Postfixes);
                int finalizerCount = HarmonyCompatibilityAudit.CountOwned(patches?.Finalizers);

                bool ok = true;
                if (requirePrefix && prefixCount == 0)
                {
                    RoleLogger.Error("[Shared]", $"[Diag] !!! {description}: Prefix 未登记");
                    ok = false;
                }
                if (requirePostfix && postfixCount == 0)
                {
                    RoleLogger.Error("[Shared]", $"[Diag] !!! {description}: Postfix 未登记");
                    ok = false;
                }
                if (requireFinalizer && finalizerCount == 0)
                {
                    RoleLogger.Error("[Shared]", $"[Diag] !!! {description}: Finalizer 未登记");
                    ok = false;
                }

                if (!HarmonyCompatibilityAudit.Inspect(method, description))
                {
                    RoleLogger.Error("[Shared]", $"[Compat] !!! {description}: blocking foreign Harmony patch");
                    ok = false;
                }

                if (ok)
                {
                    RoleLogger.Info("[Shared]",
                        $"[Diag] OK {description}: ownPrefixes={prefixCount} ownPostfixes={postfixCount} ownFinalizers={finalizerCount}");
                }
                return ok;
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[Diag] VerifyPatch({description}) 异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        ///
        /// 包含：
        ///
        /// 公共 Beta 始终登记完整的兼容性补丁集。
        /// </summary>
    }
}
