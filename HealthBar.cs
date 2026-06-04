using Raylib_cs;
using System.Numerics;
using System;

public class HealthBar 
{
    public void Draw(int current, int max, int hunger) 
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
        float startY = sh - 145; // Moved further up from the hotbar

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
                float yOffset = 0;
                if (percent <= 0.2f)
                {
                    // Low health animation: hearts jump up sequentially once every 3 seconds
                    float time = (float)Raylib.GetTime();
                    float localTime = time % 3.0f; // Total cycle length (wave + wait)
                    float angle = localTime * 15f - i * 0.6f;

                    // Only apply offset during one half-period of the sine wave (the jump)
                    if (angle > 0 && angle < MathF.PI)
                        yOffset = -MathF.Sin(angle) * 10f; // Negative Y moves hearts UP
                }

                Raylib.DrawTextureEx(tex, new Vector2(startX + i * (heartSize + spacing), startY + yOffset), 0f, heartSize / tex.Width, Color.White);
            }
        }

        // Draw Hunger Bar to the right of hearts
        int rounded = ((hunger + 5) / 10) * 10; // Round to nearest 10
        Texture2D hungerTex = AssetManager.GetTexture($"hunger_{rounded}");
        if (hungerTex.Id != 0)
        {
            float hungerX = startX + totalWidth + 15; // 15px padding from the last heart
            float hScale = (heartSize * 2.0f) / hungerTex.Height; // Double the size (2x heart height)
            // Adjust Y by half the extra size to keep it centered with the hearts
            float hungerY = startY - (heartSize / 2.0f); 
            Raylib.DrawTextureEx(hungerTex, new Vector2(hungerX, hungerY), 0f, hScale, Color.White);
        }

        // Label
        string label = $"{current}/{max}";
        int fontSize = 15;
        int labelW = Raylib.MeasureText(label, fontSize);
        Raylib.DrawText(label, sw / 2 - labelW / 2, (int)startY + (int)heartSize + 2, fontSize, Color.White);
    }
}