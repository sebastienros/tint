using System.Text.Json;
using Tint.Differential;

namespace Tint.CorpusCheck;

/// <summary>A repository of <c>corpus.json</c>.</summary>
public sealed record CorpusRepository(string Name, string Url, string? Commit, string? License, string[] Paths);

/// <summary><c>corpus.json</c>: the repositories and the per-language cap.</summary>
public sealed record CorpusDefinition(int MaxFilesPerLanguage, CorpusRepository[] Repositories)
{
    private static readonly JsonSerializerOptions s_options = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Reads a corpus definition.</summary>
    public static CorpusDefinition Load(string path)
        => JsonSerializer.Deserialize<CorpusDefinition>(File.ReadAllText(path), s_options) ?? throw new InvalidOperationException($"{path} is empty.");
}

/// <summary>A file to check: its repository, path and the grammar its extension maps to.</summary>
public sealed record CorpusFile(string Repository, string RelativePath, string FullPath, long Size, string Language, string ScopeName);

/// <summary>Picks the corpus files and the grammar of each.</summary>
public static class FileSelection
{
    /// <summary>
    /// File extension to (language, scope) from the bundled grammars' package.json metadata; the first grammar in
    /// manifest order wins an extension two grammars declare.
    /// </summary>
    public static Dictionary<string, (string Language, string Scope)> ExtensionMap(RepositoryData repository)
    {
        var map = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (var grammar in repository.Grammars)
        {
            if (grammar.Language is null)
            {
                continue;
            }

            foreach (var extension in grammar.Extensions)
            {
                map.TryAdd(extension, (grammar.Language, grammar.ScopeName));
            }
        }

        return map;
    }

    /// <summary>
    /// The files of every fetched repository whose extension maps to a grammar, at most <paramref name="cap"/> per
    /// language: repositories take turns, each in path order, so every repository contributes to a language it has.
    /// </summary>
    public static List<CorpusFile> Select(CorpusDefinition corpus, string corpusRoot, RepositoryData repository, int cap, Func<string, bool> includeLanguage)
    {
        var map = ExtensionMap(repository);
        var perRepository = new List<Dictionary<string, Queue<CorpusFile>>>();
        foreach (var repo in corpus.Repositories)
        {
            var root = Path.Combine(corpusRoot, repo.Name);
            var byLanguage = new Dictionary<string, Queue<CorpusFile>>(StringComparer.Ordinal);
            perRepository.Add(byLanguage);
            if (!Directory.Exists(root))
            {
                Console.Error.WriteLine($"warning: {repo.Name} is not fetched (run tools/Tint.CorpusCheck/fetch.sh)");
                continue;
            }

            var files = Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
                .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
                .Where(relative => !relative.StartsWith(".git/", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal);
            foreach (var relative in files)
            {
                var extension = Path.GetExtension(relative);
                if (extension.Length == 0 || !map.TryGetValue(extension, out var grammar) || !includeLanguage(grammar.Language))
                {
                    continue;
                }

                var full = Path.Combine(root, relative);
                if (!byLanguage.TryGetValue(grammar.Language, out var queue))
                {
                    byLanguage[grammar.Language] = queue = new Queue<CorpusFile>();
                }

                queue.Enqueue(new CorpusFile(repo.Name, relative, full, new FileInfo(full).Length, grammar.Language, grammar.Scope));
            }
        }

        var selected = new List<CorpusFile>();
        foreach (var language in perRepository.SelectMany(r => r.Keys).Distinct().Order(StringComparer.Ordinal))
        {
            var count = 0;
            var progress = true;
            while (count < cap && progress)
            {
                progress = false;
                foreach (var byLanguage in perRepository)
                {
                    if (count < cap && byLanguage.TryGetValue(language, out var queue) && queue.TryDequeue(out var file))
                    {
                        selected.Add(file);
                        count++;
                        progress = true;
                    }
                }
            }
        }

        return selected;
    }
}
