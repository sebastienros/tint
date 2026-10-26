using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Tint.Differential;

namespace Tint.Fuzz;

/// <summary>Judges findings with the referee and writes <c>findings.md</c>.</summary>
public static class Report
{
    private static readonly string[] s_kinds =
    [
        "harness", "tint-hang", "tint-exception", "tint-slow", "stopped-tint", "exception-type", "tokens", "styles",
        "reference-hang", "reference-exception", "reference-slow", "stopped-reference", "invalid-utf16",
    ];

    /// <summary>The order of kinds in the report, most severe for Tint first.</summary>
    public static int Severity(string kind) => Array.IndexOf(s_kinds, kind) is var i and >= 0 ? i : s_kinds.Length;

    /// <summary>
    /// Runs vscode-textmate on each token, style and exception finding whose input survives UTF-8 (no lone
    /// surrogate), up to its differing line.
    /// </summary>
    public static void Judge(Referee referee, IReadOnlyList<Finding> findings, string inputsDirectory)
    {
        Directory.CreateDirectory(inputsDirectory);
        var judged = findings.Where(f => f.Kind is "tokens" or "styles" or "tint-exception" or "reference-exception" or "exception-type").ToList();
        Console.WriteLine($"judging {judged.Count} findings with vscode-textmate");
        Parallel.ForEach(judged, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, finding =>
        {
            if (finding.Lines.Any(HasLoneSurrogate))
            {
                finding.Verdict = "not judged: the input holds a lone surrogate, which the referee's UTF-8 file cannot carry";
                return;
            }

            var path = Path.Combine(inputsDirectory, $"{Generator.StableHash(finding.Signature):x8}.txt");
            // Every line ends with \n, so a trailing empty line (or an input of one empty line) survives the round trip.
            File.WriteAllText(path, string.Concat(finding.Lines.Select(l => l + "\n")), new UTF8Encoding(false, false));
            var (tintLine, referenceLine, error) = referee.Judge(finding.ScopeName, path, finding.Line);
            if (error is not null)
            {
                finding.Verdict = $"vscode-textmate failed: {error}";
                return;
            }

            finding.TintAgreesWithVSCode = tintLine == 0;
            finding.Verdict = tintLine == 0 && referenceLine == 0 ? "vscode-textmate agrees with both"
                : tintLine == 0 ? "vscode-textmate agrees with Tint"
                : referenceLine == 0 ? $"**vscode-textmate agrees with TextMateSharp; Tint differs at line {tintLine}**"
                : $"**vscode-textmate agrees with neither (Tint differs at line {tintLine}, TextMateSharp at line {referenceLine})**";
        });
    }

    /// <summary>The report.</summary>
    public static string Write(FuzzOptions options, string tintVersion, string referenceVersion, TimeSpan elapsed, IReadOnlyDictionary<string, GrammarStats> stats, IReadOnlyList<Finding> findings, int leftBehind)
    {
        var c = CultureInfo.InvariantCulture;
        var builder = new StringBuilder();
        builder.AppendLine("# Fuzz campaign").AppendLine();
        builder.AppendLine(c, $"- Date: {DateTime.Now:yyyy-MM-dd}; machine: {RuntimeInformation.OSDescription} {RuntimeInformation.ProcessArchitecture}, {Environment.ProcessorCount} cores, {RuntimeInformation.FrameworkDescription}.");
        builder.AppendLine(c, $"- Tint {tintVersion} against TextMateSharp {referenceVersion}; seed {options.Seed}, {elapsed:hh\\:mm\\:ss} with {options.Workers} workers.");
        builder.AppendLine(c, $"- Per-line time limit {options.LineTimeLimit.TotalSeconds:F0} s on both sides; lines over {options.SlowLine.TotalMilliseconds:F0} ms are reported; a line over {options.HangLimit.TotalSeconds:F0} s is a hang.");
        builder.AppendLine(c, $"- {stats.Values.Sum(s => s.Iterations):N0} inputs, {stats.Values.Sum(s => s.Lines):N0} lines, {stats.Values.Sum(s => s.Characters):N0} characters over {stats.Count} grammars; {findings.Count} unique findings.");
        if (leftBehind > 0)
        {
            builder.AppendLine(c, $"- {leftBehind} workers were still busy (a long input or a minimization) when the campaign stopped; their current input is not counted.");
        }
        builder.AppendLine();

        builder.AppendLine("| Kind | Unique | Inputs |").AppendLine("|---|--:|--:|");
        foreach (var group in findings.GroupBy(f => f.Kind).OrderBy(g => Severity(g.Key)))
        {
            builder.AppendLine(c, $"| {group.Key} | {group.Count()} | {group.Sum(f => f.Count):N0} |");
        }

        builder.AppendLine();
        builder.AppendLine("| Grammar | Inputs | Lines | Tint s | TextMateSharp s | Slowest line Tint ms | Slowest line TextMateSharp ms | Findings |");
        builder.AppendLine("|---|--:|--:|--:|--:|--:|--:|---|");
        foreach (var (scope, s) in stats.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var kinds = string.Join(", ", findings.Where(f => f.ScopeName == scope).GroupBy(f => f.Kind).OrderBy(g => Severity(g.Key)).Select(g => $"{g.Key} {g.Count()}"));
            builder.AppendLine(c, $"| `{scope}` | {s.Iterations:N0} | {s.Lines:N0} | {Seconds(s.TintTicks):F1} | {Seconds(s.ReferenceTicks):F1} | {Seconds(s.TintMaxLineTicks) * 1000:F0} | {Seconds(s.ReferenceMaxLineTicks) * 1000:F0} | {kinds} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Findings").AppendLine();
        foreach (var finding in findings)
        {
            builder.AppendLine(c, $"### {finding.Signature}").AppendLine();
            builder.AppendLine(c, $"- {finding.Count:N0} inputs; first at iteration {finding.Iteration} ({finding.Description}); line {finding.Line} of {finding.Lines.Length} after minimization (from {finding.OriginalLines.Length}).");
            builder.AppendLine(c, $"- Time on that line: Tint {finding.TintMilliseconds:F1} ms, TextMateSharp {finding.ReferenceMilliseconds:F1} ms.");
            if (finding.Verdict is not null)
            {
                builder.AppendLine(c, $"- Referee: {finding.Verdict}.");
            }

            builder.AppendLine().AppendLine("```text");
            foreach (var line in finding.Lines.Take(20))
            {
                builder.AppendLine(Truncate(Comparison.Escape(line), 300));
            }

            if (finding.Lines.Length > 20)
            {
                builder.AppendLine(c, $"… {finding.Lines.Length - 20} more lines");
            }

            builder.AppendLine("```").AppendLine();
            builder.AppendLine("Tint:").AppendLine().AppendLine("```text").Append(Truncate(finding.Tint, 2000)).AppendLine().AppendLine("```").AppendLine();
            builder.AppendLine("TextMateSharp:").AppendLine().AppendLine("```text").Append(Truncate(finding.Reference, 2000)).AppendLine().AppendLine("```").AppendLine();
        }

        return builder.ToString();
    }

    private static double Seconds(long ticks) => (double)ticks / Stopwatch.Frequency;

    // A lone surrogate does not survive UTF-8.
    private static bool HasLoneSurrogate(string line) => Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(line)) != line;

    // Never cuts a surrogate pair in two.
    private static string Truncate(string text, int max)
    {
        if (text.Length <= max)
        {
            return text.TrimEnd();
        }

        return text[..(char.IsHighSurrogate(text[max - 1]) ? max - 1 : max)] + "…";
    }
}
