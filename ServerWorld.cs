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
    LavaPool = 9
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
    SulfurSpring = 12
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
    public ServerBomb(Vector2 pos, Vector2 vel, string owner) { Position = pos; Velocity = vel; Timer = 1.0f; OwnerName = owner; Exploded = false; TargetPlayer = ""; }
}

public class RaiderBot
{
    public string Name;
    public Vector2 Position;
    public Vector2 Velocity = Vector2.Zero;
    public int Health = 100;
    public int MaxHealth = 100;
    public float Rotation;
    public float AttackTimer;
    public byte HeldItemID = (byte)'S';
    public float AttackCooldown = 0.425f;
    public float FleeTimer = 0f;
    public Vector2? WanderTarget = null;
    public float WanderWaitTimer = 0f;
    public int ChargePhase = 0; // 0: None, 1: Prep, 2: Charging
    public float ChargeTimer = 0f;
    public float ChargeCooldown = 15f; // Initial delay
    public Vector2 ChargeDirection = Vector2.Zero;
    public bool HasDealtChargeDamage = false;
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
    public Vector2? ActiveRaidOutpostPosition = null; // NEW FIELD HERE

    // Storage for world structures like Raid Outposts
    public ConcurrentDictionary<(int, int), Structure> Structures = new();

    // Cache for generated world data
    private Dictionary<(int, int), ServerChunk> _chunks = new();
    private readonly object _worldLock = new();

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

    public ServerChunk GetOrGenerateChunk(int chunkX, int chunkY)
    {
        lock (_worldLock)
        {
            if (_chunks.TryGetValue((chunkX, chunkY), out var chunk))
                return chunk;

            // Apply the seed as a coordinate offset to "shift" the noise map
            float sx = chunkX + (Seed % 5000); 
            float sy = chunkY + (Seed / 5000);

            // Dedicated low-frequency noise for rare but massive oceans
            float oceanNoise = (Perlin.Noise(sx * 0.003f, sy * 0.003f) + 1f) * 0.5f;
            float scale = 0.008f;
            // Low frequency noise for massive biomes like Ashen Wastelands
            float ashenNoise = (Perlin.Noise(sx * 0.0015f, sy * 0.0015f) + 1f) * 0.5f;
            float riverNoise = Perlin.Noise(sx * 0.025f, sy * 0.025f);
            float noise = Perlin.Noise(sx * scale, sy * scale);
            float noise2 = Perlin.Noise(sx * scale * 0.5f + 1000, sy * scale * 0.5f - 1000) * 0.5f;
            float n = (noise + noise2 + 1f) * 0.5f;
            float landNoise = Perlin.Noise(sx * 0.018f + 5000, sy * 0.018f - 5000);
            float landN = (landNoise + 1f) * 0.5f;

            BiomeType biome;
            if (oceanNoise < 0.25f) {
                biome = BiomeType.Ocean;
            } else if (oceanNoise < 0.30f) {
                biome = BiomeType.Beach;
            } else if (ashenNoise > 0.68f) {
                // Lava Pool pockets inside Ashen Wastelands
                // frequency lowered to 0.008f for massive lakes and threshold dropped to 0.50
                float lavaNoise = (Perlin.Noise(sx * 0.008f, sy * 0.008f) + 1f) * 0.5f;
                // Buffer (0.695 > 0.68) ensures a thin border of ash always surrounds the lava
                if (ashenNoise > 0.695f && lavaNoise > 0.50f) biome = BiomeType.LavaPool;
                else biome = BiomeType.AshenWastelands;
            } else if (Math.Abs(riverNoise) < 0.035f) {
                biome = BiomeType.River;
            } else if (n > 0.80f) {
                biome = BiomeType.BrimstoneSprings;
            } else if (n < 0.20f) {
                biome = BiomeType.StonyPeaks;
            } else if (landN < 0.46f) {
                biome = BiomeType.Meadow;
            } else if (landN < 0.54f) {
                biome = BiomeType.Forest;
            } else {
                biome = BiomeType.Desert;
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
            }

            _chunks[(chunkX, chunkY)] = chunk;

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