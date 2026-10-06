using Dapr.Actors.Runtime;
using Dapr.Client;
using SudokuDaprActors.Cells.Contracts;
using SudokuDaprActors.Core;

namespace SudokuDaprActors.Cells;

/// <summary>
/// One cell, as a Dapr actor (ADR 0005). It hosts the cell's rules, keeps them in the actor state store, and carries
/// out what they decide: it announces its events on the cell's row, column and box topics, reporting the deliveries
/// to its grid's actor first, and claims any contradiction there. It asks that actor before placing a deduction.
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

    public async Task EliminateAsync(int digit)
    {
        var reaction = _rules.Eliminate(digit);

        // A naked single is placed in the same turn, rather than sent to the cell itself (ADR 0005).
        var single = reaction.Deduction is { } deduction ? await TryDeduceAsync(deduction.Digit) : null;
        await CarryOutAsync(reaction, single);
    }

    public async Task DeduceAsync(int digit)
    {
        if (await TryDeduceAsync(digit) is { } reaction)
        {
            await CarryOutAsync(reaction);
        }
    }

    public Task<Cell> GetAsync() => Task.FromResult(_rules.Snapshot());

    public async Task ForgetAsync()
    {
        await StateManager.TryRemoveStateAsync(State);
        _rules = new CellRules(_rules.Row, _rules.Column);
    }

    private IGridActor Grid => ProxyFactory.CreateActorProxy<IGridActor>(ActorIds.Grid(_grid), ActorIds.GridType);

    // A stale deduction is dropped without asking. Otherwise the grid's actor counts it, unless the grid is in
    // contradiction, and nothing can change the cell between its answer and the placement: they are one actor turn.
    private async Task<Reaction?> TryDeduceAsync(int digit) =>
        _rules.CanDeduce(digit) && await Grid.DeduceAsync() ? _rules.Deduce(digit) : null;

    // Steps come with their own ticket, so only the contradiction and the events are carried out for now.
    private async Task CarryOutAsync(params Reaction?[] reactions)
    {
        List<Reaction> changes = [.. reactions.OfType<Reaction>().Where(reaction => reaction != Reaction.None)];
        if (changes.Count == 0)
        {
            return;
        }

        await StateManager.SetStateAsync(State, _rules.Snapshot());

        if (changes.Any(reaction => reaction.Contradiction is not null))
        {
            await Grid.ContradictAsync();
        }

        List<UnitMessage> messages = [.. changes.SelectMany(reaction => reaction.Announcements).Select(ToMessage)];
        if (messages.Count == 0)
        {
            return;
        }

        var units = Unit.Of(_rules.Row, _rules.Column);
        await Grid.ReportAsync(messages.Count * units.Count);
        foreach (var message in messages)
        {
            foreach (var unit in units)
            {
                await dapr.PublishEventAsync(Topics.PubSub, unit.Topic, message);
            }
        }
    }

    private UnitMessage ToMessage(Event announcement) => announcement switch
    {
        Event.Filled filled => new(_grid, UnitMessage.Kinds.Filled, filled.Row, filled.Column, filled.Digit),
        Event.CandidateLost lost => new(_grid, UnitMessage.Kinds.CandidateLost, lost.Row, lost.Column, lost.Digit),
        _ => throw new InvalidOperationException($"An event of unknown kind {announcement}."),
    };
}
