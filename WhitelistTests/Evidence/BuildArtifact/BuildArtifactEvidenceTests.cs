using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using PluginBuildMetadata = SteamP2PFriends.Core.Build.BuildMetadata;
using PluginFingerprint = SteamP2PFriends.Core.Build.BuildFingerprint;
using PluginFingerprintSnapshot = SteamP2PFriends.Core.Build.BuildFingerprintSnapshot;
using TestBuildMetadata = SteamP2PFriends.WhitelistTests.Build.BuildMetadata;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// BuildArtifact 证据：核验统一元数据、加载程序集的版本、MVID、插件 GUID 和可独立重算的 hash。
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

            PluginFingerprintSnapshot fingerprint = PluginFingerprint.Capture(assembly);
            if (!fingerprint.IsComplete)
                return false;

            if (!string.Equals(fingerprint.Version, PluginBuildMetadata.Version, StringComparison.Ordinal) ||
                !string.Equals(fingerprint.AssemblyVersion, assembly.GetName().Version?.ToString(), StringComparison.Ordinal) ||
                !string.Equals(fingerprint.FileVersion, PluginBuildMetadata.Version, StringComparison.Ordinal) ||
                !string.Equals(fingerprint.PluginGuid, PluginBuildMetadata.PluginGuid, StringComparison.Ordinal) ||
                !string.Equals(fingerprint.BuildCaseId, PluginBuildMetadata.DefaultCaseId, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(fingerprint.CaseId))
                return false;

            Version assemblyVersion = assembly.GetName().Version;
            if (assemblyVersion == null || assemblyVersion != new Version(PluginBuildMetadata.Version))
                return false;

            FileVersionInfo fileVersion = FileVersionInfo.GetVersionInfo(artifactPath);
            if (!string.Equals(fileVersion.FileVersion, assemblyVersion.ToString(), StringComparison.Ordinal))
                return false;

            if (assembly.ManifestModule.ModuleVersionId == Guid.Empty)
                return false;

            if (!HasExpectedPluginIdentity(typeof(SteamP2PFriendsPlugin)))
                return false;

            string independentlyComputedHash = ComputeSha256(artifactPath);
            return string.Equals(fingerprint.DllSha256, independentlyComputedHash, StringComparison.Ordinal);
        }

        internal static bool Test_CaseIdOverrideIsShared()
        {
            const string expectedCaseId = "Ticket09-Shared-Case-20260827";
            string previous = Environment.GetEnvironmentVariable(PluginFingerprint.CaseIdEnvironmentVariable);
            try
            {
                Environment.SetEnvironmentVariable(PluginFingerprint.CaseIdEnvironmentVariable, expectedCaseId);
                PluginFingerprintSnapshot fingerprint = PluginFingerprint.Capture(typeof(SteamP2PFriendsPlugin).Assembly);
                return string.Equals(fingerprint.CaseId, expectedCaseId, StringComparison.Ordinal) &&
                    string.Equals(fingerprint.CaseIdSource, "environment", StringComparison.Ordinal);
            }
            finally
            {
                Environment.SetEnvironmentVariable(PluginFingerprint.CaseIdEnvironmentVariable, previous);
            }
        }

        internal static bool Test_InvalidCaseIdFallsBackToBuildMetadata()
        {
            string previous = Environment.GetEnvironmentVariable(PluginFingerprint.CaseIdEnvironmentVariable);
            try
            {
                Environment.SetEnvironmentVariable(PluginFingerprint.CaseIdEnvironmentVariable, "invalid case id");
                PluginFingerprintSnapshot fingerprint = PluginFingerprint.Capture(typeof(SteamP2PFriendsPlugin).Assembly);
                return string.Equals(fingerprint.CaseId, PluginBuildMetadata.DefaultCaseId, StringComparison.Ordinal) &&
                    string.Equals(fingerprint.CaseIdSource, "build-metadata", StringComparison.Ordinal);
            }
            finally
            {
                Environment.SetEnvironmentVariable(PluginFingerprint.CaseIdEnvironmentVariable, previous);
            }
        }

        internal static bool Test_TestAssemblyConsumesVersionMetadata()
        {
            Assembly testAssembly = typeof(Program).Assembly;
            string testPath = testAssembly.Location;
            if (string.IsNullOrWhiteSpace(testPath) || !File.Exists(testPath)) return false;

            Version expected = new Version(TestBuildMetadata.Version);
            Version actual = testAssembly.GetName().Version;
            FileVersionInfo fileVersion = FileVersionInfo.GetVersionInfo(testPath);
            return actual == expected &&
                string.Equals(fileVersion.FileVersion, TestBuildMetadata.Version, StringComparison.Ordinal) &&
                testAssembly.ManifestModule.ModuleVersionId != Guid.Empty &&
                string.Equals(TestBuildMetadata.Version, PluginBuildMetadata.Version, StringComparison.Ordinal) &&
                HasAssemblyMetadata(testAssembly, "SteamP2PFriendsVersion", TestBuildMetadata.Version) &&
                HasAssemblyMetadata(testAssembly, "SteamP2PFriendsPluginGuid", TestBuildMetadata.PluginGuid);
        }

        private static bool HasAssemblyMetadata(Assembly assembly, string key, string expectedValue)
        {
            return assembly.GetCustomAttributes(typeof(AssemblyMetadataAttribute), false)
                .OfType<AssemblyMetadataAttribute>()
                .Any(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal) &&
                    string.Equals(attribute.Value, expectedValue, StringComparison.Ordinal));
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
                string.Equals(version, PluginBuildMetadata.Version, StringComparison.Ordinal);
        }

        private static string ComputeSha256(string path)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty);
            }
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
