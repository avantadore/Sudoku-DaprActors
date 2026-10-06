using Dapr.Actors;

namespace SudokuDaprActors.Cells.Contracts;

/// <summary>
/// One grid, as a Dapr actor whose id is the grid's (ADR 0005). It counts the deliveries in flight: an actor reports
/// <c>+k</c> before it publishes k messages, a subscriber reports <c>-1</c> once the actors it called have returned,
/// and at zero the grid publishes <see cref="CascadeOver"/> with the deductions it counted. It holds the grid's
/// contradiction flag.
/// </summary>
public interface IGridActor : IActor
{
    /// <summary>
    /// A cell is about to place a deduction. True, and the deduction is counted, unless the grid is in contradiction:
    /// checked and counted in one turn, so no deduction is placed once the grid is in contradiction.
    /// </summary>
    Task<bool> DeduceAsync();

    /// <summary>Changes the number of deliveries in flight by <paramref name="change"/>.</summary>
    Task ReportAsync(int change);

    /// <summary>A delivery failed, which is a bug: the cascade is over, and the move fails with <paramref name="reason"/>.</summary>
    Task FailAsync(string reason);

    /// <summary>Puts the grid in contradiction. True only for the first contradiction, which its finder publishes.</summary>
    Task<bool> ContradictAsync();

    /// <summary>Whether the grid is in contradiction, so it accepts no further moves.</summary>
    Task<bool> IsContradictedAsync();

    /// <summary>Makes every cell and unit of the grid remove its state, and then removes its own.</summary>
    Task ForgetAsync();
}
