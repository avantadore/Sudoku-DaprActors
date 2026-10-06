using System.Runtime.CompilerServices;
using Dapr.Actors.Client;
using SudokuDaprActors.Cells.Contracts;
using SudokuDaprActors.Core;

namespace SudokuDaprActors.Api;

/// <summary>
/// Grids whose cells are Dapr actors in the Cells service, behind unit subscribers (ADR 0005). The Api only calls
/// the actors, hears when a cascade is over, and hands the steps of each game's current grid to its watchers.
/// </summary>
public sealed class DaprGridBackend(IActorProxyFactory actors, Cascades cascades, Steps steps) : IGridBackend
{
    public Task<IGameGrids> OpenAsync(Guid game, CancellationToken cancellationToken = default) =>
        Task.FromResult<IGameGrids>(new DaprGameGrids(actors, cascades, steps));

    /// <summary>
    /// One game's grids. It follows the steps of its current grid only, and hands each to every watcher. Watchers and
    /// the current grid change only between moves, while no step is on its way unless a cascade failed, which is a bug
    /// that leaves the grid taking no more moves. A step may still be handed on meanwhile, so the readers are swapped
    /// whole.
    /// </summary>
    private sealed class DaprGameGrids(IActorProxyFactory actors, Cascades cascades, Steps steps) : IGameGrids
    {
        private volatile IReadOnlyList<Action<Step>> _readers = [];
        private IDisposable? _following;

        public Task<IGrid> StartAsync() => Task.FromResult<IGrid>(new DaprGrid(Guid.NewGuid(), actors, cascades));

        public void MakeCurrent(IGrid grid)
        {
            _following?.Dispose();
            _following = steps.Follow(((DaprGrid)grid).Id, Read);
        }

        public Task<IAsyncDisposable> WatchAsync(Action<Step> read)
        {
            _readers = [.. _readers, read];
            return Task.FromResult<IAsyncDisposable>(new Reader(() => _readers = [.. _readers.Where(reader => reader != read)]));
        }

        public ValueTask DisposeAsync()
        {
            _following?.Dispose();
            _following = null;
            return ValueTask.CompletedTask;
        }

        private void Read(Step step)
        {
            foreach (var reader in _readers)
            {
                reader(step);
            }
        }

        private sealed class Reader(Action stop) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                stop();
                return ValueTask.CompletedTask;
            }
        }
    }
}

/// <summary>
/// One grid of 81 cell actors and its grid actor. A move is a direct call to the cell's actor, and completes once
/// the grid actor says its cascade is over. The Api keeps a copy of the cells, read from their actors after each
/// accepted move, so reading the grid costs no calls.
/// </summary>
internal sealed class DaprGrid(Guid id, IActorProxyFactory actors, Cascades cascades) : IGrid
{
    /// <summary>How long a move waits for its cascade, which only a bug makes it wait out.</summary>
    private static readonly TimeSpan CascadeTimeout = TimeSpan.FromMinutes(1);

    private IReadOnlyList<Cell> _cells =
        [.. Enumerable.Range(1, 9).SelectMany(row => Enumerable.Range(1, 9).Select(column => new Cell(row, column, null, null, [1, 2, 3, 4, 5, 6, 7, 8, 9])))];

    private bool _contradicted;

    // A cascade that failed or never ended leaves the grid actor's count wrong, and its CascadeOver could still arrive
    // during a later move, so the grid takes no more moves.
    private Exception? _broken;

    /// <summary>The grid's id, which every message about it carries.</summary>
    public Guid Id => id;

    public GameState State =>
        _contradicted ? GameState.Contradicted
        : _cells.All(cell => cell.Digit is not null) ? GameState.Solved
        : GameState.InProgress;

    public IEnumerable<Cell> Cells => _cells;

    public Cell Cell(int row, int column)
    {
        ThrowIfOutside1To9(row);
        ThrowIfOutside1To9(column);
        return _cells[(row - 1) * 9 + column - 1];
    }

    public async Task<(MoveOutcome Outcome, int Deductions)> MoveAsync(int row, int column, int digit)
    {
        ThrowIfOutside1To9(digit);
        Cell(row, column);

        if (_broken is not null)
        {
            throw new InvalidOperationException($"Grid {id} failed an earlier cascade, so it takes no more moves.", _broken);
        }

        if (_contradicted)
        {
            return (MoveOutcome.Rejected.InContradiction, 0);
        }

        // Waiting starts before the move, which may set off a cascade that is over before the cell's actor returns.
        using var cascade = cascades.Expect(id);
        var outcome = (await CellActor(row, column).MoveAsync(digit)).ToOutcome();
        if (outcome is not MoveOutcome.Accepted)
        {
            return (outcome, 0); // nothing changed, so nothing was announced
        }

        int deductions;
        try
        {
            deductions = await cascade.OverAsync(CascadeTimeout);
        }
        catch (Exception exception)
        {
            _broken = exception;
            throw;
        }

        await ReadAsync();
        return (outcome, deductions);
    }

    /// <summary>The grid actor makes every cell remove its state.</summary>
    public async ValueTask DisposeAsync() => await GridActor.ForgetAsync();

    private IGridActor GridActor => actors.CreateActorProxy<IGridActor>(ActorIds.Grid(id), ActorIds.GridType);

    private ICellActor CellActor(int row, int column) =>
        actors.CreateActorProxy<ICellActor>(ActorIds.Cell(id, row, column), ActorIds.CellType);

    private async Task ReadAsync()
    {
        _cells = await Task.WhenAll(_cells.Select(cell => CellActor(cell.Row, cell.Column).GetAsync()));
        _contradicted = await GridActor.IsContradictedAsync();
    }

    private static void ThrowIfOutside1To9(int value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 1, name);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 9, name);
    }
}
