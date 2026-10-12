using FluentAssertions;
using FuelFlow.Middleware;
using FuelFlow.SharedKernel.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FuelFlow.UnitTests.Middleware;

/// <summary>
/// #246's detector only pays off if a concurrency collision stays quiet. A 500 would fire the
/// operator alert on every hit, and the whole point of the token is to be hit - the alert would then
/// train everyone to ignore it. 409 is also the honest answer: the row changed underneath the
/// writer, and retrying is a reasonable thing for the caller to do.
/// </summary>
public sealed class GlobalExceptionHandlerConcurrencyTests
{
    private static GlobalExceptionHandler CreateHandler()
        // NullLogger rather than Mock.Of: the handler type is internal, and Moq cannot build a proxy
        // for ILogger<InternalType> without InternalsVisibleTo for its own assembly.
        => new(
            NullLogger<GlobalExceptionHandler>.Instance,
            NotificationDispatcher.Disabled);

    [Fact]
    public async Task A_Concurrency_Collision_Answers_409_And_Does_Not_Alert_The_Operator()
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var handler = CreateHandler();

        var handled = await handler.TryHandleAsync(
            context,
            new DbUpdateConcurrencyException("stale row"),
            CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);

        // Under 500, so the handler's own "only genuine server faults are worth a message" gate
        // never opens. Asserted through the status: the alert is fired off statusCode >= 500.
        context.Response.StatusCode.Should().BeLessThan(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task A_Genuine_Fault_Still_Answers_500()
    {
        // The 409 branch must not swallow real faults into a "try again" response, which would be a
        // lie about a bug that needs attention. A bare Exception is the one that lands on the
        // catch-all 500 - InvalidOperationException deliberately answers 400, because EF and Npgsql
        // throw it and its text must not reach an anonymous caller.
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var handler = CreateHandler();

        await handler.TryHandleAsync(context, new Exception("boom"), CancellationToken.None);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }
}