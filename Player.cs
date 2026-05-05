using Raylib_cs;
using System.Numerics;

public class Player
{
    public string Name;
    public Vector2 Position;
    public Color Color;

    public Player(string name, Vector2 startPos)
    {
        Name = name;
        Position = startPos;
        Color = Color.Yellow; // Default color
    }

    public void Draw()
    {
        // Draw the Player Body (Updated to 64x64)
        Raylib.DrawRectangleV(Position, new Vector2(64, 64), Color);

        // Draw Name Tag (Adjusted for 64 width)
        int textWidth = Raylib.MeasureText(Name, 20);
        int xPos = (int)(Position.X + 32 - (textWidth / 2)); 
        int yPos = (int)Position.Y - 25;

        Raylib.DrawText(Name, xPos, yPos, 20, Color.Yellow);
    }
}