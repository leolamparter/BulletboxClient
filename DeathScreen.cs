using Raylib_cs;
using System.Numerics;

public class DeathScreen
{
    private UIButton respawnButton;
    private UIButton titleButton;

    public DeathScreen()
    {
        respawnButton = new UIButton("RESPAWN", Vector2.Zero, 35, true);
        titleButton = new UIButton("BACK TO TITLE", Vector2.Zero, 30);
    }

    public void Update()
    {
        HomeScreen.background.Update();
        float centerX = Raylib.GetScreenWidth() / 2f;
        float centerY = Raylib.GetScreenHeight() / 2f;

        respawnButton.Position = new Vector2(centerX, centerY + 30);
        titleButton.Position = new Vector2(centerX, centerY + 90);

        if (respawnButton.IsClicked())
        {
            if (Program.LastIP == "127.0.0.1")
            {
                Program.CurrentState = GameState.SINGLEPLAYER_CONNECTING;
            }
            else
            {
                // Re-initialize state for server/LAN reconnect
                if (Program.PlayingState == null) Program.PlayingState = new Playing(Program.CurrentUser.Username);
                Program.Net.Connect(Program.LastIP, Program.LastPort, Program.CurrentUser.Username, "respawn_auth");
                
                if (Program.Net.IsConnected()) Program.CurrentState = GameState.PLAYING;
                else Program.CurrentState = GameState.DISCONNECTED;
            }
        }

        if (titleButton.IsClicked())
        {
            Program.CurrentState = GameState.HOME;
        }
    }

    public void Draw()
    {
        HomeScreen.background.Draw();
        float centerX = Raylib.GetScreenWidth() / 2f;
        float centerY = Raylib.GetScreenHeight() / 2f;

        // Dark red overlay for a "bloody" death effect
        Raylib.DrawRectangle(0, 0, Raylib.GetScreenWidth(), Raylib.GetScreenHeight(), new Color(40, 0, 0, 160));

        Raylib.DrawText("YOU DIED!", (int)centerX - Raylib.MeasureText("YOU DIED!", 60) / 2, (int)centerY - 80, 60, Color.Red);
        
        respawnButton.Draw();
        titleButton.Draw();
    }
}