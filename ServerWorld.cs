using System;
using System.Collections.Generic;
using System.Numerics;

public struct ServerChunkCoord
{
    public int X;
    public int Y;
    public ServerChunkCoord(int x, int y) { X = x; Y = y; }
}

public class ServerChunk
{
    public ServerChunkCoord Coord;
    public byte Biome;
    public byte Feature;
    public ServerChunk(int x, int y, byte biome, byte feature)
    {
        Coord = new ServerChunkCoord(x, y);
        Biome = biome;
        Feature = feature;
    }
}

public class ServerWorld
{
    // Store player positions for proximity checks and movement sync
    public Dictionary<string, Vector2> PlayerLocations = new();

    // Cache for generated world data
    private Dictionary<(int, int), ServerChunk> _chunks = new();
    private readonly object _worldLock = new();

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

    public ServerChunk GetOrGenerateChunk(int x, int y)
    {
        lock (_worldLock)
        {
            if (_chunks.TryGetValue((x, y), out var chunk))
                return chunk;

            // Procedural generation logic
            // Uses a simple hash of coordinates to ensure the world is consistent
            Random r = new Random(x.GetHashCode() ^ (y.GetHashCode() << 2));
            byte biome = (byte)r.Next(0, 5);
            byte feature = (byte)(r.Next(0, 20) == 0 ? 1 : 0);

            var newChunk = new ServerChunk(x, y, biome, feature);
            _chunks[(x, y)] = newChunk;
            return newChunk;
        }
    }
}