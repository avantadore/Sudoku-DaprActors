using Dapr.Actors.Runtime;
using Dapr.Client;
using SudokuDaprActors.Cells.Contracts;
using SudokuDaprActors.Core;

namespace SudokuDaprActors.Cells;

/// <summary>
/// One row, column or box, as a Dapr actor (ADR 0005). It hosts the unit's rules, keeps them in the actor state
/// store, and carries out what they decide: it claims any contradiction at its grid's actor, publishing it as a step if
/// it is the grid's first, and sends each hidden single on the unit's topic, reporting every delivery to that actor
/// first.
/// </summary>
internal sealed class UnitActor(ActorHost host, DaprClient dapr) : Actor(host), IUnitActor
{
    private const string State = "unit";

    private Guid _grid;
    private Unit _unit = null!;
    private UnitRules _rules = null!;

    protected override async Task OnActivateAsync()
    {
        (_grid, _unit) = ActorIds.ParseUnit(Id);
        var saved = await StateManager.TryGetStateAsync<UnitSnapshot>(State);
        _rules = saved.HasValue ? UnitRules.From(_unit.Kind, _unit.Number, Cells, saved.Value) : NewRules();
    }

    public async Task HearAsync(UnitMessage message)
    {
        var reaction = message.Kind switch
        {
            UnitMessage.Kinds.Filled => _rules.Filled(message.Row, message.Column),
            UnitMessage.Kinds.CandidateLost => _rules.CandidateLost(message.Row, message.Column, message.Digit),
            _ => throw new InvalidOperationException($"A unit's actor does not hear {message.Kind}."),
        };

        // Saved even when nothing is to be done: hearing Filled changes what the unit knows.
        await StateManager.SetStateAsync(State, _rules.Snapshot());

        // Only the grid's first contradiction is published as a step.
        var contradiction = reaction.Contradiction is { } found && await Grid.ContradictAsync() ? found : null;
        var deliveries = (contradiction is null ? 0 : 1) + (reaction.Deduction is null ? 0 : 1);
        if (deliveries > 0)
        {
            await Grid.ReportAsync(deliveries);
        }

        if (contradiction is not null)
        {
            await dapr.PublishEventAsync(Topics.InOrder, Topics.Steps, StepMessage.From(_grid, contradiction));
        }

        if (reaction.Deduction is { } deduction)
        {
            await dapr.PublishEventAsync(
                Topics.PubSub, _unit.Topic,
                new UnitMessage(_grid, UnitMessage.Kinds.PlaceDeduction, deduction.Row, deduction.Column, deduction.Digit));
        }
    }

    public async Task ForgetAsync()
    {
        await StateManager.TryRemoveStateAsync(State);
        _rules = NewRules();
    }

    private IReadOnlyList<(int Row, int Column)> Cells => [.. _unit.Cells];

    private IGridActor Grid => ProxyFactory.CreateActorProxy<IGridActor>(ActorIds.Grid(_grid), ActorIds.GridType);

    private UnitRules NewRules() => new(_unit.Kind, _unit.Number, Cells);
}
