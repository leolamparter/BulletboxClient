using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Numerics;
using System.Diagnostics;

using System.Text.Json;
using System.Linq;
using System.IO;

public class ServerProgram
{
    public static ServerWorld BulletboxWorld = new ServerWorld();
    public static List<ServerPlayer> ConnectedPlayers = new List<ServerPlayer>();
    public static bool IsRunning = false;
    private static float _raidInitialTotalHealth = 0f;
    private static float _playerRegenTimer = 0f;
    private static float _worldTime = 0f;
    private static float _flickerSpawnTimer = 0f;
    private static float _autoSaveTimer = 0f;

    private const string SinglePlayerSavePath = "singleplayer_save.json";
    public static Dictionary<string, PlayerSaveData> LoadedPlayers = new(); // Holds player data by username after loading

    public static async Task RunServerAsync()
    {
        if (IsRunning) return;
        IsRunning = true;

        Random rand = new Random();

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

                _autoSaveTimer += dt;
                if (_autoSaveTimer >= 30f) { // Save every 30 seconds
                    SaveGame();
                    _autoSaveTimer = 0f;
                }

                _worldTime += dt;
                // Simple 10-minute cycle: 5 mins Day (0-300s), 5 mins Night (300-600s)
                bool isNight = (_worldTime % 600) > 300;

                // Rare Flicker Spawning logic (Checks every 15 seconds)
                _flickerSpawnTimer += dt;
                if (isNight && _flickerSpawnTimer > 15f)
                {
                    _flickerSpawnTimer = 0;
                    if (rand.Next(100) < 10) // 10% chance every 15s of night
                    {
                        lock (ConnectedPlayers)
                        {
                            if (ConnectedPlayers.Count > 0)
                            {
                                var p = ConnectedPlayers[rand.Next(ConnectedPlayers.Count)];
                                Vector2 pPos = BulletboxWorld.PlayerLocations.GetValueOrDefault(p.Username, Vector2.Zero);
                                
                                // Spawn "Flicker" 400-600 units away from a random player
                                float spawnAngle = (float)(rand.NextDouble() * Math.PI * 2);
                                Vector2 spawnPos = pPos + new Vector2(MathF.Cos(spawnAngle) * 500, MathF.Sin(spawnAngle) * 500);
                                int id = rand.Next(10000, 99999);
                                var flicker = new RaiderBot($"Flicker {id}", spawnPos) { MaxHealth = 50, Health = 50, PreviousHealth = 50, HeldItemID = "none" };
                                lock(BulletboxWorld.Raiders) { BulletboxWorld.Raiders.Add(flicker); }
                            }
                        }
                    }
                }

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

                // Update Server-side Player Physics (e.g., knockback decay)
                lock(ConnectedPlayers) {
                    foreach(var p in ConnectedPlayers) {
                        p.Position += p.Velocity * dt;
                        p.Velocity = Vector2.Lerp(p.Velocity, Vector2.Zero, dt * 6.5f); // Decay knockback
                    }
                }
                // Update Server-side Gust Logic (NEW)
                lock(BulletboxWorld.ActiveGusts) {
                    for (int i = BulletboxWorld.ActiveGusts.Count - 1; i >= 0; i--) {
                        var gust = BulletboxWorld.ActiveGusts[i];
                        
                        gust.Position += gust.Velocity * dt;
                        gust.LifeTime -= dt;

                        bool hit = false;
                        if (gust.LifeTime <= 0) { hit = true; } // Gust expires
                        else {
                            lock(ConnectedPlayers) {
                                foreach(var p in ConnectedPlayers) {
                                    Vector2 pPos = BulletboxWorld.PlayerLocations.GetValueOrDefault(p.Username, Vector2.Zero);
                                    if (Vector2.Distance(pPos + new Vector2(32,32), gust.Position) < 16f) { // Player center to gust center
                                        p.Damage((int)gust.Damage);
                                        p.ApplyKnockback(Vector2.Normalize(gust.Velocity) * gust.KnockbackForce);
                                        p.SyncHealth();
                                        hit = true;
                                        break;
                                    }
                                }
                            }
                        }
                        if (hit) { BulletboxWorld.ActiveGusts.RemoveAt(i); }
                    }
                }

                // Brimstalker Spawning Logic
                lock(ConnectedPlayers) {
                    foreach(var p in ConnectedPlayers) {
                        Vector2 pPos = BulletboxWorld.PlayerLocations.GetValueOrDefault(p.Username, Vector2.Zero);
                        var chunk = BulletboxWorld.GetOrGenerateChunk((int)MathF.Floor(pPos.X / 16), (int)MathF.Floor(pPos.Y / 16), p.CurrentDimension);
                        
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

                // Lava Pool Damage Logic for ALL entities
                lock(ConnectedPlayers) {
                    foreach(var p in ConnectedPlayers) {
                        Vector2 pPos = BulletboxWorld.PlayerLocations.GetValueOrDefault(p.Username, Vector2.Zero);
                        var chunk = BulletboxWorld.GetOrGenerateChunk((int)MathF.Floor(pPos.X / 16), (int)MathF.Floor(pPos.Y / 16), p.CurrentDimension);
                        
                        if (p.CurrentDimension == Dimension.TheEnd && chunk.Biome == BiomeType.Void) {
                            // Push player toward the center of the island
                            Vector2 pushDir = Vector2.Normalize(Vector2.Zero - pPos);
                            float pushForce = 30f; // Force to push the player back
                            lock (p.WriterLock) {
                                p.Writer.Write((byte)7); // Packet ID 7: Knockback
                                p.Writer.Write(pushDir.X * pushForce);
                                p.Writer.Write(pushDir.Y * pushForce);
                                p.Writer.Flush();
                            }
                            p.Damage(1); // Constant damage while in the void
                            p.SyncHealth();
                        }
                        
                        // Check for Portal Teleport
                        if (p.CurrentDimension == Dimension.TheEnd)
                        {
                            var portal = BulletboxWorld.Structures.Values.FirstOrDefault(s => s.Type == StructureType.EndPortal);
                            if (portal != null && Vector2.Distance(p.Position, portal.Position) < 80f) // 5 chunks radius = 80 units
                            {
                                p.CurrentDimension = Dimension.Overworld;
                                p.Position = Vector2.Zero;
                                BulletboxWorld.UpdatePosition(p.Username, 0, 0);
                                p.SendDimensionUpdate();
                                BulletboxWorld.Structures.TryRemove((portal.ChunkX, portal.ChunkY), out _); // Destroy portal
                                p.BroadcastChat("SYSTEM", $"{p.Username} escaped The End!");
                            }
                        }

                        if (chunk.Biome == BiomeType.LavaPool) {
                            p.Damage(1); // Tick damage while standing in lava
                            p.SyncHealth();
                        }
                    }
                }
                lock(BulletboxWorld.Raiders) {
                    for (int i = BulletboxWorld.Raiders.Count - 1; i >= 0; i--) {
                        var bot = BulletboxWorld.Raiders[i];
                        var chunk = BulletboxWorld.GetOrGenerateChunk((int)MathF.Floor(bot.Position.X / 16), (int)MathF.Floor(bot.Position.Y / 16), bot.Dimension);
                        
                        if (bot.Dimension == Dimension.TheEnd && chunk.Biome == BiomeType.Void) {
                            Vector2 pushDir = Vector2.Normalize(Vector2.Zero - bot.Position);
                            bot.Velocity += pushDir * 1500f * dt;
                        }

                        if (chunk.Biome == BiomeType.LavaPool) {
                            bot.Health -= 1; // Tick damage while standing in lava
                            if (bot.Health <= 0) {
                                if (bot.Name == "APEX")
                                {
                                    // Spawn escape portal at (0,0)
                                    Vector2 portalPos = Vector2.Zero;
                                    Structure portal = new Structure(portalPos, StructureType.EndPortal, 0, 0, "");
                                    BulletboxWorld.Structures.TryAdd((0, 0), portal);
                                }
                                
                                BulletboxWorld.Raiders.RemoveAt(i); // Remove if dead
                                lock (ConnectedPlayers) {
                                    foreach (var p in ConnectedPlayers) p.SendLeaveSignal(bot.Name);
                                }
                            }
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
            bot.MaxHealth = 1000; bot.HeldItemID = "none"; // No weapons
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
            // Snap all existing world entities (like ambient Flickers spawned at night) into the raid encounter area.
            // This prevents the raid from soft-locking if a mob is too far away to be engaged.
            lock (BulletboxWorld.Raiders)
            {
                foreach (var bot in BulletboxWorld.Raiders)
                {
                    float angle = (float)(rand.NextDouble() * Math.PI * 2);
                    float dist = rand.Next(200, 500);
                    bot.Position = s.Position + new Vector2(MathF.Cos(angle) * dist, MathF.Sin(angle) * dist);
                    
                    // Reset AI state so they engage correctly from their new position
                    bot.ChargePhase = 0;
                    bot.ChargeTimer = 0;
                    bot.FleeTimer = 0;
                    bot.WanderTarget = null;
                    bot.PreviousHealth = bot.Health;

                    _raidInitialTotalHealth += bot.MaxHealth;
                }
            }

            int raiderCount = rand.Next(2, 5); 
            for (int i = 0; i < raiderCount; i++) {
                float angle = (float)(rand.NextDouble() * Math.PI * 2);
                float dist = rand.Next(100, 400); // Spawn inside the massive 120-chunk arena
                Vector2 spawnPos = s.Position + new Vector2(MathF.Cos(angle) * dist, MathF.Sin(angle) * dist);
                
                // 15% chance to spawn a Vortex (NEW)
                if (rand.Next(100) < 15)
                {
                    int id = rand.Next(10000, 99999);
                    var vortex = new RaiderBot($"Vortex {id}", spawnPos) { MaxHealth = 75, Health = 75, PreviousHealth = 75, HeldItemID = "none", AttackCooldown = 0.5f }; 
                    _raidInitialTotalHealth += vortex.MaxHealth;
                    BulletboxWorld.Raiders.Add(vortex);
                    Console.WriteLine($"[Server] Spawning Vortex at {spawnPos}");
                } else if (rand.Next(100) < 20) // 20% chance to spawn a Flicker (original logic)
                {
                    int id = rand.Next(10000, 99999);
                    var flicker = new RaiderBot($"Flicker {id}", spawnPos) { MaxHealth = 50, Health = 50, PreviousHealth = 50, HeldItemID = "none" };
                    _raidInitialTotalHealth += flicker.MaxHealth;
                    BulletboxWorld.Raiders.Add(flicker);
                    Console.WriteLine($"[Server] Spawning Flicker at {spawnPos}");
                }
                else // Otherwise, spawn a regular Raidshroomer
                {
                    int id = rand.Next(10000, 99999);
                    var bot = new RaiderBot($"Raider {id}", spawnPos);                            
                    bot.HeldItemID = "iron_sword";
                    bot.AttackCooldown = 0.425f;
                    _raidInitialTotalHealth += bot.MaxHealth; // Default RaiderBot MaxHealth is 100
                    BulletboxWorld.Raiders.Add(bot);
                }
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

                // Populate the chest inventory for the outpost that was just defeated
                if (BulletboxWorld.ActiveRaidOutpostPosition.HasValue)
                {
                    Vector2 pos = (Vector2)BulletboxWorld.ActiveRaidOutpostPosition.Value;
                    Structure? s = BulletboxWorld.Structures.Values.FirstOrDefault(st => (Vector2)st.Position == pos && st.RaidActive);
                    if (s != null)
                    {
                        s.RaidActive = false;
                        s.IsCompleted = true; // Mark as completed for chest access after victory
                        s.ChestInventory = new ServerItemStack[18];
                    for (int j = 0; j < 18; j++) s.ChestInventory[j] = new ServerItemStack("none", 0);
                    // Updated loot pool: minerals, shields, raidshrooms, and wooden/stone gear
                    string[] pool = { 
                        "stick", "rock", "copper", "iron", "diamond", "quartz", 
                        "shield", "raidshroom", 
                        "wooden_sword", "wooden_axe", "wooden_scythe", "wooden_spear",
                        "stone_sword", "stone_axe", "stone_scythe", "stone_spear" 
                    };
                        int lootCount = rand.Next(4, 9);
                        for (int j = 0; j < lootCount; j++) {
                            int slot = rand.Next(18);
                        string item = pool[rand.Next(pool.Length)];
                        // Allow minerals and consumables to stack in the chest
                        bool isStackable = item == "raidshroom" || item == "stick" || item == "rock" || item == "copper" || item == "iron" || item == "diamond" || item == "quartz";
                        s.ChestInventory[slot] = new ServerItemStack(item, isStackable ? rand.Next(1, 4) : 1);
                        }
                    }
                }
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
                        if (p.CurrentDimension != bot.Dimension) continue;
                        float d = Vector2.Distance(bot.Position, BulletboxWorld.PlayerLocations.GetValueOrDefault(p.Username, Vector2.Zero));
                        if (d < minDist) { minDist = d; target = p; }
                    }
                }

                float visionRange = 45 * 16;
                if (target != null && minDist < visionRange + 32) {
                    bot.WanderTarget = null;
                    Vector2 targetPos = BulletboxWorld.PlayerLocations[target.Username];

                    // APEX Evolutions based on Health
                    if (bot.Name == "APEX")
                    {
                        float hpPct = bot.Health / (float)bot.MaxHealth;
                        if (hpPct <= 0.8f) bot.HeldItemID = "none"; // Switch to special moves
                    }

                    // Unique strafe and target offset per raider to prevent marching in sync
                    int hash = bot.Name.GetHashCode();
                    Vector2 targetOffset = new Vector2(MathF.Cos(hash) * 35f, MathF.Sin(hash) * 35f);
                    Vector2 dir = Vector2.Normalize((targetPos + targetOffset) - bot.Position);
                    
                    // Flicker Teleportation Logic (re-added here as it was removed in previous diff)
                    if (bot.Name.StartsWith("Flicker") || (bot.Name == "APEX" && bot.Health / (float)bot.MaxHealth <= 0.8f))
                    {
                        if (bot.Health < bot.PreviousHealth)
                        {
                            // Teleport 150-300 units away in a random direction
                            float angle = (float)(rand.NextDouble() * Math.PI * 2);
                            float dist = rand.Next(150, 300);
                            bot.Position += new Vector2(MathF.Cos(angle) * dist, MathF.Sin(angle) * dist);
                            bot.PreviousHealth = bot.Health;
                            bot.ChargePhase = 0; // Reset charge if teleporting
                            bot.ChargeTimer = 0;
                        }
                    }

                    // AI Change: Brimstalker never runs away, but Raiders and Flickers can
                    if (bot.Name != "Brimstalker") {
                        if (bot.Health < 30 && bot.FleeTimer <= 0 && rand.Next(100) < 1) bot.FleeTimer = 8.0f;
                    }

                    if (bot.FleeTimer > 0 && bot.Name != "APEX") { bot.FleeTimer -= dt; dir = -dir; }

                    Vector2 sideStepDir = new Vector2(-dir.Y, dir.X);
                    float strafeFreq = 2.0f + (Math.Abs(hash) % 300 / 100f);
                    float strafeAmp = 150f + (Math.Abs(hash) % 150);
                    float strafeAmount = MathF.Sin(time * strafeFreq + hash) * strafeAmp;

                    bot.Rotation = (float)(Math.Atan2(dir.Y, dir.X) * (180.0 / Math.PI));

                    float apexHpPct = bot.Name == "APEX" ? bot.Health / (float)bot.MaxHealth : 1.0f;

                    if (bot.Name == "Brimstalker" || (bot.Name.StartsWith("Flicker") && minDist < 250f) || (bot.Name == "APEX" && apexHpPct <= 0.4f)) {
                        // Charge Attack State Machine (Shared by Brimstalker and Aggroed Flicker)
                        if (bot.ChargePhase == 0) {
                            bot.ChargeCooldown -= dt;
                            if (bot.ChargeCooldown <= 0 || (bot.Name.StartsWith("Flicker") && minDist < 150f)) {
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
                            
                            float hitRadius = bot.Name.StartsWith("Flicker") ? 35f : 45f;
                            int damage = bot.Name.StartsWith("Flicker") ? 25 : 40;

                            if (!bot.HasDealtChargeDamage && Vector2.Distance(bot.Position, targetPos) < hitRadius) {
                                target.Damage(damage);
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
                    else if (bot.Name.StartsWith("Vortex") || (bot.Name == "APEX" && apexHpPct <= 0.6f && apexHpPct > 0.4f)) // NEW Vortex AI
                    {
                        float desiredDistance = 300f; // 300 units away
                        float vortexMoveSpeed = 220f; 
                        
                        Vector2 toTarget = (targetPos + new Vector2(32,32)) - (bot.Position + new Vector2(32,32)); // Center to center
                        float dist = toTarget.Length();
                        Vector2 directDir = Vector2.Normalize(toTarget);
                        Vector2 orbitVec = new Vector2(-directDir.Y, directDir.X);
                        float orbitSide = (bot.Name.GetHashCode() % 2 == 0) ? 1f : -1f;

                        // 1. Radial Movement: Maintain desired distance
                        if (dist < desiredDistance - 8f) bot.Position -= directDir * vortexMoveSpeed * dt;
                        else if (dist > desiredDistance + 8f) bot.Position += directDir * vortexMoveSpeed * dt;

                        // 2. Tangential Movement: Constant Orbit Circling
                        bot.Position += orbitVec * orbitSide * vortexMoveSpeed * dt;

                        bot.Rotation = (float)(Math.Atan2(directDir.Y, directDir.X) * (180.0 / Math.PI));

                        bot.AttackTimer += dt;
                        if (bot.AttackTimer >= bot.AttackCooldown) {
                            bot.AttackCooldown = 0.3f + (float)rand.NextDouble() * 0.2f; // Very fast attack, 0.3-0.5s
                            bot.AttackTimer = 0;
                            Vector2 gustDir = directDir; // Shoot directly at target
                            float gustSpeed = 1500f;
                            lock(BulletboxWorld.ActiveGusts) { BulletboxWorld.ActiveGusts.Add(new ServerGust(bot.Position + new Vector2(32,32), gustDir * gustSpeed, bot.Name, 5f, 800f)); } // 5 damage, 800 knockback
                            BroadcastGust(bot.Position + new Vector2(32,32), gustDir * gustSpeed);
                        }
                    }
                    else // Generic Melee Raider Positioning
                    {
                        float attackRange = 96f; 
                        if (ServerWeaponStats.Library.TryGetValue(bot.HeldItemID, out var stats)) attackRange = stats.Range * 0.65f;

                        float stopDist = attackRange * 0.85f;
                        float moveSpeed = (minDist < stopDist) ? 60f : 260f;
                        bot.Position += (dir * moveSpeed + sideStepDir * strafeAmount) * dt;
                    }

                    // Enforce raid boundary for raiders
                    if (BulletboxWorld.ActiveRaidOutpostPosition.HasValue)
                    {
                        Vector2 outpostCenter = BulletboxWorld.ActiveRaidOutpostPosition.Value;
                        const float boundaryRadius = 120f * 16f; // 120 Chunks = 1920 Units
                        Vector2 offset = bot.Position - outpostCenter;
                        if (offset.Length() > boundaryRadius)
                        {
                            bot.Position = outpostCenter + Vector2.Normalize(offset) * boundaryRadius;
                        }
                    }

                    if (!bot.Name.StartsWith("Vortex") && !(bot.Name == "APEX" && apexHpPct <= 0.6f && apexHpPct > 0.4f))
                    {
                        bot.AttackTimer += dt;
                        if (bot.Name == "Brimstalker" || (bot.Name == "APEX" && apexHpPct <= 0.4f)) {
                            // Brimstalker Bomb Attack AI
                            if (bot.AttackTimer >= bot.AttackCooldown) {
                                bot.AttackCooldown = 1.0f + (float)rand.NextDouble() * 1.0f;
                                bot.AttackTimer = 0;
                                Vector2 bombDir = Vector2.Normalize(targetPos - bot.Position);
                                float bombSpeed = 850f;
                                lock(BulletboxWorld.ActiveBombs) {
                                    var b = new ServerBomb(bot.Position, bombDir * bombSpeed, bot.Name);
                                    b.TargetPlayer = target.Username;
                                    BulletboxWorld.ActiveBombs.Add(b);
                                }
                                BroadcastBomb(bot.Position, bombDir * bombSpeed);
                            }
                        }
                        else {
                            float meleeAttackRange = 96f; 
                            if (ServerWeaponStats.Library.TryGetValue(bot.HeldItemID, out var stats)) meleeAttackRange = stats.Range * 0.65f;

                            // Standard Raider Melee
                            if (minDist < meleeAttackRange && bot.AttackTimer >= bot.AttackCooldown && bot.FleeTimer <= 0) {
                                target.Damage(12);
                                bot.AttackTimer = 0;
                            }
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
                    float overlapRadius = (bot.Name == "APEX" || other.Name == "APEX") ? 90f : 45f;
                    if (d < overlapRadius && d > 0.1f) {
                        bot.Position += Vector2.Normalize(bot.Position - other.Position) * (overlapRadius - d) * 0.5f;
                    }
                }
                lock(ConnectedPlayers) {
                    foreach (var p in ConnectedPlayers) {
                        Vector2 pPos = BulletboxWorld.PlayerLocations.GetValueOrDefault(p.Username, Vector2.Zero);
                        float d = Vector2.Distance(bot.Position, pPos);
                        float overlapRadius = (bot.Name == "APEX") ? 90f : 45f;
                        if (d < overlapRadius && d > 0.1f) {
                            bot.Position += Vector2.Normalize(bot.Position - pPos) * (overlapRadius - d) * 0.5f;
                        }
                    }
                }

                BroadcastBotMove(bot);
            }
        }

        void BroadcastRaidUpdate(byte type, float val, SerializableVector2? outpostCenter = null) {
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

        void BroadcastGust(Vector2 start, Vector2 velocity) { // NEW
            lock (ConnectedPlayers) {
                foreach (var p in ConnectedPlayers) {
                    try { lock (p.WriterLock) { 
                        p.Writer.Write((byte)17); // Packet ID 17 for gust
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
                            if (p.CurrentDimension != bot.Dimension) continue; // Optimization: Only sync to same dimension
                            p.Writer.Write((byte)1); 
                            p.Writer.Write(bot.Name); 
                            p.Writer.Write(bot.Position.X);
                            p.Writer.Write(bot.Position.Y); 
                            p.Writer.Write(bot.Rotation); 
                            p.Writer.Write(bot.HeldItemID); 
                            p.Writer.Write("none");      // Bots have no offhand
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

    public static void SpawnAPEX(ServerWorld world)
    {
        var apex = new RaiderBot("APEX", new Vector2(100, 100)) { 
            MaxHealth = 2500, Health = 2500, PreviousHealth = 2500, 
            HeldItemID = "brimstone_sword", Dimension = Dimension.TheEnd 
        };
        lock(world.Raiders) { world.Raiders.Add(apex); }
        world.RaidActive = true;
        world.ActiveRaidOutpostPosition = null; // Bosses are not outposts
        _raidInitialTotalHealth = 2500;
        Console.WriteLine("[Server] APEX Boss spawned in The End.");
    }

    public static void SaveGame() {
        if (!IsRunning || Program.LastIP != "127.0.0.1") return;

        // PROTECTION: If no players are connected, don't save. 
        // This prevents the server from wiping the save file if it ticks while a player is joining/leaving.
        lock (ConnectedPlayers) {
            if (ConnectedPlayers.Count == 0) return;
        }

        Console.WriteLine("[Server] Saving game state...");
        WorldSaveData saveData = new WorldSaveData();
        saveData.Seed = BulletboxWorld.Seed;

        lock (ConnectedPlayers) {
            foreach (var p in ConnectedPlayers) {
                saveData.Players.Add(new PlayerSaveData {
                    Username = p.Username, Health = p.Health, MaxHealth = p.MaxHealth, Hunger = p.Hunger,
                    Position = p.Position, Rotation = p.Rotation, IsBlocking = p.IsBlocking,
                    CurrentDimension = p.CurrentDimension, SelectedSlot = p.SelectedSlot,
                    AshenTime = p.AshenTime, BrimstalkerCooldown = p.BrimstalkerCooldown,
                    Inventory = p.Inventory, CraftingSlot1 = p.CraftingSlot1, CraftingSlot2 = p.CraftingSlot2
                });
            }
        }

        lock (BulletboxWorld.Raiders) {
            foreach (var r in BulletboxWorld.Raiders) {
                saveData.Raiders.Add(new RaiderSaveData {
                    Name = r.Name, Position = r.Position, Velocity = r.Velocity,
                    Health = r.Health, PreviousHealth = r.PreviousHealth, MaxHealth = r.MaxHealth,
                    Rotation = r.Rotation, AttackTimer = r.AttackTimer, HeldItemID = r.HeldItemID,
                    AttackCooldown = r.AttackCooldown, FleeTimer = r.FleeTimer,
                    WanderTarget = r.WanderTarget, WanderWaitTimer = r.WanderWaitTimer,
                    ChargePhase = r.ChargePhase, ChargeTimer = r.ChargeTimer,
                    ChargeCooldown = r.ChargeCooldown, ChargeDirection = r.ChargeDirection,
                    HasDealtChargeDamage = r.HasDealtChargeDamage, Dimension = r.Dimension
                });
            }
        }

        saveData.ActiveBombs.AddRange(BulletboxWorld.ActiveBombs);
        saveData.ActiveGusts.AddRange(BulletboxWorld.ActiveGusts);

        lock (BulletboxWorld.Structures) {
            foreach (var entry in BulletboxWorld.Structures)
                saveData.Structures[$"{entry.Key.Item1},{entry.Key.Item2}"] = entry.Value;
        }

        saveData.WorldTime = _worldTime;
        saveData.FlickerSpawnTimer = _flickerSpawnTimer;
        saveData.PlayerRegenTimer = _playerRegenTimer;
        saveData.RaidTimer = BulletboxWorld.RaidTimer;
        saveData.RaidActive = BulletboxWorld.RaidActive;
        saveData.ActiveRaidOutpostPosition = BulletboxWorld.ActiveRaidOutpostPosition;

        try {
            string jsonString = JsonSerializer.Serialize(saveData, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true });
            File.WriteAllText(SinglePlayerSavePath, jsonString);
            Console.WriteLine("[Server] Game state saved successfully.");
        } catch (Exception ex) {
            Console.WriteLine($"[Server] Error saving game state: {ex.Message}");
        }
    }

    public static bool LoadGame() {
        if (!File.Exists(SinglePlayerSavePath)) return false;

        Console.WriteLine("[Server] Loading game state...");
        try {
            string jsonString = File.ReadAllText(SinglePlayerSavePath);
            WorldSaveData? saveData = JsonSerializer.Deserialize<WorldSaveData>(jsonString, new JsonSerializerOptions { IncludeFields = true });
            if (saveData == null) return false;

            BulletboxWorld = new ServerWorld();
            BulletboxWorld.Seed = saveData.Seed;

            // Helper to fix null ItemIDs that cause crashes (ServerItemStack is a struct, so inv[i] is never null)
            Action<ServerItemStack[]?> sanitizeInventory = (inv) => {
                if (inv == null) return;
                for (int i = 0; i < inv.Length; i++) 
                    if (inv[i].ItemID == null) inv[i].ItemID = "none";
            };

            lock (BulletboxWorld.Raiders) {
                BulletboxWorld.Raiders.Clear();
                foreach (var rData in saveData.Raiders) {
                    var bot = new RaiderBot(rData.Name, rData.Position) {
                        Velocity = rData.Velocity, Health = rData.Health,
                        PreviousHealth = rData.PreviousHealth, MaxHealth = rData.MaxHealth,
                        Rotation = rData.Rotation, AttackTimer = rData.AttackTimer,
                        HeldItemID = rData.HeldItemID, AttackCooldown = rData.AttackCooldown,
                        FleeTimer = rData.FleeTimer, WanderTarget = rData.WanderTarget,
                        WanderWaitTimer = rData.WanderWaitTimer, ChargePhase = rData.ChargePhase,
                        ChargeTimer = rData.ChargeTimer, ChargeCooldown = rData.ChargeCooldown,
                        ChargeDirection = rData.ChargeDirection, HasDealtChargeDamage = rData.HasDealtChargeDamage,
                        Dimension = rData.Dimension
                    };
                    BulletboxWorld.Raiders.Add(bot);
                }
            }

            lock (BulletboxWorld.ActiveBombs) {
                BulletboxWorld.ActiveBombs.Clear();
                BulletboxWorld.ActiveBombs.AddRange(saveData.ActiveBombs);
            }

            lock (BulletboxWorld.ActiveGusts) {
                BulletboxWorld.ActiveGusts.Clear();
                BulletboxWorld.ActiveGusts.AddRange(saveData.ActiveGusts);
            }

            lock (BulletboxWorld.Structures) {
                BulletboxWorld.Structures.Clear();
                foreach (var entry in saveData.Structures) {
                    string[] keyParts = entry.Key.Split(',');
                    int cx = int.Parse(keyParts[0]);
                    int cy = int.Parse(keyParts[1]);
                    sanitizeInventory(entry.Value.ChestInventory);
                    BulletboxWorld.Structures.TryAdd((cx, cy), entry.Value);
                }
            }

            _worldTime = saveData.WorldTime;
            _flickerSpawnTimer = saveData.FlickerSpawnTimer;
            _playerRegenTimer = saveData.PlayerRegenTimer;
            BulletboxWorld.RaidTimer = saveData.RaidTimer;
            BulletboxWorld.RaidActive = saveData.RaidActive;
            BulletboxWorld.ActiveRaidOutpostPosition = saveData.ActiveRaidOutpostPosition;

            LoadedPlayers.Clear();
            foreach (var pData in saveData.Players) {
                if (pData == null || string.IsNullOrEmpty(pData.Username)) continue;
                sanitizeInventory(pData.Inventory);
                if (pData.CraftingSlot1.ItemID == null) pData.CraftingSlot1 = new ServerItemStack("none", 0);
                if (pData.CraftingSlot2.ItemID == null) pData.CraftingSlot2 = new ServerItemStack("none", 0);
                LoadedPlayers[pData.Username] = pData;
            }
            return true;
        } catch (Exception ex) {
            Console.WriteLine($"[Server] Error loading game state: {ex.Message}");
            return false;
        }
    }
}