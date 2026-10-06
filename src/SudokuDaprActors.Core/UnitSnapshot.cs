namespace SudokuDaprActors.Core;

/// <summary>
/// What a unit's rules know, as its actor saves it: for each digit 1–9, the indexes of the cells that can still hold
/// it, and the indexes of the cells the unit has heard were filled.
/// </summary>
internal sealed record UnitSnapshot(IReadOnlyList<IReadOnlyList<int>> Holders, IReadOnlyList<int> Filled);
