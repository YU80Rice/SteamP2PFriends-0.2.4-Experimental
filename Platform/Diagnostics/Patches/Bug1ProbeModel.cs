using System;
using System.Collections.Generic;

namespace SteamP2PFriends.Platform.Diagnostics.Patches
{
    /// <summary>
    /// Bug1 定向测量仪表的纯数据模型（无 Unity 依赖，可在控制台测试中构造）。
    /// 规格来源：.scratch/takeover-v05-20261106/bug1-instrumentation-spec.md §3。
    /// 本文件只描述"测到了什么"，不包含任何判定结论。
    /// </summary>
    internal enum Bug1Role
    {
        Host = 0,
        Guest = 1,
    }

    internal enum Bug1Origin
    {
        ServerAuthority = 0,
        ObserverReplica = 1,
    }

    /// <summary>§5 离线判定标签。Unknown 是证据不足，不是第三种已证根因。</summary>
    internal enum Bug1Label
    {
        Unknown = 0,
        M1StallCatchup = 1,
        AuthStepLocked = 2,
        NoThresholdEvent = 3,
    }

    /// <summary>§3.2 packet_type：只区分原生能直接判定的两种包，其余一律 Unknown，不猜。</summary>
    internal enum Bug1PacketType
    {
        Unknown = 0,
        Walk = 1,
        Drive = 2,
    }

    /// <summary>世界坐标（Unity world XYZ，单位 U）；只允许有限值参与判定。</summary>
    internal struct Bug1Vec3 : IEquatable<Bug1Vec3>
    {
        public double X;
        public double Y;
        public double Z;

        public Bug1Vec3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public bool IsFinite =>
            !double.IsNaN(X) && !double.IsInfinity(X)
            && !double.IsNaN(Y) && !double.IsInfinity(Y)
            && !double.IsNaN(Z) && !double.IsInfinity(Z);

        public static double Distance(Bug1Vec3 a, Bug1Vec3 b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            double dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>§3.4：位置网格固定 q=0.001 U/axis，取最近网格点；判定只用该固定编码序列。</summary>
        public static double QuantizeComponent(double value) => Math.Round(value / Bug1ProbeConstants.PositionQuantumU) * Bug1ProbeConstants.PositionQuantumU;

        public Bug1Vec3 Quantized() => new Bug1Vec3(
            QuantizeComponent(X), QuantizeComponent(Y), QuantizeComponent(Z));

        /// <summary>T71/M1-XZ-RELATIVE-v2 §3.1：水平投影 H(p)=(x,z)；走/跑候选只在 XZ 上判定，Y 单独留给跳跃/落地判据。</summary>
        public Bug1Vec3 Horizontal() => new Bug1Vec3(X, 0d, Z);

        public static Bug1Vec3 Subtract(Bug1Vec3 a, Bug1Vec3 b)
            => new Bug1Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static double HorizontalDistance(Bug1Vec3 a, Bug1Vec3 b)
        {
            double dx = a.X - b.X;
            double dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        public static double HorizontalDot(Bug1Vec3 a, Bug1Vec3 b) => a.X * b.X + a.Z * b.Z;

        /// <summary>
        /// T71 §4：D_motion=0.020U 在固定 q=0.001U 网格上按整数格比较（≥20 格含边界）。
        /// 用整数平方和与 400 比较，避免 0.020/0.001 的浮点减法把 20 格误落到 19.999 格。
        /// </summary>
        public static bool HorizontalAtLeastGrids(Bug1Vec3 a, Bug1Vec3 b, int grids)
        {
            double q = Bug1ProbeConstants.PositionQuantumU;
            long gx = (long)Math.Round((a.X - b.X) / q);
            long gz = (long)Math.Round((a.Z - b.Z) / q);
            return gx * gx + gz * gz >= (long)grids * grids;
        }

        public bool Equals(Bug1Vec3 other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

        public override bool Equals(object obj) => obj is Bug1Vec3 other && Equals(other);

        public override int GetHashCode() => X.GetHashCode() ^ (Y.GetHashCode() << 1) ^ (Z.GetHashCode() << 2);
    }

    /// <summary>冻结常数：配额与精度只按规格写死，不允许运行期调大 ε。</summary>
    internal static class Bug1ProbeConstants
    {
        public const string SchemaVersion = "spf-bug1-probe/1";
        public const double PositionQuantumU = 0.001;
        /// <summary>ε_u = √3·q ≈ 0.0017320508075688772 U（三轴记录分辨率，不是实测容忍度）。</summary>
        public static readonly double EpsilonU = Math.Sqrt(3.0) * PositionQuantumU;
        public const double YawQuantumDeg = 0.01;
        public const double EpsilonYawDeg = 0.01;
        /// <summary>§5.2：至少三次连续渲染帧间小位移（至少四个位置点）。</summary>
        public const int MinStallDisplacements = 3;
        public const int MaxTraceRecords = 131072;
        public const long MaxTraceBytes = 64L * 1024 * 1024;
        /// <summary>§3.2：入队→消费关联表有界（FIFO 淘汰），不保留整包历史。</summary>
        public const int MaxRxTableEntries = 512;

        /// <summary>
        /// T71/M1-XZ-RELATIVE-v2：M1 判定规则修订号。旧 T4 三维 RootSpan&gt;ε 口径被本修订取代；
        /// 新旧数值 PASS 不可混称同一口径，旧结果按 revision 重判、原文件保留供审计。
        /// </summary>
        public const string M1RuleRevision = "M1-XZ-RELATIVE-v2";
        /// <summary>走/跑候选只做水平投影；该字段随 result.json 导出，防止审计者把三维跨度当 M1 证据。</summary>
        public const string MotionAxes = "XZ";
        /// <summary>§3.2/§4：水平事件绝对门 D=0.020U＝20 个 1mm 记录网格（含边界，≥ 语义）。</summary>
        public const double HorizontalMotionMinU = 0.020;
        /// <summary>D 在固定 q=0.001U 网格上的格数；比较用整数平方和，见 Bug1Vec3.HorizontalAtLeastGrids。</summary>
        public const int HorizontalMotionMinGrids = 20;

        /// <summary>
        /// T79：事件聚合口径修订号。右截断只局部生效——正常窗口末尾停段没有未来释放帧时，
        /// 只把该尾段记成 RIGHT_CENSORED，不得推翻同一步内更早证据完整、已闭合的事件；
        /// 事件计数因此是下界（strict_m1_events_is_lower_bound）。
        /// 与 M1RuleRevision 并存，不覆盖历史产物。
        /// </summary>
        public const string EventAggregationRevision = "CLOSED_EVENT_PRIORITY-v1";
    }
}
