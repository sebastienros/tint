using System.Diagnostics;
using System.Runtime.CompilerServices;
using GregsEx;
using Tint.Grammars;
using Tint.Internal.Regex;
using Tint.Internal.Rules;
using Tint.Internal;

namespace Tint;

/// <summary>
/// Tokenizes lines with one grammar and one theme, a port of vscode-textmate's <c>tokenizeString</c>. Not
/// thread-safe: a tokenizer owns its scratch buffers and scanners. Compiled rules, parsed patterns and scope stacks
/// are shared by the tokenizers of a registry, but each tokenizer builds its own regex scanners as rules are reached
/// and holds slot arrays sized by the grammar's rule count, so creating one per request is not free for large
/// grammars: keep one per thread or per open document and reuse it.
/// </summary>
public sealed class Tokenizer
{
    private const int DynamicCacheSize = 16;

    private readonly CompiledGrammar _grammar;
    private readonly RuleTable _rules;
    private readonly ScopeContext _context;
    private readonly Injection[] _injections;
    private readonly RuleScanner?[] _scanners;
    private readonly RuleScanner?[] _whileScanners;
    private readonly Dictionary<int, DynamicPatternCache> _dynamicPatterns = [];
    private readonly CharBuffer _buffer = new();
    private readonly List<StateStack> _whileNodes = [];

    private Token[] _tokens = new Token[64];
    private int _tokenCount;
    private EncodedToken[] _encodedTokens = new EncodedToken[64];
    private int _encodedTokenCount;
    private bool _emitEncoded;
    private bool _mergeEncoded;
    private int _lastTokenEndIndex;

    // The scopes of the last token produced, merged encoded tokens included: the synthetic newline's scopes once the
    // line is tokenized.
    private AttributedScopeStack? _lastProducedScopes;

    // Captures of the matches being handled, as a stack: each search copies its captures above the ones still in use.
    private CaptureSpan[] _captures = new CaptureSpan[128];
    private int _captureTop;

    private LocalStackElement[] _localStack = new LocalStackElement[16];
    private int _localTop;
    private string[] _scopeNames = new string[32];
    private readonly int _hotThreshold;

    internal Tokenizer(CompiledGrammar grammar, Theme theme, TokenizerOptions options)
    {
        _grammar = grammar;
        _rules = grammar.Rules;
        _context = grammar.GetContext(theme);
        LineEndScopes = _context.RootScopes;
        _injections = grammar.Injections;
        _scanners = new RuleScanner?[_rules.Count * 4];
        _whileScanners = new RuleScanner?[_rules.Count * 4];
        Theme = theme;
        Options = options;
        _hotThreshold = options.EffectiveHotThreshold;
    }

    /// <summary>The root scope name of the grammar.</summary>
    public string ScopeName => _grammar.ScopeName;

    /// <summary>The theme token attributes are resolved against.</summary>
    public Theme Theme { get; }

    /// <summary>The options the tokenizer was created with.</summary>
    public TokenizerOptions Options { get; }

    /// <summary>Scope names the grammar includes that were not registered; their rules were skipped.</summary>
    public IReadOnlyList<string> MissingGrammars => _grammar.MissingGrammars;

    /// <summary>
    /// The time a line may take, checked between searches; null for no limit. A line that runs out of time ends with
    /// the tokens found so far and <see cref="StoppedEarly"/> set.
    /// </summary>
    public TimeSpan? TimeBudget { get; set; }

    /// <summary>Whether the last line ran out of <see cref="TimeBudget"/>.</summary>
    public bool StoppedEarly { get; private set; }

    /// <summary>
    /// The scopes of the synthetic newline from the last <see cref="TokenizeLine"/> or
    /// <see cref="TokenizeLineEncoded"/> call.
    /// </summary>
    public AttributedScopeStack LineEndScopes { get; private set; }

    /// <summary>The number of regex searches that ran out of work and counted as no match.</summary>
    public long LimitExceededCount
    {
        get
        {
            long count = 0;
            foreach (var scanner in _scanners)
            {
                count += scanner?.LimitExceededCount ?? 0;
            }

            foreach (var scanner in _whileScanners)
            {
                count += scanner?.LimitExceededCount ?? 0;
            }

            foreach (var cache in _dynamicPatterns.Values)
            {
                count += cache.LimitExceededCount;
            }

            return count;
        }
    }

    // The patterns of this tokenizer's scanners that run generated code (tier 2).
    internal int CompiledPatternCount
    {
        get
        {
            var count = 0;
            foreach (var scanner in _scanners)
            {
                count += scanner?.CompiledPatternCount ?? 0;
            }

            foreach (var scanner in _whileScanners)
            {
                count += scanner?.CompiledPatternCount ?? 0;
            }

            return count;
        }
    }

    /// <summary>
    /// Tokenizes one line (without its line terminator) starting from the state the previous line ended in.
    /// </summary>
    /// <param name="line">The line text.</param>
    /// <param name="state">The state after the previous line, or <see cref="StateStack.Initial"/> for the first line. It must come from a tokenizer of the same compiled grammar (the same registry, scope and registration).</param>
    /// <param name="next">The state after this line.</param>
    /// <returns>The tokens, valid until the next call on this tokenizer.</returns>
    /// <exception cref="ArgumentException"><paramref name="state"/> comes from another compiled grammar.</exception>
    /// <exception cref="TintGrammarException">A search reached a pattern of the grammar that does not parse.</exception>
    public ReadOnlySpan<Token> TokenizeLine(ReadOnlySpan<char> line, StateStack state, out StateStack next)
    {
        Tokenize(line, state, encoded: false, out next);
        return _tokens.AsSpan(0, _tokenCount);
    }

    /// <summary>
    /// Tokenizes one line into binary tokens (vscode-textmate's <c>tokenizeLine2</c>): adjacent tokens with the same
    /// metadata are merged, token types and bracket scopes from <see cref="GrammarInfo"/> are applied.
    /// </summary>
    /// <param name="line">The line text.</param>
    /// <param name="state">The state after the previous line, or <see cref="StateStack.Initial"/> for the first line. It must come from a tokenizer of the same compiled grammar (the same registry, scope and registration).</param>
    /// <param name="next">The state after this line.</param>
    /// <returns>The tokens, valid until the next call on this tokenizer.</returns>
    /// <exception cref="ArgumentException"><paramref name="state"/> comes from another compiled grammar.</exception>
    /// <exception cref="TintGrammarException">A search reached a pattern of the grammar that does not parse.</exception>
    public ReadOnlySpan<EncodedToken> TokenizeLineEncoded(ReadOnlySpan<char> line, StateStack state, out StateStack next)
    {
        Tokenize(line, state, encoded: true, out next);
        return _encodedTokens.AsSpan(0, _encodedTokenCount);
    }

    [MethodImpl(Jit.FullOpts)]
    private void Tokenize(ReadOnlySpan<char> line, StateStack state, bool encoded, out StateStack next)
    {
        ArgumentNullException.ThrowIfNull(state);

        bool isFirstLine;
        if (state.Depth == 0)
        {
            isFirstLine = true;
            var root = _context.RootScopes;
            state = new StateStack(null, _grammar.RootId, -1, -1, false, null, root, root);
        }
        else
        {
            // Rule ids index this grammar's rule table: a state from another grammar (or from this scope before it
            // was registered again) cannot continue here.
            if (!ReferenceEquals(state.ContentNameScopes.Context.Grammar, _grammar))
            {
                throw new ArgumentException($"The state was produced by a tokenizer of '{state.ContentNameScopes.Context.Grammar.ScopeName}', not of this compiled '{_grammar.ScopeName}' grammar.", nameof(state));
            }

            // The state's line positions were reset when it was returned (vscode-textmate resets them here).
            isFirstLine = false;
        }

        // The one allocation per line: the scanner's result cache keys on the string behind the memory it searches
        // (docs/design.md section 5.5), so the line plus the appended "\n" is materialized as a string.
        var lineText = string.Concat(line, "\n");
        var lineLength = lineText.Length;

        _emitEncoded = encoded;
        _mergeEncoded = encoded && !ContainsRtl(line);
        _tokenCount = 0;
        _encodedTokenCount = 0;
        _lastTokenEndIndex = 0;
        _lastProducedScopes = null;
        _captureTop = 0;
        _localTop = 0;

        var deadline = TimeBudget is { } budget ? Stopwatch.GetTimestamp() + (long)(budget.TotalSeconds * Stopwatch.Frequency) : 0;
        StoppedEarly = TokenizeString(lineText, lineLength, isFirstLine, 0, ref state, checkWhileConditions: true, deadline);
        LineEndScopes = _lastProducedScopes ?? state.ContentNameScopes;

        if (encoded)
        {
            // Drop the token produced for the appended newline; a line without tokens gets one covering it.
            if (_encodedTokenCount > 0 && _encodedTokens[_encodedTokenCount - 1].Start == lineLength - 1)
            {
                _encodedTokenCount--;
            }

            if (_encodedTokenCount == 0)
            {
                _lastTokenEndIndex = -1;
                Produce(state.ContentNameScopes, lineLength);
                _encodedTokens[_encodedTokenCount - 1] = _encodedTokens[_encodedTokenCount - 1] with { Start = 0 };
            }
        }
        else
        {
            if (_tokenCount > 0 && _tokens[_tokenCount - 1].Start == lineLength - 1)
            {
                _tokenCount--;
            }

            if (_tokenCount == 0)
            {
                _lastTokenEndIndex = -1;
                Produce(state.ContentNameScopes, lineLength);
                _tokens[_tokenCount - 1] = new Token(0, lineLength, _tokens[_tokenCount - 1].Scopes);
            }

            // vscode-textmate's last token can cover the appended newline; report it within the line.
            ref var last = ref _tokens[_tokenCount - 1];
            if (last.End > line.Length)
            {
                last = last with { Length = line.Length - last.Start };
            }
        }

        state.ResetPositions();
        next = state;
    }

    // _tokenizeString. Returns true when the time budget ran out.
    [MethodImpl(Jit.FullOpts)]
    private bool TokenizeString(string text, int lineLength, bool isFirstLine, int linePos, ref StateStack stack, bool checkWhileConditions, long deadline)
    {
        var anchorPosition = -1;
        var frameBase = _captureTop;

        if (checkWhileConditions)
        {
            CheckWhileConditions(text, lineLength, ref isFirstLine, ref linePos, ref stack, ref anchorPosition);
            _captureTop = frameBase;
        }

        while (true)
        {
            if (deadline != 0 && Stopwatch.GetTimestamp() > deadline)
            {
                _captureTop = frameBase;
                return true;
            }

            _captureTop = frameBase;
            if (!MatchRuleOrInjections(text, lineLength, isFirstLine, linePos, stack, anchorPosition, out var match))
            {
                // No match.
                Produce(stack.ContentNameScopes, lineLength);
                break;
            }

            var captureCount = match.CaptureCount;
            var capture0 = _captures[match.CaptureOffset];
            var hasAdvanced = captureCount > 0 && capture0.End > linePos;

            if (match.RuleId == Rule.EndRuleId)
            {
                // We matched the end of this rule: pop it.
                var poppedRule = (BeginEndRule)_rules[stack.RuleId];
                Produce(stack.ContentNameScopes, capture0.Start);

                // stack.WithContentNameScopes(stack.NameScopes): end captures live under the name only.
                var endState = ReferenceEquals(stack.ContentNameScopes, stack.NameScopes)
                    ? PendingState.Of(stack)
                    : new PendingState(stack.Parent, stack.RuleId, stack.EnterPos, stack.AnchorPos, stack.BeginRuleCapturedEol, stack.EndRule, stack.NameScopes, stack.NameScopes);
                HandleCaptures(text, isFirstLine, ref endState, poppedRule.EndCaptures, match.CaptureOffset, captureCount);
                Produce(stack.NameScopes, capture0.End);

                var popped = stack;
                stack = stack.Parent!;
                anchorPosition = popped.AnchorPos;

                if (!hasAdvanced && popped.EnterPos == linePos)
                {
                    // [1] The grammar pushed and popped a rule without advancing: assume the author meant to stay
                    // in this state (vscode-textmate#12).
                    stack = endState.Materialize();
                    Produce(stack.ContentNameScopes, lineLength);
                    break;
                }
            }
            else
            {
                // We matched a rule.
                var rule = _rules[match.RuleId];
                Produce(stack.ContentNameScopes, capture0.Start);

                var beforePush = stack;
                var nameScopes = PushName(stack.ContentNameScopes, rule.Name, rule.NameIsCapturing, text, match.CaptureOffset, captureCount, rule.GrammarScopeName);

                // stack.push(...): the captures see the pushed state, which is only allocated when a capture is
                // retokenized; a begin rule then gets its final state (content name, resolved end) in one node.
                var pushed = new PendingState(stack, match.RuleId, linePos, anchorPosition, capture0.End == lineLength, null, nameScopes, nameScopes);

                if (rule is BeginEndRule beginEnd)
                {
                    HandleCaptures(text, isFirstLine, ref pushed, beginEnd.BeginCaptures, match.CaptureOffset, captureCount);
                    Produce(nameScopes, capture0.End);
                    anchorPosition = capture0.End;

                    var contentNameScopes = PushName(nameScopes, beginEnd.ContentName, beginEnd.ContentNameIsCapturing, text, match.CaptureOffset, captureCount, beginEnd.GrammarScopeName);
                    var endRule = beginEnd.EndHasBackReferences
                        ? ResolveBackReferences(match.RuleId, isWhile: false, beginEnd.End, text, match.CaptureOffset, captureCount)
                        : null;
                    stack = pushed.Materialize(contentNameScopes, endRule);

                    if (!hasAdvanced && beforePush.HasSameRuleAs(stack))
                    {
                        // [2] The grammar pushed the same rule without advancing.
                        stack = stack.Pop()!;
                        Produce(stack.ContentNameScopes, lineLength);
                        break;
                    }
                }
                else if (rule is BeginWhileRule beginWhile)
                {
                    HandleCaptures(text, isFirstLine, ref pushed, beginWhile.BeginCaptures, match.CaptureOffset, captureCount);
                    Produce(nameScopes, capture0.End);
                    anchorPosition = capture0.End;

                    var contentNameScopes = PushName(nameScopes, beginWhile.ContentName, beginWhile.ContentNameIsCapturing, text, match.CaptureOffset, captureCount, beginWhile.GrammarScopeName);
                    var whileRule = beginWhile.WhileHasBackReferences
                        ? ResolveBackReferences(match.RuleId, isWhile: true, beginWhile.While, text, match.CaptureOffset, captureCount)
                        : null;
                    stack = pushed.Materialize(contentNameScopes, whileRule);

                    if (!hasAdvanced && beforePush.HasSameRuleAs(stack))
                    {
                        // [3] The grammar pushed the same rule without advancing.
                        stack = stack.Pop()!;
                        Produce(stack.ContentNameScopes, lineLength);
                        break;
                    }
                }
                else
                {
                    var matchRule = (MatchRule)rule;
                    HandleCaptures(text, isFirstLine, ref pushed, matchRule.Captures, match.CaptureOffset, captureCount);
                    Produce(nameScopes, capture0.End);

                    // Pop the rule immediately since it is a match rule: the stack stays as it was.

                    if (!hasAdvanced)
                    {
                        // [4] The grammar is not advancing, nor is it pushing or popping.
                        stack = stack.SafePop();
                        Produce(stack.ContentNameScopes, lineLength);
                        break;
                    }
                }
            }

            if (capture0.End > linePos)
            {
                // Advance the stream.
                linePos = capture0.End;
                isFirstLine = false;
            }
        }

        _captureTop = frameBase;
        return false;
    }

    // _checkWhileConditions: from the outermost begin/while rule inward, each while pattern must match at the line
    // start; the first that fails cuts the stack above it.
    [MethodImpl(Jit.FullOpts)]
    private void CheckWhileConditions(string text, int lineLength, ref bool isFirstLine, ref int linePos, ref StateStack stack, ref int anchorPosition)
    {
        anchorPosition = stack.BeginRuleCapturedEol ? 0 : -1;

        _whileNodes.Clear();
        for (var node = stack; node is not null; node = node.Pop())
        {
            if (_rules[node.RuleId] is BeginWhileRule)
            {
                _whileNodes.Add(node);
            }
        }

        for (var i = _whileNodes.Count - 1; i >= 0; i--)
        {
            var node = _whileNodes[i];
            var rule = (BeginWhileRule)_rules[node.RuleId];
            var frameBase = _captureTop;
            var found = Search(node.RuleId, rule.GetWhilePatterns(), node.EndRule, isWhile: true, text, lineLength, linePos, isFirstLine, linePos == anchorPosition, out var match);
            if (!found)
            {
                stack = node.Pop()!;
                break;
            }

            if (match.RuleId != Rule.WhileRuleId)
            {
                // We shouldn't end up here.
                stack = node.Pop()!;
                break;
            }

            if (match.CaptureCount > 0)
            {
                var capture0 = _captures[match.CaptureOffset];
                Produce(node.ContentNameScopes, capture0.Start);
                var whileState = PendingState.Of(node);
                HandleCaptures(text, isFirstLine, ref whileState, rule.WhileCaptures, match.CaptureOffset, match.CaptureCount);
                Produce(node.ContentNameScopes, capture0.End);
                anchorPosition = capture0.End;
                if (capture0.End > linePos)
                {
                    linePos = capture0.End;
                    isFirstLine = false;
                }
            }

            _captureTop = frameBase;
        }
    }

    [MethodImpl(Jit.FullOpts)]
    private bool MatchRuleOrInjections(string text, int lineLength, bool isFirstLine, int linePos, StateStack stack, int anchorPosition, out MatchResult result)
    {
        // Look for a normal grammar rule.
        var rule = _rules[stack.RuleId];
        var found = Search(rule.Id, GetScannerPatterns(rule), stack.EndRule, isWhile: false, text, lineLength, linePos, isFirstLine, linePos == anchorPosition, out result);

        // Look for injected rules.
        if (_injections.Length == 0)
        {
            return found;
        }

        if (!MatchInjections(text, lineLength, isFirstLine, linePos, stack, anchorPosition, out var injection, out var priorityMatch))
        {
            return found;
        }

        if (!found)
        {
            result = injection;
            return true;
        }

        // The injection wins when it starts earlier, or at the same place with L: priority.
        var matchStart = _captures[result.CaptureOffset].Start;
        var injectionStart = _captures[injection.CaptureOffset].Start;
        if (injectionStart < matchStart || (priorityMatch && injectionStart == matchStart))
        {
            result = injection;
        }

        return true;
    }

    [MethodImpl(Jit.FullOpts)]
    private bool MatchInjections(string text, int lineLength, bool isFirstLine, int linePos, StateStack stack, int anchorPosition, out MatchResult result, out bool priorityMatch)
    {
        // The lower the better.
        var bestMatchRating = int.MaxValue;
        var bestPriority = 0;
        var hasBest = false;
        result = default;

        var scopes = stack.ContentNameScopes;
        var mask = GetInjectionMask(scopes);
        for (var i = 0; i < _injections.Length; i++)
        {
            var injection = _injections[i];
            if (i < 64 ? (mask & (1UL << i)) == 0 : !injection.Matcher.Matches(FillScopeNames(scopes)))
            {
                // The injection selector doesn't match the stack.
                continue;
            }

            var rule = _rules[injection.RuleId];
            if (!Search(rule.Id, GetScannerPatterns(rule), null, isWhile: false, text, lineLength, linePos, isFirstLine, linePos == anchorPosition, out var match))
            {
                continue;
            }

            var matchRating = _captures[match.CaptureOffset].Start;
            if (matchRating >= bestMatchRating)
            {
                // Injections are sorted by priority, so the previous injection had a better or equal priority.
                _captureTop = match.CaptureOffset;
                continue;
            }

            bestMatchRating = matchRating;
            result = match;
            bestPriority = injection.Priority;
            hasBest = true;

            if (bestMatchRating == linePos)
            {
                // No more need to look at the rest of the injections.
                break;
            }
        }

        priorityMatch = bestPriority == -1;
        return hasBest;
    }

    [MethodImpl(Jit.FullOpts)]
    private ulong GetInjectionMask(AttributedScopeStack scopes)
    {
        if (scopes.TryGetInjectionMask(out var mask))
        {
            return mask;
        }

        var names = FillScopeNames(scopes);
        for (var i = 0; i < _injections.Length && i < 64; i++)
        {
            if (_injections[i].Matcher.Matches(names))
            {
                mask |= 1UL << i;
            }
        }

        scopes.SetInjectionMask(mask);
        return mask;
    }

    [MethodImpl(Jit.FullOpts)]
    private RulePatterns GetScannerPatterns(Rule rule) => rule switch
    {
        RuleWithPatterns withPatterns => withPatterns.GetScannerPatterns(_rules),
        MatchRule match => match.GetScannerPatterns(),
        _ => throw new InvalidOperationException($"Rule {rule.Id} cannot be searched."),
    };

    // CompiledRule.findNextMatchSync over the rule's patterns: the static ones in one scanner, a back-referencing
    // end or while pattern in its own, merged by earliest start with the dynamic one first or last in priority.
    [MethodImpl(Jit.FullOpts)]
    private bool Search(int ruleId, RulePatterns patterns, string? endRule, bool isWhile, string text, int lineLength, int start, bool allowA, bool allowG, out MatchResult result)
    {
        RuleScanner? winner = null;
        var winnerRuleId = 0;
        var bestStart = int.MaxValue;

        RuleScanner? dynamic = null;
        if (patterns.DynamicSource is { } dynamicSource)
        {
            dynamic = GetDynamicScanner(ruleId, isWhile, dynamicSource, patterns.DynamicRuleId, endRule ?? string.Empty, allowA, allowG);
            if (patterns.DynamicIndex == 0 && dynamic.FindNextMatch(text, lineLength, start, out winnerRuleId))
            {
                winner = dynamic;
                bestStart = dynamic.Captures[0].Index;
            }
        }

        if (patterns.Entries.Length > 0 && bestStart != start)
        {
            var scanner = GetScanner(ruleId, isWhile, patterns, patterns.Variant(allowA, allowG));
            if (scanner.FindNextMatch(text, lineLength, start, out var staticRuleId) && scanner.Captures[0].Index < bestStart)
            {
                winner = scanner;
                winnerRuleId = staticRuleId;
                bestStart = scanner.Captures[0].Index;
            }
        }

        if (dynamic is not null && patterns.DynamicIndex != 0 && bestStart != start
            && dynamic.FindNextMatch(text, lineLength, start, out var dynamicRuleId) && dynamic.Captures[0].Index < bestStart)
        {
            winner = dynamic;
            winnerRuleId = dynamicRuleId;
        }

        if (winner is null)
        {
            result = default;
            return false;
        }

        result = new MatchResult(winnerRuleId, CopyCaptures(winner.Captures), winner.Captures.Length);
        return true;
    }

    [MethodImpl(Jit.FullOpts)]
    private RuleScanner GetScanner(int ruleId, bool isWhile, RulePatterns patterns, RegexAnchorVariant variant)
    {
        var scanners = isWhile ? _whileScanners : _scanners;
        var slot = (ruleId * 4) + (int)variant;
        if (scanners[slot] is { } scanner)
        {
            return scanner;
        }

        var entries = patterns.Entries;
        var compiled = new CompiledRegex[entries.Length];
        var ruleIds = new int[entries.Length];
        for (var i = 0; i < entries.Length; i++)
        {
            compiled[i] = entries[i].Source.GetCompiled(variant);
            ruleIds[i] = entries[i].RuleId;
        }

        return scanners[slot] = new RuleScanner(compiled, ruleIds, _hotThreshold);
    }

    [MethodImpl(Jit.FullOpts)]
    private RuleScanner GetDynamicScanner(int ruleId, bool isWhile, RegexSource source, int dynamicRuleId, string text, bool allowA, bool allowG)
    {
        var entry = GetDynamicCache(ruleId, isWhile).Get(text);
        var variant = (allowA && source.HasAnchorA ? 2 : 0) | (allowG && source.HasAnchorG ? 1 : 0);
        if (entry.Scanners[variant] is { } scanner)
        {
            return scanner;
        }

        var variantText = source.HasAnchor ? PatternRewriter.ResolveAnchors(text, (variant & 2) != 0, (variant & 1) != 0) : text;
        return entry.Scanners[variant] = new RuleScanner([RegexCompiler.Compile(variantText)], [dynamicRuleId]);
    }

    private DynamicPatternCache GetDynamicCache(int ruleId, bool isWhile)
    {
        var key = (ruleId * 2) + (isWhile ? 1 : 0);
        if (!_dynamicPatterns.TryGetValue(key, out var cache))
        {
            cache = new DynamicPatternCache(DynamicCacheSize);
            _dynamicPatterns[key] = cache;
        }

        return cache;
    }

    // getEndWithResolvedBackReferences / getWhileWithResolvedBackReferences. Resolved texts the rule saw recently are
    // reused, so a repeated heredoc delimiter or fence allocates nothing.
    [MethodImpl(Jit.FullOpts)]
    private string ResolveBackReferences(int ruleId, bool isWhile, RegexSource source, string text, int captureOffset, int captureCount)
    {
        PatternRewriter.ResolveBackReferences(source.Text, text, _captures.AsSpan(captureOffset, captureCount), _buffer);
        return GetDynamicCache(ruleId, isWhile).FindText(_buffer.AsSpan()) ?? _buffer.ToString();
    }

    // handleCaptures.
    [MethodImpl(Jit.FullOpts)]
    private void HandleCaptures(string text, bool isFirstLine, ref PendingState stack, CaptureRule?[] captures, int captureOffset, int captureCount)
    {
        if (captures.Length == 0)
        {
            return;
        }

        var length = Math.Min(captures.Length, captureCount);
        var localBase = _localTop;
        var maxEnd = _captures[captureOffset].End;

        for (var i = 0; i < length; i++)
        {
            var captureRule = captures[i];
            if (captureRule is null)
            {
                // Not interested.
                continue;
            }

            var capture = _captures[captureOffset + i];
            if (capture.Length == 0)
            {
                // Nothing really captured.
                continue;
            }

            if (capture.Start > maxEnd)
            {
                // Capture going beyond the consumed string.
                break;
            }

            // Pop captures while needed.
            while (_localTop > localBase && _localStack[_localTop - 1].EndPos <= capture.Start)
            {
                _localTop--;
                Produce(_localStack[_localTop].Scopes, _localStack[_localTop].EndPos);
            }

            Produce(_localTop > localBase ? _localStack[_localTop - 1].Scopes : stack.ContentNameScopes, capture.Start);

            if (captureRule.RetokenizeCapturedWithRuleId != 0)
            {
                // The capture requires additional matching: tokenize the line up to the capture's end (the prefix
                // keeps look-behinds working) from the capture's start.
                var nameScopes = PushName(stack.ContentNameScopes, captureRule.Name, captureRule.NameIsCapturing, text, captureOffset, captureCount, captureRule.GrammarScopeName);
                var contentNameScopes = PushName(nameScopes, captureRule.ContentName, captureRule.ContentNameIsCapturing, text, captureOffset, captureCount, captureRule.GrammarScopeName);
                var stackClone = stack.Materialize().Push(captureRule.RetokenizeCapturedWithRuleId, capture.Start, -1, false, null, nameScopes, contentNameScopes);
                TokenizeString(text, capture.End, isFirstLine && capture.Start == 0, capture.Start, ref stackClone, checkWhileConditions: false, deadline: 0);
                continue;
            }

            if (captureRule.Name is not null)
            {
                var baseScopes = _localTop > localBase ? _localStack[_localTop - 1].Scopes : stack.ContentNameScopes;
                var captureScopes = PushName(baseScopes, captureRule.Name, captureRule.NameIsCapturing, text, captureOffset, captureCount, captureRule.GrammarScopeName);
                if (_localTop == _localStack.Length)
                {
                    Array.Resize(ref _localStack, _localStack.Length * 2);
                }

                _localStack[_localTop++] = new LocalStackElement(captureScopes, capture.End);
            }
        }

        while (_localTop > localBase)
        {
            _localTop--;
            Produce(_localStack[_localTop].Scopes, _localStack[_localTop].EndPos);
        }
    }

    [MethodImpl(Jit.FullOpts)]
    private AttributedScopeStack PushName(AttributedScopeStack scopes, string? name, bool isCapturing, string text, int captureOffset, int captureCount, string grammarScopeName)
    {
        if (name is null)
        {
            return scopes;
        }

        if (!isCapturing)
        {
            return scopes.PushAttributed(name, grammarScopeName);
        }

        CaptureNames.Replace(name, text, _captures.AsSpan(captureOffset, captureCount), _buffer);
        return scopes.PushAttributed(_buffer.AsSpan(), grammarScopeName);
    }

    [MethodImpl(Jit.FullOpts)]
    private int CopyCaptures(ReadOnlySpan<RegexCapture> captures)
    {
        var offset = _captureTop;
        if (offset + captures.Length > _captures.Length)
        {
            Array.Resize(ref _captures, Math.Max(_captures.Length * 2, offset + captures.Length));
        }

        for (var i = 0; i < captures.Length; i++)
        {
            var capture = captures[i];
            _captures[offset + i] = capture.Participated ? new CaptureSpan(capture.Index, capture.Index + capture.Length) : default;
        }

        _captureTop = offset + captures.Length;
        return offset;
    }

    [MethodImpl(Jit.FullOpts)]
    private ReadOnlySpan<string> FillScopeNames(AttributedScopeStack scopes)
    {
        if (_scopeNames.Length < scopes.Depth)
        {
            _scopeNames = new string[Math.Max(_scopeNames.Length * 2, scopes.Depth)];
        }

        var names = _scopeNames.AsSpan(0, scopes.Depth);
        scopes.CopyScopeNames(names);
        return names;
    }

    // LineTokens.produceFromScopes.
    [MethodImpl(Jit.FullOpts)]
    private void Produce(AttributedScopeStack scopes, int endIndex)
    {
        if (_lastTokenEndIndex >= endIndex)
        {
            return;
        }

        _lastProducedScopes = scopes;
        if (_emitEncoded)
        {
            var metadata = GetEncodedMetadata(scopes);
            if (_mergeEncoded && _encodedTokenCount > 0 && _encodedTokens[_encodedTokenCount - 1].Metadata == metadata)
            {
                // No need to push a token with the same metadata.
                _lastTokenEndIndex = endIndex;
                return;
            }

            if (_encodedTokenCount == _encodedTokens.Length)
            {
                Array.Resize(ref _encodedTokens, _encodedTokens.Length * 2);
            }

            _encodedTokens[_encodedTokenCount++] = new EncodedToken(_lastTokenEndIndex, metadata);
        }
        else
        {
            if (_tokenCount == _tokens.Length)
            {
                Array.Resize(ref _tokens, _tokens.Length * 2);
            }

            _tokens[_tokenCount++] = new Token(_lastTokenEndIndex, endIndex - _lastTokenEndIndex, scopes);
        }

        _lastTokenEndIndex = endIndex;
    }

    // The binary metadata of a scope stack with the grammar's token type and bracket overrides, cached per node.
    [MethodImpl(Jit.FullOpts)]
    private uint GetEncodedMetadata(AttributedScopeStack scopes)
    {
        if (scopes.TryGetEncodedMetadata(out var cached))
        {
            return cached;
        }

        var metadata = scopes.TokenAttributes;
        var brackets = _grammar.BalancedBracketSelectors;
        var containsBalancedBrackets = brackets is { MatchesAlways: true };
        if (_grammar.TokenTypeMatchers.Length > 0 || (brackets is not null && !brackets.MatchesAlways && !brackets.MatchesNever))
        {
            var names = FillScopeNames(scopes);
            foreach (var tokenType in _grammar.TokenTypeMatchers)
            {
                if (tokenType.Matcher.Matches(names))
                {
                    metadata = EncodedTokenAttributes.Set(metadata, 0, tokenType.Type, null, FontStyle.NotSet, 0, 0);
                }
            }

            if (brackets is not null)
            {
                containsBalancedBrackets = brackets.Match(names);
            }
        }

        if (containsBalancedBrackets)
        {
            metadata = EncodedTokenAttributes.Set(metadata, 0, null, true, FontStyle.NotSet, 0, 0);
        }

        scopes.SetEncodedMetadata(metadata);
        return metadata;
    }

    // utils.ts containsRTL: any character of Unicode bidirectional class R or AL.
    [MethodImpl(Jit.FullOpts)]
    internal static bool ContainsRtl(ReadOnlySpan<char> text)
    {
#if NETSTANDARD2_0
        // The loop below skips the characters before U+05BE.
        var index = 0;
#else
        var index = text.IndexOfAnyInRange('־', '￿');
#endif
        if (index < 0)
        {
            return false;
        }

        for (var i = index; i < text.Length; i++)
        {
            var c = text[i];
            if (c < '־')
            {
                continue;
            }

            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                if (IsRtlSupplementary(c, text[i + 1]))
                {
                    return true;
                }

                i++;
                continue;
            }

            if (IsRtlBmp(c))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsRtlBmp(char c) => c switch
    {
        '־' or '׀' or '׃' or '׆' => true,
        >= 'א' and <= '״' => true,
        '؈' or '؋' or '؍' => true,
        >= '؛' and <= 'ي' => true,
        >= '٭' and <= 'ٯ' => true,
        >= 'ٱ' and <= 'ە' => true,
        'ۥ' or 'ۦ' or 'ۮ' or 'ۯ' => true,
        >= 'ۺ' and <= 'ܐ' => true,
        >= 'ܒ' and <= 'ܯ' => true,
        >= 'ݍ' and <= 'ޥ' => true,
        >= 'ޱ' and <= 'ߪ' => true,
        'ߴ' or 'ߵ' or 'ߺ' => true,
        >= '߾' and <= 'ࠕ' => true,
        'ࠚ' or 'ࠤ' or 'ࠨ' => true,
        >= '࠰' and <= 'ࡘ' => true,
        >= '࡞' and <= 'ࢎ' => true,
        >= 'ࢠ' and <= 'ࣉ' => true,
        '‏' or 'יִ' => true,
        >= 'ײַ' and <= 'ﬨ' => true,
        >= 'שׁ' and <= 'ﴽ' => true,
        >= 'ﵐ' and <= 'ﷇ' => true,
        >= 'ﷰ' and <= '﷼' => true,
        >= 'ﹰ' and <= 'ﻼ' => true,
        _ => false,
    };

    private static bool IsRtlSupplementary(char high, char low) => high switch
    {
        '\uD802' => low is (>= '\uDC00' and <= '\uDD1B') or (>= '\uDD20' and <= '\uDE00') or (>= '\uDE10' and <= '\uDE35') or (>= '\uDE40' and <= '\uDEE4') or (>= '\uDEEB' and <= '\uDF35') or (>= '\uDF40' and <= '\uDFFF'),
        '\uD803' => low is (>= '\uDC00' and <= '\uDD23') or (>= '\uDE80' and <= '\uDEA9') or (>= '\uDEAD' and <= '\uDF45') or (>= '\uDF51' and <= '\uDF81') or (>= '\uDF86' and <= '\uDFF6'),
        '\uD83A' => low is (>= '\uDC00' and <= '\uDCCF') or (>= '\uDD00' and <= '\uDD43') or (>= '\uDD4B' and <= '\uDFFF'),
        '\uD83B' => low is >= '\uDC00' and <= '\uDEBB',
        _ => false,
    };

    // A state handleCaptures sees, allocated only when a capture is retokenized from it.
    private struct PendingState
    {
        private readonly StateStack? _parent;
        private readonly int _ruleId;
        private readonly int _enterPos;
        private readonly int _anchorPos;
        private readonly bool _beginRuleCapturedEol;
        private readonly string? _endRule;
        private readonly AttributedScopeStack _nameScopes;
        private StateStack? _node;

        public PendingState(StateStack? parent, int ruleId, int enterPos, int anchorPos, bool beginRuleCapturedEol, string? endRule, AttributedScopeStack nameScopes, AttributedScopeStack contentNameScopes)
        {
            _parent = parent;
            _ruleId = ruleId;
            _enterPos = enterPos;
            _anchorPos = anchorPos;
            _beginRuleCapturedEol = beginRuleCapturedEol;
            _endRule = endRule;
            _nameScopes = nameScopes;
            ContentNameScopes = contentNameScopes;
            _node = null;
        }

        public AttributedScopeStack ContentNameScopes { get; }

        [MethodImpl(Jit.FullOpts)]
        public static PendingState Of(StateStack node)
        {
            var state = new PendingState(node.Parent, node.RuleId, node.EnterPos, node.AnchorPos, node.BeginRuleCapturedEol, node.EndRule, node.NameScopes, node.ContentNameScopes);
            state._node = node;
            return state;
        }

        [MethodImpl(Jit.FullOpts)]
        public StateStack Materialize()
            => _node ??= new StateStack(_parent, _ruleId, _enterPos, _anchorPos, _beginRuleCapturedEol, _endRule, _nameScopes, ContentNameScopes);

        // The same state with its final content scopes and resolved end rule (withContentNameScopesList, withEndRule).
        [MethodImpl(Jit.FullOpts)]
        public readonly StateStack Materialize(AttributedScopeStack contentNameScopes, string? endRule)
        {
            if (_node is { } node && ReferenceEquals(node.ContentNameScopes, contentNameScopes) && node.EndRule == endRule)
            {
                return node;
            }

            return new StateStack(_parent, _ruleId, _enterPos, _anchorPos, _beginRuleCapturedEol, endRule ?? _endRule, _nameScopes, contentNameScopes);
        }
    }

    private readonly record struct MatchResult(int RuleId, int CaptureOffset, int CaptureCount);

    private readonly record struct LocalStackElement(AttributedScopeStack Scopes, int EndPos);

    // The compiled patterns of a rule's back-referencing end or while pattern, by resolved text, most recent first.
    private sealed class DynamicPatternCache
    {
        private readonly Entry[] _entries;
        private int _count;

        public DynamicPatternCache(int capacity)
        {
            _entries = new Entry[capacity];
        }

        public long LimitExceededCount
        {
            get
            {
                long count = 0;
                for (var i = 0; i < _count; i++)
                {
                    foreach (var scanner in _entries[i].Scanners)
                    {
                        count += scanner?.LimitExceededCount ?? 0;
                    }
                }

                return count;
            }
        }

        [MethodImpl(Jit.FullOpts)]
        public string? FindText(ReadOnlySpan<char> text)
        {
            for (var i = 0; i < _count; i++)
            {
                if (text.SequenceEqual(_entries[i].Text))
                {
                    return _entries[i].Text;
                }
            }

            return null;
        }

        [MethodImpl(Jit.FullOpts)]
        public Entry Get(string text)
        {
            for (var i = 0; i < _count; i++)
            {
                var entry = _entries[i];
                if (ReferenceEquals(entry.Text, text) || entry.Text == text)
                {
                    MoveToFront(i);
                    return entry;
                }
            }

            var created = new Entry(text);
            if (_count < _entries.Length)
            {
                _count++;
            }

            MoveToFront(_count - 1);
            _entries[0] = created;
            return created;
        }

        private void MoveToFront(int index)
        {
            var entry = _entries[index];
            Array.Copy(_entries, 0, _entries, 1, index);
            _entries[0] = entry;
        }

        public sealed class Entry
        {
            public Entry(string text)
            {
                Text = text;
            }

            public string Text { get; }

            public RuleScanner?[] Scanners { get; } = new RuleScanner?[4];
        }
    }
}
