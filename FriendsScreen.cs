using Raylib_cs;
using System.Numerics;
using System.Collections.Generic;
using System;
using System.Linq;

public class FriendsScreen
{
    private UIButton backButton;
    private List<(string ip, int port, UIButton joinBtn)> worldButtons = new();
    private string _manualIp = "";
    private string _manualPort = "32308";
    private int _activeField = -1; // 0 for IP, 1 for Port
    private UIButton _manualJoinBtn;

    public FriendsScreen()
    {
        backButton = new UIButton("BACK", Vector2.Zero, 30, true);
        _manualJoinBtn = new UIButton("JOIN SERVER", Vector2.Zero, 20, true);
    }

    public void Update()
    {
        HomeScreen.background.Update();
        LanDiscovery.Update();

        float centerX = Raylib.GetScreenWidth() / 2f;
        float centerY = Raylib.GetScreenHeight() / 2f;

        Vector2 mouse = Raylib.GetMousePosition();

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

        // Manual IP Input Handling
        int sh = Raylib.GetScreenHeight();
        if (Raylib.IsMouseButtonPressed(MouseButton.Left)) {
            if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(centerX - 200, sh - 130, 250, 30))) _activeField = 0;
            else if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(centerX + 60, sh - 130, 80, 30))) _activeField = 1;
            else _activeField = -1;
        }

        if (_activeField != -1) {
            int key = Raylib.GetCharPressed();
            while (key > 0) {
                if (key >= 32 && key <= 125) {
                    if (_activeField == 0 && _manualIp.Length < 32) _manualIp += (char)key;
                    else if (_activeField == 1 && _manualPort.Length < 5) _manualPort += (char)key;
                }
                key = Raylib.GetCharPressed();
            }
            if (Raylib.IsKeyPressed(KeyboardKey.Backspace)) {
                if (_activeField == 0 && _manualIp.Length > 0) _manualIp = _manualIp[..^1];
                else if (_activeField == 1 && _manualPort.Length > 0) _manualPort = _manualPort[..^1];
            }
        }

        _manualJoinBtn.Position = new Vector2(centerX + 230, sh - 115);
        if (_manualJoinBtn.IsClicked() && !string.IsNullOrEmpty(_manualIp)) {
            if (int.TryParse(_manualPort, out int p)) {
                if (string.IsNullOrEmpty(Program.CurrentUser.Username)) Program.CurrentUser.Username = "Player";
                if (Program.PlayingState == null) Program.PlayingState = new Playing(Program.CurrentUser.Username);
                LanDiscovery.StopListening();
                Program.Net.Connect(_manualIp, p, Program.CurrentUser.Username, "manual_auth");
                if (Program.Net.IsConnected()) Program.CurrentState = GameState.PLAYING;
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

        // Manual IP UI Rendering
        int shHeight = Raylib.GetScreenHeight();
        Raylib.DrawText("DIRECT JOIN:", (int)centerX - 350, shHeight - 125, 20, Color.Gray);
        
        // IP Field
        Raylib.DrawRectangle((int)centerX - 200, shHeight - 130, 250, 30, new Color(30, 30, 30, 255));
        Raylib.DrawRectangleLines((int)centerX - 200, shHeight - 130, 250, 30, _activeField == 0 ? Color.Yellow : Color.DarkGray);
        Raylib.DrawText(string.IsNullOrEmpty(_manualIp) && _activeField != 0 ? "IP Address..." : _manualIp, (int)centerX - 190, shHeight - 123, 18, Color.White);

        // Port Field
        Raylib.DrawRectangle((int)centerX + 60, shHeight - 130, 80, 30, new Color(30, 30, 30, 255));
        Raylib.DrawRectangleLines((int)centerX + 60, shHeight - 130, 80, 30, _activeField == 1 ? Color.Yellow : Color.DarkGray);
        Raylib.DrawText(_manualPort, (int)centerX + 70, shHeight - 123, 18, Color.White);

        _manualJoinBtn.Draw();
    }
}