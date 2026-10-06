namespace SudokuDaprActors.Cells.Contracts;

/// <summary>
/// A message on a unit's topic, about one cell of the grid it carries (ADR 0005). The topics are shared by every grid.
/// </summary>
public sealed record UnitMessage(Guid Grid, UnitMessage.Kinds Kind, int Row, int Column, int Digit)
{
    public enum Kinds
    {
        /// <summary>The cell was filled with the digit. Its peers in the unit eliminate it.</summary>
        Filled,
    }
}
