using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Numerics;
using System.Collections.Generic;

// Data structure must match client exactly
public struct ServerItemStack {
    public byte ItemID;
    public int Count;
    public ServerItemStack(byte id, int count) { ItemID = id; Count = count; }
}

public class ServerPlayer
{
    public string Username = "";
    public int Health = 100;
    public int MaxHealth = 100;
    public float Rotation = 0f;

    private TcpClient _client;
    private NetworkStream _stream;
    private BinaryReader _reader;
    public BinaryWriter Writer;
    private DateTime _lastAttackTime = DateTime.MinValue;
    private DateTime _lastHitTime = DateTime.MinValue;
    public int SelectedSlot = 0;

    public readonly object WriterLock = new();

    // The Server's source of truth
    public ServerItemStack[] Inventory = new ServerItemStack[24];

    public ServerPlayer(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
        _reader = new BinaryReader(_stream);
        Writer = new BinaryWriter(_stream);

        // Initialize empty
        for (int i = 0; i < 24; i++) Inventory[i] = new ServerItemStack((byte)' ', 0);
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
                    Username = _reader.ReadString();
                    string clientVer = _reader.ReadString();
                    _reader.ReadString(); // password
                    
                    world.UpdatePosition(Username, 400, 300);
                    
                    Inventory[0] = new ServerItemStack((byte)'S', 1); // Sword
                    Inventory[1] = new ServerItemStack((byte)'A', 1); // Axe
                    Inventory[2] = new ServerItemStack((byte)'D', 1); // Dagger
                    Inventory[3] = new ServerItemStack((byte)'P', 1); // Spear
                    Inventory[4] = new ServerItemStack((byte)'Y', 1); // Scythe
                    Inventory[5] = new ServerItemStack((byte)'K', 1); // Kanabo

                    lock (WriterLock)
                    {
                        Writer.Write((byte)0);
                        Writer.Write(true);
                        SendFullInventory();
                        SyncHealth(); // Send initial health state immediately upon login
                    }
                    Console.WriteLine($"[Handshake] {Username} is in.");
                }
                else if (packetId == 1) // Move Player
                {
                    float x = _reader.ReadSingle();
                    float y = _reader.ReadSingle();
                    Rotation = _reader.ReadSingle();
                    world.UpdatePosition(Username, x, y);
                    BroadcastMove(Username, x, y, Rotation, Inventory[SelectedSlot].ItemID);
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
                    
                    if (from < 24 && to < 24)
                    {
                        // Swap items in server memory
                        ServerItemStack temp = Inventory[from];
                        Inventory[from] = Inventory[to];
                        Inventory[to] = temp;
                        SendFullInventory();
                    }
                }
                else if (packetId == 10) // Chunk Request
                {
                    int chunkX = _reader.ReadInt32();
                    int chunkY = _reader.ReadInt32();
                    var chunk = world.GetOrGenerateChunk(chunkX, chunkY);
                    lock (WriterLock)
                    {
                        Writer.Write((byte)10); 
                        Writer.Write(chunk.Coord.X);
                        Writer.Write(chunk.Coord.Y);
                        Writer.Write((byte)chunk.Biome);
                        Writer.Write((byte)chunk.Feature);
                        Writer.Flush();
                    }
                }
                else if (packetId == 6) {
                    string victimName = _reader.ReadString();
                    byte heldId = Inventory[SelectedSlot].ItemID; 

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
                            Vector2 myPos = world.PlayerLocations[this.Username];
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
                                        victim.Writer.Write(dir.X * kb);
                                        victim.Writer.Write(dir.Y * kb);
                                        victim.Writer.Flush();
                                    }
                                }
                            }
                        }
                    }
                    else {
                        _lastAttackTime = DateTime.Now;
                    }
                }
                else if (packetId == 8) // Chat Message
                {
                    string msg = _reader.ReadString();
                    BroadcastChat(Username, msg);
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
            for (int i = 0; i < 24; i++) {
                Writer.Write(Inventory[i].ItemID);
                Writer.Write(Inventory[i].Count);
            }
            Writer.Flush();
        }
    }

    private void BroadcastMove(string name, float x, float y, float rot, byte heldItemId)
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
                lock (p.WriterLock)
                {
                    p.Writer.Write((byte)1);
                    p.Writer.Write(name);
                    p.Writer.Write(x);
                    p.Writer.Write(y);
                    p.Writer.Write(rot);
                    p.Writer.Write(heldItemId);
                    p.Writer.Flush();
                }
            } catch { }
        }
    }

    private void BroadcastChat(string sender, string message)
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

    public void Damage(int amount) 
    {
        Health -= amount;
        if (Health < 0) Health = 0;
        SyncHealth();
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