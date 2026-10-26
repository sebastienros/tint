# vscode-textmate: tokenization algorithm (porting reference)

Source: microsoft/vscode-textmate @ 392b23a4 (9.3.2, 2026-10-19) and microsoft/vscode-oniguruma @
f4bf494c (2.1.0). File paths refer to the vscode-textmate `src/` folder.

## Loading, raw grammar shape, `$self` / `$base`

- `rawGrammar.ts`: `IRawGrammar { scopeName, patterns[], repository, injections?: {selector: IRawRule}, injectionSelector? }`; `IRawRule { include?, name?, contentName?, match?, captures?, begin?, beginCaptures?, end?, endCaptures?, while?, whileCaptures?, patterns?, repository?, applyEndPatternLast? }`. Captures are a map `"0".."N" -> IRawRule`. A mutable `id?: RuleId` is stamped onto raw rules during compilation (memoization).
- `parseRawGrammar.ts`: `.json` suffix → JSON, else PLIST (`plist.ts`).
- `grammar/grammar.ts: initGrammar(grammar, base)`: deep-clones the raw grammar, then sets `repository.$self = { patterns: grammar.patterns, name: grammar.scopeName }` and `repository.$base = base ?? repository.$self`. When a grammar is pulled in as an external include (`Grammar.getExternalGrammar(scopeName, repository)`), the including grammar's `repository.$base` is passed as `base`, so `$base` in an embedded grammar refers to the root grammar's `$self`. External grammars are cached per `Grammar` instance.
- `registry.ts` (`SyncRegistry`) holds raw grammars by scope, injection scope lists per target scope, the `Theme`, and one compiled `Grammar` per scope name. `main.ts: Registry.loadGrammar` runs `ScopeDependencyProcessor` (`grammar/grammarDependencies.ts`) which walks all `include`s transitively to find external scope names (full `source.x` and partial `source.x#rule`) plus injection grammars, loading them all before compiling.
- `parseInclude(include)`: `$base` → Base; `$self` → Self; no `#` → TopLevelReference(scopeName); `#` at index 0 → RelativeReference(ruleName); otherwise TopLevelRepositoryReference(scopeName, ruleName).

## Rule compilation — `rule.ts: RuleFactory`

`RuleFactory.getCompiledRuleId(desc, helper, repository)`: if `desc.id` already set, return it; else `helper.registerRule(id => ...)` (ids are 1-based sequential ints; `_ruleId2desc[0] = null`). Rule kind selection, in this exact order:

1. `desc.match` present → `MatchRule(name, match, captures=_compileCaptures(desc.captures))`.
2. `desc.begin` undefined → `IncludeOnlyRule(name, contentName, patterns)`. If `desc.repository` exists, the effective repository becomes `mergeObjects({}, repository, desc.repository)` (rule-local repository shadows outer). If `desc.patterns` is undefined but `desc.include` exists, patterns = `[{include}]`.
3. `desc.while` present → `BeginWhileRule(begin, beginCaptures||captures, while, whileCaptures||captures, patterns)`.
4. else → `BeginEndRule(begin, beginCaptures||captures, end, endCaptures||captures, applyEndPatternLast, patterns)`. Missing `end` becomes `''` (empty regex, matches immediately).

`_compileCaptures`: finds max numeric capture key, makes a dense array `[0..max]` of `CaptureRule|null`; each `CaptureRule(name, contentName, retokenizeCapturedWithRuleId)` where `retokenizeCapturedWithRuleId` = compiled id of the capture's own rule if the capture has `patterns` (0 otherwise). A `$vscodeTextmateLocation` key is skipped.

`_compilePatterns(patterns, helper, repository)` → `{ patterns: RuleId[], hasMissingPatterns }`. For each entry: if `include`: Base/Self → `getCompiledRuleId(repository['$self'|'$base'])`; Relative `#name` → `repository[name]` (if absent, silently skipped); `source.x` → `helper.getExternalGrammar(scope, repository)` then its `repository.$self`; `source.x#name` → that grammar's `repository[name]` compiled with the external grammar's repository. Non-include → `getCompiledRuleId(pattern)`. A resolved rule that is IncludeOnly/BeginEnd/BeginWhile with `hasMissingPatterns && patterns.length === 0` is dropped entirely. `hasMissingPatterns = (raw count != resolved count)`.

`Rule` base: stores `name`/`contentName`, and precomputes whether they contain `$1`/`${1:/downcase}`/`${1:/upcase}` capture references (`utils.ts: RegexSource.hasCaptures`, regex `/\$(\d+)|\${(\d+):\/(downcase|upcase)}/g`). `getName(lineText, captureIndices)` substitutes captured text (leading `.` stripped, optional case transform).

### Flattening into scanners (`collectPatterns`, `RegExpSourceList`, `CompiledRule`)

- `collectPatterns(out)`: MatchRule pushes its `match` RegExpSource (ruleId = own id); BeginEnd/BeginWhile push their `begin`; IncludeOnlyRule recursively collects all its `patterns` (includes flatten transparently into the parent's alternation list); CaptureRule throws.
- Each rule lazily builds `_cachedCompiledPatterns: RegExpSourceList` = the flattened `begin`/`match` sources of its `patterns`, in order. For `BeginEndRule`, the `end` RegExpSource (ruleId = `endRuleId = -1`) is `unshift`ed to index 0 by default, or `push`ed last if `applyEndPatternLast`. Since the scanner picks the earliest match and breaks ties by lowest index, this is the whole semantics of `applyEndPatternLast`. If `end` has back-references, a `clone()` is pushed and its text is replaced per-state via `setSource(index, resolvedEnd)` (which disposes the compiled scanner caches when the text changes).
- `BeginWhileRule` keeps a second list `_cachedCompiledWhilePatterns` containing only the `while` source (ruleId = `whileRuleId = -2`), with the same dynamic-source mechanism.
- `RegExpSourceList.compile(onigLib)` → `CompiledRule(onigLib, sources[], ruleIds[])` which creates one `OnigScanner` over all sources. `CompiledRule.findNextMatchSync(str, pos, opts)` maps the scanner's `index` back to `ruleIds[index]`.

### `RegExpSource`: `\z` rewrite, anchor detection, back-reference detection

- `\z` is replaced by `$(?!\n)(?<!\n)` (every line gets `\n` appended; this makes `\z` mean "at the real end, not before the appended newline"). `\A` or `\G` anywhere sets `hasAnchor = true`.
- `hasBackReferences = /\\(\d+)/.test(source)`.
- `resolveBackReferences(lineText, captureIndices)`: replaces every `\N` with `escapeRegExpCharacters(capturedText[N] || '')` (escape set: `\|([{}\]).?*+^$`).
- Anchor cache: four strings `A0_G0, A0_G1, A1_G0, A1_G1`, built by replacing the letter after the backslash: `\A` → `\` + U+FFFF when `allowA` is false (never matches), likewise for `\G`. `RegExpSourceList.compileAG(onigLib, allowA, allowG)` keeps four cached `CompiledRule`s, falling back to the single `compile()` cache when no item has anchors.
- When allowA/allowG are true (`tokenizeString.ts: prepareRuleSearch`): `allowA = isFirstLine` (only on the document's first line and only until the first advance within it); `allowG = (linePos === anchorPosition)`. `anchorPosition` starts at -1 each line (or 0 if the top stack element has `beginRuleCapturedEOL`), is set to `captureIndices[0].end` after a `begin` or `while` match, and is restored to the popped element's saved `anchorPos` when an `end` pops.

## Tokenization — `grammar/grammar.ts` + `grammar/tokenizeString.ts`

### StateStack (`StateStackImpl`)

Immutable singly-linked list; fields: `parent`, `ruleId`, `_enterPos` (line offset where pushed; -1 across lines), `_anchorPos`, `depth`, `beginRuleCapturedEOL` (begin match ended exactly at lineLength, i.e. consumed the appended `\n`), `endRule: string|null` (resolved dynamic end/while regex text), `nameScopesList: AttributedScopeStack` (scopes incl. the rule's `name`), `contentNameScopesList` (nameScopesList + `contentName`). Methods: `push(...)` returns a new node; `pop()` = parent; `safePop()` = parent or self; `withContentNameScopesList`, `withEndRule` return modified copies; `reset()` sets enterPos/anchorPos to -1 along the whole chain (called on `prevState` at the start of each new line); `hasSameRuleAs(other)` walks up while `_enterPos` equals and checks ruleId equality (loop guard); `equals` compares depth/ruleId/endRule structurally plus `AttributedScopeStack.equals`.

### Per-line driver: `Grammar._tokenize(lineText, prevState, emitBinary, timeLimit)`

1. First call: compile root `_rootId = getCompiledRuleId(repository.$self)` then `getInjections()`.
2. If `prevState` null: `isFirstLine = true`; default metadata via `EncodedTokenAttributes.set(0, initialLanguageId, defaultTokenType(NotSet), null, theme.defaults.fontStyle, fgId, bgId)`; root scope list = `AttributedScopeStack.createRootAndLookUpScopeName(rootRule.name, ...)`; `prevState = new StateStackImpl(null, rootId, -1, -1, false, null, scopeList, scopeList)`. Else `isFirstLine = false; prevState.reset()`.
3. `lineText = lineText + "\n"`; `lineLength` includes the `\n`.
4. `_tokenizeString(grammar, onigLine, isFirstLine, 0, prevState, tokenHandler, checkWhileConditions=true, timeLimit)`.
5. `LineTokens.getResult(stack, lineLength)`: if the last token starts at `lineLength-1` (the token for the `\n`) drop it; if no tokens remain, emit one token `[0, lineLength)` with the stack's `contentNameScopesList`. `tokenizeLine` returns `{tokens: IToken[startIndex,endIndex,scopes[]], ruleStack, stoppedEarly}`; `tokenizeLine2` returns `Uint32Array` pairs `[startIndex, metadata]`.

### `_tokenizeString`

```
anchorPosition = -1
if checkWhileConditions: (stack, linePos, isFirstLine, anchorPosition) = _checkWhileConditions(...)
loop until STOP (abort with stoppedEarly=true if timeLimit exceeded): scanNext()
```

`scanNext()`:
- `r = matchRuleOrInjections(...)`. If null: emit token to `lineLength` with `stack.contentNameScopesList`; STOP.
- `hasAdvanced = captureIndices[0].end > linePos`.
- `matchedRuleId === endRuleId`: emit up to `[0].start` with contentNameScopes; `stack = stack.withContentNameScopesList(stack.nameScopesList)` (end captures live under `name`); `handleCaptures(poppedRule.endCaptures)`; emit to `[0].end`; `popped = stack; stack = stack.parent; anchorPosition = popped.getAnchorPos()`. Guard [1]: if `!hasAdvanced && popped.getEnterPos() === linePos` → `stack = popped`, emit to lineLength, STOP.
- Otherwise a rule matched: emit to `[0].start`; `scopeName = _rule.getName(line, captures)`; `nameScopesList = stack.contentNameScopesList.pushAttributed(scopeName)`; `stack = stack.push(matchedRuleId, linePos, anchorPosition, [0].end === lineLength, null, nameScopesList, nameScopesList)`. Then:
  - BeginEndRule: `handleCaptures(beginCaptures)`; emit to `[0].end`; `anchorPosition = [0].end`; `contentNameScopesList = nameScopesList.pushAttributed(getContentName(...))`; `stack = stack.withContentNameScopesList(...)`; if `endHasBackReferences`: `stack = stack.withEndRule(resolved end)`. Guard [2]: `!hasAdvanced && beforePush.hasSameRuleAs(stack)` → pop, emit to lineLength, STOP.
  - BeginWhileRule: same with `whileHasBackReferences`. Guard [3] identical.
  - MatchRule: `handleCaptures(captures)`; emit to `[0].end`; `stack = stack.pop()`. Guard [4]: `!hasAdvanced` → `stack = stack.safePop()`, emit to lineLength, STOP.
- Finally `if ([0].end > linePos) { linePos = [0].end; isFirstLine = false; }`.

### `matchRule` / `matchRuleOrInjections`

`matchRule`: `rule = stack.getRule(grammar)`; `scanner = rule.compileAG(grammar, stack.endRule, isFirstLine, linePos === anchorPosition)`; `scanner.findNextMatchSync(lineText, linePos)` → `{captureIndices, matchedRuleId}`. `matchRuleOrInjections`: run `matchRule`; if there are injections, run `matchInjections`; if both match, injection wins when `injection.start < match.start`, or when equal start and `injectionResult.priorityMatch` (priority `L:`). Otherwise the grammar rule wins.

### `handleCaptures(grammar, lineText, isFirstLine, stack, tokenHandler, captures, captureIndices)`

Iterates `i in 0..min(captures.length, captureIndices.length)`; skips null capture rules, zero-length captures, and `break`s once a capture starts beyond `captureIndices[0].end`. Maintains a `localStack` of `(scopes, endPos)` for nested captures: before processing capture i, pops local entries whose `endPos <= capture.start` (emitting tokens at those ends), then emits a token up to `capture.start` with the innermost local scopes (or `stack.contentNameScopesList`). If `captureRule.retokenizeCapturedWithRuleId` ≠ 0: builds name/contentName scope lists from the capture rule, pushes a temporary stack element `stack.push(retokenizeRuleId, capture.start, -1, false, null, ...)`, creates a string of `lineText.substring(0, capture.end)` (prefix only, so lookbehinds still see the left context), and recursively calls `_tokenizeString(grammar, sub, isFirstLine && capture.start === 0, capture.start, stackClone, tokenHandler, checkWhileConditions=false, timeLimit=0)`; the recursive result stack is discarded. Otherwise if the capture has a name, pushes `(base.pushAttributed(name), capture.end)` on `localStack`. At the end, pops/emits all remaining local entries.

### `_checkWhileConditions` (line start only)

`anchorPosition = stack.beginRuleCapturedEOL ? 0 : -1`. Collect all `BeginWhileRule` nodes in the stack, then process from the outermost (bottom) to innermost: `scanner = rule.compileWhileAG(grammar, node.endRule, isFirstLine, linePos === anchorPosition)`; `findNextMatchSync(line, linePos)`. If matched with `ruleId === whileRuleId`: emit up to `[0].start` with that node's contentNameScopes, `handleCaptures(whileCaptures)`, emit to `[0].end`, `anchorPosition = [0].end`, advance `linePos`/clear `isFirstLine` if it moved. If no match: `stack = node.pop()` (the whole stack above and including the failed while rule is cut off) and stop checking.

## Injections — `matcher.ts` + grammar.ts

- `Grammar._collectInjections()`: (a) the grammar's own `injections` map `{selector: rule}`; (b) each injection grammar registered for this scope (`registry.injections(scopeName)`, from `package.json injectTo`), using the injection grammar's top-level `injectionSelector` and compiling that grammar's root. Each produces `Injection { matcher, priority, ruleId, grammar }`, one per comma-separated alternative. Sorted ascending by priority (`L:` = -1, default 0, `R:` = 1).
- `createMatchers(selector, matchesName)`: tokenizer regex `/([LR]:|[\w\.:][\w\.:\-]*|[\,\|\-\(\)])/g`. Grammar: top-level comma list; each alternative may start with `L:`/`R:`; a conjunction is a sequence of operands (all must match); an operand is `- operand` (negation), `( inner )` where inner is `|`/`,`-separated conjunctions (OR), or a run of identifiers (one `matchesName(identifiers, scopes)` call, i.e. a scope path like `meta.tag string.quoted`).
- `nameMatcher(identifiers, scopes)`: each identifier must match a scope in order, scanning left-to-right through the stack's scope names with a moving `lastIndex` (descendant semantics, not necessarily adjacent). `scopesAreMatching(scope, pattern)`: equal, or `scope` starts with `pattern + '.'`.
- `matchInjections`: `scopes = stack.contentNameScopesList.getScopeNames()`; for each injection whose matcher passes, compile and scan; keep the one with lowest match start (`<`, so earlier-priority injections win ties); stop early at `start === linePos`. Returns `priorityMatch = (priority === -1)`.

## Theme — `theme.ts`

- `parseTheme(raw)` → `ParsedThemeRule[]`: each setting has `scope` (string with commas, or array; missing → `['']` = global default) and `settings {fontStyle, foreground, background, fontFamily, fontSize, lineHeight}`. `fontStyle` string → bitmask (`FontStyle: NotSet=-1, None=0, Italic=1, Bold=2, Underline=4, Strikethrough=8`); a `fontStyle` key present but unknown words → `None`. Colors validated by `isValidHexColor` (#rgb/#rgba/#rrggbb/#rrggbbaa). Each scope selector is trimmed, split on spaces: last segment = `scope`, earlier segments = `parentScopes` reversed (index 0 = nearest parent); `>` child combinator segments are kept as literal `'>'` tokens. `index` = setting order.
- `resolveParsedThemeRules`: sort by `scope` string, then `parentScopes` (shorter array first, then lexicographic), then `index`. Leading rules with `scope === ''` fold into defaults (`#000000` fg, `#ffffff` bg, fontStyle None). `ColorMap.getId(color)`: uppercases; ids start at 1 (0 = "not set").
- `ThemeTrieElement`: trie keyed by dot-segments of the scope. Each node has `_mainRule` (no parent scopes) and `_rulesWithParentScopes[]`. `insert(scopeDepth, scope, parentScopes, ...)` walks/creates children (a new child clones the parent's main rule and parent-scope rules → inheritance), then at the leaf: no parentScopes → `mainRule.acceptOverwrite` (only non-NotSet/non-0 fields overwrite; `scopeDepth` recorded); with parentScopes → merge into an existing rule with identical parentScopes, else push a new rule that inherits unset fields from `_mainRule`.
- `match(scope)`: descend by segments as far as children exist (`keyword.operator.foo` falls back to `keyword.operator`, then `keyword`); return `[...rulesWithParentScopes, mainRule]` sorted by `_cmpBySpecificity`: deeper `scopeDepth` first; then compare parent scopes pairwise from the nearest parent, skipping `>`, longer parent scope name wins; then more parent scopes wins. `Theme.match(scopeStack)` caches that list per scope name and picks the first rule whose `parentScopes` satisfy `_scopePathMatchesParentScopes(scopeStack.parent, parentScopes)`: for each parent pattern walk up the stack until `_matchesScope` (equal or `name.startsWith(pattern + '.')`); with `>` the immediately-enclosing scope must match. Returns `StyleAttributes(fontStyle, foregroundId, backgroundId, …)` or null. Later settings override earlier ones with the same selector; specificity, not order, decides between different selectors.

## Scope stack attributes and binary tokens

- `AttributedScopeStack`: immutable list; node = `(parent, scopePath, tokenAttributes: number, …)`. `pushAttributed(scopePathStr, grammar)` splits on spaces and for each scope: `rawMetadata = grammar.getMetadataForScope(scope)`; `themeMatch = themeProvider.themeMatch(parentPath.push(scope))`; `metadata = EncodedTokenAttributes.set(parent.tokenAttributes, rawMetadata.languageId, rawMetadata.tokenType, null, style.fontStyle, style.fgId, style.bgId)`. The style is resolved once at push time and cached in the node; every token just reads `scopesList.tokenAttributes`.
- `grammar/basicScopesAttributeProvider.ts`: `languageId` from `embeddedLanguages` via a regex `^((escaped scopes sorted longest-first))($|\.)` → id, else 0 (= keep parent's); `tokenType` from `/\b(comment|string|regex|meta\.embedded)\b/` → Comment=1/String=2/RegEx=3/`meta.embedded`→Other=0, else NotSet=8.
- `encodedTokenAttributes.ts` bit layout (32-bit): bits 0-7 languageId, 8-9 StandardTokenType, 10 balanced-bracket, 11-14 FontStyle, 15-23 foreground color id (9), 24-31 background color id. `set()` only overwrites fields whose new value is non-zero / not NotSet / non-null.
- `LineTokens.produceFromScopes`: in binary mode, applies `tokenTypes` overrides (selectors compiled with `createMatchers` + `nameMatcher`, later wins) and `BalancedBracketSelectors.match(scopes)` (`unbalancedBracketScopes` excluders first → false; `balancedBracketScopes` includers → true; `'*'` sets allowAny). Consecutive tokens with identical metadata are merged unless the line contains RTL characters.

## Test suites (`test-cases/`)

- `first-mate/tests.json` (64 tests), `suite1/tests.json` (22), `suite1/whileTests.json` (9). Format: array of `{ desc, grammars: [relative paths], grammarPath?, grammarScopeName?, grammarInjections?: [scopeNames], lines: [{ line, tokens: [{ value, scopes: [] }] }] }`. Fixtures: `first-mate/fixtures/*.json` (37 files), `suite1/fixtures/*` (25 files, `.json`, `.plist`, `.tmLanguage`). Harness: register all `grammars` by scope, `getInjections` returns `grammarInjections` for the main scope, tokenize line-by-line threading `ruleStack`, compare `{value: line.substring(start,end), scopes}` arrays after filtering zero-length expected tokens when the line is non-empty.
- `themes/`: `grammars.json` (48 grammars under `themes/syntaxes/`), `languages.json`, 7 `.tmTheme` + 6 `.json` themes (dark_plus/light_plus are concatenated onto dark_vs/light_vs settings). `themes/tests/` holds 72 source files + `<file>.result`: `{ themeName: [{content, color}] }` for 13 themes, produced by `tokenizeLine2`, taking only the foreground color id → `colorMap[id]`, dropping empty tokens. Language ids assigned 1..N in `languages.json` order.
- `src/tests/matcher.test.ts` and `themes.test.ts` contain inline unit tests for selector parsing and theme precedence worth porting as table tests.

## Oniguruma scanner semantics (vscode-oniguruma `src/onig.cc`, `src/index.ts`)

- Regexes compiled with `onig_new(..., ONIG_OPTION_CAPTURE_GROUP, ONIG_ENCODING_UTF8, ONIG_SYNTAX_DEFAULT)`. Offsets are UTF-8 internally; `OnigString` precomputes utf16↔utf8 offset tables for non-ASCII strings.
- `findNextOnigScannerMatch(scanner, strCacheId, str, len, position, options)`: if `len < 1000` uses `onig_regset_search(..., ONIG_REGSET_POSITION_LEAD)` (earliest start, ties by lowest index). Otherwise loops over regexes in order, keeps the strictly smaller `beg[0]` (first-in-order wins ties) and breaks early when a match starts exactly at `position`. Per-regex cache: `lastSearchStrCacheId`, `lastSearchPosition`, `lastSearchOnigOption`, `lastSearchMatched`, and the region; a search at position `p' >= lastPosition` on the same string/options is answered from cache if the previous attempt had no match or if the previous match began at `>= p'`. Regexes containing `\G` bypass the cache. Unmatched groups yield `start = end = -1` (mapped to 0 when offset tables exist).
