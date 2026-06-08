using System.Numerics;

public enum StructureType : byte
{
    None = 0,
    RaidOutpost = 1,
    EndPortal = 2
}

public class Structure
{
    public SerializableVector2 Position { get; set; }
    public StructureType Type { get; set; }
    public int ChunkX { get; set; }
    public int ChunkY { get; set; }
    public string TextureName { get; set; } // Client-specific
    public bool IsCompleted { get; set; } = false;
    public float RaidCheckTimer { get; set; } = 0f;
    public bool RaidActive { get; set; } = false;
    public float RaidTimer { get; set; } = 9999f;
    public float RaidBossHealth { get; set; } = 0f;
    public bool HasPlayedCountdown { get; set; } = false;
    public ServerItemStack[]? ChestInventory { get; set; } = null;

    public Structure(Vector2 position, StructureType type, int chunkX, int chunkY, string textureName)
    {
        Position = position;
        Type = type;
        ChunkX = chunkX;
        ChunkY = chunkY;
        TextureName = textureName;
    }
}