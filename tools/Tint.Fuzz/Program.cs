using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using Tint.Differential;
using Tint.Fuzz;

var repoOption = new Option<DirectoryInfo?>("--repo")
{
    Description = "The Tint checkout. Defaults to the nearest directory containing Tint.slnx.",
};
var outOption = new Option<DirectoryInfo?>("--out")
{
    Description = "Where findings.md, findings.json and the per-finding inputs go. Defaults to artifacts/fuzz in the checkout.",
};
var seedOption = new Option<int>("--seed") { Description = "The campaign seed: the same seed generates the same inputs.", DefaultValueFactory = _ => 20261025 };
var durationOption = new Option<TimeSpan>("--duration") { Description = "The time budget (hh:mm:ss).", DefaultValueFactory = _ => TimeSpan.FromMinutes(20) };
var workersOption = new Option<int>("--workers") { Description = "Worker threads.", DefaultValueFactory = _ => Math.Max(1, Environment.ProcessorCount - 2) };
var grammarsOption = new Option<string[]>("--grammars")
{
    Description = "Only these scope names or language ids (comma-separated or repeated). Defaults to every bundled grammar.",
    AllowMultipleArgumentsPerToken = true,
};
var hangOption = new Option<int>("--hang-seconds") { Description = "A line running longer is a hang: its thread is abandoned.", DefaultValueFactory = _ => 60 };
var slowOption = new Option<int>("--slow-line-ms") { Description = "Lines slower than this on either side are reported.", DefaultValueFactory = _ => 1000 };
var lineLimitOption = new Option<int>("--line-time-limit-ms") { Description = "The per-line time limit of both engines, 0 for none.", DefaultValueFactory = _ => 10_000 };
var minimizeOption = new Option<int>("--minimize-budget") { Description = "Runs spent minimizing a new finding.", DefaultValueFactory = _ => 300 };
var minimizeSecondsOption = new Option<int>("--minimize-seconds") { Description = "Time spent minimizing a new finding.", DefaultValueFactory = _ => 30 };
var refereeOption = new Option<bool>("--referee")
{
    Description = "Judge token and style findings with vscode-textmate (needs Node and npm install in tools/Tint.CorpusCheck/referee).",
    DefaultValueFactory = _ => true,
};
var corpusOption = new Option<DirectoryInfo?>("--corpus")
{
    Description = "Corpus repositories (tools/Tint.CorpusCheck/fetch.sh) to draw seed lines from, besides the samples. Defaults to artifacts/corpus.",
};

var root = new RootCommand("Differential fuzzer: mutated lines tokenized by Tint and TextMateSharp 2.0.4, compared for tokens, styles, exceptions and time.")
{
    repoOption, outOption, seedOption, durationOption, workersOption, grammarsOption, hangOption, slowOption, lineLimitOption, minimizeOption, minimizeSecondsOption, refereeOption, corpusOption,
};
root.SetAction(result =>
{
    var repository = RepositoryData.Load(result.GetValue(repoOption)?.FullName);
    var outDirectory = result.GetValue(outOption)?.FullName ?? Path.Combine(repository.Root, "artifacts", "fuzz");
    var corpus = result.GetValue(corpusOption)?.FullName ?? Path.Combine(repository.Root, "artifacts", "corpus");
    var filter = (result.GetValue(grammarsOption) ?? []).SelectMany(g => g.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToHashSet(StringComparer.Ordinal);
    var options = new FuzzOptions(
        result.GetValue(seedOption),
        result.GetValue(durationOption),
        result.GetValue(workersOption),
        TimeSpan.FromSeconds(result.GetValue(hangOption)),
        TimeSpan.FromMilliseconds(result.GetValue(slowOption)),
        TimeSpan.FromMilliseconds(result.GetValue(lineLimitOption)),
        result.GetValue(minimizeOption),
        TimeSpan.FromSeconds(result.GetValue(minimizeSecondsOption)));

    var seeds = Seeds.Load(repository, corpus, g => filter.Count == 0 || filter.Contains(g.ScopeName) || (g.Language is not null && filter.Contains(g.Language)));
    var tint = new TintEngine(repository);
    var reference = new TextMateSharpEngine(ReferenceGrammars.Same, repository);
    Console.WriteLine($"{seeds.Count} grammars, seed {options.Seed}, {options.Duration}, {options.Workers} workers; Tint {tint.Version} vs TextMateSharp {reference.Version}");

    var stopwatch = Stopwatch.StartNew();
    var campaign = new Campaign(options, tint, reference, seeds);
    campaign.Run();
    stopwatch.Stop();

    var findings = campaign.Findings.OrderBy(f => Report.Severity(f.Kind)).ThenBy(f => f.ScopeName, StringComparer.Ordinal).ThenBy(f => f.Signature, StringComparer.Ordinal).ToList();

    // Inputs hold lone surrogates: JSON escapes them, and the Markdown report replaces them rather than failing.
    var encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
    Directory.CreateDirectory(outDirectory);
    void Save()
    {
        File.WriteAllText(Path.Combine(outDirectory, "findings.json"), JsonSerializer.Serialize(new SavedCampaign { Seed = options.Seed, Duration = stopwatch.Elapsed.ToString(), Workers = options.Workers, HangLimit = options.HangLimit, SlowLine = options.SlowLine, LineTimeLimit = options.LineTimeLimit, LeftBehind = campaign.LeftBehind, Stats = new(campaign.Stats), Findings = findings }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }), encoding);
        File.WriteAllText(Path.Combine(outDirectory, "findings.md"), Report.Write(options, tint.Version, reference.Version, stopwatch.Elapsed, campaign.Stats, findings, campaign.LeftBehind), encoding);
    }

    Save();
    var referee = new Referee(repository, tint, reference);
    if (result.GetValue(refereeOption) && referee.IsAvailable)
    {
        Report.Judge(referee, findings, Path.Combine(outDirectory, "inputs"));
        Save();
    }

    Console.WriteLine($"{campaign.Stats.Values.Sum(s => s.Iterations):N0} inputs, {campaign.Stats.Values.Sum(s => s.Lines):N0} lines, {findings.Count} unique findings; report in {outDirectory}");

    // Exit code 1 when Tint threw, hung, was slow or disagrees with vscode-textmate.
    return findings.Any(f => f.Kind is "tint-exception" or "tint-hang" or "tint-slow" or "stopped-tint" or "harness" || f.TintAgreesWithVSCode == false) ? 1 : 0;
});

var findingArgument = new Argument<FileInfo>("finding") { Description = "A findings.json file." };
var signatureOption = new Option<string?>("--signature") { Description = "The finding to replay; all of them when omitted." };
var repro = new Command("repro", "Replays findings from a findings.json with both engines and prints whether they still differ.")
{
    repoOption, findingArgument, signatureOption, lineLimitOption,
};
repro.SetAction(result =>
{
    var repository = RepositoryData.Load(result.GetValue(repoOption)?.FullName);
    using var document = JsonDocument.Parse(File.ReadAllText(result.GetValue(findingArgument)!.FullName));
    var tint = new TintEngine(repository);
    var reference = new TextMateSharpEngine(ReferenceGrammars.Same, repository);
    var limit = TimeSpan.FromMilliseconds(result.GetValue(lineLimitOption));
    var signature = result.GetValue(signatureOption);
    var differing = 0;
    foreach (var finding in document.RootElement.GetProperty("Findings").EnumerateArray())
    {
        if (signature is not null && finding.GetProperty("Signature").GetString() != signature)
        {
            continue;
        }

        var scope = finding.GetProperty("ScopeName").GetString()!;
        var lines = finding.GetProperty("Lines").EnumerateArray().Select(l => l.GetString()!).ToArray();
        var tintTokenizer = tint.CreateTokenizer(scope, limit)!;
        var referenceTokenizer = reference.CreateTokenizer(scope, limit)!;
        var outcome = "same";
        long tintMax = 0, referenceMax = 0;

        // Twice: the second run is warm (rules compiled, patterns parsed), and its slowest lines are reported.
        for (var run = 0; run < 2; run++)
        {
            tintTokenizer.Reset();
            referenceTokenizer.Reset();
            tintMax = referenceMax = 0;
            for (var i = 0; i < lines.Length; i++)
            {
                var tintOutput = tintTokenizer.Tokenize(lines[i]);
                var referenceOutput = referenceTokenizer.Tokenize(lines[i]);
                tintMax = Math.Max(tintMax, tintOutput.Ticks);
                referenceMax = Math.Max(referenceMax, referenceOutput.Ticks);
                var difference = Comparison.Compare(tintOutput, referenceOutput);
                if (difference != Difference.None && run == 1 && outcome == "same")
                {
                    outcome = $"{difference} at line {i + 1}";
                    differing++;
                }
            }
        }

        Console.WriteLine($"{finding.GetProperty("Signature").GetString()}: {outcome}; slowest line warm: Tint {tintMax * 1000.0 / Stopwatch.Frequency:F0} ms, TextMateSharp {referenceMax * 1000.0 / Stopwatch.Frequency:F0} ms");
    }

    return differing == 0 ? 0 : 1;
});
root.Add(repro);

var judgeArgument = new Argument<FileInfo>("findings") { Description = "A findings.json file, rewritten with the new verdicts along with findings.md." };
var judge = new Command("judge", "Judges the findings of a campaign again with vscode-textmate (after a referee change) and rewrites the report.")
{
    repoOption, judgeArgument,
};
judge.SetAction(result =>
{
    var repository = RepositoryData.Load(result.GetValue(repoOption)?.FullName);
    var path = result.GetValue(judgeArgument)!.FullName;
    var jsonOptions = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };
    var campaign = JsonSerializer.Deserialize<SavedCampaign>(File.ReadAllText(path), jsonOptions)!;
    var tint = new TintEngine(repository);
    var reference = new TextMateSharpEngine(ReferenceGrammars.Same, repository);
    var referee = new Referee(repository, tint, reference);
    if (!referee.IsAvailable)
    {
        Console.Error.WriteLine("The referee is not installed: npm ci in tools/Tint.CorpusCheck/referee.");
        return 2;
    }

    foreach (var finding in campaign.Findings)
    {
        finding.Verdict = null;
        finding.TintAgreesWithVSCode = null;
    }

    var directory = Path.GetDirectoryName(path)!;
    Report.Judge(referee, campaign.Findings, Path.Combine(directory, "inputs"));
    var options = new FuzzOptions(campaign.Seed, TimeSpan.Zero, campaign.Workers, campaign.HangLimit, campaign.SlowLine, campaign.LineTimeLimit, 0, TimeSpan.Zero);
    var encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
    File.WriteAllText(path, JsonSerializer.Serialize(campaign, jsonOptions), encoding);
    File.WriteAllText(Path.Combine(directory, "findings.md"), Report.Write(options, tint.Version, reference.Version, TimeSpan.Parse(campaign.Duration, System.Globalization.CultureInfo.InvariantCulture), campaign.Stats, campaign.Findings, campaign.LeftBehind), encoding);
    Console.WriteLine($"{campaign.Findings.Count(f => f.TintAgreesWithVSCode == false)} findings where vscode-textmate differs from Tint");
    return 0;
});
root.Add(judge);

return await root.Parse(args).InvokeAsync();

/// <summary>What findings.json holds: enough to rewrite the report.</summary>
internal sealed class SavedCampaign
{
    public int Seed { get; set; }

    public string Duration { get; set; } = "";

    public int Workers { get; set; }

    public TimeSpan HangLimit { get; set; }

    public TimeSpan SlowLine { get; set; }

    public TimeSpan LineTimeLimit { get; set; }

    public int LeftBehind { get; set; }

    public Dictionary<string, GrammarStats> Stats { get; set; } = [];

    public List<Finding> Findings { get; set; } = [];
}
