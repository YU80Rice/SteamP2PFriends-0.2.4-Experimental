using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver.Lifecycle;
using SteamP2PFriends.Shared;

namespace SteamP2PFriends.Adapters.Resource
{
    /// <summary>
    /// Resource 域的诊断出口：把共享引擎的统一转换记录落成既有 [ResourceObs] 取证格式，
    /// 使迁入共享编排引擎前后的运行日志字段、事件名、路径与结果词表保持一致。
    ///
    /// 它只观察不参与：不持有状态、不影响控制流。领域身份与区域来源由记录本身携带，
    /// 这里不重新计算任何需求或代次。
    /// </summary>
    public sealed class ResourceLifecycleDiagnostics : ILifecycleDiagnostics
    {
        public static readonly ResourceLifecycleDiagnostics Instance = new ResourceLifecycleDiagnostics();

        public void Transition(LifecycleDiagnostic diagnostic)
        {
            if (diagnostic == null) return;
            Write(diagnostic);
        }

        /// <summary>按 key 去重的转换：沿用既有 NoticeOnce 语义（一次通知，警告级）。</summary>
        public void TransitionOnce(string onceKey, LifecycleDiagnostic diagnostic)
        {
            if (diagnostic == null) return;
            ResourceObservability.NoticeOnce(
                onceKey,
                diagnostic.Role,
                diagnostic.EventName,
                FormatRegion(diagnostic),
                diagnostic.SessionEpoch,
                diagnostic.ConnectionGeneration,
                diagnostic.RegionGeneration,
                ToPath(diagnostic.Path),
                diagnostic.SpiActive,
                ToOutcome(diagnostic.Outcome),
                diagnostic.Detail);
        }

        private static void Write(LifecycleDiagnostic diagnostic)
        {
            string role = diagnostic.Role;
            string eventName = diagnostic.EventName;
            string region = FormatRegion(diagnostic);
            ResourceObservationPath path = ToPath(diagnostic.Path);
            ResourceObservationOutcome outcome = ToOutcome(diagnostic.Outcome);

            switch (diagnostic.Severity)
            {
                case ELifecycleSeverity.Error:
                    ResourceObservability.Error(role, eventName, region, diagnostic.SessionEpoch,
                        diagnostic.ConnectionGeneration, diagnostic.RegionGeneration,
                        path, diagnostic.SpiActive, outcome, diagnostic.Detail);
                    break;
                case ELifecycleSeverity.Warn:
                    ResourceObservability.Warn(role, eventName, region, diagnostic.SessionEpoch,
                        diagnostic.ConnectionGeneration, diagnostic.RegionGeneration,
                        path, diagnostic.SpiActive, outcome, diagnostic.Detail);
                    break;
                default:
                    ResourceObservability.Info(role, eventName, region, diagnostic.SessionEpoch,
                        diagnostic.ConnectionGeneration, diagnostic.RegionGeneration,
                        path, diagnostic.SpiActive, outcome, diagnostic.Detail);
                    break;
            }
        }

        private static string FormatRegion(LifecycleDiagnostic diagnostic) =>
            diagnostic.HasRegion ? diagnostic.Region.ToString() : "-";

        private static ResourceObservationPath ToPath(ELifecyclePath path)
        {
            return path == ELifecyclePath.Fallback
                ? ResourceObservationPath.Fallback
                : ResourceObservationPath.SPI;
        }

        private static ResourceObservationOutcome ToOutcome(ELifecycleOutcome outcome)
        {
            switch (outcome)
            {
                case ELifecycleOutcome.Failed: return ResourceObservationOutcome.Failed;
                case ELifecycleOutcome.Skipped: return ResourceObservationOutcome.Skipped;
                case ELifecycleOutcome.Rejected: return ResourceObservationOutcome.Rejected;
                case ELifecycleOutcome.Deferred: return ResourceObservationOutcome.Deferred;
                case ELifecycleOutcome.Attempt: return ResourceObservationOutcome.Attempt;
                case ELifecycleOutcome.Scheduled: return ResourceObservationOutcome.Scheduled;
                default: return ResourceObservationOutcome.Success;
            }
        }
    }
}
