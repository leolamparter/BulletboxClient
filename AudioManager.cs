using Raylib_cs;
using System.Collections.Generic;

public static class AudioManager
{
    private static Dictionary<string, Sound> _sounds = new Dictionary<string, Sound>();

    public static void LoadSound(string key, string path)
    {
        if (!_sounds.ContainsKey(key))
        {
            _sounds[key] = Raylib.LoadSound(path);
        }
    }

    public static void PlaySound(string key)
    {
        if (_sounds.TryGetValue(key, out var sound))
        {
            Raylib.PlaySound(sound);
        }
    }

    public static void UnloadAll()
    {
        foreach (var sound in _sounds.Values)
        {
            Raylib.UnloadSound(sound);
        }
        _sounds.Clear();
    }
}