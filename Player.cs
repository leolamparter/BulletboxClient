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
    public float Rotation = 0f;
    public string HeldItemID = "none";
    public float AttackAnimProgress = 0f;
    public bool IsBlocking = false;
    public string OffHandItemID = "none";

    private float _rotation = 0f;
    private const float _rotationSpeed = 150f; // Degrees per second

    // Pixelation Filter Buffer
    private RenderTexture2D _pixelTarget;    // Per-instance for unique rotations
    private RenderTexture2D _shapeTemplate;  
    private bool _initialized = false;

    public Player(string name, Vector2 startPos)
    {
        Name = name;
        Position = startPos;
        Color = Color.White; // Other players are white
        // Local player is blue, set in Playing.cs constructor
    }

    public void Update(float dt)
    {
        _rotation += _rotationSpeed * dt;
        if (_rotation >= 360f) _rotation -= 360f;
        if (_rotation < 0f) _rotation += 360f;

        if (AttackAnimProgress > 0)
        {
            AttackAnimProgress -= dt / 0.2f; // Animation duration: 0.2 seconds
            if (AttackAnimProgress < 0) AttackAnimProgress = 0;
        }

        bool isMob = Name.StartsWith("Raider") || Name == "Brimstalker" || Name.StartsWith("Flicker");

        // Initialize and Update the pixelated texture OUTSIDE of the Camera Mode for players
        if (!_initialized && !isMob)
        {
            _pixelTarget = Raylib.LoadRenderTexture(24, 24);
            Raylib.SetTextureFilter(_pixelTarget.Texture, TextureFilter.Point);

            _shapeTemplate = Raylib.LoadRenderTexture(64, 64);
            Raylib.BeginTextureMode(_shapeTemplate);
                Raylib.ClearBackground(Color.Blank);
                Raylib.DrawRectangleRounded(new Rectangle(0, 0, 64, 64), 0.25f, 16, Color.White);
            Raylib.EndTextureMode();
            Raylib.SetTextureFilter(_shapeTemplate.Texture, TextureFilter.Bilinear);
            _initialized = true;
        }

        if (!isMob)
        {
            // Render the spinning shapes into the buffer
            Raylib.BeginTextureMode(_pixelTarget);
                Raylib.ClearBackground(Color.Blank);
                float canvasScale = 24f / 96f;
                Vector2 texCenter = new Vector2(12, 12);
                Rectangle templateSource = new Rectangle(0, 0, 64, -64);
                
                // Outer Square (CCW)
                Rectangle outerDest = new Rectangle(texCenter.X, texCenter.Y, 64 * canvasScale, 64 * canvasScale);
                Vector2 outerOrigin = new Vector2(32 * canvasScale, 32 * canvasScale);
                Raylib.DrawTexturePro(_shapeTemplate.Texture, templateSource, outerDest, outerOrigin, -_rotation, Color.DarkGreen);

                // Inner Square (CW)
                float innerSize = 64 * 0.55f * canvasScale;
                Rectangle innerDest = new Rectangle(texCenter.X, texCenter.Y, innerSize, innerSize);
                Vector2 innerOrigin = new Vector2(innerSize / 2, innerSize / 2);
                Raylib.DrawTexturePro(_shapeTemplate.Texture, templateSource, innerDest, innerOrigin, _rotation, Color.Magenta);
            Raylib.EndTextureMode();
        }
    }

    public void TriggerAttack()
    {
        AttackAnimProgress = 1.0f;
    }

    public void Draw()
    {
        // Calculate the center of the player for rotation
        Vector2 center = new Vector2(Position.X + 32, Position.Y + 32);
        
        // Setup destination for the model (96x96 to match original player size)
        Rectangle dest = new Rectangle(center.X, center.Y, 96, 96);
        Vector2 destOrigin = new Vector2(48, 48); // Center the 96x96 texture on the player center

        // 1. Determine which weapon texture to use
        ItemStats.Library.TryGetValue(HeldItemID, out var item);
        string weaponKey = item?.TextureKey ?? "";
        Texture2D weaponTex = !string.IsNullOrEmpty(weaponKey) ? AssetManager.GetTexture(weaponKey) : new Texture2D();

        void DrawWeapon()
        {   
            if (weaponTex.Id == 0) return;
            
            float scale = 4.0f; 
            float currentHoldRadius = 24f; // Base distance from center to "hand"
            float visualRotation = Rotation + 45f; // Apply 45-degree clockwise rotation

            // 2. Apply Animation Offsets
            if (AttackAnimProgress > 0)
            {
                float t = 1.0f - AttackAnimProgress; // 0.0 to 1.0 progress
                
                if (HeldItemID.Contains("spear")) // Any tier of Spear: Stab (Linear thrust)
                {
                    currentHoldRadius += MathF.Sin(t * MathF.PI) * 45f;
                }
                else if (HeldItemID.Contains("sword") || HeldItemID.Contains("kanabo") || HeldItemID.Contains("axe") || HeldItemID.Contains("scythe")) // Swing (Arc)
                {
                    visualRotation += (t * 120f) - 60f;
                }
                else if (HeldItemID.Contains("bow")) // Any Bow: Pull back
                {
                    currentHoldRadius -= MathF.Sin(t * MathF.PI) * 15f;
                }
            }

            // Calculate hand position relative to center using possibly modified radius/rotation
            float rad = visualRotation * (MathF.PI / 180f);
            Vector2 handPos = new Vector2(
                center.X + MathF.Cos(rad) * currentHoldRadius,
                center.Y + MathF.Sin(rad) * currentHoldRadius
            );

            if (HeldItemID.Contains("bow")) // Bow needs to be rotated 90 degrees counter-clockwise
            {
                visualRotation -= 90f;
            }

            Rectangle src = new Rectangle(0, 0, weaponTex.Width, weaponTex.Height);
            Rectangle wDest = new Rectangle(handPos.X, handPos.Y, weaponTex.Width * scale, weaponTex.Height * scale);

            // Pivot at the middle-left (the handle)
            Vector2 origin = new Vector2(0, (weaponTex.Height * scale) / 2);
            
            Raylib.DrawTexturePro(weaponTex, src, wDest, origin, visualRotation, Color.White);
        }

        void DrawOffhand()
        {
            if (OffHandItemID == "none" || string.IsNullOrEmpty(OffHandItemID)) return;
            
            ItemStats.Library.TryGetValue(OffHandItemID, out var item);
            string textureKey = item?.TextureKey ?? "";
            if (string.IsNullOrEmpty(textureKey)) return;
            Texture2D tex = AssetManager.GetTexture(textureKey);
            if (tex.Id == 0) return;

            bool isShield = OffHandItemID == "shield";
            float scale = (isShield && IsBlocking) ? 1.0f : 0.65f;
            float radius = (isShield && IsBlocking) ? 30f : 22f;
            
            float rad = (Rotation - 45f) * (MathF.PI / 180f);
            Vector2 shieldPos = new Vector2(
                center.X + MathF.Cos(rad) * radius,
                center.Y + MathF.Sin(rad) * radius
            );

            Rectangle src = new Rectangle(0, 0, tex.Width, tex.Height);
            Rectangle dest = new Rectangle(shieldPos.X, shieldPos.Y, tex.Width * scale, tex.Height * scale);
            Vector2 origin = new Vector2((tex.Width * scale) / 2, (tex.Height * scale) / 2); // Center pivot
            Raylib.DrawTexturePro(tex, src, dest, origin, Rotation + 45f, Color.White); // Apply 45-degree clockwise rotation
        }

        // 3. Execution Pass - Draw body first, then items on top
        bool isRaider = Name.StartsWith("Raider");
        bool isBrimstalker = Name == "Brimstalker";
        bool isFlicker = Name.StartsWith("Flicker");
        bool isVortex = Name.StartsWith("Vortex");
        bool isApex = Name == "APEX";

        float apexScale = 1.0f;
        if (isApex)
        {
            apexScale = 2.0f;
            float hpPct = Health / (float)MaxHealth;
            if (hpPct > 0.8f) isRaider = true;
            else if (hpPct > 0.6f) isFlicker = true;
            else if (hpPct > 0.4f) isVortex = true;
            else isBrimstalker = true;
        }

        float currentScale = 1.0f;
        if (isFlicker) currentScale = 0.5f; // Flicker is 50% smaller
        if (isApex) currentScale = apexScale;

        if (isRaider || isBrimstalker || isFlicker || isVortex)
        {
            string texPrefix = isFlicker ? "flicker" : (isRaider ? "raidshroomer" : (isBrimstalker ? "brimstalker" : "vortex"));
            string mobTexKey = $"{texPrefix}_idle"; // Default to idle
            
            if (Health < MaxHealth * 0.3f) mobTexKey = $"{texPrefix}_afraid";
            else if (Program.PlayingState != null)
            {
                float dist = Vector2.Distance(Position, Program.PlayingState.LocalPlayer.Position);
                if (dist < 720f) mobTexKey = $"{texPrefix}_angry";
            }

            Texture2D mobTex = AssetManager.GetTexture(mobTexKey);
            if (mobTex.Id != 0)
            {
                Rectangle mobSource = new Rectangle(0, 0, mobTex.Width, mobTex.Height);
                Raylib.DrawTexturePro(mobTex, mobSource, new Rectangle(center.X, center.Y, 96 * currentScale, 96 * currentScale), new Vector2(48 * currentScale, 48 * currentScale), 0f, Color.White);
            }
            else
            {
                Console.WriteLine($"[DEBUG] Failed to load texture for entity '{Name}'. Requested key: '{mobTexKey}'. Please verify the file exists and is correctly named in 'resources/textures/entity/{texPrefix}/'.");
            }
        }
        else
        {
            Rectangle playerSource = new Rectangle(0, 0, _pixelTarget.Texture.Width, -_pixelTarget.Texture.Height);
            Raylib.DrawTexturePro(_pixelTarget.Texture, playerSource, dest, destOrigin, 0f, Color.White);
        }

        // Always draw items in front of the player body
        // Brimstalker and Flicker have no weapons or offhand items drawn
        if (!isBrimstalker && !isFlicker && !isVortex)
        {
            DrawWeapon();
            DrawOffhand();
        }
    }

    public void DrawOverheadHearts(Vector2 worldPos, int health, int max)
    {
        if (max <= 0) return;
        
        Vector2 screenPos = Raylib.GetWorldToScreen2D(worldPos + new Vector2(0, -47), Program.PlayingState!.Cam.RaylibCamera); // worldPos is now player center (Position + 32, 32)
        float percent = Math.Clamp(health / (float)max, 0, 1);
        int totalQuarters = 12; // 3 hearts * 4 quarters
        int filledQuarters = (int)MathF.Round(percent * totalQuarters);

        float heartSize = 16f;
        float spacing = 2f;
        float totalWidth = (3 * heartSize) + (2 * spacing);
        float startX = screenPos.X - (totalWidth / 2);

        for (int i = 0; i < 3; i++)
        {
            int quarters = Math.Clamp(filledQuarters - (i * 4), 0, 4);
            Texture2D tex = quarters switch
            {
                4 => AssetManager.GetTexture("heart_full"),
                3 => AssetManager.GetTexture("heart_quarter"), 
                2 => AssetManager.GetTexture("heart_half"),
                1 => AssetManager.GetTexture("heart_quarter"),
                _ => AssetManager.GetTexture("heart_empty")
            };

            if (tex.Id != 0)
            {
                Raylib.DrawTextureEx(tex, new Vector2(startX + i * (heartSize + spacing), screenPos.Y), 0f, heartSize / tex.Width, Color.White);
            }
        }

        // Draw Name Tag
        if (!Name.StartsWith("Raider") && Name != "Brimstalker" && !Name.StartsWith("Flicker"))
        {
            int textWidth = Raylib.MeasureText(Name, 20);
            int xPos = (int)(screenPos.X - (textWidth / 2));
            int yPos = (int)screenPos.Y - 25; // Position above hearts
            Raylib.DrawText(Name, xPos, yPos, 20, Color.White);
        }
    }
}