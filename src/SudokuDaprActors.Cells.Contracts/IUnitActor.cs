using Dapr.Actors;

namespace SudokuDaprActors.Cells.Contracts;

/// <summary>
/// One row, column or box of one grid, as a Dapr actor with an id such as <c>{grid}:row-3</c> (ADR 0005). It hears
/// every event on its unit's topic, finds the hidden singles it leaves, which it sends on the topic for their cell to
/// place, and the digits with no cell left in the unit, which put the grid in contradiction.
/// </summary>
public interface IUnitActor : IActor
{
    /// <summary>An event on the unit's topic: <c>Filled</c> or <c>CandidateLost</c>.</summary>
    Task HearAsync(UnitMessage message);

    /// <summary>Removes the unit's state, once its grid is no longer used.</summary>
    Task ForgetAsync();
}
