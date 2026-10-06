using static SudokuDaprActors.EndToEnd.Tests.GameApi;

namespace SudokuDaprActors.EndToEnd.Tests;

/// <summary>
/// The grid's actor owns the contradiction flag: a cascade that reaches a contradiction puts the game in it, and no
/// deduction is placed or counted after that (ADR 0005).
/// </summary>
[Collection(nameof(App))]
public class ContradictionTests(App app)
{
    private const string InContradiction =
        "The game is in contradiction, so no more moves can be made. Replay to an earlier position to continue, or start a new game.";

    [Fact]
    public async Task A_digit_left_with_no_cell_in_a_unit_puts_the_game_in_contradiction_and_rejects_further_moves()
    {
        var id = await app.Api.CreateGameAsync();
        // Row 1 holds 1–6, so 9 can only go in (1,7), (1,8) or (1,9), all in the top-right box.
        for (var digit = 1; digit <= 6; digit++)
        {
            await app.Api.PlayAsync(id, 1, digit, digit);
        }

        // A legal move, but now 9 has no place in row 1.
        var grid = await app.Api.PlayAsync(id, 2, 7, 9);

        Assert.Equal("Contradicted", grid.GetProperty("state").GetString());
        await AssertRejected(await app.Api.MoveAsync(id, 5, 5, 5), InContradiction);
        await AssertRejected(await app.Api.MoveAsync(id, 2, 7, 9), InContradiction);
        Assert.Equal(7, MovesOf(await app.Api.GetGameAsync(id)).Count);
    }

    [Fact]
    public async Task A_cascade_into_contradiction_counts_exactly_the_deductions_it_placed()
    {
        var id = await app.Api.CreateGameAsync();
        // Rows 1 and 9 hold 1–7 in columns 1–7, so (1,8), (1,9), (9,8) and (9,9) are left with 8 and 9.
        for (var column = 1; column <= 7; column++)
        {
            await app.Api.PlayAsync(id, 1, column, column);
            await app.Api.PlayAsync(id, 9, column, column % 7 + 1);
        }

        // 9 in column 9 leaves (1,9) and (9,9) with only 8, so 8 is deduced in one of them, or both, which leaves a
        // cell with no candidates. How much else is deduced before the contradiction stops the cascade can vary.
        var grid = await app.Api.PlayAsync(id, 5, 9, 9);

        Assert.Equal("Contradicted", grid.GetProperty("state").GetString());
        Assert.InRange(DeductionsOfLastMove(grid), 1, 81);
        Assert.Equal(DeducedCells(grid), MovesOf(grid).Sum(move => move.GetProperty("deductions").GetInt32()));
        await AssertRejected(await app.Api.MoveAsync(id, 5, 5, 5), InContradiction);
    }
}
