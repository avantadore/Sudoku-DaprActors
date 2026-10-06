using System.Net.ServerSentEvents;
using System.Text.Json;

namespace SudokuDaprActors.EndToEnd.Tests;

/// <summary>
/// One watcher of a game's steps, reading its Server-Sent Events stream: a <c>step</c> event per step, and a
/// <c>move-complete</c> event after the last step of each move.
/// </summary>
internal sealed class StepStream : IAsyncDisposable
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpResponseMessage _response;
    private readonly IAsyncEnumerator<SseItem<string>> _events;

    private StepStream(HttpResponseMessage response, IAsyncEnumerator<SseItem<string>> events)
    {
        _response = response;
        _events = events;
    }

    /// <summary>Starts watching the game. Once this returns, the watcher reads every step from the next move on.</summary>
    public static async Task<StepStream> OpenAsync(HttpClient api, string id)
    {
        var response = await api.GetAsync($"/games/{id}/steps", HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        return new StepStream(response, SseParser.Create(body).EnumerateAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator());
    }

    /// <summary>The steps of the next move, in the order they were read, up to the end of its steps.</summary>
    public async Task<List<JsonElement>> ReadMoveAsync()
    {
        List<JsonElement> steps = [];
        while (true)
        {
            if (!await _events.MoveNextAsync().AsTask().WaitAsync(ReadTimeout, TestContext.Current.CancellationToken))
            {
                throw new InvalidOperationException("The steps stream ended in the middle of a move.");
            }

            switch (_events.Current.EventType)
            {
                case "step":
                    steps.Add(JsonDocument.Parse(_events.Current.Data).RootElement);
                    break;
                case "move-complete":
                    return steps;
                case var other:
                    throw new InvalidOperationException($"The steps stream sent an event of unknown type '{other}'.");
            }
        }
    }

    /// <summary>Whether another event arrives within <paramref name="wait"/>. Only as the last read of the stream.</summary>
    public async Task<bool> ReadsAnythingWithinAsync(TimeSpan wait)
    {
        var next = _events.MoveNextAsync().AsTask();
        return await Task.WhenAny(next, Task.Delay(wait, TestContext.Current.CancellationToken)) == next && await next;
    }

    public async ValueTask DisposeAsync()
    {
        _response.Dispose();
        try
        {
            await _events.DisposeAsync();
        }
        catch (Exception exception) when (exception is NotSupportedException or IOException or OperationCanceledException or ObjectDisposedException)
        {
            // A read was still pending, as after ReadsAnythingWithinAsync, and the stream was closed under it.
        }
    }

    /// <summary>A placement as the stream sends it.</summary>
    public static bool IsPlacement(JsonElement step, int row, int column, int digit, string source) =>
        step.GetProperty("kind").GetString() == "Placement" && Is(step, row, column, digit)
        && step.GetProperty("source").GetString() == source;

    /// <summary>An elimination as the stream sends it.</summary>
    public static bool IsElimination(JsonElement step, int row, int column, int digit) =>
        step.GetProperty("kind").GetString() == "Elimination" && Is(step, row, column, digit);

    public static bool IsContradiction(JsonElement step) =>
        step.GetProperty("kind").GetString() is "NoCandidateForCell" or "NoCellForDigit";

    private static bool Is(JsonElement step, int row, int column, int digit) =>
        step.GetProperty("row").GetInt32() == row && step.GetProperty("column").GetInt32() == column
        && step.GetProperty("digit").GetInt32() == digit;
}
