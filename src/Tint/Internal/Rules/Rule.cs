using Tint.Grammars;
using Tint.Internal.Regex;

namespace Tint.Internal.Rules;

internal enum RuleKind
{
    Capture,
    Match,
    IncludeOnly,
    BeginEnd,
    BeginWhile,
}

// rule.ts. Rule ids are 1-based; EndRuleId and WhileRuleId mark the end and while patterns in a scanner.
internal abstract class Rule
{
    public const int EndRuleId = -1;
    public const int WhileRuleId = -2;

    protected Rule(int id, string? name, string? contentName)
    {
        Id = id;
        Name = name;
        NameIsCapturing = CaptureNames.HasCaptures(name);
        ContentName = contentName;
        ContentNameIsCapturing = CaptureNames.HasCaptures(contentName);
    }

    public abstract RuleKind Kind { get; }

    public int Id { get; }

    // The scope name of the grammar that declares the rule, for AttributedScopeStack.GrammarScopeName.
    public required string GrammarScopeName { get; init; }

    public string? Name { get; }

    public bool NameIsCapturing { get; }

    public string? ContentName { get; }

    public bool ContentNameIsCapturing { get; }

    // The begin/match patterns this rule contributes to the scanner of a rule that includes it.
    public virtual void CollectPatterns(RuleTable rules, List<PatternEntry> output) => throw new InvalidOperationException("Not supported.");
}

internal readonly record struct PatternEntry(RegexSource Source, int RuleId);

internal sealed class CaptureRule : Rule
{
    public CaptureRule(int id, string? name, string? contentName, int retokenizeCapturedWithRuleId)
        : base(id, name, contentName)
    {
        RetokenizeCapturedWithRuleId = retokenizeCapturedWithRuleId;
    }

    public override RuleKind Kind => RuleKind.Capture;

    public int RetokenizeCapturedWithRuleId { get; }
}

// A rule whose scanner searches nested patterns. The scanner patterns are collected on first use and published
// without locks; every thread computes the same list.
internal abstract class RuleWithPatterns : Rule
{
    private RulePatterns? _patterns;

    protected RuleWithPatterns(int id, string? name, string? contentName, int[] patterns, bool hasMissingPatterns)
        : base(id, name, contentName)
    {
        Patterns = patterns;
        HasMissingPatterns = hasMissingPatterns;
    }

    public int[] Patterns { get; }

    public bool HasMissingPatterns { get; }

    public RulePatterns GetScannerPatterns(RuleTable rules)
    {
        var patterns = Volatile.Read(ref _patterns);
        if (patterns is not null)
        {
            return patterns;
        }

        patterns = BuildScannerPatterns(rules);
        return Interlocked.CompareExchange(ref _patterns, patterns, null) ?? patterns;
    }

    protected virtual RulePatterns BuildScannerPatterns(RuleTable rules)
    {
        var entries = new List<PatternEntry>();
        CollectNested(rules, entries);
        return new RulePatterns([.. entries], dynamicIndex: -1, dynamicSource: null);
    }

    protected void CollectNested(RuleTable rules, List<PatternEntry> output)
    {
        foreach (var id in Patterns)
        {
            rules[id].CollectPatterns(rules, output);
        }
    }
}

internal sealed class MatchRule : Rule
{
    private RulePatterns? _patterns;

    public MatchRule(int id, string? name, RegexSource match, CaptureRule?[] captures)
        : base(id, name, null)
    {
        Match = match;
        Captures = captures;
    }

    public override RuleKind Kind => RuleKind.Match;

    public RegexSource Match { get; }

    public CaptureRule?[] Captures { get; }

    public override void CollectPatterns(RuleTable rules, List<PatternEntry> output) => output.Add(new PatternEntry(Match, Id));

    // Used when a match rule is itself an injection or a retokenized capture.
    public RulePatterns GetScannerPatterns()
    {
        var patterns = Volatile.Read(ref _patterns);
        if (patterns is not null)
        {
            return patterns;
        }

        patterns = new RulePatterns([new PatternEntry(Match, Id)], -1, null);
        return Interlocked.CompareExchange(ref _patterns, patterns, null) ?? patterns;
    }
}

internal sealed class IncludeOnlyRule : RuleWithPatterns
{
    public IncludeOnlyRule(int id, string? name, string? contentName, int[] patterns, bool hasMissingPatterns)
        : base(id, name, contentName, patterns, hasMissingPatterns)
    {
    }

    public override RuleKind Kind => RuleKind.IncludeOnly;

    public override void CollectPatterns(RuleTable rules, List<PatternEntry> output) => CollectNested(rules, output);
}

internal sealed class BeginEndRule : RuleWithPatterns
{
    public BeginEndRule(
        int id,
        string? name,
        string? contentName,
        RegexSource begin,
        CaptureRule?[] beginCaptures,
        RegexSource end,
        CaptureRule?[] endCaptures,
        bool applyEndPatternLast,
        int[] patterns,
        bool hasMissingPatterns)
        : base(id, name, contentName, patterns, hasMissingPatterns)
    {
        Begin = begin;
        BeginCaptures = beginCaptures;
        End = end;
        EndCaptures = endCaptures;
        ApplyEndPatternLast = applyEndPatternLast;
    }

    public override RuleKind Kind => RuleKind.BeginEnd;

    public RegexSource Begin { get; }

    public CaptureRule?[] BeginCaptures { get; }

    public RegexSource End { get; }

    public CaptureRule?[] EndCaptures { get; }

    public bool EndHasBackReferences => End.HasBackReferences;

    public bool ApplyEndPatternLast { get; }

    public override void CollectPatterns(RuleTable rules, List<PatternEntry> output) => output.Add(new PatternEntry(Begin, Id));

    // The end pattern goes first, or last with applyEndPatternLast. With back-references its text depends on the
    // state, so it is left out of the static list and searched on its own.
    protected override RulePatterns BuildScannerPatterns(RuleTable rules)
    {
        var entries = new List<PatternEntry>();
        CollectNested(rules, entries);
        if (End.HasBackReferences)
        {
            return new RulePatterns([.. entries], ApplyEndPatternLast ? entries.Count : 0, End);
        }

        if (ApplyEndPatternLast)
        {
            entries.Add(new PatternEntry(End, EndRuleId));
        }
        else
        {
            entries.Insert(0, new PatternEntry(End, EndRuleId));
        }

        return new RulePatterns([.. entries], -1, null);
    }
}

internal sealed class BeginWhileRule : RuleWithPatterns
{
    private RulePatterns? _whilePatterns;

    public BeginWhileRule(
        int id,
        string? name,
        string? contentName,
        RegexSource begin,
        CaptureRule?[] beginCaptures,
        RegexSource @while,
        CaptureRule?[] whileCaptures,
        int[] patterns,
        bool hasMissingPatterns)
        : base(id, name, contentName, patterns, hasMissingPatterns)
    {
        Begin = begin;
        BeginCaptures = beginCaptures;
        While = @while;
        WhileCaptures = whileCaptures;
    }

    public override RuleKind Kind => RuleKind.BeginWhile;

    public RegexSource Begin { get; }

    public CaptureRule?[] BeginCaptures { get; }

    public RegexSource While { get; }

    public CaptureRule?[] WhileCaptures { get; }

    public bool WhileHasBackReferences => While.HasBackReferences;

    public override void CollectPatterns(RuleTable rules, List<PatternEntry> output) => output.Add(new PatternEntry(Begin, Id));

    public RulePatterns GetWhilePatterns()
    {
        var patterns = Volatile.Read(ref _whilePatterns);
        if (patterns is not null)
        {
            return patterns;
        }

        patterns = While.HasBackReferences
            ? new RulePatterns([], 0, While, WhileRuleId)
            : new RulePatterns([new PatternEntry(While, WhileRuleId)], -1, null);
        return Interlocked.CompareExchange(ref _whilePatterns, patterns, null) ?? patterns;
    }
}

// The patterns of one scanner (vscode-textmate's RegExpSourceList): static entries in priority order and, for a
// back-referencing end or while, where its state-dependent pattern sits in that order.
internal sealed class RulePatterns
{
    public RulePatterns(PatternEntry[] entries, int dynamicIndex, RegexSource? dynamicSource, int dynamicRuleId = Rule.EndRuleId)
    {
        Entries = entries;
        DynamicIndex = dynamicIndex;
        DynamicSource = dynamicSource;
        DynamicRuleId = dynamicRuleId;
        foreach (var entry in entries)
        {
            HasAnchorA |= entry.Source.HasAnchorA;
            HasAnchorG |= entry.Source.HasAnchorG;
        }
    }

    public PatternEntry[] Entries { get; }

    // -1 without a dynamic pattern; otherwise 0 (before every static entry) or Entries.Length (after them all).
    public int DynamicIndex { get; }

    public RegexSource? DynamicSource { get; }

    public int DynamicRuleId { get; }

    public bool HasAnchorA { get; }

    public bool HasAnchorG { get; }

    public RegexAnchorVariant Variant(bool allowA, bool allowG)
        => (RegexAnchorVariant)((allowA && HasAnchorA ? 2 : 0) | (allowG && HasAnchorG ? 1 : 0));
}

// The compiled rules of one grammar, by id.
internal sealed class RuleTable
{
    private readonly List<Rule?> _rules = [null];

    public int Count => _rules.Count;

    public Rule this[int id] => _rules[id]!;

    public Rule? TryGet(int id) => _rules[id];

    public int Reserve()
    {
        _rules.Add(null);
        return _rules.Count - 1;
    }

    public void Set(int id, Rule rule) => _rules[id] = rule;
}

// $1, ${1:/downcase} and ${1:/upcase} in rule names (utils.ts RegexSource.replaceCaptures), without regexes.
internal static class CaptureNames
{
    public static bool HasCaptures(string? name)
    {
        if (name is null)
        {
            return false;
        }

        for (var i = name.IndexOf('$'); i >= 0; i = name.IndexOf('$', i + 1))
        {
            if (TryParseReference(name, i, out _, out _, out _))
            {
                return true;
            }
        }

        return false;
    }

    // Writes the name with capture references replaced into the buffer.
    public static void Replace(string name, ReadOnlySpan<char> line, ReadOnlySpan<CaptureSpan> captures, CharBuffer buffer)
    {
        buffer.Clear();
        var lastPushed = 0;
        for (var i = name.IndexOf('$'); i >= 0; i = name.IndexOf('$', i))
        {
            if (!TryParseReference(name, i, out var index, out var command, out var length))
            {
                i++;
                continue;
            }

            buffer.Append(name.AsSpan(lastPushed, i - lastPushed));
            if (index < captures.Length)
            {
                var capture = captures[index];
                var value = line.Slice(capture.Start, capture.Length);

                // Remove leading dots that would make the selector invalid.
                while (value.Length > 0 && value[0] == '.')
                {
                    value = value.Slice(1);
                }

                var start = buffer.Length;
                buffer.Append(value);
                if (command != 0)
                {
                    var written = buffer.AsWritableSpan(start);
                    for (var c = 0; c < written.Length; c++)
                    {
                        written[c] = command == 1 ? char.ToLowerInvariant(written[c]) : char.ToUpperInvariant(written[c]);
                    }
                }
            }
            else
            {
                buffer.Append(name.AsSpan(i, length));
            }

            i += length;
            lastPushed = i;
        }

        buffer.Append(name.AsSpan(lastPushed));
    }

    // At a '$': /\$(\d+)|\${(\d+):\/(downcase|upcase)}/. command: 0 none, 1 downcase, 2 upcase.
    private static bool TryParseReference(string name, int dollar, out int index, out int command, out int length)
    {
        index = 0;
        command = 0;
        length = 0;
        var pos = dollar + 1;
        if (pos < name.Length && char.IsAsciiDigit(name[pos]))
        {
            var end = pos;
            while (end < name.Length && char.IsAsciiDigit(name[end]))
            {
                end++;
            }

            index = ParseIndex(name.AsSpan(pos, end - pos));
            length = end - dollar;
            return true;
        }

        if (pos < name.Length && name[pos] == '{')
        {
            var digitsStart = pos + 1;
            var end = digitsStart;
            while (end < name.Length && char.IsAsciiDigit(name[end]))
            {
                end++;
            }

            if (end == digitsStart || end + 2 >= name.Length || name[end] != ':' || name[end + 1] != '/')
            {
                return false;
            }

            var rest = name.AsSpan(end + 2);
            if (rest.StartsWith("downcase}", StringComparison.Ordinal))
            {
                command = 1;
                length = end + 2 + "downcase}".Length - dollar;
            }
            else if (rest.StartsWith("upcase}", StringComparison.Ordinal))
            {
                command = 2;
                length = end + 2 + "upcase}".Length - dollar;
            }
            else
            {
                return false;
            }

            index = ParseIndex(name.AsSpan(digitsStart, end - digitsStart));
            return true;
        }

        return false;
    }

    private static int ParseIndex(ReadOnlySpan<char> digits)
        => int.TryParse(digits, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : int.MaxValue;
}
