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
    repoOption, outOption, seedOption, durationOption, workersOption, grammarsOption, hangOption, slowOption, lineLimitOption, minimizeOption, refereeOption, corpusOption,
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
        result.GetValue(minimizeOption));

    var seeds = Seeds.Load(repository, corpus, g => filter.Count == 0 || filter.Contains(g.ScopeName) || (g.Language is not null && filter.Contains(g.Language)));
    var tint = new TintEngine(repository);
    var reference = new TextMateSharpEngine(ReferenceGrammars.Same, repository);
    Console.WriteLine($"{seeds.Count} grammars, seed {options.Seed}, {options.Duration}, {options.Workers} workers; Tint {tint.Version} vs TextMateSharp {reference.Version}");

    var stopwatch = Stopwatch.StartNew();
    var campaign = new Campaign(options, tint, reference, seeds);
    campaign.Run();
    stopwatch.Stop();

    var findings = campaign.Findings.OrderBy(f => Report.Severity(f.Kind)).ThenBy(f => f.ScopeName, StringComparer.Ordinal).ThenBy(f => f.Signature, StringComparer.Ordinal).ToList();
    var referee = new Referee(repository, tint, reference);
    if (result.GetValue(refereeOption) && referee.IsAvailable)
    {
        Report.Judge(referee, findings, Path.Combine(outDirectory, "inputs"));
    }

    Directory.CreateDirectory(outDirectory);
    File.WriteAllText(Path.Combine(outDirectory, "findings.md"), Report.Write(options, tint.Version, reference.Version, stopwatch.Elapsed, campaign.Stats, findings));
    File.WriteAllText(Path.Combine(outDirectory, "findings.json"), JsonSerializer.Serialize(new { options.Seed, Duration = stopwatch.Elapsed.ToString(), Stats = campaign.Stats, Findings = findings }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
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
        for (var i = 0; i < lines.Length; i++)
        {
            var tintOutput = tintTokenizer.Tokenize(lines[i]);
            var referenceOutput = referenceTokenizer.Tokenize(lines[i]);
            var difference = Comparison.Compare(tintOutput, referenceOutput);
            if (difference != Difference.None)
            {
                outcome = $"{difference} at line {i + 1} (Tint {tintOutput.Ticks * 1000.0 / Stopwatch.Frequency:F0} ms, TextMateSharp {referenceOutput.Ticks * 1000.0 / Stopwatch.Frequency:F0} ms)";
                differing++;
                break;
            }
        }

        Console.WriteLine($"{finding.GetProperty("Signature").GetString()}: {outcome}");
    }

    return differing == 0 ? 0 : 1;
});
root.Add(repro);

return await root.Parse(args).InvokeAsync();
