using Dapr.Actors;
using SudokuDaprActors.Core;

namespace SudokuDaprActors.Cells.Contracts;

/// <summary>
/// One cell of one grid, as a Dapr actor with an id such as <c>{grid}:r3c5</c> (ADR 0005). It owns its candidates, so
/// it decides whether a move is accepted, and announces <c>Filled</c> on its row, column and box when it is.
/// </summary>
public interface ICellActor : IActor
{
    /// <summary>The player places <paramref name="digit"/> in the cell. Its cascade runs on after this returns.</summary>
    Task<MoveResult> MoveAsync(int digit);

    /// <summary>A peer was filled with <paramref name="digit"/>, so the cell eliminates it.</summary>
    Task EliminateAsync(int digit);

    /// <summary>A snapshot of the cell.</summary>
    Task<Cell> GetAsync();

    /// <summary>Removes the cell's state, once its grid is no longer used.</summary>
    Task ForgetAsync();
}
