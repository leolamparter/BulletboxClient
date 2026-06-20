
using System;
using System.Net.Sockets;
using System.IO;
using System.Threading;
using System.Numerics;


public class Connection
{
    // Biome chunk cache for prototype
    public Dictionary<(int, int), byte> ChunkBiomes = new();
    public Dictionary<(int, int), byte> ChunkFeatures = new();
    public readonly object ChunkBiomesLock = new();

    // Structure cache
    public Dictionary<(int, int), Structure> Structures = new();
    public readonly object StructuresLock = new();

    public Dimension CurrentDimension = Dimension.Overworld;
        public void SendChunkRequest(int chunkX, int chunkY)
        {
            if (!_isConnected || _writer == null) return;
            try
            {
                _writer.Write((byte)10); // Packet ID 10 for chunk request
                _writer.Write(chunkX);
                _writer.Write(chunkY);
                _writer.Flush();
            }
            catch { _isConnected = false; }
        }
    private TcpClient? _client;
    private BinaryWriter? _writer;
    private BinaryReader? _reader;
    private bool _isConnected = false;

    public void Connect(string ip, int port, string user, string pass)
    {
        try
        {
            _client = new TcpClient(ip, port);
            var stream = _client.GetStream();
            _writer = new BinaryWriter(stream);
            _reader = new BinaryReader(stream);

            // 1. Send Login
            _writer.Write((byte)0);
            _writer.Write(user);
            _writer.Write("26.1.1-02a"); // Send client version to match server expectations
            _writer.Write(pass);
            _writer.Flush(); 

            // 2. Start the background thread immediately
            // Let the background thread handle reading the success/fail
            _isConnected = true;
            Thread t = new Thread(Listen);
            t.IsBackground = true;
            t.Start();
            
            Console.WriteLine("Connection request sent...");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Connection failed: {e.Message}");
        }
    }

    private void Listen()
    {
        try
        { 
            while (_isConnected && _reader != null)
            {
                // 1. Read the Packet ID first
                // This line will 'pause' the thread until the server sends 1 byte
                byte packetId = _reader.ReadByte();

                if (packetId == 0) // SERVER RESPONSE: LOGIN SUCCESS
                {
                    bool success = _reader.ReadBoolean();
                    if (success) 
                    {
                        Console.WriteLine("Server confirmed login. World synchronization active.");
                        float startX = _reader.ReadSingle();
                        float startY = _reader.ReadSingle();
                        byte startDim = _reader.ReadByte();

                        CurrentDimension = (Dimension)startDim;
                        if (Program.PlayingState != null)
                        {
                            Program.PlayingState.LocalPlayer.Position = new Vector2(startX, startY);
                            Console.WriteLine($"[Network] Teleported to saved position: {startX}, {startY}");
                        }
                    }
                    else 
                    {
                        Console.WriteLine("Server rejected login. Disconnecting...");
                        _isConnected = false;
                    }
                }
                else if (packetId == 1) // SERVER BROADCAST: PLAYER MOVED
                {
                    string name = _reader.ReadString();
                    float x = _reader.ReadSingle();
                    float y = _reader.ReadSingle();
                    float rot = _reader.ReadSingle();
                    string heldId = _reader.ReadString();
                    string offHandId = _reader.ReadString();
                    bool isBlocking = _reader.ReadBoolean();
                    int hp = _reader.ReadInt32();
                    int maxHp = _reader.ReadInt32();
                    bool isAttacking = _reader.ReadBoolean();
                    bool isHostile = _reader.ReadBoolean();

                    // Safety check: Don't process if the game state changed
                    var playingState = Program.PlayingState;
                    if (playingState != null)
                    {
                        lock (playingState.OthersLock)
                        {
                            // Sanity Check: Ignore insane coordinates caused by potential stream corruption
                            if (float.IsNaN(x) || float.IsInfinity(x) || Math.Abs(x) > 1000000) return;

                            if (playingState.Others.TryGetValue(name, out var other))
                            {
                                other.Position = new Vector2(x, y);
                                other.Rotation = rot;
                                other.HeldItemID = heldId;
                                other.OffHandItemID = offHandId;
                                other.IsBlocking = isBlocking;
                                other.Health = hp;
                                other.MaxHealth = maxHp;
                                other.IsHostile = isHostile;
                                if (isAttacking) other.TriggerAttack();
                                // If this is the APEX entity, mark presence so UI can show boss bar
                                if (name == "APEX") playingState.ApexPresent = true;
                            }
                            else if (Program.CurrentUser != null && name != Program.CurrentUser.Username)
                            {
                                Console.WriteLine($"Player {name} entered the vision range.");
                                Player newRemotePlayer = new Player(name, new Vector2(x, y));
                                newRemotePlayer.Color = Raylib_cs.Color.White; // Remote players are white
                                newRemotePlayer.Rotation = rot;
                                newRemotePlayer.HeldItemID = heldId;
                                newRemotePlayer.OffHandItemID = offHandId;
                                newRemotePlayer.IsBlocking = isBlocking;
                                newRemotePlayer.Health = hp;
                                newRemotePlayer.MaxHealth = maxHp;
                                newRemotePlayer.IsHostile = isHostile;
                                if (isAttacking) newRemotePlayer.TriggerAttack();
                                playingState.Others[name] = newRemotePlayer;
                                if (name == "APEX") playingState.ApexPresent = true;
                            }
                        }
                    }
                }
                else if (packetId == 4) 
                {
                    for (int i = 0; i < 25; i++)
                    {
                        string id = _reader.ReadString();
                        int count = _reader.ReadInt32();
                        if (Program.PlayingState != null)
                            Program.PlayingState.PlayerInventory.Slots[i] = new ItemStack(id, count);
                    }
                }
                else if (packetId == 5) // Health Sync
                {
                    int currentHealth = _reader.ReadInt32();
                    int maxHealth = _reader.ReadInt32();
                    int currentHunger = _reader.ReadInt32();

                    // Store this in the PlayingState so the UI can see it
                    if (Program.PlayingState != null)
                    {
                        Program.PlayingState.CurrentHealth = currentHealth;
                        Program.PlayingState.MaxHealth = maxHealth;
                        Program.PlayingState.CurrentHunger = currentHunger;
                    }
                }
                else if (packetId == 7) // Knockback Force
                {
                    float forceX = _reader.ReadSingle();
                    float forceY = _reader.ReadSingle();
                    if (Program.PlayingState != null)
                        Program.PlayingState.ApplyKnockback(new Vector2(forceX, forceY));
                }
                else if (packetId == 10) // Chunk Data
                {
                    int chunkX = _reader.ReadInt32();
                    int chunkY = _reader.ReadInt32();
                    byte biome = _reader.ReadByte();
                    byte feature = _reader.ReadByte();
                    lock (ChunkBiomesLock)
                    {
                        ChunkBiomes[(chunkX, chunkY)] = biome;
                        ChunkFeatures[(chunkX, chunkY)] = feature;
                    }
                }
                else if (packetId == 8) // Incoming Chat
                {
                    string sender = _reader.ReadString();
                    string msg = _reader.ReadString();
                    if (Program.PlayingState != null)
                        Program.PlayingState.AddChatMessage(sender, msg);
                }
                else if (packetId == 9) // SERVER BROADCAST: PLAYER LEFT
                {
                    string name = _reader.ReadString();
                    if (Program.PlayingState != null)
                    {
                        lock (Program.PlayingState.OthersLock)
                        {
                            if (Program.PlayingState.Others.Remove(name, out var p))
                            {
                                lock (Program.PlayingState.UnloadQueueLock) Program.PlayingState.UnloadQueue.Add(p);
                            }
                        }
                        Console.WriteLine($"Player {name} left the world.");
                    }
                }
                else if (packetId == 11) // Raid Update
                {
                    byte type = _reader.ReadByte();
                    float val = _reader.ReadSingle();
                    bool hasOutpostPos = _reader.ReadBoolean(); // Read the new flag
                    Vector2? outpostPos = null;
                    if (hasOutpostPos)
                    {
                        float outpostX = _reader.ReadSingle();
                        float outpostY = _reader.ReadSingle();
                        outpostPos = new Vector2(outpostX, outpostY);
                    }

                    if (Program.PlayingState != null) {
                        if (type == 0) Program.PlayingState.RaidTimer = val;
                        else {
                            Program.PlayingState.RaidBossHealth = val;
                            Program.PlayingState.RaidActive = val > 0;
                        }
                        // Update the fixed outpost position on the client
                        Program.PlayingState.SetActiveRaidOutpost(outpostPos);
                    }
                }
                else if (packetId == 12) // Structure Data
                {
                    int chunkX = _reader.ReadInt32();
                    int chunkY = _reader.ReadInt32();
                    StructureType type = (StructureType)_reader.ReadByte();
                    float posX = _reader.ReadSingle();
                    float posY = _reader.ReadSingle();
                    bool isCompleted = _reader.ReadBoolean();
                    lock (StructuresLock)
                    {
                        if (type == StructureType.None)
                        {
                            Structures.Remove((chunkX, chunkY));
                        }
                        else
                        {
                            var s = new Structure(new Vector2(posX, posY), type, chunkX, chunkY, "");
                            s.IsCompleted = isCompleted;
                            Structures[(chunkX, chunkY)] = s;
                        }
                    }
                }
                else if (packetId == 14) // Shield Block Sound Trigger
                {
                    AudioManager.PlaySoundMulti("shield_block");
                }
                else if (packetId == 16) // Incoming Bomb
                {
                    float startX = _reader.ReadSingle();
                    float startY = _reader.ReadSingle();
                    float velX = _reader.ReadSingle();
                    float velY = _reader.ReadSingle();

                    if (Program.PlayingState != null) {
                        Program.PlayingState.SpawnVisualBomb(new Vector2(startX, startY), new Vector2(velX, velY));
                    }
                }
                else if (packetId == 17) // Incoming Gust (NEW)
                {
                    float startX = _reader.ReadSingle();
                    float startY = _reader.ReadSingle();
                    float velX = _reader.ReadSingle();
                    float velY = _reader.ReadSingle();

                    if (Program.PlayingState != null) {
                        Program.PlayingState.SpawnVisualGust(new Vector2(startX, startY), new Vector2(velX, velY));
                    }
                }
                else if (packetId == 18) // Crafting Update
                {
                    string input1Id = _reader.ReadString();
                    int input1Count = _reader.ReadInt32();
                    string input2Id = _reader.ReadString();
                    int input2Count = _reader.ReadInt32();
                    string outputId = _reader.ReadString();
                    int outputCount = _reader.ReadInt32();

                    if (Program.PlayingState != null) {
                        Program.PlayingState.InvMenu.UpdateCraftingSlots(
                            new ItemStack(input1Id, input1Count), new ItemStack(input2Id, input2Count), new ItemStack(outputId, outputCount));
                    }
                }
                else if (packetId == 19) // Chest Data Sync
                {
                    ItemStack[] chestSlots = new ItemStack[18];
                    for (int i = 0; i < 18; i++) {
                        string id = _reader.ReadString();
                        int count = _reader.ReadInt32();
                        chestSlots[i] = new ItemStack(id, count);
                    }
                    if (Program.PlayingState != null) 
                        Program.PlayingState.InvMenu.OpenChestUI(chestSlots);
                }
                else if (packetId == 21) // Dimension Update
                {
                    byte dim = _reader.ReadByte();
                    float newX = _reader.ReadSingle();
                    float newY = _reader.ReadSingle();

                    if (CurrentDimension == Dimension.TheEnd && (Dimension)dim == Dimension.Overworld) Program.IsEnding = false; // Reset cinematic state if returning from The End
                    CurrentDimension = (Dimension)dim;
                    lock (ChunkBiomesLock)
                    {
                        ChunkBiomes.Clear();
                        ChunkFeatures.Clear();
                    }
                    lock (StructuresLock)
                    {
                        Structures.Clear();
                    }
                    if (Program.PlayingState != null)
                    {
                        Program.PlayingState.LocalPlayer.Position = new Vector2(newX, newY);
                        Program.PlayingState.RaidActive = false;
                        Program.PlayingState.TriggerCacheClear();
                        // Changing dimension should clear APEX presence on client until re-synced
                        Program.PlayingState.ApexPresent = false;
                    }
                }
                else if (packetId == 25) // Advancement Trigger
                {
                    string advancementId = _reader.ReadString();
                    bool showPopup = _reader.ReadBoolean();
                    Console.WriteLine($"[Network] Advancement Progress: {advancementId}");

                    // 1. World-Specific Tracking (Tracked by current session and saved in world .db)
                    Program.PlayingState?.GrantAdvancement(advancementId, showPopup);

                    // 2. User-Specific Tracking (Global profile, saved in user_data.json)
                    if (Program.CurrentUser != null)
                    {
                        if (advancementId.StartsWith("EnterBiome:"))
                        {
                            if (byte.TryParse(advancementId.Split(':')[1], out byte bId))
                                if (!Program.CurrentUser.VisitedBiomes.Contains(bId)) Program.CurrentUser.VisitedBiomes.Add(bId);
                        }
                        else if (advancementId.StartsWith("Kill:"))
                        {
                            string mob = advancementId.Split(':')[1];
                            if (!Program.CurrentUser.KilledOverworld.Contains(mob)) Program.CurrentUser.KilledOverworld.Add(mob);
                        }
                        else
                        {
                            Program.CurrentUser.Advancements[advancementId] = true;
                        }
                        SaveManager.Save(Program.CurrentUser);
                    }
                }
                else if (packetId == 26) // Entity Despawned
                {
                    string name = _reader.ReadString();
                    var playingState = Program.PlayingState;
                    if (playingState != null)
                    {
                        lock (playingState.RecentlyDespawnedLock)
                        {
                            playingState.RecentlyDespawned.Add(name);
                        }
                        lock (playingState.OthersLock)
                        {
                            if (playingState.Others.Remove(name, out var p))
                            {
                                lock (playingState.UnloadQueueLock) playingState.UnloadQueue.Add(p);
                            }
                        }
                            // If the APEX entity despawned, clear the presence flag
                            if (name == "APEX") playingState.ApexPresent = false;
                            Console.WriteLine($"Entity {name} despawned.");
                    }
                }
                else if (packetId == 28) // Play Sound at Position
                {
                    string soundKey = _reader.ReadString();
                    float x = _reader.ReadSingle();
                    float y = _reader.ReadSingle();
                    if (Program.PlayingState != null)
                    {
                        // Play sound at a world position, client will handle volume/panning
                        // For now, just play it globally. Advanced audio would use distance.
                        AudioManager.PlaySoundMulti(soundKey);
                    }
                }
            }
        }
        catch (EndOfStreamException)
        {
            Console.WriteLine("Server closed the connection.");
            _isConnected = false;
        }
        catch (Exception e)
        {
            Console.WriteLine($"Network Listen Error: {e.Message}");
            _isConnected = false;
        }
        finally
        {
            // Clean up resources if the loop breaks
            _isConnected = false;
            _client?.Close();
        }
    }

    public void SendPosition(float x, float y, float rotation)
    {
        if (!_isConnected || _writer == null) return;
        try
        {
            _writer.Write((byte)1); // Movement Packet ID
            _writer.Write(x);
            _writer.Write(y);
            _writer.Write(rotation);
            _writer.Flush();
        }
        catch { _isConnected = false; }
    }

    public void SendDash(Vector2 direction)
    {
        if (!_isConnected || _writer == null) return;
        try {
            _writer.Write((byte)27); // Packet ID 27: Dash
            _writer.Write(direction.X);
            _writer.Write(direction.Y);
            _writer.Flush();
        }
        catch { _isConnected = false; }
    }

    public void SendSlotSwap(byte slot)
    {
        if (!_isConnected || _writer == null) return;
        try
        {
            _writer.Write((byte)2); // Packet ID 2 for Slot Swapping
            _writer.Write(slot);
            _writer.Flush();
        }
        catch { _isConnected = false; }
    }

    public void SendConsumeItem(byte slot)
    {
        if (!_isConnected || _writer == null) return;
        try
        {
            _writer.Write((byte)15); // Packet ID 15 for item consumption
            _writer.Write(slot);
            _writer.Flush();
        }
        catch { _isConnected = false; }
    }

    public void SendMoveItem(byte from, byte to, int count)
    {
        if (!_isConnected || _writer == null) return;
        try {
            _writer.Write((byte)3);
            _writer.Write(from);
            _writer.Write(to);
            _writer.Write(count);
            _writer.Flush();
        } catch { _isConnected = false; }
    }

    public void SendAttack(string targetName)
    {
        if (!_isConnected || _writer == null) return;
        try
        {
            _writer.Write((byte)6); // Packet ID 6
            _writer.Write(targetName);
            _writer.Flush();
        }
        catch { _isConnected = false; }
    }

    public void SendBlockingState(bool isBlocking)
    {
        if (!_isConnected || _writer == null) return;
        try {
            _writer.Write((byte)12); // Packet ID 12: Blocking State
            _writer.Write(isBlocking);
            _writer.Flush();
        } catch { _isConnected = false; }
    }

    public void SendRenderDistance(int radius)
    {
        if (!_isConnected || _writer == null) return;
        try
        {
            _writer.Write((byte)13); // Packet ID 13: Render Distance Update
            _writer.Write(radius);
            _writer.Flush();
        }
        catch { _isConnected = false; }
    }

    public void SendChat(string message)
    {
        if (!_isConnected || _writer == null) return;
        try
        {
            _writer.Write((byte)8); // Packet ID 8
            _writer.Write(message);
            _writer.Flush();
        }
        catch { _isConnected = false; }
    }

    public void SendCraftRequest(string input1Id, int input1Count, string input2Id, int input2Count)
    {
        if (!_isConnected || _writer == null) return;
        try
        {
            _writer.Write((byte)18); // Packet ID 18 for crafting request
            _writer.Write(input1Id);
            _writer.Write(input1Count);
            _writer.Write(input2Id);
            _writer.Write(input2Count);
            _writer.Flush();
        }
        catch { _isConnected = false; }
    }

    public void SendOpenChest(int chunkX, int chunkY)
    {
        if (!_isConnected || _writer == null) return;
        try {
            _writer.Write((byte)19);
            _writer.Write(chunkX);
            _writer.Write(chunkY);
            _writer.Flush();
        } catch { _isConnected = false; }
    }

    public void SendChestMove(byte chestSlot, byte invSlot, bool toChest, int count)
    {
        if (!_isConnected || _writer == null) return;
        try {
            _writer.Write((byte)20);
            _writer.Write(chestSlot);
            _writer.Write(invSlot);
            _writer.Write(toChest);
            _writer.Write(count);
            _writer.Flush();
        } catch { _isConnected = false; }
    }

    public void SendPlaySoundAtPosition(string soundKey, Vector2 position)
    {
        if (!_isConnected || _writer == null) return;
        try
        {
            _writer.Write((byte)28); // Packet ID 28: Play Sound at Position
            _writer.Write(soundKey);
            _writer.Write(position.X);
            _writer.Write(position.Y);
            _writer.Flush();
        }
        catch { _isConnected = false; }
    }
    public bool IsConnected() => _isConnected;

    public void Disconnect()
    {
        _isConnected = false;
        CurrentDimension = Dimension.Overworld;
        
        try
        {
            _writer?.Close();
            _reader?.Close();
            _client?.Close();
            
            // Clear local caches so state is fresh for the next life/connection
            lock (StructuresLock) Structures.Clear();
            lock (ChunkBiomesLock) {
                ChunkBiomes.Clear();
                ChunkFeatures.Clear();
            }
            Console.WriteLine("Disconnected from server safely.");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error during disconnect: {e.Message}");
        }
    }
}