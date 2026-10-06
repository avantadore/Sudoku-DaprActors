using System.Text.Json;
using System.Threading.Channels;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace SudokuDaprActors.EndToEnd.Tests;

/// <summary>
/// The grids whose cascades are over, from now on, which the Api does not tell. It listens on the broker
/// next to the Api: Dapr pub/sub publishes each topic to an exchange of its name, and this binds a queue of its own to
/// the <c>cascades</c> exchange.
/// </summary>
internal sealed class CascadesOver : IAsyncDisposable
{
    /// <summary>How long to wait for a cascade to be over, which only a bug makes a test wait out.</summary>
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);

    private readonly IConnection _connection;
    private readonly IChannel _channel;
    private readonly Channel<Guid> _grids = Channel.CreateUnbounded<Guid>();

    private CascadesOver(IConnection connection, IChannel channel)
    {
        _connection = connection;
        _channel = channel;
    }

    public static async Task<CascadesOver> ListenAsync(string messaging)
    {
        var connection = await new ConnectionFactory { Uri = new Uri(messaging) }.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();
        var ids = new CascadesOver(connection, channel);

        var queue = await channel.QueueDeclareAsync(); // named by the broker, and gone with the connection
        await channel.QueueBindAsync(queue.QueueName, "cascades", routingKey: "");
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) =>
        {
            // A CloudEvent, whose data is the CascadeOver.
            using var cloudEvent = JsonDocument.Parse(delivery.Body);
            ids._grids.Writer.TryWrite(cloudEvent.RootElement.GetProperty("data").GetProperty("grid").GetGuid());
            return Task.CompletedTask;
        };
        await channel.BasicConsumeAsync(queue.QueueName, autoAck: true, consumer);
        return ids;
    }

    /// <summary>The grid of the next cascade that is over.</summary>
    public async Task<Guid> NextGridAsync() =>
        await _grids.Reader.ReadAsync().AsTask().WaitAsync(ReadTimeout, TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
