using Raylib_cs;
using System.Numerics;

public class LoginScreen {
    private string username = "";
    private string password = "";
    private int activeField = 0; 
    private UIButton loginButton;
    
    public LoginScreen() {
        loginButton = new UIButton("CONFIRM & PLAY", Vector2.Zero, 30, true);
    }

    public void Update() {
        // Keep background moving
        HomeScreen.background.Update();

        // 1. CLICK TO SELECT FIELDS
        Vector2 mouse = Raylib.GetMousePosition();
        float centerX = Raylib.GetScreenWidth() / 2;
        float centerY = Raylib.GetScreenHeight() / 2;

        // Rectangle hitboxes for the two input lines
        if (Raylib.IsMouseButtonPressed(MouseButton.Left)) {
            if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(centerX - 150, centerY - 65, 300, 40))) activeField = 0;
            if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(centerX - 150, centerY + 15, 300, 40))) activeField = 1;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Tab)) activeField = (activeField + 1) % 2;

        // 2. INPUT HANDLING
        int key = Raylib.GetCharPressed();
        while (key > 0) {
            if ((key >= 32) && (key <= 125)) {
                if (activeField == 0 && username.Length < 16) username += (char)key;
                else if (activeField == 1 && password.Length < 16) password += (char)key;
            }
            key = Raylib.GetCharPressed();
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Backspace)) {
            if (activeField == 0 && username.Length > 0) username = username[..^1];
            else if (activeField == 1 && password.Length > 0) password = password[..^1];
        }

        if (loginButton.IsClicked() && username.Length > 2 && password.Length > 2) {
            SaveManager.Save(new UserData { Username = username, Password = password, HasLoggedIn = true });
            Program.CurrentState = GameState.PLAYING;
        }
    }

    public void Draw() {
        // Draw the global background
        HomeScreen.background.Draw();

        float centerX = Raylib.GetScreenWidth() / 2;
        float centerY = Raylib.GetScreenHeight() / 2;

        Raylib.DrawText("ACCOUNT LOGIN", (int)centerX - 100, (int)centerY - 150, 30, Color.Yellow);

        // 3. PLACEHOLDER LOGIC
        DrawInputField(username.Length == 0 ? "Username..." : "", username, centerY - 60, activeField == 0);
        DrawInputField(password.Length == 0 ? "Password..." : "", new string('*', password.Length), centerY + 20, activeField == 1);

        string tooltip = "New here? Enter a unique password to register your account.";
        int toolWidth = Raylib.MeasureText(tooltip, 15);
        Raylib.DrawText(tooltip, (int)centerX - toolWidth / 2, (int)centerY + 80, 15, Color.LightGray);

        loginButton.Position = new Vector2(centerX, centerY + 140);
        loginButton.Draw();
    }

    private void DrawInputField(string placeholder, string value, float y, bool active) {
        float centerX = Raylib.GetScreenWidth() / 2;
        
        // Only draw placeholder if value is empty
        if (value.Length == 0) 
            Raylib.DrawText(placeholder, (int)centerX - 150, (int)y + 5, 20, Color.DarkGray);
        
        Color lineCol = active ? Color.Yellow : Color.DarkGray;
        Raylib.DrawRectangle((int)centerX - 150, (int)y + 25, 300, 2, lineCol);
        Raylib.DrawText(value, (int)centerX - 150, (int)y + 5, 20, Color.White);
        
        if (active && (DateTime.Now.Millisecond / 500) % 2 == 0) {
            int textWidth = Raylib.MeasureText(value, 20);
            Raylib.DrawRectangle((int)centerX - 150 + textWidth + 2, (int)y + 5, 2, 20, Color.Yellow);
        }
    }
}