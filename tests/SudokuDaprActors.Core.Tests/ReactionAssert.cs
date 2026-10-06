namespace SudokuDaprActors.Core.Tests;

/// <summary>Checking what a cell's or a unit's rules decided, part by part and in order.</summary>
internal static class ReactionAssert
{
    public static void Equal(Reaction expected, Reaction actual)
    {
        Assert.Equal(expected.Steps, actual.Steps);
        Assert.Equal(expected.Contradiction, actual.Contradiction);
        Assert.Equal(expected.Deduction, actual.Deduction);
        Assert.Equal(expected.Announcements, actual.Announcements);
    }

    public static void None(Reaction actual) => Equal(Reaction.None, actual);
}
