using System.Collections.Generic;
using System.Numerics;

// This file defines the data structures for saving the server's state.

public class WorldSaveData
{
    public int Seed { get; set; }
    public List<PlayerSaveData> Players { get; set; } = new();
    public List<RaiderSaveData> Raiders { get; set; } = new();
    public List<ServerBomb> ActiveBombs { get; set; } = new();
    public List<ServerGust> ActiveGusts { get; set; } = new();
    public Dictionary<string, Structure> Structures { get; set; } = new(); // Key: "chunkX,chunkY"
    public float WorldTime { get; set; }
    public float FlickerSpawnTimer { get; set; }
    public float PlayerRegenTimer { get; set; }
    public float RaidTimer { get; set; }
    public bool RaidActive { get; set; }
    public SerializableVector2? ActiveRaidOutpostPosition { get; set; }
}

public class PlayerSaveData
{
    public string Username { get; set; } = "";
    public int Health { get; set; }
    public int MaxHealth { get; set; }
    public int Hunger { get; set; }
    public SerializableVector2 Position { get; set; }
    public float Rotation { get; set; }
    public bool IsBlocking { get; set; }
    public Dimension CurrentDimension { get; set; }
    public int SelectedSlot { get; set; }
    public float AshenTime { get; set; }
    public float BrimstalkerCooldown { get; set; }
    public ServerItemStack[] Inventory { get; set; } = new ServerItemStack[25];
    public ServerItemStack CraftingSlot1 { get; set; }
    public ServerItemStack CraftingSlot2 { get; set; }
    public float TimeInEndDimension { get; set; }
    public float TimeOnLava { get; set; }
    public int TotalMobsKilled { get; set; }
    public int TotalQuartzObtained { get; set; }
    public int TotalRaidshroomsObtained { get; set; }
    public HashSet<BiomeType> VisitedBiomes { get; set; } = new();
    public HashSet<string> KilledOverworld { get; set; } = new();
    public HashSet<string> TriggeredAdvancements { get; set; } = new();
}

public class RaiderSaveData
{
    public string Name { get; set; } = "";
    public SerializableVector2 Position { get; set; }
    public SerializableVector2 Velocity { get; set; }
    public int Health { get; set; }
    public int PreviousHealth { get; set; }
    public int MaxHealth { get; set; }
    public float Rotation { get; set; }
    public float AttackTimer { get; set; }
    public string HeldItemID { get; set; } = "";
    public float AttackCooldown { get; set; }
    public float FleeTimer { get; set; }
    public SerializableVector2? WanderTarget { get; set; }
    public float WanderWaitTimer { get; set; }
    public int ChargePhase { get; set; }
    public float ChargeTimer { get; set; }
    public float ChargeCooldown { get; set; }
    public SerializableVector2 ChargeDirection { get; set; }
    public bool HasDealtChargeDamage { get; set; }
    public Dimension Dimension { get; set; }
    public int PatrolID { get; set; }
    public bool IsHostile { get; set; }
    public float IdleSoundTimer { get; set; }
    public float AngrySoundTimer { get; set; }
}