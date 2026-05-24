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
    public byte HeldItemID = 0;
    public float AttackAnimProgress = 0f;
    public bool IsBlocking = false;
    public byte OffHandItemID = 0;

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

        // Initialize and Update the pixelated texture OUTSIDE of the Camera Mode
        // This prevents the camera matrix from being discarded during the draw call.
        if (!_initialized)
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

        // Render the spinning shapes into the buffer
        Raylib.BeginTextureMode(_pixelTarget);
            Raylib.ClearBackground(Color.Blank);
            float canvasScale = 24f / 96f;
            Vector2 texCenter = new Vector2(12, 12);
            Rectangle templateSource = new Rectangle(0, 0, 64, -64);
            
            bool isRaider = Name.StartsWith("Raider");

            // Outer Square (CCW)
            Rectangle outerDest = new Rectangle(texCenter.X, texCenter.Y, 64 * canvasScale, 64 * canvasScale);
            Vector2 outerOrigin = new Vector2(32 * canvasScale, 32 * canvasScale);
            Raylib.DrawTexturePro(_shapeTemplate.Texture, templateSource, outerDest, outerOrigin, -_rotation, isRaider ? Color.Red : Color.DarkGreen);

            // Inner Square (CW)
            float innerSize = 64 * 0.55f * canvasScale;
            Rectangle innerDest = new Rectangle(texCenter.X, texCenter.Y, innerSize, innerSize);
            Vector2 innerOrigin = new Vector2(innerSize / 2, innerSize / 2);
            Raylib.DrawTexturePro(_shapeTemplate.Texture, templateSource, innerDest, innerOrigin, _rotation, Color.Magenta);
        Raylib.EndTextureMode();
    }

    public void TriggerAttack()
    {
        AttackAnimProgress = 1.0f;
    }

    public void Draw()
    {
        // Calculate the center of the player for rotation
        Vector2 center = new Vector2(Position.X + 32, Position.Y + 32);
        
        // Now we just draw the pre-rendered texture. This is world-space safe.
        Rectangle source = new Rectangle(0, 0, 24, -24);
        Rectangle dest = new Rectangle(center.X, center.Y, 96, 96);
        Vector2 destOrigin = new Vector2(48, 48); // Center the 96x96 texture on the player center

        // 1. Determine which weapon texture to use
        string weaponKey = HeldItemID == (byte)'K' ? "kanabo" : (HeldItemID == (byte)'P' ? "spear" : (HeldItemID == (byte)'S' ? "sword" : ""));
        Texture2D weaponTex = !string.IsNullOrEmpty(weaponKey) ? AssetManager.GetTexture(weaponKey) : new Texture2D();

        // 2. Depth Sorting: If pointing 'up' (-20 to -160 degrees), draw weapon behind player
        bool weaponBehind = Rotation < -20 && Rotation > -160;

        void DrawWeapon()
        {
            if (weaponTex.Id == 0) return;
            
            float scale = 1.0f; // 2x larger than previous 0.5f
            float currentHoldRadius = 24f; // Base distance from center to "hand"
            float visualRotation = Rotation;

            // 2. Apply Animation Offsets
            if (AttackAnimProgress > 0)
            {
                float t = 1.0f - AttackAnimProgress; // 0.0 to 1.0 progress
                
                if (HeldItemID == (byte)'P') // Spear: Stab (Linear thrust)
                {
                    currentHoldRadius += MathF.Sin(t * MathF.PI) * 45f;
                }
                else if (HeldItemID == (byte)'S' || HeldItemID == (byte)'K') // Sword/Kanabo: Swing (Arc)
                {
                    visualRotation += (t * 120f) - 60f;
                }
            }

            // Calculate hand position relative to center using possibly modified radius/rotation
            float rad = visualRotation * (MathF.PI / 180f);
            Vector2 handPos = new Vector2(
                center.X + MathF.Cos(rad) * currentHoldRadius,
                center.Y + MathF.Sin(rad) * currentHoldRadius
            );

            Rectangle src = new Rectangle(0, 0, weaponTex.Width, weaponTex.Height);
            Rectangle wDest = new Rectangle(handPos.X, handPos.Y, weaponTex.Width * scale, weaponTex.Height * scale);
            
            // Pivot at the middle-left (the handle)
            Vector2 origin = new Vector2(0, (weaponTex.Height * scale) / 2);
            
            Raylib.DrawTexturePro(weaponTex, src, wDest, origin, visualRotation, Color.White);
        }

        void DrawOffhand()
        {
            if (OffHandItemID != (byte)'H') return; // 'H' for Shield
            Texture2D shieldTex = AssetManager.GetTexture("shield");
            if (shieldTex.Id == 0) return;

            float scale = IsBlocking ? 1.0f : 0.65f;
            float radius = IsBlocking ? 30f : 22f;
            // If blocking, shield is in front. If not, it's tucked to the side (-60 deg offset)
            float visualRotation = IsBlocking ? Rotation : Rotation - 60f;

            float rad = visualRotation * (MathF.PI / 180f);
            Vector2 shieldPos = new Vector2(
                center.X + MathF.Cos(rad) * radius,
                center.Y + MathF.Sin(rad) * radius
            );

            Rectangle src = new Rectangle(0, 0, shieldTex.Width, shieldTex.Height);
            Rectangle dest = new Rectangle(shieldPos.X, shieldPos.Y, shieldTex.Width * scale, shieldTex.Height * scale);
            Vector2 origin = new Vector2((shieldTex.Width * scale) / 2, (shieldTex.Height * scale) / 2);
            Raylib.DrawTexturePro(shieldTex, src, dest, origin, visualRotation + 90f, Color.White);
        }

        // 3. Execution Pass
        if (weaponBehind) { DrawWeapon(); DrawOffhand(); }
        Raylib.DrawTexturePro(_pixelTarget.Texture, source, dest, destOrigin, 0f, Color.White);
        if (!weaponBehind) { DrawWeapon(); DrawOffhand(); }

        // 3. Draw Name Tag
        int textWidth = Raylib.MeasureText(Name, 20);
        int xPos = (int)(Position.X + 32 - (textWidth / 2)); 
        int yPos = (int)Position.Y - 30;
        Raylib.DrawText(Name, xPos, yPos, 20, Color.White);

        // 4. Simple Overhead Health Bar (Visual only)
        float healthBarWidth = 64;
        float healthPercent = (float)Health / MaxHealth;
        
        // Background (Black)
        Raylib.DrawRectangle((int)Position.X, (int)Position.Y - 10, (int)healthBarWidth, 5, Color.Black);
        // Foreground (Red)
        Raylib.DrawRectangle((int)Position.X, (int)Position.Y - 10, (int)(healthBarWidth * healthPercent), 5, Color.Red);
    }
}