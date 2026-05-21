using Raylib_cs;
using System.Numerics;

public class HotbarUI {
    private Inventory inv;
    public int SelectedSlot = 0;

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
        }
    }

    public static void DrawItem(ItemStack stack, Rectangle rect) {
        if (((char)stack.ItemID) != ' ' && ((char)stack.ItemID) != '\0') {
            string id = ((char)stack.ItemID).ToString();
            int tw = Raylib.MeasureText(id, 30);
            Raylib.DrawText(id, (int)(rect.X + rect.Width / 2 - tw / 2), (int)(rect.Y + rect.Height / 2 - 15), 30, Color.White);
            
            if (stack.Count > 1) {
                Raylib.DrawText(stack.Count.ToString(), (int)rect.X + 5, (int)rect.Y + 40, 15, Color.LightGray);
            }
        }
    }
}