using Raylib_cs;
using System.Numerics;
using System;

namespace BulletboxClient;

public class OptionsScreen
{
    private Rectangle _sliderBar = new Rectangle(300, 250, 200, 10);
    private int _draggingSlider = -1; // -1: None, 0: FOV, 1: Music, 2: SFX
    private UIButton _backButton = new UIButton("BACK", Vector2.Zero, 30, true);
    private UIButton _reloadButton = new UIButton("RELOAD TEXTURES", Vector2.Zero, 30);

    public void Update(bool windowResized)
    {
        if (Program.cameFrom == GameState.HOME) HomeScreen.background.Update(windowResized);
        else if (Program.cameFrom == GameState.PLAYING && Program.PlayingState != null)
        {
            // When in options from playing, the game world should still update, especially for resizing.
            Program.PlayingState.Update(Raylib.GetFrameTime(), windowResized);
        }

        float centerX = Raylib.GetScreenWidth() / 2f;
        float centerY = Raylib.GetScreenHeight() / 2f;

        // Allow exiting back to the previous screen
        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) Program.CurrentState = Program.cameFrom;

        Vector2 mouse = Raylib.GetMousePosition();

        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            // Check FOV Slider
            if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(centerX - 100, centerY - 90, 200, 30))) _draggingSlider = 0;
            // Check Music Slider
            else if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(centerX - 100, centerY + 10, 200, 30))) _draggingSlider = 1;
            // Check SFX Slider
            else if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(centerX - 100, centerY + 60, 200, 30))) _draggingSlider = 2;
            // Check Music Toggle
            else if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(centerX - 100, centerY - 35, 200, 25))) Program.MusicEnabled = !Program.MusicEnabled;
        }

        if (Raylib.IsMouseButtonReleased(MouseButton.Left)) _draggingSlider = -1;

        if (_draggingSlider != -1)
        {
            float t = Math.Clamp((mouse.X - (centerX - 100)) / 200f, 0.0f, 1.0f);
            if (_draggingSlider == 0) { Settings.FOV = 0.5f + (t * 1.5f); Program.CurrentUser.FOV = Settings.FOV; }
            else if (_draggingSlider == 1) Program.MusicVolume = t;
            else if (_draggingSlider == 2) Program.SfxVolume = t;
        }

        // Update and handle Back Button
        _backButton.Position = new Vector2(centerX, centerY + 130);
        if (_backButton.IsClicked()) {
            Program.CurrentState = Program.cameFrom;
        }

        // Update and handle Reload Button
        _reloadButton.Position = new Vector2(centerX, centerY + 180);
        if (_reloadButton.IsClicked()) {
            Program.TriggerSplash(GameState.OPTIONS, () => {
                AssetManager.UnloadAll();
                if (Program.PlayingState != null) {
                    Program.PlayingState.LoadAssets();
                }
                Console.WriteLine("Textures reloaded from disk.");
            });
        }
    }

    public void Draw()
    {
        float sw = Raylib.GetScreenWidth();
        float sh = Raylib.GetScreenHeight();
        float centerX = sw / 2f;
        float centerY = sh / 2f;

        Raylib.DrawRectangle(0, 0, (int)sw, (int)sh, new Color(0, 0, 0, 150));

        string title = "OPTIONS";
        int titleW = Raylib.MeasureText(title, 40);
        Raylib.DrawText(title, (int)(centerX - titleW / 2), (int)(centerY - 160), 40, Color.White);

        // FOV Slider
        string label = "Field of View (Zoom)";
        int labelW = Raylib.MeasureText(label, 20);
        Raylib.DrawText(label, (int)(centerX - labelW / 2), (int)(centerY - 110), 20, Color.LightGray);
        DrawSlider(centerX - 100, centerY - 80, (Settings.FOV - 0.5f) / 1.5f, $"{(int)(150 - (Settings.FOV * 60))}");

        // Music Toggle
        string musicToggle = $"Music: {(Program.MusicEnabled ? "ON" : "OFF")}";
        int toggleW = Raylib.MeasureText(musicToggle, 20);
        Raylib.DrawText(musicToggle, (int)(centerX - toggleW / 2), (int)centerY - 35, 20, Program.MusicEnabled ? Color.Green : Color.Red);

        // Music Volume
        DrawSlider(centerX - 100, centerY + 10, Program.MusicVolume, $"Music: {(int)(Program.MusicVolume * 100)}%");

        // SFX Volume
        DrawSlider(centerX - 100, centerY + 60, Program.SfxVolume, $"SFX: {(int)(Program.SfxVolume * 100)}%");

        _backButton.Draw();
        _reloadButton.Draw();
    }

    private void DrawSlider(float x, float y, float value, string text)
    {
        Raylib.DrawRectangle((int)x, (int)y, 200, 10, Color.DarkGray);
        Raylib.DrawCircle((int)(x + (value * 200)), (int)y + 5, 10, Color.White);
        Raylib.DrawText(text, (int)(x + 215), (int)y - 5, 20, Color.White);
    }
}