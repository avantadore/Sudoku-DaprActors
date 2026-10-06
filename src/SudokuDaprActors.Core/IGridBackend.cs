namespace SudokuDaprActors.Core;

/// <summary>What grids run on, such as RabbitMQ streams. Each game opens its own <see cref="IGameGrids"/> on it.</summary>
public interface IGridBackend
{
    Task<IGameGrids> OpenAsync(Guid game, CancellationToken cancellationToken = default);
}
