using Raylib_cs;
using System.Numerics;
using System.Collections.Generic;
using System.IO;
using System;
using System.Linq;

namespace BulletboxClient
{
    public class WorldSelectionScreen
    {
        private List<string> _worldNames = new();
        private UIButton _backButton;
        private UIButton _createButton;
        private float _scrollOffset = 0f;
        private const float ScrollSpeed = 40f;
        private string _pendingDelete = "";
        private bool _isConfirmingDelete = false;

        public WorldSelectionScreen()
        {
            _backButton = new UIButton("BACK", Vector2.Zero, 30, true);
            _createButton = new UIButton("CREATE WORLD", Vector2.Zero, 30, true);
            RefreshWorldList();
        }

        public void RefreshWorldList()
        {
            _worldNames.Clear();
            if (!Directory.Exists("saves")) Directory.CreateDirectory("saves");
            
            // Filter for .db files and ignore the global metadata database
            var files = Directory.GetFiles("saves", "*.db").Where(f => !f.EndsWith("global_metadata.db"));
            foreach (var file in files)
            {
                _worldNames.Add(Path.GetFileNameWithoutExtension(file));
            }
        }

        public void Update(bool resized)
        {
            HomeScreen.background.Update(resized);

            if (_isConfirmingDelete)
            {
                return; // Logic handled by Draw popup buttons
            }

            if (Raylib.IsKeyPressed(KeyboardKey.Escape) || _backButton.IsClicked())
                Program.CurrentState = GameState.HOME;

            if (_createButton.IsClicked())
                Program.CurrentState = GameState.CREATE_WORLD;

            float mouseWheelMove = Raylib.GetMouseWheelMove();
            if (mouseWheelMove != 0) _scrollOffset += mouseWheelMove * ScrollSpeed;

            int sh = Raylib.GetScreenHeight();
            int totalHeight = _worldNames.Count * 70;
            int displayHeight = sh - 250;
            if (totalHeight > displayHeight)
                _scrollOffset = Math.Clamp(_scrollOffset, -(totalHeight - displayHeight), 0);
            else
                _scrollOffset = 0;
        }

        public void Draw()
        {
            int sw = Raylib.GetScreenWidth();
            int sh = Raylib.GetScreenHeight();

            Raylib.DrawText("SELECT WORLD", sw / 2 - Raylib.MeasureText("SELECT WORLD", 40) / 2, 40, 40, Color.Gold);

            int listX = sw / 2 - 300;
            int listY = 120;
            int listW = 600;
            int listH = sh - 250;

            Raylib.BeginScissorMode(listX, listY, listW, listH);
            for (int i = 0; i < _worldNames.Count; i++)
            {
                string name = _worldNames[i];
                int itemY = (int)(listY + (i * 70) + _scrollOffset);

                // Draw background box
                Raylib.DrawRectangle(listX, itemY, listW, 60, new Color(0, 0, 0, 150));
                Raylib.DrawText(name, listX + 20, itemY + 18, 24, Color.White);

                // Play and Delete Buttons
                var playBtn = new UIButton("PLAY", new Vector2(listX + 480, itemY + 30), 22, true);
                var delBtn = new UIButton("DEL", new Vector2(listX + 550, itemY + 30), 22, true);

                if (!_isConfirmingDelete)
                {
                    if (playBtn.IsClicked())
                    {
                        // CRITICAL: Update the global world data so the server opens the right .db file
                        Program.CurrentWorldData = new WorldData
                        {
                            WorldName = name,
                            Version = Program.VERSION // Assume current version for existing loads
                        };
                        Program.LastIP = "127.0.0.1";
                        Program.CurrentState = GameState.SINGLEPLAYER_CONNECTING;
                    }
                    if (delBtn.IsClicked())
                    {
                        _pendingDelete = name;
                        _isConfirmingDelete = true;
                    }
                }

                playBtn.Draw();
                delBtn.Draw();
            }
            Raylib.EndScissorMode();

            _backButton.Position = new Vector2(sw / 2 - 120, sh - 60);
            _createButton.Position = new Vector2(sw / 2 + 120, sh - 60);
            _backButton.Draw();
            _createButton.Draw();

            if (_isConfirmingDelete) DrawDeletePopup(sw, sh);
        }

        private void DrawDeletePopup(int sw, int sh)
        {
            Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 200));
            
            string warn = "Are you sure you want to delete this world forever?";
            string warn2 = "(A very long time!)";
            int w1 = Raylib.MeasureText(warn, 25);
            int w2 = Raylib.MeasureText(warn2, 20);
            
            Raylib.DrawText(warn, sw / 2 - w1 / 2, sh / 2 - 60, 25, Color.White);
            Raylib.DrawText(warn2, sw / 2 - w2 / 2, sh / 2 - 25, 20, Color.Red);

            var confirmBtn = new UIButton("Confirm", new Vector2(sw / 2 - 100, sh / 2 + 50), 25, true);
            var cancelBtn = new UIButton("Go Back", new Vector2(sw / 2 + 100, sh / 2 + 50), 25, true);

            if (confirmBtn.IsClicked())
            {
                string path = Path.Combine("saves", $"{_pendingDelete}.db");
                if (File.Exists(path)) File.Delete(path);
                _isConfirmingDelete = false;
                RefreshWorldList();
            }
            if (cancelBtn.IsClicked()) _isConfirmingDelete = false;

            confirmBtn.Draw();
            cancelBtn.Draw();
        }
    }
}