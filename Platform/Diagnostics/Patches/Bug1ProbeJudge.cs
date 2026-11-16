using System;
using System.Collections.Generic;
using System.Globalization;

namespace SteamP2PFriends.Platform.Diagnostics.Patches
{
    /// <summary>单帧采样：同一帧的 BEFORE_UPDATE 与 RENDER_READY 合并成一条，缺相位由标志位表达。</summary>
    internal sealed class Bug1FrameSample
    {
        public int Frame;
        public int FixedTickId;
        public long MonoTicks;
        public long MonoFrequency = 1;
        public bool HasBeforeUpdate;
        public bool HasRenderReady;
        public Bug1Vec3 ABeforeUpdate;
        public double AYawBeforeUpdate;
        public Bug1Vec3 APos;
        public double AYaw;
        public Bug1Vec3 VPos;
        public double VYaw;
        public bool CameraIsMain;
        public bool CameraDuplicate;
        public bool ThirdCarrierVerified;
        public bool RendererVerified;
        public bool ModelEndpointResolved;
        /// <summary>§3.2 关联：该帧最近一次消费的原始输入序号/接收号/队列深度；不可得时置 Has*=false。</summary>
        public bool HasLastInputSeq;
        public uint LastInputSeq;
        public bool HasLastRxId;
        public ulong LastRxId;
        public bool HasQueueDepth;
        public int QueueDepth;
    }

    /// <summary>一次 walking simulate 的前后样本；前后共享 SimCallId。</summary>
    internal sealed class Bug1SimSample
    {
        public string SimCallId;
        public int Frame;
        public bool HasBefore;
        public bool HasAfter;
        public Bug1Vec3 ABefore;
        public Bug1Vec3 AAfter;
        /// <summary>§3.2 关联：本次 simulate 消费的包（AFTER_DEQUEUE 记录）；不可得时置 Has*=false。</summary>
        public bool HasInputSeq;
        public uint InputSeq;
        public bool HasRxId;
        public ulong RxId;
        public bool HasQueueDepth;
        public int QueueDepth;
    }

    /// <summary>一个完整观测窗；只承载原始事实，判定在 Bug1ProbeJudge。</summary>
    internal sealed class Bug1StepWindow
    {
        public string StepId = string.Empty;
        public readonly List<Bug1FrameSample> Frames = new List<Bug1FrameSample>();
        public readonly List<Bug1SimSample> Sims = new List<Bug1SimSample>();
        public double NativeSpeedCapUS;
        public bool SpeedCapKnown;
        public bool RightCensored;
        public bool ModelEndpointUnresolved;
        public bool CullingContextUnresolved;
        public bool ProbePerturbationUnresolved;
        public bool ConfigChanged;
        public bool PauseCrossed;
        public bool GroupChanged;
        public bool DistanceOutOfBand;
        public bool FixedConditionsValid = true;
        public readonly List<string> FixedConditionFailures = new List<string>();
        public int StepFirstFrame = -1;
        public int StepLastFrame = -1;
        /// <summary>
        /// t45：本 run 实际落下的 AFTER_ENQUEUE / AFTER_DEQUEUE 记录数（生产路径由 Runtime 逐条累加）。
        /// null 表示该窗口不是生产采集产物（纯合成用例），不参与相位覆盖硬判；0 表示采集期间一条都没落下，
        /// 即仪表故障（Mono 可能根本没 detour 到 BCL 泛型 Queue），必须硬判 UNKNOWN，不得落成「无异常」。
        /// </summary>
        public int? RxEnqueueRecords;
        public int? RxDequeueRecords;
        /// <summary>t60：窗口收口统计（由 Runtime 在 Judge 之前用 Bug1ProbeWindowBoundary 填）。</summary>
        public int WindowHeadExcluded;
        public int WindowTailExcluded;
        public int WindowInteriorPartial;
        public string WindowExcludedReason = string.Empty;
        /// <summary>t65：尾部归因（见 Bug1WindowBounds.Boundary*）。</summary>
        public string WindowTailBoundaryKind = Bug1WindowBounds.BoundaryNone;
        public string WindowHeadBoundaryKind = Bug1WindowBounds.HeadBoundaryNone;
        public bool WindowTailUnexplained;
        public int WindowTailRenderOnly;
        public int WindowTailFirstFrame = -1;
        public int WindowTailLastFrame = -1;
        /// <summary>t65：收口时刻采集面五要素，随 result.json 一起导出，供复盘判读。</summary>
        public string CaptureState = string.Empty;
    }

    /// <summary>
    /// T79/CLOSED_EVENT_PRIORITY-v1：一件「证据完整、已闭合」的事件。
    /// 证明域就是它自己的 [StallStartFrame, ReleaseFrame]，关联检查也按该域逐件做；
    /// 只有这里的事件才计入 strict_m1_events / auth_segments，并可供代表事件选择。
    /// </summary>
    internal sealed class Bug1ClosedEvent
    {
        /// <summary>"M1" 或 "AUTH"。</summary>
        public string Kind = string.Empty;
        public int StallStartFrame;
        public int StallEndFrame;
        public int ReleaseFrame;
        /// <summary>该事件证明域内的关联（input_seq/rx_id/queue_depth、rx→seq 唯一）是否齐全。</summary>
        public bool EvidenceComplete;
    }

    /// <summary>
    /// T79：局部右截断段——停段一直延伸到窗口末尾，没有未来释放帧。
    /// 它只否定「这一段」的判读，不否定同一步里更早的闭合事件；与正式候选三元组分开导出。
    /// </summary>
    internal sealed class Bug1RightCensoredRange
    {
        public int StallStartFrame;
        public int StallEndFrame;
        public int ReleaseFrame = -1;
        public string Reason = "RIGHT_CENSORED";
    }

    /// <summary>§5 判定输出：标签 + 证据缺口 + 事件帧号。</summary>
    internal sealed class Bug1Verdict
    {
        public Bug1Label Label;
        public bool Valid;
        public string StepId = string.Empty;
        public int NExpected;
        public int NObserved;
        public readonly List<string> MissingFields = new List<string>();
        public readonly List<int> MissingFrames = new List<int>();
        public readonly List<string> UnpairedSimIds = new List<string>();
        public readonly List<int> EventFrames = new List<int>();
        /// <summary>
        /// T71/M1-XZ-RELATIVE-v2 §3.1：M1 只由「同一 RENDER_READY 相位内的相对落后 + 释放时向权威回补」决定。
        /// 这里不再导出无轴含义的 root_span；水平渲染跨度与三维 SIM 跨度分开记，后者只作旁证。
        /// </summary>
        public double RenderRootSpanXZ;
        public double SimRootSpan3D;
        public string M1RuleRevision = Bug1ProbeConstants.M1RuleRevision;
        public string MotionAxes = Bug1ProbeConstants.MotionAxes;
        public double HorizontalMotionMinU = Bug1ProbeConstants.HorizontalMotionMinU;
        /// <summary>§3.3 落后见证：停段内首个「权威已推进 ≥D 且载体在其后方」的帧，及该行 A/V。</summary>
        public int LagWitnessFrame = -1;
        public Bug1Vec3 LagWitnessAuthority;
        public Bug1Vec3 LagWitnessCarrier;
        /// <summary>§3.3 回补收敛：释放前/后的相对差距（水平）。</summary>
        public double LagBeforeReleaseU;
        public double LagAfterReleaseU;
        /// <summary>§3.4 分段统计与逐段拒绝原因；全部段都无事件才 NO_THRESHOLD_EVENT。</summary>
        public int HorizontalStallSegments;
        public int HorizontalCandidates;
        public int AuthSegments;
        public int NonM1Segments;
        public readonly List<string> CandidateRejections = new List<string>();
        public double EpsilonU = Bug1ProbeConstants.EpsilonU;
        public double NativeSpeedCapUS;
        public int CandidateA = -1;
        public int CandidateB = -1;
        public int ReleaseFrame = -1;
        public int StrictM1Events;
        public string Reason = string.Empty;
        /// <summary>t60：缺相位原因拆分——窗口内真缺（MISSING_PHASE 的唯一合法来源）与窗口越界（恒 0，越界帧已在判定前剔除）。</summary>
        public int PhaseMissingGenuine;
        public int PhaseMissingOutOfWindow;
        public int WindowHeadExcluded;
        public int WindowTailExcluded;
        public int WindowInteriorPartial;
        public string WindowExcludedReason = string.Empty;
        public string WindowTailBoundaryKind = Bug1WindowBounds.BoundaryNone;
        public string WindowHeadBoundaryKind = Bug1WindowBounds.HeadBoundaryNone;
        public bool WindowTailUnexplained;
        public int WindowTailRenderOnly;
        public int WindowTailFirstFrame = -1;
        public int WindowTailLastFrame = -1;
        public string CaptureState = string.Empty;
        /// <summary>
        /// T79/CLOSED_EVENT_PRIORITY-v1：事件聚合口径。label/valid/strict_m1_events/候选三元组
        /// 都只描述「代表闭合事件」；右截断只令尾段局部失效，不再推翻整步已闭合事件。
        /// </summary>
        public string EventAggregationRevision = Bug1ProbeConstants.EventAggregationRevision;
        /// <summary>代表事件（M1 优先，其次 AUTH）的证明域是否完整：停段/释放/同点 A/V/落后收敛/关联齐全。</summary>
        public bool SelectedEventComplete;
        /// <summary>所需数据完整且不存在「尚未判定的右截断停段」；有右截断一律 false。</summary>
        public bool EventScanComplete;
        /// <summary>存在右截断时，strict_m1_events 只是下界（还可能存在未判定的 M1）。</summary>
        public bool StrictM1EventsIsLowerBound;
        /// <summary>右截断停段计数与逐段范围；与正式候选三元组分开，不得混用。</summary>
        public int RightCensoredSegments;
        public readonly List<Bug1RightCensoredRange> RightCensoredRanges = new List<Bug1RightCensoredRange>();
        /// <summary>全部已闭合事件（M1 与 AUTH），供核对；Count(Kind=="M1") 必须等于 StrictM1Events。</summary>
        public readonly List<Bug1ClosedEvent> ClosedEvents = new List<Bug1ClosedEvent>();

        public bool IsUnknown => Label == Bug1Label.Unknown;
    }

    /// <summary>
    /// §5 离线判定器：先完整性、再停帧候选、最后互斥因果标签。
    /// 纯函数、无 Unity 依赖、无 I/O；相同 trace 重复判读必须得到相同结果。
    /// </summary>
    internal static class Bug1ProbeJudge
    {
        public static Bug1Verdict Judge(Bug1StepWindow window)
        {
            var verdict = new Bug1Verdict { StepId = window?.StepId ?? string.Empty };
            if (window == null)
            {
                MarkUnknown(verdict, "window", "NULL_WINDOW");
                return verdict;
            }
            verdict.NObserved = window.Frames.Count;
            verdict.NativeSpeedCapUS = window.NativeSpeedCapUS;
            // t60/t65：窗口边界统计随判定一起带出。OutOfWindow 是「被合法边界剔除的帧数」，
            // 有真实生产者（head+tail），不是常量占位；capture failure 时不裁剪，故该项为 0 而由 genuine 接管。
            verdict.WindowHeadExcluded = window.WindowHeadExcluded;
            verdict.WindowTailExcluded = window.WindowTailExcluded;
            verdict.WindowInteriorPartial = window.WindowInteriorPartial;
            verdict.WindowExcludedReason = window.WindowExcludedReason ?? string.Empty;
            verdict.WindowTailBoundaryKind = window.WindowTailBoundaryKind ?? Bug1WindowBounds.BoundaryNone;
            verdict.WindowHeadBoundaryKind = window.WindowHeadBoundaryKind ?? Bug1WindowBounds.HeadBoundaryNone;
            verdict.WindowTailUnexplained = window.WindowTailUnexplained;
            verdict.WindowTailRenderOnly = window.WindowTailRenderOnly;
            verdict.WindowTailFirstFrame = window.WindowTailFirstFrame;
            verdict.WindowTailLastFrame = window.WindowTailLastFrame;
            verdict.CaptureState = window.CaptureState ?? string.Empty;
            verdict.PhaseMissingOutOfWindow = window.WindowHeadExcluded + window.WindowTailExcluded;

            // t65 §F1：尾部必须先归因，再谈相位覆盖。
            // render-only 尾部（有 RENDER_READY 无 BEFORE_UPDATE）＝采集中途停摆（暂停/剔除/相机仍在跑），
            // 与「越界半帧」同形但性质相反，绝不能被边界裁剪吞掉。
            if (window.WindowTailRenderOnly > 0
                && !Bug1ProbeWindowBoundary.IsLegalBoundary(window.WindowTailBoundaryKind))
            {
                MarkUnknown(verdict, "window_tail_render_only", "PHASE_STOPPED_MID_WINDOW");
                MarkUnknown(verdict, "window_tail", "PHASE_STOPPED_MID_WINDOW");
                verdict.Label = Bug1Label.Unknown;
                return verdict;
            }
            // 尾部有帧却没有任何合法边界解释（session_reset / config_changed / 任意其它）→ 不裁、判 UNKNOWN。
            if (window.WindowTailUnexplained)
            {
                MarkUnknown(verdict, "capture_tail@" + window.WindowTailFirstFrame, "CAPTURE_FAILURE_TAIL");
                MarkUnknown(verdict, "window_tail", "TAIL_PARTIAL_UNEXPLAINED");
                verdict.Label = Bug1Label.Unknown;
                return verdict;
            }

            // t45 §R6-05：先判相位覆盖。缺 AFTER_ENQUEUE / AFTER_DEQUEUE 说明仪表根本没接到原生队列边界，
            // 此时帧数据再平滑也不能解释为「无异常」，必须 UNKNOWN(缺相位) 并写清缺哪一相。
            if (!ValidatePhaseCoverage(window, verdict))
            {
                verdict.Label = Bug1Label.Unknown;
                return verdict;
            }

            if (!ValidateContext(window, verdict))
            {
                verdict.Label = Bug1Label.Unknown;
                return verdict;
            }

            if (!ValidateFrames(window, verdict))
            {
                verdict.Label = Bug1Label.Unknown;
                return verdict;
            }

            verdict.Valid = true;
            // T71/M1-XZ-RELATIVE-v2：先水平投影筛 C_H，再逐段判「落后见证 + 回补收敛」。
            // 三维 SIM 跨度只作旁证导出，不再参与任何布尔判定。
            ClassifyHorizontalMotion(window, verdict);
            if (!verdict.Valid)
            {
                verdict.Label = Bug1Label.Unknown;
                return verdict;
            }

            // T79/CLOSED_EVENT_PRIORITY-v1：关联必须逐件在各自证明域 [a, release] 内验证，
            // 不能只验最长代表段；任一闭合事件关联失败仍是 UNKNOWN（只豁免「尚无未来 release」一条）。
            if (!ValidateClosedEventAssociations(window, verdict))
            {
                verdict.Label = Bug1Label.Unknown;
                verdict.Valid = false;
                verdict.SelectedEventComplete = false;
                return verdict;
            }

            // 外部 RightCensored 标志必须能定位到真实右截断段；定位不到或含义不明 → 维持 UNKNOWN。
            if (window.RightCensored && verdict.RightCensoredSegments == 0)
            {
                MarkUnknown(verdict, "step_end_boundary", "RIGHT_CENSORED");
                // 外部标志定位不到真实尾段属完整性失败，原因必须明确，不能被「无候选」之类的几何原因占位。
                verdict.Reason = "RIGHT_CENSORED";
                verdict.Label = Bug1Label.Unknown;
                verdict.Valid = false;
                // R14-02：外部标志定位不到尾段时扫描其实已穷尽（ranges 为空），
                // 导出必须说「完整扫描 + 无下界语义」，不能停在默认 false 谎称未穷尽。
                verdict.EventScanComplete = true;
                verdict.StrictM1EventsIsLowerBound = false;
                return verdict;
            }

            verdict.EventScanComplete = verdict.RightCensoredSegments == 0;
            verdict.StrictM1EventsIsLowerBound = verdict.RightCensoredSegments > 0;

            int closedM1 = 0;
            foreach (Bug1ClosedEvent closed in verdict.ClosedEvents)
            {
                if (string.Equals(closed.Kind, "M1", StringComparison.Ordinal)) closedM1++;
            }
            // 不变量：M1 计数与 closed_events 里 M1 件数同源，不允许各写各的。
            verdict.StrictM1Events = closedM1;

            // T79 §唯一汇总顺序：闭合 M1 > 闭合 AUTH > 仅右截断 Unknown > 完整且无事件 NoThresholdEvent。
            if (closedM1 > 0)
            {
                verdict.Label = Bug1Label.M1StallCatchup;
                verdict.Reason = "M1_STALL_CATCHUP";
            }
            else if (verdict.ClosedEvents.Count > 0)
            {
                verdict.Label = Bug1Label.AuthStepLocked;
                verdict.Reason = "AUTH_STEP_LOCKED";
            }
            else if (verdict.RightCensoredSegments > 0)
            {
                // 纯尾段、无任何闭合事件：不得因缺尾部授全步 PASS，也不得拿尾段末帧充释放点。
                verdict.Label = Bug1Label.Unknown;
                verdict.Reason = "RIGHT_CENSORED";
                verdict.Valid = false;
                verdict.CandidateA = -1;
                verdict.CandidateB = -1;
                verdict.ReleaseFrame = -1;
                verdict.LagWitnessFrame = -1;
                verdict.SelectedEventComplete = false;
                return verdict;
            }
            else
            {
                verdict.Label = Bug1Label.NoThresholdEvent;
                verdict.Reason = "NO_THRESHOLD_EVENT";
            }

            if (verdict.CandidateA >= 0)
            {
                verdict.SelectedEventComplete = IsRepresentativeEventComplete(verdict);
                verdict.EventFrames.Add(verdict.CandidateA);
                verdict.EventFrames.Add(verdict.CandidateB);
                verdict.EventFrames.Add(verdict.ReleaseFrame);
            }
            return verdict;
        }

        /// <summary>T79：代表事件（候选三元组所指的那一件）自身证明域是否完整。</summary>
        private static bool IsRepresentativeEventComplete(Bug1Verdict verdict)
        {
            foreach (Bug1ClosedEvent closed in verdict.ClosedEvents)
            {
                if (closed.StallStartFrame == verdict.CandidateA
                    && closed.StallEndFrame == verdict.CandidateB
                    && closed.ReleaseFrame == verdict.ReleaseFrame)
                {
                    return closed.EvidenceComplete;
                }
            }
            return false;
        }

        /// <summary>T79：逐件在各自证明域内做关联先验；任一失败即整步 UNKNOWN（fail-closed）。</summary>
        private static bool ValidateClosedEventAssociations(Bug1StepWindow w, Bug1Verdict v)
        {
            bool ok = true;
            foreach (Bug1ClosedEvent closed in v.ClosedEvents)
            {
                bool complete = ValidateCandidateAssociations(w, v, closed.StallStartFrame, closed.ReleaseFrame);
                closed.EvidenceComplete = complete;
                if (!complete) ok = false;
            }
            return ok;
        }

        /// <summary>
        /// t45：相位覆盖硬判。生产窗口两计数恒为非 null（Enable 时置 0，每次成功落记录 ++），
        /// 因此「采集了一条都没落下」＝仪表故障，直接 UNKNOWN 并列出缺失相位名；绝不允许继续走成
        /// NO_THRESHOLD_EVENT（那会被读成「没有异常」）。合成窗口保持 null，语义不变。
        /// </summary>
        private static bool ValidatePhaseCoverage(Bug1StepWindow w, Bug1Verdict v)
        {
            bool ok = true;
            if (w.RxEnqueueRecords.HasValue && w.RxEnqueueRecords.Value <= 0)
            {
                MarkUnknown(v, "AFTER_ENQUEUE", "PHASE_COVERAGE_MISSING");
                ok = false;
            }
            if (w.RxDequeueRecords.HasValue && w.RxDequeueRecords.Value <= 0)
            {
                MarkUnknown(v, "AFTER_DEQUEUE", "PHASE_COVERAGE_MISSING");
                ok = false;
            }
            return ok;
        }

        private static void MarkUnknown(Bug1Verdict verdict, string field, string reason)
        {
            verdict.Label = Bug1Label.Unknown;
            if (!verdict.MissingFields.Contains(field)) verdict.MissingFields.Add(field);
            if (string.IsNullOrEmpty(verdict.Reason)) verdict.Reason = reason;
        }

        /// <summary>固定条件与冻结阈值：任一失效即整段 UNKNOWN，不允许事后放宽。</summary>
        private static bool ValidateContext(Bug1StepWindow w, Bug1Verdict v)
        {
            bool ok = true;
            if (w.ConfigChanged) { MarkUnknown(v, "config", "CONFIG_CHANGED"); ok = false; }
            if (w.PauseCrossed) { MarkUnknown(v, "pause", "PAUSE_CROSSED"); ok = false; }
            if (w.GroupChanged) { MarkUnknown(v, "group", "GROUP_CHANGED"); ok = false; }
            if (w.DistanceOutOfBand) { MarkUnknown(v, "distance_horizontal_u", "DISTANCE_OUT_OF_BAND"); ok = false; }
            if (!w.FixedConditionsValid)
            {
                if (w.FixedConditionFailures.Count == 0) { MarkUnknown(v, "fixed_conditions", "FIXED_CONDITION_INVALID"); ok = false; }
                foreach (string failure in w.FixedConditionFailures)
                {
                    MarkUnknown(v, "fixed_condition:" + failure, "FIXED_CONDITION_INVALID");
                    ok = false;
                }
            }
            if (w.CullingContextUnresolved) { MarkUnknown(v, "culling_context", "CULLING_CONTEXT_UNRESOLVED"); ok = false; }
            if (w.ProbePerturbationUnresolved) { MarkUnknown(v, "probe_perturbation", "PROBE_PERTURBATION_UNRESOLVED"); ok = false; }
            if (!w.SpeedCapKnown || double.IsNaN(w.NativeSpeedCapUS) || double.IsInfinity(w.NativeSpeedCapUS) || w.NativeSpeedCapUS <= 0d)
            {
                MarkUnknown(v, "native_speed_cap_u_s", "SPEED_CAP_UNRESOLVED");
                ok = false;
            }
            if (w.ModelEndpointUnresolved) { MarkUnknown(v, "model_endpoint", "MODEL_ENDPOINT_UNRESOLVED"); ok = false; }
            // T79/CLOSED_EVENT_PRIORITY-v1：外部 RightCensored 标志不再在这里一票否决整步——
            // 「尚无未来 release」只局部作用于对应尾段。这里不做处理，交由 Judge 在分类后确认
            // 该标志能否定位到真实右截断段：定位不到（或含义不明）仍判 UNKNOWN(RIGHT_CENSORED)。
            return ok;
        }

        /// <summary>§4.2/§5.1：N_expected 逐帧闭合、相位齐全、时钟单调、SIM 成对。</summary>
        private static bool ValidateFrames(Bug1StepWindow w, Bug1Verdict v)
        {
            if (w.Frames.Count == 0)
            {
                MarkUnknown(v, "frames", "NO_FRAMES");
                return false;
            }

            int first = w.StepFirstFrame >= 0 ? w.StepFirstFrame : w.Frames[0].Frame;
            int last = w.StepLastFrame >= 0 ? w.StepLastFrame : w.Frames[w.Frames.Count - 1].Frame;
            if (last < first)
            {
                MarkUnknown(v, "step_frame_range", "STEP_FRAME_RANGE_INVALID");
                return false;
            }
            v.NExpected = last - first + 1;

            var byFrame = new Dictionary<int, Bug1FrameSample>();
            long previousTicks = long.MinValue;
            for (int i = 0; i < w.Frames.Count; i++)
            {
                Bug1FrameSample f = w.Frames[i];
                if (f == null)
                {
                    MarkUnknown(v, "frame_null@" + i, "NULL_FRAME");
                    continue;
                }
                if (byFrame.ContainsKey(f.Frame))
                {
                    MarkUnknown(v, "frame_duplicate@" + f.Frame, "DUPLICATE_FRAME");
                    continue;
                }
                byFrame.Add(f.Frame, f);
                if (!f.HasBeforeUpdate) { v.PhaseMissingGenuine++; MarkUnknown(v, "phase_BEFORE_UPDATE@" + f.Frame, "MISSING_PHASE"); }
                if (!f.HasRenderReady) { v.PhaseMissingGenuine++; MarkUnknown(v, "phase_RENDER_READY@" + f.Frame, "MISSING_PHASE"); }
                if (f.CameraDuplicate) MarkUnknown(v, "camera_duplicate@" + f.Frame, "MULTI_CAMERA_SAMPLE");
                if (!f.CameraIsMain) MarkUnknown(v, "camera_main@" + f.Frame, "CAMERA_NOT_MAIN");
                if (!f.ThirdCarrierVerified) MarkUnknown(v, "third_carrier@" + f.Frame, "MODEL_ENDPOINT_UNRESOLVED");
                if (!f.RendererVerified) MarkUnknown(v, "renderer_chain@" + f.Frame, "MODEL_ENDPOINT_UNRESOLVED");
                if (!f.ModelEndpointResolved) MarkUnknown(v, "model_endpoint@" + f.Frame, "MODEL_ENDPOINT_UNRESOLVED");
                if (!f.APos.IsFinite || !f.VPos.IsFinite) MarkUnknown(v, "non_finite_position@" + f.Frame, "NON_FINITE");
                if (double.IsNaN(f.AYaw) || double.IsInfinity(f.AYaw) || double.IsNaN(f.VYaw) || double.IsInfinity(f.VYaw))
                    MarkUnknown(v, "non_finite_yaw@" + f.Frame, "NON_FINITE");
                if (f.MonoFrequency <= 0) MarkUnknown(v, "mono_frequency@" + f.Frame, "CLOCK_INVALID");
                else if (previousTicks != long.MinValue && f.MonoTicks <= previousTicks)
                    MarkUnknown(v, "mono_clock@" + f.Frame, "CLOCK_NOT_MONOTONIC");
                previousTicks = f.MonoTicks;
            }

            for (int frame = first; frame <= last; frame++)
            {
                if (!byFrame.ContainsKey(frame))
                {
                    v.MissingFrames.Add(frame);
                    MarkUnknown(v, "missing_frame@" + frame, "MISSING_FRAME");
                }
            }
            if (v.NObserved != v.NExpected)
                MarkUnknown(v, "n_expected", "N_EXPECTED_MISMATCH");

            var paired = new HashSet<string>(StringComparer.Ordinal);
            foreach (Bug1SimSample sim in w.Sims)
            {
                if (sim == null) continue;
                string id = sim.SimCallId ?? string.Empty;
                if (string.IsNullOrEmpty(id) || !sim.HasBefore || !sim.HasAfter)
                {
                    v.UnpairedSimIds.Add(id);
                    MarkUnknown(v, "sim_pair:" + id, "SIM_UNPAIRED");
                }
                else if (!paired.Add(id))
                {
                    MarkUnknown(v, "sim_duplicate:" + id, "SIM_DUPLICATE");
                }
            }
            return v.MissingFields.Count == 0;
        }

        /// <summary>
        /// T71/M1-XZ-RELATIVE-v2 §3.2–3.4：水平投影后遍历**全部**最大连续小位移段并逐段验证。
        /// 段内先要求释放越过原速度帧界且 ≥2cm（C_H），再分型：
        /// M1 = 停段内出现落后见证 且 释放时向权威回补收敛；AUTH = 停段根未推进且释放同拍步进；
        /// 其余记 NON_M1 原因并继续搜下一段。代表事件展示最长合格段，事件计数覆盖所有段（R08）。
        /// </summary>
        private static void ClassifyHorizontalMotion(Bug1StepWindow w, Bug1Verdict v)
        {
            var frames = new List<Bug1FrameSample>(w.Frames);
            frames.Sort((x, y) => x.Frame.CompareTo(y.Frame));
            double eps = Bug1ProbeConstants.EpsilonU;

            int bestM1Start = -1, bestM1End = -1, bestM1Release = -1, bestM1Length = 0;
            int bestM1AFrame = -1, bestM1RFrame = -1;
            int bestAuthStart = -1, bestAuthEnd = -1, bestAuthRelease = -1, bestAuthLength = 0;
            int bestAuthAFrame = -1, bestAuthRFrame = -1;

            int step = 1;
            while (step < frames.Count)
            {
                if (!IsHorizontalSmallStep(frames[step - 1], frames[step], eps)) { step++; continue; }
                int s = step;
                int e = step;
                while (e + 1 < frames.Count && IsHorizontalSmallStep(frames[e], frames[e + 1], eps)) e++;
                step = e + 1;

                // C_H 要求至少 3 个连续渲染帧间隔（4 点）；不足的微动段不构成候选。
                if (e - s + 1 < Bug1ProbeConstants.MinStallDisplacements) continue;
                v.HorizontalStallSegments++;

                if (e + 1 >= frames.Count)
                {
                    // T79/CLOSED_EVENT_PRIORITY-v1：正常窗口末尾停段没有未来释放帧——只把这一段记成
                    // 局部 RIGHT_CENSORED，不推翻同一步内更早证据完整、已闭合的事件（M1/AUTH）。
                    // 事件计数因此是下界；纯尾段无闭合事件时由 Judge 统一判 UNKNOWN(RIGHT_CENSORED)。
                    v.RightCensoredSegments++;
                    v.RightCensoredRanges.Add(new Bug1RightCensoredRange
                    {
                        StallStartFrame = frames[s - 1].Frame,
                        StallEndFrame = frames[e].Frame,
                    });
                    break;
                }

                Bug1FrameSample release = frames[e + 1];
                double dt = release.MonoFrequency > 0
                    ? (double)(release.MonoTicks - frames[e].MonoTicks) / release.MonoFrequency
                    : 0d;
                if (!(dt > 0d))
                {
                    MarkUnknown(v, "release_dt@" + release.Frame, "CLOCK_INVALID");
                    v.Valid = false;
                    return;
                }

                Bug1Vec3 vBefore = frames[e].VPos.Quantized().Horizontal();
                Bug1Vec3 vRelease = release.VPos.Quantized().Horizontal();
                // §3.2：B_r = cap × dt_r + ε，走公开的 SpeedBoundary 单点定义（生产与用例同一算式）。
                double boundary = SpeedBoundary(w.NativeSpeedCapUS, dt);
                double releaseHorizontal = Bug1Vec3.HorizontalDistance(vRelease, vBefore);
                if (!(releaseHorizontal > boundary))
                {
                    v.CandidateRejections.Add("RELEASE_NOT_EXCEEDED@" + release.Frame);
                    continue;
                }
                // §3.2：2cm 绝对门是 ≥（含边界），按固定 q 网格整数格比较，避免浮点落到 19.999 格。
                if (!Bug1Vec3.HorizontalAtLeastGrids(vRelease, vBefore, Bug1ProbeConstants.HorizontalMotionMinGrids))
                {
                    v.CandidateRejections.Add("RELEASE_BELOW_2CM@" + release.Frame);
                    continue;
                }
                v.HorizontalCandidates++;

                int witness = FindLagWitness(frames, s - 1, e, eps);
                Bug1Vec3 aBefore = frames[e].APos.Quantized().Horizontal();
                Bug1Vec3 aRelease = release.APos.Quantized().Horizontal();
                double lagBefore = Bug1Vec3.HorizontalDistance(aBefore, vBefore);
                double lagAfter = Bug1Vec3.HorizontalDistance(aRelease, vRelease);
                Bug1Vec3 gapBefore = Bug1Vec3.Subtract(aBefore, vBefore);
                Bug1Vec3 carrierDelta = Bug1Vec3.Subtract(vRelease, vBefore);
                bool converged = (lagBefore - lagAfter) > eps
                    && Bug1Vec3.HorizontalDot(carrierDelta, gapBefore) > 0d;

                if (witness >= 0 && converged)
                {
                    v.ClosedEvents.Add(new Bug1ClosedEvent
                    {
                        Kind = "M1",
                        StallStartFrame = frames[s - 1].Frame,
                        StallEndFrame = frames[e].Frame,
                        ReleaseFrame = release.Frame,
                    });
                    int length = e - s + 1;
                    if (IsBetterRepresentative(length, frames[s - 1].Frame, release.Frame,
                        bestM1Length, bestM1AFrame, bestM1RFrame))
                    {
                        bestM1Length = length;
                        bestM1AFrame = frames[s - 1].Frame;
                        bestM1RFrame = release.Frame;
                        bestM1Start = s - 1;
                        bestM1End = e;
                        bestM1Release = e + 1;
                    }
                    continue;
                }

                if (IsAuthStepLocked(frames, s - 1, e, release, vRelease, vBefore, eps))
                {
                    v.AuthSegments++;
                    v.ClosedEvents.Add(new Bug1ClosedEvent
                    {
                        Kind = "AUTH",
                        StallStartFrame = frames[s - 1].Frame,
                        StallEndFrame = frames[e].Frame,
                        ReleaseFrame = release.Frame,
                    });
                    int length = e - s + 1;
                    if (IsBetterRepresentative(length, frames[s - 1].Frame, release.Frame,
                        bestAuthLength, bestAuthAFrame, bestAuthRFrame))
                    {
                        bestAuthLength = length;
                        bestAuthAFrame = frames[s - 1].Frame;
                        bestAuthRFrame = release.Frame;
                        bestAuthStart = s - 1;
                        bestAuthEnd = e;
                        bestAuthRelease = e + 1;
                    }
                    continue;
                }

                v.NonM1Segments++;
                v.CandidateRejections.Add(
                    (witness < 0 ? "LAG_WITNESS_MISSING@" : "ERROR_NOT_CONVERGED@") + release.Frame);
            }

            int a = bestM1Start >= 0 ? bestM1Start : bestAuthStart;
            if (a < 0)
            {
                v.Reason = v.HorizontalStallSegments == 0 ? "NO_CANDIDATE_RUN" : "NON_M1_HORIZONTAL_PATTERN";
                return;
            }
            int b = bestM1Start >= 0 ? bestM1End : bestAuthEnd;
            int r = bestM1Start >= 0 ? bestM1Release : bestAuthRelease;

            v.CandidateA = frames[a].Frame;
            v.CandidateB = frames[b].Frame;
            v.ReleaseFrame = frames[r].Frame;
            v.RenderRootSpanXZ = ComputeRenderRootSpanXZ(frames, a, b);
            v.SimRootSpan3D = ComputeSimRootSpan3D(w, v.CandidateA, v.CandidateB);
            int lag = FindLagWitness(frames, a, b, eps);
            if (lag >= 0)
            {
                v.LagWitnessFrame = frames[lag].Frame;
                v.LagWitnessAuthority = frames[lag].APos.Quantized();
                v.LagWitnessCarrier = frames[lag].VPos.Quantized();
            }
            v.LagBeforeReleaseU = Bug1Vec3.HorizontalDistance(
                frames[b].APos.Quantized().Horizontal(), frames[b].VPos.Quantized().Horizontal());
            v.LagAfterReleaseU = Bug1Vec3.HorizontalDistance(
                frames[r].APos.Quantized().Horizontal(), frames[r].VPos.Quantized().Horizontal());
        }

        /// <summary>T79 代表选择：同类事件取停段最长；等长取最早 a，再取最早 release。</summary>
        private static bool IsBetterRepresentative(int length, int aFrame, int releaseFrame,
            int bestLength, int bestAFrame, int bestRFrame)
        {
            if (bestLength <= 0) return true;
            if (length != bestLength) return length > bestLength;
            if (aFrame != bestAFrame) return aFrame < bestAFrame;
            return releaseFrame < bestRFrame;
        }

        private static bool IsHorizontalSmallStep(Bug1FrameSample previous, Bug1FrameSample current, double eps)
        {
            if (previous == null || current == null) return false;
            return Bug1Vec3.HorizontalDistance(
                current.VPos.Quantized().Horizontal(), previous.VPos.Quantized().Horizontal()) <= eps;
        }

        /// <summary>§3.3：首个「权威已推进 ≥D、载体在其后方且相对差距可测」的停段内帧；无则 -1。</summary>
        private static int FindLagWitness(List<Bug1FrameSample> frames, int a, int b, double eps)
        {
            Bug1Vec3 authorityStart = frames[a].APos.Quantized().Horizontal();
            for (int i = a + 1; i <= b; i++)
            {
                Bug1Vec3 authority = frames[i].APos.Quantized().Horizontal();
                if (!Bug1Vec3.HorizontalAtLeastGrids(authority, authorityStart, Bug1ProbeConstants.HorizontalMotionMinGrids))
                    continue;
                Bug1Vec3 progress = Bug1Vec3.Subtract(authority, authorityStart);
                double progressLength = Bug1Vec3.HorizontalDistance(authority, authorityStart);
                Bug1Vec3 gap = Bug1Vec3.Subtract(authority, frames[i].VPos.Quantized().Horizontal());
                double gapLength = Bug1Vec3.HorizontalDistance(
                    authority, frames[i].VPos.Quantized().Horizontal());
                if (!(gapLength > eps)) continue;
                if (!((Bug1Vec3.HorizontalDot(gap, progress) / progressLength) > eps)) continue;
                return i;
            }
            return -1;
        }

        /// <summary>§3.4：停段根未推进（段内**最大成对**水平距离 ≤ ε），且释放时根/载体增量一致 → AUTH_STEP_LOCKED。</summary>
        private static bool IsAuthStepLocked(List<Bug1FrameSample> frames, int a, int b,
            Bug1FrameSample release, Bug1Vec3 vRelease, Bug1Vec3 vBefore, double eps)
        {
            // §3.4 原文是 max‖A_i − A_j‖ ≤ ε（段内最大成对距离）。逐轴 max−min 合成只是它的下界，
            // 会把实际超过 ε 的段误判成 AUTH，方向是放宽而非收紧，故此处老实做 O(n²) 精确比较。
            double authoritySpan = 0d;
            for (int i = a; i <= b; i++)
            {
                Bug1Vec3 pi = frames[i].APos.Quantized().Horizontal();
                for (int j = i + 1; j <= b; j++)
                {
                    double d = Bug1Vec3.HorizontalDistance(pi, frames[j].APos.Quantized().Horizontal());
                    if (d > authoritySpan) authoritySpan = d;
                }
            }
            if (authoritySpan > eps) return false;
            Bug1Vec3 authorityDelta = Bug1Vec3.Subtract(
                release.APos.Quantized().Horizontal(), frames[b].APos.Quantized().Horizontal());
            Bug1Vec3 carrierDelta = Bug1Vec3.Subtract(vRelease, vBefore);
            return Bug1Vec3.HorizontalDistance(authorityDelta, carrierDelta) <= eps;
        }

        /// <summary>§3.1：停段内权威根的水平渲染跨度（含 BEFORE_UPDATE 根位置），供审计区分「停段根没动」。</summary>
        private static double ComputeRenderRootSpanXZ(List<Bug1FrameSample> frames, int a, int b)
        {
            var points = new List<Bug1Vec3>();
            for (int i = a; i <= b; i++)
            {
                if (frames[i].APos.IsFinite) points.Add(frames[i].APos.Quantized().Horizontal());
                if (frames[i].HasBeforeUpdate && frames[i].ABeforeUpdate.IsFinite)
                    points.Add(frames[i].ABeforeUpdate.Quantized().Horizontal());
            }
            double span = 0d;
            for (int i = 0; i < points.Count; i++)
            {
                for (int j = i + 1; j < points.Count; j++)
                {
                    double d = Bug1Vec3.HorizontalDistance(points[i], points[j]);
                    if (d > span) span = d;
                }
            }
            return span;
        }

        /// <summary>§3.1：同段 SIM 的三维跨度，只作旁证输出，不参与 M1 布尔判定。</summary>
        private static double ComputeSimRootSpan3D(Bug1StepWindow w, int frameA, int frameB)
        {
            var points = new List<Bug1Vec3>();
            foreach (Bug1SimSample s in w.Sims)
            {
                if (s == null || s.Frame < frameA || s.Frame > frameB) continue;
                if (s.HasBefore && s.ABefore.IsFinite) points.Add(s.ABefore.Quantized());
                if (s.HasAfter && s.AAfter.IsFinite) points.Add(s.AAfter.Quantized());
            }
            double span = 0d;
            for (int i = 0; i < points.Count; i++)
            {
                for (int j = i + 1; j < points.Count; j++)
                {
                    double d = Bug1Vec3.Distance(points[i], points[j]);
                    if (d > span) span = d;
                }
            }
            return span;
        }

        /// <summary>
        /// §3.2 / t28：候选窗口内帧与 SIM 必须各自带 input_seq、rx_id、queue_depth；
        /// 且同一 rx_id 只能对应同一个 input_seq。缺哪个字段就写哪个字段名，绝不内插补齐。
        /// </summary>
        private static bool ValidateCandidateAssociations(Bug1StepWindow w, Bug1Verdict v, int frameA, int frameB)
        {
            bool ok = true;
            var seqByRx = new Dictionary<ulong, uint>();

            foreach (Bug1FrameSample f in w.Frames)
            {
                if (f == null || f.Frame < frameA || f.Frame > frameB) continue;
                if (!f.HasLastInputSeq) { MarkUnknown(v, "input_seq@" + f.Frame, "INPUT_SEQ_MISSING"); ok = false; }
                if (!f.HasLastRxId) { MarkUnknown(v, "rx_id@" + f.Frame, "RX_ID_MISSING"); ok = false; }
                if (!f.HasQueueDepth) { MarkUnknown(v, "queue_depth@" + f.Frame, "QUEUE_DEPTH_MISSING"); ok = false; }
                if (f.HasLastRxId && f.HasLastInputSeq) TrackRxInputSeq(seqByRx, v, f.LastRxId, f.LastInputSeq, ref ok);
            }

            var seenSims = new HashSet<string>(StringComparer.Ordinal);
            foreach (Bug1SimSample s in w.Sims)
            {
                if (s == null || s.Frame < frameA || s.Frame > frameB) continue;
                string id = s.SimCallId ?? string.Empty;
                if (string.IsNullOrEmpty(id) || !seenSims.Add(id))
                {
                    MarkUnknown(v, "sim_assoc:" + id, "SIM_ASSOC_INVALID");
                    ok = false;
                    continue;
                }
                if (!s.HasInputSeq) { MarkUnknown(v, "input_seq@" + id, "INPUT_SEQ_MISSING"); ok = false; }
                if (!s.HasRxId) { MarkUnknown(v, "rx_id@" + id, "RX_ID_MISSING"); ok = false; }
                if (!s.HasQueueDepth) { MarkUnknown(v, "queue_depth@" + id, "QUEUE_DEPTH_MISSING"); ok = false; }
                if (s.HasRxId && s.HasInputSeq) TrackRxInputSeq(seqByRx, v, s.RxId, s.InputSeq, ref ok);
            }
            return ok;
        }

        /// <summary>rx_id 是本地接收号，不是输入序号；同一 rx_id 对应两个 input_seq 即为错配证据。</summary>
        private static void TrackRxInputSeq(Dictionary<ulong, uint> seqByRx, Bug1Verdict v, ulong rxId, uint inputSeq, ref bool ok)
        {
            if (rxId == 0UL)
            {
                MarkUnknown(v, "rx_id_null@" + inputSeq.ToString(CultureInfo.InvariantCulture), "RX_ID_NULL");
                ok = false;
                return;
            }
            if (seqByRx.TryGetValue(rxId, out uint existing))
            {
                if (existing != inputSeq)
                {
                    MarkUnknown(v, "rx_id_mismatch@" + rxId.ToString(CultureInfo.InvariantCulture), "RX_ID_INPUT_SEQ_MISMATCH");
                    ok = false;
                }
                return;
            }
            seqByRx.Add(rxId, inputSeq);
        }

        /// <summary>§5.4：yaw 最短角差，跨 0/360 边界为负控。</summary>
        public static double ShortestYawDelta(double fromDeg, double toDeg)
        {
            double delta = (toDeg - fromDeg + 180d) % 360d;
            if (delta < 0d) delta += 360d;
            return delta - 180d;
        }

        /// <summary>§5.3 数值例的独立实现校核：B = cap*dt + ε。</summary>
        public static double SpeedBoundary(double speedCapUS, double deltaSeconds)
            => speedCapUS * deltaSeconds + Bug1ProbeConstants.EpsilonU;
    }
}
