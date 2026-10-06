# Cells are Dapr actors behind unit subscribers

Status: accepted. Supersedes the RabbitMQ streams of ADR 0004, amends ADR 0002 (a grid is no longer a cache) and ADR 0003 (its concurrency, cascade-over and contradiction rules still hold).

The purpose of the project changed from modelling Sudoku with RabbitMQ to modelling it with Dapr actors, so each cell is now a Dapr actor, and so are the unit watchers and the grid. Each grid has 81 `CellActor`s, 27 `UnitActor`s and one `GridActor`, hosted in their own Cells service, with ids such as `{grid}:r3c5`, `{grid}:row-3` and `{grid}`. Messages still travel over RabbitMQ, but through Dapr pub/sub (`pubsub.rabbitmq`), on 27 static topics, `row-1`–`9`, `column-1`–`9` and `box-1`–`9` (boxes row by row from the top left), shared by every grid and carrying the grid id. Each topic has one subscriber in the Cells service. Dapr hands it each message as an HTTP POST, and it routes the message to actors: a command goes to the cell it is for, a `Filled` goes to the cell's 8 peers in the unit, and every event goes to the unit's `UnitActor`. Actors are called through typed proxies (`IActorProxyFactory`, JSON serialization), which talk to the sidecar over HTTP.

The `GridActor` takes over the Switchboard. It owns the contradiction flag and the deduction tally, so a cell asks it before placing a deduction, and the check and the count happen in one actor turn. It also counts deliveries in flight: an actor reports `+k` before it publishes k messages, a subscriber reports `−1` once the actors it called have returned, and at zero the `GridActor` publishes `CascadeOver` with the move's deductions on the `cascades` topic. A move is a direct call to the cell's actor, which returns its `MoveOutcome`, so there is no reply-to. Cells publish steps on one `steps` topic, which the Api fans out to the watchers of the game whose current grid it came from.

## Considered Options

- **Raw RabbitMQ streams next to Dapr actors**: keeps the broker's primitives visible, as ADR 0004 wanted, but then Dapr is only a host for the actors. Rejected, because the purpose is now to model with Dapr.
- **27 topics per grid**: Dapr reads subscriptions at startup, so per-grid topics would need streaming subscriptions created at runtime (gRPC only), and Dapr never deletes their exchanges and queues.
- **A subscriber that relays every message to all nine cells**: the cells would still skip what is not theirs, as they did on their streams, but every skip would cost an actor turn. Routing is a simple rule (the addressed cell, or everyone else in the unit), so the subscriber filters instead. ADR 0004 rejected relaying because it made the topic point-to-point. That no longer applies, because Dapr pub/sub delivers each topic to one subscriber anyway.
- **Unit watchers as in-memory state of the subscriber**: breaks once the Cells service has more than one replica or restarts.
- **A move that returns once it is accepted, with the cascade running on**: rejected, because it breaks the deduction count of a recorded move, lets a replay's next move race the previous cascade, and breaks "after awaiting a move, every step has been read".

## Consequences

- Messages may overtake each other (`concurrencyMode: parallel`), so the rules must tolerate any order, as they did across units already (ADR 0003). A unit may hear a cell's `CandidateLost` before its `Filled` and deduce into a filled cell, which ignores it.
- A cell places its own naked single in the same turn, after asking the `GridActor`, instead of sending itself a `PlaceDeduction`.
- A delivery that fails is a bug: the subscriber reports it to the `GridActor`, the move fails, and the message is dropped rather than redelivered, so the in-flight count never sees a duplicate.
- Actor state lives in the in-memory state store. A grid lives until a replay replaces it or its game is disposed, and then its `GridActor` makes its cells and units remove their state. Grids are no longer suspended when idle (ADR 0004), so a contradicted grid comes back different only after an explicit replay.
- A grid being rebuilt by replay still publishes steps, and the Api drops them, because that grid is not its game's current grid yet.
- Games still live in the Api's memory, so the Api runs as one replica. The Cells service can scale out.
- The rules are plain classes in Core, with no Dapr, and are tested without infrastructure. Everything else is tested end to end through `Aspire.Hosting.Testing`, which needs the Dapr CLI (`dapr init`, or `dapr init --slim`) as well as Docker.
