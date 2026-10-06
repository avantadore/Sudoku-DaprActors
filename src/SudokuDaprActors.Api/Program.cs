using System.Text.Json.Serialization;
using RabbitMQ.Client;
using Scalar.AspNetCore;
using SudokuDaprActors.Api;
using SudokuDaprActors.Core;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// What grids run on, chosen by the Grid setting.
switch (builder.Configuration["Grid"] ?? "RabbitMQ")
{
    case "RabbitMQ":
        builder.AddRabbitMQClient("messaging");
        builder.Services.AddSingleton<IGridBackend>(services =>
            new RabbitMqGridBackend(services.GetRequiredService<IConnectionFactory>()));
        break;
    case var unknown:
        throw new InvalidOperationException($"Grid '{unknown}' is not known. Grid must be RabbitMQ, which is the default.");
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

app.MapGameEndpoints();
app.MapDefaultEndpoints();

app.Run();
