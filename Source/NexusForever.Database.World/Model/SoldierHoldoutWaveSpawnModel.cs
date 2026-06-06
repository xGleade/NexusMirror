namespace NexusForever.Database.World.Model
{
    public class SoldierHoldoutWaveSpawnModel
    {
        public uint MissionId { get; set; }
        public uint WaveIndex { get; set; }
        public uint SpawnIndex { get; set; }
        public uint CreatureId { get; set; }
        public uint Count { get; set; }
        public uint EntityId { get; set; }

        public SoldierHoldoutWaveModel Wave { get; set; }
    }
}
