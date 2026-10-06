using SudokuDaprActors.Core.Tests;
using static SudokuDaprActors.EndToEnd.Tests.GameApi;

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
        Assert.DoesNotContain(9, DigitsOf(CellOf(grid, 5, 9).GetProperty("candidates")));
        Assert.Equal(1, DeductionsOfLastMove(grid));
    }

    [Fact]
    public async Task A_digit_with_one_possible_cell_in_a_unit_is_placed_there_as_a_deduction_counted_by_the_move()
    {
        var id = await app.Api.CreateGameAsync();
        // Row 1 holds 2–7, so (1,7), (1,8) and (1,9) are left with 1, 8 and 9, and (1,7) then loses 1.
        for (var digit = 2; digit <= 7; digit++)
        {
            await app.Api.PlayAsync(id, 1, digit - 1, digit);
        }

        await app.Api.PlayAsync(id, 4, 7, 1);

        // 1 leaves (1,8): in row 1, only (1,9) can hold 1, though it can still hold 8 and 9 too.
        var grid = await app.Api.PlayAsync(id, 7, 8, 1);

        Assert.Equal((1, "Deduction"), PlacementOf(grid, 1, 9));
        Assert.DoesNotContain(1, DigitsOf(CellOf(grid, 2, 9).GetProperty("candidates")));
        Assert.Equal(1, DeductionsOfLastMove(grid));
    }

    [Fact]
    public async Task Deductions_force_further_deductions_until_the_givens_of_a_puzzle_solve_it()
    {
        var id = await app.Api.CreateGameAsync();
        var grid = default(System.Text.Json.JsonElement);
        foreach (var (row, column, digit) in Puzzle.Givens)
        {
            // A given may already have been deduced from earlier givens, which leaves the move unchanged.
            grid = await app.Api.PlayAsync(id, row, column, digit);
        }

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
}
