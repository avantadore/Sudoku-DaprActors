using System.Collections.Concurrent;
using Dapr.Actors.Client;
using SudokuDaprActors.Cells.Contracts;
using SudokuDaprActors.Core;

namespace SudokuDaprActors.Api;

/// <summary>
/// The Api's subscription to the <see cref="Topics.Steps"/> topic (ADR 0005). It hands each step to whoever follows
/// the grid it came from, which is the game whose current grid it is, and drops the steps of any other grid, such as
/// one being rebuilt by replay. Either way it then reports the delivery handled to the grid's actor, so a move's
/// cascade is over only once its every step has been handed on.
/// </summary>
public sealed class Steps(IActorProxyFactory actors, ILogger<Steps> logger)
{
    private readonly ConcurrentDictionary<Guid, Action<Step>> _followed = new();

    /// <summary>
    /// Hands every step of <paramref name="grid"/> to <paramref name="read"/> from now on, until the result is disposed.
    /// A grid is followed by one game at a time.
    /// </summary>
    public IDisposable Follow(Guid grid, Action<Step> read)
    {
        if (!_followed.TryAdd(grid, read))
        {
            throw new InvalidOperationException($"Grid {grid} is already followed.");
        }

        return new Unfollow(() => _followed.TryRemove(grid, out _));
    }

    public async Task<IResult> HearAsync(StepMessage message)
    {
        var grid = actors.CreateActorProxy<IGridActor>(ActorIds.Grid(message.Grid), ActorIds.GridType);
        try
        {
            if (_followed.GetValueOrDefault(message.Grid) is { } read)
            {
                read(message.ToStep());
            }
        }
        catch (Exception exception)
        {
            // A bug, as for a unit's message: the move fails, and the step is dropped rather than redelivered.
            logger.LogError(exception, "Handing on {Step} failed.", message);
            await grid.FailAsync($"Handing on {message} failed: {exception.Message}");
            return Results.Ok(new { status = "DROP" });
        }

        await grid.ReportAsync(-1);
        return Results.Ok();
    }

    private sealed class Unfollow(Action stop) : IDisposable
    {
        public void Dispose() => stop();
    }
}
