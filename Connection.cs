
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
                    byte heldId = _reader.ReadByte();
                    byte offHandId = _reader.ReadByte();
                    bool isBlocking = _reader.ReadBoolean();

                    // Safety check: Don't process if the game state changed
                    if (Program.PlayingState != null)
                    {
                        // If we already know this player, update them
                        if (Program.PlayingState.Others.ContainsKey(name))
                        {
                            Program.PlayingState.Others[name].Position = new Vector2(x, y);
                            Program.PlayingState.Others[name].Rotation = rot;
                            Program.PlayingState.Others[name].HeldItemID = heldId;
                            Program.PlayingState.Others[name].OffHandItemID = offHandId;
                            Program.PlayingState.Others[name].IsBlocking = isBlocking;
                        }
                        // If it's a new player (and not us), add them to the world
                        else if (name != Program.CurrentUser.Username)
                        {
                            Console.WriteLine($"Player {name} entered the vision range.");
                            // Create the player at the received position
                            Player newRemotePlayer = new Player(name, new Vector2(x, y));
                            newRemotePlayer.Color = Raylib_cs.Color.White; // Remote players are white
                            newRemotePlayer.Rotation = rot;
                            newRemotePlayer.HeldItemID = heldId;
                            newRemotePlayer.OffHandItemID = offHandId;
                            newRemotePlayer.IsBlocking = isBlocking;
                            
                            Program.PlayingState.Others[name] = newRemotePlayer;
                        }
                    }
                }
                else if (packetId == 4) 
                {
                    for (int i = 0; i < 25; i++)
                    {
                        byte id = _reader.ReadByte(); // Read 1 byte to match the server
                        int count = _reader.ReadInt32();
                        if (Program.PlayingState != null)
                            Program.PlayingState.PlayerInventory.Slots[i] = new ItemStack(id, count);
                    }
                }
                else if (packetId == 5) // Health Sync
                {
                    int currentHealth = _reader.ReadInt32();
                    int maxHealth = _reader.ReadInt32();

                    // Store this in the PlayingState so the UI can see it
                    if (Program.PlayingState != null)
                    {
                        Program.PlayingState.CurrentHealth = currentHealth;
                        Program.PlayingState.MaxHealth = maxHealth;
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
                        Program.PlayingState.Others.Remove(name);
                        Console.WriteLine($"Player {name} left the world.");
                    }
                }
                else if (packetId == 11) // Raid Update
                {
                    byte type = _reader.ReadByte();
                    float val = _reader.ReadSingle();
                    if (Program.PlayingState != null) {
                        Program.PlayingState.RaidActive = type == 1;
                        if (type == 0) Program.PlayingState.RaidTimer = val;
                        else Program.PlayingState.RaidBossHealth = val;
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

    public void SendMoveItem(byte from, byte to)
    {
        if (!_isConnected || _writer == null) return;
        try {
            _writer.Write((byte)3);
            _writer.Write(from);
            _writer.Write(to);
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

    public bool IsConnected() => _isConnected;

    public void Disconnect()
    {
        _isConnected = false;
        
        try
        {
            _writer?.Close();
            _reader?.Close();
            _client?.Close();
            Console.WriteLine("Disconnected from server safely.");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error during disconnect: {e.Message}");
        }
    }
}