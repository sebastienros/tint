using Tint.Internal.Rules;

namespace Tint;

/// <summary>
/// An immutable scope stack whose nodes carry the token attributes (language, token type, font style, colors)
/// resolved against the theme when the scope was pushed. Nodes cache their children, so pushing a scope that was
/// pushed before from the same node returns the existing node.
/// </summary>
public sealed class AttributedScopeStack : IEquatable<AttributedScopeStack>
{
    private const int MapThreshold = 8;

    private readonly ScopeContext _context;
    private readonly int _hashCode;
    private volatile ChildSet? _children;

    // Per-node caches filled by the tokenizer: the injections whose selector matches, and the binary metadata after
    // token type and bracket overrides.
    private ulong _injectionMask;
    private volatile bool _hasInjectionMask;
    private uint _encodedMetadata;
    private volatile bool _hasEncodedMetadata;

    private AttributedScopeStack(AttributedScopeStack? parent, string scopeName, uint tokenAttributes, ScopeContext context)
    {
        Parent = parent;
        ScopeName = scopeName;
        TokenAttributes = tokenAttributes;
        Depth = parent is null ? 1 : parent.Depth + 1;
        _context = context;
        _hashCode = HashCode.Combine(parent?._hashCode ?? 0, scopeName, tokenAttributes);
    }

    /// <summary>The enclosing scopes, or null at the root.</summary>
    public AttributedScopeStack? Parent { get; }

    /// <summary>The innermost scope name.</summary>
    public string ScopeName { get; }

    /// <summary>The token attributes, as <see cref="EncodedTokenAttributes"/> reads them.</summary>
    public uint TokenAttributes { get; }

    /// <summary>The number of scopes, root included.</summary>
    public int Depth { get; }

    internal ScopeContext Context => _context;

    /// <summary>The scope names, root first. Allocates a new array.</summary>
    public string[] GetScopeNames()
    {
        var names = new string[Depth];
        CopyScopeNames(names);
        return names;
    }

    internal void CopyScopeNames(Span<string> destination)
    {
        var node = this;
        for (var i = Depth - 1; i >= 0; i--)
        {
            destination[i] = node!.ScopeName;
            node = node.Parent;
        }
    }

    /// <summary>The scope names separated by spaces, root first.</summary>
    public override string ToString() => string.Join(' ', GetScopeNames());

    /// <summary>Whether both stacks hold the same scope names with the same attributes.</summary>
    public bool Equals(AttributedScopeStack? other) => Equals(this, other);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is AttributedScopeStack other && Equals(this, other);

    /// <inheritdoc/>
    public override int GetHashCode() => _hashCode;

    internal static bool Equals(AttributedScopeStack? a, AttributedScopeStack? b)
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

            if (a._hashCode != b._hashCode || a.ScopeName != b.ScopeName || a.TokenAttributes != b.TokenAttributes)
            {
                return false;
            }

            a = a.Parent;
            b = b.Parent;
        }
    }

    internal static AttributedScopeStack CreateRoot(string scopeName, uint defaultAttributes, ScopeContext context)
    {
        // createRootAndLookUpScopeName.
        var basic = context.Grammar.Attributes.GetBasicScopeAttributes(scopeName);
        var style = context.Theme.Match(scopeName, null);
        return new AttributedScopeStack(null, scopeName, MergeAttributes(defaultAttributes, basic, style), context);
    }

    // pushAttributed: a space-separated scope path pushes each segment (empty segments included, as split(/ /g)).
    internal AttributedScopeStack PushAttributed(string? scopePath)
    {
        if (scopePath is null)
        {
            return this;
        }

        if (scopePath.IndexOf(' ') < 0)
        {
            return Push(scopePath);
        }

        return PushAttributed(scopePath.AsSpan());
    }

    internal AttributedScopeStack PushAttributed(ReadOnlySpan<char> scopePath)
    {
        var result = this;
        while (true)
        {
            var space = scopePath.IndexOf(' ');
            if (space < 0)
            {
                return result.Push(scopePath);
            }

            result = result.Push(scopePath.Slice(0, space));
            scopePath = scopePath.Slice(space + 1);
        }
    }

    private AttributedScopeStack Push(string scopeName)
    {
        var children = _children;
        if (children is not null && children.TryFind(scopeName, out var existing))
        {
            return existing;
        }

        return Add(CreateChild(scopeName));
    }

    private AttributedScopeStack Push(ReadOnlySpan<char> scopeName)
    {
        var children = _children;
        if (children is not null && children.TryFind(scopeName, out var existing))
        {
            return existing;
        }

        return Add(CreateChild(scopeName.ToString()));
    }

    private AttributedScopeStack CreateChild(string scopeName)
    {
        var basic = _context.Grammar.Attributes.GetBasicScopeAttributes(scopeName);
        var style = _context.Theme.Match(scopeName, this);
        return new AttributedScopeStack(this, scopeName, MergeAttributes(TokenAttributes, basic, style), _context);
    }

    // Publishes a new child without locks; when another thread published the same scope first, its node wins.
    private AttributedScopeStack Add(AttributedScopeStack child)
    {
        while (true)
        {
            var current = _children;
            if (current is not null && current.TryFind(child.ScopeName, out var existing))
            {
                return existing;
            }

            var updated = ChildSet.With(current, child);
#pragma warning disable CS0420 // A volatile field passed by reference stays volatile for Interlocked.
            if (Interlocked.CompareExchange(ref _children, updated, current) == current)
#pragma warning restore CS0420
            {
                return child;
            }
        }
    }

    private static uint MergeAttributes(uint existing, BasicScopeAttributes basic, Themes.StyleAttributes? style)
    {
        var fontStyle = FontStyle.NotSet;
        var foreground = 0;
        var background = 0;
        if (style is { } s)
        {
            fontStyle = s.FontStyle;
            foreground = s.ForegroundId;
            background = s.BackgroundId;
        }

        return EncodedTokenAttributes.Set(existing, basic.LanguageId, basic.TokenType, null, fontStyle, foreground, background);
    }

    internal bool TryGetInjectionMask(out ulong mask)
    {
        if (_hasInjectionMask)
        {
            mask = _injectionMask;
            return true;
        }

        mask = 0;
        return false;
    }

    internal void SetInjectionMask(ulong mask)
    {
        _injectionMask = mask;
        _hasInjectionMask = true;
    }

    internal bool TryGetEncodedMetadata(out uint metadata)
    {
        if (_hasEncodedMetadata)
        {
            metadata = _encodedMetadata;
            return true;
        }

        metadata = 0;
        return false;
    }

    internal void SetEncodedMetadata(uint metadata)
    {
        _encodedMetadata = metadata;
        _hasEncodedMetadata = true;
    }

    // An immutable snapshot of a node's children: an array, plus a dictionary once there are many.
    private sealed class ChildSet
    {
        private readonly AttributedScopeStack[] _items;
        private readonly Dictionary<string, AttributedScopeStack>? _map;

        private ChildSet(AttributedScopeStack[] items)
        {
            _items = items;
            if (items.Length > MapThreshold)
            {
                _map = new Dictionary<string, AttributedScopeStack>(items.Length, StringComparer.Ordinal);
                foreach (var item in items)
                {
                    _map[item.ScopeName] = item;
                }
            }
        }

        public static ChildSet With(ChildSet? current, AttributedScopeStack child)
        {
            if (current is null)
            {
                return new ChildSet([child]);
            }

            var items = new AttributedScopeStack[current._items.Length + 1];
            current._items.CopyTo(items, 0);
            items[^1] = child;
            return new ChildSet(items);
        }

        public bool TryFind(string scopeName, out AttributedScopeStack child)
        {
            if (_map is not null)
            {
                return _map.TryGetValue(scopeName, out child!);
            }

            foreach (var item in _items)
            {
                if (ReferenceEquals(item.ScopeName, scopeName) || item.ScopeName == scopeName)
                {
                    child = item;
                    return true;
                }
            }

            child = null!;
            return false;
        }

        public bool TryFind(ReadOnlySpan<char> scopeName, out AttributedScopeStack child)
        {
#if NET9_0_OR_GREATER
            if (_map is not null)
            {
                return _map.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(scopeName, out child!);
            }
#endif
            foreach (var item in _items)
            {
                if (scopeName.SequenceEqual(item.ScopeName))
                {
                    child = item;
                    return true;
                }
            }

            child = null!;
            return false;
        }
    }
}
