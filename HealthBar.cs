using Raylib_cs;
using System.Numerics;
using System;

public class HealthBar 
{
    public void Draw(int current, int max) 
    {
        if (max <= 0) return;

        int sw = Raylib.GetScreenWidth();
        int sh = Raylib.GetScreenHeight();

        int totalHearts = 10;
        float percent = Math.Clamp(current / (float)max, 0, 1);
        int totalQuarters = totalHearts * 4;
        int filledQuarters = (int)MathF.Round(percent * totalQuarters);

        float heartSize = 24f;
        float spacing = 4f;
        float totalWidth = (totalHearts * heartSize) + ((totalHearts - 1) * spacing);
        
        // Positioned above the hotbar
        float startX = (sw - totalWidth) / 2;
        float startY = sh - 105;

        for (int i = 0; i < totalHearts; i++)
        {
            int quarters = Math.Clamp(filledQuarters - (i * 4), 0, 4);
            Texture2D tex = quarters switch
            {
                4 => AssetManager.GetTexture("heart_full"),
                3 => AssetManager.GetTexture("heart_quarter"), 
                2 => AssetManager.GetTexture("heart_half"),
                1 => AssetManager.GetTexture("heart_quarter"),
                _ => AssetManager.GetTexture("heart_empty")
            };

            if (tex.Id != 0)
            {
                Raylib.DrawTextureEx(tex, new Vector2(startX + i * (heartSize + spacing), startY), 0f, heartSize / tex.Width, Color.White);
            }
        }

        // Label
        string label = $"{current}/{max}";
        int fontSize = 15;
        int labelW = Raylib.MeasureText(label, fontSize);
        Raylib.DrawText(label, sw / 2 - labelW / 2, (int)startY + (int)heartSize + 2, fontSize, Color.White);
    }
}