using Dapr.Actors;

namespace SudokuDaprActors.Cells.Contracts;

/// <summary>The actor types of the Cells service, and the ids of one grid's actors (ADR 0005).</summary>
public static class ActorIds
{
    public const string CellType = "CellActor";

    public const string GridType = "GridActor";

    /// <summary><c>{grid}</c>.</summary>
    public static ActorId Grid(Guid grid) => new(grid.ToString());

    /// <summary><c>{grid}:r3c5</c>.</summary>
    public static ActorId Cell(Guid grid, int row, int column) => new($"{grid}:r{row}c{column}");

    /// <summary>The grid, row and column of a cell's actor id.</summary>
    public static (Guid Grid, int Row, int Column) ParseCell(ActorId id)
    {
        var text = id.GetId();
        var separator = text.LastIndexOf(":r", StringComparison.Ordinal);
        var cell = text[(separator + 2)..].Split('c');
        return (Guid.Parse(text[..separator]), int.Parse(cell[0]), int.Parse(cell[1]));
    }
}
