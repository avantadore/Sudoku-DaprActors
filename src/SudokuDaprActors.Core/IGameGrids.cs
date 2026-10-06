namespace SudokuDaprActors.Core;

/// <summary>
/// Where one game's grids run, and how their steps reach the game's watchers. Only the current grid's steps reach
/// them, so a grid being rebuilt by replay is not seen. Disposing it frees what the game holds there; the game
/// disposes its grids and stops its watchers first.
/// </summary>
public interface IGameGrids : IAsyncDisposable
{
    /// <summary>A new grid with all 81 cells empty. None of its steps reach the watchers until it is made current.</summary>
    Task<IGrid> StartAsync();

    /// <summary>
    /// From now on, the steps of <paramref name="grid"/> reach the watchers, and no other grid's; none at all while it
    /// is null, as when the game is suspended. The grid is one this started. Only between moves.
    /// </summary>
    void MakeCurrent(IGrid? grid);

    /// <summary>
    /// Starts reading the current grid's steps, from its next step on, handing each to <paramref name="read"/>. A move
    /// completes only once every reader has been handed every step of its cascade. Disposing the result stops reading.
    /// Both only between moves.
    /// </summary>
    Task<IAsyncDisposable> WatchAsync(Action<Step> read);
}
