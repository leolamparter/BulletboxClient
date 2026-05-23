
using Raylib_cs;
using System.Numerics;
using System.Collections.Generic;
using System; // For Console
using BulletboxClient; // Added to access Settings and OptionsScreen

public class Playing
{
    // Biome chunk system prototype
    private HashSet<(int, int)> loadedChunks = new();
    private const int chunkSize = 16;
    private const int chunkViewRadius = 40; // How many chunks to load around player
    public Player LocalPlayer;
    public int CurrentHealth = 100;
    public int MaxHealth = 100;
    public Dictionary<string, Player> Others = new Dictionary<string, Player>();
    public CameraManager Cam;
    public Inventory PlayerInventory = new Inventory();
    public HotbarUI Hotbar;
    public InventoryUI InvMenu;
    private HealthBar healthBar = new HealthBar();
    
    // Optimization Caches
    private Dictionary<(int, int), byte> _chunkSnapshot = new();
    private Dictionary<(int, int), Color> _blendedColorCache = new();
    private HashSet<(int, int)> _pendingBlends = new();
    private int _lastPlayerChunkX = int.MaxValue;
    private int _lastPlayerChunkY = int.MaxValue;
    private float _refreshTimer = 0f;

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

        LoadAssets();
    }

    public void LoadAssets()
    {
        // Load player and enemy idle textures
        // Load Hotbar UI Textures
        AssetManager.LoadTexture("hotbar_active", "resources/textures/ui/inventory/hotbar_active.png");
        AssetManager.LoadTexture("hotbar_deactive", "resources/textures/ui/inventory/hotbar_deactive.png");

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

        // Biome chunk loading/unloading
        int playerChunkX = (int)MathF.Floor(LocalPlayer.Position.X / chunkSize);
        int playerChunkY = (int)MathF.Floor(LocalPlayer.Position.Y / chunkSize);

        // Optimization: Only update loading/unloading logic when player enters a new chunk
        if (playerChunkX != _lastPlayerChunkX || playerChunkY != _lastPlayerChunkY)
        {
            _lastPlayerChunkX = playerChunkX;
            _lastPlayerChunkY = playerChunkY;

            HashSet<(int, int)> needed = new();
            for (int dx = -chunkViewRadius; dx <= chunkViewRadius; dx++)
            {
                for (int dy = -chunkViewRadius; dy <= chunkViewRadius; dy++)
                {
                    int cx = playerChunkX + dx;
                    int cy = playerChunkY + dy;
                    needed.Add((cx, cy));
                    if (!loadedChunks.Contains((cx, cy)))
                    {
                        Program.Net.SendChunkRequest(cx, cy);
                        loadedChunks.Add((cx, cy));
                    }
                }
            }
            // Unload far chunks
            loadedChunks.RemoveWhere(c => !needed.Contains(c));

            // Clean up caches only when moving chunks to save CPU
            foreach (var coord in new List<(int, int)>(_blendedColorCache.Keys)) {
                if (!loadedChunks.Contains(coord)) _blendedColorCache.Remove(coord);
            }
            _pendingBlends.RemoveWhere(c => !loadedChunks.Contains(c));
        }

        // Update chunk snapshot and identify work for the amortized blender
        lock (Program.Net.ChunkBiomesLock)
        {
            // Check if we have new data compared to our snapshot
            if (_chunkSnapshot.Count != Program.Net.ChunkBiomes.Count) 
            {
                foreach (var kvp in Program.Net.ChunkBiomes)
                {
                    if (!_chunkSnapshot.TryGetValue(kvp.Key, out byte existing) || existing != kvp.Value)
                    {
                        // Mark new chunk and its neighbors within radius 3 for re-evaluation
                        for (int x = -3; x <= 3; x++) {
                            for (int y = -3; y <= 3; y++) {
                                var target = (kvp.Key.Item1 + x, kvp.Key.Item2 + y);
                                // IMPORTANT: Remove from cache to force a fresh calculation with the new neighbor data
                                _blendedColorCache.Remove(target);
                                _pendingBlends.Add(target);
                            }
                        }
                    }
                }
                _chunkSnapshot = new Dictionary<(int, int), byte>(Program.Net.ChunkBiomes);
            }
        }

        // Periodically re-queue all loaded chunks for blending to catch any "stuck" areas
        _refreshTimer += dt;
        if (_refreshTimer >= 5.0f)
        {
            _refreshTimer = 0f;
            foreach (var coord in loadedChunks)
            {
                _blendedColorCache.Remove(coord);
                _pendingBlends.Add(coord);
            }
        }

        ProcessPendingBlends();

        // Update player animations
        LocalPlayer.Update(dt);
        foreach (var other in Others.Values)
        {
            other.Update(dt);
        }
        HandleCombat();

        // Update camera AFTER all movement (including knockback) to prevent jitter
        Cam.Update(LocalPlayer.Position, dt);

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

    private void ProcessPendingBlends()
    {
        if (_pendingBlends.Count == 0) return;

        // PRIORITIZATION: Sort pending blends so the ones closest to the player are handled first
        int px = _lastPlayerChunkX;
        int py = _lastPlayerChunkY;
        
        var toProcess = new List<(int, int)>(_pendingBlends);
        toProcess.Sort((a, b) => {
            int distA = Math.Abs(a.Item1 - px) + Math.Abs(a.Item2 - py);
            int distB = Math.Abs(b.Item1 - px) + Math.Abs(b.Item2 - py);
            return distA.CompareTo(distB);
        });

        const int limit = 500; // Increased limit for faster updates
        int processed = 0;
        foreach (var pos in toProcess)
        {
            if (processed >= limit) break;

            if (!_chunkSnapshot.TryGetValue(pos, out byte myBiome)) {
                _pendingBlends.Remove(pos);
                continue;
            }

            // Process immediately with available data to avoid "unblended" popping
            _blendedColorCache[pos] = CalculateBlendedColor(pos.Item1, pos.Item2, myBiome);
            _pendingBlends.Remove(pos);
            processed++;
        }
    }

    private Color CalculateBlendedColor(int cx, int cy, byte myBiome)
    {
        Color baseCol = GetBiomeBaseColor(myBiome);
        if (myBiome == 7) return baseCol; // Rivers stay sharp

        float rSum = baseCol.R, gSum = baseCol.G, bSum = baseCol.B, wSum = 1.0f;
        for (int dx = -3; dx <= 3; dx++) {
            for (int dy = -3; dy <= 3; dy++) {
                if (dx == 0 && dy == 0) continue;
                if (_chunkSnapshot.TryGetValue((cx + dx, cy + dy), out byte nB)) {
                    if (nB == 7) continue; // Rivers are ignored in blending
                    int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    // Maximum weights for high-contrast blending
                    float weight = dist == 1 ? 0.85f : (dist == 2 ? 0.25f : 0.05f);
                    Color nCol = GetBiomeBaseColor(nB);
                    rSum += nCol.R * weight; gSum += nCol.G * weight; bSum += nCol.B * weight;
                    wSum += weight;
                }
            }
        }
        return new Color((byte)(rSum / wSum), (byte)(gSum / wSum), (byte)(bSum / wSum), (byte)255);
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
            // Optimization: Calculate screen bounds to skip drawing off-screen chunks
            var screenTopLeft = Raylib.GetScreenToWorld2D(new Vector2(0, 0), Cam.RaylibCamera);
            var screenBottomRight = Raylib.GetScreenToWorld2D(new Vector2(Raylib.GetScreenWidth(), Raylib.GetScreenHeight()), Cam.RaylibCamera);
            int margin = chunkSize * 2;

            foreach (var coord in loadedChunks)
            {
                float wx = coord.Item1 * chunkSize;
                float wy = coord.Item2 * chunkSize;

                // Frustum Culling: Only draw if the chunk is visible
                if (wx + chunkSize < screenTopLeft.X - margin || wx > screenBottomRight.X + margin || 
                    wy + chunkSize < screenTopLeft.Y - margin || wy > screenBottomRight.Y + margin) continue;

                if (!_blendedColorCache.TryGetValue(coord, out Color drawColor))
                    if (_chunkSnapshot.TryGetValue(coord, out byte b)) drawColor = GetBiomeBaseColor(b); else continue;

                Raylib.DrawRectangle((int)wx, (int)wy, chunkSize, chunkSize, drawColor);

                if (_chunkSnapshot.TryGetValue(coord, out byte biome) && biome == 6)
                    Raylib.DrawRectangle((int)wx, (int)wy, chunkSize, chunkSize, new Color(255, 200, 60, 50));
            }
            foreach (var other in Others.Values) 
            {   
                other.Draw(); // Draw other players
                Debug.DrawHitbox(other.Position);
            }
            LocalPlayer.Draw(); // Draw the local player
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

    private Color GetBiomeBaseColor(byte biome)
    {
        return biome switch
        {
            0 => new Color(180, 255, 180, 255), // Meadow
            1 => new Color(34, 139, 34, 255),  // Forest
            2 => new Color(255, 220, 60, 255), // Desert
            3 => new Color(180, 180, 180, 255), // Stony Peaks
            4 => new Color(20, 40, 120, 255),  // Ocean
            5 => new Color(255, 240, 160, 255), // Beach
            6 => new Color(255, 255, 102, 255), // Brimstone Springs
            7 => new Color(0, 121, 241, 255),   // River
            _ => Color.Gray
        };
    }

    private Color AverageColors(params Color[] colors)
    {
        int r = 0, g = 0, b = 0, a = 0;
        foreach (var c in colors)
        {
            r += c.R; g += c.G; b += c.B; a += c.A;
        }
        return new Color(
            (byte)(r / colors.Length), 
            (byte)(g / colors.Length), 
            (byte)(b / colors.Length), 
            (byte)(a / colors.Length));
    }
}