using Aspire.Hosting;
using Aspire.Hosting.Testing;

namespace SudokuDaprActors.EndToEnd.Tests;

/// <summary>
/// The whole app, as <c>aspire run</c> starts it: RabbitMQ, the Cells service and the Api with their Dapr sidecars,
/// and Web. Needs Docker and the Dapr CLI (<c>dapr init</c>). Started once for every test that shares it.
/// </summary>
public sealed class App : IAsyncLifetime
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(5);

    private DistributedApplication? _app;

    /// <summary>A client of the Api.</summary>
    public HttpClient Api { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(StartTimeout);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.SudokuDaprActors_AppHost>(timeout.Token);
        _app = await builder.BuildAsync(timeout.Token);
        await _app.StartAsync(timeout.Token);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("api", timeout.Token);
        Api = _app.CreateHttpClient("api");
    }

    public async ValueTask DisposeAsync()
    {
        Api?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}
