using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

public class ServerProgram
{
    public static ServerWorld BulletboxWorld = new ServerWorld();
    public static List<ServerPlayer> ConnectedPlayers = new List<ServerPlayer>();
    public static bool IsRunning = false;

    public static async Task RunServerAsync()
    {
        if (IsRunning) return;
        IsRunning = true;

        TcpListener listener = new TcpListener(IPAddress.Any, 32308);
        listener.Start();
        Console.WriteLine("[Integrated Server] Started on 32308...");

        try
        {
            while (IsRunning)
            {
                if (!listener.Pending()) { await Task.Delay(100); continue; }
                TcpClient clientSocket = await listener.AcceptTcpClientAsync();
                
                ServerPlayer newPlayer = new ServerPlayer(clientSocket);
                
                lock(ConnectedPlayers) { ConnectedPlayers.Add(newPlayer); }
                
                _ = Task.Run(async () => {
                    await newPlayer.Listen(BulletboxWorld);
                    
                    string leavingUser = newPlayer.Username;
                    lock(ConnectedPlayers) 
                    { 
                        ConnectedPlayers.Remove(newPlayer);
                        // Notify all remaining clients that this player is gone
                        foreach(var p in ConnectedPlayers) p.SendLeaveSignal(leavingUser);
                    }
                    Console.WriteLine($"[Server] Player {leavingUser} disconnected.");
                });
            }
        }
        catch (Exception ex) { Console.WriteLine($"[Server] Error: {ex.Message}"); }
        finally { listener.Stop(); IsRunning = false; }
    }
}