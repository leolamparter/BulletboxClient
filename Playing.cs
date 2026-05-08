using Raylib_cs;
using System.Numerics;
using System.Collections.Generic;
using System; // For Console

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

    public Playing(string myName)
    {
        LocalPlayer = new Player(myName, new Vector2(400, 300));
        LocalPlayer.Color = Color.Blue;
        Cam = new CameraManager(LocalPlayer.Position);
        Hotbar = new HotbarUI(PlayerInventory);
        InvMenu = new InventoryUI(PlayerInventory);
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

        HandleCombat();

        // KEEPING YOUR ORIGINAL CAMERA LERP
        Cam.Update(LocalPlayer.Position);

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
        if (Raylib.IsKeyDown(KeyboardKey.A)) LocalPlayer.Position.X -= speed * dt;
        if (Raylib.IsKeyDown(KeyboardKey.D)) LocalPlayer.Position.X += speed * dt;
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
            foreach (var other in Others.Values) other.Draw();
            LocalPlayer.Draw();
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