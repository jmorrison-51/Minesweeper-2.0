using Minesweeper.Core;

namespace Minesweeper.Core.Tests;

public class ChallengeTests
{
    private static Board LevelBoard(int level, int seed) =>
        new(ChallengeLevel.Get(level).Difficulty, new Random(seed), ChallengeLevel.Get(level).Rules);

    private static IEnumerable<(int X, int Y)> AllCells(Board board)
    {
        for (int y = 0; y < board.Rows; y++)
            for (int x = 0; x < board.Columns; x++)
                yield return (x, y);
    }

    [Fact]
    public void LevelsRampUpInSizeAndMines()
    {
        var levels = Enumerable.Range(1, ChallengeLevel.Count).Select(ChallengeLevel.Get).ToList();

        Assert.Equal((9, 9, 10), (levels[0].Columns, levels[0].Rows, levels[0].Mines));
        for (int i = 1; i < levels.Count; i++)
        {
            Assert.True(levels[i].Mines >= levels[i - 1].Mines, $"mines drop at level {i + 1}");
            Assert.True(levels[i].Columns * levels[i].Rows >= levels[i - 1].Columns * levels[i - 1].Rows);
            Assert.True(levels[i].Rules.Clustering >= levels[i - 1].Rules.Clustering);
            Assert.True(levels[i].Rules.MysteryFraction >= levels[i - 1].Rules.MysteryFraction);
        }
        Assert.All(levels, l => Assert.True(l.Mines < l.Columns * l.Rows - 7));
    }

    [Fact]
    public void EarlyLevelsHaveNoTwistsAndLateLevelsDo()
    {
        var first = ChallengeLevel.Get(1);
        Assert.Null(first.Rules.FlagLimit);
        Assert.Equal(0, first.Rules.MysteryFraction);
        Assert.Equal(0, first.Rules.Clustering);
        Assert.True(first.Rules.SafeStart);

        var last = ChallengeLevel.Get(20);
        Assert.True(last.Rules.FlagLimit < last.Mines);
        Assert.True(last.Rules.MysteryFraction > 0);
        Assert.True(last.Rules.Clustering > 0);
        Assert.False(last.Rules.SafeStart);
    }

    [Fact]
    public void FlagBudgetShrinksAndIsEnforced()
    {
        var level = ChallengeLevel.Get(20);
        var board = LevelBoard(20, 1);
        Assert.Equal(level.Rules.FlagLimit, board.FlagLimit);

        foreach (var (x, y) in AllCells(board)) board.ToggleFlag(x, y);

        Assert.Equal(board.FlagLimit, board.FlagCount);
        Assert.Equal(0, board.FlagsRemaining);
        Assert.True(board.FlagLimit < board.MineCount);
    }

    [Fact]
    public void SafeStartProtectsTheClickedCellAndItsNeighbors()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            var board = LevelBoard(1, seed);
            board.Reveal(4, 4);
            Assert.False(board[4, 4].IsMine);
            Assert.All(board.Neighbors(4, 4), n => Assert.False(board[n.X, n.Y].IsMine));
            Assert.Equal(0, board[4, 4].AdjacentMines);
            Assert.Equal(10, AllCells(board).Count(c => board[c.X, c.Y].IsMine));
        }
    }

    [Fact]
    public void MineCountIsExactWithClusteringAndFirstClickIsSafe()
    {
        for (int level = 6; level <= 20; level += 7)
        {
            for (int seed = 0; seed < 30; seed++)
            {
                var board = LevelBoard(level, seed);
                board.Reveal(3, 3);
                Assert.False(board[3, 3].IsMine);
                Assert.Equal(board.MineCount, AllCells(board).Count(c => board[c.X, c.Y].IsMine));
                Assert.NotEqual(GameStatus.Lost, board.Status);
            }
        }
    }

    [Fact]
    public void ClusteringProducesMoreTouchingMinesThanScattering()
    {
        static double AverageMineNeighbors(BoardRules rules)
        {
            double total = 0;
            for (int seed = 0; seed < 50; seed++)
            {
                var board = new Board(Difficulty.Expert.WithShape(BoardShape.Hex), new Random(seed), rules);
                board.Reveal(0, 0);
                foreach (var (x, y) in AllCells(board))
                    if (board[x, y].IsMine) total += board.Neighbors(x, y).Count(n => board[n.X, n.Y].IsMine);
            }
            return total;
        }

        Assert.True(AverageMineNeighbors(new BoardRules { Clustering = 0.6 }) > AverageMineNeighbors(BoardRules.Classic) * 1.3);
    }

    [Fact]
    public void MysteryCellsAreNumberedSafeCellsWithAchievableThresholds()
    {
        var board = LevelBoard(20, 3);
        board.Reveal(5, 5);

        var mystery = AllCells(board).Where(c => board[c.X, c.Y].IsMystery).ToList();
        Assert.NotEmpty(mystery);
        foreach (var (x, y) in mystery)
        {
            var cell = board[x, y];
            Assert.False(cell.IsMine);
            Assert.True(cell.AdjacentMines > 0);
            Assert.InRange(cell.MysteryNeeded, 1, ChallengeLevel.Get(20).Rules.MysteryThreshold);
            Assert.True(cell.MysteryNeeded <= board.Neighbors(x, y).Count(n => !board[n.X, n.Y].IsMine));
        }
        Assert.False(board[5, 5].IsMystery);
    }

    [Fact]
    public void MysteryNumberStaysHiddenUntilEnoughNeighborsAreRevealed()
    {
        var board = LevelBoard(20, 3);
        board.Reveal(5, 5);

        // Find a mystery cell that is still hidden and has at least two safe neighbors, then reveal it.
        var (x, y) = AllCells(board).First(c =>
            board[c.X, c.Y].IsMystery && board[c.X, c.Y].State == CellState.Hidden && board[c.X, c.Y].MysteryNeeded >= 2 &&
            board.Neighbors(c.X, c.Y).Count(n => board[n.X, n.Y].State == CellState.Revealed) == 0);
        board.Reveal(x, y);
        Assert.Equal(CellState.Revealed, board[x, y].State);
        Assert.True(board.IsNumberHidden(x, y));

        // Chording a hidden number is refused, even with matching flags around it.
        foreach (var n in board.Neighbors(x, y).Where(n => board[n.X, n.Y].IsMine)) board.ToggleFlag(n.X, n.Y);
        int revealedBefore = AllCells(board).Count(c => board[c.X, c.Y].State == CellState.Revealed);
        board.Chord(x, y);
        Assert.Equal(revealedBefore, AllCells(board).Count(c => board[c.X, c.Y].State == CellState.Revealed));

        // Revealing safe neighbors earns the number.
        foreach (var n in board.Neighbors(x, y).Where(n => !board[n.X, n.Y].IsMine))
        {
            board.Reveal(n.X, n.Y);
            if (!board.IsNumberHidden(x, y)) break;
        }
        Assert.False(board.IsNumberHidden(x, y));
    }

    [Fact]
    public void EveryMysteryNumberIsShownOnceTheGameEnds()
    {
        var board = LevelBoard(20, 3);
        board.Reveal(5, 5);
        var (mx, my) = AllCells(board).First(c => board[c.X, c.Y].IsMine);
        board.Reveal(mx, my);

        Assert.Equal(GameStatus.Lost, board.Status);
        Assert.All(AllCells(board), c => Assert.False(board.IsNumberHidden(c.X, c.Y)));
    }

    [Fact]
    public void ChallengeLevelCanBeWonByRevealingEverySafeCell()
    {
        for (int level = 1; level <= ChallengeLevel.Count; level++)
        {
            var board = LevelBoard(level, level);
            board.Reveal(4, 4);
            foreach (var (x, y) in AllCells(board))
                if (!board[x, y].IsMine) board.Reveal(x, y);
            Assert.Equal(GameStatus.Won, board.Status);
        }
    }

    [Fact]
    public void ClearingALevelUnlocksTheNextAndNeverRelocks()
    {
        var save = new SaveData();
        Assert.Equal(1, save.ChallengePlayable);

        Assert.Equal(2, save.CompleteChallengeLevel(1));
        Assert.Equal(2, save.ChallengeUnlocked);
        Assert.Equal(3, save.CompleteChallengeLevel(2));

        // Replaying an earlier level does not move progress backwards.
        save.CompleteChallengeLevel(1);
        Assert.Equal(3, save.ChallengeUnlocked);
    }

    [Fact]
    public void ClearingTheLastLevelCompletesTheChallenge()
    {
        var save = new SaveData { ChallengeUnlocked = 20 };
        Assert.False(save.ChallengeCompleted);

        Assert.Null(save.CompleteChallengeLevel(20));

        Assert.True(save.ChallengeCompleted);
        Assert.Equal(20, save.ChallengePlayable);
    }

    [Fact]
    public void ChallengeProgressSurvivesSaveAndLoad()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ms-test-{Guid.NewGuid():N}.json");
        try
        {
            var save = new SaveData { ChallengeUnlocked = 7, ChallengeCurrent = 5 };
            save.TrySetBestTime(SaveData.ChallengeKey(4), 12345);
            save.Save(path);

            var loaded = SaveData.Load(path);
            Assert.Equal(7, loaded.ChallengeUnlocked);
            Assert.Equal(5, loaded.ChallengeCurrent);
            Assert.Equal(12345, loaded.BestTimesMs["Hex Level 4"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // A level-12 board whose first click (the center) shows a lone number, so the player must guess.
    private static Board IsolatedStart(out (int X, int Y) first)
    {
        for (int seed = 0; ; seed++)
        {
            var board = LevelBoard(12, seed);
            first = (board.Columns / 2, board.Rows / 2);
            board.Reveal(first.X, first.Y);
            if (AllCells(board).Count(c => board[c.X, c.Y].State == CellState.Revealed) == 1) return board;
        }
    }

    [Fact]
    public void HittingAMineRightAfterALoneNumberIsAFailedOpeningGuess()
    {
        var board = IsolatedStart(out _);
        var mine = AllCells(board).First(c => board[c.X, c.Y].IsMine);
        board.Reveal(mine.X, mine.Y);

        Assert.Equal(GameStatus.Lost, board.Status);
        Assert.True(board.OpeningGuessFailed);
    }

    [Fact]
    public void ASafeSecondRevealOrALaterMineIsNotAFailedOpeningGuess()
    {
        var board = IsolatedStart(out _);
        var safe = AllCells(board).First(c => !board[c.X, c.Y].IsMine && board[c.X, c.Y].State == CellState.Hidden);
        board.Reveal(safe.X, safe.Y);
        var mine = AllCells(board).First(c => board[c.X, c.Y].IsMine);
        board.Reveal(mine.X, mine.Y);

        Assert.Equal(GameStatus.Lost, board.Status);
        Assert.False(board.OpeningGuessFailed);
    }

    [Fact]
    public void AMineRightAfterAnOpenedAreaIsNotAFailedOpeningGuess()
    {
        var board = LevelBoard(3, 1); // safe start: the first click always opens an area
        board.Reveal(4, 4);
        var mine = AllCells(board).First(c => board[c.X, c.Y].IsMine);
        board.Reveal(mine.X, mine.Y);

        Assert.False(board.OpeningGuessFailed);
    }

    [Fact]
    public void TwoFailedOpeningsInARowEarnAGuaranteedOpenerOnLevelsWithoutASafeStart()
    {
        var save = new SaveData();
        Assert.False(save.ChallengeRules(12).SafeStart);

        save.RecordChallengeStart(12, openingGuessFailed: true);
        Assert.False(save.NeedsGuaranteedOpener(12));
        save.RecordChallengeStart(15, openingGuessFailed: true);

        Assert.True(save.NeedsGuaranteedOpener(9));
        Assert.True(save.ChallengeRules(20).SafeStart);
        Assert.False(save.NeedsGuaranteedOpener(8)); // levels 1-8 already open safely

        // The opener game opens an area, which is a good start and resets the count.
        save.RecordChallengeStart(12, openingGuessFailed: false);
        Assert.False(save.NeedsGuaranteedOpener(12));
    }

    [Fact]
    public void AGoodStartBetweenBadOnesResetsTheCountAndSafeLevelsAreIgnored()
    {
        var save = new SaveData();
        save.RecordChallengeStart(12, openingGuessFailed: true);
        save.RecordChallengeStart(12, openingGuessFailed: false);
        save.RecordChallengeStart(12, openingGuessFailed: true);
        Assert.False(save.NeedsGuaranteedOpener(12));

        save.RecordChallengeStart(3, openingGuessFailed: false); // level 3 has a safe start; not counted
        save.RecordChallengeStart(12, openingGuessFailed: true);
        Assert.True(save.NeedsGuaranteedOpener(12));
    }

    [Fact]
    public void BestTimesAreNeverUnderOneSecond()
    {
        var save = new SaveData();
        save.TrySetBestTime("Beginner", 0);
        Assert.Equal(SaveData.MinSolveMs, save.BestTimesMs["Beginner"]);
        Assert.False(save.TrySetBestTime("Beginner", 400));
    }
}
