using Raylib_cs;
using System.Numerics;
using System;

namespace BulletboxClient;

public class OptionsScreen
{
    private Rectangle _sliderBar = new Rectangle(300, 250, 200, 10);
    private bool _isDragging = false;
    private UIButton _backButton = new UIButton("BACK", Vector2.Zero, 30, true);
    private UIButton _reloadButton = new UIButton("RELOAD TEXTURES", Vector2.Zero, 30);

    public void Update()
    {
        float centerX = Raylib.GetScreenWidth() / 2f;
        float centerY = Raylib.GetScreenHeight() / 2f;

        // Center the slider bar for collision logic
        _sliderBar.X = centerX - (_sliderBar.Width / 2f);
        _sliderBar.Y = centerY;

        // Allow exiting back to the previous screen
        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) Program.CurrentState = Program.cameFrom;

        Vector2 mouse = Raylib.GetMousePosition();

        // Simple slider logic
        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            // Allow clicking slightly outside the thin bar for better UX
            Rectangle clickArea = new Rectangle(_sliderBar.X, _sliderBar.Y - 10, _sliderBar.Width, 30);
            if (Raylib.CheckCollisionPointRec(mouse, clickArea))
            {
                _isDragging = true;
            }
        }

        if (Raylib.IsMouseButtonReleased(MouseButton.Left)) _isDragging = false;

        // Update and handle Back Button
        _backButton.Position = new Vector2(centerX, centerY + 80);
        if (_backButton.IsClicked()) {
            Program.CurrentState = Program.cameFrom;
        }

        // Update and handle Reload Button
        _reloadButton.Position = new Vector2(centerX, centerY + 140);
        if (_reloadButton.IsClicked()) {
            AssetManager.UnloadAll();
            if (Program.PlayingState != null) {
                Program.PlayingState.LoadAssets();
            }
            Console.WriteLine("Textures reloaded from disk.");
        }

        if (_isDragging)
        {
            float t = (mouse.X - _sliderBar.X) / _sliderBar.Width;
            t = Math.Clamp(t, 0.0f, 1.0f);
            // Map slider 0-1 to Zoom levels 0.5f (Wide FOV) to 2.0f (Narrow FOV)
            Settings.FOV = 0.5f + (t * 1.5f);
            Program.CurrentUser.FOV = Settings.FOV;
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
        Raylib.DrawText(title, (int)(centerX - titleW / 2), (int)(centerY - 100), 40, Color.White);

        string label = "Field of View (Zoom)";
        int labelW = Raylib.MeasureText(label, 20);
        Raylib.DrawText(label, (int)(centerX - labelW / 2), (int)(centerY - 30), 20, Color.LightGray);

        Raylib.DrawRectangleRec(_sliderBar, Color.DarkGray);
        float handlePos = (Settings.FOV - 0.5f) / 1.5f;
        Raylib.DrawCircle((int)(_sliderBar.X + (handlePos * _sliderBar.Width)), (int)_sliderBar.Y + 5, 10, Color.White);
        int displayFOV = (int)(150 - (Settings.FOV * 60));
        Raylib.DrawText($"{displayFOV}", (int)(_sliderBar.X + _sliderBar.Width + 20), (int)_sliderBar.Y - 5, 20, Color.White);

        _backButton.Draw();
        _reloadButton.Draw();
    }
}