// ItemStack.cs
public struct ItemStack {
    public byte ItemID; // 'A', 'B', etc. Use ' ' or '\0' for empty.
    public int Count;   // 1 to 99

    public ItemStack(byte id, int count) {
        ItemID = id;
        Count = count;
    }
}

// Inventory.cs
public class Inventory {
    // 6 slots (Hotbar) + 18 slots (3 rows of 6) = 24 total
    public ItemStack[] Slots = new ItemStack[24];

    public Inventory() {
        // Initialize with empty slots
        for (int i = 0; i < Slots.Length; i++) Slots[i] = new ItemStack((byte)' ', 0);
        
        // TEST DATA: Give us some items to see
        Slots[0] = new ItemStack((byte)'A', 10);
        Slots[1] = new ItemStack((byte)'B', 64);
        Slots[6] = new ItemStack((byte)'C', 1); // First slot of main inv
    }
}