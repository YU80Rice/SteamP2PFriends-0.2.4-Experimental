using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;

namespace SteamP2PFriends.WhitelistTests
{
    internal sealed class RegisteredTestExecution
    {
        internal static bool Test_ChildProtocolRejectsInvalidEvidence()
        {
            const string resultPrefix = "R=";
            const string protocolPrefix = "P=";
            const string good = "P=mvid;method;token;runtime";
            bool valid = ValidateChildProtocol("R=true\n" + good, "0", false,
                resultPrefix, "R=true", protocolPrefix, good);
            return valid
                && Rejects("R=false\n" + good, "0", false, "P=mvid;method;token;runtime")
                && Rejects("R=true\n" + good, "5", false, good)
                && Rejects("R=true", "0", false, good)
                && Rejects("R=true\n" + good + "\n" + good, "0", false, good)
                && Rejects("R=true\nP=wrong-mvid;method;token;runtime", "0", false, good)
                && Rejects("R=true\nP=mvid;wrong-method;token;runtime", "0", false, good)
                && Rejects("R=true\nP=mvid;method;wrong-token;runtime", "0", false, good)
                && Rejects("R=true\nP=mvid;method;token;wrong-runtime", "0", false, good)
                && Rejects("", "0", true, good);
        }

        internal static bool Test_RouteRejectsSameNameFromOtherModule()
        {
            string previous = Environment.GetEnvironmentVariable("SPF_TEST_RUN_LEDGER");
            Environment.SetEnvironmentVariable("SPF_TEST_RUN_LEDGER", null);
            var execution = new RegisteredTestExecution();
            execution.BeginRun(typeof(Program).Assembly);
            Environment.SetEnvironmentVariable("SPF_TEST_RUN_LEDGER", previous);
            MethodInfo real = typeof(ItemPreparationAndFirstDemandTests).GetMethod(
                "Test_GBA01_NativeFirstObserveBindingAdmission",
                BindingFlags.Static | BindingFlags.NonPublic);
            AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName("GBA01.RouteDecoy"), AssemblyBuilderAccess.Run);
            ModuleBuilder module = assembly.DefineDynamicModule("GBA01.RouteDecoy");
            TypeBuilder type = module.DefineType(
                "SteamP2PFriends.WhitelistTests.ItemPreparationAndFirstDemandTests");
            MethodBuilder method = type.DefineMethod("Test_GBA01_NativeFirstObserveBindingAdmission",
                MethodAttributes.Public | MethodAttributes.Static, typeof(bool), Type.EmptyTypes);
            method.GetILGenerator().Emit(OpCodes.Ldc_I4_1);
            method.GetILGenerator().Emit(OpCodes.Ret);
            MethodInfo decoy = type.CreateType().GetMethod(method.Name);
            return execution.IsRoute(real, real.Name) && !execution.IsRoute(decoy, decoy.Name);
        }

        internal sealed class Result
        {
            internal string Mode = "Framework-local";
            internal string ChildExit = "";
            internal string ChildStdout = "";
            internal string ChildStderr = "";
            internal string Outcome = "EXCEPTION";
            internal string Detail = "";
        }

        private readonly string _ledgerPath;
        private readonly string _dispatch;
        private readonly string _dotnet;
        private readonly string _hostDll;
        private Module _testModule;
        private string _runId;
        private string _productionMvid;
        private string _productionPath;
        private int _lookupCount;
        private int _bindingCount;
        private int _dispatchCount;

        internal RegisteredTestExecution()
        {
            _ledgerPath = Environment.GetEnvironmentVariable("SPF_TEST_RUN_LEDGER");
            _dispatch = Environment.GetEnvironmentVariable("SPF_NIL01_DISPATCH");
            _dotnet = Environment.GetEnvironmentVariable("SPF_NIL01_DOTNET");
            _hostDll = Environment.GetEnvironmentVariable("SPF_NIL01_HOST_DLL");
        }

        internal bool Configured => !string.IsNullOrWhiteSpace(_dispatch);
        internal int LookupCount => _lookupCount;
        internal int BindingCount => _bindingCount;
        internal string LedgerPath => _ledgerPath;
        internal string ProductionMvid => _productionMvid;

        internal void BeginRun(Assembly testAssembly)
        {
            _testModule = testAssembly.ManifestModule;
            if (string.IsNullOrWhiteSpace(_ledgerPath)) return;
            if (!Configured) throw new InvalidOperationException("A formal ledger requires explicit native dispatch.");
            _runId = Guid.NewGuid().ToString("D");
            Assembly production = Assembly.Load(new AssemblyName("SteamP2PFriends"));
            _productionMvid = production.ManifestModule.ModuleVersionId.ToString();
            _productionPath = production.Location;
            Console.WriteLine("RUN_CANDIDATE production=" + _productionPath + "; productionMvid=" + _productionMvid
                + "; tests=" + testAssembly.Location + "; testsMvid=" + testAssembly.ManifestModule.ModuleVersionId);
            File.WriteAllText(_ledgerPath,
                "runId\tordinal\tevidenceClass\tregisteredName\tcallback\ttestPath\ttestMvid\tproductionPath\tproductionMvid\tlocalRuntime\tlocalArch\texecutionMode\tresult\tdetail\tchildExit\tstarted\tended\tchildStdout\tchildStderr" + Environment.NewLine,
                new System.Text.UTF8Encoding(false));
        }

        internal bool Execute(Func<bool> test, out Result result)
        {
            result = new Result();
            MethodInfo method = test.Method;
            if (!Configured) return test();
            if (_dispatch != "CoreCLR10.0.9-x86") throw new InvalidOperationException("Unknown native dispatch mode: " + _dispatch);
            bool lookup = IsRoute(method, "Test_NIL01_RealNativeLookupContract");
            bool binding = IsRoute(method, "Test_GBA01_NativeFirstObserveBindingAdmission");
            if (!lookup && !binding) return test();
            if (test.Target != null) throw new InvalidOperationException("Native route cannot capture a delegate target.");
            _dispatchCount++; if (lookup) _lookupCount++; else _bindingCount++;
            result.Mode = "CoreCLR-child";
            return lookup
                ? ExecuteNativeChild(method, true, out result.ChildExit, out result.ChildStdout, out result.ChildStderr)
                : ExecuteNativeChild(method, false, out result.ChildExit, out result.ChildStdout, out result.ChildStderr);
        }

        internal bool ValidateCompleteness()
        {
            return !Configured || (_dispatchCount == 2 && _lookupCount == 1 && _bindingCount == 1);
        }

        private bool ExecuteNativeChild(MethodInfo test, bool lookup, out string childExit,
            out string childOut, out string childErr)
        {
            if (string.IsNullOrWhiteSpace(_ledgerPath)) throw new InvalidOperationException("Native dispatch requires a run ledger.");
            string dotnet = Required(_dotnet, "SPF_NIL01_DOTNET");
            string hostDll = Required(_hostDll, "SPF_NIL01_HOST_DLL");
            Assembly production = Assembly.Load(new AssemblyName("SteamP2PFriends"));
            Type bridge = production.GetType("SteamP2PFriends.Adapters.Item.DefaultNativeItemWarehouseBridge", true);
            MethodInfo target = bridge.GetMethod(lookup ? "FindClient" : "BindGuestBaselineCore",
                BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic);
            if (target == null) throw new MissingMethodException("Current native route target was not loaded.");
            string prodMvid = production.ManifestModule.ModuleVersionId.ToString();
            string testMvid = test.Module.ModuleVersionId.ToString();
            string suffix = lookup ? ".nil01-child" : ".gba01-child";
            childOut = _ledgerPath + suffix + ".stdout.txt"; childErr = _ledgerPath + suffix + ".stderr.txt";
            string arguments = "\"" + hostDll + "\" " + (lookup ? "lookup" : "binding " + prodMvid + " " + testMvid
                + " " + target.MetadataToken.ToString("X8") + " " + test.MetadataToken.ToString("X8"));
            var start = new ProcessStartInfo(dotnet, arguments) { WorkingDirectory = Path.GetDirectoryName(hostDll),
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
                StandardOutputEncoding = new System.Text.UTF8Encoding(false), StandardErrorEncoding = new System.Text.UTF8Encoding(false) };
            if (lookup)
            {
                start.EnvironmentVariables["SPF_NIL01_EXPECTED_PRODUCTION_MVID"] = prodMvid;
                start.EnvironmentVariables["SPF_NIL01_EXPECTED_TESTS_MVID"] = testMvid;
                start.EnvironmentVariables["SPF_NIL01_EXPECTED_FINDCLIENT_TOKEN"] = "0x" + target.MetadataToken.ToString("X8");
            }
            string output, error; bool timedOut = false;
            using (Process process = Process.Start(start))
            {
                if (process == null) throw new InvalidOperationException("CoreCLR child failed to start.");
                Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                Task<string> stderrTask = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(60000)) { process.Kill(); process.WaitForExit(); timedOut = true; }
                Task.WaitAll(stdoutTask, stderrTask);
                childExit = process.ExitCode.ToString(); output = stdoutTask.Result; error = stderrTask.Result;
            }
            File.WriteAllText(childOut, output, new System.Text.UTF8Encoding(false));
            File.WriteAllText(childErr, error, new System.Text.UTF8Encoding(false));
            Console.WriteLine((lookup ? "NIL01" : "GBA01") + " child stdout=" + childOut); Console.Write(output);
            if (error.Length != 0) Console.Error.Write(error);
            string protocol = lookup
                ? "NIL01_CHILD_PROTOCOL=1; RESULT=True; RUNTIME=10.0.9; ARCH=x86; PRODUCTION_MVID=" + prodMvid
                    + "; TESTS_MVID=" + testMvid + "; METHOD=SteamP2PFriends.WhitelistTests.ItemPreparationAndFirstDemandTests.Test_NIL01_RealNativeLookupContract; METHOD_TOKEN=0x"
                    + test.MetadataToken.ToString("X8") + "; TARGET_TOKEN=0x" + target.MetadataToken.ToString("X8")
                : "GBA01_CHILD_PROTOCOL=1; RESULT=True; RUNTIME=10.0.9; ARCH=x86; PRODUCTION_MVID=" + prodMvid
                    + "; TESTS_MVID=" + testMvid + "; METHOD=" + test.DeclaringType.FullName + "." + test.Name
                    + "; METHOD_TOKEN=0x" + test.MetadataToken.ToString("X8") + "; TARGET_TOKEN=0x" + target.MetadataToken.ToString("X8");
            return ValidateChildProtocol(output, childExit, timedOut,
                lookup ? "TEST_RESULT=" : "GBA01_RESULT=", lookup ? "TEST_RESULT=True" : "GBA01_RESULT=true",
                lookup ? "NIL01_CHILD_PROTOCOL=" : "GBA01_CHILD_PROTOCOL=", protocol);
        }

        private static string Required(string value, string name) => !string.IsNullOrWhiteSpace(value)
            ? value : throw new InvalidOperationException("Required native dispatch value is missing: " + name);

        internal void AppendResult(int ordinal, EvidenceClass evidenceClass, string name, Func<bool> test,
            Result result, DateTimeOffset started, DateTimeOffset ended)
        {
            if (string.IsNullOrWhiteSpace(_ledgerPath)) return;
            MethodInfo method = test.Method;
            Assembly production = Assembly.Load(new AssemblyName("SteamP2PFriends"));
            string callback = method.DeclaringType?.FullName + "." + method.Name + "#0x"
                + method.MetadataToken.ToString("X8") + ";target="
                + (test.Target == null ? "null" : test.Target.GetType().FullName);
            string[] fields = { _runId, ordinal.ToString(), evidenceClass.ToString(), name, callback,
                method.Module.Assembly.Location, method.Module.ModuleVersionId.ToString(),
                production.Location, _productionMvid, Environment.Version.ToString(),
                Environment.Is64BitProcess ? "x64" : "x86", result.Mode, result.Outcome,
                result.Detail, result.ChildExit, started.ToString("o"), ended.ToString("o"),
                result.ChildStdout, result.ChildStderr };
            for (int i = 0; i < fields.Length; i++) fields[i] = Escape(fields[i]);
            File.AppendAllText(_ledgerPath, string.Join("\t", fields) + Environment.NewLine,
                new System.Text.UTF8Encoding(false));
        }

        private static string Escape(string value) => (value ?? "").Replace("\\", "\\\\")
            .Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");

        private static bool Rejects(string output, string exit, bool timedOut, string expected)
        {
            try
            {
                ValidateChildProtocol(output, exit, timedOut, "R=", "R=true", "P=", expected);
                return false;
            }
            catch (InvalidOperationException) { return true; }
            catch (TimeoutException) { return timedOut; }
        }

        internal bool IsRoute(MethodInfo method, string name)
        {
            return method.Module == _testModule && method.IsStatic
                && method.ReturnType == typeof(bool) && method.GetParameters().Length == 0
                && method.DeclaringType?.FullName == "SteamP2PFriends.WhitelistTests.ItemPreparationAndFirstDemandTests"
                && method.Name == name;
        }

        internal static bool ValidateChildProtocol(string output, string childExit, bool timedOut,
            string resultPrefix, string expectedResult, string protocolPrefix, string expectedProtocol)
        {
            if (timedOut) throw new TimeoutException("Native child timed out.");
            if (childExit != "0") throw new InvalidOperationException("Native child exit was " + childExit + ".");
            int results = 0, protocols = 0;
            foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith(resultPrefix, StringComparison.Ordinal))
                { results++; if (line != expectedResult) throw new InvalidOperationException("Native child result mismatch."); }
                if (line.StartsWith(protocolPrefix, StringComparison.Ordinal))
                { protocols++; if (line != expectedProtocol) throw new InvalidOperationException("Native child identity mismatch."); }
            }
            if (results != 1 || protocols != 1) throw new InvalidOperationException("Native child result protocol missing or duplicated.");
            return true;
        }
    }
}
