using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using SudokuDaprActors.Core.Tests;

namespace SudokuDaprActors.Api.Tests;

/// <summary>The Api, talking to the test assembly's RabbitMQ container where Aspire would give it its own.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("ConnectionStrings:messaging", Broker.ConnectionStringAsync().GetAwaiter().GetResult());
}
