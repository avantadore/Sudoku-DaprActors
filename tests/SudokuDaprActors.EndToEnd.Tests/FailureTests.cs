using System.Diagnostics;
using System.Net;
using Aspire.Hosting;
using Aspire.Hosting.Testing;

namespace SudokuDaprActors.EndToEnd.Tests;

/// <summary>
/// A delivery that fails is a bug: the subscriber reports it to the grid's actor, which ends the cascade, so the move
/// fails rather than waits out its timeout (ADR 0005).
/// </summary>
public class FailureTests(FailureTests.FailingBox9 app) : IClassFixture<FailureTests.FailingBox9>
{
    [Fact]
    public async Task A_failing_delivery_fails_the_move_instead_of_hanging_it()
    {
        var id = await app.Api.CreateGameAsync();
        var clock = Stopwatch.StartNew();

        // Filling (9,9) announces it on box 9's topic, whose every delivery fails.
        var response = await app.Api.MoveAsync(id, 9, 9, 1);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), $"The move took {clock.Elapsed} to fail.");
    }

    [Fact]
    public async Task A_move_whose_cascade_reaches_no_failing_delivery_completes()
    {
        var id = await app.Api.CreateGameAsync();

        var response = await app.Api.MoveAsync(id, 1, 1, 1);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>The whole app, but every delivery on box 9's topic fails.</summary>
    public sealed class FailingBox9 : App
    {
        protected override void Configure(IDistributedApplicationTestingBuilder builder) =>
            Project(builder, "cells").WithEnvironment("FailingTopic", "box-9");
    }
}
