using Raylib_cs;
using System.Numerics;
using System;

public class UIButton
{
    public string Text;
    public Vector2 Position;
    public int FontSize;
    public Color BaseColor;
    public Color HoverColor;
    
    // New Field
    public bool IsPrimary = false;

    private float currentScale = 1.0f;
    private float targetScale = 1.0f;
    private float transitionSpeed = 10.0f;

    public UIButton(string text, Vector2 pos, int fontSize, bool isPrimary = false)
    {
        Text = text;
        Position = pos;
        FontSize = fontSize;
        BaseColor = Color.Gray;
        HoverColor = Color.White;
        IsPrimary = isPrimary;
    }

    public bool IsClicked()
    {
        // Calculate the "Pulse" offset if it's a primary button
        // We use GetTime() for a continuous smooth sine wave
        float pulse = 0f;
        if (IsPrimary) {
            pulse = (float)Math.Sin(Raylib.GetTime() * 4.0) * 0.2f; 
        }

        int width = Raylib.MeasureText(Text, (int)(FontSize * (currentScale + pulse)));
        Rectangle bounds = new Rectangle(
            Position.X - width / 2, 
            Position.Y - (FontSize * (currentScale + pulse)) / 2, 
            width, 
            FontSize * (currentScale + pulse)
        );

        bool isHovered = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), bounds);
        
        targetScale = isHovered ? 1.25f : 1.0f;
        currentScale = Raylib.GetFrameTime() * transitionSpeed * (targetScale - currentScale) + currentScale;

        return isHovered && Raylib.IsMouseButtonPressed(MouseButton.Left);
    }

    public void Draw()
    {
        // 1. Calculate Pulse
        float pulse = 0f;
        if (IsPrimary) {
            // Math.Sin gives us a value between -1 and 1. 
            // We multiply by 0.05 so it only grows/shrinks by 5%
            pulse = (float)Math.Sin(Raylib.GetTime() * 4.0) * 0.05f;
        }

        int size = (int)(FontSize * (currentScale + pulse));
        int width = Raylib.MeasureText(Text, size);
        
        // 2. Calculate Color (Primary buttons glow Yellow/Gold when not hovered)
        Color color = BaseColor;
        if (IsPrimary) color = Color.LightGray;
        if (targetScale > 1.0f) color = HoverColor;

        // 3. Draw Shadow/Outer Glow for Primary
        if (IsPrimary) {
            Raylib.DrawText(Text, (int)Position.X - width / 2 + 2, (int)Position.Y - size / 2 + 2, size, new Color(0, 0, 0, 100));
        }

        Raylib.DrawText(Text, (int)Position.X - width / 2, (int)Position.Y - size / 2, size, color);
    }
}