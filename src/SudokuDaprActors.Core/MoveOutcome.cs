namespace SudokuDaprActors.Core;

/// <summary>What became of a move: accepted, unchanged (the same digit again), or rejected with a reason.</summary>
public abstract record MoveOutcome
{
    private MoveOutcome()
    {
    }

    public sealed record Accepted : MoveOutcome;

    public sealed record Unchanged : MoveOutcome;

    public sealed record Rejected(string Reason) : MoveOutcome
    {
        /// <summary>Every move on a grid in contradiction, whichever cell it is for.</summary>
        public static Rejected InContradiction { get; } = new(
            "The game is in contradiction, so no more moves can be made. Replay to an earlier position to continue, or start a new game.");
    }
}
