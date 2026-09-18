using SteamP2PFriends.Shared;

namespace SteamP2PFriends.MultiObserver.Lifecycle
{
    /// <summary>
    /// 共享编排引擎的统一诊断出口。引擎只产出结构化记录，格式化与配额策略由实现决定；
    /// 领域可以用自己的受控词汇实现它来保持运行日志取证口径，但诊断永远不是第二套 Writer：
    /// 它只能观察，不能影响任何状态。
    /// </summary>
    public interface ILifecycleDiagnostics
    {
        /// <summary>写出一条转换诊断。</summary>
        void Transition(LifecycleDiagnostic diagnostic);

        /// <summary>同一个转换点的重复通知按 key 去重后写出（会话边界、稳态抑制）。</summary>
        void TransitionOnce(string onceKey, LifecycleDiagnostic diagnostic);
    }

    /// <summary>
    /// 默认诊断出口：按规格要求的最小字段集直接落 RoleLogger。领域没有提供自己的出口时
    /// 使用它，保证任何新领域接入时诊断不会静默缺失。
    /// </summary>
    public sealed class DefaultLifecycleDiagnostics : ILifecycleDiagnostics
    {
        public static readonly DefaultLifecycleDiagnostics Instance = new DefaultLifecycleDiagnostics();

        public void Transition(LifecycleDiagnostic diagnostic)
        {
            if (diagnostic == null) return;
            Write(diagnostic);
        }

        public void TransitionOnce(string onceKey, LifecycleDiagnostic diagnostic)
        {
            if (diagnostic == null) return;
            Write(diagnostic);
        }

        private static void Write(LifecycleDiagnostic diagnostic)
        {
            string message = "[LifecycleObs] event=" + diagnostic.EventName
                + " domain=" + diagnostic.Domain
                + " region=" + (diagnostic.HasRegion ? diagnostic.Region.ToString() : "-")
                + " sessionEpoch=" + diagnostic.SessionEpoch
                + " connectionGeneration=" + diagnostic.ConnectionGeneration
                + " regionGeneration=" + diagnostic.RegionGeneration
                + " observer=" + diagnostic.ObserverId
                + " attempt=" + diagnostic.Attempt
                + " outcome=" + diagnostic.Outcome
                + " path=" + diagnostic.Path
                + " spiActive=" + diagnostic.SpiActive.ToString().ToLowerInvariant()
                + (string.IsNullOrWhiteSpace(diagnostic.Detail) ? string.Empty : " " + diagnostic.Detail.Trim());

            try
            {
                switch (diagnostic.Severity)
                {
                    case ELifecycleSeverity.Error:
                        RoleLogger.Error(diagnostic.Role, message);
                        break;
                    case ELifecycleSeverity.Warn:
                        RoleLogger.Warn(diagnostic.Role, message);
                        break;
                    default:
                        RoleLogger.Info(diagnostic.Role, message);
                        break;
                }
            }
            catch
            {
                // 诊断出口不得成为故障源：日志层不可用时生命周期转换照常推进。
            }
        }
    }
}
