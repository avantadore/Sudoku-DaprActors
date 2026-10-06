namespace SudokuDaprActors.Cells.Contracts;

/// <summary>
/// No delivery of the grid's cascade is in flight any more, so the move that set it off is complete, on the
/// <see cref="Topics.Cascades"/> topic. A cascade with a <paramref name="Failure"/> ended because a delivery failed.
/// </summary>
public sealed record CascadeOver(Guid Grid, int Deductions, string? Failure = null);
