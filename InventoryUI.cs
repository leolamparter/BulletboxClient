using Raylib_cs;
using System.Numerics;

public class InventoryUI {
    private Inventory inv;
    public bool Visible = false;
    private int draggingIndex = -1; 

    public InventoryUI(Inventory inventory) { inv = inventory; }

    public void Update() {
        if (Raylib.IsKeyPressed(KeyboardKey.E)) {
            Visible = !Visible;
            draggingIndex = -1; 
        }
        if (!Visible) return;

        // Release Item
        if (Raylib.IsMouseButtonReleased(MouseButton.Left) && draggingIndex != -1) {
            int dropTarget = GetSlotUnderMouse();
            if (dropTarget != -1 && dropTarget != draggingIndex) {
                // Enforce Shield-only rule for slot 24
                if (dropTarget == 24 && (char)inv.Slots[draggingIndex].ItemID != 'H') {
                    draggingIndex = -1;
                    return;
                }
                
                Program.Net.SendMoveItem((byte)draggingIndex, (byte)dropTarget);
            }
            draggingIndex = -1;
        }
    }

    public void Draw() {
        if (!Visible) return;
        int sw = Raylib.GetScreenWidth();
        int sh = Raylib.GetScreenHeight();
        float size = 64, pad = 0; // Match Hotbar size (64x64)
        float startX = (float)Math.Floor((sw - (size + pad) * 6) / 2f);
        float startY = (float)Math.Floor(sh / 2f - 100f);
        float hotY = (float)Math.Floor(sh - size - 20f);

        Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 150));
        Raylib.DrawText("INVENTORY (E to close)", (int)startX, (int)startY - 40, 20, Color.Yellow);

        // Main Inventory Slots (6-23)
        for (int i = 6; i < 24; i++) {
            int row = (i - 6) / 6;
            int col = (i - 6) % 6;
            DrawSlotLogic(startX + (col * (size + pad)), startY + (row * (size + pad)), i, size);
        }

        // Off-hand Slot (24)
        float offX = startX - size - 20;
        DrawSlotLogic(offX, hotY, 24, size);

        // Hotbar Slots (0-5) - Shown while inventory is open for easy moving
        for (int i = 0; i < 6; i++) {
            DrawSlotLogic(startX + (i * (size + pad)), hotY, i, size);
        }

        // Mouse Ghost
        if (draggingIndex != -1) {
            ItemStack stack = inv.Slots[draggingIndex];
            Raylib.DrawText(((char)stack.ItemID).ToString(), (int)Raylib.GetMouseX() - 10, (int)Raylib.GetMouseY() - 10, 30, new Color(255, 255, 255, 180));
        }
    }

    private void DrawSlotLogic(float x, float y, int index, float size) {
        Rectangle rect = new Rectangle(x, y, size, size);
        bool isHovered = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rect);
        
        bool isSelected = (index < 6 && Program.PlayingState != null && index == Program.PlayingState.Hotbar.SelectedSlot);

        if (isSelected || isHovered) {
            Texture2D activeTex = AssetManager.GetTexture("hotbar_active");
            if (activeTex.Id != 0) {
                Raylib.DrawTexturePro(activeTex, new Rectangle(0, 0, activeTex.Width, activeTex.Height), rect, Vector2.Zero, 0f, Color.White);
            }
        } else {
            // Draw the deactive individual slots
            Texture2D deactiveTex = AssetManager.GetTexture("hotbar_deactive");
            if (deactiveTex.Id != 0) {
                Raylib.DrawTexturePro(deactiveTex, new Rectangle(0, 0, deactiveTex.Width, deactiveTex.Height), rect, Vector2.Zero, 0f, Color.White);
            } else {
                Raylib.DrawRectangleRec(rect, Color.DarkGray);
            }
        }

        if (isHovered && Raylib.IsMouseButtonPressed(MouseButton.Left)) {
            if (inv.Slots[index].ItemID != ' ') draggingIndex = index;
        }

        // Set tooltip if hovered and not dragging
        if (isHovered && draggingIndex == -1) {
            HotbarUI.HoveredStack = inv.Slots[index];
            HotbarUI.HoveredMousePos = Raylib.GetMousePosition();
        }

        if (draggingIndex != index) {
            HotbarUI.DrawItem(inv.Slots[index], rect);
        }
    }

    private int GetSlotUnderMouse() {
        int sw = Raylib.GetScreenWidth();
        int sh = Raylib.GetScreenHeight();
        float size = 64, pad = 0;
        float startX = (float)Math.Floor((sw - (size + pad) * 6) / 2f);
        float startY = (float)Math.Floor(sh / 2f - 100f);
        float hotY = (float)Math.Floor(sh - size - 20f);
        Vector2 mouse = Raylib.GetMousePosition();

        // Check Off-hand
        if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(startX - size - 20, hotY, size, size))) return 24;

        // Check Hotbar
        for (int i = 0; i < 6; i++) {
            if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(startX + (i * (size + pad)), hotY, size, size))) return i;
        }
        for (int i = 6; i < 24; i++) {
            int row = (i - 6) / 6, col = (i - 6) % 6;
            if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(startX + (col * (size + pad)), startY + (row * (size + pad)), size, size))) return i;
        }
        return -1;
    }
}