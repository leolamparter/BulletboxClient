using Raylib_cs;
using System.Numerics;
using System;

public enum GameState { HOME, LOGIN, SERVER_SELECTOR, PLAYING }

class Program
{
    public static GameState CurrentState = GameState.HOME;
    public static UserData CurrentUser = new UserData(); 
    
    public static Connection Net = new Connection();
    public static Playing? PlayingState;
    
    // NEW: Pause State
    public static bool IsPaused = false;
    public static PauseMenu? pauseMenu;

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

        while (!Raylib.WindowShouldClose())
        {
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
                case GameState.LOGIN:
                    HomeScreen.background.Update();
                    HomeScreen.background.Draw();
                    loginScreen.Draw();
                    break;
                case GameState.PLAYING:
                    PlayingState?.Draw();
                    
                    // Draw Pause Menu on top if active
                    if (IsPaused) pauseMenu.Draw(); 
                    break;
            }
            Raylib.EndDrawing();
        }

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