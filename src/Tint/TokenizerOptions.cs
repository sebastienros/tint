namespace Tint;

/// <summary>Settings of the tokenizers a <see cref="GrammarRegistry"/> creates.</summary>
public sealed class TokenizerOptions
{
    private readonly int _hotPatternThreshold = DefaultHotPatternThreshold;

    /// <summary>The default <see cref="HotPatternThreshold"/>.</summary>
    public const int DefaultHotPatternThreshold = 256;

    /// <summary>The default options: interpreted patterns only.</summary>
    public static TokenizerOptions Default { get; } = new();

    /// <summary>
    /// Whether the patterns of a rule are compiled to generated code (GregsEx's <c>OnigurumaPattern.Compile()</c>)
    /// once the rule has been searched <see cref="HotPatternThreshold"/> times. Compiled patterns are shared by the
    /// tokenizers of the registry. Only takes effect on runtimes that can generate code: under NativeAOT, in trimmed
    /// apps and with GregsEx's <c>GregsEx.RuntimeCompilation.Auto</c> switch off, patterns stay interpreted.
    /// </summary>
    public bool CompileHotPatterns { get; init; }

    /// <summary>
    /// The searches of a rule after which its patterns are compiled when <see cref="CompileHotPatterns"/> is on; 0
    /// compiles every pattern when its rule is first reached.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int HotPatternThreshold
    {
        get => _hotPatternThreshold;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _hotPatternThreshold = value;
        }
    }

    // The threshold the scanners use: -1 for interpreted only.
    internal int EffectiveHotThreshold => CompileHotPatterns && Internal.Regex.RuntimeCompilation.IsSupported ? HotPatternThreshold : -1;
}
