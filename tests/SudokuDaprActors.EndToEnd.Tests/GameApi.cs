using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SudokuDaprActors.Core.Tests;

namespace SudokuDaprActors.EndToEnd.Tests;

/// <summary>Plays games through the Api, and reads what it answers.</summary>
internal static class GameApi
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static async Task<string> CreateGameAsync(this HttpClient api)
    {
        var response = await api.PostAsync("/games", content: null, Cancellation);
        response.EnsureSuccessStatusCode();
        return (await ReadJsonAsync(response)).GetProperty("id").GetString()!;
    }

    public static Task<HttpResponseMessage> MoveAsync(this HttpClient api, string id, int row, int column, int digit) =>
        api.PutAsJsonAsync($"/games/{id}/cells/{row}/{column}", new { digit }, Cancellation);

    /// <summary>Makes a move the test expects to be accepted, and returns the grid it leaves.</summary>
    public static async Task<JsonElement> PlayAsync(this HttpClient api, string id, int row, int column, int digit)
    {
        var response = await api.MoveAsync(id, row, column, digit);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    /// <summary>
    /// Plays the game into contradiction in 7 moves: row 1 holds 1–6, so 9 can only go in (1,7), (1,8) or (1,9), all
    /// in the top-right box, and then 9 in (2,7) leaves 9 no place in row 1. Returns the grid the last move leaves.
    /// </summary>
    public static async Task<JsonElement> PlayIntoContradictionAsync(this HttpClient api, string id)
    {
        for (var digit = 1; digit <= 6; digit++)
        {
            await api.PlayAsync(id, 1, digit, digit);
        }

        return await api.PlayAsync(id, 2, 7, 9);
    }

    /// <summary>
    /// Plays the first <paramref name="count"/> givens of <see cref="Puzzle"/>, each of which the test expects to be
    /// accepted or unchanged, and returns the grid each leaves.
    /// </summary>
    public static async Task<List<JsonElement>> PlayGivensAsync(this HttpClient api, string id, int count = int.MaxValue)
    {
        List<JsonElement> grids = [];
        foreach (var (row, column, digit) in Puzzle.Givens.Take(count))
        {
            // A given may already have been deduced from earlier givens, which leaves the move unchanged.
            grids.Add(await api.PlayAsync(id, row, column, digit));
        }

        return grids;
    }

    public static Task<HttpResponseMessage> SetPositionAsync(this HttpClient api, string id, int position) =>
        api.PutAsJsonAsync($"/games/{id}/position", new { position }, Cancellation);

    /// <summary>Replays the game to <paramref name="position"/>, which the test expects to succeed.</summary>
    public static async Task<JsonElement> ReplayAsync(this HttpClient api, string id, int position)
    {
        var response = await api.SetPositionAsync(id, position);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    public static async Task<JsonElement> GetGameAsync(this HttpClient api, string id) =>
        await ReadJsonAsync(await api.GetAsync($"/games/{id}", Cancellation));

    public static Task<HttpResponseMessage> GetCandidatesAsync(this HttpClient api, string id, int row, int column) =>
        api.GetAsync($"/games/{id}/cells/{row}/{column}/candidates", Cancellation);

    public static async Task AssertRejected(HttpResponseMessage response, string reason)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await AssertProblemDetails(response);
        Assert.Equal("Move rejected", problem.GetProperty("title").GetString());
        Assert.Equal(reason, problem.GetProperty("detail").GetString());
    }

    /// <summary>A problem details response, whose status is the response's. Returns the problem.</summary>
    public static async Task<JsonElement> AssertProblemDetails(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await ReadJsonAsync(response);
        Assert.Equal((int)response.StatusCode, problem.GetProperty("status").GetInt32());
        return problem;
    }

    /// <summary>The grid representation: a flat list of 81 cells, each carrying its own coordinates.</summary>
    public static void AssertGridShape(JsonElement grid)
    {
        Assert.Equal(JsonValueKind.String, grid.GetProperty("state").ValueKind);

        var cells = grid.GetProperty("cells").EnumerateArray().ToList();
        Assert.Equal(81, cells.Count);
        Assert.Equal(81, cells.Select(cell => (cell.GetProperty("row").GetInt32(), cell.GetProperty("column").GetInt32())).Distinct().Count());
        Assert.All(cells, cell =>
        {
            Assert.InRange(cell.GetProperty("row").GetInt32(), 1, 9);
            Assert.InRange(cell.GetProperty("column").GetInt32(), 1, 9);
            Assert.Contains(cell.GetProperty("digit").ValueKind, new[] { JsonValueKind.Null, JsonValueKind.Number });
            Assert.Contains(cell.GetProperty("source").ValueKind, new[] { JsonValueKind.Null, JsonValueKind.String });
            Assert.All(cell.GetProperty("candidates").EnumerateArray(), digit => Assert.Equal(JsonValueKind.Number, digit.ValueKind));
        });
    }

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync(Cancellation);
        return JsonDocument.Parse(json).RootElement;
    }

    public static IEnumerable<int> DigitsOf(JsonElement array) => array.EnumerateArray().Select(digit => digit.GetInt32());

    public static JsonElement CellOf(JsonElement grid, int row, int column) =>
        grid.GetProperty("cells").EnumerateArray()
            .Single(cell => cell.GetProperty("row").GetInt32() == row && cell.GetProperty("column").GetInt32() == column);

    /// <summary>The cell's digit and who placed it, both null while it is empty.</summary>
    public static (int? Digit, string? Source) PlacementOf(JsonElement grid, int row, int column)
    {
        var cell = CellOf(grid, row, column);
        return (cell.GetProperty("digit") is { ValueKind: JsonValueKind.Number } digit ? digit.GetInt32() : null,
            cell.GetProperty("source").GetString());
    }

    public static IEnumerable<JsonElement> OtherCells(JsonElement grid, int row, int column) =>
        grid.GetProperty("cells").EnumerateArray()
            .Where(cell => (cell.GetProperty("row").GetInt32(), cell.GetProperty("column").GetInt32()) != (row, column));

    public static bool IsPeer(JsonElement cell, int row, int column)
    {
        var (otherRow, otherColumn) = (cell.GetProperty("row").GetInt32(), cell.GetProperty("column").GetInt32());
        return otherRow == row || otherColumn == column
            || ((otherRow - 1) / 3 == (row - 1) / 3 && (otherColumn - 1) / 3 == (column - 1) / 3);
    }

    /// <summary>The grid's cells as JSON, to compare grids by.</summary>
    public static string CellsOf(JsonElement grid) => grid.GetProperty("cells").GetRawText();

    public static IReadOnlyList<JsonElement> MovesOf(JsonElement grid) => [.. grid.GetProperty("moves").EnumerateArray()];

    /// <summary>The grid's move history as JSON, to compare move histories by.</summary>
    public static string MoveHistoryOf(JsonElement grid) => grid.GetProperty("moves").GetRawText();

    public static string StateOf(JsonElement grid) => grid.GetProperty("state").GetString()!;

    public static int PositionOf(JsonElement grid) => grid.GetProperty("position").GetInt32();

    /// <summary>How many deductions the last move of the history made.</summary>
    public static int DeductionsOfLastMove(JsonElement grid) => MovesOf(grid)[^1].GetProperty("deductions").GetInt32();

    /// <summary>How many cells hold a deduction.</summary>
    public static int DeducedCells(JsonElement grid) =>
        grid.GetProperty("cells").EnumerateArray().Count(cell => cell.GetProperty("source").GetString() == "Deduction");
}
