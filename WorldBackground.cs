using Raylib_cs;
using System.Numerics;
using System;
using System.Collections.Generic;

namespace BulletboxClient;

public class WorldBackground
{
    private float _scrollX = 0;
    private float _scrollY = 0;
    private const int ChunkSize = 16;
    
    private WorldEnvironment _env = new WorldEnvironment();
    private Shader _lightShader;
    private Shader _postShader;
    private RenderTexture2D _sceneTarget;
    private RenderTexture2D _lightingTarget;
    
    private int _seed;
    private Dictionary<(int, int), Color> _chunkCache = new();
    private Dictionary<(int, int), BiomeType> _biomeCache = new();

    private static readonly Color[] _biomeColors = new Color[]
    {
        new Color(145, 205, 135, 255), // 0: Meadow
        new Color(50, 115, 65, 255),   // 1: Forest
        new Color(230, 205, 140, 255), // 2: Desert
        new Color(140, 145, 155, 255), // 3: Stony Peaks
        new Color(45, 80, 145, 255),   // 4: Ocean
        new Color(240, 220, 180, 255), // 5: Beach
        new Color(210, 95, 60, 255),   // 6: Brimstone
        new Color(75, 150, 210, 255),  // 7: River
        new Color(34, 14, 14, 255),    // 8: Ashen Wastelands (Base: #220e0e)
        new Color(202, 28, 28, 255)    // 9: Lava Pool (Base: #ca1c1c)
    };

    private void LoadFeatureAssets()
    {
        AssetManager.LoadTexture("small_tree", "resources/textures/feature/small_tree.png");
        AssetManager.LoadTexture("large_tree", "resources/textures/feature/large_tree.png");
        AssetManager.LoadTexture("meadow_hedge", "resources/textures/feature/meadow_hedge.png");
        AssetManager.LoadTexture("meadow_flowers", "resources/textures/feature/meadow_flowers.png");
        AssetManager.LoadTexture("stone", "resources/textures/feature/stone.png");
        AssetManager.LoadTexture("palm_tree", "resources/textures/feature/palm_tree.png");
        AssetManager.LoadTexture("desert_log", "resources/textures/feature/desert_log.png");
        AssetManager.LoadTexture("tumbleweed", "resources/textures/feature/tumbleweed.png");
        AssetManager.LoadTexture("oasis_desert", "resources/textures/feature/oasis_desert.png");
        AssetManager.LoadTexture("beach_umbrella", "resources/textures/feature/beach_umbrella.png");
        AssetManager.LoadTexture("sailboat", "resources/textures/feature/sailboat.png");
        AssetManager.LoadTexture("sulfur_spring", "resources/textures/feature/sulfur_spring.png");
    }

    private Color GetBiomeBaseColor(BiomeType biome, int cx, int cy)
    {
        int idx = (int)biome;
        if (idx < 0 || idx >= _biomeColors.Length) return Color.Gray;
        
        Color baseCol = _biomeColors[idx];
        float noise = (Perlin.Noise(cx * 0.20f, cy * 0.20f) + 1f) * 0.5f; // Increased frequency for more detail
        if (biome == BiomeType.River)
        {
            // River: shimmer effect with independent rerolling to match Playing.cs
            int hash = (cx * 73856093) ^ (cy * 19349663);
            float duration = 0.8f + (Math.Abs(hash) % 401 / 1000f); 
            int timeStep = (int)(Raylib.GetTime() / duration);
            int shimmerHash = hash ^ (timeStep * 1103515245);
            int offset = (Math.Abs(shimmerHash) % 21) - 10; 

            return new Color(
                (int)Math.Clamp(baseCol.R + offset, 0, 255),
                (int)Math.Clamp(baseCol.G + offset, 0, 255),
                (int)Math.Clamp(baseCol.B + offset, 0, 255),
                255);
        }
        if (biome == BiomeType.AshenWastelands)
        {
            if (noise < 0.5f)
            {
                // Lerp between #140808 (20, 8, 8) and #330f0f (51, 15, 15) for deeper darks
                float t = noise * 2.0f;
                return new Color(
                    (int)(20 + (51 - 20) * t),
                    (int)(8 + (15 - 8) * t),
                    (int)(8 + (15 - 8) * t),
                    255);
            }
            // Lerp between #330f0f (51, 15, 15) and #352b2b (53, 43, 43)
            float t2 = (noise - 0.5f) * 2.0f;
            return new Color(
                (int)(51 + (54 - 51) * t2),
                (int)(15 + (43 - 15) * t2),
                (int)(15 + (43 - 15) * t2),
                255);
        }
        if (biome == BiomeType.LavaPool)
        {
            if (noise < 0.5f)
            {
                // Lerp between #921212 (146, 18, 18) and #ca1c1c (202, 28, 28)
                float t = noise * 2.0f;
                return new Color(
                    (int)(146 + (202 - 146) * t),
                    (int)(18 + (28 - 18) * t),
                    (int)(18 + (28 - 18) * t),
                    255);
            }
            // Lerp between #ca1c1c (202, 28, 28) and #df8b1c (223, 139, 28)
            float t2 = (noise - 0.5f) * 2.0f;
            return new Color(
                (int)(202 + (223 - 202) * t2),
                (int)(28 + (139 - 28) * t2),
                (int)(28 + (28 - 28) * t2),
                255);
        }
        // Apply color variation to other biomes
        switch (biome)
        {
            case BiomeType.Meadow:
                return new Color(
                    (int)(145 + (95 - 145) * noise),
                    (int)(205 + (155 - 205) * noise),
                    (int)(135 + (85 - 135) * noise),
                    255);
            case BiomeType.Forest:
                return new Color(
                    (int)(50 + (10 - 50) * noise),
                    (int)(115 + (75 - 115) * noise),
                    (int)(65 + (25 - 65) * noise),
                    255);
            case BiomeType.Desert:
                return new Color(
                    (int)(230 + (170 - 230) * noise),
                    (int)(205 + (145 - 205) * noise),
                    (int)(140 + (80 - 140) * noise),
                    255);
            case BiomeType.StonyPeaks:
                return new Color(
                    (int)(140 + (90 - 140) * noise),
                    (int)(145 + (95 - 145) * noise),
                    (int)(155 + (105 - 155) * noise),
                    255);
            case BiomeType.Ocean:
                return new Color(
                    (int)(45 + (10 - 45) * noise),
                    (int)(80 + (45 - 80) * noise),
                    (int)(145 + (110 - 145) * noise),
                    255);
            case BiomeType.Beach:
                return new Color(
                    (int)(240 + (190 - 240) * noise),
                    (int)(220 + (170 - 220) * noise),
                    (int)(180 + (130 - 180) * noise),
                    255);
            case BiomeType.BrimstoneSprings:
                return new Color(
                    (int)(210 + (255 - 210) * noise),
                    (int)(95 + (145 - 95) * noise),
                    (int)(60 + (110 - 60) * noise),
                    255);
            default:
                return baseCol; // Fallback for any other biome
        }
        // return baseCol; // Redundant return
    }
    public WorldBackground()
    {
        _seed = new Random().Next(-1000000, 1000000);
        _lightShader = Raylib.LoadShader(null, "resources/shaders/lighting.fs");
        _postShader = Raylib.LoadShader(null, "resources/shaders/post_process.fs");
        
        int sw = Raylib.GetScreenWidth();
        int sh = Raylib.GetScreenHeight();
        _sceneTarget = Raylib.LoadRenderTexture(sw, sh);
        _lightingTarget = Raylib.LoadRenderTexture(sw, sh);
        
        LoadFeatureAssets();
        _env.SunIntensity = 0.4f;
    }

    public void Update(bool windowResized)
    {
        float dt = Raylib.GetFrameTime(); // dt is still needed for scrolling and environment update
        
        // Scrolling at a consistent speed
        _scrollX += dt * 48f; 
        _scrollY += dt * 32f; 

        // Slow down the day-night cycle for a calmer menu experience
        _env.Update(dt * 0.25f, false);
        
        if (windowResized)
        {
            if (_sceneTarget.Id != 0)
            {
                Raylib.UnloadRenderTexture(_sceneTarget);
                Raylib.UnloadRenderTexture(_lightingTarget);
            }
            _sceneTarget = Raylib.LoadRenderTexture(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
            _lightingTarget = Raylib.LoadRenderTexture(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
                _chunkCache.Clear();
                _biomeCache.Clear();
        }
    }

    public void Draw()
    {
            int sw = _sceneTarget.Texture.Width;
            int sh = _sceneTarget.Texture.Height;

        Raylib.BeginTextureMode(_sceneTarget);
        Raylib.ClearBackground(Color.Black);
        
        int chunkRadiusX = (sw / ChunkSize) / 2 + 2;
        int chunkRadiusY = (sh / ChunkSize) / 2 + 2;
        int centerX = (int)MathF.Floor(_scrollX / ChunkSize);
        int centerY = (int)MathF.Floor(_scrollY / ChunkSize);

        // 1. Terrain Pass (with blending)
        for (int x = -chunkRadiusX; x <= chunkRadiusX; x++)
        {
            for (int y = -chunkRadiusY; y <= chunkRadiusY; y++)
            {
                int cx = centerX + x;
                int cy = centerY + y;
                
                if (!_chunkCache.TryGetValue((cx, cy), out Color col))
                {
                    col = CalculateBlendedColor(cx, cy);
                    _chunkCache[(cx, cy)] = col;
                }
                
                // Re-calculate color for rivers every frame to allow shimmering
                if (GetBiomeAt(cx, cy) == BiomeType.River)
                    col = GetBiomeBaseColor(BiomeType.River, cx, cy);

                float dx = (cx * ChunkSize) - _scrollX + (sw / 2);
                float dy = (cy * ChunkSize) - _scrollY + (sh / 2);
                Raylib.DrawRectangle((int)dx, (int)dy, ChunkSize, ChunkSize, col);
            }
        }

        // 2. Feature Pass (with top-to-bottom Y-sorting)
        for (int y = -chunkRadiusY; y <= chunkRadiusY; y++)
        {
            for (int x = -chunkRadiusX; x <= chunkRadiusX; x++)
            {
                int cx = centerX + x;
                int cy = centerY + y;
                BiomeType biome = GetBiomeAt(cx, cy);
                ServerFeatureType feature = GetFeatureAt(cx, cy, biome);

                if (feature != ServerFeatureType.None)
                {
                    DrawFeature(cx, cy, feature, sw, sh);
                }
            }
        }
        Raylib.EndTextureMode();

        // 3. Atmosphere & Shaders
        UpdateLightingUniforms(sw, sh);
        
        Raylib.BeginTextureMode(_lightingTarget);
            Raylib.BeginShaderMode(_lightShader);
                    Raylib.DrawTextureRec(_sceneTarget.Texture, new Rectangle(0, 0, _sceneTarget.Texture.Width, -_sceneTarget.Texture.Height), Vector2.Zero, Color.White);
            Raylib.EndShaderMode();
        Raylib.EndTextureMode();

        Raylib.BeginShaderMode(_postShader);
            ApplyPostProcessUniforms();
                Raylib.DrawTextureRec(_lightingTarget.Texture, new Rectangle(0, 0, _lightingTarget.Texture.Width, -_lightingTarget.Texture.Height), Vector2.Zero, Color.White);
        Raylib.EndShaderMode();

        DrawAtmosphericGradient(sw, sh);
    }

    private BiomeType GetBiomeAt(int cx, int cy)
    {
        if (_biomeCache.TryGetValue((cx, cy), out var b)) 
        {
            return b;
        }

        float sx = cx + (_seed % 5000);
        float sy = cy + (_seed / 5000);

        float oceanNoise = (Perlin.Noise(sx * 0.003f, sy * 0.003f) + 1f) * 0.5f;
        float ashenNoise = (Perlin.Noise(sx * 0.0015f, sy * 0.0015f) + 1f) * 0.5f;
        float riverNoise = Perlin.Noise(sx * 0.025f, sy * 0.025f);
        float noise = Perlin.Noise(sx * 0.008f, sy * 0.008f);
        float noise2 = Perlin.Noise(sx * 0.008f * 0.5f + 1000, sy * 0.008f * 0.5f - 1000) * 0.5f;
        float n = (noise + noise2 + 1f) * 0.5f;
        float landN = (Perlin.Noise(sx * 0.018f + 5000, sy * 0.018f - 5000) + 1f) * 0.5f;

        BiomeType biome;
        if (oceanNoise < 0.25f) biome = BiomeType.Ocean;
        else if (oceanNoise < 0.30f) biome = BiomeType.Beach;
        else if (ashenNoise > 0.68f) 
        {
            float lavaNoise = (Perlin.Noise(sx * 0.008f, sy * 0.008f) + 1f) * 0.5f;
            if (ashenNoise > 0.695f && lavaNoise > 0.50f) biome = BiomeType.LavaPool;
            else biome = BiomeType.AshenWastelands;
        }
        else if (Math.Abs(riverNoise) < 0.035f) biome = BiomeType.River;
        else if (n > 0.80f) biome = BiomeType.BrimstoneSprings;
        else if (n < 0.20f) biome = BiomeType.StonyPeaks;
        else if (landN < 0.46f) biome = BiomeType.Meadow;
        else if (landN < 0.54f) biome = BiomeType.Forest;
        else biome = BiomeType.Desert;

        _biomeCache[(cx, cy)] = biome;
        return biome;
    }

    private ServerFeatureType GetFeatureAt(int cx, int cy, BiomeType biome)
    {
        int fHash = (cx * 73856093) ^ (cy * 19349663) ^ _seed;
        int roll = Math.Abs(fHash) % 1000;
        // Spatial Filtering: ensure minimum 6 chunks distance
        for (int dx = -5; dx <= 5; dx++) {
            for (int dy = -5; dy <= 5; dy++) {
                if (dx == 0 && dy == 0) continue;
                int nx = cx + dx, ny = cy + dy;
                int nRoll = Math.Abs((nx * 73856093) ^ (ny * 19349663) ^ _seed) % 1000;
                if (nRoll < roll) return ServerFeatureType.None;
                if (nRoll == roll && (nx < cx || (nx == cx && ny < cy))) return ServerFeatureType.None;
            }
        }

        if (biome == BiomeType.Forest && roll < 50) 
        {
            int sub = Math.Abs(fHash >> 8) % 100;
            return sub < 60 ? ServerFeatureType.SmallTree : (sub < 90 ? ServerFeatureType.LargeTree : ServerFeatureType.Stone);
        }
        if (biome == BiomeType.Meadow && roll < 80)
        {
            int sub = Math.Abs(fHash >> 8) % 100;
            return sub < 30 ? ServerFeatureType.MeadowHedge : ServerFeatureType.MeadowFlowers;
        }
        if (biome == BiomeType.Desert && roll < 30)
        {
            int sub = Math.Abs(fHash >> 8) % 100;
            if (sub < 50) return ServerFeatureType.Tumbleweed;
            if (sub < 85) return ServerFeatureType.DesertLog;
            if (sub < 95) return ServerFeatureType.PalmTree;
            return ServerFeatureType.OasisDesert;
        }
        if (biome == BiomeType.Beach && roll < 20)
            return (Math.Abs(fHash >> 8) % 10 < 8) ? ServerFeatureType.PalmTree : ServerFeatureType.BeachUmbrella;
        if (biome == BiomeType.StonyPeaks && roll < 60) return ServerFeatureType.Stone;
        if (biome == BiomeType.Ocean && roll < 4) return ServerFeatureType.Sailboat;
        if (biome == BiomeType.BrimstoneSprings && roll < 40)
            return (Math.Abs(fHash >> 8) % 10 < 4) ? ServerFeatureType.SulfurSpring : ServerFeatureType.Stone;
        
        return ServerFeatureType.None;
    }

    private Color CalculateBlendedColor(int cx, int cy)
    {
        BiomeType myBiome = GetBiomeAt(cx, cy);
        Color baseCol = GetBiomeBaseColor(myBiome, cx, cy);
        if (myBiome == BiomeType.River || myBiome == BiomeType.LavaPool) return baseCol;

        float rSum = baseCol.R, gSum = baseCol.G, bSum = baseCol.B, wSum = 1.0f;
        for (int dx = -1; dx <= 1; dx++) {
            for (int dy = -1; dy <= 1; dy++) {
                if (dx == 0 && dy == 0) continue;
                BiomeType nB = GetBiomeAt(cx + dx, cy + dy);
                if (nB == BiomeType.River) continue;
                float weight = 0.5f;
                Color nCol = GetBiomeBaseColor(nB, cx + dx, cy + dy);
                rSum += nCol.R * weight; gSum += nCol.G * weight; bSum += nCol.B * weight;
                wSum += weight;
            }
        }

        if (myBiome == BiomeType.BrimstoneSprings)
        {
            float r = (rSum / wSum) * 0.85f + 255 * 0.15f;
            float g = (gSum / wSum) * 0.85f + 180 * 0.15f;
            float b = (bSum / wSum) * 0.85f + 100 * 0.15f;
            return new Color((int)r, (int)g, (int)b, 255);
        }

        return new Color((int)(rSum / wSum), (int)(gSum / wSum), (int)(bSum / wSum), 255);
    }

    private void DrawFeature(int cx, int cy, ServerFeatureType type, int sw, int sh)
    {
        string texName = type switch {
            ServerFeatureType.SmallTree => "small_tree",
            ServerFeatureType.LargeTree => "large_tree",
            ServerFeatureType.MeadowHedge => "meadow_hedge",
            ServerFeatureType.MeadowFlowers => "meadow_flowers",
            ServerFeatureType.Stone => "stone",
            ServerFeatureType.PalmTree => "palm_tree",
            ServerFeatureType.DesertLog => "desert_log",
            ServerFeatureType.Tumbleweed => "tumbleweed",
            ServerFeatureType.OasisDesert => "oasis_desert",
            ServerFeatureType.BeachUmbrella => "beach_umbrella",
            ServerFeatureType.Sailboat => "sailboat",
            ServerFeatureType.SulfurSpring => "sulfur_spring",
            _ => ""
        };

        if (string.IsNullOrEmpty(texName)) return;
        Texture2D tex = AssetManager.GetTexture(texName);
        if (tex.Id == 0) return;

        bool isSmall = (type == ServerFeatureType.MeadowHedge || type == ServerFeatureType.MeadowFlowers || 
                        type == ServerFeatureType.Stone || type == ServerFeatureType.DesertLog || 
                        type == ServerFeatureType.Tumbleweed || type == ServerFeatureType.BeachUmbrella);

        float dx = (cx * ChunkSize) - _scrollX + (sw / 2);
        float dy = (cy * ChunkSize) - _scrollY + (sh / 2);

        if (isSmall)
        {
            float scale = ((type == ServerFeatureType.MeadowFlowers) ? 0.35f : 0.5f) * 2.0f;
            Raylib.DrawTexturePro(tex, new Rectangle(0, 0, tex.Width, tex.Height), 
                new Rectangle(dx + 8, dy + 8, tex.Width * scale, tex.Height * scale), 
                new Vector2((tex.Width * scale) / 2f, tex.Height * scale), 0f, Color.White);
        }
        else
        {
            float scale = 4.0f;
            Raylib.DrawTexturePro(tex, new Rectangle(0, 0, tex.Width, tex.Height), 
                new Rectangle(dx + 8, dy + 16, tex.Width * scale, tex.Height * scale), 
                new Vector2((tex.Width * scale) / 2f, tex.Height * scale), 0f, Color.White);
        }
    }

    private void UpdateLightingUniforms(int sw, int sh)
    {
        Vector4 skyTint = new Vector4(_env.SkyTint.R / 255f, _env.SkyTint.G / 255f, _env.SkyTint.B / 255f, _env.SkyTint.A / 255f);
        Raylib.SetShaderValue(_lightShader, Raylib.GetShaderLocation(_lightShader, "skyTint"), skyTint, ShaderUniformDataType.Vec4);
        Raylib.SetShaderValue(_lightShader, Raylib.GetShaderLocation(_lightShader, "exposure"), _env.Exposure, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_lightShader, Raylib.GetShaderLocation(_lightShader, "sunDirection"), _env.ShadowDirection, ShaderUniformDataType.Vec2);
        Raylib.SetShaderValue(_lightShader, Raylib.GetShaderLocation(_lightShader, "screenResolution"), new Vector2(sw, sh), ShaderUniformDataType.Vec2);
        Raylib.SetShaderValue(_lightShader, Raylib.GetShaderLocation(_lightShader, "lightCount"), 0, ShaderUniformDataType.Int);
    }

    private void ApplyPostProcessUniforms()
    {
        Raylib.SetShaderValue(_postShader, Raylib.GetShaderLocation(_postShader, "saturation"), _env.Saturation, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_postShader, Raylib.GetShaderLocation(_postShader, "contrast"), _env.Contrast, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_postShader, Raylib.GetShaderLocation(_postShader, "vignetteIntensity"), _env.NightVignette, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_postShader, Raylib.GetShaderLocation(_postShader, "fogDensity"), _env.FogDensity, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_postShader, Raylib.GetShaderLocation(_postShader, "dustDensity"), _env.DustDensity, ShaderUniformDataType.Float);
        
        Vector4 fogCol = new Vector4(_env.FogColor.R / 255f, _env.FogColor.G / 255f, _env.FogColor.B / 255f, 1f);
        Vector4 dustCol = new Vector4(_env.DustColor.R / 255f, _env.DustColor.G / 255f, _env.DustColor.B / 255f, 1f);
        Raylib.SetShaderValue(_postShader, Raylib.GetShaderLocation(_postShader, "fogColor"), fogCol, ShaderUniformDataType.Vec4);
        Raylib.SetShaderValue(_postShader, Raylib.GetShaderLocation(_postShader, "dustColor"), dustCol, ShaderUniformDataType.Vec4);
    }

    private void DrawAtmosphericGradient(int sw, int sh)
    {
        float intensity = _env.SunIntensity;
        if (intensity <= 0.01f) return;

        Vector2 sunOrigin = new Vector2(sw + 200, -200);
        Color innerColor = new Color((byte)255, (byte)255, (byte)255, (byte)(intensity * 120));
        Color outerColor = new Color((byte)255, (byte)230, (byte)120, (byte)(intensity * 180));

        Raylib.BeginBlendMode(BlendMode.Additive);
        Raylib.DrawCircleGradient((int)sunOrigin.X, (int)sunOrigin.Y, sw * 2.5f, innerColor, outerColor);
        Raylib.EndBlendMode();
    }
}