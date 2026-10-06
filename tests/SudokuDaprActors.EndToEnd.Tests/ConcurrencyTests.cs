using System.Net;
using static SudokuDaprActors.EndToEnd.Tests.GameApi;

namespace SudokuDaprActors.EndToEnd.Tests;

/// <summary>A game makes one move at a time: a move that arrives while another is running waits its turn.</summary>
[Collection(nameof(App))]
public class ConcurrencyTests(App app)
{
    [Fact]
    public async Task A_move_made_during_another_moves_cascade_waits_its_turn_and_then_sees_it()
    {
        var id = await app.Api.CreateGameAsync();
        var solved = (await app.Api.PlayGivensAsync(id))[^1];

        // Go back to just before the move with the longest cascade, so it is still running when the next move comes.
        var moves = MovesOf(solved);
        var position = Enumerable.Range(0, moves.Count).MaxBy(index => moves[index].GetProperty("deductions").GetInt32());
        var longest = moves[position];
        await app.Api.ReplayAsync(id, position);
        var (row, column, digit) =
            (longest.GetProperty("row").GetInt32(), longest.GetProperty("column").GetInt32(), longest.GetProperty("digit").GetInt32());

        var first = app.Api.MoveAsync(id, row, column, digit);
        var second = app.Api.MoveAsync(id, row, column, digit);
        var responses = await Task.WhenAll(first, second);

        // Whichever went first was accepted, and the other found its placement, which left it unchanged.
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var grids = await Task.WhenAll(responses.Select(ReadJsonAsync));
        Assert.Equal(grids[0].GetRawText(), grids[1].GetRawText());
        Assert.Equal(position + 1, PositionOf(grids[0]));
        Assert.Equal(longest.GetRawText(), MovesOf(grids[0])[^1].GetRawText());
    }

    [Fact]
    public async Task Moves_made_at_the_same_time_are_applied_one_after_the_other()
    {
        var id = await app.Api.CreateGameAsync();

        // 1–7 in row 1, which leaves (1,8) and (1,9) with 8 and 9 whatever order the moves are applied in.
        var responses = await Task.WhenAll(Enumerable.Range(1, 7).Select(column => app.Api.MoveAsync(id, 1, column, column)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var grid = await app.Api.GetGameAsync(id);
        Assert.Equal(7, MovesOf(grid).Count);
        Assert.All(MovesOf(grid), move => Assert.Equal(0, move.GetProperty("deductions").GetInt32()));
        Assert.Equal(
            Enumerable.Range(1, 7).Select(column => ((int?)column, (string?)"Move")),
            Enumerable.Range(1, 7).Select(column => PlacementOf(grid, 1, column)));
        Assert.Equal([8, 9], DigitsOf(CellOf(grid, 1, 8).GetProperty("candidates")));
        Assert.Equal([8, 9], DigitsOf(CellOf(grid, 1, 9).GetProperty("candidates")));
    }
}
