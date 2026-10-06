using SudokuDaprActors.Cells;
using SudokuDaprActors.Cells.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddDaprClient();
builder.Services.AddActors(options =>
{
    // Typed proxies talk JSON to the actors, which every caller agrees on (ADR 0005).
    options.UseJsonSerialization = true;
    options.Actors.RegisterActor<CellActor>(ActorIds.CellType);
    options.Actors.RegisterActor<GridActor>(ActorIds.GridType);
});
builder.Services.AddSingleton<UnitSubscriber>();

var app = builder.Build();

app.UseCloudEvents();
app.MapActorsHandlers();
app.MapSubscribeHandler();

// One subscriber per unit topic, shared by every grid.
foreach (var unit in Unit.All)
{
    app.MapPost($"/units/{unit.Topic}", (UnitMessage message, UnitSubscriber subscriber) => subscriber.HearAsync(unit, message))
        .WithTopic(Topics.PubSub, unit.Topic);
}

app.MapDefaultEndpoints();

app.Run();
