using Raylib_cs;
using System.Numerics;
using BulletboxClient;

public class CreateWorldScreen
{
    private UIInputBox _worldNameInput;
    private UIButton _cheatsButton;
    private UIButton _createButton;
    private UIButton _backButton;

    private bool _cheatsEnabled = false;

    public CreateWorldScreen()
    {
        _worldNameInput = new UIInputBox(new Vector2(0, 0), 200, 30, "World Name", 20);
        _cheatsButton = new UIButton("Cheats: OFF", new Vector2(0, 0), 25);
        _createButton = new UIButton("CREATE WORLD", new Vector2(0, 0), 30);
        _backButton = new UIButton("BACK", Vector2.Zero, 30, true);
    }

    public void Update(bool resized)
    {
        HomeScreen.background.Update(resized);

        float centerX = Raylib.GetScreenWidth() / 2;
        float centerY = Raylib.GetScreenHeight() / 2;

        _worldNameInput.Position = new Vector2(centerX, centerY - 80);
        _cheatsButton.Position = new Vector2(centerX, centerY - 20);
        _createButton.Position = new Vector2(centerX, centerY + 60);
        _backButton.Position = new Vector2(centerX, centerY + 120);

        _worldNameInput.Update();

        if (_cheatsButton.IsClicked())
        {
            _cheatsEnabled = !_cheatsEnabled;
            _cheatsButton.Text = $"Cheats: {(_cheatsEnabled ? "ON" : "OFF")}";
        }

        if (_createButton.IsClicked())
        {
            // Assuming ServerProgram.CreateWorld takes a WorldData object
            // and then transitions to SINGLEPLAYER_CONNECTING
            Program.CurrentWorldData = new WorldData
            {
                WorldName = string.IsNullOrEmpty(_worldNameInput.Text) ? "New World" : _worldNameInput.Text,
                CheatsEnabled = _cheatsEnabled,
                Version = Program.VERSION // Set current version on creation
            };
            // This will trigger the SINGLEPLAYER_CONNECTING state which then attempts to connect
            // and the server will create the world with this data.
            Program.CurrentState = GameState.SINGLEPLAYER_CONNECTING;
        }

        if (_backButton.IsClicked() || Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            Program.CurrentState = GameState.WORLD_SELECTION;
        }
    }

    public void Draw()
    {
        HomeScreen.background.Draw();

        float centerX = Raylib.GetScreenWidth() / 2;
        float centerY = Raylib.GetScreenHeight() / 2;

        Raylib.DrawText("CREATE NEW WORLD", (int)(centerX - Raylib.MeasureText("CREATE NEW WORLD", 40) / 2), (int)(centerY - 180), 40, Color.Gold);

        _worldNameInput.Draw();
        _cheatsButton.Draw();
        _createButton.Draw();
        _backButton.Draw();
    }
}