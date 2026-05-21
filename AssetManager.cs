using Raylib_cs;
using System.Collections.Generic;

public static class AssetManager
{
    private static Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();

    public static void LoadTexture(string key, string path)
    {
        if (!_textures.ContainsKey(key))
        {
            _textures[key] = Raylib.LoadTexture(path);
        }
    }

    public static Texture2D GetTexture(string key)
    {
        if (_textures.TryGetValue(key, out var tex)) return tex;
        // Return an empty/dummy texture if not found to prevent crashes
        return new Texture2D(); 
    }

    public static void UnloadAll()
    {
        foreach (var tex in _textures.Values)
        {
            Raylib.UnloadTexture(tex);
        }
        _textures.Clear();
    }
}
