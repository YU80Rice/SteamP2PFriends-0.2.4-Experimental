using System;
using System.Globalization;

namespace SteamP2PFriends.Platform.Diagnostics.Patches
{
    /// <summary>
    /// R8-03：启动期开启被 ROLE_NOT_HOST 拒掉后，下一次事件到达时该等、该试、还是该放弃。
    /// 纯枚举，无 Unity 依赖，离线用例可穷举全部分支。
    /// </summary>
    internal enum Bug1StartupRetryVerdict
    {
        Wait = 0,
        Retry = 1,
        GiveUp = 2,
    }

    /// <summary>R9-01：一次重试尝试的记账结果（纯数据，无 Unity 依赖）。</summary>
    internal struct Bug1RetryOutcome
    {
        public int AttemptsUsed;
        public bool Unhook;
        public bool GiveUp;
        public string Reason;
    }

    /// <summary>
    /// t45：开启入口的纯决策逻辑（不依赖 Unity，可离线用例）。
    /// 负责 RunId/StepId 自动生成、目标选择器匹配、开启前置校验；拒绝原因统一为稳定错误码，
    /// 日志、result.json 与交付说明共用同一套词表。
    /// </summary>
    internal static class Bug1ProbeEntryLogic
    {
        public const string ErrorAlreadyRunning = "ALREADY_RUNNING";
        public const string ErrorRunOrStepMissing = "RUN_OR_STEP_MISSING";
        public const string ErrorTargetMissing = "TARGET_STEAM_ID_MISSING";
        public const string ErrorTargetNotFound = "TARGET_PLAYER_NOT_FOUND";
        public const string ErrorTargetAmbiguous = "TARGET_PLAYER_AMBIGUOUS";
        public const string ErrorSpeedCapUnresolved = "NATIVE_SPEED_CAP_UNRESOLVED";
        public const string ErrorOutputRootUnresolved = "OUTPUT_ROOT_UNRESOLVED";
        public const string ErrorRoleNotHost = "ROLE_NOT_HOST";
        public const string ErrorEntryInitFailed = "ENTRY_INIT_FAILED";

        /// <summary>R9-01 记账原因码（与 RETRY/RETRY_GIVEUP 日志同一词表，不新增对外词）。</summary>
        public const string RetryReasonStarted = "RETRY";
        public const string RetryReasonExhausted = "RETRY_GIVEUP";

        /// <summary>RunId 用 UTC 时间戳生成，同一秒内重复开启由 ALREADY_RUNNING 挡住。</summary>
        public static string BuildRunId(DateTime utcNow)
        {
            return "bug1-" + utcNow.ToUniversalTime()
                .ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        }

        public static string BuildStepId(int index)
        {
            return "step-" + index.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>TargetPlayer 可写玩家名（忽略大小写）或 SteamID64；空选择器永不匹配。</summary>
        public static bool MatchesTarget(string selector, string playerName, ulong steamId)
        {
            if (string.IsNullOrEmpty(selector)) return false;
            string needle = selector.Trim();
            if (needle.Length == 0) return false;
            if (string.Equals(needle, playerName ?? string.Empty, StringComparison.OrdinalIgnoreCase)) return true;
            return string.Equals(needle, steamId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        /// <summary>开启前一次性校验：返回 null 表示四项前置齐备；否则返回稳定错误码，调用方必须拒绝开启。</summary>
        public static string Validate(bool alreadyEnabled, string runId, string stepId, ulong targetSteamId,
            bool speedCapKnown, double speedCapUS, string outputRoot)
        {
            if (alreadyEnabled) return ErrorAlreadyRunning;
            if (string.IsNullOrEmpty(runId) || string.IsNullOrEmpty(stepId)) return ErrorRunOrStepMissing;
            if (targetSteamId == 0UL) return ErrorTargetMissing;
            if (!speedCapKnown || double.IsNaN(speedCapUS) || double.IsInfinity(speedCapUS) || !(speedCapUS > 0d))
                return ErrorSpeedCapUnresolved;
            if (string.IsNullOrEmpty(outputRoot)) return ErrorOutputRootUnresolved;
            return null;
        }

        /// <summary>
        /// R8-03：启动期（stage 6，插件加载期）必然不是 host，此时不能一拒到底。
        /// 判定规则（有界、可关闭）：
        ///   1) 用户已把 Enable 改回 false，或已在采集（running），或重试次数用尽 → GiveUp（摘钩子）；
        ///   2) 还没成为本地 host、关卡未加载、或还没有可观察的客机 → Wait（继续等事件）；
        ///   3) 本地 host 就绪 + 关卡已加载 + 至少 1 名客机 → Retry。
        /// </summary>
        public static Bug1StartupRetryVerdict EvaluateStartupRetry(bool enableRequested, bool running,
            bool isHost, bool levelLoaded, int observableClients, int attemptsUsed, int maxAttempts)
        {
            if (!enableRequested) return Bug1StartupRetryVerdict.GiveUp;
            if (running) return Bug1StartupRetryVerdict.GiveUp;
            if (maxAttempts <= 0 || attemptsUsed >= maxAttempts) return Bug1StartupRetryVerdict.GiveUp;
            if (!isHost) return Bug1StartupRetryVerdict.Wait;
            if (!levelLoaded) return Bug1StartupRetryVerdict.Wait;
            if (observableClients <= 0) return Bug1StartupRetryVerdict.Wait;
            return Bug1StartupRetryVerdict.Retry;
        }

        /// <summary>
        /// R9-01：一次重试尝试的纯记账（不碰 Unity/Provider/日志），供 Control.TryRetryStartup 使用。
        /// 输入：已试次数、上限、本次 TryStart 是否成功、失败码；输出：累计次数、是否摘钩、是否放弃、原因。
        /// 规则：成功 → 摘钩；累计次数达到上限 → 摘钩并放弃；否则保持钩子等下一次事件。
        /// </summary>
        public static Bug1RetryOutcome AccountRetryAttempt(int attemptsUsed, int maxAttempts, bool started,
            string failureCode)
        {
            if (started)
            {
                return new Bug1RetryOutcome
                {
                    AttemptsUsed = attemptsUsed,
                    Unhook = true,
                    GiveUp = false,
                    Reason = RetryReasonStarted,
                };
            }
            int used = attemptsUsed + 1;
            bool exhausted = maxAttempts <= 0 || used >= maxAttempts;
            return new Bug1RetryOutcome
            {
                AttemptsUsed = used,
                Unhook = exhausted,
                GiveUp = exhausted,
                Reason = exhausted ? RetryReasonExhausted : (failureCode ?? string.Empty),
            };
        }
    }
}
