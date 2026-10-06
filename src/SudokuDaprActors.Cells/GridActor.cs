using Dapr.Actors.Runtime;
using Dapr.Client;
using SudokuDaprActors.Cells.Contracts;
using SudokuDaprActors.Core;

namespace SudokuDaprActors.Cells;

/// <summary>
/// One grid, as a Dapr actor (ADR 0005). Each report is its own actor turn, so the count of deliveries in flight
/// never races, and neither does the contradiction flag.
/// </summary>
internal sealed class GridActor(ActorHost host, DaprClient dapr) : Actor(host), IGridActor
{
    private const string Contradicted = "contradicted";

    // Only counted during a cascade, while the actor is busy and so stays active: not worth saving.
    private int _inFlight;

    // Once a delivery has failed, the count is wrong for good, and the grid is no longer used (ADR 0005).
    private bool _failed;

    public async Task ReportAsync(int change)
    {
        if (_failed)
        {
            return;
        }

        _inFlight += change;
        if (_inFlight < 0)
        {
            await FailAsync($"Grid {Id} heard of more deliveries handled than were reported.");
        }
        else if (_inFlight == 0)
        {
            await PublishAsync(new CascadeOver(GridId, Deductions: 0));
        }
    }

    public async Task FailAsync(string reason)
    {
        if (_failed)
        {
            return;
        }

        _failed = true;
        await PublishAsync(new CascadeOver(GridId, Deductions: 0, Failure: reason));
    }

    public async Task<bool> ContradictAsync()
    {
        if (await IsContradictedAsync())
        {
            return false;
        }

        await StateManager.SetStateAsync(Contradicted, true);
        return true;
    }

    public async Task<bool> IsContradictedAsync() =>
        await StateManager.TryGetStateAsync<bool>(Contradicted) is { HasValue: true, Value: true };

    public async Task ForgetAsync()
    {
        var cells = Unit.All.Where(unit => unit.Kind == UnitKind.Row).SelectMany(row => row.Cells);
        await Task.WhenAll(cells.Select(cell => ProxyFactory
            .CreateActorProxy<ICellActor>(ActorIds.Cell(GridId, cell.Row, cell.Column), ActorIds.CellType)
            .ForgetAsync()));
        await StateManager.TryRemoveStateAsync(Contradicted);
    }

    private Guid GridId => Guid.Parse(Id.GetId());

    private Task PublishAsync(CascadeOver over) => dapr.PublishEventAsync(Topics.PubSub, Topics.Cascades, over);
}
