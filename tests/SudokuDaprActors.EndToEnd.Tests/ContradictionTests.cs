using System.Net;
using System.Text.Json;
using static SudokuDaprActors.EndToEnd.Tests.GameApi;
using static SudokuDaprActors.EndToEnd.Tests.StepStream;

namespace SudokuDaprActors.EndToEnd.Tests;

/// <summary>
/// The grid's actor owns the contradiction flag: a cascade that reaches a contradiction puts the game in it, and no
/// deduction is placed or counted after that (ADR 0005). A cascade into contradiction is promised to reach a
/// contradiction, not which one (ADR 0003).
/// </summary>
[Collection(nameof(App))]
public class ContradictionTests(App app)
{
    private const string InContradiction =
        "The game is in contradiction, so no more moves can be made. Replay to an earlier position to continue, or start a new game.";

    [Fact]
    public async Task A_digit_left_with_no_cell_in_a_unit_puts_the_game_in_contradiction_after_applying_the_move_and_rejects_further_moves()
    {
        var id = await app.Api.CreateGameAsync();
        // Row 1 holds 1–6, so 9 can only go in (1,7), (1,8) or (1,9), all in the top-right box.
        for (var digit = 1; digit <= 6; digit++)
        {
            await app.Api.PlayAsync(id, 1, digit, digit);
        }

        await using var steps = await OpenAsync(app.Api, id);

        // A legal move, but now 9 has no place in row 1. (1,7), (1,8) and (1,9) keep 7 and 8, so nothing is deduced.
        var grid = await app.Api.PlayAsync(id, 2, 7, 9);

        Assert.Equal((9, "Move"), PlacementOf(grid, 2, 7));
        Assert.Equal("Contradicted", StateOf(grid));
        AssertNoCellForDigitUnlessACellRanOut(
            Assert.Single(await steps.ReadMoveAsync(), IsContradiction), "Row", 1, 9,
            ranOutIn: (row, column) => row <= 3 && column >= 7);
        await AssertRejected(await app.Api.MoveAsync(id, 5, 5, 5), InContradiction);
        await AssertRejected(await app.Api.MoveAsync(id, 2, 7, 9), InContradiction);
        Assert.Equal(7, MovesOf(await app.Api.GetGameAsync(id)).Count);
    }

    [Fact]
    public async Task A_digit_left_with_no_cell_in_a_box_names_the_box_by_its_number_row_by_row()
    {
        var id = await app.Api.CreateGameAsync();
        // Box 4 (rows 4–6, columns 1–3) holds 1–6 in columns 2 and 3, so (4,1), (5,1) and (6,1) are left with
        // 7, 8 and 9.
        for (var row = 4; row <= 6; row++)
        {
            await app.Api.PlayAsync(id, row, 2, row - 3);
            await app.Api.PlayAsync(id, row, 3, row);
        }

        await using var steps = await OpenAsync(app.Api, id);

        // 8 in column 1, above box 4, leaves box 4 no cell for 8. (4,1), (5,1) and (6,1) keep 7 and 9, so nothing is
        // deduced, and columns 2 and 3 still have room for 8 below box 4.
        var grid = await app.Api.PlayAsync(id, 2, 1, 8);

        Assert.Equal("Contradicted", StateOf(grid));
        AssertNoCellForDigitUnlessACellRanOut(
            Assert.Single(await steps.ReadMoveAsync(), IsContradiction), "Box", 4, 8,
            ranOutIn: (_, column) => column == 1);
    }

    [Fact]
    public async Task A_cell_left_with_no_candidates_puts_the_game_in_contradiction_after_applying_the_move()
    {
        var id = await app.Api.CreateGameAsync();
        await app.Api.PlayRows1And9With1To7Async(id);
        await using var steps = await OpenAsync(app.Api, id);

        // 9 in column 9 leaves (1,9) and (9,9) both with only 8, which column 9 cannot hold twice.
        var grid = await app.Api.PlayAsync(id, 5, 9, 9);

        Assert.Equal((9, "Move"), PlacementOf(grid, 5, 9));
        Assert.Equal("Contradicted", StateOf(grid));
        Assert.Single(await steps.ReadMoveAsync(), IsContradiction);
    }

    [Fact]
    public async Task A_cascade_into_contradiction_counts_exactly_the_deductions_it_placed()
    {
        var id = await app.Api.CreateGameAsync();
        await app.Api.PlayRows1And9With1To7Async(id);

        // 9 in column 9 leaves (1,9) and (9,9) with only 8, so 8 is deduced in one of them, or both, which leaves a
        // cell with no candidates. How much else is deduced before the contradiction stops the cascade can vary.
        var grid = await app.Api.PlayAsync(id, 5, 9, 9);

        Assert.Equal("Contradicted", StateOf(grid));
        Assert.InRange(DeductionsOfLastMove(grid), 1, 81);
        Assert.Equal(DeducedCells(grid), MovesOf(grid).Sum(move => move.GetProperty("deductions").GetInt32()));
        await AssertRejected(await app.Api.MoveAsync(id, 5, 5, 5), InContradiction);
    }

    [Fact]
    public async Task Every_move_in_a_contradicted_game_is_rejected_and_the_grid_stays_readable()
    {
        var id = await app.Api.CreateGameAsync();
        var contradicted = await app.Api.PlayIntoContradictionAsync(id);
        await using var steps = await OpenAsync(app.Api, id);

        await AssertRejected(await app.Api.MoveAsync(id, 5, 5, 5), InContradiction);
        await AssertRejected(await app.Api.MoveAsync(id, 2, 7, 9), InContradiction);

        Assert.Equal(contradicted.GetRawText(), (await app.Api.GetGameAsync(id)).GetRawText());
        Assert.Equal(HttpStatusCode.OK, (await app.Api.GetCandidatesAsync(id, 5, 5)).StatusCode);
        Assert.False(await steps.ReadsAnythingWithinAsync(TimeSpan.FromSeconds(2)));
    }

    /// <summary>
    /// A unit left with one cell for a digit deduces it there. Usually that cell has already lost the digit and
    /// refuses, and the unit then finds no cell for it. But the elimination can still be on its way, and then the
    /// deduction is placed and a cell runs out of candidates instead: which contradiction wins can vary (ADR 0003).
    /// </summary>
    private static void AssertNoCellForDigitUnlessACellRanOut(
        JsonElement contradiction, string unit, int unitNumber, int digit, Func<int, int, bool> ranOutIn)
    {
        if (contradiction.GetProperty("kind").GetString() == "NoCandidateForCell")
        {
            var (row, column) = (contradiction.GetProperty("row").GetInt32(), contradiction.GetProperty("column").GetInt32());
            Assert.True(ranOutIn(row, column), $"({row},{column}) is not where a raced deduction could leave a cell.");
        }
        else
        {
            Assert.Equal(
                ("NoCellForDigit", unit, unitNumber, digit),
                (contradiction.GetProperty("kind").GetString(), contradiction.GetProperty("unit").GetString(),
                    contradiction.GetProperty("unitNumber").GetInt32(), contradiction.GetProperty("digit").GetInt32()));
        }
    }
}
