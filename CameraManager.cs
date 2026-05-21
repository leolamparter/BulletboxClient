using Raylib_cs;
using System.Numerics;
using System;

public class CameraManager
{
    public Camera2D RaylibCamera;
    public float LerpSpeed = 25.0f; // Increased for much tighter centering

    public float Zoom
    {
        get => RaylibCamera.Zoom;
        set => RaylibCamera.Zoom = value;
    }

    public CameraManager(Vector2 startTarget)
    {
        RaylibCamera = new Camera2D();
        RaylibCamera.Target = startTarget;
        RaylibCamera.Offset = new Vector2(Raylib.GetScreenWidth() / 2, Raylib.GetScreenHeight() / 2);
        RaylibCamera.Rotation = 0.0f;
        RaylibCamera.Zoom = 1.0f;
    }

    public void Update(Vector2 playerPos, float dt)
    {
        // Calculate the center of the player (64x64 size)
        Vector2 playerCenter = new Vector2(playerPos.X + 32, playerPos.Y + 32);

        // Frame-rate independent Lerp for perfectly smooth tracking
        RaylibCamera.Target.X += (playerCenter.X - RaylibCamera.Target.X) * (1.0f - (float)Math.Exp(-LerpSpeed * dt));
        RaylibCamera.Target.Y += (playerCenter.Y - RaylibCamera.Target.Y) * (1.0f - (float)Math.Exp(-LerpSpeed * dt));

        // Keep the offset updated in case the window is resized
        RaylibCamera.Offset = new Vector2(Raylib.GetScreenWidth() / 2, Raylib.GetScreenHeight() / 2);
    }

    public void Begin() => Raylib.BeginMode2D(RaylibCamera);
    public void End() => Raylib.EndMode2D();
}