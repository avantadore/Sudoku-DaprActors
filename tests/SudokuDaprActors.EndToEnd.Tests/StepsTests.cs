using static SudokuDaprActors.EndToEnd.Tests.StepAssert;
using static SudokuDaprActors.EndToEnd.Tests.StepStream;

namespace SudokuDaprActors.EndToEnd.Tests;

/// <summary>
/// Cells and units publish their steps, and the Api hands those of each game's current grid to its watchers before
/// the move completes (ADR 0005). Within a cascade only cause before effect is promised: a move's placement comes
/// first, each elimination comes after the placement that caused it, and each deduction comes after the step that
/// forced it. Everything else is compared as a set (ADR 0003).
/// </summary>
[Collection(nameof(App))]
public class StepsTests(App app)
{
    [Fact]
    public async Task A_move_sends_the_move_first_then_an_elimination_for_each_peer()
    {
        var id = await app.Api.CreateGameAsync();
        await using var steps = await OpenAsync(app.Api, id);

        await app.Api.PlayAsync(id, 1, 1, 5);

        var read = await steps.ReadMoveAsync();
        Assert.Equal(Placement(1, 1, 5, "Move"), NameOf(read[0]));
        SameSet(
            [
                Elimination(1, 2, 5), Elimination(1, 3, 5), Elimination(1, 4, 5), Elimination(1, 5, 5),
                Elimination(1, 6, 5), Elimination(1, 7, 5), Elimination(1, 8, 5), Elimination(1, 9, 5),
                Elimination(2, 1, 5), Elimination(2, 2, 5), Elimination(2, 3, 5),
                Elimination(3, 1, 5), Elimination(3, 2, 5), Elimination(3, 3, 5),
                Elimination(4, 1, 5), Elimination(5, 1, 5), Elimination(6, 1, 5),
                Elimination(7, 1, 5), Elimination(8, 1, 5), Elimination(9, 1, 5),
            ],
            read.Skip(1));
    }

    [Fact]
    public async Task A_move_sends_eliminations_only_for_peers_that_still_had_the_digit()
    {
        var id = await app.Api.CreateGameAsync();
        await app.Api.PlayAsync(id, 1, 1, 5);
        await using var steps = await OpenAsync(app.Api, id);

        await app.Api.PlayAsync(id, 2, 4, 5);

        // (2,1), (2,2) and (2,3) already lost 5 to (1,1), the box of (2,4) has lost row 1, and (1,4) lost it too.
        var read = await steps.ReadMoveAsync();
        Assert.Equal(Placement(2, 4, 5, "Move"), NameOf(read[0]));
        SameSet(
            [
                Elimination(2, 5, 5), Elimination(2, 6, 5), Elimination(2, 7, 5), Elimination(2, 8, 5), Elimination(2, 9, 5),
                Elimination(3, 4, 5), Elimination(3, 5, 5), Elimination(3, 6, 5),
                Elimination(4, 4, 5), Elimination(5, 4, 5), Elimination(6, 4, 5),
                Elimination(7, 4, 5), Elimination(8, 4, 5), Elimination(9, 4, 5),
            ],
            read.Skip(1));
    }

    [Fact]
    public async Task A_cascade_sends_each_deduction_after_the_elimination_that_forced_it_and_before_its_own_eliminations()
    {
        var id = await app.Api.CreateGameAsync();
        // Row 1 holds 1–7, so (1,8) and (1,9) are left with 8 and 9.
        for (var digit = 1; digit <= 7; digit++)
        {
            await app.Api.PlayAsync(id, 1, digit, digit);
        }

        await using var steps = await OpenAsync(app.Api, id);

        await app.Api.PlayAsync(id, 1, 8, 8);

        var read = await steps.ReadMoveAsync();
        var move = Placement(1, 8, 8, "Move");
        var forcing = Elimination(1, 9, 8); // leaves (1,9) with only 9
        var deduction = Placement(1, 9, 9, "Deduction");
        string[] eliminationsOf8 =
        [
            forcing,
            Elimination(2, 7, 8), Elimination(2, 8, 8), Elimination(2, 9, 8),
            Elimination(3, 7, 8), Elimination(3, 8, 8), Elimination(3, 9, 8),
            Elimination(4, 8, 8), Elimination(5, 8, 8), Elimination(6, 8, 8),
            Elimination(7, 8, 8), Elimination(8, 8, 8), Elimination(9, 8, 8),
        ];
        string[] eliminationsOf9 =
        [
            Elimination(2, 7, 9), Elimination(2, 8, 9), Elimination(2, 9, 9),
            Elimination(3, 7, 9), Elimination(3, 8, 9), Elimination(3, 9, 9),
            Elimination(4, 9, 9), Elimination(5, 9, 9), Elimination(6, 9, 9),
            Elimination(7, 9, 9), Elimination(8, 9, 9), Elimination(9, 9, 9),
        ];
        Assert.Equal(move, NameOf(read[0]));
        SameSet([move, .. eliminationsOf8, deduction, .. eliminationsOf9], read);
        Before(read, forcing, deduction);
        Assert.All(eliminationsOf9, elimination => Before(read, deduction, elimination));
    }

    [Fact]
    public async Task Deductions_forced_by_the_same_elimination_each_come_after_it()
    {
        var id = await app.Api.CreateGameAsync();
        for (var digit = 1; digit <= 6; digit++)
        {
            await app.Api.PlayAsync(id, 1, digit, digit);
        }

        await app.Api.PlayAsync(id, 7, 8, 9); // (1,8) is left with 7 and 8
        var grid = await app.Api.PlayAsync(id, 4, 9, 9); // (1,9) is left with 7 and 8, so 9 is a hidden single at (1,7)
        Assert.Equal((9, "Deduction"), GameApi.PlacementOf(grid, 1, 7));
        await using var steps = await OpenAsync(app.Api, id);

        // Eliminating 7 from (1,9) forces two deductions: (1,9) is a naked single for 8, and (1,8) is the only cell
        // left for 7 in row 1.
        await app.Api.PlayAsync(id, 9, 9, 7);

        var read = await steps.ReadMoveAsync();
        var forcing = Elimination(1, 9, 7);
        string[] deductions = [Placement(1, 9, 8, "Deduction"), Placement(1, 8, 7, "Deduction")];
        Assert.Equal(Placement(9, 9, 7, "Move"), NameOf(read[0]));
        Assert.Equal(deductions.Order(), DeductionsOf(read).Order());
        Assert.All(deductions, deduction => Before(read, forcing, deduction));
    }

    [Fact]
    public async Task Every_watcher_has_every_step_of_a_move_once_it_completes()
    {
        var id = await app.Api.CreateGameAsync();
        await using var first = await OpenAsync(app.Api, id);
        await using var second = await OpenAsync(app.Api, id);

        await app.Api.PlayAsync(id, 5, 5, 4);

        var firstSteps = await first.ReadMoveAsync();
        var secondSteps = await second.ReadMoveAsync();
        Assert.Equal(21, firstSteps.Count); // the placement and the 20 peers' eliminations
        SameSet(NamesOf(firstSteps), secondSteps);
    }

    [Fact]
    public async Task A_watcher_that_starts_after_a_move_reads_only_later_steps()
    {
        var id = await app.Api.CreateGameAsync();
        await app.Api.PlayAsync(id, 1, 1, 5);

        await using var steps = await OpenAsync(app.Api, id);
        await app.Api.PlayAsync(id, 9, 9, 1);

        var read = await steps.ReadMoveAsync();
        Assert.True(IsPlacement(read[0], 9, 9, 1, "Move"), $"The move came first, not {read[0]}.");
        Assert.DoesNotContain(read, step => step.GetProperty("digit").GetInt32() == 5);
    }

    [Fact]
    public async Task Steps_of_a_grid_rebuilt_by_replay_never_reach_a_watcher()
    {
        var id = await app.Api.CreateGameAsync();
        await app.Api.PlayAsync(id, 1, 1, 5);
        await app.Api.PlayAsync(id, 2, 4, 5);
        await using var steps = await OpenAsync(app.Api, id);

        await app.Api.ReplayAsync(id, 0);
        await app.Api.ReplayAsync(id, 2);
        await app.Api.PlayAsync(id, 9, 9, 1);

        var read = await steps.ReadMoveAsync();
        Assert.True(IsPlacement(read[0], 9, 9, 1, "Move"), $"The move came first, not {read[0]}.");
        Assert.DoesNotContain(read, step => step.GetProperty("digit").GetInt32() == 5);
    }

    [Fact]
    public async Task A_contradiction_is_a_step_and_the_stream_goes_on_after_it()
    {
        var id = await app.Api.CreateGameAsync();
        // Row 1 holds 1–6, so 9 can only go in (1,7), (1,8) or (1,9), all in the top-right box.
        for (var digit = 1; digit <= 6; digit++)
        {
            await app.Api.PlayAsync(id, 1, digit, digit);
        }

        await using var steps = await OpenAsync(app.Api, id);

        await app.Api.PlayAsync(id, 2, 7, 9);
        var read = await steps.ReadMoveAsync();
        Assert.Single(read, IsContradiction);

        await app.Api.ReplayAsync(id, 6);
        await app.Api.PlayAsync(id, 9, 9, 9);
        Assert.True(IsPlacement((await steps.ReadMoveAsync())[0], 9, 9, 9, "Move"));
    }

    [Fact]
    public async Task A_watcher_reads_the_end_of_each_accepted_moves_steps_after_them_and_none_for_other_moves()
    {
        var id = await app.Api.CreateGameAsync();
        await using var steps = await OpenAsync(app.Api, id);

        await app.Api.PlayAsync(id, 1, 1, 5);
        await app.Api.MoveAsync(id, 1, 1, 5); // unchanged
        await app.Api.MoveAsync(id, 1, 2, 5); // rejected
        await app.Api.PlayAsync(id, 9, 9, 1);

        // Each accepted move: its placement and its 20 peers' eliminations, then the end of its steps.
        var firstMove = await steps.ReadMoveAsync();
        var secondMove = await steps.ReadMoveAsync();
        Assert.Equal((21, Placement(1, 1, 5, "Move")), (firstMove.Count, NameOf(firstMove[0])));
        Assert.Equal((21, Placement(9, 9, 1, "Move")), (secondMove.Count, NameOf(secondMove[0])));
        Assert.False(await steps.ReadsAnythingWithinAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Rejected_and_unchanged_moves_send_no_steps()
    {
        var id = await app.Api.CreateGameAsync();
        await app.Api.PlayAsync(id, 3, 3, 3);
        await using var steps = await OpenAsync(app.Api, id);

        await app.Api.MoveAsync(id, 3, 3, 3);
        await app.Api.MoveAsync(id, 3, 3, 6);
        await app.Api.MoveAsync(id, 3, 4, 3);

        Assert.False(await steps.ReadsAnythingWithinAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Watching_an_unknown_game_returns_404()
    {
        var response = await app.Api.GetAsync($"/games/{Guid.NewGuid()}/steps", TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }
}
