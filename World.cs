using System;
using System.Collections.Concurrent;
using System.Numerics;

public class Chunk
{
    public (int X, int Y) Coord { get; set; }
    public byte Biome { get; set; }
    public byte Feature { get; set; } // 0 for no feature
}

public class World
{
    private ConcurrentDictionary<(int, int), Chunk> _chunks = new ConcurrentDictionary<(int, int), Chunk>();
    public ConcurrentDictionary<string, Vector2> PlayerLocations = new ConcurrentDictionary<string, Vector2>();

    private Random _rand = new Random();
    private int _seed = new Random().Next(-1000000, 1000000);

    // New: Structures
    public ConcurrentDictionary<(int, int), Structure> Structures = new ConcurrentDictionary<(int, int), Structure>();

    public Chunk GetOrGenerateChunk(int x, int y)
    {
        return _chunks.GetOrAdd((x, y), _ => GenerateChunk(x, y));
    }

    private Chunk GenerateChunk(int x, int y)
    {
        float sx = x + (_seed % 5000);
        float sy = y + (_seed / 5000);

        // --- REWRITTEN WORLD GEN SYSTEM (Matching ServerWorld.cs) ---
        float continentalness = (Perlin.Noise(sx * 0.0015f, sy * 0.0015f) + 1f) * 0.5f;
        float temperature = (Perlin.Noise(sx * 0.001f + 3000, sy * 0.001f + 3000) + 1f) * 0.5f;
        float humidity = (Perlin.Noise(sx * 0.0012f + 8000, sy * 0.0012f + 8000) + 1f) * 0.5f;
        float peaks = (Perlin.Noise(sx * 0.004f, sy * 0.004f) + 1f) * 0.5f;
        float river = Perlin.Noise(sx * 0.012f, sy * 0.012f);
        float ashen = (Perlin.Noise(sx * 0.0008f + 1500, sy * 0.0008f - 1500) + 1f) * 0.5f;

        byte biome;
        // 1. Water Systems
        if (continentalness < 0.25f) {
            biome = (temperature < 0.3f) ? (byte)14 : (byte)4; // Frozen Ocean or Ocean
        } else if (continentalness < 0.30f) {
            biome = (temperature < 0.4f) ? (byte)18 : (byte)5; // Rocky Beach or Beach
        } 
        // 2. Specialized Massive Biomes
        else if (ashen > 0.68f) 
        {
            float lavaNoise = (Perlin.Noise(sx * 0.006f, sy * 0.006f) + 1f) * 0.5f;
            if (ashen > 0.695f && lavaNoise > 0.50f) biome = 9; // LavaPool
            else biome = 8; // Ashen
        }
        // 3. Rivers
        else if (Math.Abs(river) < 0.035f) {
            biome = 7; // River
        }
        // 4. Land Biomes
        else {
            if (peaks > 0.75f) {
                if (temperature < 0.35f) biome = 15; // Icy Peaks
                else if (temperature > 0.75f && humidity < 0.3f) biome = 6; // Brimstone Springs
                else biome = 3; // Stony Peaks
            }
            else if (temperature < 0.3f) {
                biome = 13; // Tundra
            }
            else if (temperature > 0.65f) {
                if (humidity < 0.35f) biome = 12; // Mesa
                else biome = 2; // Desert
            }
            else {
                if (humidity > 0.8f) biome = 16; // Swamp
                else if (humidity > 0.65f) biome = 17; // Cherry Grove
                else if (humidity > 0.4f) biome = 1; // Forest
                else biome = 0; // Meadow
            }
        }

        byte feature = 0;
        // Simple feature mapping for singleplayer/legacy fallback
        int fHash = (x * 73856093) ^ (y * 19349663) ^ _seed;
        int roll = Math.Abs(fHash) % 1000;

        if (roll < 50) {
            if (biome == 1) feature = 1; // Forest -> SmallTree
            else if (biome == 0) feature = 4; // Meadow -> Flowers
            else if (biome == 2) feature = 21; // Desert -> Cactus
            else if (biome == 12) feature = 22; // Mesa -> DeadBush
            else if (biome == 13) feature = 13; // Tundra -> FrozenTree
            else if (biome == 16) feature = 15; // Swamp -> Lilypads
            else if (biome == 17) feature = 23; // Cherry -> CherryTree
        }

        Structure? structure = null; // Made nullable to resolve CS8600
        // Restriction: Prevent spawning in Ocean (4), Ashen (8), or LavaPool (9)
        if (_rand.Next(0, 100000) < 1 && biome != 4 && biome != 8 && biome != 9)
        {
            // Place a raid outpost at the center of the chunk (assuming chunkSize is 16)
            Vector2 structurePos = new Vector2(x * 16 + 8, y * 16 + 8);
            structure = new Structure(structurePos, StructureType.RaidOutpost, x, y, "");
            Structures.TryAdd((x, y), structure);
            Console.WriteLine($"Generated Raid Outpost at chunk ({x}, {y})");
        }

        return new Chunk { Coord = (x, y), Biome = biome, Feature = feature };
    }

    public void UpdatePosition(string username, float x, float y)
    {
        PlayerLocations.AddOrUpdate(username, new Vector2(x, y), (key, oldValue) => new Vector2(x, y));
    }

    public void RemovePlayer(string username)
    {
        PlayerLocations.TryRemove(username, out _);
    }
}