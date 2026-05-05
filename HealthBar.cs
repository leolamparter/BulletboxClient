using Raylib_cs;
using System.Numerics;

public class HealthBar
{
    private float visualHealth = 100f; // For smooth animation
    private float lerpSpeed = 5f;

    public void Draw(int current, int max)
    {
        int sw = Raylib.GetScreenWidth();
        int sh = Raylib.GetScreenHeight();

        // Dimensions
        float width = 250;
        float height = 25;
        float x = (sw - width) / 2; // Centered at bottom
        float y = sh - 110;         // Above the hotbar

        // Smoothly slide the health bar value
        visualHealth = Raylib.GetFrameTime() * lerpSpeed * (current - visualHealth) + visualHealth;

        // 1. Background (Dark)
        Raylib.DrawRectangleRounded(new Rectangle(x, y, width, height), 0.5f, 5, new Color(40, 40, 40, 200));

        // 2. Health Fill (Green/Red depending on health)
        float healthWidth = (visualHealth / max) * width;
        Color barColor = current > (max * 0.3f) ? Color.Green : Color.Red;
        
        Raylib.DrawRectangleRounded(new Rectangle(x, y, healthWidth, height), 0.5f, 5, barColor);

        // 3. Border
        Raylib.DrawRectangleRoundedLinesEx(new Rectangle(x, y, width, height), 0.5f, 5, 2.0f, Color.Black);

        // 4. Text
        string label = $"{current}/{max}";
        int textW = Raylib.MeasureText(label, 15);
        Raylib.DrawText(label, (int)(x + width / 2 - textW / 2), (int)(y + 5), 15, Color.White);
    }
}