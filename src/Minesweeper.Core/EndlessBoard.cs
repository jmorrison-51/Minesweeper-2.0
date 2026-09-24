namespace Minesweeper.Core;

public enum LossReason
{
    None,
    Mine,
    Bottom,
}

/// <summary>
/// Endless Mode: a 16-wide hex field where rows slide toward the bottom and new rows arrive at the top.
/// Revealing every safe cell in a row removes that row. Reaching the bottom, or revealing a mine, ends the run.
/// There are no flags. UI-free: a front end calls <see cref="Advance"/> with elapsed time and reads state back.
/// </summary>
/// <remarks>
/// Rows are identified by a serial number that counts up as rows are created; the newest row is at the top.
/// A row's serial parity gives its horizontal hex offset (odd rows sit half a cell right), so rows keep their
/// place in the hex lattice however many rows around them are cleared. A cleared row leaves a gap; its mines
/// disappear with it, so the numbers next to it are recounted and always match the mines you can see.
/// The next row's mines are generated one row ahead (a hidden buffer) so a row's numbers never change when
/// new rows arrive above it.
/// </remarks>
public sealed class EndlessBoard
{
    public const int Columns = 16;

    /// <summary>Rows of vertical space. A row whose bottom reaches the last one has touched the bottom.</summary>
    public const int Capacity = 16;

    public const int InitialRows = 2;

    private sealed class Row
    {
        public Cell[] Cells = new Cell[Columns];
        public bool Visible;
    }

    private readonly Dictionary<int, Row> _rows = new();
    private readonly Random _random;
    private int _newest;
    private double _offset;

    public EndlessBoard(Random? random = null)
    {
        _random = random ?? new Random();
        for (int serial = 0; serial <= InitialRows; serial++)
            _rows[serial] = NewRow(serial < InitialRows);
        _newest = InitialRows - 1;
        RecountAll();
    }

    public GameStatus Status { get; private set; } = GameStatus.Ready;
    public LossReason LossReason { get; private set; }
    public int RowsCleared { get; private set; }

    /// <summary>Seconds of play so far. The clock starts on the first reveal.</summary>
    public double Elapsed { get; private set; }

    /// <summary>How far the stack has slid toward the next row, from 0 to just under 1.</summary>
    public double Offset => _offset;

    public int NewestRow => _newest;

    /// <summary>Seconds between new rows once the run has lasted <paramref name="elapsed"/> seconds.</summary>
    public static double RowInterval(double elapsed) => Math.Max(1.8, 10.0 / (1.0 + elapsed / 90.0));

    public double RowsPerSecond => 1.0 / RowInterval(Elapsed);

    /// <summary>Serials of the rows on the field, oldest (lowest) first.</summary>
    public IReadOnlyList<int> VisibleRows =>
        _rows.Where(r => r.Value.Visible).Select(r => r.Key).OrderBy(s => s).ToList();

    public bool IsVisibleRow(int serial) => _rows.TryGetValue(serial, out var row) && row.Visible;

    public Cell this[int serial, int x] => _rows[serial].Cells[x];

    /// <summary>Distance from the top of the field in rows: 0 is the top slot, Capacity - 1 touches the bottom.</summary>
    public double SlotPosition(int serial) => _newest - serial + _offset;

    /// <summary>Mines touching a cell right now, including the hidden next row. What its number should show.</summary>
    public int AdjacentMineCount(int serial, int x)
    {
        int count = 0;
        foreach (var (s, c) in Adjacent(serial, x, includeBuffer: true))
            if (_rows[s].Cells[c].IsMine) count++;
        return count;
    }

    /// <summary>Neighbors on the field (odd-r hex adjacency across rows that still exist).</summary>
    public IEnumerable<(int Row, int Col)> Neighbors(int serial, int x) => Adjacent(serial, x, includeBuffer: false);

    public void Reveal(int serial, int x)
    {
        if (Status == GameStatus.Lost || !IsVisibleRow(serial) || x < 0 || x >= Columns) return;
        if (_rows[serial].Cells[x].State != CellState.Hidden) return;

        if (Status == GameStatus.Ready)
        {
            EnsureSafe(serial, x);
            Status = GameStatus.Playing;
        }

        RevealFrom(serial, x);
        if (Status != GameStatus.Lost) Settle();
    }

    /// <summary>Moves time forward. Does nothing until the first reveal and after the run ends.</summary>
    public void Advance(double seconds)
    {
        if (Status != GameStatus.Playing || seconds <= 0) return;

        Elapsed += seconds;
        _offset += seconds / RowInterval(Elapsed);
        while (_offset >= 1)
        {
            _offset -= 1;
            AddRow();
        }

        foreach (int serial in VisibleRows)
        {
            if (SlotPosition(serial) >= Capacity - 1)
            {
                Lose(LossReason.Bottom);
                return;
            }
        }
    }

    private Row NewRow(bool visible)
    {
        var row = new Row { Visible = visible };
        int mines = 2 + (_random.Next(2)); // 2 or 3 of 16, about the density of Intermediate
        var columns = Enumerable.Range(0, Columns).OrderBy(_ => _random.Next()).Take(mines);
        foreach (int c in columns) row.Cells[c].IsMine = true;
        return row;
    }

    // The buffer row becomes visible and a fresh buffer is generated above it.
    private void AddRow()
    {
        int serial = _newest + 1;
        _rows[serial].Visible = true;
        _newest = serial;
        _rows[serial + 1] = NewRow(visible: false);
        Recount(serial);
        Settle();
    }

    private IEnumerable<(int Row, int Col)> Adjacent(int serial, int x, bool includeBuffer)
    {
        if (x > 0) yield return (serial, x - 1);
        if (x < Columns - 1) yield return (serial, x + 1);

        int shift = (serial & 1) == 0 ? -1 : 0;
        for (int s = serial - 1; s <= serial + 1; s += 2)
        {
            if (!_rows.TryGetValue(s, out var row) || (!row.Visible && !includeBuffer)) continue;
            for (int c = x + shift; c <= x + shift + 1; c++)
                if (c >= 0 && c < Columns) yield return (s, c);
        }
    }

    private void Recount(int serial)
    {
        if (!_rows.TryGetValue(serial, out var row)) return;
        for (int x = 0; x < Columns; x++) row.Cells[x].AdjacentMines = AdjacentMineCount(serial, x);
    }

    private void RecountAll()
    {
        foreach (int serial in _rows.Keys.ToList()) Recount(serial);
    }

    // The first reveal is never a mine: swap it with a random safe cell on the field.
    private void EnsureSafe(int serial, int x)
    {
        if (!_rows[serial].Cells[x].IsMine) return;

        var options = new List<(int Row, int Col)>();
        foreach (var (s, row) in _rows)
            for (int c = 0; c < Columns; c++)
                if (!row.Cells[c].IsMine) options.Add((s, c));

        var (ds, dc) = options[_random.Next(options.Count)];
        _rows[serial].Cells[x].IsMine = false;
        _rows[ds].Cells[dc].IsMine = true;
        RecountAll();
    }

    private void RevealFrom(int startRow, int startCol)
    {
        var stack = new Stack<(int Row, int Col)>();
        stack.Push((startRow, startCol));

        while (stack.Count > 0)
        {
            var (s, c) = stack.Pop();
            if (!IsVisibleRow(s)) continue;
            ref Cell cell = ref _rows[s].Cells[c];
            if (cell.State != CellState.Hidden) continue;

            cell.State = CellState.Revealed;
            if (cell.IsMine)
            {
                cell.Exploded = true;
                Lose(LossReason.Mine);
                return;
            }

            if (cell.AdjacentMines == 0)
                foreach (var n in Neighbors(s, c))
                    if (_rows[n.Row].Cells[n.Col].State == CellState.Hidden) stack.Push(n);
        }
    }

    // Applies everything that follows from the current state until it stops changing: empty cells
    // open their neighbors (a recount or a new row can create these), and finished rows are removed.
    private void Settle()
    {
        bool changed;
        do
        {
            changed = false;

            foreach (int serial in VisibleRows)
            {
                for (int x = 0; x < Columns; x++)
                {
                    Cell cell = _rows[serial].Cells[x];
                    if (cell.State != CellState.Revealed || cell.AdjacentMines != 0) continue;
                    foreach (var n in Neighbors(serial, x))
                    {
                        if (_rows[n.Row].Cells[n.Col].State != CellState.Hidden) continue;
                        RevealFrom(n.Row, n.Col);
                        changed = true;
                    }
                }
            }

            foreach (int serial in VisibleRows)
            {
                if (!_rows[serial].Cells.All(c => c.IsMine || c.State == CellState.Revealed)) continue;
                _rows.Remove(serial);
                RowsCleared++;
                Recount(serial - 1);
                Recount(serial + 1);
                changed = true;
            }
        }
        while (changed);
    }

    private void Lose(LossReason reason)
    {
        Status = GameStatus.Lost;
        LossReason = reason;
        foreach (var row in _rows.Values.Where(r => r.Visible))
            for (int x = 0; x < Columns; x++)
                if (row.Cells[x].IsMine && row.Cells[x].State == CellState.Hidden) row.Cells[x].State = CellState.Revealed;
    }
}
