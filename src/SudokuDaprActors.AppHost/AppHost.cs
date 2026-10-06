using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using Aspire.Hosting.Lifecycle;
using CommunityToolkit.Aspire.Hosting.Dapr;
using Microsoft.Extensions.DependencyInjection;

var builder = DistributedApplication.CreateBuilder(args);

// The broker the grids talk on. No data volume: games live in the Api's memory, and their grids with them.
var messaging = builder.AddRabbitMQ("messaging")
    .WithManagementPlugin();

// The placement service tells the sidecars where each actor lives. The app runs its own, so that `dapr init --slim`
// is enough, and on a free port of its own, so that it runs next to the one `dapr init` starts. The sidecars are told
// the address before Aspire allocates ports, so the port is picked here.
var placementPort = FreePort();
builder.AddContainer("placement", "daprio/placement", "1.18.4")
    .WithArgs("./placement", "--port", "50005")
    .WithEndpoint(port: placementPort, targetPort: 50005, scheme: "tcp", name: "placement", isProxied: false);

// Each sidecar loads the components in dapr/: pub/sub on the broker, and the in-memory actor state store.
var sidecar = new DaprSidecarOptions
{
    PlacementHostAddress = $"localhost:{placementPort}",
    ResourcesPaths = ImmutableHashSet.Create(Path.Combine(builder.AppHostDirectory, "dapr")),
};

// The cell and grid actors, and the unit topics' subscribers.
var cells = builder.AddProject<Projects.SudokuDaprActors_Cells>("cells")
    .WithHttpHealthCheck("/health")
    .WithDaprSidecar(sidecar)
    .WaitFor(messaging);

var api = builder.AddProject<Projects.SudokuDaprActors_Api>("api")
    .WithHttpHealthCheck("/health")
    .WithEnvironment("Grid", "Dapr")
    .WithDaprSidecar(sidecar)
    .WaitFor(messaging)
    .WaitFor(cells);

// After the sidecars, which it needs to see.
builder.Services.AddSingleton<IDistributedApplicationEventingSubscriber>(new DaprSidecars(messaging.Resource));

builder.AddProject<Projects.SudokuDaprActors_Web>("web")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();

static int FreePort()
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    return ((IPEndPoint)listener.LocalEndpoint).Port;
}
