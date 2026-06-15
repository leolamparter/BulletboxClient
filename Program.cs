﻿﻿﻿﻿﻿﻿﻿using Raylib_cs;
using System.Numerics;
using System;
using System.IO;
using BulletboxClient; // Ensure this is present for WorldData
using DiscordRPC;

public enum GameState { SPLASH, HOME, LOGIN, SERVER_SELECTOR, PLAYING, OPTIONS, SINGLEPLAYER_CONNECTING, ADD_ONS, DISCONNECTED, DEATH, ADVANCEMENTS, WORLD_SELECTION, CREATE_WORLD, VERSION_WARNING, SKIN_SELECTOR }

class Program
{
    public const string VERSION = "Bulletbox 26.1 Pre-Release 5";
    public static GameState CurrentState = GameState.SPLASH;
    public static UserData CurrentUser = new UserData();
    public static string SelectedSkin 
    {
        get => CurrentUser.SelectedSkin;
        set => CurrentUser.SelectedSkin = value;
    }
    public static WorldData? CurrentWorldData; // To store the currently loaded world's data
    
    public static Connection Net = new Connection();
    public static Playing? PlayingState; // Made nullable to resolve CS8618
    public static SplashScreen? splashScreen;
    
    public static bool IsEnding = false;
    // NEW: Pause State
    public static string LastIP = "127.0.0.1";
    public static bool SpeedrunTimerEnabled = false;
    public static bool MusicEnabled = true;
    public static float MusicVolume = 0.7f;
    public static float SfxVolume = 0.8f;
    public static int LastPort = 32308;
    public static bool IsPaused = false;
    private static float _lastAttempt = 0;

    private static Random _musicRng = new Random();

    public static void RedeemSkin(string code)
    {
        var user = CurrentUser;
        if (code == "ATTHSYTOG")
        {
            if (!user.UnlockedSkins.Contains("Apex Master"))
            {
                user.UnlockedSkins.Add("Apex Master");
                SaveManager.Save(CurrentUser);
            }
        }
    }

    // Global Music Management
    private static string _currentMusicKey = "";
    private static float _musicTimer = 0f;
    private static int _currentCalmTrack = _musicRng.Next(1, 7); // Initialize with a random track

    public static void TriggerSplash(GameState next, Action? loadingAction = null)
    {
        CurrentUser = SaveManager.Load();
        Settings.FOV = CurrentUser.FOV;
        splashScreen?.Reset(next, loadingAction);
        CurrentState = GameState.SPLASH;
    }

    public static PauseMenu? pauseMenu;
    public static GameState cameFrom = GameState.HOME;
    public static bool ShowVersionWarning = false;

    static void Main()
    {
        // Use ProcessPath to find the real location of the binary on disk.
        string? searchPath = Path.GetDirectoryName(Environment.ProcessPath);
        string? initialSearchPath = searchPath;
        string? finalWorkingDir = null;
        
        Console.WriteLine($"[Core] Initial ProcessPath Dir: {initialSearchPath}");

        // Walk up the directory tree to find the correct base for resources
        while (searchPath != null)
        {
            Console.WriteLine($"[Core] Checking path: {searchPath}");

            // Check for 'resources' directly in the current path (common for dev builds or non-macOS)
            string directResourcesPath = Path.Combine(searchPath, "resources");
            if (Directory.Exists(directResourcesPath))
            {
                finalWorkingDir = searchPath;
                Console.WriteLine($"[Core] Found 'resources' directly in: {finalWorkingDir}");
                break;
            }

            // macOS Bundle Check: Contents/Resources/resources
            // If currentSearchPath is Contents/MacOS, parentDir is Contents
            string? parentDir = Path.GetDirectoryName(searchPath);
            if (parentDir != null)
            {
                string macResourcesFolder = Path.Combine(parentDir, "Resources"); // This would be Contents/Resources
                string macBundleResourcesPath = Path.Combine(macResourcesFolder, "resources"); // This would be Contents/Resources/resources
                Console.WriteLine($"[Core] Checking macOS bundle path: {macBundleResourcesPath}");
                if (Directory.Exists(macBundleResourcesPath))
                {
                    finalWorkingDir = macResourcesFolder; // Set working dir to Contents/Resources
                    Console.WriteLine($"[Core] Found 'resources' in macOS bundle structure: {finalWorkingDir}");
                    break;
                }
            }
            searchPath = parentDir!; // Move up one level
        }

        if (finalWorkingDir != null)
        {
            Directory.SetCurrentDirectory(finalWorkingDir);
            Console.WriteLine($"[Core] Working Directory set to: {Directory.GetCurrentDirectory()}");
        }
        else
        {
            Console.WriteLine("[Core] CRITICAL ERROR: Could not find 'resources' folder!");
        }

        // --- Raylib Initialization ---
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
        Raylib.InitWindow(800, 480, "Bulletbox");
        Raylib.InitAudioDevice();
        Raylib.SetTargetFPS(60);

        // MANDATORY: Stops ESC from instantly killing the app
        Raylib.SetExitKey(KeyboardKey.Null);

        CurrentUser = SaveManager.Load();
        Settings.FOV = CurrentUser.FOV;

        // --- UI Screen Initialization ---
        HomeScreen homeScreen = new HomeScreen();
        LoginScreen loginScreen = new LoginScreen();
        pauseMenu = new PauseMenu(); // Initialize the menu
        splashScreen = new SplashScreen();
        AddOnsScreen addOnsScreen = new AddOnsScreen();
        AdvancementsScreen advancementsScreen = new AdvancementsScreen();
        OptionsScreen optionsScreen = new OptionsScreen();
        DisconnectedScreen disconnectedScreen = new DisconnectedScreen();
        DeathScreen deathScreen = new DeathScreen();
        WorldSelectionScreen worldSelectionScreen = new WorldSelectionScreen();
        CreateWorldScreen createWorldScreen = new CreateWorldScreen();
        SkinSelectorScreen skinSelectorScreen = new SkinSelectorScreen();
        _skinBtnNav = new UIButton("CHANGE SKIN", Vector2.Zero, 25);

        // Load Background Soundtracks early for UI support
        for (int i = 1; i <= 6; i++) AudioManager.LoadSound($"calm_{i}", $"resources/soundtracks/calm/{i}.mp3");
        AudioManager.LoadSound("intense_1", "resources/soundtracks/intense/1.mp3");
        AudioManager.LoadSound("end_animation", "resources/soundtracks/end_animation.mp3");

        // Redirect to Login if the user hasn't logged in before, otherwise go to Home.
        TriggerSplash(CurrentUser.HasLoggedIn ? GameState.HOME : GameState.LOGIN);

        // Initialize
        var client = new DiscordRpcClient("1507766634889347295");
        client.Initialize();

        // Set static presence
        client.SetPresence(new RichPresence()
        {
            Details = "Playing Bulletbox",
            State = "In A World"
        });


        bool windowResizedThisFrame = false;
        while (!Raylib.WindowShouldClose()) // Main Game Loop
        {
            // Call this inside your main update/tick loop (e.g., in Raylib)
            client.Invoke();
            // Toggle Pause with ESC only when in-game
            if (Raylib.IsKeyPressed(KeyboardKey.Escape)) 
            {
                if (CurrentState == GameState.PLAYING) 
                {
                    IsPaused = !IsPaused;
                    if (IsPaused && PlayingState != null)
                    {
                        PlayingState.InvMenu.Visible = false;
                        PlayingState.InvMenu.ChestVisible = false;
                    }
                }
            }

            windowResizedThisFrame = Raylib.IsWindowResized();

            // --- UPDATE ---
            GameState stateBeforeUpdate = CurrentState;
            switch (CurrentState)
            {
                case GameState.SPLASH:
                    splashScreen.Update(windowResizedThisFrame);
                    break;
                case GameState.HOME:
                    homeScreen.Update(windowResizedThisFrame);
                    break;
                case GameState.SINGLEPLAYER_CONNECTING:
                    if (!ServerProgram.IsRunning) 
                    {
                        ServerProgram.ResetServerState(); // Reset FIRST on main thread
                        _ = ServerProgram.RunServerAsync(); 
                    }
                    homeScreen.Update(windowResizedThisFrame); // Update background
                    if (!ServerProgram.BulletboxWorld.IsLoaded) // Check if world is already loaded
                    {
                        ServerProgram.BulletboxWorld.IsLoaded = ServerProgram.LoadGame(); // Attempt to load game
                        
                        // Version Check: If the world version doesn't match, show the warning screen
                        if (ServerProgram.BulletboxWorld.IsLoaded && CurrentWorldData != null)
                        {
                            if (CurrentWorldData.Version != VERSION && !ShowVersionWarning)
                            {
                                CurrentState = GameState.VERSION_WARNING;
                                break;
                            }
                        }
                    }
                    if (PlayingState == null)
                    {
                        PlayingState = new Playing(string.IsNullOrEmpty(CurrentUser.Username) ? "Player" : CurrentUser.Username);
                    }
                    if (!Net.IsConnected())
                    {
                        // Throttle connection attempts while waiting for the integrated server to start
                        if (Raylib.GetTime() - _lastAttempt > 1.0)
                        {
                            _lastAttempt = (float)Raylib.GetTime();
                            Net.Connect("127.0.0.1", 32308, string.IsNullOrEmpty(CurrentUser.Username) ? "Player" : CurrentUser.Username, "local_auth");
                        }
                    }
                    else
                    {
                        // Stamp current version on singleplayer connection success
                        if (CurrentWorldData != null && LastIP == "127.0.0.1")
                        {
                            CurrentWorldData.Version = VERSION;
                        }
                        CurrentState = GameState.PLAYING;
                    }
                    break;
                case GameState.LOGIN:
                    loginScreen.Update(windowResizedThisFrame);
                    if (CurrentUser.HasLoggedIn) CurrentState = GameState.HOME;
                    break; // Removed redundant CurrentState = GameState.HOME;
                case GameState.PLAYING:
                    // Safety: Ensure PlayingState is initialized regardless of how we entered the state
                    if (PlayingState == null)
                    {
                        PlayingState = new Playing(string.IsNullOrEmpty(CurrentUser.Username) ? "Player" : CurrentUser.Username);
                    }
                    // Only initiate a connection if we aren't already connected (e.g. coming from Home -> Multiplayer)
                    if (!Net.IsConnected())
                    {
                        DisconnectAndLeave(GameState.DISCONNECTED);
                        break;
                    }
                    // Always update playing state so networking/health packets process, even if paused
                    PlayingState?.Update(Raylib.GetFrameTime(), windowResizedThisFrame);

                    if (IsPaused) pauseMenu?.Update(windowResizedThisFrame);
                    // Death Check: Kick on death
                    if (PlayingState != null && PlayingState.CurrentHealth <= 0) 
                    {
                        AudioManager.StopAll();
                        if (LastIP == "127.0.0.1") _ = ServerProgram.SaveGameAsync(); // Save on death for single-player
                        AudioManager.PlaySound("player_death");
                        Program.DisconnectAndLeave(GameState.DEATH);
                    }
                    break;
                case GameState.ADD_ONS:
                    addOnsScreen.Update(windowResizedThisFrame);
                    break;
                case GameState.ADVANCEMENTS:
                    advancementsScreen.Update(windowResizedThisFrame);
                    break;
                case GameState.OPTIONS:
                    optionsScreen.Update(windowResizedThisFrame);
                    if (_skinBtnNav != null)
                    {
                        _skinBtnNav.Position = new Vector2(Raylib.GetScreenWidth() - 150, 40);
                        if (_skinBtnNav.IsClicked()) CurrentState = GameState.SKIN_SELECTOR;
                    }

                    // Save settings if we just moved back to the home or playing screen
                    if (CurrentState != GameState.OPTIONS && CurrentState != GameState.SKIN_SELECTOR)
                    {
                        SaveManager.Save(CurrentUser);
                    }
                    break;
                case GameState.SKIN_SELECTOR:
                    skinSelectorScreen.Update(windowResizedThisFrame);
                    break;
                case GameState.DISCONNECTED:
                    disconnectedScreen.Update(windowResizedThisFrame);
                    break;
                case GameState.DEATH:
                    deathScreen.Update(windowResizedThisFrame);
                    break;
                case GameState.WORLD_SELECTION:
                    worldSelectionScreen.Update(windowResizedThisFrame);
                    break;
                case GameState.CREATE_WORLD:
                    createWorldScreen.Update(windowResizedThisFrame);
                    break;
                case GameState.VERSION_WARNING:
                    // Handle button clicks for the warning screen
                    int sw = Raylib.GetScreenWidth();
                    int sh = Raylib.GetScreenHeight();
                    UIButton backBtn = new UIButton("Back", new Vector2(sw / 2 - 100, sh / 2 + 50), 25);
                    UIButton proceedBtn = new UIButton("I know what I'm doing!", new Vector2(sw / 2 + 100, sh / 2 + 50), 25);

                    if (backBtn.IsClicked())
                    {
                        DisconnectAndLeave(GameState.HOME); // Go back to home screen
                        ShowVersionWarning = false;
                    }
                    if (proceedBtn.IsClicked())
                    {
                        // Update world version to current and proceed
                        if (CurrentWorldData != null)
                        {
                            CurrentWorldData.Version = Program.VERSION;
                            _ = ServerProgram.SaveGameAsync(); // Save the updated version
                        }
                        ShowVersionWarning = true; // Bypass warning for the remainder of this session
                        CurrentState = GameState.SINGLEPLAYER_CONNECTING; // Re-attempt connection, this time without warning
                    }
                    break;
            }

            if (stateBeforeUpdate != CurrentState && CurrentState == GameState.WORLD_SELECTION)
            {
                worldSelectionScreen.RefreshWorldList();
            }

            UpdateGlobalMusic();

            // --- DRAW ---
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.Black);

            switch (CurrentState)
            {
                case GameState.SPLASH:
                    splashScreen.Draw();
                    break;
                case GameState.HOME:
                    homeScreen.Draw(); 
                    break;
                case GameState.SINGLEPLAYER_CONNECTING:
                    HomeScreen.background.Update(windowResizedThisFrame);
                    HomeScreen.background.Draw();
                    string connText = "Connecting to integrated server...";
                    int connWidth = Raylib.MeasureText(connText, 30);
                    Raylib.DrawText(connText, Raylib.GetScreenWidth() / 2 - connWidth / 2, Raylib.GetScreenHeight() / 2, 30, Color.White);
                    break;
                case GameState.LOGIN:
                    HomeScreen.background.Update(windowResizedThisFrame);
                    HomeScreen.background.Draw();
                    loginScreen.Draw();
                    break;
                case GameState.PLAYING:
                    PlayingState?.Draw();
                    if (IsPaused) pauseMenu?.Draw(); 
                    break;
                case GameState.OPTIONS:
                    if (cameFrom == GameState.PLAYING) PlayingState?.Draw();
                    else if (cameFrom == GameState.HOME) HomeScreen.background.Draw();
                    optionsScreen.Draw();
                    _skinBtnNav?.Draw();
                    break;
                case GameState.SKIN_SELECTOR:
                    HomeScreen.background.Draw();
                    skinSelectorScreen.Draw();
                    break;
                case GameState.ADD_ONS:
                    addOnsScreen.Draw();
                    break;
                case GameState.ADVANCEMENTS:
                    if (cameFrom == GameState.PLAYING) PlayingState?.Draw();
                    else HomeScreen.background.Draw();
                    advancementsScreen.Draw();
                    break;
                case GameState.DISCONNECTED:
                    disconnectedScreen.Draw();
                    break;
                case GameState.DEATH:
                    deathScreen.Draw();
                    break;
                case GameState.WORLD_SELECTION:
                    worldSelectionScreen.Draw();
                    break;
                case GameState.CREATE_WORLD:
                    createWorldScreen.Draw();
                    break;
                case GameState.VERSION_WARNING:
                    HomeScreen.background.Draw(); // Draw background
                    int sw = Raylib.GetScreenWidth();
                    int sh = Raylib.GetScreenHeight();
                    Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 180)); // Dark overlay

                    string warningText1 = "This world is saved on a different version of Bulletbox.";
                    string warningText2 = "We recommend making a backup before playing on this world.";
                    int textWidth1 = Raylib.MeasureText(warningText1, 30);
                    int textWidth2 = Raylib.MeasureText(warningText2, 25);

                    Raylib.DrawText(warningText1, sw / 2 - textWidth1 / 2, sh / 2 - 50, 30, Color.Red);
                    Raylib.DrawText(warningText2, sw / 2 - textWidth2 / 2, sh / 2 - 10, 25, Color.Yellow);
                    new UIButton("Back", new Vector2(sw / 2 - 100, sh / 2 + 50), 25).Draw();
                    new UIButton("I know what I'm doing!", new Vector2(sw / 2 + 100, sh / 2 + 50), 25).Draw();
                    break;
            }

            if (CurrentState != GameState.PLAYING && CurrentState != GameState.SPLASH)
            {
                int sh = Raylib.GetScreenHeight();
                Color watermarkColor = new Color(180, 180, 180, 255);
                Raylib.DrawText("Copyright Bulletbox Studios 2026. DO NOT DISTRIBUTE", 10, sh - 30, 18, watermarkColor);
                Raylib.DrawText(VERSION, 10, sh - 55, 18, watermarkColor);
            }
            Raylib.EndDrawing();
        }

        // Call this when the game closes
        ServerProgram.IsRunning = false; // Stop the server tick loop
        if (LastIP == "127.0.0.1" && CurrentWorldData != null) ServerProgram.SaveGameAsync().GetAwaiter().GetResult();
        SaveManager.Save(CurrentUser);
        AudioManager.UnloadAll();
        Raylib.CloseAudioDevice();
        client.Dispose();
        Raylib.CloseWindow();
    }

    private static void UpdateGlobalMusic()
    {
        bool isIntense = false;
        bool isSilent = false;
        string targetMusic = "";
        float volume = 0.20f;
        _musicTimer += Raylib.GetFrameTime();

        // 1. Select Target Track & State
        if (IsEnding || (CurrentState == GameState.PLAYING && Net.CurrentDimension == Dimension.TheEnd))
        {
            targetMusic = "end_animation";
            volume = 0.5f; // Ending music should be clear and dramatic
        }
        else if (CurrentState == GameState.PLAYING && PlayingState != null)
        {
            byte biome = PlayingState.CurrentBiome;
            isIntense = (biome == 8 || biome == 9 || PlayingState.RaidActive || PlayingState.IsBossActive());
        }

        // Determine which soundtrack to play if not in the end sequence
        if (string.IsNullOrEmpty(targetMusic))
        {
            if (isIntense)
            {
                targetMusic = "intense_1";
                volume = 0.25f;
            }
            else if (!isSilent)
            {
                targetMusic = $"calm_{_currentCalmTrack}";
                volume = 0.20f;

                // Auto-cycle to a new random track if the current one finished
                // Give the track at least 2 seconds to initialize/play before allowing an auto-cycle
                if (_currentMusicKey == targetMusic && !AudioManager.IsSoundPlaying(targetMusic) && _musicTimer > 2.0f)
                {
                    // Pick a different track than the one that just finished or failed
                    int nextTrack = _currentCalmTrack;
                    while (nextTrack == _currentCalmTrack)
                    {
                        nextTrack = _musicRng.Next(1, 7);
                    }
                    _currentCalmTrack = nextTrack;
                    targetMusic = $"calm_{_currentCalmTrack}";
                }
            }
        }

        // 3. Handle Track Transitions
        if (_currentMusicKey != targetMusic)
        {
            if (!string.IsNullOrEmpty(_currentMusicKey)) AudioManager.StopSound(_currentMusicKey);
            _currentMusicKey = targetMusic;
            _musicTimer = 0f; // Reset timer whenever we start a new track
            if (!string.IsNullOrEmpty(_currentMusicKey)) AudioManager.PlaySound(_currentMusicKey);
        }

        // 4. Update Playback
        if (!string.IsNullOrEmpty(_currentMusicKey))
        {
            float finalVol = MusicEnabled ? volume * MusicVolume : 0f;
            AudioManager.SetVolume(_currentMusicKey, finalVol);
            // Only force a play if we aren't in the middle of starting the track
            if (finalVol > 0 && !AudioManager.IsSoundPlaying(_currentMusicKey) && _musicTimer > 2.0f) AudioManager.PlaySound(_currentMusicKey);
            else if (finalVol <= 0 && AudioManager.IsSoundPlaying(_currentMusicKey)) AudioManager.StopSound(_currentMusicKey);
        }
    }

    public static int GetRequiredChunkRadius()
    {
        // Match the chunkSize defined in Playing.cs (16 world units)
        const float chunkSize = 16f;
        
        // Calculate visible world area based on zoom
        float visibleWidth = Raylib.GetScreenWidth() / Settings.FOV;
        float visibleHeight = Raylib.GetScreenHeight() / Settings.FOV;
        
        // Calculate how many chunks are needed to reach the edge from the center
        int horizontalRadius = (int)Math.Ceiling((visibleWidth / 2.0f) / chunkSize);
        int verticalRadius = (int)Math.Ceiling((visibleHeight / 2.0f) / chunkSize);
        
        // Return the max radius plus a 1-chunk buffer to prevent "popping" at edges
        return Math.Max(horizontalRadius, verticalRadius) + 1;
    }

    public static void DisconnectAndLeave(GameState targetState = GameState.HOME)
    {
        if (LastIP == "127.0.0.1" && CurrentWorldData != null) 
        {
            // BLOCKING save to ensure data hits SQLite before we wipe the server memory
            ServerProgram.SaveGameAsync().GetAwaiter().GetResult();
            ServerProgram.ResetServerState();
        }
        Net.Disconnect();
        LanDiscovery.StopListening();
        LanDiscovery.StopBroadcasting();
        CurrentWorldData = null; // Clear world metadata so next session is fresh
        ServerProgram.IsRunning = false;
        ShowVersionWarning = false; // Reset warning flag for next world load
        PlayingState?.Cleanup();
        PlayingState = null;   
        IsPaused = false;
        IsEnding = false;
        Raylib.ShowCursor();
        CurrentState = targetState;
    }
    private static UIButton? _skinBtnNav;
}