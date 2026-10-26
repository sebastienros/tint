using System.Collections.Concurrent;
using Tint.Internal.Rules;

namespace Tint;

/// <summary>
/// An immutable scope stack whose nodes carry the token attributes (language, token type, font style, colors)
/// resolved against the theme when the scope was pushed. Nodes cache their children, so pushing a scope that was
/// pushed before from the same node returns the existing node.
/// </summary>
public sealed class AttributedScopeStack : IEquatable<AttributedScopeStack>
{
    // Children kept in a small array before the dictionary, and the most a node caches: large nodes of real
    // grammars (a C# class body) have over a hundred distinct static children, capture-derived names many more.
    internal const int SmallChildren = 8;
    internal const int MaxCachedChildren = 256;

    private readonly ScopeContext _context;
    private readonly int _hashCode;
    private volatile AttributedScopeStack[]? _smallChildren;
    private volatile ConcurrentDictionary<string, AttributedScopeStack>? _childMap;
    private int _childMapCount;

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
        if (TryFindChild(scopeName, out var existing))
        {
            return existing;
        }

        return AddChild(CreateChild(scopeName));
    }

    private AttributedScopeStack Push(ReadOnlySpan<char> scopeName)
    {
        var small = _smallChildren;
        if (small is not null)
        {
            foreach (var item in small)
            {
                if (scopeName.SequenceEqual(item.ScopeName))
                {
                    return item;
                }
            }
        }

        var map = _childMap;
#if NET9_0_OR_GREATER
        if (map is not null && map.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(scopeName, out var found))
        {
            return found;
        }

        return AddChild(CreateChild(scopeName.ToString()));
#else
        var name = scopeName.ToString();
        if (map is not null && map.TryGetValue(name, out var found))
        {
            return found;
        }

        return AddChild(CreateChild(name));
#endif
    }

    private bool TryFindChild(string scopeName, out AttributedScopeStack child)
    {
        var small = _smallChildren;
        if (small is not null)
        {
            foreach (var item in small)
            {
                if (ReferenceEquals(item.ScopeName, scopeName) || item.ScopeName == scopeName)
                {
                    child = item;
                    return true;
                }
            }
        }

        if (_childMap is { } map && map.TryGetValue(scopeName, out child!))
        {
            return true;
        }

        child = null!;
        return false;
    }

    private AttributedScopeStack CreateChild(string scopeName)
    {
        var basic = _context.Grammar.Attributes.GetBasicScopeAttributes(scopeName);
        var style = _context.Theme.Match(scopeName, this);
        return new AttributedScopeStack(this, scopeName, MergeAttributes(TokenAttributes, basic, style), _context);
    }

    // Publishes a new child without locks: the first few in a copy-on-write array, then in a concurrent dictionary,
    // up to MaxCachedChildren. Past the cap the node is returned uncached (names derived from captured text would
    // otherwise grow the cache forever); equality is structural, so tokens and states are unaffected. When another
    // thread published the same scope first, its node wins.
    private AttributedScopeStack AddChild(AttributedScopeStack child)
    {
        while (true)
        {
            var small = _smallChildren;
            if (small is not null)
            {
                foreach (var item in small)
                {
                    if (item.ScopeName == child.ScopeName)
                    {
                        return item;
                    }
                }
            }

            if (small is not null && small.Length >= SmallChildren)
            {
                break;
            }

            AttributedScopeStack[] updated = small is null ? [child] : [.. small, child];
#pragma warning disable CS0420 // A volatile field passed by reference stays volatile for Interlocked.
            if (Interlocked.CompareExchange(ref _smallChildren, updated, small) == small)
#pragma warning restore CS0420
            {
                return child;
            }
        }

        var map = _childMap;
        if (map is null)
        {
            var created = new ConcurrentDictionary<string, AttributedScopeStack>(StringComparer.Ordinal);
#pragma warning disable CS0420 // A volatile field passed by reference stays volatile for Interlocked.
            map = Interlocked.CompareExchange(ref _childMap, created, null) ?? created;
#pragma warning restore CS0420
        }

        if (map.TryGetValue(child.ScopeName, out var existing))
        {
            return existing;
        }

        if (Interlocked.Increment(ref _childMapCount) > MaxCachedChildren - SmallChildren)
        {
            Interlocked.Decrement(ref _childMapCount);
            return child;
        }

        if (map.TryAdd(child.ScopeName, child))
        {
            return child;
        }

        Interlocked.Decrement(ref _childMapCount);
        return map.TryGetValue(child.ScopeName, out existing) ? existing : child;
    }

    // The number of cached children, for tests.
    internal int CachedChildCount => (_smallChildren?.Length ?? 0) + Volatile.Read(ref _childMapCount);

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
}
