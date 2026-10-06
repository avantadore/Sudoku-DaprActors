using Dapr.Actors.Client;
using SudokuDaprActors.Cells.Contracts;

namespace SudokuDaprActors.Cells;

/// <summary>
/// The one subscriber of each unit's topic (ADR 0005). It routes what it hears to actors: a <c>Filled</c> to the
/// cell's 8 peers in the unit. Once they have returned, it reports the delivery handled to the grid's actor. Commands
/// travel on the topics only once units deduce, so there are none to route yet.
/// </summary>
internal sealed class UnitSubscriber(IActorProxyFactory actors, ILogger<UnitSubscriber> logger)
{
    public async Task<IResult> HearAsync(Unit unit, UnitMessage message)
    {
        var grid = actors.CreateActorProxy<IGridActor>(ActorIds.Grid(message.Grid), ActorIds.GridType);
        try
        {
            await Task.WhenAll(Route(unit, message));
        }
        catch (Exception exception)
        {
            // A bug: the move fails, and the message is dropped rather than redelivered, so the count of deliveries
            // in flight never sees it twice.
            logger.LogError(exception, "Delivering {Message} on {Topic} failed.", message, unit.Topic);
            await grid.FailAsync($"Delivering {message} on {unit.Topic} failed: {exception.Message}");
            return Results.Ok(new { status = "DROP" });
        }

        await grid.ReportAsync(-1);
        return Results.Ok();
    }

    private IEnumerable<Task> Route(Unit unit, UnitMessage message) => message.Kind switch
    {
        UnitMessage.Kinds.Filled => unit.Cells
            .Where(cell => cell != (message.Row, message.Column))
            .Select(peer => Cell(message.Grid, peer).EliminateAsync(message.Digit)),
        _ => throw new InvalidOperationException($"A unit message of unknown kind {message.Kind}."),
    };

    private ICellActor Cell(Guid grid, (int Row, int Column) cell) =>
        actors.CreateActorProxy<ICellActor>(ActorIds.Cell(grid, cell.Row, cell.Column), ActorIds.CellType);
}
