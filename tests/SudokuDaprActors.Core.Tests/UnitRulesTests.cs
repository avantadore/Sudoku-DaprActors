namespace SudokuDaprActors.Core.Tests;

public class UnitRulesTests
{
    // Row 4, its cells (4,1)–(4,9) by index 0–8.
    private readonly UnitRules _unit = new(UnitKind.Row, 4, [.. Enumerable.Range(1, 9).Select(column => (4, column))]);

    [Fact]
    public void A_digit_left_with_one_empty_cell_in_the_unit_is_a_hidden_single_there()
    {
        LoseInAllBut(6, 7, 8);

        var reaction = _unit.CandidateLost(4, 7, 6);

        ReactionAssert.Equal(new Reaction { Deduction = new Command.PlaceDeduction(4, 8, 6) }, reaction);
    }

    [Fact]
    public void A_digit_left_with_no_cell_in_the_unit_is_a_contradiction()
    {
        // A hidden single in (4,8) on the way, whose cell then loses 6 to a racing elimination before placing it.
        LoseInAllBut(6, 7, 8);
        _unit.CandidateLost(4, 7, 6);

        var reaction = _unit.CandidateLost(4, 8, 6);

        ReactionAssert.Equal(new Reaction { Contradiction = new Step.Contradiction.NoCellForDigit(UnitKind.Row, 4, 6) }, reaction);
    }

    [Fact]
    public void A_digit_whose_last_cell_is_already_filled_with_it_is_no_hidden_single()
    {
        ReactionAssert.None(_unit.Filled(4, 8));

        LoseInAllBut(6, 7, 8);
        var reaction = _unit.CandidateLost(4, 7, 6);

        ReactionAssert.None(reaction);
    }

    [Fact]
    public void Hearing_the_same_lost_candidate_twice_changes_nothing()
    {
        LoseInAllBut(6, 7, 8);

        ReactionAssert.None(_unit.CandidateLost(4, 9, 6));
        ReactionAssert.Equal(new Reaction { Deduction = new Command.PlaceDeduction(4, 8, 6) }, _unit.CandidateLost(4, 7, 6));
        ReactionAssert.None(_unit.CandidateLost(4, 7, 6));
    }

    [Fact]
    public void A_cells_lost_candidates_heard_before_its_Filled_lead_to_no_false_conclusion()
    {
        // (4,8) was filled with 6. The unit hears it lost every other digit, then that it was filled.
        foreach (var digit in Enumerable.Range(1, 9).Except([6]))
        {
            ReactionAssert.None(_unit.CandidateLost(4, 8, digit));
        }

        ReactionAssert.None(_unit.Filled(4, 8));

        // Its peers then lose 6, which (4,8) still holds, so the unit neither deduces nor contradicts.
        LoseInAllBut(6, 8);
    }

    [Fact]
    public void Peers_losing_a_digit_heard_before_the_Filled_that_caused_it_deduce_into_the_filled_cell()
    {
        // (4,8) was filled with 6. The unit has heard it lose every other digit, but not that it was filled, so when
        // its peers lose 6 it takes 6 for a hidden single there. The cell ignores the deduction, being already filled.
        foreach (var digit in Enumerable.Range(1, 9).Except([6]))
        {
            _unit.CandidateLost(4, 8, digit);
        }

        LoseInAllBut(6, 7, 8);
        var reaction = _unit.CandidateLost(4, 7, 6);

        ReactionAssert.Equal(new Reaction { Deduction = new Command.PlaceDeduction(4, 8, 6) }, reaction);
    }

    private void LoseInAllBut(int digit, params int[] keptColumns)
    {
        foreach (var column in Enumerable.Range(1, 9).Except(keptColumns))
        {
            ReactionAssert.None(_unit.CandidateLost(4, column, digit));
        }
    }
}
