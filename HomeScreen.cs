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
        buttons.Add(new UIButton("PLAY", Vector2.Zero, 40, true));
        buttons.Add(new UIButton("MODS", Vector2.Zero, 40));
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
                if (i == 0)
                {
                    if (Program.CurrentUser.HasLoggedIn) 
                        Program.CurrentState = GameState.PLAYING;
                    else 
                        Program.CurrentState = GameState.LOGIN;
                }
                if (i == 1) Console.WriteLine("Mods!");
                if (i == 2) Console.WriteLine("Options!");
                if (i == 3) Environment.Exit(0);
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