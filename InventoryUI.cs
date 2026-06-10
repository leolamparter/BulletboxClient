using Raylib_cs;
using System.Numerics;

public class InventoryUI {
    private Inventory inv;
    public bool Visible = false;
    private int draggingIndex = -1; // -1 for no drag, 0-24 for inventory, 100-102 for crafting slots
    private int draggingCount = -1; // -1 for the whole stack

    // Crafting slots
    private ItemStack _craftInput1 = new("none", 0);
    private ItemStack _craftInput2 = new("none", 0);
    private ItemStack _craftOutput = new("none", 0);
    
    // Recipe List
    private bool _recipesVisible = false;
    private Rectangle _recipeButtonBounds; // Bounds for the recipe toggle button
    private float _recipeScrollOffset = 0f;
    private const float RecipeScrollSpeed = 30f; // Pixels per scroll unit
    private const int RecipeItemHeight = 40; // Height of each recipe display box
    private const int RecipeItemSpacing = 5; // Spacing between boxes

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
            _recipesVisible = false; // Close recipe list when inventory closes
        }

        // Handle recipe button click
        if (Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), _recipeButtonBounds) && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            _recipesVisible = !_recipesVisible;
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
                         draggingIndex != dropTarget && !((draggingIndex == 100 || draggingIndex == 101) && dropTarget == 102)) { // Prevent dragging from crafting input to output
                    // We trust the server to move items between inventory and grid slots
                    Program.Net.SendMoveItem((byte)draggingIndex, (byte)dropTarget, draggedItem.Count);
                }
            }
            draggingIndex = -1;
        }

        // Handle recipe list scrolling
        if (_recipesVisible)
        {
            float mouseWheelMove = Raylib.GetMouseWheelMove();
            if (mouseWheelMove != 0)
            {
                _recipeScrollOffset += mouseWheelMove * RecipeScrollSpeed;
            }

            // Clamp scroll offset
            // Define local variables for layout calculation, similar to Draw()
            int sw = Raylib.GetScreenWidth();
            int sh = Raylib.GetScreenHeight();
            float size = 64; // Match Hotbar size (64x64)
            float pad = 0;
            float startY = (float)Math.Floor(sh / 2f - 100f);

            int totalCraftableRecipesHeight = 0;
            foreach (var entry in ItemStats.Recipes)
            {
                if (CanCraft(entry.Key.Item1, entry.Key.Item2))
                {
                    totalCraftableRecipesHeight += RecipeItemHeight + RecipeItemSpacing;
                }
            }
            float recipeListHeight = sh - startY - 20; // Extends to near bottom of screen, matching Draw()
            _recipeScrollOffset = Math.Clamp(_recipeScrollOffset, Math.Min(0f, recipeListHeight - totalCraftableRecipesHeight), 0f);
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
            // Hide recipe button and list when chest is open
            _recipesVisible = false;

            // Draw semi-transparent background for the whole screen
            Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 150));
            Raylib.DrawText("Chest", (int)startX, (int)startY - 40, 30, Color.Yellow);

            // Draw chest slots
            for (int i = 0; i < 18; i++) {
                int row = i / 6;
                int col = i % 6;
                DrawSlotLogic(startX + (col * (size + pad)), startY + (row * (size + pad)), 110 + i, size, _chestSlots[i]);
            }
            startY += (size + pad) * 3 + 20; // Push inventory down
        } else {
            // Draw semi-transparent background for the whole screen
            Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 150));
            Raylib.DrawText("INVENTORY (E to close)", (int)startX, (int)startY - 40, 20, Color.Yellow);

            // Crafting UI
            float craftWidth = (size * 3) + 60;
            float craftX = startX + ((size + pad) * 6) / 2f - craftWidth / 2f;
            float craftY = startY + (size + pad) * 3 + 10;

            // Recipe Button (to the left of crafting inputs)
            float recipeButtonSize = size * 0.75f; // Scale button to fit nicely
            _recipeButtonBounds = new Rectangle(craftX - recipeButtonSize - 10, craftY + (size - recipeButtonSize) / 2, recipeButtonSize, recipeButtonSize);
            Texture2D recipeButtonTex = AssetManager.GetTexture("crafting_recepie_button");
            if (recipeButtonTex.Id != 0)
            {
                Raylib.DrawTexturePro(recipeButtonTex, new Rectangle(0, 0, recipeButtonTex.Width, recipeButtonTex.Height),
                    _recipeButtonBounds, Vector2.Zero, 0f, Color.White);
            }
            Raylib.DrawRectangleLinesEx(_recipeButtonBounds, 2, _recipesVisible ? Color.Yellow : Color.DarkGray);

            // Crafting Input Slots
            DrawSlotLogic(craftX, craftY, 100, size, _craftInput1); // Input 1
            DrawSlotLogic(craftX + size + 5, craftY, 101, size, _craftInput2); // Input 2

            // Visual Arrow and Crafting Output slot
            Raylib.DrawText("->", (int)(craftX + (size * 2) + 12), (int)(craftY + size / 2 - 15), 30, Color.White);
            DrawSlotLogic(craftX + (size * 2) + 50, craftY, 102, size, _craftOutput); // Output
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

        // Draw Recipe List
        if (_recipesVisible)
        {
            float recipeListX = startX - 250; // Position to the left of the inventory grid
            float recipeListY = startY;
            float recipeListWidth = 240;
            float recipeListHeight = sh - recipeListY - 20; // Extends to near bottom of screen

            Raylib.DrawRectangle((int)recipeListX, (int)recipeListY, (int)recipeListWidth, (int)recipeListHeight, new Color(0, 0, 0, 180));
            Raylib.DrawText("Craftable Recipes", (int)recipeListX + 10, (int)recipeListY + 10, 20, Color.Yellow);

            // Scissor mode for scrolling
            Raylib.BeginScissorMode((int)recipeListX, (int)recipeListY + 40, (int)recipeListWidth, (int)recipeListHeight - 40);

            int currentRecipeY = (int)(recipeListY + 50 + _recipeScrollOffset); // Start drawing recipes below title
            float recipeItemScale = 0.6f; // Smaller scale for recipe icons
            float recipeItemDrawSize = size * recipeItemScale;

            foreach (var entry in ItemStats.Recipes)
            {
                string input1Id = entry.Key.Item1;
                string input2Id = entry.Key.Item2;
                string outputId = entry.Value;

                if (CanCraft(input1Id, input2Id))
                {
                    DrawRecipeEntry(recipeListX + 10, currentRecipeY, input1Id, input2Id, outputId, recipeItemDrawSize);
                    currentRecipeY += RecipeItemHeight + RecipeItemSpacing;
                }
            }
            Raylib.EndScissorMode();
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
        // No drag from recipe list itself, only from actual inventory/crafting slots
        
        if (draggingCount != -1 && stack.ItemID != "none") {
            stack.Count = draggingCount;
        }
        return stack;
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

    // Helper to check if a recipe can be crafted with current inventory
    private bool CanCraft(string input1Id, string input2Id)
    {
        // Create a temporary inventory to simulate item consumption
        Dictionary<string, int> tempInventory = new Dictionary<string, int>();
        foreach (var slot in inv.Slots)
        {
            if (slot.ItemID != "none" && slot.Count > 0)
            {
                if (tempInventory.ContainsKey(slot.ItemID))
                    tempInventory[slot.ItemID] += slot.Count;
                else
                    tempInventory[slot.ItemID] = slot.Count;
            }
        }

        // Include items from the crafting input slots
        if (_craftInput1.ItemID != "none" && _craftInput1.Count > 0)
        {
            if (tempInventory.ContainsKey(_craftInput1.ItemID)) tempInventory[_craftInput1.ItemID] += _craftInput1.Count;
            else tempInventory[_craftInput1.ItemID] = _craftInput1.Count;
        }
        if (_craftInput2.ItemID != "none" && _craftInput2.Count > 0)
        {
            // Only add if it's not the same item as _craftInput1, or if it is, ensure it's counted correctly
            // This is important for recipes like "wood + wood"
            if (_craftInput1.ItemID != _craftInput2.ItemID || _craftInput1.ItemID == "none")
            {
                if (tempInventory.ContainsKey(_craftInput2.ItemID)) tempInventory[_craftInput2.ItemID] += _craftInput2.Count;
                else tempInventory[_craftInput2.ItemID] = _craftInput2.Count;
            }
        }

        // Try to consume input1
        if (!tempInventory.ContainsKey(input1Id) || tempInventory[input1Id] < 1)
            return false;
        tempInventory[input1Id]--;

        // Try to consume input2
        if (!tempInventory.ContainsKey(input2Id) || tempInventory[input2Id] < 1)
            return false;
        tempInventory[input2Id]--;

        return true;
    }

    // Helper to draw a single recipe entry in the list
    private void DrawRecipeEntry(float x, float y, string input1Id, string input2Id, string outputId, float itemSize)
    {
        // Draw input 1 slot
        Rectangle input1Rect = new Rectangle(x, y, itemSize, itemSize);
        Raylib.DrawRectangleRec(input1Rect, new Color(50, 50, 50, 200)); // Background for slot
        HotbarUI.DrawItem(new ItemStack(input1Id, (input1Id == input2Id) ? 2 : 1), input1Rect);

        // Draw input 2 slot (only if different from input 1)
        Rectangle input2Rect = new Rectangle(x + itemSize + 5, y, itemSize, itemSize);
        if (input1Id != input2Id) Raylib.DrawRectangleRec(input2Rect, new Color(50, 50, 50, 200)); // Background for slot
        if (input1Id != input2Id) HotbarUI.DrawItem(new ItemStack(input2Id, 1), input2Rect);

        Raylib.DrawText("->", (int)(x + itemSize * 2 + 10), (int)(y + itemSize / 2 - 10), 20, Color.White);

        // Draw output slot
        Rectangle outputRect = new Rectangle(x + itemSize * 2 + 40, y, itemSize, itemSize);
        Raylib.DrawRectangleRec(outputRect, new Color(50, 50, 50, 200)); // Background for slot
        HotbarUI.DrawItem(new ItemStack(outputId, 1), outputRect);
    }
}