using FluentAssertions;
using FluentValidation;
using FuelFlow.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace FuelFlow.UnitTests.Middleware;

public sealed class ValidationFilterTests
{
    private readonly ValidateAttribute _attribute = new();

    private static async Task InvokeAndCaptureResultAsync(
        ValidateAttribute attribute,
        ActionExecutingContext context,
        bool markNextCalled)
    {
        await attribute.OnActionExecutionAsync(context, () =>
        {
            if (markNextCalled)
                context.HttpContext.Items["next-called"] = true;
            var ctx = new ActionContext(context.HttpContext, context.RouteData, context.ActionDescriptor);
            return Task.FromResult(new ActionExecutedContext(ctx, context.Filters, controller: null!));
        });
    }

    [Fact]
    public async Task OnActionExecutionAsync_ShouldProceed_WhenValidationPasses()
    {
        var services = new ServiceCollection();
        services.AddScoped<IValidator<CreateOrderCommand>, CreateOrderCommandValidator>();
        using var provider = services.BuildServiceProvider();

        var context = CreateActionExecutingContext(
            provider,
            new Dictionary<string, object?> { ["command"] = new CreateOrderCommand("Fuel", 3) });

        await InvokeAndCaptureResultAsync(_attribute, context, markNextCalled: true);

        context.HttpContext.Items.ContainsKey("next-called").Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task OnActionExecutionAsync_ShouldShortCircuit_WhenValidationFails()
    {
        var services = new ServiceCollection();
        services.AddScoped<IValidator<CreateOrderCommand>, CreateOrderCommandValidator>();
        using var provider = services.BuildServiceProvider();

        var context = CreateActionExecutingContext(
            provider,
            new Dictionary<string, object?> { ["command"] = new CreateOrderCommand("", 0) });

        await InvokeAndCaptureResultAsync(_attribute, context, markNextCalled: true);

        context.HttpContext.Items.ContainsKey("next-called").Should().BeFalse();
        var result = context.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var problemDetails = result.Value.Should().BeOfType<ValidationProblemDetails>().Subject;

        problemDetails.Errors.Should().ContainKeys("Name", "Quantity");
        problemDetails.Errors["Name"].Should().NotBeEmpty();
        problemDetails.Errors["Quantity"].Should().NotBeEmpty();
    }

    private static ActionExecutingContext CreateActionExecutingContext(
        IServiceProvider services,
        IDictionary<string, object?> actionArguments)
    {
        var httpContext = new DefaultHttpContext { RequestServices = services };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

        return new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            actionArguments,
            controller: null!);
    }
}

public sealed record CreateOrderCommand(string? Name, int Quantity);

public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}
