namespace SudokuDaprActors.Core.Tests;

// A game forgets a grid by disposing it, which frees what the grid runs on, such as a Dapr grid's actor state
// (ADR 0005): when a replay replaces it, and when the game ends.
public class ForgetGridTests
{
    [Fact]
    public async Task A_replay_forgets_the_grid_it_replaces_and_keeps_the_new_one()
    {
        var backend = new RecordingBackend(await Broker.GridsAsync());
        await using var game = await Game.NewAsync(backend, TestContext.Current.CancellationToken);
        await game.MoveAsync(1, 1, 5);

        await game.ReplayToAsync(0);

        Assert.Equal(2, backend.Started.Count);
        Assert.True(backend.Started[0].IsForgotten);
        Assert.False(backend.Started[1].IsForgotten);
    }

    [Fact]
    public async Task Ending_a_game_forgets_its_grid()
    {
        var backend = new RecordingBackend(await Broker.GridsAsync());
        var game = await Game.NewAsync(backend, TestContext.Current.CancellationToken);
        await game.MoveAsync(1, 1, 5);
        await game.ReplayToAsync(1);

        await game.DisposeAsync();

        Assert.All(backend.Started, grid => Assert.True(grid.IsForgotten));
    }

    /// <summary>Real grids, recording each one a game starts and whether it has been forgotten.</summary>
    private sealed class RecordingBackend(IGridBackend backend) : IGridBackend
    {
        public List<RecordingGrid> Started { get; } = [];

        public async Task<IGameGrids> OpenAsync(Guid game, CancellationToken cancellationToken = default) =>
            new RecordingGameGrids(await backend.OpenAsync(game, cancellationToken), Started);
    }

    private sealed class RecordingGameGrids(IGameGrids grids, List<RecordingGrid> started) : IGameGrids
    {
        public async Task<IGrid> StartAsync()
        {
            var grid = new RecordingGrid(await grids.StartAsync());
            started.Add(grid);
            return grid;
        }

        public void MakeCurrent(IGrid? grid) => grids.MakeCurrent((grid as RecordingGrid)?.Grid);

        public Task<IAsyncDisposable> WatchAsync(Action<Step> read) => grids.WatchAsync(read);

        public ValueTask DisposeAsync() => grids.DisposeAsync();
    }

    private sealed class RecordingGrid(IGrid grid) : IGrid
    {
        public IGrid Grid => grid;

        public bool IsForgotten { get; private set; }

        public GameState State => grid.State;

        public IEnumerable<Cell> Cells => grid.Cells;

        public Cell Cell(int row, int column) => grid.Cell(row, column);

        public Task<(MoveOutcome Outcome, int Deductions)> MoveAsync(int row, int column, int digit) =>
            grid.MoveAsync(row, column, digit);

        public async ValueTask DisposeAsync()
        {
            await grid.DisposeAsync();
            IsForgotten = true;
        }
    }
}
