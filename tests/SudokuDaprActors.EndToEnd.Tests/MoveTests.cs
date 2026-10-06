using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SudokuDaprActors.EndToEnd.Tests;

/// <summary>A move travels through the Dapr actors, and completes only once its cascade is over (ADR 0005).</summary>
public class MoveTests(App app) : IClassFixture<App>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_accepted_move_fills_the_cell_and_its_peers_lose_the_digit_before_it_completes()
    {
        var id = await CreateGame();

        var response = await Move(id, 2, 3, 8);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var grid = await ReadJson(response);
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
        var id = await CreateGame();
        await Move(id, 5, 5, 4);

        var response = await Move(id, 5, 9, 4);

        await AssertRejected(response, "4 is not a candidate for cell (5, 9).");
    }

    [Fact]
    public async Task A_move_on_a_filled_cell_is_rejected_because_placements_are_final()
    {
        var id = await CreateGame();
        await Move(id, 7, 1, 2);

        var response = await Move(id, 7, 1, 6);

        await AssertRejected(response, "Cell (7, 1) already holds 2, and placements are final.");
    }

    [Fact]
    public async Task The_same_digit_again_leaves_the_grid_unchanged()
    {
        var id = await CreateGame();
        await Move(id, 9, 9, 1);

        var response = await Move(id, 9, 9, 1);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var grid = await ReadJson(response);
        Assert.Equal(1, CellOf(grid, 9, 9).GetProperty("digit").GetInt32());
        Assert.Single(grid.GetProperty("moves").EnumerateArray());
    }

    [Fact]
    public async Task Each_move_sees_the_cascades_of_the_moves_before_it()
    {
        var id = await CreateGame();
        for (var column = 1; column <= 8; column++)
        {
            Assert.Equal(HttpStatusCode.OK, (await Move(id, 1, column, column)).StatusCode);
        }

        var response = await app.Api.GetAsync($"/games/{id}/cells/1/9/candidates", Cancellation);

        Assert.Equal([9], DigitsOf(await ReadJson(response)));
    }

    [Fact]
    public async Task Games_do_not_hear_each_others_cascades()
    {
        // Every grid shares the unit topics, so each message carries its grid.
        var first = await CreateGame();
        var second = await CreateGame();
        await Move(first, 3, 3, 7);

        var response = await app.Api.GetAsync($"/games/{second}/cells/3/4/candidates", Cancellation);

        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9], DigitsOf(await ReadJson(response)));
        Assert.Equal(HttpStatusCode.OK, (await Move(second, 3, 4, 7)).StatusCode);
    }

    private async Task<string> CreateGame()
    {
        var response = await app.Api.PostAsync("/games", content: null, Cancellation);
        response.EnsureSuccessStatusCode();
        return (await ReadJson(response)).GetProperty("id").GetString()!;
    }

    private Task<HttpResponseMessage> Move(string id, int row, int column, int digit) =>
        app.Api.PutAsJsonAsync($"/games/{id}/cells/{row}/{column}", new { digit }, Cancellation);

    private static async Task AssertRejected(HttpResponseMessage response, string reason)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await ReadJson(response);
        Assert.Equal("Move rejected", problem.GetProperty("title").GetString());
        Assert.Equal(reason, problem.GetProperty("detail").GetString());
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync(Cancellation);
        return JsonDocument.Parse(json).RootElement;
    }

    private static IEnumerable<int> DigitsOf(JsonElement array) => array.EnumerateArray().Select(digit => digit.GetInt32());

    private static JsonElement CellOf(JsonElement grid, int row, int column) =>
        grid.GetProperty("cells").EnumerateArray()
            .Single(cell => cell.GetProperty("row").GetInt32() == row && cell.GetProperty("column").GetInt32() == column);

    private static IEnumerable<JsonElement> OtherCells(JsonElement grid, int row, int column) =>
        grid.GetProperty("cells").EnumerateArray()
            .Where(cell => (cell.GetProperty("row").GetInt32(), cell.GetProperty("column").GetInt32()) != (row, column));

    private static bool IsPeer(JsonElement cell, int row, int column)
    {
        var (otherRow, otherColumn) = (cell.GetProperty("row").GetInt32(), cell.GetProperty("column").GetInt32());
        return otherRow == row || otherColumn == column
            || ((otherRow - 1) / 3 == (row - 1) / 3 && (otherColumn - 1) / 3 == (column - 1) / 3);
    }
}
