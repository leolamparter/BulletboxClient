// ItemStack.cs
public struct ItemStack {
    public string ItemID { get; set; } // e.g. "sword", "none" for empty.
    public int Count { get; set; }   // 1 to 99

    public ItemStack(string id, int count) {
        ItemID = id;
        Count = count;
    }
}

// Inventory.cs
public class Inventory {
    // 6 slots (Hotbar) + 18 slots (3 rows of 6) = 24 total
    public ItemStack[] Slots = new ItemStack[25]; // 24 standard + 1 Off-hand

    public Inventory() {
        // Initialize with empty slots
        for (int i = 0; i < Slots.Length; i++) Slots[i] = new ItemStack("none", 0);
        
        // TEST DATA: Give us some items to see
        Slots[0] = new ItemStack("iron_sword", 1);
        Slots[1] = new ItemStack("raidshroom", 1);
        Slots[2] = new ItemStack("raidshroom", 1);
        Slots[3] = new ItemStack("none", 0);
        Slots[4] = new ItemStack("raidshroom", 1);
        Slots[5] = new ItemStack("none", 0);
    }
}