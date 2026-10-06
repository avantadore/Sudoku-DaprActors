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
    public async Task Replaying_a_game_in_contradiction_back_takes_moves_again()
    {
        var id = await app.Api.CreateGameAsync();
        await app.Api.PlayIntoContradictionAsync(id);

        var back = await app.Api.ReplayAsync(id, 6);

        Assert.Equal("InProgress", back.GetProperty("state").GetString());
        Assert.Equal("InProgress", (await app.Api.PlayAsync(id, 5, 5, 9)).GetProperty("state").GetString());
    }

    [Fact]
    public async Task Replaying_a_game_forward_into_contradiction_contradicts_it_again()
    {
        // A grid in contradiction is rebuilt as a contradicted grid, though not necessarily the same one (ADR 0003).
        var id = await app.Api.CreateGameAsync();
        await app.Api.PlayIntoContradictionAsync(id);
        await app.Api.ReplayAsync(id, 6);

        var forward = await app.Api.ReplayAsync(id, 7);

        Assert.Equal("Contradicted", forward.GetProperty("state").GetString());
    }
}
