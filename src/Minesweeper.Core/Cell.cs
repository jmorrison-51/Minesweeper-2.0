namespace Minesweeper.Core;

public enum CellState : byte
{
    Hidden,
    Flagged,
    Revealed,
}

public struct Cell
{
    public bool IsMine;
    public CellState State;
    public int AdjacentMines;
    public bool Exploded;

    // Mystery cells hide AdjacentMines until MysteryNeeded of their neighbors are revealed.
    public bool IsMystery;
    public int MysteryNeeded;
}
