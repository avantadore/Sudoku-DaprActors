using System.Text.Json;
using static SudokuDaprActors.EndToEnd.Tests.StepStream;

namespace SudokuDaprActors.EndToEnd.Tests;

/// <summary>
/// Cells and units publish their steps, and the Api hands those of each game's current grid to its watchers before
/// the move completes (ADR 0005). Within a cascade only cause before effect is promised.
/// </summary>
[Collection(nameof(App))]
public class StepsTests(App app)
{
    [Fact]
    public async Task A_watcher_reads_the_move_and_then_its_cascade_cause_before_effect()
    {
        var id = await app.Api.CreateGameAsync();
        // Row 1 holds 1–7, so (1,8) and (1,9) are left with 8 and 9.
        for (var digit = 1; digit <= 7; digit++)
        {
            await app.Api.PlayAsync(id, 1, digit, digit);
        }

        await using var steps = await OpenAsync(app.Api, id);

        await app.Api.PlayAsync(id, 1, 8, 8);

        // 8 in (1,8) leaves (1,9) with only 9, a deduction, which (2,9) then loses.
        var read = await steps.ReadMoveAsync();
        Assert.True(IsPlacement(read[0], 1, 8, 8, "Move"), $"The move came first, not {read[0]}.");
        Before(read, step => IsElimination(step, 1, 9, 8), step => IsPlacement(step, 1, 9, 9, "Deduction"));
        Before(read, step => IsPlacement(step, 1, 9, 9, "Deduction"), step => IsElimination(step, 2, 9, 9));
        Assert.Contains(read, step => IsElimination(step, 9, 8, 8));
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
        Assert.Equal(InSomeFixedOrder(firstSteps), InSomeFixedOrder(secondSteps));
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
    public async Task Rejected_and_unchanged_moves_send_no_steps()
    {
        var id = await app.Api.CreateGameAsync();
        await app.Api.PlayAsync(id, 3, 3, 3);
        await using var steps = await OpenAsync(app.Api, id);

        await app.Api.MoveAsync(id, 3, 3, 3);
        await app.Api.MoveAsync(id, 3, 4, 3);

        Assert.False(await steps.ReadsAnythingWithinAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Watching_an_unknown_game_returns_404()
    {
        var response = await app.Api.GetAsync($"/games/{Guid.NewGuid()}/steps", TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    private static void Before(List<JsonElement> steps, Func<JsonElement, bool> cause, Func<JsonElement, bool> effect)
    {
        var causeAt = steps.FindIndex(step => cause(step));
        var effectAt = steps.FindIndex(step => effect(step));
        Assert.True(causeAt >= 0, "The cause was not read.");
        Assert.True(effectAt >= 0, "The effect was not read.");
        Assert.True(causeAt < effectAt, $"The cause, read at {causeAt}, should come before the effect, read at {effectAt}.");
    }

    private static List<string> InSomeFixedOrder(IEnumerable<JsonElement> steps) =>
        [.. steps.Select(step => step.GetRawText()).Order(StringComparer.Ordinal)];
}
