using System;
using System.Collections.Generic;

namespace SteamP2PFriends.Adapters.Item
{
    /// <summary>E02 §2：raw 模式输入，仅外部原始值，不含身份结论。</summary>
    public readonly struct ItemModeRaw
    {
        public ItemModeRaw(bool dedicated, bool isServer, bool p2p, bool writerOwnsSession)
        {
            Dedicated = dedicated;
            IsServer = isServer;
            P2P = p2p;
            WriterOwnsSession = writerOwnsSession;
        }

        public bool Dedicated { get; }
        public bool IsServer { get; }
        public bool P2P { get; }
        public bool WriterOwnsSession { get; }

        internal bool IsCandidate => !Dedicated && IsServer && (WriterOwnsSession || P2P);
    }

    /// <summary>当前 clients 中单条 raw 记录。</summary>
    public sealed class ItemClientRaw
    {
        public object Owner;
        public object Player;
        public object Connection;
        public ulong SteamId;
    }

    /// <summary>E02 §2：raw 绑定输入，仅引用与原始值，不含 IdentityKnown 等结论。</summary>
    public sealed class ItemBindingRaw
    {
        public object CallbackPlayer;
        public object CallbackOwner;
        public object CallbackOwnerPlayer;
        public object CallbackTransport;
        public bool CallbackIsLocalPlayer;
        public ulong CallbackOwnerSteamId;
        public object ProviderClient;
        public ulong ProviderClientSteamId;
        public List<ItemClientRaw> Clients;
        public int LoadedWidth;
        public int LoadedHeight;
        public Func<byte, byte, bool> HasLoadedCell;
        public Func<byte, byte, bool> CanProjectCell;
        public Action<byte, byte> ProjectCell;
        public object CommittedOwner;
        public object CommittedTransport;
        public ulong CommittedToken;
    }

    /// <summary>E02 §2：native adapter 契约，测试提供原始表实现。</summary>
    public interface IItemStepFiveNative
    {
        ItemModeRaw ReadMode();
        ItemBindingRaw ReadBinding(object playerKey);
    }
}
