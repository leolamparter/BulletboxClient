using Raylib_cs;
using System.Numerics;
using System.Collections.Generic;

public class PauseMenu
{
    private List<UIButton> buttons = new List<UIButton>();

    public PauseMenu()
    {
        buttons.Add(new UIButton("BACK TO GAME", Vector2.Zero, 30, true));
        buttons.Add(new UIButton("OPTIONS", Vector2.Zero, 30));
        buttons.Add(new UIButton("DISCONNECT", Vector2.Zero, 30));
    }

    public void Update()
    {
        float centerX = Raylib.GetScreenWidth() / 2;
        float centerY = Raylib.GetScreenHeight() / 2;

        for (int i = 0; i < buttons.Count; i++)
        {
            buttons[i].Position = new Vector2(centerX, centerY - 40 + (i * 60));
            
            if (buttons[i].IsClicked())
            {
                if (i == 0) Program.IsPaused = false;
                if (i == 1) Console.WriteLine("Options clicked!");
                if (i == 2) Program.DisconnectAndLeave();
            }
        }
    }

    public void Draw()
    {
        // Draw a dark semi-transparent overlay over the game
        Raylib.DrawRectangle(0, 0, Raylib.GetScreenWidth(), Raylib.GetScreenHeight(), new Color(0, 0, 0, 150));
        
        Raylib.DrawText("PAUSED", (int)(Raylib.GetScreenWidth()/2 - 70), (int)(Raylib.GetScreenHeight()/2 - 120), 40, Color.Yellow);

        foreach (var btn in buttons) btn.Draw();
    }
}