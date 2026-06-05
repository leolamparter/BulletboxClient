using Raylib_cs;
using System.Numerics;
using System.Collections.Generic;
using System;

namespace BulletboxClient;

public enum WeatherType { Clear, Rain, Fog, DustStorm }

public struct LightSource
{
    public Vector2 Position;
    public Color Color;
    public float Radius;
    public float Intensity;
}

public class WorldEnvironment
{
    // Time Settings (6 minutes = 360 seconds)
    public const float DayLength = 360f; // 6 real-world minutes
    public float CurrentTime = 0f; // Start at sunrise (time 0)
    
    // Weather State
    public WeatherType CurrentWeather = WeatherType.Clear;
    public WeatherType TargetWeather = WeatherType.Clear;
    public float WeatherTransition = 1.0f;
    private float _weatherTimer = 0f;

    // Environment Parameters for Shaders
    public Color SkyTint = Color.White;
    public float ShadowLength = 1.0f;
    public Vector2 ShadowDirection = new Vector2(-1, 1);
    public float GodRayIntensity = 0f;
    public float SunIntensity = 0.4f;
    public float Exposure = 1.0f;
    public float NightVignette = 0f;
    public float FogDensity = 0f;
    public Color FogColor = new Color(200, 200, 220, 255);
    public float DustDensity = 0f;
    public Color DustColor = new Color(180, 150, 80, 255);
    public float Saturation = 1.0f;
    public float Contrast = 1.0f;

    // Lightning State
    private Random _rand = new Random();

    // Lighting
    public List<LightSource> PointLights = new();

    public void Update(float dt, bool isRaidActive)
    {
        // 1. Advance Time
        CurrentTime = (CurrentTime + dt) % DayLength;

        // 2. Calculate Sun Position (Top-down conceptual projection)
        // 0-300 is Day, 300-600 is Night
        float dayProgress = CurrentTime / DayLength;
        float sunAngle = dayProgress * MathF.PI * 2.0f;
        
        // Shadow length is longest at sunrise/sunset, shortest at noon
        // Midday is 150s, Midnight is 450s
        float distFromNoon = MathF.Abs(CurrentTime - 150f);
        if (distFromNoon > 300f) distFromNoon = MathF.Abs(distFromNoon - 600f);
        ShadowLength = 1.0f; // Fixed shadow length

        // 3. Lighting & Color Grading Logic
        UpdateAtmosphere(isRaidActive);

        // 4. Weather Transitions
        UpdateWeather(dt);
    }

    private void UpdateAtmosphere(bool isRaidActive)
    {
        Contrast = 1.05f;
        Saturation = 1.05f;
        ShadowDirection = new Vector2(-1, 1);
        ShadowLength = 1.0f;

        Color orange = new Color(255, 180, 80, 255);
        Color day = Color.White;
        Color night = new Color(20, 40, 220, 255);

        GodRayIntensity = 0f; // Default to off

        if (CurrentTime < 15f) // Sunrise Peak
        {
            SkyTint = orange;
            Exposure = 0.9f;
            SunIntensity = 0.3f;
            NightVignette = 0f;

            // God Ray Fade: 0-5s (In), 5-10s (Peak), 10-15s (Out)
            if (CurrentTime < 5f) 
                GodRayIntensity = CurrentTime / 5f;
            else if (CurrentTime < 10f) 
                GodRayIntensity = 1.0f;
            else 
                GodRayIntensity = 1.0f - (CurrentTime - 10f) / 5f;
            
            GodRayIntensity *= 0.6f; // Cap max visibility
        }
        else if (CurrentTime < 30f) // Fade Sunrise to Day
        {
            float t = (CurrentTime - 15f) / 15f;
            SkyTint = ColorAlphaBlend(orange, day, t);
            Exposure = 0.9f + (1.0f - 0.9f) * t;
            SunIntensity = 0.3f + (0.4f - 0.3f) * t;
            NightVignette = 0f;
        }
        else if (CurrentTime < 135f) // Full Day
        {
            SkyTint = day;
            Exposure = 1.0f;
            SunIntensity = 0.4f;
            NightVignette = 0f;
        }
        else if (CurrentTime < 150f) // Fade Day to Sunset
        {
            float t = (CurrentTime - 135f) / 15f;
            SkyTint = ColorAlphaBlend(day, orange, t);
            Exposure = 1.0f + (0.9f - 1.0f) * t;
            SunIntensity = 0.4f + (0.3f - 0.4f) * t;
            NightVignette = 0f;
        }
        else if (CurrentTime < 165f) // Sunset Peak
        {
            SkyTint = orange;
            Exposure = 0.9f;
            SunIntensity = 0.3f;
            NightVignette = 0f;
        }
        else if (CurrentTime < 180f) // Fade Sunset to Night
        {
            float t = (CurrentTime - 165f) / 15f;
            SkyTint = ColorAlphaBlend(orange, night, t);
            Exposure = 0.9f + (0.35f - 0.9f) * t;
            SunIntensity = 0.3f + (0.02f - 0.3f) * t;
            NightVignette = 0f;

            // Sunset God Rays: 175-180s (Fade In)
            if (CurrentTime >= 175f)
            {
                GodRayIntensity = ((CurrentTime - 175f) / 5f) * 0.6f;
            }
        }
        else // Night Cycle (180 to 360)
        {
            if (CurrentTime < 345f) // Deep Night
            {
                SkyTint = night;
                Exposure = 0.35f;
                SunIntensity = 0.02f;
            }
            else // Fade Night to Sunrise (completes the loop for 360 -> 0)
            {
                float t = (CurrentTime - 345f) / 15f;
                SkyTint = ColorAlphaBlend(night, orange, t);
                Exposure = 0.35f + (0.9f - 0.35f) * t;
                SunIntensity = 0.02f + (0.3f - 0.02f) * t;
            }

            // Sunset God Rays: 180-185s (Fade Out)
            if (CurrentTime < 185f)
            {
                GodRayIntensity = (1.0f - (CurrentTime - 180f) / 5f) * 0.6f;
            }

            // Night Vignette Curve (Starts at 180, peaks at 195, fades by 360)
            if (CurrentTime < 195f)
                NightVignette = (CurrentTime - 180f) / 15f * 0.8f;
            else
                NightVignette = 0.8f - ((CurrentTime - 195f) / 165f * 0.8f);
        }
    }

    private void UpdateWeather(float dt)
    {
        _weatherTimer -= dt;
        if (_weatherTimer <= 0)
        {
            // For testing: Change every 20-40 seconds instead of minutes
            _weatherTimer = _rand.Next(20, 40); 
            
            // Before picking a new target, make the previous target the current
            CurrentWeather = TargetWeather;
            TargetWeather = (WeatherType)_rand.Next(0, 4);
            WeatherTransition = 0f;
        }

        if (WeatherTransition < 1.0f)
        {
            WeatherTransition = Math.Min(1.0f, WeatherTransition + dt * 0.1f); // 10s transition
        }

        // Apply Weather Visuals
        float rainEffect = GetWeatherIntensity(WeatherType.Rain);
        float fogEffect = GetWeatherIntensity(WeatherType.Fog);
        float dustEffect = GetWeatherIntensity(WeatherType.DustStorm);

        if (rainEffect > 0.1f)
        {
            FogDensity = Math.Max(FogDensity, rainEffect * 0.2f);
            FogColor = Color.Gray;
        }
        
        FogDensity = fogEffect > 0 ? fogEffect * 0.8f : FogDensity;
        DustDensity = dustEffect > 0 ? dustEffect * 0.7f : 0f;

        if (dustEffect > 0.1f)
        {
            // Particles still show, but shading remains constant
        }
    }

    public float GetWeatherIntensity(WeatherType type)
    {
        float intensity = 0f;
        if (CurrentWeather == type) intensity = 1.0f - WeatherTransition;
        if (TargetWeather == type) intensity = WeatherTransition;
        return intensity;
    }

    private Color ColorAlphaBlend(Color baseCol, Color blendCol, float amount)
    {
        return new Color(
            (byte)(baseCol.R + (blendCol.R - baseCol.R) * amount),
            (byte)(baseCol.G + (blendCol.G - baseCol.G) * amount),
            (byte)(baseCol.B + (blendCol.B - baseCol.B) * amount),
            (byte)255
        );
    }
}