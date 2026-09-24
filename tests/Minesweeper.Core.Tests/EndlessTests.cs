using Minesweeper.Core;

namespace Minesweeper.Core.Tests;

public class EndlessTests
{
    private static void AssertNumbersMatchMines(EndlessBoard board)
    {
        foreach (int s in board.VisibleRows)
            for (int x = 0; x < EndlessBoard.Columns; x++)
                Assert.Equal(board.AdjacentMineCount(s, x), board[s, x].AdjacentMines);
    }

    private static (int Row, int Col) FirstSafeHidden(EndlessBoard board, int row)
    {
        for (int x = 0; x < EndlessBoard.Columns; x++)
            if (!board[row, x].IsMine && board[row, x].State == CellState.Hidden) return (row, x);
        throw new InvalidOperationException("No hidden safe cell in this row.");
    }

    [Fact]
    public void StartsWithTwoRowsAtTheTopAndDoesNotMoveBeforeTheFirstClick()
    {
        var board = new EndlessBoard(new Random(1));

        Assert.Equal(EndlessBoard.InitialRows, board.VisibleRows.Count);
        Assert.Equal(GameStatus.Ready, board.Status);
        Assert.Equal(0, board.SlotPosition(board.NewestRow));

        board.Advance(30);
        Assert.Equal(0, board.Elapsed);
        Assert.Equal(0, board.SlotPosition(board.NewestRow));
    }

    [Fact]
    public void FirstRevealIsNeverAMineAndNumbersStayCorrect()
    {
        for (int seed = 0; seed < 200; seed++)
        {
            var board = new EndlessBoard(new Random(seed));
            board.Reveal(1, seed % EndlessBoard.Columns);

            Assert.NotEqual(GameStatus.Lost, board.Status);
            Assert.False(board[1, seed % EndlessBoard.Columns].IsMine);
            AssertNumbersMatchMines(board);
        }
    }

    [Fact]
    public void RowsHaveTwoOrThreeMines()
    {
        var board = new EndlessBoard(new Random(2));
        board.Reveal(1, 0);
        foreach (int s in board.VisibleRows)
        {
            int mines = Enumerable.Range(0, EndlessBoard.Columns).Count(x => board[s, x].IsMine);
            Assert.InRange(mines, 2, 3);
        }
    }

    [Fact]
    public void NewRowsArriveAtTheTopAsTimePasses()
    {
        var board = new EndlessBoard(new Random(3));
        board.Reveal(1, 0);
        int newestBefore = board.NewestRow;

        board.Advance(EndlessBoard.RowInterval(0) * 1.01);

        Assert.Equal(newestBefore + 1, board.NewestRow);
        Assert.True(board.SlotPosition(board.NewestRow) < 0.5);
        Assert.InRange(board.SlotPosition(newestBefore), 1.0, 1.5);
        AssertNumbersMatchMines(board);
    }

    [Fact]
    public void RowsMoveFasterTheLongerTheRunLasts()
    {
        Assert.Equal(10, EndlessBoard.RowInterval(0), 3);
        Assert.True(EndlessBoard.RowInterval(60) < EndlessBoard.RowInterval(0));
        Assert.True(EndlessBoard.RowInterval(300) < EndlessBoard.RowInterval(60));
        Assert.True(EndlessBoard.RowInterval(100000) >= 1.8);
    }

    [Fact]
    public void DoingNothingEventuallyLosesWhenARowTouchesTheBottom()
    {
        var board = new EndlessBoard(new Random(4));
        board.Reveal(1, 0);
        if (board.Status == GameStatus.Lost) return;

        for (int i = 0; i < 100000 && board.Status == GameStatus.Playing; i++) board.Advance(0.1);

        Assert.Equal(GameStatus.Lost, board.Status);
        Assert.Equal(LossReason.Bottom, board.LossReason);
        Assert.Contains(board.VisibleRows, s => board.SlotPosition(s) >= EndlessBoard.Capacity - 1);
    }

    [Fact]
    public void RevealingAMineLosesAndShowsTheMines()
    {
        var board = new EndlessBoard(new Random(5));
        board.Reveal(1, 0);
        var row = board.VisibleRows.First(s => Enumerable.Range(0, 16).Any(x => board[s, x].IsMine && board[s, x].State == CellState.Hidden));
        int mine = Enumerable.Range(0, 16).First(x => board[row, x].IsMine);

        board.Reveal(row, mine);

        Assert.Equal(GameStatus.Lost, board.Status);
        Assert.Equal(LossReason.Mine, board.LossReason);
        Assert.True(board[row, mine].Exploded);
        Assert.All(board.VisibleRows, s =>
            Assert.All(Enumerable.Range(0, 16).Where(x => board[s, x].IsMine), x => Assert.Equal(CellState.Revealed, board[s, x].State)));
    }

    [Fact]
    public void ClearingEverySafeCellInARowRemovesIt()
    {
        var board = new EndlessBoard(new Random(6));
        board.Reveal(1, 0);
        int row = 0;

        for (int x = 0; x < 16 && board.IsVisibleRow(row); x++)
            if (!board[row, x].IsMine) board.Reveal(row, x);

        Assert.False(board.IsVisibleRow(row));
        Assert.Equal(1, board.RowsCleared);
        Assert.Equal(GameStatus.Playing, board.Status);
        AssertNumbersMatchMines(board);
    }

    [Fact]
    public void RemovingARowRecountsNumbersNextToIt()
    {
        // Clear the lower row, then check every remaining number only counts mines that still exist.
        for (int seed = 0; seed < 100; seed++)
        {
            var board = new EndlessBoard(new Random(seed));
            board.Reveal(1, 0);
            if (board.Status == GameStatus.Lost) continue;

            for (int x = 0; x < 16 && board.IsVisibleRow(0); x++)
                if (!board[0, x].IsMine) board.Reveal(0, x);

            Assert.Equal(GameStatus.Playing, board.Status);
            AssertNumbersMatchMines(board);
        }
    }

    [Fact]
    public void ClearingBottomRowsBuysTime()
    {
        var board = new EndlessBoard(new Random(7));
        board.Reveal(1, 0);
        board.Advance(EndlessBoard.RowInterval(0) * 1.5);
        Assert.Equal(GameStatus.Playing, board.Status);

        // Clearing the lowest row makes the lowest remaining row sit higher up.
        double lowestBefore = board.VisibleRows.Max(s => board.SlotPosition(s));
        int bottom = board.VisibleRows.First();
        for (int x = 0; x < 16 && board.IsVisibleRow(bottom); x++)
            if (!board[bottom, x].IsMine && board[bottom, x].State == CellState.Hidden) board.Reveal(bottom, x);

        Assert.False(board.IsVisibleRow(bottom));
        Assert.True(board.VisibleRows.Max(s => board.SlotPosition(s)) < lowestBefore);
    }

    [Fact]
    public void SimulatedPlayThatClearsRowsKeepsNumbersConsistent()
    {
        for (int seed = 0; seed < 40; seed++)
        {
            var board = new EndlessBoard(new Random(seed));
            board.Reveal(1, 4);
            for (int step = 0; step < 400 && board.Status == GameStatus.Playing; step++)
            {
                board.Advance(0.5);
                var rows = board.VisibleRows;
                if (rows.Count == 0) continue;
                int target = rows[step % rows.Count];
                for (int x = 0; x < 16 && board.Status == GameStatus.Playing; x++)
                    if (board.IsVisibleRow(target) && !board[target, x].IsMine && board[target, x].State == CellState.Hidden)
                        board.Reveal(target, x);
                if (board.Status == GameStatus.Playing) AssertNumbersMatchMines(board);
            }
            Assert.NotEqual(LossReason.Mine, board.LossReason);
        }
    }

    [Fact]
    public void EmptyCellsOpenTheirNeighborsAcrossRows()
    {
        var board = new EndlessBoard(new Random(8));
        board.Reveal(1, 0);

        foreach (int s in board.VisibleRows)
            for (int x = 0; x < 16; x++)
                if (board[s, x].State == CellState.Revealed && board[s, x].AdjacentMines == 0)
                    Assert.All(board.Neighbors(s, x), n => Assert.Equal(CellState.Revealed, board[n.Row, n.Col].State));
    }

    // ---- flagless tracking and the unlock ----

    [Fact]
    public void FlagsPlacedCountsEveryPlayerFlagEvenIfRemoved()
    {
        var board = new Board(Difficulty.Beginner, new Random(1));
        Assert.Equal(0, board.FlagsPlaced);

        board.ToggleFlag(0, 0); // placed
        board.ToggleFlag(0, 0); // removed
        board.ToggleFlag(0, 0); // placed again
        board.ToggleFlag(1, 0); // placed

        Assert.Equal(3, board.FlagsPlaced);
        Assert.Equal(2, board.FlagCount);
    }

    [Fact]
    public void FlagsPlacedIgnoresTheFlagsTheGameAddsOnAWin()
    {
        var board = new Board(Difficulty.Beginner, new Random(11));
        board.Reveal(4, 4);
        for (int y = 0; y < board.Rows; y++)
            for (int x = 0; x < board.Columns; x++)
                if (!board[x, y].IsMine) board.Reveal(x, y);

        Assert.Equal(GameStatus.Won, board.Status);
        Assert.Equal(board.MineCount, board.FlagCount);
        Assert.Equal(0, board.FlagsPlaced);
    }

    [Fact]
    public void FlagsRefusedByTheLimitAreNotCounted()
    {
        var board = new Board(ChallengeLevel.Get(20).Difficulty, new Random(1), ChallengeLevel.Get(20).Rules);
        for (int y = 0; y < board.Rows; y++)
            for (int x = 0; x < board.Columns; x++)
                board.ToggleFlag(x, y);

        Assert.Equal(board.FlagLimit, board.FlagsPlaced);
    }

    [Fact]
    public void EndlessUnlocksOnlyAfterEveryLevelIsClearedWithoutFlags()
    {
        var save = new SaveData();
        Assert.False(save.EndlessUnlocked);

        for (int level = 1; level < ChallengeLevel.Count; level++) save.MarkFlaglessClear(level);
        Assert.False(save.EndlessUnlocked);
        Assert.Equal(19, save.FlaglessLevelCount);

        save.MarkFlaglessClear(3); // repeats do not count twice
        Assert.Equal(19, save.FlaglessLevelCount);

        save.MarkFlaglessClear(ChallengeLevel.Count);
        Assert.True(save.EndlessUnlocked);
    }

    [Fact]
    public void OutOfRangeLevelsDoNotCountTowardsTheUnlock()
    {
        var save = new SaveData();
        save.MarkFlaglessClear(0);
        save.MarkFlaglessClear(21);
        Assert.Equal(0, save.FlaglessLevelCount);
    }

    [Fact]
    public void FlaglessProgressAndEndlessRecordsSurviveSaveAndLoad()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ms-test-{Guid.NewGuid():N}.json");
        try
        {
            var save = new SaveData();
            save.MarkFlaglessClear(2);
            save.MarkFlaglessClear(9);
            save.RecordEndlessRun(90_000, 12);
            save.Save(path);

            var loaded = SaveData.Load(path);
            Assert.True(loaded.IsFlaglessCleared(2));
            Assert.True(loaded.IsFlaglessCleared(9));
            Assert.False(loaded.IsFlaglessCleared(3));
            Assert.Equal(90_000, loaded.EndlessBestMs);
            Assert.Equal(12, loaded.EndlessBestRows);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EndlessRecordKeepsTheLongestRunAndMostRows()
    {
        var save = new SaveData();
        Assert.True(save.RecordEndlessRun(60_000, 10));
        Assert.False(save.RecordEndlessRun(30_000, 25));
        Assert.Equal(60_000, save.EndlessBestMs);
        Assert.Equal(25, save.EndlessBestRows);
        Assert.True(save.RecordEndlessRun(61_000, 5));
        Assert.Equal(25, save.EndlessBestRows);
    }
}
