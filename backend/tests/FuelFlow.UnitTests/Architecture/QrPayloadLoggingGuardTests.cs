using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FuelFlow.UnitTests.Architecture;

/// <summary>
/// Architecture guard: a redeemable voucher QR payload must never reach a log line.
/// </summary>
/// <remarks>
/// <para>
/// A voucher's QR payload is the bearer instrument. Whoever holds it can have the voucher
/// redeemed at the pump, and nothing can revoke it afterwards - the redemption happens on the
/// brand's side. A payload that reaches a log is therefore a leaked voucher, and a leaked
/// fuel voucher cannot be recalled.
/// </para>
/// <para>
/// The log is exactly the wrong place for it. The sinks are the console and Grafana Loki
/// (<c>SharedKernel/Observability/LoggingSetup.cs</c>), plus the <c>error_logs</c> table behind
/// <c>DatabaseLoggerProvider</c> - a store with a wider audience and a longer retention than the
/// voucher table itself. The import is the moment when a whole batch of live bearer instruments
/// passes through the process, so the decoder used to write every one of them to the log in the
/// clear, at <c>Information</c> level, on every voucher of every import.
/// </para>
/// <para>
/// The guard reads the ARGUMENT GRAPH of every logging call, not just the call site: a local that
/// holds a payload is caught however it reaches the call, and a call that passes the payload's
/// fingerprint (<c>QrPayloadFingerprint.Describe</c>) is safe by construction. The same shape as
/// the other text-scanning guards here - a rule nobody checks is a rule one afternoon of "just one
/// more refactor" erases.
/// </para>
/// <para>
/// KNOWN BLIND SPOTS, stated rather than hidden: the analysis is file-scoped and textual, so a
/// payload that reaches a log call through a method in ANOTHER file is not followed, and a payload
/// reached through a field on a class this file does not assign is not followed either. The
/// property that carries the payload is named directly (<c>QrPayload</c>), and the decoder's own
/// result is typed (<c>QrDecodeResult</c>) or assigned from a <c>Decode</c> call, which is what
/// makes the two leaks that actually happened catchable. A leak through a hand-rolled DTO in a
/// third file would need Roslyn to follow, and the repo's own precedent
/// (<c>NoTrackingMutationGuardTests</c>) is that the data-flow analysis is disproportionate for
/// the shape it guards.
/// </para>
/// </remarks>
public sealed class QrPayloadLoggingGuardTests
{
    [Fact]
    public void No_Log_Call_Should_Be_Able_To_Reach_A_Qr_Payload()
    {
        var offenders = Offenders();

        offenders.Should().BeEmpty(
            "a voucher QR payload is the redeemable instrument: whoever holds it can have the "
            + "voucher spent at the pump, and nothing can revoke it afterwards. The log is a store "
            + "with a wider audience and a longer retention than the voucher table itself (console "
            + "-> Loki, plus the error_logs table), so a payload in a log line is a leaked voucher. "
            + "Log the fingerprint instead - QrPayloadFingerprint.Describe(payload) - which tells "
            + "an operator which payload it was without carrying the payload anywhere.");
    }

    [Fact]
    public void The_Decoder_Should_Log_The_Fingerprint()
    {
        var source = StripComments(ReadSource("Features", "Vouchers", "Import", "Services", "QrDecoder.cs"));

        source.Should().Contain("QrPayloadFingerprint.Describe(",
            "the decoder is the first thing in the process that holds a payload, and it is where the "
            + "leak started: what belongs in a log line is the payload's fingerprint");
    }

    /// <summary>
    /// Every logging call in every source file whose argument graph can reach a QR payload.
    /// </summary>
    private static List<string> Offenders()
    {
        var srcRoot = LocateSourceRoot();
        var offenders = new List<string>();

        foreach (var file in SourceFiles(srcRoot))
        {
            var text = StripComments(File.ReadAllText(file));
            var model = new PayloadModel(text);
            var relative = Path.GetRelativePath(srcRoot, file).Replace('\\', '/');

            foreach (var call in LogCalls(text))
            {
                foreach (var argument in call.Arguments)
                {
                    if (!model.ReachesPayload(argument)) continue;

                    offenders.Add(
                        $"{relative}:{call.Line}: {call.Method} receives an argument that can reach " +
                        $"a QR payload: {Collapse(argument)}");
                }
            }
        }

        return offenders;
    }

    /// <summary>
    /// The source files a log call can hide in. Migrations are excluded: they are generated, they
    /// log nothing, and they are rewritten by every model change.
    /// </summary>
    private static IEnumerable<string> SourceFiles(string srcRoot) =>
        Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Replace('\\', '/').Contains("/bin/"))
            .Where(path => !path.Replace('\\', '/').Contains("/obj/"))
            .Where(path => !path.Replace('\\', '/').Contains("/Migrations/"))
            .OrderBy(path => path, StringComparer.Ordinal);

    // ── the logging calls ──────────────────────────────────────────────────────────────

    private sealed class LogCall
    {
        public int Line { get; init; }
        public string Method { get; init; } = "";
        public IReadOnlyList<string> Arguments { get; init; } = [];
    }

    private static readonly Regex LogCallStart = new(
        @"(?<method>\.(?:Log(?:Trace|Debug|Information|Warning|Error|Critical))|Console\.(?:Write|WriteLine))\s*\(",
        RegexOptions.Compiled);

    private static List<LogCall> LogCalls(string text)
    {
        var calls = new List<LogCall>();

        foreach (Match match in LogCallStart.Matches(text))
        {
            var open = match.Index + match.Length - 1;
            var close = MatchingParen(text, open);
            if (close < 0) continue;

            calls.Add(new LogCall
            {
                Line = LineOf(text, match.Index),
                Method = match.Groups["method"].Value,
                Arguments = SplitArguments(text, open + 1, close)
            });
        }

        return calls;
    }

    private static int MatchingParen(string text, int open)
    {
        var depth = 0;

        for (var i = open; i < text.Length; i++)
        {
            var c = text[i];

            if (c == '"')
            {
                i = ReadLiteral(text, i).End - 1;
                continue;
            }

            if (c == '(') depth++;
            else if (c == ')')
            {
                depth--;
                if (depth == 0) return i;
            }
        }

        return -1;
    }

    private static List<string> SplitArguments(string text, int start, int end)
    {
        var arguments = new List<string>();
        var depth = 0;
        var argumentStart = start;

        for (var i = start; i < end; i++)
        {
            var c = text[i];

            if (c == '"')
            {
                i = ReadLiteral(text, i).End - 1;
                continue;
            }

            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth--;
            else if (c == ',' && depth == 0)
            {
                arguments.Add(text[argumentStart..i]);
                argumentStart = i + 1;
            }
        }

        arguments.Add(text[argumentStart..end]);
        return arguments.Where(a => a.Trim().Length > 0).ToList();
    }

    // ── what can reach a payload ───────────────────────────────────────────────────────

    /// <summary>
    /// The payload knowledge about one file: which locals hold a payload, and which locals hold
    /// a decoded QR result (whose <c>Text</c> IS the payload).
    /// </summary>
    private sealed class PayloadModel
    {
        private readonly string _text;
        private readonly bool _qrAware;

        /// <summary>Locals that hold the payload itself.</summary>
        public HashSet<string> PayloadLocals { get; } = new(StringComparer.Ordinal);

        /// <summary>Locals that hold a decoded QR result - its <c>Text</c> is the payload.</summary>
        public HashSet<string> DecodeResults { get; } = new(StringComparer.Ordinal);

        public PayloadModel(string text)
        {
            _text = text;
            _qrAware = QrAwareFile.IsMatch(text);
            CollectDecodeResults();
            CollectStringPayloads();
            PropagateAssignments();
        }

        /// <summary>
        /// Whether this expression can carry a redeemable payload into a log line.
        /// </summary>
        public bool ReachesPayload(string expression)
        {
            // The fingerprint is the payload's stand-in: it is safe by construction, and it is
            // removed BEFORE the checks below, or the payload it is computed from would be
            // read as a leak.
            var code = ReplaceFingerprintCalls(RewriteLiterals(expression));

            code = NameOfPattern.Replace(code, " ");

            if (QrPayloadMember.IsMatch(code)) return true;

            foreach (Match match in TextMemberPattern.Matches(code))
                if (DecodeResults.Contains(match.Groups["name"].Value))
                    return true;

            foreach (Match match in IdentifierPattern.Matches(code))
                if (PayloadLocals.Contains(match.Value))
                    return true;

            return false;
        }

        /// <summary>
        /// Whether this expression IS the payload - as opposed to an object that merely mentions
        /// one. The distinction matters: <c>verification = QrMatrixVerifier.Verify(parsed.QrPayload,
        /// ...)</c> produces a verification report, not a payload, and a report that carries a
        /// fingerprint is not a leak. Only a value that IS the payload propagates.
        /// </summary>
        private bool IsThePayload(string expression)
        {
            var text = expression.Trim().TrimEnd(',');
            if (text.Length == 0) return false;

            // A call is not the payload, whatever it was handed.
            if (text.Contains('(')) return false;

            if (DirectQrPayloadMember.IsMatch(text)) return true;

            var decodeResult = DirectTextMember.Match(text);
            if (decodeResult.Success && DecodeResults.Contains(decodeResult.Groups["name"].Value)) return true;

            return PayloadLocals.Contains(text);
        }

        /// <summary>
        /// The locals that hold a decoded QR result: typed as one, or assigned from a decode.
        /// </summary>
        private void CollectDecodeResults()
        {
            foreach (Match match in DecodeResultTypePattern.Matches(_text))
                DecodeResults.Add(match.Groups["name"].Value);

            foreach (Match match in DecodeResultAssignmentPattern.Matches(_text))
                DecodeResults.Add(match.Groups["name"].Value);
        }

        /// <summary>
        /// The locals and parameters that hold a payload by their very name: a string called
        /// <c>payload</c> or <c>qrPayload</c>. A <c>payload</c> of a JSON envelope type
        /// (<c>OrderCreatedPayload</c> and friends) is not a voucher payload and is not one of
        /// these - the type has to be a string. The name only counts in a file that talks about
        /// voucher payloads at all: a Telegram deep link's <c>payload</c> is a signed token, not
        /// a redeemable instrument, and calling that a leak would be a false positive that gets
        /// the guard switched off.
        /// </summary>
        private void CollectStringPayloads()
        {
            if (!_qrAware) return;

            foreach (Match match in StringPayloadPattern.Matches(_text))
                PayloadLocals.Add(match.Groups["name"].Value);
        }

        /// <summary>
        /// A local that is assigned the payload is the payload: <c>qrPayload = qrResult.Text</c>
        /// makes the local just as dangerous as the expression it was given. The assignments are
        /// walked until nothing new is found, so a chain of them is followed.
        /// </summary>
        private void PropagateAssignments()
        {
            var changed = true;

            while (changed)
            {
                changed = false;

                foreach (Match match in AssignmentPattern.Matches(_text))
                {
                    var name = match.Groups["name"].Value;
                    if (PayloadLocals.Contains(name)) continue;
                    if (!IsThePayload(match.Groups["rhs"].Value)) continue;

                    PayloadLocals.Add(name);
                    changed = true;
                }
            }
        }
    }

    /// <summary>
    /// A file that talks about voucher payloads at all. The name-based rule below only applies
    /// in such a file, so that an unrelated <c>payload</c> - a Telegram deep link, a webhook
    /// envelope - is not mistaken for a redeemable instrument.
    /// </summary>
    private static readonly Regex QrAwareFile = new(
        @"QrPayload|QrDecodeResult|QrDecoder|QrPayloadFingerprint", RegexOptions.Compiled);

    private static readonly Regex QrPayloadMember = new(@"\bQrPayload\b", RegexOptions.Compiled);

    private static readonly Regex TextMemberPattern = new(@"\b(?<name>\w+)\.Text\b", RegexOptions.Compiled);

    private static readonly Regex DirectQrPayloadMember = new(@"^[\w\.]+\.QrPayload[!?]?$", RegexOptions.Compiled);

    private static readonly Regex DirectTextMember = new(@"^(?<name>\w+)\.Text$", RegexOptions.Compiled);

    private static readonly Regex IdentifierPattern = new(@"\b\w+\b", RegexOptions.Compiled);

    private static readonly Regex NameOfPattern = new(@"\bnameof\s*\([^()]*\)", RegexOptions.Compiled);

    private static readonly Regex DecodeResultTypePattern = new(
        @"\bQrDecodeResult\s+(?<name>\w+)", RegexOptions.Compiled);

    private static readonly Regex DecodeResultAssignmentPattern = new(
        @"(?<![.\w])(?<name>\w+)\s*=(?!=|>)\s*[^;{}]*?\.\s*Decode\s*\(", RegexOptions.Compiled);

    /// <summary>
    /// A string-typed local or parameter whose name says it holds a payload.
    /// </summary>
    private static readonly Regex StringPayloadPattern = new(
        @"\bstring\??\s+(?<name>payload|qrPayload)\b", RegexOptions.Compiled);

    /// <summary>
    /// An assignment to a local (not to a property), with its right-hand side. The statement has
    /// to END there: an entry in an object initializer (<c>QrPayload = v.QrPayload,</c>) is a
    /// property being set, not a local being given the payload, and reading it as the latter
    /// marks every mention of the word as a leak.
    /// </summary>
    private static readonly Regex AssignmentPattern = new(
        @"(?<![.\w])(?<name>\w+)\s*=(?!=|>)\s*(?<rhs>[^\n;{}]*);", RegexOptions.Compiled);

    // ── the text machinery ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Removes the comments, which are what the compiler does not see. The scanner is literal-aware
    /// so that a <c>//</c> inside a string (a URL, a path) does not swallow the rest of the line.
    /// </summary>
    private static string StripComments(string text)
    {
        var sb = new StringBuilder(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (c == '"')
            {
                var literal = ReadLiteral(text, i);
                sb.Append(text, i, literal.End - i);
                i = literal.End - 1;
                continue;
            }

            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
                sb.Append('\n');
                continue;
            }

            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/')) i++;
                i = Math.Min(i + 2, text.Length);
                sb.Append(' ');
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private readonly record struct Literal(int ContentStart, int ContentEnd, int End, bool Interpolated);

    /// <summary>
    /// Reads the string literal that starts at <paramref name="start"/>, which must point at a
    /// quote. A raw literal (<c>"""..."""</c>) is read as one; a verbatim one (<c>@"..."</c>)
    /// ends at the quote that is not doubled.
    /// </summary>
    private static Literal ReadLiteral(string text, int start)
    {
        if (start + 2 < text.Length && text[start] == '"' && text[start + 1] == '"' && text[start + 2] == '"')
        {
            var rawEnd = text.IndexOf("\"\"\"", start + 3, StringComparison.Ordinal);
            if (rawEnd < 0) return new Literal(start + 3, text.Length, text.Length, false);
            return new Literal(start + 3, rawEnd, rawEnd + 3, false);
        }

        var verbatim = start > 0 && text[start - 1] == '@';
        var interpolated = start > 0 && text[start - 1] == '$';
        var quote = text[start];

        for (var i = start + 1; i < text.Length; i++)
        {
            var c = text[i];

            if (!verbatim && c == '\\')
            {
                i++;
                continue;
            }

            if (c == quote)
            {
                if (verbatim && i + 1 < text.Length && text[i + 1] == quote)
                {
                    i++;
                    continue;
                }

                return new Literal(start + 1, i, i + 1, interpolated);
            }

            if (!verbatim && c == '\n') break;
        }

        return new Literal(start + 1, text.Length, text.Length, interpolated);
    }

    /// <summary>
    /// Rewrites every string literal into the CODE it carries: an interpolated literal keeps the
    /// expressions in its holes (they are values on their way into the message), and a plain
    /// literal becomes nothing. The point is that the checks then see one piece of code.
    /// </summary>
    private static string RewriteLiterals(string text)
    {
        var sb = new StringBuilder(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '"')
            {
                sb.Append(text[i]);
                continue;
            }

            var literal = ReadLiteral(text, i);
            if (literal.Interpolated)
                sb.Append(text, literal.ContentStart, literal.ContentEnd - literal.ContentStart);
            else
                sb.Append(' ');

            i = literal.End - 1;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Removes the fingerprint calls, whole. The call's argument IS the payload, so replacing
    /// only its name would leave the payload behind and make the fingerprint look like a leak.
    /// </summary>
    private static string ReplaceFingerprintCalls(string text)
    {
        const string marker = "QrPayloadFingerprint.Describe";

        while (true)
        {
            var index = text.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0) return text;

            var open = text.IndexOf('(', index);
            if (open < 0) return text;

            var close = MatchingParen(text, open);
            if (close < 0) return text;

            text = text[..index] + "fingerprint" + text[(close + 1)..];
        }
    }

    private static int LineOf(string text, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < text.Length; i++)
            if (text[i] == '\n') line++;
        return line;
    }

    private static string Collapse(string text)
    {
        var collapsed = Regex.Replace(text, @"\s+", " ").Trim();
        return collapsed.Length <= 120 ? collapsed : collapsed[..117] + "...";
    }

    // ── the files ──────────────────────────────────────────────────────────────────────

    private static string ReadSource(params string[] relativeParts)
    {
        var path = Path.Combine(new[] { LocateApiProjectRoot() }.Concat(relativeParts).ToArray());
        if (File.Exists(path)) return File.ReadAllText(path);

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName, "src", "FuelFlow.API" }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"{Path.Combine(relativeParts)} was not found from the test assembly.");
    }

    /// <summary>
    /// The whole source tree - the API and the JobsWorker - because a payload that reaches a log
    /// in either of them is a payload in the log.
    /// </summary>
    private static string LocateSourceRoot([CallerFilePath] string thisFile = "")
    {
        // thisFile = .../backend/tests/FuelFlow.UnitTests/Architecture/<this>.cs
        var backend = Directory.GetParent(thisFile)!.Parent!.Parent!.Parent!.FullName;
        var candidate = Path.Combine(backend, "src");
        if (Directory.Exists(candidate)) return candidate;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var probe = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(probe) && Directory.Exists(Path.Combine(probe, "FuelFlow.API"))) return probe;
            var nested = Path.Combine(dir.FullName, "backend", "src");
            if (Directory.Exists(nested) && Directory.Exists(Path.Combine(nested, "FuelFlow.API"))) return nested;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate backend/src from either the test file path or the assembly directory.");
    }

    private static string LocateApiProjectRoot([CallerFilePath] string thisFile = "")
    {
        var backend = Directory.GetParent(thisFile)!.Parent!.Parent!.Parent!.FullName;
        var candidate = Path.Combine(backend, "src", "FuelFlow.API");
        if (Directory.Exists(candidate)) return candidate;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var probe = Path.Combine(dir.FullName, "src", "FuelFlow.API");
            if (Directory.Exists(probe)) return probe;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate backend/src/FuelFlow.API from either the test file path or the assembly directory.");
    }
}
