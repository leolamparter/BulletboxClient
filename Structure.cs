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
    
    // Client-side visual state
    public bool HasBeenOpened { get; set; } = false;
    public float TextFadeAlpha { get; set; } = 1.0f;

    // Parameterless constructor for deserialization
    public Structure()
    {
        Position = new SerializableVector2();
        Type = StructureType.None;
        ChunkX = 0;
        ChunkY = 0;
        TextureName = "";
        IsCompleted = false;
        RaidCheckTimer = 0f;
        RaidActive = false;
        RaidTimer = 9999f;
        RaidBossHealth = 0f;
        HasPlayedCountdown = false;
        ChestInventory = null;
        HasBeenOpened = false;
        TextFadeAlpha = 1.0f;
    }
    public Structure(Vector2 position, StructureType type, int chunkX, int chunkY, string textureName)
    {
        Position = position;
        Type = type;
        ChunkX = chunkX;
        ChunkY = chunkY;
        TextureName = textureName;
    }
}