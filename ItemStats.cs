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

        public static Dictionary<string, ItemStats> Library = new Dictionary<string, ItemStats>
    {
        // Raw Materials & Resources
        { "stick", new ItemStats("Stick", "stick") },
        { "rock", new ItemStats("Rock", "rock") },
        { "copper", new ItemStats("Copper", "copper") },
        { "iron", new ItemStats("Ingot", "iron") },
        { "diamond", new ItemStats("Diamond", "diamond") },
        { "quartz", new ItemStats("Quartz", "quartz") },
        { "brimstone_powder", new ItemStats("Brimstone Powder", "brimstone_powder") },
        
        // Unique Biome Drops / Auxiliary Gear
        { "pearl", new ItemStats("Vortex Pearl", "pearl") },
        { "brimstone_pearl", new ItemStats("Brimstone Pearl", "brimstone_pearl") },
        { "raidshroom", new ItemStats("Raidshroom", "raidshroom") },
        { "shield", new ItemStats("Shield", "shield") },

        // Wooden Tier Weapons & Tools
        { "wooden_sword", new ItemStats("Wooden Sword", "wooden_sword") },
        { "wooden_axe", new ItemStats("Wooden Axe", "wooden_axe") },
        { "wooden_kanabo", new ItemStats("Wooden Club", "wooden_kanabo") },
        { "wooden_scythe", new ItemStats("Wooden Scythe", "wooden_scythe") },
        { "wooden_spear", new ItemStats("Wooden Spear", "wooden_spear") },

        // Stone Tier Weapons & Tools
        { "stone_sword", new ItemStats("Stone Sword", "stone_sword") },
        { "stone_kanabo", new ItemStats("Stone Kanabo", "stone_kanabo") },
        { "stone_axe", new ItemStats("Stone Axe", "stone_axe") },
        { "stone_scythe", new ItemStats("Stone Scythe", "stone_scythe") },
        { "stone_spear", new ItemStats("Stone Spear", "stone_spear") },

        // Copper Tier Weapons & Tools
        { "copper_sword", new ItemStats("Copper Sword", "copper_sword") },
        { "copper_kanabo", new ItemStats("Copper Kanabo", "copper_kanabo") },
        { "copper_axe", new ItemStats("Copper Axe", "copper_axe") },
        { "copper_scythe", new ItemStats("Copper Scythe", "copper_scythe") },
        { "copper_spear", new ItemStats("Copper Spear", "copper_spear") },

        // Iron Tier Weapons & Tools
        { "iron_sword", new ItemStats("Iron Sword", "iron_sword") },
        { "iron_kanabo", new ItemStats("Iron Kanabo", "iron_kanabo") },
        { "iron_axe", new ItemStats("Iron Axe", "iron_axe") },
        { "iron_scythe", new ItemStats("Iron Scythe", "iron_scythe") },
        { "iron_spear", new ItemStats("Iron Spear", "iron_spear") },

        // Diamond Tier Weapons & Tools
        { "diamond_sword", new ItemStats("Diamond Sword", "diamond_sword") },
        { "diamond_kanabo", new ItemStats("Diamond Kanabo", "diamond_kanabo") },
        { "diamond_axe", new ItemStats("Diamond Axe", "diamond_axe") },
        { "diamond_scythe", new ItemStats("Diamond Scythe", "diamond_scythe") },
        { "diamond_spear", new ItemStats("Diamond Spear", "diamond_spear") },

        // Brimstone Tier Weapons & Tools
        { "brimstone_sword", new ItemStats("Brimstone Sword", "brimstone_sword") },
        { "brimstone_kanabo", new ItemStats("Brimstone Kanabo", "brimstone_kanabo") },
        { "brimstone_axe", new ItemStats("Brimstone Axe", "brimstone_axe") },
        { "brimstone_scythe", new ItemStats("Brimstone Scythe", "brimstone_scythe") },
        { "brimstone_spear", new ItemStats("Brimstone Spear", "brimstone_spear") }
    };

    public static Dictionary<(string, string), string> Recipes = new Dictionary<(string, string), string>
    {
        // Wooden -> Stone (Rock)
        { ("wooden_sword", "rock"), "stone_sword" },
        { ("wooden_axe", "rock"), "stone_axe" },
        { ("wooden_scythe", "rock"), "stone_scythe" },
        { ("wooden_spear", "rock"), "stone_spear" },
        { ("wooden_kanabo", "rock"), "stone_kanabo" },
        
        // Stone -> Copper
        { ("stone_sword", "copper"), "copper_sword" },
        { ("stone_axe", "copper"), "copper_axe" },
        { ("stone_scythe", "copper"), "copper_scythe" },
        { ("stone_spear", "copper"), "copper_spear" },
        { ("stone_kanabo", "copper"), "copper_kanabo" },

        // Copper -> Iron
        { ("copper_sword", "iron"), "iron_sword" },
        { ("copper_axe", "iron"), "iron_axe" },
        { ("copper_scythe", "iron"), "iron_scythe" },
        { ("copper_spear", "iron"), "iron_spear" },
        { ("copper_kanabo", "iron"), "iron_kanabo" },

        // Iron -> Diamond
        { ("iron_sword", "diamond"), "diamond_sword" },
        { ("iron_axe", "diamond"), "diamond_axe" },
        { ("iron_scythe", "diamond"), "diamond_scythe" },
        { ("iron_spear", "diamond"), "diamond_spear" },
        { ("iron_kanabo", "diamond"), "diamond_kanabo" },

        // Diamond -> Brimstone
        { ("diamond_sword", "brimstone_powder"), "brimstone_sword" },
        { ("diamond_axe", "brimstone_powder"), "brimstone_axe" },
        { ("diamond_scythe", "brimstone_powder"), "brimstone_scythe" },
        { ("diamond_spear", "brimstone_powder"), "brimstone_spear" },
        { ("diamond_kanabo", "brimstone_powder"), "brimstone_kanabo" },

        // Special
        { ("pearl", "brimstone_powder"), "brimstone_pearl" }
    };
}