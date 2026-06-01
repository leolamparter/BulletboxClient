using Raylib_cs;
using System.Numerics;
using System;

public class SplashScreen
{
    private Texture2D _logo;
    private float _timer = 0f;
    private float _alpha = 0f;
    private const float FadeInTime = 1.0f;
    private const float StayTime = 1.5f;
    private const float FadeOutTime = 1.0f;
    private bool _hasFinished = false;
    private bool _actionExecuted = false;
    public GameState TargetState = GameState.HOME;
    public Action? LoadingAction;

    public SplashScreen()
    {
        // The studio logo is loaded immediately at boot time
        _logo = Raylib.LoadTexture("resources/textures/ui/other/bbstudios.png");
    }

    public void Reset(GameState nextState, Action? action = null)
    {
        TargetState = nextState;
        LoadingAction = action;
        _timer = 0f;
        _alpha = 0f;
        _actionExecuted = false;
        _hasFinished = false;
    }

    public void Update()
    {
        if (_hasFinished) return;

        float dt = Raylib.GetFrameTime();
        _timer += dt;

        // Manage Alpha channel over time
        if (_timer < FadeInTime)
        {
            _alpha = _timer / FadeInTime;
        }
        else if (_timer < FadeInTime + StayTime)
        {
            _alpha = 1.0f;
            // Execute the loading logic while the screen is fully opaque
            if (!_actionExecuted)
            {
                LoadingAction?.Invoke();
                _actionExecuted = true;
            }
        }
        else if (_timer < FadeInTime + StayTime + FadeOutTime)
        {
            _alpha = 1.0f - ((_timer - (FadeInTime + StayTime)) / FadeOutTime);
        }
        else
        {
            _hasFinished = true;
            Program.CurrentState = TargetState;
        }
    }

    public void Draw()
    {
        Raylib.ClearBackground(new Color(4, 22, 47, 255)); // Dark Blue #04162f

        if (_logo.Id != 0 && !_hasFinished)
        {
            int sw = Raylib.GetScreenWidth();
            int sh = Raylib.GetScreenHeight();
            
            // Render centered and scaled (60% of screen height)
            float scale = (sh * 0.6f) / _logo.Height;
            Rectangle source = new Rectangle(0, 0, _logo.Width, _logo.Height);
            Rectangle dest = new Rectangle(sw / 2, sh / 2, _logo.Width * scale, _logo.Height * scale);
            Vector2 origin = new Vector2((_logo.Width * scale) / 2, (_logo.Height * scale) / 2);

            Raylib.DrawTexturePro(_logo, source, dest, origin, 0f, new Color((byte)255, (byte)255, (byte)255, (byte)(_alpha * 255)));
        }
    }
}