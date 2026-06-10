using BulletboxClient;

public class WorldData
{
    public string WorldName { get; set; } = "New World";
    public bool CheatsEnabled { get; set; } = false;
    public string Version { get; set; } = Program.VERSION; // Default to current version
    // Add other world-specific data as needed
}