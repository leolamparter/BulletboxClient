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
                Program.Net.SendMoveItem((byte)draggingIndex, (byte)dropTarget);
            }
            draggingIndex = -1;
        }
    }

    public void Draw() {
        if (!Visible) return;
        int sw = Raylib.GetScreenWidth();
        int sh = Raylib.GetScreenHeight();
        float size = 60, pad = 10;
        float startX = (sw - (size + pad) * 6) / 2;
        float startY = sh / 2 - 100;
        float hotY = sh - size - 20;

        Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 150));
        Raylib.DrawText("INVENTORY (E to close)", (int)startX, (int)startY - 40, 20, Color.Yellow);

        // Main Inventory Slots (6-23)
        for (int i = 6; i < 24; i++) {
            int row = (i - 6) / 6;
            int col = (i - 6) % 6;
            DrawSlotLogic(startX + (col * (size + pad)), startY + (row * (size + pad)), i, size);
        }

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
        
        // HOVER COLOR: Brighter if mouse is over it
        Color baseCol = isHovered ? new Color(90, 90, 90, 255) : new Color(50, 50, 50, 255);
        Raylib.DrawRectangleRec(rect, baseCol);
        Raylib.DrawRectangleLinesEx(rect, 1, Color.Gray);

        if (isHovered && Raylib.IsMouseButtonPressed(MouseButton.Left)) {
            if (inv.Slots[index].ItemID != ' ') draggingIndex = index;
        }

        if (draggingIndex != index) {
            HotbarUI.DrawItem(inv.Slots[index], rect);
        }
    }

    private int GetSlotUnderMouse() {
        int sw = Raylib.GetScreenWidth();
        int sh = Raylib.GetScreenHeight();
        float size = 60, pad = 10;
        float startX = (sw - (size + pad) * 6) / 2;
        float startY = sh / 2 - 100;
        float hotY = sh - size - 20;
        Vector2 mouse = Raylib.GetMousePosition();

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