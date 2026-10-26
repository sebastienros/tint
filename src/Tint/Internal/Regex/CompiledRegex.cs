using GregsEx;

namespace Tint.Internal.Regex;

// A parsed pattern, or the never-matching placeholder and the reason when the pattern is invalid. An invalid pattern
// only fails a search that reaches it, as Onigwrap does.
internal sealed class CompiledRegex
{
    public CompiledRegex(OnigurumaPattern pattern, string? error)
    {
        Pattern = pattern;
        Error = error;
    }

    public OnigurumaPattern Pattern { get; }

    public string? Error { get; }
}
