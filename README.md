# Sudoku-DaprActors

Sudoku on a grid that propagates its own constraints through RabbitMQ: every row, column and box is a stream that its nine cells talk on, as a topic. A filled cell announces it on its row, column and box, and its peers hear it and eliminate the digit themselves. See [GLOSSARY.md](GLOSSARY.md) for the domain language and [docs/adr/](docs/adr/) for the decisions behind it, in particular [ADR 0004](docs/adr/0004-units-are-rabbitmq-streams.md).

## Streams

Each grid has 27 unit streams, and each game one steps stream:

| Stream | What travels on it |
| --- | --- |
| `sudoku.grid.<grid>.row.1` … `row.9` | Row 1–9: its cells' `Filled` and `CandidateLost` events, and commands to its cells |
| `sudoku.grid.<grid>.column.1` … `column.9` | Column 1–9: the same |
| `sudoku.grid.<grid>.box.1` … `box.9` | Box 1–9, row by row from the top left: the same |
| `sudoku.game.<game>.steps` | Each move and the steps of its cascade, for whoever watches the game |

Open the RabbitMQ management UI from the Aspire dashboard to watch them during a cascade.

## Structure

| Project | Purpose |
| --- | --- |
| `src/SudokuDaprActors.AppHost` | Aspire AppHost: orchestrates RabbitMQ (with the management plugin), the Dapr placement service, the Cells service and the Api with their Dapr sidecars, and Web |
| `src/SudokuDaprActors.ServiceDefaults` | Aspire service defaults (telemetry, health, service discovery, resilience) |
| `src/SudokuDaprActors.Core` | The domain: games, grids, the rules of cells and units, and constraint propagation by cells and unit watchers on RabbitMQ streams |
| `src/SudokuDaprActors.Cells` | The Cells service: cell and grid Dapr actors, and the subscribers of the unit topics (ADR 0005) |
| `src/SudokuDaprActors.Cells.Contracts` | The actor interfaces and messages the Api and the Cells service share |
| `src/SudokuDaprActors.Api` | REST API (Minimal APIs, OpenAPI, Scalar) |
| `src/SudokuDaprActors.Web` | Blazor Web App (Interactive Server), talks to the Api |
| `tests/SudokuDaprActors.Core.Tests` | xUnit v3 tests for Core, against a real broker |
| `tests/SudokuDaprActors.Api.Tests` | xUnit v3 tests for the Api, over HTTP and on its game store, against a real broker |
| `tests/SudokuDaprActors.Web.Tests` | xUnit v3 tests for the Web's API client and display mapping, and that its enums match Core's |
| `tests/SudokuDaprActors.EndToEnd.Tests` | xUnit v3 tests of the whole app on the Dapr grid, started with Aspire.Hosting.Testing |

## Running

Requires the .NET 10 SDK, the Aspire CLI (`dotnet tool install -g Aspire.Cli`), Docker (Engine API 1.44 or later, which is Docker Desktop 4.27+) and the [Dapr CLI](https://docs.dapr.io/getting-started/install-dapr-cli/), initialised with `dapr init` or `dapr init --slim`. Either is enough, because the app runs its own placement service.

```sh
aspire run
```

The Aspire dashboard starts the RabbitMQ and placement containers, the Cells service and the Api with their Dapr sidecars, and links to the RabbitMQ management UI, the Api (Scalar UI at `/scalar`) and Web.

A game talks to its grid only through `IGrid`, so the Api's `Grid` setting chooses what grids run on: `RabbitMQ`, the default, or `Dapr`, which the AppHost sets.

`GET /games/{id}/steps` streams a game's steps as Server-Sent Events from the moment it is opened: a `step` event for each placement, elimination or contradiction, and a `move-complete` event after the last step of each move. Web reads it to show each cascade as it runs.

## Testing

```sh
dotnet test
```

The end-to-end tests start the whole app, as `aspire run` does, so they need the Dapr CLI as well as Docker. The Core and Api tests start a RabbitMQ container with Testcontainers. To use a broker that is already running instead, set `SUDOKU_TEST_RABBITMQ` to its connection string, for example `amqp://guest:guest@localhost:5672/`.
