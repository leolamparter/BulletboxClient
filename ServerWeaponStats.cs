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

    public static Dictionary<byte, ServerWeaponStats> Library = new Dictionary<byte, ServerWeaponStats> {
        { (byte)'S', new ServerWeaponStats("Sword", 10, 425, 250, 25) },
        { (byte)'A', new ServerWeaponStats("Axe", 22, 900, 345, 55) },
        { (byte)'D', new ServerWeaponStats("Dagger", 4, 95, 40, 0) },
        { (byte)'P', new ServerWeaponStats("Spear", 8, 575, 600, 10) },
        { (byte)'Y', new ServerWeaponStats("Scythe", 14, 625, 280, -10) },
        { (byte)'K', new ServerWeaponStats("Kanabo", 32, 1115, 305, 60) }
    };

    public static (float dmg, float kb, float range) Calculate(byte id, float elapsed, float timeSinceLastHit) {
        if (!Library.TryGetValue(id, out var w)) return (0, 0, 0);

        float cn = Math.Clamp(elapsed / w.Cooldown, 0f, 1f);
        
        if (id == (byte)'D') {
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