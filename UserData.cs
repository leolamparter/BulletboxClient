using System.Collections.Generic;

namespace BulletboxClient;

public class UserData
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public bool HasLoggedIn { get; set; } = false;
    public float FOV { get; set; } = 1.0f;
    
    // Tracks if the player has completed the movement tutorial
    public bool MovementTutorialFinnished { get; set; } = false;
    public bool RaidTutorialFinnished { get; set; } = false;
    public bool RaidCompletedTutorialFinnished { get; set; } = false;

    public HashSet<byte> VisitedBiomes { get; set; } = new();
    public HashSet<string> KilledOverworld { get; set; } = new();
    public Dictionary<string, bool> Advancements { get; set; } = new();
}