using System.Runtime.CompilerServices;
using Tint.Internal;

namespace Tint;

/// <summary>
/// The tokenizer state at the end of a line: an immutable linked list of the rules that are open, as vscode-textmate's
/// <c>StateStackImpl</c>. Editors keep one per line and stop re-tokenizing when a line ends in a state equal to the
/// stored one. Equality is structural. Beyond vscode-textmate's <c>StateStack</c> interface, which only exposes the
/// depth, <see cref="Scopes"/> tells an editor which scopes continue onto the next line; the rules stay the
/// tokenizer's business. A state is never modified once <see cref="Tokenizer.TokenizeLine"/> has returned it, so it
/// can be kept and shared across threads.
/// </summary>
public sealed class StateStack : IEquatable<StateStack>
{
    // Computed on first use: most states are pushed and popped within a line and never hashed.
    private int _hashCode;

    // Where the state was pushed on the current line, and the anchor position then. Meaningless across lines:
    // vscode-textmate resets them to -1 when a line starts from a state; Tint resets them before handing the state
    // back, so every state a caller holds already has -1 and starting a line writes nothing.
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
    }

    /// <summary>The state before the first line of a document.</summary>
    public static StateStack Initial { get; } = new();

    // The enclosing state, or null at the root.
    internal StateStack? Parent { get; }

    /// <summary>The number of open rules, the root rule included; 0 for <see cref="Initial"/>.</summary>
    public int Depth { get; }

    /// <summary>
    /// The scopes open in this state: the innermost open rule's content scope, whose parents are the enclosing rules'
    /// scopes up to the grammar's root scope. Null for <see cref="Initial"/>.
    /// </summary>
    /// <remarks>
    /// Tokens of the next line inherit these scopes, unless a <c>while</c> condition closes its rule at the start of
    /// that line. A scope that a line's first token shares with the previous line's end state therefore continues
    /// from that line, while an equal scope that is missing here was closed and opened again: two adjacent line
    /// comments, for instance, have equal scope stacks but are two scopes. Scope stacks share nodes, so compare them
    /// with <see cref="AttributedScopeStack.Equals(AttributedScopeStack)"/>.
    /// </remarks>
    public AttributedScopeStack? Scopes => Depth == 0 ? null : ContentNameScopes;

    // Whether the begin match of this rule consumed the end of its line.
    internal bool BeginRuleCapturedEol { get; }

    // The end or while pattern resolved from the captures of the begin match, when it has back-references.
    internal string? EndRule { get; }

    // The scopes including the rule's name. Null for Initial.
    internal AttributedScopeStack NameScopes { get; } = null!;

    // The scopes including the rule's name and contentName. Null for Initial.
    internal AttributedScopeStack ContentNameScopes { get; } = null!;

    internal int RuleId { get; }

    internal int EnterPos => _enterPos;

    internal int AnchorPos => _anchorPos;

    // Clears the line positions before the state leaves the tokenizer. Nodes from earlier lines already hold -1 and
    // are left untouched, so a state that callers share is never written to.
    [MethodImpl(Jit.FullOpts)]
    internal void ResetPositions()
    {
        for (var node = this; node is not null; node = node.Parent)
        {
            if (node._enterPos != -1 || node._anchorPos != -1)
            {
                node._enterPos = -1;
                node._anchorPos = -1;
            }
        }
    }

    internal StateStack? Pop() => Parent;

    internal StateStack SafePop() => Parent ?? this;

    [MethodImpl(Jit.FullOpts)]
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
    [MethodImpl(Jit.FullOpts)]
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

        if (other is null || (_hashCode != 0 && other._hashCode != 0 && _hashCode != other._hashCode) || !StructuralEquals(this, other))
        {
            return false;
        }

        return AttributedScopeStack.Equals(ContentNameScopes, other.ContentNameScopes);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is StateStack other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hashCode = _hashCode;
        if (hashCode == 0)
        {
            var hash = new HashCode();
            for (var node = this; node is not null && node.Depth > 0; node = node.Parent)
            {
                hash.Add(node.RuleId);
                hash.Add(node.EndRule);
            }

            hash.Add(Depth);
            hash.Add(ContentNameScopes?.GetHashCode() ?? 0);
            hashCode = hash.ToHashCode();
            _hashCode = hashCode == 0 ? 1 : hashCode;
        }

        return _hashCode;
    }

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

    [MethodImpl(Jit.FullOpts)]
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
