using System;

namespace SteamP2PFriends.Adapters.Item
{
    internal enum ItemSpawnProgress : byte
    {
        NotStarted = 0,
        Registered = 1,
        Uncertain = 2
    }

    /// <summary>E02 §8.2：反射分类结果——即便 Registered 也保留后置失败。</summary>
    internal readonly struct ItemSpawnResult
    {
        internal ItemSpawnResult(ItemSpawnProgress progress, Exception failure, string reason)
        {
            Progress = progress;
            Failure = failure;
            Reason = reason ?? string.Empty;
        }

        internal ItemSpawnProgress Progress { get; }
        internal Exception Failure { get; }
        internal string Reason { get; }
    }
}
