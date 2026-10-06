namespace SudokuDaprActors.Core;

/// <summary>
/// The rules of one row, column or box, with no broker and no I/O. The unit tracks which of its cells can still hold
/// each digit: when only one empty cell can, the digit is a hidden single there.
/// </summary>
internal sealed class UnitRules
{
    private readonly IReadOnlyList<(int Row, int Column)> _cells;

    /// <summary>For each digit 1–9, the indexes of the cells that can still hold it. A filled cell holds its digit.</summary>
    private readonly HashSet<int>[] _holders = [.. Enumerable.Range(0, 9).Select(_ => Enumerable.Range(0, 9).ToHashSet())];

    /// <summary>Which of the cells, by index, the unit has heard were filled.</summary>
    private readonly bool[] _filled = new bool[9];

    public UnitRules(UnitKind kind, int number, IReadOnlyList<(int Row, int Column)> cells)
    {
        Kind = kind;
        Number = number;
        _cells = cells;
    }

    /// <summary>The unit as <paramref name="snapshot"/> left it, as when its state was saved and is read back.</summary>
    public static UnitRules From(UnitKind kind, int number, IReadOnlyList<(int Row, int Column)> cells, UnitSnapshot snapshot)
    {
        var rules = new UnitRules(kind, number, cells);
        for (var digit = 1; digit <= 9; digit++)
        {
            rules._holders[digit - 1].IntersectWith(snapshot.Holders[digit - 1]);
        }

        foreach (var index in snapshot.Filled)
        {
            rules._filled[index] = true;
        }

        return rules;
    }

    public UnitKind Kind { get; }

    public int Number { get; }

    public UnitSnapshot Snapshot() =>
        new([.. _holders.Select(holders => (IReadOnlyList<int>)[.. holders.Order()])], [.. Enumerable.Range(0, 9).Where(index => _filled[index])]);

    /// <summary>The cell was filled, so it is no hidden single for the digit it holds.</summary>
    public Reaction Filled(int row, int column)
    {
        _filled[IndexOf(row, column)] = true;
        return Reaction.None;
    }

    /// <summary>
    /// The cell lost <paramref name="digit"/> as a candidate. It may be heard before the cell's own <c>Filled</c>, or
    /// before the <c>Filled</c> of the peer that made it lose the digit: then the unit may deduce into a cell already
    /// filled, which ignores the deduction.
    /// </summary>
    public Reaction CandidateLost(int row, int column, int digit)
    {
        var holders = _holders[digit - 1];
        if (!holders.Remove(IndexOf(row, column)))
        {
            return Reaction.None;
        }

        return holders.Count switch
        {
            0 => new Reaction { Contradiction = new Step.Contradiction.NoCellForDigit(Kind, Number, digit) },
            1 when holders.Single() is var only && !_filled[only] =>
                new Reaction { Deduction = new Command.PlaceDeduction(_cells[only].Row, _cells[only].Column, digit) },
            _ => Reaction.None,
        };
    }

    private int IndexOf(int row, int column)
    {
        for (var index = 0; index < 9; index++)
        {
            if (_cells[index] == (row, column))
            {
                return index;
            }
        }

        throw new ArgumentException($"Cell ({row}, {column}) is not in {Kind.ToString().ToLowerInvariant()} {Number}.");
    }
}
