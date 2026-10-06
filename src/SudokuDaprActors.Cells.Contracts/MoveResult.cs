using SudokuDaprActors.Core;

namespace SudokuDaprActors.Cells.Contracts;

/// <summary>
/// A <see cref="MoveOutcome"/> as it travels back from a cell's actor: one flat record, because JSON cannot tell an
/// abstract record's kinds apart without a discriminator.
/// </summary>
public sealed record MoveResult(MoveResult.Kinds Kind, string? Reason = null)
{
    public enum Kinds
    {
        Accepted,
        Unchanged,
        Rejected,
    }

    public static MoveResult From(MoveOutcome outcome) => outcome switch
    {
        MoveOutcome.Accepted => new(Kinds.Accepted),
        MoveOutcome.Unchanged => new(Kinds.Unchanged),
        MoveOutcome.Rejected rejected => new(Kinds.Rejected, rejected.Reason),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    public MoveOutcome ToOutcome() => Kind switch
    {
        Kinds.Accepted => new MoveOutcome.Accepted(),
        Kinds.Unchanged => new MoveOutcome.Unchanged(),
        Kinds.Rejected => new MoveOutcome.Rejected(Reason ?? ""),
        _ => throw new InvalidOperationException($"A move result of unknown kind {Kind}."),
    };
}
