using GregsEx;

namespace Tint.Internal.Regex;

// The compiled patterns of one rule and anchor variant over a GregsEx OnigurumaScanner (vscode-textmate's
// CompiledRule): the earliest match wins, the first pattern on a tie, a search that runs out of work counts as no
// match. Owned by one tokenizer: OnigurumaScanner keeps per-pattern result caches and is not thread-safe.
internal sealed class RuleScanner
{
    private readonly OnigurumaScanner _scanner;
    private readonly int[] _ruleIds;
    private readonly int _invalidIndex = -1;
    private readonly string? _invalidError;

    public RuleScanner(CompiledRegex[] patterns, int[] ruleIds)
    {
        _ruleIds = ruleIds;
        var compiled = new OnigurumaPattern[patterns.Length];
        for (var i = 0; i < patterns.Length; i++)
        {
            compiled[i] = patterns[i].Pattern;
            if (patterns[i].Error is { } error && _invalidIndex < 0)
            {
                _invalidIndex = i;
                _invalidError = error;
            }
        }

        _scanner = new OnigurumaScanner(compiled);
    }

    public ReadOnlySpan<RegexCapture> Captures => _scanner.Captures;

    public long LimitExceededCount => _scanner.LimitExceededCount;

    // Searches text[..length] from start. The memory is backed by the line string so the scanner's result cache
    // recognizes it across the searches of one line.
    public bool FindNextMatch(string text, int length, int start, out int ruleId)
    {
        var found = _scanner.FindNextMatch(text.AsMemory(0, length), start, out var index);
        if (_invalidIndex >= 0 && (!found || index > _invalidIndex || _scanner.Captures[0].Index > start))
        {
            // The patterns are searched in order up to a match at the start, as Onigwrap does: this search reached
            // the invalid one.
            throw new TintGrammarException(_invalidError!);
        }

        ruleId = found ? _ruleIds[index] : 0;
        return found;
    }
}
