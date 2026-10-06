using System.Text.Json;
using static SudokuDaprActors.EndToEnd.Tests.GameApi;

namespace SudokuDaprActors.EndToEnd.Tests;

/// <summary>A game is in progress until all 81 cells are filled, and then solved. See also <see cref="ContradictionTests"/>.</summary>
[Collection(nameof(App))]
public class GameStateTests(App app)
{
    [Fact]
    public async Task A_game_is_in_progress_while_cells_are_empty_and_solved_once_all_81_are_filled()
    {
        var id = await app.Api.CreateGameAsync();

        var grids = await app.Api.PlayGivensAsync(id);

        Assert.Equal("InProgress", StateOf(grids[0]));
        Assert.Equal("Solved", StateOf(grids[^1]));
        Assert.All(grids[^1].GetProperty("cells").EnumerateArray(), cell => Assert.Equal(JsonValueKind.Number, cell.GetProperty("digit").ValueKind));
    }
}
