using SteamP2PFriends.Core.Build;
using SteamP2PFriends.Shared;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends.Adapters.Resource
{
    /// <summary>
    /// Resource 观测路径的受控值集合。禁止调用方用任意字符串伪造路径语义。
    /// </summary>
    internal readonly struct ResourceObservationPath : IEquatable<ResourceObservationPath>
    {
        private readonly string _value;

        private ResourceObservationPath(string value) { _value = value; }

        internal static readonly ResourceObservationPath SPI = new ResourceObservationPath("SPI");
        internal static readonly ResourceObservationPath Native = new ResourceObservationPath("Native");
        internal static readonly ResourceObservationPath Legacy = new ResourceObservationPath("Legacy");
        internal static readonly ResourceObservationPath Fallback = new ResourceObservationPath("Fallback");
        internal static readonly ResourceObservationPath Unknown = new ResourceObservationPath("Unknown");

        internal static ResourceObservationPath From(string value)
        {
            switch (value)
            {
                case "SPI": return SPI;
                case "Native": return Native;
                case "Legacy": return Legacy;
                case "Fallback": return Fallback;
                case "Unknown": return Unknown;
                default: throw new ArgumentOutOfRangeException(nameof(value), "Unknown Resource observation path.");
            }
        }

        public bool Equals(ResourceObservationPath other) => string.Equals(_value, other._value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is ResourceObservationPath other && Equals(other);
        public override int GetHashCode() => _value == null ? 0 : _value.GetHashCode();
        public override string ToString() => _value ?? "Unknown";
        public static implicit operator ResourceObservationPath(string value) => From(value);
    }

    /// <summary>
    /// Resource 观测结果的受控值集合。
    /// </summary>
    internal readonly struct ResourceObservationOutcome : IEquatable<ResourceObservationOutcome>
    {
        private readonly string _value;

        private ResourceObservationOutcome(string value) { _value = value; }

        internal static readonly ResourceObservationOutcome Success = new ResourceObservationOutcome("success");
        internal static readonly ResourceObservationOutcome Failed = new ResourceObservationOutcome("failed");
        internal static readonly ResourceObservationOutcome Skipped = new ResourceObservationOutcome("skipped");
        internal static readonly ResourceObservationOutcome Rejected = new ResourceObservationOutcome("rejected");
        internal static readonly ResourceObservationOutcome Deferred = new ResourceObservationOutcome("deferred");
        internal static readonly ResourceObservationOutcome Attempt = new ResourceObservationOutcome("attempt");
        internal static readonly ResourceObservationOutcome Scheduled = new ResourceObservationOutcome("scheduled");
        internal static readonly ResourceObservationOutcome Observed = new ResourceObservationOutcome("observed");
        internal static readonly ResourceObservationOutcome Suppressed = new ResourceObservationOutcome("suppressed");
        internal static readonly ResourceObservationOutcome Accepted = new ResourceObservationOutcome("accepted");
        internal static readonly ResourceObservationOutcome Unknown = new ResourceObservationOutcome("unknown");

        internal static ResourceObservationOutcome From(string value)
        {
            switch (value)
            {
                case "success": return Success;
                case "failed": return Failed;
                case "skipped": return Skipped;
                case "rejected": return Rejected;
                case "deferred": return Deferred;
                case "attempt": return Attempt;
                case "scheduled": return Scheduled;
                case "observed": return Observed;
                case "suppressed": return Suppressed;
                case "accepted": return Accepted;
                case "unknown": return Unknown;
                default: throw new ArgumentOutOfRangeException(nameof(value), "Unknown Resource observation outcome.");
            }
        }

        public bool Equals(ResourceObservationOutcome other) => string.Equals(_value, other._value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is ResourceObservationOutcome other && Equals(other);
        public override int GetHashCode() => _value == null ? 0 : _value.GetHashCode();
        public override string ToString() => _value ?? "unknown";
        public static implicit operator ResourceObservationOutcome(string value) => From(value);
    }

    /// <summary>
    /// Resource 运行时证据的单一格式化出口。
    /// 所有路径都输出同一组字段，避免 SPI 未接线时与原生路径混淆。
    /// </summary>
    internal static class ResourceObservability
    {
        private static readonly object SyncLock = new object();
        private static readonly HashSet<string> QuotaNotices = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> OneTimeNotices = new HashSet<string>(StringComparer.Ordinal);

        internal static void ResetSession()
        {
            lock (SyncLock)
            {
                QuotaNotices.Clear();
                OneTimeNotices.Clear();
            }
        }

        internal static void QuotaSuppressed(
            string role,
            string eventName,
            string region,
            ulong sessionEpoch,
            ulong connectionGeneration,
            uint regionGeneration,
            ResourceObservationPath path,
            bool spiActive,
            string point)
        {
            bool firstNotice;
            lock (SyncLock) firstNotice = QuotaNotices.Add(point ?? eventName ?? "unknown");
            if (!firstNotice) return;

            Warn(role, eventName, region, sessionEpoch, connectionGeneration, regionGeneration,
                path, spiActive, ResourceObservationOutcome.Suppressed, "reason=diagnostic-quota-exhausted point=" + point);
        }

        internal static void NoticeOnce(
            string key,
            string role,
            string eventName,
            string region,
            ulong sessionEpoch,
            ulong connectionGeneration,
            uint regionGeneration,
            ResourceObservationPath path,
            bool spiActive,
            ResourceObservationOutcome outcome,
            string detail)
        {
            bool firstNotice;
            lock (SyncLock) firstNotice = OneTimeNotices.Add(key ?? eventName ?? "unknown");
            if (!firstNotice) return;

            Warn(role, eventName, region, sessionEpoch, connectionGeneration, regionGeneration,
                path, spiActive, outcome, detail);
        }

        internal static string Format(
            string role,
            string eventName,
            string region,
            ulong sessionEpoch,
            ulong connectionGeneration,
            uint regionGeneration,
            ResourceObservationPath path,
            bool spiActive,
            ResourceObservationOutcome outcome,
            string detail = null)
        {
            string normalizedRole = NormalizeRole(role);
            string normalizedRegion = string.IsNullOrWhiteSpace(region) ? "-" : region;
            string suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : " " + detail.Trim();

            return $"[ResourceObs] event={eventName ?? "Unknown"} " +
                $"caseId={BuildFingerprint.GetCaseIdForLogging()} role={normalizedRole} domain=Resource " +
                $"region={normalizedRegion} sessionEpoch={sessionEpoch} " +
                $"connectionGeneration={connectionGeneration} regionGeneration={regionGeneration} " +
                $"path={path} spiActive={spiActive.ToString().ToLowerInvariant()} " +
                $"shadowOnly=true outcome={outcome}{suffix}";
        }

        internal static ResourceObservationPath NativePath(bool spiActive) => spiActive ? ResourceObservationPath.Native : ResourceObservationPath.Legacy;

        internal static void Info(
            string role,
            string eventName,
            string region,
            ulong sessionEpoch,
            ulong connectionGeneration,
            uint regionGeneration,
            ResourceObservationPath path,
            bool spiActive,
            ResourceObservationOutcome outcome,
            string detail = null)
        {
            RoleLogger.Info(role, Format(role, eventName, region, sessionEpoch, connectionGeneration,
                regionGeneration, path, spiActive, outcome, detail));
        }

        internal static void Warn(
            string role,
            string eventName,
            string region,
            ulong sessionEpoch,
            ulong connectionGeneration,
            uint regionGeneration,
            ResourceObservationPath path,
            bool spiActive,
            ResourceObservationOutcome outcome,
            string detail = null)
        {
            RoleLogger.Warn(role, Format(role, eventName, region, sessionEpoch, connectionGeneration,
                regionGeneration, path, spiActive, outcome, detail));
        }

        internal static void Error(
            string role,
            string eventName,
            string region,
            ulong sessionEpoch,
            ulong connectionGeneration,
            uint regionGeneration,
            ResourceObservationPath path,
            bool spiActive,
            ResourceObservationOutcome outcome,
            string detail = null)
        {
            RoleLogger.Error(role, Format(role, eventName, region, sessionEpoch, connectionGeneration,
                regionGeneration, path, spiActive, outcome, detail));
        }

        private static string NormalizeRole(string role)
        {
            if (string.IsNullOrWhiteSpace(role)) return "Shared";
            return role.Trim().Trim('[', ']');
        }
    }
}
