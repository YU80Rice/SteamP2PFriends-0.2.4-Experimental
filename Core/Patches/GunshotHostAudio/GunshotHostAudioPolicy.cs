using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SteamP2PFriends.Core.Patches.GunshotHostAudio
{
    /// <summary>
    /// t116：Host 本机枪声叶的门控事实（纯数据，可在无 Unity 的测试进程里构造）。
    /// 任一项为 false 都不得补叶；全部为 true 才允许进入补偿路径。
    /// </summary>
    internal struct GunshotGateFacts
    {
        internal bool P2PHostMode;
        internal bool ListenHostActive;
        internal bool NotDedicated;
        internal bool Connected;
        internal bool ServerActive;
        internal bool LevelLoaded;
        internal bool ShooterNotLocal;
        internal bool RealSendAnchor;
        internal bool ProjectileNull;
        internal bool TurretFalse;
        internal bool WeaponInstanceValid;
        internal bool AudioSourcePresent;
        internal bool ClipPresent;
        internal bool NativeLeafNotEntered;
        internal bool AssociationComplete;
        internal string CollectionError;
    }

    /// <summary>
    /// t116：一次 fire 尝试的 P2 键。
    /// 键 = run/sessionEpoch + connectionGeneration + weaponInstanceGeneration + NetId + hostFireOrdinal。
    /// ordinal 在 UseableGun.fire() Prefix 入口分配，允许失败形成空洞（只增不减）。
    /// </summary>
    internal readonly struct GunshotFireKey : IEquatable<GunshotFireKey>
    {
        internal readonly ulong SessionEpoch;
        internal readonly uint ConnectionGeneration;
        internal readonly uint WeaponInstanceGeneration;
        internal readonly uint NetId;
        internal readonly long HostFireOrdinal;

        internal GunshotFireKey(ulong sessionEpoch, uint connectionGeneration, uint weaponInstanceGeneration, uint netId, long hostFireOrdinal)
        {
            SessionEpoch = sessionEpoch;
            ConnectionGeneration = connectionGeneration;
            WeaponInstanceGeneration = weaponInstanceGeneration;
            NetId = netId;
            HostFireOrdinal = hostFireOrdinal;
        }

        public bool Equals(GunshotFireKey other)
        {
            return SessionEpoch == other.SessionEpoch
                && ConnectionGeneration == other.ConnectionGeneration
                && WeaponInstanceGeneration == other.WeaponInstanceGeneration
                && NetId == other.NetId
                && HostFireOrdinal == other.HostFireOrdinal;
        }

        public override bool Equals(object obj)
        {
            return obj is GunshotFireKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)(SessionEpoch ^ (SessionEpoch >> 32));
                hash = (hash * 397) ^ (int)ConnectionGeneration;
                hash = (hash * 397) ^ (int)WeaponInstanceGeneration;
                hash = (hash * 397) ^ (int)NetId;
                hash = (hash * 397) ^ (int)HostFireOrdinal;
                hash = (hash * 397) ^ (int)(HostFireOrdinal >> 32);
                return hash;
            }
        }

        public override string ToString()
        {
            return "epoch=" + SessionEpoch.ToString(CultureInfo.InvariantCulture)
                + " connGen=" + ConnectionGeneration.ToString(CultureInfo.InvariantCulture)
                + " weaponGen=" + WeaponInstanceGeneration.ToString(CultureInfo.InvariantCulture)
                + " netId=" + NetId.ToString(CultureInfo.InvariantCulture)
                + " ordinal=" + HostFireOrdinal.ToString(CultureInfo.InvariantCulture);
        }
    }

    internal enum GunshotAttemptState
    {
        Unclaimed = 0,
        ClaimedBeforeCall = 1,
        Completed = 2,
        Failed = 3,
    }

    /// <summary>
    /// t116：去重账本。唯一允许的状态迁移：
    ///   Unclaimed -> ClaimedBeforeCall（TryClaim，必须在叶调用之前）
    ///   ClaimedBeforeCall -> Completed（叶已进入并返回）
    ///   ClaimedBeforeCall -> Failed（叶抛出/异常退出；不重试）
    /// 同一键一旦离开 Unclaimed，永不再执行补偿叶。
    /// </summary>
    internal static class GunshotHostAudioLedger
    {
        private static readonly Dictionary<GunshotFireKey, GunshotAttemptState> _states =
            new Dictionary<GunshotFireKey, GunshotAttemptState>();

        // 插入顺序队列：字典有界（MaxEntries），超限时淘汰最旧键；
        // 世代/连接变化时按世代撤销旧键，避免字典随发数无界增长。
        private static readonly Queue<GunshotFireKey> _insertionOrder = new Queue<GunshotFireKey>();

        internal const int MaxEntries = 1024;

        internal static int Count => _states.Count;

        // R24-07：清理后队列必须与字典同步重建，否则旧键会永久占住队列条目。
        internal static int InsertionQueueCount => _insertionOrder.Count;

        internal static bool TryClaim(GunshotFireKey key)
        {
            GunshotAttemptState state;
            if (_states.TryGetValue(key, out state) && state != GunshotAttemptState.Unclaimed)
            {
                return false;
            }

            if (!_states.ContainsKey(key))
            {
                // R24-07：队列里可能残留已被 PurgeExcept/PurgeWeapon 删除的键；
                // 淘汰时跳过它们，避免「Dequeue 一个已不存在的键」导致本轮不淘汰、
                // 字典继续增长。RebuildInsertionOrder 保证正常路径下队列与字典同步。
                while (_states.Count >= MaxEntries && _insertionOrder.Count > 0)
                {
                    GunshotFireKey oldest = _insertionOrder.Dequeue();
                    if (_states.Remove(oldest))
                    {
                        break;
                    }
                }

                _insertionOrder.Enqueue(key);
            }

            _states[key] = GunshotAttemptState.ClaimedBeforeCall;
            return true;
        }

        /// <summary>
        /// 世代/连接切换时撤销旧键：只保留当前 sessionEpoch + connectionGeneration 的键。
        /// 当前世代的去重不受影响（键相同则保留）。
        /// </summary>
        internal static int PurgeExcept(ulong sessionEpoch, uint connectionGeneration)
        {
            var stale = new List<GunshotFireKey>();
            foreach (KeyValuePair<GunshotFireKey, GunshotAttemptState> pair in _states)
            {
                if (pair.Key.SessionEpoch != sessionEpoch
                    || pair.Key.ConnectionGeneration != connectionGeneration)
                {
                    stale.Add(pair.Key);
                }
            }

            for (int i = 0; i < stale.Count; i++)
            {
                _states.Remove(stale[i]);
            }

            if (stale.Count > 0)
            {
                RebuildInsertionOrder();
            }

            return stale.Count;
        }

        /// <summary>
        /// 同一 NetId 的武器实例换代后，撤销旧 weaponGeneration 的键。
        /// </summary>
        internal static int PurgeWeapon(uint netId, uint keepWeaponGeneration)
        {
            var stale = new List<GunshotFireKey>();
            foreach (KeyValuePair<GunshotFireKey, GunshotAttemptState> pair in _states)
            {
                if (pair.Key.NetId == netId
                    && pair.Key.WeaponInstanceGeneration < keepWeaponGeneration)
                {
                    stale.Add(pair.Key);
                }
            }

            for (int i = 0; i < stale.Count; i++)
            {
                _states.Remove(stale[i]);
            }

            if (stale.Count > 0)
            {
                RebuildInsertionOrder();
            }

            return stale.Count;
        }

        private static void RebuildInsertionOrder()
        {
            _insertionOrder.Clear();
            foreach (GunshotFireKey key in _states.Keys)
            {
                _insertionOrder.Enqueue(key);
            }
        }

        internal static bool Complete(GunshotFireKey key)
        {
            return Transition(key, GunshotAttemptState.Completed);
        }

        internal static bool Fail(GunshotFireKey key)
        {
            return Transition(key, GunshotAttemptState.Failed);
        }

        internal static GunshotAttemptState StateOf(GunshotFireKey key)
        {
            GunshotAttemptState state;
            return _states.TryGetValue(key, out state) ? state : GunshotAttemptState.Unclaimed;
        }

        internal static void Reset()
        {
            _states.Clear();
            _insertionOrder.Clear();
        }

        private static bool Transition(GunshotFireKey key, GunshotAttemptState target)
        {
            GunshotAttemptState state;
            if (!_states.TryGetValue(key, out state) || state != GunshotAttemptState.ClaimedBeforeCall)
            {
                return false;
            }

            _states[key] = target;
            return true;
        }
    }

    internal static class GunshotHostAudioPolicy
    {
        internal static bool IsEligible(GunshotGateFacts facts)
        {
            return facts.P2PHostMode
                && facts.ListenHostActive
                && facts.NotDedicated
                && facts.Connected
                && facts.ServerActive
                && facts.LevelLoaded
                && facts.ShooterNotLocal
                && facts.RealSendAnchor
                && facts.ProjectileNull
                && facts.TurretFalse
                && facts.WeaponInstanceValid
                && facts.AudioSourcePresent
                && facts.ClipPresent
                && facts.NativeLeafNotEntered
                && facts.AssociationComplete;
        }

        /// <summary>
        /// 单行门控日志：15 项布尔 + 键（epoch/connGen/weaponGen/netId/ordinal）+ 地图 + decision。
        /// 只在每次 fire 的判定点输出一条，不做逐帧刷屏。
        /// </summary>
        internal static string FormatGateLine(GunshotGateFacts facts, GunshotFireKey key, string decision, string mapName)
        {
            var sb = new StringBuilder(384);
            sb.Append("gate");
            sb.Append(" p2pHost=").Append(Flag(facts.P2PHostMode));
            sb.Append(" listenHost=").Append(Flag(facts.ListenHostActive));
            sb.Append(" notDedicated=").Append(Flag(facts.NotDedicated));
            sb.Append(" connected=").Append(Flag(facts.Connected));
            sb.Append(" server=").Append(Flag(facts.ServerActive));
            sb.Append(" levelLoaded=").Append(Flag(facts.LevelLoaded));
            sb.Append(" shooterNotLocal=").Append(Flag(facts.ShooterNotLocal));
            sb.Append(" sendAnchor=").Append(Flag(facts.RealSendAnchor));
            sb.Append(" projectileNull=").Append(Flag(facts.ProjectileNull));
            sb.Append(" turretFalse=").Append(Flag(facts.TurretFalse));
            sb.Append(" weaponValid=").Append(Flag(facts.WeaponInstanceValid));
            sb.Append(" audioSource=").Append(Flag(facts.AudioSourcePresent));
            sb.Append(" clip=").Append(Flag(facts.ClipPresent));
            sb.Append(" leafNotEntered=").Append(Flag(facts.NativeLeafNotEntered));
            sb.Append(" association=").Append(Flag(facts.AssociationComplete));
            sb.Append(" epoch=").Append(key.SessionEpoch.ToString(CultureInfo.InvariantCulture));
            sb.Append(" connGen=").Append(key.ConnectionGeneration.ToString(CultureInfo.InvariantCulture));
            sb.Append(" weaponGen=").Append(key.WeaponInstanceGeneration.ToString(CultureInfo.InvariantCulture));
            sb.Append(" netId=").Append(key.NetId.ToString(CultureInfo.InvariantCulture));
            sb.Append(" ordinal=").Append(key.HostFireOrdinal.ToString(CultureInfo.InvariantCulture));
            sb.Append(" map=").Append(string.IsNullOrEmpty(mapName) ? "<null>" : mapName);
            sb.Append(" decision=").Append(string.IsNullOrEmpty(decision) ? "NotEligible" : decision);
            if (!string.IsNullOrEmpty(facts.CollectionError))
            {
                sb.Append(" collectError=").Append(facts.CollectionError);
            }

            return sb.ToString();
        }

        internal static string DecisionFor(GunshotGateFacts facts)
        {
            return IsEligible(facts) ? "Compensate" : "NotEligible";
        }

        private static string Flag(bool value)
        {
            return value ? "1" : "0";
        }
    }
}
