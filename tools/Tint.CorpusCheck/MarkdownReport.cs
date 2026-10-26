using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Tint.Differential;

namespace Tint.CorpusCheck;

/// <summary>The run's context, for the report header.</summary>
public sealed record RunInfo(string TintVersion, string ReferenceVersion, ReferenceGrammars Grammars, string VSCodeCommit, CorpusDefinition Corpus, int FilesSelected, int Cap, int Threads, TimeSpan Duration, TimeSpan TimeLimit);

/// <summary>Writes <c>report.md</c>.</summary>
public static class MarkdownReport
{
    private const int MaxMismatchesPerGrammar = 10;

    /// <summary>The report.</summary>
    public static string Write(RunInfo info, IReadOnlyList<GrammarReport> reports, IReadOnlyList<(string Scope, string? Tint, string? Reference)> versions)
    {
        var builder = new StringBuilder();
        var c = CultureInfo.InvariantCulture;
        builder.AppendLine("# Corpus check").AppendLine();
        builder.AppendLine(c, $"- Date: {DateTime.Now:yyyy-MM-dd}; machine: {RuntimeInformation.OSDescription} {RuntimeInformation.ProcessArchitecture}, {Environment.ProcessorCount} cores, {RuntimeInformation.FrameworkDescription}; {info.Threads} threads, {info.Duration.TotalSeconds:F0} s.");
        builder.AppendLine(c, $"- Tint {info.TintVersion} with its bundled grammars (VS Code `{info.VSCodeCommit[..8]}`) and Dark+.");
        builder.AppendLine(c, $"- Reference: TextMateSharp {info.ReferenceVersion} with Dark+.");
        builder.AppendLine(c, $"- {info.FilesSelected:N0} files selected, at most {info.Cap} per language; per-line time limit {info.TimeLimit.TotalSeconds:F0} s.");
        builder.AppendLine("- Each file is tokenized line by line from the initial state, carrying the state, with `TokenizeLine` (offsets and scopes compared) and `TokenizeLine2` (foreground, background and font style compared after merging equal neighbours). Times include both calls and the first use of each rule.");
        builder.AppendLine();
        builder.Append("Repositories: ");
        builder.AppendLine(string.Join(", ", info.Corpus.Repositories.Select(r => $"[{r.Name}]({r.Url}/tree/{r.Commit}) ({r.License})")) + ".");
        builder.AppendLine();

        var total = new GrammarReport { Language = "**total**", ScopeName = "" };
        var judged = reports.SelectMany(r => r.Mismatches).Where(m => m.TintAgreesWithVSCode is not null).ToList();
        if (judged.Count > 0)
        {
            builder.AppendLine(c, $"Referee (vscode-textmate {""}9.3.2 over vscode-oniguruma, same grammar files): of {judged.Count} files whose first mismatch is in the scopes or styles, vscode-textmate agrees with Tint on {judged.Count(m => m.TintAgreesWithVSCode == true)} and differs from Tint on {judged.Count(m => m.TintAgreesWithVSCode == false)}.");
            builder.AppendLine();
        }

        builder.AppendLine("| Language | Scope | Files | Lines | Mismatched files | Mismatched lines | Style-only lines | Stopped early | Tint exceptions | Reference exceptions | Tint s | Reference s | Speedup |");
        builder.AppendLine("|---|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|");
        foreach (var report in reports)
        {
            Row(builder, report);
            total.Files += report.Files;
            total.FilesSkipped += report.FilesSkipped;
            total.Lines += report.Lines;
            total.MismatchedFiles += report.MismatchedFiles;
            total.MismatchedLines += report.MismatchedLines;
            total.StyleOnlyLines += report.StyleOnlyLines;
            total.StoppedEarlyLines += report.StoppedEarlyLines;
            total.TintExceptions += report.TintExceptions;
            total.ReferenceExceptions += report.ReferenceExceptions;
            total.TintTicks += report.TintTicks;
            total.ReferenceTicks += report.ReferenceTicks;
        }

        Row(builder, total);
        builder.AppendLine();

        if (versions.Count > 0)
        {
            builder.AppendLine("## Grammar versions").AppendLine();
            builder.AppendLine("The `version` header of each grammar file in Tint's bundle and in TextMateSharp.Grammars 2.0.4, for the scopes both have.").AppendLine();
            builder.AppendLine("| Scope | Same | Tint | TextMateSharp.Grammars |");
            builder.AppendLine("|---|---|---|---|");
            foreach (var (scope, tintVersion, referenceVersion) in versions)
            {
                builder.AppendLine(c, $"| `{scope}` | {(tintVersion == referenceVersion ? "yes" : "**no**")} | {Short(tintVersion)} | {Short(referenceVersion)} |");
            }

            builder.AppendLine();
        }

        foreach (var report in reports.Where(r => r.Mismatches.Count > 0 || r.Issues.Count > 0))
        {
            builder.AppendLine(c, $"## {report.Language} (`{report.ScopeName}`)").AppendLine();
            foreach (var issue in report.Issues.Where(i => i.Side != "skipped"))
            {
                builder.AppendLine(c, $"- {issue.Side} exception at {issue.File}:{issue.Line}: `{issue.Message.Split('\n')[0]}`");
            }

            var skipped = report.Issues.Count(i => i.Side == "skipped");
            if (skipped > 0)
            {
                builder.AppendLine(c, $"- {skipped} files skipped: {report.Issues.First(i => i.Side == "skipped").Message} (first: {report.Issues.First(i => i.Side == "skipped").File})");
            }

            foreach (var mismatch in report.Mismatches.Take(MaxMismatchesPerGrammar))
            {
                builder.AppendLine();
                builder.AppendLine(c, $"### {mismatch.File}:{mismatch.Line} ({mismatch.Kind})").AppendLine();
                if (mismatch.Verdict is not null)
                {
                    builder.AppendLine(c, $"Referee: {mismatch.Verdict}.").AppendLine();
                }

                builder.AppendLine("```text").AppendLine(Truncate(mismatch.Text, 400)).AppendLine("```").AppendLine();
                builder.AppendLine("Tint:").AppendLine().AppendLine("```text").Append(Truncate(mismatch.Tint, 3000)).AppendLine("```").AppendLine();
                builder.AppendLine("Reference:").AppendLine().AppendLine("```text").Append(Truncate(mismatch.Reference, 3000)).AppendLine("```");
            }

            if (report.Mismatches.Count > MaxMismatchesPerGrammar)
            {
                builder.AppendLine().AppendLine(c, $"… and {report.Mismatches.Count - MaxMismatchesPerGrammar} more files (`report.json`).");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static void Row(StringBuilder builder, GrammarReport report)
    {
        var tintSeconds = (double)report.TintTicks / Stopwatch.Frequency;
        var referenceSeconds = (double)report.ReferenceTicks / Stopwatch.Frequency;
        var speedup = tintSeconds > 0 ? $"{referenceSeconds / tintSeconds:F2}x" : "";
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"| {report.Language} | {(report.ScopeName.Length > 0 ? $"`{report.ScopeName}`" : "")} | {report.Files:N0}{(report.FilesSkipped > 0 ? $" (+{report.FilesSkipped} skipped)" : "")} | {report.Lines:N0} | {report.MismatchedFiles:N0} | {report.MismatchedLines:N0} | {report.StyleOnlyLines:N0} | {report.StoppedEarlyLines:N0} | {report.TintExceptions} | {report.ReferenceExceptions} | {tintSeconds:F2} | {referenceSeconds:F2} | {speedup} |");
    }

    private static string Short(string? version) => version is null ? "(none)" : version.Length > 60 ? $"`…{version[^40..]}`" : $"`{version}`";

    // Never cuts a surrogate pair in two.
    private static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..(char.IsHighSurrogate(text[max - 1]) ? max - 1 : max)] + "…\n";
}
