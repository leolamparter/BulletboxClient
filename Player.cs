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
    public bool FacingRight = true;

    private int _animationFrame = 0;
    private float _frameTimer = 0f;
    private const float _frameDuration = 1.0f / 4.0f; // 4 FPS for a 2-frame animation

    public Player(string name, Vector2 startPos)
    {
        Name = name;
        Position = startPos;
        Color = Color.White; // Other players are white
    }

    public void Update(float dt)
    {
        _frameTimer += dt;
        if (_frameTimer >= _frameDuration)
        {
            _animationFrame = (_animationFrame + 1) % 2; // Cycle between frame 0 and 1
            _frameTimer -= _frameDuration; // Subtract, don't reset, to maintain animation accuracy
        }
    }

    public void Draw(Texture2D? texture = null)
    {
        // 1. Draw Player Body
        if (texture.HasValue && texture.Value.Id != 0)
        {
            // Calculate the source rectangle for the current animation frame
            // To flip the texture, we start the X at the right edge and use a negative width
            Rectangle sourceRec = new Rectangle(
                FacingRight ? 0 : 64, 
                _animationFrame * 64, 
                FacingRight ? 64 : -64, 
                64
            );
            Raylib.DrawTexturePro(texture.Value, sourceRec, new Rectangle(Position.X, Position.Y, 64, 64), Vector2.Zero, 0f, Color.White);
        }
        else
        {
            Raylib.DrawRectangleV(Position, new Vector2(64, 64), Color);
        }

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