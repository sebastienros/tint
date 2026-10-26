using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using Tint.CorpusCheck;
using Tint.Differential;

var repoOption = new Option<DirectoryInfo?>("--repo")
{
    Description = "The Tint checkout. Defaults to the nearest directory containing Tint.slnx.",
};
var corpusOption = new Option<DirectoryInfo?>("--corpus")
{
    Description = "Where fetch.sh cloned the repositories. Defaults to artifacts/corpus in the checkout.",
};
var outOption = new Option<DirectoryInfo?>("--out")
{
    Description = "The output directory for report.md and report.json. Defaults to artifacts/corpus-check in the checkout.",
};
var referenceOption = new Option<ReferenceGrammars>("--reference-grammars")
{
    Description = "Same: TextMateSharp reads Tint's grammar files, so differences come from the engines. Bundled: TextMateSharp uses the grammars of TextMateSharp.Grammars 2.0.4.",
    DefaultValueFactory = _ => ReferenceGrammars.Same,
};
var capOption = new Option<int?>("--max-files-per-language")
{
    Description = "Overrides corpus.json's maxFilesPerLanguage.",
};
var languagesOption = new Option<string[]>("--languages")
{
    Description = "Only these language ids (comma-separated or repeated).",
    AllowMultipleArgumentsPerToken = true,
};
var threadsOption = new Option<int>("--threads")
{
    Description = "Files checked in parallel.",
    DefaultValueFactory = _ => Math.Max(1, Environment.ProcessorCount - 2),
};
var timeLimitOption = new Option<int>("--time-limit-ms")
{
    Description = "Per-line time limit of both engines, 0 for none.",
    DefaultValueFactory = _ => 10_000,
};
var maxLineLengthOption = new Option<int>("--max-line-length")
{
    Description = "Skip files with a longer line (minified code).",
    DefaultValueFactory = _ => 10_000,
};

var refereeOption = new Option<bool>("--referee")
{
    Description = "Judge the first token or style mismatch of each file with vscode-textmate (needs Node and npm install in tools/Tint.CorpusCheck/referee).",
    DefaultValueFactory = _ => true,
};

var root = new RootCommand("Tokenizes a corpus of real files with Tint and with TextMateSharp 2.0.4 and reports every difference. Fetch the corpus first with tools/Tint.CorpusCheck/fetch.sh.")
{
    repoOption, corpusOption, outOption, referenceOption, capOption, languagesOption, threadsOption, timeLimitOption, maxLineLengthOption, refereeOption,
};
root.SetAction(result =>
{
    var repository = RepositoryData.Load(result.GetValue(repoOption)?.FullName);
    var corpusRoot = result.GetValue(corpusOption)?.FullName ?? Path.Combine(repository.Root, "artifacts", "corpus");
    var outDirectory = result.GetValue(outOption)?.FullName ?? Path.Combine(repository.Root, "artifacts", "corpus-check");
    var corpus = CorpusDefinition.Load(Path.Combine(repository.Root, "tools", "Tint.CorpusCheck", "corpus.json"));
    var cap = result.GetValue(capOption) ?? corpus.MaxFilesPerLanguage;
    var languages = (result.GetValue(languagesOption) ?? []).SelectMany(l => l.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToHashSet(StringComparer.Ordinal);
    var grammars = result.GetValue(referenceOption);
    var timeLimit = TimeSpan.FromMilliseconds(result.GetValue(timeLimitOption));
    var threads = result.GetValue(threadsOption);

    var tint = new TintEngine(repository);
    var reference = new TextMateSharpEngine(grammars, repository);
    var files = FileSelection.Select(corpus, corpusRoot, repository, cap, language => languages.Count == 0 || languages.Contains(language));
    Console.WriteLine($"{files.Count} files, {files.Select(f => f.Language).Distinct().Count()} languages; Tint {tint.Version} vs TextMateSharp {reference.Version}, {threads} threads");

    var stopwatch = Stopwatch.StartNew();
    var reports = new Checker(tint, reference, timeLimit, result.GetValue(maxLineLengthOption), threads).Run(files);
    stopwatch.Stop();

    var referee = new Referee(repository, tint, reference);
    if (result.GetValue(refereeOption) && referee.IsAvailable)
    {
        var mismatches = reports.SelectMany(r => r.Mismatches.Select(m => (r.ScopeName, Mismatch: m))).Where(m => m.Mismatch.Kind is nameof(Difference.Tokens) or nameof(Difference.Styles)).ToList();
        Console.WriteLine($"judging {mismatches.Count} mismatches with vscode-textmate");
        Parallel.ForEach(mismatches, new ParallelOptions { MaxDegreeOfParallelism = threads }, m =>
        {
            var (tintLine, referenceLine, error) = referee.Judge(m.ScopeName, m.Mismatch.FullPath, m.Mismatch.Line);
            m.Mismatch.TintAgreesWithVSCode = error is null ? tintLine == 0 : null;
            m.Mismatch.Verdict = error is not null ? $"referee failed: {error}"
                : tintLine == 0 && referenceLine == 0 ? "vscode-textmate agrees with both up to this line"
                : tintLine == 0 ? $"vscode-textmate agrees with Tint; TextMateSharp first differs from it at line {referenceLine}"
                : $"**Tint differs from vscode-textmate at line {tintLine}**{(referenceLine == 0 ? "; TextMateSharp agrees with it" : $"; TextMateSharp from line {referenceLine}")}";
        });
    }
    else if (result.GetValue(refereeOption))
    {
        Console.WriteLine("referee not installed (npm install in tools/Tint.CorpusCheck/referee): mismatches are not judged");
    }

    // Grammar versions: Tint's files against TextMateSharp.Grammars' embedded copies, for the scopes both have.
    var bundled = TextMateSharpEngine.BundledGrammarVersions();
    var versions = new List<(string, string?, string?)>();
    foreach (var grammar in repository.Grammars.Where(g => g.Format == "json" && bundled.ContainsKey(g.ScopeName)).OrderBy(g => g.ScopeName, StringComparer.Ordinal))
    {
        using var stream = File.OpenRead(repository.GrammarPath(grammar));
        versions.Add((grammar.ScopeName, RepositoryData.ReadVersionHeader(stream), bundled[grammar.ScopeName]));
    }

    var info = new RunInfo(tint.Version, reference.Version, grammars, repository.VSCodeCommit, corpus, files.Count, cap, threads, stopwatch.Elapsed, timeLimit);
    Directory.CreateDirectory(outDirectory);
    File.WriteAllText(Path.Combine(outDirectory, "report.md"), MarkdownReport.Write(info, reports, versions));
    File.WriteAllText(Path.Combine(outDirectory, "report.json"), JsonSerializer.Serialize(new { info.TintVersion, info.ReferenceVersion, Grammars = grammars.ToString(), info.FilesSelected, reports }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));

    var mismatched = reports.Sum(r => r.MismatchedLines + r.StyleOnlyLines + r.StoppedEarlyLines);
    var exceptions = reports.Sum(r => r.TintExceptions + r.ReferenceExceptions);
    Console.WriteLine($"{reports.Sum(r => r.Files)} files, {reports.Sum(r => r.Lines):N0} lines, {mismatched} differing lines, {exceptions} exceptions in {stopwatch.Elapsed.TotalSeconds:F0} s; report in {outDirectory}");
    return mismatched == 0 && exceptions == 0 ? 0 : 1;
});

var dumpScopeOption = new Option<string>("--scope") { Description = "The grammar scope.", Required = true };
var dumpFileArgument = new Argument<FileInfo>("file") { Description = "The file to tokenize." };
var dumpEngineOption = new Option<string>("--engine")
{
    Description = "tint, or textmatesharp (with --reference-grammars).",
    DefaultValueFactory = _ => "tint",
};
var dumpFirstOption = new Option<int>("--first") { Description = "The first line printed (1-based).", DefaultValueFactory = _ => 1 };
var dumpLastOption = new Option<int>("--last") { Description = "The last line tokenized.", DefaultValueFactory = _ => int.MaxValue };
var dump = new Command("dump", "Prints the tokens of a file, one block per line (L<n>, then start-end scopes), in the format of referee/tokenize.mjs.")
{
    repoOption, referenceOption, dumpScopeOption, dumpFileArgument, dumpEngineOption, dumpFirstOption, dumpLastOption,
};
dump.SetAction(result =>
{
    var repository = RepositoryData.Load(result.GetValue(repoOption)?.FullName);
    IEngine engine = result.GetValue(dumpEngineOption) == "tint" ? new TintEngine(repository) : new TextMateSharpEngine(result.GetValue(referenceOption), repository);
    var tokenizer = engine.CreateTokenizer(result.GetValue(dumpScopeOption)!, TimeSpan.Zero) ?? throw new InvalidOperationException("No such grammar.");
    var lines = Lines.Read(result.GetValue(dumpFileArgument)!.FullName);
    var first = result.GetValue(dumpFirstOption);
    var last = Math.Min(lines.Length, result.GetValue(dumpLastOption));
    var output = new System.Text.StringBuilder();
    for (var i = 0; i < last; i++)
    {
        var line = tokenizer.Tokenize(lines[i]);
        if (i + 1 < first)
        {
            continue;
        }

        output.Append('L').Append(i + 1).Append('\n');
        if (line.Exception is not null)
        {
            output.Append(line.Exception).Append('\n');
            break;
        }

        foreach (var token in line.Tokens)
        {
            output.Append(token.Start).Append('-').Append(token.End).Append(' ').Append(token.Scopes).Append('\n');
        }
    }

    Console.Out.Write(output.ToString());
    return 0;
});
root.Add(dump);

return await root.Parse(args).InvokeAsync();
