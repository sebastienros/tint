using GregsEx;

namespace Tint.Internal.Regex;

internal static class RegexCompiler
{
    // Grammars hold patterns far longer than GregsEx's default ceilings (objective-c's platform list is 282k units).
    // GregsEx's TextMate preset carries the limits the TextMateSharp + GregsEx branch validated over the VS Code
    // corpus, with the default match limits (10M work units, 64 MiB workspace).
    public static OnigurumaCompileOptions Options => OnigurumaCompileOptions.TextMate;

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
