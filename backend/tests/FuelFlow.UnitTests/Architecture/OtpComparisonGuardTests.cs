using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FuelFlow.UnitTests.Architecture;

/// <summary>
/// Verification codes are compared through <see cref="Features.Auth.Security.OtpCodeHasher"/> and
/// nowhere else (planning #682).
///
/// <para>
/// A six-digit code stored as a bare SHA-256 is searchable offline the moment the table leaks, but
/// the second half of #682 was subtler: the comparison itself was <c>!=</c> on the stored digest, so
/// response time could reveal how much of a guess matched. The hasher uses
/// <see cref="System.Security.Cryptography.CryptographicOperations.FixedTimeEquals"/>; this guard
/// keeps a future handler from reintroducing a byte-wise <c>==</c> beside it and calling the job
/// done.
/// </para>
/// </summary>
public sealed class OtpComparisonGuardTests
{
    [Fact]
    public void TheOtpHasher_ShouldCompareWithFixedTimeEquals()
    {
        var hasher = Path.Combine(ApiRoot(), "Features", "Auth", "Security", "OtpCodeHasher.cs");

        File.Exists(hasher).Should().BeTrue("the peppered hasher is the one place OTP digests are compared");

        File.ReadAllText(hasher).Should().Contain("CryptographicOperations.FixedTimeEquals",
            "a byte-by-byte comparison of the digest lets the caller learn the matching prefix from response time");
    }

    [Fact]
    public void NoAuthFile_ShouldCompareAStoredCodeWithAnEqualityOperator()
    {
        var offenders = Directory
            .EnumerateFiles(Path.Combine(ApiRoot(), "Features", "Auth"), "*.cs", SearchOption.AllDirectories)
            .Select(path => (path, lines: File.ReadAllLines(path)))
            .SelectMany(f => f.lines.Select((line, index) => (f.path, line, index)))
            .Where(f => Regex.IsMatch(f.line, @"\.Code\s*(==|!=)\s*(?!null\b)"))
            .Select(f => $"{Path.GetRelativePath(ApiRoot(), f.path)}:{f.index + 1}: {f.line.Trim()}")
            .OrderBy(f => f)
            .ToList();

        offenders.Should().BeEmpty(
            "verification codes are compared with OtpCodeHasher.Verify (FixedTimeEquals), not with "
            + "== or !=, which short-circuit on the first differing byte");
    }

    [Theory]
    [InlineData("Verify", "VerifyCodeCommand.cs")]
    [InlineData("Verify", "OtpVerificationService.cs")]
    [InlineData("Hash", "SendCodeCommand.cs")]
    public void EveryOtpPath_ShouldGoThroughTheHasher(string operation, string fileName)
    {
        var path = Directory
            .EnumerateFiles(Path.Combine(ApiRoot(), "Features", "Auth"), fileName, SearchOption.AllDirectories)
            .Single();

        File.ReadAllText(path).Should().Contain($"OtpCodeHasher.{operation}(",
            "the login path and the step-up path must share the same peppered storage and comparison");
    }

    private static string ApiRoot([CallerFilePath] string thisFile = "")
    {
        // thisFile = .../backend/tests/FuelFlow.UnitTests/Architecture/<this>.cs
        var backend = Directory.GetParent(thisFile)!.Parent!.Parent!.Parent!.FullName;
        var candidate = Path.Combine(backend, "src", "FuelFlow.API");
        if (Directory.Exists(candidate)) return candidate;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var probe = Path.Combine(dir.FullName, "src", "FuelFlow.API");
            if (Directory.Exists(probe)) return probe;
            probe = Path.Combine(dir.FullName, "backend", "src", "FuelFlow.API");
            if (Directory.Exists(probe)) return probe;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate backend/src/FuelFlow.API from either the test file path or the assembly directory.");
    }
}
