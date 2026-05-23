using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

public class ServerProgram
{
    public static ServerWorld BulletboxWorld = new ServerWorld();
    public static List<ServerPlayer> ConnectedPlayers = new List<ServerPlayer>();
    private static bool _isRunning = false;

    public static async Task RunServerAsync()
    {
        if (_isRunning) return;
        _isRunning = true;

        TcpListener listener = new TcpListener(IPAddress.Any, 32308);
        listener.Start();
        Console.WriteLine("[Integrated Server] Started on 32308...");

        while (_isRunning)
        {
            TcpClient clientSocket = await listener.AcceptTcpClientAsync();
            
            ServerPlayer newPlayer = new ServerPlayer(clientSocket);
            
            lock(ConnectedPlayers) { ConnectedPlayers.Add(newPlayer); }
            
            _ = Task.Run(async () => {
                await newPlayer.Listen(BulletboxWorld);
                
                lock(ConnectedPlayers) { ConnectedPlayers.Remove(newPlayer); }
            });
        }
        listener.Stop();
    }
}