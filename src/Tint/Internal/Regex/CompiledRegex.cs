using GregsEx;

namespace Tint.Internal.Regex;

// A parsed pattern, or the never-matching placeholder and the reason when the pattern is invalid. An invalid pattern
// only fails a search that reaches it, as Onigwrap does. The runtime-compiled form (tier 2, docs/design.md section
// 5.5) is built on request and shared by every scanner that holds this pattern.
internal sealed class CompiledRegex
{
    private OnigurumaPattern? _hotPattern;
    private volatile bool _cannotCompile;

    public CompiledRegex(OnigurumaPattern pattern, string? error)
    {
        Pattern = pattern;
        Error = error;
    }

    // The interpreted pattern.
    public OnigurumaPattern Pattern { get; }

    public string? Error { get; }

    // The runtime-compiled pattern when one was built, without building it.
    public OnigurumaPattern? HotPattern => Volatile.Read(ref _hotPattern);

    // The runtime-compiled pattern, compiled on the first call; the interpreted one when the runtime cannot generate
    // code or GregsEx declines the pattern. Concurrent first calls may both compile; the first published wins.
    public OnigurumaPattern GetOrCompileHotPattern()
    {
        if (Volatile.Read(ref _hotPattern) is { } hot)
        {
            return hot;
        }

        if (Error is not null || _cannotCompile)
        {
            return Pattern;
        }

        var compiled = RuntimeCompilation.TryCompile(Pattern);
        if (compiled is null)
        {
            _cannotCompile = true;
            return Pattern;
        }

        return Interlocked.CompareExchange(ref _hotPattern, compiled, null) ?? compiled;
    }
}
