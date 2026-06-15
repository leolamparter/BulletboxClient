using Raylib_cs;
using System.Numerics;
using System.Collections.Generic;
using BulletboxClient;
using System;

public class SkinSelectorScreen
{
    private List<string> _baseSkins = new List<string> { "Bob", "Bob's Friend" };
    private UIButton _backButton;
    private UIButton _redeemButton;
    private bool _isRedeeming = false;
    private string _redeemCode = "";

    public SkinSelectorScreen()
    {
        _backButton = new UIButton("BACK", Vector2.Zero, 30, true);
        _redeemButton = new UIButton("REDEEM SKIN", Vector2.Zero, 30, true);
    }

    public void Update(bool resized)
    {
        HomeScreen.background.Update(resized);

        if (_isRedeeming)
        {
            int key = Raylib.GetCharPressed();
            while (key > 0)
            {
                if (_redeemCode.Length < 15) _redeemCode += (char)key;
                key = Raylib.GetCharPressed();
            }

            if (Raylib.IsKeyPressed(KeyboardKey.Backspace) && _redeemCode.Length > 0)
                _redeemCode = _redeemCode.Substring(0, _redeemCode.Length - 1);

            if (Raylib.IsKeyPressed(KeyboardKey.Enter))
            {
                Program.RedeemSkin(_redeemCode.ToUpper());
                _redeemCode = "";
                _isRedeeming = false;
            }
            if (Raylib.IsKeyPressed(KeyboardKey.Escape)) _isRedeeming = false;
            return;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Escape) || _backButton.IsClicked())
            Program.CurrentState = GameState.HOME;

        if (_redeemButton.IsClicked())
        {
            _isRedeeming = true;
            _redeemCode = "";
        }
    }

    public void Draw()
    {
        int sw = Raylib.GetScreenWidth();
        int sh = Raylib.GetScreenHeight();

        Raylib.DrawText("SKIN SELECTOR", sw / 2 - Raylib.MeasureText("SKIN SELECTOR", 40) / 2, 50, 40, Color.Gold);

        var user = Program.CurrentUser;
        List<string> allSkins = new List<string>(_baseSkins);
        if (user.UnlockedSkins != null) 
        {
            foreach (string s in user.UnlockedSkins) if (!allSkins.Contains(s)) allSkins.Add(s);
        }

        for (int i = 0; i < allSkins.Count; i++)
        {
            string skinName = allSkins[i];
            Color col = (Program.SelectedSkin == skinName) ? Color.Green : Color.White;
            int yPos = 150 + (i * 60);
            int textWidth = Raylib.MeasureText(skinName, 30);
            Rectangle hitBox = new Rectangle(sw / 2 - textWidth / 2 - 10, yPos, textWidth + 20, 40);

            if (Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), hitBox))
            {
                col = Color.Yellow;
                if (Raylib.IsMouseButtonPressed(MouseButton.Left)) Program.SelectedSkin = skinName;
            }

            Raylib.DrawText(skinName, sw / 2 - textWidth / 2, yPos, 30, col);

            if (skinName == "Apex Master")
            {
                Raylib.DrawText("(SPECIAL)", sw / 2 + textWidth / 2 + 15, yPos + 5, 22, Color.Orange);
            }
        }

        if (_isRedeeming)
        {
            Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 200));
            Raylib.DrawText("ENTER CODE:", sw / 2 - Raylib.MeasureText("ENTER CODE:", 30) / 2, sh / 2 - 60, 30, Color.White);
            Raylib.DrawText(_redeemCode + "_", sw / 2 - Raylib.MeasureText(_redeemCode + "_", 40) / 2, sh / 2, 40, Color.Yellow);
        }

        _backButton.Position = new Vector2(sw / 2 - 120, sh - 80);
        _backButton.Draw();
        _redeemButton.Position = new Vector2(sw / 2 + 120, sh - 80);
        _redeemButton.Draw();
    }
}