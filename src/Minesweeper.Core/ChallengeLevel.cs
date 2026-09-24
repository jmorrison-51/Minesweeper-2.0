namespace Minesweeper.Core;

/// <summary>One of the 20 Hex Challenge levels. Difficulty ramps with the level number.</summary>
public sealed record ChallengeLevel(int Number, int Columns, int Rows, int Mines, BoardRules Rules)
{
    public const int Count = 20;

    public Difficulty Difficulty => new($"Level {Number}", Columns, Rows, Mines, BoardShape.Hex);

    public string Summary
    {
        get
        {
            var parts = new List<string> { $"{Columns}x{Rows} board", $"{Mines} mines" };
            if (Rules.FlagLimit is int flags && flags < Mines) parts.Add($"only {flags} flags");
            if (Rules.Clustering > 0) parts.Add("clustered mines");
            if (Rules.MysteryFraction > 0) parts.Add("mystery tiles");
            if (!Rules.SafeStart) parts.Add("tight first click");
            return string.Join(", ", parts);
        }
    }

    public static ChallengeLevel Get(int number)
    {
        int n = Math.Clamp(number, 1, Count);
        double t = (n - 1) / (double)(Count - 1);

        int columns = (int)Math.Round(9 + 15 * t);
        int rows = (int)Math.Round(9 + 7 * t);
        int cells = columns * rows;
        int mines = (int)Math.Round(cells * (0.123 + 0.107 * t));

        int? flagLimit = n > 10 ? mines - (int)(mines * 0.02 * (n - 10)) : null;

        var rules = new BoardRules
        {
            FlagLimit = flagLimit,
            SafeStart = n <= 8,
            Clustering = n <= 5 ? 0 : 0.5 * (n - 5) / 15.0,
            MysteryFraction = n < 6 ? 0 : 0.05 + 0.25 * (n - 6) / 14.0,
            MysteryThreshold = n <= 12 ? 2 : 3,
        };

        return new ChallengeLevel(n, columns, rows, mines, rules);
    }
}
