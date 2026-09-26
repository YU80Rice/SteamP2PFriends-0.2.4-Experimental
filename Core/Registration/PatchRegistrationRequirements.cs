using SteamP2PFriends.Core.Identity;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SteamP2PFriends.Core.Registration
{
    /// <summary>
    /// Patch Registration Closure 的生产要求清单唯一来源。
    /// 插件启动装配与 PureMemory 契约测试共用同一份清单，
    /// 防止「要求清单」与「领域登记目录」在退役/新增领域时漂移。
    /// </summary>
    public static class PatchRegistrationRequirements
    {
        public static IReadOnlyList<RegistrationRequirement> Create()
        {
            return new ReadOnlyCollection<RegistrationRequirement>(new[]
            {
                new RegistrationRequirement(DomainIds.Item, true, true),
                new RegistrationRequirement(DomainIds.Resource, true, true),
                new RegistrationRequirement(DomainIds.Building, true, true),
                new RegistrationRequirement(DomainIds.Zombie, true, true),
                new RegistrationRequirement(DomainIds.Animal, true, true)
            });
        }
    }
}
