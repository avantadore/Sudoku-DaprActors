using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace SudokuDaprActors.EndToEnd.Tests;

/// <summary>
/// The whole app, as <c>aspire run</c> starts it: RabbitMQ, the Cells service and the Api with their Dapr sidecars,
/// and Web. Needs Docker and the Dapr CLI (<c>dapr init</c>). Started once for every test that shares it.
/// </summary>
public class App : IAsyncLifetime
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(5);

    private DistributedApplication? _app;

    /// <summary>A client of the Api.</summary>
    public HttpClient Api { get; private set; } = null!;

    /// <summary>A client of the Cells service's Dapr sidecar, which hosts the actors and reads their state.</summary>
    public HttpClient CellsSidecar { get; private set; } = null!;

    /// <summary>The connection string of the broker that Dapr pub/sub runs on.</summary>
    public string MessagingConnectionString { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(StartTimeout);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.SudokuDaprActors_AppHost>(timeout.Token);
        Configure(builder);
        _app = await builder.BuildAsync(timeout.Token);
        await _app.StartAsync(timeout.Token);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("api", timeout.Token);
        Api = _app.CreateHttpClient("api");
        CellsSidecar = new HttpClient { BaseAddress = _app.GetEndpoint("cells-dapr-cli", "http") };
        MessagingConnectionString = (await _app.GetConnectionStringAsync("messaging", timeout.Token))!;
    }

    public async ValueTask DisposeAsync()
    {
        Api?.Dispose();
        CellsSidecar?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    /// <summary>Changes the app before it starts.</summary>
    protected virtual void Configure(IDistributedApplicationTestingBuilder builder)
    {
    }

    protected static IResourceBuilder<ProjectResource> Project(IDistributedApplicationTestingBuilder builder, string name) =>
        builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>().Single(project => project.Name == name));
}

/// <summary>The test classes that share one <see cref="App"/>, rather than start one each.</summary>
[CollectionDefinition(nameof(App))]
public sealed class AppCollection : ICollectionFixture<App>;
