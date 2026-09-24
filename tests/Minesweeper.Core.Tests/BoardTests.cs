using Minesweeper.Core;

namespace Minesweeper.Core.Tests;

public class BoardTests
{
    private static int CountMines(Board board)
    {
        int count = 0;
        for (int y = 0; y < board.Rows; y++)
            for (int x = 0; x < board.Columns; x++)
                if (board[x, y].IsMine) count++;
        return count;
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 4)]
    [InlineData(8, 8)]
    public void FirstClickIsNeverAMineAndMineCountIsExact(int x, int y)
    {
        for (int seed = 0; seed < 200; seed++)
        {
            var board = new Board(Difficulty.Beginner, new Random(seed));
            board.Reveal(x, y);

            Assert.False(board[x, y].IsMine);
            Assert.Equal(Difficulty.Beginner.Mines, CountMines(board));
            Assert.NotEqual(GameStatus.Lost, board.Status);
        }
    }

    [Fact]
    public void ExpertBoardPlacesAllMines()
    {
        var board = new Board(Difficulty.Expert, new Random(1));
        board.Reveal(0, 0);
        Assert.Equal(99, CountMines(board));
    }

    [Fact]
    public void AdjacentCountsMatchNeighborMines()
    {
        var board = new Board(Difficulty.Intermediate, new Random(7));
        board.Reveal(8, 8);

        for (int y = 0; y < board.Rows; y++)
        {
            for (int x = 0; x < board.Columns; x++)
            {
                int expected = board.Neighbors(x, y).Count(n => board[n.X, n.Y].IsMine);
                Assert.Equal(expected, board[x, y].AdjacentMines);
            }
        }
    }

    [Fact]
    public void RevealingZeroCellFloodFillsToNumberedBorder()
    {
        var board = new Board(Difficulty.Beginner, new Random(3));
        board.Reveal(4, 4);

        for (int y = 0; y < board.Rows; y++)
        {
            for (int x = 0; x < board.Columns; x++)
            {
                if (board[x, y].State != CellState.Revealed || board[x, y].AdjacentMines != 0) continue;
                foreach (var (nx, ny) in board.Neighbors(x, y))
                    Assert.Equal(CellState.Revealed, board[nx, ny].State);
            }
        }
    }

    [Fact]
    public void RevealingAMineLosesAndShowsAllMines()
    {
        var board = new Board(Difficulty.Beginner, new Random(5));
        board.Reveal(0, 0);
        var (mx, my) = FindCell(board, c => c.IsMine && c.State == CellState.Hidden);

        board.Reveal(mx, my);

        Assert.Equal(GameStatus.Lost, board.Status);
        Assert.True(board[mx, my].Exploded);
        for (int y = 0; y < board.Rows; y++)
            for (int x = 0; x < board.Columns; x++)
                if (board[x, y].IsMine) Assert.Equal(CellState.Revealed, board[x, y].State);
    }

    [Fact]
    public void RevealingAllSafeCellsWins()
    {
        var board = new Board(Difficulty.Beginner, new Random(11));
        board.Reveal(4, 4);

        for (int y = 0; y < board.Rows; y++)
            for (int x = 0; x < board.Columns; x++)
                if (!board[x, y].IsMine) board.Reveal(x, y);

        Assert.Equal(GameStatus.Won, board.Status);
        Assert.Equal(0, board.MinesRemaining);
    }

    [Fact]
    public void FlagsBlockRevealAndUpdateCounter()
    {
        var board = new Board(Difficulty.Beginner, new Random(2));
        board.ToggleFlag(3, 3);
        Assert.Equal(9, board.MinesRemaining);

        board.Reveal(3, 3);
        Assert.Equal(CellState.Flagged, board[3, 3].State);
        Assert.Equal(GameStatus.Ready, board.Status);

        board.ToggleFlag(3, 3);
        Assert.Equal(10, board.MinesRemaining);
        Assert.Equal(CellState.Hidden, board[3, 3].State);
    }

    [Fact]
    public void CannotPlaceMoreFlagsThanMines()
    {
        var board = new Board(Difficulty.Beginner, new Random(2));
        for (int x = 0; x < 9; x++) board.ToggleFlag(x, 0);
        board.ToggleFlag(0, 1);
        Assert.Equal(10, board.FlagCount);
        Assert.Equal(0, board.MinesRemaining);

        board.ToggleFlag(1, 1);
        Assert.Equal(10, board.FlagCount);
        Assert.Equal(0, board.MinesRemaining);
        Assert.Equal(CellState.Hidden, board[1, 1].State);

        // Removing a flag frees a slot again.
        board.ToggleFlag(0, 1);
        Assert.Equal(1, board.MinesRemaining);
        board.ToggleFlag(1, 1);
        Assert.Equal(CellState.Flagged, board[1, 1].State);
    }

    [Fact]
    public void HexNeighborsUseOddRowOffset()
    {
        var board = new Board(Difficulty.Beginner.WithShape(BoardShape.Hex), new Random(1));

        Assert.Equal(
            new[] { (3, 4), (5, 4), (3, 3), (4, 3), (3, 5), (4, 5) }.OrderBy(n => n),
            board.Neighbors(4, 4).OrderBy(n => n));
        Assert.Equal(
            new[] { (3, 3), (5, 3), (4, 2), (5, 2), (4, 4), (5, 4) }.OrderBy(n => n),
            board.Neighbors(4, 3).OrderBy(n => n));
        Assert.Equal(2, board.Neighbors(0, 0).Count());
        Assert.Equal(3, board.Neighbors(8, 3).Count());
    }

    [Fact]
    public void HexNeighborsAreSymmetric()
    {
        var board = new Board(Difficulty.Intermediate.WithShape(BoardShape.Hex), new Random(1));
        for (int y = 0; y < board.Rows; y++)
            for (int x = 0; x < board.Columns; x++)
                foreach (var n in board.Neighbors(x, y))
                    Assert.Contains((x, y), board.Neighbors(n.X, n.Y));
    }

    [Fact]
    public void HexGameKeepsMineCountAndAdjacency()
    {
        var difficulty = Difficulty.Expert.WithShape(BoardShape.Hex);
        var board = new Board(difficulty, new Random(4));
        board.Reveal(10, 8);

        Assert.False(board[10, 8].IsMine);
        Assert.Equal(99, CountMines(board));
        for (int y = 0; y < board.Rows; y++)
            for (int x = 0; x < board.Columns; x++)
                Assert.Equal(board.Neighbors(x, y).Count(n => board[n.X, n.Y].IsMine), board[x, y].AdjacentMines);
    }

    [Fact]
    public void HexBoardCanBeWon()
    {
        var board = new Board(Difficulty.Beginner.WithShape(BoardShape.Hex), new Random(11));
        board.Reveal(4, 4);
        for (int y = 0; y < board.Rows; y++)
            for (int x = 0; x < board.Columns; x++)
                if (!board[x, y].IsMine) board.Reveal(x, y);

        Assert.Equal(GameStatus.Won, board.Status);
    }

    [Fact]
    public void HexAndSquareBestTimesUseSeparateKeys()
    {
        Assert.Equal("Beginner", Difficulty.Beginner.Key);
        Assert.Equal("Hex Beginner", Difficulty.Beginner.WithShape(BoardShape.Hex).Key);
    }

    [Fact]
    public void ChordRevealsNeighborsWhenFlagsMatch()
    {
        var board = new Board(Difficulty.Beginner, new Random(9));
        board.Reveal(4, 4);
        var (x, y) = FindCell(board, c => c.State == CellState.Revealed && c.AdjacentMines > 0 && !c.IsMine,
            b => (x, y) => b.Neighbors(x, y).Any(n => b[n.X, n.Y].State == CellState.Hidden));

        foreach (var (nx, ny) in board.Neighbors(x, y))
            if (board[nx, ny].IsMine) board.ToggleFlag(nx, ny);

        board.Chord(x, y);

        foreach (var (nx, ny) in board.Neighbors(x, y))
            if (!board[nx, ny].IsMine) Assert.Equal(CellState.Revealed, board[nx, ny].State);
    }

    [Fact]
    public void ChordWithWrongFlagLoses()
    {
        var board = new Board(Difficulty.Beginner, new Random(9));
        board.Reveal(4, 4);
        var (x, y) = FindCell(board, c => c.State == CellState.Revealed && c.AdjacentMines == 1 && !c.IsMine,
            b => (x, y) => b.Neighbors(x, y).Any(n => b[n.X, n.Y].State == CellState.Hidden && !b[n.X, n.Y].IsMine));

        var wrong = board.Neighbors(x, y).First(n => board[n.X, n.Y].State == CellState.Hidden && !board[n.X, n.Y].IsMine);
        board.ToggleFlag(wrong.X, wrong.Y);
        board.Chord(x, y);

        Assert.Equal(GameStatus.Lost, board.Status);
    }

    [Fact]
    public void ChordDoesNothingWhenFlagCountDiffers()
    {
        var board = new Board(Difficulty.Beginner, new Random(9));
        board.Reveal(4, 4);
        var (x, y) = FindCell(board, c => c.State == CellState.Revealed && c.AdjacentMines > 0 && !c.IsMine);
        int revealedBefore = CountRevealed(board);

        board.Chord(x, y);

        Assert.Equal(revealedBefore, CountRevealed(board));
    }

    [Fact]
    public void NoActionsAfterGameEnds()
    {
        var board = new Board(Difficulty.Beginner, new Random(5));
        board.Reveal(0, 0);
        var (mx, my) = FindCell(board, c => c.IsMine && c.State == CellState.Hidden);
        board.Reveal(mx, my);

        board.ToggleFlag(0, 0);

        Assert.Equal(0, board.FlagCount);
    }

    [Fact]
    public void CustomDifficultyIsClamped()
    {
        var d = Difficulty.Custom(100, 100, 100000);
        Assert.Equal(50, d.Columns);
        Assert.Equal(30, d.Rows);
        Assert.Equal(50 * 30 - 9, d.Mines);
    }

    [Fact]
    public void BestTimeOnlyUpdatesWhenFaster()
    {
        var save = new SaveData();
        Assert.True(save.TrySetBestTime("Beginner", 20000));
        Assert.False(save.TrySetBestTime("Beginner", 25000));
        Assert.True(save.TrySetBestTime("Beginner", 15000));
        Assert.Equal(15000, save.BestTimesMs["Beginner"]);
    }

    private static int CountRevealed(Board board)
    {
        int n = 0;
        for (int y = 0; y < board.Rows; y++)
            for (int x = 0; x < board.Columns; x++)
                if (board[x, y].State == CellState.Revealed) n++;
        return n;
    }

    private static (int X, int Y) FindCell(Board board, Func<Cell, bool> match,
        Func<Board, Func<int, int, bool>>? extra = null)
    {
        var extraCheck = extra?.Invoke(board);
        for (int y = 0; y < board.Rows; y++)
            for (int x = 0; x < board.Columns; x++)
                if (match(board[x, y]) && (extraCheck == null || extraCheck(x, y))) return (x, y);
        throw new InvalidOperationException("No matching cell found for this seed.");
    }
}
