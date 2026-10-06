using SudokuDaprActors.Core;

namespace SudokuDaprActors.Cells.Contracts;

/// <summary>A row, column or box, with its topic, which every grid shares: <c>row-3</c>, <c>column-5</c> or <c>box-2</c>.</summary>
public sealed record Unit(UnitKind Kind, int Number)
{
    /// <summary>All 27 units: rows, then columns, then boxes, each 1–9.</summary>
    public static IReadOnlyList<Unit> All { get; } =
        [.. Enum.GetValues<UnitKind>().SelectMany(kind => Enumerable.Range(1, 9).Select(number => new Unit(kind, number)))];

    public string Topic => $"{Kind.ToString().ToLowerInvariant()}-{Number}";

    /// <summary>Its nine cells, row by row. Boxes are numbered 1–9 row by row from the top left.</summary>
    public IEnumerable<(int Row, int Column)> Cells => Kind switch
    {
        UnitKind.Row => Enumerable.Range(1, 9).Select(column => (Number, column)),
        UnitKind.Column => Enumerable.Range(1, 9).Select(row => (row, Number)),
        UnitKind.Box => Enumerable.Range(0, 9).Select(index =>
            ((Number - 1) / 3 * 3 + index / 3 + 1, (Number - 1) % 3 * 3 + index % 3 + 1)),
        _ => throw new InvalidOperationException($"A unit of unknown kind {Kind}."),
    };

    /// <summary>The row, column and box of a cell, in that order.</summary>
    public static IReadOnlyList<Unit> Of(int row, int column) =>
        [new(UnitKind.Row, row), new(UnitKind.Column, column), new(UnitKind.Box, (row - 1) / 3 * 3 + (column - 1) / 3 + 1)];
}
