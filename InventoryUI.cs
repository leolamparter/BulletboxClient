using Raylib_cs;
using System.Numerics;

public class InventoryUI {
    private Inventory inv;
    public bool Visible = false;
    private int draggingIndex = -1; // -1 for no drag, 0-24 for inventory, 100-102 for crafting slots
    private int draggingCount = -1; // -1 for the whole stack

    // Crafting slots
    private ItemStack _craftInput1 = new ItemStack("none", 0);
    private ItemStack _craftInput2 = new ItemStack("none", 0);
    private ItemStack _craftOutput = new ItemStack("none", 0);

    // Chest slots
    public bool ChestVisible = false;
    private ItemStack[] _chestSlots = new ItemStack[18];

    public InventoryUI(Inventory inventory) {
        inv = inventory;
    }

    public void Update() {
        if (Raylib.IsKeyPressed(KeyboardKey.E)) {
            Visible = !Visible;
            draggingIndex = -1; 
            ChestVisible = false;
        }

        if (!Visible) return;

        // Release Item
        if (Raylib.IsMouseButtonReleased(MouseButton.Left) && draggingIndex != -1) {
            int dropTarget = GetSlotUnderMouse();
            ItemStack draggedItem = GetDraggedItemStack();

            if (ChestVisible && draggedItem.ItemID != "none") {
                if (dropTarget >= 110 && dropTarget <= 127 && draggingIndex < 25) { // Inv to Chest
                    Program.Net.SendChestMove((byte)(dropTarget - 110), (byte)draggingIndex, true, draggedItem.Count);
                }
                else if (dropTarget >= 0 && dropTarget < 25 && draggingIndex >= 110) { // Chest to Inv
                    Program.Net.SendChestMove((byte)(draggingIndex - 110), (byte)dropTarget, false, draggedItem.Count);
                }
                draggingIndex = -1;
                return;
            }

            if (draggedItem.ItemID != "none") {
                // Handle special case: Taking items OUT of the output slot (102)
                if (draggingIndex == 102 && dropTarget >= 0 && dropTarget < 25) {
                    if (inv.Slots[dropTarget].ItemID == "none") {
                        Program.Net.SendCraftRequest(_craftInput1.ItemID, _craftInput1.Count, _craftInput2.ItemID, _craftInput2.Count);
                    }
                }
                // Handle all moves between inventory (0-24) and crafting inputs (100-101)
                else if (((dropTarget >= 0 && dropTarget < 25) || (dropTarget >= 100 && dropTarget <= 101)) && 
                         draggingIndex != dropTarget) {
                    // We trust the server to move items between inventory and grid slots
                    Program.Net.SendMoveItem((byte)draggingIndex, (byte)dropTarget, draggedItem.Count);
                }
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
        
        if (ChestVisible) {
            Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 150));
            Raylib.DrawText("Chest", (int)startX, (int)startY - 40, 30, Color.Yellow);
            for (int i = 0; i < 18; i++) {
                int row = i / 6;
                int col = i % 6;
                DrawSlotLogic(startX + (col * (size + pad)), startY + (row * (size + pad)), 110 + i, size, _chestSlots[i]);
            }
            startY += (size + pad) * 3 + 20; // Push inventory down
        } else {
            // Crafting UI - Repositioned BELOW the main inventory grid
            float craftWidth = (size * 3) + 60;
            float craftX = startX + ((size + pad) * 6) / 2f - craftWidth / 2f;
            float craftY = startY + (size + pad) * 3 + 10;

            DrawSlotLogic(craftX, craftY, 100, size, _craftInput1); // Input 1
            DrawSlotLogic(craftX + size + 5, craftY, 101, size, _craftInput2); // Input 2

            // Visual Arrow and Output slot aligned horizontally
            Raylib.DrawText("->", (int)(craftX + (size * 2) + 12), (int)(craftY + size / 2 - 15), 30, Color.White);
            DrawSlotLogic(craftX + (size * 2) + 50, craftY, 102, size, _craftOutput); // Output

            Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 150));
            Raylib.DrawText("INVENTORY (E to close)", (int)startX, (int)startY - 40, 20, Color.Yellow);
        }


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
            HotbarUI.DrawItem(GetDraggedItemStack(), new Rectangle(Raylib.GetMouseX() - 16, Raylib.GetMouseY() - 16, 32, 32)); // Draw dragged item
        }
    }

    private void DrawSlotLogic(float x, float y, int index, float size, ItemStack? itemOverride = null) {
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

        bool leftClick = Raylib.IsMouseButtonPressed(MouseButton.Left);
        bool rightClick = Raylib.IsMouseButtonPressed(MouseButton.Right);

        if (isHovered && (leftClick || rightClick)) {
            ItemStack stack = itemOverride ?? inv.Slots[index];
            if (stack.ItemID != "none") {
                draggingIndex = index;
                draggingCount = leftClick ? -1 : (stack.Count + 1) / 2;
            }
        }

        if (isHovered && draggingIndex == -1) {
            if (index >= 0 && index <= 24) // Inventory slots
            {
                HotbarUI.HoveredStack = inv.Slots[index];
            }
            else if (index >= 100 && index <= 101) // Crafting input slots
            {
                HotbarUI.HoveredStack = itemOverride;
            }
            else if (index == 102) // Crafting output slot
            {
                HotbarUI.HoveredStack = itemOverride;
            }
            else if (index >= 110 && index <= 127) // Chest slots
            {
                HotbarUI.HoveredStack = _chestSlots[index - 110];
            }
            HotbarUI.HoveredMousePos = Raylib.GetMousePosition();
        }

        ItemStack itemToDraw = itemOverride ?? inv.Slots[index];
        if (draggingIndex == index && draggingCount != -1) {
            // Draw the remainder in the slot
            ItemStack remainder = itemToDraw;
            remainder.Count -= draggingCount;
            if (remainder.Count > 0) HotbarUI.DrawItem(remainder, rect);
        } else if (draggingIndex != index) {
            HotbarUI.DrawItem(itemToDraw, rect);
        }
    }

    private ItemStack GetDraggedItemStack()
    {
        ItemStack stack = new ItemStack("none", 0);
        if (draggingIndex >= 0 && draggingIndex <= 24) stack = inv.Slots[draggingIndex];
        else if (draggingIndex == 100) stack = _craftInput1;
        else if (draggingIndex == 101) stack = _craftInput2;
        else if (draggingIndex == 102) stack = _craftOutput;
        else if (draggingIndex >= 110 && draggingIndex <= 127) stack = _chestSlots[draggingIndex - 110];
        
        if (draggingCount != -1 && stack.ItemID != "none") {
            stack.Count = draggingCount;
        }
        return stack;
    }

    private void CheckRecipe()
    {
        if (ItemStats.Recipes.TryGetValue((_craftInput1.ItemID, _craftInput2.ItemID), out string? result) ||
            ItemStats.Recipes.TryGetValue((_craftInput2.ItemID, _craftInput1.ItemID), out result))
        {
            _craftOutput = new ItemStack(result!, 1);
        }
        else
        {
            _craftOutput = new ItemStack("none", 0);
        }
    }

    public void OpenChestUI(ItemStack[] slots) {
        _chestSlots = slots;
        ChestVisible = true;
        Visible = true;
    }

    public void UpdateCraftingSlots(ItemStack input1, ItemStack input2, ItemStack output) {
        _craftInput1 = input1;
        _craftInput2 = input2;
        _craftOutput = output;
        // After server confirms, client-side inventory will be updated via packet 4
        // No need to call CheckRecipe here, server is authoritative
    }

    private int GetSlotUnderMouse() {
        int sw = Raylib.GetScreenWidth();
        int sh = Raylib.GetScreenHeight();
        float size = 64, pad = 0;
        float startX = (float)Math.Floor((sw - (size + pad) * 6) / 2f);
        float startY = (float)Math.Floor(sh / 2f - 100f);
        float hotY = (float)Math.Floor(sh - size - 20f);
        Vector2 mouse = Raylib.GetMousePosition();

        if (ChestVisible) {
            for (int i = 0; i < 18; i++) {
                int row = i / 6; int col = i % 6;
                if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(startX + (col * (size + pad)), startY + (row * (size + pad)), size, size)))
                    return 110 + i;
            }
            startY += (size + pad) * 3 + 20;
        }

        // Match the Draw() positioning for collision
        float craftWidth = (size * 3) + 60;
        float craftX = startX + ((size + pad) * 6) / 2f - craftWidth / 2f;
        float craftY = startY + (size + pad) * 3 + 10;

        if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(craftX, craftY, size, size))) return 100;
        if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(craftX + size + 5, craftY, size, size))) return 101;
        if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(craftX + (size * 2) + 50, craftY, size, size))) return 102;

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