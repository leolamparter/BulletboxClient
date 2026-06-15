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

    public static void PlaySoundMulti(string key)
    {
        if (_sounds.TryGetValue(key, out var sound))
        {
            // Standard Raylib.PlaySound used as fallback if PlaySoundMulti is missing in your current version
            Raylib.PlaySound(sound);
        }
    }

    public static void StopSound(string key)
    {
        if (_sounds.TryGetValue(key, out var sound))
        {
            Raylib.StopSound(sound);
        }
    }

    public static void SetVolume(string key, float volume)
    {
        if (_sounds.TryGetValue(key, out var sound))
        {
            Raylib.SetSoundVolume(sound, volume);
        }
    }

    public static bool IsSoundPlaying(string key)
    {
        if (_sounds.TryGetValue(key, out var sound))
        {
            return Raylib.IsSoundPlaying(sound);
        }
        return false;
    }

    public static void StopAll()
    {
        foreach (var sound in _sounds.Values)
        {
            Raylib.StopSound(sound);
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