using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace SteamP2PFriends.Core.Build
{
    /// <summary>
    /// 当前加载插件的运行时身份快照。该快照是自报告证据，不替代验收侧独立产物核验。
    /// </summary>
    internal sealed class BuildFingerprintSnapshot
    {
        internal string Version { get; set; }
        internal string AssemblyVersion { get; set; }
        internal string FileVersion { get; set; }
        internal string Mvid { get; set; }
        internal string DllSha256 { get; set; }
        internal string PluginGuid { get; set; }
        internal string CaseId { get; set; }
        internal string CaseIdSource { get; set; }
        internal string BuildCaseId { get; set; }

        internal bool IsComplete
        {
            get
            {
                return !IsUnavailable(Version) &&
                    !IsUnavailable(AssemblyVersion) &&
                    !IsUnavailable(FileVersion) &&
                    !IsUnavailable(Mvid) &&
                    !IsUnavailable(DllSha256) &&
                    !IsUnavailable(PluginGuid) &&
                    !IsUnavailable(CaseId) &&
                    !IsUnavailable(BuildCaseId);
            }
        }

        internal string ToLogString()
        {
            return string.Format(
                "version={0} assemblyVersion={1} fileVersion={2} mvid={3} dllSha256={4} pluginGuid={5} caseId={6} caseIdSource={7} buildCaseId={8} evidence=self-reported",
                Version,
                AssemblyVersion,
                FileVersion,
                Mvid,
                DllSha256,
                PluginGuid,
                CaseId,
                CaseIdSource,
                BuildCaseId);
        }

        private static bool IsUnavailable(string value)
        {
            return string.IsNullOrWhiteSpace(value) || string.Equals(value, "UNAVAILABLE", StringComparison.Ordinal);
        }
    }

    internal static class BuildFingerprint
    {
        internal const string CaseIdEnvironmentVariable = "STEAMP2PFRIENDS_CASE_ID";

        internal static BuildFingerprintSnapshot Capture(Assembly assembly = null)
        {
            assembly = assembly ?? typeof(SteamP2PFriendsPlugin).Assembly;
            string artifactPath = SafeGetLocation(assembly);

            string assemblyVersion = SafeGetAssemblyVersion(assembly);
            string fileVersion = SafeGetFileVersion(artifactPath);
            string mvid = SafeGetMvid(assembly);
            string hash = ComputeSha256(artifactPath);
            string caseId;
            string caseIdSource;
            ResolveCaseId(out caseId, out caseIdSource);

            return new BuildFingerprintSnapshot
            {
                Version = SafeGetAssemblyMetadata(assembly, "SteamP2PFriendsVersion"),
                AssemblyVersion = assemblyVersion,
                FileVersion = fileVersion,
                Mvid = mvid,
                DllSha256 = hash,
                PluginGuid = SafeGetPluginGuid(assembly),
                CaseId = caseId,
                CaseIdSource = caseIdSource,
                BuildCaseId = SafeGetAssemblyMetadata(assembly, "SteamP2PFriendsDefaultCaseId")
            };
        }

        private static string SafeGetLocation(Assembly assembly)
        {
            try { return assembly == null ? null : assembly.Location; }
            catch { return null; }
        }

        private static string SafeGetAssemblyVersion(Assembly assembly)
        {
            try { return assembly.GetName().Version?.ToString() ?? "UNAVAILABLE"; }
            catch { return "UNAVAILABLE"; }
        }

        private static string SafeGetFileVersion(string artifactPath)
        {
            if (string.IsNullOrWhiteSpace(artifactPath) || !File.Exists(artifactPath)) return "UNAVAILABLE";
            try { return FileVersionInfo.GetVersionInfo(artifactPath).FileVersion ?? "UNAVAILABLE"; }
            catch { return "UNAVAILABLE"; }
        }

        private static string SafeGetMvid(Assembly assembly)
        {
            try { return assembly.ManifestModule.ModuleVersionId.ToString("D"); }
            catch { return "UNAVAILABLE"; }
        }

        private static string SafeGetAssemblyMetadata(Assembly assembly, string key)
        {
            try
            {
                AssemblyMetadataAttribute attribute = assembly.GetCustomAttributes(typeof(AssemblyMetadataAttribute), false)
                    .OfType<AssemblyMetadataAttribute>()
                    .FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal));
                return attribute == null || string.IsNullOrWhiteSpace(attribute.Value) ? "UNAVAILABLE" : attribute.Value;
            }
            catch { return "UNAVAILABLE"; }
        }

        private static string SafeGetPluginGuid(Assembly assembly)
        {
            try
            {
                Type pluginType = assembly.GetType("SteamP2PFriends.SteamP2PFriendsPlugin", false);
                if (pluginType == null) return "UNAVAILABLE";

                object pluginAttribute = pluginType.GetCustomAttributes(inherit: false)
                    .FirstOrDefault(attribute => attribute.GetType().FullName == "BepInEx.BepInPlugin");
                if (pluginAttribute == null) return "UNAVAILABLE";
                return ReadStringMember(pluginAttribute, "GUID");
            }
            catch { return "UNAVAILABLE"; }
        }

        private static string ReadStringMember(object instance, string name)
        {
            Type type = instance.GetType();
            PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property != null)
            {
                object value = property.GetValue(instance, null);
                return value == null ? "UNAVAILABLE" : value.ToString();
            }

            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public);
            if (field == null) return "UNAVAILABLE";
            object fieldValue = field.GetValue(instance);
            return fieldValue == null ? "UNAVAILABLE" : fieldValue.ToString();
        }

        private static string ComputeSha256(string artifactPath)
        {
            if (string.IsNullOrWhiteSpace(artifactPath) || !File.Exists(artifactPath)) return "UNAVAILABLE";
            try
            {
                using (SHA256 sha256 = SHA256.Create())
                using (FileStream stream = File.OpenRead(artifactPath))
                {
                    return ToUpperHex(sha256.ComputeHash(stream));
                }
            }
            catch { return "UNAVAILABLE"; }
        }

        private static void ResolveCaseId(out string caseId, out string source)
        {
            string environmentCaseId = null;
            try { environmentCaseId = Environment.GetEnvironmentVariable(CaseIdEnvironmentVariable); }
            catch { }

            if (IsValidCaseId(environmentCaseId))
            {
                caseId = environmentCaseId.Trim();
                source = "environment";
                return;
            }

            caseId = BuildMetadata.DefaultCaseId;
            source = "build-metadata";
        }

        private static bool IsValidCaseId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 96) return false;
            foreach (char character in value.Trim())
            {
                if (!(char.IsLetterOrDigit(character) || character == '-' || character == '_' || character == '.'))
                    return false;
            }
            return true;
        }

        private static string ToUpperHex(byte[] value)
        {
            StringBuilder builder = new StringBuilder(value.Length * 2);
            foreach (byte item in value) builder.Append(item.ToString("X2"));
            return builder.ToString();
        }
    }
}
