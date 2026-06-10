using System.IO;
using System.Collections.Generic;
using System.Text.Json;

public class UserData {
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public bool HasLoggedIn { get; set; } = false;
    public float FOV { get; set; } = 1.0f;
    public bool MovementTutorialFinnished { get; set; } = false;
    public bool RaidTutorialFinnished { get; set; } = false;
    public bool RaidCompletedTutorialFinnished { get; set; } = false;

    public HashSet<byte> VisitedBiomes { get; set; } = new();
    public HashSet<string> KilledOverworld { get; set; } = new();
    public Dictionary<string, bool> Advancements { get; set; } = new();
}

public static class SaveManager {
    private static string path = "user_data.json";

    public static void Save(UserData data) {
        string json = JsonSerializer.Serialize(data);
        File.WriteAllText(path, json);
    }

    public static UserData Load() {
        if (!File.Exists(path)) return new UserData();
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<UserData>(json) ?? new UserData();
    }
}