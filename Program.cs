﻿using Raylib_cs;
using System.Numerics;
using System;
using BulletboxClient;
using DiscordRPC;

public enum GameState { HOME, LOGIN, SERVER_SELECTOR, PLAYING, OPTIONS, SINGLEPLAYER_CONNECTING }

class Program
{
    public static GameState CurrentState = GameState.HOME;
    public static UserData CurrentUser = new UserData(); 
    
    public static Connection Net = new Connection();
    public static Playing? PlayingState;
    
    // NEW: Pause State
    public static bool IsPaused = false;
    public static PauseMenu? pauseMenu;
    public static GameState cameFrom = GameState.HOME;

    static void Main()
    {
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
        Raylib.InitWindow(800, 480, "Bulletbox | C# Edition");
        Raylib.SetTargetFPS(60);

        // MANDATORY: Stops ESC from instantly killing the app
        Raylib.SetExitKey(KeyboardKey.Null); 

        CurrentUser = SaveManager.Load();

        HomeScreen homeScreen = new HomeScreen();
        LoginScreen loginScreen = new LoginScreen();
        pauseMenu = new PauseMenu(); // Initialize the menu
        OptionsScreen optionsScreen = new OptionsScreen();

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
            if (IsPaused && CurrentState == GameState.PLAYING)
            {
                pauseMenu.Update();
            }
            else 
            {
                switch (CurrentState)
                {
                    case GameState.HOME:
                        homeScreen.Update();
                        break;
                    case GameState.LOGIN:
                        loginScreen.Update();
                        if (CurrentUser.HasLoggedIn) CurrentState = GameState.PLAYING;
                        break;
                    case GameState.PLAYING:
                        if (PlayingState == null)
                        {
                            PlayingState = new Playing(CurrentUser.Username);
                            Net.Connect("127.0.0.1", 32308, CurrentUser.Username, CurrentUser.Password);
                        }
                        PlayingState.Update();
                        break;
                    case GameState.OPTIONS:
                        optionsScreen.Update();
                        break;
                }
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
            }
            Raylib.EndDrawing();
        }

        // Call this when the game closes
        client.Dispose();
        Raylib.CloseWindow();
    }

    public static void DisconnectAndLeave()
    {
        Net.Disconnect();      
        PlayingState = null;   
        IsPaused = false;      
        CurrentState = GameState.HOME;
    }
}