using System.Net;

namespace SudokuDaprActors.EndToEnd.Tests;

/// <summary>
/// A grid lives until a replay replaces it, and then its <c>GridActor</c> makes its cells and units remove their state
/// (ADR 0005).
/// </summary>
[Collection(nameof(App))]
public class ForgetTests(App app)
{
    private static readonly (int Row, int Column)[] Cells =
        [.. Enumerable.Range(1, 9).SelectMany(row => Enumerable.Range(1, 9).Select(column => (row, column)))];

    /// <summary>Each unit's topic, which names it in its actor's id.</summary>
    private static readonly string[] Units =
        [.. new[] { "row", "column", "box" }.SelectMany(kind => Enumerable.Range(1, 9).Select(number => $"{kind}-{number}"))];

    [Fact]
    public async Task A_replay_leaves_the_replaced_grids_actors_holding_no_state()
    {
        await using var cascades = await CascadesOver.ListenAsync(app.MessagingConnectionString);
        var id = await app.Api.CreateGameAsync();
        // In contradiction, so the grid's actor holds state too.
        await app.Api.PlayIntoContradictionAsync(id);
        var replaced = await cascades.NextGridAsync();
        Assert.True(await CellHasStateAsync(replaced, 1, 1));
        Assert.True(await UnitHasStateAsync(replaced, "row-1"));
        Assert.True(await GridHasStateAsync(replaced));

        await app.Api.ReplayAsync(id, 6);

        foreach (var (row, column) in Cells)
        {
            Assert.False(await CellHasStateAsync(replaced, row, column), $"Cell ({row}, {column}) holds state.");
        }

        foreach (var unit in Units)
        {
            Assert.False(await UnitHasStateAsync(replaced, unit), $"Unit {unit} holds state.");
        }

        Assert.False(await GridHasStateAsync(replaced));
    }

    private Task<bool> CellHasStateAsync(Guid grid, int row, int column) =>
        HasStateAsync("CellActor", $"{grid}:r{row}c{column}", "cell");

    private Task<bool> UnitHasStateAsync(Guid grid, string unit) => HasStateAsync("UnitActor", $"{grid}:{unit}", "unit");

    /// <summary>A grid's actor holds state only once the grid is in contradiction.</summary>
    private Task<bool> GridHasStateAsync(Guid grid) => HasStateAsync("GridActor", $"{grid}", "contradicted");

    /// <summary>Whether the actor holds state under <paramref name="key"/>, as the sidecar hosting it reads it.</summary>
    private async Task<bool> HasStateAsync(string type, string actor, string key)
    {
        var response = await app.CellsSidecar.GetAsync($"/v1.0/actors/{type}/{actor}/state/{key}", TestContext.Current.CancellationToken);
        return response.StatusCode switch
        {
            HttpStatusCode.OK => true,
            HttpStatusCode.NoContent => false,
            var status => throw new InvalidOperationException(
                $"Reading {type} {actor}'s {key} answered {status}: {await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}"),
        };
    }
}
