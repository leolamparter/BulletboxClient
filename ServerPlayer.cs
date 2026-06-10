using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;

// Data structure must match client exactly
public struct ServerItemStack {
    public string ItemID;
    public int Count;
    public ServerItemStack(string id, int count) { ItemID = id; Count = count; }
}

public class ServerPlayer
{
    public string Username = "";
    public int Health = 100;
    public int MaxHealth = 100;
    public int Hunger = 100;
    public Vector2 Position = Vector2.Zero; // Server's authoritative position
    public float Rotation = 0f;
    public bool IsBlocking = false;
    public Dimension CurrentDimension = Dimension.Overworld;
    public int ViewRadius = 40;

    private TcpClient _client;
    private NetworkStream _stream;
    private BinaryReader _reader;
    public BinaryWriter Writer;
    private DateTime _lastAttackTime = DateTime.MinValue;
    private DateTime _lastHitTime = DateTime.MinValue;
    public Vector2 Velocity = Vector2.Zero; // For knockback
    public int SelectedSlot = 0;

    public float AshenTime = 0f;
    public float BrimstalkerCooldown = 0f;
    public BiomeType LastKnownBiome = (BiomeType)255; // Track last biome for advancement
    public HashSet<string> TriggeredAdvancements = new();

    public readonly object WriterLock = new();

    // The Server's source of truth
    public ServerItemStack[] Inventory = new ServerItemStack[25];
    public ServerItemStack CraftingSlot1 = new ServerItemStack("none", 0);
    public ServerItemStack CraftingSlot2 = new ServerItemStack("none", 0);
    public Structure? CurrentOpenChest = null;

    public ServerPlayer(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
        _reader = new BinaryReader(_stream);
        Writer = new BinaryWriter(_stream);

        // Initialize empty
        for (int i = 0; i < 25; i++) Inventory[i] = new ServerItemStack("none", 0);
    }

    public async Task Listen(ServerWorld world)
    {
        try
        {
            while (_client.Connected)
            {
                byte packetId = _reader.ReadByte();

                if (packetId == 0) // Login
                {
                    string loginName = _reader.ReadString();
                    string clientVer = _reader.ReadString();
                    _reader.ReadString(); // password
                    
                    // Try to find this specific player in the loaded save dictionary
                    PlayerSaveData? savedData = null;
                    lock (ServerProgram.LoadedPlayers) { ServerProgram.LoadedPlayers.TryGetValue(loginName, out savedData); }
                    if (savedData != null)
                    {
                        Console.WriteLine($"[Server] Found saved data for {loginName}. Applying...");
                        Health = savedData.Health;
                        MaxHealth = savedData.MaxHealth;
                        Hunger = savedData.Hunger;
                        Position = savedData.Position;
                        Rotation = savedData.Rotation;
                        IsBlocking = savedData.IsBlocking;
                        CurrentDimension = savedData.CurrentDimension;
                        SelectedSlot = savedData.SelectedSlot;
                        AshenTime = savedData.AshenTime;
                        BrimstalkerCooldown = savedData.BrimstalkerCooldown;

                        // NEW: Sanitize loaded position to prevent NaN propagation
                        if (float.IsNaN(Position.X) || float.IsNaN(Position.Y) || float.IsInfinity(Position.X) || float.IsInfinity(Position.Y))
                        {
                            Console.WriteLine($"[Server] WARNING: Loaded position for {loginName} contained NaN/Infinity. Resetting to default spawn.");
                            Position = new Vector2(400, 300);
                        }
                        // Create a unique copy of the inventory array for the live session
                        Inventory = (ServerItemStack[])savedData.Inventory.Clone();
                        CraftingSlot1 = savedData.CraftingSlot1;
                        CraftingSlot2 = savedData.CraftingSlot2;

                        // If the player is loading in with no health, treat it as a respawn
                        if (Health <= 0)
                        {
                            Console.WriteLine($"[Server] {loginName} joined while dead. Teleporting to Overworld spawn.");
                            Health = MaxHealth;
                            Hunger = 100;
                            Position = Vector2.Zero;
                            CurrentDimension = Dimension.Overworld;

                            // Clear inventory on death reset
                            for (int i = 0; i < 25; i++) Inventory[i] = new ServerItemStack("none", 0);
                            Inventory[0] = new ServerItemStack("iron_sword", 1); // Always give the starter sword
                            
                            // Clear crafting slots to prevent item duplication
                            CraftingSlot1 = new ServerItemStack("none", 0);
                            CraftingSlot2 = new ServerItemStack("none", 0);
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[Server] No saved data found for {loginName}. Starting fresh.");
                        Position = new Vector2(400, 300); // Set initial position
                        Inventory[0] = new ServerItemStack("iron_sword", 1); // Starting weapon
                    }

                    // After loading/setting position, determine the initial biome
                    var initialChunk = world.GetOrGenerateChunk((int)MathF.Floor(Position.X / 16), (int)MathF.Floor(Position.Y / 16), CurrentDimension);
                    LastKnownBiome = initialChunk.Biome;

                    // Authoritative Sync: Only set the public Username AFTER inventory is safe
                    Username = loginName;

                    // Authoritative Sync: Ensure the world map is updated with the position (loaded or default)
                    world.UpdatePosition(Username, Position.X, Position.Y);

                    lock (WriterLock)
                    {
                        Writer.Write((byte)0);
                        Writer.Write(true);
                        Writer.Write(Position.X);
                        Writer.Write(Position.Y);
                        Writer.Write((byte)CurrentDimension);
                        SendFullInventory();
                        SyncHealth(); // Send initial health state immediately upon login
                    }
                    Console.WriteLine($"[Handshake] {Username} is in.");
                }
                else if (packetId == 1) // Move Player
                {
                    float x = _reader.ReadSingle();
                    float y = _reader.ReadSingle();
                    // NEW: Sanitize incoming position from client
                    if (!float.IsNaN(x) && !float.IsNaN(y) && !float.IsInfinity(x) && !float.IsInfinity(y))
                    {
                        Position = new Vector2(x, y); // Update server's authoritative position
                    }
                    else
                    { Console.WriteLine($"[Server] WARNING: Client {Username} sent NaN/Infinity position. Ignoring update."); }
                    Rotation = _reader.ReadSingle();
                    world.UpdatePosition(Username, Position.X, Position.Y);
                    BroadcastMove(Username, x, y, Rotation, Inventory[SelectedSlot].ItemID, Inventory[24].ItemID, IsBlocking, Health, MaxHealth, CurrentDimension);
                }
                else if (packetId == 2) // Slot Selection
                {
                    byte slot = _reader.ReadByte();
                    if (slot < 24) SelectedSlot = slot;
                }
                else if (packetId == 3) // Move Item Request
                {
                    byte from = _reader.ReadByte();
                    byte to = _reader.ReadByte();
                    int amount = _reader.ReadInt32();

                    // Helper to get a reference to the stack based on index
                    bool IsValid(int idx) => (idx >= 0 && idx < 25) || idx == 100 || idx == 101;
                    
                    if (IsValid(from) && IsValid(to) && amount > 0)
                    {
                        ServerItemStack source = GetStack(from);
                        ServerItemStack target = GetStack(to);
                        int actualAmount = Math.Min(amount, source.Count);

                        if (target.ItemID == "none") {
                            SetStack(to, new ServerItemStack(source.ItemID, actualAmount));
                            source.Count -= actualAmount;
                            SetStack(from, source);
                        } else if (target.ItemID == source.ItemID) {
                            int canTake = 99 - target.Count;
                            int toMove = Math.Min(actualAmount, canTake);
                            target.Count += toMove;
                            source.Count -= toMove;
                            SetStack(to, target);
                            SetStack(from, source);
                        } else if (actualAmount == source.Count) {
                            // Full Swap
                            SetStack(from, target);
                            SetStack(to, source);
                        }

                        // Cleanup empty stacks
                        if (GetStack(from).Count <= 0) SetStack(from, new ServerItemStack("none", 0));
                        
                        SendFullInventory();
                        SendCraftingSlots(CraftingSlot1, CraftingSlot2, GetCraftingPreview());
                    }
                }
                else if (packetId == 10) // Chunk Request
                {
                    int chunkX = _reader.ReadInt32();
                    int chunkY = _reader.ReadInt32();
                    var chunk = world.GetOrGenerateChunk(chunkX, chunkY, CurrentDimension);
                    lock (WriterLock)
                    {
                        Writer.Write((byte)10); 
                        Writer.Write(chunk.Coord.X);
                        Writer.Write(chunk.Coord.Y);
                        Writer.Write((byte)chunk.Biome);
                        Writer.Write((byte)chunk.Feature);
                        Writer.Flush();

                        // Send structure data for this chunk if it exists
                        if (world.Structures.TryGetValue((chunkX, chunkY), out var structure))
                        {
                            Writer.Write((byte)12); // Packet ID 12: Structure Data
                            Writer.Write(structure.ChunkX);
                            Writer.Write(structure.ChunkY);
                            Writer.Write((byte)structure.Type);
                            Writer.Write(structure.Position.X);
                            Writer.Write(structure.Position.Y);
                        }
                    }
                }
                else if (packetId == 12) // Blocking State
                {
                    IsBlocking = _reader.ReadBoolean();
                }
                else if (packetId == 13) // Render Distance Update
                {
                    ViewRadius = _reader.ReadInt32();
                }
                else if (packetId == 6) {
                    string victimName = _reader.ReadString();
                    string heldId = Inventory[SelectedSlot].ItemID; 

                    float elapsed = (float)(DateTime.Now - _lastAttackTime).TotalSeconds;
                    float timeSinceHit = (float)(DateTime.Now - _lastHitTime).TotalSeconds;

                    var (dmg, kb, range) = ServerWeaponStats.Calculate(heldId, elapsed, timeSinceHit);

                    if (dmg > 0) {
                        ServerPlayer? victim;
                        lock (ServerProgram.ConnectedPlayers)
                        {
                            victim = ServerProgram.ConnectedPlayers.Find(p => p.Username == victimName);
                        }

                        if (victim != null) {
                            Vector2 myPos = this.Position; // Use server's authoritative position
                            Vector2 victimPos = world.PlayerLocations[victim.Username];
                            float dist = Vector2.Distance(myPos, victimPos);
                            
                            if (dist <= range) {
                                _lastAttackTime = DateTime.Now; 
                                _lastHitTime = DateTime.Now;   
                                victim.Damage((int)dmg);

                                if (Math.Abs(kb) > 0.1f) {
                                    Vector2 dir = Vector2.Normalize(victimPos - myPos);
                                    lock (victim.WriterLock)
                                    {
                                        victim.Writer.Write((byte)7); 
                                        victim.Writer.Write(dir.X * kb); // Knockback is sent as a force
                                        victim.Writer.Write(dir.Y * kb);
                                        victim.Writer.Flush();
                                    }
                                }
                            }
                        } else {
                            var bot = world.Raiders.Find(b => b.Name == victimName);
                            if (bot != null && bot.Dimension == CurrentDimension && Vector2.Distance(this.Position, bot.Position) <= range) {
                                _lastAttackTime = DateTime.Now;
                                _lastHitTime = DateTime.Now;
                                bot.Health -= (int)dmg;

                                if (Math.Abs(kb) > 0.1f) {
                                    Vector2 dir = Vector2.Normalize(bot.Position - world.PlayerLocations[this.Username]);
                                    bot.Velocity += dir * kb * 15f; // Turn 'distance' into 'velocity'
                                }

                                if (bot.Health <= 0) {
                                    world.Raiders.Remove(bot);
                                    if (bot.Name == "APEX")
                                    {
                                        Vector2 portalPos = Vector2.Zero;
                                        Structure portal = new Structure(portalPos, StructureType.EndPortal, 0, 0, "");
                                        world.Structures.TryAdd((0, 0), portal);
                                    }
                                    // Notify all clients to remove this bot from their screens
                                    lock (ServerProgram.ConnectedPlayers) {
                                        foreach (var p in ServerProgram.ConnectedPlayers)
                                        {
                                            p.SendLeaveSignal(bot.Name);
                                            if (bot.Name == "APEX") ServerProgram.TriggerAdvancement(p, "DefeatApex");
                                            if (bot.Name == "Brimstalker") ServerProgram.TriggerAdvancement(p, "DefeatBrimstalker");
                                            if (bot.Name.StartsWith("Raider")) ServerProgram.TriggerAdvancement(p, "Kill:Raider");
                                            if (bot.Name.StartsWith("Flicker")) ServerProgram.TriggerAdvancement(p, "Kill:Flicker");
                                            if (bot.Name.StartsWith("Vortex")) ServerProgram.TriggerAdvancement(p, "Kill:Vortex");
                                            if (bot.Name == "Brimstalker") ServerProgram.TriggerAdvancement(p, "Kill:Brimstalker");
                                        }
                                    }
                                    if (bot.Name.StartsWith("Raider")) AddItem("raidshroom", Random.Shared.Next(1, 4));
                                    if (bot.Name.StartsWith("Vortex")) AddItem("pearl", Random.Shared.Next(1, 3));
                                    if (bot.Name == "Brimstalker") AddItem("brimstone_powder", Random.Shared.Next(3, 6));
                                }
                            }
                        }
                    } else {
                        _lastAttackTime = DateTime.Now;
                    }
                }
                else if (packetId == 8) // Chat Message
                {
                    string msg = _reader.ReadString();
                    if (msg == "overworld")
                    {
                        CurrentDimension = Dimension.Overworld;
                        Position = Vector2.Zero;
                        world.UpdatePosition(Username, 0, 0);
                        SendDimensionUpdate();
                        BroadcastChat("SYSTEM", $"{Username} returned to the Overworld.");
                        continue;
                    }
                    if (msg.StartsWith("giveitem:"))
                    {
                        string[] parts = msg.Split(':');
                        if (parts.Length >= 2 && parts[1].Length > 0)
                        {
                            string itemId = parts[1];
                            int amount = 1;
                            if (parts.Length >= 3 && int.TryParse(parts[2], out int parsedAmount))
                            {
                                amount = parsedAmount;
                            }
                            
                            AddItem(itemId, amount);
                            
                            // Send a private confirmation message back to the player
                            lock (WriterLock)
                            {
                                Writer.Write((byte)8);
                                Writer.Write("SYSTEM");
                                Writer.Write($"Gave {amount}x '{itemId}'");
                                Writer.Flush();
                            }
                        }
                    }
                    else
                    {
                        BroadcastChat(Username, msg);
                    }
                }
                else if (packetId == 18) // Crafting Request
                {
                    string input1Id = _reader.ReadString();
                    int input1Count = _reader.ReadInt32();
                    string input2Id = _reader.ReadString();
                    int input2Count = _reader.ReadInt32();

                    if (ItemStats.Recipes.TryGetValue((input1Id, input2Id), out string? outputId) ||
                        ItemStats.Recipes.TryGetValue((input2Id, input1Id), out outputId))
                    {
                        // Consume from the actual crafting slots
                        if (CraftingSlot1.Count > 0 && CraftingSlot2.Count > 0)
                        {
                            CraftingSlot1.Count--;
                            if (CraftingSlot1.Count <= 0) CraftingSlot1 = new ServerItemStack("none", 0);
                            CraftingSlot2.Count--;
                            if (CraftingSlot2.Count <= 0) CraftingSlot2 = new ServerItemStack("none", 0);

                            AddItem(outputId!, 1);
                        }
                    }

                    // Send updated inventory and crafting slots back to client
                    SendFullInventory();
                    SendCraftingSlots(CraftingSlot1, CraftingSlot2, GetCraftingPreview());
                }
                else if (packetId == 19) // Open Chest Request
                {
                    int cx = _reader.ReadInt32();
                    int cy = _reader.ReadInt32();
                    if (world.Structures.TryGetValue((cx, cy), out var s) && s.IsCompleted) {
                        CurrentOpenChest = s;
                        SendChestInventory(s.ChestInventory);
                    }
                }
                else if (packetId == 20) // Chest Item Move
                {
                    byte chestIdx = _reader.ReadByte();
                    byte invIdx = _reader.ReadByte();
                    bool toChest = _reader.ReadBoolean();
                    int amount = _reader.ReadInt32();

                    if (CurrentOpenChest != null && CurrentOpenChest.ChestInventory != null && chestIdx < 18 && invIdx < 25) {
                        var src = toChest ? Inventory[invIdx] : CurrentOpenChest.ChestInventory[chestIdx];
                        var dst = toChest ? CurrentOpenChest.ChestInventory[chestIdx] : Inventory[invIdx];
                        amount = Math.Min(amount, src.Count);

                        if (dst.ItemID == "none") {
                            dst = new ServerItemStack(src.ItemID, amount);
                            src.Count -= amount;
                        } else if (dst.ItemID == src.ItemID) {
                            int canTake = 99 - dst.Count;
                            int toMove = Math.Min(amount, canTake);
                            dst.Count += toMove;
                            src.Count -= toMove;
                        } else if (amount == src.Count) {
                            var temp = src;
                            src = dst;
                            dst = temp;
                        }

                        if (src.Count <= 0) src = new ServerItemStack("none", 0);
                        if (toChest) { Inventory[invIdx] = src; CurrentOpenChest.ChestInventory[chestIdx] = dst; }
                        else { CurrentOpenChest.ChestInventory[chestIdx] = src; Inventory[invIdx] = dst; }

                        SendFullInventory();
                        SendChestInventory(CurrentOpenChest.ChestInventory);
                    }
                }
                else if (packetId == 15) // Consume Item Request
                {
                    byte slot = _reader.ReadByte();
                    if (slot < 25 && Inventory[slot].ItemID == "raidshroom" && Inventory[slot].Count > 0 && Hunger < 110)
                    {
                        Hunger = Math.Min(110, Hunger + 15);
                        Inventory[slot].Count--;
                        if (Inventory[slot].Count <= 0)
                        {
                            Inventory[slot].ItemID = "none";
                            Inventory[slot].Count = 0;
                        }
                    }
                    else if (slot < 25 && Inventory[slot].ItemID == "brimstone_pearl" && Inventory[slot].Count > 0)
                    {
                        if (CurrentDimension == Dimension.TheEnd)
                        {
                            BroadcastChat("SYSTEM", "Brimstone Pearls do not work in The End.");
                            continue; // Prevent pearl usage in The End
                        }
                        // Teleport to The End
                        CurrentDimension = Dimension.TheEnd;
                        Position = new Vector2(250, 250); // Reset position away from the exit portal (0,0)
                        world.UpdatePosition(Username, Position.X, Position.Y);
                        SendDimensionUpdate();
                        
                        // Spawn APEX if it doesn't exist in The End (Thread-safe check)
                        lock(world.Raiders)
                        {
                            if (!world.Raiders.Any(r => r.Name == "APEX" && r.Dimension == Dimension.TheEnd))
                            {
                                ServerProgram.SpawnAPEX(world);
                            }
                        }
                        BroadcastChat("SYSTEM", $"{Username} used a Brimstone Pearl and traveled to The End.");
                        
                        Inventory[slot].Count--;
                        if (Inventory[slot].Count <= 0) {
                            Inventory[slot].ItemID = "none";
                            Inventory[slot].Count = 0;
                        }
                    }
                }
            }
        }
        catch (Exception e) { Console.WriteLine($"Client Error: {e.Message}"); }
        finally { world.RemovePlayer(Username); _client.Close(); }
    }

    public void SendFullInventory() {
        lock (WriterLock)
        {
            Writer.Write((byte)4); 
            for (int i = 0; i < 25; i++) {
                Writer.Write(Inventory[i].ItemID);
                Writer.Write(Inventory[i].Count);
            }
            Writer.Flush();
        }
    }

    public void SendChestInventory(ServerItemStack[]? chest) {
        lock (WriterLock) {
            Writer.Write((byte)19);
            for (int i = 0; i < 18; i++) {
                var item = (chest != null && i < chest.Length) ? chest[i] : new ServerItemStack("none", 0);
                Writer.Write(item.ItemID);
                Writer.Write(item.Count);
            }
            Writer.Flush();
        }
    }

    public void SendCraftingSlots(ServerItemStack input1, ServerItemStack input2, ServerItemStack output)
    {
        lock (WriterLock)
        {
            Writer.Write((byte)18); // Packet ID 18 for crafting update
            Writer.Write(input1.ItemID);
            Writer.Write(input1.Count);
            Writer.Write(input2.ItemID);
            Writer.Write(input2.Count);
            Writer.Write(output.ItemID);
            Writer.Write(output.Count);
            Writer.Flush();
        }
    }
    public void AddItem(string itemId, int amount)
    {
        // 1. Try to stack onto existing items of the same ID (limit 99)
        for (int i = 0; i < Inventory.Length; i++)
        {
            if (Inventory[i].ItemID == itemId && Inventory[i].Count < 99)
            {
                int space = 99 - Inventory[i].Count;
                int toAdd = Math.Min(amount, space);
                Inventory[i].Count += toAdd;
                amount -= toAdd;
            }
            if (amount <= 0) break;
        }

        // 2. If still have items, find the first empty slot
        if (amount > 0)
        {
            for (int i = 0; i < Inventory.Length; i++)
            {
                if (Inventory[i].ItemID == "none" || Inventory[i].Count <= 0)
                {
                    Inventory[i].ItemID = itemId;
                    Inventory[i].Count = Math.Min(amount, 99);
                    amount -= Inventory[i].Count;
                }
                if (amount <= 0) break;
            }
        }

        // Trigger Obtain advancements
        if (itemId == "diamond") ServerProgram.TriggerAdvancement(this, "ObtainDiamonds");
        if (itemId == "diamond_sword") ServerProgram.TriggerAdvancement(this, "ObtainDiamondSword");
        if (itemId == "stone_kanabo") ServerProgram.TriggerAdvancement(this, "ObtainKanabo");
        if (itemId == "brimstone_pearl") ServerProgram.TriggerAdvancement(this, "ObtainBrimstonePearl");
        if (itemId.StartsWith("brimstone_") && itemId != "brimstone_powder" && itemId != "brimstone_pearl") 
            ServerProgram.TriggerAdvancement(this, "ObtainBrimstone");
        
        // Check for ObtainAllDiamond (Set of Sword, Axe, Scythe, Spear)
        bool hasS = Inventory.Any(i => i.ItemID == "diamond_sword");
        bool hasA = Inventory.Any(i => i.ItemID == "diamond_axe");
        bool hasSc = Inventory.Any(i => i.ItemID == "diamond_scythe");
        bool hasSp = Inventory.Any(i => i.ItemID == "diamond_spear");
        if (hasS && hasA && hasSc && hasSp) ServerProgram.TriggerAdvancement(this, "ObtainAllDiamond");

        // Check for ObtainAllBrimstone
        bool hasBS = Inventory.Any(i => i.ItemID == "brimstone_sword");
        bool hasBA = Inventory.Any(i => i.ItemID == "brimstone_axe");
        bool hasBSc = Inventory.Any(i => i.ItemID == "brimstone_scythe");
        bool hasBSp = Inventory.Any(i => i.ItemID == "brimstone_spear");
        bool hasBK = Inventory.Any(i => i.ItemID == "brimstone_kanabo");
        if (hasBS && hasBA && hasBSc && hasBSp && hasBK) ServerProgram.TriggerAdvancement(this, "ObtainAllBrimstone");

        SendFullInventory();
    }

    private int FindItemInInventory(string itemId)
    {
        for (int i = 0; i < Inventory.Length; i++)
        {
            if (Inventory[i].ItemID == itemId && Inventory[i].Count > 0) return i;
        }
        return -1;
    }

    private ServerItemStack GetStack(int index) {
        if (index >= 0 && index < 25) return Inventory[index];
        if (index == 100) return CraftingSlot1;
        if (index == 101) return CraftingSlot2;
        return new ServerItemStack("none", 0);
    }

    private void SetStack(int index, ServerItemStack stack) {
        if (index >= 0 && index < 25) Inventory[index] = stack;
        else if (index == 100) CraftingSlot1 = stack;
        else if (index == 101) CraftingSlot2 = stack;
    }

    private ServerItemStack GetCraftingPreview()
    {
        if (ItemStats.Recipes.TryGetValue((CraftingSlot1.ItemID, CraftingSlot2.ItemID), out string? outputId) ||
            ItemStats.Recipes.TryGetValue((CraftingSlot2.ItemID, CraftingSlot1.ItemID), out outputId))
        {
            return new ServerItemStack(outputId!, 1);
        }
        return new ServerItemStack("none", 0);
    }


    private void BroadcastMove(string name, float x, float y, float rot, string heldItemId, string offHandId, bool blocking, int hp, int maxHp, Dimension dimension)
    {
        List<ServerPlayer> playersToNotify;
        lock (ServerProgram.ConnectedPlayers)
        {
            playersToNotify = new List<ServerPlayer>(ServerProgram.ConnectedPlayers);
        }

        foreach (var p in playersToNotify)
        {
            try {
                if (p.Username == name) continue; 
                if (p.CurrentDimension != dimension) continue;
                lock (p.WriterLock)
                {
                    p.Writer.Write((byte)1);
                    p.Writer.Write(name);
                    p.Writer.Write(x);
                    p.Writer.Write(y);
                    p.Writer.Write(rot);
                    p.Writer.Write(heldItemId);
                    // Add offhand data to movement broadcast so others can see it
                    p.Writer.Write(offHandId);
                    p.Writer.Write(blocking);
                    p.Writer.Write(hp);
                    p.Writer.Write(maxHp);
                    p.Writer.Flush();
                }
            } catch { }
        }
    }

    public void BroadcastChat(string sender, string message)
    {
        List<ServerPlayer> playersToNotify;
        lock (ServerProgram.ConnectedPlayers)
        {
            playersToNotify = new List<ServerPlayer>(ServerProgram.ConnectedPlayers);
        }

        foreach (var p in playersToNotify)
        {
            try
            {
                lock (p.WriterLock)
                {
                    p.Writer.Write((byte)8); 
                    p.Writer.Write(sender);
                    p.Writer.Write(message);
                    p.Writer.Flush();
                }
            }
            catch { }
        }
    }

    public void SendLeaveSignal(string username)
    {
        lock (WriterLock)
        {
            Writer.Write((byte)9); // Packet ID 9: Player Left
            Writer.Write(username);
            Writer.Flush();
        }
    }

    public void SendDimensionUpdate()
    {
        lock (WriterLock)
        {
            Writer.Write((byte)21); // Packet ID 21: Dimension Update
            Writer.Write((byte)CurrentDimension);
            Writer.Write(Position.X);
            Writer.Write(Position.Y);
            Writer.Flush();
        }
    }

    public void Damage(int amount) 
    {
        if (IsBlocking) 
        {
            amount = (int)(amount * 0.10f); // 90% reduction
            lock (WriterLock)
            {
                Writer.Write((byte)14); // Packet ID 14: Shield Block Sound Trigger
                Writer.Flush();
            }
        }
        Health -= amount;
        if (Health < 0) Health = 0;
        SyncHealth();
    }

    public void ApplyKnockback(Vector2 force)
    {
        Velocity += force * 15f; // Multiplier to turn 'distance' into 'velocity'
    }

    public void SyncHealth() 
    {
        lock (WriterLock)
        {
            Writer.Write((byte)5); // Packet ID 5: Health Sync
            Writer.Write(Health);
            Writer.Write(MaxHealth);
            Writer.Flush();
        }
    }
}