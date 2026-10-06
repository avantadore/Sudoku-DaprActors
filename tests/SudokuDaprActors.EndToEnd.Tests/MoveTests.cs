using System.Net;
using static SudokuDaprActors.EndToEnd.Tests.GameApi;

namespace SudokuDaprActors.EndToEnd.Tests;

/// <summary>A move travels through the Dapr actors, and completes only once its cascade is over (ADR 0005).</summary>
[Collection(nameof(App))]
public class MoveTests(App app)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_accepted_move_fills_the_cell_and_its_peers_lose_the_digit_before_it_completes()
    {
        var id = await app.Api.CreateGameAsync();

        var response = await app.Api.MoveAsync(id, 2, 3, 8);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var grid = await ReadJsonAsync(response);
        var cell = CellOf(grid, 2, 3);
        Assert.Equal(8, cell.GetProperty("digit").GetInt32());
        Assert.Equal("Move", cell.GetProperty("source").GetString());
        Assert.Equal([8], DigitsOf(cell.GetProperty("candidates")));
        Assert.All(OtherCells(grid, 2, 3), other =>
        {
            var candidates = DigitsOf(other.GetProperty("candidates"));
            if (IsPeer(other, 2, 3))
            {
                Assert.DoesNotContain(8, candidates);
            }
            else
            {
                Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9], candidates);
            }
        });
        Assert.Equal(1, grid.GetProperty("position").GetInt32());
    }

    [Fact]
    public async Task A_move_on_a_digit_a_peer_holds_is_rejected_because_it_is_no_candidate()
    {
        // The previous move's cascade is over before this one is made, so the cell has already lost the digit.
        var id = await app.Api.CreateGameAsync();
        await app.Api.MoveAsync(id, 5, 5, 4);

        var response = await app.Api.MoveAsync(id, 5, 9, 4);

        await AssertRejected(response, "4 is not a candidate for cell (5, 9).");
    }

    [Fact]
    public async Task A_move_on_a_filled_cell_is_rejected_because_placements_are_final()
    {
        var id = await app.Api.CreateGameAsync();
        await app.Api.MoveAsync(id, 7, 1, 2);

        var response = await app.Api.MoveAsync(id, 7, 1, 6);

        await AssertRejected(response, "Cell (7, 1) already holds 2, and placements are final.");
    }

    [Fact]
    public async Task The_same_digit_again_leaves_the_grid_unchanged()
    {
        var id = await app.Api.CreateGameAsync();
        await app.Api.MoveAsync(id, 9, 9, 1);

        var response = await app.Api.MoveAsync(id, 9, 9, 1);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var grid = await ReadJsonAsync(response);
        Assert.Equal(1, CellOf(grid, 9, 9).GetProperty("digit").GetInt32());
        Assert.Single(grid.GetProperty("moves").EnumerateArray());
    }

    [Fact]
    public async Task Each_move_sees_the_cascades_of_the_moves_before_it()
    {
        var id = await app.Api.CreateGameAsync();
        for (var column = 1; column <= 8; column++)
        {
            Assert.Equal(HttpStatusCode.OK, (await app.Api.MoveAsync(id, 1, column, column)).StatusCode);
        }

        var response = await app.Api.GetAsync($"/games/{id}/cells/1/9/candidates", Cancellation);

        Assert.Equal([9], DigitsOf(await ReadJsonAsync(response)));
    }

    [Fact]
    public async Task Games_do_not_hear_each_others_cascades()
    {
        // Every grid shares the unit topics, so each message carries its grid.
        var first = await app.Api.CreateGameAsync();
        var second = await app.Api.CreateGameAsync();
        await app.Api.MoveAsync(first, 3, 3, 7);

        var response = await app.Api.GetAsync($"/games/{second}/cells/3/4/candidates", Cancellation);

        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9], DigitsOf(await ReadJsonAsync(response)));
        Assert.Equal(HttpStatusCode.OK, (await app.Api.MoveAsync(second, 3, 4, 7)).StatusCode);
    }
}
