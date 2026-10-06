using SudokuDaprActors.Core;

namespace SudokuDaprActors.Api;

/// <summary>
/// One step as the steps stream sends it: a flat record whose <see cref="Kind"/> says which fields it carries. A
/// placement and an elimination are about a cell, and a placement has a source. A contradiction is about a cell with
/// no candidates left, or about a unit with no cell left for a digit.
/// </summary>
public sealed record StepResponse(
    StepKind Kind, int? Row, int? Column, int? Digit, PlacementSource? Source, UnitKind? Unit, int? UnitNumber)
{
    public static StepResponse From(Step step) => step switch
    {
        Step.Placement placement =>
            new(StepKind.Placement, placement.Row, placement.Column, placement.Digit, placement.Source, null, null),
        Step.Elimination elimination =>
            new(StepKind.Elimination, elimination.Row, elimination.Column, elimination.Digit, null, null, null),
        Step.Contradiction.NoCandidateForCell noCandidate =>
            new(StepKind.NoCandidateForCell, noCandidate.Row, noCandidate.Column, null, null, null, null),
        Step.Contradiction.NoCellForDigit noCell =>
            new(StepKind.NoCellForDigit, null, null, noCell.Digit, null, noCell.UnitKind, noCell.UnitNumber),
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, null),
    };
}

/// <summary>Named after the kinds of <see cref="Step"/>: a placement, an elimination, or a contradiction of either kind.</summary>
public enum StepKind
{
    Placement,
    Elimination,
    NoCandidateForCell,
    NoCellForDigit,
}
