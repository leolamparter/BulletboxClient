
using Raylib_cs;
using System.Numerics;
using System.Collections.Generic;
using System; // For Console
using System.Linq;
using BulletboxClient; // Added to access Settings and OptionsScreen

// Duplicated from server for client-side rendering logic
public enum FeatureType
{
    None,
    SmallTree,
    LargeTree,
    MeadowHedge,
    MeadowFlowers,
    Stone,
    PalmTree,
    DesertLog,
    Tumbleweed,
    OasisDesert,
    BeachUmbrella,
    Sailboat,
    SulfurSpring
}

public class Playing
{
    // Biome chunk system prototype
    private HashSet<(int, int)> loadedChunks = new();
    private const int chunkSize = 16;
    public int ChunkViewRadius = 40; // Dynamic radius
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
    private Dictionary<(int, int), byte> _featureSnapshot = new();
    private Dictionary<(int, int), Color> _blendedColorCache = new();
    private HashSet<(int, int)> _pendingBlends = new();
    private List<(int, int)> _sortedPending = new();
    private int _lastPlayerChunkX = int.MaxValue;
    private int _lastPlayerChunkY = int.MaxValue;
    private bool _lastRaidActive = false;

    // Combat Timers
    private float _cAttackTimer = 0f; 
    private float _cHitTimer = 10f;   
    private int _selectedHotbarIndex = 0;
    private Vector2 _kbVelocity = Vector2.Zero;
    
    // Chat and UI State
    private bool _isChatting = false;
    private string _chatInput = "";
    private List<(string sender, string msg, float time)> _chatLog = new();

    private List<(Player player, Vector2 screenPos, float rotation)> _playerArrows = new();

    // Raid UI State
    public bool RaidActive = false;
    public float RaidTimer = 0f;
    public float RaidBossHealth = 0f;

    private int _lastSortX = int.MaxValue;
    private int _lastSortY = int.MaxValue;

    private static readonly Color[] _biomeColors = new Color[]
    {
        new Color(145, 205, 135, 255), // 0: Meadow
        new Color(50, 115, 65, 255),   // 1: Forest
        new Color(230, 205, 140, 255), // 2: Desert
        new Color(140, 145, 155, 255), // 3: Stony Peaks
        new Color(45, 80, 145, 255),   // 4: Ocean
        new Color(240, 220, 180, 255), // 5: Beach
        new Color(210, 95, 60, 255),   // 6: Brimstone
        new Color(75, 150, 210, 255)   // 7: River
    };

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

        // Re-verify these paths match your request
        AssetManager.LoadTexture("kanabo", "resources/textures/item/kanabo.png");
        AssetManager.LoadTexture("spear", "resources/textures/item/spear.png");
        AssetManager.LoadTexture("sword", "resources/textures/item/sword.png");
        AssetManager.LoadTexture("shield", "resources/textures/item/shield.png");
        AssetManager.LoadTexture("bow", "resources/textures/item/bow.png"); // Assuming 'bow.png' exists

        // Load heart textures for overhead health bars
        AssetManager.LoadTexture("heart_full", "resources/textures/ui/health_bar/heart_full.png");
        AssetManager.LoadTexture("heart_full_flash", "resources/textures/ui/health_bar/heart_full_flash.png");
        AssetManager.LoadTexture("heart_empty", "resources/textures/ui/health_bar/heart_empty.png");
        AssetManager.LoadTexture("heart_empty_flash", "resources/textures/ui/health_bar/heart_empty_flash.png");
        AssetManager.LoadTexture("heart_quarter", "resources/textures/ui/health_bar/heart_quarter.png");
        AssetManager.LoadTexture("heart_quarter_flash", "resources/textures/ui/health_bar/heart_quarter_flash.png");
        AssetManager.LoadTexture("heart_half", "resources/textures/ui/health_bar/heart_half.png");
        AssetManager.LoadTexture("heart_half_flash", "resources/textures/ui/health_bar/heart_half_flash.png");

        // Audio
        AudioManager.LoadSound("raid_horn", "resources/sounds/raid/raid_horn.mp3");
        AudioManager.LoadSound("shield_block", "resources/sounds/item/shield/block_1.mp3");


        if (AssetManager.GetTexture("hotbar_active").Id == 0) Console.WriteLine("ERROR: 'hotbar_active' texture failed to load! Check path: resources/textures/ui/inventory/hotbar_active.png");
        if (AssetManager.GetTexture("hotbar_deactive").Id == 0) Console.WriteLine("ERROR: 'hotbar_deactive' texture failed to load! Check path: resources/textures/ui/inventory/hotbar_deactive.png");
    }

    public void AddChatMessage(string sender, string msg)
    {
        _chatLog.Add((sender, msg, (float)Raylib.GetTime()));
        if (_chatLog.Count > 50) _chatLog.RemoveAt(0);
    }

    public void ApplyKnockback(Vector2 force)
    {
        _kbVelocity += force * 15f; // Multiplier to turn 'distance' into 'velocity'
    } 

    public void Update()
    {
        float dt = Raylib.GetFrameTime();

        // Condition for game logic to run:
        // Game logic pauses if Program.IsPaused is true AND we are connected to an integrated server (127.0.0.1).
        // Game logic continues if Program.IsPaused is false, OR if we are connected to a remote server.
        bool isMenuOpen = Program.IsPaused || Program.CurrentState == GameState.OPTIONS;
        bool runGameLogic = !isMenuOpen || (Program.Net.IsConnected() && Program.LastIP != "127.0.0.1");

        // Always update UI elements that are not directly tied to game world state
        // This ensures chat, inventory, hotbar, and mouse cursor management work even when "paused"
        HotbarUI.HoveredStack = null; // Reset tooltip state for the frame
        HandleChatInput();
        Hotbar.Update();
        InvMenu.Update();
        // Mouse Cursor Management
        if (!InvMenu.Visible && !Program.IsPaused && !_isChatting) {
            Raylib.HideCursor();
        } else {
            Raylib.ShowCursor();
        }

        // --- Game Logic that should pause ---
        if (runGameLogic)
        {
            _cAttackTimer += dt;
            _cHitTimer += dt;

            // Update Blocking State
            bool wasBlocking = LocalPlayer.IsBlocking;
            LocalPlayer.OffHandItemID = PlayerInventory.Slots[24].ItemID;
            LocalPlayer.IsBlocking = Raylib.IsMouseButtonDown(MouseButton.Right) && LocalPlayer.OffHandItemID == (byte)'H';
            
            if (LocalPlayer.IsBlocking != wasBlocking) {
                Program.Net.SendBlockingState(LocalPlayer.IsBlocking);
            }

            // Handle Debug inputs
            Debug.Update();

            if (_isChatting) return; // Block game input while chatting

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

            int targetRadius = Program.GetRequiredChunkRadius();
            bool radiusChanged = targetRadius != ChunkViewRadius;

            // Optimization: Only update loading/unloading logic when player enters a new chunk
            if (playerChunkX != _lastPlayerChunkX || playerChunkY != _lastPlayerChunkY || radiusChanged)
            {
                _lastPlayerChunkX = playerChunkX;
                _lastPlayerChunkY = playerChunkY;
                ChunkViewRadius = targetRadius;

                if (radiusChanged) Program.Net.SendRenderDistance(ChunkViewRadius);

                HashSet<(int, int)> needed = new();
                for (int dx = -ChunkViewRadius; dx <= ChunkViewRadius; dx++)
                {
                    for (int dy = -ChunkViewRadius; dy <= ChunkViewRadius; dy++)
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
                foreach (var coord in loadedChunks)
                {
                    if (Program.Net.ChunkBiomes.TryGetValue(coord, out byte currentBiome))
                    {
                        bool isNew = !_chunkSnapshot.TryGetValue(coord, out byte oldBiome);
                        if (isNew || oldBiome != currentBiome)
                        {
                            _chunkSnapshot[coord] = currentBiome;
                            
                            // Dirty a 3x3 area (radius 1) for re-blending.
                            for (int x = -1; x <= 1; x++) {
                                for (int y = -1; y <= 1; y++) {
                                    var target = (coord.Item1 + x, coord.Item2 + y);
                                    _blendedColorCache.Remove(target);
                                    _pendingBlends.Add(target);
                                }
                            }
                        }
                    }
                    if (Program.Net.ChunkFeatures.TryGetValue(coord, out byte f))
                        _featureSnapshot[coord] = f;
                }
            }

            ProcessPendingBlends();

            // Clear previous arrows
            _playerArrows.Clear();

            // Find nearest two players and prepare arrow data
            List<(Player player, float distance)> sortedOthers = new();
            foreach (var other in Others.Values.ToList())
            {
                float dist = Vector2.Distance(LocalPlayer.Position, other.Position);
                sortedOthers.Add((other, dist));
            }
            sortedOthers.Sort((a, b) => a.distance.CompareTo(b.distance));

            int playersToTrack = Math.Min(2, sortedOthers.Count);
            for (int i = 0; i < playersToTrack; i++)
            {
                Player targetPlayer = sortedOthers[i].player;
                Vector2 targetWorldPos = targetPlayer.Position + new Vector2(32, 32); // Center of the other player

                // Convert target's world position to screen position
                Vector2 targetScreenPos = Raylib.GetWorldToScreen2D(targetWorldPos, Cam.RaylibCamera);

                int screenWidth = Raylib.GetScreenWidth();
                int screenHeight = Raylib.GetScreenHeight();
                Vector2 screenCenter = new Vector2(screenWidth / 2f, screenHeight / 2f);

                // Check if player is on screen
                bool onScreen = targetScreenPos.X >= 0 && targetScreenPos.X <= screenWidth &&
                                targetScreenPos.Y >= 0 && targetScreenPos.Y <= screenHeight;

                if (!onScreen)
                {
                    // Calculate direction vector from screen center to target
                    Vector2 dir = Vector2.Normalize(targetScreenPos - screenCenter);
                    float angle = MathF.Atan2(dir.Y, dir.X) * (180f / MathF.PI);

                    // Calculate intersection with screen edges
                    Vector2 arrowPos = screenCenter;
                    float halfWidth = screenWidth / 2f;
                    float halfHeight = screenHeight / 2f;

                    float t = float.MaxValue;
                    if (dir.X != 0) t = Math.Min(t, halfWidth / MathF.Abs(dir.X));
                    if (dir.Y != 0) t = Math.Min(t, halfHeight / MathF.Abs(dir.Y));
                    
                    if (t == float.MaxValue) continue; // Should not happen for off-screen players

                    arrowPos = screenCenter + dir * t;

                    float padding = 20f; // Distance from the edge
                    arrowPos.X = Math.Clamp(arrowPos.X, padding, screenWidth - padding);
                    arrowPos.Y = Math.Clamp(arrowPos.Y, padding, screenHeight - padding);

                    _playerArrows.Add((targetPlayer, arrowPos, angle));
                }
            }

            // Update player animations
            LocalPlayer.Update(dt);
            foreach (var other in Others.Values)
            {
                other.Update(dt);
            }
            HandleCombat();

            // 1. Update Rotation towards mouse
            Vector2 mouseWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), Cam.RaylibCamera);
            Vector2 playerCenter = new Vector2(LocalPlayer.Position.X + 32, LocalPlayer.Position.Y + 32);
            LocalPlayer.Rotation = (float)(Math.Atan2(mouseWorld.Y - playerCenter.Y, mouseWorld.X - playerCenter.X) * (180.0 / Math.PI));
            LocalPlayer.HeldItemID = PlayerInventory.Slots[_selectedHotbarIndex].ItemID;

            // 2. Camera & Network Sync
            Cam.Update(LocalPlayer.Position, dt);
            Cam.Zoom = Settings.FOV;

            // Only send updates if moved or rotated significantly to save bandwidth
            // but we send it every frame for now to ensure other players see smooth weapon rotation
            Program.Net.SendPosition(LocalPlayer.Position.X, LocalPlayer.Position.Y, LocalPlayer.Rotation);
        }

        // Detect Raid Start to play SFX
        if (RaidActive && !_lastRaidActive)
        {
            AudioManager.PlaySound("raid_horn");
        }
        _lastRaidActive = RaidActive;
    }

    private void HandleChatInput()
    {
        if (!_isChatting && Raylib.IsKeyPressed(KeyboardKey.Slash))
        {
            _isChatting = true;
            _chatInput = "";
            return;
        }

        if (_isChatting)
        {
            int key = Raylib.GetCharPressed();
            while (key > 0)
            {
                if (key >= 32 && key <= 125 && _chatInput.Length < 50) _chatInput += (char)key;
                key = Raylib.GetCharPressed();
            }

            if (Raylib.IsKeyPressed(KeyboardKey.Backspace) && _chatInput.Length > 0) _chatInput = _chatInput[..^1];
            if (Raylib.IsKeyPressed(KeyboardKey.Enter))
            {
                if (!string.IsNullOrWhiteSpace(_chatInput))
                    Program.Net.SendChat(_chatInput);
                _isChatting = false;
            }
            if (Raylib.IsKeyPressed(KeyboardKey.Escape)) _isChatting = false;
        }
    }

    private void HandleMovement(float dt)
    {
        float baseSpeed = 350f;
        if (LocalPlayer.IsBlocking) baseSpeed *= 0.55f; // 45% slow down while blocking

        Vector2 direction = Vector2.Zero;

        if (Raylib.IsKeyDown(KeyboardKey.W)) direction.Y -= 1;
        if (Raylib.IsKeyDown(KeyboardKey.S)) direction.Y += 1;
        if (Raylib.IsKeyDown(KeyboardKey.A))
        {
            direction.X -= 1;
            LocalPlayer.FacingRight = false;
        }
        if (Raylib.IsKeyDown(KeyboardKey.D))
        {
            direction.X += 1;
            LocalPlayer.FacingRight = true;
        }

        if (direction != Vector2.Zero)
        {
            // Normalize ensures that diagonal movement is not faster than cardinal movement
            LocalPlayer.Position += Vector2.Normalize(direction) * baseSpeed * dt;
        }
    }

    private void ProcessPendingBlends()
    {
        if (_pendingBlends.Count == 0 && _sortedPending.Count == 0)
        {
            return;
        }

        // OPTIMIZATION: Only re-sort the entire queue when the player moves to a new chunk.
        if (_lastPlayerChunkX != _lastSortX || _lastPlayerChunkY != _lastSortY)
        {
            _lastSortX = _lastPlayerChunkX;
            _lastSortY = _lastPlayerChunkY;

            // Combine all pending and currently sorted items into a temporary HashSet for uniqueness
            HashSet<(int, int)> combinedUnique = new HashSet<(int, int)>(_pendingBlends);
            foreach (var item in _sortedPending)
            {
                combinedUnique.Add(item);
            }

            // Sort the unique combined items by proximity to the player
            _sortedPending = combinedUnique.OrderBy(p => Math.Abs(p.Item1 - _lastSortX) + Math.Abs(p.Item2 - _lastSortY)).ToList();
            _pendingBlends.Clear(); // All items from _pendingBlends are now in _sortedPending
        }
        else if (_pendingBlends.Count > 0) // Only new blends, no player movement
        {
            // If no re-sort is needed, just add new unique items from _pendingBlends to _sortedPending
            // Use a temporary HashSet for efficient uniqueness checks
            HashSet<(int, int)> existingSortedSet = new HashSet<(int, int)>(_sortedPending);
            foreach (var p in _pendingBlends)
            {
                if (!existingSortedSet.Contains(p))
                {
                    _sortedPending.Add(p);
                }
            }
            _pendingBlends.Clear();
        }

        const int limit = 200; 
        int processed = 0;

        // Process items from the front of the sorted list (closest to player)
        for (int i = 0; i < Math.Min(limit, _sortedPending.Count); i++)
        {
            var pos = _sortedPending[i];
            processed++;
            
            if (!_chunkSnapshot.TryGetValue(pos, out byte myBiome)) {
                continue;
            }

            // Process immediately with available data to avoid "unblended" popping
            _blendedColorCache[pos] = CalculateBlendedColor(pos.Item1, pos.Item2, myBiome);
        }

        // Remove the processed batch from the work queue
        if (processed > 0)
        {
            _sortedPending.RemoveRange(0, processed);
        }
    }

    private Color CalculateBlendedColor(int cx, int cy, byte myBiome)
    {
        Color baseCol = GetBiomeBaseColor(myBiome);
        if (myBiome == 7) return baseCol; // Rivers stay sharp

        // OPTIMIZATION: Homogeneity Check. 
        // If all 8 immediate neighbors are the same biome, skip the heavy blending math.
        bool isUniform = true;
        for (int dx = -1; dx <= 1; dx++) {
            for (int dy = -1; dy <= 1; dy++) {
                if (dx == 0 && dy == 0) continue;
                if (!_chunkSnapshot.TryGetValue((cx + dx, cy + dy), out byte nB) || nB != myBiome) {
                    isUniform = false;
                    break;
                }
            }
            if (!isUniform) break;
        }
        if (isUniform) return baseCol;

        float rSum = baseCol.R, gSum = baseCol.G, bSum = baseCol.B, wSum = 1.0f;

        // OPTIMIZATION: Radius 1 (3x3).
        for (int dx = -1; dx <= 1; dx++) {
            for (int dy = -1; dy <= 1; dy++) {
                if (dx == 0 && dy == 0) continue;
                if (_chunkSnapshot.TryGetValue((cx + dx, cy + dy), out byte nB)) {
                    if (nB == 7) continue; // Rivers are ignored in blending
                    float weight = 0.5f;
                    Color nCol = GetBiomeBaseColor(nB);
                    rSum += nCol.R * weight; gSum += nCol.G * weight; bSum += nCol.B * weight;
                    wSum += weight;
                }
            }
        }

        // OPTIMIZATION: Bake the Brimstone Spring (Biome 6) tint directly into the blended color.
        // This removes the need for a second DrawRectangle call for these chunks in the render loop.
        if (myBiome == 6)
        {
            float r = (rSum / wSum) * 0.85f + 255 * 0.15f;
            float g = (gSum / wSum) * 0.85f + 180 * 0.15f;
            float b = (bSum / wSum) * 0.85f + 100 * 0.15f;
            return new Color((byte)r, (byte)g, (byte)b, (byte)255);
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
                LocalPlayer.TriggerAttack();
                
                Vector2 worldMouse = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), Cam.RaylibCamera);
                foreach (var other in Others.Values.ToList())
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
        }

        // Feature Pass
        foreach (var coord in loadedChunks)
        {
            float wx = coord.Item1 * chunkSize;
            float wy = coord.Item2 * chunkSize;

            if (wx + chunkSize < screenTopLeft.X - margin || wx > screenBottomRight.X + margin || 
                wy + chunkSize < screenTopLeft.Y - margin || wy > screenBottomRight.Y + margin) continue;

            if (_featureSnapshot.TryGetValue(coord, out byte feature) && feature != 0)
            {
                string texName = "";
                bool isSmall = false;
                FeatureType type = (FeatureType)feature;

                switch (type)
                {
                    case FeatureType.LargeTree: texName = "large_tree"; break;
                    case FeatureType.SmallTree: texName = "small_tree"; break;
                    case FeatureType.MeadowHedge: texName = "meadow_hedge"; isSmall = true; break;
                    case FeatureType.MeadowFlowers: texName = "meadow_flowers"; isSmall = true; break;
                    case FeatureType.Stone: texName = "stone"; isSmall = true; break;
                    case FeatureType.PalmTree: texName = "palm_tree"; break;
                    case FeatureType.DesertLog: texName = "desert_log"; isSmall = true; break;
                    case FeatureType.Tumbleweed: texName = "tumbleweed"; isSmall = true; break;
                    case FeatureType.OasisDesert: texName = "oasis_desert"; break;
                    case FeatureType.BeachUmbrella: texName = "beach_umbrella"; isSmall = true; break;
                    case FeatureType.Sailboat: texName = "sailboat"; break;
                    case FeatureType.SulfurSpring: texName = "sulfur_spring"; break;
                }

                if (string.IsNullOrEmpty(texName)) continue;

                var tex = AssetManager.GetTexture(texName);

                if (tex.Id != 0)
                {
                    if (isSmall)
                    {
                        float scale = (type == FeatureType.MeadowFlowers) ? 0.35f : 0.5f;
                        Rectangle source = new Rectangle(0, 0, tex.Width, tex.Height);

                        Rectangle dest = new Rectangle(
                            wx + 8,
                            wy + 8,
                            tex.Width * scale,
                            tex.Height * scale
                        );

                        Vector2 origin = new Vector2(
                            (tex.Width * scale) / 2f,
                            tex.Height * scale
                        );

                        Raylib.DrawTexturePro(
                            tex,
                            source,
                            dest,
                            origin,
                            0f,
                            Color.White
                        );
                    }
                    else
                    {
                        Raylib.DrawTexture(
                            tex,
                            (int)wx - (tex.Width / 2) + 8,
                            (int)wy - tex.Height + 16,
                            Color.White
                        );
                    }
                }
            }
        }

        foreach (var other in Others.Values.ToList()) 
        {   
            other.Draw(); // Draw other players
            Debug.DrawHitbox(other.Position);
        }
        LocalPlayer.Draw(); // Draw the local player
        Debug.DrawHitbox(LocalPlayer.Position);
        
        Cam.End();

        // UI Overlay Pass (Draw after Cam.End to be in true Screen Space)
        foreach (var other in Others.Values)
        {
            other.DrawOverheadHearts(other.Position + new Vector2(32, 32), other.Health, other.MaxHealth);
        }
        LocalPlayer.DrawOverheadHearts(LocalPlayer.Position + new Vector2(32, 32), CurrentHealth, MaxHealth);

        // Draw player direction arrows for off-screen targets
        foreach (var arrow in _playerArrows)
        {
            float arrowSize = 25f;
            Vector2 tip = arrow.screenPos;
            float rad = arrow.rotation * (MathF.PI / 180f);

            // Calculate base center (position behind the tip)
            Vector2 baseCenter = new Vector2(
                tip.X - arrowSize * MathF.Cos(rad),
                tip.Y - arrowSize * MathF.Sin(rad)
            );

            // Calculate the two base corners of the triangle
            Vector2 p2 = new Vector2(
                baseCenter.X + (arrowSize * 0.5f) * MathF.Cos(rad + MathF.PI / 2f),
                baseCenter.Y + (arrowSize * 0.5f) * MathF.Sin(rad + MathF.PI / 2f)
            );
            Vector2 p3 = new Vector2(
                baseCenter.X + (arrowSize * 0.5f) * MathF.Cos(rad - MathF.PI / 2f),
                baseCenter.Y + (arrowSize * 0.5f) * MathF.Sin(rad - MathF.PI / 2f)
            );

            // Swapped p3 and p2 to ensure Counter-Clockwise winding for proper filling
            Raylib.DrawTriangle(tip, p3, p2, Color.Blue);
            Raylib.DrawTriangleLines(tip, p3, p2, new Color(0, 0, 150, 255));
        }

        DrawChat();

        // Draw Off-hand Shield Slot
        int swUI = Raylib.GetScreenWidth();
        int shUI = Raylib.GetScreenHeight();
        int offHandX = swUI / 2 - 270; // Positioned to the left of the hotbar
        int offHandY = shUI - 72;
        
        Raylib.DrawTexture(AssetManager.GetTexture("hotbar_deactive"), offHandX, offHandY, Color.White);

        if (Raylib.IsKeyDown(KeyboardKey.Tab)) DrawPlayerList();

        healthBar.Draw(CurrentHealth, MaxHealth);
        Hotbar.Draw();

        
        // Draw Raid UI
        int sw = Raylib.GetScreenWidth();
        if (!RaidActive) {
            string timerText = $"Next Raid in {(int)RaidTimer}s";
            int tw = Raylib.MeasureText(timerText, 25);
            Raylib.DrawText(timerText, sw / 2 - tw / 2, 20, 25, Color.White);
        } else {
            int barW = 400, barH = 20;
            int x = sw / 2 - barW / 2;
            
            // Draw Glow
            Raylib.DrawRectangleRounded(new Rectangle(x - 4, 36, barW + 8, barH + 8), 0.5f, 4, new Color(255, 80, 0, 40));
            Raylib.DrawRectangleRounded(new Rectangle(x - 2, 38, barW + 4, barH + 4), 0.5f, 4, new Color(255, 80, 0, 80));
            
            // Bar Background
            Raylib.DrawRectangleRounded(new Rectangle(x, 40, barW, barH), 0.5f, 4, new Color(40, 40, 40, 200));
            
            // Bar Fill
            float fillWidth = barW * RaidBossHealth;
            if (fillWidth > 5) // Only draw fill if there's health to prevent rounding artifacts
                Raylib.DrawRectangleRounded(new Rectangle(x, 40, fillWidth, barH), 0.5f, 4, new Color(255, 80, 0, 255));
                
            Raylib.DrawRectangleRoundedLines(new Rectangle(x, 40, barW, barH), 0.5f, 4, Color.Black);
            Raylib.DrawText("RAID ENCOUNTER", sw / 2 - 80, 15, 20, new Color(255, 200, 0, 255));
        }

        
        // UI Visual for Cooldown (Optional, helps testing)
        DrawCooldownUI();
        
        InvMenu.Draw();

        // Draw Dynamic Crosshair
        if (!InvMenu.Visible && !Program.IsPaused && !_isChatting)
        {
            Vector2 mousePos = Raylib.GetMousePosition();
            Vector2 worldMouse = Raylib.GetScreenToWorld2D(mousePos, Cam.RaylibCamera);
            Vector2 playerCenterPos = new Vector2(LocalPlayer.Position.X + 32, LocalPlayer.Position.Y + 32);
            float distToMouse = Vector2.Distance(playerCenterPos, worldMouse);

            byte heldId = PlayerInventory.Slots[_selectedHotbarIndex].ItemID;
            var (_, _, currentRange) = WeaponStats.Calculate(heldId, _cAttackTimer, _cHitTimer);

            Color crossColor = (distToMouse <= currentRange && heldId != (byte)' ' && heldId != 0) ? Color.Green : Color.Red;

            // Visibility Optimization: Check background color for contrast
            int cx = (int)MathF.Floor(worldMouse.X / chunkSize);
            int cy = (int)MathF.Floor(worldMouse.Y / chunkSize);
            Color bgColor = Color.Gray;
            if (_blendedColorCache.TryGetValue((cx, cy), out Color blended)) bgColor = blended;
            else if (_chunkSnapshot.TryGetValue((cx, cy), out byte b)) bgColor = GetBiomeBaseColor(b);

            // Simple luminosity check (0.299R + 0.587G + 0.114B)
            float lum = (bgColor.R * 0.299f + bgColor.G * 0.587f + bgColor.B * 0.114f);
            
            // If background is bright, make crosshair darker; if dark, make it brighter.
            if (lum > 140) crossColor = Raylib.ColorBrightness(crossColor, -0.3f);
            else crossColor = Raylib.ColorBrightness(crossColor, 0.2f);

            int cs = 10; // Crosshair arm length
            int thickness = 3; // Crosshair thickness

            // Horizontal bar
            Raylib.DrawRectangle((int)mousePos.X - cs, (int)mousePos.Y - thickness / 2, 2 * cs, thickness, crossColor);
            // Vertical bar
            Raylib.DrawRectangle((int)mousePos.X - thickness / 2, (int)mousePos.Y - cs, thickness, 2 * cs, crossColor);
        }

        // Render tooltips last so they are on top of everything
        HotbarUI.RenderTooltip();
    }

    private void DrawChat()
    {
        int sh = Raylib.GetScreenHeight();
        float currentTime = (float)Raylib.GetTime();
        int fontSize = 20;
        int spacing = 22;
        int anchorY = sh - 80; // The Y-position for the most recent message

        int displayedCount = 0;
        // Iterate through the log backwards to keep the newest message at the bottom
        for (int i = _chatLog.Count - 1; i >= 0; i--)
        {
            if (displayedCount >= 10) break;

            var entry = _chatLog[i];
            float age = currentTime - entry.time;

            if (!_isChatting && age > 15.0f) continue;

            // Calculate fade alpha (stays 1.0 until 13s, then fades to 0 over the next 2s)
            float alpha = 1.0f;
            if (!_isChatting && age > 13.0f) alpha = 1.0f - ((age - 13.0f) / 2.0f);

            string text = $"[{entry.sender}]: {entry.msg}";
            int textWidth = Raylib.MeasureText(text, fontSize);
            int yPos = anchorY - (displayedCount * spacing);

            // Draw Minecraft-style semi-transparent background and text
            Raylib.DrawRectangle(10, yPos - 2, textWidth + 20, fontSize + 4, new Color((byte)40, (byte)40, (byte)40, (byte)(160 * alpha)));
            Raylib.DrawText(text, 20, yPos, fontSize, new Color((byte)255, (byte)255, (byte)255, (byte)(255 * alpha)));

            displayedCount++;
        }

        if (_isChatting)
        {
            Raylib.DrawRectangle(10, sh - 45, 500, 35, new Color(0, 0, 0, 180));
            Raylib.DrawText("> " + _chatInput + "_", 20, sh - 38, 20, Color.Yellow);
        }
    }

    private void DrawPlayerList()
    {
        var players = Others.Values.ToList();
        players.Add(LocalPlayer);
        
        // Show up to 30 nearest players
        var sorted = players.OrderBy(p => Vector2.Distance(LocalPlayer.Position, p.Position)).Take(30).ToList();

        int sw = Raylib.GetScreenWidth();
        int sh = Raylib.GetScreenHeight();
        Raylib.DrawRectangle(sw / 2 - 300, sh / 2 - 200, 600, 380, new Color(0, 0, 0, 200));
        Raylib.DrawText("ONLINE PLAYERS (Nearest 30)", sw / 2 - 140, sh / 2 - 180, 20, Color.Yellow);

        for (int i = 0; i < sorted.Count; i++)
        {
            int col = i / 10;
            int row = i % 10;
            int x = sw / 2 - 270 + (col * 200);
            int y = sh / 2 - 140 + (row * 30);
            
            Color nameCol = (sorted[i] == LocalPlayer) ? Color.SkyBlue : Color.White;
            Raylib.DrawText(sorted[i].Name, x, y, 20, nameCol);
        }
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
        // OPTIMIZATION: Use a pre-allocated array lookup instead of a switch statement.
        if (biome < _biomeColors.Length) return _biomeColors[biome];
        return Color.Gray;
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