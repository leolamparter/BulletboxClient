using Raylib_cs;
using System.Numerics;
using System.Collections.Generic;
using BulletboxClient;

public class HomeScreen
{
    private List<UIButton> buttons;
    public static WorldBackground background = new WorldBackground();
    private string title = "BULLETBOX";
    private Player _previewPlayer;
    private UIButton _skinSelectButton;

    public void DrawBackgroundOnly() 
    {
        background.Draw();
    }

    public HomeScreen()
    {
        background = new WorldBackground();
        buttons = new List<UIButton>();

        // We initialize with dummy positions; Draw() will position them correctly
        buttons.Add(new UIButton("SINGLEPLAYER", Vector2.Zero, 40, true));
        buttons.Add(new UIButton("ADD-ONS", Vector2.Zero, 40));
        buttons.Add(new UIButton("OPTIONS", Vector2.Zero, 40));
        buttons.Add(new UIButton("ADVANCEMENTS", Vector2.Zero, 40));
        buttons.Add(new UIButton("QUIT GAME", Vector2.Zero, 40));

        _previewPlayer = new Player("Preview", Vector2.Zero);
        _skinSelectButton = new UIButton("SELECT SKIN", Vector2.Zero, 25);
    }

    public void Update(bool windowResized)
    {
        background.Update(windowResized);

        float sw = Raylib.GetScreenWidth();
        float sh = Raylib.GetScreenHeight();

        // Position preview on the left side and update its rotation/state
        _previewPlayer.Position = new Vector2(sw / 6f - 32, sh / 2f - 32);

        // Update preview colors based on selected skin
        if (Program.SelectedSkin == "Apex Master")
        {
            _previewPlayer.Color = Color.White;
            _previewPlayer.InnerColor = Color.Magenta;
        }
        else if (Program.SelectedSkin == "Bob's Friend")
        {
            _previewPlayer.Color = Color.Blue;
            _previewPlayer.InnerColor = Color.Magenta;
        }
        else
        {
            _previewPlayer.Color = Color.DarkGreen;
            _previewPlayer.InnerColor = Color.Magenta;
        }

        _previewPlayer.Update(Raylib.GetFrameTime());

        _skinSelectButton.Position = new Vector2(sw / 6f, sh / 2f + 60);
        if (_skinSelectButton.IsClicked()) Program.CurrentState = GameState.SKIN_SELECTOR;

        for (int i = 0; i < buttons.Count; i++)
        {
            if (buttons[i].IsClicked())
            {
                string text = buttons[i].Text;
                if (text == "SINGLEPLAYER") 
                {
                    Program.CurrentState = GameState.WORLD_SELECTION;
                }
                else if (text == "ADD-ONS") {
                    Program.CurrentState = GameState.ADD_ONS;
                }
                else if (text == "OPTIONS") {
                    Program.cameFrom = GameState.HOME;
                    Program.CurrentState = GameState.OPTIONS;
                }
                else if (text == "ADVANCEMENTS") {
                    Program.CurrentState = GameState.ADVANCEMENTS;
                }
                else if (text == "QUIT GAME") Environment.Exit(0);
            }
        }
    }

    public void Draw()
    {
        background.Draw();

        _previewPlayer.Draw();
        _skinSelectButton.Draw();

        float screenW = Raylib.GetScreenWidth();
        float screenH = Raylib.GetScreenHeight();
        float centerX = screenW / 2;
        float centerY = screenH / 2;

        // Draw Title (Centered and Yellow)
        int titleFontSize = 85;
        int titleWidth = Raylib.MeasureText(title, titleFontSize);
        Raylib.DrawText(title, (int)centerX - titleWidth / 2, (int)centerY - 180, titleFontSize, Color.Yellow);

        // Position and Draw Buttons relative to the center
        float startY = centerY - 40;
        float spacing = 60;

        for (int i = 0; i < buttons.Count; i++)
        {
            buttons[i].Position = new Vector2(centerX, startY + (i * spacing));
            buttons[i].Draw();
        }
    }
}