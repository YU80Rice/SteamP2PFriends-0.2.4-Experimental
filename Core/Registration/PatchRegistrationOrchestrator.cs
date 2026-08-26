using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SteamP2PFriends.Core.Registration
{
    internal sealed class PatchRegistrationStage
    {
        public PatchRegistrationStage(int order, string category, string traceId,
            string owner, string priority, string harmonyTarget, Action execute)
        {
            Order = order;
            Category = category ?? string.Empty;
            TraceId = traceId ?? string.Empty;
            Owner = owner ?? string.Empty;
            Priority = priority ?? string.Empty;
            HarmonyTarget = harmonyTarget ?? string.Empty;
            Execute = execute ?? throw new ArgumentNullException(nameof(execute));
        }

        public int Order { get; }
        public string Category { get; }
        public string TraceId { get; }
        public string Owner { get; }
        public string Priority { get; }
        public string HarmonyTarget { get; }
        public Action Execute { get; }
    }

    internal sealed class PatchRegistrationStageRecord
    {
        internal PatchRegistrationStageRecord(PatchRegistrationStage stage)
        {
            Order = stage.Order;
            Category = stage.Category;
            TraceId = stage.TraceId;
            Owner = stage.Owner;
            Priority = stage.Priority;
            HarmonyTarget = stage.HarmonyTarget;
        }

        public int Order { get; }
        public string Category { get; }
        public string TraceId { get; }
        public string Owner { get; }
        public string Priority { get; }
        public string HarmonyTarget { get; }
    }

    internal sealed class PatchRegistrationStageCatalog
    {
        private readonly List<PatchRegistrationStageRecord> _records =
            new List<PatchRegistrationStageRecord>();
        private readonly string _expectedOwner;
        private int _lastOrder;
        private bool _closed;

        public PatchRegistrationStageCatalog(string expectedOwner)
        {
            _expectedOwner = expectedOwner ?? string.Empty;
        }

        public IReadOnlyList<PatchRegistrationStageRecord> Snapshot =>
            new ReadOnlyCollection<PatchRegistrationStageRecord>(
                new List<PatchRegistrationStageRecord>(_records));

        public bool TryRegister(PatchRegistrationStage stage, out string failure)
        {
            if (_closed)
            {
                failure = "Patch registration stage catalog 已关闭";
                return false;
            }
            if (stage == null)
            {
                failure = "注册阶段不能为 null";
                return false;
            }
            if (!RegistrationTraceBaseline.TryValidate(stage, _expectedOwner, out failure))
                return false;
            if (_records.Count > 0 && stage.Order <= _lastOrder)
            {
                failure = "注册顺序冲突: " + stage.TraceId + " order=" + stage.Order +
                    " previous=" + _lastOrder;
                return false;
            }

            _records.Add(new PatchRegistrationStageRecord(stage));
            _lastOrder = stage.Order;
            failure = string.Empty;
            return true;
        }

        public bool TryClose(out string failure)
        {
            if (_closed)
            {
                failure = "Patch registration stage catalog 已关闭";
                return false;
            }
            _closed = true;
            failure = string.Empty;
            return true;
        }
    }

    internal sealed class PatchRegistrationPlan
    {
        public PatchRegistrationPlan(RegistrationClosure adapterClosure,
            IReadOnlyList<PatchRegistrationStage> stages, string harmonyOwner, Action verify)
        {
            AdapterClosure = adapterClosure ?? throw new ArgumentNullException(nameof(adapterClosure));
            Stages = stages ?? throw new ArgumentNullException(nameof(stages));
            HarmonyOwner = harmonyOwner ?? throw new ArgumentNullException(nameof(harmonyOwner));
            Verify = verify ?? throw new ArgumentNullException(nameof(verify));
        }

        public RegistrationClosure AdapterClosure { get; }
        public IReadOnlyList<PatchRegistrationStage> Stages { get; }
        public string HarmonyOwner { get; }
        public Action Verify { get; }
    }

    /// <summary>
    /// Patch Registration Orchestrator 顶层入口。
    /// 它只负责按 Registration Trace 编排阶段、关闭适配器注册表并触发最终验证；
    /// Harmony target 的具体登记仍由各既有注册实现完成。
    /// </summary>
    internal sealed class PatchRegistrationOrchestrator
    {
        public bool Execute(PatchRegistrationPlan plan, out string failure)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));

            var stageCatalog = new PatchRegistrationStageCatalog(plan.HarmonyOwner);
            foreach (PatchRegistrationStage stage in plan.Stages)
            {
                if (!stageCatalog.TryRegister(stage, out failure)) return false;
                try
                {
                    stage.Execute();
                }
                catch (Exception ex)
                {
                    failure = stage.TraceId + " 执行异常: " + ex.GetType().Name;
                    return false;
                }
            }

            if (!plan.AdapterClosure.TryClose(out failure)) return false;
            if (!stageCatalog.TryClose(out failure)) return false;

            try
            {
                plan.Verify();
            }
            catch (Exception ex)
            {
                failure = "注册后验证异常: " + ex.GetType().Name;
                return false;
            }

            failure = string.Empty;
            return true;
        }
    }

    /// <summary>
    /// Ticket 01 的阶段级静态基线。它不是 Harmony 运行时 metadata 的替代品，
    /// 而是防止编排入口悄然改写 trace id、分类、priority 或 target 摘要。
    /// </summary>
    internal static class RegistrationTraceBaseline
    {
        internal const string U3SdkCommit = "ea7b4973af5ba10f62baad2bfde36ab2e5b060eb";

        internal static bool TryValidate(PatchRegistrationStage stage, string expectedOwner,
            out string failure)
        {
            string category;
            string traceId;
            string priority;
            string harmonyTarget;
            switch (stage.Order)
            {
                case 1:
                    category = "Transport";
                    traceId = "U3-REG-01-Wrapper";
                    priority = "default";
                    harmonyTarget = "SteamNetworkingSockets/Callback wrappers";
                    break;
                case 2:
                    category = "Diagnostics";
                    traceId = "U3-REG-02-InternalDiagnostics";
                    priority = "default";
                    harmonyTarget = "internal NetMessages and lifecycle handlers";
                    break;
                case 3:
                    category = "Security";
                    traceId = "U3-REG-03-RouteB";
                    priority = "declared by patch";
                    harmonyTarget = "Route B admission and command permission";
                    break;
                case 4:
                    category = "Diagnostics";
                    traceId = "U3-REG-04-AssetAndAudit";
                    priority = "declared by patch";
                    harmonyTarget = "asset integrity and audit fixes";
                    break;
                case 5:
                    category = "Domain";
                    traceId = "U3-REG-05-WorldSyncAndAdapters";
                    priority = "declared by patch";
                    harmonyTarget = "world-sync reset, barricade lifecycle and adapter catalog";
                    break;
                case 6:
                    category = "Diagnostics";
                    traceId = "U3-REG-06-Probes";
                    priority = "n/a";
                    harmonyTarget = "Unity log bridge, SNS probe and redaction self-test";
                    break;
                case 7:
                    category = "Verification";
                    traceId = "U3-REG-07-ClosureAndVerification";
                    priority = "n/a";
                    harmonyTarget = "post-registration verification and closure";
                    break;
                default:
                    failure = "未知的 U3-SDK 注册阶段 order=" + stage.Order;
                    return false;
            }

            if (!string.Equals(stage.Owner, expectedOwner, StringComparison.Ordinal) ||
                !string.Equals(stage.Category, category, StringComparison.Ordinal) ||
                !string.Equals(stage.TraceId, traceId, StringComparison.Ordinal) ||
                !string.Equals(stage.Priority, priority, StringComparison.Ordinal) ||
                !string.Equals(stage.HarmonyTarget, harmonyTarget, StringComparison.Ordinal))
            {
                failure = "注册阶段 metadata 与 Ticket 01 基线不一致: " + stage.TraceId;
                return false;
            }

            failure = string.Empty;
            return true;
        }
    }
}
