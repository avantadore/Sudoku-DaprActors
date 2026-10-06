namespace SudokuDaprActors.Core;

/// <summary>
/// An instruction addressed to one cell. The rules only ever send deductions: a move reaches its cell as a direct call
/// to the cell's actor (ADR 0005).
/// </summary>
internal abstract record Command
{
    private Command(int row, int column)
    {
        Row = row;
        Column = column;
    }

    public int Row { get; }

    public int Column { get; }

    /// <summary>
    /// The rules force <paramref name="Digit"/> in the cell: a naked single, which the cell places itself, or a hidden
    /// single, which a unit's actor sends on the unit's topic.
    /// </summary>
    public sealed record PlaceDeduction(int Row, int Column, int Digit) : Command(Row, Column);
}
