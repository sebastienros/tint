using System.Globalization;
using System.Text;
using System.Xml;
using Tint.Grammars;
using Tint.Loading;

namespace Tint.Generator;

// The GregsEx package a grammar is validated and compiled against, and whether the consumer can run kernels.
internal sealed record GregsExContext(string? PackageRoot, bool SupportsKernels);

internal static class GrammarEmitter
{
    private const string RawRuleType = "global::Tint.Grammars.RawRule";
    private const string RegexSourceType = "global::Tint.Grammars.RegexSource";
    private const string PatternType = "global::GregsEx.OnigurumaPattern";
    private const string FrozenDictionary = "global::System.Collections.Frozen.FrozenDictionary";

    public static GenerationResult Generate(GenerationUnit unit, GregsExContext gregsEx, CancellationToken cancellationToken)
    {
        var diagnostics = new List<DiagnosticInfo>();
        var fileLocation = LocationInfo.InFile(unit.DisplayPath);
        var grammar = Parse(unit, fileLocation, diagnostics);
        if (grammar is null)
        {
            return new GenerationResult(unit.HintName, null, null, diagnostics.ToEquatableArray());
        }

        cancellationToken.ThrowIfCancellationRequested();
        var info = BuildInfo(unit, grammar, fileLocation, diagnostics);
        var table = PatternTable.Build(grammar);

        // Validate every pattern the runtime will parse, and emit kernels when asked for and possible.
        var shim = GregsExShim.Get(gregsEx.PackageRoot, out var failure);
        var emitKernels = unit.Kernels && shim is not null && gregsEx.SupportsKernels;
        if (unit.Kernels && !emitKernels)
        {
            var reason = shim is null ? "GregsEx is not reachable at build time (" + failure + ")" : "the project does not target .NET 8 or later";
            diagnostics.Add(DiagnosticInfo.Create(TintDiagnostics.KernelsUnavailable, fileLocation, unit.ClassName, reason));
        }

        var kernels = new List<(int Index, ulong Fingerprint, string Plain, string? Capture)>();
        var declined = 0;
        if (shim is null)
        {
            diagnostics.Add(DiagnosticInfo.Create(TintDiagnostics.GregsExUnavailable, fileLocation, unit.ClassName, failure ?? "GregsEx was not found"));
        }
        else
        {
            for (var i = 0; i < table.ParsedCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = shim.TryEmit(table.Texts[i], KernelName(i, "Plain"), KernelName(i, "Capture"));
                if (result is null)
                {
                    continue;
                }

                switch (result.Status)
                {
                    case KernelStatus.Invalid:
                        table.Invalid.Add(i);
                        table.InvalidMessages[i] = (result.Offset, result.Message ?? "invalid pattern");
                        break;
                    case KernelStatus.Succeeded when emitKernels && result.PlainClass is not null:
                        kernels.Add((i, result.Fingerprint, result.PlainClass, result.CaptureClass));
                        break;
                    default:
                        declined++;
                        break;
                }
            }

            foreach (var source in table.Sources)
            {
                var bad = source.Variants?.Where(table.Invalid.Contains).Select(i => (int?)i).FirstOrDefault();
                if (bad is { } index)
                {
                    source.Invalid = true;
                    var (offset, message) = table.InvalidMessages[index];
                    diagnostics.Add(DiagnosticInfo.Create(
                        TintDiagnostics.InvalidRegex, fileLocation, source.FirstPath, unit.DisplayPath, message, offset.ToString(CultureInfo.InvariantCulture)));
                }
            }

            if (emitKernels)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    TintDiagnostics.KernelSummary, fileLocation, unit.ClassName, kernels.Count.ToString(CultureInfo.InvariantCulture), declined.ToString(CultureInfo.InvariantCulture)));
            }
        }

        var mainSource = EmitMain(unit, grammar, info, table, kernels.Count > 0);
        var kernelSource = kernels.Count > 0 ? EmitKernels(unit, kernels) : null;
        return new GenerationResult(unit.HintName, mainSource, kernelSource, diagnostics.ToEquatableArray());
    }

    public static RawGrammar? Parse(GenerationUnit unit, LocationInfo fileLocation, List<DiagnosticInfo> diagnostics)
    {
        try
        {
            var utf8 = Encoding.UTF8.GetBytes(unit.Text);
            var json = unit.FullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
            var tree = json ? DocumentTree.ParseJson(utf8) : DocumentTree.ParsePlist(utf8);
            if (tree is not TreeMap root)
            {
                throw new FormatException("A grammar must be an object.");
            }

            return GrammarLoader.ReadGrammar(root);
        }
        catch (JsonTreeException e)
        {
            diagnostics.Add(DiagnosticInfo.Create(TintDiagnostics.InvalidFile, LocationInfo.InFile(unit.DisplayPath, e.Line - 1, e.Column - 1), unit.DisplayPath, "grammar", e.Message));
        }
        catch (XmlException e)
        {
            diagnostics.Add(DiagnosticInfo.Create(TintDiagnostics.InvalidFile, LocationInfo.InFile(unit.DisplayPath, Math.Max(0, e.LineNumber - 1), Math.Max(0, e.LinePosition - 1)), unit.DisplayPath, "grammar", e.Message));
        }
        catch (FormatException e)
        {
            diagnostics.Add(DiagnosticInfo.Create(TintDiagnostics.InvalidFile, fileLocation, unit.DisplayPath, "grammar", e.Message));
        }

        return null;
    }

    private static string KernelName(int index, string kind) => "__TintKernel" + index.ToString(CultureInfo.InvariantCulture) + kind;

    private sealed class Info
    {
        public string? LanguageId;
        public List<string> Aliases = [];
        public List<string> FileExtensions = [];
        public List<KeyValuePair<string, string>> EmbeddedLanguages = [];
        public List<string> InjectTo = [];
        public string? InjectionSelector;
        public List<KeyValuePair<string, string>> TokenTypes = [];
        public List<string>? BalancedBracketScopes;
        public List<string> UnbalancedBracketScopes = [];
        public List<string> Dependencies = [];
    }

    private static Info BuildInfo(GenerationUnit unit, RawGrammar grammar, LocationInfo location, List<DiagnosticInfo> diagnostics)
    {
        var settings = unit.Settings;
        var info = new Info
        {
            LanguageId = settings.LanguageId,
            Aliases = [.. settings.Aliases],
            FileExtensions = [.. settings.FileExtensions],
            InjectTo = [.. settings.InjectTo],
            InjectionSelector = settings.InjectionSelector,
            BalancedBracketScopes = settings.BalancedBracketScopes is { } balanced ? [.. balanced] : null,
            UnbalancedBracketScopes = [.. settings.UnbalancedBracketScopes],
            Dependencies = GrammarLoader.CollectDependencies(grammar),
        };

        foreach (var entry in settings.EmbeddedLanguages)
        {
            if (TrySplit(entry, out var scope, out var language))
            {
                info.EmbeddedLanguages.Add(new KeyValuePair<string, string>(scope, language));
            }
            else
            {
                diagnostics.Add(DiagnosticInfo.Create(TintDiagnostics.InvalidMetadata, location, entry, "EmbeddedLanguages", unit.ClassName, "expected 'scope=languageId'"));
            }
        }

        foreach (var entry in settings.TokenTypes)
        {
            if (TrySplit(entry, out var selector, out var type) && TokenType(type) is { } member)
            {
                info.TokenTypes.Add(new KeyValuePair<string, string>(selector, member));
            }
            else
            {
                diagnostics.Add(DiagnosticInfo.Create(TintDiagnostics.InvalidMetadata, location, entry, "TokenTypes", unit.ClassName, "expected 'selector=comment|string|regex|other'"));
            }
        }

        if (info.Dependencies.Count > 0)
        {
            diagnostics.Add(DiagnosticInfo.Create(TintDiagnostics.ExternalScopes, location, grammar.ScopeName, string.Join(", ", info.Dependencies)));
        }

        return info;
    }

    private static bool TrySplit(string entry, out string key, out string value)
    {
        var equals = entry.LastIndexOf('=');
        key = equals > 0 ? entry.Substring(0, equals).Trim() : string.Empty;
        value = equals > 0 ? entry.Substring(equals + 1).Trim() : string.Empty;
        return key.Length > 0 && value.Length > 0;
    }

    private static string? TokenType(string value) => value.ToLowerInvariant() switch
    {
        "other" => "Other",
        "comment" => "Comment",
        "string" => "String",
        "regex" or "regexp" => "RegEx",
        _ => null,
    };

    // A distinct regex of the grammar: its text and whether the runtime resolves it per match (back-referencing end and
    // while patterns), plus the table entries of the anchor variants it needs.
    private sealed class RegexEntry
    {
        public RegexEntry(RegexSource source, bool dynamic, string path)
        {
            Source = source;
            Dynamic = dynamic;
            FirstPath = path;
        }

        public RegexSource Source { get; }

        public bool Dynamic { get; }

        public string FirstPath { get; }

        public int TextIndex { get; set; }

        // Table indices for A0G0, A0G1, A1G0, A1G1; null for dynamic patterns.
        public int[]? Variants { get; set; }

        public bool Invalid { get; set; }

        public int Index { get; set; }
    }

    private sealed class PatternTable
    {
        private readonly Dictionary<(string Text, bool Dynamic), RegexEntry> _byKey = [];
        private readonly Dictionary<RegexSource, RegexEntry> _bySource = new(ReferenceEqualityComparer.Instance);

        public List<RegexEntry> Sources { get; } = [];

        // Parsed texts first (indices below ParsedCount, the PatternIndex entries), then texts that only seed a
        // RegexSource (anchored base texts and dynamic patterns).
        public List<string> Texts { get; } = [];

        public int ParsedCount { get; private set; }

        public HashSet<int> Invalid { get; } = [];

        public Dictionary<int, (int Offset, string Message)> InvalidMessages { get; } = [];

        public RegexEntry Get(RegexSource source) => _bySource[source];

        public static PatternTable Build(RawGrammar grammar)
        {
            var table = new PatternTable();
            var walker = new Walker(table);
            walker.Patterns(grammar.Patterns, "patterns");
            foreach (var entry in grammar.Repository.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                walker.Rule(entry.Value, "repository." + entry.Key);
            }

            for (var i = 0; i < grammar.Injections.Count; i++)
            {
                walker.Rule(grammar.Injections[i].Value, "injections[" + grammar.Injections[i].Key + "]");
            }

            var textIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            int Add(string text)
            {
                if (!textIndex.TryGetValue(text, out var index))
                {
                    index = table.Texts.Count;
                    table.Texts.Add(text);
                    textIndex[text] = index;
                }

                return index;
            }

            foreach (var entry in table.Sources)
            {
                if (!entry.Dynamic)
                {
                    entry.Variants = new int[4];
                    for (var v = 0; v < 4; v++)
                    {
                        entry.Variants[v] = Add(entry.Source.GetText((RegexAnchorVariant)v));
                    }
                }
            }

            table.ParsedCount = table.Texts.Count;
            foreach (var entry in table.Sources)
            {
                entry.TextIndex = Add(entry.Source.Text);
            }

            return table;
        }

        public void Add(RegexSource source, bool dynamic, string path)
        {
            if (!_byKey.TryGetValue((source.Text, dynamic), out var entry))
            {
                entry = new RegexEntry(source, dynamic, path) { Index = Sources.Count };
                _byKey[(source.Text, dynamic)] = entry;
                Sources.Add(entry);
            }

            _bySource[source] = entry;
        }
    }

    private sealed class Walker(PatternTable table)
    {
        public void Patterns(IReadOnlyList<RawRule>? rules, string path)
        {
            if (rules is null)
            {
                return;
            }

            for (var i = 0; i < rules.Count; i++)
            {
                Rule(rules[i], path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]");
            }
        }

        public void Rule(RawRule? rule, string path)
        {
            if (rule is null)
            {
                return;
            }

            Regex(rule.Match, false, path + ".match");
            Regex(rule.Begin, false, path + ".begin");
            Regex(rule.End, rule.End?.HasBackReferences == true, path + ".end");
            Regex(rule.While, rule.While?.HasBackReferences == true, path + ".while");
            Captures(rule.Captures, path + ".captures");
            Captures(rule.BeginCaptures, path + ".beginCaptures");
            Captures(rule.EndCaptures, path + ".endCaptures");
            Captures(rule.WhileCaptures, path + ".whileCaptures");
            Patterns(rule.Patterns, path + ".patterns");
            if (rule.Repository is { } repository)
            {
                foreach (var entry in repository.OrderBy(e => e.Key, StringComparer.Ordinal))
                {
                    Rule(entry.Value, path + ".repository." + entry.Key);
                }
            }
        }

        private void Captures(IReadOnlyList<RawRule?>? captures, string path)
        {
            if (captures is null)
            {
                return;
            }

            for (var i = 0; i < captures.Count; i++)
            {
                Rule(captures[i], path + "." + i.ToString(CultureInfo.InvariantCulture));
            }
        }

        private void Regex(RegexSource? source, bool dynamic, string path)
        {
            if (source is not null)
            {
                table.Add(source, dynamic, path);
            }
        }
    }

    private static string EmitMain(GenerationUnit unit, RawGrammar grammar, Info info, PatternTable table, bool hasKernels)
    {
        var w = new CodeWriter();
        var nesting = Header(w, unit, " : global::Tint.Grammars.IGrammarSource");
        var c = unit.ClassName;

        w.Line("private static readonly global::Tint.Grammars.GrammarInfo __tintInfo = " + InfoExpression(info) + ";");
        w.Line("private static global::Tint.Grammars.RawGrammar? __tintGrammar;");
        w.Line();
        w.Line("/// <summary>The shared instance.</summary>");
        w.Line("public static " + c + " Instance { get; } = new " + c + "();");
        w.Line();
        w.Line("/// <summary>Distinct pattern texts (anchor variants resolved) to their index in the pattern table.</summary>");
        w.Line("public static global::System.Collections.Frozen.FrozenDictionary<string, int> PatternIndex => __TintIndex.Value;");
        w.Line();
        w.Line("/// <inheritdoc/>");
        w.Line("public string ScopeName => " + CodeWriter.Literal(grammar.ScopeName) + ";");
        w.Line();
        w.Line("/// <inheritdoc/>");
        w.Line("public global::Tint.Grammars.GrammarInfo Info => __tintInfo;");
        w.Line();
        w.Line("/// <inheritdoc/>");
        w.Open("public global::Tint.Grammars.RawGrammar GetGrammar()");
        w.Line("var grammar = global::System.Threading.Volatile.Read(ref __tintGrammar);");
        w.Line("if (grammar is not null)").Line("{").Line("    return grammar;").Line("}");
        w.Line();
        w.Line("grammar = __TintBuild();");
        w.Line("return global::System.Threading.Interlocked.CompareExchange(ref __tintGrammar, grammar, null) ?? grammar;");
        w.Close();
        w.Line();

        // The rule tree.
        var emitter = new RuleEmitter(w, table);
        w.Open("private static global::Tint.Grammars.RawGrammar __TintBuild()");
        w.Line("var r = __TintRegexes();");
        var repository = grammar.Repository.OrderBy(e => e.Key, StringComparer.Ordinal).ToList();
        string repositoryExpression;
        if (repository.Count == 0)
        {
            repositoryExpression = "null";
        }
        else
        {
            w.Line("var repository = new global::System.Collections.Generic.Dictionary<string, " + RawRuleType + ">("
                + repository.Count.ToString(CultureInfo.InvariantCulture) + ", global::System.StringComparer.Ordinal);");
            for (var i = 0; i < repository.Count; i++)
            {
                w.Line("repository[" + CodeWriter.Literal(repository[i].Key) + "] = __TintRule" + i.ToString(CultureInfo.InvariantCulture) + "(r);");
            }

            repositoryExpression = FrozenDictionary + ".ToFrozenDictionary(repository, global::System.StringComparer.Ordinal)";
        }

        w.Write("var patterns = ");
        emitter.RuleList(grammar.Patterns);
        w.Line(";");
        string injectionsExpression;
        if (grammar.Injections.Count == 0)
        {
            injectionsExpression = "null";
        }
        else
        {
            injectionsExpression = "injections";
            w.Line("var injections = new global::System.Collections.Generic.KeyValuePair<string, " + RawRuleType + ">[]");
            w.Line("{");
            w.Indent();
            foreach (var injection in grammar.Injections)
            {
                w.Write("new global::System.Collections.Generic.KeyValuePair<string, " + RawRuleType + ">(" + CodeWriter.Literal(injection.Key) + ", ");
                emitter.Rule(injection.Value);
                w.Line("),");
            }

            w.Unindent();
            w.Line("};");
        }

        w.Line("return new global::Tint.Grammars.RawGrammar(" + CodeWriter.Literal(grammar.ScopeName) + ", patterns, " + repositoryExpression + ", "
            + injectionsExpression + ", " + CodeWriter.Literal(grammar.InjectionSelector) + ", " + CodeWriter.Literal(grammar.Name) + ");");
        w.Close();

        for (var i = 0; i < repository.Count; i++)
        {
            w.Line();
            w.Write("private static " + RawRuleType + " __TintRule" + i.ToString(CultureInfo.InvariantCulture) + "(" + RegexSourceType + "[] r) => ");
            emitter.Rule(repository[i].Value);
            w.Line(";");
        }

        // The regex sources, one per distinct text.
        w.Line();
        w.Open("private static " + RegexSourceType + "[] __TintRegexes()");
        w.Line("var t = __TintData.Texts;");
        w.Line("return new " + RegexSourceType + "[]");
        w.Line("{");
        w.Indent();
        foreach (var entry in table.Sources)
        {
            var source = entry.Source;
            var factory = entry.Variants is { } v && !entry.Invalid
                ? "new __TintFactory(" + string.Join(", ", v.Select(i => i.ToString(CultureInfo.InvariantCulture))) + ").Create"
                : "null";
            w.Line(RegexSourceType + ".FromRewritten(t[" + entry.TextIndex.ToString(CultureInfo.InvariantCulture) + "], " + CodeWriter.Bool(source.HasAnchorA) + ", "
                + CodeWriter.Bool(source.HasAnchorG) + ", " + CodeWriter.Bool(source.HasBackReferences) + ", " + factory + "),");
        }

        w.Unindent();
        w.Line("};");
        w.Close();
        w.Line();

        // The pattern table.
        w.Open("private static " + PatternType + " __TintPattern(int index)");
        w.Line("var pattern = global::System.Threading.Volatile.Read(ref __TintData.Patterns[index]);");
        w.Line("if (pattern is not null)").Line("{").Line("    return pattern;").Line("}");
        w.Line();
        w.Line("pattern = " + PatternType + ".Parse(__TintData.Texts[index], global::GregsEx.RegexProfiles.OnigurumaTextMate, global::GregsEx.OnigurumaCompileOptions.TextMate);");
        if (hasKernels)
        {
            w.Line("pattern = __TintBindKernels(index, pattern);");
        }

        w.Line("return global::System.Threading.Interlocked.CompareExchange(ref __TintData.Patterns[index], pattern, null) ?? pattern;");
        w.Close();
        w.Line();
        w.Open("private sealed class __TintFactory");
        w.Line("private readonly int _a0g0;");
        w.Line("private readonly int _a0g1;");
        w.Line("private readonly int _a1g0;");
        w.Line("private readonly int _a1g1;");
        w.Line();
        w.Open("public __TintFactory(int a0g0, int a0g1, int a1g0, int a1g1)");
        w.Line("_a0g0 = a0g0;");
        w.Line("_a0g1 = a0g1;");
        w.Line("_a1g0 = a1g0;");
        w.Line("_a1g1 = a1g1;");
        w.Close();
        w.Line();
        w.Line("public " + PatternType + " Create(global::Tint.Grammars.RegexAnchorVariant variant) => __TintPattern(variant switch");
        w.Line("{");
        w.Line("    global::Tint.Grammars.RegexAnchorVariant.A0G1 => _a0g1,");
        w.Line("    global::Tint.Grammars.RegexAnchorVariant.A1G0 => _a1g0,");
        w.Line("    global::Tint.Grammars.RegexAnchorVariant.A1G1 => _a1g1,");
        w.Line("    _ => _a0g0,");
        w.Line("});");
        w.Close();
        w.Line();
        w.Open("private static class __TintData");
        w.Line("internal static readonly string[] Texts = new string[]");
        w.Line("{");
        w.Indent();
        foreach (var text in table.Texts)
        {
            w.Line(CodeWriter.Literal(text) + ",");
        }

        w.Unindent();
        w.Line("};");
        w.Line();
        w.Line("internal static readonly " + PatternType + "?[] Patterns = new " + PatternType + "?[" + table.ParsedCount.ToString(CultureInfo.InvariantCulture) + "];");
        w.Close();
        w.Line();
        w.Open("private static class __TintIndex");
        w.Line("internal static readonly global::System.Collections.Frozen.FrozenDictionary<string, int> Value = Create();");
        w.Line();
        w.Open("private static global::System.Collections.Frozen.FrozenDictionary<string, int> Create()");
        w.Line("var index = new global::System.Collections.Generic.Dictionary<string, int>(" + table.ParsedCount.ToString(CultureInfo.InvariantCulture) + ", global::System.StringComparer.Ordinal);");
        w.Open("for (var i = 0; i < " + table.ParsedCount.ToString(CultureInfo.InvariantCulture) + "; i++)");
        w.Line("index[__TintData.Texts[i]] = i;");
        w.Close();
        w.Line();
        w.Line("return " + FrozenDictionary + ".ToFrozenDictionary(index, global::System.StringComparer.Ordinal);");
        w.Close();
        w.Close();

        Footer(w, nesting);
        return w.ToString();
    }

    private static string InfoExpression(Info info)
    {
        var parts = new List<string>();
        if (info.LanguageId is not null)
        {
            parts.Add("LanguageId = " + CodeWriter.Literal(info.LanguageId));
        }

        if (info.Aliases.Count > 0)
        {
            parts.Add("Aliases = " + CodeWriter.StringArray(info.Aliases));
        }

        if (info.FileExtensions.Count > 0)
        {
            parts.Add("FileExtensions = " + CodeWriter.StringArray(info.FileExtensions));
        }

        if (info.EmbeddedLanguages.Count > 0)
        {
            parts.Add("EmbeddedLanguages = " + FrozenDictionary + ".ToFrozenDictionary(new global::System.Collections.Generic.Dictionary<string, string>(global::System.StringComparer.Ordinal) { "
                + string.Join(", ", info.EmbeddedLanguages.Select(e => "[" + CodeWriter.Literal(e.Key) + "] = " + CodeWriter.Literal(e.Value)))
                + " }, global::System.StringComparer.Ordinal)");
        }

        if (info.InjectTo.Count > 0)
        {
            parts.Add("InjectTo = " + CodeWriter.StringArray(info.InjectTo));
        }

        if (info.InjectionSelector is not null)
        {
            parts.Add("InjectionSelector = " + CodeWriter.Literal(info.InjectionSelector));
        }

        if (info.TokenTypes.Count > 0)
        {
            parts.Add("TokenTypes = new global::System.Collections.Generic.KeyValuePair<string, global::Tint.Grammars.StandardTokenType>[] { "
                + string.Join(", ", info.TokenTypes.Select(t => "new global::System.Collections.Generic.KeyValuePair<string, global::Tint.Grammars.StandardTokenType>("
                    + CodeWriter.Literal(t.Key) + ", global::Tint.Grammars.StandardTokenType." + t.Value + ")"))
                + " }");
        }

        if (info.BalancedBracketScopes is not null)
        {
            parts.Add("BalancedBracketScopes = " + CodeWriter.StringArray(info.BalancedBracketScopes));
        }

        if (info.UnbalancedBracketScopes.Count > 0)
        {
            parts.Add("UnbalancedBracketScopes = " + CodeWriter.StringArray(info.UnbalancedBracketScopes));
        }

        if (info.Dependencies.Count > 0)
        {
            parts.Add("Dependencies = " + CodeWriter.StringArray(info.Dependencies));
        }

        return parts.Count == 0 ? "new global::Tint.Grammars.GrammarInfo()" : "new global::Tint.Grammars.GrammarInfo { " + string.Join(", ", parts) + " }";
    }

    // Object initializers mirroring the grammar file, one RawRule object per rule as the loader builds them.
    private sealed class RuleEmitter(CodeWriter w, PatternTable table)
    {
        public void Rule(RawRule rule)
        {
            var properties = new List<Action>();
            if (rule.Include is { } include)
            {
                properties.Add(() => w.Write("Include = " + Include(include)));
            }

            if (rule.Name is not null)
            {
                properties.Add(() => w.Write("Name = " + CodeWriter.Literal(rule.Name)));
            }

            if (rule.ContentName is not null)
            {
                properties.Add(() => w.Write("ContentName = " + CodeWriter.Literal(rule.ContentName)));
            }

            AddRegex(properties, "Match", rule.Match);
            AddRegex(properties, "Begin", rule.Begin);
            AddRegex(properties, "End", rule.End);
            AddRegex(properties, "While", rule.While);
            AddCaptures(properties, "Captures", rule.Captures);
            AddCaptures(properties, "BeginCaptures", rule.BeginCaptures);
            AddCaptures(properties, "EndCaptures", rule.EndCaptures);
            AddCaptures(properties, "WhileCaptures", rule.WhileCaptures);
            if (rule.Patterns is { } patterns)
            {
                properties.Add(() =>
                {
                    w.Write("Patterns = ");
                    RuleList(patterns);
                });
            }

            if (rule.Repository is { } repository)
            {
                properties.Add(() =>
                {
                    w.Line("Repository = " + FrozenDictionary + ".ToFrozenDictionary(new global::System.Collections.Generic.Dictionary<string, " + RawRuleType + ">(global::System.StringComparer.Ordinal)");
                    w.Line("{");
                    w.Indent();
                    foreach (var entry in repository.OrderBy(e => e.Key, StringComparer.Ordinal))
                    {
                        w.Write("[" + CodeWriter.Literal(entry.Key) + "] = ");
                        Rule(entry.Value);
                        w.Line(",");
                    }

                    w.Unindent();
                    w.Write("}, global::System.StringComparer.Ordinal)");
                });
            }

            if (rule.ApplyEndPatternLast)
            {
                properties.Add(() => w.Write("ApplyEndPatternLast = true"));
            }

            if (properties.Count == 0)
            {
                w.Write("new " + RawRuleType + "()");
                return;
            }

            w.Line("new " + RawRuleType);
            w.Line("{");
            w.Indent();
            foreach (var property in properties)
            {
                property();
                w.Line(",");
            }

            w.Unindent();
            w.Write("}");
        }

        public void RuleList(IReadOnlyList<RawRule> rules)
        {
            if (rules.Count == 0)
            {
                w.Write("global::System.Array.Empty<" + RawRuleType + ">()");
                return;
            }

            w.Line("new " + RawRuleType + "[]");
            w.Line("{");
            w.Indent();
            foreach (var rule in rules)
            {
                Rule(rule);
                w.Line(",");
            }

            w.Unindent();
            w.Write("}");
        }

        private void AddRegex(List<Action> properties, string name, RegexSource? source)
        {
            if (source is not null)
            {
                properties.Add(() => w.Write(name + " = r[" + table.Get(source).Index.ToString(CultureInfo.InvariantCulture) + "]"));
            }
        }

        private void AddCaptures(List<Action> properties, string name, IReadOnlyList<RawRule?>? captures)
        {
            if (captures is null)
            {
                return;
            }

            properties.Add(() =>
            {
                if (captures.Count == 0)
                {
                    w.Write(name + " = global::System.Array.Empty<" + RawRuleType + "?>()");
                    return;
                }

                w.Line(name + " = new " + RawRuleType + "?[]");
                w.Line("{");
                w.Indent();
                foreach (var capture in captures)
                {
                    if (capture is null)
                    {
                        w.Write("null");
                    }
                    else
                    {
                        Rule(capture);
                    }

                    w.Line(",");
                }

                w.Unindent();
                w.Write("}");
            });
        }

        private static string Include(IncludeReference include) => include.Kind switch
        {
            IncludeReferenceKind.Self => "global::Tint.Grammars.IncludeReference.Self",
            IncludeReferenceKind.Base => "global::Tint.Grammars.IncludeReference.Base",
            IncludeReferenceKind.Relative => "global::Tint.Grammars.IncludeReference.Relative(" + CodeWriter.Literal(include.RuleName) + ")",
            IncludeReferenceKind.TopLevel => "global::Tint.Grammars.IncludeReference.TopLevel(" + CodeWriter.Literal(include.ScopeName) + ")",
            _ => "global::Tint.Grammars.IncludeReference.TopLevelRepository(" + CodeWriter.Literal(include.ScopeName) + ", " + CodeWriter.Literal(include.RuleName) + ")",
        };
    }

    private static string EmitKernels(GenerationUnit unit, List<(int Index, ulong Fingerprint, string Plain, string? Capture)> kernels)
    {
        var w = new CodeWriter();
        var nesting = Header(w, unit, string.Empty, kernelFile: true);
        w.Open("private static " + PatternType + " __TintBindKernels(int index, " + PatternType + " pattern)");
        w.Open("switch (index)");
        foreach (var (index, fingerprint, _, capture) in kernels)
        {
            var plain = KernelName(index, "Plain") + ".Run";
            w.Line("case " + index.ToString(CultureInfo.InvariantCulture) + ": return pattern.WithGeneratedKernels(0x" + fingerprint.ToString("X16", CultureInfo.InvariantCulture)
                + "UL, " + plain + ", " + (capture is null ? plain : KernelName(index, "Capture") + ".Run") + ");");
        }

        w.Line("default: return pattern;");
        w.Close();
        w.Close();
        foreach (var (_, _, plain, capture) in kernels)
        {
            w.Line();
            foreach (var line in plain.Replace("\r\n", "\n").TrimEnd().Split('\n'))
            {
                w.Line(line);
            }

            if (capture is not null)
            {
                w.Line();
                foreach (var line in capture.Replace("\r\n", "\n").TrimEnd().Split('\n'))
                {
                    w.Line(line);
                }
            }
        }

        Footer(w, nesting);
        return w.ToString();
    }

    // The file header, namespace and containing types, and the class declaration; returns the number of open braces.
    internal static int Header(CodeWriter w, GenerationUnit unit, string baseList, bool kernelFile = false)
    {
        w.Line("// <auto-generated/>");
        w.Line("// Generated by Tint.Generator from " + unit.DisplayPath.Replace('\\', '/').Substring(unit.DisplayPath.Replace('\\', '/').LastIndexOf('/') + 1) + ".");
        w.Line("#nullable enable");
        w.Line("#pragma warning disable");
        if (kernelFile)
        {
            // GregsEx's kernel classes are written against `using System;`.
            w.Line();
            w.Line("using System;");
        }

        w.Line();
        var open = 0;
        if (unit.Namespace is not null)
        {
            w.Open("namespace " + unit.Namespace);
            open++;
        }

        foreach (var containing in unit.ContainingTypes)
        {
            w.Open(containing);
            open++;
        }

        if (!kernelFile)
        {
            w.Line("[global::System.CodeDom.Compiler.GeneratedCode(\"Tint.Generator\", \"" + typeof(GrammarEmitter).Assembly.GetName().Version + "\")]");
        }

        w.Open((unit.Accessibility is null ? string.Empty : unit.Accessibility + " ") + "partial class " + unit.ClassName + baseList);
        return open + 1;
    }

    internal static void Footer(CodeWriter w, int open)
    {
        for (var i = 0; i < open; i++)
        {
            w.Close();
        }
    }
}
