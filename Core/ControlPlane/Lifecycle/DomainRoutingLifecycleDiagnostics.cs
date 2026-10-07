using SteamP2PFriends.Core.Identity;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends.MultiObserver.Lifecycle
{
    /// <summary>
    /// 领域路由诊断出口：按转换记录自带的 <see cref="LifecycleDiagnostic.Domain"/> 选择
    /// 真正的领域出口。路由表由生产接线以数据注入——本类不读取任何领域常量、不认识任何
    /// 具体领域；未登记的领域（含会话边界这类引擎级转换）交给 fallback 出口。
    /// 诊断永远只观察不参与：路由不改写记录内容，也不影响任何控制流。
    /// </summary>
    public sealed class DomainRoutingLifecycleDiagnostics : ILifecycleDiagnostics
    {
        private readonly Dictionary<DomainId, ILifecycleDiagnostics> _routes;
        private readonly ILifecycleDiagnostics _fallback;

        public DomainRoutingLifecycleDiagnostics(
            IReadOnlyDictionary<DomainId, ILifecycleDiagnostics> routes,
            ILifecycleDiagnostics fallback)
        {
            if (routes == null) throw new ArgumentNullException(nameof(routes));
            // .NET 4.7.2 的 Dictionary 构造函数没有 IReadOnlyDictionary 重载，手动复制。
            _routes = new Dictionary<DomainId, ILifecycleDiagnostics>();
            foreach (KeyValuePair<DomainId, ILifecycleDiagnostics> route in routes)
            {
                if (route.Value == null)
                    throw new ArgumentException("路由表不得包含空出口", nameof(routes));
                _routes.Add(route.Key, route.Value);
            }
            _fallback = fallback ?? DefaultLifecycleDiagnostics.Instance;
        }

        /// <summary>按记录携带的领域路由；未登记领域（含引擎级转换）走 fallback。</summary>
        public void Transition(LifecycleDiagnostic diagnostic)
        {
            if (diagnostic == null) return;
            Resolve(diagnostic.Domain).Transition(diagnostic);
        }

        /// <summary>去重语义由目标出口实现，路由只保证领域身份原样到达。</summary>
        public void TransitionOnce(string onceKey, LifecycleDiagnostic diagnostic)
        {
            if (diagnostic == null) return;
            Resolve(diagnostic.Domain).TransitionOnce(onceKey, diagnostic);
        }

        private ILifecycleDiagnostics Resolve(DomainId domain)
        {
            ILifecycleDiagnostics route;
            return _routes.TryGetValue(domain, out route) ? route : _fallback;
        }
    }
}
