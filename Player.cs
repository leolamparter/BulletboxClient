using Raylib_cs;
using System.Numerics;

public class Player
{
    public string Name;
    public Vector2 Position;
    public Color Color;
    
    // Add health tracking for visual display
    public int Health = 100;
    public int MaxHealth = 100;

    public Player(string name, Vector2 startPos)
    {
        Name = name;
        Position = startPos;
        Color = Color.White; // Other players are white
    }

    public void Draw()
    {
        // 1. Draw Player Body
        Raylib.DrawRectangleV(Position, new Vector2(64, 64), Color);

        // 2. Draw Name Tag
        int textWidth = Raylib.MeasureText(Name, 20);
        int xPos = (int)(Position.X + 32 - (textWidth / 2)); 
        int yPos = (int)Position.Y - 30;
        Raylib.DrawText(Name, xPos, yPos, 20, Color.White);

        // 3. Simple Overhead Health Bar (Visual only)
        float healthBarWidth = 64;
        float healthPercent = (float)Health / MaxHealth;
        
        // Background (Black)
        Raylib.DrawRectangle((int)Position.X, (int)Position.Y - 10, (int)healthBarWidth, 5, Color.Black);
        // Foreground (Red)
        Raylib.DrawRectangle((int)Position.X, (int)Position.Y - 10, (int)(healthBarWidth * healthPercent), 5, Color.Red);
    }
}