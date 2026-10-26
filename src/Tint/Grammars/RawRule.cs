using System.Collections.Frozen;

namespace Tint.Grammars;

/// <summary>
/// One rule of a TextMate grammar, as written in the grammar file. Values that vscode-textmate treats as absent
/// (empty names, an empty <c>match</c> or <c>while</c>) are null.
/// </summary>
public sealed class RawRule
{
    /// <summary>The <c>include</c> reference, which takes precedence over the rest of the rule inside a <c>patterns</c> list.</summary>
    public IncludeReference? Include { get; init; }

    /// <summary>The scope name, which may refer to captures (<c>$1</c>, <c>${1:/downcase}</c>).</summary>
    public string? Name { get; init; }

    /// <summary>The scope name of the text between <c>begin</c> and <c>end</c>.</summary>
    public string? ContentName { get; init; }

    /// <summary>The pattern of a match rule.</summary>
    public RegexSource? Match { get; init; }

    /// <summary>The pattern that opens a begin/end or begin/while rule.</summary>
    public RegexSource? Begin { get; init; }

    /// <summary>The pattern that closes a begin/end rule.</summary>
    public RegexSource? End { get; init; }

    /// <summary>The pattern each following line must match for a begin/while rule to continue.</summary>
    public RegexSource? While { get; init; }

    /// <summary>Capture rules by group number for <c>match</c>, and the default for the other patterns.</summary>
    public IReadOnlyList<RawRule?>? Captures { get; init; }

    /// <summary>Capture rules of the <c>begin</c> pattern.</summary>
    public IReadOnlyList<RawRule?>? BeginCaptures { get; init; }

    /// <summary>Capture rules of the <c>end</c> pattern.</summary>
    public IReadOnlyList<RawRule?>? EndCaptures { get; init; }

    /// <summary>Capture rules of the <c>while</c> pattern.</summary>
    public IReadOnlyList<RawRule?>? WhileCaptures { get; init; }

    /// <summary>The nested rules.</summary>
    public IReadOnlyList<RawRule>? Patterns { get; init; }

    /// <summary>Rules local to this rule, shadowing the enclosing repository.</summary>
    public FrozenDictionary<string, RawRule>? Repository { get; init; }

    /// <summary>Whether the <c>end</c> pattern loses ties against the nested patterns instead of winning them.</summary>
    public bool ApplyEndPatternLast { get; init; }
}
