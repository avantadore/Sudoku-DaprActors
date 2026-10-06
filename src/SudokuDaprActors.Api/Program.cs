using System.Text.Json.Serialization;
using Dapr.Actors.Client;
using RabbitMQ.Client;
using Scalar.AspNetCore;
using SudokuDaprActors.Api;
using SudokuDaprActors.Cells.Contracts;
using SudokuDaprActors.Core;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// What grids run on, chosen by the Grid setting.
var grid = builder.Configuration["Grid"] ?? "RabbitMQ";
switch (grid)
{
    case "RabbitMQ":
        builder.AddRabbitMQClient("messaging");
        builder.Services.AddSingleton<IGridBackend>(services =>
            new RabbitMqGridBackend(services.GetRequiredService<IConnectionFactory>()));
        break;
    case "Dapr":
        // Typed proxies talk JSON to the actors, which every caller agrees on (ADR 0005).
        builder.Services.AddSingleton<IActorProxyFactory>(new ActorProxyFactory(new ActorProxyOptions { UseJsonSerialization = true }));
        builder.Services.AddSingleton<Cascades>();
        builder.Services.AddSingleton<IGridBackend, DaprGridBackend>();
        break;
    case var unknown:
        throw new InvalidOperationException($"Grid '{unknown}' is not known. Grid must be RabbitMQ, which is the default, or Dapr.");
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(services => new GameStore(
    services.GetRequiredService<IGridBackend>(), services.GetRequiredService<TimeProvider>(), GameStore.DefaultIdleAfter));
builder.Services.AddHostedService<IdleGameSuspender>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

if (grid == "Dapr")
{
    // The Api hears when a cascade is over, to complete the waiting move.
    app.UseCloudEvents();
    app.MapSubscribeHandler();
    app.MapPost("/cascades", (CascadeOver over, Cascades cascades) => cascades.Hear(over))
        .WithTopic(Topics.PubSub, Topics.Cascades);
}

app.MapGameEndpoints();
app.MapDefaultEndpoints();

app.Run();
