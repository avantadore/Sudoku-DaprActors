using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

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

    public static async Task AssertRejected(HttpResponseMessage response, string reason)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await ReadJsonAsync(response);
        Assert.Equal("Move rejected", problem.GetProperty("title").GetString());
        Assert.Equal(reason, problem.GetProperty("detail").GetString());
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

    public static IReadOnlyList<JsonElement> MovesOf(JsonElement grid) => [.. grid.GetProperty("moves").EnumerateArray()];

    /// <summary>How many deductions the last move of the history made.</summary>
    public static int DeductionsOfLastMove(JsonElement grid) => MovesOf(grid)[^1].GetProperty("deductions").GetInt32();

    /// <summary>How many cells hold a deduction.</summary>
    public static int DeducedCells(JsonElement grid) =>
        grid.GetProperty("cells").EnumerateArray().Count(cell => cell.GetProperty("source").GetString() == "Deduction");
}
