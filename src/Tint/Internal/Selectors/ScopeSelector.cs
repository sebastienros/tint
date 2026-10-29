namespace Tint.Internal.Selectors;

// Matches a run of identifiers (a scope path such as `meta.tag string.quoted`) against scope names, root first.
internal delegate bool NameMatcher(string[] identifiers, ReadOnlySpan<string> scopes);

internal abstract class ScopeMatcher
{
    public abstract bool Matches(ReadOnlySpan<string> scopes);
}

internal readonly record struct ScopeMatcherWithPriority(ScopeMatcher Matcher, int Priority);

// A port of vscode-textmate's matcher.ts: comma-separated alternatives, each optionally prefixed by L: (priority -1)
// or R: (priority 1); conjunctions of operands; `-` negation; parentheses holding `|`/`,` alternatives.
internal static class ScopeSelector
{
    // grammar.ts nameMatcher: each identifier matches a later scope (descendant semantics), by equality or prefix up
    // to a dot.
    public static readonly NameMatcher ScopeNameMatcher = static (identifiers, scopes) =>
    {
        if (scopes.Length < identifiers.Length)
        {
            return false;
        }

        var lastIndex = 0;
        foreach (var identifier in identifiers)
        {
            var found = false;
            for (var i = lastIndex; i < scopes.Length; i++)
            {
                if (ScopesAreMatching(scopes[i], identifier))
                {
                    lastIndex = i + 1;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    };

    public static bool ScopesAreMatching(string? thisScopeName, string scopeName)
    {
        if (thisScopeName is null || thisScopeName.Length == 0)
        {
            return false;
        }

        if (thisScopeName == scopeName)
        {
            return true;
        }

        var length = scopeName.Length;
        return thisScopeName.Length > length && thisScopeName.StartsWith(scopeName, StringComparison.Ordinal) && thisScopeName[length] == '.';
    }

    public static List<ScopeMatcherWithPriority> CreateMatchers(string selector, NameMatcher matchesName)
    {
        var parser = new Parser(selector, matchesName);
        return parser.ParseAll();
    }

    private sealed class Parser
    {
        private readonly string _input;
        private readonly NameMatcher _matchesName;
        private int _position;
        private string? _token;

        public Parser(string input, NameMatcher matchesName)
        {
            _input = input;
            _matchesName = matchesName;
            _token = Next();
        }

        public List<ScopeMatcherWithPriority> ParseAll()
        {
            var results = new List<ScopeMatcherWithPriority>();
            while (_token is not null)
            {
                var priority = 0;
                if (_token.Length == 2 && _token[1] == ':')
                {
                    priority = _token[0] switch
                    {
                        'R' => 1,
                        'L' => -1,
                        _ => 0,
                    };
                    _token = Next();
                }

                var matcher = ParseConjunction();
                results.Add(new ScopeMatcherWithPriority(matcher, priority));
                if (_token != ",")
                {
                    break;
                }

                _token = Next();
            }

            return results;
        }

        private ScopeMatcher? ParseOperand()
        {
            if (_token == "-")
            {
                _token = Next();
                return new Negation(ParseOperand());
            }

            if (_token == "(")
            {
                _token = Next();
                var inner = ParseInnerExpression();
                if (_token == ")")
                {
                    _token = Next();
                }

                return inner;
            }

            if (IsIdentifier(_token))
            {
                var identifiers = new List<string>();
                do
                {
                    identifiers.Add(_token!);
                    _token = Next();
                }
                while (IsIdentifier(_token));

                return new Names([.. identifiers], _matchesName);
            }

            return null;
        }

        private ScopeMatcher ParseConjunction()
        {
            var matchers = new List<ScopeMatcher>();
            var matcher = ParseOperand();
            while (matcher is not null)
            {
                matchers.Add(matcher);
                matcher = ParseOperand();
            }

            return new Conjunction([.. matchers]);
        }

        private ScopeMatcher ParseInnerExpression()
        {
            var matchers = new List<ScopeMatcher>();
            var matcher = ParseConjunction();
            while (true)
            {
                matchers.Add(matcher);
                if (_token is "|" or ",")
                {
                    do
                    {
                        _token = Next();
                    }
                    while (_token is "|" or ",");
                }
                else
                {
                    break;
                }

                matcher = ParseConjunction();
            }

            return new Disjunction([.. matchers]);
        }

        // /[\w\.:]+/ anywhere in the token.
        private static bool IsIdentifier(string? token)
        {
            if (token is null || token.Length == 0)
            {
                return false;
            }

            foreach (var c in token)
            {
                if (IsWord(c) || c is '.' or ':')
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsWord(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';

        // The tokens of /([LR]:|[\w\.:][\w\.:\-]*|[\,\|\-\(\)])/g; characters matching none are skipped.
        private string? Next()
        {
            while (_position < _input.Length)
            {
                var c = _input[_position];
                if (c is 'L' or 'R' && _position + 1 < _input.Length && _input[_position + 1] == ':')
                {
                    _position += 2;
                    return c == 'L' ? "L:" : "R:";
                }

                if (IsWord(c) || c is '.' or ':')
                {
                    var start = _position++;
                    while (_position < _input.Length && (IsWord(_input[_position]) || _input[_position] is '.' or ':' or '-'))
                    {
                        _position++;
                    }

                    return _input.Substring(start, _position - start);
                }

                _position++;
                switch (c)
                {
                    case ',':
                        return ",";
                    case '|':
                        return "|";
                    case '-':
                        return "-";
                    case '(':
                        return "(";
                    case ')':
                        return ")";
                }
            }

            return null;
        }
    }

    private sealed class Negation : ScopeMatcher
    {
        private readonly ScopeMatcher? _inner;

        public Negation(ScopeMatcher? inner)
        {
            _inner = inner;
        }

        public override bool Matches(ReadOnlySpan<string> scopes) => _inner is not null && !_inner.Matches(scopes);
    }

    private sealed class Names : ScopeMatcher
    {
        private readonly string[] _identifiers;
        private readonly NameMatcher _matchesName;

        public Names(string[] identifiers, NameMatcher matchesName)
        {
            _identifiers = identifiers;
            _matchesName = matchesName;
        }

        public override bool Matches(ReadOnlySpan<string> scopes) => _matchesName(_identifiers, scopes);
    }

    private sealed class Conjunction : ScopeMatcher
    {
        private readonly ScopeMatcher[] _matchers;

        public Conjunction(ScopeMatcher[] matchers)
        {
            _matchers = matchers;
        }

        public override bool Matches(ReadOnlySpan<string> scopes)
        {
            foreach (var matcher in _matchers)
            {
                if (!matcher.Matches(scopes))
                {
                    return false;
                }
            }

            return true;
        }
    }

    private sealed class Disjunction : ScopeMatcher
    {
        private readonly ScopeMatcher[] _matchers;

        public Disjunction(ScopeMatcher[] matchers)
        {
            _matchers = matchers;
        }

        public override bool Matches(ReadOnlySpan<string> scopes)
        {
            foreach (var matcher in _matchers)
            {
                if (matcher.Matches(scopes))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
