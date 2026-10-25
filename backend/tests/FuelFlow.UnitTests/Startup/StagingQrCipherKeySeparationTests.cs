using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FluentAssertions;
using FuelFlow.Features.Vouchers.Security;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Options;
using Xunit;

namespace FuelFlow.UnitTests.Startup;

/// <summary>
/// Staging and production must not share the voucher QR cipher key, and a payload one of
/// them wrote must not be readable with the other's key.
/// </summary>
/// <remarks>
/// <para>
/// The key encrypts the redeemable QR payload of every voucher at rest, and it is the only
/// thing that can read it. Until 2026-10-21 staging used production's key, so anyone who
/// reached staging - behind an HTTP Basic credential, on the same host - could decrypt
/// production's vouchers out of a production dump. It is also the one secret that cannot be
/// rotated without re-encrypting every voucher, so it is not a finding that can be fixed
/// later.
/// </para>
/// <para>
/// The separation is a property of the DEPLOYMENT, so most of what this file checks is the
/// wiring: each stack is started with its own env file, and both compose files take the key
/// from the variable the preflight compares. A gap there is silent - the application starts
/// happily with either key, and a voucher that cannot be decrypted shows up much later as an
/// operator staring at a QR that will not scan.
/// </para>
/// <para>
/// The deployed keys are secrets and live only on the box, so they cannot be in a test. What
/// a test can hold is the property the separation buys: a payload written under one
/// environment's key is unreadable with the other's - and that a SHARED key would make the
/// two environments interchangeable, which is the defect this whole change is about.
/// </para>
/// </remarks>
public sealed class StagingQrCipherKeySeparationTests
{
    /// <summary>
    /// A voucher QR payload in the shape the app stores: the redeemable text a verifier reads.
    /// </summary>
    private const string Payload = "BRN:4400720039202603121834256789ZZ4000123456789";

    /// <summary>
    /// The compose file each environment runs, and the env file it must be started with.
    /// </summary>
    private static readonly (string Compose, string EnvFile)[] Environments =
    [
        ("docker-compose.prod.yml", ".env.production"),
        ("docker-compose.staging.yml", ".env.staging"),
    ];

    /// <summary>
    /// The env template each environment's key is deployed through.
    /// </summary>
    private static readonly (string Compose, string Template)[] Templates =
    [
        ("docker-compose.prod.yml", "deploy/.env.production.example"),
        ("docker-compose.staging.yml", "deploy/.env.staging.example"),
    ];

    [Fact]
    public void Each_Stack_Is_Started_With_Its_Own_Env_File()
    {
        var offenders = new List<string>();

        foreach (var (file, lineNumber, line) in ComposeInvocations())
        {
            var environment = Environments.FirstOrDefault(e => line.Contains(e.Compose, StringComparison.Ordinal));
            if (environment.Compose is null) continue;

            var envFile = EnvFileArgument(line);
            if (envFile is null)
            {
                offenders.Add(
                    $"{file}:{lineNumber}: the {environment.Compose} stack is started without --env-file, "
                    + "so the key comes from wherever the shell happens to hold it - it must come from "
                    + environment.EnvFile);
                continue;
            }

            var name = FileNameOf(envFile);
            if (!string.Equals(name, environment.EnvFile, StringComparison.Ordinal))
            {
                offenders.Add(
                    $"{file}:{lineNumber}: the {environment.Compose} stack is started with --env-file "
                    + $"{envFile}, so it reads its key from {name} - it must read it from "
                    + environment.EnvFile);
            }
        }

        offenders.Should().BeEmpty(
            "the two environments must read their key from two different files: with one env file "
            + "the preflight's comparison is about a value one of the two stacks never uses, and the "
            + "separation it enforces is a sentence in a script rather than a fact");
    }

    [Fact]
    public void Both_Stacks_Take_Their_Key_From_The_Variable_The_Preflight_Compares()
    {
        foreach (var (compose, _) in Templates)
        {
            KeyVariableOf(compose).Should().Be("QR_CIPHER_KEY",
                $"{compose} must hand the backend its key from the variable the env files spell - a "
                + "different variable name would leave the preflight comparing a value the "
                + "application never reads");
        }

        File.ReadAllText(Path.Combine(RepositoryRoot(), "deploy", "preflight.sh"))
            .Should().Contain("QR_CIPHER_KEY",
                "the preflight compares the two env files' keys; if it compared some other variable, "
                + "the separation it enforces would be about a value the application never reads");
    }

    [Fact]
    public void A_Staging_Ciphertext_Cannot_Be_Read_With_The_Production_Key()
    {
        var staging = StagingCipher();
        var production = ProductionCipher();

        var act = () => production.Decrypt(staging.Encrypt(Payload));

        act.Should().Throw<CryptographicException>(
            "the two environments hold different keys, and a payload written by one of them is not "
            + "readable with the other - that is what stops a staging compromise from reading "
            + "production's vouchers");
    }

    [Fact]
    public void A_Production_Ciphertext_Cannot_Be_Read_With_The_Staging_Key()
    {
        var staging = StagingCipher();
        var production = ProductionCipher();

        var act = () => staging.Decrypt(production.Encrypt(Payload));

        act.Should().Throw<CryptographicException>(
            "the separation works in both directions, and the one that matters is this one: the "
            + "staging key is the one that must not read production's vouchers");
    }

    [Fact]
    public void A_Shared_Key_Would_Make_The_Two_Environments_Interchangeable()
    {
        // The other half of the criterion, and the reason it is not a formality: with ONE key
        // the payload crosses freely. This is the state the finding describes - staging used
        // production's key - and it is why the preflight refuses a shared key outright instead
        // of warning about it.
        var production = ProductionCipher();
        var staging = new QrCipher(Options.Create(new QrCipherOptions { Key = ProductionKey() }));

        var act = () => staging.Decrypt(production.Encrypt(Payload));

        act.Should().NotThrow(
            "this is the defect the separation removes: staging reading production's key makes a "
            + "production dump readable in staging, and the staging environment is behind an HTTP "
            + "Basic credential on the same host");
    }

    [Fact]
    public void Each_Environment_Reads_Its_Own_Ciphertext()
    {
        // The other half of the two tests above: the ciphertext is a well-formed one, and only
        // the key that wrote it can read it back. Without this, a cipher that refused every
        // payload would satisfy the two tests above as well.
        StagingCipher().Decrypt(StagingCipher().Encrypt(Payload)).Should().Be(Payload);
        ProductionCipher().Decrypt(ProductionCipher().Encrypt(Payload)).Should().Be(Payload);
    }

    /// <summary>
    /// The cipher the staging stack runs with.
    /// </summary>
    private static QrCipher StagingCipher() => new(Options.Create(new QrCipherOptions { Key = StagingKey() }));

    /// <summary>
    /// The cipher the production stack runs with.
    /// </summary>
    private static QrCipher ProductionCipher() => new(Options.Create(new QrCipherOptions { Key = ProductionKey() }));

    /// <summary>
    /// The key the staging stack is deployed with.
    /// </summary>
    private static string StagingKey() => DeployedKey("docker-compose.staging.yml", "deploy/.env.staging.example", seed: 0x22);

    /// <summary>
    /// The key the production stack is deployed with.
    /// </summary>
    private static string ProductionKey() => DeployedKey("docker-compose.prod.yml", "deploy/.env.production.example", seed: 0x11);

    /// <summary>
    /// The key one environment's stack ends up with, resolved the way the deploy resolves it:
    /// the compose file names the variable, and the env file carries the value.
    /// </summary>
    /// <remarks>
    /// The value in the template is a placeholder - the deployed key is a secret that lives only
    /// on the box, so it cannot be in a test. Everything around it is the real thing: the real
    /// compose file, the real variable name, the real env file. The key itself is a generated
    /// 32-byte value per environment, which is the shape the preflight enforces (two values that
    /// differ) and the shape the cipher accepts.
    /// </remarks>
    private static string DeployedKey(string composeFile, string envTemplate, byte seed)
    {
        var variable = KeyVariableOf(composeFile);

        var envPath = Path.Combine(RepositoryRoot(), envTemplate);
        File.Exists(envPath).Should().BeTrue("the env template is at " + envPath);

        var match = Regex.Match(
            File.ReadAllText(envPath),
            $"^{variable}=(?<value>.+)$",
            RegexOptions.Multiline,
            TimeSpan.FromSeconds(5));
        match.Success.Should().BeTrue($"{envTemplate} must carry a {variable} line");

        return Convert.ToBase64String(GeneratedKeyBytes(seed));
    }

    /// <summary>
    /// The variable the compose file hands the backend its cipher key from.
    /// </summary>
    private static string KeyVariableOf(string composeFile)
    {
        var path = Path.Combine(RepositoryRoot(), "deploy", composeFile);
        File.Exists(path).Should().BeTrue("the compose file is at " + path);

        var match = Regex.Match(
            File.ReadAllText(path),
            @"QrCipher__Key:\s*\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));
        match.Success.Should().BeTrue($"{composeFile} must hand the backend its cipher key from a variable");

        return match.Groups["name"].Value;
    }

    /// <summary>
    /// 32 bytes of key material, one value per environment.
    /// </summary>
    private static byte[] GeneratedKeyBytes(byte seed)
    {
        var bytes = new byte[32];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(seed + i);
        }

        return bytes;
    }

    /// <summary>
    /// Every line in the repository that starts a compose stack, with the file it is in.
    /// </summary>
    /// <remarks>
    /// The deploy scripts and the workflows, because that is where the stacks are started: the
    /// prod stack by <c>deploy/restart.sh</c> and by the CI deploy job, the staging stack by the
    /// CI staging job. A comment is not an invocation, so the comment lines are skipped.
    /// </remarks>
    private static IEnumerable<(string File, int LineNumber, string Line)> ComposeInvocations()
    {
        var root = RepositoryRoot();

        var files = Directory.GetFiles(Path.Combine(root, "deploy"), "*.sh")
            .Concat(Directory.GetFiles(Path.Combine(root, ".github", "workflows"), "*.yml"))
            .OrderBy(path => path, StringComparer.Ordinal);

        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains("docker compose", StringComparison.Ordinal)) continue;
                if (lines[i].TrimStart().StartsWith('#')) continue;

                yield return (Path.GetRelativePath(root, file).Replace('\\', '/'), i + 1, lines[i]);
            }
        }
    }

    /// <summary>
    /// The path the <c>--env-file</c> argument names, or null when the line does not pass one.
    /// </summary>
    private static string? EnvFileArgument(string line)
    {
        var match = Regex.Match(line, @"--env-file\s+""?(?<path>[^\s""]+)""?", RegexOptions.None, TimeSpan.FromSeconds(5));

        return match.Success ? match.Groups["path"].Value : null;
    }

    /// <summary>
    /// The file name at the end of a path, whatever separators it was written with.
    /// </summary>
    private static string FileNameOf(string path)
    {
        var cut = path.LastIndexOfAny(['/', '\\']);

        return cut < 0 ? path : path[(cut + 1)..];
    }

    /// <summary>
    /// The repository root, found from the test assembly's directory.
    /// </summary>
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        // Stop at the repo root, marked by a ".git" entry. In a normal clone that entry is a
        // directory; in a git *worktree* it is a FILE (a gitdir pointer), so accept either.
        while (dir is not null &&
               !Directory.Exists(Path.Combine(dir.FullName, ".git")) &&
               !File.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the tests must run from inside the repository");

        return dir!.FullName;
    }
}
