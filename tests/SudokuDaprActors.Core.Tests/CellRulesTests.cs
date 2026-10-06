namespace SudokuDaprActors.Core.Tests;

public class CellRulesTests
{
    private readonly CellRules _cell = new(2, 3);

    [Fact]
    public void A_move_on_a_candidate_is_accepted_fills_the_cell_and_announces_it()
    {
        var (outcome, reaction) = _cell.Move(5);

        Assert.IsType<MoveOutcome.Accepted>(outcome);
        AssertFilled(5, PlacementSource.Move);
        ReactionAssert.Equal(
            new Reaction
            {
                Steps = [new Step.Placement(2, 3, 5, PlacementSource.Move)],
                // Filled first, then every candidate the cell lost by it.
                Announcements =
                [
                    new Event.Filled(2, 3, 5, PlacementSource.Move),
                    .. new[] { 1, 2, 3, 4, 6, 7, 8, 9 }.Select(digit => new Event.CandidateLost(2, 3, digit)),
                ],
            },
            reaction);
    }

    [Fact]
    public void The_same_digit_again_leaves_the_cell_unchanged()
    {
        _cell.Move(5);

        var (outcome, reaction) = _cell.Move(5);

        Assert.IsType<MoveOutcome.Unchanged>(outcome);
        ReactionAssert.None(reaction);
    }

    [Fact]
    public void A_move_on_a_filled_cell_is_rejected_because_placements_are_final()
    {
        _cell.Move(5);

        var (outcome, reaction) = _cell.Move(6);

        Assert.Equal(new MoveOutcome.Rejected("Cell (2, 3) already holds 5, and placements are final."), outcome);
        ReactionAssert.None(reaction);
        AssertFilled(5, PlacementSource.Move);
    }

    [Fact]
    public void A_move_on_an_eliminated_digit_is_rejected()
    {
        _cell.Eliminate(7);

        var (outcome, reaction) = _cell.Move(7);

        Assert.Equal(new MoveOutcome.Rejected("7 is not a candidate for cell (2, 3)."), outcome);
        ReactionAssert.None(reaction);
        Assert.Null(_cell.Digit);
    }

    [Fact]
    public void A_peer_filled_with_a_candidate_eliminates_it_and_the_cell_announces_the_loss()
    {
        var reaction = _cell.Eliminate(7);

        Assert.Equal([1, 2, 3, 4, 5, 6, 8, 9], _cell.Snapshot().Candidates);
        ReactionAssert.Equal(
            new Reaction { Steps = [new Step.Elimination(2, 3, 7)], Announcements = [new Event.CandidateLost(2, 3, 7)] },
            reaction);
    }

    [Fact]
    public void Eliminating_a_digit_already_gone_does_nothing()
    {
        // As when two peers that share two units with the cell both announce the same digit.
        _cell.Eliminate(7);

        ReactionAssert.None(_cell.Eliminate(7));
        Assert.Equal([1, 2, 3, 4, 5, 6, 8, 9], _cell.Snapshot().Candidates);
    }

    [Fact]
    public void A_filled_cell_eliminates_nothing_when_a_peer_is_filled_with_another_digit()
    {
        _cell.Move(5);

        ReactionAssert.None(_cell.Eliminate(7));
        AssertFilled(5, PlacementSource.Move);
    }

    [Fact]
    public void A_cell_left_with_one_candidate_has_a_naked_single()
    {
        EliminateAllBut(8, 9);

        var reaction = _cell.Eliminate(8);

        ReactionAssert.Equal(
            new Reaction
            {
                Steps = [new Step.Elimination(2, 3, 8)],
                Deduction = new Command.PlaceDeduction(2, 3, 9),
                Announcements = [new Event.CandidateLost(2, 3, 8)],
            },
            reaction);
        Assert.Null(_cell.Digit); // placed only once the deduction comes back to the cell
    }

    [Fact]
    public void A_cell_left_with_no_candidate_is_a_contradiction()
    {
        EliminateAllBut(9);

        var reaction = _cell.Eliminate(9);

        ReactionAssert.Equal(
            new Reaction
            {
                Steps = [new Step.Elimination(2, 3, 9)],
                Contradiction = new Step.Contradiction.NoCandidateForCell(2, 3),
                Announcements = [new Event.CandidateLost(2, 3, 9)],
            },
            reaction);
    }

    [Fact]
    public void A_peer_filled_with_the_cells_own_digit_is_a_contradiction()
    {
        // Only a deduction that raced the elimination ruling it out can do that: the digit is now twice in a unit.
        _cell.Deduce(5);

        var reaction = _cell.Eliminate(5);

        ReactionAssert.Equal(new Reaction { Contradiction = new Step.Contradiction.NoCandidateForCell(2, 3) }, reaction);
        AssertFilled(5, PlacementSource.Deduction);
    }

    [Fact]
    public void A_deduction_fills_the_cell_and_announces_it()
    {
        var reaction = _cell.Deduce(9);

        AssertFilled(9, PlacementSource.Deduction);
        ReactionAssert.Equal(
            new Reaction
            {
                Steps = [new Step.Placement(2, 3, 9, PlacementSource.Deduction)],
                Announcements =
                [
                    new Event.Filled(2, 3, 9, PlacementSource.Deduction),
                    .. Enumerable.Range(1, 8).Select(digit => new Event.CandidateLost(2, 3, digit)),
                ],
            },
            reaction!);
    }

    [Fact]
    public void A_deduction_for_a_cell_already_filled_is_stale()
    {
        // As when a unit hears its other cells lose the digit before it hears this cell was filled with it.
        _cell.Move(9);

        Assert.Null(_cell.Deduce(9));
        AssertFilled(9, PlacementSource.Move);
    }

    [Fact]
    public void A_deduction_of_a_digit_the_cell_has_since_lost_is_stale()
    {
        _cell.Eliminate(9);

        Assert.Null(_cell.Deduce(9));
        Assert.Null(_cell.Digit);
    }

    private void EliminateAllBut(params int[] kept)
    {
        foreach (var digit in Enumerable.Range(1, 9).Except(kept))
        {
            _cell.Eliminate(digit);
        }
    }

    private void AssertFilled(int digit, PlacementSource source)
    {
        var snapshot = _cell.Snapshot();
        Assert.Equal((2, 3, digit, source), (snapshot.Row, snapshot.Column, snapshot.Digit, snapshot.Source));
        Assert.Equal([digit], snapshot.Candidates);
    }
}
