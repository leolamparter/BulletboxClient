using Raylib_cs;
using System.Numerics;

public static class Debug
{
    public static bool ShowHitboxes = false;

    public static void Update()
    {
        // Hold Grave (`) and press H to toggle hitboxes
        if (Raylib.IsKeyDown(KeyboardKey.Grave) && Raylib.IsKeyPressed(KeyboardKey.H))
        {
            ShowHitboxes = !ShowHitboxes;
        }
    }

    public static void DrawHitbox(Vector2 position)
    {
        if (ShowHitboxes)
        {
            // Draw a yellow outline exactly where the 64x64 hitbox is
            Raylib.DrawRectangleLines((int)position.X, (int)position.Y, 64, 64, Color.Yellow);
        }
    }
}