namespace SudokuDaprActors.Cells.Contracts;

/// <summary>The Dapr pub/sub component every grid talks on, and its topics besides the units' (ADR 0005).</summary>
public static class Topics
{
    /// <summary>The pub/sub component, <c>pubsub.rabbitmq</c>.</summary>
    public const string PubSub = "pubsub";

    /// <summary>Where the grid actor announces that a cascade is over, for the Api to complete the waiting move.</summary>
    public const string Cascades = "cascades";
}
