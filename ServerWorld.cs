using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Numerics;

public enum BiomeType : byte
{
    Meadow = 0,
    Forest = 1,
    Desert = 2,
    StonyPeaks = 3,
    Ocean = 4,
    Beach = 5,
    BrimstoneSprings = 6,
    River = 7,
    AshenWastelands = 8,
    LavaPool = 9,
    TheEnd = 10,
    Void = 11,
    Mesa = 12,
    Tundra = 13,
    FrozenOcean = 14,
    IcyPeaks = 15,
    Swamp = 16,
    CherryGrove = 17,
    RockyBeach = 18
}

public enum Dimension : byte
{
    Overworld = 0,
    TheEnd = 1
}

public enum ServerFeatureType : byte
{
    None = 0,
    SmallTree = 1,
    LargeTree = 2,
    MeadowHedge = 3,
    MeadowFlowers = 4,
    Stone = 5,
    PalmTree = 6,
    DesertLog = 7,
    Tumbleweed = 8,
    OasisDesert = 9,
    BeachUmbrella = 10,
    Sailboat = 11,
    SulfurSpring = 12,
    FrozenTree = 13,
    BerryBush = 14,
    Lilypads = 15,
    IceSpike1 = 16,
    IceSpike2 = 17,
    SnowPile1 = 18,
    SnowPile2 = 19,
    SnowPile3 = 20,
    Cactus = 21,
    DeadBush = 22,
    CherryTree = 23
}

public struct ServerChunkCoord
{
    public int X;
    public int Y;
    public ServerChunkCoord(int x, int y) { X = x; Y = y; }
}

public class ServerChunk
{
    public ServerChunkCoord Coord;
    public BiomeType Biome;
    public ServerFeatureType Feature;
    public ServerChunk(int x, int y, BiomeType biome, ServerFeatureType feature = ServerFeatureType.None)
    {
        Coord = new ServerChunkCoord(x, y);
        Biome = biome;
        Feature = feature;
    }
}

public class ServerBomb
{
    public Vector2 Position;
    public Vector2 Velocity;
    public float Timer;
    public string OwnerName;
    public string TargetPlayer;
    public bool Exploded;
    public ServerBomb(Vector2 pos, Vector2 vel, string owner) { Position = pos; Velocity = vel; Timer = 1.0f; OwnerName = owner; Exploded = false; TargetPlayer = ""; } // OwnerName can be player or bot
}

public class ServerGust // NEW
{
    public Vector2 Position;
    public Vector2 Velocity;
    public float LifeTime;
    public string OwnerName;
    public float Damage;
    public float KnockbackForce;
    public ServerGust(Vector2 pos, Vector2 vel, string owner, float damage, float knockback) { Position = pos; Velocity = vel; LifeTime = 2.0f; OwnerName = owner; Damage = damage; KnockbackForce = knockback; } // 2 seconds life
}

public class RaiderBot
{
    public string Name;
    public Vector2 Position;
    public Vector2 Velocity = Vector2.Zero;
    public int Health = 100;
    public int PreviousHealth = 100;
    public int MaxHealth = 100;
    public float Rotation;
    public float AttackTimer;
    public string HeldItemID = "iron_sword";
    public float AttackCooldown = 0.425f;
    public float FleeTimer = 0f;
    public Vector2? WanderTarget = null;
    public float WanderWaitTimer = 0f;
    public int ChargePhase = 0; // 0: None, 1: Prep, 2: Charging
    public float ChargeTimer = 0f;
    public float ChargeCooldown = 15f; // Initial delay
    public Vector2 ChargeDirection = Vector2.Zero;
    public bool HasDealtChargeDamage = false;
    // NEW: Apex-specific fields
    public bool HasTriggeredStage3Intro = false;
    public float ApexTeleportTimer = 0f;
    public Dimension Dimension = Dimension.Overworld;
    public RaiderBot(string name, Vector2 pos) { Name = name; Position = pos; }
}

public class ServerWorld
{
    // Store player positions for proximity checks and movement sync
    public Dictionary<string, Vector2> PlayerLocations = new();

    public int Seed;

    // Raid State
    public float RaidTimer = 30f;
    public bool RaidActive = false;
    public List<RaiderBot> Raiders = new();
    public List<ServerBomb> ActiveBombs = new();
    public List<ServerGust> ActiveGusts = new(); // NEW
    public SerializableVector2? ActiveRaidOutpostPosition = null; // NEW FIELD HERE

    // Storage for world structures like Raid Outposts
    public ConcurrentDictionary<(int, int), Structure> Structures = new();

    // Cache for generated world data
    public Dictionary<(int, int, Dimension), ServerChunk> _chunks = new();
    private readonly object _worldLock = new();
    public bool IsLoaded { get; set; } = false; // Flag to indicate if world data has been loaded

    public ServerWorld()
    {
        Seed = new Random().Next(-1000000, 1000000);
    }

    public void UpdatePosition(string username, float x, float y)
    {
        lock (_worldLock)
        {
            PlayerLocations[username] = new Vector2(x, y);
        }
    }

    public void RemovePlayer(string username)
    {
        lock (_worldLock)
        {
            PlayerLocations.Remove(username);
        }
    }

    public ServerChunk GetOrGenerateChunk(int chunkX, int chunkY, Dimension dimension = Dimension.Overworld)
    {
        lock (_worldLock)
        {
            if (_chunks.TryGetValue((chunkX, chunkY, dimension), out var chunk))
                return chunk;

            // Apply the seed as a coordinate offset to "shift" the noise map
            float sx = chunkX + (Seed % 5000); 
            float sy = chunkY + (Seed / 5000);

            if (dimension == Dimension.TheEnd)
            {
                // Calculate distance from center for circular island logic
                float dist = MathF.Sqrt(chunkX * chunkX + chunkY * chunkY);
                // Add noise to the radius to make it a "rough" circle
                float edgeNoise = (Perlin.Noise(sx * 0.1f, sy * 0.1f) + 1f) * 8f; 
                BiomeType endBiome = (dist > 150f + edgeNoise) ? BiomeType.Void : BiomeType.TheEnd;

                chunk = new ServerChunk(chunkX, chunkY, endBiome);
                _chunks[(chunkX, chunkY, dimension)] = chunk;
                return chunk;
            }

            // --- REWRITTEN WORLD GEN SYSTEM ---
            // Significantly reduced frequencies to create much larger biome regions
            float continentalness = (Perlin.Noise(sx * 0.0015f, sy * 0.0015f) + 1f) * 0.5f;
            float temperature = (Perlin.Noise(sx * 0.001f + 3000, sy * 0.001f + 3000) + 1f) * 0.5f;
            float humidity = (Perlin.Noise(sx * 0.0012f + 8000, sy * 0.0012f + 8000) + 1f) * 0.5f;
            float peaks = (Perlin.Noise(sx * 0.004f, sy * 0.004f) + 1f) * 0.5f;
            float river = Perlin.Noise(sx * 0.012f, sy * 0.012f);
            float ashen = (Perlin.Noise(sx * 0.0008f + 1500, sy * 0.0008f - 1500) + 1f) * 0.5f;

            BiomeType biome;

            // 1. Water Systems (Highest Priority)
            if (continentalness < 0.25f) {
                biome = (temperature < 0.3f) ? BiomeType.FrozenOcean : BiomeType.Ocean;
            } else if (continentalness < 0.30f) {
                biome = (temperature < 0.4f) ? BiomeType.RockyBeach : BiomeType.Beach;
            } 
            // 2. Specialized Massive Biomes
            else if (ashen > 0.68f) {
                float lavaNoise = (Perlin.Noise(sx * 0.006f, sy * 0.006f) + 1f) * 0.5f;
                if (ashen > 0.695f && lavaNoise > 0.50f) biome = BiomeType.LavaPool;
                else biome = BiomeType.AshenWastelands;
            }
            // 3. Rivers (Abs Ridge Noise)
            else if (Math.Abs(river) < 0.035f) {
                biome = BiomeType.River;
            }
            // 4. Multi-Layer Land Biome Selection (Temperature / Humidity / Peaks)
            else {
                if (peaks > 0.75f) {
                    if (temperature < 0.35f) biome = BiomeType.IcyPeaks;
                    else if (temperature > 0.75f && humidity < 0.3f) biome = BiomeType.BrimstoneSprings;
                    else biome = BiomeType.StonyPeaks;
                }
                else if (temperature < 0.3f) {
                    biome = BiomeType.Tundra;
                }
                else if (temperature > 0.65f) {
                    if (humidity < 0.35f) biome = BiomeType.Mesa;
                    else biome = BiomeType.Desert;
                }
                else { // Temperate Zone
                    if (humidity > 0.8f) biome = BiomeType.Swamp;
                    else if (humidity > 0.65f) biome = BiomeType.CherryGrove;
                    else if (humidity > 0.4f) biome = BiomeType.Forest;
                    else biome = BiomeType.Meadow;
                }
            }

            chunk = new ServerChunk(chunkX, chunkY, biome);
            
            // Feature Generation (Reduced density to prevent "piling")
            int fHash = (chunkX * 73856093) ^ (chunkY * 19349663) ^ Seed;
            int roll = Math.Abs(fHash) % 1000; // Switch to 1000 for finer control

            // Spatial Filtering: Only allow a feature to spawn if it is the "priority winner" 
            // in a 11x11 area. This ensures a minimum of 6 chunks between features.
            bool passesFilter = true;
            for (int dx = -5; dx <= 5; dx++)
            {
                for (int dy = -5; dy <= 5; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = chunkX + dx;
                    int ny = chunkY + dy;
                    int nHash = (nx * 73856093) ^ (ny * 19349663) ^ Seed;
                    int nRoll = Math.Abs(nHash) % 1000;

                    // If any neighbor has a higher priority (lower roll), this chunk loses.
                    if (nRoll < roll) { passesFilter = false; break; }
                    // Tie-breaker for identical rolls based on coordinate priority.
                    if (nRoll == roll && (nx < chunkX || (nx == chunkX && ny < chunkY))) { passesFilter = false; break; }
                }
                if (!passesFilter) break;
            }

            if (passesFilter)
            {
                if (biome == BiomeType.Forest)
                {
                    if (roll < 50) // 5.0% density
                    {
                        int sub = Math.Abs(fHash >> 8) % 100;
                        if (sub < 60) chunk.Feature = ServerFeatureType.SmallTree;
                        else if (sub < 90) chunk.Feature = ServerFeatureType.LargeTree;
                        else chunk.Feature = ServerFeatureType.Stone;
                    }
                }
                else if (biome == BiomeType.Meadow)
                {
                    if (roll < 80) // 8% density
                    {
                        int sub = Math.Abs(fHash >> 8) % 100;
                        chunk.Feature = (sub < 30) ? ServerFeatureType.MeadowHedge : ServerFeatureType.MeadowFlowers;
                    }
                }
                else if (biome == BiomeType.Desert)
                {
                    if (roll < 30) // 3.0% density
                    {
                        int sub = Math.Abs(fHash >> 8) % 100;
                        if (sub < 50) chunk.Feature = ServerFeatureType.Tumbleweed;
                        else if (sub < 85) chunk.Feature = ServerFeatureType.DesertLog;
                        else if (sub < 95) chunk.Feature = ServerFeatureType.PalmTree;
                        else chunk.Feature = ServerFeatureType.OasisDesert;
                    }
                }
                else if (biome == BiomeType.Beach)
                {
                    if (roll < 20) chunk.Feature = (Math.Abs(fHash >> 8) % 10 < 8) ? ServerFeatureType.PalmTree : ServerFeatureType.BeachUmbrella;
                }
                else if (biome == BiomeType.StonyPeaks)
                {
                    if (roll < 60) chunk.Feature = ServerFeatureType.Stone;
                }
                else if (biome == BiomeType.Ocean)
                {
                    if (roll < 4) chunk.Feature = ServerFeatureType.Sailboat;
                }
                else if (biome == BiomeType.BrimstoneSprings)
                {
                    if (roll < 40) chunk.Feature = (Math.Abs(fHash >> 8) % 10 < 4) ? ServerFeatureType.SulfurSpring : ServerFeatureType.Stone;
                }
                else if (biome == BiomeType.Tundra)
                {
                    if (roll < 60)
                    {
                        int sub = Math.Abs(fHash >> 8) % 100;
                        if (sub < 20) chunk.Feature = ServerFeatureType.FrozenTree;
                        else if (sub < 50) chunk.Feature = ServerFeatureType.BerryBush;
                        else if (sub < 70) chunk.Feature = ServerFeatureType.SnowPile1;
                        else if (sub < 85) chunk.Feature = ServerFeatureType.SnowPile2;
                        else chunk.Feature = ServerFeatureType.SnowPile3;
                    }
                }
                else if (biome == BiomeType.IcyPeaks)
                {
                    if (roll < 80)
                    {
                        int sub = Math.Abs(fHash >> 8) % 100;
                        if (sub < 30) chunk.Feature = ServerFeatureType.IceSpike1;
                        else if (sub < 50) chunk.Feature = ServerFeatureType.IceSpike2;
                        else chunk.Feature = ServerFeatureType.SnowPile1;
                    }
                }
                else if (biome == BiomeType.FrozenOcean)
                {
                    if (roll < 15) chunk.Feature = (Math.Abs(fHash >> 8) % 2 == 0) ? ServerFeatureType.IceSpike1 : ServerFeatureType.IceSpike2;
                }
                else if (biome == BiomeType.Swamp)
                {
                    if (roll < 70) chunk.Feature = (Math.Abs(fHash >> 8) % 10 < 7) ? ServerFeatureType.Lilypads : ServerFeatureType.SmallTree;
                }
                else if (biome == BiomeType.CherryGrove)
                {
                    if (roll < 55) chunk.Feature = ServerFeatureType.CherryTree;
                }
                else if (biome == BiomeType.Mesa)
                {
                    if (roll < 30) chunk.Feature = (Math.Abs(fHash >> 8) % 10 < 8) ? ServerFeatureType.DeadBush : ServerFeatureType.Cactus;
                }
                else if (biome == BiomeType.RockyBeach)
                {
                    if (roll < 50) chunk.Feature = ServerFeatureType.Stone;
                }
            }

            _chunks[(chunkX, chunkY, dimension)] = chunk;

            // Structure Generation: 0.005% chance per chunk (1 in 20,000)
            Random rng = new Random(fHash);
            if (rng.Next(0, 20000) < 1)
            {
                // Place a raid outpost at the center of the chunk
                const int MIN_RAID_OUTPOST_DISTANCE_CHUNKS = 180;
                bool canPlaceOutpost = true;

                // Ensure outposts don't spawn within 180 chunks of the world origin (spawn)
                if (Math.Sqrt(chunkX * chunkX + chunkY * chunkY) < 180) canPlaceOutpost = false;

                // Restriction: Prevent spawning in Ocean, River, Lava, or Ashen biomes
                if (biome == BiomeType.Ocean || biome == BiomeType.River || 
                    biome == BiomeType.LavaPool || biome == BiomeType.AshenWastelands) 
                    canPlaceOutpost = false;

                if (canPlaceOutpost)
                {
                    foreach (var existingStructure in Structures.Values)
                    {
                        if (existingStructure.Type == StructureType.RaidOutpost)
                        {
                            int dx = chunkX - existingStructure.ChunkX;
                            int dy = chunkY - existingStructure.ChunkY;
                            double distance = Math.Sqrt(dx * dx + dy * dy);

                            if (distance < MIN_RAID_OUTPOST_DISTANCE_CHUNKS)
                            {
                                canPlaceOutpost = false;
                                break;
                            }
                        }
                    }
                }

                if (canPlaceOutpost)
                {
                    Vector2 structurePos = new Vector2(chunkX * 16 + 8, chunkY * 16 + 8);
                    Structure outpost = new Structure(structurePos, StructureType.RaidOutpost, chunkX, chunkY, "");
                    Structures.TryAdd((chunkX, chunkY), outpost);
                    // Clear feature if a structure exists in this chunk to ensure it generates "on top"
                    chunk.Feature = ServerFeatureType.None;
                }
            }

            return chunk;
        }
    }
}