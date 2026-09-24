namespace Minesweeper.Core;

/// <summary>Optional rule twists on top of a <see cref="Difficulty"/>. The defaults are classic Minesweeper.</summary>
public sealed record BoardRules
{
    public static readonly BoardRules Classic = new();

    /// <summary>Most flags the player may place. Null means one per mine.</summary>
    public int? FlagLimit { get; init; }

    /// <summary>When true the first click's neighbors are mine-free too, so it opens up an area.</summary>
    public bool SafeStart { get; init; }

    /// <summary>0..1 chance that each mine is placed next to an existing mine instead of anywhere.</summary>
    public double Clustering { get; init; }

    /// <summary>0..1 share of numbered safe cells that show an upside-down "?" until enough neighbors are revealed.</summary>
    public double MysteryFraction { get; init; }

    /// <summary>Revealed neighbors a mystery cell needs before it shows its number.</summary>
    public int MysteryThreshold { get; init; } = 2;
}
