using Aspire.Hosting.Eventing;
using Aspire.Hosting.Lifecycle;

/// <summary>
/// Gives every Dapr sidecar the broker its <c>pubsub</c> component connects to: the sidecar waits until the broker is
/// healthy, and finds its connection string in <see cref="ConnectionStringVariable"/>. The Dapr hosting creates the
/// sidecars as the app starts, so this must be registered after it, to see them.
/// </summary>
internal sealed class DaprSidecars(IResourceWithConnectionString broker) : IDistributedApplicationEventingSubscriber
{
    /// <summary>Where <c>dapr/pubsub.yaml</c> reads the connection string, through the environment secret store.</summary>
    public const string ConnectionStringVariable = "SUDOKU_MESSAGING";

    public Task SubscribeAsync(
        IDistributedApplicationEventing eventing, DistributedApplicationExecutionContext executionContext, CancellationToken cancellationToken)
    {
        eventing.Subscribe<BeforeStartEvent>((start, _) =>
        {
            // The Dapr hosting names each sidecar's CLI resource after its project: cells-dapr-cli, api-dapr-cli.
            foreach (var sidecar in start.Model.Resources.Where(resource => resource.Name.EndsWith("-dapr-cli", StringComparison.Ordinal)))
            {
                sidecar.Annotations.Add(new WaitAnnotation(broker, WaitType.WaitUntilHealthy));
                sidecar.Annotations.Add(new EnvironmentCallbackAnnotation(environment =>
                    environment[ConnectionStringVariable] = broker.ConnectionStringExpression));
            }

            return Task.CompletedTask;
        });
        return Task.CompletedTask;
    }
}
