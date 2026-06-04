
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

public class DamageParticle
{
    public Vector2 Position;
    public Vector2 Velocity;
    public float Life;
    public float MaxLife;
    public Color ParticleColor;
    public float Size;
    public float Rotation;
    public float AngularVelocity;
}

// The StructureType enum and Structure class are now in BulletboxClient/Structure.cs
public class Playing
{
    // Biome chunk system prototype
    private HashSet<(int, int)> loadedChunks = new();
    private const int chunkSize = 16;
    public int ChunkViewRadius = 40; // Dynamic radius
    public Player LocalPlayer;
    public int CurrentHealth = 100;
    public int MaxHealth = 100;
    public int CurrentHunger = 100;
    public Dictionary<string, Player> Others = new Dictionary<string, Player>();
    public CameraManager Cam;
    public Inventory PlayerInventory = new Inventory();
    public HotbarUI Hotbar;
    public InventoryUI InvMenu;
    public Dictionary<(int, int), Structure> Structures = new(); 
    private HealthBar healthBar = new HealthBar();

    // Optimization Caches
    private Dictionary<(int, int), byte> _chunkSnapshot = new();
    private Dictionary<(int, int), byte> _featureSnapshot = new();
    private Dictionary<(int, int), Color> _blendedColorCache = new();
    private HashSet<(int, int)> _pendingBlends = new();
    private List<(int, int)> _sortedPending = new();
    private int _lastPlayerChunkX = int.MaxValue;
    private int _lastPlayerChunkY = int.MaxValue;

    // Combat Timers
    private float _cAttackTimer = 0f; 
    private float _cHitTimer = 10f;   
    private int _selectedHotbarIndex = 0;
    private int _lastLocalHealth = 100;
    private float _hungerHealTimer = 0f;
    private Dictionary<string, int> _lastOthersHealth = new();
    private List<DamageParticle> _damageParticles = new();

    private Vector2 _kbVelocity = Vector2.Zero;
    
    // Footstep Sound State
    private string _currentFootstepKey = "";
    private float _footstepVolume = 0f;
    private string _fadingFootstepKey = "";
    private float _fadingFootstepVolume = 0f;

    // Chat and UI State
    private bool _isChatting = false;
    private string _chatInput = "";
    private List<(string sender, string msg, float time)> _chatLog = new();

    // Death and Kill Tracking State
    private List<string> _lastOtherNames = new();
    private string _lastAttackedName = "";
    private float _lastAttackTime = 0f;

    // Raid State (Moved from Structure to Player/Session level)
    public bool RaidActive = false;
    public float RaidBossHealth = 0f;
    public float RaidTimer = 9999f;
    private bool _hasPlayedCountdown = false;
    private bool _hasPlayedHorn = false;
    private Vector2? _fixedRaidOutpostPosition = null; // NEW FIELD

    private List<(Player player, Vector2 screenPos, float rotation)> _playerArrows = new();

    private int _lastSortX = int.MaxValue;
    private int _lastSortY = int.MaxValue;

    // Movement Tutorial State
    private bool _showMovementTutorial = false;
    private bool _tutorialFading = false;
    private float _tutorialAlpha = 1.0f;

    // Raid Tutorial State
    private enum RaidTutorialStage { None, InfoMessage, Fading, CompletionMessage, CompletionFading }
    private RaidTutorialStage _raidTutorialStage = RaidTutorialStage.None;
    private float _raidTutorialAlpha = 1.0f;
    private bool _wasRaidActive = false;

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

        // Initialize starting items: Sword only
        PlayerInventory.Slots[0].ItemID = (byte)'S';

        Hotbar = new HotbarUI(PlayerInventory);
        InvMenu = new InventoryUI(PlayerInventory);
        LoadAssets();
        _showMovementTutorial = !Program.CurrentUser.MovementTutorialFinnished;
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
        AssetManager.LoadTexture("raidshroom", "resources/textures/item/raidshroom.png");
        AssetManager.LoadTexture("shield", "resources/textures/item/shield.png");
        AssetManager.LoadTexture("bow", "resources/textures/item/bow.png"); // Assuming 'bow.png' exists

        // Load raidshroomer textures
        AssetManager.LoadTexture("raidshroomer_idle", "resources/textures/entity/raidshroomer/idle.png");
        AssetManager.LoadTexture("raidshroomer_angry", "resources/textures/entity/raidshroomer/angry.png");
        AssetManager.LoadTexture("raidshroomer_afraid", "resources/textures/entity/raidshroomer/afraid.png");

        // Load heart textures for overhead health bars
        AssetManager.LoadTexture("heart_full", "resources/textures/ui/health_bar/heart_full.png");
        AssetManager.LoadTexture("heart_full_flash", "resources/textures/ui/health_bar/heart_full_flash.png");
        AssetManager.LoadTexture("heart_empty", "resources/textures/ui/health_bar/heart_empty.png");
        AssetManager.LoadTexture("heart_empty_flash", "resources/textures/ui/health_bar/heart_empty_flash.png");
        AssetManager.LoadTexture("heart_quarter", "resources/textures/ui/health_bar/heart_quarter.png");
        AssetManager.LoadTexture("heart_quarter_flash", "resources/textures/ui/health_bar/heart_quarter_flash.png");
        AssetManager.LoadTexture("heart_half", "resources/textures/ui/health_bar/heart_half.png");
        AssetManager.LoadTexture("heart_half_flash", "resources/textures/ui/health_bar/heart_half_flash.png");

        // Load structure textures
        AssetManager.LoadTexture("raid_outpost_center", "resources/textures/structure/raidoutpost/center.png");

        // Audio
        AudioManager.LoadSound("raid_horn", "resources/sounds/raid/raid_horn.mp3");
        AudioManager.LoadSound("shield_block", "resources/sounds/item/shield/block_1.mp3");
        AudioManager.LoadSound("meadow", "resources/sounds/footstep/meadow.mp3");
        AudioManager.LoadSound("forest", "resources/sounds/footstep/forest.mp3");
        AudioManager.LoadSound("desert", "resources/sounds/footstep/desert.mp3");
        AudioManager.LoadSound("stonypeaks", "resources/sounds/footstep/stonypeaks.mp3");
        AudioManager.LoadSound("beach", "resources/sounds/footstep/beach.mp3");
        AudioManager.LoadSound("river", "resources/sounds/footstep/river.mp3");
        AudioManager.LoadSound("sword_swing", "resources/sounds/item/sword/swing_1.mp3");
        AudioManager.LoadSound("raid_countdown", "resources/sounds/raid/countdown.mp3");
        AudioManager.LoadSound("player_death", "resources/sounds/player/death.mp3");
        AudioManager.LoadSound("player_died", "resources/sounds/player/died.mp3");
        AudioManager.LoadSound("player_kill", "resources/sounds/player/kill.mp3");

        // Load Hunger Bar textures
        for (int i = 0; i <= 110; i += 10)
        {
            AssetManager.LoadTexture($"hunger_{i}", $"resources/textures/ui/hunger_bar/{i}.png");
        }

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

        // Raid Tutorial Trigger (Info Message)
        if (!RaidActive && RaidTimer <= 3.0f && RaidTimer > 0 && !Program.CurrentUser.RaidTutorialFinnished && _raidTutorialStage == RaidTutorialStage.None)
        {
            _raidTutorialStage = RaidTutorialStage.InfoMessage;
            _raidTutorialAlpha = 1.0f;
        }

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

            // Passive Healing Logic: 5 HP for 4 Hunger per second
            _hungerHealTimer += dt;
            if (_hungerHealTimer >= 1.0f)
            {
                _hungerHealTimer -= 1.0f;
                if (CurrentHealth < MaxHealth && CurrentHunger >= 5)
                {
                    CurrentHealth = Math.Min(MaxHealth, CurrentHealth + 5);
                    CurrentHunger -= 4;
                }
            }

            // Hotbar Right-Click Consumption
            if (Raylib.IsMouseButtonPressed(MouseButton.Right) && !InvMenu.Visible && !_isChatting)
            {
                var stack = PlayerInventory.Slots[_selectedHotbarIndex];
                if (stack.ItemID == (byte)'R' && CurrentHunger < 110)
                {
                    CurrentHunger = Math.Min(110, CurrentHunger + 15);
                    if (stack.Count > 1) PlayerInventory.Slots[_selectedHotbarIndex].Count--;
                    else PlayerInventory.Slots[_selectedHotbarIndex] = new ItemStack((byte)' ', 0);
                    Program.Net.SendConsumeItem((byte)_selectedHotbarIndex);
                }
            }

            // Movement Tutorial Update
            if (_showMovementTutorial)
            {
                if (!_tutorialFading)
                {
                    if (Raylib.IsKeyDown(KeyboardKey.W) || Raylib.IsKeyDown(KeyboardKey.A) || 
                        Raylib.IsKeyDown(KeyboardKey.S) || Raylib.IsKeyDown(KeyboardKey.D))
                    {
                        _tutorialFading = true;
                    }
                }
                else
                {
                    _tutorialAlpha -= dt * 0.4f; // Fade out over ~2.5 seconds
                    if (_tutorialAlpha <= 0)
                    {
                        _tutorialAlpha = 0;
                        _showMovementTutorial = false;
                        Program.CurrentUser.MovementTutorialFinnished = true;
                        SaveManager.Save(Program.CurrentUser); // Persist tutorial status immediately
                    }
                }
            }

            // Raid Tutorial Messages & Fade Update
            if (_raidTutorialStage == RaidTutorialStage.InfoMessage || _raidTutorialStage == RaidTutorialStage.CompletionMessage)
            {
                if (Raylib.IsKeyDown(KeyboardKey.W) || Raylib.IsKeyDown(KeyboardKey.A) || 
                    Raylib.IsKeyDown(KeyboardKey.S) || Raylib.IsKeyDown(KeyboardKey.D))
                {
                    if (_raidTutorialStage == RaidTutorialStage.InfoMessage)
                        _raidTutorialStage = RaidTutorialStage.Fading;
                    else
                        _raidTutorialStage = RaidTutorialStage.CompletionFading;
                }
            }
            else if (_raidTutorialStage == RaidTutorialStage.Fading)
            {
                _raidTutorialAlpha -= dt * 0.4f;
                if (_raidTutorialAlpha <= 0)
                {
                    _raidTutorialAlpha = 0;
                    _raidTutorialStage = RaidTutorialStage.None;
                    Program.CurrentUser.RaidTutorialFinnished = true;
                    SaveManager.Save(Program.CurrentUser);
                }
            }
            else if (_raidTutorialStage == RaidTutorialStage.CompletionFading)
            {
                _raidTutorialAlpha -= dt * 0.4f;
                if (_raidTutorialAlpha <= 0)
                {
                    _raidTutorialAlpha = 0;
                    _raidTutorialStage = RaidTutorialStage.None;
                    Program.CurrentUser.RaidCompletedTutorialFinnished = true;
                    SaveManager.Save(Program.CurrentUser);
                }
            }

            int targetRadius = Program.GetRequiredChunkRadius();
            bool radiusChanged = targetRadius != ChunkViewRadius;
            int playerChunkX = (int)MathF.Floor(LocalPlayer.Position.X / chunkSize);
            int playerChunkY = (int)MathF.Floor(LocalPlayer.Position.Y / chunkSize);

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

            // Process incoming structure data from the NetworkManager
            lock (Program.Net.StructuresLock) // Assuming a lock for structures in Program.Net
            {
                foreach (var structureEntry in Program.Net.Structures)
                {
                    var coord = (structureEntry.Value.ChunkX, structureEntry.Value.ChunkY);
                    if (loadedChunks.Contains(coord) && !Structures.ContainsKey(coord))
                    {
                        // Map server-side structure type to client-side texture name
                        string textureName = "";
                        switch (structureEntry.Value.Type)
                        {
                            case StructureType.RaidOutpost:
                                textureName = "raid_outpost_center";
                                break;
                            default:
                                textureName = ""; // No texture for unknown types
                                break;
                        }
                        if (!string.IsNullOrEmpty(textureName))
                        {
                            Structures.Add(coord, new Structure(structureEntry.Value.Position, structureEntry.Value.Type, structureEntry.Value.ChunkX, structureEntry.Value.ChunkY, textureName));
                        }
                    }
                }
                // Remove structures that are no longer in loaded chunks
                var keysToRemove = Structures.Keys.Where(k => !loadedChunks.Contains(k)).ToList();
                foreach (var key in keysToRemove) Structures.Remove(key);
            }

            // Global Raid Sound Logic
            if (RaidActive && !_hasPlayedHorn)
            {
                AudioManager.PlaySound("raid_horn");
                _hasPlayedHorn = true;
            }
            if (!RaidActive)
            {
                _hasPlayedHorn = false; // Reset for next raid
                if (RaidTimer <= 3.0f && RaidTimer > 0 && !_hasPlayedCountdown)
                {
                    AudioManager.PlaySound("raid_countdown");
                    _hasPlayedCountdown = true;
                }
                if (RaidTimer > 3.0f || RaidTimer <= 0) _hasPlayedCountdown = false;
            }

            if (_wasRaidActive && !RaidActive && RaidBossHealth <= 0 && !Program.CurrentUser.RaidCompletedTutorialFinnished)
            {
                _raidTutorialStage = RaidTutorialStage.CompletionMessage;
                _raidTutorialAlpha = 1.0f;
            }
            _wasRaidActive = RaidActive;

            // Damage Splash Detection & Particle Update
            if (CurrentHealth < _lastLocalHealth) SpawnDamageSplash(LocalPlayer.Position + new Vector2(32, 32));
            _lastLocalHealth = CurrentHealth;

            foreach (var kvp in Others)
            {
                if (!_lastOthersHealth.TryGetValue(kvp.Key, out int lastH)) {
                    _lastOthersHealth[kvp.Key] = kvp.Value.Health;
                    continue;
                }
                if (kvp.Value.Health < lastH) SpawnDamageSplash(kvp.Value.Position + new Vector2(32, 32));
                _lastOthersHealth[kvp.Key] = kvp.Value.Health;
            }

            // Cleanup stale health tracking
            var staleKeys = _lastOthersHealth.Keys.Where(k => !Others.ContainsKey(k)).ToList();
            foreach (var k in staleKeys) _lastOthersHealth.Remove(k);

            // Update particles
            for (int i = _damageParticles.Count - 1; i >= 0; i--)
            {
                var p = _damageParticles[i];
                p.Life -= dt;
                if (p.Life <= 0) { _damageParticles.RemoveAt(i); continue; }
                
                p.Velocity.Y += 1200f * dt; // Strong Gravity
                p.Position += p.Velocity * dt;
                p.Rotation += p.AngularVelocity * dt;
                p.Velocity *= (1.0f - dt * 1.5f); // Slight Air friction
            }

            ProcessPendingBlends();

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
            
            // Footsteps and Position
            HandleMovement(dt);

            // Apply and Decay Knockback Velocity
            LocalPlayer.Position += _kbVelocity * dt;
            _kbVelocity = Vector2.Lerp(_kbVelocity, Vector2.Zero, dt * 6.5f); // Smooth friction

            // Raid Boundary Enforcement: Clamp player within 120 chunks of the active outpost
            // Use _fixedRaidOutpostPosition if a raid is active or approaching
            if ((RaidActive || (RaidTimer > 0 && RaidTimer <= 3.0f)) && _fixedRaidOutpostPosition.HasValue)
            {
                Vector2 activeOutpostCenter = _fixedRaidOutpostPosition.Value;
                const float boundaryRadius = 120f * 16f; // 120 Chunks = 1920 Units
                Vector2 offset = LocalPlayer.Position - activeOutpostCenter;
                // Only clamp if the player is outside the boundary
                if (offset.Length() > boundaryRadius)
                {
                    // Normalize the offset and scale it to the boundary radius
                    LocalPlayer.Position = activeOutpostCenter + Vector2.Normalize(offset) * boundaryRadius;
                }
            }

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
                    
                    if (t == float.MaxValue) continue; // Should not happen for off-screen players, but a safety check

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

            // 2. Camera & Network Sync (always send position for smooth weapon rotation)
            Cam.Update(LocalPlayer.Position, dt);
            Cam.Zoom = Settings.FOV;

            // Only send updates if moved or rotated significantly to save bandwidth
            // but we send it every frame for now to ensure other players see smooth weapon rotation
            Program.Net.SendPosition(LocalPlayer.Position.X, LocalPlayer.Position.Y, LocalPlayer.Rotation);
        }
        
        // --- Global Death/Kill Detection ---
        var currentOtherNames = Others.Keys.ToList();
        foreach (var name in _lastOtherNames)
        {
            if (!currentOtherNames.Contains(name))
            {
                // Something died or disconnected
                AudioManager.PlaySound("player_death"); // death.mp3

                // If it's the target we just hit, play the kill sound on top
                if (name == _lastAttackedName && (float)Raylib.GetTime() - _lastAttackTime < 1.5f)
                {
                    AudioManager.PlaySound("player_kill"); // kill.mp3
                } 
            }
        }
        _lastOtherNames = currentOtherNames;

    }

    public void SetActiveRaidOutpost(Vector2? outpostPos)
    {
        _fixedRaidOutpostPosition = outpostPos;
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
                if (key >= 32 && key <= 125 && _chatInput.Length < 50) _chatInput += (char)key; // Max 50 chars
                key = Raylib.GetCharPressed();
            }

            if (Raylib.IsKeyPressed(KeyboardKey.Backspace) && _chatInput.Length > 0) _chatInput = _chatInput[..^1];
            if (Raylib.IsKeyPressed(KeyboardKey.Enter))
            {
                if (!string.IsNullOrWhiteSpace(_chatInput))
                    Program.Net.SendChat(_chatInput);
                _isChatting = false;
            }
            if (Raylib.IsKeyPressed(KeyboardKey.Escape)) _isChatting = false; // Close chat on escape
        }
    }

    private void SpawnDamageSplash(Vector2 pos)
    {
        Random r = new Random();
        int count = r.Next(35, 50); // Massive particle burst
        for (int i = 0; i < count; i++)
        {
            float angle = (float)(r.NextDouble() * Math.PI * 2);
            float speed = (float)(r.NextDouble() * 500 + 100);
            float life = (float)(r.NextDouble() * 0.3f + 0.2f);

            int redVal = r.Next(130, 230); // Richer red tones

            _damageParticles.Add(new DamageParticle {
                Position = pos,
                Velocity = new Vector2(MathF.Cos(angle) * speed, MathF.Sin(angle) * speed),
                Life = life,
                MaxLife = life,
                ParticleColor = new Color(redVal, 0, 0, 255),
                Size = (float)(r.NextDouble() * 6 + 3),
                Rotation = (float)(r.NextDouble() * 360),
                AngularVelocity = (float)(r.NextDouble() * 800 - 400)
            });
        }
    }

    private void HandleMovement(float dt)
    {
        float baseSpeed = 350f;
        if (LocalPlayer.IsBlocking) baseSpeed *= 0.55f; // 45% slow down while blocking
        
        Vector2 direction = Vector2.Zero;
        if (Raylib.IsKeyDown(KeyboardKey.W)) direction.Y -= 1;
        if (Raylib.IsKeyDown(KeyboardKey.S)) direction.Y += 1;
        if (Raylib.IsKeyDown(KeyboardKey.A)) direction.X -= 1;
        if (Raylib.IsKeyDown(KeyboardKey.D)) direction.X += 1;

        if (direction.X < 0) LocalPlayer.FacingRight = false;
        else if (direction.X > 0) LocalPlayer.FacingRight = true;
        
        if (direction != Vector2.Zero)
        {
            // Normalize ensures that diagonal movement is not faster than cardinal movement
            LocalPlayer.Position += Vector2.Normalize(direction) * baseSpeed * dt;
        }

        // --- Footstep Sound Logic ---
        int cx = (int)MathF.Floor(LocalPlayer.Position.X / chunkSize);
        int cy = (int)MathF.Floor(LocalPlayer.Position.Y / chunkSize);

        // Lookup biome with fallback to network cache if snapshot isn't ready
        byte biome = 0;
        bool hasBiome = _chunkSnapshot.TryGetValue((cx, cy), out biome);
        if (!hasBiome) // Fallback if snapshot not yet populated
        {
            lock (Program.Net.ChunkBiomesLock)
            {
                hasBiome = Program.Net.ChunkBiomes.TryGetValue((cx, cy), out biome);
            }
        }

        string targetFootstep = "";
        if (hasBiome)
        {
            targetFootstep = biome switch {
                0 => "meadow",
                1 => "forest",
                2 => "desert",
                3 => "stonypeaks",
                4 => "river", // Ocean fallback
                5 => "beach",
                6 => "stonypeaks", // Brimstone fallback
                7 => "river", // River
                _ => ""
            };
        }

        bool isMoving = direction != Vector2.Zero;
        bool shouldPlay = isMoving && !string.IsNullOrEmpty(targetFootstep) && !Program.IsPaused;

        // Crossfade logic: if the target changed, move current to fading, and start new
        if (shouldPlay && _currentFootstepKey != targetFootstep)
        {
            if (!string.IsNullOrEmpty(_fadingFootstepKey)) AudioManager.StopSound(_fadingFootstepKey);
            _fadingFootstepKey = _currentFootstepKey;
            _fadingFootstepVolume = _footstepVolume;
            _currentFootstepKey = targetFootstep;
            _footstepVolume = 0f;
        }

        if (shouldPlay) _footstepVolume = Math.Min(_footstepVolume + dt * 5f, 1.0f); // Fade in
        else _footstepVolume = Math.Max(_footstepVolume - dt * 5f, 0.0f);

        if (!string.IsNullOrEmpty(_fadingFootstepKey))
            _fadingFootstepVolume = Math.Max(_fadingFootstepVolume - dt * 5f, 0.0f);

        if (!string.IsNullOrEmpty(_currentFootstepKey))
        {
            if (_footstepVolume <= 0 && !shouldPlay) // Stop if faded out and not playing
            {
                AudioManager.StopSound(_currentFootstepKey);
                _currentFootstepKey = "";
            }
            else if (!AudioManager.IsSoundPlaying(_currentFootstepKey) && _footstepVolume > 0)
            {
                AudioManager.SetVolume(_currentFootstepKey, _footstepVolume);
                AudioManager.PlaySound(_currentFootstepKey);
            }
            else if (AudioManager.IsSoundPlaying(_currentFootstepKey)) // Update volume if already playing
            {
                AudioManager.SetVolume(_currentFootstepKey, _footstepVolume);
            }
        }

        if (!string.IsNullOrEmpty(_fadingFootstepKey))
        {
            if (_fadingFootstepVolume > 0)
            { // Continue fading out
                if (AudioManager.IsSoundPlaying(_fadingFootstepKey))
                    AudioManager.SetVolume(_fadingFootstepKey, _fadingFootstepVolume);
            }
            else
            {
                AudioManager.StopSound(_fadingFootstepKey);
                _fadingFootstepKey = "";
            }
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

        const int limit = 200; // Process up to 200 blends per frame
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
        Color baseCol = GetBiomeBaseColor(myBiome, cx, cy);
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
                    Color nCol = GetBiomeBaseColor(nB, cx + dx, cy + dy);
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
            return new Color((int)r, (int)g, (int)b, 255);
        }

        return new Color((int)(rSum / wSum), (int)(gSum / wSum), (int)(bSum / wSum), 255);
    }

    private void HandleCombat()
    {
        if (Raylib.IsMouseButtonPressed(MouseButton.Left) && !InvMenu.Visible) 
        { // Only attack if inventory is not open
            byte heldId = PlayerInventory.Slots[_selectedHotbarIndex].ItemID;
            var (dmg, kb, range) = WeaponStats.Calculate(heldId, _cAttackTimer, _cHitTimer);

            if (dmg > 0)
            {
                LocalPlayer.TriggerAttack();
                AudioManager.PlaySound("sword_swing");
                _lastAttackTime = (float)Raylib.GetTime();
                
                Vector2 worldMouse = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), Cam.RaylibCamera);
                foreach (var other in Others.Values.ToList())
                {
                    Rectangle hitBox = new Rectangle(other.Position.X, other.Position.Y, 64, 64);
                    float dist = Vector2.Distance(LocalPlayer.Position, other.Position);

                    if (Raylib.CheckCollisionPointRec(worldMouse, hitBox) && dist <= range)
                    {
                        Console.WriteLine($"Attacking {other.Name} for {dmg} dmg!");
                        _lastAttackedName = other.Name;
                        Program.Net.SendAttack(other.Name);
                        
                        _cAttackTimer = 0; // Reset attack cooldown
                        _cHitTimer = 0;
                        break; 
                    }
                }
            }
            else 
            {
                _cAttackTimer = 0; // Still reset if no damage, to prevent spamming
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
                if (_chunkSnapshot.TryGetValue(coord, out byte b)) drawColor = GetBiomeBaseColor(b, coord.Item1, coord.Item2); else continue;

            Raylib.DrawRectangle((int)wx, (int)wy, chunkSize, chunkSize, drawColor);
        }

        // Draw Structures
        foreach (var structureEntry in Structures)
        {
            var structure = structureEntry.Value;
            var tex = AssetManager.GetTexture(structure.TextureName);
            if (tex.Id != 0)
            {
                // Structures are centered on their chunk, so position is already center.
                // Need to adjust for texture origin to draw correctly.
                // Assuming structure texture is 64x64, and chunk is 16x16.
                // Structure position is (chunkX * 16 + 8, chunkY * 16 + 8).
                // To draw centered, subtract half texture width/height.
                Raylib.DrawTexture(
                    tex,
                    (int)(structure.Position.X - tex.Width / 2),
                    (int)(structure.Position.Y - tex.Height / 2),
                    Color.White
                );
            }
        }

        // Draw Raid Boundary Visuals (Red forcefield ring)
        if ((RaidActive || (RaidTimer > 0 && RaidTimer <= 3.0f)) && _fixedRaidOutpostPosition.HasValue)
        {
            Vector2 activeOutpostCenter = _fixedRaidOutpostPosition.Value;
            const float boundaryRadius = 120f * 16f; // 120 Chunks = 1920 Units
            const float thickness = 10f; // Desired line thickness
            Raylib.DrawRing(activeOutpostCenter, boundaryRadius - thickness / 2, boundaryRadius + thickness / 2, 0, 360, 360, new Color(255, 0, 0, 180));
        }

        // Feature Pass - Rendered Top to Bottom (Y-Sorting) for correct overlap
        var sortedFeatures = loadedChunks
            .Where(coord => {
                float wx = coord.Item1 * chunkSize;
                float wy = coord.Item2 * chunkSize;
                return !(wx + chunkSize < screenTopLeft.X - margin || wx > screenBottomRight.X + margin || 
                         wy + chunkSize < screenTopLeft.Y - margin || wy > screenBottomRight.Y + margin);
            })
            .Where(coord => _featureSnapshot.TryGetValue(coord, out byte feature) && feature != 0)
            .OrderBy(coord => coord.Item2) // Draw low Y (top) first, high Y (bottom) last
            .ToList();

        foreach (var coord in sortedFeatures)
        {
            float wx = coord.Item1 * chunkSize;
            float wy = coord.Item2 * chunkSize;
            _featureSnapshot.TryGetValue(coord, out byte feature);

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

        // Render Players - Sorted Top to Bottom (Y-Sorting)
        var playersToDraw = Others.Values.ToList();
        playersToDraw.Add(LocalPlayer);
        foreach (var p in playersToDraw.OrderBy(p => p.Position.Y))
        {
            p.Draw();
            Debug.DrawHitbox(p.Position);
        }

        // Draw Damage Splashes
        foreach (var p in _damageParticles)
        {
            float t = p.Life / p.MaxLife;
            float currentSize = p.Size * MathF.Pow(t, 0.5f); // Shrink slightly over time

            // Pixelation: Snap position and size to a 2x2 or 4x4 virtual pixel grid
            float pSize = 4f; 
            Vector2 drawPos = new Vector2(MathF.Round(p.Position.X / pSize) * pSize, MathF.Round(p.Position.Y / pSize) * pSize);
            float s = MathF.Max(pSize, MathF.Round(currentSize / pSize) * pSize);
            
            // Layer 1: Dark outer "goo" / Shadow (offset by 1 virtual pixel)
            Raylib.DrawRectangleV(drawPos + new Vector2(pSize, pSize), new Vector2(s, s), new Color(40, 0, 0, (int)(t * 180)));
            
            // Layer 2: Core vibrant blood color
            Raylib.DrawRectangleV(drawPos, new Vector2(s, s), new Color((int)p.ParticleColor.R, 0, 0, (int)(t * 255)));
            
            // Layer 3: Specular highlight (top-left virtual pixel)
            float hSize = MathF.Max(pSize, s * 0.5f);
            Raylib.DrawRectangleV(drawPos, new Vector2(hSize, hSize), new Color(255, 100, 100, (int)(t * 150)));
        }

        Cam.End();

        // UI Overlay Pass (Draw after Cam.End to be in true Screen Space)
        float healthPercent = CurrentHealth / (float)MaxHealth;
        float time = (float)Raylib.GetTime();
        
        // Always present health-based vignette, but use a power curve so it's very faint at high health
        float healthIntensity = MathF.Pow(1.0f - healthPercent, 2.0f);
        
        // Light base intensity if a raid is active or approaching
        bool raidOngoing = RaidActive || (RaidTimer > 0 && RaidTimer <= 3.0f);
        float raidIntensity = raidOngoing ? 0.12f : 0.0f;

        // Pulse effect (throbbing intensity)
        float pulse = MathF.Sin(time * 4.0f) * 0.2f + 0.8f;
        float totalIntensity = Math.Clamp(healthIntensity + raidIntensity, 0f, 1.0f);

        if (totalIntensity > 0.01f)
        {
            int sw = Raylib.GetScreenWidth();
            int sh = Raylib.GetScreenHeight();
            float radius = MathF.Sqrt(sw * sw + sh * sh) * 0.5f;
            int alpha = (int)(totalIntensity * pulse * 180);
            Raylib.DrawCircleGradient(sw / 2, sh / 2, radius, new Color(255, 0, 0, 0), new Color(255, 0, 0, Math.Clamp(alpha, 0, 255)));
        }

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

        if (Raylib.IsKeyDown(KeyboardKey.Tab)) DrawPlayerList();

        healthBar.Draw(CurrentHealth, MaxHealth, CurrentHunger);
        Hotbar.Draw();


        // Draw Global Raid UI
        if (RaidActive || (RaidTimer > 0 && RaidTimer <= 3.0f))
        {
            int sw = Raylib.GetScreenWidth();
            int barW = 400, barH = 24;
            int x = sw / 2 - barW / 2;
            int y = 45; // Fixed top-center position

            // Draw Boss Bar Background (Glow + Inner)
            Raylib.DrawRectangleRounded(new Rectangle(x - 4, y - 4, barW + 8, barH + 8), 0.5f, 4, new Color(255, 80, 0, 40));
            Raylib.DrawRectangleRounded(new Rectangle(x, y, barW, barH), 0.5f, 4, new Color(20, 20, 20, 200));

            // Draw Health Fill
            // If active, show boss HP. If approaching, fill bar based on 3s countdown progress.
            float fillRatio = RaidActive ? RaidBossHealth : Math.Clamp(1.0f - (RaidTimer / 3.0f), 0, 1);
            float fillWidth = barW * fillRatio;
            if (fillWidth > 2)
                Raylib.DrawRectangleRounded(new Rectangle(x, y, fillWidth, barH), 0.5f, 4, new Color(255, 80, 0, 255));
                
            Raylib.DrawRectangleRoundedLines(new Rectangle(x, y, barW, barH), 0.5f, 4, Color.Black);
            
            string raidTitle = RaidActive ? "RAID ENCOUNTER" : "RAID APPROACHING...";
            int tw = Raylib.MeasureText(raidTitle, 22);
            Raylib.DrawText(raidTitle, sw / 2 - tw / 2, y - 28, 22, new Color(255, 200, 0, 255));
        }

        // UI Visual for Cooldown (Optional, helps testing)
        DrawCooldownUI();

        // Movement Tutorial Overlay
        if (_showMovementTutorial)
        {
            int sw = Raylib.GetScreenWidth();
            int sh = Raylib.GetScreenHeight();
            string tutText = "Use the W, A, S, and D keys to move";
            int tutFontSize = 40;
            int tutTextWidth = Raylib.MeasureText(tutText, tutFontSize);

            // Semi-transparent black background bar
            Raylib.DrawRectangle(0, sh / 2 - 60, sw, 120, new Color(0, 0, 0, (int)(160 * _tutorialAlpha)));
            // Big white text
            Raylib.DrawText(tutText, sw / 2 - tutTextWidth / 2, sh / 2 - 20, tutFontSize, new Color(255, 255, 255, (int)(255 * _tutorialAlpha)));
        }
        
        // Raid Tutorial Overlay
        if (_raidTutorialStage != RaidTutorialStage.None)
        {
            int sw = Raylib.GetScreenWidth();
            int sh = Raylib.GetScreenHeight();

            string tutText = "";
            if (_raidTutorialStage == RaidTutorialStage.InfoMessage || _raidTutorialStage == RaidTutorialStage.Fading)
                tutText = "This is a raid. Fight off the raiders by their outpost to win.";
            else if (_raidTutorialStage == RaidTutorialStage.CompletionMessage || _raidTutorialStage == RaidTutorialStage.CompletionFading)
                tutText = "You completed your first raid! Good Job!";

            int tutFontSize = 30;
            int tutTextWidth = Raylib.MeasureText(tutText, tutFontSize);
            float alpha = _raidTutorialAlpha;

            // Semi-transparent black background bar
            Raylib.DrawRectangle(0, sh / 2 - 60, sw, 120, new Color(0, 0, 0, (int)(160 * alpha)));
            // Big white text
            Raylib.DrawText(tutText, sw / 2 - tutTextWidth / 2, sh / 2 - 15, tutFontSize, new Color(255, 255, 255, (int)(255 * alpha)));
        }

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
            
            // If background is bright, make crosshair darker; if dark, make it brighter for contrast.
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

        int displayedCount = 0; // Track how many messages are drawn
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
            Raylib.DrawRectangle(10, yPos - 2, textWidth + 20, fontSize + 4, new Color(40, 40, 40, (int)(160 * alpha)));
            Raylib.DrawText(text, 20, yPos, fontSize, new Color(255, 255, 255, (int)(255 * alpha)));

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
            int y = sh / 2 - 140 + (row * 30); // Vertical spacing
            
            Color nameCol = (sorted[i] == LocalPlayer) ? Color.SkyBlue : Color.White;
            Raylib.DrawText(sorted[i].Name, x, y, 20, nameCol);
        }
    }

    private void DrawCooldownUI()
    {
        if (InvMenu.Visible || Program.IsPaused || _isChatting) return;

        byte heldId = PlayerInventory.Slots[_selectedHotbarIndex].ItemID;
        if (WeaponStats.Library.TryGetValue(heldId, out var stats))
        {
            float charge = Math.Clamp(_cAttackTimer / stats.Cooldown, 0, 1);
            Color barColor = charge < 0.35f ? Color.Red : Color.Green;

            Vector2 mousePos = Raylib.GetMousePosition();
            int barWidth = 40;
            int barHeight = 4;
            int x = (int)mousePos.X - (barWidth / 2);
            int y = (int)mousePos.Y + 16; // Positioned below the crosshair

            // Draw small background for contrast
            Raylib.DrawRectangle(x - 1, y - 1, barWidth + 2, barHeight + 2, new Color(0, 0, 0, 120));
            Raylib.DrawRectangle(x, y, (int)(barWidth * charge), barHeight, barColor);
        }
    }

    private Color GetBiomeBaseColor(byte biome, int cx = 0, int cy = 0)
    {
        // OPTIMIZATION: Use a pre-allocated array lookup instead of a switch statement.
        if (biome < _biomeColors.Length) // Ensure biome index is valid
        {
            Color baseCol = _biomeColors[biome];
            if (biome == 7) // River: shimmer effect with independent rerolling
            {
                int hash = (cx * 73856093) ^ (cy * 19349663);
                float duration = 0.8f + (Math.Abs(hash) % 401 / 1000f); // 0.8s to 1.2s
                int timeStep = (int)(Raylib.GetTime() / duration);

                // Mix timeStep into the hash to pick a "new number" every interval
                int shimmerHash = hash ^ (timeStep * 1103515245);
                int offset = (Math.Abs(shimmerHash) % 21) - 10; // Range: -10 to +10 brightness

                return new Color(
                    (int)Math.Clamp(baseCol.R + offset, 0, 255),
                    (int)Math.Clamp(baseCol.G + offset, 0, 255),
                    (int)Math.Clamp(baseCol.B + offset, 0, 255),
                    255);
            }
            return baseCol;
        }
        return Color.Gray;
    }

    private Color AverageColors(params Color[] colors)
    {
        int r = 0, g = 0, b = 0, a = 0;
        foreach (var c in colors)
        {
            r += c.R; g += c.G; b += c.B; a += c.A;
        }
        return new Color( // Average the color components
            (byte)(r / colors.Length), 
            (byte)(g / colors.Length), 
            (byte)(b / colors.Length), 
            (byte)(a / colors.Length));
    }
}