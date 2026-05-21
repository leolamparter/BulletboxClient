using Raylib_cs;
using System.Numerics;

public class HealthBar
{
    private float visualHealth = 100f; // For smooth animation
    private float lerpSpeed = 5f;
    
    private Texture2D heartFullTexture;
    private Texture2D heartFullFlashTexture;
    private Texture2D heartEmptyTexture;
    private Texture2D heartEmptyFlashTexture;
    private Texture2D heartQuarterTexture;
    private Texture2D heartQuarterFlashTexture;
    private Texture2D heartHalfTexture;
    private Texture2D heartHalfFlashTexture;
    
    private bool texturesLoaded = false;

    private int previousHearts = 10;
    private double lastHealthChangeTime = 0;
    private int lastLostHeartStart = -1;
    private int lastLostHeartEnd = -1;

    public void LoadTextures()
    {
        if (!texturesLoaded)
        {
            heartFullTexture = Raylib.LoadTexture("resources/textures/ui/health_bar/heart_full.png");
            heartFullFlashTexture = Raylib.LoadTexture("resources/textures/ui/health_bar/heart_full_flash.png");
            heartEmptyTexture = Raylib.LoadTexture("resources/textures/ui/health_bar/heart_empty.png");
            heartEmptyFlashTexture = Raylib.LoadTexture("resources/textures/ui/health_bar/heart_empty_flash.png");
            heartQuarterTexture = Raylib.LoadTexture("resources/textures/ui/health_bar/heart_quarter.png");
            heartQuarterFlashTexture = Raylib.LoadTexture("resources/textures/ui/health_bar/heart_quarter_flash.png");
            heartHalfTexture = Raylib.LoadTexture("resources/textures/ui/health_bar/heart_half.png");
            heartHalfFlashTexture = Raylib.LoadTexture("resources/textures/ui/health_bar/heart_half_flash.png");
            texturesLoaded = true;
        }
    }

    public void UnloadTextures()
    {
        if (texturesLoaded)
        {
            Raylib.UnloadTexture(heartFullTexture);
            Raylib.UnloadTexture(heartFullFlashTexture);
            Raylib.UnloadTexture(heartEmptyTexture);
            Raylib.UnloadTexture(heartEmptyFlashTexture);
            Raylib.UnloadTexture(heartQuarterTexture);
            Raylib.UnloadTexture(heartQuarterFlashTexture);
            Raylib.UnloadTexture(heartHalfTexture);
            Raylib.UnloadTexture(heartHalfFlashTexture);
            texturesLoaded = false;
        }
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
                    texture = isFlashing ? heartFullFlashTexture : heartFullTexture;
                    break;
                case 3:
                    texture = isFlashing ? heartQuarterFlashTexture : heartQuarterTexture; // Use quarter flash for 3/4
                    break;
                case 2:
                    texture = isFlashing ? heartHalfFlashTexture : heartHalfTexture;
                    break;
                case 1:
                    texture = isFlashing ? heartQuarterFlashTexture : heartQuarterTexture;
                    break;
                default:
                    texture = isFlashing ? heartEmptyFlashTexture : heartEmptyTexture;
                    break;
            }
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