using Raylib_cs;
using System.Numerics;

public class UIInputBox
{
    public Vector2 Position;
    public float Width;
    public float Height;
    public string Text = "";
    public string Placeholder;
    public int MaxLength;
    private bool _isActive = false;

    public UIInputBox(Vector2 pos, float w, float h, string placeholder, int maxLen)
    {
        Position = pos;
        Width = w;
        Height = h;
        Placeholder = placeholder;
        MaxLength = maxLen;
    }

    public void Update()
    {
        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            // Check collision relative to the center-based position
            Rectangle rec = new Rectangle(Position.X - Width / 2, Position.Y - Height / 2, Width, Height);
            _isActive = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rec);
        }

        if (_isActive)
        {
            int key = Raylib.GetCharPressed();
            while (key > 0)
            {
                if (key >= 32 && key <= 125 && Text.Length < MaxLength)
                {
                    Text += (char)key;
                }
                key = Raylib.GetCharPressed();
            }

            if (Raylib.IsKeyPressed(KeyboardKey.Backspace) && Text.Length > 0)
            {
                Text = Text.Substring(0, Text.Length - 1);
            }
        }
    }

    public void Draw()
    {
        Rectangle rec = new Rectangle(Position.X - Width / 2, Position.Y - Height / 2, Width, Height);
        Raylib.DrawRectangleRec(rec, new Color(30, 30, 30, 255));
        Raylib.DrawRectangleLinesEx(rec, 2, _isActive ? Color.Gold : Color.Gray);

        int fontSize = 20;
        if (string.IsNullOrEmpty(Text) && !_isActive)
        {
            Raylib.DrawText(Placeholder, (int)(Position.X - Width / 2 + 10), (int)(Position.Y - fontSize / 2), fontSize, Color.DarkGray);
        }
        else
        {
            string displayText = Text + (_isActive && (((int)(Raylib.GetTime() * 2)) % 2 == 0) ? "_" : "");
            Raylib.DrawText(displayText, (int)(Position.X - Width / 2 + 10), (int)(Position.Y - fontSize / 2), fontSize, Color.White);
        }
    }
}