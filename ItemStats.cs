using System.Collections.Generic;

public class ItemStats
{
    public string Name;
    public string TextureKey;

    public ItemStats(string name, string textureKey)
    {
        Name = name;
        TextureKey = textureKey;
    }

    public static Dictionary<byte, ItemStats> Library = new Dictionary<byte, ItemStats>
    {
        { (byte)'S', new ItemStats("Sword", "sword") },
        { (byte)'A', new ItemStats("Axe", "axe") },
        { (byte)'D', new ItemStats("Dagger", "dagger") },
        { (byte)'P', new ItemStats("Spear", "spear") },
        { (byte)'Y', new ItemStats("Scythe", "scythe") },
        { (byte)'K', new ItemStats("Kanabo", "kanabo") },
        { (byte)'H', new ItemStats("Shield", "shield") },
        { (byte)'B', new ItemStats("Bow", "bow") },
        { (byte)'R', new ItemStats("Raidshroom", "raidshroom") }
    };
}