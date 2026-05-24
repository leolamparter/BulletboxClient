using Raylib_cs;
using System.Numerics;
using System.Collections.Generic;

public class HotbarUI {
    private Inventory inv;
    public int SelectedSlot = 0;

    public static ItemStack? HoveredStack = null;
    public static Vector2 HoveredMousePos;

    public HotbarUI(Inventory inventory) {
        inv = inventory;
    }

    public void Update() {
        // Handle 1-6 keys for slot selection
        for (int i = 0; i < 6; i++) {
            if (Raylib.IsKeyPressed((KeyboardKey)(49 + i))) {
                SelectedSlot = i;
                Program.Net.SendSlotSwap((byte)i); // Tell server we changed held item
            }
        }
    }

    public void Draw() {
        int sw = Raylib.GetScreenWidth();
        int sh = Raylib.GetScreenHeight();
        float size = 64;
        float pad = 0;
        float totalW = (size + pad) * 6;
        float startX = (float)Math.Floor((sw - totalW) / 2f);
        float y = (float)Math.Floor(sh - size - 20f);

        // --- Draw Off-hand Slot (Shield) ---
        float offhandX = startX - size - 20;
        Rectangle offRect = new Rectangle(offhandX, y, size, size);
        Texture2D offTex = AssetManager.GetTexture("hotbar_deactive");
        if (offTex.Id != 0) {
            Raylib.DrawTexturePro(offTex, new Rectangle(0, 0, offTex.Width, offTex.Height), offRect, Vector2.Zero, 0f, Color.White);
        }
        
        // Draw Item in Off-hand (Slot 24)
        DrawItem(inv.Slots[24], offRect);

        if (Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), offRect) && !Program.IsPaused) {
            HoveredStack = inv.Slots[24];
            HoveredMousePos = Raylib.GetMousePosition();
        }

        // --- Draw Standard Hotbar ---
        for (int i = 0; i < 6; i++) {
            Rectangle rect = new Rectangle(startX + (i * (size + pad)), y, size, size);

            // Draw themed hotbar slot texture
            string textureKey = i == SelectedSlot ? "hotbar_active" : "hotbar_deactive";
            Texture2D tex = AssetManager.GetTexture(textureKey);
            if (tex.Id != 0) {
                Raylib.DrawTexturePro(tex, new Rectangle(0, 0, tex.Width, tex.Height), rect, Vector2.Zero, 0f, Color.White);
            }

            // Draw Item
            DrawItem(inv.Slots[i], rect);

            // Tooltip Detection
            if (Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rect) && !Program.IsPaused) {
                HoveredStack = inv.Slots[i];
                HoveredMousePos = Raylib.GetMousePosition();
            }
        }
    }

    public static void RenderTooltip() {
        if (HoveredStack == null || HoveredStack.Value.ItemID == ' ' || HoveredStack.Value.ItemID == '\0') return;
        
        ItemStack stack = HoveredStack.Value;
        WeaponStats? stats = null;
        WeaponStats.Library.TryGetValue(stack.ItemID, out stats);

        string name = (stats != null) ? stats.Name : ((char)stack.ItemID).ToString();
        List<string> infoLines = new List<string>();

        if (stats != null) {
            infoLines.Add($"Damage: {stats.Damage}");
            infoLines.Add($"Speed: {1f/stats.Cooldown:F1}/s");
            infoLines.Add($"Range: {stats.Range}");
            infoLines.Add($"Knockback: {stats.Knockback}");
        }

        float width = 180;
        float height = 35 + (infoLines.Count * 20);
        
        // Position box relative to mouse
        float x = HoveredMousePos.X + 15;
        float y = HoveredMousePos.Y - height - 15;

        // Screen collision
        if (x + width > Raylib.GetScreenWidth()) x = HoveredMousePos.X - width - 15;
        if (y < 0) y = HoveredMousePos.Y + 15;

        Rectangle rect = new Rectangle(x, y, width, height);
        
        // Minecraft-style colors: Dark purple/black bg with a gray border
        Raylib.DrawRectangleRounded(rect, 0.15f, 10, new Color(16, 0, 16, 235)); 
        Raylib.DrawRectangleRoundedLines(rect, 0.15f, 10, new Color(80, 80, 80, 255));
        
        Raylib.DrawText(name, (int)x + 10, (int)y + 10, 20, Color.Yellow);
        for (int i = 0; i < infoLines.Count; i++) {
            Raylib.DrawText(infoLines[i], (int)x + 10, (int)y + 35 + (i * 20), 16, Color.LightGray);
        }
    }

    public static void DrawItem(ItemStack stack, Rectangle rect) {
        if (((char)stack.ItemID) != ' ' && ((char)stack.ItemID) != '\0') {
            char idChar = (char)stack.ItemID;
            string id = idChar.ToString();
            
            string textureKey = "";
            if (idChar == 'K') textureKey = "kanabo";
            else if (idChar == 'P') textureKey = "spear";
            else if (idChar == 'S') textureKey = "sword";
            else if (idChar == 'H') textureKey = "shield";
            
            Texture2D itemTex = string.IsNullOrEmpty(textureKey) ? new Texture2D() : AssetManager.GetTexture(textureKey);

            if (itemTex.Id != 0) {
                // Draw centered in slot with small padding
                float scale = (rect.Width - 10) / itemTex.Width;
                
                // Calculate destination and origin for 45-degree CCW rotation centered in the slot
                Rectangle dest = new Rectangle(rect.X + rect.Width / 2, rect.Y + rect.Height / 2, itemTex.Width * scale, itemTex.Height * scale);
                Vector2 origin = new Vector2((itemTex.Width * scale) / 2, (itemTex.Height * scale) / 2);
                Raylib.DrawTexturePro(itemTex, new Rectangle(0, 0, itemTex.Width, itemTex.Height), dest, origin, -45f, Color.White);
            } else {
                int tw = Raylib.MeasureText(id, 30);
                Raylib.DrawText(id, (int)(rect.X + rect.Width / 2 - tw / 2), (int)(rect.Y + rect.Height / 2 - 15), 30, Color.White);
            }

            if (stack.Count > 1) {
                Raylib.DrawText(stack.Count.ToString(), (int)rect.X + 5, (int)rect.Y + 40, 15, Color.LightGray);
            }
        }
    }
}