using SteamP2PFriends.Shared;
using System;
using System.Collections.Generic;

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
            if (onceKey != null)
            {
                lock (OnceKeys)
                {
                    if (!OnceKeys.Add(onceKey)) return;
                }
            }
            Write(diagnostic);
        }

        /// <summary>
        /// TransitionOnce 的按 key 去重状态。接口契约是「同一转换点的重复通知按 key 去重后
        /// 写出」：引擎级 onceKey（会话边界的幂等分支）在旧接线下由 Resource 出口去重，
        /// 改道默认出口后由这里承接——否则非托管状态下的幂等分支会每帧刷日志。
        /// 进程粒度去重：这些 key 都是「不该重复发生的异常路径通知」，防刷屏是唯一目的。
        /// </summary>
        private static readonly HashSet<string> OnceKeys =
            new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// 只读审计探针：该 onceKey 是否已被本出口消费过一次通知权。与
        /// ResourceObservability.IsNoticeRecorded 同型的测试观测缝，不改任何行为。
        /// </summary>
        internal static bool IsOnceKeyRecorded(string onceKey)
        {
            lock (OnceKeys) return OnceKeys.Contains(onceKey ?? string.Empty);
        }

        /// <summary>
        /// 只读审计探针：本出口实际写出的行计数。契约测试用它证明「重复 onceKey 不再写出」
        /// 而不是只看去重集合状态位。只增不减，不改任何输出行为。
        /// </summary>
        internal static int WriteCount => _writeCount;

        /// <summary>观测缝的状态复位：清空去重集合与行计数（测试隔离用，生产不复位）。</summary>
        internal static void ResetProbeState()
        {
            lock (OnceKeys) OnceKeys.Clear();
            System.Threading.Interlocked.Exchange(ref _writeCount, 0);
        }

        private static int _writeCount;

        private static void Write(LifecycleDiagnostic diagnostic)
        {
            string message = FormatLine(diagnostic);
            System.Threading.Interlocked.Increment(ref _writeCount);

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

        /// <summary>
        /// 统一 [LifecycleObs] 行格式：领域取记录自带值（实际执行端口的身份），字段集是
        /// 规格要求的最小关联键。独立成纯函数使生产行格式可被契约测试覆盖。
        /// </summary>
        internal static string FormatLine(LifecycleDiagnostic diagnostic)
        {
            return "[LifecycleObs] event=" + diagnostic.EventName
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
        }
    }
}
