using Raylib_cs;
using System;
using System.Collections.Generic;
using System.Linq;

public class GameOfLife
{
    private int width = 80;
    private int height = 45;
    private bool[,] cells = new bool[0, 0];
    private float updateTimer = 0;
    private float updateRate = 0.1f;

    // History and Stagnation tracking
    private Queue<int> history = new Queue<int>();
    private int stagnationTicks = 0;
    private const int RESET_THRESHOLD = 20; // Reset after ~2 seconds of no meaningful change

    public GameOfLife()
    {
        InitializeBoard();
    }

    private void InitializeBoard()
    {
        cells = new bool[width, height];
        Random rnd = new Random();
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                cells[x, y] = rnd.Next(0, 100) < 15; 
        
        history.Clear();
        stagnationTicks = 0;
    }

    public void Update()
    {
        updateTimer += Raylib.GetFrameTime();
        if (updateTimer < updateRate) return;
        updateTimer = 0;

        bool[,] nextGen = new bool[width, height];
        int currentLiveCount = 0;

        for (int x = 0; x < width; x++) {
            for (int y = 0; y < height; y++) {
                int neighbors = CountNeighbors(x, y);
                if (cells[x, y]) nextGen[x, y] = (neighbors == 2 || neighbors == 3);
                else nextGen[x, y] = (neighbors == 3);

                if (nextGen[x, y]) currentLiveCount++;
            }
        }

        // --- THE RESET LOGIC ---
        
        // If the board is totally dead, reset immediately
        if (currentLiveCount == 0)
        {
            InitializeBoard();
        }
        // If this count matches any of the last 3 frames, increment stagnation
        else if (history.Contains(currentLiveCount))
        {
            stagnationTicks++;
            if (stagnationTicks > RESET_THRESHOLD)
            {
                InitializeBoard(); // THE RESET TRIGGER
            }
        }
        else
        {
            stagnationTicks = 0; // Everything is still evolving
        }

        // Update history queue
        history.Enqueue(currentLiveCount);
        if (history.Count > 3) history.Dequeue();

        cells = nextGen;
    }

    private int CountNeighbors(int x, int y) {
        int count = 0;
        for (int i = -1; i <= 1; i++) {
            for (int j = -1; j <= 1; j++) {
                if (i == 0 && j == 0) continue;
                int nx = (x + i + width) % width;
                int ny = (y + j + height) % height;
                if (cells[nx, ny]) count++;
            }
        }
        return count;
    }

    public void Draw()
    {
        int cellW = Raylib.GetScreenWidth() / width;
        int cellH = Raylib.GetScreenHeight() / height;

        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                if (cells[x, y])
                    Raylib.DrawRectangle(x * cellW, y * cellH, cellW - 1, cellH - 1, new Color(20, 40, 100, 255));
    }
}