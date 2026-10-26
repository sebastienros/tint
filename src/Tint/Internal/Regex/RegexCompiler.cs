using GregsEx;

namespace Tint.Internal.Regex;

internal static class RegexCompiler
{
    // Grammars hold patterns far longer than GregsEx's default ceilings (objective-c's platform list is 282k units).
    // These are the limits the TextMateSharp + GregsEx branch validated over the VS Code corpus.
    public static OnigurumaCompileOptions Options { get; } = new(
        new RegexCompileLimits(
            maxPatternUnits: 1 << 22,
            maxNestingDepth: 4096,
            maxInstructions: 1 << 24,
            maxClassRanges: 1 << 20,
            maxCaptureGroups: 32767,
            maxCompileWork: 1L << 32,
            maxPlanBytes: 1L << 30,
            maxCompileMemoryBytes: 1L << 30),
        new RegexMatchLimits(maxWork: 10_000_000, maxWorkspaceBytes: 64L << 20));

    private static readonly OnigurumaPattern s_never = OnigurumaPattern.Parse("(?!)", RegexProfiles.OnigurumaTextMate, Options);

    public static CompiledRegex Compile(string text)
    {
        if (OnigurumaPattern.TryParse(text, RegexProfiles.OnigurumaTextMate, out var pattern, out var diagnostic, Options) && pattern is not null)
        {
            return new CompiledRegex(pattern, null);
        }

        return new CompiledRegex(s_never, $"Invalid regular expression '{text}': {diagnostic.Message} (offset {diagnostic.Offset}).");
    }
}
