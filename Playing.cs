using Raylib_cs;
using System.Numerics;
using System.Collections.Generic;
using System; // For Console
using BulletboxClient; // Added to access Settings and OptionsScreen

public class Playing
{
    public Player LocalPlayer;
    public int CurrentHealth = 100;
    public int MaxHealth = 100;
    public Dictionary<string, Player> Others = new Dictionary<string, Player>();
    public CameraManager Cam;
    public Inventory PlayerInventory = new Inventory();
    public HotbarUI Hotbar;
    public InventoryUI InvMenu;
    private HealthBar healthBar = new HealthBar();
    
    // Combat Timers
    private float _cAttackTimer = 0f; 
    private float _cHitTimer = 10f;   
    private int _selectedHotbarIndex = 0;
    private Vector2 _kbVelocity = Vector2.Zero;
    
    private Texture2D _playerIdleTexture;
    private Texture2D _enemyIdleTexture; // Assuming enemies use the same animation for now

    public Playing(string myName)
    {
        LocalPlayer = new Player(myName, new Vector2(400, 300));
        LocalPlayer.Color = Color.Blue;
        Cam = new CameraManager(LocalPlayer.Position);
        Hotbar = new HotbarUI(PlayerInventory);
        InvMenu = new InventoryUI(PlayerInventory);

        LoadAssets();
    }

    public void LoadAssets()
    {
        // Load player and enemy idle textures
        AssetManager.LoadTexture("player_idle", "./resources/textures/entity/player/idle.png");
        AssetManager.LoadTexture("enemy_idle", "./resources/textures/entity/player/idle.png");

        // Load Hotbar UI Textures
        AssetManager.LoadTexture("hotbar_active", "resources/textures/ui/inventory/hotbar_active.png");
        AssetManager.LoadTexture("hotbar_deactive", "resources/textures/ui/inventory/hotbar_deactive.png");

        // Retrieve loaded textures
        _playerIdleTexture = AssetManager.GetTexture("player_idle");
        _enemyIdleTexture = AssetManager.GetTexture("enemy_idle");

        // Diagnostic: Check if textures loaded successfully
        if (_playerIdleTexture.Id == 0) Console.WriteLine("ERROR: 'player_idle' texture failed to load! Please ensure 'resources/textures/entity/player/idle.png' exists and is copied to the output directory.");
        if (_enemyIdleTexture.Id == 0) Console.WriteLine("ERROR: 'enemy_idle' texture failed to load! Please ensure 'resources/textures/entity/player/idle.png' exists and is copied to the output directory.");
        if (AssetManager.GetTexture("hotbar_active").Id == 0) Console.WriteLine("ERROR: 'hotbar_active' texture failed to load! Check path: resources/textures/ui/inventory/hotbar_active.png");
        if (AssetManager.GetTexture("hotbar_deactive").Id == 0) Console.WriteLine("ERROR: 'hotbar_deactive' texture failed to load! Check path: resources/textures/ui/inventory/hotbar_deactive.png");
    }

    public void ApplyKnockback(Vector2 force)
    {
        _kbVelocity += force * 15f; // Multiplier to turn 'distance' into 'velocity'
    }

    public void Update()
    {
        float dt = Raylib.GetFrameTime();
        
        // Update Timers every frame
        _cAttackTimer += dt;
        _cHitTimer += dt;

        // Handle Debug inputs
        Debug.Update();

        // Handle Hotbar Selection (Keys 1-6)
        for (int i = 0; i < 6; i++)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.One + i))
            {
                _selectedHotbarIndex = i;
                Program.Net.SendSlotSwap((byte)i);
            }
        }

        Vector2 lastPos = LocalPlayer.Position;

        HandleMovement(dt);
        
        // Apply and Decay Knockback Velocity
        LocalPlayer.Position += _kbVelocity * dt;
        _kbVelocity = Vector2.Lerp(_kbVelocity, Vector2.Zero, dt * 6.5f); // Smooth friction

        // Update player animations
        LocalPlayer.Update(dt);
        foreach (var other in Others.Values)
        {
            other.Update(dt);
        }
        HandleCombat();

        // KEEPING YOUR ORIGINAL CAMERA LERP
        Cam.Update(LocalPlayer.Position);
        
        // Apply FOV setting from Options
        Cam.Zoom = Settings.FOV; // Assuming CameraManager now has a public 'Zoom' property

        // Network Sync
        if (LocalPlayer.Position != lastPos)
        {
            Program.Net.SendPosition(LocalPlayer.Position.X, LocalPlayer.Position.Y);
        }

        Hotbar.Update();
        InvMenu.Update();
    }

    private void HandleMovement(float dt)
    {
        float speed = 350f;
        if (Raylib.IsKeyDown(KeyboardKey.W)) LocalPlayer.Position.Y -= speed * dt;
        if (Raylib.IsKeyDown(KeyboardKey.S)) LocalPlayer.Position.Y += speed * dt;
        if (Raylib.IsKeyDown(KeyboardKey.A)) 
        {
            LocalPlayer.Position.X -= speed * dt;
            LocalPlayer.FacingRight = false;
        }
        if (Raylib.IsKeyDown(KeyboardKey.D)) 
        {
            LocalPlayer.Position.X += speed * dt;
            LocalPlayer.FacingRight = true;
        }
    }

    private void HandleCombat()
    {
        if (Raylib.IsMouseButtonPressed(MouseButton.Left) && !InvMenu.Visible) 
        {
            byte heldId = PlayerInventory.Slots[_selectedHotbarIndex].ItemID;
            var (dmg, kb, range) = WeaponStats.Calculate(heldId, _cAttackTimer, _cHitTimer);

            if (dmg > 0)
            {
                Vector2 worldMouse = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), Cam.RaylibCamera);
                foreach (var other in Others.Values)
                {
                    Rectangle hitBox = new Rectangle(other.Position.X, other.Position.Y, 64, 64);
                    float dist = Vector2.Distance(LocalPlayer.Position, other.Position);

                    if (Raylib.CheckCollisionPointRec(worldMouse, hitBox) && dist <= range)
                    {
                        Console.WriteLine($"Attacking {other.Name} for {dmg} dmg!");
                        Program.Net.SendAttack(other.Name);
                        
                        _cAttackTimer = 0;
                        _cHitTimer = 0;
                        break; 
                    }
                }
            }
            else 
            {
                _cAttackTimer = 0;
            }
        }
    }

    public void Draw()
    {
        Raylib.DrawRectangle(0, 0, Raylib.GetScreenWidth(), Raylib.GetScreenHeight(), new Color(0, 0, 0, 100));

        Cam.Begin();
            foreach (var other in Others.Values) 
            {
                other.Draw(_enemyIdleTexture); // Pass the animation texture to other players
                Debug.DrawHitbox(other.Position);
            }
            LocalPlayer.Draw(_playerIdleTexture); // Pass the animation texture to the local player
            Debug.DrawHitbox(LocalPlayer.Position);
        Cam.End();

        Hotbar.Draw();
        healthBar.Draw(CurrentHealth, MaxHealth);
        
        // UI Visual for Cooldown (Optional, helps testing)
        DrawCooldownUI();
        
        InvMenu.Draw();
    }

    private void DrawCooldownUI()
    {
        byte heldId = PlayerInventory.Slots[_selectedHotbarIndex].ItemID;
        if (WeaponStats.Library.TryGetValue(heldId, out var stats))
        {
            float charge = Math.Clamp(_cAttackTimer / stats.Cooldown, 0, 1);
            Color barColor = charge < 0.35f ? Color.Red : Color.Green;
            Raylib.DrawRectangle(10, Raylib.GetScreenHeight() - 20, (int)(100 * charge), 10, barColor);
        }
    }
}