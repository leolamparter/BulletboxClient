using Raylib_cs;
using System.Numerics;

public class CameraManager
{
    public Camera2D RaylibCamera;
    public float LerpFactor = 0.12f; // Adjust this for "laziness" (0.1 is slower, 0.5 is snappier)

    public CameraManager(Vector2 startTarget)
    {
        RaylibCamera = new Camera2D();
        RaylibCamera.Target = startTarget;
        RaylibCamera.Offset = new Vector2(Raylib.GetScreenWidth() / 2, Raylib.GetScreenHeight() / 2);
        RaylibCamera.Rotation = 0.0f;
        RaylibCamera.Zoom = 1.0f;
    }

    public void Update(Vector2 playerPos)
    {
        // Calculate the center of the player (64x64 size)
        Vector2 playerCenter = new Vector2(playerPos.X + 32, playerPos.Y + 32);

        // Smoothly move the camera target towards the player center
        RaylibCamera.Target.X += (playerCenter.X - RaylibCamera.Target.X) * LerpFactor;
        RaylibCamera.Target.Y += (playerCenter.Y - RaylibCamera.Target.Y) * LerpFactor;

        // Keep the offset updated in case the window is resized
        RaylibCamera.Offset = new Vector2(Raylib.GetScreenWidth() / 2, Raylib.GetScreenHeight() / 2);
    }

    public void Begin() => Raylib.BeginMode2D(RaylibCamera);
    public void End() => Raylib.EndMode2D();
}