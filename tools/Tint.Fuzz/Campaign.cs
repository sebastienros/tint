using System.Collections.Concurrent;
using System.Diagnostics;
using Tint.Differential;

namespace Tint.Fuzz;

/// <summary>The settings of a campaign.</summary>
public sealed record FuzzOptions(int Seed, TimeSpan Duration, int Workers, TimeSpan HangLimit, TimeSpan SlowLine, TimeSpan LineTimeLimit, int MinimizeBudget);

/// <summary>A unique finding: the first input that showed it, minimized, and how many inputs showed it since.</summary>
public sealed class Finding
{
    /// <summary>tokens, styles, invalid-utf16, tint-exception, reference-exception, exception-type, stopped-tint, stopped-reference, tint-slow, reference-slow, tint-hang, reference-hang.</summary>
    public required string Kind { get; init; }

    /// <summary>The grammar.</summary>
    public required string ScopeName { get; init; }

    /// <summary>What makes two findings the same: the kind, the grammar and the scopes or exception involved.</summary>
    public required string Signature { get; init; }

    /// <summary>The iteration that found it.</summary>
    public int Iteration { get; init; }

    /// <summary>How the input was generated.</summary>
    public string Description { get; init; } = "";

    /// <summary>The input, minimized when the kind allows it.</summary>
    public string[] Lines { get; set; } = [];

    /// <summary>The 1-based line where the outputs first differ (or that was slow or hung).</summary>
    public int Line { get; set; }

    /// <summary>The input before minimization.</summary>
    public string[] OriginalLines { get; init; } = [];

    /// <summary>Tint's output for that line.</summary>
    public string Tint { get; set; } = "";

    /// <summary>TextMateSharp's output for that line.</summary>
    public string Reference { get; set; } = "";

    /// <summary>Milliseconds Tint spent on the line.</summary>
    public double TintMilliseconds { get; set; }

    /// <summary>Milliseconds TextMateSharp spent on the line.</summary>
    public double ReferenceMilliseconds { get; set; }

    /// <summary>Inputs that showed this finding.</summary>
    public int Count;

    /// <summary>What vscode-textmate says, when the referee ran.</summary>
    public string? Verdict { get; set; }

    /// <summary>Whether vscode-textmate agrees with Tint on the minimized input (null when not judged).</summary>
    public bool? TintAgreesWithVSCode { get; set; }
}

/// <summary>Per-grammar counters.</summary>
public sealed class GrammarStats
{
    /// <summary>Inputs run.</summary>
    public long Iterations;

    /// <summary>Lines tokenized by both engines.</summary>
    public long Lines;

    /// <summary>UTF-16 characters tokenized.</summary>
    public long Characters;

    /// <summary>Ticks in Tint.</summary>
    public long TintTicks;

    /// <summary>Ticks in TextMateSharp.</summary>
    public long ReferenceTicks;

    /// <summary>The slowest line in Tint, in ticks.</summary>
    public long TintMaxLineTicks;

    /// <summary>The slowest line in TextMateSharp, in ticks.</summary>
    public long ReferenceMaxLineTicks;

    /// <summary>Inputs that showed a finding (new or known).</summary>
    public long Hits;
}

/// <summary>Runs inputs on worker threads until the duration ends, with a watchdog for hangs.</summary>
public sealed class Campaign(FuzzOptions options, IEngine tint, IEngine reference, IReadOnlyList<SeedLines> seeds)
{
    private readonly ConcurrentDictionary<string, Finding> _findings = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, GrammarStats> _stats = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<int, Worker> _workers = new();
    private long _next;
    private int _nextWorkerId;
    private long _deadline;

    /// <summary>The unique findings.</summary>
    public IReadOnlyCollection<Finding> Findings => _findings.Values.ToList();

    /// <summary>The counters, per grammar.</summary>
    public IReadOnlyDictionary<string, GrammarStats> Stats => _stats;

    /// <summary>Runs the campaign.</summary>
    public void Run()
    {
        foreach (var seed in seeds)
        {
            _stats[seed.ScopeName] = new GrammarStats();
        }

        _deadline = Stopwatch.GetTimestamp() + (long)(options.Duration.TotalSeconds * Stopwatch.Frequency);
        for (var i = 0; i < options.Workers; i++)
        {
            StartWorker();
        }

        var started = Stopwatch.StartNew();
        var lastStatus = TimeSpan.Zero;
        while (_workers.Values.Any(w => !w.Abandoned && w.Thread.IsAlive))
        {
            Thread.Sleep(250);
            var now = Stopwatch.GetTimestamp();
            foreach (var worker in _workers.Values)
            {
                if (worker.Abandoned || worker.Current is not { } current || now - current.LineStart < options.HangLimit.TotalSeconds * Stopwatch.Frequency)
                {
                    continue;
                }

                // A line ran past the hang limit: record the input, leave the stuck (background) thread behind and
                // carry on with a fresh worker.
                worker.Abandoned = true;
                var lines = current.Lines;
                var kind = current.Side == "Tint" ? "tint-hang" : "reference-hang";
                Record(new Finding
                {
                    Kind = kind,
                    ScopeName = current.Seeds.ScopeName,
                    Signature = $"{kind} {current.Seeds.ScopeName}",
                    Iteration = current.Iteration,
                    Description = current.Description,
                    Lines = lines,
                    OriginalLines = lines,
                    Line = current.LineIndex + 1,
                    Tint = current.Side == "Tint" ? $"still running after {options.HangLimit.TotalSeconds:F0} s" : "",
                    Reference = current.Side == "Tint" ? "" : $"still running after {options.HangLimit.TotalSeconds:F0} s",
                }, minimize: null);
                Console.WriteLine($"  hang: {current.Side} on {current.Seeds.ScopeName} iteration {current.Iteration} line {current.LineIndex + 1}");
                if (Stopwatch.GetTimestamp() < _deadline)
                {
                    StartWorker();
                }
            }

            if (started.Elapsed - lastStatus >= TimeSpan.FromSeconds(30))
            {
                lastStatus = started.Elapsed;
                Console.WriteLine($"  {started.Elapsed:hh\\:mm\\:ss} {Interlocked.Read(ref _next):N0} inputs, {_stats.Values.Sum(s => s.Lines):N0} lines, {_findings.Count} unique findings");
            }
        }
    }

    private void StartWorker()
    {
        var worker = new Worker(Interlocked.Increment(ref _nextWorkerId));
        worker.Thread = new Thread(() => WorkerLoop(worker), maxStackSize: 64 * 1024 * 1024) { IsBackground = true, Name = $"fuzz-{worker.Id}" };
        _workers[worker.Id] = worker;
        worker.Thread.Start();
    }

    private void WorkerLoop(Worker worker)
    {
        while (!worker.Abandoned && Stopwatch.GetTimestamp() < _deadline)
        {
            var n = Interlocked.Increment(ref _next) - 1;
            var grammar = seeds[(int)(n % seeds.Count)];
            var iteration = (int)(n / seeds.Count);
            var lines = Generator.Generate(grammar, options.Seed, iteration, out var description);
            try
            {
                RunInput(worker, grammar, iteration, lines, description);
            }
            catch (Exception e)
            {
                Record(new Finding
                {
                    Kind = "harness",
                    ScopeName = grammar.ScopeName,
                    Signature = $"harness {e.GetType().Name} {TintEngineFrame(e)}",
                    Iteration = iteration,
                    Description = description,
                    Lines = lines,
                    OriginalLines = lines,
                    Tint = e.ToString(),
                }, minimize: null);
            }
        }
    }

    private void RunInput(Worker worker, SeedLines grammar, int iteration, string[] lines, string description)
    {
        var (tintTokenizer, referenceTokenizer) = worker.Get(grammar.ScopeName, tint, reference, options.LineTimeLimit);
        var stats = _stats[grammar.ScopeName];
        tintTokenizer?.Reset();
        referenceTokenizer?.Reset();
        if (tintTokenizer is null || referenceTokenizer is null)
        {
            return;
        }

        Interlocked.Increment(ref stats.Iterations);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            worker.Current = new InFlight(grammar, iteration, description, lines, i, "Tint", Stopwatch.GetTimestamp());
            var tintOutput = tintTokenizer.Tokenize(line);
            if (worker.Abandoned)
            {
                return;
            }

            worker.Current = worker.Current with { Side = "TextMateSharp", LineStart = Stopwatch.GetTimestamp() };
            var referenceOutput = referenceTokenizer.Tokenize(line);
            worker.Current = null;
            if (worker.Abandoned)
            {
                return;
            }

            Interlocked.Increment(ref stats.Lines);
            Interlocked.Add(ref stats.Characters, line.Length);
            Interlocked.Add(ref stats.TintTicks, tintOutput.Ticks);
            Interlocked.Add(ref stats.ReferenceTicks, referenceOutput.Ticks);
            Max(ref stats.TintMaxLineTicks, tintOutput.Ticks);
            Max(ref stats.ReferenceMaxLineTicks, referenceOutput.Ticks);

            var slowTicks = options.SlowLine.TotalSeconds * Stopwatch.Frequency;
            var kind = Classify(tintOutput, referenceOutput, lines);
            if (kind is null && (tintOutput.Ticks > slowTicks || referenceOutput.Ticks > slowTicks))
            {
                // Slow, not different: one finding per grammar and side.
                kind = tintOutput.Ticks > slowTicks ? "tint-slow" : "reference-slow";
            }

            if (kind is null)
            {
                continue;
            }

            Interlocked.Increment(ref stats.Hits);
            var finding = new Finding
            {
                Kind = kind,
                ScopeName = grammar.ScopeName,
                Signature = Signature(kind, grammar.ScopeName, line, tintOutput, referenceOutput),
                Iteration = iteration,
                Description = description,
                Lines = lines[..(i + 1)],
                OriginalLines = lines,
                Line = i + 1,
                Tint = Comparison.Format(tintOutput, line, kind == "styles" ? Difference.Styles : Difference.Tokens),
                Reference = Comparison.Format(referenceOutput, line, kind == "styles" ? Difference.Styles : Difference.Tokens),
                TintMilliseconds = tintOutput.Ticks * 1000.0 / Stopwatch.Frequency,
                ReferenceMilliseconds = referenceOutput.Ticks * 1000.0 / Stopwatch.Frequency,
            };
            var differs = kind is not ("tint-slow" or "reference-slow" or "stopped-tint" or "stopped-reference");
            Record(finding, differs ? candidate => Classify(candidate, tintTokenizer, referenceTokenizer, out _) == kind : null, tintTokenizer, referenceTokenizer);

            // Later lines start from different states: stop this input at its first finding.
            return;
        }
    }

    private static string? Classify(LineOutput tint, LineOutput reference, string[] lines)
    {
        return Comparison.Compare(tint, reference) switch
        {
            Difference.None => null,
            Difference.Exception => tint.Exception is null ? "reference-exception" : reference.Exception is null ? "tint-exception" : "exception-type",
            Difference.StoppedEarly => tint.StoppedEarly ? "stopped-tint" : "stopped-reference",
            // Onigwrap works on UTF-8: a lone surrogate shifts TextMateSharp's offsets.
            Difference.Tokens => lines.Any(HasLoneSurrogate) ? "invalid-utf16" : "tokens",
            _ => lines.Any(HasLoneSurrogate) ? "invalid-utf16" : "styles",
        };
    }

    // Runs a candidate input from the initial state; the kind of its first difference (or null), and that line.
    private static string? Classify(string[] lines, ILineTokenizer tintTokenizer, ILineTokenizer referenceTokenizer, out int line)
    {
        tintTokenizer.Reset();
        referenceTokenizer.Reset();
        for (line = 0; line < lines.Length; line++)
        {
            var kind = Classify(tintTokenizer.Tokenize(lines[line]), referenceTokenizer.Tokenize(lines[line]), lines);
            if (kind is not null)
            {
                return kind;
            }
        }

        return null;
    }

    private static string Signature(string kind, string scopeName, string line, LineOutput tint, LineOutput reference)
    {
        if (kind is "tint-exception" or "reference-exception" or "exception-type")
        {
            return $"{kind} {scopeName} {Type(tint.Exception)} {Type(reference.Exception)}";
        }

        if (kind is "tokens")
        {
            // The innermost scopes of the first differing token on each side.
            var index = 0;
            while (index < tint.Tokens.Length && index < reference.Tokens.Length && tint.Tokens[index] == reference.Tokens[index])
            {
                index++;
            }

            return $"{kind} {scopeName} {Last(tint.Tokens, index)} | {Last(reference.Tokens, index)}";
        }

        return $"{kind} {scopeName}";

        static string Last(ScopeToken[] tokens, int index) => index < tokens.Length ? tokens[index].Scopes[(tokens[index].Scopes.LastIndexOf(' ') + 1)..] : "(none)";

        static string? Type(string? exception) => exception?[..exception.IndexOf(':')];
    }

    private void Record(Finding finding, Func<string[], bool>? minimize, ILineTokenizer? tintTokenizer = null, ILineTokenizer? referenceTokenizer = null)
    {
        if (_findings.TryGetValue(finding.Signature, out var known))
        {
            Interlocked.Increment(ref known.Count);
            return;
        }

        if (minimize is not null && tintTokenizer is not null && referenceTokenizer is not null)
        {
            finding.Lines = Minimizer.Minimize(finding.Lines, minimize, options.MinimizeBudget);
            Classify(finding.Lines, tintTokenizer, referenceTokenizer, out var line);
            finding.Line = Math.Min(line + 1, finding.Lines.Length);

            // Re-run the minimized input for the outputs of its differing line.
            tintTokenizer.Reset();
            referenceTokenizer.Reset();
            for (var i = 0; i < finding.Line; i++)
            {
                var tintOutput = tintTokenizer.Tokenize(finding.Lines[i]);
                var referenceOutput = referenceTokenizer.Tokenize(finding.Lines[i]);
                if (i == finding.Line - 1)
                {
                    var difference = finding.Kind == "styles" ? Difference.Styles : Difference.Tokens;
                    finding.Tint = Comparison.Format(tintOutput, finding.Lines[i], difference);
                    finding.Reference = Comparison.Format(referenceOutput, finding.Lines[i], difference);
                }
            }
        }

        finding.Count = 1;
        if (_findings.TryAdd(finding.Signature, finding))
        {
            Console.WriteLine($"  new: {finding.Signature} (iteration {finding.Iteration})");
        }
    }

    private static bool HasLoneSurrogate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(text[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static void Max(ref long target, long value)
    {
        var current = Interlocked.Read(ref target);
        while (value > current && Interlocked.CompareExchange(ref target, value, current) != current)
        {
            current = Interlocked.Read(ref target);
        }
    }

    private static string TintEngineFrame(Exception e) => e.StackTrace?.Split('\n', StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";

    private sealed record InFlight(SeedLines Seeds, int Iteration, string Description, string[] Lines, int LineIndex, string Side, long LineStart);

    private sealed class Worker(int id)
    {
        private readonly Dictionary<string, (ILineTokenizer?, ILineTokenizer?)> _tokenizers = new(StringComparer.Ordinal);

        public int Id { get; } = id;

        public Thread Thread { get; set; } = null!;

        public volatile bool Abandoned;

        public InFlight? Current { get; set; }

        public (ILineTokenizer? Tint, ILineTokenizer? Reference) Get(string scopeName, IEngine tint, IEngine reference, TimeSpan timeLimit)
        {
            if (!_tokenizers.TryGetValue(scopeName, out var pair))
            {
                pair = (tint.CreateTokenizer(scopeName, timeLimit), reference.CreateTokenizer(scopeName, timeLimit));
                _tokenizers[scopeName] = pair;
            }

            return pair;
        }
    }
}
