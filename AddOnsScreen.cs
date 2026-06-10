using Raylib_cs;
using System.Numerics;

public class AddOnsScreen
{
    private UIButton backButton;

    public AddOnsScreen()
    {
        // Initialize the back button with a dummy position; it will be positioned in Draw()
        backButton = new UIButton("BACK", Vector2.Zero, 30);
    }

    public void Update(bool windowResized)
    {
        HomeScreen.background.Update(windowResized); // Keep the background moving

        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            Vector2 mouse = Raylib.GetMousePosition();
            int sw = Raylib.GetScreenWidth();
            int sh = Raylib.GetScreenHeight();

            // Toggle Speedrun Timer Hitbox
            Rectangle toggleRect = new Rectangle(sw / 2 - 150, sh / 2 - 20, 300, 40); // This is a hardcoded hitbox, not a UIButton
            if (Raylib.CheckCollisionPointRec(mouse, toggleRect))
            {
                Program.SpeedrunTimerEnabled = !Program.SpeedrunTimerEnabled;
            }
        }
        if (backButton.IsClicked()) Program.CurrentState = GameState.HOME;
    }

    public void Draw()
    {
        HomeScreen.background.Draw();
        int sw = Raylib.GetScreenWidth();
        int sh = Raylib.GetScreenHeight();

        // Title
        string title = "ADD-ONS";
        int titleWidth = Raylib.MeasureText(title, 40);
        Raylib.DrawText(title, sw / 2 - titleWidth / 2, 60, 40, Color.White);

        // Speedrun Timer Toggle
        string toggleText = $"Speedrun Timer: {(Program.SpeedrunTimerEnabled ? "ON" : "OFF")}";
        int toggleWidth = Raylib.MeasureText(toggleText, 30);
        Raylib.DrawText(toggleText, sw / 2 - toggleWidth / 2, sh / 2 - 15, 30, Program.SpeedrunTimerEnabled ? Color.Green : Color.Red);

        // Back Button (now a UIButton)
        // Position the button at the bottom center, similar to the original text placement
        backButton.Position = new Vector2(sw / 2, sh - 65); // Center Y adjusted for font size 30
        backButton.Draw();
    }
}