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

    public Playing(string myName)
    {
        LocalPlayer = new Player(myName, new Vector2(400, 300));
        LocalPlayer.Color = Color.Blue;
        Cam = new CameraManager(LocalPlayer.Position);
        Hotbar = new HotbarUI(PlayerInventory);
        InvMenu = new InventoryUI(PlayerInventory);
    }

    public void Update()
    {
        float dt = Raylib.GetFrameTime();
        
        // Update Timers every frame
        _cAttackTimer += dt;
        _cHitTimer += dt;

        float speed = 350f;
        Vector2 lastPos = LocalPlayer.Position;

        // Movement
        if (Raylib.IsKeyDown(KeyboardKey.W)) LocalPlayer.Position.Y -= speed * dt;
        if (Raylib.IsKeyDown(KeyboardKey.S)) LocalPlayer.Position.Y += speed * dt;
        if (Raylib.IsKeyDown(KeyboardKey.A)) LocalPlayer.Position.X -= speed * dt;
        if (Raylib.IsKeyDown(KeyboardKey.D)) LocalPlayer.Position.X += speed * dt;

        // Combat Logic
        if (Raylib.IsMouseButtonPressed(MouseButton.Left) && !InvMenu.Visible) 
        {
            // Get currently held weapon from hotbar (Slot 0)
            byte heldId = PlayerInventory.Slots[0].ItemID;
            
            // Calculate potential damage/range based on your new math
            var (dmg, kb, range) = WeaponStats.Calculate(heldId, _cAttackTimer, _cHitTimer);

            // Only proceed if charge is > 35% (dmg will be > 0)
            if (dmg > 0)
            {
                Vector2 worldMouse = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), Cam.RaylibCamera);

                foreach (var other in Others.Values)
                {
                    Rectangle hitBox = new Rectangle(other.Position.X, other.Position.Y, 64, 64);
                    float dist = Vector2.Distance(LocalPlayer.Position, other.Position);

                    // Check if mouse is over them AND they are within your dynamically scaled range
                    if (Raylib.CheckCollisionPointRec(worldMouse, hitBox) && dist <= range)
                    {
                        Console.WriteLine($"Attacking {other.Name} for {dmg} dmg!");
                        Program.Net.SendAttack(other.Name);
                        
                        _cAttackTimer = 0; // Reset charge
                        _cHitTimer = 0;    // Reset combo timer
                        break; 
                    }
                }
            }
            else 
            {
                // Penalize spamming: clicking under 35% resets your charge to zero
                _cAttackTimer = 0;
            }
        }

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
        byte heldId = PlayerInventory.Slots[0].ItemID;
        if (WeaponStats.Library.TryGetValue(heldId, out var stats))
        {
            float charge = Math.Clamp(_cAttackTimer / stats.Cooldown, 0, 1);
            Color barColor = charge < 0.35f ? Color.Red : Color.Green;
            Raylib.DrawRectangle(10, Raylib.GetScreenHeight() - 20, (int)(100 * charge), 10, barColor);
        }
    }
}