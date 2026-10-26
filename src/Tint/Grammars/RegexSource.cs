#if !TINT_GENERATOR
using GregsEx;
#endif
using Tint.Internal.Regex;

namespace Tint.Grammars;

/// <summary>
/// The four textual variants of a pattern that uses <c>\A</c> or <c>\G</c>. A disallowed anchor is replaced by
/// <c>\</c> followed by U+FFFF, which never matches.
/// </summary>
public enum RegexAnchorVariant
{
    /// <summary>Neither <c>\A</c> nor <c>\G</c> may match.</summary>
    A0G0 = 0,

    /// <summary>Only <c>\G</c> may match.</summary>
    A0G1 = 1,

    /// <summary>Only <c>\A</c> may match.</summary>
    A1G0 = 2,

    /// <summary>Both anchors may match.</summary>
    A1G1 = 3,
}

#if !TINT_GENERATOR
/// <summary>Returns the compiled pattern of one anchor variant of a <see cref="RegexSource"/>.</summary>
public delegate OnigurumaPattern RegexPatternFactory(RegexAnchorVariant variant);
#endif

// The generator compiles this file without GregsEx (TINT_GENERATOR): it keeps the text, the rewrites and the anchor
// variants, and drops the compiled patterns.
/// <summary>
/// A grammar regex: its Oniguruma text after Tint's rewrites (<c>\xHH</c> becomes <c>\x{HH}</c>, <c>\z</c> becomes
/// <c>$(?!\n)(?&lt;!\n)</c>), what it contains, and its compiled patterns, built on first use.
/// </summary>
public sealed class RegexSource
{
    private readonly string?[] _variantTexts = new string?[4];
#if !TINT_GENERATOR
    private readonly CompiledRegex?[] _compiled = new CompiledRegex?[4];

    /// <summary>Creates a source from the pattern as written in the grammar, applying Tint's rewrites.</summary>
    /// <param name="pattern">The pattern text from the grammar file.</param>
    /// <param name="patternFactory">Precompiled patterns per anchor variant, or null to parse on first use.</param>
    public RegexSource(string pattern, RegexPatternFactory? patternFactory = null)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        Text = PatternRewriter.Rewrite(pattern, out var hasAnchorA, out var hasAnchorG);
        HasAnchorA = hasAnchorA;
        HasAnchorG = hasAnchorG;
        HasBackReferences = PatternRewriter.HasBackReferences(Text);
        PatternFactory = patternFactory;
    }

    private RegexSource(string text, bool hasAnchorA, bool hasAnchorG, bool hasBackReferences, RegexPatternFactory? patternFactory)
    {
        Text = text;
        HasAnchorA = hasAnchorA;
        HasAnchorG = hasAnchorG;
        HasBackReferences = hasBackReferences;
        PatternFactory = patternFactory;
    }
#else
    /// <summary>Creates a source from the pattern as written in the grammar, applying Tint's rewrites.</summary>
    public RegexSource(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        Text = PatternRewriter.Rewrite(pattern, out var hasAnchorA, out var hasAnchorG);
        HasAnchorA = hasAnchorA;
        HasAnchorG = hasAnchorG;
        HasBackReferences = PatternRewriter.HasBackReferences(Text);
    }
#endif

    /// <summary>The empty pattern, which matches immediately (the <c>end</c> of a rule that has none).</summary>
    public static RegexSource Empty { get; } = new(string.Empty);

#if !TINT_GENERATOR
    /// <summary>
    /// Creates a source from text that already went through Tint's rewrites, as generated grammars do. Code written
    /// by hand uses the constructor, which applies the rewrites.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static RegexSource FromRewritten(string text, bool hasAnchorA, bool hasAnchorG, bool hasBackReferences, RegexPatternFactory? patternFactory = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new RegexSource(text, hasAnchorA, hasAnchorG, hasBackReferences, patternFactory);
    }
#endif

    /// <summary>The rewritten Oniguruma pattern text.</summary>
    public string Text { get; }

    /// <summary>Whether the pattern uses <c>\A</c>.</summary>
    public bool HasAnchorA { get; }

    /// <summary>Whether the pattern uses <c>\G</c>.</summary>
    public bool HasAnchorG { get; }

    /// <summary>Whether the pattern uses <c>\A</c> or <c>\G</c>.</summary>
    public bool HasAnchor => HasAnchorA || HasAnchorG;

    /// <summary>Whether the pattern refers to captures of a <c>begin</c> match (<c>\1</c>), as dynamic <c>end</c> and <c>while</c> patterns do.</summary>
    public bool HasBackReferences { get; }

#if !TINT_GENERATOR
    /// <summary>Precompiled patterns per anchor variant, or null when patterns are parsed on first use.</summary>
    public RegexPatternFactory? PatternFactory { get; }
#endif

    /// <summary>The text of one anchor variant.</summary>
    public string GetText(RegexAnchorVariant variant)
    {
        var index = (int)Normalize(variant);
        if (index == 0 && !HasAnchor)
        {
            return Text;
        }

        return _variantTexts[index] ??= PatternRewriter.ResolveAnchors(Text, (index & 2) != 0, (index & 1) != 0);
    }

    /// <summary>The text of the pattern.</summary>
    public override string ToString() => Text;

    // Variants that differ only by an anchor the pattern does not contain share one compiled pattern.
    internal RegexAnchorVariant Normalize(RegexAnchorVariant variant)
        => (RegexAnchorVariant)(((int)variant & 2 & (HasAnchorA ? 2 : 0)) | ((int)variant & 1 & (HasAnchorG ? 1 : 0)));

#if !TINT_GENERATOR
    internal CompiledRegex GetCompiled(RegexAnchorVariant variant)
    {
        variant = Normalize(variant);
        var index = (int)variant;
        var compiled = Volatile.Read(ref _compiled[index]);
        if (compiled is not null)
        {
            return compiled;
        }

        compiled = PatternFactory is { } factory
            ? new CompiledRegex(factory(variant), null)
            : RegexCompiler.Compile(GetText(variant));
        return Interlocked.CompareExchange(ref _compiled[index], compiled, null) ?? compiled;
    }
#endif
}
