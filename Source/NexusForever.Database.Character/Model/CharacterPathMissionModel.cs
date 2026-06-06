namespace NexusForever.Database.Character.Model
{
    public class CharacterPathMissionModel
    {
        public ulong Id { get; set; }
        public ushort MissionId { get; set; }
        public byte Completed { get; set; }
        public uint UserData { get; set; }
        public uint StateData { get; set; }

        public CharacterModel Character { get; set; }
    }
}
