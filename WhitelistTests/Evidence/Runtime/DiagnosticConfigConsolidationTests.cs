using BepInEx.Configuration;
using HarmonyLib;
using SteamP2PFriends.Core.Config;
using SteamP2PFriends.Core.Patches.GunshotHostAudio;
using SteamP2PFriends.Platform.Diagnostics.Patches;
using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// t145/t146：三个诊断开关（Bug1Probe / Bug1Gate / Bug2HostGunshot）统一并入插件主配置。
    ///
    /// 自动化层只证明：
    ///  - 主 cfg 的键与默认值存在且可读；
    ///  - 启动自证行包含绝对路径与三个布尔；
    ///  - 三个模块的 Initialize 不再自行 new ConfigFile（IL 契约，可被「改回旧行为」变异打红）；
    ///  - 热切换主 cfg 的 Bug2HostGunshot.Enable 能当场安装/卸载（沿用真实 Harmony 缝）。
    ///
    /// 不证明：真实 BepInEx 在游戏内创建 cfg 的路径行为；实机日志是否落盘。
    /// </summary>
    internal static class DiagnosticConfigConsolidationTests
    {
        internal static bool Test_MainConfigDefaultsAndKeys()
        {
            Assert(PluginConfig.Bug1ProbeEnable != null, "Bug1Probe.Enable entry must be bound");
            Assert(PluginConfig.Bug1ProbeTargetPlayer != null, "Bug1Probe.TargetPlayer entry must be bound");
            Assert(PluginConfig.Bug1ProbeSpeedTier != null, "Bug1Probe.SpeedTier entry must be bound");
            Assert(PluginConfig.Bug1GateEnable != null, "Bug1Gate.Enable entry must be bound");
            Assert(PluginConfig.Bug1GateTargetPlayer != null, "Bug1Gate.TargetPlayer entry must be bound");
            Assert(PluginConfig.Bug1GateOffsetU != null, "Bug1Gate.OffsetU entry must be bound");
            Assert(PluginConfig.Bug1GateMaxDeviationU != null, "Bug1Gate.MaxDeviationU entry must be bound");
            Assert(PluginConfig.Bug1GateSamples != null, "Bug1Gate.Samples entry must be bound");
            Assert(PluginConfig.Bug1GateSampleIntervalSeconds != null, "Bug1Gate.SampleIntervalSeconds entry must be bound");
            Assert(PluginConfig.Bug1GateMinSamplesPerCell != null, "Bug1Gate.MinSamplesPerCell entry must be bound");
            Assert(PluginConfig.Bug1GateMaxSamples != null, "Bug1Gate.MaxSamples entry must be bound");
            Assert(PluginConfig.Bug2HostGunshotEnable != null, "Bug2HostGunshot.Enable entry must be bound");

            Assert(!PluginConfig.Bug1ProbeEnable.Value, "Bug1Probe.Enable default must be false");
            Assert(PluginConfig.Bug1ProbeTargetPlayer.Value == "", "Bug1Probe.TargetPlayer default must be empty");
            Assert(PluginConfig.Bug1ProbeSpeedTier.Value == "auto", "Bug1Probe.SpeedTier default must be auto");
            Assert(!PluginConfig.Bug1GateEnable.Value, "Bug1Gate.Enable default must be false");
            Assert(PluginConfig.Bug1GateTargetPlayer.Value == "", "Bug1Gate.TargetPlayer default must be empty");
            Assert(Math.Abs(PluginConfig.Bug1GateOffsetU.Value - 0.1f) < 1e-6f, "Bug1Gate.OffsetU default must be 0.1");
            Assert(Math.Abs(PluginConfig.Bug1GateMaxDeviationU.Value) < 1e-6f, "Bug1Gate.MaxDeviationU default must be 0");
            Assert(PluginConfig.Bug1GateSamples.Value == 64, "Bug1Gate.Samples default must be 64");
            Assert(Math.Abs(PluginConfig.Bug1GateSampleIntervalSeconds.Value - 1f) < 1e-6f,
                "Bug1Gate.SampleIntervalSeconds default must be 1");
            Assert(PluginConfig.Bug1GateMinSamplesPerCell.Value == 3, "Bug1Gate.MinSamplesPerCell default must be 3");
            Assert(PluginConfig.Bug1GateMaxSamples.Value == 240, "Bug1Gate.MaxSamples default must be 240");
            Assert(!PluginConfig.Bug2HostGunshotEnable.Value, "Bug2HostGunshot.Enable default must be false");

            Assert(PluginConfig.BoundConfigFilePath.Length > 0, "bound config path must be recorded");
            Assert(Path.IsPathRooted(PluginConfig.BoundConfigFilePath), "bound config path must be absolute");
            return true;
        }

        internal static bool Test_StartupConfigLineFormat()
        {
            string line = PluginConfig.FormatDiagnosticsConfigLine();
            Assert(line.StartsWith("[Diagnostics] configPath=", StringComparison.Ordinal),
                "startup line must start with [Diagnostics] configPath=, got: " + line);
            Assert(line.Contains("configPath=" + PluginConfig.BoundConfigFilePath),
                "startup line must contain the absolute config path, got: " + line);
            Assert(line.Contains(" bug1Probe="), "startup line must contain bug1Probe=");
            Assert(line.Contains(" bug1Gate="), "startup line must contain bug1Gate=");
            Assert(line.Contains(" bug2HostGunshot="), "startup line must contain bug2HostGunshot=");
            Assert(line.Contains("bug1Probe=" + BoolText(PluginConfig.Bug1ProbeEnable.Value)),
                "bug1Probe value mismatch: " + line);
            Assert(line.Contains("bug1Gate=" + BoolText(PluginConfig.Bug1GateEnable.Value)),
                "bug1Gate value mismatch: " + line);
            Assert(line.Contains("bug2HostGunshot=" + BoolText(PluginConfig.Bug2HostGunshotEnable.Value)),
                "bug2HostGunshot value mismatch: " + line);

            // t153（R31-03）：自证行由 PluginConfig.Bind 在绑定完成处无条件发出。
            // 该点位于任何注册/诊断 stage 之前，前序 stage 失败也不会吞掉这一行。
            MethodInfo bind = AccessTools.Method(typeof(PluginConfig), "Bind",
                new[] { typeof(BepInEx.Configuration.ConfigFile) });
            Assert(bind != null, "PluginConfig.Bind(ConfigFile) not found");
            int emitted = IlContractProbe.CountMethodCalls(bind,
                "SteamP2PFriends.Core.Config.PluginConfig", "FormatDiagnosticsConfigLine");
            Assert(emitted >= 1, "PluginConfig.Bind must emit FormatDiagnosticsConfigLine, calls=" + emitted);

            // R32-02（t157）：只按名字计数钉不住实参——审查官把发射点改成
            // FormatDiagnosticsConfigLine(BoundConfigFilePath, false, false, false) 后全绿。
            // 现在的发射点是「读开关 + 双参重载」：Bind 必须调用
            // FormatDiagnosticsConfigLine(string, (bool,bool,bool))，且必须调用 ReadDiagnosticsSwitches()。
            MethodInfo emitCall = null;
            bool readsSwitches = false;
            foreach (GunshotIlScan.IlInstruction instruction in GunshotIlScan.Read(bind))
            {
                if (instruction.OpCode.FlowControl != FlowControl.Call || !(instruction.Operand is MethodInfo called))
                    continue;
                if (called.DeclaringType == typeof(PluginConfig) && called.Name == "ReadDiagnosticsSwitches")
                    readsSwitches = true;
                if (called.DeclaringType == typeof(PluginConfig) && called.Name == "FormatDiagnosticsConfigLine")
                    emitCall = called;
            }
            Assert(readsSwitches,
                "PluginConfig.Bind must call ReadDiagnosticsSwitches() before formatting");
            Assert(emitCall != null,
                "PluginConfig.Bind must call FormatDiagnosticsConfigLine");
            Assert(emitCall.GetParameters().Length == 2,
                "PluginConfig.Bind must call FormatDiagnosticsConfigLine(string, switches), got "
                + emitCall.GetParameters().Length + " parameter(s)");
            return true;
        }

        /// <summary>
        /// t157（R32-02）：读开关这一环必须真读 ConfigEntry。
        /// 用一份全部 Enable=true 的主 cfg 完成一次完整 Bind，然后：
        ///  - ReadDiagnosticsSwitches() 三个值必须全 true；
        ///  - 启动行文本（wrapper）三个布尔必须全 True；
        ///  - 再把三个 entry 改为 false，两个观测点必须同步变 False。
        /// 把 ReadDiagnosticsSwitches() 里任一项写成常量 false，第一条断言必红。
        /// </summary>
        internal static bool Test_DiagnosticsSwitchesFollowEntries()
        {
            string path = Path.Combine(Path.GetTempPath(), "spf-t157-" + Guid.NewGuid().ToString("N") + ".cfg");
            File.WriteAllText(path,
                "[Bug1Probe]\nEnable = true\n\n[Bug1Gate]\nEnable = true\n\n[Bug2HostGunshot]\nEnable = true\n");
            ConfigFile config = new ConfigFile(path, false);
            try
            {
                PluginConfig.Bind(config);

                (bool probe, bool gate, bool gun) switches = PluginConfig.ReadDiagnosticsSwitches();
                Assert(switches.probe && switches.gate && switches.gun,
                    "ReadDiagnosticsSwitches must follow the bound entries (all true), got "
                    + switches.probe + "/" + switches.gate + "/" + switches.gun);

                string line = PluginConfig.FormatDiagnosticsConfigLine();
                Assert(line.Contains("bug1Probe=True") && line.Contains("bug1Gate=True")
                    && line.Contains("bug2HostGunshot=True"),
                    "startup line must report True for all three bound switches, got: " + line);

                PluginConfig.Bug1ProbeEnable.Value = false;
                PluginConfig.Bug1GateEnable.Value = false;
                PluginConfig.Bug2HostGunshotEnable.Value = false;

                switches = PluginConfig.ReadDiagnosticsSwitches();
                Assert(!switches.probe && !switches.gate && !switches.gun,
                    "ReadDiagnosticsSwitches must follow entries after they turn false, got "
                    + switches.probe + "/" + switches.gate + "/" + switches.gun);

                line = PluginConfig.FormatDiagnosticsConfigLine();
                Assert(line.Contains("bug1Probe=False") && line.Contains("bug1Gate=False")
                    && line.Contains("bug2HostGunshot=False"),
                    "startup line must report False after entries turn false, got: " + line);
                return true;
            }
            finally
            {
                // 恢复成默认真值的主 cfg，避免影响后续用例。
                string resetPath = Path.Combine(Path.GetTempPath(), "spf-t157-" + Guid.NewGuid().ToString("N") + ".cfg");
                PluginConfig.Bind(new ConfigFile(resetPath, false));
                try { File.Delete(path); } catch { }
                try { File.Delete(resetPath); } catch { }
            }
        }

        internal static bool Test_ModulesDoNotCreateIndependentConfigFiles()
        {
            Type[] controls =
            {
                typeof(GunshotHostAudioControl),
                typeof(Bug1ProbeControl),
                typeof(Bug1GateProbe)
            };
            foreach (Type control in controls)
            {
                // t153：不能只扫 Initialize。枚举类型及其嵌套编译器生成类型（<>c / <>c__DisplayClass）
                // 的全部 DeclaredOnly 方法与构造函数，避免把新加的 __LoadLegacyCfg() 之类的私有/lambda 路径漏掉。
                int created = CountConfigFileCtorsInTypeTree(control);
                Assert(created == 0,
                    control.Name + " (all methods incl. compiler-generated lambdas) must not construct ConfigFile (found "
                    + created + "); diagnostics must read the plugin main config");
                Assert(control.GetField("ConfigFileName", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) == null,
                    control.Name + " must not keep an independent ConfigFileName");
            }

            // 源码级兜底：Core/ 与 Platform/ 的非注释代码不得出现 new ConfigFile /
            // new BepInEx.Configuration.ConfigFile；旧三个 cfg 文件名只允许出现在注释里。
            string root = RepositoryRoot();
            Assert(Directory.Exists(Path.Combine(root, "Core")), "repository Core/ not found at " + root);
            Assert(Directory.Exists(Path.Combine(root, "Platform")), "repository Platform/ not found at " + root);

            int constructed = 0;
            int legacyNameInCode = 0;
            foreach (string file in EnumerateSourceFiles(root))
            {
                string code = StripComments(File.ReadAllText(file));
                constructed += CountOccurrences(code, "new ConfigFile");
                constructed += CountOccurrences(code, "new BepInEx.Configuration.ConfigFile");
                legacyNameInCode += CountOccurrences(code, "SteamP2PFriends.Bug1Probe.cfg");
                legacyNameInCode += CountOccurrences(code, "SteamP2PFriends.Bug1Gate.cfg");
                legacyNameInCode += CountOccurrences(code, "SteamP2PFriends.Bug2HostGunshot.cfg");
            }

            Assert(constructed == 0, "Core/ and Platform/ must not construct ConfigFile outside comments (found " + constructed + ")");
            Assert(legacyNameInCode == 0,
                "legacy cfg filenames may only appear in comments, found " + legacyNameInCode + " in code");
            return true;
        }

        internal static bool Test_FormatLineTruthTable()
        {
            string path = @"C:\spf\com.yu80rice.steamp2pfriends.cfg";
            for (int bits = 0; bits < 8; bits++)
            {
                bool probe = (bits & 1) != 0;
                bool gate = (bits & 2) != 0;
                bool gun = (bits & 4) != 0;
                string expected = "[Diagnostics] configPath=" + path
                    + " bug1Probe=" + BoolText(probe)
                    + " bug1Gate=" + BoolText(gate)
                    + " bug2HostGunshot=" + BoolText(gun);
                string actual = PluginConfig.FormatDiagnosticsConfigLine(path, probe, gate, gun);
                Assert(actual == expected,
                    "truth-table mismatch for probe=" + probe + " gate=" + gate + " gun=" + gun
                    + "; expected [" + expected + "] actual [" + actual + "]");
            }

            string unbound = PluginConfig.FormatDiagnosticsConfigLine("", false, false, false);
            Assert(unbound.Contains("configPath=<unbound>"), "empty path must render <unbound>, got: " + unbound);
            string nullPath = PluginConfig.FormatDiagnosticsConfigLine(null, true, false, true);
            Assert(nullPath.Contains("configPath=<unbound>"), "null path must render <unbound>, got: " + nullPath);
            return true;
        }

        internal static bool Test_FormatLineWiring()
        {
            ConfigEntry<bool> probe = PluginConfig.Bug1ProbeEnable;
            ConfigEntry<bool> gate = PluginConfig.Bug1GateEnable;
            ConfigEntry<bool> gun = PluginConfig.Bug2HostGunshotEnable;
            Assert(probe != null && gate != null && gun != null, "main cfg entries must be bound");

            bool oldProbe = probe.Value;
            bool oldGate = gate.Value;
            bool oldGun = gun.Value;
            try
            {
                probe.Value = false;
                gate.Value = true;
                gun.Value = true;
                string enabledLine = PluginConfig.FormatDiagnosticsConfigLine();
                Assert(enabledLine.Contains("bug1Probe=False"), "wiring: bug1Probe must be False, got: " + enabledLine);
                Assert(enabledLine.Contains("bug1Gate=True"), "wiring: bug1Gate must be True, got: " + enabledLine);
                Assert(enabledLine.Contains("bug2HostGunshot=True"),
                    "wiring: bug2HostGunshot must be True, got: " + enabledLine);

                gate.Value = false;
                gun.Value = false;
                string disabledLine = PluginConfig.FormatDiagnosticsConfigLine();
                Assert(disabledLine.Contains("bug1Gate=False"), "wiring: bug1Gate must be False, got: " + disabledLine);
                Assert(disabledLine.Contains("bug2HostGunshot=False"),
                    "wiring: bug2HostGunshot must be False, got: " + disabledLine);
            }
            finally
            {
                probe.Value = oldProbe;
                gate.Value = oldGate;
                gun.Value = oldGun;
            }

            return true;
        }

        private static int CountConfigFileCtorsInTypeTree(Type root)
        {
            int total = 0;
            var pending = new System.Collections.Generic.Stack<Type>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                Type type = pending.Pop();
                foreach (Type nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                {
                    pending.Push(nested);
                }

                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                foreach (MethodInfo method in type.GetMethods(flags))
                {
                    total += IlContractProbe.CountMethodCalls(method, "BepInEx.Configuration.ConfigFile", ".ctor");
                }

                foreach (ConstructorInfo ctor in type.GetConstructors(flags))
                {
                    total += IlContractProbe.CountMethodCalls(ctor, "BepInEx.Configuration.ConfigFile", ".ctor");
                }
            }

            return total;
        }

        private static string RepositoryRoot()
        {
            return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."));
        }

        private static System.Collections.Generic.IEnumerable<string> EnumerateSourceFiles(string root)
        {
            foreach (string dir in new[] { "Core", "Platform" })
            {
                foreach (string file in Directory.GetFiles(Path.Combine(root, dir), "*.cs", SearchOption.AllDirectories))
                {
                    yield return file;
                }
            }
        }

        /// <summary>
        /// 去掉 // 与 /* */ 注释（保留字符串字面量内容），用于「非注释代码」文本断言。
        /// 这是测试侧的简化扫描器，只服务于本契约，不参与生产逻辑。
        /// </summary>
        private static string StripComments(string source)
        {
            var builder = new System.Text.StringBuilder(source.Length);
            bool inLineComment = false;
            bool inBlockComment = false;
            bool inString = false;
            bool inChar = false;
            bool escaped = false;
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                char next = i + 1 < source.Length ? source[i + 1] : '\0';
                if (inLineComment)
                {
                    if (c == '\n') { inLineComment = false; builder.Append(c); }
                    else builder.Append(' ');
                    continue;
                }

                if (inBlockComment)
                {
                    if (c == '*' && next == '/') { inBlockComment = false; builder.Append("  "); i++; }
                    else builder.Append(c == '\n' ? '\n' : ' ');
                    continue;
                }

                if (inString)
                {
                    builder.Append(c);
                    if (escaped) { escaped = false; }
                    else if (c == '\\') { escaped = true; }
                    else if (c == '"') { inString = false; }
                    continue;
                }

                if (inChar)
                {
                    builder.Append(c);
                    if (escaped) { escaped = false; }
                    else if (c == '\\') { escaped = true; }
                    else if (c == '\'') { inChar = false; }
                    continue;
                }

                if (c == '/' && next == '/') { inLineComment = true; builder.Append("  "); i++; continue; }
                if (c == '/' && next == '*') { inBlockComment = true; builder.Append("  "); i++; continue; }
                if (c == '"') { inString = true; builder.Append(c); continue; }
                if (c == '\'') { inChar = true; builder.Append(c); continue; }
                builder.Append(c);
            }

            return builder.ToString();
        }

        private static int CountOccurrences(string text, string needle)
        {
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }

            return count;
        }

        internal static bool Test_GunshotHotToggleFromMainConfig()
        {
            ConfigEntry<bool> entry = PluginConfig.Bug2HostGunshotEnable;
            Assert(entry != null, "Bug2HostGunshot.Enable must be bound");

            bool previous = entry.Value;
            entry.Value = false;
            Harmony harmony = new Harmony("spf.t145.diagnostic-config");
            try
            {
                GunshotHostAudioControl.ResetForTest();
                GunshotHostAudioPatch.ResetForTest();
                GunshotHostAudioPatch.FireTargetForTest = AccessTools.Method(typeof(FakeGun), "FakeFire");
                GunshotHostAudioPatch.PlayGunshotTargetForTest = AccessTools.Method(typeof(FakeGun), "FakeLeaf");
                GunshotHostAudioPatch.SkipAnchorVerifyForTest = true;
                GunshotHostAudioPatch.SkipFirePatchForTest = true;

                Assert(GunshotHostAudioControl.Initialize(harmony), "initialize with main cfg entry");
                Assert(!GunshotHostAudioPatch.Installed, "disabled module must not install patches");

                // 热开启：SettingChanged 绑在主 cfg 的 ConfigEntry 上，当场安装。
                entry.Value = true;
                Assert(GunshotHostAudioPatch.Installed, "hot enable from main cfg must install patches");
                Assert(GunshotHostAudioControl.IsEnabled, "module must report enabled after hot toggle");
                // R31-01 接线：同一个 true 窗口内，自证行必须真实反映 bug2HostGunshot=True。
                Assert(PluginConfig.FormatDiagnosticsConfigLine().Contains("bug2HostGunshot=True"),
                    "startup line must show bug2HostGunshot=True while enabled, got: "
                    + PluginConfig.FormatDiagnosticsConfigLine());

                // 热关闭：当场卸载。
                entry.Value = false;
                Assert(!GunshotHostAudioPatch.Installed, "hot disable from main cfg must remove patches");
                Assert(!GunshotHostAudioControl.IsEnabled, "module must report disabled after hot toggle");
                Assert(PluginConfig.FormatDiagnosticsConfigLine().Contains("bug2HostGunshot=False"),
                    "startup line must show bug2HostGunshot=False after disable, got: "
                    + PluginConfig.FormatDiagnosticsConfigLine());
            }
            finally
            {
                entry.Value = false;
                GunshotHostAudioPatch.Uninstall();
                GunshotHostAudioControl.ResetForTest();
                GunshotHostAudioPatch.ResetForTest();
                entry.Value = previous;
            }

            return true;
        }

        private static string BoolText(bool value)
        {
            return value ? "True" : "False";
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
