using System.Collections.Frozen;
using System.Globalization;
using Tint.Grammars;

namespace Tint.Loading;

/// <summary>The file formats of grammars and themes.</summary>
public enum DocumentFormat
{
    /// <summary>JSON when the content starts with <c>{</c>, <c>[</c> or a comment, plist otherwise.</summary>
    Auto,

    /// <summary>JSON, with comments and trailing commas allowed.</summary>
    Json,

    /// <summary>An XML property list (<c>.tmLanguage</c>, <c>.plist</c>, <c>.tmTheme</c>).</summary>
    Plist,
}

/// <summary>Reads TextMate grammars from JSON (<c>.tmLanguage.json</c>) and plist (<c>.tmLanguage</c>) files.</summary>
public static class GrammarLoader
{
    /// <summary>Loads a grammar file: JSON when the name ends with <c>.json</c>, plist otherwise, as vscode-textmate decides.</summary>
    /// <param name="path">The grammar file.</param>
    /// <param name="info">Metadata for the grammar, or null for none.</param>
    public static LoadedGrammar Load(string path, GrammarInfo? info = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        var format = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? DocumentFormat.Json : DocumentFormat.Plist;
        return Load(File.ReadAllBytes(path), format, info);
    }

    /// <summary>Loads a grammar from a stream.</summary>
    /// <param name="stream">The grammar content.</param>
    /// <param name="format">The format of the content.</param>
    /// <param name="info">Metadata for the grammar, or null for none.</param>
    public static LoadedGrammar Load(Stream stream, DocumentFormat format = DocumentFormat.Auto, GrammarInfo? info = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Load(buffer.GetBuffer().AsMemory(0, (int)buffer.Length), format, info);
    }

    /// <summary>Loads a grammar from UTF-8 content.</summary>
    /// <param name="utf8">The grammar content.</param>
    /// <param name="format">The format of the content.</param>
    /// <param name="info">Metadata for the grammar, or null for none.</param>
    public static LoadedGrammar Load(ReadOnlySpan<byte> utf8, DocumentFormat format = DocumentFormat.Auto, GrammarInfo? info = null)
        => Load(utf8.ToArray().AsMemory(), format, info);

    private static LoadedGrammar Load(ReadOnlyMemory<byte> utf8, DocumentFormat format, GrammarInfo? info)
    {
        if (format == DocumentFormat.Auto)
        {
            format = DocumentTree.LooksLikeJson(utf8.Span) ? DocumentFormat.Json : DocumentFormat.Plist;
        }

        var tree = format == DocumentFormat.Json ? DocumentTree.ParseJson(utf8) : DocumentTree.ParsePlist(utf8);
        if (tree is not TreeMap root)
        {
            throw new FormatException("A grammar must be an object.");
        }

        var grammar = ReadGrammar(root);
        return new LoadedGrammar(grammar, WithDependencies(info ?? GrammarInfo.Empty, grammar));
    }

    /// <summary>Reads a grammar from its document tree.</summary>
    internal static RawGrammar ReadGrammar(TreeMap root)
    {
        var scopeName = root.Get("scopeName") as string ?? throw new FormatException("A grammar must have a scopeName.");
        var patterns = ReadPatterns(root.Get("patterns")) ?? [];
        var repository = ReadRepository(root.Get("repository"));
        List<KeyValuePair<string, RawRule>>? injections = null;
        if (root.Get("injections") is TreeMap injectionMap)
        {
            injections = [];
            foreach (var entry in injectionMap.Entries)
            {
                if (entry.Value is TreeMap rule)
                {
                    injections.Add(new KeyValuePair<string, RawRule>(entry.Key, ReadRule(rule)));
                }
            }
        }

        return new RawGrammar(
            scopeName,
            patterns,
            repository,
            injections,
            NonEmptyString(root.Get("injectionSelector")),
            NonEmptyString(root.Get("name")));
    }

    // The fields as vscode-textmate's RuleFactory tests them: `if (desc.match)` treats an empty match as absent,
    // `typeof desc.begin === 'undefined'` keeps an empty begin, names go through `name || null`.
    private static RawRule ReadRule(TreeMap map)
    {
        var include = NonEmptyString(map.Get("include"));
        var begin = map.Get("begin") as string;
        return new RawRule
        {
            Include = include is null ? null : IncludeReference.Parse(include),
            Name = NonEmptyString(map.Get("name")),
            ContentName = NonEmptyString(map.Get("contentName")),
            Match = Regex(NonEmptyString(map.Get("match"))),
            Begin = Regex(begin),
            End = Regex(NonEmptyString(map.Get("end"))),
            While = Regex(NonEmptyString(map.Get("while"))),
            Captures = ReadCaptures(map.Get("captures")),
            BeginCaptures = ReadCaptures(map.Get("beginCaptures")),
            EndCaptures = ReadCaptures(map.Get("endCaptures")),
            WhileCaptures = ReadCaptures(map.Get("whileCaptures")),
            Patterns = ReadPatterns(map.Get("patterns")),
            Repository = ReadRepository(map.Get("repository")),
            ApplyEndPatternLast = IsTruthy(map.Get("applyEndPatternLast")),
        };
    }

    private static RegexSource? Regex(string? pattern) => pattern is null ? null : new RegexSource(pattern);

    private static RawRule[]? ReadPatterns(object? value)
    {
        if (value is not List<object?> list)
        {
            return null;
        }

        var result = new List<RawRule>(list.Count);
        foreach (var item in list)
        {
            if (item is TreeMap rule)
            {
                result.Add(ReadRule(rule));
            }
        }

        return [.. result];
    }

    private static FrozenDictionary<string, RawRule>? ReadRepository(object? value)
    {
        if (value is not TreeMap map)
        {
            return null;
        }

        var rules = new Dictionary<string, RawRule>(map.Entries.Count, StringComparer.Ordinal);
        foreach (var entry in map.Entries)
        {
            if (entry.Value is TreeMap rule)
            {
                rules[entry.Key] = ReadRule(rule);
            }
        }

        return rules.ToFrozenDictionary(StringComparer.Ordinal);
    }

    // _compileCaptures: a dense array up to the largest numeric key (parseInt semantics); other keys are ignored.
    private static RawRule?[]? ReadCaptures(object? value)
    {
        if (value is not TreeMap map)
        {
            return null;
        }

        var max = -1;
        foreach (var entry in map.Entries)
        {
            if (TryParseCaptureId(entry.Key, out var id) && id > max)
            {
                max = id;
            }
        }

        var captures = new RawRule?[max + 1];
        foreach (var entry in map.Entries)
        {
            if (TryParseCaptureId(entry.Key, out var id))
            {
                captures[id] = entry.Value is TreeMap rule ? ReadRule(rule) : new RawRule();
            }
        }

        return captures;
    }

    // parseInt(key, 10) yielding a non-negative array index.
    private static bool TryParseCaptureId(string key, out int id)
    {
        var span = key.AsSpan().TrimStart();
        if (span.Length > 0 && span[0] == '+')
        {
            span = span.Slice(1);
        }

        var digits = 0;
        while (digits < span.Length && char.IsAsciiDigit(span[digits]))
        {
            digits++;
        }

        return int.TryParse(span.Slice(0, digits), NumberStyles.None, CultureInfo.InvariantCulture, out id) && id < 100_000;
    }

    private static bool IsTruthy(object? value) => value switch
    {
        bool b => b,
        double d => d != 0 && !double.IsNaN(d),
        string s => s.Length > 0,
        null => false,
        _ => true,
    };

    private static string? NonEmptyString(object? value) => value is string { Length: > 0 } s ? s : null;

    private static GrammarInfo WithDependencies(GrammarInfo info, RawGrammar grammar)
    {
        if (info.Dependencies.Count > 0)
        {
            return info;
        }

        var dependencies = new List<string>();
        var visited = new HashSet<RawRule>(ReferenceEqualityComparer.Instance);
        foreach (var rule in grammar.Patterns)
        {
            CollectDependencies(rule, grammar.ScopeName, dependencies, visited);
        }

        foreach (var rule in grammar.Repository.Values)
        {
            CollectDependencies(rule, grammar.ScopeName, dependencies, visited);
        }

        foreach (var injection in grammar.Injections)
        {
            CollectDependencies(injection.Value, grammar.ScopeName, dependencies, visited);
        }

        if (dependencies.Count == 0)
        {
            return info;
        }

        return new GrammarInfo
        {
            LanguageId = info.LanguageId,
            Aliases = info.Aliases,
            FileExtensions = info.FileExtensions,
            EmbeddedLanguages = info.EmbeddedLanguages,
            InjectTo = info.InjectTo,
            InjectionSelector = info.InjectionSelector,
            TokenTypes = info.TokenTypes,
            BalancedBracketScopes = info.BalancedBracketScopes,
            UnbalancedBracketScopes = info.UnbalancedBracketScopes,
            Dependencies = dependencies,
        };
    }

    private static void CollectDependencies(RawRule? rule, string self, List<string> dependencies, HashSet<RawRule> visited)
    {
        if (rule is null || !visited.Add(rule))
        {
            return;
        }

        if (rule.Include is { ScopeName: { } scope } && scope != self && !dependencies.Contains(scope))
        {
            dependencies.Add(scope);
        }

        CollectAll(rule.Patterns, self, dependencies, visited);
        CollectAll(rule.Captures, self, dependencies, visited);
        CollectAll(rule.BeginCaptures, self, dependencies, visited);
        CollectAll(rule.EndCaptures, self, dependencies, visited);
        CollectAll(rule.WhileCaptures, self, dependencies, visited);
        if (rule.Repository is { } repository)
        {
            foreach (var nested in repository.Values)
            {
                CollectDependencies(nested, self, dependencies, visited);
            }
        }
    }

    private static void CollectAll(IReadOnlyList<RawRule?>? rules, string self, List<string> dependencies, HashSet<RawRule> visited)
    {
        if (rules is not null)
        {
            foreach (var rule in rules)
            {
                CollectDependencies(rule, self, dependencies, visited);
            }
        }
    }
}
