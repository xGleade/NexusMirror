using System.Collections.Generic;

namespace NexusForever.Database.World.Model
{
    public class SoldierHoldoutWaveModel
    {
        public uint MissionId { get; set; }
        public uint WaveIndex { get; set; }
        public bool IsBoss { get; set; }
        public float? SpawnRadius { get; set; }

        public SoldierHoldoutModel Holdout { get; set; }
        public ICollection<SoldierHoldoutWaveSpawnModel> Spawns { get; set; } = new HashSet<SoldierHoldoutWaveSpawnModel>();
    }
}
