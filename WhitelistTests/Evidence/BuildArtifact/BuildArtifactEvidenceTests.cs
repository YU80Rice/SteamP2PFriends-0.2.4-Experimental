using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// BuildArtifact 证据：只核验加载程序集的文件、版本、MVID、插件 GUID 和可独立重算的 hash。
    /// 不宣称真实游戏 Runtime、SP、listen-host、U3DS 或 P2P 行为通过。
    /// </summary>
    internal static class BuildArtifactEvidenceTests
    {
        internal static bool Test_All()
        {
            Assembly assembly = typeof(SteamP2PFriendsPlugin).Assembly;
            string artifactPath = assembly.Location;
            if (string.IsNullOrWhiteSpace(artifactPath) || !File.Exists(artifactPath))
                return false;

            Version assemblyVersion = assembly.GetName().Version;
            if (assemblyVersion == null || assemblyVersion != new Version("0.2.4.8"))
                return false;

            FileVersionInfo fileVersion = FileVersionInfo.GetVersionInfo(artifactPath);
            if (!string.Equals(fileVersion.FileVersion, assemblyVersion.ToString(), StringComparison.Ordinal))
                return false;

            if (assembly.ManifestModule.ModuleVersionId == Guid.Empty)
                return false;

            if (!HasExpectedPluginIdentity(typeof(SteamP2PFriendsPlugin)))
                return false;

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] digest = sha256.ComputeHash(File.ReadAllBytes(artifactPath));
                return digest.Length == 32 && digest.Any(value => value != 0);
            }
        }

        private static bool HasExpectedPluginIdentity(Type pluginType)
        {
            object pluginAttribute = pluginType.GetCustomAttributes(inherit: false)
                .FirstOrDefault(attribute => attribute.GetType().FullName == "BepInEx.BepInPlugin");
            if (pluginAttribute == null)
                return false;

            string guid = ReadStringMember(pluginAttribute, "GUID");
            string name = ReadStringMember(pluginAttribute, "Name");
            string version = ReadStringMember(pluginAttribute, "Version");
            return string.Equals(guid, SteamP2PFriendsPlugin.HARMONY_ID, StringComparison.Ordinal) &&
                string.Equals(name, "SteamP2PFriends", StringComparison.Ordinal) &&
                string.Equals(version, "0.2.4.8", StringComparison.Ordinal);
        }

        private static string ReadStringMember(object instance, string name)
        {
            Type type = instance.GetType();
            PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property != null)
            {
                object value = property.GetValue(instance, null);
                return value == null ? null : value.ToString();
            }

            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public);
            if (field == null)
                return null;

            object fieldValue = field.GetValue(instance);
            return fieldValue == null ? null : fieldValue.ToString();
        }
    }
}
