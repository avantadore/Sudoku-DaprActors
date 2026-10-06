namespace SudokuDaprActors.Api;

/// <summary>What grids run on, such as Dapr actors. Each game opens its own <see cref="IGameGrids"/> on it.</summary>
public interface IGridBackend
{
    Task<IGameGrids> OpenAsync(Guid game, CancellationToken cancellationToken = default);
}
