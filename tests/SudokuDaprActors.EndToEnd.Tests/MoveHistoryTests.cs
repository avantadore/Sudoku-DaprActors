using static SudokuDaprActors.EndToEnd.Tests.GameApi;
using static SudokuDaprActors.EndToEnd.Tests.StepAssert;

namespace SudokuDaprActors.EndToEnd.Tests;

/// <summary>
/// A game records each accepted move, with how many deductions its cascade made, and every grid the Api answers
/// carries the position and the whole move history (ADR 0002).
/// </summary>
[Collection(nameof(App))]
public class MoveHistoryTests(App app)
{
    [Fact]
    public async Task Accepted_moves_are_recorded_in_order_with_how_many_deductions_their_cascade_made()
    {
        var id = await app.Api.CreateGameAsync();
        for (var digit = 1; digit <= 7; digit++)
        {
            await app.Api.PlayAsync(id, 1, digit, digit);
        }

        // Leaves (1,9) with only 9, which the game deduces.
        var moved = await app.Api.PlayAsync(id, 1, 8, 8);
        var read = await app.Api.GetGameAsync(id);

        foreach (var grid in new[] { moved, read })
        {
            Assert.Equal(8, PositionOf(grid));
            Assert.Equal(
                """
                [{"row":1,"column":1,"digit":1,"deductions":0},{"row":1,"column":2,"digit":2,"deductions":0},{"row":1,"column":3,"digit":3,"deductions":0},{"row":1,"column":4,"digit":4,"deductions":0},{"row":1,"column":5,"digit":5,"deductions":0},{"row":1,"column":6,"digit":6,"deductions":0},{"row":1,"column":7,"digit":7,"deductions":0},{"row":1,"column":8,"digit":8,"deductions":1}]
                """,
                MoveHistoryOf(grid));
        }
    }

    [Fact]
    public async Task Rejected_and_unchanged_moves_are_not_recorded()
    {
        var id = await app.Api.CreateGameAsync();
        await app.Api.PlayAsync(id, 1, 1, 5);

        await app.Api.MoveAsync(id, 1, 1, 5); // unchanged
        await app.Api.MoveAsync(id, 1, 1, 6); // already filled
        await app.Api.MoveAsync(id, 1, 2, 5); // not a candidate

        var grid = await app.Api.GetGameAsync(id);
        Assert.Equal("""[{"row":1,"column":1,"digit":5,"deductions":0}]""", MoveHistoryOf(grid));
        Assert.Equal(1, PositionOf(grid));
    }

    [Fact]
    public async Task A_move_into_contradiction_is_recorded_with_the_deductions_placed_before_its_cascade_stopped()
    {
        var id = await app.Api.CreateGameAsync();
        await app.Api.PlayRows1And9With1To7Async(id);
        await using var steps = await StepStream.OpenAsync(app.Api, id);

        // 9 in column 9 leaves (1,9) and (9,9) with only 8, so 8 is deduced in one of them, which leaves the other
        // with no candidates. How much else is deduced before the contradiction stops the cascade can vary.
        var grid = await app.Api.PlayAsync(id, 5, 9, 9);

        var deductions = DeductionsOf(await steps.ReadMoveAsync()).Count();
        Assert.Equal("Contradicted", StateOf(grid));
        Assert.InRange(deductions, 1, 81);
        Assert.Equal($$"""{"row":5,"column":9,"digit":9,"deductions":{{deductions}}}""", MovesOf(grid)[^1].GetRawText());
        Assert.Equal(15, PositionOf(grid));
    }
}
