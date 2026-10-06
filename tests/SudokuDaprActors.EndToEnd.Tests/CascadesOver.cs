using System.Net.Http.Json;
using System.Text.Json;

namespace SudokuDaprActors.EndToEnd.Tests;

/// <summary>
/// The grids whose cascades are over, from now on, which the Api does not tell. It listens on the broker next to the
/// Api, through the management HTTP API: Dapr pub/sub publishes each topic to an exchange of its name, and this binds
/// a queue of its own to the <c>cascades</c> exchange and polls it.
/// </summary>
internal sealed class CascadesOver : IAsyncDisposable
{
    /// <summary>How long to wait for a cascade to be over, which only a bug makes a test wait out.</summary>
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private readonly HttpClient _management;
    private readonly string _queue;

    private CascadesOver(HttpClient management, string queue)
    {
        _management = management;
        _queue = queue;
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static async Task<CascadesOver> ListenAsync(HttpClient management)
    {
        var listener = new CascadesOver(management, $"end-to-end-cascades-{Guid.NewGuid():N}");
        // Durable, as the broker allows no other queue outside a connection, but expires a minute after it was last
        // used, in case the test never gets to delete it.
        await SucceedsAsync(await management.PutAsJsonAsync(
            listener.QueuePath, new { durable = true, auto_delete = false, arguments = new Dictionary<string, object> { ["x-expires"] = 60_000 } },
            Cancellation));
        // Dapr declares the exchange only once it first publishes or subscribes, which may not have happened yet, so
        // this declares it as Dapr does.
        await SucceedsAsync(await management.PutAsJsonAsync(
            "api/exchanges/%2F/cascades", new { type = "fanout", durable = true, auto_delete = true }, Cancellation));
        await SucceedsAsync(await management.PostAsJsonAsync(
            $"api/bindings/%2F/e/cascades/q/{listener._queue}", new { routing_key = "" }, Cancellation));
        return listener;
    }

    private string QueuePath => $"api/queues/%2F/{_queue}";

    /// <summary>The grid of the next cascade that is over.</summary>
    public async Task<Guid> NextGridAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        timeout.CancelAfter(ReadTimeout);
        while (true)
        {
            var response = await _management.PostAsJsonAsync(
                $"{QueuePath}/get", new { count = 1, ackmode = "ack_requeue_false", encoding = "auto" }, timeout.Token);
            await SucceedsAsync(response);
            using var messages = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            if (messages.RootElement.GetArrayLength() > 0)
            {
                // A CloudEvent, whose data is the CascadeOver.
                using var cloudEvent = JsonDocument.Parse(messages.RootElement[0].GetProperty("payload").GetString()!);
                return cloudEvent.RootElement.GetProperty("data").GetProperty("grid").GetGuid();
            }

            await Task.Delay(PollInterval, timeout.Token);
        }
    }

    public async ValueTask DisposeAsync() => await _management.DeleteAsync(QueuePath, CancellationToken.None);

    /// <summary>Fails with what the broker answered, unless the response is a success.</summary>
    private static async Task SucceedsAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} answered {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
    }
}
