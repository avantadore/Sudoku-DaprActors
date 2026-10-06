using System.Net;
using System.Text.Json;
using static SudokuDaprActors.EndToEnd.Tests.GameApi;

namespace SudokuDaprActors.EndToEnd.Tests;

/// <summary>Every game starts with all 81 cells empty, and no moves.</summary>
[Collection(nameof(App))]
public class NewGameTests(App app)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Creating_a_game_returns_201_with_its_location_and_an_empty_grid_in_progress()
    {
        var response = await app.Api.PostAsync("/games", content: null, Cancellation);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var grid = await ReadJsonAsync(response);
        var id = grid.GetProperty("id").GetString();
        Assert.Equal($"/games/{id}", response.Headers.Location?.OriginalString);
        Assert.Equal("InProgress", StateOf(grid));
        AssertGridShape(grid);
        Assert.Equal(0, PositionOf(grid));
        Assert.Empty(MovesOf(grid));
    }

    [Fact]
    public async Task Every_cell_of_a_new_game_is_empty_with_all_nine_candidates_and_listed_row_by_row()
    {
        var id = await app.Api.CreateGameAsync();

        var grid = await app.Api.GetGameAsync(id);

        var cells = grid.GetProperty("cells").EnumerateArray().ToList();
        Assert.Equal(
            Enumerable.Range(1, 9).SelectMany(row => Enumerable.Range(1, 9).Select(column => (row, column))),
            cells.Select(cell => (cell.GetProperty("row").GetInt32(), cell.GetProperty("column").GetInt32())));
        Assert.All(cells, cell =>
        {
            Assert.Equal(JsonValueKind.Null, cell.GetProperty("digit").ValueKind);
            Assert.Equal(JsonValueKind.Null, cell.GetProperty("source").ValueKind);
            Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9], DigitsOf(cell.GetProperty("candidates")));
        });
    }

    [Fact]
    public async Task A_created_game_can_be_read_back_by_its_id()
    {
        var id = await app.Api.CreateGameAsync();

        var response = await app.Api.GetAsync($"/games/{id}", Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var grid = await ReadJsonAsync(response);
        Assert.Equal(id, grid.GetProperty("id").GetString());
        AssertGridShape(grid);
    }

    [Fact]
    public async Task Reading_a_cells_candidates_in_a_new_game_returns_1_to_9()
    {
        var id = await app.Api.CreateGameAsync();

        var response = await app.Api.GetCandidatesAsync(id, 4, 7);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9], DigitsOf(await ReadJsonAsync(response)));
    }
}
