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
        private int _lastOrder;
        private bool _closed;

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
            IReadOnlyList<PatchRegistrationStage> stages, Action verify)
        {
            AdapterClosure = adapterClosure ?? throw new ArgumentNullException(nameof(adapterClosure));
            Stages = stages ?? throw new ArgumentNullException(nameof(stages));
            Verify = verify ?? throw new ArgumentNullException(nameof(verify));
        }

        public RegistrationClosure AdapterClosure { get; }
        public IReadOnlyList<PatchRegistrationStage> Stages { get; }
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

            var stageCatalog = new PatchRegistrationStageCatalog();
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
}
