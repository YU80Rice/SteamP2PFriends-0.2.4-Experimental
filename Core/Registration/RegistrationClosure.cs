using SteamP2PFriends.MultiObserver.SPI;
using SteamP2PFriends.Core.Identity;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SteamP2PFriends.Core.Registration
{
    /// <summary>
    /// Registration Closure 中登记的适配器角色。
    /// 生命周期与状态复制是两个独立角色，即使由同一个适配器实现也必须分别登记。
    /// </summary>
    public enum RegistrationRole
    {
        Lifecycle,
        Replication
    }

    /// <summary>
    /// 一个领域在当前插件会话中需要的注册角色声明。
    /// </summary>
    public sealed class RegistrationRequirement
    {
        public RegistrationRequirement(DomainId domainId, bool requiresLifecycle, bool requiresReplication)
        {
            if (!domainId.IsDefined)
                throw new ArgumentException("Domain Id 不能为空", nameof(domainId));
            if (!requiresLifecycle && !requiresReplication)
                throw new ArgumentException("领域至少需要一个注册角色", nameof(requiresLifecycle));

            DomainId = domainId;
            RequiresLifecycle = requiresLifecycle;
            RequiresReplication = requiresReplication;
        }

        public DomainId DomainId { get; }
        public bool RequiresLifecycle { get; }
        public bool RequiresReplication { get; }
    }

    /// <summary>
    /// 可审计的适配器登记结果。它只暴露身份和顺序，不暴露运行时适配器实例。
    /// </summary>
    public sealed class RegistrationRecord
    {
        internal RegistrationRecord(DomainId domainId, RegistrationRole role, string capability,
            int order, string adapterTypeName)
        {
            DomainId = domainId;
            Role = role;
            Capability = capability ?? string.Empty;
            Order = order;
            AdapterTypeName = adapterTypeName ?? string.Empty;
        }

        public DomainId DomainId { get; }
        public RegistrationRole Role { get; }
        public string Capability { get; }
        public int Order { get; }
        public string AdapterTypeName { get; }
    }

    /// <summary>
    /// 插件会话级适配器登记模块。
    ///
    /// 该模块是一个深模块：调用者只需登记两个角色并关闭，未知身份、重复角色、
    /// 角色顺序、缺失角色和关闭后的变更均由内部验证完成。关闭后快照和适配器集合不可变。
    /// </summary>
    public sealed class RegistrationClosure
    {
        private sealed class RegistrationSlot
        {
            public ILifecycleDomainAdapter Lifecycle;
            public IStateReplicationAdapter Replication;
        }

        private readonly Dictionary<DomainId, RegistrationRequirement> _requirements;
        private readonly Dictionary<DomainId, RegistrationSlot> _slots =
            new Dictionary<DomainId, RegistrationSlot>();
        private readonly List<RegistrationRecord> _records = new List<RegistrationRecord>();
        private bool _closed;

        public RegistrationClosure(IEnumerable<RegistrationRequirement> requirements)
        {
            if (requirements == null) throw new ArgumentNullException(nameof(requirements));

            _requirements = new Dictionary<DomainId, RegistrationRequirement>();
            foreach (RegistrationRequirement requirement in requirements)
            {
                if (requirement == null) throw new ArgumentException("注册要求不能为 null", nameof(requirements));
                if (_requirements.ContainsKey(requirement.DomainId))
                    throw new ArgumentException("重复的 Domain Id: " + requirement.DomainId, nameof(requirements));
                _requirements.Add(requirement.DomainId, requirement);
            }
        }

        public bool IsClosed => _closed;

        public IReadOnlyList<RegistrationRecord> Snapshot
        {
            get
            {
                return new ReadOnlyCollection<RegistrationRecord>(
                    new List<RegistrationRecord>(_records));
            }
        }

        public bool TryRegisterLifecycle(ILifecycleDomainAdapter adapter, out string failure)
        {
            return TryRegister(adapter?.DomainId ?? default(DomainId), RegistrationRole.Lifecycle,
                adapter?.Capability, adapter, null, out failure);
        }

        public bool TryRegisterReplication(IStateReplicationAdapter adapter, out string failure)
        {
            return TryRegister(adapter?.DomainId ?? default(DomainId), RegistrationRole.Replication,
                string.Empty, null, adapter, out failure);
        }

        public bool TryClose(out string failure)
        {
            if (_closed)
            {
                failure = "Registration Closure 已经关闭";
                return false;
            }

            foreach (RegistrationRequirement requirement in _requirements.Values)
            {
                if (!_slots.TryGetValue(requirement.DomainId, out RegistrationSlot slot))
                {
                    failure = "Domain Id 未登记: " + requirement.DomainId;
                    return false;
                }
                if (requirement.RequiresLifecycle && slot.Lifecycle == null)
                {
                    failure = "缺少生命周期角色: " + requirement.DomainId;
                    return false;
                }
                if (requirement.RequiresReplication && slot.Replication == null)
                {
                    failure = "缺少状态复制角色: " + requirement.DomainId;
                    return false;
                }
            }

            _closed = true;
            failure = string.Empty;
            return true;
        }

        public bool TryGetLifecycle(DomainId domainId, out ILifecycleDomainAdapter adapter)
        {
            adapter = null;
            if (!domainId.IsDefined || !_closed) return false;
            return _slots.TryGetValue(domainId, out RegistrationSlot slot) &&
                (adapter = slot.Lifecycle) != null;
        }

        public bool TryGetReplication(DomainId domainId, out IStateReplicationAdapter adapter)
        {
            adapter = null;
            if (!domainId.IsDefined || !_closed) return false;
            return _slots.TryGetValue(domainId, out RegistrationSlot slot) &&
                (adapter = slot.Replication) != null;
        }

        private bool TryRegister(DomainId domainId, RegistrationRole role, string capability,
            ILifecycleDomainAdapter lifecycle, IStateReplicationAdapter replication, out string failure)
        {
            if (_closed)
            {
                failure = "Registration Closure 已关闭，不允许继续登记";
                return false;
            }
            if (!domainId.IsDefined || !_requirements.ContainsKey(domainId))
            {
                failure = "未知 Domain Id: " + (domainId.IsDefined ? domainId.Value : "<none>");
                return false;
            }
            if (role == RegistrationRole.Replication && lifecycle == null &&
                (!_slots.TryGetValue(domainId, out RegistrationSlot existing) || existing.Lifecycle == null))
            {
                failure = "注册顺序冲突：必须先登记生命周期角色: " + domainId;
                return false;
            }

            if (!_slots.TryGetValue(domainId, out RegistrationSlot slot))
            {
                slot = new RegistrationSlot();
                _slots.Add(domainId, slot);
            }
            if (role == RegistrationRole.Lifecycle && slot.Lifecycle != null)
            {
                failure = "重复登记生命周期角色: " + domainId;
                return false;
            }
            if (role == RegistrationRole.Replication && slot.Replication != null)
            {
                failure = "重复登记状态复制角色: " + domainId;
                return false;
            }

            if (role == RegistrationRole.Lifecycle)
                slot.Lifecycle = lifecycle;
            else
                slot.Replication = replication;

            _records.Add(new RegistrationRecord(domainId, role, capability,
                _records.Count + 1,
                (role == RegistrationRole.Lifecycle ? (object)lifecycle : replication).GetType().FullName));
            failure = string.Empty;
            return true;
        }
    }

    /// <summary>
    /// 兼容旧注册调用点的稳定领域身份入口。显示名称和能力文本不承担身份职责。
    /// </summary>
    public static class RegistrationDomainIds
    {
        public static readonly DomainId Item = DomainIds.Item;
        public static readonly DomainId Resource = DomainIds.Resource;
        public static readonly DomainId Building = DomainIds.Building;
        public static readonly DomainId Zombie = DomainIds.Zombie;
        public static readonly DomainId Animal = DomainIds.Animal;
        public static readonly DomainId Collision = DomainIds.Collision;
    }
}
