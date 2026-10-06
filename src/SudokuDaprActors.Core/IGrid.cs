namespace SudokuDaprActors.Core;

/// <summary>
/// A game's grid, as the game sees it wherever its cells run. The game makes one move at a time on it, and forgets it
/// by disposing it, when a replay replaces it or the game ends, which frees whatever the grid runs on.
/// </summary>
public interface IGrid : IAsyncDisposable
{
    GameState State { get; }

    /// <summary>All 81 cells, row by row.</summary>
    IEnumerable<Cell> Cells { get; }

    Cell Cell(int row, int column);

    /// <summary>
    /// The player places <paramref name="digit"/> in a cell. Completes once the whole cascade has, with how many
    /// deductions it made. The caller makes one move at a time.
    /// </summary>
    Task<(MoveOutcome Outcome, int Deductions)> MoveAsync(int row, int column, int digit);
}
