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

    public Board(Difficulty difficulty, Random? random = null, BoardRules? rules = null)
    {
        Difficulty = difficulty;
        Rules = rules ?? BoardRules.Classic;
        Columns = difficulty.Columns;
        Rows = difficulty.Rows;
        MineCount = difficulty.Mines;
        FlagLimit = Math.Min(Rules.FlagLimit ?? MineCount, MineCount);
        _cells = new Cell[Columns, Rows];
        _random = random ?? new Random();
    }

    public Difficulty Difficulty { get; }
    public BoardRules Rules { get; }
    public int FlagLimit { get; }
    public int Columns { get; }
    public int Rows { get; }
    public int MineCount { get; }
    public GameStatus Status { get; private set; } = GameStatus.Ready;
    public int FlagCount { get; private set; }

    /// <summary>
    /// Flags the player has put down this game, including ones they later removed. The automatic
    /// flags added to every mine on a win are not counted. Used for "cleared without flags".
    /// </summary>
    public int FlagsPlaced { get; private set; }
    public int MinesRemaining => MineCount - FlagCount;
    public int FlagsRemaining => Math.Max(0, FlagLimit - FlagCount);

    /// <summary>True for a revealed mystery cell that has not yet earned its number.</summary>
    public bool IsNumberHidden(int x, int y)
    {
        if (IsOver || !InBounds(x, y)) return false;
        Cell cell = _cells[x, y];
        if (!cell.IsMystery || cell.State != CellState.Revealed) return false;

        int revealed = 0;
        foreach (var (nx, ny) in Neighbors(x, y))
            if (_cells[nx, ny].State == CellState.Revealed) revealed++;
        return revealed < cell.MysteryNeeded;
    }

    public Cell this[int x, int y] => _cells[x, y];

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Columns && y < Rows;

    // Square boards are 8-way. Hex boards use "odd-r" offset rows (odd rows shifted half a cell
    // right, pointy-top hexes) and have 6 neighbors.
    public IEnumerable<(int X, int Y)> Neighbors(int x, int y)
    {
        if (Difficulty.Shape == BoardShape.Hex)
        {
            int shift = (y & 1) == 0 ? -1 : 0;
            (int X, int Y)[] hex =
            [
                (x - 1, y), (x + 1, y),
                (x + shift, y - 1), (x + shift + 1, y - 1),
                (x + shift, y + 1), (x + shift + 1, y + 1),
            ];
            foreach (var n in hex)
                if (InBounds(n.X, n.Y)) yield return n;
            yield break;
        }

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
                if (FlagCount >= FlagLimit) return;
                cell.State = CellState.Flagged;
                FlagCount++;
                FlagsPlaced++;
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
        if (IsNumberHidden(x, y)) return;

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
        var safe = new HashSet<int> { safeY * Columns + safeX };
        if (Rules.SafeStart)
            foreach (var (nx, ny) in Neighbors(safeX, safeY)) safe.Add(ny * Columns + nx);

        var candidates = new List<int>(total);
        for (int i = 0; i < total; i++)
            if (!safe.Contains(i)) candidates.Add(i);

        int mines = Math.Min(MineCount, candidates.Count);
        if (Rules.Clustering > 0) PlaceClusteredMines(candidates, mines);
        else PlaceScatteredMines(candidates, mines);

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

        if (Rules.MysteryFraction > 0) PlaceMysteryCells(safeY * Columns + safeX);
    }

    private void PlaceScatteredMines(List<int> candidates, int mines)
    {
        for (int placed = 0; placed < mines; placed++)
        {
            int pick = _random.Next(placed, candidates.Count);
            (candidates[placed], candidates[pick]) = (candidates[pick], candidates[placed]);
            int index = candidates[placed];
            _cells[index % Columns, index / Columns].IsMine = true;
        }
    }

    // Each mine has a Clustering chance of going next to an existing mine, which makes clumps.
    private void PlaceClusteredMines(List<int> candidates, int mines)
    {
        var free = new List<int>(candidates);
        var slot = new int[Columns * Rows];
        Array.Fill(slot, -1);
        for (int i = 0; i < free.Count; i++) slot[free[i]] = i;

        var placed = new List<int>(mines);

        void Take(int index)
        {
            int at = slot[index];
            int last = free[^1];
            free[at] = last;
            slot[last] = at;
            free.RemoveAt(free.Count - 1);
            slot[index] = -1;
            _cells[index % Columns, index / Columns].IsMine = true;
            placed.Add(index);
        }

        while (placed.Count < mines && free.Count > 0)
        {
            if (placed.Count > 0 && _random.NextDouble() < Rules.Clustering)
            {
                int anchor = placed[_random.Next(placed.Count)];
                var open = Neighbors(anchor % Columns, anchor / Columns)
                    .Select(n => n.Y * Columns + n.X)
                    .Where(i => slot[i] >= 0)
                    .ToList();
                if (open.Count > 0)
                {
                    Take(open[_random.Next(open.Count)]);
                    continue;
                }
            }
            Take(free[_random.Next(free.Count)]);
        }
    }

    // Picks numbered safe cells to hide behind "?". Each one needs up to MysteryThreshold revealed
    // neighbors, capped by how many safe neighbors it has so the number can always be earned.
    private void PlaceMysteryCells(int firstClick)
    {
        var eligible = new List<int>();
        for (int y = 0; y < Rows; y++)
        {
            for (int x = 0; x < Columns; x++)
            {
                int index = y * Columns + x;
                if (index == firstClick) continue;
                Cell cell = _cells[x, y];
                if (cell.IsMine || cell.AdjacentMines == 0) continue;
                if (Neighbors(x, y).Any(n => !_cells[n.X, n.Y].IsMine)) eligible.Add(index);
            }
        }

        int count = (int)Math.Round(eligible.Count * Rules.MysteryFraction);
        for (int i = 0; i < count; i++)
        {
            int pick = _random.Next(i, eligible.Count);
            (eligible[i], eligible[pick]) = (eligible[pick], eligible[i]);
            int x = eligible[i] % Columns, y = eligible[i] / Columns;
            int safeNeighbors = Neighbors(x, y).Count(n => !_cells[n.X, n.Y].IsMine);
            _cells[x, y].IsMystery = true;
            _cells[x, y].MysteryNeeded = Math.Min(Rules.MysteryThreshold, safeNeighbors);
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
