using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Numerics;
using System.Diagnostics;
using System.IO;

using System.Text.Json;
using System.Linq;
using Microsoft.Data.Sqlite; // New: Using SQLite namespace

public class ServerProgram
{
    public static ServerWorld BulletboxWorld = new();
    public static List<ServerPlayer> ConnectedPlayers = new List<ServerPlayer>();
    public static bool IsRunning = false;
    private static float _raidInitialTotalHealth = 0f;
    private static float _playerRegenTimer = 0f;
    private static float _flickerSpawnTimer = 0f;
    private static float _worldTime = 0f;
    private static float _autoSaveTimer = 0f;

    // Use a property to ensure the server ALWAYS uses the correct world file name from the UI
    public static string ActiveWorldName => Program.CurrentWorldData?.WorldName ?? "default";
    private static string GetDatabasePath() 
    {
        if (!Directory.Exists("saves")) Directory.CreateDirectory("saves");
        return Path.Combine("saves", $"{ActiveWorldName}.db");
    }

    private static readonly object _connectedPlayersLock = new object(); // Lock for ConnectedPlayers
    public static readonly Dictionary<string, PlayerSaveData> LoadedPlayers = new(StringComparer.OrdinalIgnoreCase); // Holds player data by username after loading

    private static TcpListener? _listener;

    public static async Task RunServerAsync()
    {
        if (IsRunning) return;
        IsRunning = true;

        Random rand = new Random();
        
        InitializeDatabase(); // NEW: Initialize the database at server startup
        _listener = new TcpListener(IPAddress.Any, 32308); 
        if (!Directory.Exists("saves")) Directory.CreateDirectory("saves");

        _listener.Start(); 
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
                    _ = SaveGameAsync();
                    _autoSaveTimer = 0f;
                }

                _worldTime += dt;
                // Simple 10-minute cycle: 5 mins Day (0-300s), 5 mins Night (300-600s)
                bool isNight = (_worldTime % 600) > 300;

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
                            lock(_connectedPlayersLock) {
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
                lock(ConnectedPlayers) { // Use ConnectedPlayers directly as it's the list being iterated
                    foreach(var p in ConnectedPlayers) {
                        // NEW: Sanitize velocity before applying
                        if (float.IsNaN(p.Velocity.X) || float.IsNaN(p.Velocity.Y) || float.IsInfinity(p.Velocity.X) || float.IsInfinity(p.Velocity.Y))
                        {
                            Console.WriteLine($"[Server] WARNING: Player {p.Username}'s velocity contained NaN/Infinity. Resetting.");
                            p.Velocity = Vector2.Zero;
                        }
                        p.Position += p.Velocity * dt;
                        p.Velocity = Vector2.Lerp(p.Velocity, Vector2.Zero, dt * 6.5f); // Decay knockback
                        // NEW: Sanitize position after applying velocity
                        if (float.IsNaN(p.Position.X) || float.IsNaN(p.Position.Y) || float.IsInfinity(p.Position.X) || float.IsInfinity(p.Position.Y))
                        {
                            Console.WriteLine($"[Server] WARNING: Player {p.Username}'s position became NaN/Infinity. Resetting to default spawn.");
                            p.Position = new Vector2(400, 300); // Reset to a known good position
                            p.Velocity = Vector2.Zero; // Also reset velocity
                        }
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
                lock(ConnectedPlayers) { // Use ConnectedPlayers directly
                    foreach(var p in ConnectedPlayers) {
                        Vector2 pPos = BulletboxWorld.PlayerLocations.GetValueOrDefault(p.Username, Vector2.Zero);
                        var chunk = BulletboxWorld.GetOrGenerateChunk((int)MathF.Floor(pPos.X / 16), (int)MathF.Floor(pPos.Y / 16), p.CurrentDimension);
                        
                        if (chunk.Biome == BiomeType.AshenWastelands) {
                            p.AshenTime += dt;
                            TriggerAdvancement(p, "EnterAshen");
                            // Spawns after 1 minute in the biome
                            if (p.AshenTime > 60f && p.BrimstalkerCooldown <= 0f && !BulletboxWorld.RaidActive) {
                                    // Advancement: This Seems Safe
                                    if (!p.HasIronOrDiamondWeapons())
                                    {
                                        TriggerAdvancement(p, "ThisSeemsSafe");
                                    }
                                    else
                                    {
                                        // If they have weapons, reset the advancement trigger for next time
                                        p.TriggeredAdvancements.Remove("ThisSeemsSafe");
                                    }
                                SpawnBrimstalker(pPos, rand);
                                TriggerAdvancement(p, "SpawnBrimstalker");
                                p.BrimstalkerCooldown = 300f; // 5 minute cooldown
                            }
                        }
                        if (p.BrimstalkerCooldown > 0) p.BrimstalkerCooldown -= dt;

                        // Dimension and Biome Discovery
                        if (p.LastKnownBiome != chunk.Biome)
                        {
                            p.VisitedBiomes.Add(chunk.Biome);
                            TriggerAdvancement(p, "EnterBiome:" + (byte)chunk.Biome);
                            
                            // Specific biome advancements
                            if (chunk.Biome == BiomeType.Meadow) TriggerAdvancement(p, "TouchGrass");
                            if (chunk.Biome == BiomeType.Beach) TriggerAdvancement(p, "SandyShores");
                            
                            if (p.VisitedBiomes.Count >= 10) TriggerAdvancement(p, "EnterAllBiomes");
                            p.LastKnownBiome = chunk.Biome;
                        }
                        
                        if (p.CurrentDimension == Dimension.TheEnd) {
                            TriggerAdvancement(p, "EnterEnd");
                            p.TimeInEndDimension += dt;
                            if (p.TimeInEndDimension >= 600f) TriggerAdvancement(p, "WhatAreYouDoing");
                        } else p.TimeInEndDimension = 0;

                        if (chunk.Biome == BiomeType.LavaPool) {
                            p.TimeOnLava += dt;
                            if (p.TimeOnLava >= 2.0f) TriggerAdvancement(p, "IRegretNothing");
                        } else p.TimeOnLava = 0;
                    }
                }

                // Lava Pool Damage Logic for ALL entities
                lock(ConnectedPlayers) { // Use ConnectedPlayers directly
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
                                // Advancement: Who Needs Protection? (checked in HandleMobKillAdvancements)
                                {
                                    // Spawn escape portal at (0,0)
                                    Vector2 portalPos = Vector2.Zero;
                                    Structure portal = new Structure(portalPos, StructureType.EndPortal, 0, 0, "");
                                    BulletboxWorld.Structures.TryAdd((0, 0), portal);
                                }
                                
                                BulletboxWorld.Raiders.RemoveAt(i); // Remove if dead
                                lock (ConnectedPlayers) {
                                    foreach (var p in ConnectedPlayers) HandleMobKillAdvancements(p, bot.Name, null); // No specific killer if lava killed it
                                }
                            }
                        }
                    }
                }

                // Player Regeneration Logic (Authoritative)
                _playerRegenTimer += dt;
                if (_playerRegenTimer >= 1.0f) {
                    _playerRegenTimer -= 1.0f;
                    lock(ConnectedPlayers) { // Use ConnectedPlayers directly
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

                    foreach (var kvp in BulletboxWorld.Structures.ToList()) // Use ToList to avoid modification during iteration
                    {
                        var s = kvp.Value; // The current structure (raid outpost)

                        if (s.Type != StructureType.RaidOutpost) continue;

                        foreach (var p in ConnectedPlayers)
                        {
                            Vector2 pPos = BulletboxWorld.PlayerLocations.GetValueOrDefault(p.Username, Vector2.Zero); // Ensure player is in the world
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
                                BroadcastRaidUpdate(1, 1.0f, BulletboxWorld.ActiveRaidOutpostPosition, Dimension.Overworld);
                                BroadcastRaidUpdate(0, 0, null, Dimension.Overworld); // Sync timer to 0 immediately
                            }
                            else BroadcastRaidUpdate(0, BulletboxWorld.RaidTimer, null, Dimension.Overworld);
                        }
                        // If no player is near an outpost, and the timer was counting down, reset it
                        else if (BulletboxWorld.RaidTimer != 9999f)
                        {
                            BulletboxWorld.RaidTimer = 9999f;
                            BroadcastRaidUpdate(0, 9999f, null, Dimension.Overworld);
                        }
                    }
                    else
                    {
                        // Handle active global raid state
                        float currentTotalRaiderHp = 0;
                        var raiders = BulletboxWorld.Raiders.ToList();
                    
                    // NEW: Separate APEX health from general raid pool to prevent influence from other mobs
                    var apex = raiders.FirstOrDefault(r => r.Name == "APEX");
                    if (apex != null)
                    {
                        currentTotalRaiderHp = apex.Health;
                        // APEX fight is not tied to an outpost position for the UI boundary
                        BroadcastRaidUpdate(1, _raidInitialTotalHealth > 0 ? currentTotalRaiderHp / _raidInitialTotalHealth : 0, null, Dimension.TheEnd);
                    }
                    else
                    {
                        foreach (var bot in raiders) { currentTotalRaiderHp += bot.Health; }
                        BroadcastRaidUpdate(1, _raidInitialTotalHealth > 0 ? currentTotalRaiderHp / _raidInitialTotalHealth : 0, BulletboxWorld.ActiveRaidOutpostPosition, Dimension.Overworld);
                    }
                    }
                }
            }
        });
        
        // Client connection acceptance loop
        try {
            while (IsRunning) {
                if (_listener == null || !_listener.Pending()) { await Task.Delay(100); continue; }
                TcpClient clientSocket = await _listener.AcceptTcpClientAsync();
                
                ServerPlayer newPlayer = new ServerPlayer(clientSocket);
                lock(_connectedPlayersLock) { ConnectedPlayers.Add(newPlayer); }
                
                _ = Task.Run(async () => {
                    await newPlayer.Listen(BulletboxWorld);
                    
                    string leavingUser = newPlayer.Username;
                    
                    // This logic was already in place, but ensure it's within the correct lock
                    // NEW: Perform a final authoritative sync to the persistent cache before the player is removed.
                    // This ensures that 100% of progress is captured even if the server saves immediately after disconnect.
                    if (!string.IsNullOrEmpty(leavingUser)) {
                        lock (ConnectedPlayers) {
                            LoadedPlayers[leavingUser] = new PlayerSaveData {
                                Username = newPlayer.Username, Health = newPlayer.Health, MaxHealth = newPlayer.MaxHealth,
                                Hunger = newPlayer.Hunger, TotalMobsKilled = newPlayer.TotalMobsKilled,
                                TotalQuartzObtained = newPlayer.TotalQuartzObtained,
                                TotalRaidshroomsObtained = newPlayer.TotalRaidshroomsObtained,
                                TimeInEndDimension = newPlayer.TimeInEndDimension, TimeOnLava = newPlayer.TimeOnLava,
                                VisitedBiomes = newPlayer.VisitedBiomes, TriggeredAdvancements = newPlayer.TriggeredAdvancements,
                                KilledOverworld = newPlayer.KilledOverworld,
                                Position = newPlayer.Position, Rotation = newPlayer.Rotation, IsBlocking = newPlayer.IsBlocking,
                                CurrentDimension = newPlayer.CurrentDimension, SelectedSlot = newPlayer.SelectedSlot,
                                AshenTime = newPlayer.AshenTime, BrimstalkerCooldown = newPlayer.BrimstalkerCooldown,
                                Inventory = (ServerItemStack[])newPlayer.Inventory.Clone(),
                                CraftingSlot1 = newPlayer.CraftingSlot1, CraftingSlot2 = newPlayer.CraftingSlot2
                            };
                        }
                    }

                    lock(_connectedPlayersLock) 
                    { 
                        ConnectedPlayers.Remove(newPlayer);
                        // Notify all remaining clients that this player is gone
                        foreach(var p in ConnectedPlayers) p.SendLeaveSignal(leavingUser); // Ensure this is thread-safe
                    }
                    Console.WriteLine($"[Server] Player {leavingUser} disconnected.");
                });
            }
        }
        catch (Exception ex) { Console.WriteLine($"[Server] Error: {ex.Message}"); }
        finally { _listener?.Stop(); _listener = null; IsRunning = false; }
    }

    private static void SpawnBrimstalker(Vector2 pos, Random rand)
    {
        BulletboxWorld.RaidActive = true; // Set raid active
        _raidInitialTotalHealth = 1000;
        var bot = new RaiderBot("Brimstalker", pos + new Vector2(rand.Next(-200, 200), rand.Next(-200, 200)));
        bot.MaxHealth = 1000; bot.HeldItemID = "none"; // No weapons
        bot.Health = 1000;
        BulletboxWorld.Raiders.Add(bot);
        BroadcastRaidUpdate(1, 1.0f, null, Dimension.Overworld); // Sync immediately
    }

    private static void SpawnRaidersForOutpost(Structure s, Random rand) {
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

    private static void UpdateRaiderAI(float dt) {
        // This method is called from RunServerAsync, so it needs to be static
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
                BroadcastRaidUpdate(0, 9999f);   // Force timer reset on clients
                _raidInitialTotalHealth = 0;

                // Populate the chest inventory for the outpost that was just defeated
                if (BulletboxWorld.ActiveRaidOutpostPosition.HasValue)
                {
                    Vector2 pos = (Vector2)BulletboxWorld.ActiveRaidOutpostPosition.Value;

                    lock (ConnectedPlayers) {
                        foreach (var p in ConnectedPlayers) {
                            TriggerAdvancement(p, "DefeatRaid");
                        }
                    }

                    Structure? s = BulletboxWorld.Structures.Values.FirstOrDefault(st => (Vector2)st.Position == pos && st.RaidActive);
                    if (s != null)
                    {
                        s.RaidActive = false;
                        PopulateRaidLoot(s, rand);
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

                    // APEX Boss AI Logic
                    if (bot.Name == "APEX")
                    {
                        UpdateApexAI(bot, target, dt, rand, minDist);
                    }
                    Vector2 targetPos = BulletboxWorld.PlayerLocations[target.Username];

                    // APEX Evolutions based on Health
                    if (bot.Name == "APEX")
                    {
                        float hpPct = bot.Health / (float)bot.MaxHealth;
                        if (hpPct <= 0.8f) bot.HeldItemID = "none"; // Switch to special moves
                    }

                    // Unique strafe and target offset per raider to prevent marching in sync
                    int hash = bot.Name.GetHashCode(); // Re-use hash for consistency
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
                            bot.ChargePhase = 0; // Reset charge if teleporting (Apex specific)
                            bot.ChargeTimer = 0;
                        }
                    }

                    // AI Change: Brimstalker never runs away, but Raiders and Flickers can
                    if (bot.Name != "Brimstalker") {
                        if (bot.Health < 30 && bot.FleeTimer <= 0 && rand.Next(100) < 1) bot.FleeTimer = 8.0f;
                    } // End of Brimstalker check

                    if (bot.FleeTimer > 0 && bot.Name != "APEX") { bot.FleeTimer -= dt; dir = -dir; }

                    Vector2 sideStepDir = new Vector2(-dir.Y, dir.X);
                    float strafeFreq = 2.0f + (Math.Abs(hash) % 300 / 100f);
                    float strafeAmp = 150f + (Math.Abs(hash) % 150);
                    float strafeAmount = MathF.Sin(time * strafeFreq + hash) * strafeAmp;

                    bot.Rotation = (float)(Math.Atan2(dir.Y, dir.X) * (180.0 / Math.PI));

                    // --- LAVA AVOIDANCE STEERING ---
                    // Probe 3 points ahead (center, left-diag, right-diag) to steer away from lava
                    Vector2 lavaAvoidance = Vector2.Zero;
                    float[] probeAngles = { 0, -35, 35 };
                    foreach (float angleOffset in probeAngles) {
                        float rad = (bot.Rotation + angleOffset) * (MathF.PI / 180f);
                        Vector2 probePos = bot.Position + new Vector2(MathF.Cos(rad), MathF.Sin(rad)) * 48f;
                        var probeChunk = BulletboxWorld.GetOrGenerateChunk((int)MathF.Floor(probePos.X / 16), (int)MathF.Floor(probePos.Y / 16), bot.Dimension);
                        if (probeChunk.Biome == BiomeType.LavaPool) lavaAvoidance += Vector2.Normalize(bot.Position - probePos);
                    }
                    if (lavaAvoidance != Vector2.Zero) {
                        dir = Vector2.Normalize(dir + lavaAvoidance * 2.5f);
                        bot.Rotation = (float)(Math.Atan2(dir.Y, dir.X) * (180.0 / Math.PI));
                    }

                    float apexHpPct = bot.Name == "APEX" ? bot.Health / (float)bot.MaxHealth : 1.0f;
                    
                    if (bot.Name == "Brimstalker" || (bot.Name.StartsWith("Flicker") && minDist < 250f) || (bot.Name == "APEX" && apexHpPct <= 0.4f)) {
                        // Charge Attack State Machine (Shared by Brimstalker and Aggroed Flicker)
                        if (bot.ChargePhase == 0) {
                            // Ensure the Brimstalker stays mobile and circles the player during charge cooldowns
                            if (bot.Name == "Brimstalker") {
                                float idealDist = 450f;
                                float distFactor = (minDist - idealDist) * 0.5f; // Maintain engagement distance
                                bot.Position += (dir * distFactor + sideStepDir * strafeAmount) * dt;
                            }
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
                    else if (bot.Name.StartsWith("Vortex")) // NEW Vortex AI
                    { // This block is now mostly handled by UpdateApexAI for APEX
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
                            BroadcastGust(bot.Position + new Vector2(32,32), gustDir * gustSpeed, bot.Dimension);
                        }
                    }
                    else if (bot.Name == "Brimstalker")
                    {
                        // Handled in the charge block above for mobility consistency
                    }
                    else if (bot.Name != "APEX" && !bot.Name.StartsWith("Vortex")) // Generic Melee Raider Positioning (Only for non-Apex raiders)
                    {
                        float attackRange = 96f; 
                        if (ServerWeaponStats.Library.TryGetValue(bot.HeldItemID, out var stats)) attackRange = stats.Range * 0.65f;

                        float stopDist = attackRange * 0.85f;
                        float moveSpeed = (minDist < stopDist) ? 60f : 260f;
                        bot.Position += (dir * moveSpeed + sideStepDir * strafeAmount) * dt;
                    } // End of Generic Melee Raider Positioning

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

                    if (!bot.Name.StartsWith("Vortex") && bot.Name != "APEX") // Only non-Apex, non-Vortex raiders use this for attack logic
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
                                BroadcastBomb(bot.Position, bombDir * bombSpeed, bot.Dimension);
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

                // Update PreviousHealth for Apex for damage-based triggers
                if (bot.Name == "APEX") {
                    bot.PreviousHealth = bot.Health;
                }

                // Anti-overlap: Push raiders away from each other and players
                foreach (var other in botsToUpdate) {
                    if (bot == other) continue;
                    float d = Vector2.Distance(bot.Position, other.Position);
                float overlapRadius = (bot.Name == "APEX" || other.Name == "APEX") ? 180f : 45f;
                    if (d < overlapRadius && d > 0.1f) {
                        bot.Position += Vector2.Normalize(bot.Position - other.Position) * (overlapRadius - d) * 0.5f;
                    }
                }
                lock(_connectedPlayersLock) {
                    foreach (var p in ConnectedPlayers) {
                        Vector2 pPos = BulletboxWorld.PlayerLocations.GetValueOrDefault(p.Username, Vector2.Zero);
                        float d = Vector2.Distance(bot.Position, pPos);
                    float overlapRadius = (bot.Name == "APEX") ? 180f : 45f;
                        if (d < overlapRadius && d > 0.1f) {
                            bot.Position += Vector2.Normalize(bot.Position - pPos) * (overlapRadius - d) * 0.5f;
                        }
                    }
                }

                BroadcastBotMove(bot);
            }
        }

    private static void UpdateApexAI(RaiderBot bot, ServerPlayer target, float dt, Random rand, float minDist)
        {
            float hpPct = bot.Health / (float)bot.MaxHealth;
            Vector2 targetPos = BulletboxWorld.PlayerLocations[target.Username];
            int hash = bot.Name.GetHashCode();
            float time = (float)(DateTime.Now.Ticks / 10000000.0);

            // Common movement calculations
            Vector2 targetOffset = new Vector2(MathF.Cos(hash) * 35f, MathF.Sin(hash) * 35f);
            Vector2 dir = Vector2.Normalize((targetPos + targetOffset) - bot.Position);
            Vector2 sideStepDir = new Vector2(-dir.Y, dir.X);
            float strafeFreq = 2.0f + (Math.Abs(hash) % 300 / 100f);
            float strafeAmp = 150f + (Math.Abs(hash) % 150);
            float strafeAmount = MathF.Sin(time * strafeFreq + hash) * strafeAmp;
            
            // Teleport on damage (Stage 4 & 5)
            if (hpPct <= 0.3f && bot.Health < bot.PreviousHealth)
            {
                TeleportApex(bot, rand);
                return; // Skip further AI for this frame after teleport
            }

            // Stage 5: Teleports without damage, 2-4 bombs, charges every 3s
            if (hpPct <= 0.1f)
            {
                bot.ApexTeleportTimer -= dt;
                if (bot.ApexTeleportTimer <= 0)
                {
                    TeleportApex(bot, rand);
                    bot.ApexTeleportTimer = 2.0f + (float)rand.NextDouble() * 1.0f; // Teleport every 2-3 seconds
                    return; // Skip further AI for this frame after teleport
                }

                // Aggressively circle the player while bombarding them in Stage 5
                bot.Position += (dir * ((minDist - 400f) * 0.5f) + sideStepDir * strafeAmount) * dt;

                // Charge every 3 seconds
                if (bot.ChargePhase == 0) {
                    bot.ChargeCooldown -= dt;
                    if (bot.ChargeCooldown <= 0) {
                        bot.ChargePhase = 1;
                        bot.ChargeTimer = 1.0f; // 1s Backing up phase
                        bot.ChargeCooldown = 3.0f; // Fixed 3s cooldown
                    }
                }
                // Bomb attack: 2-4 bombs at a time, very frequent
                bot.AttackTimer += dt;
                if (bot.AttackTimer >= 0.5f) // Very frequent bombs
                {
                    bot.AttackTimer = 0;
                    int numBombs = rand.Next(2, 5); // 2 to 4 bombs
                        for (int i = 0; i < numBombs; i++) // Use a loop for multiple bombs
                    {
                        Vector2 bombDir = Vector2.Normalize(targetPos - bot.Position + new Vector2(rand.Next(-50, 50), rand.Next(-50, 50))); // Slight spread
                        float bombSpeed = 850f;
                        lock(BulletboxWorld.ActiveBombs) {
                            var b = new ServerBomb(bot.Position, bombDir * bombSpeed, bot.Name);
                            b.TargetPlayer = target.Username;
                            BulletboxWorld.ActiveBombs.Add(b);
                        }
                        BroadcastBomb(bot.Position, bombDir * bombSpeed);
                    }
                }
            }
            // Stage 4: Frequent bombs, better aim charge, teleports on damage (handled above)
            else if (hpPct <= 0.3f)
            {
                // Charge more frequently
                if (bot.ChargePhase == 0) {
                    bot.ChargeCooldown -= dt;
                    if (bot.ChargeCooldown <= 0) {
                        bot.ChargePhase = 1;
                        bot.ChargeTimer = 0.8f; // Shorter back off for faster charges
                        bot.ChargeCooldown = 5.0f + (float)rand.NextDouble() * 2.0f; // 5-7s cooldown
                    }
                }
                // Bomb attack: More frequent
                bot.AttackTimer += dt;
                if (bot.AttackTimer >= 1.0f) // More frequent bombs
                {
                    bot.AttackTimer = 0;
                    Vector2 bombDir = Vector2.Normalize(targetPos - bot.Position);
                    float bombSpeed = 850f;
                    lock(BulletboxWorld.ActiveBombs) {
                        var b = new ServerBomb(bot.Position, bombDir * bombSpeed, bot.Name);
                        b.TargetPlayer = target.Username;
                        BulletboxWorld.ActiveBombs.Add(b);
                    }
                    BroadcastBomb(bot.Position, bombDir * bombSpeed, bot.Dimension);
                }
            }
            // Stage 3: No melee, charge attack, backs off, shoots bombs. Initial 10 bombs.
            else if (hpPct <= 0.5f) // 50-30%
            {
                if (!bot.HasTriggeredStage3Intro)
                {
                    bot.HasTriggeredStage3Intro = true;
                    // Spawn 10 bombs simultaneously
                    for (int i = 0; i < 10; i++)
                    {
                        float angle = (float)(rand.NextDouble() * MathF.PI * 2);
                        Vector2 bombDir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                        float bombSpeed = 850f;
                        lock(BulletboxWorld.ActiveBombs) { // Ensure thread safety
                            var b = new ServerBomb(bot.Position, bombDir * bombSpeed, bot.Name);
                            b.TargetPlayer = target.Username; // Target player for better tracking
                            BulletboxWorld.ActiveBombs.Add(b);
                        }
                        BroadcastBomb(bot.Position, bombDir * bombSpeed, bot.Dimension);
                    }
                }
                // AI Loop: Charge or back off/bomb
                if (bot.ChargePhase == 0) {
                    bot.ChargeCooldown -= dt;
                    if (bot.ChargeCooldown <= 0) {
                        bot.ChargePhase = 1;
                        bot.ChargeTimer = 1.0f; // 1s Backing up phase
                        bot.ChargeCooldown = 10.0f + (float)rand.NextDouble() * 5.0f; // Longer cooldown for charge
                    }
                }
                // If not charging, move away and shoot bombs (similar to Stage 2)
                if (bot.ChargePhase == 0) {
                    dir = Vector2.Normalize(bot.Position - targetPos); // Move away
                    // Apply aggressive orbital movement while backing off
                    bot.Position += (dir * 260f + sideStepDir * strafeAmount) * dt;

                    bot.AttackTimer += dt;
                    if (bot.AttackTimer >= 2.0f) // Shoot bombs every 2 seconds
                    {
                        bot.AttackTimer = 0;
                        Vector2 bombDir = Vector2.Normalize(targetPos - bot.Position);
                        float bombSpeed = 850f;
                        lock(BulletboxWorld.ActiveBombs) {
                            var b = new ServerBomb(bot.Position, bombDir * bombSpeed, bot.Name);
                            b.TargetPlayer = target.Username;
                            BulletboxWorld.ActiveBombs.Add(b);
                        }
                            BroadcastBomb(bot.Position, bombDir * bombSpeed, bot.Dimension); // Broadcast each bomb
                    }
                }
            }
            // Stage 2: Backs off longer, shoots bombs
            else if (hpPct <= 0.8f) // 80-50%
            {
                // Maintain engagement distance through circling instead of linear retreat
                float distFactor = (minDist - 550f) * 0.5f;
                bot.Position += (dir * distFactor + sideStepDir * strafeAmount) * dt;

                bot.AttackTimer += dt;
                if (bot.AttackTimer >= 3.0f) // Shoot bombs every 3 seconds
                {
                    bot.AttackTimer = 0;
                    Vector2 bombDir = Vector2.Normalize(targetPos - bot.Position);
                    float bombSpeed = 850f;
                    lock(BulletboxWorld.ActiveBombs) {
                        var b = new ServerBomb(bot.Position, bombDir * bombSpeed, bot.Name);
                        b.TargetPlayer = target.Username;
                        BulletboxWorld.ActiveBombs.Add(b);
                    }
                    BroadcastBomb(bot.Position, bombDir * bombSpeed, bot.Dimension);
                }
            }
            // Stage 1: Melee attacks, backs off, comes back for more attacks, repeat.
            else // hpPct > 0.8f (100-80%)
            {
                float attackRange = 96f; // Standard melee range
                float stopDist = attackRange * 0.85f;
                float moveSpeed = (minDist < stopDist) ? 60f : 260f;
                bot.Position += (dir * moveSpeed + sideStepDir * strafeAmount) * dt;

                // Standard melee attack logic
                bot.AttackTimer += dt;
                if (minDist < attackRange && bot.AttackTimer >= bot.AttackCooldown) {
                    target.Damage(12); // Standard melee damage
                    bot.AttackTimer = 0;
                }
            }

            // Handle Charge Phases (shared by stages that use charge)
            if (bot.ChargePhase == 1) {
                // Phase 1: Back away from the player to telegraph the charge
                Vector2 fromPlayer = Vector2.Normalize(bot.Position - targetPos);
                bot.Position += fromPlayer * 250f * dt;
                bot.ChargeTimer -= dt;
                if (bot.ChargeTimer <= 0) { // Transition to charge phase
                    bot.ChargePhase = 2;
                    bot.ChargeTimer = 0.7f; // Charge duration
                    bot.ChargeDirection = Vector2.Normalize(targetPos - bot.Position);
                    bot.HasDealtChargeDamage = false;
                }
            }
            else if (bot.ChargePhase == 2) {
                // Phase 2: High speed charge
                bot.Position += bot.ChargeDirection * 1300f * dt;
                bot.ChargeTimer -= dt;
                
                float hitRadius = 45f; // Apex charge hit radius
                int damage = 40; // Apex charge damage

                if (!bot.HasDealtChargeDamage && Vector2.Distance(bot.Position, targetPos) < hitRadius) {
                    target.Damage(damage);
                    target.SyncHealth();
                    bot.HasDealtChargeDamage = true;
                }

                if (bot.ChargeTimer <= 0) {
                    bot.ChargePhase = 0;
                }
            }
        }

    private static void PopulateRaidLoot(Structure s, Random rand)
    {
        s.IsCompleted = true; // Mark as completed for chest access after victory
        s.ChestInventory = new ServerItemStack[18];
        for (int j = 0; j < 18; j++) s.ChestInventory[j] = new ServerItemStack("none", 0);

        // Weighted loot pool: Higher weight means more common
        var pool = new List<LootPoolEntry> {
            new LootPoolEntry("rock", 60, 1, 3),
            new LootPoolEntry("raidshroom", 95, 2, 8),
            new LootPoolEntry("copper", 60, 1, 3),
            new LootPoolEntry("iron", 50, 1, 4),
            new LootPoolEntry("quartz", 20, 1, 3),
            new LootPoolEntry("pearl", 10, 1, 3),
            new LootPoolEntry("shield", 20, 1, 1),
            new LootPoolEntry("wooden_axe", 20, 1, 1),
            new LootPoolEntry("wooden_scythe", 10, 1, 1),
            new LootPoolEntry("wooden_spear", 15, 1, 1),
            new LootPoolEntry("stone_sword", 10, 1, 1),
            new LootPoolEntry("stone_axe", 15, 1, 1),
            new LootPoolEntry("stone_scythe", 20, 1, 1),
            new LootPoolEntry("stone_spear", 20, 1, 1),
            new LootPoolEntry("diamond", 9, 1, 3),
            new LootPoolEntry("stone_kanabo", 4, 1, 1)
        };

        int totalWeight = pool.Sum(e => e.Weight);
        int lootCount = rand.Next(4, 9);

        for (int j = 0; j < lootCount; j++) 
        {
            int roll = rand.Next(totalWeight);
            int currentWeight = 0;
            LootPoolEntry selected = pool[0];

            foreach (var entry in pool) {
                currentWeight += entry.Weight;
                if (roll < currentWeight) { selected = entry; break; }
            }

            int count = rand.Next(selected.MinCount, selected.MaxCount + 1);
            int slot = rand.Next(18);
            s.ChestInventory[slot] = new ServerItemStack(selected.ItemID, count);
        }
    }

    private static void TeleportApex(RaiderBot bot, Random rand)
    {
        // This method is called from UpdateApexAI, so it needs to be static
            // Teleport 200-400 units away in a random direction
            float angle = (float)(rand.NextDouble() * MathF.PI * 2);
            float dist = rand.Next(200, 400);
            bot.Position += new Vector2(MathF.Cos(angle) * dist, MathF.Sin(angle) * dist);
            // Reset any ongoing charge or flee
            bot.ChargePhase = 0;
            bot.ChargeTimer = 0;
            bot.FleeTimer = 0;
            bot.WanderTarget = null;
            Console.WriteLine($"[Server] APEX teleported to {bot.Position.X}, {bot.Position.Y}");
        }

    private static void BroadcastRaidUpdate(byte type, float val, SerializableVector2? outpostCenter = null, Dimension? targetDim = null) {
        // This method is called from RunServerAsync and UpdateRaiderAI, so it needs to be static
            lock (_connectedPlayersLock) {
                foreach (var p in ConnectedPlayers) {
                    if (targetDim.HasValue && p.CurrentDimension != targetDim.Value) continue;
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

    private static void BroadcastBomb(Vector2 start, Vector2 velocity, Dimension? targetDim = null) {
        // This method is called from RunServerAsync and UpdateApexAI, so it needs to be static
            lock (_connectedPlayersLock) {
                foreach (var p in ConnectedPlayers) {
                    if (targetDim.HasValue && p.CurrentDimension != targetDim.Value) continue;
                    try { lock (p.WriterLock) { 
                        p.Writer.Write((byte)16); 
                        p.Writer.Write(start.X); p.Writer.Write(start.Y);
                        p.Writer.Write(velocity.X); p.Writer.Write(velocity.Y);
                        p.Writer.Flush(); 
                    } } catch { }
                }
            }
        }

    private static void BroadcastGust(Vector2 start, Vector2 velocity, Dimension? targetDim = null) { // NEW
        // This method is called from RunServerAsync and UpdateRaiderAI, so it needs to be static
            lock (_connectedPlayersLock) {
                foreach (var p in ConnectedPlayers) {
                    if (targetDim.HasValue && p.CurrentDimension != targetDim.Value) continue;
                    try { lock (p.WriterLock) { 
                        p.Writer.Write((byte)17); // Packet ID 17 for gust
                        p.Writer.Write(start.X); p.Writer.Write(start.Y);
                        p.Writer.Write(velocity.X); p.Writer.Write(velocity.Y);
                        p.Writer.Flush(); 
                    } } catch { }
                }
            }
        }


    private static void BroadcastBotMove(RaiderBot bot) {
        // This method is called from RunServerAsync and UpdateRaiderAI, so it needs to be static
            lock (_connectedPlayersLock) {
                foreach (var p in ConnectedPlayers) {
                    try {
                        lock (p.WriterLock) {
                            if (p.CurrentDimension != bot.Dimension) continue; // Optimization: Only sync to same dimension
                            p.Writer.Write((byte)1); 
                            p.Writer.Write(bot.Name); 
                            p.Writer.Write(bot.Position.X);
                            p.Writer.Write(bot.Position.Y); 
                            p.Writer.Write(bot.Rotation); 
                            p.Writer.Write(bot.HeldItemID ?? "none"); 
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

    public static void ResetServerState()
    {
        Console.WriteLine("[Server] Wiping internal memory for fresh world state...");
        IsRunning = false;
        try {
            _listener?.Stop();
            _listener = null;
        } catch {}

        // Wipe all player caches
        lock (_connectedPlayersLock)
        {
            ConnectedPlayers.Clear();
        }
        lock (LoadedPlayers)
        {
            LoadedPlayers.Clear();
        }

        // Destroy the old world object and its chunk/entity caches
        BulletboxWorld = new ServerWorld();
        _worldTime = 0f;
        _playerRegenTimer = 0f;
        _autoSaveTimer = 0f;
        _raidInitialTotalHealth = 0f;
    }

    private static void InitializeDatabase()
    {
        string dbPath = GetDatabasePath();
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS WorldData (Key TEXT PRIMARY KEY, Value TEXT);
                    CREATE TABLE IF NOT EXISTS PlayerData (
                        Username TEXT PRIMARY KEY, Health INTEGER, MaxHealth INTEGER, Hunger INTEGER,
                        PositionX REAL, PositionY REAL, Rotation REAL, IsBlocking INTEGER,
                        CurrentDimension INTEGER, SelectedSlot INTEGER, AshenTime REAL, BrimstalkerCooldown REAL,
                        Inventory TEXT, CraftingSlot1ItemID TEXT, CraftingSlot1Count INTEGER,
                        CraftingSlot2ItemID TEXT, CraftingSlot2Count INTEGER, TimeInEndDimension REAL,
                        TimeOnLava REAL, TotalMobsKilled INTEGER, TotalQuartzObtained INTEGER,
                        TotalRaidshroomsObtained INTEGER, VisitedBiomes TEXT, KilledOverworld TEXT,
                        TriggeredAdvancements TEXT
                    );
                    CREATE TABLE IF NOT EXISTS Raiders (Id INTEGER PRIMARY KEY AUTOINCREMENT, Data TEXT);
                    CREATE TABLE IF NOT EXISTS Structures (PosKey TEXT PRIMARY KEY, Data TEXT);
                    CREATE TABLE IF NOT EXISTS ActiveBombs (Data TEXT);
                    CREATE TABLE IF NOT EXISTS ActiveGusts (Data TEXT);
                ";
                command.ExecuteNonQuery();
            }
        }
    }

    public static async Task SaveGameAsync() { // Save method updated for SQLite
        // REMOVED !IsRunning check: It prevents saving during server shutdown!
        if (Program.LastIP != "127.0.0.1") return;

        WorldSaveData saveData = new WorldSaveData();

        // 1. Snapshot Data (Brief Locking)
        lock (LoadedPlayers) {
            // CRITICAL: Clear the connected player snapshot and rebuild it from the live players
            // We don't clear the whole dictionary so that offline players stay in the save.
            
            // Sync current online player data into the persistent dictionary cache
            foreach (var p in ConnectedPlayers) {
                if (string.IsNullOrEmpty(p.Username)) continue;

                lock (_connectedPlayersLock) {
                    LoadedPlayers[p.Username] = new PlayerSaveData {
                    Username = p.Username, Health = p.Health, MaxHealth = p.MaxHealth,
                    Hunger = p.Hunger, TotalMobsKilled = p.TotalMobsKilled,
                    TotalQuartzObtained = p.TotalQuartzObtained,
                    TotalRaidshroomsObtained = p.TotalRaidshroomsObtained,
                    TimeInEndDimension = p.TimeInEndDimension, TimeOnLava = p.TimeOnLava,
                    VisitedBiomes = new HashSet<BiomeType>(p.VisitedBiomes), 
                    TriggeredAdvancements = new HashSet<string>(p.TriggeredAdvancements),
                    KilledOverworld = new HashSet<string>(p.KilledOverworld),
                    Position = p.Position, Rotation = p.Rotation, IsBlocking = p.IsBlocking,
                    CurrentDimension = p.CurrentDimension, SelectedSlot = p.SelectedSlot, 
                    AshenTime = p.AshenTime, BrimstalkerCooldown = p.BrimstalkerCooldown,
                    Inventory = (ServerItemStack[])p.Inventory.Clone(), // Save a unique copy of the items
                    CraftingSlot1 = p.CraftingSlot1, CraftingSlot2 = p.CraftingSlot2
                };
                }
            }
        }

        saveData.Seed = BulletboxWorld.Seed;
        
        // Persist ALL known players
        lock (LoadedPlayers) { saveData.Players.AddRange(LoadedPlayers.Values); }

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
        saveData.PlayerRegenTimer = _playerRegenTimer;
        saveData.FlickerSpawnTimer = _flickerSpawnTimer;
        saveData.RaidTimer = BulletboxWorld.RaidTimer;
        saveData.RaidActive = BulletboxWorld.RaidActive;
        saveData.ActiveRaidOutpostPosition = BulletboxWorld.ActiveRaidOutpostPosition;

        // 2. Perform Slow I/O Asynchronously
        try {
            string dbPath = GetDatabasePath();
            using (var connection = new SqliteConnection($"Data Source={dbPath}"))
            {
                await connection.OpenAsync();

                // Start a transaction for atomicity
                using (var transaction = connection.BeginTransaction())
                {
                    // Save World Data
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = 
                            @"INSERT OR REPLACE INTO WorldData (Key, Value) VALUES 
                            ('Seed', @Seed), ('WorldTime', @WorldTime),
                            ('PlayerRegenTimer', @PlayerRegenTimer), ('FlickerSpawnTimer', @FlickerSpawnTimer),
                            ('RaidTimer', @RaidTimer), ('RaidActive', @RaidActive),
                            ('ActiveRaidOutpostPositionX', @ActiveRaidOutpostPositionX), ('ActiveRaidOutpostPositionY', @ActiveRaidOutpostPositionY),
                            ('Version', @Version);";
                        command.Parameters.AddWithValue("@Seed", saveData.Seed);
                        command.Parameters.AddWithValue("@WorldTime", saveData.WorldTime);
                        command.Parameters.AddWithValue("@PlayerRegenTimer", saveData.PlayerRegenTimer);
                        command.Parameters.AddWithValue("@FlickerSpawnTimer", saveData.FlickerSpawnTimer);
                        command.Parameters.AddWithValue("@RaidTimer", saveData.RaidTimer);
                        command.Parameters.AddWithValue("@RaidActive", saveData.RaidActive);
                        command.Parameters.AddWithValue("@ActiveRaidOutpostPositionX", saveData.ActiveRaidOutpostPosition?.X ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@ActiveRaidOutpostPositionY", saveData.ActiveRaidOutpostPosition?.Y ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@Version", Program.VERSION);
                        command.Transaction = transaction;
                        await command.ExecuteNonQueryAsync();
                    }

                    // Save Player Data (INSERT OR REPLACE based on Username)
                    foreach (var pData in saveData.Players)
                    {
                        using (var command = connection.CreateCommand())
                        {
                            // For complex objects like Inventory, VisitedBiomes, TriggeredAdvancements,
                            // you'd typically serialize them to JSON strings or create separate tables.
                            // Here, we'll serialize Inventory as an example.
                            string inventoryJson = JsonSerializer.Serialize(pData.Inventory);
                            string visitedBiomesJson = JsonSerializer.Serialize(pData.VisitedBiomes);
                            string triggeredAdvancementsJson = JsonSerializer.Serialize(pData.TriggeredAdvancements);
                            string killedOverworldJson = JsonSerializer.Serialize(pData.KilledOverworld);

                            command.CommandText =
                                @"INSERT OR REPLACE INTO PlayerData (
                                Username, Health, MaxHealth, Hunger, PositionX, PositionY, Rotation, IsBlocking, CurrentDimension, SelectedSlot,
                                AshenTime, BrimstalkerCooldown, Inventory, CraftingSlot1ItemID, CraftingSlot1Count, CraftingSlot2ItemID, CraftingSlot2Count,
                                TimeInEndDimension, TimeOnLava, TotalMobsKilled, TotalQuartzObtained, TotalRaidshroomsObtained,
                                VisitedBiomes, KilledOverworld, TriggeredAdvancements
                                ) VALUES (
                                @Username, @Health, @MaxHealth, @Hunger, @PositionX, @PositionY, @Rotation, @IsBlocking, @CurrentDimension, @SelectedSlot,
                                @AshenTime, @BrimstalkerCooldown, @Inventory, @CraftingSlot1ItemID, @CraftingSlot1Count, @CraftingSlot2ItemID, @CraftingSlot2Count,
                                @TimeInEndDimension, @TimeOnLava, @TotalMobsKilled, @TotalQuartzObtained, @TotalRaidshroomsObtained,
                                @VisitedBiomes, @KilledOverworld, @TriggeredAdvancements
                                );";

                            command.Parameters.AddWithValue("@Username", pData.Username);
                            command.Parameters.AddWithValue("@Health", pData.Health);
                            command.Parameters.AddWithValue("@MaxHealth", pData.MaxHealth);
                            command.Parameters.AddWithValue("@Hunger", pData.Hunger);
                            command.Parameters.AddWithValue("@PositionX", pData.Position.X);
                            command.Parameters.AddWithValue("@PositionY", pData.Position.Y);
                            command.Parameters.AddWithValue("@Rotation", pData.Rotation);
                            command.Parameters.AddWithValue("@IsBlocking", pData.IsBlocking ? 1 : 0);
                            command.Parameters.AddWithValue("@CurrentDimension", (int)pData.CurrentDimension);
                            command.Parameters.AddWithValue("@SelectedSlot", pData.SelectedSlot);
                            command.Parameters.AddWithValue("@AshenTime", pData.AshenTime);
                            command.Parameters.AddWithValue("@BrimstalkerCooldown", pData.BrimstalkerCooldown);
                            command.Parameters.AddWithValue("@Inventory", inventoryJson);
                            command.Parameters.AddWithValue("@CraftingSlot1ItemID", pData.CraftingSlot1.ItemID ?? "none");
                            command.Parameters.AddWithValue("@CraftingSlot1Count", pData.CraftingSlot1.Count);
                            command.Parameters.AddWithValue("@CraftingSlot2ItemID", pData.CraftingSlot2.ItemID ?? "none");
                            command.Parameters.AddWithValue("@CraftingSlot2Count", pData.CraftingSlot2.Count);
                            command.Parameters.AddWithValue("@TimeInEndDimension", pData.TimeInEndDimension);
                            command.Parameters.AddWithValue("@TimeOnLava", pData.TimeOnLava);
                            command.Parameters.AddWithValue("@TotalMobsKilled", pData.TotalMobsKilled);
                            command.Parameters.AddWithValue("@TotalQuartzObtained", pData.TotalQuartzObtained);
                            command.Parameters.AddWithValue("@TotalRaidshroomsObtained", pData.TotalRaidshroomsObtained);
                            command.Parameters.AddWithValue("@VisitedBiomes", visitedBiomesJson);
                            command.Parameters.AddWithValue("@KilledOverworld", killedOverworldJson);
                            command.Parameters.AddWithValue("@TriggeredAdvancements", triggeredAdvancementsJson);
                            command.Transaction = transaction;
                            await command.ExecuteNonQueryAsync();
                        }
                    }

                    // Save Active Bombs
                    using (var cmd = connection.CreateCommand()) { cmd.CommandText = "DELETE FROM ActiveBombs;"; cmd.Transaction = transaction; cmd.ExecuteNonQuery(); }
                    foreach (var bomb in saveData.ActiveBombs)
                    {
                        using (var cmd = connection.CreateCommand())
                        {
                            cmd.CommandText = "INSERT INTO ActiveBombs (Data) VALUES (@Data);";
                            cmd.Parameters.AddWithValue("@Data", JsonSerializer.Serialize(bomb));
                            cmd.Transaction = transaction;
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }

                    // Save Active Gusts
                    using (var cmd = connection.CreateCommand()) { cmd.CommandText = "DELETE FROM ActiveGusts;"; cmd.Transaction = transaction; cmd.ExecuteNonQuery(); }
                    foreach (var gust in saveData.ActiveGusts)
                    {
                        using (var cmd = connection.CreateCommand())
                        {
                            cmd.CommandText = "INSERT INTO ActiveGusts (Data) VALUES (@Data);";
                            cmd.Parameters.AddWithValue("@Data", JsonSerializer.Serialize(gust));
                            cmd.Transaction = transaction;
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }

                    // Save Raiders
                    using (var cmd = connection.CreateCommand()) { cmd.CommandText = "DELETE FROM Raiders;"; cmd.Transaction = transaction; cmd.ExecuteNonQuery(); }
                    foreach (var r in saveData.Raiders)
                    {
                        using (var cmd = connection.CreateCommand())
                        {
                            cmd.CommandText = "INSERT INTO Raiders (Data) VALUES (@Data);";
                            cmd.Parameters.AddWithValue("@Data", JsonSerializer.Serialize(r));
                            cmd.Transaction = transaction;
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }

                    // Save Structures
                    foreach (var entry in saveData.Structures)
                    {
                        using (var cmd = connection.CreateCommand())
                        {
                            cmd.CommandText = "INSERT OR REPLACE INTO Structures (PosKey, Data) VALUES (@PosKey, @Data);";
                            cmd.Parameters.AddWithValue("@PosKey", entry.Key);
                            cmd.Parameters.AddWithValue("@Data", JsonSerializer.Serialize(entry.Value));
                            cmd.Transaction = transaction;
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }

                    transaction.Commit();
                }
            }
            Console.WriteLine($"[Server] Game state saved to SQLite: {dbPath}");
        } catch (Exception ex) {
            Console.WriteLine($"[Server] Error saving game state: {ex.Message}");
        }
    }

    public static bool LoadGame() {
        InitializeDatabase(); // Ensure tables exist before loading
        string dbPath = GetDatabasePath();
        if (!File.Exists(dbPath)) return false;

        Console.WriteLine("[Server] Loading game state from SQLite...");
        try {
            using (var connection = new SqliteConnection($"Data Source={dbPath}"))
            {
                connection.Open();

                // Load World Data
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT Key, Value FROM WorldData";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string key = reader.GetString(0);
                            string val = reader.GetValue(1).ToString() ?? "";
                            if (key == "Seed") BulletboxWorld.Seed = int.Parse(val);
                            else if (key == "WorldTime") _worldTime = float.Parse(val);
                            else if (key == "PlayerRegenTimer") _playerRegenTimer = float.Parse(val);
                            else if (key == "FlickerSpawnTimer") _flickerSpawnTimer = float.Parse(val);
                            else if (key == "RaidTimer") BulletboxWorld.RaidTimer = float.Parse(val);
                            else if (key == "RaidActive") BulletboxWorld.RaidActive = val == "1" || val.Equals("True", StringComparison.OrdinalIgnoreCase);
                            else if (key == "Version" && Program.CurrentWorldData != null) Program.CurrentWorldData.Version = val;
                            else if (key == "ActiveRaidOutpostPositionX") {
                                if (float.TryParse(val, out float x)) {
                                    var current = BulletboxWorld.ActiveRaidOutpostPosition ?? new SerializableVector2(0, 0);
                                    BulletboxWorld.ActiveRaidOutpostPosition = new SerializableVector2(x, current.Y);
                                }
                            }
                            else if (key == "ActiveRaidOutpostPositionY") {
                                if (float.TryParse(val, out float y)) {
                                    var current = BulletboxWorld.ActiveRaidOutpostPosition ?? new SerializableVector2(0, 0);
                                    BulletboxWorld.ActiveRaidOutpostPosition = new SerializableVector2(current.X, y);
                                }
                            }
                        }
                    }
                }

                // Load Raiders
                lock (BulletboxWorld.Raiders)
                {
                    BulletboxWorld.Raiders.Clear();
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "SELECT Data FROM Raiders";
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var rData = JsonSerializer.Deserialize<RaiderSaveData>(reader.GetString(0));
                                if (rData == null) continue;
                                var bot = new RaiderBot(rData.Name, rData.Position) {
                                    Velocity = rData.Velocity, Health = rData.Health,
                                    PreviousHealth = rData.PreviousHealth, MaxHealth = rData.MaxHealth,
                                    Rotation = rData.Rotation, AttackTimer = rData.AttackTimer,
                                    HeldItemID = rData.HeldItemID ?? "none", AttackCooldown = rData.AttackCooldown,
                                    FleeTimer = rData.FleeTimer, WanderTarget = rData.WanderTarget,
                                    WanderWaitTimer = rData.WanderWaitTimer, ChargePhase = rData.ChargePhase,
                                    ChargeTimer = rData.ChargeTimer, ChargeCooldown = rData.ChargeCooldown,
                                    ChargeDirection = rData.ChargeDirection, HasDealtChargeDamage = rData.HasDealtChargeDamage,
                                    Dimension = rData.Dimension
                                };
                                BulletboxWorld.Raiders.Add(bot);
                            }
                        }
                    }
                }

                // Load Active Bombs
                lock (BulletboxWorld.ActiveBombs)
                {
                    BulletboxWorld.ActiveBombs.Clear();
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "SELECT Data FROM ActiveBombs";
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var bombData = JsonSerializer.Deserialize<ServerBomb>(reader.GetString(0));
                                if (bombData != null)
                                {
                                    BulletboxWorld.ActiveBombs.Add(bombData);
                                }
                            }
                        }
                    }
                }

                // Load Active Gusts
                lock (BulletboxWorld.ActiveGusts)
                {
                    BulletboxWorld.ActiveGusts.Clear();
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "SELECT Data FROM ActiveGusts";
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var gustData = JsonSerializer.Deserialize<ServerGust>(reader.GetString(0));
                                if (gustData != null)
                                {
                                    BulletboxWorld.ActiveGusts.Add(gustData);
                                }
                            }
                        }
                    }
                }

                // Load Structures
                lock (BulletboxWorld.Structures)
                {
                    BulletboxWorld.Structures.Clear();
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "SELECT Data FROM Structures";
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var sData = JsonSerializer.Deserialize<Structure>(reader.GetString(0));
                                if (sData != null)
                                {
                                    BulletboxWorld.Structures.TryAdd((sData.ChunkX, sData.ChunkY), sData);
                                }
                            }
                        }
                    }
                }

                // Load Players
                lock (LoadedPlayers)
                {
                    LoadedPlayers.Clear();
                    using (var cmd = connection.CreateCommand())
                    {
                        // Use explicit column names to prevent index mismatch bugs
                        cmd.CommandText = @"SELECT 
                            Username, Health, MaxHealth, Hunger, PositionX, PositionY, Rotation, IsBlocking, 
                            CurrentDimension, SelectedSlot, AshenTime, BrimstalkerCooldown, Inventory, 
                            CraftingSlot1ItemID, CraftingSlot1Count, CraftingSlot2ItemID, CraftingSlot2Count, 
                            TimeInEndDimension, TimeOnLava, TotalMobsKilled, TotalQuartzObtained, 
                            TotalRaidshroomsObtained, VisitedBiomes, KilledOverworld, TriggeredAdvancements 
                            FROM PlayerData";
                            
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var pData = new PlayerSaveData();
                                pData.Username = reader.GetString(reader.GetOrdinal("Username"));
                                pData.Health = reader.GetInt32(reader.GetOrdinal("Health"));
                                pData.MaxHealth = reader.GetInt32(reader.GetOrdinal("MaxHealth"));
                                pData.Hunger = reader.GetInt32(reader.GetOrdinal("Hunger"));
                                pData.Position = new Vector2(reader.GetFloat(reader.GetOrdinal("PositionX")), reader.GetFloat(reader.GetOrdinal("PositionY")));
                                pData.Rotation = reader.GetFloat(reader.GetOrdinal("Rotation"));
                                pData.IsBlocking = reader.GetInt32(reader.GetOrdinal("IsBlocking")) == 1;
                                pData.CurrentDimension = (Dimension)reader.GetInt32(reader.GetOrdinal("CurrentDimension"));
                                pData.SelectedSlot = reader.GetInt32(reader.GetOrdinal("SelectedSlot"));
                                pData.AshenTime = reader.GetFloat(reader.GetOrdinal("AshenTime"));
                                pData.BrimstalkerCooldown = reader.GetFloat(reader.GetOrdinal("BrimstalkerCooldown"));
                                pData.Inventory = JsonSerializer.Deserialize<ServerItemStack[]>(reader.GetString(reader.GetOrdinal("Inventory"))) ?? new ServerItemStack[25];
                                pData.CraftingSlot1 = new ServerItemStack(reader.GetString(reader.GetOrdinal("CraftingSlot1ItemID")), reader.GetInt32(reader.GetOrdinal("CraftingSlot1Count")));
                                pData.CraftingSlot2 = new ServerItemStack(reader.GetString(reader.GetOrdinal("CraftingSlot2ItemID")), reader.GetInt32(reader.GetOrdinal("CraftingSlot2Count")));
                                pData.TimeInEndDimension = reader.GetFloat(reader.GetOrdinal("TimeInEndDimension"));
                                pData.TimeOnLava = reader.GetFloat(reader.GetOrdinal("TimeOnLava"));
                                pData.TotalMobsKilled = reader.GetInt32(reader.GetOrdinal("TotalMobsKilled"));
                                pData.TotalQuartzObtained = reader.GetInt32(reader.GetOrdinal("TotalQuartzObtained"));
                                pData.TotalRaidshroomsObtained = reader.GetInt32(reader.GetOrdinal("TotalRaidshroomsObtained"));
                                pData.VisitedBiomes = JsonSerializer.Deserialize<HashSet<BiomeType>>(reader.GetString(reader.GetOrdinal("VisitedBiomes"))) ?? new HashSet<BiomeType>();
                                pData.KilledOverworld = JsonSerializer.Deserialize<HashSet<string>>(reader.GetString(reader.GetOrdinal("KilledOverworld"))) ?? new HashSet<string>();
                                pData.TriggeredAdvancements = JsonSerializer.Deserialize<HashSet<string>>(reader.GetString(reader.GetOrdinal("TriggeredAdvancements"))) ?? new HashSet<string>();
                                
                                LoadedPlayers[pData.Username] = pData;
                            }
                        }
                    }
                }
            }
            return true;
        } catch (Exception ex) {
            Console.WriteLine($"[Server] Error loading game state: {ex.Message}");
            return false;
        }
    }

    private struct LootPoolEntry
    {
        public string ItemID;
        public int Weight;
        public int MinCount;
        public int MaxCount;
        public LootPoolEntry(string id, int weight, int min, int max)
        {
            ItemID = id; Weight = weight; MinCount = min; MaxCount = max;
        }
    }

    public static void TriggerAdvancement(ServerPlayer p, string id)
    {
        if (string.IsNullOrEmpty(p.Username) || p.TriggeredAdvancements.Contains(id)) return;

        try {
            lock (p.WriterLock) {
                p.TriggeredAdvancements.Add(id);
                p.Writer.Write((byte)25); // Use 25 to avoid collisions with Chest Move (20)
                p.Writer.Write(id);
                p.Writer.Write(true); // Indicate this is a new trigger that should show a popup
                p.Writer.Flush();
            }
        } catch {}
    }

    public static void HandleMobKillAdvancements(ServerPlayer killer, string mobName, ServerPlayer? attacker)
    {
        if (killer == null) return;

        killer.TotalMobsKilled++;
        if (killer.TotalMobsKilled == 1) TriggerAdvancement(killer, "FirstBlood");
        if (killer.TotalMobsKilled == 25) TriggerAdvancement(killer, "GettingStronger");

        // Specific mob kill advancements
        if (mobName == "APEX") {
            TriggerAdvancement(killer, "DefeatApex");
            if (attacker != null && !attacker.HasShield()) {
                TriggerAdvancement(attacker, "WhoNeedsProtection");
            }
        }
        if (mobName == "Brimstalker") TriggerAdvancement(killer, "DefeatBrimstalker");
        if (mobName.StartsWith("Raider")) TriggerAdvancement(killer, "Kill:Raider");
        if (mobName.StartsWith("Flicker")) TriggerAdvancement(killer, "Kill:Flicker");
        if (mobName.StartsWith("Vortex")) TriggerAdvancement(killer, "Kill:Vortex");
        if (mobName == "Brimstalker") TriggerAdvancement(killer, "Kill:Brimstalker");

        // Master Hunter Tracking (KillAllOverworld)
        if (mobName.StartsWith("Raider")) killer.KilledOverworld.Add("Raider");
        if (mobName.StartsWith("Flicker")) killer.KilledOverworld.Add("Flicker");
        if (mobName.StartsWith("Vortex")) killer.KilledOverworld.Add("Vortex");
        if (mobName == "Brimstalker") killer.KilledOverworld.Add("Brimstalker");

        if (killer.KilledOverworld.Count >= 4) TriggerAdvancement(killer, "KillAllOverworld");
    }
}