using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace SudokuDaprActors.Core;

/// <summary>
/// One game's connection to RabbitMQ and its steps stream, which belongs to the game, so watchers keep it through a
/// replay; its grids each have their own unit streams (ADR 0004). Only the current grid publishes steps, and it counts
/// one delivery per watcher for each.
/// </summary>
internal sealed class RabbitMqGameGrids : IGameGrids
{
    private readonly IConnection _connection;
    private readonly IChannel _channel; // declares and deletes the steps stream
    private readonly string _stepsStream;
    private volatile RabbitMqGrid? _current;
    private int _watchers;

    private RabbitMqGameGrids(Guid game, IConnection connection, IChannel channel)
    {
        _connection = connection;
        _channel = channel;
        _stepsStream = Topology.StepsStream(game);
    }

    /// <summary>Opens the game's own connection from <paramref name="connections"/> and declares its steps stream.</summary>
    public static async Task<RabbitMqGameGrids> OpenAsync(
        IConnectionFactory connections, Guid game, CancellationToken cancellationToken = default)
    {
        var connection = await connections.CreateConnectionAsync($"sudoku game {game}", cancellationToken);
        try
        {
            var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
            await channel.DeclareStreamAsync(Topology.StepsStream(game));
            return new RabbitMqGameGrids(game, connection, channel);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task<IGrid> StartAsync() => await RabbitMqGrid.StartAsync(_connection);

    public void MakeCurrent(IGrid? grid)
    {
        // Every grid of the game was started here, so it is one of ours: it publishes the steps and counts their
        // deliveries itself.
        if (grid is RabbitMqGrid current)
        {
            current.StepsStream = _stepsStream;
            current.StepWatchers = _watchers;
        }

        _current = (RabbitMqGrid?)grid;
    }

    public async Task<IAsyncDisposable> WatchAsync(Action<Step> read)
    {
        var reader = await Reader.StartAsync(_connection, _stepsStream, read, () => _current?.StepRead(), StopReading);
        CountWatchers(+1);
        return reader;
    }

    /// <summary>Deletes the steps stream, and closes the game's connection.</summary>
    public async ValueTask DisposeAsync()
    {
        await _channel.DeleteStreamAsync(_stepsStream);
        await _connection.CloseAsync();
        await _connection.DisposeAsync();
    }

    private void StopReading() => CountWatchers(-1);

    private void CountWatchers(int change)
    {
        _watchers += change;
        if (_current is { } current)
        {
            current.StepWatchers = _watchers;
        }
    }

    /// <summary>One watcher's consumer of the steps stream, on its own channel.</summary>
    private sealed class Reader : IAsyncDisposable
    {
        private readonly Action _stopped;
        private IChannel? _channel;

        private Reader(IChannel channel, Action stopped)
        {
            _channel = channel;
            _stopped = stopped;
        }

        /// <summary>
        /// Starts reading the steps stream at its next step, handing each to <paramref name="read"/> and then
        /// calling <paramref name="handled"/>.
        /// </summary>
        public static async Task<Reader> StartAsync(
            IConnection connection, string stream, Action<Step> read, Action handled, Action stopped)
        {
            var channel = await connection.CreateChannelAsync();
            await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: Topology.Prefetch, global: false);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, delivery) =>
            {
                read((Step)Envelope.Open(delivery.BasicProperties, delivery.Body));
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false);
                handled();
            };
            await channel.BasicConsumeAsync(stream, autoAck: false, consumerTag: "", noLocal: false, exclusive: false,
                arguments: Topology.StartAt("next"), consumer);
            return new Reader(channel, stopped);
        }

        /// <summary>The grid stops counting on this reader, and its channel closes.</summary>
        public async ValueTask DisposeAsync()
        {
            if (_channel is { } channel)
            {
                _channel = null;
                _stopped();
                await channel.CloseAsync();
                channel.Dispose();
            }
        }
    }
}
