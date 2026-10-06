using SudokuDaprActors.Core.Tests;
using static SudokuDaprActors.EndToEnd.Tests.GameApi;
using static SudokuDaprActors.EndToEnd.Tests.StepAssert;

namespace SudokuDaprActors.EndToEnd.Tests;

/// <summary>
/// Cells place their naked singles, and units find hidden singles, through the Dapr actors, and each move is recorded
/// with how many deductions its cascade made (ADR 0005).
/// </summary>
[Collection(nameof(App))]
public class DeductionTests(App app)
{
    [Fact]
    public async Task A_cell_left_with_one_candidate_is_filled_as_a_deduction_counted_by_the_move()
    {
        var id = await app.Api.CreateGameAsync();
        // Row 1 holds 1–7, so (1,8) and (1,9) are left with 8 and 9.
        for (var digit = 1; digit <= 7; digit++)
        {
            await app.Api.PlayAsync(id, 1, digit, digit);
        }

        var grid = await app.Api.PlayAsync(id, 1, 8, 8);

        Assert.Equal((9, "Deduction"), PlacementOf(grid, 1, 9));
        Assert.Equal([9], DigitsOf(CellOf(grid, 1, 9).GetProperty("candidates")));
        Assert.DoesNotContain(9, DigitsOf(CellOf(grid, 5, 9).GetProperty("candidates")));
        Assert.DoesNotContain(9, DigitsOf(CellOf(grid, 2, 7).GetProperty("candidates")));
        Assert.Equal(1, DeductionsOfLastMove(grid));
    }

    [Fact]
    public async Task A_digit_with_one_possible_cell_in_a_row_is_placed_there_as_a_deduction_counted_by_the_move()
    {
        var id = await app.Api.CreateGameAsync();
        await PlayRow1With2To7(id); // (1,7), (1,8) and (1,9) are left with 1, 8 and 9
        await app.Api.PlayAsync(id, 4, 7, 1); // 1 leaves (1,7)

        // 1 leaves (1,8): in row 1, only (1,9) can hold 1, though it can still hold 8 and 9 too.
        var grid = await app.Api.PlayAsync(id, 7, 8, 1);

        Assert.Equal((1, "Deduction"), PlacementOf(grid, 1, 9));
        Assert.DoesNotContain(1, DigitsOf(CellOf(grid, 2, 9).GetProperty("candidates")));
        Assert.Equal(1, DeductionsOfLastMove(grid));
    }

    // Each grid below is built so the hidden single exists in one unit only, and its cell still has other candidates,
    // so the deduction can only come from that unit.
    [Fact]
    public async Task A_digit_with_one_possible_cell_in_a_column_is_placed_there_as_a_deduction()
    {
        var id = await app.Api.CreateGameAsync();
        for (var digit = 2; digit <= 7; digit++)
        {
            await app.Api.PlayAsync(id, digit - 1, 1, digit); // (7,1), (8,1) and (9,1) are left with 1, 8 and 9
        }

        await app.Api.PlayAsync(id, 7, 4, 1); // 1 leaves (7,1)

        var grid = await app.Api.PlayAsync(id, 8, 7, 1); // 1 leaves (8,1): in column 1, only (9,1) can hold 1

        Assert.Equal((1, "Deduction"), PlacementOf(grid, 9, 1));
        Assert.DoesNotContain(1, DigitsOf(CellOf(grid, 9, 2).GetProperty("candidates")));
    }

    [Fact]
    public async Task A_digit_with_one_possible_cell_in_a_box_is_placed_there_as_a_deduction()
    {
        var id = await app.Api.CreateGameAsync();
        // Box 1 holds 2–7, leaving (1,1), (2,2) and (3,3) with 1, 8 and 9.
        await app.Api.PlayAsync(id, 1, 2, 2);
        await app.Api.PlayAsync(id, 1, 3, 3);
        await app.Api.PlayAsync(id, 2, 1, 4);
        await app.Api.PlayAsync(id, 2, 3, 5);
        await app.Api.PlayAsync(id, 3, 1, 6);
        await app.Api.PlayAsync(id, 3, 2, 7);
        await app.Api.PlayAsync(id, 1, 5, 1); // 1 leaves (1,1)

        var grid = await app.Api.PlayAsync(id, 8, 2, 1); // 1 leaves (2,2): in box 1, only (3,3) can hold 1

        Assert.Equal((1, "Deduction"), PlacementOf(grid, 3, 3));
        Assert.DoesNotContain(1, DigitsOf(CellOf(grid, 3, 9).GetProperty("candidates")));
    }

    [Fact]
    public async Task Filling_a_cell_with_another_digit_can_leave_a_hidden_single()
    {
        var id = await app.Api.CreateGameAsync();
        for (var digit = 2; digit <= 6; digit++)
        {
            await app.Api.PlayAsync(id, 1, digit - 1, digit);
        }

        await app.Api.PlayAsync(id, 4, 7, 1); // 1 leaves (1,7)
        await app.Api.PlayAsync(id, 7, 8, 1); // 1 leaves (1,8): in row 1, 1 can go in (1,6) or (1,9)

        var grid = await app.Api.PlayAsync(id, 1, 6, 7); // filling (1,6) with 7 leaves (1,9) as the only cell for 1 in row 1

        Assert.Equal((1, "Deduction"), PlacementOf(grid, 1, 9));
    }

    [Fact]
    public async Task Hidden_and_naked_singles_chain_within_one_cascade()
    {
        var id = await app.Api.CreateGameAsync();
        await PlayRow1With2To7(id);
        await app.Api.PlayAsync(id, 4, 7, 1); // (1,7) is left with 8 and 9
        await app.Api.PlayAsync(id, 5, 8, 9); // (1,8) is left with 1 and 8
        await using var watcher = await StepStream.OpenAsync(app.Api, id);

        // 1 leaves (1,8): it becomes a naked single (8), and (1,9) a hidden single for 1 in row 1.
        // Either of those placements then leaves (1,7) as the only place for 9.
        var grid = await app.Api.PlayAsync(id, 7, 8, 1);

        var steps = await watcher.ReadMoveAsync();
        var forcing = Elimination(1, 8, 1);
        string[] deductions = [Placement(1, 8, 8, "Deduction"), Placement(1, 9, 1, "Deduction"), Placement(1, 7, 9, "Deduction")];
        Assert.Equal(Placement(7, 8, 1, "Move"), NameOf(steps[0]));
        Assert.Equal(deductions.Order(), DeductionsOf(steps).Order());
        Assert.All(deductions, deduction => Before(steps, forcing, deduction));
        var names = NamesOf(steps);
        Assert.True(
            names.IndexOf(deductions[2]) > Math.Min(names.IndexOf(deductions[0]), names.IndexOf(deductions[1])),
            "(1,7) is forced only once (1,8) or (1,9) is filled.");
        Assert.Equal(3, DeductionsOfLastMove(grid));
    }

    [Fact]
    public async Task Deductions_force_further_deductions_until_the_givens_of_a_puzzle_solve_it()
    {
        var id = await app.Api.CreateGameAsync();

        var grid = (await app.Api.PlayGivensAsync(id))[^1];

        Assert.Equal("Solved", grid.GetProperty("state").GetString());
        foreach (var (row, column, digit) in Puzzle.Solution)
        {
            var (placed, source) = PlacementOf(grid, row, column);
            Assert.Equal(digit, placed);
            if (!Puzzle.IsGiven(row, column))
            {
                Assert.Equal("Deduction", source);
            }
        }

        // Every cell was filled by a recorded move or by a deduction one of them counted.
        var moves = MovesOf(grid);
        Assert.Equal(81, moves.Count + moves.Sum(move => move.GetProperty("deductions").GetInt32()));
    }

    private async Task PlayRow1With2To7(string id)
    {
        for (var digit = 2; digit <= 7; digit++)
        {
            await app.Api.PlayAsync(id, 1, digit - 1, digit);
        }
    }
}
