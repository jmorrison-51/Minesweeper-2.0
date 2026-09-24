namespace Minesweeper.Core;

public enum GameStatus
{
    Ready,
    Playing,
    Won,
    Lost,
}

public sealed class Board
{
    private readonly Cell[,] _cells;
    private readonly Random _random;
    private int _revealedSafe;

    public Board(Difficulty difficulty, Random? random = null)
    {
        Difficulty = difficulty;
        Columns = difficulty.Columns;
        Rows = difficulty.Rows;
        MineCount = difficulty.Mines;
        _cells = new Cell[Columns, Rows];
        _random = random ?? new Random();
    }

    public Difficulty Difficulty { get; }
    public int Columns { get; }
    public int Rows { get; }
    public int MineCount { get; }
    public GameStatus Status { get; private set; } = GameStatus.Ready;
    public int FlagCount { get; private set; }
    public int MinesRemaining => MineCount - FlagCount;

    public Cell this[int x, int y] => _cells[x, y];

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Columns && y < Rows;

    public IEnumerable<(int X, int Y)> Neighbors(int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (InBounds(nx, ny)) yield return (nx, ny);
            }
        }
    }

    public void Reveal(int x, int y)
    {
        if (IsOver || !InBounds(x, y)) return;
        if (_cells[x, y].State != CellState.Hidden) return;

        if (Status == GameStatus.Ready)
        {
            PlaceMines(x, y);
            Status = GameStatus.Playing;
        }

        RevealFrom(x, y);
    }

    public void ToggleFlag(int x, int y)
    {
        if (IsOver || !InBounds(x, y)) return;

        ref Cell cell = ref _cells[x, y];
        switch (cell.State)
        {
            case CellState.Hidden:
                cell.State = CellState.Flagged;
                FlagCount++;
                break;
            case CellState.Flagged:
                cell.State = CellState.Hidden;
                FlagCount--;
                break;
        }
    }

    // Reveals the neighbors of a revealed number whose flag count matches the number.
    public void Chord(int x, int y)
    {
        if (Status != GameStatus.Playing || !InBounds(x, y)) return;

        Cell cell = _cells[x, y];
        if (cell.State != CellState.Revealed || cell.AdjacentMines == 0) return;

        int flags = 0;
        foreach (var (nx, ny) in Neighbors(x, y))
            if (_cells[nx, ny].State == CellState.Flagged) flags++;
        if (flags != cell.AdjacentMines) return;

        foreach (var (nx, ny) in Neighbors(x, y))
        {
            if (_cells[nx, ny].State == CellState.Hidden) RevealFrom(nx, ny);
            if (IsOver) break;
        }
    }

    private bool IsOver => Status is GameStatus.Won or GameStatus.Lost;

    private void RevealFrom(int startX, int startY)
    {
        var stack = new Stack<(int X, int Y)>();
        stack.Push((startX, startY));

        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            ref Cell cell = ref _cells[x, y];
            if (cell.State != CellState.Hidden) continue;

            cell.State = CellState.Revealed;

            if (cell.IsMine)
            {
                cell.Exploded = true;
                Lose();
                return;
            }

            _revealedSafe++;

            if (cell.AdjacentMines == 0)
            {
                foreach (var n in Neighbors(x, y))
                    if (_cells[n.X, n.Y].State == CellState.Hidden) stack.Push(n);
            }
        }

        if (_revealedSafe == Columns * Rows - MineCount) Win();
    }

    private void PlaceMines(int safeX, int safeY)
    {
        int total = Columns * Rows;
        var candidates = new List<int>(total);
        for (int i = 0; i < total; i++)
        {
            if (i % Columns == safeX && i / Columns == safeY) continue;
            candidates.Add(i);
        }

        for (int placed = 0; placed < MineCount; placed++)
        {
            int pick = _random.Next(placed, candidates.Count);
            (candidates[placed], candidates[pick]) = (candidates[pick], candidates[placed]);
            int index = candidates[placed];
            _cells[index % Columns, index / Columns].IsMine = true;
        }

        for (int y = 0; y < Rows; y++)
        {
            for (int x = 0; x < Columns; x++)
            {
                int count = 0;
                foreach (var (nx, ny) in Neighbors(x, y))
                    if (_cells[nx, ny].IsMine) count++;
                _cells[x, y].AdjacentMines = count;
            }
        }
    }

    private void Win()
    {
        Status = GameStatus.Won;
        for (int y = 0; y < Rows; y++)
        {
            for (int x = 0; x < Columns; x++)
            {
                ref Cell cell = ref _cells[x, y];
                if (cell.IsMine) cell.State = CellState.Flagged;
            }
        }
        FlagCount = MineCount;
    }

    private void Lose()
    {
        Status = GameStatus.Lost;
        for (int y = 0; y < Rows; y++)
        {
            for (int x = 0; x < Columns; x++)
            {
                ref Cell cell = ref _cells[x, y];
                if (cell.IsMine && cell.State == CellState.Hidden) cell.State = CellState.Revealed;
            }
        }
    }
}
