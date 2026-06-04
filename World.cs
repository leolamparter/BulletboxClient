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

    // New: Structures
    public ConcurrentDictionary<(int, int), Structure> Structures = new ConcurrentDictionary<(int, int), Structure>();

    public Chunk GetOrGenerateChunk(int x, int y)
    {
        return _chunks.GetOrAdd((x, y), _ => GenerateChunk(x, y));
    }

    private Chunk GenerateChunk(int x, int y)
    {
        // Simplified generation for now
        byte biome = (byte)_rand.Next(0, 8); // Example biome generation
        byte feature = 0; // No feature by default

        // Structure generation: 0.005% chance per chunk (1 in 20,000)
        Structure? structure = null; // Made nullable to resolve CS8600
        if (_rand.Next(0, 20000) < 1)
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