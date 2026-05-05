using System.IO;
using System.Text.Json;

public class UserData {
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public bool HasLoggedIn { get; set; } = false;
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