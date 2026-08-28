using SteamP2PFriends.Core.Build;
using SteamP2PFriends.Shared;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends.Adapters.Resource
{
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
            string path,
            bool spiActive,
            string point)
        {
            bool firstNotice;
            lock (SyncLock) firstNotice = QuotaNotices.Add(point ?? eventName ?? "unknown");
            if (!firstNotice) return;

            Warn(role, eventName, region, sessionEpoch, connectionGeneration, regionGeneration,
                path, spiActive, "suppressed", "reason=diagnostic-quota-exhausted point=" + point);
        }

        internal static void NoticeOnce(
            string key,
            string role,
            string eventName,
            string region,
            ulong sessionEpoch,
            ulong connectionGeneration,
            uint regionGeneration,
            string path,
            bool spiActive,
            string outcome,
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
            string path,
            bool spiActive,
            string outcome,
            string detail = null)
        {
            string normalizedRole = NormalizeRole(role);
            string normalizedRegion = string.IsNullOrWhiteSpace(region) ? "-" : region;
            string normalizedPath = string.IsNullOrWhiteSpace(path) ? "Unknown" : path;
            string normalizedOutcome = string.IsNullOrWhiteSpace(outcome) ? "unknown" : outcome;
            string suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : " " + detail.Trim();

            return $"[ResourceObs] event={eventName ?? "Unknown"} " +
                $"caseId={BuildFingerprint.GetCaseIdForLogging()} role={normalizedRole} domain=Resource " +
                $"region={normalizedRegion} sessionEpoch={sessionEpoch} " +
                $"connectionGeneration={connectionGeneration} regionGeneration={regionGeneration} " +
                $"path={normalizedPath} spiActive={spiActive.ToString().ToLowerInvariant()} " +
                $"shadowOnly=true outcome={normalizedOutcome}{suffix}";
        }

        internal static string NativePath(bool spiActive) => spiActive ? "Native" : "Legacy";

        internal static void Info(
            string role,
            string eventName,
            string region,
            ulong sessionEpoch,
            ulong connectionGeneration,
            uint regionGeneration,
            string path,
            bool spiActive,
            string outcome,
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
            string path,
            bool spiActive,
            string outcome,
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
            string path,
            bool spiActive,
            string outcome,
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
