using System.Collections.Concurrent;
using SudokuDaprActors.Cells.Contracts;

namespace SudokuDaprActors.Api;

/// <summary>
/// The Api's subscription to the <see cref="Topics.Cascades"/> topic, which completes the moves waiting for their
/// cascade to be over (ADR 0005). A grid makes one move at a time, so it waits for at most one cascade.
/// </summary>
public sealed class Cascades
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<CascadeOver>> _waiting = new();

    /// <summary>
    /// Starts waiting for the grid's cascade to be over, before the move that sets it off is made, so it cannot be
    /// missed. Disposing the result stops waiting.
    /// </summary>
    public Expected Expect(Guid grid)
    {
        var over = new TaskCompletionSource<CascadeOver>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_waiting.TryAdd(grid, over))
        {
            throw new InvalidOperationException($"Grid {grid} is already waiting for a cascade.");
        }

        return new Expected(over.Task, () => _waiting.TryRemove(grid, out _));
    }

    /// <summary>A grid's cascade is over. Nobody waits for it if the Api has since forgotten the move.</summary>
    public void Hear(CascadeOver over)
    {
        if (_waiting.TryRemove(over.Grid, out var waiting))
        {
            waiting.TrySetResult(over);
        }
    }

    /// <summary>One move's wait for its cascade to be over.</summary>
    public sealed class Expected(Task<CascadeOver> over, Action stop) : IDisposable
    {
        /// <summary>Completes once the cascade is over, with how many deductions it made, or fails if it failed.</summary>
        public async Task<int> OverAsync(TimeSpan timeout)
        {
            var cascade = await over.WaitAsync(timeout);
            return cascade.Failure is { } failure
                ? throw new InvalidOperationException($"The cascade failed: {failure}")
                : cascade.Deductions;
        }

        public void Dispose() => stop();
    }
}
