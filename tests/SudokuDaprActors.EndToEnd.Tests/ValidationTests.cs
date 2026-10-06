using System.Net;
using static SudokuDaprActors.EndToEnd.Tests.GameApi;

namespace SudokuDaprActors.EndToEnd.Tests;

/// <summary>
/// Rows, columns and digits are 1–9, and a position is 0 to the number of moves: anything else is a 400 with problem
/// details. A game the Api does not know is a 404.
/// </summary>
[Collection(nameof(App))]
public class ValidationTests(App app)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(10, 1, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 10, 1)]
    [InlineData(1, 1, 0)]
    [InlineData(1, 1, 10)]
    public async Task A_move_outside_1_to_9_returns_400(int row, int column, int digit)
    {
        var id = await app.Api.CreateGameAsync();

        var response = await app.Api.MoveAsync(id, row, column, digit);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertProblemDetails(response);
        Assert.Empty(MovesOf(await app.Api.GetGameAsync(id)));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(10, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 10)]
    public async Task Reading_candidates_outside_rows_and_columns_1_to_9_returns_400(int row, int column)
    {
        var id = await app.Api.CreateGameAsync();

        var response = await app.Api.GetCandidatesAsync(id, row, column);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertProblemDetails(response);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public async Task Setting_a_position_outside_0_to_the_number_of_moves_returns_400_keyed_position_and_keeps_the_game(int position)
    {
        var id = await app.Api.CreateGameAsync();
        await app.Api.PlayAsync(id, 1, 1, 5);
        await app.Api.PlayAsync(id, 2, 4, 5);

        var response = await app.Api.SetPositionAsync(id, position);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await AssertProblemDetails(response);
        Assert.Equal(["Position"], problem.GetProperty("errors").EnumerateObject().Select(error => error.Name));
        Assert.Equal(2, PositionOf(await app.Api.GetGameAsync(id)));
    }

    [Fact]
    public async Task Reading_an_unknown_game_returns_404()
    {
        var response = await app.Api.GetAsync($"/games/{Guid.NewGuid()}", Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_move_in_an_unknown_game_returns_404()
    {
        var response = await app.Api.MoveAsync(Guid.NewGuid().ToString(), 1, 1, 1);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Setting_the_position_of_an_unknown_game_returns_404()
    {
        var response = await app.Api.SetPositionAsync(Guid.NewGuid().ToString(), 0);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reading_candidates_of_an_unknown_game_returns_404()
    {
        var response = await app.Api.GetCandidatesAsync(Guid.NewGuid().ToString(), 1, 1);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
