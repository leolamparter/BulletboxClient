
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

public class CloudParticle
{
    public Vector2 Position;
    public Vector2 Velocity;
    public float Life;
    public float MaxLife;
    public float Size;
    public float Rotation;
    public float Alpha;
}

public class ShootingStar
{
    public Vector2 Position;
    public Vector2 Velocity;
    public float Life;
}

public class VisualBomb
{
    public Vector2 Position;
    public Vector2 Velocity;
    public float Life;
    public float Rotation;
    public VisualBomb(Vector2 pos, Vector2 vel) { Position = pos; Velocity = vel; Life = 1.0f; Rotation = 0; }
}

public class VisualGust // NEW
{
    public Vector2 Position;
    public Vector2 Velocity;
    public float Life;
    public float Rotation;
    public VisualGust(Vector2 pos, Vector2 vel) { Position = pos; Velocity = vel; Life = 1.0f; Rotation = 0; }
}

public class AdvancementNotification
{
    public string Title;
    public float Timer = 0f;
    public float SlideY = -100f;
    public float HeaderAlpha = 1f;
    public float TitleAlpha = 0f;
    public bool Finished = false;
    public AdvancementNotification(string title) { Title = title; }
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
    public readonly object OthersLock = new();
    public CameraManager Cam;
    public Inventory PlayerInventory = new Inventory();
    public HotbarUI Hotbar;
    public InventoryUI InvMenu;
    public Dictionary<(int, int), Structure> Structures = new(); 
    private HealthBar healthBar = new HealthBar();

    public bool CheatsEnabled { get; private set; }
    // World-Specific Advancement Tracking
    public HashSet<string> WorldAdvancements = new();
    public HashSet<byte> WorldVisitedBiomes = new();
    public HashSet<string> WorldKilledOverworld = new();
    public int WorldTotalMobsKilled = 0;
    public int WorldTotalQuartzObtained = 0;
    public int WorldTotalRaidshroomsObtained = 0;

    // Environmental Systems
    public WorldEnvironment Env = new WorldEnvironment();
    private Shader _lightShader;
    private Shader _postShader;
    private RenderTexture2D _sceneTarget;
    private RenderTexture2D _lightingTarget;
    private List<Vector2> _rainParticles = new();
    private List<Vector2> _dustParticles = new();
    private List<Vector2> _moteParticles = new();
    private List<CloudParticle> _cloudParticles = new();
    private List<ShootingStar> _shootingStars = new();

    private List<VisualBomb> _visualBombs = new();
    private readonly object _bombsLock = new();
    private List<VisualGust> _visualGusts = new(); // NEW
    private readonly object _gustsLock = new();

    private List<AdvancementNotification> _activePopups = new();

    // Optimization Caches
    private Dictionary<(int, int), byte> _chunkSnapshot = new();
    private Dictionary<(int, int), byte> _featureSnapshot = new();
    private Dictionary<(int, int), Color> _blendedColorCache = new();
    private HashSet<(int, int)> _pendingBlends = new();
    private List<(int, int)> _sortedPending = new();
    private int _lastPlayerChunkX = int.MaxValue;
    private int _lastPlayerChunkY = int.MaxValue;
    private int _lastScreenWidth;
    private int _lastScreenHeight;

    private float _speedrunTime = 0f;
    private bool _speedrunFinished = false;
    private Dimension _lastDimension = (Dimension)0;
    // Combat Timers
    private float _cAttackTimer = 0f; 
    private float _cHitTimer = 10f;   
    private int _selectedHotbarIndex = 0;
    private int _brimstonePearlSlot = -1;
    private int _lastLocalHealth = 100;
    private float _damageFlashTimer = 0f;
    private float _cameraShakeIntensity = 0f;
    private float _endSequenceTimer = -1f;
    private float _hungerHealTimer = 0f;
    private Dictionary<string, int> _lastOthersHealth = new();
    private List<DamageParticle> _damageParticles = new();
    private List<DamageParticle> _emberParticles = new();
    private List<Vector2> _ashFallParticles = new(); // New list for ash fall
    private float _ashFallAlpha = 0f; // Controls visibility of ash fall
    private float _dustStormAlpha = 0f; // Controls visibility of dust storms based on biome
    private Random _rng = new Random();

    private float _playerBaseSpeed = 350f; // Default player movement speed
    private bool _isSuperSpeedActive = false;

    private Vector2 _kbVelocity = Vector2.Zero;
    
    // Footstep Sound State
    private string _currentFootstepKey = "";
    private float _footstepVolume = 0f;
    private string _fadingFootstepKey = "";
    private float _fadingFootstepVolume = 0f;

    // Ambience State
    private Dictionary<string, float> _ambientVolumes = new();

    // Chat and UI State
    private bool _isChatting = false;
    private string _chatInput = "";
    private List<(string sender, string msg, float time)> _chatLog = new();
    private readonly object _chatLock = new();

    // Death and Kill Tracking State
    private List<string> _lastOtherNames = new();
    private string _lastAttackedName = "";
    private Dictionary<string, Vector2> _lastOtherPositions = new();
    private float _lastAttackTime = 0f;

    // Raid State (Moved from Structure to Player/Session level)
    public byte CurrentBiome = 0;
    public bool RaidActive = false;
    public float RaidBossHealth = 0f;
    public float RaidTimer = 9999f;
    private bool _hasPlayedCountdown = false;
    private bool _hasPlayedHorn = false;
    private Vector2? _fixedRaidOutpostPosition = null; // NEW FIELD

    private List<(Player player, Vector2 screenPos, float rotation)> _playerArrows = new();

    private int _lastSortX = int.MaxValue;
    private int _lastSortY = int.MaxValue;

    private bool _needsCacheClear = false;

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
        new Color(75, 150, 210, 255), // 7: River
        new Color(34, 14, 14, 255),    // 8: Ashen Wastelands (Base: #220e0e)
        new Color(202, 28, 28, 255),    // 9: Lava Pool (Base: #ca1c1c)
        new Color(40, 40, 40, 255),    // 10: The End (Base: Dark Gray)
        new Color(0, 0, 0, 255)        // 11: Void (Solid Black)
    };

    public Playing(string myName)
    {
        LocalPlayer = new Player(myName, new Vector2(400, 300));
        LocalPlayer.Color = Color.Blue;
        Cam = new CameraManager(LocalPlayer.Position);

        // Initialize starting items: Sword only
        PlayerInventory.Slots[0].ItemID = "iron_sword";

        Hotbar = new HotbarUI(PlayerInventory);
        InvMenu = new InventoryUI(PlayerInventory);
        LoadAssets();

        // Initialize Shaders and Targets
        _lightShader = Raylib.LoadShader(null, "resources/shaders/lighting.fs");
        _postShader = Raylib.LoadShader(null, "resources/shaders/post_process.fs");
        _sceneTarget = Raylib.LoadRenderTexture(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
        _lightingTarget = Raylib.LoadRenderTexture(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
        _lastScreenWidth = Raylib.GetScreenWidth();
        _lastScreenHeight = Raylib.GetScreenHeight();
        InitializeWeatherParticles();

        // Initial shader uniform setup
        CheatsEnabled = Program.CurrentWorldData?.CheatsEnabled ?? false; // Initialize cheats from world data

        Raylib.SetShaderValue(_lightShader, Raylib.GetShaderLocation(_lightShader, "screenResolution"), 
            new Vector2(Raylib.GetScreenWidth(), Raylib.GetScreenHeight()), ShaderUniformDataType.Vec2);

        _showMovementTutorial = !Program.CurrentUser.MovementTutorialFinnished;
    }

    public void LoadAssets()
    {
        // Load player and enemy idle textures
        // Load Hotbar UI Textures
        AssetManager.LoadTexture("hotbar_active", "resources/textures/ui/inventory/hotbar_active.png");
        AssetManager.LoadTexture("hotbar_deactive", "resources/textures/ui/inventory/hotbar_deactive.png");
        AssetManager.LoadTexture("crafting_recepie_button", "resources/textures/ui/inventory/crafting_recepie_button.png");

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

        // Dynamically load all item textures from the ItemStats library
        // This ensures keys like "iron_sword" or "wooden_axe" are correctly mapped to their files
        foreach (var entry in ItemStats.Library)
        {
            AssetManager.LoadTexture(entry.Value.TextureKey, $"resources/textures/item/{entry.Value.TextureKey}.png");
        }

        // Load raidshroomer textures
        AssetManager.LoadTexture("raidshroomer_idle", "resources/textures/entity/raidshroomer/idle.png");
        AssetManager.LoadTexture("raidshroomer_angry", "resources/textures/entity/raidshroomer/angry.png");
        AssetManager.LoadTexture("raidshroomer_afraid", "resources/textures/entity/raidshroomer/afraid.png");

        // Load brimstalker textures
        AssetManager.LoadTexture("brimstalker_idle", "resources/textures/entity/brimstalker/idle.png");
        AssetManager.LoadTexture("brimstalker_angry", "resources/textures/entity/brimstalker/angry.png");
        AssetManager.LoadTexture("brimstalker_afraid", "resources/textures/entity/brimstalker/afraid.png");
        
        // Load Apex Boss stages
        AssetManager.LoadTexture("apex_stage1", "resources/textures/entity/apex/stage1.png");
        AssetManager.LoadTexture("apex_stage2", "resources/textures/entity/apex/stage2.png");
        AssetManager.LoadTexture("apex_stage3", "resources/textures/entity/apex/stage3.png");
        AssetManager.LoadTexture("apex_stage4", "resources/textures/entity/apex/stage4.png");

        // Load Vortex textures (NEW)
        AssetManager.LoadTexture("vortex_idle", "resources/textures/entity/vortex/idle.png");
        AssetManager.LoadTexture("vortex_angry", "resources/textures/entity/vortex/angry.png");
        AssetManager.LoadTexture("vortex_afraid", "resources/textures/entity/vortex/afraid.png");
        AssetManager.LoadTexture("vortex_gust", "resources/textures/entity/vortex/gust.png"); // Projectile texture
        // Load Flicker texture
        AssetManager.LoadTexture("flicker_idle", "resources/textures/entity/flicker/idle.png");
        AssetManager.LoadTexture("flicker_angry", "resources/textures/entity/flicker/angry.png");
        AssetManager.LoadTexture("flicker_afraid", "resources/textures/entity/flicker/afraid.png");

        // Load bomb texture
        AssetManager.LoadTexture("brimstalker_bomb", "resources/textures/entity/brimstalker/bomb.png");

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

        // Ambience
        AudioManager.LoadSound("amb_brimstone", "resources/sounds/ambience/brimstone.mp3");
        AudioManager.LoadSound("amb_desert", "resources/sounds/ambience/desert.mp3");
        AudioManager.LoadSound("amb_raid", "resources/sounds/ambience/raid.mp3");
        AudioManager.LoadSound("amb_rain", "resources/sounds/ambience/rain.mp3");
        AudioManager.LoadSound("amb_storm", "resources/sounds/ambience/storm.mp3");
        AudioManager.LoadSound("amb_volcano", "resources/sounds/ambience/volcano.mp3");

        // Load Hunger Bar textures
        for (int i = 0; i <= 110; i += 10)
        {
            AssetManager.LoadTexture($"hunger_{i}", $"resources/textures/ui/hunger_bar/{i}.png");
        }

        if (AssetManager.GetTexture("hotbar_active").Id == 0) Console.WriteLine("ERROR: 'hotbar_active' texture failed to load! Check path: resources/textures/ui/inventory/hotbar_active.png");
        if (AssetManager.GetTexture("hotbar_deactive").Id == 0) Console.WriteLine("ERROR: 'hotbar_deactive' texture failed to load! Check path: resources/textures/ui/inventory/hotbar_deactive.png");
    }
    
    private void InitializeWeatherParticles()
    {
        Random r = new Random();
        // Massive counts and large initial distribution (4000) for high-res/fullscreen support
        int spawnRange = 4000;
        for(int i=0; i<500; i++) _rainParticles.Add(new Vector2(r.Next(0, spawnRange), r.Next(0, spawnRange)));
        for(int i=0; i<3000; i++) _dustParticles.Add(new Vector2(r.Next(0, spawnRange), r.Next(0, spawnRange))); 
        for(int i=0; i<500; i++) _moteParticles.Add(new Vector2(r.Next(0, spawnRange), r.Next(0, spawnRange)));
        for(int i=0; i<3500; i++) _ashFallParticles.Add(new Vector2(r.Next(0, spawnRange), r.Next(0, spawnRange)));
    }

    private void UpdateWeatherParticles(float dt)
    {
        float rainInt = Env.GetWeatherIntensity(WeatherType.Rain);
        float dustInt = Env.GetWeatherIntensity(WeatherType.DustStorm);
        int sw = Raylib.GetScreenWidth();
        int sh = Raylib.GetScreenHeight();
        
        for (int i=0; i<_rainParticles.Count; i++)
            _rainParticles[i] = new Vector2((_rainParticles[i].X + 100 * dt) % sw, (_rainParticles[i].Y + 800 * dt) % sh);
        for (int i=0; i<_dustParticles.Count; i++)
        {
            // Turbulent movement logic for a more atmospheric storm
            float jitter = MathF.Sin((float)Raylib.GetTime() * 1.5f + i) * 12f * dt;
            _dustParticles[i] = new Vector2((_dustParticles[i].X + 450 * dt) % sw, (_dustParticles[i].Y + 30 * dt + jitter) % sh);
        }
        for (int i=0; i<_moteParticles.Count; i++)
            _moteParticles[i] = new Vector2((_moteParticles[i].X - 50 * dt) % sw, (_moteParticles[i].Y + 30 * dt) % sh);

        // Update Ash Fall particles
        for (int i=0; i<_ashFallParticles.Count; i++)
        {
            Vector2 p = _ashFallParticles[i];
            // Use index-based pseudo-random hashing for more unique speeds and sway frequencies.
            // This prevents particles from clumping into the same 40 identical "lanes".
            float speedVariation = ((i * 73856093) % 5000) / 100f; 
            p.Y += (60 + speedVariation) * dt; 
            float swayFreq = 0.4f + ((i * 19349663) % 1000) / 1000f;
            float sway = MathF.Sin((float)Raylib.GetTime() * swayFreq + i) * 25f * dt;
            p.X += sway + (10f * dt); // Slight global drift to the right
            if (p.Y > sh) p.Y = 0; 
            if (p.X < -50) p.X = sw + 50;
            if (p.X > sw + 50) p.X = -50;
            _ashFallParticles[i] = p;
        }

        // Handle Shooting Stars (Only at Night)
        Random r = new Random();
        if (Env.CurrentTime > 180f && r.Next(0, 400) == 0) // Rare chance per frame during night
        {
            _shootingStars.Add(new ShootingStar {
                Position = new Vector2(r.Next(0, Raylib.GetScreenWidth()), r.Next(0, Raylib.GetScreenHeight() / 2)),
                Velocity = new Vector2(r.Next(600, 1200), r.Next(200, 500)),
                Life = 1.0f
            });
        }

        for (int i = _shootingStars.Count - 1; i >= 0; i--)
        {
            var s = _shootingStars[i];
            s.Position += s.Velocity * dt;
            s.Life -= dt * 1.5f;
            if (s.Life <= 0) _shootingStars.RemoveAt(i);
        }

        // Handle Cloud Particles
        for (int i = _cloudParticles.Count - 1; i >= 0; i--)
        {
            var p = _cloudParticles[i];
            p.Life -= dt;
            if (p.Life <= 0) { _cloudParticles.RemoveAt(i); continue; }
            p.Position += p.Velocity * dt;
            p.Size += dt * 40f; // Puff up effect
            p.Alpha = p.Life / p.MaxLife;
        }

        // Spawn Boundary Clouds during Raids
        if (RaidActive && _fixedRaidOutpostPosition.HasValue)
        {
            Random rng = new Random();
            for (int j = 0; j < 3; j++) // Spawn 3 clouds every frame for "much more frequent" look
            {
                float angle = (float)(rng.NextDouble() * Math.PI * 2);
                Vector2 pos = _fixedRaidOutpostPosition.Value + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (120f * 16f);
                if (rng.Next(0, 2) == 0) SpawnBoundaryCloud(pos);
            }
        }
    }

    private void RedistributeParticles(int oldW, int oldH, int newW, int newH)
    {
        if (oldW <= 0 || oldH <= 0) return;
        Vector2 ratio = new Vector2((float)newW / oldW, (float)newH / oldH);

        for (int i = 0; i < _rainParticles.Count; i++) _rainParticles[i] *= ratio;
        for (int i = 0; i < _dustParticles.Count; i++) _dustParticles[i] *= ratio;
        for (int i = 0; i < _moteParticles.Count; i++) _moteParticles[i] *= ratio;
        for (int i = 0; i < _ashFallParticles.Count; i++) _ashFallParticles[i] *= ratio;
    }

    public void SpawnVisualBomb(Vector2 start, Vector2 velocity)
    {
        lock (_bombsLock)
        {
            _visualBombs.Add(new VisualBomb(start, velocity));
        }
    }

    private void UpdateVisualBombs(float dt)
    {
        lock (_bombsLock)
        {
            for (int i = _visualBombs.Count - 1; i >= 0; i--) {
                var b = _visualBombs[i];
                
                // Client-side Aimbot: Gently curve the visual projectile toward the local player
                Vector2 playerCenter = LocalPlayer.Position + new Vector2(32, 32);
                Vector2 desiredDir = Vector2.Normalize(playerCenter - b.Position);
                b.Velocity = Vector2.Normalize(Vector2.Lerp(Vector2.Normalize(b.Velocity), desiredDir, dt * 3.5f)) * b.Velocity.Length();
                
                b.Position += b.Velocity * dt;
                b.Life -= dt;
                b.Rotation += dt * 360f;
                
                bool exploded = b.Life <= 0;
                if (!exploded)
                {
                    float dist = Vector2.Distance(LocalPlayer.Position + new Vector2(32, 32), b.Position);
                    if (dist < 25f) exploded = true;
                }

                if (exploded) {
                    SpawnDeathPuff(b.Position); // Visual explosion
                    _visualBombs.RemoveAt(i);
                }
            }
        }
    }

    public bool IsBossActive()
    {
        lock (OthersLock) { return Others.Values.Any(o => o.Name == "Brimstalker"); }
    }

    private void UpdateAudioSystem(float dt, byte biome, bool raid)
    {
        if (_endSequenceTimer >= 0) return;

        // --- Ambience Management ---
        void ManageAmbience(string key, bool active, float maxVol = 0.15f) // Reduced default maxVol
        {
            maxVol *= Program.SfxVolume; // Apply global SFX volume setting
            if (!_ambientVolumes.ContainsKey(key)) _ambientVolumes[key] = 0f;
            
            if (active && !Program.IsPaused) // Ambience should still pause with the game
            {
                if (!AudioManager.IsSoundPlaying(key)) AudioManager.PlaySound(key);
                _ambientVolumes[key] = Math.Min(_ambientVolumes[key] + dt * 0.4f, maxVol);
            }
            else
            {
                _ambientVolumes[key] = Math.Max(_ambientVolumes[key] - dt * 0.4f, 0f);
                if (_ambientVolumes[key] <= 0 && AudioManager.IsSoundPlaying(key)) AudioManager.StopSound(key);
            }
            
            if (AudioManager.IsSoundPlaying(key))
                AudioManager.SetVolume(key, _ambientVolumes[key]);
        }

        ManageAmbience("amb_brimstone", biome == 6);
        ManageAmbience("amb_desert", biome == 2);
        ManageAmbience("amb_raid", raid); // Raid ambience should still play if raid is active
        ManageAmbience("amb_rain", Env.GetWeatherIntensity(WeatherType.Rain) > 0.4f);
        ManageAmbience("amb_storm", (biome == 2 || biome == 5) && Env.GetWeatherIntensity(WeatherType.DustStorm) > 0.4f); // Added biome check
        ManageAmbience("amb_volcano", biome == 9);
    }

    public void SpawnVisualGust(Vector2 start, Vector2 velocity) // NEW
    {
        lock (_gustsLock)
        {
            _visualGusts.Add(new VisualGust(start, velocity));
        }
    }

    private void UpdateVisualGusts(float dt) // NEW
    {
        lock (_gustsLock)
        {
            for (int i = _visualGusts.Count - 1; i >= 0; i--) {
                var g = _visualGusts[i];
                g.Position += g.Velocity * dt;
                g.Life -= dt;
                g.Rotation += dt * 720f; // Faster rotation for gust
                if (g.Life <= 0) { _visualGusts.RemoveAt(i); }
            }
        }
    }

    public void AddChatMessage(string sender, string msg)
    {
        lock (_chatLock)
        {
            _chatLog.Add((sender, msg, (float)Raylib.GetTime()));
            if (_chatLog.Count > 50) _chatLog.RemoveAt(0);
        }
    }

    public void ApplyKnockback(Vector2 force)
    {
        _kbVelocity += force * 15f; // Multiplier to turn 'distance' into 'velocity'
    } 

    public void TriggerCacheClear()
    {
        _needsCacheClear = true;
    }

    public void GrantAdvancement(string key, bool showPopup = true)
    {
        if (WorldAdvancements.Contains(key)) return;
        WorldAdvancements.Add(key);

        // Parse progress-based data from the keys sent by the server
        if (key.StartsWith("EnterBiome:")) {
            if (byte.TryParse(key.Split(':')[1], out byte b)) WorldVisitedBiomes.Add(b);
        }
        else if (key.StartsWith("Kill:")) {
            WorldKilledOverworld.Add(key.Split(':')[1]);
        }
        else if (key == "FirstBlood") WorldTotalMobsKilled = Math.Max(WorldTotalMobsKilled, 1);
        else if (key == "GettingStronger") WorldTotalMobsKilled = Math.Max(WorldTotalMobsKilled, 25);
        else if (key == "EnoughCrystalsAlready") WorldTotalQuartzObtained = Math.Max(WorldTotalQuartzObtained, 20);
        else if (key == "ThatsEnoughCrystalsNo") WorldTotalQuartzObtained = Math.Max(WorldTotalQuartzObtained, 99);
        else if (key == "StopItWithTheCrystals") WorldTotalQuartzObtained = Math.Max(WorldTotalQuartzObtained, 198);
        else if (key == "ImHungry") WorldTotalRaidshroomsObtained = Math.Max(WorldTotalRaidshroomsObtained, 20);
        else if (key == "FOOOOOOOOOOD") WorldTotalRaidshroomsObtained = Math.Max(WorldTotalRaidshroomsObtained, 99);

        if (!showPopup) return;

        var adv = AdvancementsScreen.AllAdvancements.FirstOrDefault(a => a.Key == key);
        if (!string.IsNullOrEmpty(adv.Title))
        {
            _activePopups.Add(new AdvancementNotification(adv.Title));
        }
    }

    public void TriggerAdvancementPopup(string key) => GrantAdvancement(key, true);

    private bool IsAdvancementAlreadyCompleted(string key)
    {
        return WorldAdvancements.Contains(key);
    }
    
    private void UpdatePopups(float dt)
    {
        for (int i = _activePopups.Count - 1; i >= 0; i--)
        {
            var p = _activePopups[i];
            p.Timer += dt;

            // Slide In (0.5s)
            if (p.Timer < 0.5f) p.SlideY = -100 + (20 - (-100)) * (p.Timer / 0.5f);
            
            // Stage 1: "ADVANCEMENT COMPLETE!" visible for 1s
            else if (p.Timer < 1.5f) { p.SlideY = 20; p.HeaderAlpha = 1; p.TitleAlpha = 0; }
            
            // Stage 2: Crossfade (0.5s)
            else if (p.Timer < 2.0f)
            {
                float t = (p.Timer - 1.5f) / 0.5f;
                p.HeaderAlpha = 1.0f - t;
                p.TitleAlpha = t;
            }
            
            // Stage 3: Title visible for 2s
            else if (p.Timer < 4.0f) { p.HeaderAlpha = 0; p.TitleAlpha = 1; }
            
            // Stage 4: Slide Out (0.5s)
            else if (p.Timer < 4.5f) p.SlideY = 20 + (-100 - 20) * ((p.Timer - 4.0f) / 0.5f);
            
            else p.Finished = true;

            if (p.Finished) _activePopups.RemoveAt(i);
        }
    }

    private void ClearWorldCaches()
    {
        loadedChunks.Clear();
        _chunkSnapshot.Clear();
        _featureSnapshot.Clear();
        _blendedColorCache.Clear();
        _pendingBlends.Clear();
        _sortedPending.Clear();
        Structures.Clear();
        _lastPlayerChunkX = int.MaxValue;
        _lastPlayerChunkY = int.MaxValue;
        Cam.RaylibCamera.Target = new Vector2(32, 32); // Snap camera to player center
    }

    public void Update(float dt, bool windowResized)
    {
        if (_needsCacheClear)
        {
            ClearWorldCaches();
            _needsCacheClear = false;
        }

        int playerChunkX = 0;
        int playerChunkY = 0;

        // Condition for game logic to run:
        // Game logic pauses if Program.IsPaused is true AND we are connected to an integrated server (127.0.0.1).
        // Game logic continues if Program.IsPaused is false, OR if we are connected to a remote server.
        bool isMenuOpen = Program.IsPaused || Program.CurrentState == GameState.OPTIONS;
        bool runGameLogic = !isMenuOpen || (Program.Net.IsConnected() && Program.LastIP != "127.0.0.1");

        int sw = Raylib.GetScreenWidth();
        int sh = Raylib.GetScreenHeight();

        // Handle Window Resizing for Render Textures
        if (windowResized || sw != _lastScreenWidth || sh != _lastScreenHeight)
        {
            Raylib.UnloadRenderTexture(_sceneTarget);
            Raylib.UnloadRenderTexture(_lightingTarget);
            _sceneTarget = Raylib.LoadRenderTexture(sw, sh);
            _lightingTarget = Raylib.LoadRenderTexture(sw, sh);

            RedistributeParticles(_lastScreenWidth, _lastScreenHeight, sw, sh);
            _lastScreenWidth = sw;
            _lastScreenHeight = sh;

            // NEW: Clear blended color cache and force chunk re-evaluation on window resize
            _blendedColorCache.Clear();
            _pendingBlends.Clear();
            _sortedPending.Clear();
            // Force re-evaluation of chunk radius and chunk loading in the next update cycle
            _lastPlayerChunkX = int.MaxValue;
            _lastPlayerChunkY = int.MaxValue;
        }

        // Update Environment
        Env.Update(dt, RaidActive);
        UpdateWeatherParticles(dt);
        UpdateVisualBombs(dt);
        UpdateVisualGusts(dt); // NEW
        UpdatePopups(dt);

        if (_endSequenceTimer >= 0)
        {
            _endSequenceTimer += dt;
            Program.IsEnding = true;
            Env.TargetWeather = WeatherType.Clear;
            Env.CurrentWeather = WeatherType.Clear;
            Env.WeatherTransition = 1.0f;
            
            // Teleport to the end during the black screen (4.2 seconds) to avoid music glitches
            if (_brimstonePearlSlot >= 0 && _endSequenceTimer >= 4.2f)
            {
                // Update client inventory immediately so the item disappears
                if (PlayerInventory.Slots[_brimstonePearlSlot].Count > 1)
                    PlayerInventory.Slots[_brimstonePearlSlot].Count--;
                else
                    PlayerInventory.Slots[_brimstonePearlSlot] = new ItemStack("none", 0);

                Program.Net.SendConsumeItem((byte)_brimstonePearlSlot);
                _brimstonePearlSlot = -2; // Sentinel value: packet sent, waiting for dimension sync
            }

            // Return to gameplay once teleport is processed and sequence duration is finished
            if (_brimstonePearlSlot == -2 && _endSequenceTimer >= 5.5f)
            {
                _endSequenceTimer = -1f;
                _brimstonePearlSlot = -1;
                Program.IsEnding = false;
                return;
            }

            if (_endSequenceTimer > 25.0f) // Return home once credits have finished scrolling
            {
                Program.IsEnding = false;
                Program.DisconnectAndLeave();
            }
            return; // Halt regular gameplay updates during the sequence
        }

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
        if (!_isChatting)
        {
            Hotbar.Update();
            InvMenu.Update();
        }
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

            if (Program.SpeedrunTimerEnabled)
            {
                if (!_speedrunFinished)
                {
                    _speedrunTime += dt;
                }

                if (_lastDimension == Dimension.TheEnd && Program.Net.CurrentDimension == (Dimension)0)
                {
                    _speedrunFinished = true;
                }
                _lastDimension = Program.Net.CurrentDimension;
            }

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
                bool interacted = false;
                Vector2 worldMouse = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), Cam.RaylibCamera);
                foreach (var s in Structures.Values) {
                    // Check for right-clicking structures (Chests) - Server validates IsCompleted
                    if (Vector2.Distance(worldMouse, s.Position) < 150f && s.Type == StructureType.RaidOutpost) {
                        if (s.IsCompleted)
                        {
                            s.HasBeenOpened = true;
                        }
                        Program.Net.SendOpenChest(s.ChunkX, s.ChunkY);
                        interacted = true;
                        break;
                    }
                }

                var stack = PlayerInventory.Slots[_selectedHotbarIndex];
                if (!interacted && stack.ItemID == "brimstone_pearl" && Program.Net.CurrentDimension != Dimension.TheEnd)
                {
                    _endSequenceTimer = 0f;
                    _brimstonePearlSlot = _selectedHotbarIndex;
                    Program.IsEnding = true;
                    AudioManager.StopAll();
                    interacted = true;
                }

                if (!interacted && stack.ItemID == "raidshroom" && CurrentHunger < 110)
                {
                    CurrentHunger = Math.Min(110, CurrentHunger + 15);
                    if (stack.Count > 1) PlayerInventory.Slots[_selectedHotbarIndex].Count--;
                    else PlayerInventory.Slots[_selectedHotbarIndex] = new ItemStack("none", 0);
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
            playerChunkX = (int)MathF.Floor(LocalPlayer.Position.X / chunkSize);
            playerChunkY = (int)MathF.Floor(LocalPlayer.Position.Y / chunkSize);

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
                    if (Program.Net.ChunkBiomes.TryGetValue(coord, out CurrentBiome))
                    {
                        bool isNew = !_chunkSnapshot.TryGetValue(coord, out byte oldBiome);
                        if (isNew || oldBiome != CurrentBiome)
                        {
                            _chunkSnapshot[coord] = CurrentBiome;
                            
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
                            case StructureType.EndPortal:
                    textureName = "end_portal";
                    break;
                            default:
                                textureName = ""; // No texture for unknown types
                                break;
                        }
                        if (!string.IsNullOrEmpty(textureName))
                        {
                            var newS = new Structure(structureEntry.Value.Position, structureEntry.Value.Type, structureEntry.Value.ChunkX, structureEntry.Value.ChunkY, textureName);
                            newS.IsCompleted = structureEntry.Value.IsCompleted;
                            Structures.Add(coord, newS);
                        }
                    }
                    else if (Structures.TryGetValue(coord, out var existingS))
                    {
                        existingS.IsCompleted = structureEntry.Value.IsCompleted;
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

            if (_wasRaidActive && !RaidActive && RaidBossHealth <= 0)
            {
                // Mark the local outpost as completed to show the "Open" text immediately
                if (_fixedRaidOutpostPosition.HasValue)
                {
                    foreach (var s in Structures.Values)
                    {
                        if (Vector2.Distance(s.Position, _fixedRaidOutpostPosition.Value) < 50f)
                        {
                            s.IsCompleted = true;
                            break;
                        }
                    }
                }

                if (!Program.CurrentUser.RaidCompletedTutorialFinnished)
                {
                    _raidTutorialStage = RaidTutorialStage.CompletionMessage;
                    _raidTutorialAlpha = 1.0f;
                }
            }
            _wasRaidActive = RaidActive;

            // Damage Splash Detection & Particle Update
            if (CurrentHealth < _lastLocalHealth) 
            {
                _damageFlashTimer = 0.2f;
                _cameraShakeIntensity = 15f;
                SpawnDamageSplash(LocalPlayer.Position + new Vector2(32, 32));
            }
            _lastLocalHealth = CurrentHealth;

            lock (OthersLock)
            {
                foreach (var kvp in Others)
                {
                    if (!_lastOthersHealth.TryGetValue(kvp.Key, out int lastH)) {
                        _lastOthersHealth[kvp.Key] = kvp.Value.Health;
                        continue;
                    }
                    if (kvp.Value.Health < lastH) SpawnDamageSplash(kvp.Value.Position + new Vector2(32, 32));
                    _lastOthersHealth[kvp.Key] = kvp.Value.Health;
                }
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

            // Update ember particles
            for (int i = _emberParticles.Count - 1; i >= 0; i--)
            {
                var p = _emberParticles[i];
                p.Life -= dt;
                if (p.Life <= 0) { _emberParticles.RemoveAt(i); continue; }

                p.Position += p.Velocity * dt;
                p.Rotation += p.AngularVelocity * dt;
            p.Velocity.Y -= 60f * dt; // Embers float upwards faster
            }

            ProcessPendingBlends();

            // Update Blocking State
            bool wasBlocking = LocalPlayer.IsBlocking;
            LocalPlayer.OffHandItemID = PlayerInventory.Slots[24].ItemID;
            LocalPlayer.IsBlocking = Raylib.IsMouseButtonDown(MouseButton.Right) && LocalPlayer.OffHandItemID == "shield";
            
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
            // Use _fixedRaidOutpostPosition aaif a raid is active or approaching
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
            List<Player> currentOthers;
            lock (OthersLock)
            {
                currentOthers = Others.Values.ToList();
            }
            foreach (var other in currentOthers)
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
            lock (OthersLock)
            {
                foreach (var other in Others.Values)
                {
                    other.Update(dt);
                }
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

            // Apply Camera Shake
            if (_cameraShakeIntensity > 0)
            {
                Cam.RaylibCamera.Offset += new Vector2(
                    (float)(_rng.NextDouble() * 2 - 1) * _cameraShakeIntensity,
                    (float)(_rng.NextDouble() * 2 - 1) * _cameraShakeIntensity
                );
            }

            // Decay visual effects
            if (_damageFlashTimer > 0) _damageFlashTimer -= dt;
            if (_cameraShakeIntensity > 0) _cameraShakeIntensity = Math.Max(0, _cameraShakeIntensity - dt * 75f);

            // Only send updates if moved or rotated significantly to save bandwidth
            // but we send it every frame for now to ensure other players see smooth weapon rotation
            Program.Net.SendPosition(LocalPlayer.Position.X, LocalPlayer.Position.Y, LocalPlayer.Rotation);
        }

        // Control Ash Fall effect based on current biome
        playerChunkX = (int)MathF.Floor(LocalPlayer.Position.X / chunkSize);
        playerChunkY = (int)MathF.Floor(LocalPlayer.Position.Y / chunkSize);
        bool hasBiome = _chunkSnapshot.TryGetValue((playerChunkX, playerChunkY), out CurrentBiome);
        if (!hasBiome) // Fallback if snapshot not yet populated
        {
            lock (Program.Net.ChunkBiomesLock)
            {
                hasBiome = Program.Net.ChunkBiomes.TryGetValue((playerChunkX, playerChunkY), out CurrentBiome);
            }
        }
        // Ash should fall in Ashen Wastelands and Lava Pools, but not in The End dimension
        if (hasBiome && (CurrentBiome == (byte)BiomeType.AshenWastelands || CurrentBiome == (byte)BiomeType.LavaPool) && Program.Net.CurrentDimension != Dimension.TheEnd)
            _ashFallAlpha = Math.Min(1.0f, _ashFallAlpha + dt * 0.5f); // Fade in
        else _ashFallAlpha = Math.Max(0.0f, _ashFallAlpha - dt * 0.5f); // Fade out
        
        // Dust Storm logic: Only visible in Desert (2) or Beach (5)
        if (hasBiome && (CurrentBiome == 2 || CurrentBiome == 5)) _dustStormAlpha = Math.Min(1.0f, _dustStormAlpha + dt * 0.5f);
        else _dustStormAlpha = Math.Max(0.0f, _dustStormAlpha - dt * 0.5f);

        // Ambient Embers for Ashen Wastelands and Lava Pools
        if (runGameLogic)
        {
            Random r = new Random();
            // Check current and neighbors for Lava (9) or Ashen (8) to decide what to spawn
            bool isNearLava = false;
            bool isNearAshen = false;
            for (int dx = -1; dx <= 1; dx++) {
                for (int dy = -1; dy <= 1; dy++) {
                    if (_chunkSnapshot.TryGetValue((playerChunkX + dx, playerChunkY + dy), out byte b)) {
                        if (b == 9) isNearLava = true;
                        if (b == 8) isNearAshen = true;
                    }
                }
            }

            if ((isNearLava || isNearAshen) && Program.Net.CurrentDimension != Dimension.TheEnd && r.Next(0, 100) < 15) {
                Vector2 randomOffset = new Vector2(r.Next(-500, 500), r.Next(-500, 500));
                // If nearby lava, 60% chance for orange, else red-ash
                Color col = (isNearLava && r.Next(0, 10) < 6) ? new Color(255, 140, 20, 255) : new Color(110, 50, 45, 255);
                SpawnEmber(LocalPlayer.Position + new Vector2(32, 32) + randomOffset, col);
            }
        }
        
        // --- Global Death/Kill Detection ---
        List<string> currentOtherNames;
        lock (OthersLock)
        {
            currentOtherNames = Others.Keys.ToList();
        }
        foreach (var name in _lastOtherNames)
        {
            if (!currentOtherNames.Contains(name))
            {
                // Something died or disconnected
                if (_lastOtherPositions.TryGetValue(name, out var pos)) SpawnDeathPuff(pos + new Vector2(32, 32));
                AudioManager.PlaySound("player_death"); // death.mp3

                // If it's the target we just hit, play the kill sound on top
                if (name == _lastAttackedName && (float)Raylib.GetTime() - _lastAttackTime < 1.5f)
                {
                    AudioManager.PlaySound("player_kill"); // kill.mp3
                } 
            }
        }
        _lastOtherNames = currentOtherNames;
        
        // Cache positions for the next frame's death detection
        lock (OthersLock)
        {
            _lastOtherPositions.Clear();
            foreach (var kvp in Others) {
                _lastOtherPositions[kvp.Key] = kvp.Value.Position;
            }
        }

        // Update Structure Text Alpha Fading
        foreach (var s in Structures.Values)
        {
            if (s.HasBeenOpened && s.TextFadeAlpha > 0)
            {
                s.TextFadeAlpha -= dt * 2.0f; // Fade out over 0.5 seconds
                if (s.TextFadeAlpha < 0) s.TextFadeAlpha = 0;
            }
        }
        
        UpdateAudioSystem(dt, CurrentBiome, RaidActive);
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
                {
                    // Command handling for testing purposes
                    if (_chatInput.StartsWith("time="))
                    {
                        if (float.TryParse(_chatInput.Substring(5), out float newTime))
                        { // This is client-side only for now
                            Env.CurrentTime = Math.Clamp(newTime, 0f, WorldEnvironment.DayLength);
                            AddChatMessage("SYSTEM", $"Time set to {Env.CurrentTime:F1}s");
                        }
                    }
                    else if (CheatsEnabled) // Only allow these commands if cheats are enabled
                    {
                        if (_chatInput.StartsWith("giveitem:"))
                        {
                            Program.Net.SendChat(_chatInput);
                        }
                        else if (_chatInput.Equals("superspeed", StringComparison.OrdinalIgnoreCase))
                        {
                            _playerBaseSpeed = 3500f; // Make player super fast
                            _isSuperSpeedActive = true;
                            AddChatMessage("SYSTEM", "Superspeed activated!");
                        }
                        else if (_chatInput.Equals("normalspeed", StringComparison.OrdinalIgnoreCase))
                        {
                            _playerBaseSpeed = 350f; // Revert to normal speed
                            _isSuperSpeedActive = false;
                            AddChatMessage("SYSTEM", "Normal speed restored.");
                        }
                        else if (_chatInput.Equals("overworld", StringComparison.OrdinalIgnoreCase))
                        {
                            Program.Net.SendChat("/teleport 0 0 0"); // Assuming this teleports to overworld spawn
                            AddChatMessage("SYSTEM", "Teleporting to Overworld spawn.");
                        }
                        else
                        {
                            Program.Net.SendChat(_chatInput);
                        }
                    }
                    else
                    {
                        Program.Net.SendChat(_chatInput);
                    }
                }
                _isChatting = false;
            }
            if (Raylib.IsKeyPressed(KeyboardKey.Escape)) _isChatting = false; // Close chat on escape
        }
    }

    private void SpawnEmber(Vector2 pos, Color col)
    {
        Random r = new Random();
        float life = (float)(r.NextDouble() * 1.2f + 0.6f); // Longer life (0.6s to 1.8s)
        _emberParticles.Add(new DamageParticle {
            Position = pos + new Vector2(r.Next(-15, 15), r.Next(-5, 5)),
            Velocity = new Vector2(r.Next(-30, 30), r.Next(-150, -40)), // Higher velocity
            Life = life,
            MaxLife = life,
            ParticleColor = col,
            Size = (float)(r.NextDouble() * 2 + 2),
            Rotation = (float)(r.NextDouble() * 360),
            AngularVelocity = (float)(r.NextDouble() * 400 - 200)
        });
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

    private void SpawnDeathPuff(Vector2 pos)
    {
        Random r = new Random();
        int count = r.Next(6, 10);
        for (int i = 0; i < count; i++)
        {
            float angle = (float)(r.NextDouble() * Math.PI * 2);
            float speed = (float)(r.NextDouble() * 40 + 20);
            float life = (float)(r.NextDouble() * 0.4f + 0.3f);
            _cloudParticles.Add(new CloudParticle {
                Position = pos + new Vector2(r.Next(-10, 10), r.Next(-10, 10)),
                Velocity = new Vector2(MathF.Cos(angle) * speed, MathF.Sin(angle) * speed),
                Life = life,
                MaxLife = life,
                Size = (float)(r.NextDouble() * 20 + 20),
                Rotation = (float)(r.NextDouble() * 360)
            });
        }
    }

    private void SpawnBoundaryCloud(Vector2 pos)
    {
        Random r = new Random();
        float life = (float)(r.NextDouble() * 4.0f + 3.0f); // Lingering: 3 to 7 seconds
        float angle = (float)(r.NextDouble() * Math.PI * 2);
        float speed = (float)(r.NextDouble() * 8 + 2); // Slower drift
        
        _cloudParticles.Add(new CloudParticle {
            Position = pos,
            Velocity = new Vector2(MathF.Cos(angle) * speed, MathF.Sin(angle) * speed),
            Life = life,
            MaxLife = life,
            Size = (float)(r.NextDouble() * 25 + 20), // Much smaller individual clouds
            Rotation = (float)(r.NextDouble() * 360),
            // Boundary clouds are much fainter
            Alpha = 0.3f 
        });
    }

    private void HandleMovement(float dt)
    {
        float currentSpeed = _playerBaseSpeed;
        bool isMoving = false;
        int cx = 0, cy = 0;
        byte biome = 0;
        bool hasBiome = false;

        if (LocalPlayer.IsBlocking && !_isSuperSpeedActive) currentSpeed *= 0.55f; // 45% slow down while blocking, unless superspeed is active

        Vector2 direction = Vector2.Zero;
        if (Raylib.IsKeyDown(KeyboardKey.W)) direction.Y -= 1;
        if (Raylib.IsKeyDown(KeyboardKey.S)) direction.Y += 1;
        if (Raylib.IsKeyDown(KeyboardKey.A)) direction.X -= 1;
        if (Raylib.IsKeyDown(KeyboardKey.D)) direction.X += 1;

        if (direction.X < 0) LocalPlayer.FacingRight = false;
        else if (direction.X > 0) LocalPlayer.FacingRight = true;
        
        if (direction != Vector2.Zero)
        {
            // Normalize ensures that diagonal movement is not faster than cardinal movement, then apply currentSpeed
            LocalPlayer.Position += Vector2.Normalize(direction) * currentSpeed * dt;
        }

        isMoving = direction != Vector2.Zero;
        cx = (int)MathF.Floor(LocalPlayer.Position.X / chunkSize);
        cy = (int)MathF.Floor(LocalPlayer.Position.Y / chunkSize);

        hasBiome = _chunkSnapshot.TryGetValue((cx, cy), out biome);

        // Spawn Embers in Ashen Wastelands and Lava Pools
        if (isMoving && !Program.IsPaused)
        {
            // Use unique colors for different biomes: Ash Gray for Ashen, Bright Orange for Lava
            if (biome == (byte)BiomeType.AshenWastelands && Program.Net.CurrentDimension != Dimension.TheEnd && new Random().Next(0, 100) < 15) 
                SpawnEmber(LocalPlayer.Position + new Vector2(32, 58), new Color(110, 50, 45, 255));
            else if (biome == (byte)BiomeType.LavaPool && Program.Net.CurrentDimension != Dimension.TheEnd && new Random().Next(0, 100) < 15)
                SpawnEmber(LocalPlayer.Position + new Vector2(32, 58), new Color(255, 140, 20, 255)); // Lava embers
        }

        // --- Footstep Sound Logic ---
        if (!hasBiome) 
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
                10 => "stonypeaks", // The End fallback
                8 => "stonypeaks", // Ashen Wastelands fallback
                _ => ""
            };
        }
        bool shouldPlay = isMoving && !string.IsNullOrEmpty(targetFootstep) && !Program.IsPaused && _endSequenceTimer < 0;

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
        if (myBiome == 7 || myBiome == 9) return baseCol; // Rivers and Lava stay sharp

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
            string heldId = PlayerInventory.Slots[_selectedHotbarIndex].ItemID;
            var (dmg, kb, range) = WeaponStats.Calculate(heldId, _cAttackTimer, _cHitTimer);

            if (dmg > 0)
            {
                LocalPlayer.TriggerAttack();
                AudioManager.PlaySound("sword_swing");
                _lastAttackTime = (float)Raylib.GetTime();
                
                Vector2 worldMouse = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), Cam.RaylibCamera);
                List<Player> combatTargets;
                lock (OthersLock) { combatTargets = Others.Values.ToList(); }
                foreach (var other in combatTargets)
                {
                    // Increase hitbox size for the APEX boss
                    float hitBoxSize = (other.Name == "APEX") ? 256f : 64f;
                    float offset = (64f - hitBoxSize) / 2f;
                    Rectangle hitBox = new Rectangle(other.Position.X + offset, other.Position.Y + offset, hitBoxSize, hitBoxSize);

                    float dist = Vector2.Distance(LocalPlayer.Position, other.Position);

                    if (Raylib.CheckCollisionPointRec(worldMouse, hitBox) && dist <= range)
                    {
                        // Only allow attacking other players or raiders
                        if (!other.Name.StartsWith("Raider") && !other.Name.StartsWith("Flicker") && !other.Name.StartsWith("Vortex") && other.Name != "Brimstalker" && other.Name != "APEX" && other.Name != LocalPlayer.Name) continue;
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

    private void DrawShadows()
    {
        // Render directional skewed shadows for trees and structures
        Vector2 sunDir = Vector2.Normalize(Env.ShadowDirection); // Ensure normalized for consistent offset
        float len = Env.ShadowLength; // Shadow length multiplier
        Color shadowCol = new Color(0, 0, 0, 120);

        // Shadows for Players and Raiders
        List<Player> allPlayers;
        lock (OthersLock)
        {
            allPlayers = Others.Values.ToList();
        }
        allPlayers.Add(LocalPlayer);
        foreach(var p in allPlayers)
        {
            Vector2 playerBase = new Vector2(p.Position.X + 32, p.Position.Y + 55); // Center X, near bottom Y of 64x64 sprite
            Vector2 shadowOffset = sunDir * (10 * len); // Reduced multiplier to keep shadows attached
            float shadowWidth = 25 + (len * 5); // Wider and stretches with length
            float shadowHeight = 12 + (len * 3); // Taller and stretches with length
            Raylib.DrawEllipse((int)(playerBase.X + shadowOffset.X), (int)(playerBase.Y + shadowOffset.Y), 
                               (int)shadowWidth, (int)shadowHeight, shadowCol);
        }
    }

    public void Draw()
    {
        // 2. Main Scene Pass
        Raylib.BeginTextureMode(_sceneTarget);
        Raylib.ClearBackground(Color.Black);
        
        Cam.Begin();
        // Optimization: Calculate screen bounds to skip drawing off-screen chunks
        Vector2 screenTopLeft = Raylib.GetScreenToWorld2D(new Vector2(0, 0), Cam.RaylibCamera);
        Vector2 screenBottomRight = Raylib.GetScreenToWorld2D(new Vector2(Raylib.GetScreenWidth(), Raylib.GetScreenHeight()), Cam.RaylibCamera);
        
        // Calculate visible chunk range instead of iterating over thousands of loaded chunks
        int minX = (int)MathF.Floor(screenTopLeft.X / chunkSize) - 1;
        int maxX = (int)MathF.Ceiling(screenBottomRight.X / chunkSize) + 1;
        int minY = (int)MathF.Floor(screenTopLeft.Y / chunkSize) - 1;
        int maxY = (int)MathF.Ceiling(screenBottomRight.Y / chunkSize) + 1;

        // 1. Terrain Pass
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                var coord = (x, y);
                if (!_blendedColorCache.TryGetValue(coord, out Color drawColor))
                {
                    if (_chunkSnapshot.TryGetValue(coord, out byte b)) 
                        drawColor = GetBiomeBaseColor(b, x, y); 
                    else continue;
                }
                Raylib.DrawRectangle(x * chunkSize, y * chunkSize, chunkSize, chunkSize, drawColor);
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

        // 2. Feature Pass - Rendered Top to Bottom (Y-Sorting)
        // Iterating by Y automatically provides Y-sorting without Linq.OrderBy overhead.
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                var coord = (x, y);
                if (!_featureSnapshot.TryGetValue(coord, out byte feature) || feature == 0) continue;

                float wx = x * chunkSize;
                float wy = y * chunkSize;
                FeatureType type = (FeatureType)feature;
                string texName = type switch {
                    FeatureType.LargeTree => "large_tree",
                    FeatureType.SmallTree => "small_tree",
                    FeatureType.MeadowHedge => "meadow_hedge",
                    FeatureType.MeadowFlowers => "meadow_flowers",
                    FeatureType.Stone => "stone",
                    FeatureType.PalmTree => "palm_tree",
                    FeatureType.DesertLog => "desert_log",
                    FeatureType.Tumbleweed => "tumbleweed",
                    FeatureType.OasisDesert => "oasis_desert",
                    FeatureType.BeachUmbrella => "beach_umbrella",
                    FeatureType.Sailboat => "sailboat",
                    FeatureType.SulfurSpring => "sulfur_spring",
                    _ => ""
                };

                if (string.IsNullOrEmpty(texName)) continue;
                var tex = AssetManager.GetTexture(texName);
                if (tex.Id == 0) continue;

                bool isSmall = (type == FeatureType.MeadowHedge || type == FeatureType.MeadowFlowers || 
                                type == FeatureType.Stone || type == FeatureType.DesertLog || 
                                type == FeatureType.Tumbleweed || type == FeatureType.BeachUmbrella);

                if (isSmall)
                {
                    float scale = (type == FeatureType.MeadowFlowers ? 0.35f : 0.5f) * 2.0f;
                    Raylib.DrawTexturePro(tex, new Rectangle(0, 0, tex.Width, tex.Height), 
                        new Rectangle(wx + 8, wy + 8, tex.Width * scale, tex.Height * scale), 
                        new Vector2((tex.Width * scale) / 2f, tex.Height * scale), 0f, Color.White);
                }
                else
                {
                    float scale = 4.0f;
                    Raylib.DrawTexturePro(tex, new Rectangle(0, 0, tex.Width, tex.Height), 
                        new Rectangle(wx + 8, wy + 16, tex.Width * scale, tex.Height * scale), 
                        new Vector2((tex.Width * scale) / 2f, tex.Height * scale), 0f, Color.White);
                }
            }
        }

        // Draw Shadows (now in world space, correctly positioned)
        DrawShadows();

        // Render Players - Sorted Top to Bottom (Y-Sorting)
        List<Player> playersToDraw;
        lock (OthersLock)
        {
            playersToDraw = Others.Values.ToList();
        }
        playersToDraw.Add(LocalPlayer);
        foreach (var p in playersToDraw.OrderBy(p => p.Position.Y))
        {
            p.Draw();
            Debug.DrawHitbox(p.Position);
        }

        // Draw Visual Bombs
        Texture2D bombTex = AssetManager.GetTexture("brimstalker_bomb");
        lock (_bombsLock)
        {
            foreach (var b in _visualBombs) {
                if (bombTex.Id != 0) {
                    Raylib.DrawTexturePro(bombTex, new Rectangle(0, 0, bombTex.Width, bombTex.Height), 
                        new Rectangle(b.Position.X, b.Position.Y, 32, 32), new Vector2(16, 16), b.Rotation, Color.White);
                }
            }
        }

        // Draw Structures last in the world-space pass to ensure they are on top of everything
        foreach (var structureEntry in Structures)
        {
            var structure = structureEntry.Value;

            if (structure.Type == StructureType.EndPortal)
            {
                Raylib.DrawCircleV(structure.Position, 80, Color.Black); // 5 chunks radius = 80 units
                Raylib.DrawCircleLines((int)structure.Position.X, (int)structure.Position.Y, 80, Color.Magenta);
                continue;
            }

            var tex = AssetManager.GetTexture(structure.TextureName);
            if (tex.Id != 0)
            {
                float scale = 4.0f;
                Vector2 drawPos = new Vector2(
                    structure.Position.X - (tex.Width * scale) / 2f,
                    structure.Position.Y - (tex.Height * scale) / 2f
                );
                Raylib.DrawTextureEx(tex, drawPos, 0f, scale, Color.White);
            }

            // Draw "Right Click To Open" text for completed raids
            if (structure.Type == StructureType.RaidOutpost && structure.IsCompleted && structure.TextFadeAlpha > 0.01f)
            {
                float bounce = MathF.Sin((float)Raylib.GetTime() * 5.0f) * 12.0f;
                int fontSize = 24;
                string text = "Right Click To Open";
                int textWidth = Raylib.MeasureText(text, fontSize);
                Vector2 textPos = new Vector2(structure.Position.X - textWidth / 2f, structure.Position.Y - 100 + bounce);
                Color textColor = new Color(255, 255, 0, (int)(structure.TextFadeAlpha * 255));
                Raylib.DrawText(text, (int)textPos.X, (int)textPos.Y, fontSize, textColor);
            }
        }

        // Draw Visual Gusts (NEW)
        Texture2D gustTex = AssetManager.GetTexture("vortex_gust");
        lock (_gustsLock)
        {
            foreach (var g in _visualGusts) {
                if (gustTex.Id != 0) {
                    // Gusts are smaller, maybe 16x16, and rotate faster
                    Raylib.DrawTexturePro(gustTex, new Rectangle(0, 0, gustTex.Width, gustTex.Height), 
                        new Rectangle(g.Position.X, g.Position.Y, 16, 16), new Vector2(8, 8), g.Rotation, Color.White);
                }
            }
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

        // Draw Ember Particles
        foreach (var p in _emberParticles)
        {
            float t = p.Life / p.MaxLife;
            Color col = p.ParticleColor;
            col.A = (byte)(t * 255);
            
            // Draw as small glowing squares
            Raylib.DrawRectanglePro(new Rectangle(p.Position.X, p.Position.Y, p.Size, p.Size), new Vector2(p.Size/2f, p.Size/2f), p.Rotation, col);
        }

        // Draw Cloud Particles as blocky clusters for a pixelated look
        foreach (var p in _cloudParticles)
        {
            float finalAlpha = (p.MaxLife > 1.0f) ? p.Alpha * 0.07f : p.Alpha * 0.6f;
            Color cloudColor = new Color((byte)255, (byte)255, (byte)255, (byte)(finalAlpha * 255));
            
            // Snap everything to a 3x3 virtual pixel grid for much more detail
            float vPix = 3.0f;
            Vector2 snapPos = new Vector2(MathF.Round(p.Position.X / vPix) * vPix, MathF.Round(p.Position.Y / vPix) * vPix);
            float s = MathF.Round((p.Size * 0.5f) / vPix) * vPix;

            // Draw a denser cluster of 7 blocks for a "detailed" pixel cloud
            Raylib.DrawRectangleV(snapPos - new Vector2(s * 0.5f, s * 0.5f), new Vector2(s, s), cloudColor); // Center
            
            // Offset sub-blocks for detail
            Raylib.DrawRectangleV(snapPos + new Vector2(s * 0.4f, -s * 0.3f), new Vector2(s * 0.7f, s * 0.7f), cloudColor);
            Raylib.DrawRectangleV(snapPos + new Vector2(-s * 0.8f, s * 0.1f), new Vector2(s * 0.6f, s * 0.6f), cloudColor);
            Raylib.DrawRectangleV(snapPos + new Vector2(-s * 0.2f, -s * 0.9f), new Vector2(s * 0.5f, s * 0.5f), cloudColor);
            Raylib.DrawRectangleV(snapPos + new Vector2(s * 0.1f, s * 0.5f), new Vector2(s * 0.8f, s * 0.4f), cloudColor);
            
            // Tiny detail bits
            Raylib.DrawRectangleV(snapPos + new Vector2(s * 0.9f, s * 0.2f), new Vector2(vPix, vPix), cloudColor);
            Raylib.DrawRectangleV(snapPos + new Vector2(-s * 0.7f, -s * 0.7f), new Vector2(vPix, vPix), cloudColor);
        }

        Cam.End();
        Raylib.EndTextureMode();

        // 3. Lighting Pass (Scene -> Lighting Buffer)
        UpdateLightingUniforms();
        Raylib.BeginTextureMode(_lightingTarget);
            Raylib.BeginShaderMode(_lightShader);
                Raylib.DrawTextureRec(_sceneTarget.Texture, new Rectangle(0, 0, _sceneTarget.Texture.Width, -_sceneTarget.Texture.Height), Vector2.Zero, Color.White);
            Raylib.EndShaderMode();
        Raylib.EndTextureMode();

        // 4. Post Processing Pass (Lighting Buffer -> Screen)
        Raylib.BeginShaderMode(_postShader);
        Raylib.SetShaderValue(_postShader, Raylib.GetShaderLocation(_postShader, "saturation"), Env.Saturation, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_postShader, Raylib.GetShaderLocation(_postShader, "contrast"), Env.Contrast, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_postShader, Raylib.GetShaderLocation(_postShader, "fogDensity"), Env.FogDensity, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_postShader, Raylib.GetShaderLocation(_postShader, "dustDensity"), Env.DustDensity * _dustStormAlpha, ShaderUniformDataType.Float);

        // Pass missing color uniforms for fog and dust
        Vector4 fogCol = new Vector4(Env.FogColor.R / 255f, Env.FogColor.G / 255f, Env.FogColor.B / 255f, 1f);
        Vector4 dustCol = new Vector4(Env.DustColor.R / 255f, Env.DustColor.G / 255f, Env.DustColor.B / 255f, 1f);
        Raylib.SetShaderValue(_postShader, Raylib.GetShaderLocation(_postShader, "fogColor"), fogCol, ShaderUniformDataType.Vec4);
        Raylib.SetShaderValue(_postShader, Raylib.GetShaderLocation(_postShader, "dustColor"), dustCol, ShaderUniformDataType.Vec4);

        // Calculate and pass vignette intensity based on health and raid status
        float healthPercent = CurrentHealth / (float)MaxHealth;
        float vignetteIntensity = MathF.Pow(1.0f - healthPercent, 2.0f) + (RaidActive ? 0.15f : 0.0f) + Env.NightVignette;
        Raylib.SetShaderValue(_postShader, Raylib.GetShaderLocation(_postShader, "vignetteIntensity"), vignetteIntensity, ShaderUniformDataType.Float);

        // Draw the lit world to the screen
        Raylib.DrawTextureRec(_lightingTarget.Texture, new Rectangle(0, 0, _lightingTarget.Texture.Width, -_lightingTarget.Texture.Height), Vector2.Zero, Color.White);
        Raylib.EndShaderMode();

        // UI Overlay Pass (Draw after Cam.End to be in true Screen Space)
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

        lock (OthersLock)
        {
            foreach (var other in Others.Values)
            {
                other.DrawOverheadHearts(other.Position + new Vector2(32, 32), other.Health, other.MaxHealth);
            }
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

        if (Program.SpeedrunTimerEnabled)
        {
            Color timeColor = _speedrunFinished ? Color.Green : Color.Yellow;
            TimeSpan ts = TimeSpan.FromSeconds(_speedrunTime);
            string timeStr = $"TIME: {ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
            
            int sw = Raylib.GetScreenWidth();
            int textWidth = Raylib.MeasureText(timeStr, 25);
            Raylib.DrawText(timeStr, sw - textWidth - 20, 140, 25, timeColor);
        }


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
            
            // Use the thread-safe 'playersToDraw' list we captured earlier in the Draw method
            bool isApexActive = playersToDraw.Any(o => o.Name == "APEX");
            bool isBrimstalkerActive = playersToDraw.Any(o => o.Name == "Brimstalker");
            string raidTitle = isApexActive ? "APEX" : (isBrimstalkerActive ? "BRIMSTALKER" : (RaidActive ? "RAID ENCOUNTER" : "RAID APPROACHING..."));
            int tw = Raylib.MeasureText(raidTitle, 22);
            Raylib.DrawText(raidTitle, sw / 2 - tw / 2, y - 28, 22, new Color(255, 200, 0, 255));
        }

        // UI Visual for Cooldown (Optional, helps testing)
        DrawCooldownUI();

        DrawAdvancementPopups();

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

        // Draw Damage Flash Overlay
        if (_damageFlashTimer > 0)
        {
            float flashAlpha = (_damageFlashTimer / 0.2f) * 0.4f; // Max 40% alpha
            Raylib.DrawRectangle(0, 0, Raylib.GetScreenWidth(), Raylib.GetScreenHeight(), new Color(255, 0, 0, (int)(flashAlpha * 255)));
        }

        InvMenu.Draw();

        // Draw Dynamic Crosshair
        if (!InvMenu.Visible && !Program.IsPaused && !_isChatting)
        {
            Vector2 mousePos = Raylib.GetMousePosition();
            Vector2 worldMouse = Raylib.GetScreenToWorld2D(mousePos, Cam.RaylibCamera);
            Vector2 playerCenterPos = new Vector2(LocalPlayer.Position.X + 32, LocalPlayer.Position.Y + 32);
            float distToMouse = Vector2.Distance(playerCenterPos, worldMouse);

            string heldId = PlayerInventory.Slots[_selectedHotbarIndex].ItemID;
            var (_, _, currentRange) = WeaponStats.Calculate(heldId, _cAttackTimer, _cHitTimer);
            Color crossColor = (distToMouse <= currentRange && heldId != "none" && !string.IsNullOrEmpty(heldId) && currentRange > 0) ? Color.Green : Color.Red;

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

        // Atmosphere Pass: Apply the global gradient over the world but under the UI
        DrawAtmosphericGradient();

        // Draw Shooting Stars (Night only)
        if (Env.CurrentTime > 180f) DrawShootingStars();

        // Draw God Rays over the atmosphere
        DrawGodRays();

        // Draw Weather Overlays (Rain, Dust, Ash)
        DrawWeatherOverlays();

        // Render tooltips last so they are on top of everything
        HotbarUI.RenderTooltip();

        // --- Cinematic End Sequence Overlay ---
        if (_endSequenceTimer >= 4.0f)
        {
            int sw = Raylib.GetScreenWidth();
            int sh = Raylib.GetScreenHeight();
            Raylib.DrawRectangle(0, 0, sw, sh, Color.Black);

            // If we're teleporting via pearl, don't show the ending credits
            if (_brimstonePearlSlot != -1) return;

            if (_endSequenceTimer >= 5.0f)
            {
                string t1 = "BULLETBOX";
                int fs1 = 80;
                int w1 = Raylib.MeasureText(t1, fs1);
                Raylib.DrawText(t1, sw / 2 - w1 / 2, sh / 2 - 120, fs1, Color.Yellow);
            }

            if (_endSequenceTimer >= 6.0f)
            {
                string t2 = "THE END";
                int fs2 = 40;
                int w2 = Raylib.MeasureText(t2, fs2);
                Raylib.DrawText(t2, sw / 2 - w2 / 2, sh / 2 - 20, fs2, Color.White);
            }

            if (_endSequenceTimer >= 8.0f)
            {
                float scrollT = _endSequenceTimer - 8.0f;
                float scrollY = sh - (scrollT * 60f); // 60 pixels per second
                
                int fsC = 25;
                string[] credits = new string[] {
                    "Lead developer:",
                    "Leonard J. Lamparter",
                    "(LeonardoDCapitan)",
                    "",
                    "Soundtrack Designer:",
                    ".Winter",
                    "",
                    "",
                    "THE END."
                };

                for (int i = 0; i < credits.Length; i++)
                {
                    int cw = Raylib.MeasureText(credits[i], fsC);
                    Raylib.DrawText(credits[i], sw / 2 - cw / 2, (int)(scrollY + i * 35), fsC, Color.White);
                }
            }
        }

        DrawNightVignetteOverlay(); // Draw night vignette absolutely last
    }

    private void DrawAdvancementPopups()
    {
        int sw = Raylib.GetScreenWidth();
        foreach (var p in _activePopups)
        {
            int boxW = 360;
            int boxH = 60;
            int x = sw / 2 - boxW / 2;
            int y = (int)p.SlideY;

            // Background
            Rectangle rec = new Rectangle(x, y, boxW, boxH);
            Raylib.DrawRectangleRounded(rec, 0.3f, 8, new Color(0, 0, 0, 220));
            Raylib.DrawRectangleRoundedLines(rec, 0.3f, 8, Color.RayWhite);

            // Header Text
            if (p.HeaderAlpha > 0)
            {
                string header = "ADVANCEMENT COMPLETE!";
                int hw = Raylib.MeasureText(header, 20);
                Raylib.DrawText(header, sw / 2 - hw / 2, y + 20, 20, new Color(255, 215, 0, (int)(p.HeaderAlpha * 255)));
            }

            // Advancement Title Text
            if (p.TitleAlpha > 0)
            {
                int tw = Raylib.MeasureText(p.Title, 22);
                Raylib.DrawText(p.Title, sw / 2 - tw / 2, y + 19, 22, new Color(255, 255, 255, (int)(p.TitleAlpha * 255)));
            }
        }
    }

    private void DrawAtmosphericGradient()
    {
        float sw = Raylib.GetScreenWidth();
        float sh = Raylib.GetScreenHeight();

        // Dynamic Intensity based on the day-night cycle
        float intensity = Env.SunIntensity;
        if (intensity <= 0.01f) return;

        // Origin fixed to top-right to shine down to bottom-left as requested
        Vector2 sunOrigin = new Vector2(sw + 200, -200);

        // Yellow-White Gradient: Pure white core fading into a warm yellow transparent edge
        Color innerColor = new Color((byte)255, (byte)255, (byte)255, (byte)(intensity * 120)); // Reduced alpha multiplier
        Color outerColor = new Color((byte)255, (byte)230, (byte)120, (byte)(intensity * 180)); // Reduced alpha multiplier

        Raylib.BeginBlendMode(BlendMode.Additive);
        // Using sw * 2.5f provides a massive, ultra-smooth falloff that remains visible as a gradient
        Raylib.DrawCircleGradient((int)sunOrigin.X, (int)sunOrigin.Y, sw * 2.5f, innerColor, outerColor);
        Raylib.EndBlendMode();
    }

    private void DrawGodRays()
    {
        int sw = Raylib.GetScreenWidth();
        int sh = Raylib.GetScreenHeight();
        float time = (float)Raylib.GetTime();

        Raylib.BeginBlendMode(BlendMode.Additive);
        
        if (Env.GodRayIntensity > 0.01f)
        {
            // Add a gentle sway to the origin and angles
            Vector2 origin = new Vector2(sw + 100 + MathF.Sin(time * 0.5f) * 20, -100 + MathF.Cos(time * 0.5f) * 10);
            float globalDrift = MathF.Sin(time * 0.3f) * 2.0f; 
            Color rayColor = new Color(255, 220, 100, (int)(Env.GodRayIntensity * 35));
            
            for (int i = 0; i < 7; i++)
            {
                float angleStart = (130f + i * 16f + globalDrift) * (MathF.PI / 180f);
                float angleEnd = angleStart + (8f * (MathF.PI / 180f));
                float length = sw * 2.0f;
                Vector2 p2 = origin + new Vector2(MathF.Cos(angleStart) * length, MathF.Sin(angleStart) * length);
                Vector2 p3 = origin + new Vector2(MathF.Cos(angleEnd) * length, MathF.Sin(angleEnd) * length);
                Raylib.DrawTriangle(origin, p3, p2, rayColor);
            }
        }

        // Draw "Shining Spots" (Dust Motes) - Always visible, but base intensity is 0.1f
        for (int i = 0; i < _moteParticles.Count; i++)
        {
            Vector2 p = _moteParticles[i];
            float sparkle = MathF.Sin(time * 4f + i) * 0.5f + 0.5f;
            // Increased base visibility to 0.25f so they show up better during normal daytime
            int alpha = (int)((0.25f + Env.GodRayIntensity) * sparkle * 180);
            if (alpha > 5)
                Raylib.DrawCircleV(new Vector2(p.X % sw, p.Y % sh), 2, new Color(255, 240, 150, alpha));
        }
        
        Raylib.EndBlendMode();
    }

    private void DrawShootingStars()
    {
        Raylib.BeginBlendMode(BlendMode.Additive);
        foreach (var s in _shootingStars)
        {
            Color col = new Color(200, 230, 255, (int)(s.Life * 255));
            // Draw a trailing line
            Raylib.DrawLineEx(s.Position, s.Position - (s.Velocity * 0.05f), 2.5f, col);
            // Draw a tiny bright head
            Raylib.DrawCircleV(s.Position, 2, new Color(255, 255, 255, (int)(s.Life * 255)));
        }
        Raylib.EndBlendMode();
    }

    private void DrawNightVignetteOverlay()
    {
        float nightVignetteAmount = Env.NightVignette;
        if (nightVignetteAmount <= 0.01f) return; // Only draw if there's a noticeable effect

        int sw = Raylib.GetScreenWidth();
        int sh = Raylib.GetScreenHeight();

        // The vignette should be black, fading from transparent in the center to opaque at the edges
        // The alpha of the outer color is controlled by Env.NightVignette
        Color innerColor = new Color(0, 0, 0, 0); // Fully transparent black in the center
        Color outerColor = new Color(0, 0, 0, (int)(nightVignetteAmount * 255)); // Black with dynamic alpha at the edges

        Raylib.DrawCircleGradient(sw / 2, sh / 2, MathF.Max(sw, sh) * 0.7f, innerColor, outerColor);
    }
    
    private void UpdateLightingUniforms()
    {
        // Convert byte-based Color to float-based Vector4 for the shader
        Vector4 skyTintVec = new Vector4(Env.SkyTint.R / 255f, Env.SkyTint.G / 255f, Env.SkyTint.B / 255f, Env.SkyTint.A / 255f);
        Raylib.SetShaderValue(_lightShader, Raylib.GetShaderLocation(_lightShader, "skyTint"), skyTintVec, ShaderUniformDataType.Vec4);
        Raylib.SetShaderValue(_lightShader, Raylib.GetShaderLocation(_lightShader, "exposure"), Env.Exposure, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_lightShader, Raylib.GetShaderLocation(_lightShader, "sunDirection"), Env.ShadowDirection, ShaderUniformDataType.Vec2);
        
        Raylib.SetShaderValue(_lightShader, Raylib.GetShaderLocation(_lightShader, "screenResolution"), 
            new Vector2(Raylib.GetScreenWidth(), Raylib.GetScreenHeight()), ShaderUniformDataType.Vec2);

        // Collect and send Point Lights (e.g., Local Player and Others).
        // The point lights are separate from the global atmospheric gradient.
        int lightCount = 0;
        Vector2 playerScreenPos = Raylib.GetWorldToScreen2D(LocalPlayer.Position + new Vector2(32, 32), Cam.RaylibCamera);
        
        // Pass Local Player as a light source
        SetShaderLight(0, playerScreenPos, new Color(255, 200, 150, 255), 300f, 1.2f);
        lightCount++;

        // Pass other players or entities.
        // Limiting to 31 others + 1 local player for a total of 32 lights,
        // which is a common shader uniform array size limit.
        lock (OthersLock)
        {
            foreach (var other in Others.Values.Take(31))
            {
                Vector2 otherScreenPos = Raylib.GetWorldToScreen2D(other.Position + new Vector2(32, 32), Cam.RaylibCamera);
                SetShaderLight(lightCount, otherScreenPos, Color.White, 200f, 0.8f);
                lightCount++;
            }
        }

        Raylib.SetShaderValue(_lightShader, Raylib.GetShaderLocation(_lightShader, "lightCount"), lightCount, ShaderUniformDataType.Int);
    }

    private void SetShaderLight(int index, Vector2 pos, Color col, float radius, float intensity)
    {
        string baseName = $"lights[{index}]";
        Raylib.SetShaderValue(_lightShader, Raylib.GetShaderLocation(_lightShader, baseName + ".position"), pos, ShaderUniformDataType.Vec2);
        Vector4 colorVec = new Vector4(col.R / 255f, col.G / 255f, col.B / 255f, col.A / 255f);
        Raylib.SetShaderValue(_lightShader, Raylib.GetShaderLocation(_lightShader, baseName + ".color"), colorVec, ShaderUniformDataType.Vec4);
        Raylib.SetShaderValue(_lightShader, Raylib.GetShaderLocation(_lightShader, baseName + ".radius"), radius, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_lightShader, Raylib.GetShaderLocation(_lightShader, baseName + ".intensity"), intensity, ShaderUniformDataType.Float);
    }

    private void DrawWeatherOverlays()
    {
        float rainInt = Env.GetWeatherIntensity(WeatherType.Rain);
        int sw = Raylib.GetScreenWidth();
        int sh = Raylib.GetScreenHeight();
        
        if (rainInt > 0.1f)
        {
            foreach (var p in _rainParticles)
            {
                Vector2 screenP = new Vector2(p.X % sw, p.Y % sh);
                Raylib.DrawLineV(screenP, screenP + new Vector2(2, 15), new Color(150, 180, 255, (int)(rainInt * 200)));
            }
        }
        float dustInt = Env.GetWeatherIntensity(WeatherType.DustStorm) * _dustStormAlpha;
        if (dustInt > 0.1f)
        {
            foreach (var p in _dustParticles)
            {
                int dx = (int)(p.X % sw);
                int dy = (int)(p.Y % sh);
                // Replaced large blobs with grainy tiny rectangles
                int dSize = (dx + dy) % 3 + 1; 
                Raylib.DrawRectangle(dx, dy, dSize, dSize, new Color(165, 135, 75, (int)(dustInt * 170)));
            }
        }

        // Draw Ash Fall particles
        if (_ashFallAlpha > 0.01f)
        {
            for (int i = 0; i < _ashFallParticles.Count; i++)
            {
                Vector2 p = _ashFallParticles[i];
                int ax = (int)p.X;
                int ay = (int)p.Y;

                // Deterministic variance based on index rather than screen position.
                // This prevents flakes from changing size as they move, which was making overlaps look "glitchy".
                int aSize = (i % 4) + 1;
                int gray = 100 + (i % 50); 
                
                Raylib.DrawRectangle(ax, ay, aSize, aSize, new Color(gray, gray, gray, (int)(_ashFallAlpha * 150))); 
            }
        }
    }

    private void DrawChat()
    {
        int sh = Raylib.GetScreenHeight();
        float currentTime = (float)Raylib.GetTime();
        int fontSize = 20;
        int spacing = 22;
        int anchorY = sh - 80; // The Y-position for the most recent message

        int displayedCount = 0; // Track how many messages are drawn
        lock (_chatLock)
        {
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
        }

        if (_isChatting)
        {
            Raylib.DrawRectangle(10, sh - 45, 500, 35, new Color(0, 0, 0, 180));
            Raylib.DrawText("> " + _chatInput + "_", 20, sh - 38, 20, Color.Yellow);
        }
    }

    private void DrawPlayerList()
    {
        List<Player> players;
        lock (OthersLock)
        {
            players = Others.Values.ToList();
        }
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

    private Color GetBiomeBaseColor(byte biome, int cx = 0, int cy = 0)
    {
        if (biome < _biomeColors.Length)
        {
            Color baseCol = _biomeColors[biome];
            float noise = (Perlin.Noise(cx * 0.20f, cy * 0.20f) + 1f) * 0.5f;

            if (biome == 7) // River: shimmer effect
            {
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
            if (biome == 8) // Ashen Wastelands
            {
                if (noise < 0.5f)
                {
                    float t = noise * 2.0f;
                    return new Color(
                        (int)(34 + (51 - 34) * t),
                        (int)(14 + (15 - 14) * t),
                        (int)(14 + (15 - 14) * t),
                        255);
                }
                float t2 = (noise - 0.5f) * 2.0f;
                return new Color(
                    (int)(51 + (53 - 51) * t2),
                    (int)(15 + (43 - 15) * t2),
                    (int)(15 + (43 - 15) * t2),
                    255);
            }
            if (biome == 9) // Lava Pool
            {
                if (noise < 0.5f)
                {
                    float t = noise * 2.0f;
                    return new Color(
                        (int)(146 + (202 - 146) * t),
                        (int)(18 + (28 - 18) * t),
                        (int)(18 + (28 - 18) * t),
                        255);
                }
                float t2 = (noise - 0.5f) * 2.0f;
                return new Color(
                    (int)(202 + (223 - 202) * t2),
                    (int)(28 + (139 - 28) * t2),
                    (int)(28 + (28 - 28) * t2),
                    255);
            }
            if (biome == (byte)BiomeType.TheEnd) // The End: Dark gray mixed with lighter gray
            {
                if (noise < 0.5f)
                {
                    float t = noise * 2.0f;
                    return new Color(
                        (int)(40 + (60 - 40) * t),
                        (int)(40 + (60 - 40) * t),
                        (int)(40 + (60 - 40) * t),
                        255);
                }
                float t2 = (noise - 0.5f) * 2.0f;
                return new Color(
                    (int)(60 + (80 - 60) * t2),
                    (int)(60 + (80 - 60) * t2),
                    (int)(60 + (80 - 60) * t2),
                    255);
            }
            if (biome == (byte)11) // Void
            {
                return Color.Black;
            }

            // Standard biome noise variation
            switch ((BiomeType)biome)
            {
                case BiomeType.Meadow:
                    return new Color((int)(145 + (95 - 145) * noise), (int)(205 + (155 - 205) * noise), (int)(135 + (85 - 135) * noise), 255);
                case BiomeType.Forest:
                    return new Color((int)(50 + (10 - 50) * noise), (int)(115 + (75 - 115) * noise), (int)(65 + (25 - 65) * noise), 255);
                case BiomeType.Desert:
                    return new Color((int)(230 + (170 - 230) * noise), (int)(205 + (145 - 205) * noise), (int)(140 + (80 - 140) * noise), 255);
                case BiomeType.StonyPeaks:
                    return new Color((int)(140 + (90 - 140) * noise), (int)(145 + (95 - 145) * noise), (int)(155 + (105 - 155) * noise), 255);
                case BiomeType.Ocean:
                    return new Color((int)(45 + (10 - 45) * noise), (int)(80 + (45 - 80) * noise), (int)(145 + (110 - 145) * noise), 255);
                case BiomeType.Beach:
                    return new Color((int)(240 + (190 - 240) * noise), (int)(220 + (170 - 220) * noise), (int)(180 + (130 - 180) * noise), 255);
                case BiomeType.BrimstoneSprings:
                    return new Color((int)(210 + (255 - 210) * noise), (int)(95 + (145 - 95) * noise), (int)(60 + (110 - 60) * noise), 255);
                default:
                    return baseCol;
            }
        }
        return Color.Gray;
    }

    private void DrawCooldownUI()
    {
        if (InvMenu.Visible || Program.IsPaused || _isChatting) return;

        string heldIdStr = PlayerInventory.Slots[_selectedHotbarIndex].ItemID;
        string heldId = PlayerInventory.Slots[_selectedHotbarIndex].ItemID;
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
}