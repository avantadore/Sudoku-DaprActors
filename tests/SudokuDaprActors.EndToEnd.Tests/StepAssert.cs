using System.Text.Json;

namespace SudokuDaprActors.EndToEnd.Tests;

/// <summary>
/// Checking the steps a watcher read. Within a cascade, steps promise only that cause comes before effect (ADR 0003),
/// so everything else is compared as a set. Each step is named as text, so the expected steps read as they would be
/// written.
/// </summary>
internal static class StepAssert
{
    public static string Placement(int row, int column, int digit, string source) => $"{source} {digit} in ({row},{column})";

    public static string Elimination(int row, int column, int digit) => $"({row},{column}) lost {digit}";

    /// <summary>The step as <see cref="Placement"/> or <see cref="Elimination"/> name it, or its raw JSON otherwise.</summary>
    public static string NameOf(JsonElement step) => step.GetProperty("kind").GetString() switch
    {
        "Placement" => Placement(Row(step), Column(step), Digit(step), step.GetProperty("source").GetString()!),
        "Elimination" => Elimination(Row(step), Column(step), Digit(step)),
        _ => step.GetRawText(),
    };

    public static List<string> NamesOf(IEnumerable<JsonElement> steps) => [.. steps.Select(NameOf)];

    public static void SameSet(IEnumerable<string> expected, IEnumerable<JsonElement> actual) =>
        Assert.Equal(expected.Order(StringComparer.Ordinal), NamesOf(actual).Order(StringComparer.Ordinal));

    public static void Before(IReadOnlyList<JsonElement> steps, string cause, string effect)
    {
        var names = NamesOf(steps);
        var causeAt = names.IndexOf(cause);
        var effectAt = names.IndexOf(effect);
        Assert.True(causeAt >= 0, $"{cause} was not read.");
        Assert.True(effectAt >= 0, $"{effect} was not read.");
        Assert.True(causeAt < effectAt, $"{cause}, read at {causeAt}, should come before {effect}, read at {effectAt}.");
    }

    /// <summary>The deductions read, as <see cref="Placement"/> names them.</summary>
    public static IEnumerable<string> DeductionsOf(IEnumerable<JsonElement> steps) =>
        NamesOf(steps.Where(step => step.GetProperty("kind").GetString() == "Placement"
            && step.GetProperty("source").GetString() == "Deduction"));

    private static int Row(JsonElement step) => step.GetProperty("row").GetInt32();

    private static int Column(JsonElement step) => step.GetProperty("column").GetInt32();

    private static int Digit(JsonElement step) => step.GetProperty("digit").GetInt32();
}
