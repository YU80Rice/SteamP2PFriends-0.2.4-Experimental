using System;
using System.Collections.Generic;

namespace SteamP2PFriends.MultiObserver.Demand
{
    /// <summary>
    /// 单条观察者样本记录的准入输入：该记录是否可用，以及能否读到它的观察者身份。
    /// 读不到的记录不是「零需求」，也不是「已离开」——它是不可判定的观测。
    /// </summary>
    public readonly struct ObserverSampleRecord
    {
        public ObserverSampleRecord(ulong observerId, bool usable)
        {
            ObserverId = observerId;
            Usable = usable;
        }

        /// <summary>该记录归属的观察者；身份读不到时为 0。</summary>
        public ulong ObserverId { get; }

        /// <summary>该记录是否可用（原生字段齐备且自洽）。</summary>
        public bool Usable { get; }
    }

    /// <summary>
    /// 一次样本捕获的准入计划：哪些记录可提交、哪些观察者进入 Deferred Observer Demand、
    /// 以及本拍是否还能按「缺席」移除观察者。逐条判定，因此单条坏记录只影响它自己。
    /// </summary>
    public sealed class ObserverSampleAdmissionPlan
    {
        internal ObserverSampleAdmissionPlan(
            bool isBatchRejected,
            string batchRejectReason,
            bool allowAbsenceRemoval,
            ulong[] admissibleObserverIds,
            ulong[] deferredObserverIds,
            int ignoredDuplicateCount)
        {
            IsBatchRejected = isBatchRejected;
            BatchRejectReason = batchRejectReason ?? string.Empty;
            AllowAbsenceRemoval = allowAbsenceRemoval;
            AdmissibleObserverIds = admissibleObserverIds;
            DeferredObserverIds = deferredObserverIds;
            IgnoredDuplicateCount = ignoredDuplicateCount;
        }

        /// <summary>整批不可判定（客户端名册不可用）：既不提交也不移除任何观察者。</summary>
        public bool IsBatchRejected { get; }

        public string BatchRejectReason { get; }

        /// <summary>
        /// 本拍是否可以按「缺席」移除观察者。只有当本拍没有任何身份不可读的记录时才成立——
        /// 读不到身份就无法证明谁在场，也就不具备移除资格。
        /// </summary>
        public bool AllowAbsenceRemoval { get; }

        /// <summary>可提交的观察者身份（本拍有可用记录）。</summary>
        public IReadOnlyList<ulong> AdmissibleObserverIds { get; }

        /// <summary>进入 Deferred Observer Demand 的观察者身份（本拍记录不可用，但不代表离开）。</summary>
        public IReadOnlyList<ulong> DeferredObserverIds { get; }

        /// <summary>同一观察者的重复记录数（首次记录已被采纳，重复记录不再改变结论）。</summary>
        public int IgnoredDuplicateCount { get; }
    }

    /// <summary>
    /// 逐条样本准入策略：把「一条读不全的记录」与「整批不可判定」分开。
    ///
    /// 单条记录不可用只暂缓它自己的观察者（Deferred Observer Demand），其它有效观察者照常提交；
    /// 只有客户端名册本身不可用（容量超限、拷贝期间变化、构建异常）才是整批不可判定，此时
    /// 既不提交也不移除任何人。身份不可读的记录会关上本拍的「缺席移除」资格。
    /// </summary>
    public static class ObserverSampleAdmission
    {
        public static ObserverSampleAdmissionPlan Plan(
            IReadOnlyList<ObserverSampleRecord> records,
            bool batchUsable,
            string batchFailureReason)
        {
            if (!batchUsable)
            {
                return new ObserverSampleAdmissionPlan(
                    true, batchFailureReason, false,
                    Array.Empty<ulong>(), Array.Empty<ulong>(), 0);
            }

            var admissible = new List<ulong>();
            var deferred = new List<ulong>();
            var admissibleSet = new HashSet<ulong>();
            var deferredSet = new HashSet<ulong>();
            bool allowAbsenceRemoval = true;
            int ignoredDuplicates = 0;
            int count = records?.Count ?? 0;
            for (int index = 0; index < count; index++)
            {
                ObserverSampleRecord record = records[index];
                if (record.ObserverId == 0UL)
                {
                    // 身份读不到：无法证明谁在场，因此本拍不具备按缺席移除的资格；
                    // 也不知道该把这次不可用记到谁头上，只能不动任何人的贡献。
                    allowAbsenceRemoval = false;
                    continue;
                }

                if (HasSeenRecord(admissibleSet, deferredSet, record.ObserverId))
                {
                    ignoredDuplicates++;
                }

                if (!record.Usable)
                {
                    // 不可用但不等于离开：进入 Deferred Observer Demand，保留既有贡献。
                    if (!admissibleSet.Contains(record.ObserverId) && deferredSet.Add(record.ObserverId))
                        deferred.Add(record.ObserverId);
                    continue;
                }

                if (admissibleSet.Add(record.ObserverId))
                {
                    admissible.Add(record.ObserverId);
                    if (deferredSet.Remove(record.ObserverId)) deferred.Remove(record.ObserverId);
                }
            }

            return new ObserverSampleAdmissionPlan(
                false, string.Empty, allowAbsenceRemoval,
                admissible.ToArray(), deferred.ToArray(), ignoredDuplicates);
        }

        private static bool HasSeenRecord(
            HashSet<ulong> admissible, HashSet<ulong> deferred, ulong observerId)
        {
            return admissible.Contains(observerId) || deferred.Contains(observerId);
        }
    }
}
