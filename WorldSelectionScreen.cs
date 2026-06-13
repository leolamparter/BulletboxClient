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
        private bool _isDeleteRestricted = false;
        private bool _isEditingName = false;
        private string _originalName = "";
        private UIInputBox? _editInput;

        public WorldSelectionScreen()
        {
            _backButton = new UIButton("BACK", Vector2.Zero, 30, true);
            _createButton = new UIButton("CREATE WORLD", Vector2.Zero, 30, true);
            AssetManager.LoadTexture("world_unfavorited", "resources/textures/ui/other/unfavorited.png");
            AssetManager.LoadTexture("world_favorited", "resources/textures/ui/other/favorited.png");
            RefreshWorldList();
        }

        public void RefreshWorldList()
        {
            _worldNames.Clear();
            if (!Directory.Exists("saves")) Directory.CreateDirectory("saves");
            
            // Filter for .db files and ignore the global metadata database
            var files = Directory.GetFiles("saves", "*.db").Where(f => !f.EndsWith("global_metadata.db")).ToList();
            var names = files.Select(f => Path.GetFileNameWithoutExtension(f)).ToList();

            // Favorited worlds stay at the top, then sorted alphabetically
            var favorites = Program.CurrentUser.FavoriteWorlds;
            _worldNames = names.OrderByDescending(n => favorites.Contains(n)).ThenBy(n => n).ToList();
        }

        public void Update(bool resized)
        {
            HomeScreen.background.Update(resized);

            if (_isConfirmingDelete || _isEditingName)
            {
                if (_isEditingName) _editInput?.Update();
                return; 
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

                // Favorite Toggle Button
                bool isFavorited = Program.CurrentUser.FavoriteWorlds.Contains(name);
                Texture2D favTex = AssetManager.GetTexture(isFavorited ? "world_favorited" : "world_unfavorited");
                Rectangle favRect = new Rectangle(listX + 15, itemY + 15, 30, 30);
                
                if (!_isConfirmingDelete && !_isEditingName)
                {
                    if (Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), favRect) && Raylib.IsMouseButtonPressed(MouseButton.Left))
                    {
                        if (isFavorited)
                        {
                            Program.CurrentUser.FavoriteWorlds.Remove(name);
                            SaveManager.Save(Program.CurrentUser);
                            RefreshWorldList();
                        }
                        else if (Program.CurrentUser.FavoriteWorlds.Count < 3)
                        {
                            Program.CurrentUser.FavoriteWorlds.Add(name);
                            SaveManager.Save(Program.CurrentUser);
                            RefreshWorldList();
                        }
                    }
                }

                if (favTex.Id != 0)
                {
                    Raylib.DrawTexturePro(favTex, new Rectangle(0, 0, favTex.Width, favTex.Height), favRect, Vector2.Zero, 0, Color.White);
                }
                else
                {
                    // Fallback: Draw a geometric star/icon if the PNG files are missing
                    float centerX = favRect.X + favRect.Width / 2;
                    float centerY = favRect.Y + favRect.Height / 2;
                    Raylib.DrawPoly(new Vector2(centerX, centerY), 5, 12, 0, isFavorited ? Color.Yellow : Color.Gray);
                    Raylib.DrawPolyLines(new Vector2(centerX, centerY), 5, 12, 0, Color.Black);
                }

                Raylib.DrawText(name, listX + 60, itemY + 18, 24, Color.White);

                // Play, Edit, and Delete Buttons
                var playBtn = new UIButton("PLAY", new Vector2(listX + 410, itemY + 30), 22, true);
                var editBtn = new UIButton("EDIT", new Vector2(listX + 485, itemY + 30), 22, true);
                var delBtn = new UIButton("DEL", new Vector2(listX + 560, itemY + 30), 22, true);

                if (!_isConfirmingDelete && !_isEditingName)
                {
                    if (playBtn.IsClicked())
                    {
                        // CRITICAL: Update the global world data so the server opens the right .db file
                        Program.CurrentWorldData = new WorldData
                        {
                            WorldName = name,
                            Version = "Unknown" // Loaded from the DB in ServerProgram.LoadGame
                        };
                        Program.LastIP = "127.0.0.1";
                        Program.CurrentState = GameState.SINGLEPLAYER_CONNECTING;
                    }
                    if (editBtn.IsClicked())
                    {
                        _originalName = name;
                        _isEditingName = true;
                        _editInput = new UIInputBox(Vector2.Zero, 300, 40, "New Name", 24);
                        _editInput.Text = name;
                    }
                    if (delBtn.IsClicked())
                    {
                        _pendingDelete = name;
                        _isConfirmingDelete = true;
                        if (isFavorited)
                        {
                            _isDeleteRestricted = true;
                        }
                        else
                        {
                            _isDeleteRestricted = false;
                        }
                    }
                }

                playBtn.Draw();
                editBtn.Draw();
                delBtn.Draw();
            }
            Raylib.EndScissorMode();

            _backButton.Position = new Vector2(sw / 2 - 120, sh - 60);
            _createButton.Position = new Vector2(sw / 2 + 120, sh - 60);
            _backButton.Draw();
            _createButton.Draw();

            if (_isConfirmingDelete) DrawDeletePopup(sw, sh);
            if (_isEditingName) DrawEditPopup(sw, sh);
        }

        private void DrawDeletePopup(int sw, int sh)
        {
            Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 200));
            
            if (_isDeleteRestricted)
            {
                string msg = "You have to first unfavorite this world before deleting it";
                int mw = Raylib.MeasureText(msg, 25);
                Raylib.DrawText(msg, sw / 2 - mw / 2, sh / 2 - 20, 25, Color.White);

                var backBtn = new UIButton("Go Back", new Vector2(sw / 2, sh / 2 + 50), 25, true);
                if (backBtn.IsClicked())
                {
                    _isConfirmingDelete = false;
                    _isDeleteRestricted = false;
                }
                backBtn.Draw();
                return;
            }

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
                Program.CurrentUser.FavoriteWorlds.Remove(_pendingDelete);
                SaveManager.Save(Program.CurrentUser);
                _isConfirmingDelete = false;
                RefreshWorldList();
            }
            if (cancelBtn.IsClicked()) _isConfirmingDelete = false;

            confirmBtn.Draw();
            cancelBtn.Draw();
        }

        private void DrawEditPopup(int sw, int sh)
        {
            Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 200));
            
            string title = "RENAME WORLD";
            int tw = Raylib.MeasureText(title, 30);
            Raylib.DrawText(title, sw / 2 - tw / 2, sh / 2 - 100, 30, Color.Gold);

            if (_editInput != null)
            {
                _editInput.Position = new Vector2(sw / 2, sh / 2 - 20);
                _editInput.Draw();
            }

            var confirmBtn = new UIButton("Save", new Vector2(sw / 2 - 100, sh / 2 + 60), 25, true);
            var cancelBtn = new UIButton("Cancel", new Vector2(sw / 2 + 100, sh / 2 + 60), 25, true);

            if (confirmBtn.IsClicked())
            {
                string newName = _editInput?.Text.Trim() ?? "";
                if (!string.IsNullOrEmpty(newName) && newName != _originalName)
                {
                    string oldPath = Path.Combine("saves", $"{_originalName}.db");
                    string newPath = Path.Combine("saves", $"{newName}.db");
                    
                    if (File.Exists(oldPath) && !File.Exists(newPath))
                    {
                        File.Move(oldPath, newPath);
                        // Update favorite list name if changed
                        if (Program.CurrentUser.FavoriteWorlds.Contains(_originalName))
                        {
                            Program.CurrentUser.FavoriteWorlds.Remove(_originalName);
                            Program.CurrentUser.FavoriteWorlds.Add(newName);
                            SaveManager.Save(Program.CurrentUser);
                        }
                        RefreshWorldList();
                    }
                }
                _isEditingName = false;
            }
            if (cancelBtn.IsClicked()) _isEditingName = false;

            confirmBtn.Draw();
            cancelBtn.Draw();
        }
    }
}