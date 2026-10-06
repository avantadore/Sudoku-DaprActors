using Dapr.Actors.Runtime;
using Dapr.Client;
using SudokuDaprActors.Cells.Contracts;
using SudokuDaprActors.Core;

namespace SudokuDaprActors.Cells;

/// <summary>
/// One cell, as a Dapr actor (ADR 0005). It hosts the cell's rules, keeps them in the actor state store, and carries
/// out what they decide: it announces <c>Filled</c> on the cell's row, column and box topics, reporting the deliveries
/// to its grid's actor first, and claims any contradiction there.
/// </summary>
internal sealed class CellActor(ActorHost host, DaprClient dapr) : Actor(host), ICellActor
{
    private const string State = "cell";

    private Guid _grid;
    private CellRules _rules = null!;

    protected override async Task OnActivateAsync()
    {
        (_grid, var row, var column) = ActorIds.ParseCell(Id);
        var saved = await StateManager.TryGetStateAsync<Cell>(State);
        _rules = saved.HasValue ? CellRules.From(saved.Value) : new CellRules(row, column);
    }

    public async Task<MoveResult> MoveAsync(int digit)
    {
        var (outcome, reaction) = _rules.Move(digit);
        await CarryOutAsync(reaction);
        return MoveResult.From(outcome);
    }

    public Task EliminateAsync(int digit) => CarryOutAsync(_rules.Eliminate(digit));

    public Task<Cell> GetAsync() => Task.FromResult(_rules.Snapshot());

    public async Task ForgetAsync()
    {
        await StateManager.TryRemoveStateAsync(State);
        _rules = new CellRules(_rules.Row, _rules.Column);
    }

    private IGridActor Grid => ProxyFactory.CreateActorProxy<IGridActor>(ActorIds.Grid(_grid), ActorIds.GridType);

    // Steps and deductions come with their own tickets, and so do the units that hear CandidateLost, so only Filled
    // is announced for now.
    private async Task CarryOutAsync(Reaction reaction)
    {
        if (reaction == Reaction.None)
        {
            return;
        }

        await StateManager.SetStateAsync(State, _rules.Snapshot());

        if (reaction.Contradiction is not null)
        {
            await Grid.ContradictAsync();
        }

        var filled = reaction.Announcements.OfType<Event.Filled>().ToList();
        if (filled.Count == 0)
        {
            return;
        }

        var units = Unit.Of(_rules.Row, _rules.Column);
        await Grid.ReportAsync(filled.Count * units.Count);
        foreach (var announcement in filled)
        {
            var message = new UnitMessage(_grid, UnitMessage.Kinds.Filled, announcement.Row, announcement.Column, announcement.Digit);
            foreach (var unit in units)
            {
                await dapr.PublishEventAsync(Topics.PubSub, unit.Topic, message);
            }
        }
    }
}
