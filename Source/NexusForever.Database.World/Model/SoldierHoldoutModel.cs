using System.Collections.Generic;

namespace NexusForever.Database.World.Model
{
    public class SoldierHoldoutModel
    {
        public uint MissionId { get; set; }
        public uint ActivatedCreatureId { get; set; }
        public uint ActivatedDisplayInfoId { get; set; }
        public uint ActiveModelSequenceId { get; set; }
        public ushort FallbackFactionId { get; set; }
        public string Source { get; set; }
        public string Confidence { get; set; }

        public ICollection<SoldierHoldoutWaveModel> Waves { get; set; } = new HashSet<SoldierHoldoutWaveModel>();
    }
}
