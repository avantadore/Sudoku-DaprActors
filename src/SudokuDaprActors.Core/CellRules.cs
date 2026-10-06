namespace SudokuDaprActors.Core;

/// <summary>
/// The rules of one cell, with no broker and no I/O. The cell owns its candidates, so it decides whether a move is
/// accepted, and which events it announces.
/// </summary>
internal sealed class CellRules
{
    private readonly SortedSet<int> _candidates = [1, 2, 3, 4, 5, 6, 7, 8, 9];

    public CellRules(int row, int column)
    {
        Row = row;
        Column = column;
    }

    /// <summary>The cell as <paramref name="snapshot"/> left it, as when its state was saved and is read back.</summary>
    public static CellRules From(Cell snapshot)
    {
        var rules = new CellRules(snapshot.Row, snapshot.Column) { Digit = snapshot.Digit, Source = snapshot.Source };
        rules._candidates.IntersectWith(snapshot.Candidates);
        return rules;
    }

    public int Row { get; }

    public int Column { get; }

    public int? Digit { get; private set; }

    public PlacementSource? Source { get; private set; }

    public Cell Snapshot() => new(Row, Column, Digit, Source, [.. _candidates]);

    /// <summary>The player places <paramref name="digit"/> in the cell.</summary>
    public (MoveOutcome Outcome, Reaction Reaction) Move(int digit)
    {
        if (Digit == digit)
        {
            return (new MoveOutcome.Unchanged(), Reaction.None);
        }

        if (Digit is { } placed)
        {
            return (new MoveOutcome.Rejected($"Cell ({Row}, {Column}) already holds {placed}, and placements are final."), Reaction.None);
        }

        if (!_candidates.Contains(digit))
        {
            return (new MoveOutcome.Rejected($"{digit} is not a candidate for cell ({Row}, {Column})."), Reaction.None);
        }

        return (new MoveOutcome.Accepted(), Fill(digit, PlacementSource.Move));
    }

    /// <summary>
    /// The rules force <paramref name="digit"/> in the cell. A unit's view of its cells can lag, and so can the cell's
    /// own: by the time a deduction arrives, the cell may be filled or the digit gone, and then it is stale and null.
    /// </summary>
    public Reaction? Deduce(int digit) => CanDeduce(digit) ? Fill(digit, PlacementSource.Deduction) : null;

    /// <summary>Whether <see cref="Deduce"/> would place <paramref name="digit"/>, rather than find it stale.</summary>
    public bool CanDeduce(int digit) => Digit is null && _candidates.Contains(digit);

    /// <summary>A peer was filled with <paramref name="digit"/>, so the cell eliminates it.</summary>
    public Reaction Eliminate(int digit)
    {
        if (Digit == digit)
        {
            // Only a deduction that raced the elimination ruling it out can do that, so the digit is now twice in a
            // unit and this cell has no candidate left.
            return new Reaction { Contradiction = new Step.Contradiction.NoCandidateForCell(Row, Column) };
        }

        if (!_candidates.Remove(digit))
        {
            return Reaction.None; // already gone, e.g. eliminated by a peer that shares two units with this cell
        }

        return new Reaction
        {
            Steps = [new Step.Elimination(Row, Column, digit)],
            Contradiction = _candidates.Count == 0 ? new Step.Contradiction.NoCandidateForCell(Row, Column) : null,
            Deduction = _candidates.Count == 1 ? new Command.PlaceDeduction(Row, Column, _candidates.Min) : null,
            Announcements = [new Event.CandidateLost(Row, Column, digit)],
        };
    }

    /// <summary>Puts the digit in the cell, announcing it as filled and then the candidates it lost by it.</summary>
    private Reaction Fill(int digit, PlacementSource source)
    {
        List<int> lost = [.. _candidates.Where(candidate => candidate != digit)];
        Digit = digit;
        Source = source;
        _candidates.IntersectWith([digit]);

        // Filled first, so a unit already knows the cell holds its digit when it hears which candidates it lost.
        return new Reaction
        {
            Steps = [new Step.Placement(Row, Column, digit, source)],
            Announcements = [new Event.Filled(Row, Column, digit, source), .. lost.Select(candidate => new Event.CandidateLost(Row, Column, candidate))],
        };
    }
}
