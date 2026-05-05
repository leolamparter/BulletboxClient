using Raylib_cs;
using System.Numerics;
using System.Collections.Generic;

public class Playing
{
    public Player LocalPlayer;
    public int CurrentHealth = 100;
    public int MaxHealth = 100;
    public Dictionary<string, Player> Others = new Dictionary<string, Player>();
    public CameraManager Cam;
    public Inventory PlayerInventory = new Inventory();
    public HotbarUI Hotbar;
    public InventoryUI InvMenu;
    private HealthBar healthBar = new HealthBar();

    public Playing(string myName)
    {
        LocalPlayer = new Player(myName, new Vector2(400, 300));
        LocalPlayer.Color = Color.Blue;
        Cam = new CameraManager(LocalPlayer.Position);
        Hotbar = new HotbarUI(PlayerInventory);
        InvMenu = new InventoryUI(PlayerInventory);
    }

    public void Update()
    {
        float dt = Raylib.GetFrameTime();
        float speed = 350f;
        Vector2 lastPos = LocalPlayer.Position;

        if (Raylib.IsKeyDown(KeyboardKey.W)) LocalPlayer.Position.Y -= speed * dt;
        if (Raylib.IsKeyDown(KeyboardKey.S)) LocalPlayer.Position.Y += speed * dt;
        if (Raylib.IsKeyDown(KeyboardKey.A)) LocalPlayer.Position.X -= speed * dt;
        if (Raylib.IsKeyDown(KeyboardKey.D)) LocalPlayer.Position.X += speed * dt;

        Cam.Update(LocalPlayer.Position);

        if (LocalPlayer.Position != lastPos)
        {
            Program.Net.SendPosition(LocalPlayer.Position.X, LocalPlayer.Position.Y);
        }

        Hotbar.Update();
        InvMenu.Update();
    }

    public void Draw()
    {
        Raylib.DrawRectangle(0, 0, Raylib.GetScreenWidth(), Raylib.GetScreenHeight(), new Color(0, 0, 0, 100));

        // World Space
        Cam.Begin();
            foreach (var other in Others.Values) other.Draw();
            LocalPlayer.Draw();
        Cam.End();

        // Screen Space
        Hotbar.Draw();
        healthBar.Draw(CurrentHealth, MaxHealth);
        InvMenu.Draw();
    }
}