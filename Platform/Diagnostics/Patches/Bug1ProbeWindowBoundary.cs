using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SteamP2PFriends.Platform.Diagnostics.Patches
{
    /// <summary>
    /// t60：窗口边界统计。窗口只在「首末都完整采到的帧」之间判定；两端因开窗/收窗时机造成的
    /// 半帧不得算成缺相位，必须单独计数并带原因码，供下游区分「真没采到」与「窗口越界」。
    /// </summary>
    internal sealed class Bug1WindowBounds
    {
        public const string ReasonHeadPartial = "WINDOW_HEAD_PARTIAL";
        public const string ReasonTailPartial = "WINDOW_TAIL_PARTIAL";
        public const string ReasonTailUnexplained = "WINDOW_TAIL_UNEXPLAINED";
        public const string ReasonNone = "WINDOW_BOUNDARY_NONE";

        /// <summary>t65：尾部归因。只有前两种是「合法边界」，允许裁剪；capture_failure 一律不裁。</summary>
        public const string BoundaryPlayerDestroyed = "boundary_player_destroyed";
        public const string BoundaryLevelChanged = "boundary_level_changed";
        public const string BoundaryCaptureFailure = "suspected_capture_failure";
        public const string BoundaryNone = "none";
        public const string HeadBoundaryPartial = "window_head_partial";
        public const string HeadBoundaryNone = "none";

        public int HeadExcluded;
        public int TailExcluded;
        /// <summary>t65：尾部候选帧数（含未被合法边界解释、因而未裁的帧）。</summary>
        public int TailCandidateCount;
        public int InteriorPartial;
        public int FirstFrame = -1;
        public int LastFrame = -1;
        public int RemainingFrames;
        public string ExcludedReason = ReasonNone;

        /// <summary>t65：被剔除尾部（或本应剔除但证据不足）的首末帧号。</summary>
        public int TailFirstFrame = -1;
        public int TailLastFrame = -1;
        /// <summary>t65：尾部归因结果，见 Boundary* 常量。</summary>
        public string TailBoundaryKind = BoundaryNone;
        public string HeadBoundaryKind = HeadBoundaryNone;
        /// <summary>t65：尾部存在但没有任何合法边界解释——判 UNKNOWN，绝不裁。</summary>
        public bool TailUnexplained;
        /// <summary>t65：尾部里「有 RENDER_READY 无 BEFORE_UPDATE」的帧数（render-only 尾部）。</summary>
        public int TailRenderOnly;
    }

    /// <summary>
    /// t65：收口时刻的采集面证据。边界是否「合法」不能只看 reason 字符串——
    /// 还要确认 detour 没有被 fail-closed、目标确实已销毁（target_left）或关卡确实在切换。
    /// 纯数据，无 Unity/SDK 依赖，便于离线用例直接构造。
    /// </summary>
    internal sealed class Bug1WindowBoundaryContext
    {
        public string EndReason = string.Empty;
        public bool TargetAlive;
        public bool ThirdAlive;
        public bool RendererAlive;
        public bool HookFailClosed;
        public string LastHookError = string.Empty;
    }

    /// <summary>
    /// t60：窗口收口（纯函数，无 Unity/SDK 依赖）。
    /// 规则：只在 [首个相位齐全帧 .. 末个相位齐全帧] 之间判定；
    /// 两端不齐全的帧从窗口移除并各自计数（head/tail），窗口内部的半帧保留——那是真正的缺相位。
    /// 若整窗没有任何齐全帧（例如 detour 全灭），一律不裁剪，交给判定器如实报 MISSING_PHASE，
    /// 绝不因为「裁剪后变干净」而掩盖系统性采集失败。
    /// </summary>
    internal static class Bug1ProbeWindowBoundary
    {
        public static Bug1WindowBounds Finalize(Bug1StepWindow window)
        {
            // 无上下文＝无法证明任何边界合法，按 capture failure 处理（fail-closed）。
            return Finalize(window, null);
        }

        public static Bug1WindowBounds Finalize(Bug1StepWindow window, Bug1WindowBoundaryContext ctx)
        {
            var bounds = new Bug1WindowBounds();
            if (window == null || window.Frames == null || window.Frames.Count == 0) return bounds;

            int firstComplete = -1;
            int lastComplete = -1;
            for (int i = 0; i < window.Frames.Count; i++)
            {
                Bug1FrameSample f = window.Frames[i];
                if (f != null && f.HasBeforeUpdate && f.HasRenderReady)
                {
                    if (firstComplete < 0) firstComplete = i;
                    lastComplete = i;
                }
            }

            bounds.RemainingFrames = window.Frames.Count;
            if (firstComplete < 0 || lastComplete < firstComplete)
            {
                bounds.ExcludedReason = Bug1WindowBounds.ReasonNone;
                return bounds;
            }

            bounds.HeadExcluded = firstComplete;
            int tailCount = window.Frames.Count - 1 - lastComplete;
            for (int i = firstComplete; i <= lastComplete; i++)
            {
                Bug1FrameSample f = window.Frames[i];
                if (f == null || !f.HasBeforeUpdate || !f.HasRenderReady) bounds.InteriorPartial++;
            }

            if (tailCount > 0)
            {
                bounds.TailCandidateCount = tailCount;
                bounds.TailFirstFrame = window.Frames[lastComplete + 1].Frame;
                bounds.TailLastFrame = window.Frames[window.Frames.Count - 1].Frame;
                for (int i = lastComplete + 1; i < window.Frames.Count; i++)
                {
                    Bug1FrameSample f = window.Frames[i];
                    // render-only：渲染相位还在产帧、BEFORE_UPDATE detour 已停（暂停/剔除/相机仍在跑）。
                    if (f != null && f.HasRenderReady && !f.HasBeforeUpdate) bounds.TailRenderOnly++;
                }
            }

            bounds.HeadBoundaryKind = bounds.HeadExcluded > 0
                ? Bug1WindowBounds.HeadBoundaryPartial
                : Bug1WindowBounds.HeadBoundaryNone;
            bounds.TailBoundaryKind = ClassifyTail(tailCount, ctx);
            bool legalTail = IsLegalBoundary(bounds.TailBoundaryKind);
            // TailExcluded 只统计「真的被剔除」的帧；证据不足时不裁，计数必须为 0，
            // 候选帧数另记在 TailCandidateCount 里，避免把「没裁」写成「裁了」。
            bounds.TailExcluded = legalTail ? tailCount : 0;

            var parts = new List<string>();
            if (bounds.HeadExcluded > 0) parts.Add(Bug1WindowBounds.ReasonHeadPartial);
            if (tailCount > 0)
                parts.Add(legalTail ? Bug1WindowBounds.ReasonTailPartial : Bug1WindowBounds.ReasonTailUnexplained);
            bounds.ExcludedReason = parts.Count == 0
                ? Bug1WindowBounds.ReasonNone
                : string.Join("+", parts.ToArray());

            // 头部半帧由「开窗时机」决定（Enable 落在帧中间），与采集中途失效无关，始终裁掉；
            // 尾部只在有合法边界解释时裁——否则保留原样交判定器报 UNKNOWN，绝不因为「裁干净」而掩盖失效。
            bounds.TailUnexplained = tailCount > 0 && !legalTail;
            if (legalTail && tailCount > 0)
                window.Frames.RemoveRange(lastComplete + 1, tailCount);
            if (bounds.HeadExcluded > 0)
                window.Frames.RemoveRange(0, bounds.HeadExcluded);

            bounds.RemainingFrames = window.Frames.Count;
            if (window.Frames.Count > 0)
            {
                bounds.FirstFrame = window.Frames[0].Frame;
                bounds.LastFrame = window.Frames[window.Frames.Count - 1].Frame;
                window.StepFirstFrame = bounds.FirstFrame;
                window.StepLastFrame = bounds.LastFrame;
            }
            return bounds;
        }

        /// <summary>尾部归因：只有 target_left / level_changed 且采集面未被 fail-closed 才算合法边界。</summary>
        private static string ClassifyTail(int tailCount, Bug1WindowBoundaryContext ctx)
        {
            if (tailCount <= 0) return Bug1WindowBounds.BoundaryNone;
            if (ctx == null || ctx.HookFailClosed) return Bug1WindowBounds.BoundaryCaptureFailure;

            string reason = ctx.EndReason ?? string.Empty;
            if (string.Equals(reason, "target_left", StringComparison.Ordinal) && !ctx.TargetAlive)
                return Bug1WindowBounds.BoundaryPlayerDestroyed;
            if (string.Equals(reason, "level_changed", StringComparison.Ordinal))
                return Bug1WindowBounds.BoundaryLevelChanged;

            // session_reset / config_changed / 任意其它原因都不解释采集中途停止。
            return Bug1WindowBounds.BoundaryCaptureFailure;
        }

        public static bool IsLegalBoundary(string kind)
        {
            return string.Equals(kind, Bug1WindowBounds.BoundaryPlayerDestroyed, StringComparison.Ordinal)
                || string.Equals(kind, Bug1WindowBounds.BoundaryLevelChanged, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// t60：远端插值观测（只读）。对应 SDK `PlayerMovement.cs:1746`
    /// `player.first.position = Vector3.Lerp(lastUpdatePos, transform.position, (Time.realtimeSinceStartup - player.input.tick) / PlayerInput.RATE)`。
    /// 任一输入不可得就置 Has*=false 并在 Unavailable 里写明，绝不填默认值冒充真值。
    /// </summary>
    internal sealed class Bug1InterpolationSample
    {
        public bool HasTick;
        public double Tick;
        public bool HasSampleTime;
        public double SampleTime;
        public bool HasRate;
        public double Rate;
        public bool HasLerp;
        public double Lerp;
        public double LerpClamped;
        public bool HasLastUpdatePos;
        public Bug1Vec3 LastUpdatePos;
        public bool HasAuthorityPos;
        public Bug1Vec3 AuthorityPos;
        public bool HasModelPos;
        public Bug1Vec3 ModelPos;
        public readonly List<string> Unavailable = new List<string>();

        public static Bug1InterpolationSample Compute(
            bool hasTick, double tick,
            bool hasSampleTime, double sampleTime,
            bool hasRate, double rate,
            bool hasLastUpdatePos, Bug1Vec3 lastUpdatePos,
            bool hasAuthorityPos, Bug1Vec3 authorityPos,
            bool hasModelPos, Bug1Vec3 modelPos)
        {
            var s = new Bug1InterpolationSample();
            s.HasTick = hasTick && IsFinite(tick);
            s.Tick = s.HasTick ? tick : 0d;
            s.HasSampleTime = hasSampleTime && IsFinite(sampleTime);
            s.SampleTime = s.HasSampleTime ? sampleTime : 0d;
            s.HasRate = hasRate && IsFinite(rate) && rate > 0d;
            s.Rate = s.HasRate ? rate : 0d;
            s.HasLastUpdatePos = hasLastUpdatePos && lastUpdatePos.IsFinite;
            s.LastUpdatePos = lastUpdatePos;
            s.HasAuthorityPos = hasAuthorityPos && authorityPos.IsFinite;
            s.AuthorityPos = authorityPos;
            s.HasModelPos = hasModelPos && modelPos.IsFinite;
            s.ModelPos = modelPos;
            if (s.HasTick && s.HasSampleTime && s.HasRate)
            {
                s.HasLerp = true;
                s.Lerp = (s.SampleTime - s.Tick) / s.Rate;
                s.LerpClamped = s.Lerp < 0d ? 0d : (s.Lerp > 1d ? 1d : s.Lerp);
            }
            if (!s.HasTick) s.Unavailable.Add("remote_input_tick");
            if (!s.HasSampleTime) s.Unavailable.Add("sample_time_s");
            if (!s.HasRate) s.Unavailable.Add("input_rate");
            if (!s.HasLerp) s.Unavailable.Add("lerp_factor");
            if (!s.HasLastUpdatePos) s.Unavailable.Add("last_update_pos");
            if (!s.HasAuthorityPos) s.Unavailable.Add("authority_pos");
            if (!s.HasModelPos) s.Unavailable.Add("model_pos");
            return s;
        }

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }

    /// <summary>
    /// t60：input_association 失配必须可解释。规则固定为「渲染帧关联到该帧之前最近一次消费
    /// （AFTER_DEQUEUE）」，失配时给出原因码与候选 rx_id/inputseq，不再只写一个笼统的
    /// last_consume_record_incomplete。
    /// </summary>
    internal static class Bug1InputAssociation
    {
        public const string Rule = "last_consume_before_render";
        public const string ReasonNoConsumeYet = "no_consume_yet";
        public const string ReasonMissingInputSeq = "last_consume_missing_inputseq";
        public const string ReasonMissingRxId = "last_consume_missing_rx_id";
        public const string ReasonMissingQueueDepth = "last_consume_missing_queue_depth";

        /// <summary>关联成立返回 null；否则返回原因码。</summary>
        public static string Classify(bool hasLastConsume, bool hasInputSeq, bool hasRxId, bool hasQueueDepth)
        {
            if (!hasLastConsume) return ReasonNoConsumeYet;
            if (!hasInputSeq) return ReasonMissingInputSeq;
            if (!hasRxId) return ReasonMissingRxId;
            if (!hasQueueDepth) return ReasonMissingQueueDepth;
            return null;
        }

        public static string JsonFragment(string reason, bool hasRxId, ulong rxId, bool hasInputSeq, uint inputSeq)
        {
            var sb = new StringBuilder(160);
            sb.Append("\"input_association_rule\":\"").Append(Rule).Append('"');
            sb.Append(",\"input_association_reason\":");
            sb.Append(string.IsNullOrEmpty(reason) ? "null" : "\"" + reason + "\"");
            sb.Append(",\"candidate_rx_ids\":[");
            if (hasRxId) sb.Append('"').Append(rxId.ToString(CultureInfo.InvariantCulture)).Append('"');
            sb.Append(']');
            sb.Append(",\"candidate_inputseqs\":[");
            if (hasInputSeq) sb.Append(inputSeq.ToString(CultureInfo.InvariantCulture));
            sb.Append(']');
            return sb.ToString();
        }
    }
}
