using SDG.Unturned;
using SteamP2PFriends.Shared;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// T69 回归守卫：SteamPersonaDisplay.TryGetConnectedCharacterName 的 playerID 判空。
    ///
    /// 根因与 Bug3 NRE 同源：SDG.Unturned.SteamPlayerID 重载了 ==/!=，实现直接解引用
    /// playerID_0.steamID（SDK 源 SteamPlayerID.cs:136-139），任一侧为 null 都会先抛
    /// NullReferenceException。因此 `sp.playerID != null` 不是判空，它本身就是崩溃点。
    /// 现场：Provider.clients 里只要有一个 playerID 尚未绑定的玩家（主机本地玩家早期），
    /// 循环在第一个 client 处 NRE，被外层 catch 吞掉，后面真正匹配的 client 永远查不到。
    ///
    /// 本用例直接调用生产私有方法（崩溃现场），构造 [playerID=null, playerID=目标] 两个
    /// client：修复后必须跳过前者并返回后者的 characterName；把判空改回 != null 时，
    /// 循环在第一个 client 处中断 → 返回 null → 本用例红。
    /// </summary>
    internal static class SteamPersonaDisplayPlayerIdGuardTests
    {
        internal static void Test_NullPlayerIdClientDoesNotAbortLookup()
        {
            const ulong targetId = 76561198000000123UL;
            const string expectedName = "Alice";

            FieldInfo clientsField = typeof(Provider).GetField("_clients", BindingFlags.Static | BindingFlags.NonPublic);
            if (clientsField == null) throw new InvalidOperationException("Provider._clients 字段未找到，测试无法构造客户端列表");

            MethodInfo probe = typeof(SteamPersonaDisplay).GetMethod(
                "TryGetConnectedCharacterName", BindingFlags.NonPublic | BindingFlags.Static);
            if (probe == null) throw new InvalidOperationException("SteamPersonaDisplay.TryGetConnectedCharacterName 未找到");

            object originalClients = clientsField.GetValue(null);
            bool originalBypass = SteamPersonaDisplay._testBypassThreadAssert;
            Func<CSteamID, string> originalProvider = SteamPersonaDisplay._testConnectedNameProvider;
            try
            {
                // playerID 未绑定的 SteamPlayer：FormatterServices 不跑 ctor/cctor，_playerID 保持 null。
                var ghost = (SteamPlayer)FormatterServices.GetUninitializedObject(typeof(SteamPlayer));
                var valid = (SteamPlayer)FormatterServices.GetUninitializedObject(typeof(SteamPlayer));
                SetPrivateField(valid, "_playerID", new SteamPlayerID(
                    new CSteamID(targetId), 0, "player", expectedName, "nick", new CSteamID(0UL)));

                clientsField.SetValue(null, new List<SteamPlayer> { ghost, valid });
                SteamPersonaDisplay._testConnectedNameProvider = null;
                // 注意：_testBypassThreadAssert=true 时生产会直接 return null、绕过 Provider.clients 循环，
                // 所以这里必须为 false，才能真正走到判空现场。
                SteamPersonaDisplay._testBypassThreadAssert = false;
                SteamPersonaDisplay.ClearDisplayNameCacheForTest();

                object raw;
                try
                {
                    raw = probe.Invoke(null, new object[] { new CSteamID(targetId) });
                }
                catch (TargetInvocationException tie)
                {
                    Exception inner = tie.InnerException ?? tie;
                    throw new InvalidOperationException(
                        "playerID=null 的 client 不得让判定抛异常；实际异常=" + inner.GetType().Name + ": " + inner.Message, inner);
                }

                string actual = raw as string;
                Assert(actual == expectedName,
                    "playerID=null 的 client 必须被跳过、后续匹配 client 必须命中；期望=\"" + expectedName + "\"，实际=" +
                    (actual == null ? "<null>" : "\"" + actual + "\""));
            }
            finally
            {
                clientsField.SetValue(null, originalClients);
                SteamPersonaDisplay._testBypassThreadAssert = originalBypass;
                SteamPersonaDisplay._testConnectedNameProvider = originalProvider;
                SteamPersonaDisplay.ClearDisplayNameCacheForTest();
            }
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            Type type = target.GetType();
            FieldInfo field = null;
            while (type != null && field == null)
            {
                field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                type = type.BaseType;
            }
            if (field == null) throw new InvalidOperationException("字段未找到: " + fieldName);

            var setter = new DynamicMethod(
                "T69Set_" + fieldName, typeof(void), new[] { typeof(object), typeof(object) },
                typeof(SteamPersonaDisplayPlayerIdGuardTests).Module, true);
            ILGenerator il = setter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, field.DeclaringType);
            il.Emit(OpCodes.Ldarg_1);
            if (field.FieldType.IsValueType) il.Emit(OpCodes.Unbox_Any, field.FieldType);
            else il.Emit(OpCodes.Castclass, field.FieldType);
            il.Emit(OpCodes.Stfld, field);
            il.Emit(OpCodes.Ret);
            setter.Invoke(null, new object[] { target, value });
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
