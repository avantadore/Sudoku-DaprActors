using System.Text.Json.Serialization;
using Dapr.Actors.Client;
using Scalar.AspNetCore;
using SudokuDaprActors.Api;
using SudokuDaprActors.Cells.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Grids run on Dapr actors in the Cells service. Typed proxies talk JSON to the actors, which every caller agrees on
// (ADR 0005).
builder.Services.AddSingleton<IActorProxyFactory>(new ActorProxyFactory(new ActorProxyOptions { UseJsonSerialization = true }));
builder.Services.AddSingleton<Cascades>();
builder.Services.AddSingleton<Steps>();
builder.Services.AddSingleton<IGridBackend, DaprGridBackend>();
builder.Services.AddSingleton<GameStore>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// The Api hears each step, one at a time and in order, to hand it to the game's watchers, and when a cascade is over,
// to complete the waiting move.
app.UseCloudEvents();
app.MapSubscribeHandler();
app.MapPost("/steps", (StepMessage step, Steps steps) => steps.HearAsync(step))
    .WithTopic(Topics.InOrder, Topics.Steps);
app.MapPost("/cascades", (CascadeOver over, Cascades cascades) => cascades.Hear(over))
    .WithTopic(Topics.PubSub, Topics.Cascades);

app.MapGameEndpoints();
app.MapDefaultEndpoints();

app.Run();
