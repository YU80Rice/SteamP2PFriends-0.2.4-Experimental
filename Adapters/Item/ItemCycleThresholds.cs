using SDG.Unturned;
using System;

namespace SteamP2PFriends.Adapters.Item
{
    public sealed class ItemCycleThresholds
    {
        public static ItemCycleThresholds Override { get; set; }

        public float DespawnNaturalTime { get; }
        public float DespawnDroppedTime { get; }
        public float RespawnTime { get; }
        public float SpawnChance { get; }
        public bool NativeCyclePermitted { get; }

        public ItemCycleThresholds(
            float despawnNaturalTime,
            float despawnDroppedTime,
            float respawnTime,
            float spawnChance,
            bool nativeCyclePermitted = true)
        {
            DespawnNaturalTime = despawnNaturalTime;
            DespawnDroppedTime = despawnDroppedTime;
            RespawnTime = respawnTime;
            SpawnChance = spawnChance;
            NativeCyclePermitted = nativeCyclePermitted;
        }

        public static ItemCycleThresholds Resolve()
        {
            if (Override != null) return Override;
            try
            {
                var items = Provider.modeConfigData?.Items;
                if (items == null) return null;
                bool permitted = Level.info != null && Level.info.type != ELevelType.ARENA;
                return new ItemCycleThresholds(
                    items.Despawn_Natural_Time,
                    items.Despawn_Dropped_Time,
                    items.Respawn_Time,
                    items.Spawn_Chance,
                    permitted);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
