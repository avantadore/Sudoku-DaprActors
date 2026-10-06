using RabbitMQ.Client;

namespace SudokuDaprActors.Core;

/// <summary>
/// A row, column or box's watcher: a worker reading the unit's stream alongside its nine cells. It never relays:
/// the cells hear each other directly. It routes the events it hears to the unit's rules, sends the hidden singles
/// they find on the unit's stream, and publishes the contradictions. It skips the commands it hears.
/// </summary>
internal sealed class UnitWorker : IAsyncDisposable
{
    private readonly UnitRules _rules;
    private readonly Switchboard _switchboard;
    private Mailbox? _mailbox;

    public UnitWorker(Guid grid, UnitKind kind, int number, IReadOnlyList<(int Row, int Column)> cells, Switchboard switchboard)
    {
        _rules = new UnitRules(kind, number, cells);
        _switchboard = switchboard;
        Stream = Topology.UnitStream(grid, kind, number);
    }

    /// <summary>The unit's stream: its topic, which its cells and this watcher read.</summary>
    public string Stream { get; }

    public async Task StartAsync(IConnection connection)
    {
        _mailbox = await Mailbox.OpenAsync(connection, _switchboard);
        await _mailbox.ReadAsync(Stream, HandleAsync);
    }

    public ValueTask DisposeAsync() => _mailbox?.DisposeAsync() ?? ValueTask.CompletedTask;

    private Task HandleAsync(object message) => message switch
    {
        Event.Filled filled => CarryOutAsync(_rules.Filled(filled.Row, filled.Column)),
        Event.CandidateLost lost => CarryOutAsync(_rules.CandidateLost(lost.Row, lost.Column, lost.Digit)),
        _ => Task.CompletedTask,
    };

    private Task CarryOutAsync(Reaction reaction) => _mailbox!.CarryOutAsync(reaction, deductionsTo: Stream, announceTo: []);
}
