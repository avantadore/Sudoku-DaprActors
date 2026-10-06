using RabbitMQ.Client;

namespace SudokuDaprActors.Core;

/// <summary>Grids whose cells and units talk on RabbitMQ streams (ADR 0004). Each game has its own connection.</summary>
public sealed class RabbitMqGridBackend(IConnectionFactory connections) : IGridBackend
{
    public async Task<IGameGrids> OpenAsync(Guid game, CancellationToken cancellationToken = default) =>
        await RabbitMqGameGrids.OpenAsync(connections, game, cancellationToken);
}
