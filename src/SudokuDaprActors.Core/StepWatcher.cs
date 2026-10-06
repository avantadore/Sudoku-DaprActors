using System.Collections.Concurrent;

namespace SudokuDaprActors.Core;

/// <summary>
/// One reader of a game's steps, from when it started watching. A move completes only once every watcher has read
/// every step of its cascade, so after awaiting a move, <see cref="TryRead"/> returns everything it did.
/// </summary>
public sealed class StepWatcher : IAsyncEnumerable<Step>, IAsyncDisposable
{
    private readonly ConcurrentQueue<Step> _steps = new();
    private readonly SemaphoreSlim _arrived = new(0); // released once per step and once on completion; may run ahead
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Func<StepWatcher, ValueTask> _stop;
    private IAsyncDisposable? _reader;

    internal StepWatcher(Func<StepWatcher, ValueTask> stop)
    {
        _stop = stop;
    }

    /// <summary>Completes when the watcher stops: when it is disposed, or its game is. Steps read until then remain.</summary>
    public Task Completion => _completion.Task;

    /// <summary>A watcher of a game that has already been disposed: it has stopped before it started.</summary>
    internal static StepWatcher Stopped()
    {
        var watcher = new StepWatcher(_ => ValueTask.CompletedTask);
        watcher._completion.SetResult();
        return watcher;
    }

    /// <summary>The reader that hands this watcher its steps, stopped when the watcher stops.</summary>
    internal void ReadFrom(IAsyncDisposable reader) => _reader = reader;

    /// <summary>Hands the watcher a step it has read.</summary>
    internal void Read(Step step)
    {
        _steps.Enqueue(step);
        _arrived.Release();
    }

    /// <summary>Takes the next step read and not yet taken, if there is one.</summary>
    public bool TryRead(out Step step) => _steps.TryDequeue(out step!);

    public async IAsyncEnumerator<Step> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            if (_steps.TryDequeue(out var step))
            {
                yield return step;
            }
            else if (_completion.Task.IsCompleted)
            {
                yield break;
            }
            else
            {
                await _arrived.WaitAsync(cancellationToken);
            }
        }
    }

    /// <summary>Stops watching. The game stops counting on this watcher before its next move.</summary>
    public ValueTask DisposeAsync() => _stop(this);

    /// <summary>Stops the watcher's reader and ends its enumeration, once the game no longer counts on it.</summary>
    internal async ValueTask StopAsync()
    {
        if (_reader is { } reader)
        {
            _reader = null;
            await reader.DisposeAsync();
        }

        _completion.TrySetResult();
        _arrived.Release();
    }
}
