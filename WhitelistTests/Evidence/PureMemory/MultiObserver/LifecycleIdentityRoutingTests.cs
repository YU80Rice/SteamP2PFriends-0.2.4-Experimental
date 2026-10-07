using SteamP2PFriends.Adapters.Resource;
using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver.Lifecycle;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// 票 09-R1：共享生命周期诊断的身份路由契约。转换记录的领域身份来自实际执行端口
    /// 产出的 LifecycleDiagnostic.Domain，Resource 格式化出口不得吞掉或改写它。
    /// </summary>
    internal static class LifecycleIdentityRoutingTests
    {
        private static bool Expect(bool condition, string detail)
        {
            if (!condition) throw new InvalidOperationException(detail);
            return true;
        }

        /// <summary>
        /// 真实生产桥（ResourceLifecycleDiagnostics）收到 Collision 转换时，不得把它送进
        /// Resource 的 [ResourceObs] 格式化/一次通知出口——错标即从这一步发生。Resource
        /// 转换必须照常到达该出口（对照臂证明守门只拦外来身份）。
        /// </summary>
        internal static bool Test_LIR01_BridgeRejectsForeignDomainAtFormattingExit()
        {
            ResourceObservability.ResetSession();
            var collision = new LifecycleDiagnostic(
                DomainIds.Collision, LifecycleEvents.RegionAcquire, ELifecycleOutcome.Success)
            {
                Region = new RegionKey(3, 4),
                HasRegion = true,
                SessionEpoch = 41UL,
                Detail = "reason=region-demand-entered authority=Collision demand=1"
            };
            ResourceLifecycleDiagnostics.Instance.TransitionOnce(
                "09R1-collision-once", collision);
            bool collisionReachedResourceExit =
                ResourceObservability.IsNoticeRecorded("09R1-collision-once");

            var resource = new LifecycleDiagnostic(
                DomainIds.Resource, LifecycleEvents.RegionAcquire, ELifecycleOutcome.Success)
            {
                Region = new RegionKey(3, 4),
                HasRegion = true,
                SessionEpoch = 41UL,
                Detail = "reason=region-demand-entered authority=Resource demand=1"
            };
            ResourceLifecycleDiagnostics.Instance.TransitionOnce(
                "09R1-resource-once", resource);
            bool resourceReachedResourceExit =
                ResourceObservability.IsNoticeRecorded("09R1-resource-once");

            return Expect(!collisionReachedResourceExit && resourceReachedResourceExit,
                "collisionReachedResourceExit=" + collisionReachedResourceExit
                + " resourceReachedResourceExit=" + resourceReachedResourceExit);
        }

        /// <summary>
        /// 领域路由出口的接线契约：每条转换按记录自带的领域到达对应出口，未登记领域
        /// （引擎级转换）走 fallback；路由不改写记录、不吞记录。
        /// </summary>
        internal static bool Test_LIR02_RoutingSinkPreservesDomainPerRoute()
        {
            var resourceSink = new RecordingSink();
            var collisionSink = new RecordingSink();
            var fallback = new RecordingSink();
            var router = new DomainRoutingLifecycleDiagnostics(
                new Dictionary<DomainId, ILifecycleDiagnostics>
                {
                    { DomainIds.Resource, resourceSink },
                    { DomainIds.Collision, collisionSink }
                },
                fallback);

            var resource = new LifecycleDiagnostic(
                DomainIds.Resource, LifecycleEvents.RegionAcquire, ELifecycleOutcome.Success);
            var collision = new LifecycleDiagnostic(
                DomainIds.Collision, LifecycleEvents.RegionReentry, ELifecycleOutcome.Success);
            var engineLevel = new LifecycleDiagnostic(
                default(DomainId), LifecycleEvents.SessionBegin, ELifecycleOutcome.Success);
            router.Transition(resource);
            router.Transition(collision);
            router.TransitionOnce("engine-once", engineLevel);

            return Expect(resourceSink.Entries.Count == 1 && resourceSink.Entries[0] == resource
                && collisionSink.Entries.Count == 1 && collisionSink.Entries[0] == collision
                && fallback.Entries.Count == 1 && fallback.Entries[0] == engineLevel
                && fallback.OnceKeys.Count == 1 && fallback.OnceKeys[0] == "engine-once",
                "resource=" + resourceSink.Entries.Count + " collision=" + collisionSink.Entries.Count
                + " fallback=" + fallback.Entries.Count + " fallbackOnce=" + fallback.OnceKeys.Count);
        }

        /// <summary>
        /// [LifecycleObs] 生产行格式契约：Collision Reentry 行在同一行内携带 event=LeaseReentry、
        /// 真实 domain、authority=Collision、outcome=success、hysteresisCancelled=true 与全部
        /// 关联键——单行即可判读，无需关联其它行或反推。各 severity 经格式行保留。
        /// </summary>
        internal static bool Test_LIR03_DefaultLineCarriesReentryContract()
        {
            var reentry = new LifecycleDiagnostic(
                DomainIds.Collision, LifecycleEvents.RegionReentry, ELifecycleOutcome.Success)
            {
                Region = new RegionKey(3, 4),
                HasRegion = true,
                SessionEpoch = 41UL,
                ConnectionGeneration = 2002UL,
                RegionGeneration = 7U,
                ObserverId = 200UL,
                Detail = "authority=Collision hysteresisCancelled=true"
            };
            string line = DefaultLifecycleDiagnostics.FormatLine(reentry);
            string warnLine = DefaultLifecycleDiagnostics.FormatLine(new LifecycleDiagnostic(
                DomainIds.Collision, LifecycleEvents.RegionRelease, ELifecycleOutcome.Rejected)
            {
                Severity = ELifecycleSeverity.Warn,
                Detail = "reason=release-identity-rejected"
            });
            string errorLine = DefaultLifecycleDiagnostics.FormatLine(new LifecycleDiagnostic(
                DomainIds.Collision, LifecycleEvents.ObserverUpdate, ELifecycleOutcome.Failed)
            {
                Severity = ELifecycleSeverity.Error
            });

            return Expect(ContainsAll(line,
                    "[LifecycleObs] event=LeaseReentry",
                    "domain=Collision",
                    "region=(3,4)",
                    "sessionEpoch=41",
                    "connectionGeneration=2002",
                    "regionGeneration=7",
                    "observer=200",
                    "outcome=Success",
                    "authority=Collision",
                    "hysteresisCancelled=true")
                && !line.Contains("domain=Resource") && !line.Contains("[ResourceObs]")
                && warnLine.Contains("outcome=Rejected") && warnLine.Contains("domain=Collision")
                && errorLine.Contains("outcome=Failed") && errorLine.Contains("domain=Collision"),
                "line=" + line + " warn=" + warnLine + " error=" + errorLine);
        }

        private static bool ContainsAll(string line, params string[] expected)
        {
            foreach (string token in expected)
            {
                if (string.IsNullOrEmpty(token) || !line.Contains(token)) return false;
            }
            return true;
        }

        /// <summary>
        /// 票 09-R1 审查修复：接口契约承诺 TransitionOnce 按 key 去重。引擎级 onceKey
        /// （如 lifecycle-session-end-inactive）在旧接线下经 Resource 出口去重；改道默认
        /// 出口后若无去重，非托管状态下每帧幂等分支都会刷一条日志——默认出口必须实现
        /// 同一去重语义。
        /// </summary>
        internal static bool Test_LIR04_DefaultSinkDeduplicatesOnceKeys()
        {
            try
            {
                DefaultLifecycleDiagnostics.ResetProbeState();
                var first = new LifecycleDiagnostic(
                    default(DomainId), LifecycleEvents.SessionEnd, ELifecycleOutcome.Skipped)
                {
                    Role = "Shared",
                    Detail = "reason=session-not-active"
                };
                int before = DefaultLifecycleDiagnostics.WriteCount;
                DefaultLifecycleDiagnostics.Instance.TransitionOnce(
                    "lifecycle-session-end-inactive", first);
                int afterFirst = DefaultLifecycleDiagnostics.WriteCount;
                DefaultLifecycleDiagnostics.Instance.TransitionOnce(
                    "lifecycle-session-end-inactive", first);
                int afterRepeat = DefaultLifecycleDiagnostics.WriteCount;
                bool onceKeyConsumed =
                    DefaultLifecycleDiagnostics.IsOnceKeyRecorded("lifecycle-session-end-inactive");

                var other = new LifecycleDiagnostic(
                    default(DomainId), LifecycleEvents.SessionBegin, ELifecycleOutcome.Skipped)
                {
                    Role = "Shared"
                };
                DefaultLifecycleDiagnostics.Instance.TransitionOnce(
                    "lifecycle-session-already-active", other);
                bool otherKeyIndependent =
                    DefaultLifecycleDiagnostics.IsOnceKeyRecorded("lifecycle-session-already-active");

                return Expect(onceKeyConsumed && otherKeyIndependent
                    && afterFirst == before + 1 && afterRepeat == afterFirst,
                    "onceKeyConsumed=" + onceKeyConsumed + " otherKeyIndependent=" + otherKeyIndependent
                    + " before=" + before + " afterFirst=" + afterFirst + " afterRepeat=" + afterRepeat);
            }
            finally
            {
                DefaultLifecycleDiagnostics.ResetProbeState();
            }
        }

        /// <summary>记录到达出口的转换与 onceKey：只观察，不参与控制流。</summary>
        private sealed class RecordingSink : ILifecycleDiagnostics
        {
            internal readonly List<LifecycleDiagnostic> Entries = new List<LifecycleDiagnostic>();
            internal readonly List<string> OnceKeys = new List<string>();

            public void Transition(LifecycleDiagnostic diagnostic) => Entries.Add(diagnostic);

            public void TransitionOnce(string onceKey, LifecycleDiagnostic diagnostic)
            {
                OnceKeys.Add(onceKey);
                Entries.Add(diagnostic);
            }
        }
    }
}
