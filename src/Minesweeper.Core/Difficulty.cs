namespace Minesweeper.Core;

public sealed record Difficulty(string Name, int Columns, int Rows, int Mines)
{
    public static readonly Difficulty Beginner = new("Beginner", 9, 9, 10);
    public static readonly Difficulty Intermediate = new("Intermediate", 16, 16, 40);
    public static readonly Difficulty Expert = new("Expert", 30, 16, 99);

    public static Difficulty Custom(int columns, int rows, int mines)
    {
        columns = Math.Clamp(columns, 8, 50);
        rows = Math.Clamp(rows, 8, 30);
        mines = Math.Clamp(mines, 1, columns * rows - 9);
        return new Difficulty("Custom", columns, rows, mines);
    }
}
