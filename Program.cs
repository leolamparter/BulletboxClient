﻿﻿﻿﻿﻿﻿﻿﻿﻿using Raylib_cs;
using System.Numerics;
using System;
using System.IO;
using BulletboxClient;
using DiscordRPC;

public enum GameState { HOME, LOGIN, SERVER_SELECTOR, PLAYING, OPTIONS, SINGLEPLAYER_CONNECTING, FRIENDS, DISCONNECTED, DEATH }

class Program
{
    public static GameState CurrentState = GameState.HOME;
    public static UserData CurrentUser = new UserData(); 
    
    public static Connection Net = new Connection();
    public static Playing? PlayingState;
    
    // NEW: Pause State
    public static string LastIP = "127.0.0.1";
    public static int LastPort = 32308;
    public static bool IsPaused = false;
    public static PauseMenu? pauseMenu;
    public static GameState cameFrom = GameState.HOME;

    static void Main()
    {
        // Use ProcessPath to find the real location of the binary on disk.
        string searchPath = Path.GetDirectoryName(Environment.ProcessPath);
        string initialSearchPath = searchPath;
        string finalWorkingDir = null;
        
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
            string parentDir = Path.GetDirectoryName(searchPath);
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
            searchPath = parentDir; // Move up one level
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

        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
        Raylib.InitWindow(800, 480, "Bulletbox");
        Raylib.InitAudioDevice();
        Raylib.SetTargetFPS(60);

        // MANDATORY: Stops ESC from instantly killing the app
        Raylib.SetExitKey(KeyboardKey.Null); 

        CurrentUser = SaveManager.Load();
        Settings.FOV = CurrentUser.FOV;

        HomeScreen homeScreen = new HomeScreen();
        LoginScreen loginScreen = new LoginScreen();
        pauseMenu = new PauseMenu(); // Initialize the menu
        FriendsScreen friendsScreen = new FriendsScreen();
        OptionsScreen optionsScreen = new OptionsScreen();
        DisconnectedScreen disconnectedScreen = new DisconnectedScreen();
        DeathScreen deathScreen = new DeathScreen();

        // Initialize
        var client = new DiscordRpcClient("1507766634889347295");
        client.Initialize();

        // Set static presence
        client.SetPresence(new RichPresence()
        {
            Details = "Playing Bulletbox",
            State = "In A World"
        });


        while (!Raylib.WindowShouldClose())
        {
            // Call this inside your main update/tick loop (e.g., in Raylib)
            client.Invoke();
            // Toggle Pause with ESC only when in-game
            if (Raylib.IsKeyPressed(KeyboardKey.Escape)) 
            {
                if (CurrentState == GameState.PLAYING) IsPaused = !IsPaused;
            }

            // --- UPDATE ---
            switch (CurrentState)
            {
                case GameState.HOME:
                    homeScreen.Update();
                    break;
                case GameState.SINGLEPLAYER_CONNECTING:
                    // 1. Start the integrated server (it checks if it's already running)
                    _ = ServerProgram.RunServerAsync();

                    // 2. Initialize PlayingState NOW so it's ready for packets
                    if (PlayingState == null)
                    {
                        PlayingState = new Playing(string.IsNullOrEmpty(CurrentUser.Username) ? "Player" : CurrentUser.Username);
                    }

                    LastIP = "127.0.0.1";
                    LastPort = 32308;

                    // 2. Ensure a fallback username for local play
                    if (string.IsNullOrEmpty(CurrentUser.Username)) CurrentUser.Username = "Player";

                    // 3. Attempt connection to localhost. Net.Connect handles errors internally.
                    Net.Connect("127.0.0.1", 32308, CurrentUser.Username, "local_auth");

                    if (Net.IsConnected()) CurrentState = GameState.PLAYING;
                    else CurrentState = GameState.DISCONNECTED;
                    break;
                case GameState.LOGIN:
                    loginScreen.Update();
                    if (CurrentUser.HasLoggedIn) CurrentState = GameState.PLAYING;
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
                    PlayingState.Update();

                    if (IsPaused) pauseMenu.Update();

                    // Death Check: Kick on death
                    if (PlayingState != null && PlayingState.CurrentHealth <= 0) Program.DisconnectAndLeave(GameState.DEATH);
                    break;
                case GameState.FRIENDS:
                    friendsScreen.Update();
                    break;
                case GameState.OPTIONS:
                    optionsScreen.Update();
                    // Save settings if we just moved back to the home or playing screen
                    if (CurrentState != GameState.OPTIONS)
                    {
                        SaveManager.Save(CurrentUser);
                    }
                    break;
                case GameState.DISCONNECTED:
                    disconnectedScreen.Update();
                    break;
                case GameState.DEATH:
                    deathScreen.Update();
                    break;
            }

            // --- DRAW ---
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.Black);

            switch (CurrentState)
            {
                case GameState.HOME:
                    homeScreen.Draw();
                    break;
                case GameState.SINGLEPLAYER_CONNECTING:
                    HomeScreen.background.Update();
                    HomeScreen.background.Draw();
                    string connText = "Connecting to integrated server...";
                    int connWidth = Raylib.MeasureText(connText, 30);
                    Raylib.DrawText(connText, Raylib.GetScreenWidth() / 2 - connWidth / 2, Raylib.GetScreenHeight() / 2, 30, Color.White);
                    break;
                case GameState.LOGIN:
                    HomeScreen.background.Update();
                    HomeScreen.background.Draw();
                    loginScreen.Draw();
                    break;
                case GameState.PLAYING:
                    PlayingState?.Draw();
                    if (IsPaused) pauseMenu.Draw(); 
                    break;
                case GameState.OPTIONS:
                    if (cameFrom == GameState.PLAYING) PlayingState?.Draw();
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
            Raylib.EndDrawing();
        }

        // Call this when the game closes
        SaveManager.Save(CurrentUser);
        AudioManager.UnloadAll();
        Raylib.CloseAudioDevice();
        client.Dispose();
        Raylib.CloseWindow();
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