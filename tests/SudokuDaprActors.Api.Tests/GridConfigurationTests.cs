using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SudokuDaprActors.Core;

namespace SudokuDaprActors.Api.Tests;

public class GridConfigurationTests
{
    [Fact]
    public async Task Without_a_grid_configured_the_Api_runs_its_grids_on_RabbitMQ()
    {
        await using var factory = new ApiFactory();

        Assert.IsType<RabbitMqGridBackend>(factory.Services.GetRequiredService<IGridBackend>());
    }

    [Fact]
    public async Task Configuring_RabbitMQ_runs_the_grids_on_RabbitMQ()
    {
        await using var factory = WithGrid("RabbitMQ");

        Assert.IsType<RabbitMqGridBackend>(factory.Services.GetRequiredService<IGridBackend>());
    }

    [Fact]
    public async Task Configuring_Dapr_runs_the_grids_on_Dapr_actors()
    {
        await using var factory = WithGrid("Dapr");

        Assert.IsType<DaprGridBackend>(factory.Services.GetRequiredService<IGridBackend>());
    }

    [Fact]
    public async Task Grids_on_RabbitMQ_are_suspended_when_idle()
    {
        await using var factory = WithGrid("RabbitMQ");

        Assert.Contains(factory.Services.GetServices<IHostedService>(), service => service is IdleGameSuspender);
    }

    [Fact]
    public async Task Grids_on_Dapr_actors_are_never_suspended_for_being_idle()
    {
        // They hold no broker resources, so an idle grid costs only its actors' state (ADR 0005).
        await using var factory = WithGrid("Dapr");

        Assert.DoesNotContain(factory.Services.GetServices<IHostedService>(), service => service is IdleGameSuspender);
    }

    [Fact]
    public async Task An_unknown_grid_stops_the_Api_from_starting()
    {
        await using var factory = WithGrid("CarrierPigeons");

        var exception = Assert.ThrowsAny<Exception>(() => factory.Services);

        Assert.Contains("CarrierPigeons", exception.Message);
    }

    private static WebApplicationFactory<Program> WithGrid(string grid) =>
        new ApiFactory().WithWebHostBuilder(builder => builder.UseSetting("Grid", grid));
}
