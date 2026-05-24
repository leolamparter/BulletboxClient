using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Numerics;

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

        // Start the World Logic Tick (AI and Raid)
        _ = Task.Run(async () => {
            Random rand = new Random();
            while (IsRunning) {
                await Task.Delay(16); // ~60 FPS
                float dt = 0.016f;

                if (!BulletboxWorld.RaidActive) {
                    BulletboxWorld.RaidTimer -= dt;
                    if (BulletboxWorld.RaidTimer <= 0) {
                        BulletboxWorld.RaidActive = true;
                        
                        Vector2 spawnCenter = Vector2.Zero;
                        lock(ConnectedPlayers) { 
                            if (ConnectedPlayers.Count > 0) 
                                spawnCenter = BulletboxWorld.PlayerLocations.GetValueOrDefault(ConnectedPlayers[0].Username, Vector2.Zero);
                        }

                        for (int i = 0; i < 5; i++) {
                            float angle = (float)(rand.NextDouble() * Math.PI * 2);
                            float dist = 660f; // Just past chunkViewRadius (40 * 16 = 640)
                            Vector2 spawnPos = spawnCenter + new Vector2(MathF.Cos(angle) * dist, MathF.Sin(angle) * dist);
                            
                            var bot = new RaiderBot($"Raider {rand.Next(1000, 9999)}", spawnPos);
                            int weaponRoll = rand.Next(3);
                            bot.HeldItemID = weaponRoll == 0 ? (byte)'S' : (weaponRoll == 1 ? (byte)'K' : (byte)'P');
                            bot.AttackCooldown = weaponRoll == 0 ? 0.425f : (weaponRoll == 1 ? 0.85f : 0.65f);
                            
                            BulletboxWorld.Raiders.Add(bot);
                        }
                    }
                    // Broadcast Countdown (Packet 11, Type 0)
                    BroadcastRaidUpdate(0, BulletboxWorld.RaidTimer);
                } else {
                    UpdateRaiderAI(dt);
                    
                    float totalHp = 0, totalMax = 0;
                    foreach(var b in BulletboxWorld.Raiders) { totalHp += b.Health; totalMax += b.MaxHealth; }
                    
                    if (BulletboxWorld.Raiders.Count == 0 || totalHp <= 0) {
                        BulletboxWorld.RaidActive = false;
                        BulletboxWorld.RaidTimer = 60f;
                        BulletboxWorld.Raiders.Clear();
                        lock(ConnectedPlayers) {
                            foreach(var p in ConnectedPlayers) {
                                p.Health = p.MaxHealth;
                                p.SyncHealth();
                            }
                        }
                    }
                    // Broadcast Bossbar (Packet 11, Type 1)
                    BroadcastRaidUpdate(1, totalMax > 0 ? totalHp / totalMax : 0);
                }
            }
        });

        void UpdateRaiderAI(float dt) {
            Random rand = new Random();
            float time = (float)(DateTime.Now.Ticks / 10000000.0); // Simple time reference in seconds
            
            // Use a copy to prevent "Collection modified" errors if a bot dies during iteration
            List<RaiderBot> botsToUpdate;
            lock(BulletboxWorld.Raiders) { botsToUpdate = new List<RaiderBot>(BulletboxWorld.Raiders); }

            foreach (var bot in botsToUpdate) {
                // Apply and Decay Knockback Velocity
                bot.Position += bot.Velocity * dt;
                bot.Velocity = Vector2.Lerp(bot.Velocity, Vector2.Zero, dt * 6.5f);

                ServerPlayer? target = null;
                float minDist = float.MaxValue;
                lock(ConnectedPlayers) {
                    foreach(var p in ConnectedPlayers) {
                        float d = Vector2.Distance(bot.Position, BulletboxWorld.PlayerLocations.GetValueOrDefault(p.Username, Vector2.Zero));
                        if (d < minDist) { minDist = d; target = p; }
                    }
                }

                float visionRange = 45 * 16; // 45 chunks
                if (target != null && minDist < visionRange) {
                    bot.WanderTarget = null; // Clear wander if we see a player
                    
                    Vector2 targetPos = BulletboxWorld.PlayerLocations[target.Username];
                    Vector2 dir = Vector2.Normalize(targetPos - bot.Position);
                    
                    // Flee Logic
                    if (bot.Health < 30 && bot.FleeTimer <= 0 && rand.Next(100) < 1) bot.FleeTimer = 8.0f;
                    
                    if (bot.FleeTimer > 0) {
                        bot.FleeTimer -= dt;
                        dir = -dir; // Run away
                    }

                    // Movement Variety: High intensity strafing
                    Vector2 sideStepDir = new Vector2(-dir.Y, dir.X); // Perpendicular to target
                    float phase = bot.Name.GetHashCode() % 100; // Unique offset per bot
                    float strafeAmount = MathF.Sin(time * 3.5f + phase) * 280f; 
                    
                    bot.Rotation = (float)(Math.Atan2(dir.Y, dir.X) * (180.0 / Math.PI)) + (MathF.Sin(time * 5f + phase) * 8f);
                    
                    // Always keep moving, but slow down slightly when in melee range
                    float moveSpeed = (minDist < 100) ? 120f : 250f;
                    float finalSpeed = moveSpeed + (bot.Name.GetHashCode() % 40);
                    bot.Position += (dir * finalSpeed + sideStepDir * strafeAmount) * dt;

                    // Attack logic (Sword, Kanabo, or Spear)
                    float attackRange = bot.HeldItemID == (byte)'P' ? 180f : 120f;
                    bot.AttackTimer += dt;
                    if (minDist < attackRange && bot.AttackTimer >= bot.AttackCooldown && bot.FleeTimer <= 0) {
                        target.Damage(bot.HeldItemID == (byte)'K' ? 25 : 12);
                        bot.AttackTimer = 0;
                    }
                } else {
                    // Wandering Behavior
                    if (bot.WanderTarget == null) {
                        bot.WanderWaitTimer -= dt;
                        if (bot.WanderWaitTimer <= 0) {
                            float rx = (float)(rand.NextDouble() * 320 - 160); // within 10 chunks
                            float ry = (float)(rand.NextDouble() * 320 - 160);
                            bot.WanderTarget = bot.Position + new Vector2(rx, ry);
                            bot.WanderWaitTimer = 2.0f;
                        }
                    } else {
                        Vector2 wDir = Vector2.Normalize(bot.WanderTarget.Value - bot.Position);
                        bot.Rotation = (float)(Math.Atan2(wDir.Y, wDir.X) * (180.0 / Math.PI));
                        bot.Position += wDir * 100f * dt;
                        if (Vector2.Distance(bot.Position, bot.WanderTarget.Value) < 10f) bot.WanderTarget = null;
                    }
                }
                BroadcastBotMove(bot);
            }
        }

        void BroadcastRaidUpdate(byte type, float val) {
            lock (ConnectedPlayers) {
                foreach (var p in ConnectedPlayers) {
                    try { lock (p.WriterLock) { p.Writer.Write((byte)11); p.Writer.Write(type); p.Writer.Write(val); p.Writer.Flush(); } } catch { }
                }
            }
        }

        void BroadcastBotMove(RaiderBot bot) {
            lock (ConnectedPlayers) {
                foreach (var p in ConnectedPlayers) {
                    try {
                        lock (p.WriterLock) {
                            p.Writer.Write((byte)1); p.Writer.Write(bot.Name); p.Writer.Write(bot.Position.X);
                            p.Writer.Write(bot.Position.Y); p.Writer.Write(bot.Rotation); p.Writer.Write(bot.HeldItemID); p.Writer.Flush();
                        }
                    } catch { }
                }
            }
        }

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