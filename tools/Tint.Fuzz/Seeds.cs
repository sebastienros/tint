using Tint.Differential;

namespace Tint.Fuzz;

/// <summary>Loads the seed documents of each grammar.</summary>
public static class Seeds
{
    private const int MaxCorpusFilesPerGrammar = 40;
    private const long MaxFileBytes = 256 * 1024;

    /// <summary>
    /// For each grammar: its sample in <c>test/Tint.Tests/Fixtures/samples</c> (by language id) and up to 40 corpus
    /// files with one of its extensions; a grammar with neither (injections, included-only grammars) draws from every
    /// sample.
    /// </summary>
    public static List<SeedLines> Load(RepositoryData repository, string corpus, Func<ManifestGrammar, bool> include)
    {
        var samplesDirectory = Path.Combine(repository.Root, "test", "Tint.Tests", "Fixtures", "samples");
        var allSamples = Directory.EnumerateFiles(samplesDirectory, "*.sample").Order(StringComparer.Ordinal).Select(Lines.Read).ToList();
        var corpusFiles = Directory.Exists(corpus)
            ? Directory.EnumerateFiles(corpus, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .ToList()
            : [];

        var result = new List<SeedLines>();
        foreach (var grammar in repository.Grammars.Where(include))
        {
            var documents = new List<string[]>();
            if (grammar.Language is { } language && File.Exists(Path.Combine(samplesDirectory, language + ".sample")))
            {
                documents.Add(Lines.Read(Path.Combine(samplesDirectory, language + ".sample")));
            }

            if (grammar.Extensions.Count > 0)
            {
                documents.AddRange(corpusFiles
                    .Where(p => grammar.Extensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase) && new FileInfo(p).Length <= MaxFileBytes)
                    .Take(MaxCorpusFilesPerGrammar)
                    .Select(Lines.Read)
                    .Where(lines => lines.Length > 0));
            }

            if (documents.Count == 0)
            {
                documents.AddRange(allSamples);
            }

            result.Add(new SeedLines(grammar.ScopeName, documents));
        }

        return result;
    }
}
