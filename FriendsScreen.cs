using Raylib_cs;
using System.Numerics;
using System.Collections.Generic;
using System;
using System.Linq;

public class FriendsScreen
{
    private UIButton backButton;
    private List<(string ip, int port, UIButton joinBtn)> worldButtons = new();

    public FriendsScreen()
    {
        backButton = new UIButton("BACK", Vector2.Zero, 30, true);
    }

    public void Update()
    {
        HomeScreen.background.Update();
        LanDiscovery.Update();

        float centerX = Raylib.GetScreenWidth() / 2f;
        float centerY = Raylib.GetScreenHeight() / 2f;

        backButton.Position = new Vector2(centerX, Raylib.GetScreenHeight() - 60);
        if (backButton.IsClicked())
        {
            LanDiscovery.StopListening();
            Program.CurrentState = GameState.HOME;
        }

        lock (LanDiscovery.DiscoveredWorlds)
        {
            // Simple sync: if count differs, rebuild the list
            if (worldButtons.Count != LanDiscovery.DiscoveredWorlds.Count)
            {
                worldButtons.Clear();
                foreach (var world in LanDiscovery.DiscoveredWorlds)
                {
                    worldButtons.Add((world.Key, world.Value.port, new UIButton("JOIN", Vector2.Zero, 20)));
                }
            }
        }

        for (int i = 0; i < worldButtons.Count; i++)
        {
            var item = worldButtons[i];
            item.joinBtn.Position = new Vector2(centerX + 150, centerY - 100 + (i * 40));
            if (item.joinBtn.IsClicked())
            {
                if (string.IsNullOrEmpty(Program.CurrentUser.Username)) Program.CurrentUser.Username = "Player";

                // Pre-initialize PlayingState so it's ready to catch incoming packets (health, pos, etc.)
                if (Program.PlayingState == null)
                {
                    Program.PlayingState = new Playing(Program.CurrentUser.Username);
                }

                LanDiscovery.StopListening();
                Program.Net.Connect(item.ip, item.port, Program.CurrentUser.Username, "lan_auth");
                if (Program.Net.IsConnected()) Program.CurrentState = GameState.PLAYING;
            }
        }
    }

    public void Draw()
    {
        HomeScreen.background.Draw();
        float centerX = Raylib.GetScreenWidth() / 2f;
        float centerY = Raylib.GetScreenHeight() / 2f;

        Raylib.DrawText("LOCAL NETWORK WORLDS", (int)centerX - 180, (int)centerY - 180, 30, Color.Yellow);

        if (worldButtons.Count == 0)
        {
            Raylib.DrawText("Scanning for worlds...", (int)centerX - 100, (int)centerY, 20, Color.LightGray);
        }

        for (int i = 0; i < worldButtons.Count; i++)
        {
            var item = worldButtons[i];
            Raylib.DrawText($"Server on port: {item.port}", (int)centerX - 150, (int)centerY - 100 + (i * 40) - 10, 20, Color.White);
            item.joinBtn.Draw();
        }
        backButton.Draw();
    }
}