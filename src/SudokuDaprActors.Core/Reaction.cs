namespace SudokuDaprActors.Core;

/// <summary>
/// What a cell's or a unit's rules decided in answer to one message, for whoever hosts them to carry out in this
/// order: publish the steps, then claim the contradiction (only the grid's first one is published) or send the
/// deduction, then announce the events on the cell's units.
/// </summary>
internal sealed record Reaction
{
    /// <summary>Nothing to do: the message changed nothing.</summary>
    public static Reaction None { get; } = new();

    public IReadOnlyList<Step> Steps { get; init; } = [];

    /// <summary>The contradiction the message revealed, if any.</summary>
    public Step.Contradiction? Contradiction { get; init; }

    /// <summary>A naked or hidden single found by the message, if any, to be placed by the cell it is for.</summary>
    public Command.PlaceDeduction? Deduction { get; init; }

    public IReadOnlyList<Event> Announcements { get; init; } = [];
}
