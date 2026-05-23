using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;

public class LanDiscovery
{
    private const int DiscoveryPort = 44444;
    private static UdpClient? _broadcastClient;
    private static UdpClient? _listenerClient;
    private static bool _isBroadcasting = false;
    private static bool _isListening = false;

    public static Dictionary<string, (int port, DateTime lastSeen)> DiscoveredWorlds = new();

    public static void StartBroadcasting(int gamePort, string worldName)
    {
        if (_isBroadcasting) return;
        _isBroadcasting = true;

        _broadcastClient = new UdpClient();
        _broadcastClient.EnableBroadcast = true;
        IPEndPoint endPoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);

        Thread t = new Thread(() =>
        {
            byte[] data = Encoding.UTF8.GetBytes($"{worldName}:{gamePort}");
            while (_isBroadcasting)
            {
                try { _broadcastClient.Send(data, data.Length, endPoint); }
                catch { break; }
                Thread.Sleep(2000);
            }
        });
        t.IsBackground = true;
        t.Start();
    }

    public static void StopBroadcasting()
    {
        _isBroadcasting = false;
        _broadcastClient?.Close();
        _broadcastClient = null;
    }

    public static void StartListening()
    {
        if (_isListening) return;
        _isListening = true;
        lock (DiscoveredWorlds) { DiscoveredWorlds.Clear(); }

        try 
        {
            _listenerClient = new UdpClient(DiscoveryPort);
            _listenerClient.Client.ReceiveTimeout = 500; // Check _isListening every 500ms
        }
        catch (Exception ex) 
        {
            Console.WriteLine($"[LAN] Failed to bind listener: {ex.Message}");
            return;
        }

        Thread t = new Thread(() =>
        {
            IPEndPoint groupEP = new IPEndPoint(IPAddress.Any, DiscoveryPort);
            while (_isListening)
            {
                try
                {
                    byte[] bytes = _listenerClient.Receive(ref groupEP);
                    string message = Encoding.UTF8.GetString(bytes, 0, bytes.Length);
                    string[] parts = message.Split(':');
                    if (parts.Length == 2 && int.TryParse(parts[1], out int port))
                    {
                        string ip = groupEP.Address.ToString();
                        lock (DiscoveredWorlds) { DiscoveredWorlds[ip] = (port, DateTime.Now); }
                    }
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
                {
                    continue; // Timeout reached, loop back and check _isListening
                }
                catch { break; }
            }
        });
        t.IsBackground = true;
        t.Start();
    }

    public static void StopListening()
    {
        _isListening = false;
        _listenerClient?.Dispose();
        _listenerClient = null;
    }

    public static void Update()
    {
        lock (DiscoveredWorlds)
        {
            var keysToRemove = new List<string>();
            foreach (var kvp in DiscoveredWorlds) { if ((DateTime.Now - kvp.Value.lastSeen).TotalSeconds > 10) keysToRemove.Add(kvp.Key); }
            foreach (var key in keysToRemove) DiscoveredWorlds.Remove(key);
        }
    }
}