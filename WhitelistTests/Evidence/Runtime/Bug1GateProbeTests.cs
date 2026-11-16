using BepInEx.Configuration;
using SteamP2PFriends.Platform.Diagnostics.Patches;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// t85/t90：G 门探针里**能离线判定**的部分。
    /// 真实蒙皮位移 / BakeMesh / Collider / Raycast 只能在 1H+1G 实机跑，离线一律 UNKNOWN——
    /// 这里有一条用例专门把「离线不得 PASS」钉死，另有一条 IL 用例把「生产必须调用下沉后的比较函数」钉死。
    /// </summary>
    internal static class Bug1GateProbeTests
    {
        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Bug1Gate: " + message);
        }

        /// <summary>配置默认值必须与契约一致：关闭 / 0.10U / 64 点 / epsilon 未指定，且节名独立。</summary>
        public static void Test_ConfigDefaultsAreOffAndTenCentimetres()
        {
            string dir = Path.Combine(Path.GetTempPath(), "spf-t90-cfg-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);
                var file = new ConfigFile(Path.Combine(dir, "Bug1Gate.cfg"), false);
                ConfigEntry<bool> enabled;
                ConfigEntry<string> target;
                ConfigEntry<float> offset;
                ConfigEntry<int> samples;
                ConfigEntry<float> maxDeviation;
                Bug1GateProbe.BindConfig(file, out enabled, out target, out offset, out samples, out maxDeviation);

                Assert(Bug1GateProbeLogic.Section == "Bug1Gate", "节名必须是独立的 Bug1Gate，实际 " + Bug1GateProbeLogic.Section);
                Assert(!enabled.Value, "G 门探针必须默认关闭");
                Assert(target.Value == string.Empty, "TargetPlayer 默认必须为空（唯一远端玩家）");
                Assert(Math.Abs(offset.Value - 0.10f) < 1e-6f, "OffsetU 默认必须是 0.10U，实际 " + offset.Value);
                Assert(samples.Value == 64, "Samples 默认必须是 64，实际 " + samples.Value);
                Assert(Math.Abs(maxDeviation.Value) < 1e-9f,
                    "MaxDeviationU 默认必须是 0（= 未指定 epsilon，只判 UNKNOWN），实际 " + maxDeviation.Value);

                ConfigEntry<bool> enabled2;
                ConfigEntry<string> target2;
                ConfigEntry<float> offset2;
                ConfigEntry<int> samples2;
                ConfigEntry<float> maxDeviation2;
                Bug1GateProbe.BindConfig(file, out enabled2, out target2, out offset2, out samples2, out maxDeviation2);
                Assert(ReferenceEquals(enabled, enabled2), "重复绑定必须复用同一 ConfigEntry（同一节同一键）");
                Assert(ReferenceEquals(target, target2), "重复绑定必须复用同一 ConfigEntry（同一节同一键）");
                Assert(ReferenceEquals(offset, offset2), "重复绑定必须复用同一 ConfigEntry（同一节同一键）");
                Assert(ReferenceEquals(samples, samples2), "重复绑定必须复用同一 ConfigEntry（同一节同一键）");
                Assert(ReferenceEquals(maxDeviation, maxDeviation2), "重复绑定必须复用同一 ConfigEntry（同一节同一键）");
            }
            finally
            {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
            }
        }

        /// <summary>目标解析复用 Bug1Probe 的 selector 语义：排除自己、空选择器要求唯一远端、歧义报码。</summary>
        public static void Test_TargetSelectionReusesSelectorSemantics()
        {
            var clients = new List<Bug1ClientSnapshot>
            {
                Snap(100UL, "HostSelf", 0),
                Snap(200UL, "Alice", 1),
            };

            Bug1TargetSelection unique = Bug1ProbeTargetSelector.Select(clients, string.Empty, 100UL);
            Assert(unique.Status == Bug1TargetSelectionStatus.Selected, "只有一名远端时必须选中");
            Assert(unique.SelectedIndex == 1, "必须选中远端（不能选自己），实际 index=" + unique.SelectedIndex);
            Assert(Bug1GateProbeLogic.MapSelectionError(unique) == null, "Selected 不应产生错误码");

            var twoRemotes = new List<Bug1ClientSnapshot>
            {
                Snap(100UL, "HostSelf", 0),
                Snap(200UL, "Alice", 1),
                Snap(300UL, "Bob", 2),
            };
            Bug1TargetSelection ambiguous = Bug1ProbeTargetSelector.Select(twoRemotes, string.Empty, 100UL);
            Assert(ambiguous.Status == Bug1TargetSelectionStatus.Ambiguous, "两名远端且未指定时必须歧义");
            Assert(Bug1GateProbeLogic.MapSelectionError(ambiguous) == "TARGET_PLAYER_AMBIGUOUS",
                "歧义必须映射到 TARGET_PLAYER_AMBIGUOUS，实际 " + Bug1GateProbeLogic.MapSelectionError(ambiguous));

            Bug1TargetSelection byName = Bug1ProbeTargetSelector.Select(twoRemotes, "Alice", 100UL);
            Assert(byName.Status == Bug1TargetSelectionStatus.Selected && byName.SteamId == 200UL,
                "按名字必须选中 Alice");

            Bug1TargetSelection byId = Bug1ProbeTargetSelector.Select(twoRemotes, "300", 100UL);
            Assert(byId.Status == Bug1TargetSelectionStatus.Selected && byId.SteamId == 300UL,
                "按 SteamID64 必须选中 Bob");

            Bug1TargetSelection missing = Bug1ProbeTargetSelector.Select(twoRemotes, "Nobody", 100UL);
            Assert(missing.Status == Bug1TargetSelectionStatus.NotFound, "匹配不到必须是 NotFound");
            Assert(Bug1GateProbeLogic.MapSelectionError(missing) == "TARGET_PLAYER_NOT_FOUND",
                "NotFound 必须映射到 TARGET_PLAYER_NOT_FOUND，实际 " + Bug1GateProbeLogic.MapSelectionError(missing));

            var withNullId = new List<Bug1ClientSnapshot>
            {
                new Bug1ClientSnapshot { HasPlayerId = false, SourceIndex = 0 },
                Snap(200UL, "Alice", 1),
            };
            Bug1TargetSelection afterNull = Bug1ProbeTargetSelector.Select(withNullId, string.Empty, 100UL);
            Assert(afterNull.Status == Bug1TargetSelectionStatus.Selected && afterNull.SelectedIndex == 1,
                "null playerID 的 client 必须被跳过，不能中断选择");
        }

        /// <summary>
        /// 原因码映射：未测量 / 非纯节点 / 还原不符 / 快照不可用 / collider / raycast /
        /// 没移动 / 移过头 / 未指定 epsilon / 部分移动 / 通过，逐分支钉住。
        /// </summary>
        public static void Test_ReasonCodeMapping()
        {
            List<Bug1GateRendererStats> good = Renderers(0.10d, 0.10d, 0.0d);
            List<Bug1GateRendererStats> still = Renderers(0.10d, 0.0d, 0.10d);
            List<Bug1GateRendererStats> tooFar = Renderers(0.10d, 0.30d, 0.0d);
            List<Bug1GateRendererStats> partial = Renderers(0.10d, 0.10d, 0.02d);

            AssertDecide(false, true, true, true, true, true, true, true, good, 0.001d,
                Bug1GateProbeLogic.StatusUnknown, Bug1GateProbeLogic.ReasonNotMeasured, "未测量");
            AssertDecide(true, false, true, true, true, true, true, true, good, 0.001d,
                Bug1GateProbeLogic.StatusFail, Bug1GateProbeLogic.ReasonImpureRenderNode, "非纯节点");
            AssertDecide(true, true, false, true, true, true, true, true, good, 0.001d,
                Bug1GateProbeLogic.StatusFail, Bug1GateProbeLogic.ReasonRestoreMismatch, "localPosition 未位级还原");
            AssertDecide(true, true, true, false, true, true, true, true, good, 0.001d,
                Bug1GateProbeLogic.StatusFail, Bug1GateProbeLogic.ReasonRestoreMismatch, "表面未回基线");
            AssertDecide(true, true, true, true, false, true, true, true, good, 0.001d,
                Bug1GateProbeLogic.StatusUnknown, Bug1GateProbeLogic.ReasonColliderUnavailable, "collider 快照不可用");
            AssertDecide(true, true, true, true, true, true, false, true, good, 0.001d,
                Bug1GateProbeLogic.StatusUnknown, Bug1GateProbeLogic.ReasonRaycastUnavailable, "raycast 快照不可用");
            AssertDecide(true, true, true, true, true, false, true, true, good, 0.001d,
                Bug1GateProbeLogic.StatusFail, Bug1GateProbeLogic.ReasonColliderChanged, "collider 变了");
            AssertDecide(true, true, true, true, true, true, true, false, good, 0.001d,
                Bug1GateProbeLogic.StatusFail, Bug1GateProbeLogic.ReasonRaycastChanged, "raycast 变了");
            AssertDecide(true, true, true, true, true, true, true, true, new List<Bug1GateRendererStats>(), 0.001d,
                Bug1GateProbeLogic.StatusFail, Bug1GateProbeLogic.ReasonNoVisibleSurface, "没有可见 renderer");
            AssertDecide(true, true, true, true, true, true, true, true, still, 0.001d,
                Bug1GateProbeLogic.StatusFail, Bug1GateProbeLogic.ReasonSurfaceNotMoved, "表面没跟着动");
            AssertDecide(true, true, true, true, true, true, true, true, tooFar, 0.001d,
                Bug1GateProbeLogic.StatusFail, Bug1GateProbeLogic.ReasonSurfaceMovedTooFar, "移过头");
            AssertDecide(true, true, true, true, true, true, true, true, good, 0d,
                Bug1GateProbeLogic.StatusUnknown, Bug1GateProbeLogic.ReasonEpsilonUnspecified, "epsilon 未指定");
            AssertDecide(true, true, true, true, true, true, true, true, partial, 0.001d,
                Bug1GateProbeLogic.StatusFail, Bug1GateProbeLogic.ReasonPartialSurfaceMoved, "部分点没跟上");
            AssertDecide(true, true, true, true, true, true, true, true, good, 0.001d,
                Bug1GateProbeLogic.StatusPass, Bug1GateProbeLogic.ReasonNone, "全部残差 ≤ epsilon");
        }

        /// <summary>
        /// T86-F1：判定必须逐 renderer 看 max 残差——一个 renderer 完美、另一个撕裂时，
        /// 中位数/P10 口径会 PASS，这里必须 FAIL/PARTIAL_SURFACE_MOVED。
        /// </summary>
        public static void Test_PerRendererMaxResidualDecides()
        {
            var mixed = new List<Bug1GateRendererStats>
            {
                Renderer("Third/Model_0/Body", 0.10d, 0.10d, 0.0d),
                Renderer("Third/Model_0/Head", 0.10d, 0.10d, 0.04d),
            };
            AssertDecide(true, true, true, true, true, true, true, true, mixed, 0.001d,
                Bug1GateProbeLogic.StatusFail, Bug1GateProbeLogic.ReasonPartialSurfaceMoved,
                "单个 renderer 撕裂必须 FAIL，不能被另一个 renderer 的中位数掩盖");

            // 未指定 epsilon 时，即使两个 renderer 都「看起来在动」，也不许 PASS。
            List<Bug1GateRendererStats> bothMoving = Renderers(0.10d, 0.10d, 0.02d);
            AssertDecide(true, true, true, true, true, true, true, true, bothMoving, 0d,
                Bug1GateProbeLogic.StatusUnknown, Bug1GateProbeLogic.ReasonEpsilonUnspecified,
                "epsilon 未指定时不许宣布通过");

            // 残差大于位移本身 ⇒ 表面没跟着 delta 走。
            List<Bug1GateRendererStats> notFollowing = Renderers(0.10d, 0.005d, 0.10d);
            AssertDecide(true, true, true, true, true, true, true, true, notFollowing, 0.001d,
                Bug1GateProbeLogic.StatusFail, Bug1GateProbeLogic.ReasonSurfaceNotMoved,
                "残差大于位移必须判 SURFACE_NOT_MOVED");
        }

        /// <summary>R16-01：白名单判定下沉为纯函数，放行项与拒绝项逐类钉住。</summary>
        public static void Test_WhitelistRenderComponentTypes()
        {
            string[] allowed =
            {
                "Transform", "Renderer", "SkinnedMeshRenderer", "MeshRenderer",
                "MeshFilter", "Animator", "Animation", "LODGroup",
            };
            for (int i = 0; i < allowed.Length; i++)
            {
                Assert(Bug1GateProbeLogic.IsWhitelistedRenderComponentType(allowed[i]),
                    "白名单必须放行 " + allowed[i]);
            }

            string[] rejected =
            {
                "Cloth", "Rigidbody", "BoxCollider", "CapsuleCollider", "SphereCollider",
                "MeshCollider", "MonoBehaviour", "CharacterController", "Joint", "AudioSource",
                "Camera", "Light", "ParticleSystem", "SteamPlayer", "",
            };
            for (int i = 0; i < rejected.Length; i++)
            {
                Assert(!Bug1GateProbeLogic.IsWhitelistedRenderComponentType(rejected[i]),
                    "白名单必须拒绝 " + (rejected[i] == string.Empty ? "<empty>" : rejected[i]));
            }

            var types = new List<string>
            {
                "Transform", "SkinnedMeshRenderer", "MeshFilter", "Cloth",
                "BoxCollider", "Cloth", "<read-failed:MissingMethodException>",
            };
            List<string> impure = Bug1GateProbeLogic.CollectImpureComponentTypes(types);
            Assert(impure.Count == 3, "去重后应剩 3 类不纯组件，实际 " + impure.Count);
            Assert(impure[0] == "Cloth" && impure[1] == "BoxCollider",
                "顺序必须是首次出现顺序，实际 " + string.Join(",", impure.ToArray()));
            Assert(impure[2].StartsWith("<read-failed:", StringComparison.Ordinal),
                "读失败必须原样保留成可读诊断");
            Assert(Bug1GateProbeLogic.CollectImpureComponentTypes(new List<string>()).Count == 0,
                "空输入必须返回空（纯节点）");
        }

        /// <summary>T86-F2/F4：三个比较函数下沉为纯逻辑，逐字段差异必须被检出，不可用必须 false。</summary>
        public static void Test_SnapshotComparisonsAreReal()
        {
            Bug1GateTransformSnapshot a = Transform(1f, 2f, 3f, 0f, 0f, 0f, 1f);
            Bug1GateTransformSnapshot b = Transform(1f, 2f, 3f, 0f, 0f, 0f, 1f);
            Assert(Bug1GateProbeLogic.TransformsEqual(a, b), "完全相同的 transform 必须相等");

            Bug1GateTransformSnapshot shifted = Transform(1f, 2f, 3.0001f, 0f, 0f, 0f, 1f);
            Assert(!Bug1GateProbeLogic.TransformsEqual(a, shifted), "位置有差异必须不相等（位级比较）");

            Bug1GateTransformSnapshot rotated = Transform(1f, 2f, 3f, 0f, 0.0001f, 0f, 1f);
            Assert(!Bug1GateProbeLogic.TransformsEqual(a, rotated), "旋转有差异必须不相等");

            Bug1GateTransformSnapshot unavailable = Transform(1f, 2f, 3f, 0f, 0f, 0f, 1f);
            unavailable.Available = false;
            Assert(!Bug1GateProbeLogic.TransformsEqual(a, unavailable),
                "快照不可用必须返回 false（fail-closed，不能当成「没变」）");

            List<Bug1GateColliderSnapshot> colliders = Colliders(0.5f);
            Assert(Bug1GateProbeLogic.CollidersEqual(colliders, Colliders(0.5f)), "相同 collider 必须相等");
            Assert(!Bug1GateProbeLogic.CollidersEqual(colliders, Colliders(0.5001f)),
                "collider bounds 有差异必须不相等");
            List<Bug1GateColliderSnapshot> unavailableColliders = Colliders(0.5f);
            unavailableColliders[0].Available = false;
            Assert(!Bug1GateProbeLogic.CollidersEqual(colliders, unavailableColliders),
                "collider 快照不可用必须返回 false");

            Bug1GateRaycastSnapshot hit = Raycast("CapsuleCollider@Player", 1.25d);
            Assert(Bug1GateProbeLogic.RaycastsEqual(hit, Raycast("CapsuleCollider@Player", 1.25d)),
                "相同 raycast 必须相等");
            Assert(!Bug1GateProbeLogic.RaycastsEqual(hit, Raycast("CapsuleCollider@Player", 1.2501d)),
                "raycast 距离有差异必须不相等");
            Assert(!Bug1GateProbeLogic.RaycastsEqual(hit, Raycast("BoxCollider@Player", 1.25d)),
                "raycast 命中体有差异必须不相等");
            Bug1GateRaycastSnapshot missing = Raycast("none", -1d);
            missing.Available = false;
            Assert(!Bug1GateProbeLogic.RaycastsEqual(hit, missing),
                "raycast 快照不可用必须返回 false");
        }

        /// <summary>R16-05：Samples 是硬上限，实际采样点数不得超过它，也不能因为上限小于顶点数就取 0 个。</summary>
        public static void Test_SampleIndicesRespectHardCap()
        {
            Assert(Bug1GateProbeLogic.SelectSampleIndices(0, 64).Count == 0, "0 顶点必须取 0 个样本");
            Assert(Bug1GateProbeLogic.SelectSampleIndices(10, 64).Count == 10, "顶点数小于上限时全取");
            Assert(Bug1GateProbeLogic.SelectSampleIndices(1000, 64).Count <= 64,
                "顶点数大于上限时不得超过上限，实际 " + Bug1GateProbeLogic.SelectSampleIndices(1000, 64).Count);
            Assert(Bug1GateProbeLogic.SelectSampleIndices(1000, 64).Count > 0, "大 mesh 也必须至少取 1 个点");
            Assert(Bug1GateProbeLogic.SelectSampleIndices(65, 64).Count <= 64, "65/64 必须 ≤ 64");
            Assert(Bug1GateProbeLogic.SelectSampleIndices(1, 64).Count == 1, "单顶点必须取到");
        }

        /// <summary>T86-F5：profile 指纹必须随模型/材质/骨骼/碰撞体变化，不能是常量。</summary>
        public static void Test_ProfileHashChangesWithModel()
        {
            List<Bug1GateRendererProfile> baseline = Profiles("Body", "Mesh_A", 12, "Third/Skeleton/Hips", "Mat_A");
            string hash = Bug1GateProbeLogic.ComputeProfileHash(baseline);
            Assert(hash.Length == 64, "SHA256 十六进制必须是 64 字符，实际 " + hash.Length);
            Assert(hash == Bug1GateProbeLogic.ComputeProfileHash(Profiles("Body", "Mesh_A", 12, "Third/Skeleton/Hips", "Mat_A")),
                "同一 profile 必须得到同一指纹");

            Assert(hash != Bug1GateProbeLogic.ComputeProfileHash(Profiles("Body", "Mesh_B", 12, "Third/Skeleton/Hips", "Mat_A")),
                "换 mesh 必须换指纹");
            Assert(hash != Bug1GateProbeLogic.ComputeProfileHash(Profiles("Body", "Mesh_A", 13, "Third/Skeleton/Hips", "Mat_A")),
                "换骨骼数量必须换指纹");
            Assert(hash != Bug1GateProbeLogic.ComputeProfileHash(Profiles("Body", "Mesh_A", 12, "Third/Skeleton/Hips", "Mat_B")),
                "换材质必须换指纹");
            Assert(hash != Bug1GateProbeLogic.ComputeProfileHash(Profiles("Head", "Mesh_A", 12, "Third/Skeleton/Hips", "Mat_A")),
                "换层级路径必须换指纹");
        }

        /// <summary>JSON 字段是契约的一部分：新增的 epsilon/残差/可用性/指纹/renderer 明细都必须出现。</summary>
        public static void Test_JsonContainsAllContractFields()
        {
            var result = new Bug1GateResult
            {
                RunId = "gate-test",
                TargetName = "Alice",
                Status = Bug1GateProbeLogic.StatusUnknown,
                Reason = Bug1GateProbeLogic.ReasonEpsilonUnspecified,
                RuntimeMeasured = true,
                OffsetU = 0.10d,
                EpsilonU = 0d,
                EpsilonSource = Bug1GateProbeLogic.EpsilonSourceUnspecified,
                SampleCount = 32,
                RendererCount = 1,
                MaxResidualU = 0.002d,
                MaxDeviationRatio = 0.02d,
                ProfileHash = "abc",
                RootBefore = Transform(0f, 0f, 0f, 0f, 0f, 0f, 1f),
                RaycastBefore = Raycast("none", -1d),
            };
            result.Renderers.Add(Renderer("Third/Model_0/Body", 0.10d, 0.10d, 0.002d));
            result.ExcludedRenderers.Add(new Bug1GateRendererAudit { Path = "Third/Model_1", Reason = "renderer-disabled" });
            result.ColliderBefore.Add(Colliders(0.5f)[0]);

            string json = Bug1GateProbeLogic.BuildJson(result);
            string[] required =
            {
                "\"gate_result\"", "\"reason\"", "\"run_id\"", "\"target\"", "\"runtime_measured\"",
                "\"offset_u\"", "\"epsilon_u\"", "\"epsilon_source\"", "\"sample_count\"", "\"renderer_count\"",
                "\"max_residual_u\"", "\"max_deviation_ratio\"", "\"displacement_p10_u\"",
                "\"displacement_p50_u\"", "\"displacement_p90_u\"", "\"displacement_median_u\"",
                "\"deviation_ratio\"", "\"pure_node\"", "\"restore_exact\"", "\"surface_restored\"",
                "\"collider_available\"", "\"raycast_available\"", "\"colliders_unchanged\"",
                "\"raycast_unchanged\"", "\"profile_hash\"", "\"root_before\"", "\"root_after\"",
                "\"third_before\"", "\"third_after\"", "\"skeleton_before\"", "\"skeleton_after\"",
                "\"collider_before\"", "\"collider_after\"", "\"raycast_before\"", "\"raycast_after\"",
                "\"renderers\"", "\"excluded_renderers\"", "\"notes\"", "\"failure_detail\"",
                "\"path\"", "\"mesh\"", "\"root_bone\"", "\"samples\"", "\"max_displacement_u\"",
                "\"p10_u\"", "\"p50_u\"", "\"p90_u\"", "\"center\"", "\"size\"", "\"available\"",
            };
            for (int i = 0; i < required.Length; i++)
            {
                Assert(json.IndexOf(required[i], StringComparison.Ordinal) >= 0, "JSON 缺少字段 " + required[i]);
            }
            Assert(json.IndexOf("renderer-disabled", StringComparison.Ordinal) >= 0, "被排除的 renderer 必须写进 JSON");
        }

        /// <summary>分位数与位级判等的基础性质。</summary>
        public static void Test_SummaryQuantiles()
        {
            var magnitudes = new List<double> { 0.0d, 0.1d, 0.2d, 0.3d, 0.4d };
            Bug1GateDisplacementStats stats = Bug1GateProbeLogic.Summarize(magnitudes);
            Assert(stats.Count == 5, "样本数应为 5");
            Assert(Math.Abs(stats.P50 - 0.2d) < 1e-12, "P50 应为 0.2，实际 " + stats.P50);
            Assert(Math.Abs(stats.P10 - 0.04d) < 1e-12, "P10 应为 0.04，实际 " + stats.P10);
            Assert(Math.Abs(stats.P90 - 0.36d) < 1e-12, "P90 应为 0.36，实际 " + stats.P90);

            var withBad = new List<double> { double.NaN, 0.1d, double.PositiveInfinity, 0.3d };
            Assert(Bug1GateProbeLogic.Summarize(withBad).Count == 2, "NaN/Inf 必须被过滤");
        }

        /// <summary>还原判等必须是位级（+0 与 -0 也算不同），不能退化成 epsilon 比较。</summary>
        public static void Test_BitEqualIsBitLevel()
        {
            Assert(Bug1GateProbeLogic.BitEqual(0.1f, 0.1f), "同一 float 必须位级相等");
            Assert(!Bug1GateProbeLogic.BitEqual(0.1f, 0.10000001f), "相邻 float 必须位级不等");
            Assert(!Bug1GateProbeLogic.BitEqual(0.0f, -0.0f), "+0 与 -0 位级不同，必须不等");
            Assert(Bug1GateProbeLogic.BitEqual(0.1d, 0.1d), "同一 double 必须位级相等");
            Assert(!Bug1GateProbeLogic.BitEqual(0.1d, 0.1000000000000001d), "相邻 double 必须位级不等");
        }

        /// <summary>离线不得 PASS：即使所有布尔都好看，measured=false 也只能 UNKNOWN。</summary>
        public static void Test_OfflineCannotProducePass()
        {
            List<Bug1GateRendererStats> perfect = Renderers(0.10d, 0.10d, 0.0d);
            var clean = new Bug1GateInvariance();
            string status;
            string reason = Bug1GateProbeLogic.Decide(false, true, true, true, clean,
                0.10d, perfect, 0.001d, out status);
            Assert(status == Bug1GateProbeLogic.StatusUnknown, "离线必须 UNKNOWN，实际 " + status);
            Assert(reason == Bug1GateProbeLogic.ReasonNotMeasured, "离线原因必须是 NOT_MEASURED，实际 " + reason);

            reason = Bug1GateProbeLogic.Decide(true, true, true, true, clean,
                0.10d, perfect, 0.001d, out status);
            Assert(status == Bug1GateProbeLogic.StatusPass, "控制组：measured=true 且残差 0 时必须 PASS");
            Assert(reason == Bug1GateProbeLogic.ReasonNone, "PASS 的原因必须是 NONE");
        }

        /// <summary>IMPURE_RENDER_NODE 的诊断载荷必须是组件类型名清单，不是数量。</summary>
        public static void Test_ImpureComponentListIsDiagnosable()
        {
            var types = new List<string> { "CapsuleCollider", "Rigidbody", "CapsuleCollider" };
            Assert(Bug1GateProbeLogic.FormatImpureComponents(types) == "components=CapsuleCollider,Rigidbody",
                "必须给出类型名清单，实际 " + Bug1GateProbeLogic.FormatImpureComponents(types));
            Assert(Bug1GateProbeLogic.FormatImpureComponents(new List<string>()) == "components=(none)",
                "空清单必须显式写 (none)");
            Assert(Bug1GateProbeLogic.FormatImpureComponents(null) == "components=(none)",
                "null 清单必须显式写 (none)");
        }

        /// <summary>
        /// T86-F2 的接线断言：生产 `RunGate` 必须真的调用下沉后的比较/白名单/profile 函数。
        /// 把任一调用删掉（或改成恒真）都会让这条变红——纯函数单测单独绿不代表门守住了。
        /// </summary>
        public static void Test_ProductionWiringUsesLogicComparisons()
        {
            MethodInfo runGate = typeof(Bug1GateProbe).GetMethod("RunGate",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert(runGate != null, "必须能反射到 Bug1GateProbe.RunGate");
            string logic = typeof(Bug1GateProbeLogic).FullName;

            AssertWired(runGate, logic, "CollectImpureComponentTypes", "纯节点门");
            AssertWired(runGate, logic, "CollidersEqual", "collider 比较");
            AssertWired(runGate, logic, "RaycastsEqual", "raycast 比较");
            AssertWired(runGate, logic, "TransformsEqual", "transform 位级比较");
            AssertWired(runGate, logic, "ComputeProfileHash", "profile 指纹");
            AssertWired(runGate, logic, "Decide", "唯一裁决入口");

            MethodInfo onPreCull = typeof(Bug1GateProbe).GetMethod("OnPreCull",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert(onPreCull != null, "必须能反射到 Bug1GateProbe.OnPreCull");
            int targetGone = IlContractProbe.CountMethodCalls(onPreCull, typeof(Bug1GateProbe).FullName, "HandleTargetGone");
            Assert(targetGone >= 1, "R16-02：目标离场必须走 HandleTargetGone（写 JSON + 重试），实际调用 " + targetGone);
            int emit = IlContractProbe.CountMethodCalls(runGate, typeof(Bug1GateProbe).FullName, "Emit");
            Assert(emit >= 1, "生产必须写 GATE.json（RunGate→Emit），实际调用 " + emit);

            // T86-F3：可见表面必须过滤 enabled 且 activeInHierarchy，否则隐藏 renderer 会冒充可见表面。
            MethodInfo collect = typeof(Bug1GateProbe).GetMethod("CollectVisibleRenderers",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert(collect != null, "必须能反射到 Bug1GateProbe.CollectVisibleRenderers");
            int enabledReads = IlContractProbe.CountMethodCalls(collect, "UnityEngine.Renderer", "get_enabled");
            Assert(enabledReads >= 1, "T86-F3：必须读取 Renderer.enabled 做过滤，实际 " + enabledReads);
            int activeReads = IlContractProbe.CountMethodCalls(collect, "UnityEngine.GameObject", "get_activeInHierarchy");
            Assert(activeReads >= 1, "T86-F3：必须读取 GameObject.activeInHierarchy 做过滤，实际 " + activeReads);
            int visibleCalls = IlContractProbe.CountMethodCalls(runGate, typeof(Bug1GateProbe).FullName, "CollectVisibleRenderers");
            Assert(visibleCalls >= 1, "RunGate 必须调用 CollectVisibleRenderers，实际 " + visibleCalls);
        }

        // ---- T92：三条物理边逐条判定 -----------------------------------------

        /// <summary>
        /// T92-F1：root / Third / Skeleton 任一条被动了都必须 FAIL，且拒绝码能区分是哪一条；
        /// 快照不可用只能 UNKNOWN（fail-closed），不许当成「没变化」。
        /// </summary>
        public static void Test_T92_TransformEdgesAreGatedPerEdge()
        {
            List<Bug1GateRendererStats> good = Renderers(0.10d, 0.10d, 0.0d);

            AssertDecideEdges(true, true, true, true,
                true, false, true, true,
                true, true, true, true,
                good, 0.001d, Bug1GateProbeLogic.StatusFail, Bug1GateProbeLogic.ReasonRootChanged,
                "root 被动了");
            AssertDecideEdges(true, true, true, true,
                true, true, false, true,
                true, true, true, true,
                good, 0.001d, Bug1GateProbeLogic.StatusFail, Bug1GateProbeLogic.ReasonThirdChanged,
                "Third 被动了");
            AssertDecideEdges(true, true, true, true,
                true, true, true, false,
                true, true, true, true,
                good, 0.001d, Bug1GateProbeLogic.StatusFail, Bug1GateProbeLogic.ReasonSkeletonChanged,
                "Skeleton 被动了");
            AssertDecideEdges(true, true, true, true,
                false, true, true, true,
                true, true, true, true,
                good, 0.001d, Bug1GateProbeLogic.StatusUnknown, Bug1GateProbeLogic.ReasonTransformUnavailable,
                "transform 快照不可用");
            // 三条边都干净 + 表面跟随 + epsilon 给定 → PASS，证明上面的 FAIL 不是「永远 FAIL」。
            AssertDecideEdges(true, true, true, true,
                true, true, true, true,
                true, true, true, true,
                good, 0.001d, Bug1GateProbeLogic.StatusPass, Bug1GateProbeLogic.ReasonNone,
                "三条边都干净");
        }

        /// <summary>
        /// T92-F1：生产必须对 root / Third / Skeleton 各做一次位级比较（三条边），
        /// 而不是只比其中两条——「删掉任意一条边」都必须让计数掉到 3 以下。
        /// </summary>
        public static void Test_T92_TransformEdgeComparisonsAreWired()
        {
            MethodInfo runGate = typeof(Bug1GateProbe).GetMethod("RunGate",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert(runGate != null, "必须能反射到 Bug1GateProbe.RunGate");
            string logic = typeof(Bug1GateProbeLogic).FullName;
            int edges = IlContractProbe.CountMethodCalls(runGate, logic, "TransformsEqual");
            Assert(edges >= 3, "T92-F1：root/Third/Skeleton 三条边必须各自比较一次（≥3），实际 " + edges);
            int decides = IlContractProbe.CountMethodCalls(runGate, logic, "Decide");
            Assert(decides >= 1, "三条边的比较结果必须进入 Decide，实际 Decide 调用 " + decides);
        }

        private static void AssertWired(MethodBase method, string declaringType, string callee, string what)
        {
            int calls = IlContractProbe.CountMethodCalls(method, declaringType, callee);
            Assert(calls >= 1, what + "：生产必须调用 Bug1GateProbeLogic." + callee + "，实际 " + calls);
        }

        // ---- helpers -------------------------------------------------------

        private static void AssertDecide(
            bool measured, bool pureNode, bool restoreExact, bool surfaceRestored,
            bool collidersAvailable, bool collidersUnchanged, bool raycastAvailable, bool raycastUnchanged,
            List<Bug1GateRendererStats> renderers, double epsilonU,
            string expectedStatus, string expectedReason, string what)
        {
            AssertDecideEdges(measured, pureNode, restoreExact, surfaceRestored,
                true, true, true, true,
                collidersAvailable, collidersUnchanged, raycastAvailable, raycastUnchanged,
                renderers, epsilonU, expectedStatus, expectedReason, what);
        }

        private static void AssertDecideEdges(
            bool measured, bool pureNode, bool restoreExact, bool surfaceRestored,
            bool transformsAvailable, bool rootUnchanged, bool thirdUnchanged, bool skeletonUnchanged,
            bool collidersAvailable, bool collidersUnchanged, bool raycastAvailable, bool raycastUnchanged,
            List<Bug1GateRendererStats> renderers, double epsilonU,
            string expectedStatus, string expectedReason, string what)
        {
            var invariance = new Bug1GateInvariance
            {
                TransformsAvailable = transformsAvailable,
                RootUnchanged = rootUnchanged,
                ThirdUnchanged = thirdUnchanged,
                SkeletonUnchanged = skeletonUnchanged,
                CollidersAvailable = collidersAvailable,
                CollidersUnchanged = collidersUnchanged,
                RaycastAvailable = raycastAvailable,
                RaycastUnchanged = raycastUnchanged,
            };
            string status;
            string reason = Bug1GateProbeLogic.Decide(measured, pureNode, restoreExact, surfaceRestored,
                invariance, 0.10d, renderers, epsilonU, out status);
            Assert(status == expectedStatus, what + "：状态应为 " + expectedStatus + "，实际 " + status);
            Assert(reason == expectedReason, what + "：原因应为 " + expectedReason + "，实际 " + reason);
        }

        private static List<Bug1GateRendererStats> Renderers(double offset, double maxDisplacement, double maxResidual)
        {
            return new List<Bug1GateRendererStats> { Renderer("Third/Model_0/Body", offset, maxDisplacement, maxResidual) };
        }

        private static Bug1GateRendererStats Renderer(string path, double offset, double maxDisplacement, double maxResidual)
        {
            return new Bug1GateRendererStats
            {
                Path = path,
                MeshName = "Mesh_A",
                BoneCount = 12,
                RootBonePath = "Third/Skeleton/Hips",
                SampleCount = 8,
                MaxDisplacementU = maxDisplacement,
                MaxResidualU = maxResidual,
                MaxDeviationRatio = Bug1GateProbeLogic.MaxDeviationRatio(offset, maxResidual),
            };
        }

        /// <summary>T95：姿态集合必须覆盖 Skeleton 全子树、renderer 绑定骨骼、Collider 父链，并按路径去重保序。</summary>
        public static void Test_T95_PoseNodePlanCoversSubtreeBonesAndColliderChain()
        {
            List<Bug1PoseNodePlan> plan = Bug1GateProbeLogic.BuildPoseNodePlan(
                new List<string> { "Third/Skeleton", "Third/Skeleton/Hips" },
                new List<string> { "Third/Skeleton/Hips", "Third/Model_0/ExtraBone" },
                new List<string> { "Third/Model_0/ExtraBone", "Player", "Third" });
            Assert(plan.Count == 5, "去重后应为 5 个节点，实际 " + plan.Count);
            Assert(plan[0].Path == "Third/Skeleton" && plan[0].Role == Bug1GateProbeLogic.PoseRoleSkeleton,
                "Skeleton 子树必须先入且角色为 skeleton");
            Assert(plan[1].Path == "Third/Skeleton/Hips" && plan[1].Role == Bug1GateProbeLogic.PoseRoleSkeleton,
                "同路径只保留首个角色（skeleton 优先于 bound）");
            Assert(plan[2].Path == "Third/Model_0/ExtraBone" && plan[2].Role == Bug1GateProbeLogic.PoseRoleBound,
                "Skeleton 树外的绑定骨骼必须纳入");
            Assert(plan[3].Path == "Player" && plan[3].Role == Bug1GateProbeLogic.PoseRoleColliderParent,
                "Collider 挂载节点必须纳入");
            Assert(plan[4].Path == "Third", "Collider 父链到 root 的中间节点必须纳入");
        }

        /// <summary>T95：姿态比较必须是 float32 逐位相等——1 ULP 也要判不等，并给出首个差异定位。</summary>
        public static void Test_T95_PoseComparisonIsBitLevel()
        {
            Bug1PoseSnapshot a = Pose("Third/Skeleton", 0.1f);
            Bug1PoseSnapshot b = Pose("Third/Skeleton", 0.1f);
            Assert(Bug1GateProbeLogic.PosesEqual(a, b), "相同位必须相等");
            Assert(Bug1GateProbeLogic.PoseDiff(a, b) == string.Empty, "相同位不得报差异");

            float next = NextUp(0.1f);
            Assert(next != 0.1f, "测试前提：NextUp 必须产生不同位模式");
            Bug1PoseSnapshot c = Pose("Third/Skeleton", next);
            Assert(!Bug1GateProbeLogic.PosesEqual(a, c), "local 1 ULP 差异必须判不等（不许 epsilon）");
            string diff = Bug1GateProbeLogic.PoseDiff(a, c);
            Assert(diff.Contains("local[0]"), "差异必须定位到 local[0]，实际 " + diff);

            Bug1PoseSnapshot d = Pose("Third/Skeleton", 0.1f);
            d.Entries[0].World[15] = NextUp(d.Entries[0].World[15]);
            Assert(!Bug1GateProbeLogic.PosesEqual(a, d), "world 矩阵 1 ULP 差异必须判不等");
            Assert(Bug1GateProbeLogic.PoseDiff(a, d).Contains("world[15]"), "差异必须定位到 world[15]");

            Bug1PoseSnapshot unavailable = Pose("Third/Skeleton", 0.1f);
            unavailable.Available = false;
            unavailable.UnavailableReason = "unreadable:Third/Skeleton";
            Assert(!Bug1GateProbeLogic.PosesEqual(a, unavailable), "不可用快照不得与可用快照相等");
            Bug1PoseSnapshot other = Pose("Third/Other", 0.1f);
            Assert(!Bug1GateProbeLogic.PosesEqual(a, other), "节点集合不同必须判不等");
        }

        private static Bug1GateTransformSnapshot Transform(
            float px, float py, float pz, float qx, float qy, float qz, float qw)
        {
            return new Bug1GateTransformSnapshot
            {
                Available = true,
                Px = px,
                Py = py,
                Pz = pz,
                Qx = qx,
                Qy = qy,
                Qz = qz,
                Qw = qw,
            };
        }

        private static List<Bug1GateColliderSnapshot> Colliders(float size)
        {
            return new List<Bug1GateColliderSnapshot>
            {
                new Bug1GateColliderSnapshot
                {
                    Name = "Player",
                    Type = "CapsuleCollider",
                    Enabled = true,
                    Available = true,
                    CenterX = 0d,
                    CenterY = 0.9d,
                    CenterZ = 0d,
                    SizeX = size,
                    SizeY = 1.8d,
                    SizeZ = size,
                },
            };
        }

        private static Bug1GateRaycastSnapshot Raycast(string hit, double distance)
        {
            return new Bug1GateRaycastSnapshot { Available = true, HitName = hit, Distance = distance };
        }

        private static List<Bug1GateRendererProfile> Profiles(
            string path, string mesh, int bones, string rootBone, string material)
        {
            var profile = new Bug1GateRendererProfile
            {
                Path = path,
                MeshName = mesh,
                BoneCount = bones,
                RootBonePath = rootBone,
            };
            profile.MaterialNames.Add(material);
            profile.ColliderTypes.Add("CapsuleCollider:off");
            return new List<Bug1GateRendererProfile> { profile };
        }

        private static Bug1ClientSnapshot Snap(ulong steamId, string name, int index)
        {
            return new Bug1ClientSnapshot
            {
                IsNull = false,
                HasPlayerId = true,
                HasPlayer = true,
                PlayerName = name,
                SteamId = steamId,
                SourceIndex = index,
            };
        }

        /// <summary>
        /// T95/T98：六格归类必须按真实输入——EPlayerStance 无 IDLE，站立不动=idle、站立行走=walk；
        /// 朝向按实测 yaw 与 0°/90° 的 ±5° 窗判定（旧的主轴比 1.5 ≈ ±33.7° 已废弃）。
        /// </summary>
        public static void Test_T95_SixCellClassification()
        {
            string reason;
            Assert(Bug1GateProbeLogic.ClassifyStanceCell("STAND", false, out reason) == Bug1GateProbeLogic.SpeedCellIdle,
                "站立不动必须归 idle（EPlayerStance 没有 IDLE）");
            Assert(Bug1GateProbeLogic.ClassifyStanceCell("STAND", true, out reason) == Bug1GateProbeLogic.SpeedCellWalk,
                "站立行走必须归 walk");
            Assert(Bug1GateProbeLogic.ClassifyStanceCell("SPRINT", false, out reason) == Bug1GateProbeLogic.SpeedCellSprint
                && Bug1GateProbeLogic.ClassifyStanceCell("SPRINT", true, out reason) == Bug1GateProbeLogic.SpeedCellSprint,
                "SPRINT 必须归 sprint（与 moving 无关）");
            Assert(Bug1GateProbeLogic.ClassifyStanceCell("IDLE", false, out reason) == Bug1GateProbeLogic.SpeedUnstable
                && reason.Contains("stance_unknown"), "不存在的 IDLE 枚举值不得被当成 idle");
            Assert(Bug1GateProbeLogic.ClassifyStanceCell("CROUCH", false, out reason) == Bug1GateProbeLogic.SpeedUnstable
                && reason.Contains("out_of_scope"), "CROUCH 不在支持域，必须 unstable 并说明原因");
            Assert(Bug1GateProbeLogic.ClassifyStanceCell(string.Empty, false, out reason) == Bug1GateProbeLogic.SpeedUnstable,
                "空 stance 必须 unstable");

            float yaw;
            Assert(Bug1GateProbeLogic.ClassifyHeadingBucket(0f, 1f, out yaw, out reason) == Bug1GateProbeLogic.HeadingBucketZ,
                "面向 +Z（yaw 0°）必须归 z 桶");
            Assert(Math.Abs(yaw) < 0.01f, "yaw 必须回传 0°，实际 " + yaw);
            Assert(Bug1GateProbeLogic.ClassifyHeadingBucket(1f, 0f, out yaw, out reason) == Bug1GateProbeLogic.HeadingBucketX,
                "面向 +X（yaw 90°）必须归 x 桶");
            Assert(Math.Abs(yaw - 90f) < 0.01f, "yaw 必须回传 90°，实际 " + yaw);
            Assert(Bug1GateProbeLogic.ClassifyHeadingBucket(1f, 0.1f, out yaw, out reason) == Bug1GateProbeLogic.HeadingUnstable
                && reason == "yaw_outside_nominal", "84° 超出 ±5° 窗必须 unstable，实际 " + yaw);
            double edge = (Bug1GateProbeLogic.HeadingNominalXDeg + 4.5d) * Math.PI / 180d;
            Assert(Bug1GateProbeLogic.ClassifyHeadingBucket((float)Math.Sin(edge), (float)Math.Cos(edge), out yaw, out reason)
                == Bug1GateProbeLogic.HeadingBucketX, "94.5° 在 ±5° 窗内必须归 x 桶，实际 yaw=" + yaw);
            Assert(Bug1GateProbeLogic.ClassifyHeadingBucket(0f, 0f, out yaw, out reason) == Bug1GateProbeLogic.HeadingUnstable
                && reason == "heading_too_small", "零向量必须 unstable");
            Assert(Bug1GateProbeLogic.ClassifyHeadingBucket(float.NaN, 1f, out yaw, out reason)
                == Bug1GateProbeLogic.HeadingUnstable, "NaN 朝向必须 unstable");

            // 核心回归：站姿样本必须落 idle 格，不能被 walk 冒充。
            Assert(Bug1GateProbeLogic.ComposeCellId(
                    Bug1GateProbeLogic.ClassifyStanceCell("STAND", false, out reason),
                    Bug1GateProbeLogic.HeadingBucketZ) == "idle-z",
                "站立不动 + yaw0 必须是 idle-z");
            Assert(Bug1GateProbeLogic.ComposeCellId(Bug1GateProbeLogic.SpeedCellWalk, Bug1GateProbeLogic.HeadingBucketZ) == "walk-z",
                "格 id 必须是 speed-bucket");
            Assert(Bug1GateProbeLogic.ComposeCellId(Bug1GateProbeLogic.SpeedUnstable, Bug1GateProbeLogic.HeadingBucketX) == null,
                "任一维不稳必须不归档");
            Assert(Bug1GateProbeLogic.AllCells.Length == 6, "必须恰好六格");
        }

        /// <summary>T95：覆盖校验器——反例即 FAIL，样本不足/无正证据/epsilon 未指定即 UNKNOWN，六格全过才 PASS。</summary>
        public static void Test_T95_CoverageEvaluatorRules()
        {
            var all = new List<Bug1GateCellSample>();
            for (int i = 0; i < Bug1GateProbeLogic.AllCells.Length; i++)
            {
                all.Add(CellSample(Bug1GateProbeLogic.AllCells[i], true, true, true, true, 0.001d));
            }
            Bug1GateCoverageReport pass = Bug1GateProbeLogic.EvaluateCoverage(all, 1, 0.01d);
            Assert(pass.Status == Bug1GateProbeLogic.StatusPass && pass.Reason == Bug1GateProbeLogic.ReasonNone,
                "六格全正证据必须 PASS，实际 " + pass.Status + "/" + pass.Reason);
            Assert(pass.Cells.Count == 6 && pass.EvidenceRevision == Bug1GateProbeLogic.EvidenceRevision,
                "必须带六格与 g_evidence_revision");

            var counterexample = new List<Bug1GateCellSample>(all)
            {
                CellSample("walk-x", false, true, true, true, 0d),
            };
            Bug1GateCoverageReport fail = Bug1GateProbeLogic.EvaluateCoverage(counterexample, 1, 0.01d);
            Assert(fail.Status == Bug1GateProbeLogic.StatusFail
                && fail.Reason == Bug1GateProbeLogic.ReasonPoseCounterexample,
                "pose 稳定但表面不随动必须 FAIL/POSE_COUNTEREXAMPLE，实际 " + fail.Status + "/" + fail.Reason);
            Assert(fail.Detail.Contains("walk-x"), "FAIL 详情必须指出是哪一格，实际 " + fail.Detail);

            var torn = new List<Bug1GateCellSample>(all)
            {
                CellSample("sprint-z", true, true, true, true, 0.001d, torn: true),
            };
            Assert(Bug1GateProbeLogic.EvaluateCoverage(torn, 1, 0.01d).Status == Bug1GateProbeLogic.StatusFail,
                "撕裂同样必须 FAIL");

            Bug1GateCoverageReport few = Bug1GateProbeLogic.EvaluateCoverage(
                new List<Bug1GateCellSample> { CellSample("idle-x", true, true, true, true, 0.001d) }, 2, 0.01d);
            Assert(few.Status == Bug1GateProbeLogic.StatusUnknown
                && few.Reason == Bug1GateProbeLogic.ReasonSamplesInsufficient,
                "样本不足必须 UNKNOWN/SAMPLES_INSUFFICIENT，实际 " + few.Status + "/" + few.Reason);

            var noEpsilon = new List<Bug1GateCellSample>();
            for (int i = 0; i < Bug1GateProbeLogic.AllCells.Length; i++)
            {
                noEpsilon.Add(CellSample(Bug1GateProbeLogic.AllCells[i], true, true, true, false, 0d));
            }
            Bug1GateCoverageReport unspecified = Bug1GateProbeLogic.EvaluateCoverage(noEpsilon, 1, 0d);
            Assert(unspecified.Status == Bug1GateProbeLogic.StatusUnknown
                && unspecified.Reason == Bug1GateProbeLogic.ReasonEpsilonUnspecified,
                "epsilon 未指定必须 UNKNOWN/EPSILON_UNSPECIFIED，实际 " + unspecified.Status + "/" + unspecified.Reason);

            var overEpsilon = new List<Bug1GateCellSample>();
            for (int i = 0; i < Bug1GateProbeLogic.AllCells.Length; i++)
            {
                overEpsilon.Add(CellSample(Bug1GateProbeLogic.AllCells[i], true, true, true, true, 0.5d));
            }
            Bug1GateCoverageReport noPositive = Bug1GateProbeLogic.EvaluateCoverage(overEpsilon, 1, 0.01d);
            Assert(noPositive.Status == Bug1GateProbeLogic.StatusUnknown
                && noPositive.Reason == Bug1GateProbeLogic.ReasonNoPositiveEvidence,
                "残差超 epsilon 且无反例时必须 UNKNOWN/NO_POSITIVE_EVIDENCE，实际 " + noPositive.Status + "/" + noPositive.Reason);

            Bug1GateCoverageReport again = Bug1GateProbeLogic.EvaluateCoverage(all, 1, 0.01d);
            Assert(again.Status == pass.Status && again.Reason == pass.Reason && again.Cells.Count == pass.Cells.Count,
                "同输入必须可复算出一致结论");
            for (int i = 0; i < pass.Cells.Count; i++)
            {
                Assert(again.Cells[i].Valid == pass.Cells[i].Valid && again.Cells[i].Positive == pass.Cells[i].Positive,
                    "第 " + i + " 格计数必须可复算");
            }
        }

        /// <summary>T95：生产路径必须真的取姿态、比姿态、算覆盖——三条边之外再加四条接线断言。</summary>
        public static void Test_T95_ProductionWiringUsesPoseCoverage()
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            Type probe = typeof(Bug1GateProbe);
            MethodInfo sampleOnce = probe.GetMethod("SampleOnce", flags);
            Assert(!ReferenceEquals(sampleOnce, null), "生产必须存在 SampleOnce");
            int snapshots = IlContractProbe.CountMethodCalls(sampleOnce, typeof(Bug1GateProbe).FullName, "SnapshotPose");
            Assert(snapshots >= 2, "零偏移对照至少要取两次姿态快照（C0/C1），实际 " + snapshots);
            int compared = IlContractProbe.CountMethodCalls(sampleOnce, typeof(Bug1GateProbeLogic).FullName, "PosesEqual");
            Assert(compared >= 1, "零偏移对照必须比较姿态，实际 " + compared);

            MethodInfo measure = probe.GetMethod("MeasureCellAxis", flags);
            Assert(!ReferenceEquals(measure, null), "生产必须存在 MeasureCellAxis");
            int poseChecks = IlContractProbe.CountMethodCalls(measure, typeof(Bug1GateProbeLogic).FullName, "PosesEqual");
            Assert(poseChecks >= 2, "施加后（A1）与还原后（A2）都必须比较姿态，实际 " + poseChecks);

            MethodInfo limits = probe.GetMethod("CheckCoverageLimits", flags);
            Assert(!ReferenceEquals(limits, null), "生产必须存在 CheckCoverageLimits");
            Assert(IlContractProbe.CountMethodCalls(limits, typeof(Bug1GateProbeLogic).FullName, "EvaluateCoverage") >= 1,
                "采样过程中必须实时算覆盖");

            MethodInfo finish = probe.GetMethod("FinishCoverage", flags);
            Assert(!ReferenceEquals(finish, null), "生产必须存在 FinishCoverage");
            Assert(IlContractProbe.CountMethodCalls(finish, typeof(Bug1GateProbeLogic).FullName, "EvaluateCoverage") >= 1,
                "收尾必须重算覆盖后再落盘");

            MethodInfo preCull = probe.GetMethod("OnPreCull", flags);
            Assert(!ReferenceEquals(preCull, null), "生产必须存在 OnPreCull");
            Assert(IlContractProbe.CountMethodCalls(preCull, typeof(Bug1GateProbe).FullName, "SampleOnce") >= 1,
                "相机回调必须驱动采样");
        }

        private static Bug1PoseSnapshot Pose(string path, float local0)
        {
            var snapshot = new Bug1PoseSnapshot { Available = true };
            var entry = new Bug1PoseEntry
            {
                Path = path,
                Role = Bug1GateProbeLogic.PoseRoleSkeleton,
                HasLocal = true,
                HasWorld = true,
            };
            entry.Local[0] = local0;
            entry.Local[7] = 1f;
            entry.World[15] = 1f;
            snapshot.Entries.Add(entry);
            return snapshot;
        }

        private static float NextUp(float value)
        {
            int bits = BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
            bits = value >= 0f ? bits + 1 : bits - 1;
            return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
        }

        private static Bug1GateCellSample CellSample(
            string cell, bool moved, bool poseEqual, bool invarianceOk,
            bool epsilonSpecified, double maxResidualU, bool torn = false)
        {
            return new Bug1GateCellSample
            {
                Cell = cell,
                SampleId = cell + "-" + Guid.NewGuid().ToString("N"),
                Available = true,
                VertexMoved = moved,
                Torn = torn,
                PoseEqual = poseEqual,
                InvarianceOk = invarianceOk,
                EpsilonSpecified = epsilonSpecified,
                MaxResidualU = maxResidualU,
                Detail = "axis=X;max_moved_u=0.1;min_moved_u=0.1",
            };
        }

        /// <summary>T98/R17-02：朝向稳定化单位是采样——首样本不稳、连续两次同桶才接受、换桶清零。</summary>
        public static void Test_T98_HeadingStabilityCountsSamples()
        {
            int count;
            Assert(Bug1GateProbeLogic.AdvanceHeadingStability(
                Bug1GateProbeLogic.HeadingUnstable, Bug1GateProbeLogic.HeadingBucketZ, 0, out count)
                == Bug1GateProbeLogic.HeadingUnstable && count == 1,
                "首次命中不得直接接受（必须连续 2 次采样）");
            Assert(Bug1GateProbeLogic.AdvanceHeadingStability(
                Bug1GateProbeLogic.HeadingBucketZ, Bug1GateProbeLogic.HeadingBucketZ, count, out count)
                == Bug1GateProbeLogic.HeadingBucketZ && count == 2,
                "连续两次同桶必须接受");
            Assert(Bug1GateProbeLogic.AdvanceHeadingStability(
                Bug1GateProbeLogic.HeadingBucketZ, Bug1GateProbeLogic.HeadingBucketX, 2, out count)
                == Bug1GateProbeLogic.HeadingUnstable && count == 1,
                "换桶必须清零并回到 unstable");
            Assert(Bug1GateProbeLogic.AdvanceHeadingStability(
                Bug1GateProbeLogic.HeadingBucketZ, Bug1GateProbeLogic.HeadingUnstable, 2, out count)
                == Bug1GateProbeLogic.HeadingUnstable && count == 0,
                "unstable 必须清零");
        }

        /// <summary>T98/R17-01：生产遍历必须有界（节点数与深度），且生产路径真的调用判定函数。</summary>
        public static void Test_T98_PoseWalkIsBounded()
        {
            Assert(Bug1GateProbeLogic.PoseWalkAllowed(0, Bug1GateProbeLogic.MaxPoseNodes), "0 节点必须允许继续");
            Assert(Bug1GateProbeLogic.PoseWalkAllowed(Bug1GateProbeLogic.MaxPoseNodes - 1, 0),
                "未达上限必须允许继续（0 表示用默认上限）");
            Assert(!Bug1GateProbeLogic.PoseWalkAllowed(Bug1GateProbeLogic.MaxPoseNodes, Bug1GateProbeLogic.MaxPoseNodes),
                "达到节点上限必须停止遍历");
            Assert(Bug1GateProbeLogic.MaxPoseDepth > 0 && Bug1GateProbeLogic.MaxPoseNodes > 0, "上限必须为正");
            // R18-02：深度上限必须有独立纯函数与边界证据（64 允许、65 拒绝）。
            Assert(Bug1GateProbe.PoseDepthAllowed(Bug1GateProbeLogic.MaxPoseDepth),
                "深度 = MaxPoseDepth 必须允许");
            Assert(!Bug1GateProbe.PoseDepthAllowed(Bug1GateProbeLogic.MaxPoseDepth + 1),
                "深度 = MaxPoseDepth + 1 必须拒绝");
            Assert(Bug1GateProbe.PoseDepthAllowed(0), "深度 0 必须允许");
            Assert(!Bug1GateProbe.PoseDepthAllowed(-1), "负深度必须拒绝");

            MethodInfo collect = null;
            MethodInfo[] collects = typeof(Bug1GateProbe).GetMethods(
                BindingFlags.Static | BindingFlags.NonPublic);
            for (int i = 0; i < collects.Length; i++)
            {
                if (collects[i].Name == "CollectSubtree" && collects[i].GetParameters().Length == 6)
                {
                    collect = collects[i];
                    break;
                }
            }
            Assert(!ReferenceEquals(collect, null), "必须存在带深度与遍历状态的 CollectSubtree");
            int guarded = IlContractProbe.CountMethodCalls(
                collect, typeof(Bug1GateProbeLogic).FullName, "PoseWalkAllowed");
            Assert(guarded >= 1, "CollectSubtree 必须调用 PoseWalkAllowed 做节点上限判定，实际=" + guarded);
            int depthGuarded = IlContractProbe.CountMethodCalls(
                collect, typeof(Bug1GateProbe).FullName, "PoseDepthAllowed");
            Assert(depthGuarded >= 1, "CollectSubtree 必须调用 PoseDepthAllowed 做深度判定，实际=" + depthGuarded);
        }

        /// <summary>T98/R17-03：还原门是位级比较（1 ULP 即不等），且生产路径真的调用它。</summary>
        public static void Test_T98_RestoreGateIsBitLevel()
        {
            float[] before = { 0.1f, -2.5f, 3f };
            float[] same = { 0.1f, -2.5f, 3f };
            float[] oneUlp = { NextUp(0.1f), -2.5f, 3f };
            Assert(Bug1GateProbeLogic.RestoreBitsEqual(before, same), "相同位必须判等");
            Assert(!Bug1GateProbeLogic.RestoreBitsEqual(before, oneUlp), "1 ULP 差异必须判不等（禁 epsilon）");
            Assert(!Bug1GateProbeLogic.RestoreBitsEqual(before, new float[] { 0.1f, -2.5f }),
                "长度不一致必须判不等");
            Assert(!Bug1GateProbeLogic.RestoreBitsEqual(null, same), "null 必须判不等");

            MethodInfo measure = typeof(Bug1GateProbe).GetMethod(
                "MeasureCellAxis", BindingFlags.Static | BindingFlags.NonPublic);
            Assert(!ReferenceEquals(measure, null), "必须存在 MeasureCellAxis");
            int calls = IlContractProbe.CountMethodCalls(
                measure, typeof(Bug1GateProbeLogic).FullName, "RestoreBitsEqual");
            Assert(calls >= 1, "MeasureCellAxis 必须调用 RestoreBitsEqual 做还原门，实际=" + calls);
        }

        /// <summary>T98/R17-04：只有可用样本占预算，unassigned/不稳样本不计。</summary>
        public static void Test_T98_BudgetCountsOnlyValidSamples()
        {
            var samples = new List<Bug1GateCellSample>
            {
                CellSample("idle-z", true, true, true, true, 0.01d),
                new Bug1GateCellSample { Cell = "unassigned", Available = false, Detail = "cell-unstable" },
                new Bug1GateCellSample { Cell = "walk-z", Available = false, Detail = "POSE_UNSTABLE" },
            };
            Assert(Bug1GateProbeLogic.CountValidSamples(samples) == 1,
                "只有 1 个可用样本，实际 " + Bug1GateProbeLogic.CountValidSamples(samples));

            MethodInfo limits = typeof(Bug1GateProbe).GetMethod(
                "CheckCoverageLimits", BindingFlags.Static | BindingFlags.NonPublic);
            Assert(!ReferenceEquals(limits, null), "必须存在 CheckCoverageLimits");
            int wired = IlContractProbe.CountMethodCalls(
                limits, typeof(Bug1GateProbeLogic).FullName, "CountValidSamples");
            Assert(wired >= 1, "CheckCoverageLimits 必须用 CountValidSamples 计预算，实际=" + wired);
        }

        /// <summary>T98/t96-F3 + R18-05：note 按模式拆成两个方法，覆盖模式不可能写出单帧口径。</summary>
        public static void Test_T98_ModeNoteIsSplit()
        {
            string coverage = Bug1GateProbe.BuildCoverageNote(12, 7);
            Assert(coverage.Contains("六格采样") && coverage.Contains("12") && coverage.Contains("7"),
                "覆盖模式 note 必须写采样数与有效样本数，实际 " + coverage);
            Assert(!coverage.Contains("single_frame_single_pose"), "覆盖模式不得再写单帧单姿态");
            string single = Bug1GateProbe.BuildSingleNote();
            Assert(single.Contains("single_frame_single_pose"), "单次测量路径才写单帧单姿态");

            MethodInfo finish = typeof(Bug1GateProbe).GetMethod(
                "FinishCoverage", BindingFlags.Static | BindingFlags.NonPublic);
            Assert(!ReferenceEquals(finish, null), "必须存在 FinishCoverage");
            int wired = IlContractProbe.CountMethodCalls(
                finish, typeof(Bug1GateProbe).FullName, "BuildCoverageNote");
            Assert(wired >= 1, "FinishCoverage 必须写覆盖模式 note（BuildCoverageNote），实际=" + wired);
            int wrong = IlContractProbe.CountMethodCalls(
                finish, typeof(Bug1GateProbe).FullName, "BuildSingleNote");
            Assert(wrong == 0, "覆盖路径不得调用 BuildSingleNote（错误模式必须不可表达），实际=" + wrong);
        }

        /// <summary>T98/R17-02：cells[] 必须记录真实 stance 与实测 yaw 区间（可复核入格依据）。</summary>
        public static void Test_T98_CoverageJsonRecordsStanceAndYaw()
        {
            var samples = new List<Bug1GateCellSample>();
            for (int i = 0; i < Bug1GateProbeLogic.AllCells.Length; i++)
            {
                string cell = Bug1GateProbeLogic.AllCells[i];
                string stance = cell.StartsWith("idle", StringComparison.Ordinal) ? "STAND" : "SPRINT";
                double yaw = cell.EndsWith("-x", StringComparison.Ordinal) ? 90d : 0d;
                samples.Add(new Bug1GateCellSample
                {
                    Cell = cell,
                    SampleId = "s" + i,
                    Available = true,
                    VertexMoved = true,
                    PoseEqual = true,
                    InvarianceOk = true,
                    EpsilonSpecified = true,
                    MaxResidualU = 0.001d,
                    Stance = stance,
                    MeasuredYawDeg = yaw,
                });
            }
            Bug1GateCoverageReport report = Bug1GateProbeLogic.EvaluateCoverage(samples, 1, 0.01d);
            Assert(report.Status == Bug1GateProbeLogic.StatusPass, "六格全正必须 PASS，实际 " + report.Reason);
            string json = Bug1GateProbeLogic.BuildCoverageJson(report);
            Assert(json.Contains("\"stances\":[\"STAND\"]"), "idle 格必须记录真实 stance=STAND");
            Assert(json.Contains("\"stances\":[\"SPRINT\"]"), "sprint 格必须记录真实 stance=SPRINT");
            Assert(json.Contains("\"measured_yaw_deg_min\":0"), "z 桶必须记录实测 yaw 0°");
            Assert(json.Contains("\"measured_yaw_deg_max\":90"), "x 桶必须记录实测 yaw 90°");
        }

        /// <summary>T104/R18-01：子树被截断时必须 fail-closed（快照不可用 + pose_truncated:collected/limit）。</summary>
        public static void Test_T104_PoseTruncationIsFailClosed()
        {
            Assert(!Bug1GateProbe.PoseWalkTruncated(0, Bug1GateProbeLogic.MaxPoseNodes), "0 节点未截断");
            Assert(!Bug1GateProbe.PoseWalkTruncated(Bug1GateProbeLogic.MaxPoseNodes - 1,
                Bug1GateProbeLogic.MaxPoseNodes), "上限前一个节点未截断");
            Assert(Bug1GateProbe.PoseWalkTruncated(Bug1GateProbeLogic.MaxPoseNodes,
                Bug1GateProbeLogic.MaxPoseNodes), "达到上限必须判截断");
            Assert(Bug1GateProbe.BuildPoseTruncatedReason(512, Bug1GateProbeLogic.MaxPoseNodes)
                == "pose_truncated:512/" + Bug1GateProbeLogic.MaxPoseNodes,
                "截断原因必须带 collected/limit，实际 " + Bug1GateProbe.BuildPoseTruncatedReason(512, 0));

            MethodInfo snapshot = typeof(Bug1GateProbe).GetMethod(
                "SnapshotPose", BindingFlags.Static | BindingFlags.NonPublic);
            Assert(!ReferenceEquals(snapshot, null), "必须存在 SnapshotPose");
            // 二层契约：生产路径必须经过判定点（不作为主要证据）。
            int wired = IlContractProbe.CountMethodCalls(
                snapshot, typeof(Bug1GateProbe).FullName, "ApplyPoseAvailability");
            Assert(wired >= 1, "SnapshotPose 必须经 ApplyPoseAvailability 决定可用性，实际=" + wired);

            // R19-01 主证据：直接读判定返回值与快照字段，删掉/反转判定都会红。
            var truncated = new Bug1GateProbe.Bug1PoseWalkState();
            truncated.Truncated = true;
            truncated.Collected = 512;
            string reason;
            bool available = Bug1GateProbe.PoseSnapshotAvailable(truncated, out reason);
            Assert(!available, "Truncated=true 时 PoseSnapshotAvailable 必须返回 false");
            Assert(reason == "pose_truncated:512/" + Bug1GateProbeLogic.MaxPoseNodes,
                "截断原因必须命中 pose_truncated:<n>/<limit>，实际 " + (reason ?? "<null>"));
            Assert(Bug1GateProbe.ShouldMarkPoseUnavailable(truncated, out reason),
                "Truncated=true 时 ShouldMarkPoseUnavailable 必须为 true");

            var truncatedSnapshot = new Bug1PoseSnapshot();
            bool applied = Bug1GateProbe.ApplyPoseAvailability(truncatedSnapshot, truncated);
            Assert(!applied, "ApplyPoseAvailability 截断时必须返回 false");
            Assert(!truncatedSnapshot.Available, "截断快照的 Available 必须为 false（不得被反转）");
            Assert(truncatedSnapshot.UnavailableReason == "pose_truncated:512/" + Bug1GateProbeLogic.MaxPoseNodes,
                "截断快照必须带 pose_truncated 原因，实际 " + (truncatedSnapshot.UnavailableReason ?? "<null>"));

            var clean = new Bug1GateProbe.Bug1PoseWalkState();
            Assert(Bug1GateProbe.PoseSnapshotAvailable(clean, out reason),
                "Truncated=false 时 PoseSnapshotAvailable 必须返回 true");
            Assert(string.IsNullOrEmpty(reason), "未截断时不得带不可用原因，实际 " + (reason ?? "<null>"));
            Assert(!Bug1GateProbe.ShouldMarkPoseUnavailable(clean, out reason),
                "Truncated=false 时 ShouldMarkPoseUnavailable 必须为 false");
            var cleanSnapshot = new Bug1PoseSnapshot();
            Assert(Bug1GateProbe.ApplyPoseAvailability(cleanSnapshot, clean),
                "ApplyPoseAvailability 未截断时必须返回 true");
            Assert(cleanSnapshot.Available, "未截断快照的 Available 必须为 true");
            Assert(string.IsNullOrEmpty(cleanSnapshot.UnavailableReason), "未截断快照不得带原因");
        }

        /// <summary>T104/R18-03：总样本硬上限（含不可用样本）与收尾原因必须真实存在。</summary>
        public static void Test_T104_TotalSampleBudget()
        {
            Assert(Bug1GateProbe.MaxTotalSamplesDefault > 0, "总样本上限必须为正");
            Assert(!Bug1GateProbe.TotalSamplesExceeded(Bug1GateProbe.MaxTotalSamplesDefault - 1),
                "未达上限不得收尾");
            Assert(Bug1GateProbe.TotalSamplesExceeded(Bug1GateProbe.MaxTotalSamplesDefault),
                "达到上限必须收尾");

            MethodInfo limits = typeof(Bug1GateProbe).GetMethod(
                "CheckCoverageLimits", BindingFlags.Static | BindingFlags.NonPublic);
            Assert(!ReferenceEquals(limits, null), "必须存在 CheckCoverageLimits");
            int wired = IlContractProbe.CountMethodCalls(
                limits, typeof(Bug1GateProbe).FullName, "TotalSamplesExceeded");
            Assert(wired >= 1, "CheckCoverageLimits 必须按总样本判定，实际=" + wired);
            Assert(IlContractProbe.ContainsStringLiteral(limits, "max_total_samples"),
                "收尾原因必须写 max_total_samples");
        }

        /// <summary>T104/R18-05：覆盖 note 与单帧 note 是两条不可互换的路径。</summary>
        public static void Test_T104_ModeNotesCannotMix()
        {
            string coverage = Bug1GateProbe.BuildCoverageNote(3, 3);
            Assert(coverage.Contains("六格采样"), "覆盖 note 必须写六格口径，实际 " + coverage);
            Assert(!coverage.Contains("single_frame_single_pose"), "覆盖 note 不得含单帧口径");
            Assert(Bug1GateProbe.BuildSingleNote().Contains("single_frame_single_pose"),
                "单次测量 note 必须写明单帧单姿态");

            MethodInfo runGate = typeof(Bug1GateProbe).GetMethod(
                "RunGate", BindingFlags.Static | BindingFlags.NonPublic);
            Assert(!ReferenceEquals(runGate, null), "必须存在 RunGate");
            int single = IlContractProbe.CountMethodCalls(
                runGate, typeof(Bug1GateProbe).FullName, "BuildSingleNote");
            Assert(single >= 1, "RunGate 必须写单次测量 note，实际=" + single);
            int coverageOnRunGate = IlContractProbe.CountMethodCalls(
                runGate, typeof(Bug1GateProbe).FullName, "BuildCoverageNote");
            Assert(coverageOnRunGate == 0, "RunGate 不得写覆盖 note（模式互斥），实际=" + coverageOnRunGate);

            // R19-03：名字叫 Cannot Mix，就必须两侧都钉住——FinishCoverage 只许覆盖 note，
            // 且两段文案互不含对方口径（覆盖文本不含单帧串、单帧文本不含六格串）。
            MethodInfo finishCoverage = typeof(Bug1GateProbe).GetMethod(
                "FinishCoverage", BindingFlags.Static | BindingFlags.NonPublic);
            Assert(!ReferenceEquals(finishCoverage, null), "必须存在 FinishCoverage");
            int coverageOnFinish = IlContractProbe.CountMethodCalls(
                finishCoverage, typeof(Bug1GateProbe).FullName, "BuildCoverageNote");
            Assert(coverageOnFinish >= 1, "FinishCoverage 必须写覆盖 note，实际=" + coverageOnFinish);
            int singleOnFinish = IlContractProbe.CountMethodCalls(
                finishCoverage, typeof(Bug1GateProbe).FullName, "BuildSingleNote");
            Assert(singleOnFinish == 0, "FinishCoverage 不得写单次测量 note，实际=" + singleOnFinish);
            Assert(!Bug1GateProbe.BuildSingleNote().Contains("六格采样"), "单帧 note 不得含六格口径");
        }

        /// <summary>T104/R18-04：探针必须接受 Debug 的 ceq 否定形态，但删掉真实分支仍要判红。</summary>
        public static void Test_T104_IlProbeAcceptsDebugNegation()
        {
            var debugShape = new List<IlContractProbe.IlInstruction>
            {
                new IlContractProbe.IlInstruction { Offset = 0, OpCode = OpCodes.Call },
                new IlContractProbe.IlInstruction { Offset = 5, OpCode = OpCodes.Stloc_0 },
                new IlContractProbe.IlInstruction { Offset = 6, OpCode = OpCodes.Ldloc_0 },
                new IlContractProbe.IlInstruction { Offset = 7, OpCode = OpCodes.Ldc_I4_0 },
                new IlContractProbe.IlInstruction { Offset = 8, OpCode = OpCodes.Ceq },
                new IlContractProbe.IlInstruction { Offset = 9, OpCode = OpCodes.Stloc_1 },
                new IlContractProbe.IlInstruction { Offset = 10, OpCode = OpCodes.Ldloc_1 },
                new IlContractProbe.IlInstruction { Offset = 11, OpCode = OpCodes.Brtrue_S },
            };
            Assert(IlContractProbe.ResultFeedsConditionalBranch(debugShape, 0, 8),
                "Debug 否定形态（ceq;stloc;ldloc;br）必须被识别为采纳了返回值");

            // 反向证据：删掉真实分支后仍必须判红，避免把探针放松成永真。
            var branchless = new List<IlContractProbe.IlInstruction>
            {
                new IlContractProbe.IlInstruction { Offset = 0, OpCode = OpCodes.Call },
                new IlContractProbe.IlInstruction { Offset = 5, OpCode = OpCodes.Stloc_0 },
                new IlContractProbe.IlInstruction { Offset = 6, OpCode = OpCodes.Ldloc_0 },
                new IlContractProbe.IlInstruction { Offset = 7, OpCode = OpCodes.Ldc_I4_0 },
                new IlContractProbe.IlInstruction { Offset = 8, OpCode = OpCodes.Ceq },
                new IlContractProbe.IlInstruction { Offset = 9, OpCode = OpCodes.Stloc_1 },
                new IlContractProbe.IlInstruction { Offset = 10, OpCode = OpCodes.Ldloc_1 },
                new IlContractProbe.IlInstruction { Offset = 11, OpCode = OpCodes.Pop },
            };
            Assert(!IlContractProbe.ResultFeedsConditionalBranch(branchless, 0, 8),
                "删掉真实分支后必须判红（探针不得永真）");
            var discarded = new List<IlContractProbe.IlInstruction>
            {
                new IlContractProbe.IlInstruction { Offset = 0, OpCode = OpCodes.Call },
                new IlContractProbe.IlInstruction { Offset = 5, OpCode = OpCodes.Pop },
            };
            Assert(!IlContractProbe.ResultFeedsConditionalBranch(discarded, 0, 8),
                "call 后直接 pop 必须判红");
        }

        /// <summary>
        /// R21-01（t110）：重试预算按触发源分别计数。
        /// 实机缺陷：level-loaded 在托管就绪前连续拒绝，把全局 3 次预算吃光，
        /// 等 player-created 到来时已经 GIVEUP —— 探针在 listen host 上永远起不来。
        /// </summary>
        public static void Test_R21_RetryBudgetIsPerTrigger()
        {
            const int max = 3;
            // 纯判定层：level-loaded 用满 3 次后，player-created 的额度必须完好。
            var state = new Bug1GateProbeLogic.Bug1RetryBudgetState();
            for (int i = 1; i <= max; i++)
            {
                Bug1GateProbeLogic.Bug1RetryDecision retry =
                    Bug1GateProbeLogic.DecideRetry(state, Bug1GateProbeLogic.Bug1RetryTrigger.LevelLoaded, max);
                Assert(retry.Retry && !retry.GiveUp, "level-loaded 第 " + i + " 次应可重试");
                Assert(retry.Used == i, "level-loaded 第 " + i + " 次 used 应为 " + i + "，实际 " + retry.Used);
            }
            Bug1GateProbeLogic.Bug1RetryDecision exhausted =
                Bug1GateProbeLogic.DecideRetry(state, Bug1GateProbeLogic.Bug1RetryTrigger.LevelLoaded, max);
            Assert(!exhausted.Retry && exhausted.GiveUp,
                "level-loaded 用满后必须 GIVEUP（不能永不死心）");
            Assert(exhausted.Reason == Bug1GateProbeLogic.RetryReasonGiveUp,
                "GIVEUP 原因码应为 RETRY_GIVEUP，实际 " + exhausted.Reason);

            Bug1GateProbeLogic.Bug1RetryDecision other =
                Bug1GateProbeLogic.DecideRetry(state, Bug1GateProbeLogic.Bug1RetryTrigger.PlayerCreated, max);
            Assert(other.Retry, "level-loaded 耗尽不得消耗 player-created 的预算（R21-01 核心）");
            Assert(other.Used == 1, "player-created 应从头计数，实际 used=" + other.Used);
            Assert(state.PlayerCreated == 1, "player-created 计数应独立为 1，实际 " + state.PlayerCreated);

            // 反向：同一触发源（player-created）自己用满 3 次后也必须 GIVEUP。
            for (int i = 2; i <= max; i++)
            {
                Bug1GateProbeLogic.Bug1RetryDecision more =
                    Bug1GateProbeLogic.DecideRetry(state, Bug1GateProbeLogic.Bug1RetryTrigger.PlayerCreated, max);
                Assert(more.Retry && more.Used == i, "player-created 第 " + i + " 次应可重试");
            }
            Bug1GateProbeLogic.Bug1RetryDecision playerExhausted =
                Bug1GateProbeLogic.DecideRetry(state, Bug1GateProbeLogic.Bug1RetryTrigger.PlayerCreated, max);
            Assert(!playerExhausted.Retry && playerExhausted.GiveUp,
                "player-created 自己用满后也必须 GIVEUP");

            // 未知触发源不得共享任何人的额度（枚举只能靠强转造出非法值）。
            var fresh = new Bug1GateProbeLogic.Bug1RetryBudgetState();
            Bug1GateProbeLogic.Bug1RetryDecision unknown =
                Bug1GateProbeLogic.DecideRetry(fresh, (Bug1GateProbeLogic.Bug1RetryTrigger)99, max);
            Assert(!unknown.Retry && unknown.GiveUp, "未知触发源必须直接 GIVEUP");
            Assert(unknown.Reason == Bug1GateProbeLogic.RetryReasonUnknownTrigger,
                "未知触发源原因码应为 RETRY_TRIGGER_UNKNOWN，实际 " + unknown.Reason);
            Assert(fresh.LevelLoaded == 0 && fresh.PlayerCreated == 0, "未知触发源不得改动任何计数");

            // 生产路径：走 TryRetry 用的同一个记账入口（不是另写一份判定）。
            Bug1GateProbe.__TestResetRetryBudget();
            for (int i = 1; i <= max; i++)
            {
                Bug1GateProbeLogic.Bug1RetryDecision prod =
                    Bug1GateProbe.__TestAccountRetry(Bug1GateProbeLogic.Bug1RetryTrigger.LevelLoaded);
                Assert(prod.Retry && prod.Used == i, "生产路径 level-loaded 第 " + i + " 次应可重试");
            }
            Bug1GateProbeLogic.Bug1RetryDecision prodExhausted =
                Bug1GateProbe.__TestAccountRetry(Bug1GateProbeLogic.Bug1RetryTrigger.LevelLoaded);
            Assert(!prodExhausted.Retry, "生产路径 level-loaded 用满后必须拒绝继续重试");

            Bug1GateProbeLogic.Bug1RetryDecision prodPlayer =
                Bug1GateProbe.__TestAccountRetry(Bug1GateProbeLogic.Bug1RetryTrigger.PlayerCreated);
            Assert(prodPlayer.Retry,
                "实机缺陷复现：level-loaded 3 次拒绝后，player-created 仍必须可重试（ARMED 而非 GIVEUP）");
            int levelUsed;
            int playerUsed;
            Bug1GateProbe.__TestRetryBudget(out levelUsed, out playerUsed);
            Assert(levelUsed == max, "生产路径 level-loaded 计数应为 " + max + "，实际 " + levelUsed);
            Assert(playerUsed == 1, "生产路径 player-created 计数应独立为 1，实际 " + playerUsed);

            Bug1GateProbe.__TestResetRetryBudget();

            // 接线：TryRetry 必须经 AccountRetry 记账（删掉这条边即变红）。
            MethodInfo tryRetry = typeof(Bug1GateProbe).GetMethod(
                "TryRetry", BindingFlags.Static | BindingFlags.NonPublic);
            Assert(!ReferenceEquals(tryRetry, null), "必须存在 TryRetry");
            int wired = IlContractProbe.CountMethodCalls(
                tryRetry, typeof(Bug1GateProbe).FullName, "AccountRetry");
            Assert(wired >= 1, "TryRetry 必须经 AccountRetry 记账，实际=" + wired);
        }

        /// <summary>
        /// R21-02（t114）：触发源改成枚举后，两处「改错就静默失效」的隐患都要被判据抓住。
        /// 1) 枚举→计数器映射：LevelLoaded 只动 LevelLoaded，PlayerCreated 只动 PlayerCreated；
        /// 2) 回调里压栈的枚举常量必须是对应那个（IL 逐条读 ldc.i4 操作数），传错/传未知值即红；
        /// 3) UnhookRetrySource 遇未知源只记日志，两个订阅标志都不许变。
        /// </summary>
        public static void Test_R21_EnumTriggerIsGuarded()
        {
            const int max = 3;

            // 1) 枚举 → 计数器：各自只动自己那一格。
            var state = new Bug1GateProbeLogic.Bug1RetryBudgetState();
            Bug1GateProbeLogic.Bug1RetryDecision level =
                Bug1GateProbeLogic.DecideRetry(state, Bug1GateProbeLogic.Bug1RetryTrigger.LevelLoaded, max);
            Assert(level.Retry && level.Used == 1, "LevelLoaded 第一次应 Retry/used=1");
            Assert(state.LevelLoaded == 1 && state.PlayerCreated == 0,
                "LevelLoaded 只能动 LevelLoaded 计数，实际 " + state.LevelLoaded + "/" + state.PlayerCreated);
            Bug1GateProbeLogic.Bug1RetryDecision player =
                Bug1GateProbeLogic.DecideRetry(state, Bug1GateProbeLogic.Bug1RetryTrigger.PlayerCreated, max);
            Assert(player.Retry && player.Used == 1, "PlayerCreated 第一次应 Retry/used=1");
            Assert(state.LevelLoaded == 1 && state.PlayerCreated == 1,
                "PlayerCreated 只能动 PlayerCreated 计数，实际 " + state.LevelLoaded + "/" + state.PlayerCreated);

            // 展示名只用于日志，不参与判定；两个源必须可区分。
            Assert(Bug1GateProbeLogic.TriggerName(Bug1GateProbeLogic.Bug1RetryTrigger.LevelLoaded) == "level-loaded",
                "LevelLoaded 展示名应为 level-loaded");
            Assert(Bug1GateProbeLogic.TriggerName(Bug1GateProbeLogic.Bug1RetryTrigger.PlayerCreated) == "player-created",
                "PlayerCreated 展示名应为 player-created");

            // 2) 回调必须传对应枚举：把 ldc.i4 操作数钉死，传错/未知值即红。
            MethodInfo levelCallback = typeof(Bug1GateProbe).GetMethod(
                "OnLevelLoadedRetry", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo playerCallback = typeof(Bug1GateProbe).GetMethod(
                "OnPlayerCreatedRetry", BindingFlags.Static | BindingFlags.NonPublic);
            Assert(!ReferenceEquals(levelCallback, null), "必须存在 OnLevelLoadedRetry");
            Assert(!ReferenceEquals(playerCallback, null), "必须存在 OnPlayerCreatedRetry");
            Assert(TriggerLiteralBeforeTryRetry(levelCallback) == (int)Bug1GateProbeLogic.Bug1RetryTrigger.LevelLoaded,
                "OnLevelLoadedRetry 必须传 LevelLoaded（传错枚举=静默停摆）");
            Assert(TriggerLiteralBeforeTryRetry(playerCallback) == (int)Bug1GateProbeLogic.Bug1RetryTrigger.PlayerCreated,
                "OnPlayerCreatedRetry 必须传 PlayerCreated");

            // 3) 未知源不得摘除任何订阅。
            Bug1GateProbe.__TestResetRetryBudget();
            Bug1GateProbe.__TestSetRetryHookFlags(true, true);
            Bug1GateProbe.__TestUnhookRetrySource((Bug1GateProbeLogic.Bug1RetryTrigger)99);
            bool levelHooked;
            bool playerHooked;
            Bug1GateProbe.__TestRetryHookFlags(out levelHooked, out playerHooked);
            Assert(levelHooked, "未知触发源不得摘除 level-loaded 订阅");
            Assert(playerHooked, "未知触发源不得摘除 player-created 订阅（R21-02 核心）");

            // 已知源仍要各自摘：证明上面的断言不是「谁都不摘」的假绿。
            Bug1GateProbe.__TestUnhookRetrySource(Bug1GateProbeLogic.Bug1RetryTrigger.LevelLoaded);
            Bug1GateProbe.__TestRetryHookFlags(out levelHooked, out playerHooked);
            Assert(!levelHooked && playerHooked, "LevelLoaded 只摘自己，player-created 必须留着");
            Bug1GateProbe.__TestUnhookRetrySource(Bug1GateProbeLogic.Bug1RetryTrigger.PlayerCreated);
            Bug1GateProbe.__TestRetryHookFlags(out levelHooked, out playerHooked);
            Assert(!levelHooked && !playerHooked, "PlayerCreated 只摘自己");

            Bug1GateProbe.__TestResetRetryBudget();
        }

        /// <summary>
        /// 读「call TryRetry 之前压栈的那个 int 常量」——枚举实参在 IL 里就是 ldc.i4。
        /// 找不到 call 或前面不是常量返回 -1。
        /// </summary>
        private static int TriggerLiteralBeforeTryRetry(MethodInfo method)
        {
            List<IlContractProbe.IlInstruction> instructions = IlContractProbe.ReadInstructions(method);
            if (ReferenceEquals(instructions, null)) return -1;
            for (int i = 0; i < instructions.Count; i++)
            {
                if (!IlContractProbe.IsCallTo(instructions[i], method, typeof(Bug1GateProbe).FullName, "TryRetry"))
                    continue;
                for (int j = i - 1; j >= 0; j--)
                {
                    OpCode op = instructions[j].OpCode;
                    if (op == OpCodes.Nop) continue;
                    if (op == OpCodes.Ldc_I4_0) return 0;
                    if (op == OpCodes.Ldc_I4_1) return 1;
                    if (op == OpCodes.Ldc_I4_2) return 2;
                    if (op == OpCodes.Ldc_I4_S || op == OpCodes.Ldc_I4) return instructions[j].Operand;
                    return -1;
                }
            }
            return -1;
        }
    }
}
