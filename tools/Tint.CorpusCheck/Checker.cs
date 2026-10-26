using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Tint.Differential;

namespace Tint.CorpusCheck;

/// <summary>The first differing line of a file.</summary>
public sealed record Mismatch(string File, int Line, string Kind, string Text, string Tint, string Reference)
{
    /// <summary>The file on disk.</summary>
    public string FullPath { get; init; } = "";

    /// <summary>What vscode-textmate says, when the referee ran.</summary>
    public string? Verdict { get; set; }

    /// <summary>Whether vscode-textmate tokenizes the file like Tint up to this line (null when not judged).</summary>
    public bool? TintAgreesWithVSCode { get; set; }
}

/// <summary>An exception or a skipped file.</summary>
public sealed record FileIssue(string File, int Line, string Side, string Message);

/// <summary>The results of one grammar.</summary>
public sealed class GrammarReport
{
    /// <summary>The language id.</summary>
    public required string Language { get; init; }

    /// <summary>The grammar scope.</summary>
    public required string ScopeName { get; init; }

    /// <summary>Files tokenized to the end or to an exception.</summary>
    public int Files;

    /// <summary>Files skipped (longest line over the limit, or no reference grammar).</summary>
    public int FilesSkipped;

    /// <summary>Lines compared.</summary>
    public long Lines;

    /// <summary>Lines whose scopes differ (cascades included: a file's state may differ after its first mismatch).</summary>
    public long MismatchedLines;

    /// <summary>Lines with the same scopes and different colors or font styles.</summary>
    public long StyleOnlyLines;

    /// <summary>Files with at least one differing line.</summary>
    public int MismatchedFiles;

    /// <summary>Lines where exactly one side ran out of the time limit.</summary>
    public long StoppedEarlyLines;

    /// <summary>Exceptions thrown by Tint.</summary>
    public int TintExceptions;

    /// <summary>Exceptions thrown by the reference.</summary>
    public int ReferenceExceptions;

    /// <summary>Time in Tint, in <see cref="Stopwatch"/> ticks.</summary>
    public long TintTicks;

    /// <summary>Time in the reference, in <see cref="Stopwatch"/> ticks.</summary>
    public long ReferenceTicks;

    /// <summary>The first mismatch of each file.</summary>
    public List<Mismatch> Mismatches { get; } = [];

    /// <summary>Exceptions and skipped files.</summary>
    public List<FileIssue> Issues { get; } = [];
}

/// <summary>Tokenizes the corpus with both engines and compares every line.</summary>
public sealed class Checker(IEngine tint, IEngine reference, TimeSpan timeLimit, int maxLineLength, int threads)
{
    private sealed class Worker
    {
        public Dictionary<string, (ILineTokenizer? Tint, ILineTokenizer? Reference)> Tokenizers { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>Runs the files, largest first, and returns one report per grammar.</summary>
    public List<GrammarReport> Run(IReadOnlyList<CorpusFile> files)
    {
        var reports = new ConcurrentDictionary<string, GrammarReport>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            reports.GetOrAdd(file.ScopeName, _ => new GrammarReport { Language = file.Language, ScopeName = file.ScopeName });
        }

        var ordered = files.OrderByDescending(f => f.Size).ToList();
        var workers = new ThreadLocal<Worker>(() => new Worker());
        var done = 0;
        var totalBytes = ordered.Sum(f => f.Size);
        long doneBytes = 0;
        var stopwatch = Stopwatch.StartNew();
        using var timer = new Timer(_ => Console.WriteLine(
            $"  {stopwatch.Elapsed:hh\\:mm\\:ss} {Volatile.Read(ref done)}/{ordered.Count} files, {100.0 * Interlocked.Read(ref doneBytes) / Math.Max(1, totalBytes):F1}% of bytes"),
            null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));

        Parallel.ForEach(
            Partitioner.Create(ordered, EnumerablePartitionerOptions.NoBuffering),
            new ParallelOptions { MaxDegreeOfParallelism = threads },
            file =>
            {
                var report = reports[file.ScopeName];
                try
                {
                    CheckFile(file, report, workers.Value!);
                }
                catch (Exception e)
                {
                    lock (report)
                    {
                        report.Issues.Add(new FileIssue(Name(file), 0, "checker", e.ToString()));
                    }
                }

                Interlocked.Increment(ref done);
                Interlocked.Add(ref doneBytes, file.Size);
            });

        foreach (var report in reports.Values)
        {
            report.Mismatches.Sort((a, b) => string.CompareOrdinal(a.File, b.File));
            report.Issues.Sort((a, b) => string.CompareOrdinal(a.File, b.File));
        }

        return [.. reports.Values.OrderBy(r => r.Language, StringComparer.Ordinal)];
    }

    private void CheckFile(CorpusFile file, GrammarReport report, Worker worker)
    {
        var lines = Lines.Read(file.FullPath);
        var longest = lines.Length == 0 ? 0 : lines.Max(l => l.Length);
        if (longest > maxLineLength)
        {
            lock (report)
            {
                report.FilesSkipped++;
                report.Issues.Add(new FileIssue(Name(file), 0, "skipped", $"a line has {longest} characters (--max-line-length {maxLineLength})"));
            }

            return;
        }

        if (!worker.Tokenizers.TryGetValue(file.ScopeName, out var pair))
        {
            pair = (tint.CreateTokenizer(file.ScopeName, timeLimit), reference.CreateTokenizer(file.ScopeName, timeLimit));
            worker.Tokenizers[file.ScopeName] = pair;
        }

        if (pair.Tint is null || pair.Reference is null)
        {
            lock (report)
            {
                report.FilesSkipped++;
                report.Issues.Add(new FileIssue(Name(file), 0, "skipped", $"no {(pair.Tint is null ? "Tint" : "reference")} grammar for {file.ScopeName}"));
            }

            return;
        }

        pair.Tint.Reset();
        pair.Reference.Reset();
        long lineCount = 0, mismatched = 0, styleOnly = 0, stopped = 0, tintTicks = 0, referenceTicks = 0;
        int tintExceptions = 0, referenceExceptions = 0;
        Mismatch? first = null;
        FileIssue? issue = null;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var tintOutput = pair.Tint.Tokenize(line);
            var referenceOutput = pair.Reference.Tokenize(line);
            tintTicks += tintOutput.Ticks;
            referenceTicks += referenceOutput.Ticks;
            lineCount++;
            if (tintOutput.Exception is not null || referenceOutput.Exception is not null)
            {
                tintExceptions += tintOutput.Exception is null ? 0 : 1;
                referenceExceptions += referenceOutput.Exception is null ? 0 : 1;
                issue = new FileIssue(Name(file), i + 1, tintOutput.Exception is null ? "reference" : referenceOutput.Exception is null ? "Tint" : "both", tintOutput.Exception ?? referenceOutput.Exception!);
                if (Comparison.Compare(tintOutput, referenceOutput) != Difference.None)
                {
                    first ??= Describe(file, i, line, tintOutput, referenceOutput, Difference.Exception);
                }

                break;
            }

            var difference = Comparison.Compare(tintOutput, referenceOutput);
            switch (difference)
            {
                case Difference.Tokens:
                    mismatched++;
                    break;
                case Difference.Styles:
                    styleOnly++;
                    break;
                case Difference.StoppedEarly:
                    stopped++;
                    break;
            }

            if (difference != Difference.None)
            {
                first ??= Describe(file, i, line, tintOutput, referenceOutput, difference);
            }
        }

        lock (report)
        {
            report.Files++;
            report.Lines += lineCount;
            report.MismatchedLines += mismatched;
            report.StyleOnlyLines += styleOnly;
            report.StoppedEarlyLines += stopped;
            report.TintExceptions += tintExceptions;
            report.ReferenceExceptions += referenceExceptions;
            report.TintTicks += tintTicks;
            report.ReferenceTicks += referenceTicks;
            if (first is not null)
            {
                report.MismatchedFiles++;
                report.Mismatches.Add(first);
            }

            if (issue is not null)
            {
                report.Issues.Add(issue);
            }
        }
    }

    private static Mismatch Describe(CorpusFile file, int index, string line, LineOutput tint, LineOutput reference, Difference difference)
        => new(Name(file), index + 1, difference.ToString(), Comparison.Escape(line), Comparison.Format(tint, line, difference), Comparison.Format(reference, line, difference)) { FullPath = file.FullPath };

    private static string Name(CorpusFile file) => $"{file.Repository}/{file.RelativePath}";
}
