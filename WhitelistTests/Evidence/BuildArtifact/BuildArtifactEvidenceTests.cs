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

        /// <summary>
        /// 票 01 身份门：本阶段（Collision Migration Slice）Metadata Source 授予 0.2.4.9，
        /// 发布通道仍为 Experimental，默认 Case-ID 能把本切片与 0.2.4.8 结构基线分开。
        /// 票 05 起默认 Case-ID 追加候选角色后缀（本切片当前交付只读影子候选）。
        /// 该字面量是本阶段的冻结身份，随版本推进显式更新；不做隐式漂移。
        /// </summary>
        internal static bool Test_SliceIdentityIsPinnedToMigrationStage()
        {
            const string expectedVersion = "0.2.4.9";
            const string expectedChannel = "Experimental";
            const string expectedCaseId = "SPF-0.2.4.9-Experimental-CollisionSlice-ReadOnlyShadow";

            PluginFingerprintSnapshot fingerprint = PluginFingerprint.Capture(typeof(SteamP2PFriendsPlugin).Assembly);
            return string.Equals(PluginBuildMetadata.Version, expectedVersion, StringComparison.Ordinal)
                && string.Equals(PluginBuildMetadata.ReleaseChannel, expectedChannel, StringComparison.Ordinal)
                && string.Equals(PluginBuildMetadata.VersionIdentity,
                    expectedVersion + "-" + expectedChannel, StringComparison.Ordinal)
                && string.Equals(PluginBuildMetadata.DefaultCaseId, expectedCaseId, StringComparison.Ordinal)
                && string.Equals(fingerprint.Version, expectedVersion, StringComparison.Ordinal)
                && string.Equals(fingerprint.AssemblyVersion, expectedVersion, StringComparison.Ordinal)
                && string.Equals(fingerprint.FileVersion, expectedVersion, StringComparison.Ordinal)
                && string.Equals(fingerprint.BuildCaseId, expectedCaseId, StringComparison.Ordinal);
        }

        /// <summary>
        /// 影子候选与正式切换候选必须是可区分的构建指纹（票 05/ADR 0012）：候选角色进程序集
        /// 元数据与默认 Case-ID，因此两个角色的产物身份不同，两类候选的验收日志不得混用。
        /// 本门断言「角色确实参与身份」——正式切换候选的具体角色值由票 08 冻结。
        /// </summary>
        internal static bool Test_ShadowCandidateRoleIsDistinctFromCutoverCandidate()
        {
            const string shadowRole = "ReadOnlyShadow";
            const string cutoverRole = "Cutover";
            Assembly assembly = typeof(SteamP2PFriendsPlugin).Assembly;
            PluginFingerprintSnapshot fingerprint = PluginFingerprint.Capture(assembly);

            string shadowCaseId = PluginBuildMetadata.DefaultCaseId;
            string cutoverCaseId = shadowCaseId.Replace(shadowRole, cutoverRole);
            bool roleIsEmbedded = HasAssemblyMetadata(assembly, "SteamP2PFriendsCandidateRole", shadowRole)
                && string.Equals(fingerprint.CandidateRole, shadowRole, StringComparison.Ordinal);
            bool roleIsInCaseId = shadowCaseId.EndsWith("-" + shadowRole, StringComparison.Ordinal);
            bool rolesAreDistinct = !string.Equals(shadowCaseId, cutoverCaseId, StringComparison.Ordinal)
                && !string.Equals(fingerprint.CaseId, cutoverCaseId, StringComparison.Ordinal)
                && fingerprint.IsComplete;

            return string.Equals(PluginBuildMetadata.CandidateRole, shadowRole, StringComparison.Ordinal)
                && roleIsEmbedded
                && roleIsInCaseId
                && rolesAreDistinct;
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

        internal static bool Test_VerifierRequiresFullLogIdentity()
        {
            string scriptPath = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Tools", "Verify-BuildFingerprintArtifact.ps1"));
            if (!File.Exists(scriptPath)) return false;

            string script = File.ReadAllText(scriptPath);
            return script.IndexOf("mvidPattern", StringComparison.Ordinal) >= 0
                && script.IndexOf("assemblyVersionPattern", StringComparison.Ordinal) >= 0
                && script.IndexOf("fileVersionPattern", StringComparison.Ordinal) >= 0
                && script.IndexOf("$mvidPattern", StringComparison.Ordinal) >= 0
                && script.IndexOf("$assemblyVersionPattern", StringComparison.Ordinal) >= 0
                && script.IndexOf("$fileVersionPattern", StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// 票 01 独立核验门：对交付 DLL 直接运行独立产物核验脚本——脚本自行读取 DLL 并重算
        /// FileVersion、程序集版本、MVID、GUID 与 SHA-256，再与 `Build/Version.props` 比对，
        /// 不使用运行时自报告。本测试证明 0.2.4.9 交付产物可被独立核验，且被测试程序集与
        /// 交付 DLL 是同一构建。
        /// </summary>
        internal static bool Test_VerifierConfirmsBuiltArtifactIdentity()
        {
            string scriptPath = VerifierScriptPath();
            string artifactPath = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "bin", "Release", "SteamP2PFriends.dll"));
            if (!File.Exists(scriptPath) || !File.Exists(artifactPath)) return false;

            PluginFingerprintSnapshot fingerprint = PluginFingerprint.Capture(typeof(SteamP2PFriendsPlugin).Assembly);
            string output;
            int exitCode = RunVerifierScript(scriptPath, artifactPath, null, null, out output);

            return exitCode == 0
                && output != null
                && output.IndexOf("INDEPENDENT_ARTIFACT_VERIFICATION_PASS", StringComparison.Ordinal) >= 0
                && output.IndexOf(fingerprint.Mvid, StringComparison.OrdinalIgnoreCase) >= 0
                && output.IndexOf(fingerprint.DllSha256, StringComparison.OrdinalIgnoreCase) >= 0
                && output.IndexOf(PluginBuildMetadata.DefaultCaseId, StringComparison.Ordinal) >= 0;
        }

        internal static bool Test_VerifierRejectsIncompleteLogIdentity()
        {
            string scriptPath = VerifierScriptPath();
            string artifactPath = typeof(SteamP2PFriendsPlugin).Assembly.Location;
            if (!File.Exists(scriptPath) || !File.Exists(artifactPath)) return false;

            string logPath = Path.Combine(Path.GetTempPath(), "spf-incomplete-" + Guid.NewGuid().ToString("N") + ".log");
            try
            {
                File.WriteAllText(logPath, "caseId=" + PluginBuildMetadata.DefaultCaseId + " version=" + PluginBuildMetadata.Version);
                string output;
                int exitCode = RunVerifierScript(scriptPath, artifactPath,
                    PluginBuildMetadata.DefaultCaseId, logPath, out output);
                return exitCode > 0;
            }
            finally
            {
                try { if (File.Exists(logPath)) File.Delete(logPath); }
                catch { }
            }
        }

        private static string VerifierScriptPath()
        {
            return Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Tools", "Verify-BuildFingerprintArtifact.ps1"));
        }

        private static int RunVerifierScript(
            string scriptPath,
            string artifactPath,
            string expectedCaseId,
            string logPath,
            out string output)
        {
            string arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File " +
                QuotePowerShell(scriptPath) +
                " -Path " + QuotePowerShell(artifactPath);
            if (!string.IsNullOrWhiteSpace(expectedCaseId))
                arguments += " -ExpectedCaseId " + QuotePowerShell(expectedCaseId);
            if (!string.IsNullOrWhiteSpace(logPath))
                arguments += " -LogPath " + QuotePowerShell(logPath);

            var startInfo = new ProcessStartInfo("powershell.exe", arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (Process process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    output = null;
                    return -1;
                }

                process.WaitForExit(30000);
                bool exited = process.HasExited;
                output = exited
                    ? process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd()
                    : null;
                return exited ? process.ExitCode : -1;
            }
        }

        /// <summary>
        /// 命令行参数引号：`-File` 与脚本参数都不剥离单引号（单引号会被当作路径字符，
        /// 导致 `不支持给定路径的格式` 而脚本从未执行），必须用双引号包裹。
        /// </summary>
        private static string QuotePowerShell(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
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
