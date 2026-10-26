namespace Tint;

/// <summary>
/// The tokenizer state at the end of a line: an immutable linked list of the rules that are open, as vscode-textmate's
/// <c>StateStackImpl</c>. Editors keep one per line and stop re-tokenizing when a line ends in a state equal to the
/// stored one. Equality is structural.
/// </summary>
public sealed class StateStack : IEquatable<StateStack>
{
    private readonly int _structuralHash;
    private readonly int _hashCode;

    // Where the state was pushed on the current line, and the anchor position then. Meaningless across lines; reset
    // to -1 when a line starts from this state (vscode-textmate mutates them the same way).
    private int _enterPos;
    private int _anchorPos;

    private StateStack()
    {
        Depth = 0;
        _enterPos = -1;
        _anchorPos = -1;
    }

    internal StateStack(
        StateStack? parent,
        int ruleId,
        int enterPos,
        int anchorPos,
        bool beginRuleCapturedEol,
        string? endRule,
        AttributedScopeStack nameScopes,
        AttributedScopeStack contentNameScopes)
    {
        Parent = parent;
        RuleId = ruleId;
        _enterPos = enterPos;
        _anchorPos = anchorPos;
        BeginRuleCapturedEol = beginRuleCapturedEol;
        EndRule = endRule;
        NameScopes = nameScopes;
        ContentNameScopes = contentNameScopes;
        Depth = parent is null ? 1 : parent.Depth + 1;
        _structuralHash = HashCode.Combine(parent?._structuralHash ?? 0, ruleId, Depth, endRule);
        _hashCode = HashCode.Combine(_structuralHash, contentNameScopes.GetHashCode());
    }

    /// <summary>The state before the first line of a document.</summary>
    public static StateStack Initial { get; } = new();

    /// <summary>The enclosing state, or null at the root.</summary>
    public StateStack? Parent { get; }

    /// <summary>The number of open rules, the root rule included; 0 for <see cref="Initial"/>.</summary>
    public int Depth { get; }

    /// <summary>Whether the <c>begin</c> match of this rule consumed the end of its line.</summary>
    public bool BeginRuleCapturedEol { get; }

    /// <summary>The <c>end</c> or <c>while</c> pattern resolved from the captures of the <c>begin</c> match, when it has back-references.</summary>
    public string? EndRule { get; }

    /// <summary>The scopes including the rule's <c>name</c>. Null for <see cref="Initial"/>.</summary>
    public AttributedScopeStack NameScopes { get; } = null!;

    /// <summary>The scopes including the rule's <c>name</c> and <c>contentName</c>. Null for <see cref="Initial"/>.</summary>
    public AttributedScopeStack ContentNameScopes { get; } = null!;

    internal int RuleId { get; }

    internal int EnterPos => _enterPos;

    internal int AnchorPos => _anchorPos;

    internal void Reset()
    {
        for (var node = this; node is not null; node = node.Parent)
        {
            node._enterPos = -1;
            node._anchorPos = -1;
        }
    }

    internal StateStack? Pop() => Parent;

    internal StateStack SafePop() => Parent ?? this;

    internal StateStack Push(int ruleId, int enterPos, int anchorPos, bool beginRuleCapturedEol, string? endRule, AttributedScopeStack nameScopes, AttributedScopeStack contentNameScopes)
        => new(this, ruleId, enterPos, anchorPos, beginRuleCapturedEol, endRule, nameScopes, contentNameScopes);

    internal StateStack WithContentNameScopes(AttributedScopeStack contentNameScopes)
    {
        if (ReferenceEquals(ContentNameScopes, contentNameScopes))
        {
            return this;
        }

        return new StateStack(Parent, RuleId, _enterPos, _anchorPos, BeginRuleCapturedEol, EndRule, NameScopes, contentNameScopes);
    }

    internal StateStack WithEndRule(string endRule)
    {
        if (EndRule == endRule)
        {
            return this;
        }

        return new StateStack(Parent, RuleId, _enterPos, _anchorPos, BeginRuleCapturedEol, endRule, NameScopes, ContentNameScopes);
    }

    // Loop guard: walks up while the enter position is the same, looking for the same rule.
    internal bool HasSameRuleAs(StateStack other)
    {
        for (var node = this; node is not null && node._enterPos == other._enterPos; node = node.Parent)
        {
            if (node.RuleId == other.RuleId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether both states have the same open rules, resolved end patterns and content scopes.</summary>
    public bool Equals(StateStack? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || _hashCode != other._hashCode || !StructuralEquals(this, other))
        {
            return false;
        }

        return AttributedScopeStack.Equals(ContentNameScopes, other.ContentNameScopes);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is StateStack other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _hashCode;

    /// <summary>The open rules with their scopes, outermost first.</summary>
    public override string ToString()
    {
        if (Depth == 0)
        {
            return "[]";
        }

        var parts = new List<string>();
        for (var node = this; node is not null; node = node.Parent)
        {
            parts.Add($"({node.RuleId}, {node.NameScopes}, {node.ContentNameScopes})");
        }

        parts.Reverse();
        return "[" + string.Join(",", parts) + "]";
    }

    private static bool StructuralEquals(StateStack? a, StateStack? b)
    {
        while (true)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            if (a is null || b is null)
            {
                return false;
            }

            if (a.Depth != b.Depth || a.RuleId != b.RuleId || a.EndRule != b.EndRule)
            {
                return false;
            }

            a = a.Parent;
            b = b.Parent;
        }
    }
}
