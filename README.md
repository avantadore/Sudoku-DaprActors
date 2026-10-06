# Sudoku-DaprActors

Sudoku on a grid that propagates its own constraints through Dapr actors: every cell is an actor, and every row, column and box is a topic that its nine cells talk on. A filled cell announces it on its row, column and box, and its peers hear it and eliminate the digit themselves. See [GLOSSARY.md](GLOSSARY.md) for the domain language and [docs/adr/](docs/adr/) for the decisions behind it, in particular [ADR 0005](docs/adr/0005-cells-are-dapr-actors-behind-unit-subscribers.md).

## Topics

Messages travel over RabbitMQ through Dapr pub/sub. Every grid shares the same topics, and each message carries its grid's id:

| Topic | Component | What travels on it |
| --- | --- | --- |
| `row-1` … `row-9` | `pubsub` | Row 1–9: its cells' `Filled` and `CandidateLost` events, and the deductions its unit sends to its cells |
| `column-1` … `column-9` | `pubsub` | Column 1–9: the same |
| `box-1` … `box-9` | `pubsub` | Box 1–9, row by row from the top left: the same |
| `cascades` | `pubsub` | That a move's cascade is over, with how many deductions it made, for the Api to complete the move |
| `steps` | `pubsub-in-order` | Each move and the steps of its cascade, cause before effect, for the Api to hand to the game's watchers |

Dapr publishes each topic to a RabbitMQ exchange of its name. Open the RabbitMQ management UI from the Aspire dashboard to watch them during a cascade.

## The Cells service

The Cells service hosts each grid's actors: 81 `CellActor`s, 27 `UnitActor`s and one `GridActor`, with ids such as `{grid}:r3c5`, `{grid}:row-3` and `{grid}`. Each unit topic has one subscriber there, which routes each message to actors: a deduction to the cell it is for, a `Filled` to the cell's 8 peers in the unit, and every event to the unit's actor. The `GridActor` owns the contradiction flag and the deduction tally, and counts the deliveries in flight, so it knows when a cascade is over. A move is a direct call to the cell's actor. Actor state lives in Dapr's in-memory state store.

The Api holds the games, their move histories and their watchers in memory, so it runs as one replica. The Cells service can scale out.

## Structure

| Project | Purpose |
| --- | --- |
| `src/SudokuDaprActors.AppHost` | Aspire AppHost: orchestrates RabbitMQ (with the management plugin), the Dapr placement service, the Cells service and the Api with their Dapr sidecars, and Web |
| `src/SudokuDaprActors.ServiceDefaults` | Aspire service defaults (telemetry, health, service discovery, resilience) |
| `src/SudokuDaprActors.Core` | The rules of cells and units, and the messages and steps they produce, with no Dapr or RabbitMQ |
| `src/SudokuDaprActors.Cells` | The Cells service: cell, unit and grid Dapr actors, and the subscribers of the unit topics |
| `src/SudokuDaprActors.Cells.Contracts` | The actor interfaces and messages the Api and the Cells service share |
| `src/SudokuDaprActors.Api` | REST API (Minimal APIs, OpenAPI, Scalar): the games, their move histories and the game store, on the Dapr grid |
| `src/SudokuDaprActors.Web` | Blazor Web App (Interactive Server), talks to the Api |
| `tests/SudokuDaprActors.Core.Tests` | xUnit v3 tests of the cell and unit rules, with no infrastructure |
| `tests/SudokuDaprActors.Web.Tests` | xUnit v3 tests for the Web's API client and display mapping, and that its enums match Core's |
| `tests/SudokuDaprActors.EndToEnd.Tests` | xUnit v3 tests of the whole app over the Api's HTTP surface, started with Aspire.Hosting.Testing |

## Running

Requires:

- the .NET 10 SDK;
- the Aspire CLI (`dotnet tool install -g Aspire.Cli`);
- Docker (Engine API 1.44 or later, which is Docker Desktop 4.27+);
- the [Dapr CLI](https://docs.dapr.io/getting-started/install-dapr-cli/), initialised with `dapr init` or `dapr init --slim`. Either is enough, because the app runs its own placement service.

```sh
aspire run
```

The Aspire dashboard starts the RabbitMQ and placement containers, the Cells service and the Api with their Dapr sidecars, and links to the RabbitMQ management UI, the Api (Scalar UI at `/scalar`) and Web.

`GET /games/{id}/steps` streams a game's steps as Server-Sent Events from the moment it is opened: a `step` event for each placement, elimination or contradiction, and a `move-complete` event after the last step of each move. Web reads it to show each cascade as it runs.

## Testing

```sh
dotnet test
```

The Core tests need nothing else. The end-to-end tests start the whole app, as `aspire run` does, so they need Docker and the Dapr CLI too.
