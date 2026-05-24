using System;
using System.Collections.Generic;
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
    River = 7
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

            if (biome == BiomeType.Forest)
            {
                if (roll < 25) // 2.5% density
                {
                    int sub = Math.Abs(fHash >> 8) % 100;
                    if (sub < 60) chunk.Feature = ServerFeatureType.SmallTree;
                    else if (sub < 90) chunk.Feature = ServerFeatureType.LargeTree;
                    else chunk.Feature = ServerFeatureType.Stone;
                }
            }
            else if (biome == BiomeType.Meadow)
            {
                if (roll < 40) // 4% density
                {
                    int sub = Math.Abs(fHash >> 8) % 100;
                    chunk.Feature = (sub < 30) ? ServerFeatureType.MeadowHedge : ServerFeatureType.MeadowFlowers;
                }
            }
            else if (biome == BiomeType.Desert)
            {
                if (roll < 15) // 1.5% density
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
                if (roll < 10) chunk.Feature = (Math.Abs(fHash >> 8) % 10 < 8) ? ServerFeatureType.PalmTree : ServerFeatureType.BeachUmbrella;
            }
            else if (biome == BiomeType.StonyPeaks)
            {
                if (roll < 30) chunk.Feature = ServerFeatureType.Stone;
            }
            else if (biome == BiomeType.Ocean)
            {
                if (roll < 2) chunk.Feature = ServerFeatureType.Sailboat;
            }
            else if (biome == BiomeType.BrimstoneSprings)
            {
                if (roll < 20) chunk.Feature = (Math.Abs(fHash >> 8) % 10 < 4) ? ServerFeatureType.SulfurSpring : ServerFeatureType.Stone;
            }

            _chunks[(chunkX, chunkY)] = chunk;
            return chunk;
        }
    }
}