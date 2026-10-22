using System.Runtime.CompilerServices;
using FluentAssertions;
using FuelFlow.SharedKernel.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelFlow.UnitTests.Architecture;

/// <summary>
/// The guard that keeps an outbound HTTP client from living without a deadline (planning finding
/// C-7, issue #537).
/// </summary>
/// <remarks>
/// <para>
/// The defect it exists for: the acquiring path had no timeout, so a provider that never answered
/// held a request thread for the hundred seconds that <see cref="HttpClient"/> allows by default -
/// on the endpoint that creates the invoice. Three more clients (the SMS brand, the push server,
/// the OTA manifest) had none either, and nobody could tell by reading the registration, because
/// "no timeout" was not a thing anybody wrote down: it was what you got by not saying anything.
/// </para>
/// <para>
/// The rule it enforces: <b>an outbound client is registered in one place, and that place asks
/// for a deadline.</b> The deadline is a parameter of the registration rather than an option of
/// it, so a client without one does not compile - and the test below proves that no registration
/// slipped past the one place.
/// </para>
/// <para>
/// The second half of the file is the other side of the same finding. A client may be retried only
/// when every call it makes is safe to repeat, and that is a claim about the client's code, not
/// about the client's registration - so the claim is checked against the code.
/// </para>
/// </remarks>
public sealed class HttpClientTimeoutGuardTests
{
    /// <summary>
    /// The one file allowed to register an HTTP client. Everything else goes through it.
    /// </summary>
    private const string RegistrationFile = "SharedKernel/Resilience/OutboundHttpClient.cs";

    /// <summary>
    /// The calls that register a client. The parentheses and the angle bracket are part of the
    /// pattern on purpose: <c>AddHttpClientInstrumentation</c> is OpenTelemetry's tracing hook and
    /// registers nothing.
    /// </summary>
    private static readonly string[] RawRegistrations =
    [
        "AddHttpClient(",
        "AddHttpClient<",
    ];

    /// <summary>
    /// The calls that write. A client that only reads may be retried; a client that writes may
    /// not, because a repeated call repeats its effect.
    /// </summary>
    private static readonly string[] WriteCalls =
    [
        "PostAsync",
        "PostAsJsonAsync",
        "PutAsync",
        "PatchAsync",
        "DeleteAsync",
        "HttpMethod.Post",
        "HttpMethod.Put",
        "HttpMethod.Patch",
        "HttpMethod.Delete",
    ];

    /// <summary>
    /// No client is registered outside the one place that asks for a deadline.
    /// </summary>
    [Fact]
    public void No_HttpClient_Should_Be_Registered_Outside_The_Outbound_Registration()
    {
        var src = Path.Combine(LocateBackendRoot(), "src");

        var offenders = new List<string>();

        foreach (var file in SourceFiles(src))
        {
            var relative = Path.GetRelativePath(src, file).Replace('\\', '/');
            if (relative.EndsWith(RegistrationFile, StringComparison.Ordinal))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var code = CodeOf(lines[i]);
                if (code.Length == 0)
                {
                    continue;
                }

                foreach (var registration in RawRegistrations)
                {
                    if (code.Contains(registration, StringComparison.Ordinal))
                    {
                        offenders.Add($"{relative}:{i + 1} registers a client with {registration}");
                    }
                }
            }
        }

        offenders.Should().BeEmpty(
            "an outbound client is registered in one place, and that place asks for a deadline: "
            + "without it, the client's timeout is the hundred seconds that HttpClient allows by "
            + "default, and a provider that never answers holds a request thread for a minute and "
            + "a half. Offenders:{0}",
            string.Join(" | ", offenders));
    }

    /// <summary>
    /// A client without a deadline is refused at the registration, not at the first slow provider.
    /// </summary>
    [Fact]
    public void A_Client_Without_A_Deadline_Should_Be_Refused()
    {
        var services = new ServiceCollection();

        var register = () => services.AddOutboundHttpClient(
            "no-deadline", TimeSpan.Zero, OutboundCallKind.Read);

        register.Should().Throw<ArgumentOutOfRangeException>(
            "the deadline is the parameter that keeps a client from living on the hundred-second "
            + "default, so a registration without one is a mistake rather than a choice");
    }

    /// <summary>
    /// The client the factory hands out carries the deadline it was registered with.
    /// </summary>
    [Fact]
    public void A_Registered_Client_Should_Carry_Its_Deadline()
    {
        var deadline = TimeSpan.FromSeconds(7);

        var services = new ServiceCollection();
        services.AddOutboundHttpClient("probe", deadline, OutboundCallKind.Read);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("probe");

        client.Timeout.Should().Be(
            deadline,
            "the deadline is what the registration asked for, and it is what the client does");
    }

    /// <summary>
    /// A client that is declared read-only does not write.
    /// </summary>
    /// <remarks>
    /// The declaration is what decides whether a call may be repeated, so it has to be true. The
    /// acquirer is the reason this is not a detail: its client creates an invoice, and a repeated
    /// invoice creation is how one basket becomes two invoices.
    /// </remarks>
    [Fact]
    public void A_Client_Declared_Read_Only_Should_Not_Write()
    {
        var src = Path.Combine(LocateBackendRoot(), "src");
        var files = SourceFiles(src).ToArray();

        var offenders = new List<string>();

        foreach (var registration in ReadOnlyRegistrations(files))
        {
            var implementation = files.FirstOrDefault(f => DeclaresType(f, registration.Implementation))
                ?? throw new InvalidOperationException(
                    $"The client '{registration.Implementation}' is registered but no file declares it.");

            var lines = File.ReadAllLines(implementation);
            for (var i = 0; i < lines.Length; i++)
            {
                var code = CodeOf(lines[i]);
                if (code.Length == 0)
                {
                    continue;
                }

                foreach (var write in WriteCalls)
                {
                    if (code.Contains(write, StringComparison.Ordinal))
                    {
                        offenders.Add(
                            $"{Path.GetRelativePath(src, implementation).Replace('\\', '/')}:{i + 1} "
                            + $"calls {write}, so '{registration.Implementation}' is not read-only");
                    }
                }
            }
        }

        offenders.Should().BeEmpty(
            "a read-only client is retried on a transient failure, because repeating a read cannot "
            + "repeat its effect. A client that writes does not get that retry - so declaring one "
            + "read-only while it posts something would repeat the write. Offenders:{0}",
            string.Join(" | ", offenders));
    }

    /// <summary>
    /// The typed clients that were registered as read-only.
    /// </summary>
    private static IEnumerable<(string Implementation, string File)> ReadOnlyRegistrations(string[] files)
    {
        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                var code = CodeOf(lines[i]);
                if (!code.Contains("AddOutboundHttpClient<", StringComparison.Ordinal))
                {
                    continue;
                }

                // The declaration is on the call or on one of the lines that follow it; the kind
                // and the type are both in that stretch of code.
                var window = string.Join(
                    " ", lines.Skip(i).Take(6).Select(CodeOf));

                if (!window.Contains("OutboundCallKind.Read", StringComparison.Ordinal))
                {
                    continue;
                }

                var type = TypeArgumentOf(code);
                if (type is null)
                {
                    continue;
                }

                yield return (type, file);
            }
        }
    }

    /// <summary>
    /// The client type of a registration: the implementation of a typed registration, or the single
    /// type of a one-argument registration.
    /// </summary>
    private static string? TypeArgumentOf(string code)
    {
        var open = code.IndexOf("AddOutboundHttpClient<", StringComparison.Ordinal);
        if (open < 0)
        {
            return null;
        }

        var close = code.IndexOf('>', open);
        if (close < 0)
        {
            return null;
        }

        var arguments = code[(open + "AddOutboundHttpClient<".Length)..close]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (arguments.Length == 0)
        {
            return null;
        }

        // Two arguments mean <TClient, TImplementation>; the factory names the client after the
        // implementation, so that is the type whose code decides what the client does.
        var type = arguments.Length >= 2 ? arguments[1] : arguments[0];
        type = type.Split('<')[0].Trim();

        return type.Length == 0 ? null : type;
    }

    /// <summary>
    /// True when the file declares the type.
    /// </summary>
    private static bool DeclaresType(string file, string type)
    {
        var text = File.ReadAllText(file);
        return System.Text.RegularExpressions.Regex.IsMatch(
            text,
            @"\bclass\s+" + System.Text.RegularExpressions.Regex.Escape(type) + @"\b");
    }

    /// <summary>
    /// Every C# file of the application - and nothing else. The build output directories hold
    /// copies of the same sources.
    /// </summary>
    private static IEnumerable<string> SourceFiles(string src)
    {
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            yield return file;
        }
    }

    /// <summary>
    /// The line's code, with the trailing comment removed. A comment may name the API this guard
    /// forbids; code may not.
    /// </summary>
    private static string CodeOf(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("*", StringComparison.Ordinal)
            || trimmed.StartsWith("/*", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var comment = line.IndexOf("//", StringComparison.Ordinal);
        if (comment < 0)
        {
            return line;
        }

        // A "//" inside a string is not a comment. An odd number of quotes before it means the
        // line has an open string, so the "//" belongs to that string.
        var before = line[..comment];
        return before.Count(c => c == '"') % 2 == 1 ? line : before;
    }

    private static string LocateBackendRoot([CallerFilePath] string thisFile = "")
    {
        // thisFile = .../backend/tests/FuelFlow.UnitTests/Architecture/<this>.cs
        var backend = Directory.GetParent(thisFile)!.Parent!.Parent!.Parent!.FullName;
        if (Directory.Exists(Path.Combine(backend, "src")) && Directory.Exists(Path.Combine(backend, "tests")))
        {
            return backend;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "backend");
            if (Directory.Exists(Path.Combine(candidate, "src")) && Directory.Exists(Path.Combine(candidate, "tests")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("backend/src and backend/tests were not found from the test assembly.");
    }
}
