using System.Collections.Frozen;
using System.Globalization;
using Tint.Grammars;

namespace Tint.Loading;

// The document-tree reading shared with Tint.Generator, which compiles this file into the generator (TINT_GENERATOR)
// so that generated and loaded grammars come from one interpretation of the grammar file.
public static partial class GrammarLoader
{
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

    /// <summary>The scope names of the other grammars a grammar includes, in first-reference order.</summary>
    internal static List<string> CollectDependencies(RawGrammar grammar)
    {
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

        return dependencies;
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
