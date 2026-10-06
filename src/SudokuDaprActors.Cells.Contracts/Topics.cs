namespace SudokuDaprActors.Cells.Contracts;

/// <summary>The Dapr pub/sub components every grid talks on, and their topics besides the units' (ADR 0005).</summary>
public static class Topics
{
    /// <summary>The pub/sub component, <c>pubsub.rabbitmq</c>, which hands its subscribers messages in parallel.</summary>
    public const string PubSub = "pubsub";

    /// <summary>
    /// The same broker as <see cref="PubSub"/>, but a publish returns only once the broker has queued the message, and
    /// a subscriber is handed one message at a time, in the order they were queued. So a step published before the
    /// events that cause later steps reaches the subscriber before those steps: cause before effect.
    /// </summary>
    public const string InOrder = "pubsub-in-order";

    /// <summary>Where the grid actor announces that a cascade is over, for the Api to complete the waiting move.</summary>
    public const string Cascades = "cascades";

    /// <summary>
    /// Where cells and units publish each step, on <see cref="InOrder"/>, for the Api to hand to the watchers of the
    /// game whose current grid it came from.
    /// </summary>
    public const string Steps = "steps";
}
