using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SteamP2PFriends.Platform.Diagnostics.Patches
{
    /// <summary>
    /// t85/t90：G 门（t76 §2）里**可以离线判定**的那一半——采样上限、白名单、
    /// 逐 renderer 偏差、快照比较、profile 指纹、原因码映射、JSON 结构。
    /// 这里不引用任何 Unity 类型，控制台进程可直接跑用例；
    /// 真实蒙皮位移、BakeMesh、Collider/Raycast 只能实机测，不在本文件假装。
    ///
    /// t90 修订要点（T86-F1/F2/F4/F5 + R16-01/05）：
    /// - 判定改为 t76 §2.2 原口径：**每个** renderer 的 `max‖(p_after−p_before)−delta‖ ≤ epsilon`；
    ///   P50/P90 只作报告，不进判定。
    /// - epsilon 不预设：默认 0 表示「未指定」，此时只能 UNKNOWN/EPSILON_UNSPECIFIED，
    ///   绝不为了让数据好看挑一个刚好过关的阈值。
    /// - 三个比较函数与白名单判定下沉到这里，生产侧调用它们（IL 断言钉住调用点）。
    /// </summary>
    internal static class Bug1GateProbeLogic
    {
        public const string Section = "Bug1Gate";

        /// <summary>t76 §2.2 固定 0.10U；只做实验，不参与任何生产插值。</summary>
        public const double DefaultOffsetU = 0.10d;

        /// <summary>每轴表面采样点数的**硬上限**（R16-05：cfg 名字即语义，实际点数不会超过它）。</summary>
        public const int DefaultSampleCount = 64;

        /// <summary>
        /// epsilon 默认 **0 = 未指定**（T86-F1：t76 §2.2 只给了符号，没给数值）。
        /// 未指定时判定停在 UNKNOWN/EPSILON_UNSPECIFIED，由决策者按 max_residual_u 定阈；
        /// 严禁实现方自己拍一个刚好过关的数。
        /// </summary>
        public const double UnspecifiedEpsilonU = 0d;

        public const string EpsilonSourceUnspecified = "unspecified";
        public const string EpsilonSourceConfig = "config:MaxDeviationU";

        /// <summary>还原判等：位级相等是硬门，另用这个 epsilon 判断「表面回到基线」。</summary>
        public const double RestoreEpsilonU = 1e-4d;

        public const string StatusPass = "PASS";
        public const string StatusFail = "FAIL";
        public const string StatusUnknown = "UNKNOWN";

        public const string ReasonNone = "NONE";
        public const string ReasonNotMeasured = "NOT_MEASURED";
        public const string ReasonEpsilonUnspecified = "EPSILON_UNSPECIFIED";

        // 契约要求的五个可读原因码。
        public const string ReasonSurfaceNotMoved = "SURFACE_NOT_MOVED";
        public const string ReasonPartialSurfaceMoved = "PARTIAL_SURFACE_MOVED";
        public const string ReasonColliderChanged = "COLLIDER_CHANGED";
        public const string ReasonRaycastChanged = "RAYCAST_CHANGED";
        public const string ReasonRestoreMismatch = "RESTORE_MISMATCH";

        // T92-F1：三条物理边（root / Third / Skeleton）逐条判定，拒绝码必须能区分是哪一条被动了。
        public const string ReasonRootChanged = "ROOT_CHANGED";
        public const string ReasonThirdChanged = "THIRD_CHANGED";
        public const string ReasonSkeletonChanged = "SKELETON_CHANGED";
        public const string ReasonTransformUnavailable = "UNAVAILABLE_TRANSFORM_SNAPSHOT";

        // 准入与 fail-closed 的补充原因码（契约只要求前五个可读，这里补齐失败现场）。
        public const string ReasonSurfaceMovedTooFar = "SURFACE_MOVED_TOO_FAR";
        public const string ReasonRoleNotHost = "ROLE_NOT_HOST";
        public const string ReasonTargetNotFound = "TARGET_NOT_FOUND";
        public const string ReasonTargetAmbiguous = "TARGET_AMBIGUOUS";
        public const string ReasonTargetMissing = "TARGET_STEAM_ID_MISSING";
        public const string ReasonTargetGone = "TARGET_GONE_BEFORE_FIRST_SAMPLE";
        public const string ReasonThirdUnresolved = "THIRD_UNRESOLVED";
        public const string ReasonModelNodeMissing = "MODEL_NODE_MISSING";
        public const string ReasonNoVisibleSurface = "NO_VISIBLE_SURFACE";
        public const string ReasonImpureRenderNode = "IMPURE_RENDER_NODE";
        public const string ReasonBakeUnavailable = "BAKE_UNAVAILABLE";
        public const string ReasonBakeFailed = "BAKE_FAILED";
        public const string ReasonBonesUnreadable = "BONES_UNREADABLE";
        public const string ReasonMeshChanged = "MESH_CHANGED";
        public const string ReasonUnexpectedException = "UNEXPECTED_EXCEPTION";
        public const string ReasonOutputFailed = "OUTPUT_FAILED";
        public const string ReasonAlreadyRan = "ALREADY_RAN";

        /// <summary>T86-F4：快照读不到 ≠ 没变化；这两类只允许 UNKNOWN。</summary>
        public const string ReasonColliderUnavailable = "UNAVAILABLE_COLLIDER_SNAPSHOT";
        public const string ReasonRaycastUnavailable = "UNAVAILABLE_RAYCAST_SNAPSHOT";

        // ==== T94/T95：六格姿态覆盖（g_evidence_revision=T94_POSE_GRID-v1） ====

        /// <summary>T94 §4.1：新证据版本号，与旧单格 PASS 区分，不升级历史结果。</summary>
        public const string EvidenceRevision = "T94_POSE_GRID-v1";

        /// <summary>姿态节点角色：Skeleton 子树 / renderer 绑定骨骼 / Collider 父链。</summary>
        public const string PoseRoleSkeleton = "skeleton";
        public const string PoseRoleBound = "bound";
        public const string PoseRoleColliderParent = "collider-parent";

        /// <summary>T94 §3.1：local TRS 3+4+3 = 10 个 float；world matrix 16 个 float。</summary>
        public const int PoseLocalFieldCount = 10;
        public const int PoseWorldFieldCount = 16;

        /// <summary>朝向桶：面向 ±X 主轴 / 面向 ±Z 主轴（两桶中心相差 90°）。</summary>
        public const string HeadingBucketX = "x";
        public const string HeadingBucketZ = "z";
        public const string HeadingUnstable = "unstable";

        /// <summary>速度档：idle / walk / sprint；unstable 表示落在死区，不参与归档。</summary>
        public const string SpeedCellIdle = "idle";
        public const string SpeedCellWalk = "walk";
        public const string SpeedCellSprint = "sprint";
        public const string SpeedUnstable = "unstable";

        /// <summary>
        /// T94 §2：两桶标称 yaw 为 0°（面向 +Z）与 90°（面向 +X），容差 ±5°。
        /// 旧实现用「主轴分量 ≥ 次轴 1.5 倍」等价于 ±33.7° 的窗口，比 t94 宽得多，已废弃。
        /// </summary>
        public const float HeadingNominalXDeg = 90f;
        public const float HeadingNominalZDeg = 0f;
        public const float HeadingToleranceDeg = 5f;
        /// <summary>
        /// 同一朝向桶需连续命中 N 次采样才接受（R17-02：单位是采样，不是渲染帧；
        /// 采样间隔由 SampleIntervalSeconds 决定，默认 1s，故不能按 16ms 帧解释）。
        /// </summary>
        public const int HeadingStableSamples = 2;

        /// <summary>速度带：≤10% 走速 = idle；≥50% 走速且 &lt; 走/跑中点 = walk；≥中点 = sprint。</summary>
        public const double IdleSpeedRatio = 0.10d;
        public const double WalkMinRatio = 0.50d;

        public const int MinSamplesPerCellDefault = 3;
        public const int MaxSamplesDefault = 240;

        /// <summary>
        /// R17-01：生产侧姿态遍历必须有界（此前只有 plan 层有界，CollectSubtree 无上限）。
        /// 单次快照最多收集 512 个节点、递归深度最多 64 层，超限即 fail-closed。
        /// </summary>
        public const int MaxPoseNodes = 512;
        public const int MaxPoseDepth = 64;

        public const string ModeCoverageNote =
            "六格采样：姿态由 stance（STAND+isMoving 派生 idle/walk、SPRINT→sprint）与实测 yaw（0°/90°±5°）区分";
        public const string ModeSingleNote =
            "single_frame_single_pose：只测一个相机帧、一个姿态（t76 §2.2 的多姿态需实机/Architect 决定）";

        public const string ReasonSamplesInsufficient = "SAMPLES_INSUFFICIENT";
        public const string ReasonPoseCounterexample = "POSE_COUNTEREXAMPLE";
        public const string ReasonNoPositiveEvidence = "NO_POSITIVE_EVIDENCE";
        public const string ReasonPoseUnavailable = "POSE_UNAVAILABLE";
        public const string ReasonPoseUnstable = "POSE_UNSTABLE";
        public const string ReasonHeadingUnstable = "HEADING_UNSTABLE";
        public const string ReasonSpeedUnstable = "SPEED_UNSTABLE";
        public const string ReasonInvarianceChanged = "INVARIANCE_CHANGED";

        /// <summary>六格 id（速度档 × 朝向桶）。</summary>
        public static readonly string[] AllCells =
        {
            "idle-x", "idle-z", "walk-x", "walk-z", "sprint-x", "sprint-z",
        };

        /// <summary>t76 §2.1 白名单：纯渲染节点上允许出现的组件类型（按类型名，离线可测）。</summary>
        private static readonly string[] WhitelistedComponentTypes =
        {
            "Transform",
            "Renderer",          // SkinnedMeshRenderer / MeshRenderer 等派生类型由 IsAssignableFrom 口径另行放行
            "MeshFilter",
            "Animator",
            "Animation",
            "LODGroup",
        };

        /// <summary>
        /// R16-01：白名单判定下沉为纯函数，离线可逐类断言。
        /// 注意派生 renderer 的**具体类型名**（SkinnedMeshRenderer/MeshRenderer）也放行——
        /// 生产侧先按类型层级过滤，再把剩余类型名交给这里，两层口径一致。
        /// </summary>
        public static bool IsWhitelistedRenderComponentType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return false;
            for (int i = 0; i < WhitelistedComponentTypes.Length; i++)
            {
                if (string.Equals(WhitelistedComponentTypes[i], typeName, StringComparison.Ordinal)) return true;
            }
            // 具体 renderer 类型名（派生自 Renderer，但 GetType().Name 不是 "Renderer"）。
            if (string.Equals(typeName, "SkinnedMeshRenderer", StringComparison.Ordinal)) return true;
            if (string.Equals(typeName, "MeshRenderer", StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// R16-01：生产侧把「组件类型名」收集出来，判定在这里做（纯函数）。
        /// 返回全部白名单外的类型名（去重、按出现顺序）。
        /// </summary>
        public static List<string> CollectImpureComponentTypes(IList<string> componentTypeNames)
        {
            var impure = new List<string>();
            if (componentTypeNames == null) return impure;
            for (int i = 0; i < componentTypeNames.Count; i++)
            {
                string typeName = componentTypeNames[i];
                if (string.IsNullOrEmpty(typeName)) continue;
                if (typeName.StartsWith("<", StringComparison.Ordinal)) // <read-failed:*> / <null-component>
                {
                    if (!impure.Contains(typeName)) impure.Add(typeName);
                    continue;
                }
                if (IsWhitelistedRenderComponentType(typeName)) continue;
                if (!impure.Contains(typeName)) impure.Add(typeName);
            }
            return impure;
        }

        /// <summary>升序分位数（线性插值，样本少时也不会除零）。</summary>
        public static double Percentile(IList<double> sortedAscending, double percentile)
        {
            if (sortedAscending == null || sortedAscending.Count == 0) return 0d;
            if (percentile <= 0d) return sortedAscending[0];
            if (percentile >= 1d) return sortedAscending[sortedAscending.Count - 1];
            double position = (sortedAscending.Count - 1) * percentile;
            int lower = (int)Math.Floor(position);
            int upper = (int)Math.Ceiling(position);
            if (lower == upper) return sortedAscending[lower];
            double weight = position - lower;
            return sortedAscending[lower] * (1d - weight) + sortedAscending[upper] * weight;
        }

        /// <summary>把逐点位移量汇总成 p10/p50/p90/median/mean；输入顺序无关。</summary>
        public static Bug1GateDisplacementStats Summarize(IList<double> magnitudes)
        {
            var stats = new Bug1GateDisplacementStats();
            if (magnitudes == null || magnitudes.Count == 0) return stats;
            var sorted = new List<double>(magnitudes.Count);
            double sum = 0d;
            for (int i = 0; i < magnitudes.Count; i++)
            {
                double value = magnitudes[i];
                if (double.IsNaN(value) || double.IsInfinity(value)) continue;
                sorted.Add(value);
                sum += value;
            }
            if (sorted.Count == 0) return stats;
            sorted.Sort();
            stats.Count = sorted.Count;
            stats.P10 = Percentile(sorted, 0.10d);
            stats.P50 = Percentile(sorted, 0.50d);
            stats.P90 = Percentile(sorted, 0.90d);
            stats.Median = stats.P50;
            stats.Mean = sum / sorted.Count;
            return stats;
        }

        /// <summary>
        /// R16-05：把「每轴最多 samples 个点」做成硬上限的取样索引。
        /// step = ceil(vertexCount / samples)，因此取出的点数恒 ≤ samples。
        /// </summary>
        public static List<int> SelectSampleIndices(int vertexCount, int maxSamples)
        {
            var indices = new List<int>();
            if (vertexCount <= 0) return indices;
            int cap = maxSamples > 0 ? maxSamples : DefaultSampleCount;
            int step = (vertexCount + cap - 1) / cap; // ceil
            if (step < 1) step = 1;
            for (int i = 0; i < vertexCount; i += step) indices.Add(i);
            return indices;
        }

        /// <summary>
        /// 唯一裁决入口（t76 §2.2 原口径）。
        /// `measured=false` 一律 UNKNOWN——离线拿不到真实蒙皮位移，不许冒充 PASS。
        /// `epsilonU<=0` 表示阈值未指定：只有「明显没跟上」（残差大于位移本身）才 FAIL，
        /// 其余一律 UNKNOWN/EPSILON_UNSPECIFIED，把原始 max_residual_u 交给决策者。
        /// </summary>
        public static string Decide(
            bool measured,
            bool pureNode,
            bool restoreExact,
            bool surfaceRestored,
            Bug1GateInvariance invariance,
            double offsetU,
            IList<Bug1GateRendererStats> renderers,
            double epsilonU,
            out string status)
        {
            if (!measured)
            {
                status = StatusUnknown;
                return ReasonNotMeasured;
            }
            if (!pureNode)
            {
                status = StatusFail;
                return ReasonImpureRenderNode;
            }
            if (!restoreExact || !surfaceRestored)
            {
                status = StatusFail;
                return ReasonRestoreMismatch;
            }
            // T86-F4：快照不可用不是「没变化」，也不是「变了」——只能 UNKNOWN。
            if (invariance == null || !invariance.TransformsAvailable)
            {
                status = StatusUnknown;
                return ReasonTransformUnavailable;
            }
            // T92-F1：root / Third / Skeleton 三条边逐条判定，任一被动了都 FAIL，且拒绝码区分是哪条。
            if (!invariance.RootUnchanged)
            {
                status = StatusFail;
                return ReasonRootChanged;
            }
            if (!invariance.ThirdUnchanged)
            {
                status = StatusFail;
                return ReasonThirdChanged;
            }
            if (!invariance.SkeletonUnchanged)
            {
                status = StatusFail;
                return ReasonSkeletonChanged;
            }
            if (!invariance.CollidersAvailable)
            {
                status = StatusUnknown;
                return ReasonColliderUnavailable;
            }
            if (!invariance.RaycastAvailable)
            {
                status = StatusUnknown;
                return ReasonRaycastUnavailable;
            }
            if (!invariance.CollidersUnchanged)
            {
                status = StatusFail;
                return ReasonColliderChanged;
            }
            if (!invariance.RaycastUnchanged)
            {
                status = StatusFail;
                return ReasonRaycastChanged;
            }
            if (renderers == null || renderers.Count == 0)
            {
                status = StatusFail;
                return ReasonNoVisibleSurface;
            }

            double offset = offsetU > 0d ? offsetU : DefaultOffsetU;
            bool anySampled = false;
            bool movedTooFar = false;
            bool notMoved = false;
            bool partial = false;
            double worstResidual = 0d;

            for (int i = 0; i < renderers.Count; i++)
            {
                Bug1GateRendererStats renderer = renderers[i];
                if (renderer == null || renderer.SampleCount <= 0) continue;
                anySampled = true;
                if (renderer.MaxResidualU > worstResidual) worstResidual = renderer.MaxResidualU;

                // 「没跟上」有两种：位移量太小（表面没动），或残差大于位移本身（动了但不是跟着 delta）。
                if (renderer.MaxDisplacementU < offset * 0.5d || renderer.MaxResidualU > offset)
                {
                    notMoved = true;
                    continue;
                }
                if (renderer.MaxDisplacementU > offset * 2d)
                {
                    movedTooFar = true;
                    continue;
                }
                if (epsilonU > 0d && renderer.MaxResidualU > epsilonU) partial = true;
            }

            if (!anySampled)
            {
                status = StatusFail;
                return ReasonSurfaceNotMoved;
            }
            if (notMoved)
            {
                status = StatusFail;
                return ReasonSurfaceNotMoved;
            }
            if (movedTooFar)
            {
                status = StatusFail;
                return ReasonSurfaceMovedTooFar;
            }
            if (epsilonU <= 0d)
            {
                // 阈值未指定：即使所有点看起来都在动，也不许宣布通过。
                status = StatusUnknown;
                return ReasonEpsilonUnspecified;
            }
            if (partial)
            {
                status = StatusFail;
                return ReasonPartialSurfaceMoved;
            }
            status = StatusPass;
            return ReasonNone;
        }

        /// <summary>相对口径：max‖Δp−delta‖ / ‖delta‖，供决策者自己定阈。</summary>
        public static double MaxDeviationRatio(double offsetU, double maxResidualU)
        {
            double offset = offsetU > 0d ? offsetU : DefaultOffsetU;
            if (maxResidualU < 0d || double.IsNaN(maxResidualU) || double.IsInfinity(maxResidualU)) return -1d;
            return maxResidualU / offset;
        }

        /// <summary>偏差比（中位数相对 0.10U），仅作报告；无样本时记 -1。</summary>
        public static double DeviationRatio(double offsetU, Bug1GateDisplacementStats stats)
        {
            if (stats == null || stats.Count <= 0) return -1d;
            double offset = offsetU > 0d ? offsetU : DefaultOffsetU;
            return Math.Abs(stats.Median - offset) / offset;
        }

        /// <summary>
        /// 目标解析复用现有 selector 语义：Selected 返回 null，其余映射成可读错误码。
        /// 本函数不碰 Unity 类型，可离线用合成快照验证。
        /// </summary>
        public static string MapSelectionError(Bug1TargetSelection selection)
        {
            if (selection == null) return ReasonTargetMissing;
            if (selection.Status == Bug1TargetSelectionStatus.Selected) return null;
            if (!string.IsNullOrEmpty(selection.ErrorCode)) return selection.ErrorCode;
            return selection.Status == Bug1TargetSelectionStatus.Ambiguous
                ? ReasonTargetAmbiguous
                : ReasonTargetNotFound;
        }

        /// <summary>
        /// IMPURE_RENDER_NODE 的诊断载荷：必须给**组件类型名清单**，不是数量。
        /// 实机撞上时靠它判断「按类型放宽白名单」还是「纯叶方案不成立」，报数量等于让人再跑一轮。
        /// </summary>
        public static string FormatImpureComponents(IList<string> typeNames)
        {
            if (typeNames == null || typeNames.Count == 0) return "components=(none)";
            var builder = new StringBuilder("components=");
            var seen = new List<string>(typeNames.Count);
            bool first = true;
            for (int i = 0; i < typeNames.Count; i++)
            {
                string name = typeNames[i] ?? "<null-name>";
                if (seen.Contains(name)) continue;
                seen.Add(name);
                if (!first) builder.Append(',');
                builder.Append(name);
                first = false;
            }
            if (first) return "components=(none)";
            return builder.ToString();
        }

        /// <summary>float 位级判等：还原后必须逐位等于原生值，不能只比 epsilon。</summary>
        public static bool BitEqual(float a, float b)
            => BitConverter.ToInt32(BitConverter.GetBytes(a), 0) == BitConverter.ToInt32(BitConverter.GetBytes(b), 0);

        public static bool BitEqual(double a, double b)
            => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);

        /// <summary>T86-F2：下沉到纯逻辑的位级比较，生产侧必须调用（IL 断言钉住）。</summary>
        public static bool TransformsEqual(Bug1GateTransformSnapshot left, Bug1GateTransformSnapshot right)
        {
            if (ReferenceEquals(left, null) || ReferenceEquals(right, null)) return ReferenceEquals(left, right);
            if (!left.Available || !right.Available) return false;
            return BitEqual(left.Px, right.Px)
                && BitEqual(left.Py, right.Py)
                && BitEqual(left.Pz, right.Pz)
                && BitEqual(left.Qx, right.Qx)
                && BitEqual(left.Qy, right.Qy)
                && BitEqual(left.Qz, right.Qz)
                && BitEqual(left.Qw, right.Qw);
        }

        /// <summary>T86-F2/F4：Collider 快照逐字段比较；任一侧不可用即返回 false（调用方另判 UNKNOWN）。</summary>
        public static bool CollidersEqual(IList<Bug1GateColliderSnapshot> left, IList<Bug1GateColliderSnapshot> right)
        {
            if (left == null || right == null) return ReferenceEquals(left, right);
            if (left.Count != right.Count) return false;
            for (int i = 0; i < left.Count; i++)
            {
                Bug1GateColliderSnapshot a = left[i];
                Bug1GateColliderSnapshot b = right[i];
                if (ReferenceEquals(a, null) || ReferenceEquals(b, null)) return ReferenceEquals(a, b);
                if (!a.Available || !b.Available) return false;
                if (!string.Equals(a.Name, b.Name, StringComparison.Ordinal)) return false;
                if (!string.Equals(a.Type, b.Type, StringComparison.Ordinal)) return false;
                if (a.Enabled != b.Enabled) return false;
                if (!BitEqual(a.CenterX, b.CenterX)
                    || !BitEqual(a.CenterY, b.CenterY)
                    || !BitEqual(a.CenterZ, b.CenterZ)) return false;
                if (!BitEqual(a.SizeX, b.SizeX)
                    || !BitEqual(a.SizeY, b.SizeY)
                    || !BitEqual(a.SizeZ, b.SizeZ)) return false;
            }
            return true;
        }

        /// <summary>T86-F2/F4：Raycast 快照比较；任一侧不可用即 false（调用方另判 UNKNOWN）。</summary>
        public static bool RaycastsEqual(Bug1GateRaycastSnapshot left, Bug1GateRaycastSnapshot right)
        {
            if (ReferenceEquals(left, null) || ReferenceEquals(right, null)) return ReferenceEquals(left, right);
            if (!left.Available || !right.Available) return false;
            if (!string.Equals(left.HitName, right.HitName, StringComparison.Ordinal)) return false;
            return BitEqual(left.Distance, right.Distance);
        }

        /// <summary>
        /// T86-F5：profile 指纹——把「测的是哪套模型/材质/骨骼/碰撞体」规范化后取 SHA256。
        /// 同一 player 换装、换模型、UMA 重建后指纹必须变，前后两份 GATE.json 才可比对。
        /// </summary>
        /// <summary>
        /// T94 §3.1：姿态节点集合 K = Skeleton 全子树 ∪ 每个 renderer 的 bones[]/rootBone ∪
        /// Collider 所挂 Transform 到 root 的父链；按路径去重、保序（skeleton 在前，其次 bound，
        /// 最后 collider-parent）。生产侧只负责把三类路径取出来，判定与去重都在这里，离线可测。
        /// </summary>
        public static List<Bug1PoseNodePlan> BuildPoseNodePlan(
            IList<string> skeletonSubtreePaths,
            IList<string> bonePaths,
            IList<string> colliderParentPaths)
        {
            var plan = new List<Bug1PoseNodePlan>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            AppendPlan(plan, seen, skeletonSubtreePaths, PoseRoleSkeleton);
            AppendPlan(plan, seen, bonePaths, PoseRoleBound);
            AppendPlan(plan, seen, colliderParentPaths, PoseRoleColliderParent);
            return plan;
        }

        private static void AppendPlan(List<Bug1PoseNodePlan> plan, HashSet<string> seen,
            IList<string> paths, string role)
        {
            if (paths == null) return;
            for (int i = 0; i < paths.Count; i++)
            {
                string path = paths[i];
                if (string.IsNullOrEmpty(path)) continue;
                if (!seen.Add(path)) continue;
                plan.Add(new Bug1PoseNodePlan { Path = path, Role = role });
            }
        }

        /// <summary>
        /// T94 §3.1：逐节点逐字段 float32 位级比较（local 10 项 + world 16 项）。
        /// 不许用 epsilon、量化、bounds 或哈希代替；任一侧不可用即 false（调用方另判 UNKNOWN）。
        /// </summary>
        public static bool PosesEqual(Bug1PoseSnapshot left, Bug1PoseSnapshot right)
        {
            if (ReferenceEquals(left, null) || ReferenceEquals(right, null)) return ReferenceEquals(left, right);
            if (!left.Available || !right.Available) return false;
            if (left.Entries.Count != right.Entries.Count) return false;
            for (int i = 0; i < left.Entries.Count; i++)
            {
                Bug1PoseEntry a = left.Entries[i];
                Bug1PoseEntry b = right.Entries[i];
                if (ReferenceEquals(a, null) || ReferenceEquals(b, null)) return ReferenceEquals(a, b);
                if (!string.Equals(a.Path, b.Path, StringComparison.Ordinal)) return false;
                if (!string.Equals(a.Role, b.Role, StringComparison.Ordinal)) return false;
                if (!PoseEntryEqual(a, b)) return false;
            }
            return true;
        }

        private static bool PoseEntryEqual(Bug1PoseEntry a, Bug1PoseEntry b)
        {
            if (a.HasLocal != b.HasLocal || a.HasWorld != b.HasWorld) return false;
            if (a.HasLocal && !FloatArrayBitEqual(a.Local, b.Local, PoseLocalFieldCount)) return false;
            if (a.HasWorld && !FloatArrayBitEqual(a.World, b.World, PoseWorldFieldCount)) return false;
            return true;
        }

        private static bool FloatArrayBitEqual(float[] left, float[] right, int count)
        {
            if (left == null || right == null) return ReferenceEquals(left, right);
            if (left.Length < count || right.Length < count) return false;
            for (int i = 0; i < count; i++)
            {
                if (!BitEqual(left[i], right[i])) return false;
            }
            return true;
        }

        /// <summary>T94 §3.1：给出首个差异定位（path|local[i] / path|world[i]），相等返回空串。</summary>
        public static string PoseDiff(Bug1PoseSnapshot left, Bug1PoseSnapshot right)
        {
            if (ReferenceEquals(left, null) || ReferenceEquals(right, null)) return "snapshot-null";
            if (!left.Available) return "left-unavailable:" + left.UnavailableReason;
            if (!right.Available) return "right-unavailable:" + right.UnavailableReason;
            if (left.Entries.Count != right.Entries.Count)
                return "entry-count:" + left.Entries.Count.ToString(CultureInfo.InvariantCulture)
                    + "!=" + right.Entries.Count.ToString(CultureInfo.InvariantCulture);
            for (int i = 0; i < left.Entries.Count; i++)
            {
                Bug1PoseEntry a = left.Entries[i];
                Bug1PoseEntry b = right.Entries[i];
                if (ReferenceEquals(a, null) || ReferenceEquals(b, null)) return "entry-null@" + i;
                if (!string.Equals(a.Path, b.Path, StringComparison.Ordinal)) return "path@" + i + ":" + a.Path + "!=" + b.Path;
                if (a.HasLocal != b.HasLocal) return a.Path + "|has_local";
                if (a.HasWorld != b.HasWorld) return a.Path + "|has_world";
                if (a.HasLocal)
                {
                    for (int f = 0; f < PoseLocalFieldCount; f++)
                    {
                        if (!BitEqual(a.Local[f], b.Local[f])) return a.Path + "|local[" + f + "]";
                    }
                }
                if (a.HasWorld)
                {
                    for (int f = 0; f < PoseWorldFieldCount; f++)
                    {
                        if (!BitEqual(a.World[f], b.World[f])) return a.Path + "|world[" + f + "]";
                    }
                }
            }
            return string.Empty;
        }

        /// <summary>
        /// T94 §2：朝向桶。按实测 yaw（atan2(fx, fz)，0° = 面向 +Z，90° = 面向 +X）与
        /// 两个标称值比较，容差 ±5°；落在两个 5° 窗之外 → unstable（不归档）。
        /// yaw 同时回传给调用方，写入样本与 cells[]，便于复核入格依据。
        /// </summary>
        public static string ClassifyHeadingBucket(float forwardX, float forwardZ, out float yawDeg, out string reason)
        {
            yawDeg = float.NaN;
            reason = ReasonNone;
            if (float.IsNaN(forwardX) || float.IsNaN(forwardZ)
                || float.IsInfinity(forwardX) || float.IsInfinity(forwardZ))
            {
                reason = "heading_non_finite";
                return HeadingUnstable;
            }
            if (Math.Abs(forwardX) < float.Epsilon && Math.Abs(forwardZ) < float.Epsilon)
            {
                reason = "heading_too_small";
                return HeadingUnstable;
            }
            double yaw = Math.Atan2(forwardX, forwardZ) * (180d / Math.PI);
            if (yaw < 0d) yaw += 360d;
            yawDeg = (float)yaw;
            if (AngularDistanceDeg(yaw, HeadingNominalZDeg) <= HeadingToleranceDeg) return HeadingBucketZ;
            if (AngularDistanceDeg(yaw, HeadingNominalXDeg) <= HeadingToleranceDeg) return HeadingBucketX;
            reason = "yaw_outside_nominal";
            return HeadingUnstable;
        }

        /// <summary>环形角距（度），结果 ∈ [0, 180]。</summary>
        public static double AngularDistanceDeg(double leftDeg, double rightDeg)
        {
            double diff = Math.Abs(leftDeg - rightDeg) % 360d;
            return diff > 180d ? 360d - diff : diff;
        }

        /// <summary>
        /// T94 §2：速度档。阈值以原生走/跑上限为基准：≤10% 走速 = idle；
        /// [50% 走速, 走跑中点) = walk；≥中点 = sprint；其余（含上限不可用/非有限） = unstable。
        /// </summary>
        public static string ClassifySpeedCell(double speedU, double walkCapUS, double sprintCapUS, out string reason)
        {
            reason = ReasonNone;
            if (!IsPositiveFinite(walkCapUS) || !IsPositiveFinite(sprintCapUS))
            {
                reason = "speed_cap_unresolved";
                return SpeedUnstable;
            }
            if (double.IsNaN(speedU) || double.IsInfinity(speedU) || speedU < 0d)
            {
                reason = "speed_invalid";
                return SpeedUnstable;
            }
            if (speedU <= walkCapUS * IdleSpeedRatio) return SpeedCellIdle;
            if (speedU >= (walkCapUS + sprintCapUS) * 0.5d) return SpeedCellSprint;
            if (speedU >= walkCapUS * WalkMinRatio) return SpeedCellWalk;
            reason = "speed_between_idle_and_walk";
            return SpeedUnstable;
        }

        /// <summary>六格 id；任一维 unstable 返回 null（该帧不归档，不补样本）。</summary>
        public static string ComposeCellId(string speedCell, string headingBucket)
        {
            if (string.Equals(speedCell, SpeedUnstable, StringComparison.Ordinal)) return null;
            if (string.Equals(headingBucket, HeadingUnstable, StringComparison.Ordinal)) return null;
            if (string.IsNullOrEmpty(speedCell) || string.IsNullOrEmpty(headingBucket)) return null;
            return speedCell + "-" + headingBucket;
        }

        /// <summary>
        /// T94 §2 / R17-02：按原生 stance 枚举名 + 原生移动标志归档到 idle/walk/sprint。
        /// 真实枚举（EPlayerStance）没有 IDLE：站立不动与站立行走都报 STAND，
        /// 因此必须叠加 PlayerMovement.isMoving 才能区分 idle 与 walk；
        /// SPRINT 直接给 sprint。crouch/prone/swim/climb/driving/sitting 不在支持域 → unstable。
        /// </summary>
        public static string ClassifyStanceCell(string stanceName, bool moving, out string reason)
        {
            reason = ReasonNone;
            string stance = (stanceName ?? string.Empty).Trim().ToUpperInvariant();
            if (stance.Length == 0)
            {
                reason = "stance_unreadable";
                return SpeedUnstable;
            }
            if (stance == "SPRINT") return SpeedCellSprint;
            if (stance == "STAND") return moving ? SpeedCellWalk : SpeedCellIdle;
            if (stance == "CROUCH" || stance == "PRONE" || stance == "SWIM"
                || stance == "CLIMB" || stance == "DRIVING" || stance == "SITTING")
            {
                reason = "stance_out_of_scope:" + stance;
                return SpeedUnstable;
            }
            reason = "stance_unknown:" + stance;
            return SpeedUnstable;
        }

        private static bool IsPositiveFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0d;
        }

        /// <summary>
        /// R17-02：朝向稳定化的纯判定。单位是**采样**（不是渲染帧）：
        /// 只有连续 N 次采样命中同一桶才接受；换桶或 unstable 一律把计数清零。
        /// </summary>
        public static string AdvanceHeadingStability(
            string previousBucket, string currentBucket, int stableCount, out int newCount)
        {
            newCount = 0;
            if (string.IsNullOrEmpty(currentBucket)
                || string.Equals(currentBucket, HeadingUnstable, StringComparison.Ordinal))
            {
                return HeadingUnstable;
            }
            if (!string.IsNullOrEmpty(previousBucket)
                && string.Equals(previousBucket, currentBucket, StringComparison.Ordinal))
            {
                newCount = stableCount + 1;
            }
            else
            {
                newCount = 1;
            }
            return newCount >= HeadingStableSamples ? currentBucket : HeadingUnstable;
        }

        /// <summary>R17-04：只有可用样本才占 MaxSamples 预算，unassigned/不稳样本不计。</summary>
        public static int CountValidSamples(IList<Bug1GateCellSample> samples)
        {
            if (samples == null) return 0;
            int count = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                Bug1GateCellSample sample = samples[i];
                if (!ReferenceEquals(sample, null) && sample.Available) count++;
            }
            return count;
        }

        /// <summary>
        /// R17-01：生产侧姿态遍历的节点/深度上限（此前只有 plan 层有界）。
        /// 超限 → false，调用方必须把快照标成不可用，而不是继续遍历。
        /// </summary>
        public static bool PoseWalkAllowed(int collectedNodes, int maxNodes)
        {
            int cap = maxNodes > 0 ? maxNodes : MaxPoseNodes;
            return collectedNodes >= 0 && collectedNodes < cap;
        }

        /// <summary>
        /// R17-03 / MV-5：还原门的位级比较。逐 float32 位比较，任一位不同即 false；
        /// 长度不一致、null、NaN 位型不一致都算不等（不做 epsilon）。
        /// </summary>
        public static bool RestoreBitsEqual(float[] before, float[] after)
        {
            if (ReferenceEquals(before, null) || ReferenceEquals(after, null)) return false;
            if (before.Length != after.Length) return false;
            for (int i = 0; i < before.Length; i++)
            {
                if (!BitEqual(before[i], after[i])) return false;
            }
            return true;
        }

        /// <summary>
        /// t96-F3：note 按模式拆分。覆盖采样模式写采样口径，单次测量路径才写单帧单姿态。
        /// </summary>
        public static string BuildModeNote(bool coverageMode, int samples, int validSamples)
        {
            if (!coverageMode) return ModeSingleNote;
            return ModeCoverageNote + "；采样 " + samples.ToString(CultureInfo.InvariantCulture)
                + " 次、有效样本 " + validSamples.ToString(CultureInfo.InvariantCulture) + " 个";
        }

        /// <summary>
        /// T94 §4.1 / §5：六格覆盖校验（纯逻辑，可对固定输入复算）。
        /// 规则：任一格出现稳定反例（顶点确实移动 + A0==A1 逐位 + 物理边未变）→ 整体 FAIL；
        /// 任一格有效样本不足 → UNKNOWN（不许算通过）；六格都达标且无反例 → PASS。
        /// 读不到/不稳的样本不计有效样本，只让该格停在 UNKNOWN。
        /// 判定顺序：反例（pose 稳定但表面不随动/撕裂）→ FAIL 短路；有效样本不足 → UNKNOWN；
        /// epsilon 未指定 → UNKNOWN/EPSILON_UNSPECIFIED（不许自选阈值）；某格无正证据 → UNKNOWN；
        /// 六格都有正证据 → PASS。
        /// </summary>
        public static Bug1GateCoverageReport EvaluateCoverage(
            IList<Bug1GateCellSample> samples, int minSamples, double epsilonU)
        {
            var report = new Bug1GateCoverageReport
            {
                EvidenceRevision = EvidenceRevision,
                MinSamples = minSamples > 0 ? minSamples : MinSamplesPerCellDefault,
                Status = StatusUnknown,
                Reason = ReasonSamplesInsufficient,
            };
            for (int i = 0; i < AllCells.Length; i++)
            {
                report.Cells.Add(new Bug1GateCellState { Cell = AllCells[i], State = StatusUnknown });
            }
            if (samples != null)
            {
                for (int i = 0; i < samples.Count; i++)
                {
                    Bug1GateCellSample sample = samples[i];
                    if (ReferenceEquals(sample, null)) continue;
                    Bug1GateCellState state = FindCellState(report, sample.Cell);
                    if (ReferenceEquals(state, null)) continue;
                    state.Samples++;
                    // R17-02：cells[] 必须能复核入格依据——记录真实 stance 与实测 yaw。
                    string stance = string.IsNullOrEmpty(sample.Stance) ? "unknown" : sample.Stance;
                    if (!state.Stances.Contains(stance)) state.Stances.Add(stance);
                    if (!double.IsNaN(sample.MeasuredYawDeg))
                    {
                        if (double.IsNaN(state.MinYawDeg) || sample.MeasuredYawDeg < state.MinYawDeg)
                            state.MinYawDeg = sample.MeasuredYawDeg;
                        if (double.IsNaN(state.MaxYawDeg) || sample.MeasuredYawDeg > state.MaxYawDeg)
                            state.MaxYawDeg = sample.MeasuredYawDeg;
                    }
                    if (!sample.Available)
                    {
                        if (string.IsNullOrEmpty(state.Detail)) state.Detail = "unavailable:" + sample.Detail;
                        continue;
                    }
                    state.Valid++;
                    bool observed = sample.PoseEqual && sample.InvarianceOk;
                    if (observed && (sample.Torn || !sample.VertexMoved))
                    {
                        // t94 §4.1：pose 稳定、物理门未变，但表面不随动/撕裂 → 反例，否定该 profile。
                        state.Counterexamples++;
                        state.Detail = "counterexample:" + sample.SampleId + "|" + sample.Detail;
                    }
                    else if (observed && sample.VertexMoved && sample.EpsilonSpecified
                        && sample.MaxResidualU <= epsilonU)
                    {
                        state.Positive++;
                    }
                }
            }
            for (int i = 0; i < report.Cells.Count; i++)
            {
                if (report.Cells[i].Counterexamples > 0)
                {
                    report.Status = StatusFail;
                    report.Reason = ReasonPoseCounterexample;
                    report.Detail = report.Cells[i].Cell + ":" + report.Cells[i].Detail;
                    return report;
                }
            }
            var missing = new List<string>();
            var unspecified = new List<string>();
            var noPositive = new List<string>();
            for (int i = 0; i < report.Cells.Count; i++)
            {
                Bug1GateCellState state = report.Cells[i];
                if (state.Valid < report.MinSamples)
                {
                    state.State = StatusUnknown;
                    missing.Add(state.Cell + "(" + state.Valid.ToString(CultureInfo.InvariantCulture)
                        + "/" + report.MinSamples.ToString(CultureInfo.InvariantCulture) + ")");
                }
                else if (state.Positive == 0)
                {
                    state.State = StatusUnknown;
                    if (epsilonU <= 0d) unspecified.Add(state.Cell);
                    else noPositive.Add(state.Cell);
                }
                else
                {
                    state.State = StatusPass;
                }
            }
            if (missing.Count > 0)
            {
                report.Status = StatusUnknown;
                report.Reason = ReasonSamplesInsufficient;
                report.Detail = string.Join(",", missing.ToArray());
                return report;
            }
            if (unspecified.Count > 0)
            {
                report.Status = StatusUnknown;
                report.Reason = ReasonEpsilonUnspecified;
                report.Detail = "epsilon 未指定（t76 §2.2 无阈值）: " + string.Join(",", unspecified.ToArray());
                return report;
            }
            if (noPositive.Count > 0)
            {
                report.Status = StatusUnknown;
                report.Reason = ReasonNoPositiveEvidence;
                report.Detail = string.Join(",", noPositive.ToArray());
                return report;
            }
            report.Status = StatusPass;
            report.Reason = ReasonNone;
            report.Detail = string.Empty;
            return report;
        }

        private static Bug1GateCellState FindCellState(Bug1GateCoverageReport report, string cell)
        {
            for (int i = 0; i < report.Cells.Count; i++)
            {
                if (string.Equals(report.Cells[i].Cell, cell, StringComparison.Ordinal)) return report.Cells[i];
            }
            return null;
        }

        /// <summary>T94 §4.1：六格覆盖汇总的 JSON 片段（GATE.json 内嵌）。</summary>
        public static string BuildCoverageJson(Bug1GateCoverageReport report)
        {
            if (ReferenceEquals(report, null)) return "{}";
            var sb = new StringBuilder(512);
            sb.Append('{');
            sb.Append("\"g_evidence_revision\":").Append(Quote(report.EvidenceRevision)).Append(',');
            sb.Append("\"coverage_status\":").Append(Quote(report.Status)).Append(',');
            sb.Append("\"coverage_reason\":").Append(Quote(report.Reason)).Append(',');
            sb.Append("\"min_samples_per_cell\":").Append(report.MinSamples.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"coverage_detail\":").Append(Quote(report.Detail)).Append(',');
            sb.Append("\"cells\":[");
            for (int i = 0; i < report.Cells.Count; i++)
            {
                if (i > 0) sb.Append(',');
                Bug1GateCellState cell = report.Cells[i];
                sb.Append('{');
                sb.Append("\"cell\":").Append(Quote(cell.Cell)).Append(',');
                sb.Append("\"samples\":").Append(cell.Samples.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"valid\":").Append(cell.Valid.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"counterexamples\":").Append(cell.Counterexamples.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"positive\":").Append(cell.Positive.ToString(CultureInfo.InvariantCulture)).Append(',');
                // R17-02：入格依据可复核——真实 stance 集合与实测 yaw 区间。
                sb.Append("\"stances\":[");
                for (int s = 0; s < cell.Stances.Count; s++)
                {
                    if (s > 0) sb.Append(',');
                    sb.Append(Quote(cell.Stances[s]));
                }
                sb.Append("],");
                sb.Append("\"measured_yaw_deg_min\":").Append(NumberOrNull(cell.MinYawDeg)).Append(',');
                sb.Append("\"measured_yaw_deg_max\":").Append(NumberOrNull(cell.MaxYawDeg)).Append(',');
                sb.Append("\"state\":").Append(Quote(cell.State)).Append(',');
                sb.Append("\"detail\":").Append(Quote(cell.Detail));
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        public static string ComputeProfileHash(IList<Bug1GateRendererProfile> profiles)
        {
            var builder = new StringBuilder(512);
            if (profiles != null)
            {
                for (int i = 0; i < profiles.Count; i++)
                {
                    Bug1GateRendererProfile profile = profiles[i];
                    if (ReferenceEquals(profile, null)) continue;
                    builder.Append("path=").Append(profile.Path ?? string.Empty).Append('\n');
                    builder.Append("mesh=").Append(profile.MeshName ?? string.Empty).Append('\n');
                    builder.Append("bones=").Append(profile.BoneCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
                    builder.Append("rootbone=").Append(profile.RootBonePath ?? string.Empty).Append('\n');
                    builder.Append("materials=");
                    if (profile.MaterialNames != null)
                    {
                        for (int m = 0; m < profile.MaterialNames.Count; m++)
                        {
                            if (m > 0) builder.Append('|');
                            builder.Append(profile.MaterialNames[m] ?? string.Empty);
                        }
                    }
                    builder.Append('\n');
                    builder.Append("colliders=");
                    if (profile.ColliderTypes != null)
                    {
                        for (int c = 0; c < profile.ColliderTypes.Count; c++)
                        {
                            if (c > 0) builder.Append('|');
                            builder.Append(profile.ColliderTypes[c] ?? string.Empty);
                        }
                    }
                    builder.Append("\n--\n");
                }
            }
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
                var hex = new StringBuilder(digest.Length * 2);
                for (int i = 0; i < digest.Length; i++) hex.Append(digest[i].ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }

        /// <summary>结构化 JSON；字段集是契约的一部分，缺字段即测试红。</summary>
        public static string BuildJson(Bug1GateResult result, Bug1GateCoverageReport coverage = null)
        {
            if (result == null) return "{}";
            var sb = new StringBuilder(2048);
            sb.Append('{');
            sb.Append("\"gate_result\":").Append(Quote(result.Status)).Append(',');
            sb.Append("\"reason\":").Append(Quote(result.Reason)).Append(',');
            sb.Append("\"run_id\":").Append(Quote(result.RunId)).Append(',');
            sb.Append("\"target\":").Append(Quote(result.TargetName)).Append(',');
            sb.Append("\"runtime_measured\":").Append(result.RuntimeMeasured ? "true" : "false").Append(',');
            sb.Append("\"offset_u\":").Append(Number(result.OffsetU)).Append(',');
            sb.Append("\"epsilon_u\":").Append(Number(result.EpsilonU)).Append(',');
            sb.Append("\"epsilon_source\":").Append(Quote(result.EpsilonSource)).Append(',');
            sb.Append("\"sample_count\":").Append(result.SampleCount.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"renderer_count\":").Append(result.RendererCount.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"max_residual_u\":").Append(Number(result.MaxResidualU)).Append(',');
            sb.Append("\"max_deviation_ratio\":").Append(Number(result.MaxDeviationRatio)).Append(',');
            sb.Append("\"displacement_p10_u\":").Append(Number(result.P10U)).Append(',');
            sb.Append("\"displacement_p50_u\":").Append(Number(result.P50U)).Append(',');
            sb.Append("\"displacement_p90_u\":").Append(Number(result.P90U)).Append(',');
            sb.Append("\"displacement_median_u\":").Append(Number(result.MedianU)).Append(',');
            sb.Append("\"deviation_ratio\":").Append(Number(result.DeviationRatio)).Append(',');
            sb.Append("\"pure_node\":").Append(result.PureNode ? "true" : "false").Append(',');
            sb.Append("\"restore_exact\":").Append(result.RestoreExact ? "true" : "false").Append(',');
            sb.Append("\"surface_restored\":").Append(result.SurfaceRestored ? "true" : "false").Append(',');
            sb.Append("\"collider_available\":").Append(result.ColliderAvailable ? "true" : "false").Append(',');
            sb.Append("\"raycast_available\":").Append(result.RaycastAvailable ? "true" : "false").Append(',');
            sb.Append("\"colliders_unchanged\":").Append(result.CollidersUnchanged ? "true" : "false").Append(',');
            sb.Append("\"raycast_unchanged\":").Append(result.RaycastUnchanged ? "true" : "false").Append(',');
            // T92-F1：三条物理边逐条导出，便于事后核对是哪一条被动了。
            sb.Append("\"transforms_available\":").Append(result.TransformsAvailable ? "true" : "false").Append(',');
            sb.Append("\"root_unchanged\":").Append(result.RootUnchanged ? "true" : "false").Append(',');
            sb.Append("\"third_unchanged\":").Append(result.ThirdUnchanged ? "true" : "false").Append(',');
            sb.Append("\"skeleton_unchanged\":").Append(result.SkeletonUnchanged ? "true" : "false").Append(',');
            sb.Append("\"profile_hash\":").Append(Quote(result.ProfileHash)).Append(',');
            sb.Append("\"root_before\":").Append(TransformJson(result.RootBefore)).Append(',');
            sb.Append("\"root_after\":").Append(TransformJson(result.RootAfter)).Append(',');
            sb.Append("\"third_before\":").Append(TransformJson(result.ThirdBefore)).Append(',');
            sb.Append("\"third_after\":").Append(TransformJson(result.ThirdAfter)).Append(',');
            sb.Append("\"skeleton_before\":").Append(TransformJson(result.SkeletonBefore)).Append(',');
            sb.Append("\"skeleton_after\":").Append(TransformJson(result.SkeletonAfter)).Append(',');
            sb.Append("\"collider_before\":").Append(CollidersJson(result.ColliderBefore)).Append(',');
            sb.Append("\"collider_after\":").Append(CollidersJson(result.ColliderAfter)).Append(',');
            sb.Append("\"raycast_before\":").Append(RaycastJson(result.RaycastBefore)).Append(',');
            sb.Append("\"raycast_after\":").Append(RaycastJson(result.RaycastAfter)).Append(',');
            sb.Append("\"renderers\":").Append(RenderersJson(result.Renderers)).Append(',');
            sb.Append("\"excluded_renderers\":").Append(ExcludedJson(result.ExcludedRenderers)).Append(',');
            sb.Append("\"notes\":").Append(StringArrayJson(result.Notes)).Append(',');
            sb.Append("\"failure_detail\":").Append(Quote(result.FailureDetail));
            if (!ReferenceEquals(coverage, null))
            {
                // t94 §4.1：六格覆盖汇总内嵌同一份 GATE.json，带 g_evidence_revision。
                sb.Append(",\"pose_coverage\":").Append(BuildCoverageJson(coverage));
            }
            sb.Append('}');
            return sb.ToString();
        }

        private static string RenderersJson(List<Bug1GateRendererStats> renderers)
        {
            if (renderers == null) return "[]";
            var sb = new StringBuilder(256);
            sb.Append('[');
            for (int i = 0; i < renderers.Count; i++)
            {
                if (i > 0) sb.Append(',');
                Bug1GateRendererStats r = renderers[i];
                sb.Append('{');
                sb.Append("\"path\":").Append(Quote(r.Path)).Append(',');
                sb.Append("\"mesh\":").Append(Quote(r.MeshName)).Append(',');
                sb.Append("\"bones\":").Append(r.BoneCount.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"root_bone\":").Append(Quote(r.RootBonePath)).Append(',');
                sb.Append("\"samples\":").Append(r.SampleCount.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append("\"max_residual_u\":").Append(Number(r.MaxResidualU)).Append(',');
                sb.Append("\"max_deviation_ratio\":").Append(Number(r.MaxDeviationRatio)).Append(',');
                sb.Append("\"max_displacement_u\":").Append(Number(r.MaxDisplacementU)).Append(',');
                sb.Append("\"p10_u\":").Append(Number(r.P10)).Append(',');
                sb.Append("\"p50_u\":").Append(Number(r.P50)).Append(',');
                sb.Append("\"p90_u\":").Append(Number(r.P90));
                sb.Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string ExcludedJson(List<Bug1GateRendererAudit> excluded)
        {
            if (excluded == null) return "[]";
            var sb = new StringBuilder(128);
            sb.Append('[');
            for (int i = 0; i < excluded.Count; i++)
            {
                if (i > 0) sb.Append(',');
                Bug1GateRendererAudit e = excluded[i];
                sb.Append('{');
                sb.Append("\"path\":").Append(Quote(e.Path)).Append(',');
                sb.Append("\"reason\":").Append(Quote(e.Reason));
                sb.Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string TransformJson(Bug1GateTransformSnapshot t)
        {
            if (t == null) return "null";
            var sb = new StringBuilder(96);
            sb.Append('{');
            sb.Append("\"available\":").Append(t.Available ? "true" : "false").Append(',');
            sb.Append("\"position\":[").Append(Number(t.Px)).Append(',').Append(Number(t.Py)).Append(',').Append(Number(t.Pz)).Append("],");
            sb.Append("\"rotation\":[").Append(Number(t.Qx)).Append(',').Append(Number(t.Qy)).Append(',')
                .Append(Number(t.Qz)).Append(',').Append(Number(t.Qw)).Append(']');
            sb.Append('}');
            return sb.ToString();
        }

        private static string CollidersJson(List<Bug1GateColliderSnapshot> colliders)
        {
            if (colliders == null) return "[]";
            var sb = new StringBuilder(128);
            sb.Append('[');
            for (int i = 0; i < colliders.Count; i++)
            {
                if (i > 0) sb.Append(',');
                Bug1GateColliderSnapshot c = colliders[i];
                sb.Append('{');
                sb.Append("\"name\":").Append(Quote(c.Name)).Append(',');
                sb.Append("\"type\":").Append(Quote(c.Type)).Append(',');
                sb.Append("\"enabled\":").Append(c.Enabled ? "true" : "false").Append(',');
                sb.Append("\"available\":").Append(c.Available ? "true" : "false").Append(',');
                sb.Append("\"center\":[").Append(Number(c.CenterX)).Append(',').Append(Number(c.CenterY))
                    .Append(',').Append(Number(c.CenterZ)).Append("],");
                sb.Append("\"size\":[").Append(Number(c.SizeX)).Append(',').Append(Number(c.SizeY))
                    .Append(',').Append(Number(c.SizeZ)).Append(']');
                sb.Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string RaycastJson(Bug1GateRaycastSnapshot raycast)
        {
            if (raycast == null) return "null";
            return "{\"hit\":" + Quote(raycast.HitName)
                + ",\"distance\":" + Number(raycast.Distance)
                + ",\"available\":" + (raycast.Available ? "true" : "false") + "}";
        }

        private static string StringArrayJson(List<string> values)
        {
            if (values == null) return "[]";
            var sb = new StringBuilder(64);
            sb.Append('[');
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Quote(values[i]));
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string Number(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return "-1";
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string NumberOrNull(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return "null";
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string Quote(string value)
        {
            if (string.IsNullOrEmpty(value)) return "\"\"";
            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        // ---- R21-01（t110）：重试预算按触发源分别计数 ----

        /// <summary>
        /// R21-02（t114）：触发源用枚举，不用字符串。
        /// 字符串版本一旦拼错（例如 "level_loaded"）不会编译报错，只会在运行期被判成未知源并静默 GIVEUP，
        /// 探针在真机上停摆而离线套件全绿——枚举把这类漂移挡在编译期。
        /// </summary>
        public enum Bug1RetryTrigger
        {
            /// <summary>关卡载入，发生在 listen host 就绪之前。</summary>
            LevelLoaded = 0,

            /// <summary>本地玩家创建，listen host 就绪之后才会到。</summary>
            PlayerCreated = 1,
        }

        /// <summary>仅用于日志/产物展示，不参与任何记账判定。</summary>
        public static string TriggerName(Bug1RetryTrigger trigger)
        {
            switch (trigger)
            {
                case Bug1RetryTrigger.LevelLoaded: return "level-loaded";
                case Bug1RetryTrigger.PlayerCreated: return "player-created";
                default: return "unknown-trigger:" + ((int)trigger).ToString();
            }
        }

        public const string RetryReasonRetry = "RETRY";
        public const string RetryReasonGiveUp = "RETRY_GIVEUP";
        public const string RetryReasonUnknownTrigger = "RETRY_TRIGGER_UNKNOWN";

        /// <summary>
        /// 每个触发源各自的已用次数。两个字段**互不相干**：level-loaded 在托管就绪前连续拒绝，
        /// 不得吃掉 player-created 的额度（t110 实机缺陷 R21-01 的根因）。
        /// </summary>
        internal sealed class Bug1RetryBudgetState
        {
            public int LevelLoaded;
            public int PlayerCreated;
        }

        internal struct Bug1RetryDecision
        {
            public bool Retry;
            public bool GiveUp;
            public int Used;
            public string Trigger;
            public string Reason;
        }

        /// <summary>
        /// 纯判定：按触发源消费一次重试预算。
        /// 预算耗尽返回 GiveUp（保留「该源耗尽就放弃」的语义，不改成永远重试）；
        /// 未知触发源直接 GiveUp——不猜、也不共享别人的额度。
        /// </summary>
        public static Bug1RetryDecision DecideRetry(
            Bug1RetryBudgetState state, Bug1RetryTrigger trigger, int maxPerTrigger)
        {
            var decision = new Bug1RetryDecision
            {
                Trigger = TriggerName(trigger),
                Reason = RetryReasonGiveUp,
                GiveUp = true,
            };
            if (ReferenceEquals(state, null)) return decision;

            // R21-02：按枚举分派，不存在「字符串拼错 → 落到未知分支」的运行期路径。
            int used;
            switch (trigger)
            {
                case Bug1RetryTrigger.LevelLoaded:
                    used = state.LevelLoaded;
                    break;
                case Bug1RetryTrigger.PlayerCreated:
                    used = state.PlayerCreated;
                    break;
                default:
                    // 只有强转出来的非法枚举值会到这里；不猜、也不共享别人的额度。
                    decision.Reason = RetryReasonUnknownTrigger;
                    return decision;
            }

            decision.Used = used;
            if (maxPerTrigger <= 0 || used >= maxPerTrigger) return decision;

            decision.Used = used + 1;
            decision.Retry = true;
            decision.GiveUp = false;
            decision.Reason = RetryReasonRetry;
            if (trigger == Bug1RetryTrigger.LevelLoaded) state.LevelLoaded = decision.Used;
            else state.PlayerCreated = decision.Used;
            return decision;
        }
    }

    internal sealed class Bug1GateDisplacementStats
    {
        public int Count;
        public double P10;
        public double P50;
        public double P90;
        public double Median;
        public double Mean;
    }

    /// <summary>逐 renderer 的统计：max 进判定，P50/P90 只作报告（T86-F1）。</summary>
    internal sealed class Bug1GateRendererStats
    {
        public string Path = string.Empty;
        public string MeshName = string.Empty;
        public int BoneCount;
        public string RootBonePath = string.Empty;
        public int SampleCount;
        public double MaxResidualU;
        public double MaxDeviationRatio;
        public double MaxDisplacementU;
        public double P10;
        public double P50;
        public double P90;
    }

    /// <summary>T86-F5：profile 指纹的输入（层级路径/mesh/材质/骨骼/碰撞体）。</summary>
    internal sealed class Bug1GateRendererProfile
    {
        public string Path = string.Empty;
        public string MeshName = string.Empty;
        public int BoneCount;
        public string RootBonePath = string.Empty;
        public readonly List<string> MaterialNames = new List<string>();
        public readonly List<string> ColliderTypes = new List<string>();
    }

    /// <summary>T86-F3：被排除的 renderer 与原因，写入 GATE.json 供审计。</summary>
    internal sealed class Bug1GateRendererAudit
    {
        public string Path = string.Empty;
        public string Reason = string.Empty;
    }

    internal sealed class Bug1GateTransformSnapshot
    {
        public bool Available;
        public double Px, Py, Pz;
        public double Qx, Qy, Qz, Qw;
    }

    internal sealed class Bug1GateColliderSnapshot
    {
        public string Name = string.Empty;
        public string Type = string.Empty;
        public bool Enabled;
        public bool Available;
        public double CenterX, CenterY, CenterZ;
        public double SizeX, SizeY, SizeZ;
    }

    internal sealed class Bug1GateRaycastSnapshot
    {
        public bool Available;
        public string HitName = "none";
        public double Distance = -1d;
    }

    /// <summary>
    /// T92-F1：物理侧不变性输入。root / Third / Skeleton 三条边必须逐条判定，
    /// 任一条被动了都要 FAIL 并给出可区分的原因码——不允许只比其中两条。
    /// </summary>
    internal sealed class Bug1GateInvariance
    {
        public bool TransformsAvailable = true;
        public bool RootUnchanged = true;
        public bool ThirdUnchanged = true;
        public bool SkeletonUnchanged = true;
        public bool CollidersAvailable = true;
        public bool CollidersUnchanged = true;
        public bool RaycastAvailable = true;
        public bool RaycastUnchanged = true;
    }

    internal sealed class Bug1GateResult
    {
        public string RunId = string.Empty;
        public string TargetName = string.Empty;
        public string Status = Bug1GateProbeLogic.StatusUnknown;
        public string Reason = Bug1GateProbeLogic.ReasonNotMeasured;
        public bool RuntimeMeasured;
        public bool PureNode;
        public bool RestoreExact;
        public bool SurfaceRestored;
        public bool ColliderAvailable;
        public bool RaycastAvailable;
        public bool CollidersUnchanged;
        public bool RaycastUnchanged;
        public bool TransformsAvailable;
        public bool RootUnchanged;
        public bool ThirdUnchanged;
        public bool SkeletonUnchanged;
        public double OffsetU = Bug1GateProbeLogic.DefaultOffsetU;
        public double EpsilonU = Bug1GateProbeLogic.UnspecifiedEpsilonU;
        public string EpsilonSource = Bug1GateProbeLogic.EpsilonSourceUnspecified;
        public int SampleCount;
        public int RendererCount;
        public double MaxResidualU = -1d;
        public double MaxDeviationRatio = -1d;
        public double MedianU;
        public double P10U;
        public double P50U;
        public double P90U;
        public double DeviationRatio = -1d;
        public string ProfileHash = string.Empty;
        public Bug1GateTransformSnapshot RootBefore;
        public Bug1GateTransformSnapshot RootAfter;
        public Bug1GateTransformSnapshot ThirdBefore;
        public Bug1GateTransformSnapshot ThirdAfter;
        public Bug1GateTransformSnapshot SkeletonBefore;
        public Bug1GateTransformSnapshot SkeletonAfter;
        public readonly List<Bug1GateColliderSnapshot> ColliderBefore = new List<Bug1GateColliderSnapshot>();
        public readonly List<Bug1GateColliderSnapshot> ColliderAfter = new List<Bug1GateColliderSnapshot>();
        public Bug1GateRaycastSnapshot RaycastBefore = new Bug1GateRaycastSnapshot();
        public Bug1GateRaycastSnapshot RaycastAfter = new Bug1GateRaycastSnapshot();
        public readonly List<Bug1GateRendererStats> Renderers = new List<Bug1GateRendererStats>();
        public readonly List<Bug1GateRendererAudit> ExcludedRenderers = new List<Bug1GateRendererAudit>();
        public readonly List<string> Notes = new List<string>();
        public string FailureDetail = string.Empty;
    }

    /// <summary>T94 §3.1：姿态节点计划项（路径 + 角色），由纯逻辑去重后交给生产侧读值。</summary>
    internal sealed class Bug1PoseNodePlan
    {
        public string Path = string.Empty;
        public string Role = string.Empty;
    }

    /// <summary>
    /// T94 §3.1：单个姿态节点。Local = localPosition(3) + localRotation(4) + localScale(3)；
    /// World = localToWorldMatrix 的 16 个 float。全部按 float32 位比较，不做 epsilon。
    /// </summary>
    internal sealed class Bug1PoseEntry
    {
        public string Path = string.Empty;
        public string Role = string.Empty;
        public bool HasLocal;
        public bool HasWorld;
        public float[] Local = new float[Bug1GateProbeLogic.PoseLocalFieldCount];
        public float[] World = new float[Bug1GateProbeLogic.PoseWorldFieldCount];
    }

    /// <summary>T94 §3.2：不可读/非有限/集合缺失 → Available=false，由调用方判 UNKNOWN。</summary>
    internal sealed class Bug1PoseSnapshot
    {
        public bool Available = true;
        public string UnavailableReason = string.Empty;
        public readonly List<Bug1PoseEntry> Entries = new List<Bug1PoseEntry>();
    }

    /// <summary>
    /// T94 §5：一次同帧采样的结论。反例口径按 t94 §4.1：
    /// 观测有效（pose 位级不变、物理门未变）但**表面不随动/撕裂** → 否定最小路线。
    /// 正证据 = 表面确实随动 且 pose 不变 且物理门未变 且残差在已指定 epsilon 内。
    /// </summary>
    internal sealed class Bug1GateCellSample
    {
        public string Cell = string.Empty;
        public string SampleId = string.Empty;
        public bool Available = true;
        public bool VertexMoved;
        public bool Torn;
        public bool PoseEqual;
        public bool InvarianceOk;
        public bool EpsilonSpecified;
        public double MaxResidualU;
        /// <summary>R17-02：本次采样的真实 stance 枚举名（EPlayerStance.ToString()）。</summary>
        public string Stance = string.Empty;
        /// <summary>R17-02：实测 yaw（度，0° = 面向 +Z）；不可读时为 NaN。</summary>
        public double MeasuredYawDeg = double.NaN;
        public string Detail = string.Empty;
    }

    internal sealed class Bug1GateCellState
    {
        public string Cell = string.Empty;
        public int Samples;
        public int Valid;
        public int Positive;
        public int Counterexamples;
        public string State = Bug1GateProbeLogic.StatusUnknown;
        public string Detail = string.Empty;
        /// <summary>该格实际出现过的 stance 枚举名（去重保序）。</summary>
        public readonly List<string> Stances = new List<string>();
        public double MinYawDeg = double.NaN;
        public double MaxYawDeg = double.NaN;
    }

    internal sealed class Bug1GateCoverageReport
    {
        public string EvidenceRevision = Bug1GateProbeLogic.EvidenceRevision;
        public string Status = Bug1GateProbeLogic.StatusUnknown;
        public string Reason = Bug1GateProbeLogic.ReasonSamplesInsufficient;
        public int MinSamples = Bug1GateProbeLogic.MinSamplesPerCellDefault;
        public string Detail = string.Empty;
        public readonly List<Bug1GateCellState> Cells = new List<Bug1GateCellState>();
    }
}
