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
        // A grid holds streams and channels on the broker, so an idle game's grid is suspended (ADR 0004).
        builder.Services.AddHostedService<IdleGameSuspender>();
        break;
    case "Dapr":
        // Typed proxies talk JSON to the actors, which every caller agrees on (ADR 0005).
        builder.Services.AddSingleton<IActorProxyFactory>(new ActorProxyFactory(new ActorProxyOptions { UseJsonSerialization = true }));
        builder.Services.AddSingleton<Cascades>();
        builder.Services.AddSingleton<Steps>();
        // No grid is suspended for being idle: it holds nothing on the broker, and a replay or the end of its game
        // forgets it (ADR 0005).
        builder.Services.AddSingleton<IGridBackend, DaprGridBackend>();
        break;
    case var unknown:
        throw new InvalidOperationException($"Grid '{unknown}' is not known. Grid must be RabbitMQ, which is the default, or Dapr.");
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(services => new GameStore(
    services.GetRequiredService<IGridBackend>(), services.GetRequiredService<TimeProvider>(), GameStore.DefaultIdleAfter));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

if (grid == "Dapr")
{
    // The Api hears each step, one at a time and in order, to hand it to the game's watchers, and when a cascade is
    // over, to complete the waiting move.
    app.UseCloudEvents();
    app.MapSubscribeHandler();
    app.MapPost("/steps", (StepMessage step, Steps steps) => steps.HearAsync(step))
        .WithTopic(Topics.InOrder, Topics.Steps);
    app.MapPost("/cascades", (CascadeOver over, Cascades cascades) => cascades.Hear(over))
        .WithTopic(Topics.PubSub, Topics.Cascades);
}

app.MapGameEndpoints();
app.MapDefaultEndpoints();

app.Run();
