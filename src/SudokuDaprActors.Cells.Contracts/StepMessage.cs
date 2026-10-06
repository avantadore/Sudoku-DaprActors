using SudokuDaprActors.Core;

namespace SudokuDaprActors.Cells.Contracts;

/// <summary>
/// A <see cref="Step"/> of the grid it carries, on the <see cref="Topics.Steps"/> topic (ADR 0005): one flat record,
/// because JSON cannot tell an abstract record's kinds apart without a discriminator. Only the fields of its kind are
/// set.
/// </summary>
public sealed record StepMessage(
    Guid Grid, StepMessage.Kinds Kind, int Row = 0, int Column = 0, int Digit = 0, PlacementSource? Source = null,
    UnitKind? UnitKind = null, int UnitNumber = 0)
{
    public enum Kinds
    {
        Placement,
        Elimination,
        NoCandidateForCell,
        NoCellForDigit,
    }

    public static StepMessage From(Guid grid, Step step) => step switch
    {
        Step.Placement placement => new(grid, Kinds.Placement, placement.Row, placement.Column, placement.Digit, placement.Source),
        Step.Elimination elimination => new(grid, Kinds.Elimination, elimination.Row, elimination.Column, elimination.Digit),
        Step.Contradiction.NoCandidateForCell noCandidate => new(grid, Kinds.NoCandidateForCell, noCandidate.Row, noCandidate.Column),
        Step.Contradiction.NoCellForDigit noCell =>
            new(grid, Kinds.NoCellForDigit, Digit: noCell.Digit, UnitKind: noCell.UnitKind, UnitNumber: noCell.UnitNumber),
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, null),
    };

    public Step ToStep() => Kind switch
    {
        Kinds.Placement => new Step.Placement(Row, Column, Digit, Source ?? throw Missing(nameof(Source))),
        Kinds.Elimination => new Step.Elimination(Row, Column, Digit),
        Kinds.NoCandidateForCell => new Step.Contradiction.NoCandidateForCell(Row, Column),
        Kinds.NoCellForDigit => new Step.Contradiction.NoCellForDigit(UnitKind ?? throw Missing(nameof(UnitKind)), UnitNumber, Digit),
        _ => throw new InvalidOperationException($"A step of unknown kind {Kind}."),
    };

    private InvalidOperationException Missing(string field) => new($"A {Kind} step without its {field}.");
}
