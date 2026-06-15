using System;
using System.Collections.Generic;

public class ServerWeaponStats {
    public string Name;
    public int Damage;
    public float Cooldown; 
    public float Range;
    public float Knockback;

    public ServerWeaponStats(string n, int d, float c, float r, float k) {
        Name = n; Damage = d; Cooldown = c / 1000f; Range = r; Knockback = k;
    }

    // Synchronized with Client WeaponStats Library
    public static Dictionary<string, ServerWeaponStats> Library = new Dictionary<string, ServerWeaponStats> {
        // --- WOODEN TIER ---
        { "wooden_sword", new ServerWeaponStats("Wooden Sword", 4, 200, 230, 20) },
        { "wooden_axe", new ServerWeaponStats("Wooden Axe", 9, 475, 320, 45) },
        { "wooden_scythe", new ServerWeaponStats("Wooden Scythe", 5, 325, 260, -1) },
        { "wooden_spear", new ServerWeaponStats("Wooden Spear", 3, 275, 550, 5) },
        { "wooden_kanabo", new ServerWeaponStats("Wooden Club", 12, 600, 290, 50) },

        // --- STONE TIER ---
        { "stone_sword", new ServerWeaponStats("Stone Sword", 7, 225, 240, 22) },
        { "stone_axe", new ServerWeaponStats("Stone Axe", 15, 460, 330, 50) },
        { "stone_scythe", new ServerWeaponStats("Stone Scythe", 10, 320, 270, -5) },
        { "stone_spear", new ServerWeaponStats("Stone Spear", 6, 290, 580, 8) },
        { "stone_kanabo", new ServerWeaponStats("Stone Kanabo", 22, 575, 300, 55) },

        // --- COPPER TIER ---
        { "copper_sword", new ServerWeaponStats("Copper Sword", 9, 205, 250, 24) },
        { "copper_axe", new ServerWeaponStats("Copper Axe", 19, 440, 340, 52) },
        { "copper_scythe", new ServerWeaponStats("Copper Scythe", 12, 300, 280, -10) },
        { "copper_spear", new ServerWeaponStats("Copper Spear", 7, 280, 590, 9) },
        { "copper_kanabo", new ServerWeaponStats("Copper Kanabo", 28, 550, 305, 58) },

        // --- IRON TIER ---
        { "iron_sword", new ServerWeaponStats("Iron Sword", 10, 212.5f, 250, 25) },
        { "iron_axe", new ServerWeaponStats("Iron Axe", 22, 450, 345, 55) },
        { "iron_scythe", new ServerWeaponStats("Iron Scythe", 14, 312.5f, 280, -10) },
        { "iron_spear", new ServerWeaponStats("Iron Spear", 8, 287.5f, 600, 10) },
        { "iron_kanabo", new ServerWeaponStats("Iron Kanabo", 32, 557.5f, 305, 60) },

        // --- DIAMOND TIER ---
        { "diamond_sword", new ServerWeaponStats("Diamond Sword", 15, 200, 260, 28) },
        { "diamond_axe", new ServerWeaponStats("Diamond Axe", 30, 425, 360, 60) },
        { "diamond_scythe", new ServerWeaponStats("Diamond Scythe", 20, 290, 290, -12) },
        { "diamond_spear", new ServerWeaponStats("Diamond Spear", 12, 270, 630, 12) },
        { "diamond_kanabo", new ServerWeaponStats("Diamond Kanabo", 44, 540, 315, 65) },

        // --- BRIMSTONE TIER ---
        { "brimstone_sword", new ServerWeaponStats("Brimstone Sword", 20, 230, 250, 35) },
        { "brimstone_axe", new ServerWeaponStats("Brimstone Axe", 38, 475, 350, 70) },
        { "brimstone_scythe", new ServerWeaponStats("Brimstone Scythe", 26, 325, 280, -15) },
        { "brimstone_spear", new ServerWeaponStats("Brimstone Spear", 16, 300, 610, 18) },
        { "brimstone_kanabo", new ServerWeaponStats("Brimstone Kanabo", 58, 600, 310, 85) }
    };

    public static (float dmg, float kb, float range) Calculate(string id, float elapsed, float timeSinceLastHit) {
        if (!Library.TryGetValue(id, out var w)) return (0, 0, 0);

        float cn = Math.Clamp(elapsed / w.Cooldown, 0f, 1f);
        
        if (id == "dagger") {
            float comboDecay = (float)Math.Exp(-0.25f * timeSinceLastHit);
            float d = w.Damage * (1f + 0.3f * comboDecay);
            if (elapsed < (0.45f * w.Cooldown)) d *= 0.8f;
            return (d, 0, 40);
        }

        if (cn < 0.35f) return (0, 0, 0);
        float d_final = w.Damage * (1.1764f * (float)Math.Pow(cn, 1.6f));
        float kb_final = w.Knockback * ((2f * (float)Math.Pow(cn, 1.4f)) - 0.65f);
        float r_final = w.Range * (float)Math.Pow(cn, 1.2f);
        return (d_final, kb_final, r_final);
    }
}