using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Numerics;
using System.Diagnostics;

public class ServerProgram
{
    public static ServerWorld BulletboxWorld = new ServerWorld();
    public static List<ServerPlayer> ConnectedPlayers = new List<ServerPlayer>();
    public static bool IsRunning = false;
    private static float _raidInitialTotalHealth = 0f;
    private static float _playerRegenTimer = 0f;
    private static float _raidRecheckTimer = 0f;

    public static async Task RunServerAsync()
    {
        if (IsRunning) return;
        IsRunning = true;

        Random rand = new Random();

        // Reset world state for a clean restart
        BulletboxWorld.RaidActive = false;
        BulletboxWorld.RaidTimer = 9999f;
        _raidInitialTotalHealth = 0;
        BulletboxWorld.ActiveRaidOutpostPosition = null; // Clear active outpost on server restart
        BulletboxWorld.Raiders.Clear(); // Clear any existing raiders
        
        lock (BulletboxWorld.Structures) {
            // Reset 'IsCompleted' for all outposts so the player can re-encounter them
            foreach (var s in BulletboxWorld.Structures.Values) s.IsCompleted = false;
        }

        TcpListener listener = new TcpListener(IPAddress.Any, 32308); // Listener for incoming client connections
        listener.Start(); // Start listening for client connections
        Console.WriteLine("[Integrated Server] Started on 32308...");

        _ = Task.Run(async () => {
            Stopwatch sw = Stopwatch.StartNew();
            while (IsRunning) {
                // FIX: Use a measured DeltaTime for AI and physics consistency
                float dt = (float)sw.Elapsed.TotalSeconds;
                sw.Restart();

                // Wait for the next tick
                await Task.Delay(16); 
                if (Program.IsPaused && Program.LastIP == "127.0.0.1") continue;

                float triggerDist = 960f; // 60 chunks * 16 units/chunk

                // Update Raider AI (global for now, will be filtered by raid later)
                UpdateRaiderAI(dt);

                // Update Server-side Bomb Logic
                lock(BulletboxWorld.ActiveBombs) {
                    for (int i = BulletboxWorld.ActiveBombs.Count - 1; i >= 0; i--) {
                        var bomb = BulletboxWorld.ActiveBombs[i];
                        
                        // Slight Aimbot: Adjust velocity towards the target player over time
                        if (!string.IsNullOrEmpty(bomb.TargetPlayer))
                        {
                            Vector2 targetPPos = BulletboxWorld.PlayerLocations.GetValueOrDefault(bomb.TargetPlayer, Vector2.Zero);
                            if (targetPPos != Vector2.Zero)
                            {
                                Vector2 desiredDir = Vector2.Normalize(targetPPos - bomb.Position);
                                bomb.Velocity = Vector2.Normalize(Vector2.Lerp(Vector2.Normalize(bomb.Velocity), desiredDir, dt * 3.5f)) * bomb.Velocity.Length();
                            }
                        }

                        bomb.Position += bomb.Velocity * dt;
                        bomb.Timer -= dt;

                        bool triggered = bomb.Timer <= 0;
                        if (!triggered) {
                            lock(ConnectedPlayers) {
                                foreach(var p in ConnectedPlayers) {
                                    Vector2 pPos = BulletboxWorld.PlayerLocations.GetValueOrDefault(p.Username, Vector2.Zero);
                                    if (Vector2.Distance(pPos, bomb.Position) < 25f) { triggered = true; break; }
                                }
                            }
                        }

                        if (triggered && !bomb.Exploded) {
                            bomb.Exploded = true;
                            // Check damage against players
                            lock(ConnectedPlayers) {
                                foreach(var p in ConnectedPlayers) {
                                    Vector2 pPos = BulletboxWorld.PlayerLocations.GetValueOrDefault(p.Username, Vector2.Zero);
                                    float dist = Vector2.Distance(pPos, bomb.Position);
                                    
                                    if (dist < 20f) {
                                        p.Damage(30); // Direct Hit
                                        p.SyncHealth();
                                    }
                                    else if (dist < 48f) {
                                        p.Damage(10); // Splash (3 chunks = 48 units)
                                        p.SyncHealth();
                                    }
                                }
                            }
                            BulletboxWorld.ActiveBombs.RemoveAt(i);
                        }
                    }
                }

                // Brimstalker Spawning Logic
                lock(ConnectedPlayers) {
                    foreach(var p in ConnectedPlayers) {
                        Vector2 pPos = BulletboxWorld.PlayerLocations.GetValueOrDefault(p.Username, Vector2.Zero);
                        var chunk = BulletboxWorld.GetOrGenerateChunk((int)MathF.Floor(pPos.X / 16), (int)MathF.Floor(pPos.Y / 16));
                        
                        if (chunk.Biome == BiomeType.AshenWastelands) {
                            p.AshenTime += dt;
                            // Spawns after 1 minute in the biome
                            if (p.AshenTime > 60f && p.BrimstalkerCooldown <= 0f && !BulletboxWorld.RaidActive) {
                                SpawnBrimstalker(pPos, rand);
                                p.BrimstalkerCooldown = 300f; // 5 minute cooldown
                            }
                        }
                        if (p.BrimstalkerCooldown > 0) p.BrimstalkerCooldown -= dt;
                    }
                }

                // Lava Pool Damage Logic
                lock(ConnectedPlayers) {
                    foreach(var p in ConnectedPlayers) {
                        Vector2 pPos = BulletboxWorld.PlayerLocations.GetValueOrDefault(p.Username, Vector2.Zero);
                        var chunk = BulletboxWorld.GetOrGenerateChunk((int)MathF.Floor(pPos.X / 16), (int)MathF.Floor(pPos.Y / 16));
                        if (chunk.Biome == BiomeType.LavaPool) {
                            p.Damage(1); // Tick damage while standing in lava
                        }
                    }
                }

                // Player Regeneration Logic (Authoritative)
                _playerRegenTimer += dt;
                if (_playerRegenTimer >= 1.0f) {
                    _playerRegenTimer -= 1.0f;
                    lock(ConnectedPlayers) {
                        foreach(var p in ConnectedPlayers) {
                            if (p.Health < p.MaxHealth && p.Hunger >= 5) {
                                p.Health = Math.Min(p.MaxHealth, p.Health + 5);
                                p.Hunger -= 4;
                                p.SyncHealth();
                            }
                        }
                    }
                }

                // Re-trigger logic: Every minute, completed outposts have a 30% chance to be raid-able again
                _raidRecheckTimer += dt;
                if (_raidRecheckTimer >= 60f) {
                    _raidRecheckTimer = 0;
                    lock (BulletboxWorld.Structures) {
                        foreach (var s in BulletboxWorld.Structures.Values) 
                            if (s.IsCompleted && rand.Next(100) < 30) s.IsCompleted = false;
                    }
                }

                // Iterate through all structures to manage their raid states
                lock (BulletboxWorld.Structures)
                {
                    bool anyPlayerNearOutpost = false;
                    Structure? triggeredStructure = null;

                    foreach (var kvp in BulletboxWorld.Structures)
                    {
                        var s = kvp.Value; // The current structure (raid outpost)

                        foreach (var p in ConnectedPlayers)
                        {
                            Vector2 pPos = BulletboxWorld.PlayerLocations.GetValueOrDefault(p.Username, Vector2.Zero);
                            if (Vector2.Distance(pPos, s.Position) < triggerDist)
                            {
                                if (!s.IsCompleted) {
                                    anyPlayerNearOutpost = true;
                                    triggeredStructure = s;
                                }
                                break;
                            }
                        }
                    }

                    if (!BulletboxWorld.RaidActive)
                    {
                        if (anyPlayerNearOutpost && triggeredStructure != null)
                        {
                            if (BulletboxWorld.RaidTimer > 3f) BulletboxWorld.RaidTimer = 3f;
                            
                            BulletboxWorld.RaidTimer -= dt;
                            if (BulletboxWorld.RaidTimer <= 0)
                            {
                                BulletboxWorld.RaidActive = true;
                                triggeredStructure.IsCompleted = true;
                                SpawnRaidersForOutpost(triggeredStructure, rand);
                                BulletboxWorld.ActiveRaidOutpostPosition = triggeredStructure.Position; // Capture the outpost position when the raid *starts*
                                BulletboxWorld.RaidTimer = 0;
                                BroadcastRaidUpdate(1, 1.0f, BulletboxWorld.ActiveRaidOutpostPosition);
                                BroadcastRaidUpdate(0, 0); // Sync timer to 0 immediately
                            }
                            else BroadcastRaidUpdate(0, BulletboxWorld.RaidTimer);
                        }
                        // If no player is near an outpost, and the timer was counting down, reset it
                        else if (BulletboxWorld.RaidTimer != 9999f)
                        {
                            BulletboxWorld.RaidTimer = 9999f;
                            BroadcastRaidUpdate(0, 9999f);
                        }
                    }
                    else
                    {
                        // Handle active global raid state
                        float currentTotalRaiderHp = 0;
                        var raiders = BulletboxWorld.Raiders.ToList();

                        foreach (var bot in raiders) { currentTotalRaiderHp += bot.Health; }

                        // Pass the fixed active outpost's position during an active raid
                        BroadcastRaidUpdate(1, _raidInitialTotalHealth > 0 ? currentTotalRaiderHp / _raidInitialTotalHealth : 0, BulletboxWorld.ActiveRaidOutpostPosition);
                    }
                }
            }
        });

        void SpawnBrimstalker(Vector2 pos, Random rand)
        {
            BulletboxWorld.RaidActive = true;
            _raidInitialTotalHealth = 1000;
            var bot = new RaiderBot("Brimstalker", pos + new Vector2(rand.Next(-200, 200), rand.Next(-200, 200)));
            bot.MaxHealth = 1000; bot.HeldItemID = 0; // No weapons
            bot.Health = 1000;
            BulletboxWorld.Raiders.Add(bot);
            // Sync immediately
            BroadcastRaidUpdate(1, 1.0f, null);
        }

        void SpawnRaidersForOutpost(Structure s, Random rand)
        {
            // Set the raid active flag on the structure itself (server-side structure object)
            s.RaidActive = true;
            // Store the active outpost position in the world for consistent broadcasting
            BulletboxWorld.ActiveRaidOutpostPosition = s.Position;

            _raidInitialTotalHealth = 0;
            int raiderCount = rand.Next(2, 5); 
            for (int i = 0; i < raiderCount; i++) {
                float angle = (float)(rand.NextDouble() * Math.PI * 2);
                float dist = rand.Next(100, 400); // Spawn inside the massive 120-chunk arena
                Vector2 spawnPos = s.Position + new Vector2(MathF.Cos(angle) * dist, MathF.Sin(angle) * dist);
                
                int id = rand.Next(10000, 99999);
                var bot = new RaiderBot($"Raider {id}", spawnPos);                            
                bot.HeldItemID = (byte)'S';
                bot.AttackCooldown = 0.425f;
                _raidInitialTotalHealth += bot.MaxHealth;
                BulletboxWorld.Raiders.Add(bot);
            }
        }

        void UpdateRaiderAI(float dt) {
            Random rand = new Random();
            float time = (float)(DateTime.Now.Ticks / 10000000.0);
            
            List<RaiderBot> botsToUpdate;
            lock(BulletboxWorld.Raiders) { botsToUpdate = new List<RaiderBot>(BulletboxWorld.Raiders); }

            // Check if raid ended (all raiders defeated)
            if (BulletboxWorld.RaidActive && BulletboxWorld.Raiders.Count == 0)
            {
                BulletboxWorld.RaidActive = false;
                BulletboxWorld.RaidTimer = 9999f;
                BroadcastRaidUpdate(1, 0, null); // Raid ended, send null for outpost position
                BroadcastRaidUpdate(0, 9999f);   // Force timer reset on clients
                _raidInitialTotalHealth = 0;
                BulletboxWorld.ActiveRaidOutpostPosition = null; // Clear the active outpost
                // Heal players
                lock (ConnectedPlayers)
                {
                    foreach (var p in ConnectedPlayers) { p.Health = p.MaxHealth; p.SyncHealth(); }
                }
            }

            foreach (var bot in botsToUpdate) {
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

                float visionRange = 45 * 16;
                if (target != null && minDist < visionRange + 32) {
                    bot.WanderTarget = null;
                    Vector2 targetPos = BulletboxWorld.PlayerLocations[target.Username];

                    // Unique strafe and target offset per raider to prevent marching in sync
                    int hash = bot.Name.GetHashCode();
                    Vector2 targetOffset = new Vector2(MathF.Cos(hash) * 35f, MathF.Sin(hash) * 35f);
                    Vector2 dir = Vector2.Normalize((targetPos + targetOffset) - bot.Position);
                    
                    // AI Change: Brimstalker never runs away
                    if (bot.Name != "Brimstalker") {
                        if (bot.Health < 30 && bot.FleeTimer <= 0 && rand.Next(100) < 1) bot.FleeTimer = 8.0f;
                    }

                    if (bot.FleeTimer > 0) { bot.FleeTimer -= dt; dir = -dir; }

                    Vector2 sideStepDir = new Vector2(-dir.Y, dir.X);
                    float strafeFreq = 2.0f + (Math.Abs(hash) % 300 / 100f);
                    float strafeAmp = 150f + (Math.Abs(hash) % 150);
                    float strafeAmount = MathF.Sin(time * strafeFreq + hash) * strafeAmp;

                    bot.Rotation = (float)(Math.Atan2(dir.Y, dir.X) * (180.0 / Math.PI));

                    if (bot.Name == "Brimstalker") {
                        // Charge Attack State Machine
                        if (bot.ChargePhase == 0) {
                            bot.ChargeCooldown -= dt;
                            if (bot.ChargeCooldown <= 0) {
                                bot.ChargePhase = 1;
                                bot.ChargeTimer = 1.0f; // 1s Backing up phase
                            }
                        }
                        else if (bot.ChargePhase == 1) {
                            // Phase 1: Back away from the player to telegraph the charge
                            Vector2 fromPlayer = Vector2.Normalize(bot.Position - targetPos);
                            bot.Position += fromPlayer * 250f * dt;
                            bot.ChargeTimer -= dt;
                            if (bot.ChargeTimer <= 0) {
                                bot.ChargePhase = 2;
                                bot.ChargeTimer = 0.7f; // Charge duration
                                bot.ChargeDirection = Vector2.Normalize(targetPos - bot.Position);
                                bot.HasDealtChargeDamage = false;
                            }
                            BroadcastBotMove(bot);
                            continue; // Skip normal movement/bomb logic during charge prep
                        }
                        else if (bot.ChargePhase == 2) {
                            // Phase 2: High speed charge (5x normal raider speed = 1300)
                            bot.Position += bot.ChargeDirection * 1300f * dt;
                            bot.ChargeTimer -= dt;

                            // Collision Damage Check (40 Damage)
                            if (!bot.HasDealtChargeDamage && Vector2.Distance(bot.Position, targetPos) < 45f) {
                                target.Damage(40);
                                target.SyncHealth();
                                bot.HasDealtChargeDamage = true;
                            }

                            if (bot.ChargeTimer <= 0) {
                                bot.ChargePhase = 0;
                                bot.ChargeCooldown = 20f + (float)rand.NextDouble() * 10f; // 20-30s cooldown
                            }
                            BroadcastBotMove(bot);
                            continue; // Skip normal movement/bomb logic during charge lunge
                        }
                    }

                    float attackRange = 96f; 
                    if (ServerWeaponStats.Library.TryGetValue(bot.HeldItemID, out var stats)) attackRange = stats.Range * 0.65f;

                    float stopDist = attackRange * 0.85f;
                    float moveSpeed = (minDist < stopDist) ? 60f : 260f;
                    bot.Position += (dir * moveSpeed + sideStepDir * strafeAmount) * dt;

                    // Enforce raid boundary for raiders
                    if (BulletboxWorld.ActiveRaidOutpostPosition is Vector2 outpostCenter)
                    {
                        const float boundaryRadius = 120f * 16f; // 120 Chunks = 1920 Units
                        Vector2 offset = bot.Position - outpostCenter;
                        if (offset.Length() > boundaryRadius)
                        {
                            bot.Position = outpostCenter + Vector2.Normalize(offset) * boundaryRadius;
                        }
                    }

                    bot.AttackTimer += dt;
                    if (bot.Name == "Brimstalker") {
                        // Brimstalker Bomb Attack AI
                        if (bot.AttackTimer >= bot.AttackCooldown) {
                            // Set next interval (1-2 seconds)
                            bot.AttackCooldown = 1.0f + (float)rand.NextDouble() * 1.0f;
                            bot.AttackTimer = 0;

                            Vector2 bombDir = Vector2.Normalize(targetPos - bot.Position);
                            float bombSpeed = 850f; // MUCH faster
                            // Spawn Bomb
                            lock(BulletboxWorld.ActiveBombs) {
                                var b = new ServerBomb(bot.Position, bombDir * bombSpeed, bot.Name);
                                b.TargetPlayer = target.Username;
                                BulletboxWorld.ActiveBombs.Add(b);
                            }
                            // Broadcast bomb visual (Packet 16)
                            BroadcastBomb(bot.Position, bombDir * bombSpeed);
                        }
                    }
                    else {
                        // Standard Raider Melee
                        if (minDist < attackRange && bot.AttackTimer >= bot.AttackCooldown && bot.FleeTimer <= 0) {
                            target.Damage(12);
                            bot.AttackTimer = 0;
                        }
                    }
                } else {
                    if (bot.WanderTarget is not Vector2 wanderPos) {
                        bot.WanderWaitTimer -= dt;
                        if (bot.WanderWaitTimer <= 0) {
                            bot.WanderTarget = bot.Position + new Vector2(rand.Next(-160, 160), rand.Next(-160, 160));
                            bot.WanderWaitTimer = 2.0f;
                        }
                    } else {
                        Vector2 wDir = Vector2.Normalize(wanderPos - bot.Position);
                        bot.Rotation = (float)(Math.Atan2(wDir.Y, wDir.X) * (180.0 / Math.PI));
                        bot.Position += wDir * 100f * dt;
                        if (Vector2.Distance(bot.Position, wanderPos) < 10f) bot.WanderTarget = null;
                    }
                }

                // Anti-overlap: Push raiders away from each other and players
                foreach (var other in botsToUpdate) {
                    if (bot == other) continue;
                    float d = Vector2.Distance(bot.Position, other.Position);
                    if (d < 45f && d > 0.1f) {
                        bot.Position += Vector2.Normalize(bot.Position - other.Position) * (45f - d) * 0.5f;
                    }
                }
                lock(ConnectedPlayers) {
                    foreach (var p in ConnectedPlayers) {
                        Vector2 pPos = BulletboxWorld.PlayerLocations.GetValueOrDefault(p.Username, Vector2.Zero);
                        float d = Vector2.Distance(bot.Position, pPos);
                        if (d < 45f && d > 0.1f) {
                            bot.Position += Vector2.Normalize(bot.Position - pPos) * (45f - d) * 0.5f;
                        }
                    }
                }

                BroadcastBotMove(bot);
            }
        }

        void BroadcastRaidUpdate(byte type, float val, Vector2? outpostCenter = null) {
            lock (ConnectedPlayers) {
                foreach (var p in ConnectedPlayers) {
                    try { lock (p.WriterLock) { 
                        p.Writer.Write((byte)11); 
                        p.Writer.Write(type); 
                        p.Writer.Write(val); 
                        bool sendOutpostPos = outpostCenter.HasValue && (type == 0 || type == 1); // Send if timer or boss health update
                        p.Writer.Write(sendOutpostPos); // Indicate if position follows
                        if (sendOutpostPos) { p.Writer.Write(outpostCenter!.Value.X); p.Writer.Write(outpostCenter!.Value.Y); }
                        p.Writer.Flush(); 
                    } } catch { }
                }
            }
        }

        void BroadcastBomb(Vector2 start, Vector2 velocity) {
            lock (ConnectedPlayers) {
                foreach (var p in ConnectedPlayers) {
                    try { lock (p.WriterLock) { 
                        p.Writer.Write((byte)16); 
                        p.Writer.Write(start.X); p.Writer.Write(start.Y);
                        p.Writer.Write(velocity.X); p.Writer.Write(velocity.Y);
                        p.Writer.Flush(); 
                    } } catch { }
                }
            }
        }

        void BroadcastBotMove(RaiderBot bot) {
            lock (ConnectedPlayers) {
                foreach (var p in ConnectedPlayers) {
                    try {
                        lock (p.WriterLock) {
                            p.Writer.Write((byte)1); 
                            p.Writer.Write(bot.Name); 
                            p.Writer.Write(bot.Position.X);
                            p.Writer.Write(bot.Position.Y); 
                            p.Writer.Write(bot.Rotation); 
                            p.Writer.Write(bot.HeldItemID); 
                            p.Writer.Write((byte)'\0'); // Bots have no offhand
                            p.Writer.Write(false);      // Bots don't block yet
                            p.Writer.Write(bot.Health);
                            p.Writer.Write(bot.MaxHealth);
                            p.Writer.Flush();
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