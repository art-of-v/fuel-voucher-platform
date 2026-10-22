using System.Reflection;
using FluentAssertions;
using FuelFlow.Middleware;
using FuelFlow.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace FuelFlow.UnitTests.Architecture;

/// <summary>
/// The guard on the request-body ceiling (planning finding S-15, issue #624).
/// </summary>
/// <remarks>
/// <para>
/// The ceiling used to be 25 MB <i>globally</i>, because 25 MB is what a voucher-PDF import
/// needs. A global ceiling is a ceiling on every route, so every endpoint - the anonymous ones
/// included - could be handed 25 MB of body, and the global rate limit of 300 requests a minute
/// per source address turns that into about 7.5 GB a minute of buffering against a container
/// with an 896 MB memory limit.
/// </para>
/// <para>
/// The ceiling is now what a JSON body needs, and the routes that accept a file raise it for
/// themselves. This guard is what keeps that true: it pins the global number, and it checks
/// every route that takes a file - so a new upload route cannot silently inherit the 1 MiB
/// ceiling (which would break it) or raise the ceiling past the file ceiling (which would open
/// the hole the finding is about).
/// </para>
/// </remarks>
public sealed class RequestSizeLimitGuardTests
{
    /// <summary>
    /// The global ceiling: one mebibyte. Asserted here so it cannot be raised silently - the
    /// number is the whole point of the finding.
    /// </summary>
    [Fact]
    public void The_Global_Ceiling_Is_One_Mebibyte()
    {
        RequestSizeLimits.DefaultMaxRequestBodySize.Should().Be(1_048_576,
            "a JSON body needs a megabyte, not the 25 MB a voucher-PDF import needs");

        RequestSizeLimits.FileUploadMaxRequestBodySize.Should().Be(25_000_000,
            "the voucher-PDF import is sized for a 25 MB file, and that is the largest body any route accepts");
    }

    [Fact]
    public void Every_Route_That_Accepts_A_File_Declares_Its_Own_Ceiling()
    {
        var offenders = new List<string>();

        foreach (var (route, method) in FileRoutes())
        {
            var bodyLimit = BodyCeilingOf(method);
            if (bodyLimit is null)
            {
                offenders.Add($"{route}: declares no request-size limit, so it inherits the 1 MiB ceiling and cannot take a file");
                continue;
            }

            if (bodyLimit > RequestSizeLimits.FileUploadMaxRequestBodySize)
            {
                offenders.Add($"{route}: raises the ceiling to {bodyLimit} bytes, past the {RequestSizeLimits.FileUploadMaxRequestBodySize} byte file ceiling");
            }

            // The multipart reader has its own ceiling, and it is global. A route that accepts a
            // body of X bytes must not have a multipart ceiling below X, or the reader refuses a
            // body the route accepts.
            var multipartCeiling = MultipartCeilingOf(method)
                ?? RequestSizeLimits.DefaultMaxRequestBodySize;

            if (multipartCeiling < bodyLimit)
            {
                offenders.Add($"{route}: accepts a {bodyLimit} byte body but the multipart ceiling is {multipartCeiling} bytes");
            }
        }

        offenders.Should().BeEmpty(
            "a route that takes a file declares the ceiling it accepts, and the multipart reader's "
            + "ceiling is never below it. Offenders:{0}",
            string.Join(" | ", offenders));
    }

    /// <summary>
    /// Every action that takes an <see cref="IFormFile"/>, with the route it is reachable on.
    /// </summary>
    private static IEnumerable<(string Route, MethodInfo Method)> FileRoutes()
    {
        var assembly = typeof(ApplicationDbContext).Assembly;

        foreach (var type in assembly.GetTypes())
        {
            if (type.GetCustomAttribute<ApiControllerAttribute>() is null)
            {
                continue;
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (!method.GetParameters().Any(p => typeof(IFormFile).IsAssignableFrom(p.ParameterType)))
                {
                    continue;
                }

                yield return ($"{type.Name}.{method.Name}", method);
            }
        }
    }

    /// <summary>
    /// The ceiling the route declares for its body, from the attribute that carries it.
    /// <see cref="RequestSizeLimitAttribute"/> keeps the number private and publishes it through
    /// <see cref="IRequestSizeLimitMetadata"/>, which is what the middleware reads too.
    /// </summary>
    private static long? BodyCeilingOf(MethodInfo method)
    {
        var attribute = method.GetCustomAttribute<RequestSizeLimitAttribute>()
            ?? method.DeclaringType?.GetCustomAttribute<RequestSizeLimitAttribute>();

        return attribute is null ? null : ((IRequestSizeLimitMetadata)attribute).MaxRequestBodySize;
    }

    /// <summary>
    /// The ceiling the route declares for the multipart reader, published the same way.
    /// </summary>
    private static long? MultipartCeilingOf(MethodInfo method)
    {
        var attribute = method.GetCustomAttribute<RequestFormLimitsAttribute>()
            ?? method.DeclaringType?.GetCustomAttribute<RequestFormLimitsAttribute>();

        return attribute is null ? null : ((IFormOptionsMetadata)attribute).MultipartBodyLengthLimit;
    }
}
