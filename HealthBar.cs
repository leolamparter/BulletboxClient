using Raylib_cs;
using System.Numerics;

public class HealthBar
{
    private float visualHealth = 100f; // For smooth animation
    private float lerpSpeed = 5f;
    
    private bool texturesLoaded = false;

    private int previousHearts = 10;
    private double lastHealthChangeTime = 0;
    private int lastLostHeartStart = -1;
    private int lastLostHeartEnd = -1;

    public void LoadTextures()
    {
        if (!texturesLoaded)
        {
            AssetManager.LoadTexture("heart_full", "resources/textures/ui/health_bar/heart_full.png");
            AssetManager.LoadTexture("heart_full_flash", "resources/textures/ui/health_bar/heart_full_flash.png");
            AssetManager.LoadTexture("heart_empty", "resources/textures/ui/health_bar/heart_empty.png");
            AssetManager.LoadTexture("heart_empty_flash", "resources/textures/ui/health_bar/heart_empty_flash.png");
            AssetManager.LoadTexture("heart_quarter", "resources/textures/ui/health_bar/heart_quarter.png");
            AssetManager.LoadTexture("heart_quarter_flash", "resources/textures/ui/health_bar/heart_quarter_flash.png");
            AssetManager.LoadTexture("heart_half", "resources/textures/ui/health_bar/heart_half.png");
            AssetManager.LoadTexture("heart_half_flash", "resources/textures/ui/health_bar/heart_half_flash.png");
            texturesLoaded = true;
        }
    }

    public void UnloadTextures()
    {
        // Managed by AssetManager.UnloadAll() in Program.cs
    }

    public void Draw(int current, int max)
    {
        LoadTextures();
        int sw = Raylib.GetScreenWidth();
        int sh = Raylib.GetScreenHeight();

        int totalHearts = 10;
        float heartValue = max / (float)totalHearts;
        float health = current / heartValue;

        // Detect health loss for flashing
        int prevHearts = previousHearts;
        int prevQuarters = (int)(previousHearts * 4);
        int currentQuarters = (int)MathF.Round(health * 4);
        if (currentQuarters < prevQuarters)
        {
            lastHealthChangeTime = Raylib.GetTime();
            lastLostHeartStart = currentQuarters;
            lastLostHeartEnd = prevQuarters;
        }
        previousHearts = (int)MathF.Floor(health);

        // Heart display settings
        float heartSize = 14f;
        float heartSpacing = 4f;
        float totalWidth = (totalHearts * heartSize) + ((totalHearts - 1) * heartSpacing);
        float startX = (sw - totalWidth) / 2;
        float startY = sh - 110;

        double flashDuration = 1.0; // seconds
        double now = Raylib.GetTime();

        for (int i = 0; i < totalHearts; i++)
        {
            float xPos = startX + (i * (heartSize + heartSpacing));
            float yPos = startY;
            int heartIndex = i * 4;
            int quarters = Math.Clamp(currentQuarters - heartIndex, 0, 4);
            bool isFlashing = (lastLostHeartStart <= heartIndex + 3 && heartIndex < lastLostHeartEnd) && (now - lastHealthChangeTime) < flashDuration;
            Texture2D texture;
            switch (quarters)
            {
                case 4:
                    texture = isFlashing ? AssetManager.GetTexture("heart_full_flash") : AssetManager.GetTexture("heart_full");
                    break;
                case 3:
                    texture = isFlashing ? AssetManager.GetTexture("heart_quarter_flash") : AssetManager.GetTexture("heart_quarter");
                    break;
                case 2:
                    texture = isFlashing ? AssetManager.GetTexture("heart_half_flash") : AssetManager.GetTexture("heart_half");
                    break;
                case 1:
                    texture = isFlashing ? AssetManager.GetTexture("heart_quarter_flash") : AssetManager.GetTexture("heart_quarter");
                    break;
                default:
                    texture = isFlashing ? AssetManager.GetTexture("heart_empty_flash") : AssetManager.GetTexture("heart_empty");
                    break;
            }
            if (texture.Id != 0 && texture.Width > 0)
                Raylib.DrawTextureEx(texture, new Vector2(xPos, yPos), 0f, heartSize / texture.Width, Color.White);
        }

        // Draw text label
        string label = $"{current}/{max}";
        int textW = Raylib.MeasureText(label, 15);
        float textX = (sw - textW) / 2;
        float textY = startY + heartSize + 10;
        Raylib.DrawText(label, (int)textX, (int)textY, 15, Color.White);
    }
}