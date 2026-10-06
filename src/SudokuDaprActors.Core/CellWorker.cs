using RabbitMQ.Client;

namespace SudokuDaprActors.Core;

/// <summary>
/// One cell, as a worker reading its row, column and box streams. It routes what it hears to the cell's rules and
/// carries out what they decide: it answers moves, publishes steps, and announces events on all three streams. It
/// skips whatever else it hears: commands for other cells, its own events, and candidates its peers lost.
/// </summary>
internal sealed class CellWorker : IAsyncDisposable
{
    private readonly CellRules _rules;
    private readonly Lock _state = new(); // the worker writes while the game may take a snapshot
    private readonly Switchboard _switchboard;
    private Mailbox? _mailbox;
    private string[] _units = [];

    public CellWorker(int row, int column, Switchboard switchboard)
    {
        _rules = new CellRules(row, column);
        _switchboard = switchboard;
    }

    public int Row => _rules.Row;

    public int Column => _rules.Column;

    /// <summary>The box this cell belongs to, 1–9 row by row from the top left.</summary>
    public int Box => Topology.BoxOf(Row, Column);

    /// <summary>Starts reading the cell's row, column and box streams, in that order.</summary>
    public async Task StartAsync(IConnection connection, string row, string column, string box)
    {
        _units = [row, column, box];
        _mailbox = await Mailbox.OpenAsync(connection, _switchboard);
        foreach (var unit in _units)
        {
            await _mailbox.ReadAsync(unit, HandleAsync);
        }
    }

    public bool IsFilled
    {
        get
        {
            lock (_state)
            {
                return _rules.Digit is not null;
            }
        }
    }

    public Cell Snapshot()
    {
        lock (_state)
        {
            return _rules.Snapshot();
        }
    }

    public ValueTask DisposeAsync() => _mailbox?.DisposeAsync() ?? ValueTask.CompletedTask;

    private Mailbox Mailbox => _mailbox ?? throw new InvalidOperationException($"Cell ({Row}, {Column}) has not started.");

    private Task HandleAsync(object message) => message switch
    {
        Command.PlaceMove move when IsThis(move) => PlaceMoveAsync(move),
        Command.PlaceDeduction deduction when IsThis(deduction) => PlaceDeductionAsync(deduction.Digit),
        Event.Filled filled when !IsThis(filled) => CarryOutAsync(Locked(() => _rules.Eliminate(filled.Digit))),
        _ => Task.CompletedTask,
    };

    private bool IsThis(IAboutCell message) => message.Row == Row && message.Column == Column;

    private async Task PlaceMoveAsync(Command.PlaceMove move)
    {
        var reply = move.ReplyTo ?? throw new InvalidOperationException($"{move} has no reply address.");
        var (outcome, reaction) = Locked(() => _rules.Move(move.Digit));
        await Mailbox.ReplyAsync(reply, outcome);
        await CarryOutAsync(reaction);
    }

    // Checked and placed as one with the grid's contradiction flag, so no deduction slips in once a contradiction has
    // been decided, and a stale one is not counted.
    private async Task PlaceDeductionAsync(int digit)
    {
        Reaction? reaction = null;
        bool Place()
        {
            reaction = Locked(() => _rules.Deduce(digit));
            return reaction is not null;
        }

        if (_switchboard.Deduce(Place))
        {
            await CarryOutAsync(reaction!);
        }
    }

    private T Locked<T>(Func<T> rule)
    {
        lock (_state)
        {
            return rule();
        }
    }

    // A naked single goes to the cell itself on its row stream, so that whatever it has already heard is handled first.
    private Task CarryOutAsync(Reaction reaction) => Mailbox.CarryOutAsync(reaction, deductionsTo: _units[0], announceTo: _units);
}
