using Raylib_cs;
using System.Numerics;
using System.Collections.Generic;

public class HomeScreen
{
    private List<UIButton> buttons;
    public static GameOfLife background = new GameOfLife();
    private string title = "BULLETBOX";

    public void DrawBackgroundOnly() 
    {
        background.Draw();
    }

    public HomeScreen()
    {
        background = new GameOfLife();
        buttons = new List<UIButton>();

        // We initialize with dummy positions; Draw() will position them correctly
        buttons.Add(new UIButton("SINGLEPLAYER", Vector2.Zero, 40, true));
        buttons.Add(new UIButton("FRIENDS", Vector2.Zero, 40));
        buttons.Add(new UIButton("OPTIONS", Vector2.Zero, 40));
        buttons.Add(new UIButton("QUIT GAME", Vector2.Zero, 40));
    }

    public void Update()
    {
        background.Update();
        for (int i = 0; i < buttons.Count; i++)
        {
            if (buttons[i].IsClicked())
            {
                string text = buttons[i].Text;
                if (text == "SINGLEPLAYER") Program.CurrentState = GameState.SINGLEPLAYER_CONNECTING;
                else if (text == "FRIENDS") {
                    Program.CurrentState = GameState.FRIENDS;
                    LanDiscovery.StartListening();
                }
                else if (text == "OPTIONS") {
                    Program.cameFrom = GameState.HOME;
                    Program.CurrentState = GameState.OPTIONS;
                }
                else if (text == "QUIT GAME") Environment.Exit(0);
            }
        }
    }

    public void Draw()
    {
        background.Draw();

        float screenW = Raylib.GetScreenWidth();
        float screenH = Raylib.GetScreenHeight();
        float centerX = screenW / 2;
        float centerY = screenH / 2;

        // Draw Title (Centered and Yellow)
        int titleFontSize = 85;
        int titleWidth = Raylib.MeasureText(title, titleFontSize);
        Raylib.DrawText(title, (int)centerX - titleWidth / 2, (int)centerY - 180, titleFontSize, Color.Yellow);

        // Position and Draw Buttons relative to the center
        float startY = centerY - 40;
        float spacing = 60;

        for (int i = 0; i < buttons.Count; i++)
        {
            buttons[i].Position = new Vector2(centerX, startY + (i * spacing));
            buttons[i].Draw();
        }
    }
}