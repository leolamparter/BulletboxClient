using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using BulletboxClient; // Ensure we use the correct UserData class

// The UserData class definition has been moved to BulletboxClient/UserData.cs

public static class SaveManager {
    private static string dbPath = "saves/global_metadata.db";
    private static string oldJsonPath = "user_data.json";

    public static void Save(UserData data) {
        if (!Directory.Exists("saves")) Directory.CreateDirectory("saves");
        
        // If the old JSON still exists, delete it now that we are saving to SQL
        if (File.Exists(oldJsonPath)) File.Delete(oldJsonPath);

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS GlobalUserData (
                        Id INTEGER PRIMARY KEY CHECK (Id = 1),
                        Data TEXT
                    );
                    INSERT OR REPLACE INTO GlobalUserData (Id, Data) VALUES (1, @Data);";
                cmd.Parameters.AddWithValue("@Data", JsonSerializer.Serialize(data));
                cmd.ExecuteNonQuery();
            }
        }
    }

    public static UserData Load() {
        // Migration: If SQL doesn't exist but JSON does, import it
        if (!File.Exists(dbPath) && File.Exists(oldJsonPath)) {
            try {
                string json = File.ReadAllText(oldJsonPath);
                var data = JsonSerializer.Deserialize<UserData>(json);
                if (data != null) {
                    Save(data); // Save to SQL immediately
                    if (File.Exists(oldJsonPath)) File.Delete(oldJsonPath); // Kill JSON forever
                    return data;
                }
            } catch {}
        }

        if (!File.Exists(dbPath)) return new UserData();

        try
        {
            using (var connection = new SqliteConnection($"Data Source={dbPath}"))
            {
                connection.Open();
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT Data FROM GlobalUserData WHERE Id = 1";
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return JsonSerializer.Deserialize<UserData>(reader.GetString(0)) ?? new UserData();
                        }
                    }
                }
            }
        }
        catch
        {
            // If table doesn't exist yet, return fresh data
        }
        return new UserData();
    }
}