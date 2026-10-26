using GregsEx;

namespace Tint.Internal.Regex;

// The compiled patterns of one rule and anchor variant over a GregsEx OnigurumaScanner (vscode-textmate's
// CompiledRule): the earliest match wins, the first pattern on a tie, a search that runs out of work counts as no
// match. Owned by one tokenizer: OnigurumaScanner keeps per-pattern result caches and is not thread-safe.
internal sealed class RuleScanner
{
    private readonly CompiledRegex[] _patterns;
    private readonly int[] _ruleIds;
    private readonly int _invalidIndex = -1;
    private readonly string? _invalidError;
    private OnigurumaScanner _scanner;

    // Searches left before the patterns are runtime-compiled; 0 when they never are or already were.
    private int _searchesUntilCompile;
    private long _retiredLimitExceededCount;

    // hotThreshold: -1 keeps the interpreted patterns; otherwise the number of searches after which the patterns are
    // swapped for their runtime-compiled forms (0: right away). Patterns another scanner already compiled are used
    // from the start.
    public RuleScanner(CompiledRegex[] patterns, int[] ruleIds, int hotThreshold = -1)
    {
        _patterns = patterns;
        _ruleIds = ruleIds;
        var compiled = new OnigurumaPattern[patterns.Length];
        var allHot = true;
        for (var i = 0; i < patterns.Length; i++)
        {
            var pattern = patterns[i];
            if (hotThreshold == 0)
            {
                compiled[i] = pattern.GetOrCompileHotPattern();
            }
            else if (hotThreshold > 0 && pattern.HotPattern is { } hot)
            {
                compiled[i] = hot;
            }
            else
            {
                compiled[i] = pattern.Pattern;
                allHot &= pattern.Error is not null;
            }

            if (pattern.Error is { } error && _invalidIndex < 0)
            {
                _invalidIndex = i;
                _invalidError = error;
            }
        }

        _scanner = new OnigurumaScanner(compiled);
        _searchesUntilCompile = hotThreshold > 0 && !allHot ? hotThreshold : 0;
    }

    public ReadOnlySpan<RegexCapture> Captures => _scanner.Captures;

    public long LimitExceededCount => _retiredLimitExceededCount + _scanner.LimitExceededCount;

    // The patterns running generated code.
    public int CompiledPatternCount
    {
        get
        {
            var count = 0;
            foreach (var pattern in _scanner.Patterns)
            {
                if (pattern.ExecutionBackend == RegexExecutionBackend.Compiled)
                {
                    count++;
                }
            }

            return count;
        }
    }

    // Searches text[..length] from start. The memory is backed by the line string so the scanner's result cache
    // recognizes it across the searches of one line.
    public bool FindNextMatch(string text, int length, int start, out int ruleId)
    {
        if (_searchesUntilCompile > 0 && --_searchesUntilCompile == 0)
        {
            SwapToCompiled();
        }

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

    // A new scanner over the compiled patterns (a pattern that does not compile stays interpreted). It starts with an
    // empty result cache, which only costs the next search of the current line its cached results.
    private void SwapToCompiled()
    {
        var compiled = new OnigurumaPattern[_patterns.Length];
        for (var i = 0; i < _patterns.Length; i++)
        {
            compiled[i] = _patterns[i].GetOrCompileHotPattern();
        }

        _retiredLimitExceededCount += _scanner.LimitExceededCount;
        _scanner = new OnigurumaScanner(compiled);
    }
}
