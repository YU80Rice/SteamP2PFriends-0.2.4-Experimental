using SteamP2PFriends.MultiObserver.Demand;
using System.Collections.Generic;
using System.Linq;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// 票 04：逐条样本准入策略的纯内存契约。它把「一条记录读不全」与「整批不可判定」分开：
    /// 前者只暂缓它自己的观察者，后者才整批暂缓；身份读不到会关上本拍的缺席移除资格。
    /// 这条接缝是协调器与共享引擎之间的准入决策，不触达原生状态。
    /// </summary>
    internal static class ObserverSampleAdmissionTests
    {
        internal static bool Test_SAM01_SingleUnusableRecordOnlyDefersItsObserver()
        {
            var records = new List<ObserverSampleRecord>
            {
                new ObserverSampleRecord(100UL, usable: true),
                new ObserverSampleRecord(200UL, usable: false),
                new ObserverSampleRecord(300UL, usable: true)
            };

            ObserverSampleAdmissionPlan plan = ObserverSampleAdmission.Plan(records, true, string.Empty);

            return !plan.IsBatchRejected
                && plan.AllowAbsenceRemoval
                && plan.AdmissibleObserverIds.Count == 2
                && plan.AdmissibleObserverIds.Contains(100UL)
                && plan.AdmissibleObserverIds.Contains(300UL)
                && plan.DeferredObserverIds.Count == 1
                && plan.DeferredObserverIds.Contains(200UL)
                && plan.IgnoredDuplicateCount == 0;
        }

        /// <summary>
        /// 整批不可判定（客户端名册缺失/超限/拷贝期间变化）时谁都不提交、谁都不移除：
        /// 此时没有「谁在场」的证据，任何缺席推断都会误关世界。
        /// </summary>
        internal static bool Test_SAM02_BatchFailureDefersEveryone()
        {
            var records = new List<ObserverSampleRecord>
            {
                new ObserverSampleRecord(100UL, usable: true)
            };

            ObserverSampleAdmissionPlan plan = ObserverSampleAdmission.Plan(
                records, false, "client-list-unavailable");

            return plan.IsBatchRejected
                && plan.BatchRejectReason == "client-list-unavailable"
                && !plan.AllowAbsenceRemoval
                && plan.AdmissibleObserverIds.Count == 0
                && plan.DeferredObserverIds.Count == 0;
        }

        /// <summary>
        /// 身份不可读的记录既不能提交也不能移除：它关上本拍的缺席移除资格，但不影响其它
        /// 有效记录的准入；同一观察者的重复记录不改变结论，只计入忽略数。
        /// </summary>
        internal static bool Test_SAM03_UnknownIdentityAndDuplicatesKeepOtherRecords()
        {
            var records = new List<ObserverSampleRecord>
            {
                new ObserverSampleRecord(0UL, usable: false),
                new ObserverSampleRecord(100UL, usable: true),
                new ObserverSampleRecord(100UL, usable: false),
                new ObserverSampleRecord(200UL, usable: false),
                new ObserverSampleRecord(200UL, usable: true)
            };

            ObserverSampleAdmissionPlan plan = ObserverSampleAdmission.Plan(records, true, string.Empty);

            return !plan.IsBatchRejected
                && !plan.AllowAbsenceRemoval
                && plan.AdmissibleObserverIds.Count == 2
                && plan.AdmissibleObserverIds.Contains(100UL)
                && plan.AdmissibleObserverIds.Contains(200UL)
                && plan.DeferredObserverIds.Count == 0
                && plan.IgnoredDuplicateCount == 2;
        }
    }
}
