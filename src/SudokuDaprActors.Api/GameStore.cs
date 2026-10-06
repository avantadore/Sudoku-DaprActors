using System.Collections.Concurrent;

namespace SudokuDaprActors.Api;

/// <summary>
/// Holds games in memory for the lifetime of the process. A game makes one move at a time by itself, but a request
/// also reads the grid it leaves behind, so the store hands a game out only while holding that game's semaphore:
/// one request at a time, held across its awaits. A game's grid costs nothing on the broker, so it lives until a
/// replay replaces it or the store is disposed (ADR 0005).
/// </summary>
public sealed class GameStore(IGridBackend grids) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, Entry> _games = new();

    /// <summary>Starts a new game, running <paramref name="use"/> on it before any other request can reach it.</summary>
    public async Task<T> NewAsync<T>(Func<Guid, Game, T> use)
    {
        var game = await Game.NewAsync(grids);
        var result = use(game.Id, game);
        _games[game.Id] = new Entry(game);
        return result;
    }

    /// <summary>
    /// Runs <paramref name="use"/> on the game while holding its semaphore. Found is false, and Result the default,
    /// if there is no such game.
    /// </summary>
    public async Task<(bool Found, T Result)> TryUseAsync<T>(Guid id, Func<Game, Task<T>> use)
    {
        if (_games.GetValueOrDefault(id) is not { } entry)
        {
            return (false, default!);
        }

        await entry.Turn.WaitAsync();
        try
        {
            return (true, await use(entry.Game));
        }
        finally
        {
            entry.Turn.Release();
        }
    }

    /// <summary>Disposes every game, which forgets their grids.</summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var entry in _games.Values)
        {
            await entry.Game.DisposeAsync();
        }

        _games.Clear();
    }

    private sealed record Entry(Game Game)
    {
        public SemaphoreSlim Turn { get; } = new(1, 1);
    }
}
