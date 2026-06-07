using System;
using System.Collections.Generic;

public class WeaponStats {
    public string Name;
    public int Damage;
    public float Cooldown; // converted to seconds
    public float Range;
    public float Knockback;

    public WeaponStats(string n, int d, float c, float r, float k) {
        Name = n; Damage = d; Cooldown = c / 1000f; Range = r; Knockback = k;
    }

    public static Dictionary<string, WeaponStats> Library = new Dictionary<string, WeaponStats> {

        // --- WOODEN TIER ---
        { "wooden_sword", new WeaponStats("Wooden Sword", 4, 400, 230, 20) },
        { "wooden_axe", new WeaponStats("Wooden Axe", 9, 950, 320, 45) },
        { "wooden_scythe", new WeaponStats("Wooden Scythe", 5, 650, 260, -1) },
        { "wooden_spear", new WeaponStats("Wooden Spear", 3, 550, 550, 5) },
        { "wooden_kanabo", new WeaponStats("Wooden Club", 12, 1200, 290, 50) },

        // --- STONE TIER ---
        { "stone_sword", new WeaponStats("Stone Sword", 7, 450, 240, 22) },
        { "stone_axe", new WeaponStats("Stone Axe", 15, 920, 330, 50) },
        { "stone_scythe", new WeaponStats("Stone Scythe", 10, 640, 270, -5) },
        { "stone_spear", new WeaponStats("Stone Spear", 6, 580, 580, 8) },
        { "stone_kanabo", new WeaponStats("Stone Kanabo", 22, 1150, 300, 55) },

        // --- COPPER TIER ---
        { "copper_sword", new WeaponStats("Copper Sword", 9, 410, 250, 24) },
        { "copper_axe", new WeaponStats("Copper Axe", 19, 880, 340, 52) },
        { "copper_scythe", new WeaponStats("Copper Scythe", 12, 600, 280, -10) },
        { "copper_spear", new WeaponStats("Copper Spear", 7, 560, 590, 9) },
        { "copper_kanabo", new WeaponStats("Copper Kanabo", 28, 1100, 305, 58) },

        // --- IRON TIER (The baseline metrics you designed) ---
        { "iron_sword", new WeaponStats("Iron Sword", 10, 425, 250, 25) },
        { "iron_axe", new WeaponStats("Iron Axe", 22, 900, 345, 55) },
        { "iron_scythe", new WeaponStats("Iron Scythe", 14, 625, 280, -10) },
        { "iron_spear", new WeaponStats("Iron Spear", 8, 575, 600, 10) },
        { "iron_kanabo", new WeaponStats("Iron Kanabo", 32, 1115, 305, 60) },

        // --- DIAMOND TIER ---
        { "diamond_sword", new WeaponStats("Diamond Sword", 15, 400, 260, 28) },
        { "diamond_axe", new WeaponStats("Diamond Axe", 30, 850, 360, 60) },
        { "diamond_scythe", new WeaponStats("Diamond Scythe", 20, 580, 290, -12) },
        { "diamond_spear", new WeaponStats("Diamond Spear", 12, 540, 630, 12) },
        { "diamond_kanabo", new WeaponStats("Diamond Kanabo", 44, 1080, 315, 65) },

        // --- BRIMSTONE TIER (High Damage, Brutal Knockback, Sluggish Cooldowns) ---
        { "brimstone_sword", new WeaponStats("Brimstone Sword", 20, 460, 250, 35) },
        { "brimstone_axe", new WeaponStats("Brimstone Axe", 38, 950, 350, 70) },
        { "brimstone_scythe", new WeaponStats("Brimstone Scythe", 26, 650, 280, -15) },
        { "brimstone_spear", new WeaponStats("Brimstone Spear", 16, 600, 610, 18) },
        { "brimstone_kanabo", new WeaponStats("Brimstone Kanabo", 58, 1200, 310, 85) }
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