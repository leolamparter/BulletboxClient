using BulletboxClient;
using Raylib_cs;
using System.Numerics;
using System.Collections.Generic;

public class AdvancementsScreen
{
    public struct AdvancementDisplayData
    {
        public string Title;
        public string Description;
        public string Key;
    }

    private UIButton backButton;
    private float _scrollOffset = 0f;
    private const float ScrollSpeed = 30f; // Pixels per scroll unit
    private const int ItemHeight = 80; // Height of each advancement display box
    private const int ItemSpacing = 10; // Spacing between boxes

    public AdvancementsScreen()
    {
        backButton = new UIButton("BACK", Vector2.Zero, 30, true);
    }

    public static AdvancementDisplayData[] AllAdvancements = new[]
    {
        new AdvancementDisplayData { Title = "Raid Conqueror", Description = "Complete a raid.", Key = "DefeatRaid" },
        new AdvancementDisplayData { Title = "Shiny Rocks", Description = "Obtain Diamonds.", Key = "ObtainDiamonds" },
        new AdvancementDisplayData { Title = "Blade of Brilliance", Description = "Craft a Diamond Sword.", Key = "ObtainDiamondSword" },
        new AdvancementDisplayData { Title = "Bonk.", Description = "Wield a Stone Kanabo.", Key = "ObtainKanabo" },
        new AdvancementDisplayData { Title = "Diamond Arsenal", Description = "Obtain a full set of Diamond Weapons.", Key = "ObtainAllDiamond" },
        new AdvancementDisplayData { Title = "Ashen Explorer", Description = "Enter an Ashen Wasteland.", Key = "EnterAshen" },
        new AdvancementDisplayData { Title = "Adventurer's Quest", Description = "Discover all Overworld Biomes.", Key = "EnterAllBiomes" },
        new AdvancementDisplayData { Title = "Brimstalker's Awakening", Description = "Provoke a Brimstalker.", Key = "SpawnBrimstalker" },
        new AdvancementDisplayData { Title = "Brimslayer", Description = "Defeat a Brimstalker.", Key = "DefeatBrimstalker" },
        new AdvancementDisplayData { Title = "That's Hot!", Description = "Obtain a Brimstone Weapon.", Key = "ObtainBrimstone"},
        new AdvancementDisplayData { Title = "Master Hunter", Description = "Eliminate every Overworld Mob and Boss.", Key = "KillAllOverworld" },
        new AdvancementDisplayData { Title = "Ender's Gateway", Description = "Acquire a Brimstone Pearl.", Key = "ObtainBrimstonePearl" },
        new AdvancementDisplayData { Title = "The End?", Description = "Enter the End dimension.", Key = "EnterEnd" },
        new AdvancementDisplayData { Title = "The End.", Description = "Vanquish the Apex.", Key = "DefeatApex" },
        new AdvancementDisplayData { Title = "Rock Bottom", Description = "Obtain a rock.", Key = "RockBottom" },
        new AdvancementDisplayData { Title = "Oxidized", Description = "Obtain copper.", Key = "Oxidized" },
        new AdvancementDisplayData { Title = "Iron Age", Description = "Obtain iron.", Key = "IronAge" },
        new AdvancementDisplayData { Title = "Reinforced", Description = "Upgrade a tool to iron.", Key = "Reinforced" },
        new AdvancementDisplayData { Title = "First Blood", Description = "Kill a mob.", Key = "FirstBlood" },
        new AdvancementDisplayData { Title = "Getting Stronger", Description = "Kill 25 mobs.", Key = "GettingStronger" },
        new AdvancementDisplayData { Title = "Crystal Clear", Description = "Obtain quartz.", Key = "CrystalClear" },
        new AdvancementDisplayData { Title = "Enough Crystals Already", Description = "Obtain 20 quartz.", Key = "EnoughCrystalsAlready" },
        new AdvancementDisplayData { Title = "Thats Enough Crystals, No?", Description = "Obtain 99 quartz.", Key = "ThatsEnoughCrystalsNo" },
        new AdvancementDisplayData { Title = "Stop It With The Crystals", Description = "Obtain 198 quartz.", Key = "StopItWithTheCrystals" },
        new AdvancementDisplayData { Title = "I'm Hungry", Description = "Obtain 20 raidshrooms.", Key = "ImHungry" },
        new AdvancementDisplayData { Title = "FOOOOOOOOOOD!", Description = "Obtain 99 raidshrooms.", Key = "FOOOOOOOOOOD" },
        new AdvancementDisplayData { Title = "Sandy Shores", Description = "Visit a beach.", Key = "SandyShores" },
        new AdvancementDisplayData { Title = "Where Am I?", Description = "Have an X or Y coordinate of above 5000.", Key = "WhereAmI" },
        new AdvancementDisplayData { Title = "Bonk Bonk", Description = "Obtain a brimstone kanabo.", Key = "BonkBonk" },
        new AdvancementDisplayData { Title = "Thanks, But No Thanks", Description = "Make a mob take explosion damage from its own bomb.", Key = "ThanksButNoThanks" },
        new AdvancementDisplayData { Title = "Survivor", Description = "Be at 1 health.", Key = "Survivor" },
        new AdvancementDisplayData { Title = "What Are You Doing?", Description = "Be in the end dimension for over 10 minutes.", Key = "WhatAreYouDoing" },
        new AdvancementDisplayData { Title = "Who Needs Protection?", Description = "Defeat the apex without a shield in your inventory.", Key = "WhoNeedsProtection" },
        new AdvancementDisplayData { Title = "Touch Grass", Description = "Visit a Meadow biome.", Key = "TouchGrass" },
        new AdvancementDisplayData { Title = "I Regret Nothing", Description = "Stand on lava for 2 seconds.", Key = "IRegretNothing" },
        new AdvancementDisplayData { Title = "Definitely Prepared", Description = "Enter The End with no diamond or brimstone weapons.", Key = "DefinitelyPrepared" },
        new AdvancementDisplayData { Title = "This Seems Safe", Description = "Summon the Brimstalker without any iron or diamond weapons.", Key = "ThisSeemsSafe" },
        new AdvancementDisplayData { Title = "Cover Me With Hot Stuff", Description = "Obtain a full set of Brimstone Weapons.", Key = "ObtainAllBrimstone" },
    };

    public void Update(bool resized)
    {
        HomeScreen.background.Update(resized);

        if (Raylib.IsKeyPressed(KeyboardKey.Escape) || backButton.IsClicked())
            Program.CurrentState = GameState.HOME;

        // Handle scrolling
        float mouseWheelMove = Raylib.GetMouseWheelMove();
        if (mouseWheelMove != 0)
        {
            _scrollOffset += mouseWheelMove * ScrollSpeed;
        }

        // Calculate max scroll offset
        int sw = Raylib.GetScreenWidth();
        int sh = Raylib.GetScreenHeight();
        int startY = 130; // Starting Y for the first advancement
        int totalItemDisplayHeight = ItemHeight + ItemSpacing;
        int totalAdvancementContentHeight = AllAdvancements.Length * totalItemDisplayHeight;
        int displayAreaHeight = sh - startY - 100; // Area from startY to just above back button

        if (totalAdvancementContentHeight > displayAreaHeight)
        {
            _scrollOffset = Math.Clamp(_scrollOffset, -(totalAdvancementContentHeight - displayAreaHeight), 0);
        }
        else
        {
            _scrollOffset = 0; // No need to scroll if all fit
        }
    }

    public void Draw()
    {
        int sw = Raylib.GetScreenWidth();
        int sh = Raylib.GetScreenHeight();
        string title = "ADVANCEMENTS";

        int titleW = Raylib.MeasureText(title, 40);
        Raylib.DrawText(title, sw / 2 - titleW / 2, 40, 40, Color.Gold);

        string sub = "Scroll to view all advancements.";
        int subW = Raylib.MeasureText(sub, 20);
        Raylib.DrawText(sub, sw / 2 - subW / 2, 85, 20, Color.Gray);

        // Define the scrollable area
        int displayAreaX = sw / 2 - 250; // Same X as the advancement boxes
        int displayAreaY = 130;
        int displayAreaWidth = 500; // Same width as the advancement boxes
        int displayAreaHeight = sh - displayAreaY - 100; // Area from startY to just above back button

        // Use ScissorMode to clip the drawing to the scrollable area
        Raylib.BeginScissorMode(displayAreaX, displayAreaY, displayAreaWidth, displayAreaHeight);

        for (int i = 0; i < AllAdvancements.Length; i++)
        {
            AdvancementDisplayData adv = AllAdvancements[i];
            bool completed = false;
            string statusChar = "X";

            // Check advancement status from the user data dictionary
            try
            {
                if (adv.Key == "EnterAllBiomes")
                {
                    completed = (Program.CurrentUser as dynamic).VisitedBiomes.Count >= 10;
                    if (!completed) statusChar = $"{(Program.CurrentUser as dynamic).VisitedBiomes.Count}/10";
                }
                else if (adv.Key == "KillAllOverworld")
                {
                    completed = (Program.CurrentUser as dynamic).KilledOverworld.Count >= 4;
                    if (!completed) statusChar = $"{(Program.CurrentUser as dynamic).KilledOverworld.Count}/4";
                }
                else if (adv.Key == "GettingStronger")
                {
                    completed = (Program.CurrentUser as dynamic).TotalMobsKilled >= 25;
                    if (!completed) statusChar = $"{(Program.CurrentUser as dynamic).TotalMobsKilled}/25";
                }
                else if (adv.Key == "EnoughCrystalsAlready")
                {
                    completed = (Program.CurrentUser as dynamic).TotalQuartzObtained >= 20;
                    if (!completed) statusChar = $"{(Program.CurrentUser as dynamic).TotalQuartzObtained}/20";
                }
                else if (adv.Key == "ThatsEnoughCrystalsNo")
                {
                    completed = (Program.CurrentUser as dynamic).TotalQuartzObtained >= 99;
                    if (!completed) statusChar = $"{(Program.CurrentUser as dynamic).TotalQuartzObtained}/99";
                }
                else if (adv.Key == "StopItWithTheCrystals")
                {
                    completed = (Program.CurrentUser as dynamic).TotalQuartzObtained >= 198;
                    if (!completed) statusChar = $"{(Program.CurrentUser as dynamic).TotalQuartzObtained}/198";
                }
                else
                    completed = (Program.CurrentUser as dynamic).Advancements.ContainsKey(adv.Key);

                if (completed) statusChar = "DONE!";
            }
            catch { /* UserData structure mismatch safety */ }

            int advY = (int)(displayAreaY + (i * (ItemHeight + ItemSpacing)) + _scrollOffset);
            int advX = sw / 2 - 250;

            // Draw semi-transparent black box
            Raylib.DrawRectangle(advX, advY, 500, ItemHeight, new Color(0, 0, 0, 180));

            // Draw Title
            Raylib.DrawText(adv.Title, advX + 20, advY + 10, 25, Color.White);

            // Draw Description
            Raylib.DrawText(adv.Description, advX + 20, advY + 40, 18, Color.LightGray);

            // Draw status (DONE! / Progress / X)
            Color statusColor = completed ? Color.Green : (statusChar != "X" ? Color.Yellow : Color.Red);
            int statusCharWidth = Raylib.MeasureText(statusChar, 30);
            Raylib.DrawText(statusChar, advX + 500 - statusCharWidth - 20, advY + 25, 30, statusColor);
        }

        Raylib.EndScissorMode();

        backButton.Position = new Vector2(sw / 2, sh - 60);
        backButton.Draw();
    }
}