﻿﻿﻿using Raylib_cs;
using System.Numerics;
using System;
using System.IO;
using BulletboxClient;
using DiscordRPC;

public enum GameState { SPLASH, HOME, LOGIN, SERVER_SELECTOR, PLAYING, OPTIONS, SINGLEPLAYER_CONNECTING, FRIENDS, DISCONNECTED, DEATH }

class Program
{
    public const string VERSION = "Bulletbox 26.1.1 Snapshot 03b";
    public static GameState CurrentState = GameState.SPLASH;
    public static UserData CurrentUser = new UserData(); 
    
    public static Connection Net = new Connection();
    public static Playing? PlayingState; // Made nullable to resolve CS8618
    public static SplashScreen? splashScreen;
    
    public static bool IsEnding = false;
    // NEW: Pause State
    public static string LastIP = "127.0.0.1";
    public static int LastPort = 32308;
    public static bool IsPaused = false;
    private static float _lastAttempt = 0;

    private static Random _musicRng = new Random();
    // Global Music Management
    private static string _currentMusicKey = "";
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
        FriendsScreen friendsScreen = new FriendsScreen();
        OptionsScreen optionsScreen = new OptionsScreen();
        DisconnectedScreen disconnectedScreen = new DisconnectedScreen();
        DeathScreen deathScreen = new DeathScreen();

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
                }
            }

            windowResizedThisFrame = Raylib.IsWindowResized();

            // --- UPDATE ---
            switch (CurrentState)
            {
                case GameState.SPLASH:
                    splashScreen.Update(windowResizedThisFrame);
                    break;
                case GameState.HOME:
                    homeScreen.Update(windowResizedThisFrame);
                    break;
                case GameState.SINGLEPLAYER_CONNECTING:
                    if (!ServerProgram.IsRunning) _ = ServerProgram.RunServerAsync(); // This should probably be awaitaed or handled differently for proper server startup
                    homeScreen.Update(windowResizedThisFrame); // Update background

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
                        CurrentState = GameState.PLAYING;
                    }
                    break;
                case GameState.LOGIN:
                    loginScreen.Update(windowResizedThisFrame);
                    if (CurrentUser.HasLoggedIn) CurrentState = GameState.HOME;
                    break;
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
                    // Always update playing state so networking/health packets process
                    PlayingState.Update(Raylib.GetFrameTime(), windowResizedThisFrame);

                    if (IsPaused) pauseMenu.Update(windowResizedThisFrame);
                    // Death Check: Kick on death
                    if (PlayingState != null && PlayingState.CurrentHealth <= 0) 
                    {
                        AudioManager.StopAll();
                        AudioManager.PlaySound("player_death");
                        Program.DisconnectAndLeave(GameState.DEATH);
                    }
                    break;
                case GameState.FRIENDS:
                    friendsScreen.Update(windowResizedThisFrame);
                    break;
                case GameState.OPTIONS:
                    optionsScreen.Update(windowResizedThisFrame);
                    // Save settings if we just moved back to the home or playing screen
                    if (CurrentState != GameState.OPTIONS)
                    {
                        SaveManager.Save(CurrentUser);
                    }
                    break;
                case GameState.DISCONNECTED:
                    disconnectedScreen.Update(windowResizedThisFrame);
                    break;
                case GameState.DEATH:
                    deathScreen.Update(windowResizedThisFrame);
                    break;
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
                    if (IsPaused) pauseMenu.Draw(); 
                    break;
                case GameState.OPTIONS:
                    if (cameFrom == GameState.PLAYING) PlayingState?.Draw();
                    else if (cameFrom == GameState.HOME) HomeScreen.background.Draw();
                    optionsScreen.Draw();
                    break;
                case GameState.FRIENDS:
                    friendsScreen.Draw();
                    break;
                case GameState.DISCONNECTED:
                    disconnectedScreen.Draw();
                    break;
                case GameState.DEATH:
                    deathScreen.Draw();
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

        // 1. Select Target Track & State
        if (IsEnding)
        {
            targetMusic = "end_animation";
            volume = 0.5f; // Ending music should be clear and dramatic
        }
        else if (CurrentState == GameState.PLAYING && PlayingState != null)
        {
            byte biome = PlayingState.CurrentBiome;
            isIntense = (biome == 8 || biome == 9); // Ashen Wastelands or Lava Pools
            isSilent = PlayingState.RaidActive || PlayingState.IsBossActive();
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
                if (_currentMusicKey == targetMusic && !AudioManager.IsSoundPlaying(targetMusic))
                {
                    _currentCalmTrack = _musicRng.Next(1, 7);
                    targetMusic = $"calm_{_currentCalmTrack}";
                }
            }
        }

        // 3. Handle Track Transitions
        if (_currentMusicKey != targetMusic)
        {
            if (!string.IsNullOrEmpty(_currentMusicKey)) AudioManager.StopSound(_currentMusicKey);
            _currentMusicKey = targetMusic;
            if (!string.IsNullOrEmpty(_currentMusicKey)) AudioManager.PlaySound(_currentMusicKey);
        }

        // 4. Update Playback
        if (!string.IsNullOrEmpty(_currentMusicKey))
        {
            AudioManager.SetVolume(_currentMusicKey, volume);
            if (!AudioManager.IsSoundPlaying(_currentMusicKey)) AudioManager.PlaySound(_currentMusicKey);
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
        Net.Disconnect();
        LanDiscovery.StopListening();
        LanDiscovery.StopBroadcasting();
        ServerProgram.IsRunning = false;
        PlayingState = null;   
        IsPaused = false;      
        Raylib.ShowCursor();
        CurrentState = targetState;
    }
}