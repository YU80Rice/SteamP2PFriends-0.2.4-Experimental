using BepInEx.Configuration;
using HarmonyLib;
using SteamP2PFriends.Shared;
using System;

namespace SteamP2PFriends.Core.Patches.GunshotHostAudio
{
    /// <summary>
    /// t116：Host 枪声叶补偿的生产开关（BepInEx 配置，默认关闭）。
    ///
    /// 契约：
    ///   - 缺省 Enable=false，且 cfg 缺省不写 true；
    ///   - Enable=false：不注册 Harmony patch（零订阅/零 patch）、不写运行时状态；
    ///   - Enable=true：安装 Prefix/Finalizer/Transpiler；任何失败路径 fail-closed。
    /// </summary>
    internal static class GunshotHostAudioControl
    {
        internal const string Label = "[Bug2HostGunshot]";
        internal const string Section = "Bug2HostGunshot";
        internal const bool DefaultEnable = false;

        private static ConfigEntry<bool> _enabledEntry;
        private static bool _initialized;
        private static Harmony _harmony;
        private static bool? _testEnabled;

        internal static bool IsEnabled => _testEnabled ?? (_enabledEntry != null && _enabledEntry.Value);
        internal static string LastFailure => GunshotHostAudioPatch.FailureReason;

        internal static bool Initialize(Harmony harmony)
        {
            if (_initialized)
            {
                // 重复初始化：报告当前是否处于「已启用且已安装」的健康状态。
                return !IsEnabled || GunshotHostAudioPatch.Installed;
            }

            _initialized = true;
            _harmony = harmony;
            try
            {
                // t145：开关并入插件主 cfg（com.yu80rice.steamp2pfriends.cfg）。
                // 不再创建/读取独立 SteamP2PFriends.Bug2HostGunshot.cfg。
                _enabledEntry = Core.Config.PluginConfig.Bug2HostGunshotEnable;
                if (_enabledEntry == null)
                {
                    RoleLogger.Error("[Shared]", Label + " ENTRY_INIT_FAILED main config not bound (PluginConfig.Bind must run first)");
                    return false;
                }

                _enabledEntry.SettingChanged += OnSettingChanged;
                if (!Apply(harmony, "startup"))
                {
                    RoleLogger.Error("[Shared]", Label + " ENTRY_INIT_FAILED register failed: "
                        + GunshotHostAudioPatch.FailureReason);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]", Label + " ENTRY_INIT_FAILED " + ex);
                return false;
            }
        }

        internal static void ResetForTest()
        {
            if (_enabledEntry != null)
            {
                _enabledEntry.SettingChanged -= OnSettingChanged;
            }

            _enabledEntry = null;
            _initialized = false;
            _harmony = null;
            _testEnabled = null;
        }

        /// <summary>
        /// 测试入口：不读 BepInEx 配置，直接以给定 harmony/开关走真实 Apply 控制流。
        /// </summary>
        internal static bool ApplyForTest(Harmony harmony, bool enabled)
        {
            _harmony = harmony;
            _testEnabled = enabled;
            return Apply(harmony, "test");
        }

        private static void OnSettingChanged(object sender, EventArgs e)
        {
            try
            {
                // 热开启必须传真实 harmony；传 null 会让 Patch 直接 fail-closed。
                if (!Apply(_harmony, "config-change"))
                {
                    RoleLogger.Error("[Shared]", Label + " CONFIG_APPLY_FAILED reason="
                        + GunshotHostAudioPatch.FailureReason);
                }
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]", Label + " CONFIG_APPLY_FAILED " + ex);
            }
        }

        private static bool Apply(Harmony harmony, string origin)
        {
            bool enabled = IsEnabled;
            GunshotHostAudioRuntime.SetEnabled(enabled);
            bool ok = true;
            if (enabled)
            {
                // 重新 enable 时清除黏性 FailClosed，允许修复后恢复。
                GunshotHostAudioRuntime.ClearFailClosed();
                ok = GunshotHostAudioPatch.RegisterManual(harmony);
            }
            else
            {
                GunshotHostAudioPatch.Uninstall();
            }

            if (ok)
            {
                RoleLogger.Info("[Shared]", Label + " ENTRY " + origin + " enabled=" + enabled);
            }
            else
            {
                RoleLogger.Error("[Shared]", Label + " ENTRY_FAILED " + origin + " enabled=" + enabled
                    + " reason=" + GunshotHostAudioPatch.FailureReason);
            }

            return ok;
        }
    }
}
