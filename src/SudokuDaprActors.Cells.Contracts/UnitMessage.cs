namespace SudokuDaprActors.Cells.Contracts;

/// <summary>
/// A message on a unit's topic, about one cell of the grid it carries (ADR 0005). The topics are shared by every grid.
/// </summary>
public sealed record UnitMessage(Guid Grid, UnitMessage.Kinds Kind, int Row, int Column, int Digit)
{
    public enum Kinds
    {
        /// <summary>An event: the cell was filled with the digit. Its peers in the unit eliminate it.</summary>
        Filled,

        /// <summary>An event: the cell lost the digit as a candidate. Only the unit's actor hears it.</summary>
        CandidateLost,

        /// <summary>A command: the unit found the digit a hidden single in the cell, which places it.</summary>
        PlaceDeduction,
    }
}
