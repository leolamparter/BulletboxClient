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

        float oceanNoise = (Perlin.Noise(sx * 0.003f, sy * 0.003f) + 1f) * 0.5f;
        float ashenNoise = (Perlin.Noise(sx * 0.0015f, sy * 0.0015f) + 1f) * 0.5f;
        float riverNoise = Perlin.Noise(sx * 0.025f, sy * 0.025f);
        float noise = Perlin.Noise(sx * 0.008f, sy * 0.008f);
        float noise2 = Perlin.Noise(sx * 0.008f * 0.5f + 1000, sy * 0.008f * 0.5f - 1000) * 0.5f;
        float n = (noise + noise2 + 1f) * 0.5f;
        float landN = (Perlin.Noise(sx * 0.018f + 5000, sy * 0.018f - 5000) + 1f) * 0.5f;

        byte biome;
        if (oceanNoise < 0.25f) biome = 4; // Ocean
        else if (oceanNoise < 0.30f) biome = 5; // Beach
        else if (ashenNoise > 0.68f) 
        {
            float lavaNoise = (Perlin.Noise(sx * 0.008f, sy * 0.008f) + 1f) * 0.5f;
            if (ashenNoise > 0.695f && lavaNoise > 0.50f) biome = 9; // LavaPool
            else biome = 8; // Ashen
        }
        else if (Math.Abs(riverNoise) < 0.035f) biome = 7; // River
        else if (n > 0.80f) biome = 6; // Brimstone
        else if (n < 0.20f) biome = 3; // Stony Peaks
        else if (landN < 0.46f) biome = 0; // Meadow
        else if (landN < 0.54f) biome = 1; // Forest
        else biome = 2; // Desert

        byte feature = 0;

        // Structure generation: 0.005% chance per chunk (1 in 20,000)
        Structure? structure = null; // Made nullable to resolve CS8600
        // Restriction: Prevent spawning in Ocean (4), River (7), Ashen (8), or LavaPool (9)
        if (_rand.Next(0, 20000) < 1 && biome != 4 && biome != 7 && biome != 8 && biome != 9)
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