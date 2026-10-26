namespace Tint.Grammars;

/// <summary>The kinds of <see cref="IncludeReference"/>.</summary>
public enum IncludeReferenceKind
{
    /// <summary><c>$self</c>: the patterns of the current grammar.</summary>
    Self,

    /// <summary><c>$base</c>: the patterns of the grammar the tokenizer started from.</summary>
    Base,

    /// <summary><c>#name</c>: a rule of the current repository.</summary>
    Relative,

    /// <summary><c>source.x</c>: the patterns of another grammar.</summary>
    TopLevel,

    /// <summary><c>source.x#name</c>: a rule of another grammar's repository.</summary>
    TopLevelRepository,
}

/// <summary>A parsed <c>include</c> value, as vscode-textmate's <c>parseInclude</c> reads it.</summary>
public readonly struct IncludeReference : IEquatable<IncludeReference>
{
    private IncludeReference(IncludeReferenceKind kind, string? scopeName, string? ruleName)
    {
        Kind = kind;
        ScopeName = scopeName;
        RuleName = ruleName;
    }

    /// <summary>The <c>$self</c> reference.</summary>
    public static IncludeReference Self => new(IncludeReferenceKind.Self, null, null);

    /// <summary>The <c>$base</c> reference.</summary>
    public static IncludeReference Base => new(IncludeReferenceKind.Base, null, null);

    /// <summary>The kind of reference.</summary>
    public IncludeReferenceKind Kind { get; }

    /// <summary>The referenced grammar for <see cref="IncludeReferenceKind.TopLevel"/> and <see cref="IncludeReferenceKind.TopLevelRepository"/>.</summary>
    public string? ScopeName { get; }

    /// <summary>The referenced repository rule for <see cref="IncludeReferenceKind.Relative"/> and <see cref="IncludeReferenceKind.TopLevelRepository"/>.</summary>
    public string? RuleName { get; }

    /// <summary>A <c>#name</c> reference.</summary>
    public static IncludeReference Relative(string ruleName) => new(IncludeReferenceKind.Relative, null, ruleName);

    /// <summary>A <c>source.x</c> reference.</summary>
    public static IncludeReference TopLevel(string scopeName) => new(IncludeReferenceKind.TopLevel, scopeName, null);

    /// <summary>A <c>source.x#name</c> reference.</summary>
    public static IncludeReference TopLevelRepository(string scopeName, string ruleName) => new(IncludeReferenceKind.TopLevelRepository, scopeName, ruleName);

    /// <summary>Parses an include value: <c>$base</c>, <c>$self</c>, <c>#name</c>, <c>scope</c> or <c>scope#name</c>.</summary>
    public static IncludeReference Parse(string include)
    {
        ArgumentNullException.ThrowIfNull(include);
        if (include == "$base")
        {
            return Base;
        }

        if (include == "$self")
        {
            return Self;
        }

        var sharp = include.IndexOf('#');
        if (sharp == -1)
        {
            return TopLevel(include);
        }

        if (sharp == 0)
        {
            return Relative(include.Substring(1));
        }

        return TopLevelRepository(include.Substring(0, sharp), include.Substring(sharp + 1));
    }

    /// <inheritdoc/>
    public bool Equals(IncludeReference other) => Kind == other.Kind && ScopeName == other.ScopeName && RuleName == other.RuleName;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is IncludeReference other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Kind, ScopeName, RuleName);

    /// <summary>The include value in grammar syntax.</summary>
    public override string ToString() => Kind switch
    {
        IncludeReferenceKind.Self => "$self",
        IncludeReferenceKind.Base => "$base",
        IncludeReferenceKind.Relative => "#" + RuleName,
        IncludeReferenceKind.TopLevel => ScopeName!,
        _ => ScopeName + "#" + RuleName,
    };

    /// <summary>Equality.</summary>
    public static bool operator ==(IncludeReference left, IncludeReference right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(IncludeReference left, IncludeReference right) => !left.Equals(right);
}
