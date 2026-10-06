using System.Net;
using System.Text.Json;
using static SudokuDaprActors.EndToEnd.Tests.GameApi;

namespace SudokuDaprActors.EndToEnd.Tests;

/// <summary>A replay builds a fresh grid from the move history up to the chosen position (ADR 0005).</summary>
[Collection(nameof(App))]
public class ReplayTests(App app)
{
    [Fact]
    public async Task Replaying_back_and_forward_rebuilds_the_grid_at_each_position_and_keeps_every_move()
    {
        var id = await app.Api.CreateGameAsync();
        var afterFirst = await app.Api.PlayAsync(id, 1, 1, 5);
        await app.Api.PlayAsync(id, 2, 4, 5);
        var afterThird = await app.Api.PlayAsync(id, 5, 5, 7);

        var back = await app.Api.ReplayAsync(id, 1);

        Assert.Equal(1, back.GetProperty("position").GetInt32());
        Assert.Equal(CellsOf(afterFirst), CellsOf(back));
        Assert.Equal(3, MovesOf(back).Count);

        var start = await app.Api.ReplayAsync(id, 0);

        Assert.Equal(0, start.GetProperty("position").GetInt32());
        Assert.All(start.GetProperty("cells").EnumerateArray(), cell => Assert.Equal(JsonValueKind.Null, cell.GetProperty("digit").ValueKind));
        Assert.Equal(3, MovesOf(start).Count);

        var forward = await app.Api.ReplayAsync(id, 3);

        Assert.Equal(3, forward.GetProperty("position").GetInt32());
        Assert.Equal(CellsOf(afterThird), CellsOf(forward));
    }

    [Fact]
    public async Task A_move_after_replaying_back_discards_the_later_moves()
    {
        var id = await app.Api.CreateGameAsync();
        await app.Api.PlayAsync(id, 1, 1, 5);
        await app.Api.PlayAsync(id, 2, 4, 5);
        await app.Api.PlayAsync(id, 5, 5, 7);
        await app.Api.ReplayAsync(id, 1);

        // (2,5) lost 5 to (2,4) before the replay, and has it back after.
        var grid = await app.Api.PlayAsync(id, 2, 5, 5);

        Assert.Equal(2, grid.GetProperty("position").GetInt32());
        Assert.Equal([(1, 1, 5), (2, 5, 5)], MovesOf(grid).Select(move =>
            (move.GetProperty("row").GetInt32(), move.GetProperty("column").GetInt32(), move.GetProperty("digit").GetInt32())));
        Assert.Equal((null, null), PlacementOf(grid, 2, 4));
        Assert.Equal((null, null), PlacementOf(grid, 5, 5));
    }

    [Fact]
    public async Task Placing_exactly_the_next_recorded_move_is_a_new_move_that_discards_the_ones_after_it()
    {
        var id = await app.Api.CreateGameAsync();
        await app.Api.PlayAsync(id, 1, 1, 5);
        await app.Api.PlayAsync(id, 2, 4, 5);
        await app.Api.PlayAsync(id, 5, 5, 7);
        await app.Api.ReplayAsync(id, 1);

        var grid = await app.Api.PlayAsync(id, 2, 4, 5);

        Assert.Equal(2, PositionOf(grid));
        Assert.Equal("""[{"row":1,"column":1,"digit":5,"deductions":0},{"row":2,"column":4,"digit":5,"deductions":0}]""", MoveHistoryOf(grid));
    }

    [Fact]
    public async Task An_unchanged_or_rejected_move_at_an_earlier_position_keeps_the_later_moves()
    {
        var id = await app.Api.CreateGameAsync();
        await app.Api.PlayAsync(id, 1, 1, 5);
        await app.Api.PlayAsync(id, 2, 4, 5);
        var moves = MoveHistoryOf(await app.Api.PlayAsync(id, 5, 5, 7));
        await app.Api.ReplayAsync(id, 1);

        var unchanged = await app.Api.MoveAsync(id, 1, 1, 5);
        var rejected = await app.Api.MoveAsync(id, 1, 2, 5); // 5 is no longer a candidate in row 1

        Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        var grid = await app.Api.GetGameAsync(id);
        Assert.Equal(1, PositionOf(grid));
        Assert.Equal(moves, MoveHistoryOf(grid));
    }

    [Fact]
    public async Task Replaying_to_the_last_move_gives_the_same_grid_as_the_live_game()
    {
        var id = await app.Api.CreateGameAsync();
        var live = (await app.Api.PlayGivensAsync(id, 20))[^1];

        var replayed = await app.Api.ReplayAsync(id, MovesOf(live).Count);

        Assert.Equal(live.GetRawText(), replayed.GetRawText());
    }

    [Fact]
    public async Task Setting_the_same_position_twice_gives_the_same_grid()
    {
        var id = await app.Api.CreateGameAsync();
        await app.Api.PlayAsync(id, 1, 1, 5);
        await app.Api.PlayAsync(id, 2, 4, 5);

        var first = await app.Api.ReplayAsync(id, 1);
        var second = await app.Api.ReplayAsync(id, 1);

        Assert.Equal(first.GetRawText(), second.GetRawText());
    }

    [Fact]
    public async Task Two_fresh_games_given_the_same_moves_reach_the_same_grid()
    {
        var first = await app.Api.CreateGameAsync();
        var second = await app.Api.CreateGameAsync();

        var firstGrid = (await app.Api.PlayGivensAsync(first, 20))[^1];
        var secondGrid = (await app.Api.PlayGivensAsync(second, 20))[^1];

        Assert.Equal(CellsOf(firstGrid), CellsOf(secondGrid));
        Assert.Equal(MoveHistoryOf(firstGrid), MoveHistoryOf(secondGrid));
    }

    [Fact]
    public async Task Replaying_a_game_in_contradiction_back_takes_moves_again()
    {
        var id = await app.Api.CreateGameAsync();
        await app.Api.PlayIntoContradictionAsync(id);

        var back = await app.Api.ReplayAsync(id, 6);

        Assert.Equal("InProgress", StateOf(back));
        Assert.Equal("InProgress", StateOf(await app.Api.PlayAsync(id, 5, 5, 9)));
    }

    [Fact]
    public async Task Replaying_a_game_forward_into_contradiction_contradicts_it_again()
    {
        // A grid in contradiction is rebuilt as a contradicted grid, though not necessarily the same one (ADR 0003).
        var id = await app.Api.CreateGameAsync();
        await app.Api.PlayIntoContradictionAsync(id);
        await app.Api.ReplayAsync(id, 6);

        var forward = await app.Api.ReplayAsync(id, 7);

        Assert.Equal("Contradicted", StateOf(forward));
    }
}
