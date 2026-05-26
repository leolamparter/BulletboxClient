using Raylib_cs;
using System.Numerics;

public class DisconnectedScreen
{
    private UIButton backButton;

    public DisconnectedScreen()
    {
        backButton = new UIButton("BACK", Vector2.Zero, 30, true);
    }

    public void Update()
    {
        HomeScreen.background.Update();
        float centerX = Raylib.GetScreenWidth() / 2f;
        float centerY = Raylib.GetScreenHeight() / 2f;

        backButton.Position = new Vector2(centerX, centerY + 50);
        if (backButton.IsClicked())
        {
            Program.CurrentState = GameState.HOME;
        }
    }

    public void Draw()
    {
        HomeScreen.background.Draw();
        float centerX = Raylib.GetScreenWidth() / 2f;
        float centerY = Raylib.GetScreenHeight() / 2f;

        Raylib.DrawText("Disconnected.", (int)centerX - Raylib.MeasureText("Disconnected.", 40) / 2, (int)centerY - 50, 40, Color.White);
        backButton.Draw();
    }
}